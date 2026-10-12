"""Every training or evaluation run leaves a folder that explains itself, and a line in the journal.

    ML/experiments/<date>-<name>/
        config.json    everything needed to run it again: the command, its settings, the code and data versions
        metrics.csv    one row per epoch or update
        curves.svg     the same, drawn
        summary.json   the final numbers
        model.pt / model.onnx   the network (not kept in git; made again by running the experiment)
    ML/experiments/index.csv   one line per experiment
    ML/JOURNAL.md              what was tried, why, and what came of it, in order: the story for the report

Nothing here needs a plotting library: curves are written as plain SVG.
"""
import csv
import json
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXPERIMENTS = ROOT / "ML" / "experiments"
JOURNAL = ROOT / "ML" / "JOURNAL.md"
COLORS = ["#2f6fdf", "#e0702a", "#2e9d57", "#b0429c", "#8a8f98", "#c9a227"]


def git_commit() -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True, text=True, timeout=10)
        dirty = subprocess.run(["git", "status", "--porcelain"], cwd=ROOT, capture_output=True, text=True, timeout=10)
        return out.stdout.strip() + (" + uncommitted changes" if dirty.stdout.strip() else "")
    except Exception:
        return "unknown"


class Experiment:
    def __init__(self, name: str, kind: str, config: dict):
        self.name, self.kind = name, kind
        self.started = datetime.now()
        self.id = self.started.strftime("%Y%m%d-%H%M") + "-" + name
        self.folder = EXPERIMENTS / self.id
        copy = 2
        while self.folder.exists():
            self.folder = EXPERIMENTS / f"{self.id}-{copy}"
            copy += 1
        self.id = self.folder.name
        self.folder.mkdir(parents=True)
        self.config = dict(config)
        self.config.update({"experiment": self.id, "kind": kind, "started": self.started.isoformat(timespec="seconds"),
                            "command": subprocess.list2cmdline(sys.argv), "code": git_commit()})
        self.write_json("config.json", self.config)
        self.rows = []
        self.clock = time.time()

    def write_json(self, name: str, value):
        (self.folder / name).write_text(json.dumps(value, indent=1, default=str), encoding="utf-8")

    def log(self, row: dict):
        """One row of metrics.csv. The file is rewritten each time, so it is whole whenever the run stops."""
        self.rows.append(dict(row))
        keys = list(dict.fromkeys(k for r in self.rows for k in r))
        with open(self.folder / "metrics.csv", "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=keys)
            writer.writeheader()
            writer.writerows(self.rows)

    def curves(self, x: str, groups: dict, name: str = "curves.svg"):
        """groups: {panel title: [column, ...]}. One panel per group, side by side."""
        panels = []
        for title, columns in groups.items():
            series = {c: [(r[x], r[c]) for r in self.rows if c in r and r[c] is not None] for c in columns}
            panels.append((title, {k: v for k, v in series.items() if v}))
        (self.folder / name).write_text(svg_panels(panels, x), encoding="utf-8")

    def finish(self, summary: dict, journal: str):
        """Writes summary.json, the index line and the journal entry. journal is Markdown: what was done and what came of it."""
        summary = dict(summary)
        summary.update({"experiment": self.id, "kind": self.kind, "minutes": round((time.time() - self.clock) / 60, 1)})
        self.write_json("summary.json", summary)
        index = EXPERIMENTS / "index.csv"
        rows = []
        if index.exists():
            with open(index, newline="", encoding="utf-8") as f:
                rows = list(csv.DictReader(f))
        rows.append({k: v for k, v in summary.items() if not isinstance(v, (dict, list))})
        keys = list(dict.fromkeys(k for r in rows for k in r))
        with open(index, "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=keys)
            writer.writeheader()
            writer.writerows(rows)
        if not JOURNAL.exists():
            JOURNAL.write_text("# TinyTribe training journal\n\nEvery experiment in the order it was run: what was tried, why, with what, "
                               "and what came of it. Each entry is written by the script that ran the experiment; the numbers and curves "
                               "are in the folder it names. Notes added by hand are marked as such.\n", encoding="utf-8")
        entry = (f"\n## {self.started.strftime('%Y-%m-%d %H:%M')}  {self.name}  ({self.kind})\n\n{journal.strip()}\n\n"
                 f"Folder: `ML/experiments/{self.id}/`. Run again with: `{self.config['command']}`\n")
        with open(JOURNAL, "a", encoding="utf-8") as f:
            f.write(entry)
        return summary


def markdown_table(rows: list, columns: list = None) -> str:
    columns = columns or list(rows[0].keys())
    fmt = lambda v: f"{v:.4g}" if isinstance(v, float) else str(v)
    lines = ["| " + " | ".join(columns) + " |", "|" + "|".join("---" for _ in columns) + "|"]
    lines += ["| " + " | ".join(fmt(r.get(c, "")) for c in columns) + " |" for r in rows]
    return "\n".join(lines)


def svg_panels(panels: list, x_label: str, width: int = 380, height: int = 260) -> str:
    """Line charts side by side. panels: [(title, {series name: [(x, y), ...]})]."""
    pad_l, pad_r, pad_t, pad_b = 52, 14, 30, 40
    total = width * max(1, len(panels))
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{total}" height="{height}" viewBox="0 0 {total} {height}" '
           f'font-family="Segoe UI, Arial, sans-serif" font-size="11">', f'<rect width="{total}" height="{height}" fill="white"/>']
    for p, (title, series) in enumerate(panels):
        ox = p * width
        points = [pt for s in series.values() for pt in s]
        if not points:
            continue
        xs, ys = [float(a) for a, _ in points], [float(b) for _, b in points]
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        if x1 == x0:
            x1 = x0 + 1
        if y1 == y0:
            y1 = y0 + 1
        y0, y1 = y0 - (y1 - y0) * 0.05, y1 + (y1 - y0) * 0.05
        sx = lambda v: ox + pad_l + (float(v) - x0) / (x1 - x0) * (width - pad_l - pad_r)
        sy = lambda v: pad_t + (1 - (float(v) - y0) / (y1 - y0)) * (height - pad_t - pad_b)
        out.append(f'<text x="{ox + pad_l}" y="18" font-size="13" font-weight="600">{title}</text>')
        for i in range(5):
            v = y0 + (y1 - y0) * i / 4
            out.append(f'<line x1="{ox + pad_l}" y1="{sy(v):.1f}" x2="{ox + width - pad_r}" y2="{sy(v):.1f}" stroke="#e3e5e8"/>')
            out.append(f'<text x="{ox + pad_l - 6}" y="{sy(v) + 4:.1f}" text-anchor="end" fill="#555">{v:.3g}</text>')
        for i in range(5):
            v = x0 + (x1 - x0) * i / 4
            out.append(f'<text x="{sx(v):.1f}" y="{height - pad_b + 16}" text-anchor="middle" fill="#555">{v:.4g}</text>')
        out.append(f'<text x="{ox + (width + pad_l - pad_r) / 2:.0f}" y="{height - 6}" text-anchor="middle" fill="#555">{x_label}</text>')
        out.append(f'<rect x="{ox + pad_l}" y="{pad_t}" width="{width - pad_l - pad_r}" height="{height - pad_t - pad_b}" fill="none" stroke="#9aa0a8"/>')
        for k, (name, pts) in enumerate(series.items()):
            color = COLORS[k % len(COLORS)]
            path = " ".join(f"{sx(a):.1f},{sy(b):.1f}" for a, b in pts)
            out.append(f'<polyline points="{path}" fill="none" stroke="{color}" stroke-width="1.8"/>')
            out.append(f'<rect x="{ox + pad_l + 8 + k * 110}" y="{pad_t + 6}" width="10" height="3" fill="{color}"/>')
            out.append(f'<text x="{ox + pad_l + 22 + k * 110}" y="{pad_t + 11}" fill="#333">{name}</text>')
    out.append("</svg>")
    return "\n".join(out)
