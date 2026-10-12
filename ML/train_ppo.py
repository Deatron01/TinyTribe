"""Stage 2: learning from outcomes (PPO).

    ML/.venv/Scripts/python ML/train_ppo.py --start <experiment id> --name ppo-<what> --updates 400

The network plays many villages at once and is rewarded only by what the engine pays (Sim Rewards).
With the workbook's reward_shaping_scale of 0 that is outcomes alone: hurt, starving, a death, a
first building of its kind, a technology, a winter survived. Nothing says how to get there.

  - Every villager's reward mixes what it earned for itself and as one of the village by its
    empathy, as the design has it: R_total = R_individual x (1 - empathy / 2) + R_global x empathy.
  - PPO with the LSTM: stretches of --steps ticks from every villager of every village; advantages
    by GAE; the LSTM is run again over each stretch from the memory it had at its start.
  - The critic (how good is this situation?) is a network of its own. It reads what the policy's
    LSTM makes of the situation but its errors are not sent back into the policy. The first run had
    one shared network: the critic, knowing nothing yet, rewrote in its first ten updates what
    imitation had taught, and the policy never recovered (20261008-1529-ppo-from-dagger).
  - The policy moves in small steps: an update stops as soon as the policy has moved --target-kl
    from where it stood, and for --teacher-updates it is also held near its starting self by a
    penalty that fades to nothing. For the first --critic-warmup updates only the critic learns.
    --start none learns from nothing.

Progress is logged per update in metrics.csv and drawn in curves.svg.
"""
import argparse
import time

import numpy as np
import torch
import torch.nn as nn

from evaluate import load_model
from experiment import EXPERIMENTS, Experiment, markdown_table
from model import TribeNet, export_onnx, masked, to_save
from tribe_env import Actor, TribeEnv

WATCHED = ["days", "alive", "built", "techs", "deaths", "crafted", "gathered", "crops_reaped"]


class Critic(nn.Module):
    """How much reward is still to come from here, from what the policy's LSTM holds and the villager's own vector."""

    def __init__(self, hidden: int, vector_length: int):
        super().__init__()
        self.net = nn.Sequential(nn.Linear(hidden + vector_length, 256), nn.ReLU(), nn.Linear(256, 128), nn.ReLU(), nn.Linear(128, 1))

    def forward(self, out, vector):
        return self.net(torch.cat([out, vector], dim=-1)).squeeze(-1)


def run_lstm(net, x, state, first):
    """x [N, T, F]; first [N, T] is 1 where a villager's memory starts empty (a new village took its place)."""
    h, c = state
    outs = []
    for t in range(x.shape[1]):
        keep = (1.0 - first[:, t]).view(1, -1, 1)
        h, c = h * keep, c * keep
        out, (h, c) = net.lstm(x[:, t:t + 1], (h, c))
        outs.append(out)
    return torch.cat(outs, dim=1)


def policy_of(net, batch):
    """The stretch run again: log-probability and entropy of the stored choices, the action distribution, and what the LSTM held."""
    seen = net.encode(batch["grid"], batch["vector"])
    out = run_lstm(net, seen, batch["state"], batch["first"])
    action_all, arg_all = net.heads(out, seen)
    action_logits = masked(action_all, batch["action_mask"])
    row = arg_all.gather(2, batch["action"][..., None, None].expand(-1, -1, 1, net.layout.arg_count)).squeeze(2)
    action_dist = torch.distributions.Categorical(logits=action_logits)
    arg_dist = torch.distributions.Categorical(logits=masked(row, batch["arg_mask"]))
    logprob = action_dist.log_prob(batch["action"]) + arg_dist.log_prob(batch["arg"])
    return logprob, action_dist.entropy() + arg_dist.entropy(), action_dist.logits, out


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--name", required=True)
    p.add_argument("--start", default="none", help="experiment id of the network to start from, or 'none'")
    p.add_argument("--updates", type=int, default=400)
    p.add_argument("--envs", type=int, default=32)
    p.add_argument("--steps", type=int, default=128, help="ticks gathered from every villager between updates")
    p.add_argument("--days", type=int, default=28, help="a village is played for this many days, then a new one starts")
    p.add_argument("--together", action="store_true", help="start all villages at once (by default they are put out of step, so every "
                                                             "update sees every season)")
    p.add_argument("--gamma", type=float, default=0.999)
    p.add_argument("--lam", type=float, default=0.95)
    p.add_argument("--lr", type=float, default=3e-5, help="the policy's learning rate")
    p.add_argument("--critic-lr", type=float, default=1e-3)
    p.add_argument("--epochs", type=int, default=3)
    p.add_argument("--minibatches", type=int, default=4)
    p.add_argument("--clip", type=float, default=0.1)
    p.add_argument("--target-kl", type=float, default=0.015, help="an update stops when the policy has moved this far")
    p.add_argument("--entropy", type=float, default=0.003)
    p.add_argument("--critic-warmup", type=int, default=30, help="updates in which only the critic learns")
    p.add_argument("--teacher-coef", type=float, default=1.0, help="how hard the policy is held to its starting self at first")
    p.add_argument("--teacher-updates", type=int, default=150, help="updates, after the warm-up, over which that hold fades to nothing")
    p.add_argument("--shaping", type=float, default=-1.0, help="reward_shaping_scale; below 0: as the workbook says (0)")
    p.add_argument("--hidden", type=int, default=128)
    p.add_argument("--slots", type=int, default=12, help="places per village: the villagers it starts with and the children born to them")
    p.add_argument("--seed", type=int, default=20000)
    p.add_argument("--note", default="")
    args = p.parse_args()

    torch.manual_seed(0)
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    env = TribeEnv(envs=args.envs, max_ticks=args.days * 240, seed=args.seed, reward_shaping_scale=args.shaping, stagger=not args.together,
                   max_agents=args.slots)
    layout, n = env.layout, env.envs * env.agents
    from_scratch = args.start == "none"
    teacher = None
    if from_scratch:
        net = TribeNet(layout, hidden=args.hidden).to(device)
    else:
        net, _ = load_model(EXPERIMENTS / args.start / "model.pt", device)
        if args.teacher_coef > 0 and args.teacher_updates > 0:
            teacher, _ = load_model(EXPERIMENTS / args.start / "model.pt", device)
    critic = Critic(net.hidden, layout.vector_length).to(device)
    policy_optimiser = torch.optim.Adam(net.parameters(), lr=args.lr, eps=1e-5)
    critic_optimiser = torch.optim.Adam(critic.parameters(), lr=args.critic_lr, eps=1e-5)
    warmup = 0 if from_scratch else args.critic_warmup
    experiment = Experiment(args.name, "learning from outcomes", {**vars(args), "device": str(device), "agents": env.agents,
                                                                 "layout": layout.to_dict(), "workbook_sha256": env.hello.get("workbook_sha256"),
                                                                 "parameters": sum(q.numel() for q in net.parameters())})
    print(f"{experiment.id}: {args.envs} villages x {env.agents} villagers, {args.steps} ticks an update, {args.days}-day villages, from {args.start}")

    empathy_at = layout.fields["social stats"][0] + 1
    actor = Actor(net, env, device)
    teacher_actor = Actor(teacher, env, device) if teacher is not None else None
    frame = env.frame
    first = torch.zeros(n, device=device)
    recent = {k: [] for k in WATCHED + ["achievements"]}
    villages_done, steps_total, started = 0, 0, time.time()
    T = args.steps
    size = max(1, n // args.minibatches)
    as_tensor = lambda array, kind=None: torch.from_numpy(np.ascontiguousarray(array if kind is None else array.astype(kind))).to(device)

    for update in range(1, args.updates + 1):
        net.eval()
        buf = {"grid": torch.zeros(T, n, layout.grid_length, dtype=torch.uint8, device=device),
               "vector": torch.zeros(T, n, layout.vector_length, device=device),
               "action_mask": torch.zeros(T, n, layout.action_count, dtype=torch.uint8, device=device),
               "arg_mask": torch.zeros(T, n, layout.arg_count, dtype=torch.uint8, device=device),
               "action": torch.zeros(T, n, dtype=torch.long, device=device), "arg": torch.zeros(T, n, dtype=torch.long, device=device),
               "logprob": torch.zeros(T, n, device=device), "reward": torch.zeros(T, n, device=device),
               "alive": torch.zeros(T, n, device=device), "first": torch.zeros(T, n, device=device), "ended": torch.zeros(T, n, device=device)}
        if teacher is not None:
            buf["teacher"] = torch.zeros(T, n, layout.action_count, device=device)
        state0 = tuple(s.clone() for s in actor.state)

        for t in range(T):
            out = actor.act(frame)
            vector = as_tensor(frame["vector"].reshape(n, -1))
            action_mask = as_tensor(frame["action_mask"].reshape(n, -1))
            if teacher_actor is not None:
                with torch.no_grad():
                    logits, _, _, teacher_actor.state = teacher(as_tensor(frame["grid"].reshape(n, 1, -1)), vector.unsqueeze(1), teacher_actor.state)
                    buf["teacher"][t] = torch.log_softmax(masked(logits[:, 0].float(), action_mask), -1)
            alive = as_tensor(frame["alive"].reshape(n), np.float32)
            empathy = vector[:, empathy_at]
            buf["grid"][t] = as_tensor(frame["grid"].reshape(n, -1))
            buf["vector"][t], buf["action_mask"][t], buf["arg_mask"][t] = vector, action_mask, out["arg_mask"]
            buf["action"][t], buf["arg"][t], buf["logprob"][t] = out["action"], out["arg"], out["logprob"]
            buf["alive"][t], buf["first"][t] = alive, first

            frame = env.step(out["actions"])
            reward = as_tensor(frame["reward"].reshape(n, 2))
            buf["reward"][t] = reward[:, 0] * (1 - empathy / 2) + reward[:, 1] * empathy
            done = as_tensor(np.repeat(frame["done"], env.agents), np.float32)
            died = alive * (1 - as_tensor(frame["alive"].reshape(n), np.float32))
            # The stretch of experience ends for a villager when its village does or when it dies.
            buf["ended"][t] = torch.clamp(done + died, max=1)
            actor.forget(frame["done"])
            if teacher_actor is not None:
                teacher_actor.forget(frame["done"])
            first = done
            for result in frame["finished"]:
                if result["EndReason"] == "staggered":
                    continue  # cut short on purpose at the start, to put the villages out of step
                villages_done += 1
                for k in WATCHED:
                    recent[k].append(result["Metrics"].get(k, 0))
                recent["days"][-1] = result["Ticks"] / 240.0
                recent["alive"][-1] = result["Alive"]
                recent["achievements"].append(result["Achievements"])
        steps_total += T * n

        # What the critic makes of every situation of the stretch, and of the one it ended in.
        with torch.no_grad():
            value = torch.zeros(T, n, device=device)
            for i in range(0, n, size):
                idx = torch.arange(i, min(n, i + size), device=device)
                batch = {k: v[:, idx].transpose(0, 1) for k, v in buf.items()}
                held = run_lstm(net, net.encode(batch["grid"], batch["vector"]), (state0[0][:, idx].contiguous(), state0[1][:, idx].contiguous()), batch["first"])
                value[:, idx] = critic(held, batch["vector"]).transpose(0, 1)
            last_vector = as_tensor(frame["vector"].reshape(n, 1, -1))
            last_out, _ = net.lstm(net.encode(as_tensor(frame["grid"].reshape(n, 1, -1)), last_vector), actor.state)
            last_value = critic(last_out, last_vector)[:, 0]
            advantage = torch.zeros(T, n, device=device)
            running = torch.zeros(n, device=device)
            for t in range(T - 1, -1, -1):
                next_value = last_value if t == T - 1 else value[t + 1]
                going_on = 1 - buf["ended"][t]
                delta = buf["reward"][t] + args.gamma * next_value * going_on - value[t]
                running = delta + args.gamma * args.lam * going_on * running
                advantage[t] = running
            returns = advantage + value

        net.train()
        policy_on = update > warmup
        since = update - warmup - 1
        teacher_weight = args.teacher_coef * max(0.0, 1 - since / max(1, args.teacher_updates)) if teacher is not None and policy_on else 0.0
        sums = {"policy": 0.0, "value": 0.0, "entropy": 0.0, "kl": 0.0, "clipped": 0.0, "teacher": 0.0}
        batches, epochs_run, moved = 0, 0, 0.0
        for epoch in range(args.epochs):
            order = torch.randperm(n, device=device)
            moved = 0.0
            for i in range(0, n, size):
                idx = order[i:i + size]
                batch = {k: v[:, idx].transpose(0, 1) for k, v in buf.items()}
                batch["state"] = (state0[0][:, idx].contiguous(), state0[1][:, idx].contiguous())
                alive = batch["alive"]
                count = alive.sum().clamp(min=1)
                with torch.set_grad_enabled(policy_on):
                    logprob, entropy, action_logp, held = policy_of(net, batch)

                value_now = critic(held.detach(), batch["vector"])
                value_loss = (((value_now - returns[:, idx].transpose(0, 1)) ** 2) * alive).sum() / count
                critic_optimiser.zero_grad(set_to_none=True)
                value_loss.backward()
                torch.nn.utils.clip_grad_norm_(critic.parameters(), 1.0)
                critic_optimiser.step()

                adv = advantage[:, idx].transpose(0, 1)
                adv = (adv - (adv * alive).sum() / count) / (((adv ** 2 * alive).sum() / count).sqrt() + 1e-6)
                ratio = torch.exp(logprob - batch["logprob"])
                policy_loss = -(torch.min(ratio * adv, torch.clamp(ratio, 1 - args.clip, 1 + args.clip) * adv) * alive).sum() / count
                entropy_mean = (entropy * alive).sum() / count
                teacher_loss = torch.zeros((), device=device)
                if teacher is not None:
                    # How far the policy has moved from its starting self, on what it is seeing now.
                    teacher_logp = batch["teacher"]
                    teacher_loss = ((teacher_logp.exp() * (teacher_logp - action_logp)).sum(-1) * alive).sum() / count
                if policy_on:
                    loss = policy_loss - args.entropy * entropy_mean + teacher_weight * teacher_loss
                    policy_optimiser.zero_grad(set_to_none=True)
                    loss.backward()
                    torch.nn.utils.clip_grad_norm_(net.parameters(), 0.5)
                    policy_optimiser.step()
                with torch.no_grad():
                    kl = float(((batch["logprob"] - logprob) * alive).sum() / count)
                    moved += kl
                    sums["policy"] += float(policy_loss)
                    sums["value"] += float(value_loss)
                    sums["entropy"] += float(entropy_mean)
                    sums["kl"] += kl
                    sums["clipped"] += float((((ratio - 1).abs() > args.clip).float() * alive).sum() / count)
                    sums["teacher"] += float(teacher_loss)
                batches += 1
            epochs_run += 1
            if policy_on and moved / args.minibatches > args.target_kl:
                break

        mean = lambda xs, last=40: float(np.mean(xs[-last:])) if xs else None
        explained = 1 - float(((returns - value) ** 2 * buf["alive"]).sum() / (((returns - (returns * buf["alive"]).sum() / buf["alive"].sum().clamp(min=1)) ** 2 * buf["alive"]).sum() + 1e-6))
        row = {"update": update, "villager_ticks": steps_total, "villages": villages_done,
               "reward_per_tick": float((buf["reward"] * buf["alive"]).sum() / buf["alive"].sum().clamp(min=1)),
               "alive_share": float(buf["alive"].mean()),
               "days_lived": mean(recent["days"]), "alive_at_end": mean(recent["alive"]), "achievements": mean(recent["achievements"]),
               "buildings": mean(recent["built"]), "technologies": mean(recent["techs"]), "deaths": mean(recent["deaths"]),
               "crafted": mean(recent["crafted"]), "gathered": mean(recent["gathered"]),
               **{k: v / max(1, batches) for k, v in sums.items()}, "critic_explains": explained, "epochs": epochs_run,
               "teacher_weight": teacher_weight, "policy_learning": int(policy_on), "ticks_per_second": round(steps_total / (time.time() - started))}
        experiment.log(row)
        if update % 10 == 0 or update == args.updates:
            experiment.curves("update", {"Reward per villager-tick": ["reward_per_tick"],
                                         f"Days a village lasts (of {args.days})": ["days_lived"],
                                         "Share of villagers alive": ["alive_share"],
                                         "Achievements per village": ["achievements"]})
            torch.save(to_save(net, critic=critic.state_dict(), experiment=experiment.id), experiment.folder / "model.pt")
            show = lambda v: "-" if v is None else f"{v:.2f}"
            print(f"update {update:4d}  reward/tick {row['reward_per_tick']:+.4f}  alive {row['alive_share']:.2f}  days {show(row['days_lived'])}  "
                  f"achievements {show(row['achievements'])}  entropy {row['entropy']:.2f}  kl {row['kl']:.4f}  from start {row['teacher']:.3f}  "
                  f"critic {row['critic_explains']:.2f}  {row['ticks_per_second']:,}/s  villages {villages_done}", flush=True)

    env.close()
    export_onnx(net.cpu(), str(experiment.folder / "model.onnx"))
    rows = experiment.rows
    learning = [r for r in rows if r["policy_learning"]] or rows
    before = [r for r in rows if not r["policy_learning"]] or rows[:max(1, len(rows) // 10)]
    tenth = max(1, len(learning) // 10)
    avg = lambda part, key: float(np.mean([r[key] for r in part if r.get(key) is not None])) if any(r.get(key) is not None for r in part) else float("nan")
    keys = ["reward_per_tick", "alive_share", "days_lived", "alive_at_end", "achievements", "buildings", "technologies", "gathered", "crafted"]
    table = [{"": "before the policy changed (critic warm-up)" if warmup else "first tenth of training", **{k: avg(before, k) for k in keys}},
             {"": "last tenth of training", **{k: avg(learning[-tenth:], k) for k in keys}}]
    shaping = "as the workbook has it (0: outcomes only)" if args.shaping < 0 else str(args.shaping)
    hold = ("nothing to start from: random weights" if from_scratch else
            f"only the critic for the first {warmup} updates; then the policy held to its starting self with weight {args.teacher_coef} fading to 0 over "
            f"{args.teacher_updates} updates")
    journal = f"""{args.note or 'The network plays villages and learns from what the engine pays.'}

**Setup.** Started from `{args.start}` ({hold}). {args.envs} villages of {env.agents} at once, each played for {args.days} days and then replaced by a
new map{"" if args.together else ", the villages out of step so that every update holds every season"}; {args.updates} updates of {args.steps} ticks = {steps_total:,} villager-ticks over {villages_done} villages.
Reward: Sim Rewards with shaping {shaping}, a villager's own and the village's parts mixed by its empathy. PPO: gamma {args.gamma}, lambda {args.lam},
clip {args.clip}, up to {args.epochs} epochs x {args.minibatches} minibatches stopping at KL {args.target_kl}, policy lr {args.lr}, entropy {args.entropy};
a critic of its own (lr {args.critic_lr}) whose errors do not reach the policy.

**Before and after** (averages over the updates named; villages that ended in them):

{markdown_table(table)}

"alive_share" is the share of villagers alive over all the ticks played in an update. Curves: `curves.svg`. These are villages of {args.days} days played
while learning, the policy picking by chance in proportion to its scores; the evaluation that follows plays whole years on maps not used here."""
    experiment.finish({**{("end_" + k): table[1][k] for k in keys}, **{("start_" + k): table[0][k] for k in keys},
                       "villager_ticks": steps_total, "villages": villages_done, "started_from": args.start}, journal)
    print("->", experiment.folder)


if __name__ == "__main__":
    main()
