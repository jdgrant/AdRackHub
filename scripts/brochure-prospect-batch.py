#!/usr/bin/env python3
"""Brochure Prospect Batch — turn new brochure PDFs into AdRackHub prospects.

Named pipeline used for KY (and nearby) brochure scan imports:

  1. prepare  PDF → cropped PNG + OCR (+ draft import JSON)
  2. enrich   (manual / agent) fill Attraction, Address, City, State, Zip, WebURL
  3. import   attach scans and create missing prospects via dotnet CLI

Examples:
  # Today's new PDFs from Google Drive Scans/PDF
  python3 scripts/brochure-prospect-batch.py prepare --since today

  # Specific files
  python3 scripts/brochure-prospect-batch.py prepare --pdf Sc36426082410290.pdf Sc36426082410291.pdf

  # After ScanBatchYYYYMMDD.json is filled in:
  python3 scripts/brochure-prospect-batch.py import --batch 20260825

  # One-liner when JSON is already ready:
  python3 scripts/brochure-prospect-batch.py import --batch Batch-20260825
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from datetime import date, datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "AdRackHub"
DEFAULT_PDF_DIR = Path.home() / "jon@ad-rack.net - Google Drive" / "My Drive" / "Scans" / "PDF"
# Fallback common mount name if home-relative path differs
ALT_PDF_DIRS = [
    Path("/Users/jongrant/jon@ad-rack.net - Google Drive/My Drive/Scans/PDF"),
]


def resolve_pdf_dir(explicit: str | None) -> Path:
    if explicit:
        path = Path(explicit).expanduser()
        if not path.is_dir():
            raise SystemExit(f"PDF directory not found: {path}")
        return path
    for candidate in [DEFAULT_PDF_DIR, *ALT_PDF_DIRS]:
        if candidate.is_dir():
            return candidate
    raise SystemExit(
        "Could not find Google Drive Scans/PDF. Pass --pdf-dir /path/to/Scans/PDF"
    )


def batch_key(value: str) -> str:
    text = value.strip()
    if text.lower().startswith("batch-"):
        text = text[6:]
    return text.replace("-", "")


def batch_paths(key: str) -> tuple[Path, Path]:
    key = batch_key(key)
    scans = APP / "Data" / "Scans" / f"Batch-{key}"
    json_path = APP / "Data" / "Imports" / f"ScanBatch{key}.json"
    return scans, json_path


def parse_since(value: str) -> datetime:
    value = value.strip().lower()
    now = datetime.now()
    if value in {"today", "0d"}:
        return datetime(now.year, now.month, now.day)
    if value in {"yesterday", "1d"}:
        day = date.today() - timedelta(days=1)
        return datetime(day.year, day.month, day.day)
    if value.endswith("h") and value[:-1].isdigit():
        return now - timedelta(hours=int(value[:-1]))
    if value.endswith("d") and value[:-1].isdigit():
        return now - timedelta(days=int(value[:-1]))
    # YYYY-MM-DD or YYYYMMDD
    for fmt in ("%Y-%m-%d", "%Y%m%d"):
        try:
            return datetime.strptime(value, fmt)
        except ValueError:
            pass
    raise SystemExit(f"Unrecognized --since value: {value}")


def select_pdfs(pdf_dir: Path, since: datetime | None, names: list[str]) -> list[Path]:
    if names:
        files = []
        for name in names:
            path = pdf_dir / name
            if not path.exists():
                path = pdf_dir / (name if name.lower().endswith(".pdf") else f"{name}.pdf")
            if not path.exists():
                raise SystemExit(f"PDF not found: {name} in {pdf_dir}")
            files.append(path)
        return sorted(files)

    files = sorted(pdf_dir.glob("*.pdf"))
    if since is None:
        raise SystemExit("Pass --since today|YYYY-MM-DD or --pdf <files…>")
    selected = [p for p in files if datetime.fromtimestamp(p.stat().st_mtime) >= since]
    if not selected:
        raise SystemExit(f"No PDFs in {pdf_dir} newer than {since.isoformat(sep=' ', timespec='minutes')}")
    return selected


def crop_panel(im):
    from PIL import Image  # noqa: F401 — type hint only via runtime

    gray = im.convert("L")
    w, h = gray.size
    px = gray.load()
    step_y, step_x = 3, 3
    dark, col_min, row_min, pad = 240, 6, 6, 12
    col_dark = [0] * w
    row_dark = [0] * h
    for x in range(0, w, step_x):
        d = 0
        for y in range(0, h, step_y):
            if px[x, y] < dark:
                d += 1
                row_dark[y] += 1
        col_dark[x] = d
        for dx in range(1, step_x):
            if x + dx < w:
                col_dark[x + dx] = d
    xs = [i for i, d in enumerate(col_dark) if d >= col_min]
    ys = [i for i, d in enumerate(row_dark) if d >= row_min]
    if not xs or not ys:
        return im
    left = max(0, xs[0] - pad)
    right = min(w, xs[-1] + 1 + pad)
    top = max(0, ys[0] - pad)
    bottom = min(h, ys[-1] + 1 + pad)
    if (right - left) < w * 0.15 or (bottom - top) < h * 0.15:
        return im
    return im.crop((left, top, right, bottom))


def convert_pdfs(pdfs: list[Path], out_dir: Path) -> list[dict]:
    import fitz
    from PIL import Image

    out_dir.mkdir(parents=True, exist_ok=True)
    matrix = fitz.Matrix(150 / 72, 150 / 72)
    manifest = []
    t0 = time.time()
    for i, pdf in enumerate(pdfs, 1):
        dest = out_dir / f"{pdf.stem}.png"
        doc = fitz.open(pdf)
        page = doc[0]
        pix = page.get_pixmap(matrix=matrix, alpha=False)
        im = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
        cropped = crop_panel(im)
        cropped.save(dest, "PNG", optimize=True)
        doc.close()
        rec = {
            "sourcePdf": pdf.name,
            "imageName": dest.name,
            "origSize": list(im.size),
            "croppedSize": list(cropped.size),
            "bytes": dest.stat().st_size,
        }
        manifest.append(rec)
        print(f"  [{i}/{len(pdfs)}] {pdf.name} → {dest.name} ({dest.stat().st_size} bytes)")
    (out_dir / "_manifest.json").write_text(json.dumps(manifest, indent=2))
    print(f"Converted {len(manifest)} PDFs in {time.time() - t0:.1f}s → {out_dir}")
    return manifest


def ocr_batch(out_dir: Path) -> list[dict]:
    try:
        from ocrmac import ocrmac
    except ImportError as exc:
        raise SystemExit(
            "ocrmac is required for OCR. Install with: pip3 install ocrmac"
        ) from exc

    rows = []
    pngs = sorted(out_dir.glob("Sc*.png"))
    if not pngs:
        pngs = sorted(p for p in out_dir.glob("*.png") if not p.name.startswith("_"))
    for i, path in enumerate(pngs, 1):
        text = ocrmac.OCR(str(path)).recognize()
        if isinstance(text, list):
            lines = []
            for item in text:
                if isinstance(item, (list, tuple)) and item:
                    lines.append(str(item[0]))
                else:
                    lines.append(str(item))
            blob = "\n".join(lines)
        else:
            blob = str(text)
        rows.append({"file": path.name, "text": blob})
        preview = " / ".join(blob.splitlines()[:3])[:120]
        print(f"  [{i}/{len(pngs)}] OCR {path.name}: {preview}")
    (out_dir / "_ocr.json").write_text(json.dumps(rows, indent=2, ensure_ascii=False))
    print(f"OCR wrote {out_dir / '_ocr.json'}")
    return rows


def guess_attraction(ocr_text: str) -> str:
    lines = [ln.strip() for ln in ocr_text.splitlines() if ln.strip()]
    skip = {
        "team", "kentucky", "kentucky department of tourism", "welcome to",
        "paid in part", "scan me", "book now", "follow us",
    }
    candidates = []
    for ln in lines[:12]:
        low = ln.lower()
        if low in skip or len(ln) < 3:
            continue
        if re.fullmatch(r"[\d\W]+", ln):
            continue
        candidates.append(ln)
    if not candidates:
        return ""
    # Prefer multi-word title-ish lines
    candidates.sort(key=lambda s: (len(s.split()) < 2, -min(len(s), 40)))
    return candidates[0][:120]


def write_draft_json(ocr_rows: list[dict], json_path: Path, force: bool) -> None:
    if json_path.exists() and not force:
        print(f"Draft JSON already exists (use --force to overwrite): {json_path}")
        return
    draft = []
    for row in ocr_rows:
        draft.append(
            {
                "Attraction": guess_attraction(row.get("text") or ""),
                "ImageName": row["file"],
                "Address": "",
                "City": "",
                "State": "KY",
                "Zip": "",
                "WebURL": "",
                "_ocrPreview": " | ".join((row.get("text") or "").splitlines()[:6])[:240],
            }
        )
    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(draft, indent=2, ensure_ascii=False) + "\n")
    print(f"Wrote draft import JSON ({len(draft)} rows): {json_path}")
    print("Next: enrich Attraction/Address/City/State/Zip/WebURL (remove _ocrPreview), then:")
    key = json_path.stem.replace("ScanBatch", "")
    print(f"  python3 scripts/brochure-prospect-batch.py import --batch {key}")


def cmd_prepare(args: argparse.Namespace) -> int:
    pdf_dir = resolve_pdf_dir(args.pdf_dir)
    since = parse_since(args.since) if args.since else None
    pdfs = select_pdfs(pdf_dir, since, args.pdf or [])
    key = batch_key(args.batch) if args.batch else datetime.now().strftime("%Y%m%d")
    scans_dir, json_path = batch_paths(key)
    print(f"Brochure Prospect Batch prepare → Batch-{key}")
    print(f"  PDF source: {pdf_dir}")
    print(f"  Files: {len(pdfs)}")
    convert_pdfs(pdfs, scans_dir)
    ocr_rows = ocr_batch(scans_dir)
    write_draft_json(ocr_rows, json_path, force=args.force)
    return 0


def cmd_import(args: argparse.Namespace) -> int:
    if not args.batch:
        raise SystemExit("--batch YYYYMMDD is required for import")
    key = batch_key(args.batch)
    scans_dir, json_path = batch_paths(key)
    if not json_path.is_file():
        raise SystemExit(f"Missing {json_path}. Run prepare + enrich first.")
    if not scans_dir.is_dir():
        raise SystemExit(f"Missing scans folder: {scans_dir}")

    # Strip helper keys if still present
    rows = json.loads(json_path.read_text())
    cleaned = []
    for row in rows:
        cleaned.append(
            {
                "Attraction": (row.get("Attraction") or "").strip(),
                "ImageName": (row.get("ImageName") or "").strip(),
                "Address": row.get("Address") or "",
                "City": row.get("City") or "",
                "State": row.get("State") or "KY",
                "Zip": row.get("Zip") or "",
                "WebURL": row.get("WebURL") or "",
            }
        )
    empty = [r["ImageName"] for r in cleaned if not r["Attraction"]]
    if empty:
        raise SystemExit(
            "Import JSON still has blank Attraction for: "
            + ", ".join(empty)
            + f"\nEdit {json_path} then re-run import."
        )
    json_path.write_text(json.dumps(cleaned, indent=2, ensure_ascii=False) + "\n")

    env = os.environ.copy()
    env.setdefault("ASPNETCORE_ENVIRONMENT", "Development")
    cmd = [
        "dotnet",
        "run",
        "--no-build",
        "--",
        f"--import-scan-batch={key}",
    ]
    print(f"Brochure Prospect Batch import → {key}")
    # Build first so --no-build is safe
    build = subprocess.run(
        ["dotnet", "build", "-v", "q"],
        cwd=APP,
        env=env,
    )
    if build.returncode != 0:
        return build.returncode
    proc = subprocess.run(cmd, cwd=APP, env=env)
    return proc.returncode


def cmd_status(args: argparse.Namespace) -> int:
    scans_root = APP / "Data" / "Scans"
    imports = APP / "Data" / "Imports"
    batches = sorted(scans_root.glob("Batch-*"))
    if not batches:
        print("No Batch-* folders under Data/Scans")
        return 0
    print("Brochure Prospect Batch status")
    for folder in batches:
        key = folder.name.replace("Batch-", "")
        json_path = imports / f"ScanBatch{key}.json"
        pngs = list(folder.glob("*.png"))
        pngs = [p for p in pngs if not p.name.startswith("_")]
        ocr = folder / "_ocr.json"
        print(f"  {folder.name}: {len(pngs)} pngs, ocr={'yes' if ocr.exists() else 'no'}, "
              f"json={'yes' if json_path.exists() else 'MISSING'} ({json_path.name})")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Brochure Prospect Batch — PDF scans → prospects + brochure images",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_prep = sub.add_parser("prepare", help="Convert PDFs, OCR, write draft ScanBatch JSON")
    p_prep.add_argument("--batch", help="Batch id YYYYMMDD (default: today)")
    p_prep.add_argument("--since", help="Select PDFs modified since today|yesterday|YYYY-MM-DD|Nh|Nd")
    p_prep.add_argument("--pdf", nargs="+", help="Specific PDF filenames")
    p_prep.add_argument("--pdf-dir", help="Override Google Drive Scans/PDF path")
    p_prep.add_argument("--force", action="store_true", help="Overwrite existing draft JSON")
    p_prep.set_defaults(func=cmd_prepare)

    p_imp = sub.add_parser("import", help="Import ScanBatch JSON + PNGs into AdRackHub")
    p_imp.add_argument("--batch", required=True, help="Batch id YYYYMMDD or Batch-YYYYMMDD")
    p_imp.set_defaults(func=cmd_import)

    p_stat = sub.add_parser("status", help="List existing brochure prospect batches")
    p_stat.set_defaults(func=cmd_status)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
