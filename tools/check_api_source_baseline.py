"""Partial source drift tripwire, NOT a C# parser or binary/public-package API check.

Only AuthoringInfo.cs and the leading ApiVersion declaration/preamble in Plugins.cs
are covered. Bodies, comments and whitespace (except newline style) are deliberate
conservative inputs. No baseline-update mode: reviewed changes are made separately.
"""
import argparse
import difflib
import json
from pathlib import Path
import re
import sys

INFO = "src/Brutal.Authoring/AuthoringInfo.cs"
VERSION = "src/Brutal.Authoring/Plugins.cs::ApiVersion"
KEYS = {INFO, VERSION}
FORMAT = "partial-source-baseline-v1"


def version_prefix(lines):
    """Current source layout: first type is ApiVersion, ending at column-zero }.

    This intentionally fails/changes on layout drift rather than inferring C#
    semantics. It never scans later types. The using/namespace/attribute preamble
    is included so moving/changing the declaration's context is noticed too.
    """
    kept = []
    found = False
    for line in lines:
        line = line.replace("\r\n", "\n").replace("\r", "\n")
        kept.append(line)
        if not found and re.match(r"^public\s", line):
            if not re.match(r"^public readonly record struct ApiVersion\b", line):
                raise ValueError("Expected ApiVersion as the first public declaration; review extraction scope")
            found = True
        if found and line.rstrip("\n") == "}":
            return "".join(kept)
    raise ValueError("ApiVersion declaration boundary missing; no compatibility result")


def capture(root):
    info = (root / INFO).read_text(encoding="utf-8")
    with (root / "src/Brutal.Authoring/Plugins.cs").open(encoding="utf-8") as stream:
        version = version_prefix(stream)
    return {INFO: info, VERSION: version}


def changes(expected, actual):
    if set(expected) != KEYS or set(actual) != KEYS:
        raise ValueError("Baseline scope must be exactly AuthoringInfo and ApiVersion")
    if not all(isinstance(v, str) for v in expected.values()):
        raise ValueError("Source snapshots must be strings")
    out = []
    for key in sorted(KEYS):
        if expected[key] != actual[key]:
            out.extend(difflib.unified_diff(expected[key].splitlines(), actual[key].splitlines(),
                                          fromfile="baseline/" + key, tofile="current/" + key,
                                          lineterm=""))
    return out


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--baseline", type=Path)
    args = parser.parse_args(argv)
    path = args.baseline or args.root / "docs/api/source_compatibility_baseline.json"
    try:
        baseline = json.loads(path.read_text(encoding="utf-8"))
        if baseline.get("format") != FORMAT or baseline.get("coverage") != "partial-source-only":
            raise ValueError("Not the reviewed partial-source baseline format")
        diff = changes(baseline["sources"], capture(args.root))
    except (OSError, UnicodeError, ValueError, KeyError, TypeError) as error:
        print("ERROR: " + str(error), file=sys.stderr)
        return 2
    if diff:
        print("SOURCE CHANGED: review the diff and compatibility ledger; do not auto-accept.")
        print("\n".join(diff))
        return 1
    print("UNCHANGED: two scoped source snapshots only. Compiled/full API compatibility UNKNOWN.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
