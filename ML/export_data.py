"""Export the 'Sim ...' sheets of the design workbook to JSON for the Unity sim core.

Usage:  python ML/export_data.py
Writes one JSON file per sheet (a list of row objects) plus manifest.json to
Game/TinyTribe/Assets/StreamingAssets/Data/. The workbook is the single source
of truth; never edit the JSON by hand.
"""
import hashlib
import json
import sys
from pathlib import Path

import openpyxl

ROOT = Path(__file__).resolve().parent.parent
WORKBOOK = ROOT / "Plan" / "AISim_English_v12.xlsx"
OUT_DIR = ROOT / "Game" / "TinyTribe" / "Assets" / "StreamingAssets" / "Data"

# Sheets that end with documentation rows: keep only rows whose key column is an int.
INT_KEY_ONLY = {"Sim Action Args": "arg_int"}
# Sheets with a trailing non-data row identified by a key value.
DROP_ROWS = {"Sim Rewards": ("reward_id", "formula")}


def file_name(sheet_title: str) -> str:
    return sheet_title[len("Sim "):].lower().replace(" ", "_") + ".json"


def read_sheet(ws):
    rows = ws.iter_rows(values_only=True)
    header = [str(h) for h in next(rows)]
    out = []
    for row in rows:
        if all(v is None for v in row):
            continue
        out.append(dict(zip(header, row)))
    key = INT_KEY_ONLY.get(ws.title)
    if key:
        out = [r for r in out if isinstance(r[key], int)]
    drop = DROP_ROWS.get(ws.title)
    if drop:
        out = [r for r in out if r[drop[0]] != drop[1]]
    return out


def main() -> int:
    if not WORKBOOK.exists():
        print(f"Workbook not found: {WORKBOOK}", file=sys.stderr)
        return 1
    wb = openpyxl.load_workbook(WORKBOOK, read_only=True, data_only=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    manifest = {
        "workbook": WORKBOOK.name,
        "workbook_sha256": hashlib.sha256(WORKBOOK.read_bytes()).hexdigest(),
        "files": {},
    }
    for ws in wb.worksheets:
        if not ws.title.startswith("Sim "):
            continue
        rows = read_sheet(ws)
        name = file_name(ws.title)
        (OUT_DIR / name).write_text(json.dumps(rows, indent=1, ensure_ascii=False), encoding="utf-8")
        manifest["files"][name] = len(rows)
        print(f"{name:28s} {len(rows):4d} rows")
    (OUT_DIR / "manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    print(f"Wrote {len(manifest['files'])} files to {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
