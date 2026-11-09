# SPDX-License-Identifier: MIT
"""Read-only header hygiene report over an explicitly reviewed file list. Not licence clearance."""
import argparse
import json
from pathlib import Path, PurePosixPath


HEADERS = {".cs": "// SPDX-License-Identifier: MIT", ".py": "# SPDX-License-Identifier: MIT"}


def has_header(text, suffix):
    """Recognize the agreed standalone line before code, among blank/single-line comments."""
    expected = HEADERS[suffix]
    for line in text.lstrip("\ufeff").splitlines():
        line = line.strip()
        if line == expected:
            return True
        if not line or line.startswith("//" if suffix == ".cs" else "#"):
            continue
        return False
    return False


def reviewed_files(manifest):
    """Validate an explicit scope record; this does not approve the listed files for a caller."""
    if not isinstance(manifest, dict) or set(manifest) != {"scope", "excluded", "files"}:
        raise ValueError("manifest requires exactly scope, excluded and files")
    if any(not isinstance(manifest[k], str) or not manifest[k].strip() for k in ("scope", "excluded")):
        raise ValueError("scope and excluded must be nonempty descriptions")
    files = manifest["files"]
    if not isinstance(files, list) or not files:
        raise ValueError("files must be a nonempty explicit list")
    seen = set()
    for name in files:
        if not isinstance(name, str) or not name or "\\" in name or ":" in name:
            raise ValueError("file names must be relative forward-slash paths")
        path = PurePosixPath(name)
        if path.is_absolute() or any(part in ("", ".", "..") for part in name.split("/")):
            raise ValueError("file names must not contain empty, dot or parent segments")
        if name.casefold() in seen:
            raise ValueError("duplicate file name")
        seen.add(name.casefold())
    return files


def report(root, manifest):
    """Read only named files. Never recurse, rewrite, add a header, or inspect excluded content."""
    files = reviewed_files(manifest)
    root = Path(root).resolve()
    findings = []
    for name in files:
        suffix = PurePosixPath(name).suffix
        if suffix not in HEADERS:
            findings.append({"file": name, "status": "unsupported"})
            continue
        target = root / name
        try:
            resolved = target.resolve()
            if not resolved.is_relative_to(root):
                findings.append({"file": name, "status": "unreadable", "reason": "outside declared root"})
                continue
            text = target.read_text(encoding="utf-8-sig")
            findings.append({"file": name, "status": "present" if has_header(text, suffix) else "missing"})
        except (OSError, UnicodeError, ValueError):
            findings.append({"file": name, "status": "unreadable", "reason": "not readable as UTF-8 source"})
    return {"scope": manifest["scope"], "excluded": manifest["excluded"],
            "coverage_complete": all(x["status"] in ("present", "missing") for x in findings),
            "policy_enforced": False, "files": findings}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        result = report(args.root, json.loads(args.manifest.read_text(encoding="utf-8-sig")))
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))
    print(json.dumps(result, indent=2, ensure_ascii=True))
    return 0  # Missing/unreadable/unsupported are report findings, never an adopted policy gate.


if __name__ == "__main__":
    raise SystemExit(main())
