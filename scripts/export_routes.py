#!/usr/bin/env python3
"""Export route sheets from AdRack_Routes_by_Tab.xlsx to CSV for import."""

import re
import sys
from pathlib import Path

import pandas as pd

SRC = Path(
    "/Users/jongrant/Library/CloudStorage/GoogleDrive-jdgrant@dividedeye.com/My Drive/Ad-Rack/AdRack_Routes_by_Tab.xlsx"
)
OUT_DIR = Path(__file__).resolve().parent.parent / "src" / "AdRackHub" / "Data" / "Imports"

ROUTE_SHEETS = {
    "central-oh": "Central OH",
    "northern-interstate": "Northern Interstate",
    "cincinnati-nky": "Cincinnati-NKY",
    "i-65-24": "I-65 & 24",
    "i-75": "I-75",
    "lex-frankfort": "Lex-Frankfort",
    "louisville": "Louisville",
    "mid-tn": "Mid-TN",
    "northeast-oh": "Northeast OH",
}

EXPORT_COLS = [
    "Status", "Step #", "Rack Placement/Note", "Business Name", "Address",
    "City", "State", "ZIP", "Highway/Exit", "Notes",
]


def parse_unparsed_raw_line(raw: str):
    raw = str(raw).strip()
    if not raw or re.match(r"^\d+\s+[\d/'.]+$", raw):
        return None
    if raw.lower().startswith("she'll") or raw.lower().startswith("residence inn"):
        return None

    m = re.search(
        r"\s+(\d+)\s+([A-Z][A-Z0-9 &'\-/]+?)\s{2,}(.+?)\s{2,}([A-Z][A-Z\s\-\.]+),\s*(OH|KY|TN)",
        raw,
    )
    if m:
        step, business, address, city, state = m.groups()
        rack = raw[: m.start()].strip()
        highway = raw[m.end() :].strip()
        return int(step), rack, business.strip(), address.strip(), city.strip(), state, highway

    m = re.search(
        r"^([\d/'\.]+\s+)?(.+?)\s{2,}(.+?)\s{2,}([A-Z][A-Z\s\-\.]+),\s*(OH|KY|TN)(?:\s*\([^)]+\))?\s*(#?.+)?$",
        raw,
    )
    if m:
        rack, business, address, city, state, highway = m.groups()
        return None, (rack or "").strip(), business.strip(), address.strip(), city.strip(), state, (highway or "").strip()

    m = re.search(
        r"^(.+?)\s+([A-Z][A-Z0-9 &'\-/]+?)\s{2,}(\d[^,]+?)\s{2,}([A-Z][A-Z\s\-\.]+),\s*(OH|KY|TN)(?:\s*\([^)]+\))?\s*(#?.+)?$",
        raw,
    )
    if m:
        rack, business, address, city, state, highway = m.groups()
        return None, rack.strip(), business.strip(), address.strip(), city.strip(), state, (highway or "").strip()

    return None


def fix_unparsed_rows(df: pd.DataFrame) -> int:
    fixed = 0
    pending_step = None
    for i, row in df.iterrows():
        raw = str(row.get("Raw Line", "")).strip()
        if re.match(r"^\d+\s+[\d/'.]+$", raw):
            pending_step = int(raw.split()[0])
            continue

        notes = str(row.get("Notes", ""))
        if not notes.startswith("UNPARSED"):
            pending_step = None
            continue

        parsed = parse_unparsed_raw_line(raw)
        if not parsed:
            continue
        step, rack, business, address, city, state, highway = parsed
        if step is None and pending_step is not None:
            step = pending_step
        pending_step = None
        if step is not None:
            df.at[i, "Step #"] = step
        df.at[i, "Rack Placement/Note"] = rack
        df.at[i, "Business Name"] = business
        df.at[i, "Address"] = address
        df.at[i, "City"] = city
        df.at[i, "State"] = state
        df.at[i, "Highway/Exit"] = highway
        df.at[i, "Notes"] = ""
        fixed += 1
    return fixed


def normalize_df(df: pd.DataFrame) -> pd.DataFrame:
    df.columns = [
        "Status", "Step #", "Rack Placement/Note", "Business Name", "Address",
        "City", "State", "ZIP", "Highway/Exit", "Direction", "Notes", "Raw Line", "Source File",
    ]
    fix_unparsed_rows(df)
    for col in df.columns:
        if col == "Step #":
            df[col] = df[col].apply(
                lambda x: "" if pd.isna(x) else (int(x) if isinstance(x, float) and x == int(x) else str(x).strip())
            )
        else:
            df[col] = df[col].fillna("").astype(str).str.strip()
    return df


def export_route(slug: str) -> Path:
    sheet = ROUTE_SHEETS[slug]
    df = normalize_df(pd.read_excel(SRC, sheet_name=sheet))
    out = OUT_DIR / f"{slug}.csv"
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    df[EXPORT_COLS].to_csv(out, index=False)
    active = (df["Status"] == "Active").sum()
    removed = (df["Status"] == "Removed").sum()
    empty = (df["Business Name"] == "").sum()
    print(f"{sheet}: wrote {len(df)} rows ({active} active, {removed} removed, {empty} skipped) -> {out}")
    return out


def main():
    slugs = sys.argv[1:] or list(ROUTE_SHEETS.keys())
    for slug in slugs:
        if slug not in ROUTE_SHEETS:
            raise SystemExit(f"Unknown route slug: {slug}")
        export_route(slug)


if __name__ == "__main__":
    main()
