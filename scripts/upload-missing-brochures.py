#!/usr/bin/env python3
"""Upload local brochure images to production only when the remote file is missing.

Requires ADRACKHUB_FTP_PASSWORD. Never overwrites an existing remote file.
"""
from __future__ import annotations

import argparse
import os
import sys
from ftplib import FTP, error_perm
from pathlib import Path

HOST = os.environ.get("ADRACKHUB_FTP_HOST", "win8120.site4now.net")
USER = os.environ.get("ADRACKHUB_FTP_USER", "adrackhub")
PASS = os.environ.get("ADRACKHUB_FTP_PASSWORD")
LOCAL_ROOT = Path(__file__).resolve().parents[1] / "src/AdRackHub/App_Data/Uploads/brochures"
REMOTE_ROOT = "App_Data/Uploads/brochures"


def ensure_remote_dir(ftp: FTP, path: str) -> None:
    if not path or path in (".", ""):
        return
    parts = path.replace("\\", "/").split("/")
    cur = ""
    for part in parts:
        cur = f"{cur}/{part}" if cur else part
        try:
            ftp.mkd(cur)
        except Exception:
            pass


def remote_names(ftp: FTP, remote_dir: str) -> set[str]:
    try:
        return {Path(name).name for name in ftp.nlst(remote_dir)}
    except error_perm:
        return set()


def main() -> int:
    parser = argparse.ArgumentParser(description="Upload missing brochure files to production FTP.")
    parser.add_argument("--min-id", type=int, default=322)
    parser.add_argument("--max-id", type=int, default=456)
    args = parser.parse_args()

    if not PASS:
        print("Set ADRACKHUB_FTP_PASSWORD.", file=sys.stderr)
        return 1

    if not LOCAL_ROOT.is_dir():
        print(f"Local brochures root missing: {LOCAL_ROOT}", file=sys.stderr)
        return 1

    ids = []
    for d in sorted(LOCAL_ROOT.iterdir(), key=lambda p: int(p.name) if p.name.isdigit() else 0):
        if not d.is_dir() or not d.name.isdigit():
            continue
        cid = int(d.name)
        if args.min_id <= cid <= args.max_id:
            ids.append(cid)

    ftp = FTP(HOST, timeout=180)
    ftp.login(USER, PASS)
    ftp.set_pasv(True)
    print("Connected. Remote:", ftp.pwd())
    ensure_remote_dir(ftp, REMOTE_ROOT)

    uploaded = 0
    skipped_existing = 0
    missing_local = 0
    errors: list[tuple[str, str]] = []

    for cid in ids:
        local_dir = LOCAL_ROOT / str(cid)
        files = sorted(
            p for p in local_dir.iterdir()
            if p.is_file() and p.suffix.lower() in {".png", ".jpg", ".jpeg"}
        )
        if not files:
            missing_local += 1
            continue

        remote_dir = f"{REMOTE_ROOT}/{cid}"
        existing = remote_names(ftp, remote_dir)
        if not existing:
            ensure_remote_dir(ftp, remote_dir)

        for local_file in files:
            remote_path = f"{remote_dir}/{local_file.name}"
            if local_file.name in existing:
                skipped_existing += 1
                continue
            try:
                with open(local_file, "rb") as handle:
                    ftp.storbinary(f"STOR {remote_path}", handle)
                uploaded += 1
                print(f"OK {remote_path} ({local_file.stat().st_size} bytes)")
            except Exception as exc:
                errors.append((remote_path, str(exc)))
                print(f"FAIL {remote_path}: {exc}")

    print(
        f"\nFolders={len(ids)} Uploaded={uploaded} "
        f"SkippedExisting={skipped_existing} EmptyLocal={missing_local} Errors={len(errors)}"
    )
    for path, err in errors:
        print(f"  ERR {path}: {err}")

    try:
        ftp.quit()
    except Exception:
        pass

    return 0 if not errors else 2


if __name__ == "__main__":
    raise SystemExit(main())
