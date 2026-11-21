#!/usr/bin/env python3
"""Read Ad-Rack contract files and write a Hotels / Rest Areas spreadsheet.

Source: Data/Contracts (pdf, Word, Outlook mail, photos).
Amounts are monthly. A route marked no, or left blank, is 0.
When a price is written as new/old, the first number is kept.
The Total is the figure on the contract; if that is blank, it is the sum of the route columns.
"""

from __future__ import annotations

import argparse
import calendar
import json
import re
import subprocess
import tempfile
from dataclasses import dataclass, field
from datetime import date, timedelta
from difflib import SequenceMatcher
from pathlib import Path

import fitz
from openpyxl import Workbook, load_workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.utils import get_column_letter

ROOT = Path("/Users/jongrant/Cursor/AdRackHub/Data/Contracts")
DEFAULT_OUT = Path(
    "/Users/jongrant/Cursor/AdRackHub/src/AdRackHub/Data/Imports/ContractInventory.xlsx"
)
CACHE_PATH = Path(
    "/Users/jongrant/Cursor/AdRackHub/src/AdRackHub/Data/Imports/.contract-ocr-cache.json"
)

HOTEL_ROUTES = [
    ("northeast", "Exit - Northeast OH"),
    ("central", "Exit - Central OH"),
    ("northern", "Exit - Northern Interstate"),
    ("cincinnati", "Exit - Cincinnati-NKY"),
    ("louisville", "Exit - Louisville"),
    ("lex", "Exit - Lex-Frankfort"),
    ("i75", "Exit - I-75"),
    ("i65", "Exit - I-65 & 24"),
    ("midtn", "Exit - Mid-TN"),
]
REST_ROUTES = [
    ("ra_i64e", "Rest Area - I-64 East of Lexington"),
    ("ra_i64w", "Rest Area - I-64 West & I-71"),
    ("ra_i75", "Rest Area - I-75"),
    ("ra_i65", "Rest Area - I-65"),
]
ROUTE_IDS = {
    "Exit - Northeast OH": 47,
    "Exit - Central OH": 39,
    "Exit - Northern Interstate": 40,
    "Exit - Cincinnati-NKY": 41,
    "Exit - Louisville": 45,
    "Exit - Lex-Frankfort": 44,
    "Exit - I-75": 43,
    "Exit - I-65 & 24": 42,
    "Exit - Mid-TN": 46,
    "Rest Area - I-64 East of Lexington": 32,
    "Rest Area - I-64 West & I-71": 34,
    "Rest Area - I-75": 33,
    "Rest Area - I-65": 35,
}

MONTHS = {
    "jan": 1, "january": 1,
    "feb": 2, "february": 2,
    "mar": 3, "march": 3,
    "apr": 4, "april": 4,
    "may": 5,
    "jun": 6, "june": 6,
    "jul": 7, "july": 7,
    "aug": 8, "august": 8,
    "sep": 9, "sept": 9, "september": 9,
    "oct": 10, "october": 10,
    "nov": 11, "november": 11,
    "dec": 12, "december": 12,
}

MONEY_RE = re.compile(
    r"\$\s*(\d{1,5}(?:,\d{3})*(?:\.\d{1,2})?)"
    r"(?:\s*/\s*\$?\s*(\d{1,5}(?:,\d{3})*(?:\.\d{1,2})?))?"
)
PAIR_RE = re.compile(
    r"(?<![\d.])(\d{1,4}\.\d{2})\s*/\s*\$?\s*(\d{1,4}\.\d{2})(?![\d.])"
)
BARE_RE = re.compile(r"(?<![\d.#/])(\d{1,4}\.\d{2})(?![\d.])")


@dataclass
class Line:
    text: str
    x: float
    y: float
    x1: float
    y1: float
    words: list = field(default_factory=list)


@dataclass
class Hit:
    kind: str
    client: str
    start: date | None
    end: date | None
    term: str
    total: float | None
    total_from_routes: bool
    signed: str
    routes: dict
    notes: list
    review: list
    source: str
    year: str
    active_client: str = ""
    customer_id: int | None = None
    client_match: str = ""
    record_type: str = ""
    record_status: str = ""


def clean_spaces(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip(" \t-_|")


def strip_blanks(text: str) -> str:
    text = re.sub(r"[_\.]{2,}", " ", text)
    text = re.sub(r"\$\s*[-–—]+\s*", " ", text)
    return clean_spaces(text)


def money_value(raw: str) -> float:
    return float(raw.replace(",", ""))


def route_amount(text: str) -> tuple[float, float | None] | None:
    """The monthly route price is the amount written just before 'per month'."""
    text = re.split(
        r"\b(IMPORTANT|NOTE\s*:|SHIP BROCHURES|DISCOUNT)",
        text,
        maxsplit=1,
        flags=re.I,
    )[0]
    per_month = re.search(r"PER\s+MONTH", text, flags=re.I)
    candidates: list[tuple[int, float, float | None]] = []
    occupied: list[tuple[int, int]] = []
    for match in MONEY_RE.finditer(text):
        current = money_value(match.group(1))
        replaced = money_value(match.group(2)) if match.group(2) else None
        if current >= 10000 and replaced is None:
            continue
        candidates.append((match.start(), current, replaced))
        occupied.append(match.span())
    for match in PAIR_RE.finditer(text):
        if any(not (match.end() <= start or match.start() >= end) for start, end in occupied):
            continue
        candidates.append((match.start(), money_value(match.group(1)), money_value(match.group(2))))
        occupied.append(match.span())
    for match in BARE_RE.finditer(text):
        if any(not (match.end() <= start or match.start() >= end) for start, end in occupied):
            continue
        value = money_value(match.group(1))
        if value < 10000:
            candidates.append((match.start(), value, None))
    if not candidates:
        return None
    if per_month:
        before = [item for item in candidates if item[0] < per_month.start()]
        if before:
            _pos, current, replaced = before[-1]
            return current, replaced
    _pos, current, replaced = candidates[0]
    return current, replaced


def amounts_in(text: str) -> list[tuple[float, float | None]]:
    """Return (current, replaced) pairs. The first number of a slash pair is current."""
    found: list[tuple[float, float | None]] = []
    spans: list[tuple[int, int]] = []
    for match in MONEY_RE.finditer(text):
        current = money_value(match.group(1))
        replaced = money_value(match.group(2)) if match.group(2) else None
        if current >= 10000 and replaced is None:
            continue
        found.append((current, replaced))
        spans.append(match.span())
    for match in PAIR_RE.finditer(text):
        if any(not (match.end() <= a or match.start() >= b) for a, b in spans):
            continue
        found.append((money_value(match.group(1)), money_value(match.group(2))))
        spans.append(match.span())
    if not found:
        for match in BARE_RE.finditer(text):
            value = money_value(match.group(1))
            if value < 10000:
                found.append((value, None))
    return found


def yes_no(text: str) -> bool | None:
    sample = re.sub(r"NO\.\s*KENTUCKY", " ", text, flags=re.I)
    match = re.match(r"^[\s_]*(yes|no|x)\b", sample.strip(), flags=re.I)
    if not match:
        match = re.search(r"(^|[\s_])(yes|no)(?=[\s_]|$)", sample.strip(), flags=re.I)
        token = match.group(2) if match else None
    else:
        token = match.group(1)
    if not token:
        return None
    return token.lower() in {"yes", "x"}


def mentions_interstate(text: str, number: str) -> bool:
    """Match I-65, 1-75, and the same roads when a scan reads the letter I as a 1."""
    return re.search(rf"[IL1]\s*[-–]\s*{number}\b", text, flags=re.I) is not None


def route_key(text: str, kind: str) -> str | None:
    upper = text.upper()
    if kind == "hotel":
        if "NORTHEAST" in upper and "OHIO" in upper:
            return "northeast"
        if "CENTRAL" in upper and "OHIO" in upper:
            return "central"
        if "NORTHERN" in upper and "INTERSTATE" in upper:
            return "northern"
        if "CINCINNATI" in upper and ("ROUTE" in upper or "KENTUCKY" in upper):
            return "cincinnati"
        if "LOUISVILLE" in upper and "ROUTE" in upper:
            return "louisville"
        if "LEXINGTON" in upper and ("FRANKFORT" in upper or "ROUTE" in upper):
            return "lex"
        if "MID-TENN" in upper or "MID TENN" in upper or ("KNOXVILLE" in upper and "ROUTE" in upper):
            return "midtn"
        fraction = re.search(r"1\s*/\s*[24]", upper) is not None
        if fraction and "SOUTHERN" in upper:
            # "1/2 SOUTHERN INTERSTATE ROUTE (KY, TN)" is the I-75 half.
            # "1/2 SOUTHERN RT (I-75 OR I-65)" is the choice line, even if a scan cuts it off.
            named_i75_half = (
                "INTERSTATE" in upper
                and "RT" not in upper
                and " OR " not in upper
                and not mentions_interstate(upper, "75")
                and not mentions_interstate(upper, "65")
                and not mentions_interstate(upper, "24")
            )
            return "i75" if named_i75_half else "half"
        if "SOUTHERN" in upper and ("INTERSTATE" in upper or "ROUTE" in upper):
            return "i75"
        return None
    if mentions_interstate(upper, "64") and "EAST" in upper:
        return "ra_i64e"
    if mentions_interstate(upper, "64") and "WEST" in upper:
        return "ra_i64w"
    if mentions_interstate(upper, "75") and "SOUTHERN" not in upper and "CINCINNATI" not in upper:
        return "ra_i75"
    if mentions_interstate(upper, "65") and "SOUTHERN" not in upper and "ROUTE" not in upper:
        return "ra_i65"
    return None


def classify(text: str) -> str | None:
    upper = text.upper()
    hotel = "NORTHEAST OHIO" in upper or "AREA TO BE COVERED" in upper
    rest = "KENTUCKY REST AREA" in upper or ("I-64" in upper and "EAST" in upper and "REST" in upper)
    if hotel and not rest:
        return "hotel"
    if rest and not hotel:
        return "rest"
    if hotel and rest:
        return "hotel" if upper.count("ROUTE") >= upper.count("REST AREA") else "rest"
    return None


def lines_from_words(words: list[tuple]) -> list[Line]:
    ordered = sorted(words, key=lambda word: (round(word[1], 1), word[0]))
    lines: list[Line] = []
    for x0, y0, x1, y1, text in ordered:
        token = text.strip()
        if not token:
            continue
        placed = False
        if lines:
            last = lines[-1]
            same_band = abs(y0 - last.y) <= 5
            continues = last.x - 12 <= x0 <= last.x1 + 26
            if same_band and continues:
                last.words.append((x0, y0, x1, y1, token))
                last.text = " ".join(word[4] for word in last.words)
                last.x = min(last.x, x0)
                last.y = min(last.y, y0)
                last.x1 = max(last.x1, x1)
                last.y1 = max(last.y1, y1)
                placed = True
        if not placed:
            lines.append(Line(token, x0, y0, x1, y1, [(x0, y0, x1, y1, token)]))
    lines.sort(key=lambda line: (line.y, line.x))
    return lines


def band_text(lines: list[Line], y: float, dy: float = 9) -> str:
    """Join every word in a horizontal band, so typed answers land between the printed labels."""
    words: list[tuple] = []
    for line in lines:
        if abs(line.y - y) > dy:
            continue
        if line.words:
            words.extend(line.words)
        else:
            words.append((line.x, line.y, line.x1, line.y1, line.text))
    words.sort(key=lambda word: word[0])
    return " ".join(word[4] for word in words)


def page_geometry(lines: list[Line]) -> tuple[float, float, float]:
    """Column split, look-back, and look-ahead. Scans are in pixels; PDFs are in points."""
    width = max((line.x1 for line in lines), default=612)
    if width < 750:
        return 270, 10, 58
    scale = width / 620
    return width * 0.46, 10 * scale, 58 * scale


def same_column(header: Line, line: Line, split: float) -> bool:
    return (header.x < split) == (line.x < split)


def column_blob(lines: list[Line], index: int, kind: str, split: float, lookback: float, forward: float) -> str:
    header = lines[index]
    parts = [header.text]
    for line in reversed(lines[:index]):
        if header.y - line.y > lookback:
            break
        if same_column(header, line, split) and yes_no(line.text) is not None and len(line.text.strip()) <= 6:
            parts.insert(0, line.text)
            break
    for line in lines[index + 1 :]:
        if line.y > header.y + forward:
            break
        if not same_column(header, line, split):
            continue
        if route_key(line.text, kind):
            break
        parts.append(line.text)
    return " ".join(parts)


def half_destination(line: Line, lines: list[Line], drawings: list, forward: float) -> str | None:
    nearby = [line.text]
    for other in lines:
        if other is line:
            continue
        if abs(other.y - line.y) > forward * 0.45:
            continue
        if other.x >= line.x - 30:
            nearby.append(other.text)
    upper = " ".join(nearby).upper()
    has_i75 = mentions_interstate(upper, "75") or "I-7S" in upper
    has_i65 = mentions_interstate(upper, "65") or mentions_interstate(upper, "24") or "I-6S" in upper
    if has_i65 and not has_i75:
        return "i65"
    if has_i75 and not has_i65:
        return "i75"
    if not drawings or not line.words:
        return None
    i75_boxes = []
    i65_boxes = []
    for x0, y0, x1, y1, token in line.words:
        token_upper = token.upper()
        box = (x0, y0, x1, y1)
        if "I-75" in token_upper or "I-7S" in token_upper:
            i75_boxes.append(box)
        if "I-65" in token_upper or "I-24" in token_upper or "I-6S" in token_upper:
            i65_boxes.append(box)
    if not i75_boxes and not i65_boxes:
        return None

    def hits(box) -> bool:
        x0, y0, x1, y1 = box
        for drawing in drawings:
            rect = drawing.get("rect")
            if rect is None:
                continue
            if rect.width < 8 or rect.height < 6 or rect.width > 160 or rect.height > 40:
                continue
            if rect.x1 < x0 - 4 or rect.x0 > x1 + 8 or rect.y1 < y0 - 6 or rect.y0 > y1 + 6:
                continue
            return True
        return False

    i65_hit = any(hits(box) for box in i65_boxes)
    i75_hit = any(hits(box) for box in i75_boxes)
    if i65_hit and not i75_hit:
        return "i65"
    if i75_hit and not i65_hit:
        return "i75"
    return None


def client_from_lines(lines: list[Line]) -> str:
    candidates: list[str] = []
    for line in lines:
        upper = line.text.upper()
        band = band_text(lines, line.y)
        if re.search(r"(?:DISPLAY\s+RACK|REST\s+AREA)\s+TO\b", upper) or re.search(
            r"(?:DISPLAY\s+RACK|REST\s+AREA)\s+TO\b", band, flags=re.I
        ):
            match = re.search(r"(?:DISPLAY\s+RACK|REST\s+AREA)\s+TO\s+(.+)$", band, flags=re.I)
            if match:
                candidates.append(match.group(1))
        if re.search(r"\bPROVIDED\s+BY\b", upper) and "BROCHURES" not in upper:
            match = re.search(r"PROVIDED\s+BY\s+(.+)$", band, flags=re.I)
            if match:
                candidates.append(match.group(1))
    cleaned: list[str] = []
    for raw in candidates:
        name = strip_blanks(raw)
        name = re.split(r"\bAD-?RACK\b", name, flags=re.I)[0]
        name = re.split(r"\bAGREES\b", name, flags=re.I)[0]
        name = re.split(r"\bPROVIDED\b", name, flags=re.I)[0]
        name = strip_blanks(name).strip(" .,-")
        name = re.sub(r"^(ONE|1)\s+", "", name, flags=re.I)
        if re.search(r"\b(FOLLOWS|CONTRACT|BROCHURE|ADVERTISING)\b", name, flags=re.I):
            continue
        if len(re.sub(r"[^A-Za-z]", "", name)) >= 3:
            cleaned.append(name)
    if not cleaned:
        return ""
    cleaned.sort(key=len, reverse=True)
    return cleaned[0]


def period_from_lines(lines: list[Line]) -> tuple[str, str]:
    for line in lines:
        if "PERIOD OF THIS CONTRACT" not in line.text.upper():
            continue
        blob = band_text(lines, line.y, dy=8)
        # Beginning sometimes sits a few points lower.
        blob = clean_spaces(blob + " " + band_text(lines, line.y + 10, dy=6))
        match = re.search(
            r"PERIOD OF THIS CONTRACT IS\s+(.+?)\s+BEGINNING\s*(.*)$",
            blob,
            flags=re.I,
        )
        if not match:
            continue
        term = strip_blanks(match.group(1))
        term = re.split(r"\bTHIS CONTRACT\b", term, flags=re.I)[0].strip(" .,-")
        beginning = strip_blanks(match.group(2))
        beginning = re.split(r"\bTHIS CONTRACT\b", beginning, flags=re.I)[0].strip(" .,-")
        return term, beginning
    return "", ""


def total_entries(lines: list[Line]) -> list[tuple[str, float, float | None]]:
    """Amounts written in the 'for the sum of' blank, top to bottom.

    A single figure, or old/new pair, is one entry. A seasonal stack
    (March $105, Apr-Sept $365, Oct-Dec $105) is one entry per row.
    """
    sum_line = next((line for line in lines if "SUM OF" in line.text.upper()), None)
    if sum_line is None:
        return []
    zone = [
        line for line in lines
        if sum_line.y - 36 <= line.y <= sum_line.y + 8 and 95 <= line.x <= 240
    ]
    rows: list[list[Line]] = []
    for line in sorted(zone, key=lambda item: (item.y, item.x)):
        if rows and abs(line.y - rows[-1][0].y) <= 4:
            rows[-1].append(line)
        else:
            rows.append([line])
    entries: list[tuple[str, float, float | None]] = []
    for row in rows:
        text = " ".join(line.text for line in sorted(row, key=lambda item: item.x))
        found = amounts_in(text)
        if not found:
            continue
        label = strip_blanks(re.sub(r"\$?\s*\d[\d,]*(?:\.\d+)?", " ", text))
        if len(found) > 1 and all(replaced is None for _current, replaced in found):
            for current, replaced in found:
                entries.append((label, current, replaced))
        else:
            entries.append((label, found[0][0], found[0][1]))
    if entries:
        return entries
    found = amounts_in(band_text(lines, sum_line.y, dy=10))
    if found:
        return [("", found[0][0], found[0][1])]
    return []


def parse_start(text: str) -> date | None:
    if not text:
        return None
    match = re.search(r"(\d{1,2})[/-](\d{1,2})[/-](\d{2,4})", text)
    if match:
        month, day, year = int(match.group(1)), int(match.group(2)), int(match.group(3))
        if year < 100:
            year += 2000
        if 1 <= month <= 12 and 1 <= day <= 31 and 2000 <= year <= 2035:
            return date(year, month, min(day, calendar.monthrange(year, month)[1]))
    match = re.search(
        r"(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|"
        r"aug(?:ust)?|sep(?:t|tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)"
        r"\.?\s+(\d{1,2})(?:st|nd|rd|th)?\,?\s+(20\d{2})",
        text,
        flags=re.I,
    )
    if match:
        month = MONTHS[match.group(1).lower()[:3] if match.group(1).lower()[:3] in MONTHS else match.group(1).lower()]
        # MONTHS keys include full names; normalize via the dict lookup below.
        key = match.group(1).lower().rstrip(".")
        month = MONTHS.get(key) or MONTHS.get(key[:3])
        year = int(match.group(3))
        day = int(match.group(2))
        if month:
            return date(year, month, min(day, calendar.monthrange(year, month)[1]))
    match = re.search(
        r"(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|"
        r"aug(?:ust)?|sep(?:t|tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)"
        r"[\s,\.]+(20\d{2})",
        text,
        flags=re.I,
    )
    if match:
        key = match.group(1).lower().rstrip(".")
        month = MONTHS.get(key) or MONTHS.get(key[:3])
        if month:
            return date(int(match.group(2)), month, 1)
    return None


def term_months(term: str) -> int | None:
    if not term:
        return None
    if re.search(r"\bone\s+year\b|\b1\s+year\b|\b12\s+months?\b", term, flags=re.I):
        return 12
    match = re.search(r"(\d+)\s+years?\b", term, flags=re.I)
    if match:
        return int(match.group(1)) * 12
    match = re.search(r"(\d+)\s+months?\b", term, flags=re.I)
    if match:
        return int(match.group(1))
    return None


def add_months(start: date, months: int) -> date:
    month_index = start.month - 1 + months
    year = start.year + month_index // 12
    month = month_index % 12 + 1
    day = min(start.day, calendar.monthrange(year, month)[1])
    return date(year, month, day)


def filename_span(name: str) -> tuple[date | None, date | None, str]:
    match = re.search(r"(\d{1,2})\s*[-–]\s*(\d{1,2})\s*['’]?\s*(20\d{2})", name)
    if not match:
        return None, None, ""
    start_month, end_month, year = int(match.group(1)), int(match.group(2)), int(match.group(3))
    if not (1 <= start_month <= 12 and 1 <= end_month <= 12):
        return None, None, ""
    start = date(year, start_month, 1)
    if end_month < start_month:
        end_year = year + 1
    else:
        end_year = year
    last_day = calendar.monthrange(end_year, end_month)[1]
    end = date(end_year, end_month, last_day)
    months = (end_year - year) * 12 + end_month - start_month + 1
    return start, end, f"{months} months"


def name_from_filename(path: Path) -> str:
    stem = path.stem
    stem = re.sub(r"\d{1,2}\s*[-–]\s*\d{1,2}\s*['’]?\s*20\d{2}", " ", stem)
    stem = re.sub(
        r"\b(20\d{2}|signed|unsigned|contract|contracts|corrected copy|copy|dbl|double|pocket|"
        r"hotels?|rest areas?|agreement|for services)\b",
        " ",
        stem,
        flags=re.I,
    )
    stem = re.sub(r"\b(AR|RA)\b", " ", stem)
    stem = re.sub(r"[_\.]+", " ", stem)
    return clean_spaces(stem).strip(" -")


def align_folder_year(day: date | None, folder: str) -> date | None:
    """Scans sometimes read 2022 as 2072. Trust the folder year when the last digit matches."""
    if day is None or not folder.isdigit():
        return day
    folder_year_number = int(folder)
    if 2070 <= day.year <= 2079 and day.year % 10 == folder_year_number % 10:
        return day.replace(year=folder_year_number)
    return day


def folder_year(path: Path) -> str:
    match = re.search(r"(20\d{2})", path.parent.name)
    return match.group(1) if match else ""


def folder_signed(path: Path) -> str:
    parent = path.parent.name.lower()
    if "unsigned" in parent:
        return "Unsigned"
    if "signed" in parent:
        return "Signed"
    return ""


def signed_from(path: Path, page_text: str) -> str:
    if re.search(r"Status:\s*Signed|Agreement completed|e-signed", page_text, flags=re.I):
        return "Signed"
    return folder_signed(path)


ROUTE_LABELS = {key: label for key, label in HOTEL_ROUTES + REST_ROUTES}


def parse_contract(
    lines: list[Line],
    drawings: list,
    source: str,
    path: Path,
    page_text: str,
    document_text: str = "",
) -> Hit | None:
    text = "\n".join(line.text for line in lines)
    kind = classify(text)
    if kind is None:
        return None
    keys = [key for key, _label in (HOTEL_ROUTES if kind == "hotel" else REST_ROUTES)]
    routes: dict[str, float | None] = {key: 0.0 for key in keys}
    notes: list[str] = []
    review: list[str] = []
    split, lookback, forward = page_geometry(lines)

    for index, line in enumerate(lines):
        key = route_key(line.text, kind)
        if key is None:
            continue
        blob = column_blob(lines, index, kind, split, lookback, forward)
        selected = yes_no(blob)
        found = route_amount(blob)
        amount = found[0] if found else None
        replaced = found[1] if found else None
        target = key
        if key == "half":
            target = half_destination(line, lines, drawings, forward)
            if target is None:
                if selected is False or (amount is None and selected is not True):
                    continue
                review.append("Half southern route is marked but I-75 vs I-65/I-24 is not circled")
                if amount is not None:
                    notes.append(f"Half southern amount ${amount:.2f} was not placed on a route")
                continue
            upper_line = line.text.upper()
            if "I-75" in upper_line and ("I-65" in upper_line or "I-24" in upper_line):
                notes.append(
                    "Half southern route placed on "
                    + ("Exit - I-75" if target == "i75" else "Exit - I-65 & 24")
                )
        label = ROUTE_LABELS.get(target, target)
        if target not in routes:
            continue
        if selected is False:
            value = 0.0
        elif amount is not None:
            value = amount
            if replaced is not None:
                notes.append(f"{label} revised from ${replaced:.2f} to ${amount:.2f}")
        elif selected is True:
            value = None
            review.append(f"{label} marked yes with no amount")
        else:
            value = 0.0
        previous = routes.get(target)
        if previous not in (None, 0) and value not in (None, 0) and previous != value:
            notes.append(f"{label} had both ${previous:.2f} and ${value:.2f}; kept the later line")
        if value is None or value != 0 or previous in (None, 0):
            routes[target] = value

    client = client_from_lines(lines) or name_from_filename(path)
    term, beginning = period_from_lines(lines)
    start = parse_start(beginning)
    months = term_months(term)
    end = add_months(start, months) - timedelta(days=1) if start and months else None
    if start is None or end is None:
        file_start, file_end, file_term = filename_span(path.name)
        if start is None and file_start:
            start = file_start
            review.append("Start date taken from the file name")
        if end is None and file_end and (start == file_start or start is None):
            end = file_end
            if not term:
                term = file_term
            review.append("End date taken from the file name")
    numbered = [value for value in routes.values() if isinstance(value, (int, float))]
    route_sum = round(sum(numbered), 2)
    entries = total_entries(lines)
    written = []
    for label, current, replaced in entries:
        written.append(current)
        if replaced is not None:
            written.append(replaced)
    schedule = len(entries) >= 2 and any(re.search(r"[A-Za-z]", label) for label, _c, _r in entries)
    total = None
    replaced_total = None
    total_from_routes = False
    if schedule:
        matched = next((amount for amount in written if abs(amount - route_sum) <= 1), None)
        total = matched if matched is not None else (max(written) if written else None)
        parts = []
        for label, current, _replaced in entries:
            parts.append(f"{label} ${current:.2f}".strip() if label else f"${current:.2f}")
        notes.append("Monthly total by season: " + "; ".join(parts))
    elif len(entries) == 1:
        _label, total, replaced_total = entries[0]
        if replaced_total is not None and route_sum and abs(route_sum - replaced_total) <= 1 and abs(route_sum - total) > 1:
            # The route lines add up to the other figure on the sum line.
            notes.append(f"Sum line shows ${total:.2f} and ${replaced_total:.2f}; used ${route_sum:.2f} to match the routes")
            total = route_sum
            replaced_total = None
        elif replaced_total is not None:
            notes.append(f"Total revised from ${replaced_total:.2f} to ${total:.2f}")
    elif len(written) > 1:
        matched = next((amount for amount in written if abs(amount - route_sum) <= 1), None)
        total = matched if matched is not None else max(written)
        others = sorted({amount for amount in written if abs(amount - total) > 1})
        if others:
            notes.append(
                "Sum line also shows "
                + ", ".join(f"${amount:.2f}" for amount in others)
            )
    if total is None:
        total = route_sum
        total_from_routes = True
        if route_sum:
            notes.append("Total was blank on the contract; used the sum of the route amounts")
    elif abs(total - route_sum) > 1.0:
        review.append(f"Contract total ${total:.2f} does not match route sum ${route_sum:.2f}")

    for line in lines:
        if re.search(r"\bclosed\b", line.text, flags=re.I):
            note = strip_blanks(line.text)
            if note and note not in notes:
                notes.append(note)
            break
    if re.search(r"double\s*pocket|\bdbl\b", path.name, flags=re.I):
        notes.append("File name says double pocket")
    if not any(value not in (0, 0.0, None) for value in routes.values()) and total in (None, 0, 0.0):
        review.append("No amounts found")

    start = align_folder_year(start, folder_year(path))
    end = align_folder_year(end, folder_year(path))
    signed = signed_from(path, document_text or page_text)
    return Hit(
        kind=kind,
        client=client,
        start=start,
        end=end,
        term=term,
        total=total,
        total_from_routes=total_from_routes,
        signed=signed,
        routes=routes,
        notes=notes,
        review=review,
        source=source,
        year=folder_year(path) or (str(start.year) if start else ""),
    )


def words_from_pdf_page(page) -> list[tuple]:
    return [
        (word[0], word[1], word[2], word[3], word[4])
        for word in page.get_text("words")
    ]


def ocr_png(path: Path, cache: dict, cache_key: str) -> list[Line]:
    if cache_key in cache:
        return [Line(item["text"], item["x"], item["y"], item["x1"], item["y1"]) for item in cache[cache_key]]
    import Quartz
    import Vision
    from Foundation import NSURL

    url = NSURL.fileURLWithPath_(str(path))
    request = Vision.VNRecognizeTextRequest.alloc().init()
    request.setRecognitionLevel_(0)
    request.setUsesLanguageCorrection_(True)
    image = Quartz.CIImage.imageWithContentsOfURL_(url)
    width = image.extent().size.width
    height = image.extent().size.height
    handler = Vision.VNImageRequestHandler.alloc().initWithURL_options_(url, None)
    ok, _error = handler.performRequests_error_([request], None)
    lines: list[Line] = []
    if ok and request.results():
        for observation in request.results():
            candidates = observation.topCandidates_(1)
            if not candidates:
                continue
            text = str(candidates[0].string())
            box = observation.boundingBox()
            x = box.origin.x * width
            y = (1 - box.origin.y - box.size.height) * height
            x1 = x + box.size.width * width
            y1 = y + box.size.height * height
            lines.append(Line(text, x, y, x1, y1))
    lines.sort(key=lambda line: (line.y, line.x))
    cache[cache_key] = [
        {"text": line.text, "x": line.x, "y": line.y, "x1": line.x1, "y1": line.y1} for line in lines
    ]
    return lines


def ocr_pdf_page(page, cache: dict, cache_key: str, zoom: float = 1.7) -> list[Line]:
    if cache_key in cache:
        return [Line(item["text"], item["x"], item["y"], item["x1"], item["y1"]) for item in cache[cache_key]]
    with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as handle:
        png = Path(handle.name)
    try:
        pixmap = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), alpha=False)
        pixmap.save(png)
        return ocr_png(png, cache, cache_key)
    finally:
        png.unlink(missing_ok=True)


def hit_has_money(hit: Hit | None) -> bool:
    if hit is None:
        return False
    if hit.total not in (None, 0, 0.0) and not hit.total_from_routes:
        return True
    return any(value not in (None, 0, 0.0) for value in hit.routes.values())


def parse_pdf(path: Path, cache: dict, do_ocr: bool) -> list[Hit]:
    document = fitz.open(path)
    hits: list[Hit] = []
    try:
        document_text = "\n".join(page.get_text() for page in document)
        for index, page in enumerate(document):
            words = words_from_pdf_page(page)
            lines = lines_from_words(words)
            page_text = page.get_text()
            source = path.name if document.page_count == 1 else f"{path.name} p.{index + 1}"
            hit = (
                parse_contract(lines, page.get_drawings(), source, path, page_text, document_text)
                if lines
                else None
            )
            if do_ocr and not hit_has_money(hit):
                key = f"{path}|{path.stat().st_mtime}|{index}"
                ocr_lines = ocr_pdf_page(page, cache, key)
                ocr_text = "\n".join(line.text for line in ocr_lines)
                if classify(ocr_text):
                    ocr_hit = parse_contract(ocr_lines, [], source, path, ocr_text)
                    if hit is None or hit_has_money(ocr_hit) or (ocr_hit and not hit_has_money(hit)):
                        hit = ocr_hit
                        if hit and "Read from a scan" not in hit.notes:
                            hit.notes.append("Read from a scan")
            if hit:
                hits.append(hit)
    finally:
        document.close()
    return hits


def lines_from_plain(text: str) -> list[Line]:
    lines: list[Line] = []
    y = 0.0
    for raw in text.splitlines():
        cleaned = clean_spaces(raw)
        if not cleaned:
            y += 14
            continue
        lines.append(Line(cleaned, 0, y, 400, y + 12))
        y += 14
    return lines


def parse_doc(path: Path) -> list[Hit]:
    result = subprocess.run(
        ["textutil", "-convert", "txt", "-stdout", str(path)],
        check=False,
        capture_output=True,
        text=True,
    )
    text = result.stdout or ""
    lines = lines_from_plain(text)
    hit = parse_contract(lines, [], path.name, path, text)
    return [hit] if hit else []


def parse_msg(path: Path, cache: dict, do_ocr: bool) -> list[Hit]:
    try:
        import extract_msg
    except ImportError:
        return []
    message = extract_msg.Message(str(path))
    try:
        body = message.body or ""
        attachment_hits: list[Hit] = []
        for attachment in message.attachments:
            name = attachment.longFilename or attachment.shortFilename or "attachment"
            data = attachment.data
            if not data:
                continue
            suffix = Path(name).suffix.lower()
            with tempfile.NamedTemporaryFile(suffix=suffix or ".bin", delete=False) as handle:
                handle.write(data)
                temp = Path(handle.name)
            try:
                if data[:5] == b"%PDF-" or suffix == ".pdf":
                    for found in parse_pdf(temp, cache, do_ocr):
                        found.source = f"{path.name} / {name}"
                        found.year = folder_year(path) or found.year
                        found.start = align_folder_year(found.start, found.year)
                        found.end = align_folder_year(found.end, found.year)
                        if not found.signed:
                            found.signed = folder_signed(path)
                        attachment_hits.append(found)
                elif suffix in {".jpg", ".jpeg", ".png"}:
                    for found in parse_image(temp, cache, do_ocr):
                        found.source = f"{path.name} / {name}"
                        found.year = folder_year(path) or found.year
                        found.start = align_folder_year(found.start, found.year)
                        found.end = align_folder_year(found.end, found.year)
                        if not found.signed:
                            found.signed = folder_signed(path)
                        attachment_hits.append(found)
            finally:
                temp.unlink(missing_ok=True)
        if attachment_hits:
            return attachment_hits
        lines = lines_from_plain(body)
        hit = parse_contract(lines, [], path.name, path, body)
        return [hit] if hit else []
    finally:
        message.close()


def parse_image(path: Path, cache: dict, do_ocr: bool) -> list[Hit]:
    if not do_ocr:
        return []
    key = f"{path}|{path.stat().st_mtime}|image"
    lines = ocr_png(path, cache, key)
    text = "\n".join(line.text for line in lines)
    hit = parse_contract(lines, [], path.name, path, text)
    if hit and "Read from a scan" not in hit.notes:
        hit.notes.append("Read from a scan")
    return [hit] if hit else []


def is_pdf(path: Path) -> bool:
    if path.suffix.lower() == ".pdf":
        return True
    try:
        with path.open("rb") as handle:
            return handle.read(5) == b"%PDF-"
    except OSError:
        return False


def excerpt(path: Path) -> str:
    suffix = path.suffix.lower()
    text = ""
    try:
        if is_pdf(path):
            document = fitz.open(path)
            text = "\n".join(page.get_text() for page in document)
            document.close()
            if len(text.strip()) < 40:
                return "Scanned file; no standard hotel or rest-area contract was found."
        elif suffix == ".doc":
            result = subprocess.run(
                ["textutil", "-convert", "txt", "-stdout", str(path)],
                check=False,
                capture_output=True,
                text=True,
            )
            text = result.stdout or ""
        elif suffix == ".msg":
            import extract_msg

            message = extract_msg.Message(str(path))
            names = [
                attachment.longFilename or attachment.shortFilename or "attachment"
                for attachment in message.attachments
            ]
            text = f"Subject: {message.subject or ''}\n{message.body or ''}"
            if names:
                text += " Attachments: " + ", ".join(names)
            message.close()
    except Exception as error:  # noqa: BLE001 — keep going through a messy archive
        return f"Could not read file: {error}"
    text = clean_spaces(text)
    return text[:500]


def collect(only: str | None, do_ocr: bool) -> tuple[list[Hit], list[dict]]:
    cache: dict = {}
    if CACHE_PATH.exists():
        try:
            cache = json.loads(CACHE_PATH.read_text())
        except json.JSONDecodeError:
            cache = {}
    hits: list[Hit] = []
    other: list[dict] = []
    files = sorted(path for path in ROOT.rglob("*") if path.is_file() and path.name != ".DS_Store")
    if only:
        files = [path for path in files if only.lower() in str(path).lower()]
    for number, path in enumerate(files, start=1):
        if number == 1 or number % 25 == 0 or number == len(files):
            print(f"[{number}/{len(files)}] {path.parent.name}/{path.name}", flush=True)
        suffix = path.suffix.lower()
        found: list[Hit] = []
        try:
            if is_pdf(path):
                found = parse_pdf(path, cache, do_ocr)
            elif suffix == ".doc":
                found = parse_doc(path)
            elif suffix == ".msg":
                found = parse_msg(path, cache, do_ocr)
            elif suffix in {".jpg", ".jpeg", ".png"}:
                found = parse_image(path, cache, do_ocr)
        except Exception as error:  # noqa: BLE001
            other.append(
                {
                    "client": name_from_filename(path),
                    "year": folder_year(path),
                    "signed": folder_signed(path),
                    "source": str(path.relative_to(ROOT)),
                    "excerpt": f"Could not read file: {error}",
                }
            )
            continue
        if found:
            for hit in found:
                hit.source = str(path.parent.name + "/" + hit.source)
            hits.extend(found)
        else:
            other.append(
                {
                    "client": name_from_filename(path),
                    "year": folder_year(path),
                    "signed": folder_signed(path),
                    "source": str(path.relative_to(ROOT)),
                    "excerpt": excerpt(path),
                }
            )
        if number % 20 == 0:
            CACHE_PATH.parent.mkdir(parents=True, exist_ok=True)
            CACHE_PATH.write_text(json.dumps(cache))
    CACHE_PATH.parent.mkdir(parents=True, exist_ok=True)
    CACHE_PATH.write_text(json.dumps(cache))
    return hits, other


def write_sheet(workbook: Workbook, title: str, rows: list[Hit], route_columns: list[tuple[str, str]]) -> None:
    sheet = workbook.create_sheet(title)
    headers = [
        "Client Name",
        "Active Client",
        "Customer Id",
        "Match",
        "Record Type",
        "Record Status",
        "Start Date",
        "End Date",
        "Term",
        "Total",
        "Signed",
        *[label for _key, label in route_columns],
        "Year",
        "Source File",
        "Notes",
        "Review",
    ]
    sheet.append(headers)
    header_fill = PatternFill("solid", fgColor="1F4E79")
    for cell in sheet[1]:
        cell.font = Font(bold=True, color="FFFFFF")
        cell.fill = header_fill
        cell.alignment = Alignment(wrap_text=True, vertical="center")
    review_fill = PatternFill("solid", fgColor="FFF2CC")
    for hit in rows:
        values = [
            hit.client,
            hit.active_client or None,
            hit.customer_id,
            hit.client_match or None,
            hit.record_type or None,
            hit.record_status or None,
            hit.start,
            hit.end,
            hit.term,
            hit.total,
            hit.signed,
        ]
        for key, _label in route_columns:
            amount = hit.routes.get(key)
            values.append(amount if amount is not None else None)
        values.extend(
            [
                hit.year,
                hit.source,
                "; ".join(dict.fromkeys(note for note in hit.notes if note)),
                "; ".join(dict.fromkeys(hit.review)),
            ]
        )
        sheet.append(values)
        row = sheet.max_row
        for column in (7, 8):
            sheet.cell(row, column).number_format = "MMM DD, YYYY"
        for column in range(10, 12 + len(route_columns)):
            if column == 11:
                continue
            sheet.cell(row, column).number_format = '"$"#,##0.00'
        if hit.review:
            for cell in sheet[row]:
                cell.fill = review_fill
    sheet.auto_filter.ref = sheet.dimensions
    sheet.freeze_panes = "A2"
    sheet.auto_filter.ref = f"A1:{get_column_letter(len(headers))}{max(sheet.max_row, 1)}"
    sheet.row_dimensions[1].height = 30
    widths = [32, 36, 14, 12, 16, 14, 16, 16, 16, 12, 12] + [16] * len(route_columns) + [8, 46, 42, 36]
    for index, width in enumerate(widths, start=1):
        sheet.column_dimensions[get_column_letter(index)].width = width
    sheet.auto_filter.ref = f"A1:{get_column_letter(len(headers))}{sheet.max_row}"


def write_other(workbook: Workbook, rows: list[dict]) -> None:
    sheet = workbook.create_sheet("Other")
    headers = ["Client Name", "Year", "Signed", "Source File", "What was in the file"]
    sheet.append(headers)
    for cell in sheet[1]:
        cell.font = Font(bold=True, color="FFFFFF")
        cell.fill = PatternFill("solid", fgColor="1F4E79")
    for row in rows:
        sheet.append([row["client"], row["year"], row["signed"], row["source"], row["excerpt"]])
    sheet.freeze_panes = "A2"
    sheet.auto_filter.ref = f"A1:E{sheet.max_row}"
    for index, width in enumerate([32, 10, 12, 55, 80], start=1):
        sheet.column_dimensions[get_column_letter(index)].width = width


def sort_key(hit: Hit):
    return (hit.client.lower(), hit.start or date.min, hit.source.lower())


NAME_STOP = {
    "THE", "AND", "OF", "FOR", "COUNTY", "TOURISM", "TOURIST", "COMMISSION",
    "BUREAU", "CHAMBER", "COMMERCE", "VISITORS", "CONVENTION", "INC", "LLC",
    "CVB", "KY", "OH", "OHIO", "KENTUCKY",
}


def normalize_client(name: str) -> str:
    text = (name or "").upper().replace("&", " AND ")
    text = re.sub(r"[^A-Z0-9]+", " ", text)
    return re.sub(r"\s+", " ", text).strip()


def client_tokens(name: str) -> set[str]:
    return {
        token for token in normalize_client(name).split()
        if len(token) > 2 and token not in NAME_STOP
    }


def names_strong(left: str, right: str) -> bool:
    a, b = normalize_client(left), normalize_client(right)
    if not a or not b:
        return False
    if a == b:
        return True
    return SequenceMatcher(None, a, b).ratio() >= 0.82


def names_match(left: str, right: str) -> bool:
    if names_strong(left, right):
        return True
    a, b = normalize_client(left), normalize_client(right)
    if not a or not b:
        return False
    ratio = SequenceMatcher(None, a, b).ratio()
    tokens_a, tokens_b = client_tokens(left), client_tokens(right)
    return bool(tokens_a and tokens_b and (tokens_a <= tokens_b or tokens_b <= tokens_a) and ratio >= 0.72)


def dates_match(left: date | None, right: date | None) -> bool:
    if left is None or right is None:
        return True
    return left == right or (left.month == right.month and left.day == right.day)


def route_signature(hit: Hit) -> tuple[float, ...]:
    columns = HOTEL_ROUTES if hit.kind == "hotel" else REST_ROUTES
    return tuple(round(float(hit.routes.get(key) or 0), 2) for key, _label in columns)


def route_total(hit: Hit) -> float:
    return round(sum(route_signature(hit)), 2)


def same_contract(left: Hit, right: Hit) -> bool:
    if left.kind != right.kind or left.year != right.year:
        return False
    if abs(float(left.total or 0) - float(right.total or 0)) > 1:
        return False
    if not dates_match(left.start, right.start):
        return False
    if not names_match(left.client, right.client):
        return False
    if route_signature(left) == route_signature(right):
        return True
    # Same client and price, but the signed scan missed a circled route.
    signed = {left.signed, right.signed}
    return names_strong(left.client, right.client) and "Signed" in signed and "Unsigned" in signed


STALE_REVIEW = (
    "does not match route sum",
    "Half southern route is marked",
    "marked yes with no amount",
)


def fill_routes_from_match(kept: Hit, group: list[Hit]) -> None:
    """Keep the signed row, but use route dollars that add up to the contract total."""
    total = float(kept.total or 0)
    if not total or abs(route_total(kept) - total) <= 1:
        return
    donors = [
        hit for hit in group
        if hit is not kept and abs(route_total(hit) - total) <= 1 and route_total(hit) > 0
    ]
    if not donors:
        return
    donor = max(donors, key=lambda hit: (hit.signed == "Signed", route_total(hit)))
    kept.routes = dict(donor.routes)
    kept.review = [item for item in kept.review if not any(phrase in item for phrase in STALE_REVIEW)]
    kept.notes = [note for note in kept.notes if "was not placed on a route" not in note]
    if donor.signed != "Signed":
        kept.notes.append("Route amounts taken from the unsigned copy")


def keep_score(hit: Hit) -> tuple:
    notes = " ".join(hit.notes).lower()
    source = hit.source.lower()
    return (
        hit.signed == "Signed",
        hit.start is not None,
        hit.end is not None,
        not hit.review,
        "scan" not in notes,
        ".msg" not in source,
        ".pdf" in source,
        -len(hit.review),
    )


def dedupe_hits(hits: list[Hit]) -> tuple[list[Hit], int]:
    """Drop unsigned copies when a signed copy of the same contract exists."""
    parent = list(range(len(hits)))

    def find(index: int) -> int:
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    buckets: dict[tuple, list[int]] = {}
    for index, hit in enumerate(hits):
        key = (hit.kind, hit.year, round(float(hit.total or 0), 2))
        buckets.setdefault(key, []).append(index)
    for indexes in buckets.values():
        for left_i, left in enumerate(indexes):
            for right in indexes[left_i + 1 :]:
                if same_contract(hits[left], hits[right]):
                    parent[find(right)] = find(left)
    groups: dict[int, list[Hit]] = {}
    for index, hit in enumerate(hits):
        groups.setdefault(find(index), []).append(hit)
    kept: list[Hit] = []
    removed = 0
    for group in groups.values():
        group.sort(key=keep_score, reverse=True)
        chosen = group[0]
        fill_routes_from_match(chosen, group)
        kept.append(chosen)
        removed += len(group) - 1
    return kept, removed


STATE_WORDS = {"KENTUCKY", "OHIO", "TENNESSEE", "INDIANA", "MICHIGAN", "ILLINOIS", "VIRGINIA"}
CLIENT_STOP = {
    "THE", "AND", "OF", "FOR", "COUNTY", "TOURISM", "TOURIST", "COMMISSION", "BUREAU",
    "CHAMBER", "COMMERCE", "VISITORS", "CONVENTION", "INC", "LLC", "CVB", "ASSOCIATION",
    "CENTER", "HISTORICAL", "SOCIETY", "MUSEUM", "PARK", "AREA", "AREAS", "LAKE", "RIVER",
    "NATIONAL", "STATE", "VISITOR", "GUIDE", "MAGAZINE", "TOUR", "TOURS", "RENTALS",
    "RENTAL", "OFFICIAL", "WELCOME", "DRIVE", "GROUP", "CORP", "CORPORATION", "COMMITTEE",
    "COUNCIL", "COMPANY", "DISTRICT", "USA", "PRODUCTIONS", "PRODUCTION",
}
CLIENT_ABBREV = (
    (r"\bCO\b", "COUNTY"),
    (r"\bTN\b", "TENNESSEE"),
    (r"\bKY\b", "KENTUCKY"),
    (r"\bIN\b", "INDIANA"),
    (r"\bOH\b", "OHIO"),
    (r"\bRR\b", "RAILROAD"),
    (r"\bMT\b", "MOUNT"),
    (r"\bST\b", "SAINT"),
    (r"\bCTR\b", "CENTER"),
    (r"\bTHEATRE\b", "THEATER"),
)


@dataclass
class ActiveClient:
    customer_id: int
    name: str
    expanded: str
    tokens: frozenset[str]
    record_type: str
    status: str


class ClientIndex:
    def __init__(self, clients: list[ActiveClient]):
        self.clients = clients
        owners: dict[str, set[int]] = {}
        for client in clients:
            for token in client.tokens:
                owners.setdefault(token, set()).add(client.customer_id)
        self.owners = owners


def expand_client_name(name: str) -> str:
    text = (name or "").upper().replace("&", " AND ").replace("'", "")
    text = re.sub(
        r"\((KY|OH|OHIO|TN|TENNESSEE|IN|INDIANA|MI|MICHIGAN|IL|ILLINOIS|VA|VIRGINIA)\)",
        r" \1 ",
        text,
    )
    text = re.sub(r"[()]", " ", text)
    text = re.sub(r"[^A-Z0-9]+", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    for pattern, repl in CLIENT_ABBREV:
        text = re.sub(pattern, repl, text)
    text = re.sub(r"\b\d+\b.*", "", text)
    return re.sub(r"\s+", " ", text).strip()


def distinctive_tokens(name: str) -> frozenset[str]:
    expanded = expand_client_name(name)
    return frozenset(
        token for token in expanded.split()
        if len(token) >= 3 and token not in CLIENT_STOP and token not in STATE_WORDS
    )


def name_variants(name: str) -> list[str]:
    text = expand_client_name(name)
    found = [text]
    if " FOR " in f" {text} ":
        found.append(text.split(" FOR ", 1)[1].strip())
    return [item for item in found if item]


def load_active_clients() -> ClientIndex:
    import pymssql

    settings = Path(__file__).resolve().parents[1] / "src" / "AdRackHub" / "appsettings.json"
    connection = json.loads(settings.read_text())["ConnectionStrings"]["DefaultConnection"]
    parts = {}
    for piece in connection.split(";"):
        if "=" in piece:
            key, value = piece.split("=", 1)
            parts[key.strip().lower()] = value
    sql = pymssql.connect(
        server=parts["server"],
        user=parts["user id"],
        password=parts["password"],
        database=parts["database"],
    )
    try:
        cursor = sql.cursor()
        cursor.execute(
            "SELECT Id, CustomerName, Type, Status FROM Customers "
            "WHERE CustomerName IS NOT NULL AND LTRIM(RTRIM(CustomerName)) <> '' "
            "ORDER BY CustomerName"
        )
        clients = [
            ActiveClient(
                row[0],
                row[1],
                expand_client_name(row[1]),
                distinctive_tokens(row[1]),
                row[2] or "",
                row[3] or "",
            )
            for row in cursor.fetchall()
            if row[1]
        ]
    finally:
        sql.close()
    return ClientIndex(clients)


def _fuzzy_same(left: frozenset[str], right: frozenset[str]) -> bool:
    if len(left) != len(right) or not left:
        return False
    return all(max(SequenceMatcher(None, token, other).ratio() for other in right) >= 0.84 for token in left)


def _anchor_matches(name: str, index: ClientIndex) -> list[tuple[float, str, ActiveClient]]:
    """Catch a record whose rare name is buried in a longer contract name."""
    tokens = distinctive_tokens(name)
    if not tokens:
        return []
    expanded = expand_client_name(name)
    contract_states = {word for word in expanded.split() if word in STATE_WORDS}

    def states_conflict(client: ActiveClient) -> bool:
        client_states = {word for word in client.expanded.split() if word in STATE_WORDS}
        return bool(contract_states and client_states and contract_states.isdisjoint(client_states))

    same = [client for client in index.clients if client.tokens == tokens and not states_conflict(client)]
    if contract_states:
        narrowed = [
            client for client in same
            if contract_states <= {word for word in client.expanded.split() if word in STATE_WORDS}
        ]
        if len(narrowed) == 1:
            same = narrowed
        else:
            same = []
    if len(same) == 1:
        ratio = SequenceMatcher(None, expanded, same[0].expanded).ratio() if expanded and same[0].expanded else 0
        return [(max(ratio, 0.9), "Likely", same[0])]
    anchors = []
    for token in tokens:
        if len(token) < 6:
            continue
        owner_ids = index.owners.get(token, ())
        if len(owner_ids) != 1:
            continue
        owner = next(client for client in index.clients if client.customer_id == next(iter(owner_ids)))
        if states_conflict(owner):
            continue
        anchors.append(owner)
    ids = {client.customer_id for client in anchors}
    if len(ids) != 1 or not anchors:
        return []
    owner = anchors[0]
    if len(tokens) > 3 and len(owner.tokens) == 1:
        return []
    return [(0.86, "Likely", owner)]


def _match_rank(item: tuple[float, str, ActiveClient]) -> tuple:
    score, label, client = item
    exact = 0 if label == "Exact" or score >= 0.98 else 1
    type_rank = {"Customer": 0, "Prospect": 1, "ExpandedProspect": 2}.get(client.record_type, 3)
    status_rank = 0 if client.status == "Active" else 1
    return (exact, -score, type_rank, status_rank, client.name)


def match_active_client(name: str, index: ClientIndex):
    hits: list[tuple[float, str, ActiveClient]] = []
    for variant in name_variants(name):
        tokens = distinctive_tokens(variant)
        expanded = expand_client_name(variant)
        for client in index.clients:
            ratio = SequenceMatcher(None, expanded, client.expanded).ratio() if expanded and client.expanded else 0
            contract_states = {word for word in expanded.split() if word in STATE_WORDS}
            client_states = {word for word in client.expanded.split() if word in STATE_WORDS}
            if contract_states and client_states and contract_states.isdisjoint(client_states):
                continue
            if expanded and expanded == client.expanded:
                hits.append((1.0, "Exact", client))
                continue
            if not tokens or not client.tokens:
                continue
            if tokens <= client.tokens:
                if client_states and not contract_states and len(tokens) < 2:
                    continue
                unique = len(tokens) == 1 and len(index.owners.get(next(iter(tokens)), ())) == 1
                if len(tokens) >= 2 or tokens == client.tokens and unique or ratio >= 0.88:
                    hits.append((ratio, "Likely", client))
            elif _fuzzy_same(tokens, client.tokens):
                hits.append((max(ratio, 0.84), "Likely", client))
    if not hits:
        hits.extend(_anchor_matches(name, index))
    if not hits:
        return "", None, "", "", ""
    best_by_id: dict[int, tuple[float, str, ActiveClient]] = {}
    for score, label, client in hits:
        current = best_by_id.get(client.customer_id)
        if current is None or score > current[0]:
            best_by_id[client.customer_id] = (score, label, client)
    ranked = sorted(best_by_id.values(), key=_match_rank)
    best = ranked[0]
    customers = [item for item in ranked if item[2].record_type == "Customer"]
    if customers and best[2].record_type != "Customer" and customers[0][0] >= best[0] - 0.05:
        best = customers[0]
    rivals = [
        item for item in ranked
        if item[2].customer_id != best[2].customer_id
        and item[2].record_type == best[2].record_type
        and item[0] >= best[0] - 0.03
    ]
    if rivals:
        return f"{best[2].name} or {rivals[0][2].name}", None, "Review", "", ""
    label = "Exact" if best[1] == "Exact" or best[0] >= 0.98 else "Likely"
    client = best[2]
    return client.name, client.customer_id, label, client.record_type, client.status


def assign_active_clients(hits: list[Hit], index: ClientIndex) -> None:
    for hit in hits:
        name, customer_id, label, record_type, status = match_active_client(hit.client, index)
        hit.active_client = name
        hit.customer_id = customer_id
        hit.client_match = label
        hit.record_type = record_type
        hit.record_status = status


def _header_fill(cell) -> None:
    cell.font = Font(bold=True, color="FFFFFF")
    cell.fill = PatternFill("solid", fgColor="1F4E79")
    cell.alignment = Alignment(wrap_text=True, vertical="center")


def ensure_record_columns(sheet) -> list:
    headers = [cell.value for cell in sheet[1]]
    if "Record Type" not in headers and "Match" in headers:
        insert_at = headers.index("Match") + 2
        sheet.insert_cols(insert_at, 2)
        sheet.cell(1, insert_at, "Record Type")
        sheet.cell(1, insert_at + 1, "Record Status")
        _header_fill(sheet.cell(1, insert_at))
        _header_fill(sheet.cell(1, insert_at + 1))
        headers = [cell.value for cell in sheet[1]]
    return headers


def write_import(workbook: Workbook) -> int:
    if "Import" in workbook.sheetnames:
        del workbook["Import"]
    sheet = workbook.create_sheet("Import", 0)
    headers = [
        "Line Id",
        "Customer Id",
        "Hub Name",
        "Record Type",
        "Record Status",
        "Match",
        "Contract Name",
        "Product",
        "Route Id",
        "Route",
        "Monthly Amount",
        "Start Date",
        "End Date",
        "Term",
        "Contract Monthly Total",
        "Signed",
        "Year",
        "Source File",
        "Notes",
        "Review",
    ]
    sheet.append(headers)
    for cell in sheet[1]:
        _header_fill(cell)
    line_id = 0
    for title, product in (("Hotels", "Hotel"), ("Rest Areas", "Rest Area")):
        if title not in workbook.sheetnames:
            continue
        source = workbook[title]
        source_headers = [cell.value for cell in source[1]]
        index = {name: position for position, name in enumerate(source_headers)}

        def value(row: int, name: str):
            position = index.get(name)
            return source.cell(row, position + 1).value if position is not None else None

        for row in range(2, source.max_row + 1):
            contract_name = value(row, "Client Name")
            if not contract_name:
                continue
            priced = []
            for header in source_headers:
                if header not in ROUTE_IDS:
                    continue
                amount = value(row, header)
                if amount in (None, "", 0, 0.0):
                    continue
                priced.append((header, amount))
            if not priced:
                priced.append((None, None))
            for route_name, amount in priced:
                line_id += 1
                sheet.append(
                    [
                        line_id,
                        value(row, "Customer Id"),
                        value(row, "Active Client"),
                        value(row, "Record Type"),
                        value(row, "Record Status"),
                        value(row, "Match"),
                        contract_name,
                        product,
                        ROUTE_IDS.get(route_name) if route_name else None,
                        route_name,
                        amount,
                        value(row, "Start Date"),
                        value(row, "End Date"),
                        value(row, "Term"),
                        value(row, "Total"),
                        value(row, "Signed"),
                        value(row, "Year"),
                        value(row, "Source File"),
                        value(row, "Notes"),
                        value(row, "Review"),
                    ]
                )
                current = sheet.max_row
                for column in (12, 13):
                    sheet.cell(current, column).number_format = "MMM DD, YYYY"
                for column in (11, 15):
                    sheet.cell(current, column).number_format = '"$"#,##0.00'
    sheet.freeze_panes = "A2"
    sheet.auto_filter.ref = f"A1:{get_column_letter(len(headers))}{max(sheet.max_row, 1)}"
    sheet.row_dimensions[1].height = 30
    widths = [10, 14, 36, 16, 14, 12, 36, 12, 12, 34, 16, 16, 16, 16, 22, 12, 8, 46, 42, 36]
    for position, width in enumerate(widths, start=1):
        sheet.column_dimensions[get_column_letter(position)].width = width
    return line_id


def link_clients(path: Path) -> None:
    index = load_active_clients()
    workbook = load_workbook(path)
    linked = {"Exact": 0, "Likely": 0, "Review": 0, "": 0}
    types = {"Customer": 0, "Prospect": 0, "ExpandedProspect": 0}
    for title in ("Hotels", "Rest Areas"):
        sheet = workbook[title]
        headers = [cell.value for cell in sheet[1]]
        if "Active Client" not in headers:
            sheet.insert_cols(2, 3)
            sheet.cell(1, 2, "Active Client")
            sheet.cell(1, 3, "Customer Id")
            sheet.cell(1, 4, "Match")
            for column in (2, 3, 4):
                _header_fill(sheet.cell(1, column))
            headers = [cell.value for cell in sheet[1]]
        headers = ensure_record_columns(sheet)
        name_col = headers.index("Client Name") + 1
        client_col = headers.index("Active Client") + 1
        id_col = headers.index("Customer Id") + 1
        match_col = headers.index("Match") + 1
        type_col = headers.index("Record Type") + 1
        status_col = headers.index("Record Status") + 1
        for row in range(2, sheet.max_row + 1):
            client_name = sheet.cell(row, name_col).value
            if not client_name:
                continue
            name, customer_id, label, record_type, status = match_active_client(str(client_name), index)
            sheet.cell(row, client_col).value = name or None
            sheet.cell(row, id_col).value = customer_id
            sheet.cell(row, match_col).value = label or None
            sheet.cell(row, type_col).value = record_type or None
            sheet.cell(row, status_col).value = status or None
            linked[label] += 1
            if customer_id and record_type in types:
                types[record_type] += 1
        sheet.column_dimensions["B"].width = 36
        sheet.column_dimensions["C"].width = 14
        sheet.column_dimensions["D"].width = 12
        sheet.column_dimensions["E"].width = 16
        sheet.column_dimensions["F"].width = 14
        sheet.auto_filter.ref = f"A1:{get_column_letter(sheet.max_column)}{sheet.max_row}"
    lines = write_import(workbook)
    workbook.save(path)
    print(
        f"Records searched: {len(index.clients)}  "
        f"Exact: {linked['Exact']}  Likely: {linked['Likely']}  "
        f"Review: {linked['Review']}  Unmatched: {linked['']}"
    )
    print(
        f"Matched customers: {types['Customer']}  "
        f"Matched prospects: {types['Prospect']}  "
        f"Import rows: {lines}"
    )
    print(f"Updated {path}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="Only files whose path contains this text")
    parser.add_argument("--skip-ocr", action="store_true")
    parser.add_argument("--link-clients", action="store_true", help="Match the existing spreadsheet to active customers")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = parser.parse_args()
    if args.link_clients:
        link_clients(args.out)
        return
    hits, other = collect(args.only, do_ocr=not args.skip_ocr)
    hits, removed = dedupe_hits(hits)
    missing_end = sum(1 for hit in hits if hit.end is None)
    hits = [hit for hit in hits if hit.end is not None]
    try:
        assign_active_clients(hits, load_active_clients())
    except Exception as error:
        print(f"Active clients were not linked: {error}")
    hotels = sorted((hit for hit in hits if hit.kind == "hotel"), key=sort_key)
    rests = sorted((hit for hit in hits if hit.kind == "rest"), key=sort_key)
    workbook = Workbook()
    default = workbook.active
    workbook.remove(default)
    write_sheet(workbook, "Hotels", hotels, HOTEL_ROUTES)
    write_sheet(workbook, "Rest Areas", rests, REST_ROUTES)
    write_other(workbook, other)
    write_import(workbook)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    workbook.save(args.out)
    print(
        f"Hotels: {len(hotels)}  Rest areas: {len(rests)}  Other: {len(other)}  "
        f"Duplicates removed: {removed}  Missing end date removed: {missing_end}"
    )
    print(f"Wrote {args.out}")


if __name__ == "__main__":
    main()
