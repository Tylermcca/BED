# SPDX-License-Identifier: MIT
"""Source-only sample inventory checks. Never runs MSBuild, samples or README commands."""
from pathlib import Path
import os
import xml.etree.ElementTree as ET


def sample_files(root):
    """Only project/README metadata, excluding generated directories and linked folders."""
    result = []
    for folder, dirs, files in os.walk(root / "samples", followlinks=False):
        dirs[:] = sorted(d for d in dirs if d not in {"bin", "obj", ".git"}
                         and not (Path(folder) / d).is_symlink())
        result.extend(Path(folder) / name for name in sorted(files)
                      if (name.endswith(".csproj") or name.lower() == "readme.md")
                      and not (Path(folder) / name).is_symlink())
    return result


def check_membership(root):
    """Check all source sample projects, independent of solution folder labels."""
    root = Path(root).resolve()
    projects = [p.relative_to(root).as_posix() for p in sample_files(root) if p.suffix == ".csproj"]
    solution = ET.parse(root / "Bed.slnx")
    members = {node.attrib["Path"].replace("\\", "/") for node in solution.iter("Project")}
    errors = ["Sample project is absent from solution: " + p for p in projects if p not in members]
    if not projects:
        errors.append("No source sample projects found")
    return sorted(projects), errors


# Supported documentation grammar: one root-relative command per fenced line or inline code span.
# No shell evaluation, cd inference, variable expansion, globbing or command execution.
import argparse
import json
import re
import shlex

COMMANDS = {"dotnet", "python", "python3", "BED", "bed-cli"}
PATH_OPTIONS = {"--project", "--data", "--schemas", "--map", "--adapter", "--settings", "--root"}


def commands(text):
    fenced = False
    for number, line in enumerate(text.splitlines(), 1):
        if line.strip().startswith("```"):
            fenced = not fenced
            continue
        spans = [line.strip()] if fenced else re.findall(r"`([^`]+)`", line)
        for span in spans:
            if not span.strip():
                continue
            first = span.split()[0]
            if first in COMMANDS:
                yield number, span
            elif fenced:
                raise ValueError(f"line {number}: unsupported command block; document a single supported root-relative command")


def check_readme(root, text, name, overlay=None):
    root = Path(root).resolve()
    overlay = Path(overlay).resolve() if overlay else None
    errors, reports, built = [], [], set()

    def source(relative):
        if overlay and (overlay / relative).exists():
            return overlay / relative
        return root / relative

    def relative(token):
        token = token.replace("\\", "/")
        path = Path(token)
        if path.is_absolute() or re.match(r"^[A-Za-z]:", token):
            raise ValueError("absolute paths are not portable sample commands: " + token)
        resolved = (root / path).resolve()
        try:
            return resolved.relative_to(root)
        except ValueError:
            raise ValueError("path escapes the repository: " + token) from None

    try:
        entries = list(commands(text))
    except ValueError as error:
        return [f"{name}: {error}"], reports
    for line, command in entries:
        at = f"{name}:{line}"
        try:
            tokens = shlex.split(command)
            if any(t in {"&&", "||", ";", "|", ">", "<"} for t in tokens):
                raise ValueError("compound shell syntax is not checked")
            build_project = None
            for i, token in enumerate(tokens[1:], 1):
                prior = tokens[i - 1]
                forced = prior in PATH_OPTIONS or (i == 1 and tokens[0] in {"python", "python3"})
                forced |= i == 2 and tokens[0] == "dotnet" and tokens[1] in {"build", "test"}
                if re.fullmatch(r"<[^<>]+>", token):
                    reports.append(f"{at}: USER-SUPPLIED {token} (existence not claimed)")
                    continue
                if token.startswith("-") or not (forced or "/" in token or "\\" in token or token.endswith((".csproj", ".slnx", ".py", ".json", ".dll"))):
                    continue
                rel = relative(token)
                if "bin" in rel.parts:
                    owner = Path(*rel.parts[:rel.parts.index("bin")])
                    projects = sorted(source(owner).glob("*.csproj"))
                    if not projects or not any((owner / q.name).as_posix() in built for q in projects):
                        raise ValueError(f"generated path lacks an owning csproj with a prior documented build: {token}")
                    reports.append(f"{at}: GENERATED {token} (existence not claimed)")
                    continue
                if not source(rel).exists():
                    raise ValueError("missing source path: " + token)
                reports.append(f"{at}: SOURCE {token}")
                if i == 2 and tokens[:2] == ["dotnet", "build"]:
                    if rel.suffix == ".csproj":
                        build_project = rel.as_posix()
                    elif source(rel).is_dir():
                        owners = list(source(rel).glob("*.csproj"))
                        if len(owners) == 1:
                            build_project = (rel / owners[0].name).as_posix()
            if build_project:
                built.add(build_project)
        except ValueError as error:
            errors.append(f"{at}: {error}")
    return errors, reports


def inspect(root, overlay=None):
    root = Path(root).resolve()
    overlay = Path(overlay).resolve() if overlay else None
    projects, errors = check_membership(root)
    readmes = {p.relative_to(root).as_posix(): p for p in sample_files(root) if p.name.lower() == "readme.md"}
    if overlay:
        readmes.update({p.relative_to(overlay).as_posix(): p for p in sample_files(overlay) if p.name.lower() == "readme.md"})
    reports = []
    for name, path in sorted(readmes.items()):
        found, detail = check_readme(root, path.read_text(encoding="utf-8-sig"), name, overlay)
        errors.extend(found)
        reports.extend(detail)
    if "samples/README.md" not in readmes:
        errors.append("Missing samples/README.md index")
    else:
        index = readmes["samples/README.md"].read_text(encoding="utf-8-sig")
        for project in projects:
            if project not in index:
                errors.append("Sample index omits project path: " + project)
    return {"projects": projects, "readmes": sorted(readmes), "paths": reports, "errors": errors,
            "builds": "UNRUN; source membership/path checks only"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--overlay", type=Path, help="prepared README/tools overlay; never copied or executed")
    args = parser.parse_args()
    try:
        result = inspect(args.root, args.overlay)
    except (OSError, ET.ParseError, KeyError, ValueError) as error:
        print("ERROR: " + str(error))
        return 2
    print(json.dumps(result, indent=2))
    return 1 if result["errors"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
