"""Public-text guard over an explicitly supplied tracked-file list. Never invokes Git.

INVENTED release predicate, not a secret scanner, licence audit or publication approval.
Findings omit source excerpts and matched private names. No files are rewritten.
"""
from __future__ import annotations
import argparse
from dataclasses import asdict, dataclass
import hashlib
import json
from pathlib import Path, PurePosixPath
import re

TEXT_SUFFIXES = frozenset(".cs .py .json .proposal .md .txt .props .targets .csproj .sln .slnx .yml .yaml .xml .html .css .js .ts .toml .cfg .ini .sh .ps1 .bat .cmd .svg .csv .editorconfig .gitignore .gitattributes .config .resx .nuspec .gumx .gusx .gucx .gutx .behx .gumj .gusj .gucj .gutj .behj .fnt .achx .ganx .ganj".split())
TEXT_NAMES = frozenset({"README", "LICENSE", "LICENCE", "NOTICE", "COPYING", "Dockerfile", ".gitignore", ".gitattributes", ".editorconfig"})
BINARY_SUFFIXES = frozenset(".png .jpg .jpeg .gif .ico .webp .bmp .ttf .otf .woff .woff2 .dll .exe .pdb .zip .nupkg .pdf .blend .glb .gltf .wav .mp3 .ogg .mp4 .xlsx".split())
DRIVE = re.compile(r"(?<![A-Za-z0-9_])[A-Za-z]:[\\/]")
HOME = re.compile(r"/(?:Users|home)/[^/\s<>\"']+", re.IGNORECASE)
CODES = {"PT001": "drive-rooted path (value omitted)", "PT002": "home-rooted path (value omitted)", "PT003": "private project name (value omitted)"}

@dataclass(frozen=True)
class Finding:
    file: str
    line: int
    code: str
    message: str
    line_sha256: str


def digest(data):
    return hashlib.sha256(data).hexdigest()


def relative_path(value):
    if not isinstance(value, str) or not value or "\\" in value or ":" in value or any(ord(c) < 32 for c in value):
        raise ValueError("tracked paths must be nonempty forward-slash repository-relative names")
    path = PurePosixPath(value)
    if path.is_absolute() or any(part in {"", ".", ".."} for part in value.split("/")):
        raise ValueError("tracked paths must not be rooted or contain dot/empty components")
    return value


def tracked_paths(data, format="nul"):
    text = data.decode("utf-8-sig")
    if format == "nul":
        rows = text.split("\x00")
        if rows[-1] == "": rows.pop()
    elif format == "lines":
        if "\x00" in text: raise ValueError("NUL in line-delimited list; use nul format")
        rows = text.splitlines()
    else:
        raise ValueError("unknown tracked-list format")
    if not rows: raise ValueError("empty tracked-file list")
    paths = [relative_path(row) for row in rows]
    if len(set(paths)) != len(paths): raise ValueError("duplicate tracked-file entry")
    return paths


def private_names(path):
    if not path.exists(): return []  # optional local configuration; intentionally quiet
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict): raise ValueError("private-name configuration must be an object")
    names = value.get("consumer_roots", [])
    if not isinstance(names, list) or any(not isinstance(n, str) or not n.strip() for n in names):
        raise ValueError("consumer_roots must be nonempty strings")
    return list(dict.fromkeys(names))


def segments(text):
    """Unicode alphanumeric words split at lower/upper, acronym/title and digit transitions."""
    result = []
    for word in re.findall(r"[^\W_]+", text, re.UNICODE):
        start = 0
        for i in range(1, len(word)):
            previous, current = word[i-1], word[i]
            split = previous.isdigit() != current.isdigit()
            split |= previous.islower() and current.isupper()
            split |= previous.isupper() and current.isupper() and i+1 < len(word) and word[i+1].islower()
            if split:
                result.append(word[start:i].casefold()); start = i
        result.append(word[start:].casefold())
    return result


def contains_name(text, name):
    # A whole word is case-insensitive even when a name's internal capitals differ. Otherwise require complete
    # identifier segments on BOTH ends; a short fragment inside an ordinary word is not a project identity.
    words = re.findall(r"[^\W_]+", text, re.UNICODE)
    if any(word.casefold() == name.casefold() for word in words): return True
    wanted = segments(name)
    if not wanted: return False
    actual = segments(text)
    return any(actual[i:i+len(wanted)] == wanted for i in range(len(actual)-len(wanted)+1))


def scan_text(text, file, names):
    # Whole words or complete identifier segments, including prose/comments. No source escape decoding.
    # Optional private names are never printed.
    findings = []
    for number, line in enumerate(text.splitlines(), 1):
        codes = []
        if DRIVE.search(line): codes.append("PT001")
        if HOME.search(line): codes.append("PT002")
        if any(contains_name(line, name) for name in names): codes.append("PT003")
        for code in codes:
            findings.append(Finding(file, number, code, CODES[code], digest(line.encode("utf-8"))))
    return findings


def load_allowlist(path):
    if path is None: return []
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict) or value.get("version") != 1:
        raise ValueError("allowlist version must be 1")
    rows = value.get("entries")
    if not isinstance(rows, list) or len(rows) > 32: raise ValueError("allowlist must contain at most 32 exact entries")
    seen = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"file", "line", "code", "line_sha256", "reason"}:
            raise ValueError("allowlist requires exact file/line/code/line_sha256/reason")
        relative_path(row["file"])
        if type(row["line"]) is not int or row["line"] < 1 or row["code"] not in CODES:
            raise ValueError("invalid allowlist line/code")
        if not isinstance(row["line_sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", row["line_sha256"]):
            raise ValueError("allowlist requires an exact line SHA-256")
        if not isinstance(row["reason"], str) or len(row["reason"].strip()) < 10:
            raise ValueError("allowlist needs a specific human reason")
        key = tuple(row[k] for k in ("file", "line", "code", "line_sha256"))
        if key in seen: raise ValueError("duplicate allowlist entry")
        seen.add(key)
    return rows


def load_omissions(path):
    if path is None: return []
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    rows = value.get("omissions", [])
    if not isinstance(rows, list) or len(rows) > 1: raise ValueError("only the reviewed upstream fixture omission is supported")
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"prefix", "reason"}:
            raise ValueError("omission needs exact prefix and reason")
        if row["prefix"] != "fixtures/json-schema-suite/" or not isinstance(row["reason"], str) or len(row["reason"].strip()) < 10:
            raise ValueError("unreviewed omission prefix or missing reason")
    return rows


def linked(path):
    return path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction())


def safe_file(root, relative):
    path = root
    for part in PurePosixPath(relative_path(relative)).parts:
        path = path / part
        if linked(path): raise ValueError("linked path is not followed")
    if not path.is_file(): raise ValueError("listed file is missing or not a regular file")
    return path


def inspect(repo, paths, names, allowlist, omissions=()):
    root = Path(repo)
    if linked(root): raise ValueError("linked repository root is not followed")
    root = root.resolve(strict=True)
    findings, allowed, omitted, errors, inputs = [], [], [], [], {}
    keys = {(r["file"], r["line"], r["code"], r["line_sha256"]): r for r in allowlist}
    used = set()
    decodings = {}
    for relative in paths:
        try:
            relative_path(relative)
            omitted_by = next((row for row in omissions if relative.startswith(row["prefix"])), None)
            if omitted_by is not None:
                omitted.append({"file": relative, "reason": "explicit upstream fixture omission: " + omitted_by["reason"]})
                continue
            path = safe_file(root, relative)
            if path.suffix.casefold() in BINARY_SUFFIXES:
                omitted.append({"file": relative, "reason": "binary/resource extension; content not opened"})
                continue
            if path.suffix.casefold() not in TEXT_SUFFIXES and path.name not in TEXT_NAMES:
                errors.append({"file": relative, "line": 1, "message": "unclassified extension; explicit text policy review needed"})
                continue
            if path.stat().st_size > 16 * 1024 * 1024:
                raise ValueError("text exceeds bounded read limit")
            data = path.read_bytes()
            inputs[relative] = digest(data)
            try:
                text = data.decode("utf-8-sig")
                decodings[relative] = "utf-8-sig"
            except UnicodeDecodeError:
                if relative != "tests/Bed.Tests/UntypedFieldsTests.cs": raise
                text = data.decode("latin-1")
                decodings[relative] = "latin-1 (explicit known-file fallback)"
            if "\x00" in text: raise ValueError("NUL in selected text; encoding/content review needed")
            for finding in scan_text(text, relative, names):
                key = (finding.file, finding.line, finding.code, finding.line_sha256)
                if key in keys:
                    used.add(key)
                    allowed.append({**asdict(finding), "reason": keys[key]["reason"]})
                else: findings.append(asdict(finding))
        except (OSError, UnicodeError, ValueError):
            errors.append({"file": relative, "line": 1, "message": "listed text could not be safely read; path/encoding review needed"})
    stale = [r for key, r in keys.items() if key not in used]
    return {"scope": "caller-supplied tracked-file list; tracking not independently verified", "listed": len(paths),
            "inputs": inputs, "decodings": decodings, "findings": findings, "allowed": allowed, "omitted": omitted, "errors": errors,
            "stale_allowlist": stale, "private_names_loaded": len(names),
            "limitations": ["UTF-8 literal text plus one explicitly named latin-1 fallback; no escape decoding or secret detection", "binary/resource contents not inspected", "no Git, code evaluation, cleanup or publication"]}


def run(repo, tracked_file, format="nul", allowlist=None):
    root = Path(repo)
    if linked(root): raise ValueError("linked repository root is not followed")
    data = Path(tracked_file).read_bytes()
    paths = tracked_paths(data, format)
    local = root / "tools" / "generic_boundary_local.json"
    # A linked local configuration is not an excuse to read outside the checkout.
    if linked(local): raise ValueError("linked local configuration is not followed")
    if local.exists(): safe_file(root.resolve(strict=True), "tools/generic_boundary_local.json")
    result = inspect(root, paths, private_names(local), load_allowlist(allowlist), load_omissions(allowlist))
    result["tracked_list_sha256"] = digest(data)
    for row in result["findings"]: print(f"{row['file']}:{row['line']}: {row['code']} {row['message']}")
    for row in result["errors"]: print(f"{row['file']}:{row['line']}: PT900 {row['message']}")
    print(f"{len(result['inputs'])} text files checked; {len(result['findings'])} findings; {len(result['allowed'])} allowed; "
          f"{len(result['omitted'])} binary/resource omissions; {len(result['errors'])} review errors; {len(result['stale_allowlist'])} stale allowances")
    code = 2 if result["errors"] or result["stale_allowlist"] else 1 if result["findings"] else 0
    return code, result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", required=True, type=Path)
    parser.add_argument("--tracked-files", required=True, type=Path)
    parser.add_argument("--tracked-format", choices=["nul", "lines"], default="nul")
    parser.add_argument("--allowlist", type=Path)
    args = parser.parse_args(argv)
    try: return run(args.repo, args.tracked_files, args.tracked_format, args.allowlist)[0]
    except (OSError, UnicodeError, ValueError, TypeError):
        print("<configuration>:1: PT900 invalid/unreadable list, repository or configuration (values omitted)")
        return 2

if __name__ == "__main__": raise SystemExit(main())
