"""Give an experiment that was cut off its place in the records.

    ML/.venv/Scripts/python ML/close_experiment.py <experiment id> --note "what happened"

A run that is stopped before its end has its settings and its metrics.csv but no summary, no line in
the index and no journal entry. This writes them from what the run had logged, marked as cut short,
so the journal still tells everything that was tried.
"""
import argparse
import csv
import json
from datetime import datetime

from experiment import EXPERIMENTS, JOURNAL, markdown_table


def number(text):
    try:
        return float(text)
    except (TypeError, ValueError):
        return text


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("experiment")
    p.add_argument("--note", required=True)
    p.add_argument("--columns", default="", help="comma-separated columns of metrics.csv to show; default: all")
    args = p.parse_args()

    folder = EXPERIMENTS / args.experiment
    if (folder / "summary.json").exists():
        print(args.experiment, "already has a summary; nothing to do")
        return 0
    config = json.loads((folder / "config.json").read_text(encoding="utf-8"))
    with open(folder / "metrics.csv", newline="", encoding="utf-8") as f:
        rows = [{k: number(v) for k, v in r.items()} for r in csv.DictReader(f)]
    columns = [c for c in args.columns.split(",") if c] or list(rows[0].keys())
    last = rows[-1]
    summary = {"experiment": args.experiment, "kind": config.get("kind"), "cut_short": True, "rows_logged": len(rows),
               **{k: v for k, v in last.items() if isinstance(v, float)}}
    (folder / "summary.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")

    index = EXPERIMENTS / "index.csv"
    listed = []
    if index.exists():
        with open(index, newline="", encoding="utf-8") as f:
            listed = list(csv.DictReader(f))
    listed.append(summary)
    keys = list(dict.fromkeys(k for r in listed for k in r))
    with open(index, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=keys)
        writer.writeheader()
        writer.writerows(listed)

    started = datetime.fromisoformat(config["started"])
    name = args.experiment.split("-", 2)[2] if args.experiment.count("-") >= 2 else args.experiment
    entry = (f"\n## {started.strftime('%Y-%m-%d %H:%M')}  {name}  ({config.get('kind')}, cut short)\n\n{args.note.strip()}\n\n"
             f"What it had logged when it stopped:\n\n{markdown_table(rows, columns)}\n\n"
             f"Folder: `ML/experiments/{args.experiment}/`. Run again with: `{config.get('command')}`\n")
    with open(JOURNAL, "a", encoding="utf-8") as f:
        f.write(entry)
    print("closed", args.experiment, "with", len(rows), "rows")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
