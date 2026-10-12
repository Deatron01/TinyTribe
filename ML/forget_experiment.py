"""Remove an experiment that was only a trial: its folder, its line in the index and its journal entry.

    ML/.venv/Scripts/python ML/forget_experiment.py <experiment id>

For smoke tests and runs started by mistake. Real experiments stay, including the ones that failed:
the journal is the record of what was tried.
"""
import csv
import re
import shutil
import sys

from experiment import EXPERIMENTS, JOURNAL


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    experiment = sys.argv[1]
    folder = EXPERIMENTS / experiment
    if folder.exists():
        shutil.rmtree(folder)
    index = EXPERIMENTS / "index.csv"
    if index.exists():
        with open(index, newline="", encoding="utf-8") as f:
            rows = [r for r in csv.DictReader(f)]
        kept = [r for r in rows if r.get("experiment") != experiment]
        keys = list(dict.fromkeys(k for r in kept for k in r))
        with open(index, "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=keys)
            writer.writeheader()
            writer.writerows(kept)
    if JOURNAL.exists():
        text = JOURNAL.read_text(encoding="utf-8")
        # An entry runs from its "## " heading to the next one; it names its folder.
        parts = re.split(r"(?m)^(?=## )", text)
        kept = [part for part in parts if f"ML/experiments/{experiment}/" not in part]
        JOURNAL.write_text("".join(kept), encoding="utf-8")
    print("forgot", experiment)
    return 0


if __name__ == "__main__":
    sys.exit(main())
