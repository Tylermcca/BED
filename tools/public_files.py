"""Read-only public-source proposal; no copying, editing, publishing or history access.

INVENTED conservative classifier from the release-boundary requirements.
Private identifiers come from an excluded input, never from public rules.
Reports contain paths, categories and line numbers, never matching private text.
"""
from __future__ import annotations
import argparse
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import sys

# Complete reviewable policy. First file rule wins; no catch-all approval.
POLICY = {
    "pruned_directories": {
        ".git": "version-control metadata; owner reviews history separately",
        "codex": "private inbox, outbox and scratch",
        "bin": "generated build output", "obj": "generated restore/build output",
        "artifacts": "generated build output", "dist": "distribution reviewed separately",
        "testresults": "generated test output", "__pycache__": "Python cache",
        ".pytest_cache": "test cache", ".vs": "local IDE state", ".idea": "local IDE state",
        ".venv": "local environment", "venv": "local environment",
        "node_modules": "installed dependencies", ".gate": "private proof scratch",
    },
    "file_rules": [
        ("AGENTS.md", "private", "internal workflow; publish the contributor guide instead"),
        ("CLAUDE.md", "private", "internal workflow; publish the contributor guide instead"),
        ("handoff/**", "private", "internal assignments, reviews and coordination"),
        ("docs/plan/**", "private", "internal inventories and integration decisions"),
        ("docs/research/release_readiness.md", "private", "internal release audit and historical proof locations"),
        ("docs/release/public_file_set.md", "private", "internal census names withheld files; do not ship it"),
        ("docs/release/readiness_again.md", "private", "internal release audit; do not ship it"),
        ("tools/build_worksheet_r2.py", "private", "internal planning-sheet generator"),
        ("tools/test_build_worksheet_r2.py", "private", "tests the internal planning-sheet generator"),
        ("tools/check_handoff_move.py", "private", "internal handoff migration utility"),
        ("tools/test_check_handoff_move.py", "private", "tests the internal handoff migration utility"),
        ("docs/guide/api_overview.md", "needs-change", "replace the stale internal delivery reference with the existing public tutorial"),
        ("README.md", "needs-change", "rewrite the entry point without private project/process assumptions"),
        ("docs/goals.md", "needs-change", "retain generic goals, remove private project/process references"),
        ("fixtures/json-schema-suite/**", "needs-change", "approved borrowed corpus: reconcile public content policy, provenance and unchanged notices before inclusion"),
        ("src/Brutal.Authoring.Editor.ImGui/Branding/LOGO_BRUTAL_WHT.png", "needs-change", "existing third-party branding: confirm release exception/permission; do not remove required credits to pass a scan"),
        (".gitattributes", "public", "portable text normalization"),
        (".gitignore", "public", "source ignore rules, not proof of clean history"),
        ("Brutal.Authoring.slnx", "public", "public solution"),
        ("Directory.Build.props", "public", "public build contract"),
        ("Directory.Packages.props", "public", "dependency version declarations"),
        ("global.json", "public", "SDK selection"),
        ("LICENSE.txt", "public", "intentional grant; preserve the chosen copyright identity"),
        ("NOTICE.txt", "public", "distribution notice"),
        ("THIRD-PARTY-NOTICES.md", "public", "dependency record; completeness separately reviewed"),
        ("CONTRIBUTING.md", "public", "generic contribution workflow"),
        ("src/**", "public", "generic library and host source"),
        ("samples/**", "public", "original generic samples/adapters"),
        ("tests/**", "public", "generic regression and acceptance tests"),
        ("tools/**", "public", "generic build/check/release tooling"),
        ("build/**", "public", "shared build and notice targets"),
        ("third_party/**", "public", "vendored MIT source, unchanged, with its licence and PINNED.md; recorded in THIRD-PARTY-NOTICES.md"),
        ("docs/guide/**", "public", "portable user/developer guidance"),
        ("docs/spec/**", "public", "public format/API contracts"),
        ("docs/research/**", "public", "generic research after findings are resolved"),
        ("docs/release/**", "public", "portable release/dependency guidance"),
        ("docs/quickstart.md", "public", "generic first-document tutorial"),
        ("docs/quickstart_journal.json", "public", "original tutorial replay journal"),
        ("fixtures/checkers/**", "public", "original game-neutral board positions and checker"),
        ("fixtures/data-perf/**", "public", "original synthetic performance generators"),
        ("fixtures/licences/**", "public", "original compatibility-reference metadata; no legal clearance"),
        ("fixtures/import/**", "public", "original fixtures, generator and provenance"),
    ],
    "reviewed_binary_locations": [("fixtures/import/**", {".glb", ".png"})],
    "detectors": {
        "machine-path": r"(?i)(?:(?<![a-z0-9])[a-z]:[\\/]|\\\\[a-z0-9_.-]+[\\/]|/(?:Users|home)/[^/\s]+/)",
        "email": r"(?i)(?<![a-z0-9._%+\-])[a-z0-9._%+\-]{1,128}@[a-z0-9\-]{1,128}(?:\.[a-z0-9\-]{1,128}){1,8}",
    },
}


def _rule(relative: str) -> tuple[str, str, str | None]:
    for pattern, classification, reason in POLICY["file_rules"]:
        if fnmatch.fnmatchcase(relative.casefold(), pattern.casefold()):
            return classification, reason, pattern
    return "needs-change", "no classification rule; review this candidate explicitly", None


def _private_location(root: Path, path: Path) -> bool:
    try:
        relative = path.relative_to(root)
    except ValueError:
        return True
    return (
        any(part.casefold() in POLICY["pruned_directories"] for part in relative.parts)
        or _rule(relative.as_posix())[0] == "private"
    )


def read_private_list(root: Path, path: Path) -> list[str]:
    if path.is_symlink():
        raise ValueError("private list must be a regular explicit file")
    resolved = path.resolve(strict=True)
    if not _private_location(root, resolved):
        raise ValueError("private list must be outside the proposed public set")
    if resolved.stat().st_size > 2_000_000:
        raise ValueError("configuration is too large")
    raw = json.loads(resolved.read_text(encoding="utf-8-sig"))
    if not isinstance(raw, dict) or set(raw) != {"private_markers"}:
        raise ValueError("configuration needs exactly private_markers")
    markers = raw["private_markers"]
    if not isinstance(markers, list) or not markers:
        raise ValueError("reviewed nonempty marker list required")
    if any(not isinstance(s, str) or len(s.strip()) < 2 or s != s.strip() or any(c in s for c in "\r\n\0") for s in markers):
        raise ValueError("markers must be single-line identifiers")
    if len({s.casefold() for s in markers}) != len(markers):
        raise ValueError("markers must be unique")
    return markers


def _link(path: Path) -> bool:
    return path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction())


def candidates(root: Path) -> tuple[list[Path], list[dict]]:
    files, excluded = [], []
    for directory, dirs, names in os.walk(root, topdown=True, followlinks=False):
        directory = Path(directory)
        keep = []
        for name in sorted(dirs):
            path = directory / name
            reason = POLICY["pruned_directories"].get(name.casefold())
            if reason:
                excluded.append({"path": path.relative_to(root).as_posix(), "reason": reason})
            elif _link(path):
                files.append(path)
            else:
                keep.append(name)
        dirs[:] = keep
        for name in sorted(names):
            path = directory / name
            reason = POLICY["pruned_directories"].get(name.casefold())
            if reason:
                excluded.append({"path": path.relative_to(root).as_posix(), "reason": reason})
            else:
                files.append(path)
    return sorted(files, key=lambda p: p.relative_to(root).as_posix()), excluded



def _csharp_path_text(text: str) -> str:
    """Interpret ordinary C# string escaping for path scans, preserving source line numbers.

    Comments, raw strings and verbatim strings remain literal. This prevents
    JSON Unicode-escape regression strings being mistaken for network paths,
    while actual escaped drive/network strings remain detectable.
    """
    result, i = [], 0
    while i < len(text):
        if text.startswith("//", i) or text.startswith("/*", i):
            end = text.find("\n", i) if text.startswith("//", i) else text.find("*/", i + 2)
            end = len(text) if end < 0 else end + (0 if text.startswith("//", i) else 2)
            result.append(text[i:end])
            i = end
        elif text.startswith('@"', i):
            end = i + 2
            while end < len(text):
                if text.startswith('""', end):
                    end += 2
                elif text[end] == '"':
                    end += 1
                    break
                else:
                    end += 1
            result.append(text[i:end])
            i = end
        elif text[i] in "\"'":
            quote = text[i]
            count = 1
            while i + count < len(text) and text[i + count] == quote:
                count += 1
            if quote == '"' and count >= 3:
                end = text.find(quote * count, i + count)
                end = len(text) if end < 0 else end + count
                result.append(text[i:end])
                i = end
                continue
            end = i + 1
            while end < len(text):
                if text[end] == "\\":
                    end += 2
                elif text[end] == quote:
                    end += 1
                    break
                else:
                    end += 1
            literal = text[i:end]
            if quote == '"':
                try:
                    value = json.loads(literal)
                    literal = '"' + value.replace("\n", "\\n").replace("\r", "\\r") + '"'
                except (ValueError, TypeError):
                    pass
            result.append(literal)
            i = end
        else:
            result.append(text[i])
            i += 1
    return "".join(result)


def _findings(text: str, patterns: dict[str, re.Pattern], *, location: str, suffix: str = "") -> list[dict]:
    result = []
    for kind, pattern in patterns.items():
        scanned = _csharp_path_text(text) if kind == "machine-path" and suffix == ".cs" else text
        lines = sorted({scanned.count("\n", 0, m.start()) + 1 for m in pattern.finditer(scanned)})
        if lines:
            result.append({"kind": kind, "location": location, "lines": lines})
    return result


def classify(root: Path, markers: list[str]) -> dict:
    root = root.resolve(strict=True)
    if not root.is_dir():
        raise ValueError("root is not a directory")
    if not markers or any(not isinstance(s, str) or not s.strip() for s in markers):
        raise ValueError("reviewed marker list required")
    patterns = {k: re.compile(v) for k, v in POLICY["detectors"].items()}
    patterns["private-marker"] = re.compile("|".join(re.escape(s) for s in markers), re.IGNORECASE)
    paths, excluded = candidates(root)
    rows = []
    for path in paths:
        relative = path.relative_to(root).as_posix()
        declared, reason, rule = _rule(relative)
        row = {"path": relative, "declared": declared, "classification": declared,
               "reason": reason, "rule": rule, "issues": []}
        if _link(path):
            row.update(classification="needs-change", reason="link requires review; target was not followed")
            row["issues"].append({"kind": "link", "location": "path", "lines": []})
        elif declared != "private":
            row["issues"].extend(_findings(relative, patterns, location="path"))
            try:
                data = path.read_bytes()
                row["bytes"] = len(data)
                row["sha256"] = hashlib.sha256(data).hexdigest()
                text = data.decode("utf-8-sig", errors="replace")
                binary = b"\0" in data or "\ufffd" in text
                row["binary"] = binary
                reviewed = any(
                    fnmatch.fnmatchcase(relative.casefold(), pattern.casefold())
                    and path.suffix.casefold() in suffixes
                    for pattern, suffixes in POLICY["reviewed_binary_locations"]
                )
                if binary and not reviewed:
                    row["issues"].append({"kind": "binary-review", "location": "content", "lines": []})
                row["issues"].extend(_findings(text, patterns, location="content", suffix=path.suffix.casefold()))
            except OSError:
                row["issues"].append({"kind": "read-error", "location": "content", "lines": []})
        if rule is None:
            row["issues"].append({"kind": "no-rule", "location": "path", "lines": []})
        if row["issues"] and declared == "public":
            row.update(classification="needs-change", reason="public candidate has review findings; " + reason)
        row["check_failure"] = (
            (declared == "public" and bool(row["issues"]))
            or any(i["kind"] in {"no-rule", "read-error", "link"} for i in row["issues"])
        )
        rows.append(row)
    counts = {k: sum(r["classification"] == k for r in rows) for k in ("public", "private", "needs-change")}
    return {
        "schema": 1, "candidate_files": len(rows), "counts": counts,
        "scope": "current-disk source candidates; excluded trees listed separately; no history/packages",
        "check_failures": sum(r["check_failure"] for r in rows),
        "files": rows, "excluded_trees": excluded,
        "limits": "Matches need triage; no matched values printed. Not provenance, secret, binary-metadata, legal, history or release clearance.",
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--private-list", type=Path, required=True, help="reviewed JSON outside the proposed public set")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args(argv)
    try:
        root = args.root.resolve(strict=True)
        report = classify(root, read_private_list(root, args.private_list))
    except (OSError, ValueError, json.JSONDecodeError):
        print(json.dumps({"error": "invalid root or private list; use a regular reviewed JSON outside the public set"}), file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2, ensure_ascii=True))
    return 1 if args.check and report["check_failures"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
