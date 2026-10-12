"""Load recorded TinyTribe runs into tables, and compare them.

Every game the Unity project records goes into its own folder (the workbook's
`Sim Run Log Schema` sheet describes each file). This module reads those folders:

    from runs import load_run, load_index
    run = load_run("Game/TinyTribe/Runs/20261007-102256-s2-survival")
    run.meta["result"]["metrics"]["built"]     # the counters, achievements, setup...
    run.timeline                               # the village every 10 ticks   (DataFrame)
    run.agents                                 # each villager every 10 ticks (DataFrame)
    run.events                                 # every engine event, named    (DataFrame)
    run.actions                                # tick x villager x (action, arg)  (ndarray)

Command line:  ML/.venv/Scripts/python ML/runs.py [runs folder] [--run RUN_ID]
Without --run it prints one line per finished run; with it, that run in detail.

The observation each villager saw is not stored (it would be fifty times the size of
everything else). The simulation is deterministic, so it is rebuilt in Unity by
RunReplay from run.json and actions.bin.
"""
import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_RUNS = ROOT / "Game" / "TinyTribe" / "Runs"
DATA = ROOT / "Game" / "TinyTribe" / "Assets" / "StreamingAssets" / "Data"

EVENT_DTYPE = np.dtype([("tick", "<u4"), ("type", "<u2"), ("agent", "<i2"), ("item", "<i2"), ("qty", "<i4"), ("x", "<i2"), ("y", "<i2")])

# The order of SimEventType in Assets/Sim/Core/Types.cs.
EVENT_TYPES = [
    "InvalidAction", "Harvested", "NodeDepleted", "NodeRegrown", "Consumed", "ToolBroke", "Died", "NodeChanged", "Crafted",
    "BuildingStarted", "BuildingCompleted", "Deposited", "Taken", "CampfireOut", "HomeClaimed", "AgentHurt", "AnimalKilled",
    "VoteOpened", "VoteCast", "VoteClosed", "RoleAssigned", "WeatherChanged", "BuildingDestroyed", "TechResearched",
    "CropPlanted", "TerrainChanged", "SicknessStarted", "SicknessEnded",
]


@dataclass
class Run:
    folder: Path
    meta: dict
    timeline: pd.DataFrame
    agents: pd.DataFrame
    events: pd.DataFrame
    actions: np.ndarray

    @property
    def metrics(self) -> dict:
        return (self.meta.get("result") or {}).get("metrics", {})

    @property
    def achievements(self) -> pd.DataFrame:
        return pd.DataFrame((self.meta.get("result") or {}).get("achievements", []))


def _names(file: str, key: str, value: str) -> dict:
    path = DATA / file
    if not path.exists():
        return {}
    return {row[key]: row[value] for row in json.loads(path.read_text(encoding="utf-8"))}


def _magic(path: Path, magic: bytes) -> bytes:
    raw = path.read_bytes()
    if raw[:8] != magic:
        raise ValueError(f"{path} does not start with {magic!r}")
    return raw[8:]


def load_actions(folder: Path) -> np.ndarray:
    """tick x villager x 2 (action_id, action_arg). A run whose population changed comes back ragged, as a list per tick."""
    raw = np.frombuffer(_magic(folder / "actions.bin", b"TTACT001"), dtype=np.uint8)
    if raw.size == 0:
        return np.zeros((0, 0, 2), dtype=np.uint8)
    n = int(raw[0])
    record = 1 + 2 * n
    if raw.size % record == 0 and np.all(raw[::record] == n):
        return raw.reshape(-1, record)[:, 1:].reshape(-1, n, 2)
    ticks, at = [], 0
    while at < raw.size:
        n = int(raw[at])
        ticks.append(raw[at + 1: at + 1 + 2 * n].reshape(n, 2))
        at += 1 + 2 * n
    return np.array(ticks, dtype=object)


def load_events(folder: Path) -> pd.DataFrame:
    events = pd.DataFrame(np.frombuffer(_magic(folder / "events.bin", b"TTEVT001"), dtype=EVENT_DTYPE))
    events["type"] = events["type"].map(lambda t: EVENT_TYPES[t] if t < len(EVENT_TYPES) else str(t))
    return events


def load_run(folder) -> Run:
    folder = Path(folder)
    meta = json.loads((folder / "run.json").read_text(encoding="utf-8"))
    return Run(folder, meta, pd.read_csv(folder / "timeline.csv"), pd.read_csv(folder / "agents.csv"), load_events(folder), load_actions(folder))


def load_index(runs_folder=DEFAULT_RUNS) -> pd.DataFrame:
    """One row per finished run in the folder."""
    return pd.read_csv(Path(runs_folder) / "index.csv")


def describe(run: Run) -> str:
    meta, result = run.meta, run.meta.get("result")
    setup = meta["setup"]
    lines = [f"{meta['run_id']}: seed {setup['seed']}, {setup['agents']} villagers ({setup['brains']}), data {meta['data']['workbook_sha256'][:8]}"]
    if not result:
        return lines[0] + "\n  cut off before it finished: no result"
    lines.append(f"  {result['ticks']} ticks ({result['days']} days), ended {result['end_reason']}, {result['alive']} alive, "
                 f"{len(result['achievements'])} achievements")
    for a in result["achievements"]:
        lines.append(f"    day {a['day'] + 1:3d}  {a['name']}")
    items = _names("items.json", "item_int", "item_id")
    gathered = run.events[run.events["type"] == "Harvested"].groupby("item")["qty"].sum().sort_values(ascending=False).head(6)
    lines.append("  gathered most: " + ", ".join(f"{items.get(i, i)} {q}" for i, q in gathered.items()))
    actions = _names("actions.json", "action_id", "action")
    if run.actions.ndim == 3 and run.actions.size:
        counts = np.bincount(run.actions[:, :, 0].ravel(), minlength=len(actions) or 1)
        top = np.argsort(counts)[::-1][:5]
        lines.append("  actions chosen: " + ", ".join(f"{actions.get(int(i), int(i))} {100 * counts[i] / counts.sum():.0f}%" for i in top))
    needs = [c for c in ("hp", "hunger", "thirst", "energy", "temp", "morale") if c in run.timeline]
    lines.append("  mean needs over the run: " + ", ".join(f"{c} {run.timeline[c].mean():.0f}" for c in needs))
    lines.append(f"  reward per villager per tick: {run.metrics.get('reward_mean_per_agent_tick', 0):+.4f}")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("runs", nargs="?", default=str(DEFAULT_RUNS))
    parser.add_argument("--run", help="run id (folder name) to show in detail")
    args = parser.parse_args()
    runs = Path(args.runs)
    if args.run:
        print(describe(load_run(runs / args.run)))
        return 0
    if not (runs / "index.csv").exists():
        print(f"No finished runs in {runs}", file=sys.stderr)
        return 1
    index = load_index(runs)
    with pd.option_context("display.width", 200, "display.max_columns", 20):
        print(index.drop(columns=["folder", "started_utc"]).to_string(index=False))
    print(f"\n{len(index)} runs. Mean alive {index['alive'].mean():.2f}, built {index['built'].mean():.1f}, "
          f"techs {index['techs'].mean():.2f}, achievements {index['achievements'].mean():.1f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
