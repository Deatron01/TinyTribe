"""Stage 1: imitation. Train the villager's network to choose what the recorded villagers chose.

    ML/.venv/Scripts/python ML/train_bc.py --dataset ML/data/datasets/bots --name bc-bots

The data is recorded runs exported by `tinytribe-host export`. Whole villages are held out for
validation, so the accuracy reported is on villages the network never saw.

The loss is the cross-entropy of the action, plus that of its argument for actions that take one.
Both are taken over what was possible at the time only (the engine's masks). With --return-weight
above 0 it becomes the design's custom loss: each example is weighted by how well things went for
that villager afterwards, with its own and the village's rewards mixed by its empathy
(R_total = R_individual x (1 - empathy / 2) + R_global x empathy), so that what led to good
outcomes is imitated more strongly than what did not.
"""
import argparse
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F

from dataset import Windows, load_episodes, split_by_seed
from experiment import Experiment, markdown_table
from model import TribeNet, export_onnx, masked, to_save


def to_device(batch: dict, device) -> dict:
    return {k: torch.from_numpy(np.ascontiguousarray(v)).to(device, non_blocking=True) for k, v in batch.items()}


def action_weights(episodes, power: float, device) -> torch.Tensor:
    """A weight per action for the loss: the rarer the action among the recorded choices, the heavier.

    weight = (1 / (share x number of actions used)) ^ power, kept between 0.25 and 8. power 0 is no
    weighting; 1 would make every action count the same in total however rare. Without it the loss is
    nearly all sleeping, waiting and walking, and the few ticks on which a villager eats, drinks or
    picks up its spear, which decide whether it lives, are hardly learned.
    """
    counts = np.zeros(episodes[0].layout.action_count)
    for e in episodes:
        counts += np.bincount(np.asarray(e.action[..., 0])[np.asarray(e.alive) > 0], minlength=len(counts))
    share = counts / max(1.0, counts.sum())
    used = max(1, int((counts > 0).sum()))
    weight = np.where(counts > 0, (1.0 / np.maximum(share * used, 1e-9)) ** power, 1.0)
    return torch.tensor(np.clip(weight, 0.25, 8.0), dtype=torch.float32, device=device)


def losses(net, batch, takes_argument, burn_in: int, weight_scale: float, stats: tuple, action_weight=None, turn: float = 1.0):
    """Returns the loss and the counts behind the accuracies, over the rows that count: the villager was
    alive, the LSTM has warmed up, and what the villager chose was possible when it chose it."""
    action_logits, arg_logits, _, _ = net(batch["grid"], batch["vector"].float())
    action_logits, arg_logits = action_logits.float(), arg_logits.float()
    action, arg = batch["action"][..., 0].long(), batch["action"][..., 1].long()
    possible = batch["action_mask"].gather(-1, action.unsqueeze(-1)).squeeze(-1) > 0
    needs_arg = takes_argument[action]
    arg_possible = batch["arg_mask"].gather(-1, arg.unsqueeze(-1)).squeeze(-1) > 0
    counted = (batch["alive"] > 0) & possible & (~needs_arg | arg_possible)
    counted[:, :burn_in] = False

    action_scores = masked(action_logits, batch["action_mask"])
    action_loss = F.cross_entropy(action_scores.flatten(0, 1), action.flatten(), reduction="none").view_as(action)
    row = arg_logits.gather(2, action[..., None, None].expand(-1, -1, 1, arg_logits.shape[-1])).squeeze(2)
    arg_scores = masked(row, batch["arg_mask"])
    arg_loss = F.cross_entropy(arg_scores.flatten(0, 1), arg.flatten(), reduction="none").view_as(arg)
    with_arg = counted & needs_arg

    weight = torch.ones_like(action_loss)
    if weight_scale > 0 and "return" in batch:
        mean, std = stats
        z = ((batch["return"].float() - mean) / std).clamp(-3, 3)
        weight = torch.exp(weight_scale * z)
    if action_weight is not None:
        weight = weight * action_weight[action]
    if turn != 1.0:
        # The ticks on which a villager begins something else than it was doing (sets off, turns, arrives, starts to
        # work or to build) decide how a day goes, and are few among the ticks on which it simply carries on.
        changed = torch.ones_like(counted)
        changed[:, 1:] = (batch["action"][:, 1:] != batch["action"][:, :-1]).any(dim=-1)
        weight = weight * torch.where(changed, torch.full_like(weight, turn), torch.ones_like(weight))
    weight = weight / weight[counted].mean().clamp(min=1e-6) if counted.any() else weight

    n = counted.sum().clamp(min=1)
    loss = (action_loss * weight * counted).sum() / n + (arg_loss * weight * with_arg).sum() / n
    with torch.no_grad():
        action_hit = (action_scores.argmax(-1) == action) & counted
        arg_hit = (arg_scores.argmax(-1) == arg) & with_arg
        counts = {
            "rows": int(counted.sum()), "arg_rows": int(with_arg.sum()), "loss_sum": float(loss) * int(n),
            "action_hits": int(action_hit.sum()), "arg_hits": int(arg_hit.sum()),
            "both_hits": int((action_hit & (~needs_arg | arg_hit)).sum()),
            "per_action_rows": torch.bincount(action[counted], minlength=action_logits.shape[-1]).cpu().numpy(),
            "per_action_hits": torch.bincount(action[action_hit], minlength=action_logits.shape[-1]).cpu().numpy(),
        }
    return loss, counts


def add(total: dict, counts: dict):
    for k, v in counts.items():
        total[k] = total.get(k, 0) + v


def rates(total: dict, prefix: str) -> dict:
    rows = max(1, total["rows"])
    return {prefix + "loss": total["loss_sum"] / rows, prefix + "action_acc": total["action_hits"] / rows,
            prefix + "arg_acc": total["arg_hits"] / max(1, total["arg_rows"]), prefix + "both_acc": total["both_hits"] / rows}


def baselines(episodes) -> dict:
    """What accuracy takes no learning: always the commonest action, and a blind pick among what is possible."""
    counts = np.zeros(episodes[0].layout.action_count)
    blind, rows = 0.0, 0
    for e in episodes:
        alive = np.asarray(e.alive) > 0
        counts += np.bincount(np.asarray(e.action[..., 0])[alive], minlength=len(counts))
        options = np.asarray(e.action_mask).sum(axis=-1)[alive]
        blind += float((1.0 / np.maximum(1, options)).sum())
        rows += int(alive.sum())
    return {"most_common_action": int(counts.argmax()), "most_common_acc": float(counts.max() / max(1, rows)), "blind_pick_acc": blind / max(1, rows)}


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--dataset", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--epochs", type=int, default=12)
    p.add_argument("--length", type=int, default=128, help="ticks in a training window")
    p.add_argument("--blocks", type=int, default=12, help="windows in a batch; each holds every villager of a village")
    p.add_argument("--burn-in", type=int, default=16, help="ticks at the start of a window that only warm the LSTM up")
    p.add_argument("--hidden", type=int, default=128)
    p.add_argument("--lr", type=float, default=1e-3)
    p.add_argument("--val", type=float, default=0.17, help="share of villages held out")
    p.add_argument("--return-weight", type=float, default=0.0, help="0: plain imitation; above 0: the custom loss, weighting by what came after")
    p.add_argument("--gamma", type=float, default=0.999)
    p.add_argument("--balance", type=float, default=0.0, help="0: every tick counts the same; above 0: rare actions weigh more (0.5 is a good start)")
    p.add_argument("--extra", default="", help="comma-separated dataset folders gathered by correction runs (train_dagger.py), to learn from as well; "
                                               "the villages held out stay the recorded ones")
    p.add_argument("--raw", action="store_true", help="the vector as it comes, items and jobs as single numbers (the first networks)")
    p.add_argument("--seed", type=int, default=0)
    p.add_argument("--note", default="", help="why this experiment is being run; goes into the journal")
    args = p.parse_args()

    torch.manual_seed(args.seed)
    rng = np.random.default_rng(args.seed)
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    episodes = load_episodes(args.dataset)
    train, val = split_by_seed(episodes, args.val)
    extra = [e for folder in args.extra.split(",") if folder for e in load_episodes(folder)]
    train = train + extra
    layout = episodes[0].layout

    returns, stats = None, (0.0, 1.0)
    if args.return_weight > 0:
        returns = {e.folder.name: e.returns(args.gamma) for e in episodes + extra}
        every = np.concatenate([returns[e.folder.name][np.asarray(e.alive) > 0] for e in train])
        stats = (float(every.mean()), float(every.std() + 1e-6))

    net = TribeNet(layout, hidden=args.hidden, decode=not args.raw).to(device)
    parameters = sum(p.numel() for p in net.parameters())
    optimiser = torch.optim.AdamW(net.parameters(), lr=args.lr, weight_decay=1e-4)
    train_windows = Windows(train, args.length, args.blocks, rng, returns)
    val_windows = Windows(val, args.length, args.blocks, rng, returns)
    schedule = torch.optim.lr_scheduler.OneCycleLR(optimiser, max_lr=args.lr, total_steps=args.epochs * len(train_windows))
    takes_argument = torch.from_numpy(train_windows.takes_argument).to(device)
    balance = action_weights(train, args.balance, device) if args.balance > 0 else None
    base = baselines(val)

    experiment = Experiment(args.name, "imitation", {
        **vars(args), "device": str(device), "parameters": parameters, "layout": layout.to_dict(),
        "train_villages": [int(e.meta["seed"]) for e in train], "val_villages": [int(e.meta["seed"]) for e in val],
        "train_rows": sum(e.rows for e in train), "val_rows": sum(e.rows for e in val),
        "workbook_sha256": episodes[0].meta.get("workbook_sha256"), "demonstrator": sorted({e.meta["brains"] for e in episodes}),
        "baselines": base,
    })
    print(f"{experiment.id}: {len(train)} villages to learn from ({experiment.config['train_rows']} rows), {len(val)} held out, "
          f"{parameters} parameters on {device}")

    scaler = torch.amp.GradScaler(enabled=device.type == "cuda")
    best, per_action = None, None
    for epoch in range(1, args.epochs + 1):
        started = time.time()
        net.train()
        seen = {}
        for batch in train_windows.batches(shuffle=True):
            batch = to_device(batch, device)
            with torch.autocast(device_type=device.type, dtype=torch.float16, enabled=device.type == "cuda"):
                loss, counts = losses(net, batch, takes_argument, args.burn_in, args.return_weight, stats, balance)
            optimiser.zero_grad(set_to_none=True)
            scaler.scale(loss).backward()
            scaler.unscale_(optimiser)
            torch.nn.utils.clip_grad_norm_(net.parameters(), 1.0)
            scaler.step(optimiser)
            scaler.update()
            schedule.step()
            add(seen, counts)

        net.eval()
        held = {}
        with torch.no_grad():
            for batch in val_windows.batches(shuffle=False):
                batch = to_device(batch, device)
                with torch.autocast(device_type=device.type, dtype=torch.float16, enabled=device.type == "cuda"):
                    _, counts = losses(net, batch, takes_argument, args.burn_in, 0.0, stats)
                add(held, counts)
        row = {"epoch": epoch, **rates(seen, "train_"), **rates(held, "val_"), "seconds": round(time.time() - started, 1)}
        experiment.log(row)
        experiment.curves("epoch", {"Loss": ["train_loss", "val_loss"], "Action chosen as recorded": ["train_action_acc", "val_action_acc"],
                                    "Argument chosen as recorded": ["train_arg_acc", "val_arg_acc"]})
        print(f"epoch {epoch:2d}  loss {row['train_loss']:.4f} / {row['val_loss']:.4f}   action {row['train_action_acc']:.3f} / {row['val_action_acc']:.3f}"
              f"   argument {row['val_arg_acc']:.3f}   both {row['val_both_acc']:.3f}   {row['seconds']} s")
        if best is None or row["val_both_acc"] >= best["val_both_acc"]:
            best, per_action = row, held
            torch.save(to_save(net, experiment=experiment.id), experiment.folder / "model.pt")

    saved = torch.load(experiment.folder / "model.pt", map_location="cpu")
    net.load_state_dict(saved["state"])
    export_onnx(net, str(experiment.folder / "model.onnx"))

    names = episodes[0].meta["actions"]
    recall = [{"action": names[i], "share of choices": per_action["per_action_rows"][i] / max(1, per_action["rows"]),
               "chosen as recorded": per_action["per_action_hits"][i] / per_action["per_action_rows"][i]}
              for i in np.argsort(-per_action["per_action_rows"]) if per_action["per_action_rows"][i] > 0]
    summary = {
        "val_action_acc": best["val_action_acc"], "val_arg_acc": best["val_arg_acc"], "val_both_acc": best["val_both_acc"], "val_loss": best["val_loss"],
        "best_epoch": best["epoch"], "most_common_acc": base["most_common_acc"], "blind_pick_acc": base["blind_pick_acc"],
        "train_rows": experiment.config["train_rows"], "val_rows": experiment.config["val_rows"], "parameters": parameters,
        "per_action": recall,
    }
    loss_name = ("the custom loss: imitation weighted by exp(%.2g x return), returns mixed by empathy, gamma %.4g" % (args.return_weight, args.gamma)
                 if args.return_weight > 0 else "plain imitation (cross-entropy over what was possible)")
    if args.balance > 0:
        loss_name += ", rare actions weighted up (power %.2g)" % args.balance
    journal = f"""{args.note or 'Imitation of recorded villagers.'}

**Data.** `{args.dataset}`: {len(episodes)} recorded villages played by {', '.join(experiment.config['demonstrator'])}; {len(train) - len(extra)} to learn from
({experiment.config['train_rows']:,} villager-ticks), {len(val)} held out whole ({experiment.config['val_rows']:,}).
{('It also learned from ' + str(len(extra)) + ' villages that earlier networks played with the bot advising on every tick: ' + args.extra + '. ') if extra else ''}**Network.** CNN over the 11 x 11 view ({layout.planes} one-hot planes), MLP over the {layout.vector_length}-value vector, LSTM ({args.hidden}), heads for
{layout.action_count} actions and {layout.action_count} x {layout.arg_count} arguments; {parameters:,} parameters.
**Training.** {args.epochs} epochs, windows of {args.length} ticks ({args.burn_in} to warm the LSTM up), {args.blocks} villages a batch, AdamW, lr {args.lr}, {loss_name}.

**Result on the held-out villages** (best epoch, {best['epoch']}):

{markdown_table([
    {"": "this network", "action as recorded": best["val_action_acc"], "argument as recorded": best["val_arg_acc"], "both": best["val_both_acc"]},
    {"": "always the commonest action (" + names[base["most_common_action"]] + ")", "action as recorded": base["most_common_acc"], "argument as recorded": "", "both": ""},
    {"": "a blind pick among what is possible", "action as recorded": base["blind_pick_acc"], "argument as recorded": "", "both": ""},
])}

By action, most chosen first:

{markdown_table(recall[:12])}

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network."""
    experiment.finish(summary, journal)
    print(f"best epoch {best['epoch']}: action {best['val_action_acc']:.3f}, argument {best['val_arg_acc']:.3f}, both {best['val_both_acc']:.3f} "
          f"(commonest action {base['most_common_acc']:.3f}, blind pick {base['blind_pick_acc']:.3f})\n-> {experiment.folder}")


if __name__ == "__main__":
    main()
