"""Gate for a written deliverable (research, profiles, guides): the document has the required sections, names every
required item, and backs its claims with sources.

    python tools/check_doc.py <doc.md> [--sections "## A,## B"] [--mention a,b,c] [--min-links N]
    exit 0 = complete"""
import argparse
import re
import sys
from pathlib import Path


def problems(text, sections=(), mention=(), min_links=0):
    out = []
    low = text.lower()
    for s in sections:
        if s.lower() not in low:
            out.append(f"missing section '{s}'")
    for m in mention:
        if m.lower() not in low:
            out.append(f"never mentions '{m}'")
    links = len(re.findall(r"https?://\S+", text)) + len(re.findall(r"`[^`]*[\\/][^`]*\.(?:xml|cs|md|json|py|nuspec)`", text))
    if links < min_links:
        out.append(f"{links} source links or file references; needs at least {min_links}")
    return out


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("doc", type=Path)
    ap.add_argument("--sections", default="")
    ap.add_argument("--mention", default="")
    ap.add_argument("--min-links", type=int, default=0)
    a = ap.parse_args()
    split = lambda s: [x.strip() for x in s.split(",") if x.strip()]
    p = problems(a.doc.read_text(encoding="utf-8"), split(a.sections), split(a.mention), a.min_links)
    for line in p:
        print("MISSING", line)
    print("OK" if not p else f"{len(p)} problems")
    sys.exit(1 if p else 0)
