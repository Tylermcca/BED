"""Record the dependencies of an actual staged release, without restore, network or execution.

INVENTED generator; BORROWED evidence formats: .NET deps.json and NuGet nuspec.
This records declarations and local files, never legal compatibility decisions.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath


class RecordError(ValueError):
    """The built inputs or local evidence are incomplete or inconsistent."""


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def safe_relative(text: str) -> str:
    text = text.replace("\\", "/")
    value = PurePosixPath(text)
    if not text or value.is_absolute() or any(p in {"..", "."} for p in text.split("/")) or ":" in text or "\0" in text:
        raise RecordError("unsafe relative evidence path")
    return value.as_posix()


def inside(root: Path, relative: str) -> Path:
    path = (root / safe_relative(relative)).resolve()
    if not path.is_relative_to(root.resolve()):
        raise RecordError("evidence path escapes its local package")
    return path


def local_caches() -> list[Path]:
    values = [os.environ.get("NUGET_PACKAGES", "")]
    values += os.environ.get("RestoreFallbackFolders", "").split(";")
    values += [str(Path.home() / ".nuget" / "packages")]
    return list(dict.fromkeys(Path(v).resolve() for v in values if v))


def package_metadata(identity: str, library: dict, caches: list[Path]) -> dict:
    """Read the exact package's declaration. No licence is inferred from a URL or name."""
    name, version = identity.rsplit("/", 1)
    if library["type"] == "runtimepack" and name.startswith("runtimepack."):
        name = name[len("runtimepack."):]
    relative = safe_relative(library.get("path", f"{name.lower()}/{version.lower()}"))
    folder = next((inside(c, relative) for c in caches if inside(c, relative).is_dir()), None)
    if folder is None:
        raise RecordError(f"local package unavailable: {name}/{version}; restore it before recording")
    nuspecs = sorted(folder.glob("*.nuspec"))
    if len(nuspecs) != 1:
        raise RecordError(f"expected one nuspec for {name}/{version}")
    root = ET.fromstring(nuspecs[0].read_bytes())
    def element(tag: str):
        return next((e for e in root.iter() if e.tag.rsplit("}", 1)[-1] == tag), None)
    def value(tag: str) -> str:
        e = element(tag)
        return (e.text or "").strip() if e is not None else ""
    if value("id").casefold() != name.casefold() or value("version").casefold() != version.casefold():
        raise RecordError(f"nuspec identity does not match built package {name}/{version}")
    licence = element("license")
    declared = (licence.text or "").strip() if licence is not None else ""
    kind = licence.get("type", "") if licence is not None else ""
    if kind not in {"file", "expression"} or not declared:
        kind, declared = "unknown", "UNKNOWN (no licence declaration)"
    files = []
    for p in sorted(folder.rglob("*")):
        if p.is_file() and re.match(r"(?i)^(licen[cs]e|copying)([._-]|$)", p.name):
            rel = p.relative_to(folder).as_posix()
            inside(folder, rel)
            files.append(rel)
    notes = [p for p in sorted(folder.rglob("*")) if p.is_file()
             and ("third-party" in p.name.lower() or p.name.lower() in {"notice", "notice.txt"})]
    for p in notes:
        inside(folder, p.relative_to(folder).as_posix())
    missing = False
    heading = ""
    if kind == "file":
        chosen = inside(folder, declared)
        missing = not chosen.is_file()
        if not missing:
            if declared not in files:
                files.append(declared)
            heading = chosen.read_text(encoding="utf-8-sig", errors="replace").splitlines()[0].strip()
    receipts = {p.relative_to(folder).as_posix(): sha(p) for p in [nuspecs[0], *notes]}
    receipts.update({f: sha(inside(folder, f)) for f in files})
    return dict(id=name, version=version, type=library["type"], cache=relative, folder=folder,
                licence_kind=kind, licence=declared, licence_heading=heading,
                licence_files=sorted(files), missing_declared_file=missing,
                nuspec=nuspecs[0].name, notices=notes, receipts=receipts)


def notice_sections(path: Path) -> list[dict]:
    """Read explicitly labelled vendor metadata, preserving its words rather than guessing SPDX."""
    lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    result = []
    fields = {"Name": "name", "Version": "version", "SPDX": "licence", "Shipped as": "shipped"}
    current = None
    last = None
    for line in lines:
        match = re.match(r"^  (Name|Version|SPDX|Shipped as)\s{2,}(.*)$", line)
        if match:
            key, text = match.groups()
            if key == "Name":
                if current:
                    result.append(current)
                current = {"name": text.strip(), "version": "UNKNOWN", "licence": "UNKNOWN", "shipped": ""}
            if current is not None:
                last = fields[key]
                current[last] = text.strip()
        elif current is not None and last and re.match(r"^\s{10,}\S", line):
            current[last] += " " + line.strip()
        else:
            last = None
    if current:
        result.append(current)
    expanded = []
    for item in result:
        if item["version"].endswith(" respectively"):
            names = [v.strip() for v in item["name"].split(",")]
            versions = [v.strip() for v in item["version"].removesuffix(" respectively").split(",")]
            if len(names) == len(versions):
                expanded.extend(dict(item, name=n, version=v) for n, v in zip(names, versions))
                continue
        expanded.append(item)
    return expanded


def applicability(package: str, item: dict, rid: str) -> str:
    """Pinned vendor manifest scope: distinguish declarations for sibling packages/platforms."""
    if item["shipped"] and "only" in item["shipped"] and "linux-x64" in item["shipped"] and not rid.startswith("linux-"):
        return "not selected: vendor says Linux only"
    if package.lower().endswith((".imgui", ".imgui.backends", ".imgui.demowindow", ".imgui.freetype",
                                 ".implot", ".implot.demowindow", ".imgui.abstractions", ".imgui.extensions")):
        suffix = package.lower()
        name = item["name"].lower()
        if "implot" in name and ".implot" not in suffix:
            return "not selected: sibling ImPlot package"
        if name.startswith("stb_") and not suffix.endswith((".imgui", ".imgui.freetype")):
            return "not selected: stb compiled into sibling native ImGui package"
        if suffix.endswith((".imgui.abstractions", ".imgui.extensions")):
            return "not selected: vendor table says no third-party code"
        if "dear imgui" in name and ".implot" in suffix:
            return "not selected: sibling ImGui package"
    return "vendor declaration for selected package; linkage/terms need human review"

def collect(published: Path, caches: list[Path], rid: str | None = None) -> dict:
    """Use all staged deps manifests, including adapters and self-contained runtime packs."""
    published = published.resolve()
    manifests = sorted(published.rglob("*.deps.json"))
    if not manifests:
        raise RecordError("no published .deps.json files; build a fresh release first")
    documents = [(p, json.loads(p.read_text(encoding="utf-8-sig"))) for p in manifests]
    rids = {d["runtimeTarget"]["name"].split("/", 1)[1] for _, d in documents if "/" in d["runtimeTarget"]["name"]}
    if rid is None:
        if len(rids) != 1:
            raise RecordError("specify --rid when the staged manifests do not identify exactly one RID")
        rid = next(iter(rids))
    if rids and rids != {rid}:
        raise RecordError("staged runtime identifiers disagree with --rid")
    native_helpers = set()
    for _, doc in documents:
        for entry in doc["targets"][doc["runtimeTarget"]["name"]].values():
            native_helpers.update(PurePosixPath(a).name.casefold() for a in entry.get("native", {}))
            native_helpers.update(PurePosixPath(a).name.casefold() for a, v in entry.get("runtimeTargets", {}).items()
                                  if v.get("assetType") == "native" and v.get("rid") == rid)
    for exe in published.rglob("*.exe"):
        if not exe.with_suffix(".deps.json").is_file() and exe.name.casefold() not in native_helpers:
            raise RecordError("an executable has no dependency manifest: " + exe.relative_to(published).as_posix())
    file_index = {}
    for p in sorted(published.rglob("*")):
        if p.is_file():
            file_index.setdefault(p.name.casefold(), []).append(p)
    def candidates_for(manifest, asset):
        basename = PurePosixPath(asset).name
        bases = [manifest.parent]
        # The current release loads adapters from adapters/ and shares the host's libraries.
        if manifest.relative_to(published).parts[0] == "adapters":
            bases.append(published)
        expected = {(base / asset).resolve() for base in bases} | {(base / basename).resolve() for base in bases}
        return [p for p in file_index.get(basename.casefold(), []) if p.resolve() in expected]

    packages, native, components, excluded, unparsed, references = {}, {}, {}, {}, {}, {}
    programs, known_binaries, evidence, runtime_bytes = [], set(), {}, {}
    for path, doc in documents:
        target_name = doc["runtimeTarget"]["name"]
        if target_name not in doc["targets"]:
            raise RecordError("runtime target missing from manifest")
        target = doc["targets"][target_name]
        program = path.relative_to(published).as_posix().removesuffix(".deps.json")
        programs.append(dict(name=program, target=target_name, manifest=path.relative_to(published).as_posix(),
                             sha256=sha(path), packages=sum(doc["libraries"].get(k, {}).get("type") in {"package", "runtimepack"} for k in target)))
        roots = [v for k, v in target.items() if doc["libraries"].get(k, {}).get("type") == "project"
                 and any(PurePosixPath(a).name == path.name.removesuffix(".deps.json") + ".dll" for a in v.get("runtime", {}))]
        direct = set().union(*(set(v.get("dependencies", {})) for v in roots))
        for identity, entry in sorted(target.items()):
            library = doc["libraries"].get(identity)
            if library is None:
                raise RecordError("target library has no metadata: " + identity)
            for asset in list(entry.get("runtime", {})) + list(entry.get("resources", {})) + [a for a, v in entry.get("runtimeTargets", {}).items() if v.get("assetType") == "runtime" and v.get("rid") == rid]:
                if PurePosixPath(asset).name != "_._":
                    known_binaries.add(PurePosixPath(safe_relative(asset)).name.casefold())
            if library["type"] == "project":
                continue
            if library["type"] == "reference":
                key = (identity, program)
                files = []
                for asset in entry.get("runtime", {}):
                    copies = candidates_for(path, safe_relative(asset))
                    if not copies:
                        raise RecordError("declared assembly reference absent: " + asset)
                    for copy in copies:
                        files.append(copy.relative_to(published).as_posix())
                        runtime_bytes[copy.relative_to(published).as_posix()] = sha(copy)
                references[key] = dict(identity=identity, uses=program, files=sorted(files),
                                       licence="UNKNOWN (non-NuGet assembly reference; inspect its project/source terms)")
                continue
            if library["type"] not in {"package", "runtimepack"}:
                raise RecordError("unsupported library type: " + library["type"])
            if identity not in packages:
                packages[identity] = package_metadata(identity, library, caches)
                packages[identity]["uses"] = {}
            metadata = packages[identity]
            metadata["uses"][program] = "direct" if identity.rsplit("/", 1)[0] in direct else "transitive"
            managed = list(entry.get("runtime", {}))
            managed += [a for a, v in entry.get("runtimeTargets", {}).items()
                        if v.get("assetType") == "runtime" and v.get("rid") == rid]
            for asset in managed:
                asset = safe_relative(asset)
                if PurePosixPath(asset).name == "_._":
                    continue
                basename = PurePosixPath(asset).name
                original = inside(metadata["folder"], asset)
                if not original.is_file():
                    found = [p for p in metadata["folder"].rglob(basename) if p.is_file()]
                    if len(found) != 1:
                        raise RecordError("managed source unavailable or ambiguous: " + basename)
                    original = found[0]
                expected = sha(original)
                copies = [p for p in candidates_for(path, asset) if sha(p) == expected]
                if not copies:
                    raise RecordError("published managed file absent or differs from local package: " + basename)
                for copy in copies:
                    runtime_bytes[copy.relative_to(published).as_posix()] = expected
            for note in metadata["notices"]:
                location = metadata["cache"] + "/" + note.relative_to(metadata["folder"]).as_posix()
                parsed = notice_sections(note)
                if not parsed:
                    unparsed[location] = sha(note)
                for item in parsed:
                    status = applicability(metadata["id"], item, rid)
                    key = (item["name"], item["version"], item["licence"], location)
                    group = excluded if status.startswith("not selected") else components
                    if key not in group:
                        group[key] = dict(item, location=location, sha256=sha(note), uses=set(), status=status)
                    group[key]["uses"].add(program)
            natives = list(entry.get("native", {}))
            natives += [a for a, v in entry.get("runtimeTargets", {}).items()
                        if v.get("assetType") == "native" and v.get("rid") == rid]
            for asset in natives:
                asset = safe_relative(asset)
                if PurePosixPath(asset).name == "_._":
                    continue
                basename = PurePosixPath(asset).name
                candidates = candidates_for(path, asset)
                if not candidates:
                    raise RecordError("declared native file absent from release: " + basename)
                original = inside(metadata["folder"], asset)
                if not original.is_file():
                    matches = [p for p in metadata["folder"].rglob(basename) if p.is_file()]
                    if len(matches) != 1:
                        raise RecordError("native source unavailable or ambiguous: " + basename)
                    original = matches[0]
                expected = sha(original)
                matches = [p for p in candidates if sha(p) == expected]
                if not matches:
                    raise RecordError("published native bytes differ from resolved local package: " + basename)
                key = (identity, asset)
                if key not in native:
                    native[key] = dict(name=basename, package=identity, version=metadata["version"],
                                       source=metadata["cache"] + "/" + original.relative_to(metadata["folder"]).as_posix(),
                                       published=[p.relative_to(published).as_posix() for p in matches],
                                       sha256=expected, uses=set())
                native[key]["uses"].add(program)
                known_binaries.add(basename.casefold())
    orphans = [p.relative_to(published).as_posix() for p in published.rglob("*")
               if p.is_file() and p.suffix.lower() in {".dll", ".so", ".dylib"} and p.name.casefold() not in known_binaries]
    for package in packages.values():
        for path, digest in package["receipts"].items():
            evidence[package["cache"] + "/" + path] = digest
    return dict(rid=rid, programs=programs, packages=packages, native=native,
                components=components, excluded=excluded, unparsed=unparsed, references=references, orphans=sorted(orphans),
                evidence=evidence, runtime_receipt=hashlib.sha256(json.dumps(runtime_bytes,sort_keys=True).encode()).hexdigest())


def cell(value) -> str:
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ").replace("<", "&lt;").replace(">", "&gt;")


def table(headers: list[str], rows: list[list]) -> str:
    return "\n".join(["| " + " | ".join(headers) + " |", "|" + "|".join("---" for _ in headers) + "|",
                      *("| " + " | ".join(cell(v) for v in row) + " |" for row in rows)])


def render(record: dict) -> str:
    """Portable deterministic Markdown: no user/cache absolute paths and no compatibility verdict."""
    lines = ["# Built release dependency record", "",
             "Generated by tools/dependency_record.py from every staged .deps.json and local NuGet package files.",
             "This records declarations and binary presence, not legal compatibility or permission to publish.",
             "Cache locations below are relative to a NuGet package-cache root; no machine path is recorded.",
             f"RID: {record['rid']}. Framework-dependent adapters inherit this release RID.",
             f"Matched managed NuGet/runtime binary-set SHA-256: {record['runtime_receipt']}.", "", "## Programs and adapters", "",
             table(["Built entry", "Runtime target", "Runtime package/pack entries", "Manifest SHA-256"],
                   [[p["name"], p["target"], p["packages"], p["sha256"]] for p in record["programs"]]),
             "", "## Direct and transitive runtime packages", "",
             "Direct/transitive describes the dependency from each entry's own project. References through another project are transitive.",
             "Runtime packs are distinguished from ordinary NuGet references. All resolved versions are exact.",
             "", table(["Package", "Exact version", "Kind", "Declared licence", "Local licence file(s)", "Used by"],
                       [[p["id"], p["version"], p["type"],
                         p["licence_kind"] + ": " + p["licence"] + ("; heading: " + p["licence_heading"] if p["licence_heading"] else ""),
                         "; ".join(p["cache"] + "/" + f for f in p["licence_files"]) or "not included in package",
                         "; ".join(n + " (" + k + ")" for n, k in sorted(p["uses"].items()))]
                        for _, p in sorted(record["packages"].items())]),
             "", "## Non-NuGet assembly references", "",
             "These files have reference metadata instead of a package declaration. Their version is the manifest identity; their licence is not guessed.",
             "", table(["Assembly identity / version", "Published files", "Used by", "Declared licence"],
                       [[p["identity"], "; ".join(p["files"]), p["uses"], p["licence"]]
                        for _, p in sorted(record["references"].items())]),
             "", "## Native files actually present", "",
             "The exact version is the containing resolved package/pack version, not an invented upstream component version.",
             "Each published file's bytes were matched to that local package. Its package licence declaration is in the table above; embedded terms are below.",
             "", table(["Native file", "Containing package / version", "Declared package licence", "Package-relative source", "Published location(s)", "Used by", "SHA-256"],
                       [[p["name"], p["package"], record["packages"][p["package"]]["licence_kind"] + ": " + record["packages"][p["package"]]["licence"], p["source"], "; ".join(p["published"]), "; ".join(sorted(p["uses"])), p["sha256"]]
                        for _, p in sorted(record["native"].items())]),
             "", "## Vendor-declared embedded and generated components", "",
             "Versions/SPDX below are verbatim metadata fields from bundled notices. They are declarations, not independently discovered licence conclusions.",
             "A notice may also describe managed bindings, documentation or data; these are not all separate native DLLs.",
             "", table(["Component", "Declared upstream version", "Declared SPDX", "Notice location", "Used by"],
                       [[p["name"], p["version"], p["licence"], p["location"], "; ".join(sorted(p["uses"]))]
                        for _, p in sorted(record["components"].items())]),
             "", "## Broader notice declarations excluded from this RID/package", "",
             "Keeping a broad notice is appropriate; its unrelated sibling/platform entries are not evidence that those components are shipped.",
             "", table(["Declaration", "Version", "Declared SPDX", "Why excluded", "Notice location"],
                       [[p["name"], p["version"], p["licence"], p["status"], p["location"]]
                        for _, p in sorted(record["excluded"].items())]),
             "", "## Additional notices and unattributed binaries", ""]
    for path, digest in sorted(record["unparsed"].items()):
        lines.append(f"- Unstructured notice: {cell(path)}; SHA-256 {digest}. Read it in full; no component-level completeness is claimed for its unstructured contents.")
    for path in record["orphans"]:
        lines.append(f"- UNKNOWN binary origin/licence/version: {cell(path)}. Not declared by the supplied deps manifests.")
    if not record["unparsed"] and not record["orphans"]:
        lines.append("No unparsed notice files or unattributed binary filenames found in this bounded release.")
    lines += ["", "## Evidence digests", "",
              table(["Cache-relative evidence file", "SHA-256"], [[p, h] for p, h in sorted(record["evidence"].items())]),
              "", "## Open for a person", "",
              "- Review compatibility for this exact distribution; no row grants clearance. Read complete licences and notices, including their alternatives, exceptions, attribution and generated-code qualifications.",
              "- Review BRUTAL's custom terms, credits/logo/NOTICE and the public name before publication. This record does not choose a name or contact anybody.",
              "- An expression declaration is not an embedded licence text. Obtain/retain the appropriate full text where required; inspect packages whose local licence file is absent.",
              "- Unstructured aggregate runtime notices and SDK-generated apphosts need human component/attribution review. A runtime-pack version is not a separate upstream version for every embedded component.",
              "- Verify the platform/package exclusions against vendor declarations. This parser does not infer dependency licences from filenames, guess undeclared versions or prove absence of private content.",
              "- Resolve the governing project/source terms of non-NuGet assembly references; UNKNOWN is not an approval.",
              "- Review preview packages and lock/update policy. Test-only packages and source-only borrowed fixtures are outside this shipped-runtime closure; retain their notices in a source distribution."]
    for p in record["packages"].values():
        if p["licence_kind"] == "unknown" or p["missing_declared_file"]:
            lines.append(f"- {cell(p['id'])}/{cell(p['version'])}: " +
                         ("licence UNKNOWN (no declaration)." if p["licence_kind"] == "unknown" else "declared licence file is missing."))
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--published", type=Path, required=True, help="fresh staged release directory")
    parser.add_argument("--cache", type=Path, action="append", help="local NuGet cache root; repeat for fallbacks")
    parser.add_argument("--rid", help="validate/select the release RID; inferred when unique")
    parser.add_argument("--out", type=Path, default=Path("docs/release/DEPENDENCIES.md"))
    parser.add_argument("--check", action="store_true", help="exit 1 if the record is missing or stale; never write")
    args = parser.parse_args(argv)
    try:
        content = render(collect(args.published, args.cache or local_caches(), args.rid))
        if args.check:
            if not args.out.is_file() or args.out.read_bytes() != content.encode("utf-8"):
                print("Dependency record is missing or stale; rebuild the release and regenerate it.", file=sys.stderr)
                return 1
            print("Dependency record matches the built release.")
        else:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_text(content, encoding="utf-8", newline="\n")
            print("Dependency record written.")
        return 0
    except (RecordError, OSError, ValueError, KeyError, ET.ParseError) as exc:
        print("Cannot record dependencies: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
