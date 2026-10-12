"""Let a trained network play whole villages, record them, and compare with the scripted bots on the same maps.

    ML/.venv/Scripts/python ML/evaluate.py --model ML/experiments/<id>/model.pt --name eval-<what> --villages 16

Every village is recorded like any other run (ML/data/runs/<experiment>/), so each has its
report.md, counters and achievements, and can be replayed. The bots (and, with --random, villagers
choosing blindly) play the same seeds for comparison; their runs are kept and reused.
"""
import argparse
import json
import subprocess
import time
from pathlib import Path

import numpy as np
import pandas as pd
import torch

from experiment import Experiment, markdown_table
from model import saved_net
from tribe_env import DATA_ARGS, HOST, ROOT, Actor, TribeEnv

RUNS = ROOT / "ML" / "data" / "runs"

# What a village is judged by: columns of the comparison, each a counter of the run (see Sim Run Log Schema).
MEASURES = [("days lived", "days"), ("alive at the end", "alive"), ("achievements", None), ("buildings", "built"), ("weddings", "marriages"), ("children born", "births"),
            ("technologies", "techs"), ("things crafted", "crafted"), ("things gathered", "gathered"), ("crops reaped", "crops_reaped")]


def load_model(path, device):
    saved = torch.load(path, map_location="cpu")
    return saved_net(saved).to(device).eval(), saved


def read_runs(folder: Path, seeds: set) -> list:
    """The finished runs of those seeds in a runs folder, one per seed (the first that ran its course)."""
    runs = {}
    for run_file in sorted(folder.glob("*/run.json")):
        run = json.loads(run_file.read_text(encoding="utf-8"))
        seed, result = int(run["setup"]["seed"]), run.get("result")
        if seed in seeds and seed not in runs and result and result["end_reason"] in ("time_up", "everyone_dead"):
            runs[seed] = run
    return [runs[s] for s in sorted(runs)]


def measure(runs: list) -> dict:
    rows = []
    for run in runs:
        result, metrics = run["result"], run["result"]["metrics"]
        row = {"seed": run["setup"]["seed"], "ticks": result["ticks"]}
        for title, metric in MEASURES:
            row[title] = len(result["achievements"]) if metric is None else (result["alive"] if metric == "alive" else metrics.get(metric, 0))
        row["days lived"] = result["ticks"] / 240.0
        row["villager-days"] = sum(a["ticks_alive"] for a in result["agents"]) / 240.0
        row["reward per villager-tick"] = metrics.get("reward_mean_per_agent_tick", 0)
        rows.append(row)
    return pd.DataFrame(rows)


def reached(runs: list) -> dict:
    """For each achievement, the share of villages that reached it."""
    counts = {}
    for run in runs:
        for a in run["result"]["achievements"]:
            counts[a["name"]] = counts.get(a["name"], 0) + 1
    return {name: n / max(1, len(runs)) for name, n in counts.items()}


def play(net, device, folder: Path, label: str, villages: int, seed: int, days: int, greedy: bool, options: dict, temperature: float = 1.0) -> float:
    """Plays one village per seed to its end, recording into folder. Returns villager-ticks per second."""
    env = TribeEnv(envs=villages, max_ticks=days * 240, seed=seed, record_folder=str(folder), brains_label=label, **options)
    actor = Actor(net, env, device, greedy=greedy, temperature=temperature)
    finished, steps, started = set(), 0, time.time()
    frame = env.frame
    try:
        while len(finished) < villages:
            out = actor.act(frame)
            frame = env.step(out["actions"])
            actor.forget(frame["done"])
            steps += 1
            for result in frame["finished"]:
                if seed <= result["Seed"] < seed + villages:
                    finished.add(result["Seed"])
    finally:
        env.close()
    return steps * villages * env.agents / max(1e-6, time.time() - started)


def bots(kind: str, seed: int, villages: int, days: int) -> Path:
    """The scripted players on the same seeds; played once and kept."""
    folder = RUNS / f"{kind}-seeds{seed}-{seed + villages - 1}-{days}d"
    if len(read_runs(folder, set(range(seed, seed + villages)))) < villages if folder.exists() else True:
        subprocess.run([str(HOST), "batch", "--out", str(folder), "--seeds", f"{seed}-{seed + villages - 1}", "--days", str(days), "--brains", kind] + DATA_ARGS,
                       check=True, cwd=ROOT, stdout=subprocess.DEVNULL)
    return folder


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--model", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--villages", type=int, default=16)
    p.add_argument("--seed", type=int, default=1001, help="first seed; training data used other seeds")
    p.add_argument("--days", type=int, default=28)
    p.add_argument("--greedy", action="store_true", help="always the highest-scoring choice instead of sampling")
    p.add_argument("--random", action="store_true", help="also compare with villagers that choose blindly")
    p.add_argument("--slots", type=int, default=12, help="places per village: the villagers it starts with and the children born to them")
    p.add_argument("--temperature", type=float, default=1.0, help="below 1 the network keeps closer to its highest-scoring choice; 1 samples as trained")
    p.add_argument("--note", default="")
    args = p.parse_args()

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    net, saved = load_model(args.model, device)
    experiment = Experiment(args.name, "evaluation", {**vars(args), "model_from": saved.get("experiment"), "device": str(device)})
    seeds = set(range(args.seed, args.seed + args.villages))
    folder = RUNS / experiment.id
    speed = play(net, device, folder, saved.get("experiment") or args.name, args.villages, args.seed, args.days, args.greedy, {"max_agents": args.slots},
                 args.temperature)

    sides = {"this network": read_runs(folder, seeds), "scripted bots": read_runs(bots("survival", args.seed, args.villages, args.days), seeds)}
    if args.random:
        sides["blind choice"] = read_runs(bots("random", args.seed, args.villages, args.days), seeds)
    tables = {name: measure(runs) for name, runs in sides.items()}
    columns = [title for title, _ in MEASURES] + ["villager-days", "reward per villager-tick"]
    comparison = [{"": name, "villages": len(table), **{c: float(table[c].mean()) for c in columns}} for name, table in tables.items()]
    reach = {name: reached(runs) for name, runs in sides.items()}
    every = sorted({a for r in reach.values() for a in r}, key=lambda a: -max(r.get(a, 0) for r in reach.values()))
    reach_rows = [{"achievement": a, **{name: f"{100 * reach[name].get(a, 0):.0f}%" for name in sides}} for a in every]

    for name, table in tables.items():
        table.to_csv(experiment.folder / (name.replace(" ", "_") + ".csv"), index=False)
    experiment.write_json("comparison.json", {"comparison": comparison, "achievements": reach})
    summary = {f"{name.replace(' ', '_')}_{c.replace(' ', '_')}": row[c] for row in comparison for name in [row[""]] for c in ("days lived", "alive at the end", "achievements")}
    summary.update({"villages": args.villages, "days": args.days, "model_from": saved.get("experiment"), "villager_ticks_per_second": round(speed)})

    how = "taking its highest-scoring choice every time" if args.greedy else "sampling its choices" + (f" (temperature {args.temperature:g})" if args.temperature != 1 else "")
    journal = f"""{args.note or 'A trained network plays whole villages by itself.'}

**Setup.** The network of `{saved.get('experiment')}` played {args.villages} villages (seeds {args.seed} to {args.seed + args.villages - 1}, none of them used in training)
for up to {args.days} days each, {how}, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/{experiment.id}/` with its own report. The network played {speed:,.0f} villager-ticks a second.

**Averages over the villages:**

{markdown_table(comparison, ["", "villages"] + columns)}

**Share of villages that reached each achievement:**

{markdown_table(reach_rows)}"""
    experiment.finish(summary, journal)
    with pd.option_context("display.width", 220, "display.max_columns", 30, "display.float_format", "{:.3g}".format):
        print(pd.DataFrame(comparison).to_string(index=False))
    print("->", experiment.folder)


if __name__ == "__main__":
    main()
