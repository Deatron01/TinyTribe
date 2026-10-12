"""Stage 1b: learning from its own mistakes, with the bot correcting (DAgger).

    ML/.venv/Scripts/python ML/train_dagger.py --start <imitation experiment id> --dataset ML/data/datasets/bots --name dagger-bots

Plain imitation only ever sees villages run by the bots. Run by the network, a village soon drifts
into situations the bots never got into, and there the network has learned nothing. So, in rounds:

  1. the network plays villages itself (mixed with the bot's choices in the first rounds);
  2. on every tick the bot says what it would have done in that villager's place;
  3. those situations, labelled with the bot's choices, are added to the training data;
  4. the network trains on everything gathered so far.

The simulation program gives the bot's advice (`expert`); the villages are played here. Each round's
data is kept as a dataset like any other, one folder per village.
"""
import argparse
import json
import time
from pathlib import Path

import numpy as np
import torch

from dataset import Episode, Windows, load_episodes, split_by_seed
from evaluate import load_model
from experiment import EXPERIMENTS, ROOT, Experiment, markdown_table
from model import export_onnx, to_save
from train_bc import action_weights, add, losses, pair_weights, rates, to_device
from tribe_env import Actor, TribeEnv


def save_npy(path: Path, array: np.ndarray):
    np.save(path, np.ascontiguousarray(array))


def collect(net, device, folder: Path, round_number: int, villages: int, horizon: int, seed: int, bot_share: float, rng, remembers: bool = False,
            slots: int = 0, temperature: float = 1.0) -> dict:
    """Plays `villages` villages for up to `horizon` ticks, the network choosing (the bot instead with
    probability bot_share, village by village and tick by tick), and writes one episode per village
    with the bot's choice as the label. Returns what happened."""
    env = TribeEnv(envs=villages, max_ticks=horizon, seed=seed, expert=True, expert_remembers=remembers, max_agents=slots)
    lay, agents = env.layout, env.agents
    actor = Actor(net, env, device, temperature=temperature)
    keep = {name: [] for name in ("grid", "vector", "action_mask", "arg_mask", "expert", "taken", "alive", "reward")}
    ended = np.full(villages, -1)
    results = {}
    agree = total = 0
    frame = env.frame
    try:
        for tick in range(horizon):
            out = actor.act(frame)
            learner, expert, alive = out["actions"], frame["expert"], frame["alive"] > 0
            agree += int(((learner == expert).all(-1) & alive).sum())
            total += int(alive.sum())
            use_bot = rng.random(villages) < bot_share
            actions = np.where(use_bot[:, None, None], expert, learner)
            for name, value in (("grid", frame["grid"]), ("vector", frame["vector"]), ("action_mask", frame["action_mask"]),
                                ("arg_mask", frame["arg_mask"]), ("expert", expert), ("taken", actions), ("alive", frame["alive"])):
                keep[name].append(value.copy())
            frame = env.step(actions)
            keep["reward"].append(frame["reward"].copy())
            actor.forget(frame["done"])
            for result in frame["finished"]:
                e = result["Env"]
                if ended[e] < 0:
                    ended[e] = tick + 1
                    results[e] = result
            if (ended >= 0).all():
                break
    finally:
        env.close()

    stacked = {name: np.stack(values) for name, values in keep.items()}       # [T, E, A, ...]
    ticks = stacked["alive"].shape[0]
    rows = 0
    for e in range(villages):
        length = int(ended[e]) if ended[e] >= 0 else ticks
        # Places no child was born into are left out: they are rows of nothing.
        born = np.flatnonzero(stacked["alive"][:length, e].any(axis=0))
        used = int(born.max()) + 1 if len(born) else 1
        episode = folder / f"round{round_number:02d}-village{e:02d}"
        episode.mkdir(parents=True, exist_ok=True)
        for name, file in (("grid", "grid"), ("vector", "vector"), ("action_mask", "action_mask"), ("arg_mask", "arg_mask"),
                           ("expert", "action"), ("taken", "taken"), ("alive", "alive"), ("reward", "reward")):
            save_npy(episode / (file + ".npy"), stacked[name][:length, e, :used])
        rows += int(stacked["alive"][:length, e].sum())
        (episode / "episode.json").write_text(json.dumps({
            "run_id": episode.name, "seed": seed + e, "brains": f"network corrected by the bot (round {round_number}, bot plays {bot_share:.0%})",
            "ticks": length, "agents": used, "workbook_sha256": env.hello.get("workbook_sha256"), "layout": lay.to_dict(),
            "actions": env.action_names, "takes_argument": [bool(t) for t in env.takes_argument],
            "villagers": [{"empathy": 0.5}] * used,
            "arrays": {"action": "what the scripted bot would have done (the label)", "taken": "what was actually played"},
        }, indent=1), encoding="utf-8")
    lived = np.array([results[e]["Ticks"] if e in results else ticks for e in range(villages)], dtype=float)
    alive_end = np.array([results[e]["Alive"] if e in results else int(stacked["alive"][-1, e].sum()) for e in range(villages)], dtype=float)
    return {"rows": rows, "agreement": agree / max(1, total), "ticks_lived": float(lived.mean()), "alive_at_end": float(alive_end.mean()),
            "villages_wiped_out": int(sum(1 for e in results if results[e]["EndReason"] == "everyone_dead"))}


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--start", required=True, help="experiment id of the network to start from")
    p.add_argument("--dataset", required=True, help="the recorded-bot dataset the starting network was trained on")
    p.add_argument("--name", required=True)
    p.add_argument("--rounds", type=int, default=6)
    p.add_argument("--villages", type=int, default=24)
    p.add_argument("--horizon", type=int, default=2400, help="ticks a village is played for in a round (240 = one day)")
    p.add_argument("--bot-share", default="0.5,0.3,0.15,0", help="how often the bot's choice is played, per round; the last value repeats")
    p.add_argument("--epochs", type=int, default=3, help="passes over everything gathered, after each round")
    p.add_argument("--length", type=int, default=128)
    p.add_argument("--blocks", type=int, default=12)
    p.add_argument("--burn-in", type=int, default=16)
    p.add_argument("--lr", type=float, default=3e-4)
    p.add_argument("--val", type=float, default=0.17)
    p.add_argument("--seed", type=int, default=5000, help="first seed of the villages played; each round uses new ones")
    p.add_argument("--balance", type=float, default=0.5, help="rare actions weigh more in the loss; 0 for none")
    p.add_argument("--pairs", action="store_true", help="--balance weighs each action with its argument (a pickaxe apart from a cooked fish), not the action alone")
    p.add_argument("--turn", type=float, default=1.0, help="how much more a tick counts on which the advice is to begin something else than the tick before; 1 for no more")
    p.add_argument("--remembers", action="store_true", help="the advising bot keeps its plans whatever the learner does (the old way); "
                                                              "by default it keeps them only while the learner does as advised")
    p.add_argument("--extra", default="", help="comma-separated dataset folders gathered by earlier correction runs, to train on as well")
    p.add_argument("--slots", type=int, default=0, help="places per village: the villagers it starts with and the children born to them; "
                                                      "0 for no children, which is right for rounds too short for any (a wedding takes three weeks)")
    p.add_argument("--temperature", type=float, default=1.0, help="how the network picks while it plays: below 1, closer to its highest-scoring choice")
    p.add_argument("--batches", type=int, default=500, help="at most this many batches in a pass, drawn afresh from everything gathered, so a round takes the same time however much there is")
    p.add_argument("--note", default="")
    args = p.parse_args()

    torch.manual_seed(0)
    rng = np.random.default_rng(0)
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    net, saved = load_model(EXPERIMENTS / args.start / "model.pt", device)
    recorded = load_episodes(args.dataset)
    bot_train, bot_val = split_by_seed(recorded, args.val)
    earlier = [e for folder in args.extra.split(",") if folder for e in load_episodes(folder)]
    shares = [float(x) for x in args.bot_share.split(",")]
    experiment = Experiment(args.name, "imitation with correction", {**vars(args), "started_from": args.start, "device": str(device),
                                                                    "recorded_rows": sum(e.rows for e in bot_train)})
    own = ROOT / "ML" / "data" / "datasets" / experiment.id
    optimiser = torch.optim.AdamW(net.parameters(), lr=args.lr, weight_decay=1e-4)
    scaler = torch.amp.GradScaler(enabled=device.type == "cuda")
    takes_argument = None
    gathered, table = [], []
    print(f"{experiment.id}: from {args.start}; {len(bot_train)} recorded villages, {args.rounds} rounds of {args.villages} villages x {args.horizon} ticks")

    for round_number in range(1, args.rounds + 1):
        share = shares[min(round_number - 1, len(shares) - 1)]
        started = time.time()
        net.eval()
        played = collect(net, device, own, round_number, args.villages, args.horizon, args.seed + (round_number - 1) * args.villages, share, rng,
                         args.remembers, args.slots, args.temperature)
        gathered = [Episode(f) for f in sorted(own.iterdir()) if (f / "episode.json").exists()]
        collect_seconds = time.time() - started

        windows = Windows(bot_train + earlier + gathered, args.length, args.blocks, rng)
        held_out = Windows(bot_val, args.length, args.blocks, rng)
        takes_argument = torch.from_numpy(windows.takes_argument).to(device)
        balance = None
        if args.balance > 0:
            balance = (pair_weights if args.pairs else action_weights)(bot_train + earlier + gathered, args.balance, device)
        net.train()
        seen = {}
        for _ in range(args.epochs):
            for number, batch in enumerate(windows.batches(shuffle=True)):
                if number >= args.batches:
                    break
                batch = to_device(batch, device)
                with torch.autocast(device_type=device.type, dtype=torch.float16, enabled=device.type == "cuda"):
                    loss, counts = losses(net, batch, takes_argument, args.burn_in, 0.0, (0.0, 1.0), balance, args.turn)
                optimiser.zero_grad(set_to_none=True)
                scaler.scale(loss).backward()
                scaler.unscale_(optimiser)
                torch.nn.utils.clip_grad_norm_(net.parameters(), 1.0)
                scaler.step(optimiser)
                scaler.update()
                add(seen, counts)
        net.eval()
        held = {}
        with torch.no_grad():
            for batch in held_out.batches(shuffle=False):
                batch = to_device(batch, device)
                with torch.autocast(device_type=device.type, dtype=torch.float16, enabled=device.type == "cuda"):
                    _, counts = losses(net, batch, takes_argument, args.burn_in, 0.0, (0.0, 1.0))
                add(held, counts)

        row = {"round": round_number, "bot_share": share, "days_lived": played["ticks_lived"] / 240.0, "alive_at_end": played["alive_at_end"],
               "villages_wiped_out": played["villages_wiped_out"], "agreement_while_playing": played["agreement"],
               "rows_gathered": sum(e.rows for e in earlier + gathered), **rates(seen, "train_"), **rates(held, "val_"),
               "collect_seconds": round(collect_seconds), "train_seconds": round(time.time() - started - collect_seconds)}
        experiment.log(row)
        experiment.curves("round", {"Days a village lasted (of %.0f)" % (args.horizon / 240): ["days_lived"],
                                    "Same choice as the bot": ["agreement_while_playing", "val_both_acc"],
                                    "Loss": ["train_loss", "val_loss"]})
        table.append({"round": round_number, "bot plays": f"{share:.0%}", "days lasted": row["days_lived"], "alive at the end": row["alive_at_end"],
                      "wiped out": f"{row['villages_wiped_out']} of {args.villages}", "same choice as the bot, while playing": row["agreement_while_playing"],
                      "on recorded villages (held out)": row["val_both_acc"]})
        print(f"round {round_number}: bot plays {share:.0%}; villages lasted {row['days_lived']:.1f} days, {row['alive_at_end']:.1f} alive, "
              f"{row['villages_wiped_out']} wiped out; agreement {row['agreement_while_playing']:.3f} playing / {row['val_both_acc']:.3f} recorded; "
              f"{row['collect_seconds']} s + {row['train_seconds']} s")
        torch.save(to_save(net, experiment=experiment.id), experiment.folder / "model.pt")

    export_onnx(net.cpu(), str(experiment.folder / "model.onnx"))
    last = experiment.rows[-1]
    journal = f"""{args.note or 'The network plays villages itself and the bot corrects it (DAgger).'}

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** {"It keeps its own plans whatever the network does." if args.remembers else "It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see."}
Rare actions are weighted up in the loss (power {args.balance}).
**Setup.** Started from `{args.start}`. {args.rounds} rounds; each plays {args.villages} new villages for up to {args.horizon} ticks
({args.horizon / 240:.0f} days), then trains {args.epochs} passes over the {len(bot_train)} recorded villages plus everything gathered so far
(lr {args.lr}). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/{experiment.id}/`.{(" It also trained on what earlier correction runs gathered: " + args.extra + ".") if args.extra else ""}

{markdown_table(table)}

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows."""
    experiment.finish({"days_lived_last_round": last["days_lived"], "alive_at_end_last_round": last["alive_at_end"],
                       "agreement_while_playing": last["agreement_while_playing"], "val_both_acc": last["val_both_acc"],
                       "rows_gathered": last["rows_gathered"], "started_from": args.start}, journal)
    print("->", experiment.folder)


if __name__ == "__main__":
    main()
