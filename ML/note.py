"""Add a note to the journal: what was understood between experiments, in plain words.

    ML/.venv/Scripts/python ML/note.py "title" "text"

The scripts write down what each experiment did and measured. Why the next one was set up the way it
was is often a finding of its own: a cause tracked down, a mistake in the method. Those go here, in
the same journal and in order, and show up in the report between the experiments they sit between.
"""
import sys
from datetime import datetime

from experiment import JOURNAL


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    title, text = sys.argv[1], sys.argv[2]
    entry = f"\n## {datetime.now().strftime('%Y-%m-%d %H:%M')}  note: {title}\n\n{text.strip()}\n"
    with open(JOURNAL, "a", encoding="utf-8") as f:
        f.write(entry)
    print("noted:", title)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
