#!/usr/bin/env python3
"""Deploy the local publish/ folder to production via FTP.

Skips server-specific config and all uploaded customer files so production
brochure scans are never overwritten by local dev uploads.
"""

from __future__ import annotations

import argparse
import os
import sys
import time
from ftplib import FTP
from pathlib import Path

SKIP_FILENAMES = {"appsettings.Production.json"}
SKIP_REMOTE_PREFIXES = (
    "App_Data/Uploads/",
    "Data/Uploads/",
)
ENSURE_REMOTE_DIRS = (
    "App_Data/Uploads/brochures",
    "logs",
)


def should_skip(remote_path: str) -> bool:
    normalized = remote_path.replace("\\", "/")
    name = normalized.rsplit("/", 1)[-1]
    if name in SKIP_FILENAMES:
        return True
    return any(normalized.startswith(prefix) for prefix in SKIP_REMOTE_PREFIXES)


def ensure_remote_dir(ftp: FTP, path: str) -> None:
    if not path:
        return
    current = ""
    for part in path.replace("\\", "/").split("/"):
        current = f"{current}/{part}" if current else part
        try:
            ftp.mkd(current)
        except Exception:
            pass


def upload_tree(ftp: FTP, local_dir: Path, remote_dir: str = "") -> tuple[int, int, list[tuple[str, str]]]:
    uploaded = 0
    skipped = 0
    errors: list[tuple[str, str]] = []

    for name in sorted(os.listdir(local_dir)):
        local_path = local_dir / name
        remote_path = f"{remote_dir}/{name}" if remote_dir else name

        if local_path.is_dir():
            if should_skip(f"{remote_path}/"):
                skipped += 1
                print(f"Skipped directory: {remote_path}/")
                continue
            ensure_remote_dir(ftp, remote_path)
            child_uploaded, child_skipped, child_errors = upload_tree(ftp, local_path, remote_path)
            uploaded += child_uploaded
            skipped += child_skipped
            errors.extend(child_errors)
            continue

        if should_skip(remote_path):
            skipped += 1
            print(f"Skipped: {remote_path}")
            continue

        try:
            with local_path.open("rb") as handle:
                ftp.storbinary(f"STOR {remote_path}", handle)
            uploaded += 1
            if uploaded % 25 == 0:
                print(f"Uploaded {uploaded} files...")
        except Exception as exc:
            errors.append((remote_path, str(exc)))

    return uploaded, skipped, errors


def retry_errors(ftp: FTP, local_root: Path, errors: list[tuple[str, str]], attempts: int, delay_seconds: int) -> list[tuple[str, str]]:
    remaining = [path for path, _ in errors]
    for attempt in range(1, attempts + 1):
        if not remaining:
            break
        print(f"Retry attempt {attempt}, {len(remaining)} files left...")
        still_failed: list[str] = []
        for remote_path in remaining:
            local_path = local_root / remote_path.replace("/", os.sep)
            try:
                with local_path.open("rb") as handle:
                    ftp.storbinary(f"STOR {remote_path}", handle)
                print(f"Retry OK: {remote_path}")
            except Exception:
                still_failed.append(remote_path)
        remaining = still_failed
        if remaining and attempt < attempts:
            time.sleep(delay_seconds)
    return [(path, "locked or upload failed") for path in remaining]


def main() -> int:
    parser = argparse.ArgumentParser(description="Deploy publish/ output to production FTP.")
    parser.add_argument(
        "--local",
        default=str(Path(__file__).resolve().parents[1] / "publish"),
        help="Local publish folder",
    )
    parser.add_argument("--host", default=os.environ.get("ADRACKHUB_FTP_HOST", "win8120.site4now.net"))
    parser.add_argument("--user", default=os.environ.get("ADRACKHUB_FTP_USER", "adrackhub"))
    parser.add_argument("--password", default=os.environ.get("ADRACKHUB_FTP_PASSWORD"))
    parser.add_argument("--retry-attempts", type=int, default=5)
    parser.add_argument("--retry-delay", type=int, default=8)
    args = parser.parse_args()

    if not args.password:
        print("Set ADRACKHUB_FTP_PASSWORD or pass --password.", file=sys.stderr)
        return 1

    local_root = Path(args.local)
    if not local_root.is_dir():
        print(f"Publish folder not found: {local_root}", file=sys.stderr)
        return 1

    ftp = FTP(args.host, timeout=120)
    ftp.login(args.user, args.password)
    ftp.set_pasv(True)
    print("Connected. Remote:", ftp.pwd())

    for remote_dir in ENSURE_REMOTE_DIRS:
        ensure_remote_dir(ftp, remote_dir)

    uploaded, skipped, errors = upload_tree(ftp, local_root)
    print(f"Uploaded {uploaded} files, skipped {skipped}.")

    if errors:
        print(f"Initial errors: {len(errors)}")
        for remote_path, message in errors:
            print(f"  {remote_path}: {message}")
        remaining = retry_errors(ftp, local_root, errors, args.retry_attempts, args.retry_delay)
        if remaining:
            print("Still failed:")
            for remote_path, message in remaining:
                print(f"  {remote_path}: {message}")
            ftp.quit()
            return 2

    ftp.quit()
    print("Deploy complete.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
