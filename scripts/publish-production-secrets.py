#!/usr/bin/env python3
"""Put Mailgun SMTP keys on production without adding them to git.

Reads the key from (in order):
  MAILGUN_API_KEY / ADRACKHUB_MAILGUN_API_KEY
  gitignored src/AdRackHub/appsettings.Development.local.json
  gitignored src/AdRackHub/appsettings.Production.json

Uploads only appsettings.Production.json via FTP. Never prints the key.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
from ftplib import FTP
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
APP_DIR = REPO_ROOT / "src" / "AdRackHub"
REMOTE_NAME = "appsettings.Production.json"


def mailgun_key(settings: object) -> str:
    if not isinstance(settings, dict):
        return ""
    mailgun = settings.get("Mailgun")
    if not isinstance(mailgun, dict):
        return ""
    key = mailgun.get("ApiKey")
    return key.strip() if isinstance(key, str) else ""


def load_json(path: Path) -> dict:
    if not path.is_file():
        return {}
    data = json.loads(path.read_text())
    return data if isinstance(data, dict) else {}


def local_key() -> str:
    for env_name in ("MAILGUN_API_KEY", "ADRACKHUB_MAILGUN_API_KEY"):
        value = (os.environ.get(env_name) or "").strip()
        if value:
            return value
    for name in ("appsettings.Development.local.json", "appsettings.Production.json"):
        key = mailgun_key(load_json(APP_DIR / name))
        if key:
            return key
    return ""


def download_json(ftp: FTP, name: str) -> dict:
    buf = io.BytesIO()
    try:
        ftp.retrbinary(f"RETR {name}", buf.write)
    except Exception:
        return {}
    raw = buf.getvalue().decode("utf-8-sig").strip()
    if not raw:
        return {}
    data = json.loads(raw)
    return data if isinstance(data, dict) else {}


def main() -> int:
    parser = argparse.ArgumentParser(description="Publish Mailgun keys to production, not git.")
    parser.add_argument("--host", default=os.environ.get("ADRACKHUB_FTP_HOST", "win8120.site4now.net"))
    parser.add_argument("--user", default=os.environ.get("ADRACKHUB_FTP_USER", "adrackhub"))
    parser.add_argument("--password", default=os.environ.get("ADRACKHUB_FTP_PASSWORD"))
    args = parser.parse_args()

    if not args.password:
        print("Set ADRACKHUB_FTP_PASSWORD or pass --password.", file=sys.stderr)
        return 1

    key = local_key()
    if not key:
        print("No Mailgun API key found in env or gitignored local settings.", file=sys.stderr)
        return 1

    ftp = FTP(args.host, timeout=60)
    ftp.login(args.user, args.password)
    ftp.set_pasv(True)

    remote = download_json(ftp, REMOTE_NAME)
    mailgun = remote.get("Mailgun") if isinstance(remote.get("Mailgun"), dict) else {}
    already = mailgun_key(remote)
    mailgun["ApiKey"] = key
    remote["Mailgun"] = mailgun

    payload = (json.dumps(remote, indent=2) + "\n").encode("utf-8")
    ftp.storbinary(f"STOR {REMOTE_NAME}", io.BytesIO(payload))
    ftp.quit()

    action = "updated" if already else "created"
    print(f"Production {REMOTE_NAME} {action}; Mailgun API key present. Not added to git.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
