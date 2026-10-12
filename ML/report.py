"""Build one page out of everything that was trained and measured: ML/REPORT.html.

    ML/.venv/Scripts/python ML/report.py

The page is made from the experiments' own files (config.json, summary.json, metrics.csv, curves.svg,
comparison.json) and the journal, so it is always what was actually run. It has:
  1. the ladder: every network that was evaluated, with the bots and blind choice, on the same maps;
  2. which achievements each of them reached;
  3. every experiment in order, with its settings, curves and result;
It needs no plotting library and no internet: charts are inline SVG, and the file opens anywhere.
"""
import csv
import html
import json
import re
from datetime import datetime

from experiment import COLORS, EXPERIMENTS, JOURNAL, ROOT

OUT = ROOT / "ML" / "REPORT.html"
MEASURES = ["days lived", "alive at the end", "achievements", "buildings", "technologies", "things gathered", "things crafted"]

CSS = """
body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: #f4f5f7; color: #1d2330; line-height: 1.5; }
main { max-width: 1120px; margin: 0 auto; padding: 28px 22px 60px; }
h1 { font-size: 30px; margin: 0 0 4px; } h2 { font-size: 22px; margin: 44px 0 10px; border-bottom: 2px solid #d9dce3; padding-bottom: 6px; }
h3 { font-size: 17px; margin: 30px 0 6px; } p { margin: 8px 0; } .lead { color: #4a5263; font-size: 16px; }
table { border-collapse: collapse; margin: 12px 0; font-size: 14px; background: white; }
th, td { border: 1px solid #d9dce3; padding: 5px 10px; text-align: right; } th { background: #e9ecf2; }
td:first-child, th:first-child { text-align: left; } tr.best td { font-weight: 600; }
code { background: #e9ecf2; padding: 1px 5px; border-radius: 3px; font-size: 13px; }
.card { background: white; border: 1px solid #d9dce3; border-radius: 6px; padding: 14px 18px; margin: 14px 0; overflow-x: auto; }
.kind { display: inline-block; font-size: 12px; background: #2f6fdf; color: white; border-radius: 10px; padding: 1px 9px; margin-left: 8px; vertical-align: middle; }
.cmd { font-family: Consolas, monospace; font-size: 12px; color: #4a5263; word-break: break-all; }
.note { color: #6a7080; font-size: 13px; }
"""


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def experiments() -> list:
    found = []
    for folder in sorted(p for p in EXPERIMENTS.iterdir() if p.is_dir()):
        config = read_json(folder / "config.json")
        if config:
            found.append({"id": folder.name, "folder": folder, "config": config, "summary": read_json(folder / "summary.json"),
                          "comparison": read_json(folder / "comparison.json")})
    return found


def inline(text: str) -> str:
    text = html.escape(text)
    text = re.sub(r"`([^`]+)`", r"<code>\1</code>", text)
    return re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", text)


def markdown(text: str) -> str:
    """The little Markdown the journal uses: paragraphs, bold, code and tables."""
    out, table, para = [], [], []

    def flush():
        nonlocal table, para
        if para:
            out.append("<p>" + inline(" ".join(para)) + "</p>")
            para = []
        if table:
            rows = [[c.strip() for c in line.strip().strip("|").split("|")] for line in table if not re.match(r"^\|[-| :]+\|$", line.strip())]
            body = "".join("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>" for r in rows[1:])
            out.append("<table><tr>" + "".join(f"<th>{inline(c)}</th>" for c in rows[0]) + "</tr>" + body + "</table>")
            table = []

    for line in text.splitlines():
        if line.strip().startswith("|"):
            if para:
                flush()
            table.append(line)
        elif not line.strip():
            flush()
        else:
            if table:
                flush()
            para.append(line.strip())
    flush()
    return "\n".join(out)


def bars(title: str, items: list, unit: str = "", width: int = 520) -> str:
    """Horizontal bars. items: [(label, value, colour)]."""
    row, left, top = 26, 230, 30
    height = top + row * len(items) + 12
    top_value = max([v for _, v, _ in items] + [1e-9])
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" font-family="Segoe UI, Arial, sans-serif" font-size="12">',
           f'<text x="0" y="16" font-size="14" font-weight="600">{html.escape(title)}</text>']
    for i, (label, value, colour) in enumerate(items):
        y = top + i * row
        w = (width - left - 60) * value / top_value
        out.append(f'<text x="{left - 8}" y="{y + 14}" text-anchor="end">{html.escape(label)}</text>')
        out.append(f'<rect x="{left}" y="{y + 2}" width="{max(1, w):.1f}" height="{row - 8}" fill="{colour}" rx="2"/>')
        out.append(f'<text x="{left + max(1, w) + 6:.1f}" y="{y + 14}">{value:.3g}{unit}</text>')
    out.append("</svg>")
    return "\n".join(out)


def ladder(found: list) -> tuple:
    """Every evaluated network, oldest first, then the bots and blind choice from the latest evaluation."""
    rows, reach, reference = [], {}, {}
    for e in found:
        comparison = e["comparison"]
        if not comparison:
            continue
        for row in comparison["comparison"]:
            name = row[""]
            if name == "this network":
                label = e["config"].get("model_from") or e["id"]
                rows.append({"who": label.split("-", 2)[-1], "experiment": e["id"], **row})
                reach[label.split("-", 2)[-1]] = comparison["achievements"].get("this network", {})
            else:
                reference[name] = row
                reach[name] = comparison["achievements"].get(name, {})
    for name in ("blind choice", "scripted bots"):
        if name in reference:
            rows.append({"who": name, "experiment": "", **reference[name]})
    return rows, reach


def main():
    found = experiments()
    journal = JOURNAL.read_text(encoding="utf-8") if JOURNAL.exists() else ""
    rows, reach = ladder(found)
    page = [f"<!doctype html><html lang='en'><head><meta charset='utf-8'><title>TinyTribe: training report</title><style>{CSS}</style></head><body><main>",
            "<h1>TinyTribe: training the villagers</h1>",
            f"<p class='lead'>Every network that was trained and how it did, built from the experiments' own files on "
            f"{datetime.now().strftime('%Y-%m-%d %H:%M')}. {len(found)} experiments.</p>",
            "<p>A villager sees an 11 x 11 patch of the map and a vector about itself and its village, and chooses one of 21 actions each tick. "
            "One network, shared by all villagers, was trained in stages: by imitating recorded play, by being corrected on its own play, and "
            "by learning from what the game pays for outcomes. Each stage was judged the same way: the network runs whole villages for a "
            "year on maps nothing was trained on, and the scripted bots and blind choice play the same maps.</p>"]

    if rows:
        page.append("<h2>1. The ladder</h2><p>Averages over the evaluation villages. A village is played for up to 28 days (one year).</p>")
        best = max((r for r in rows if r["who"] not in ("scripted bots", "blind choice")), key=lambda r: r["days lived"], default=None)
        page.append("<table><tr><th>who plays</th>" + "".join(f"<th>{m}</th>" for m in MEASURES) + "</tr>")
        for r in rows:
            cells = "".join(f"<td>{r.get(m, 0):.3g}</td>" for m in MEASURES)
            page.append(f"<tr{' class=best' if r is best else ''}><td>{html.escape(r['who'])}</td>{cells}</tr>")
        page.append("</table>")
        colour = lambda who: "#8a8f98" if who == "blind choice" else "#2e9d57" if who == "scripted bots" else "#2f6fdf"
        page.append("<div class='card'>" + bars("Days a village lasted (of 28)", [(r["who"], r["days lived"], colour(r["who"])) for r in rows]) + "</div>")
        page.append("<div class='card'>" + bars("Achievements reached per village (of 37)", [(r["who"], r["achievements"], colour(r["who"])) for r in rows]) + "</div>")

        names = [r["who"] for r in rows]
        every = sorted({a for who in names for a in reach.get(who, {})}, key=lambda a: -max(reach.get(who, {}).get(a, 0) for who in names))
        page.append("<h2>2. What they achieved</h2><p>Share of villages that reached each achievement (the list is the workbook's <code>Sim Achievements</code>).</p>")
        page.append("<div class='card'><table><tr><th>achievement</th>" + "".join(f"<th>{html.escape(n)}</th>" for n in names) + "</tr>")
        for a in every:
            page.append("<tr><td>" + html.escape(a) + "</td>" + "".join(f"<td>{100 * reach.get(n, {}).get(a, 0):.0f}%</td>" for n in names) + "</tr>")
        page.append("</table></div>")

    page.append("<h2>3. Everything that was tried, in order</h2><p>The journal: each experiment as the script that ran it wrote it down, "
                "and between them the notes on what was understood and why the next one was set up as it was.</p>")
    by_id = {e["id"]: e for e in found}
    told = set()
    for part in re.split(r"(?m)^(?=## )", journal):
        if not part.startswith("## "):
            continue
        heading, _, body = part.partition("\n")
        match = re.search(r"ML/experiments/([^/`]+)/", part)
        e = by_id.get(match.group(1)) if match else None
        if e is None:
            title = heading[3:].split("note:", 1)[-1].strip() if "note:" in heading else heading[3:].strip()
            page.append(f"<h3>{html.escape(title)}<span class='kind' style='background:#b0429c'>note</span></h3>")
            page.append("<div class='card'>" + markdown(body) + "</div>")
            continue
        told.add(e["id"])
        kind = str(e["config"].get("kind", "")) + (", cut short" if (e["summary"] or {}).get("cut_short") else "")
        page.append(f"<h3>{html.escape(e['id'])}<span class='kind'>{html.escape(kind)}</span></h3>")
        text = "\n".join(line for line in body.splitlines() if not line.startswith("Folder: "))
        page.append("<div class='card'>" + markdown(text) + "</div>")
        curves = e["folder"] / "curves.svg"
        if curves.exists():
            page.append("<div class='card'>" + curves.read_text(encoding="utf-8") + "</div>")
        page.append(f"<p class='cmd'>{html.escape(str(e['config'].get('command', '')))}</p>")
    for e in found:
        if e["id"] in told:
            continue
        page.append(f"<h3>{html.escape(e['id'])}<span class='kind'>{html.escape(str(e['config'].get('kind', '')))}</span></h3>")
        page.append("<p class='note'>Still running, or stopped before it wrote its result.</p>")
        curves = e["folder"] / "curves.svg"
        if curves.exists():
            page.append("<div class='card'>" + curves.read_text(encoding="utf-8") + "</div>")

    page.append("<h2>4. How to run it again</h2><p>See <code>ML/README.md</code>. Each experiment's folder under <code>ML/experiments/</code> has its "
                "exact command and settings in <code>config.json</code>; the villages it played are recorded under <code>ML/data/runs/</code> and can be "
                "replayed tick for tick.</p></main></body></html>")
    OUT.write_text("\n".join(page), encoding="utf-8")
    print(f"wrote {OUT} ({OUT.stat().st_size // 1024} KB), {len(found)} experiments, {len(rows)} rows in the ladder")


if __name__ == "__main__":
    main()
