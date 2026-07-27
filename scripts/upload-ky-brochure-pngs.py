#!/usr/bin/env python3
"""Upload KY brochure PNG files for specific prospect folders to production FTP.

Requires ADRACKHUB_FTP_PASSWORD. Only uploads .png under the listed customer IDs.
"""
from __future__ import annotations

import os
import sys
from ftplib import FTP
from pathlib import Path

HOST = os.environ.get("ADRACKHUB_FTP_HOST", "win8120.site4now.net")
USER = os.environ.get("ADRACKHUB_FTP_USER", "adrackhub")
PASS = os.environ.get("ADRACKHUB_FTP_PASSWORD")
LOCAL_ROOT = Path(__file__).resolve().parents[1] / "src/AdRackHub/App_Data/Uploads/brochures"
REMOTE_ROOT = "App_Data/Uploads/brochures"

IDS = [
    135, 147, 161, 205, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273,
    274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288,
    289, 290, 291, 292, 293, 294, 295, 296, 297, 299, 300, 301, 302, 303, 304,
    305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319,
]


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


def main() -> int:
    if not PASS:
        print("Set ADRACKHUB_FTP_PASSWORD.", file=sys.stderr)
        return 1

    if not LOCAL_ROOT.is_dir():
        print(f"Local brochures root missing: {LOCAL_ROOT}", file=sys.stderr)
        return 1

    ftp = FTP(HOST, timeout=180)
    ftp.login(USER, PASS)
    ftp.set_pasv(True)
    print("Connected. Remote:", ftp.pwd())

    ensure_remote_dir(ftp, REMOTE_ROOT)

    uploaded = 0
    errors: list[tuple[str, str]] = []

    for cid in IDS:
        local_dir = LOCAL_ROOT / str(cid)
        if not local_dir.is_dir():
            errors.append((str(cid), "local dir missing"))
            continue

        remote_dir = f"{REMOTE_ROOT}/{cid}"
        ensure_remote_dir(ftp, remote_dir)
        pngs = sorted(local_dir.glob("*.png"))
        if not pngs:
            errors.append((str(cid), "no png files"))
            continue

        for png in pngs:
            remote_path = f"{remote_dir}/{png.name}"
            try:
                with open(png, "rb") as handle:
                    ftp.storbinary(f"STOR {remote_path}", handle)
                uploaded += 1
                print(f"OK {remote_path} ({png.stat().st_size} bytes)")
            except Exception as exc:
                errors.append((remote_path, str(exc)))
                print(f"FAIL {remote_path}: {exc}")

    print(f"\nUploaded={uploaded} Errors={len(errors)}")
    for path, err in errors:
        print(f"  ERR {path}: {err}")

    try:
        ftp.quit()
    except Exception:
        pass

    return 0 if not errors else 2


if __name__ == "__main__":
    raise SystemExit(main())
