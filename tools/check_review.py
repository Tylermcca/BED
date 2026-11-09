"""Gate for a worksheet review card. The review must take a stance on every named decision, answer every row of the named
worksheet sheets, and contain the named sections.

Defaults are card A001's: D1-D10, worksheet_r1 plus the revision-2 community additions, and the Access, Isolation,
Community release and On board sections. Other cards (A003) pass their own.

    python tools/check_review.py <review.md> [<worksheet.csv> ...] [--decisions D11,D12] [--sections "## A,## B"]
    exit 0 = complete"""
import argparse
import csv
import re
import sys
from pathlib import Path

STANCES = ("AGREE", "OBJECT", "AMEND")
DECISIONS = [f"D{d}" for d in range(1, 11)]   # D1-D8 from worksheet_r1, D9 licence and D10 name from the additions
SECTIONS = ("## Access", "## Isolation", "## Community release", "## On board")
PLAN = Path(__file__).resolve().parents[1] / "docs" / "plan"
SHEETS = [PLAN / "worksheet_r1.csv", PLAN / "worksheet_r2_additions.csv"]


def problems(review_text, row_ids, decisions=DECISIONS, sections=SECTIONS):
    out = []
    for d in decisions:
        m = re.search(rf"^\s*[-*|]?\s*\**{re.escape(d)}\b[^\n]*", review_text, re.M)
        if not m:
            out.append(f"{d}: no line for this decision")
        elif not any(s in m.group(0).upper() for s in STANCES):
            out.append(f"{d}: no stance ({'/'.join(STANCES)}) on its line")
    for rid in row_ids:
        m = re.search(rf"^[^\n]*\b{re.escape(rid)}\b[^\n]*", review_text, re.M)
        if not m:
            out.append(f"{rid}: row not answered")
        elif not re.search(r"\b(OK|CORRECT|FIX|MISSING|STALE|UNVERIFIED)\b", m.group(0).upper()):
            out.append(f"{rid}: no verdict (OK/CORRECT/FIX/MISSING/STALE/UNVERIFIED) on its first line")
    for heading in sections:
        if heading.lower() not in review_text.lower():
            out.append(f"missing section '{heading}'")
    return out


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("review", type=Path)
    ap.add_argument("sheets", type=Path, nargs="*")
    ap.add_argument("--decisions", default=",".join(DECISIONS))
    ap.add_argument("--sections", default=",".join(SECTIONS))
    a = ap.parse_args()
    review = a.review.read_text(encoding="utf-8")
    sheets = a.sheets or SHEETS
    decisions = [d.strip() for d in a.decisions.split(",") if d.strip()]
    sections = [s.strip() for s in a.sections.split(",") if s.strip()]
    ids = [r["ID"] for s in sheets for r in csv.DictReader(s.open(encoding="utf-8"))]
    p = problems(review, ids, decisions, sections)
    for line in p:
        print("MISSING", line)
    print("OK" if not p else f"{len(p)} problems", f"({len(ids)} rows, {len(decisions)} decisions)")
    sys.exit(1 if p else 0)
