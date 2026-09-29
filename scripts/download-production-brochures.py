#!/usr/bin/env python3
"""Download production brochure files to local App_Data (and legacy Data) uploads.

Requires ADRACKHUB_FTP_PASSWORD. Skips files that already exist locally with the
same size. Overwrites local files when the production size differs.
"""
from __future__ import annotations

import os
import sys
from ftplib import FTP, error_perm
from pathlib import Path

HOST = os.environ.get("ADRACKHUB_FTP_HOST", "win8120.site4now.net")
USER = os.environ.get("ADRACKHUB_FTP_USER", "adrackhub")
PASS = os.environ.get("ADRACKHUB_FTP_PASSWORD")
REPO = Path(__file__).resolve().parents[1]
ROOTS = (
    ("App_Data/Uploads/brochures", REPO / "src/AdRackHub/App_Data/Uploads/brochures"),
    ("Data/Uploads/brochures", REPO / "src/AdRackHub/Data/Uploads/brochures"),
)


def list_entries(ftp: FTP, remote_dir: str) -> tuple[list[str], list[tuple[str, str, int]]]:
    lines: list[str] = []
    try:
        ftp.retrlines(f"LIST {remote_dir}", lines.append)
    except error_perm:
        return [], []

    dirs: list[str] = []
    files: list[tuple[str, str, int]] = []
    for line in lines:
        parts = line.split()
        if len(parts) < 4:
            continue
        name = parts[-1]
        if name in {".", ".."}:
            continue
        remote_path = f"{remote_dir}/{name}"
        if "<DIR>" in line.upper():
            dirs.append(remote_path)
            continue
        try:
            size = int(parts[-2])
        except ValueError:
            size = -1
        files.append((remote_path, name, size))
    return dirs, files


def download_tree(ftp: FTP, remote_dir: str, local_dir: Path) -> tuple[int, int, int, list[str]]:
    downloaded = 0
    skipped = 0
    overwritten = 0
    errors: list[str] = []

    dirs, files = list_entries(ftp, remote_dir)
    local_dir.mkdir(parents=True, exist_ok=True)
    print(f"Scanning {remote_dir} ({len(dirs)} folders, {len(files)} files)", flush=True)

    for remote_path, name, size in files:
        local_path = local_dir / name
        if local_path.is_file() and local_path.stat().st_size == size:
            skipped += 1
            continue
        existed = local_path.is_file()
        try:
            with local_path.open("wb") as handle:
                ftp.retrbinary(f"RETR {remote_path}", handle.write)
            if existed:
                overwritten += 1
                print(f"Updated {remote_path} ({size} bytes)", flush=True)
            else:
                downloaded += 1
                print(f"Downloaded {remote_path} ({size} bytes)", flush=True)
        except Exception as exc:
            errors.append(f"{remote_path}: {exc}")
            print(f"FAIL {remote_path}: {exc}", flush=True)
            if local_path.exists() and local_path.stat().st_size == 0:
                local_path.unlink()

    for child in dirs:
        child_name = child.rsplit("/", 1)[-1]
        child_downloaded, child_skipped, child_overwritten, child_errors = download_tree(
            ftp, child, local_dir / child_name
        )
        downloaded += child_downloaded
        skipped += child_skipped
        overwritten += child_overwritten
        errors.extend(child_errors)

    return downloaded, skipped, overwritten, errors


def main() -> int:
    if not PASS:
        print("Set ADRACKHUB_FTP_PASSWORD.", file=sys.stderr)
        return 1

    ftp = FTP(HOST, timeout=180)
    ftp.login(USER, PASS)
    ftp.set_pasv(True)
    print("Connected. Remote:", ftp.pwd(), flush=True)

    total_new = total_skip = total_update = 0
    all_errors: list[str] = []

    for remote_root, local_root in ROOTS:
        print(f"\n=== {remote_root} -> {local_root} ===", flush=True)
        downloaded, skipped, overwritten, errors = download_tree(ftp, remote_root, local_root)
        print(
            f"{remote_root}: new={downloaded} updated={overwritten} "
            f"already-matched={skipped} errors={len(errors)}"
        )
        total_new += downloaded
        total_skip += skipped
        total_update += overwritten
        all_errors.extend(errors)

    try:
        ftp.quit()
    except Exception:
        pass

    print(
        f"\nDone. New={total_new} Updated={total_update} "
        f"AlreadyMatched={total_skip} Errors={len(all_errors)}"
    )
    for err in all_errors:
        print(f"  ERR {err}")
    return 0 if not all_errors else 2


if __name__ == "__main__":
    raise SystemExit(main())
