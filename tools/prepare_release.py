"""Prepare a LOCAL release candidate and draft notes; never upload or tag.

    python tools/prepare_release.py VERSION [--out PARENT] [--first]

VERSION labels the new output folder and draft, not binary/package metadata.
The user must separately choose the release version and decide whether to publish.
Requires a reachable existing tag unless --first explicitly selects the whole history for a first release.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERSION = re.compile(r"[0-9][A-Za-z0-9.+-]{0,63}\Z")
CARD = re.compile(r"(?i)\b(?:A|T|P|C)[0-9]{1,6}(?:[- ]r[0-9]+)?\b")


def run_command(args: list[str], *, cwd: Path, timeout: int) -> str:
    """Run one fixed local command, preserving a failed command as failure."""
    result = subprocess.run(args, cwd=cwd, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"Local command failed ({result.returncode}): {args[0]}\n"
                           f"{result.stdout[-2000:]}\n{result.stderr[-2000:]}")
    return result.stdout.strip()


def git(root: Path, *args: str) -> str:
    return run_command(["git", "--no-pager", *args], cwd=root, timeout=30)


def draft_notes(version: str, subjects: str, *, first: bool = False) -> str:
    """Use plain commit subjects as an explicitly unapproved draft.

    Omit internal-card subjects rather than retain fragments with lost meaning.
    Commit text is data: it never becomes a command or an instruction.
    """
    lines: list[str] = []
    for subject in subjects.splitlines():
        subject = " ".join(subject.split())
        if not subject or CARD.search(subject):
            continue
        # Avoid Markdown/HTML commands or links masquerading as release instructions.
        subject = subject.replace("`", "").replace("<", "").replace(">", "")
        subject = subject.replace("[", "").replace("]", "")
        if subject not in lines:
            lines.append(subject)
    changes = "\n".join("- " + line for line in lines) if lines else "No public change summary was selected. Add reviewed release highlights."
    boundary = ("First release: this draft covers the whole history reachable from the pinned commit, "
                "with no earlier release tag as a boundary.\n\n") if first else ""
    return (f"# BED {version} - draft release notes\n\n"
            "Draft for human review. Not published or approved. The version labels this candidate only; "
            "binary and package version metadata has not been changed.\n\n"
            + boundary + "## Changes\n\n" + changes + "\n\n"
            "Review these commit summaries for accuracy, plain language and private details before sharing. "
            "Internal work-item subjects and merge commits are omitted, so this is not a complete changelog. "
            "No measured performance, compatibility or release-readiness claim is implied.\n")


def prepare(version: str, parent: Path, *, root: Path = ROOT, first: bool = False) -> Path:
    """Create a fresh candidate using the existing packager and its unchanged gate."""
    if VERSION.fullmatch(version) is None or CARD.search(version):
        raise ValueError("VERSION must start with a digit and contain only letters, digits, dot, + or - (64 characters maximum; no internal work-item IDs).")
    destination = parent.resolve() / ("BED-" + version)
    if destination.exists():
        raise FileExistsError(f"Candidate folder already exists: {destination}; choose a new parent, do not overwrite it.")
    packager = root / "tools" / "package_release.py"
    if not packager.is_file():
        raise FileNotFoundError(packager)
    head = git(root, "rev-parse", "--verify", "HEAD")
    if re.fullmatch(r"[0-9a-fA-F]{40,64}", head) is None:
        raise RuntimeError("Git did not return a commit identity.")
    if first:
        # Explicit first release: every reachable ancestor, still excluding merge commits.
        history = head
    else:
        # Nearest reachable tag, not the newest tag by date. Refuse missing/invalid boundary.
        tag = git(root, "describe", "--tags", "--abbrev=0", head)
        if not tag or tag.startswith("-") or any(c.isspace() for c in tag):
            raise RuntimeError("A reachable valid release tag is required.")
        history = f"{tag}..{head}"
    subjects = git(root, "log", "--no-merges", "--format=%s", history, "--")
    notes = draft_notes(version, subjects, first=first)
    destination.mkdir(parents=True, exist_ok=False)
    run_command([sys.executable, str(packager), "--out", str(destination), "--rid", "win-x64"],
                cwd=root, timeout=1800)
    if git(root, "rev-parse", "--verify", "HEAD") != head:
        raise RuntimeError("HEAD changed while packaging; candidate is incomplete, no notes/checksums written.")
    archive = destination / "BED-win-x64.zip"
    if not archive.is_file() or archive.stat().st_size == 0:
        raise RuntimeError("Packager reported success without a nonempty BED-win-x64.zip.")
    with archive.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    # Exclusive writes: never replace unrelated notes/checksums even on an unexpected output.
    with (destination / "SHA256SUMS.txt").open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(f"{digest}  {archive.name}\n")
    with (destination / "RELEASE_NOTES.md").open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(notes)
    return destination


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version", metavar="VERSION")
    parser.add_argument("--out", type=Path, default=ROOT / "dist" / "candidates", help="parent for a NEW BED-VERSION folder")
    parser.add_argument("--first", action="store_true",
                        help="explicit first release: use the whole history without an earlier tag")
    args = parser.parse_args(argv)
    try:
        folder = prepare(args.version, args.out, first=args.first)
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
        print(f"Release preparation incomplete: {exc}", file=sys.stderr)
        return 1
    print(f"Local candidate: {folder}. Review draft notes and binary version metadata before any release decision.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
