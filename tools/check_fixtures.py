"""Gate for the import fixtures (card A005): every import-matrix case IM-01..IM-11 has an entry in
fixtures/import/expected.json, every entry's file exists (or is made at test time by a named generator), and each entry
states its expected outcome and diagnostics in the agreed shape.

    python tools/check_fixtures.py <root>      root holds fixtures/import/ (an outbox or the repo)   exit 0 = complete"""
import json
import sys
from pathlib import Path

CASES = [f"IM-{n:02d}" for n in range(1, 12)]
OUTCOMES = ("imports", "imports-with-warnings", "refused")
FIELDS = {"id", "file", "outcome", "diagnostics", "checks", "why"}


def problems(root):
    root = Path(root)
    out = []
    exp = root / "fixtures" / "import" / "expected.json"
    if not exp.is_file():
        return [f"no {exp}"]
    try:
        data = json.loads(exp.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        return [f"expected.json is not valid JSON: {e}"]
    if data.get("kind") != "import-fixtures" or data.get("version") != 1:
        out.append('expected.json needs "kind": "import-fixtures", "version": 1')
    entries = data.get("cases", [])
    seen = {}
    for i, e in enumerate(entries):
        where = f"cases[{i}]"
        missing = FIELDS - set(e)
        if missing:
            out.append(f"{where}: missing {sorted(missing)}")
            continue
        seen.setdefault(e["id"], []).append(e)
        if e["outcome"] not in OUTCOMES:
            out.append(f"{where}: outcome must be one of {OUTCOMES}")
        if not isinstance(e["diagnostics"], list) or (e["outcome"] != "imports" and not e["diagnostics"]):
            out.append(f"{where}: diagnostics must be a list, non-empty unless the outcome is 'imports'")
        if not isinstance(e["checks"], dict) or not e["checks"]:
            out.append(f"{where}: checks must name what to measure (counts, bounds, positions ...)")
        f = e["file"]
        if isinstance(f, str):
            if not (root / "fixtures" / "import" / f).is_file():
                out.append(f"{where}: file fixtures/import/{f} doesn't exist")
        elif not (isinstance(f, dict) and f.get("generated_by")):
            out.append(f"{where}: file is a path, or {{\"generated_by\": <script and function>}} for cases made at test time")
    for case in CASES:
        if case not in seen:
            out.append(f"{case}: no case")
    gen = root / "fixtures" / "import" / "make_fixtures.py"
    if not gen.is_file():
        out.append("no fixtures/import/make_fixtures.py (every binary fixture is made by script)")
    return out


if __name__ == "__main__":
    p = problems(sys.argv[1])
    for line in p:
        print("MISSING", line)
    print("OK" if not p else f"{len(p)} problems")
    sys.exit(1 if p else 0)
