"""INVENTED read-only source-boundary witnesses, not a shipping certification.

Optional AUTHORING_SEPARATION_CONFIG names an excluded private JSON file with
repo_root, game_root, game_projects, authoring_roots, private_list and
private_data_roots. No private project identity or local path belongs here.
Without configuration the external game check skips; the public census still
runs with a synthetic marker, so it makes no private-client identity claim.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

import pytest


def _settings():
    name = os.environ.get("AUTHORING_SEPARATION_CONFIG")
    if not name:
        return {}
    p = Path(name)
    assert p.is_file() and not p.is_symlink(), "private configuration must be a regular file"
    value = json.loads(p.read_text(encoding="utf-8-sig"))
    assert isinstance(value, dict), "private configuration must be an object"
    return value


def _repo(settings):
    if "repo_root" in settings:
        p = Path(settings["repo_root"]).resolve(strict=True)
        assert (p / "tools/public_files.py").is_file()
        return p
    for p in Path(__file__).resolve().parents:
        if (p / "tools/public_files.py").is_file():
            return p
    raise AssertionError("cannot locate tools/public_files.py; supply repo_root")


def _local(tag):
    return tag.rsplit("}", 1)[-1]


def _linked(p):
    return p.is_symlink() or (hasattr(p, "is_junction") and p.is_junction())


def _under(p, parent):
    return p == parent or parent in p.parents


def _graph(root, projects, forbidden=()):
    """Conservative literal XML graph, including inherited and explicit imports.

    Conditions are deliberately not pruned. Unsupported property/glob/reference
    expressions fail instead of silently disappearing. SDK/package-generated
    targets and evaluated MSBuild item mutations are outside this source proof.
    """
    root = Path(root).resolve(strict=True)
    forbidden = [Path(p).resolve() for p in forbidden]
    inputs, edges, seen = {}, [], set()

    def xml(p):
        assert p.is_file() and not _linked(p), "missing or linked graph input: " + p.name
        data = p.read_bytes()
        inputs[str(p)] = hashlib.sha256(data).hexdigest()
        return ET.fromstring(data)

    def expand(text, owner, project, properties, strict=True):
        text = text.replace("$(MSBuildThisFileDirectory)", str(owner.parent) + os.sep)
        text = text.replace("$(MSBuildProjectDirectory)", str(project.parent))
        for _ in range(len(properties) + 1):
            old = text
            for name, value in properties.items():
                text = text.replace("$(" + name + ")", value)
            if old == text:
                break
        if strict:
            assert not re.search(r"[$@%]\(|[?*]", text), "unresolved reference expression: " + owner.name
        return text.replace("\\", os.sep)

    def inherited(project, name):
        for directory in [project.parent, *project.parent.parents]:
            p = directory / name
            if p.is_file():
                return p
        return None

    def items(p, project, visited, properties):
        p = p.resolve(strict=True)
        if p in visited:
            return []
        visited.add(p)
        tree = xml(p)
        refs = []
        for item in tree.iter():
            kind = _local(item.tag)
            if kind == "PropertyGroup":
                for prop in item:
                    value = (prop.text or "").strip()
                    if item.get("Condition") or prop.get("Condition"):
                        value = "$(unsupported_conditional_property)"
                    properties[_local(prop.tag)] = expand(value, p, project, properties, strict=False)
            if kind in {"AssemblyName", "RootNamespace"}:
                assert "authoring" not in expand(item.text or "", p, project, properties).casefold(), "authoring assembly identity: " + project.name
            if kind == "ProjectReference":
                assert "Update" not in item.attrib and "Remove" not in item.attrib, "evaluated reference mutation needs review"
                include = item.get("Include")
                assert include and include.strip(), "empty project reference"
                for part in include.split(";"):
                    target = (project.parent / expand(part.strip(), p, project, properties)).resolve()
                    refs.append(target)
            elif kind == "Import":
                name = item.get("Project")
                assert name, "empty import"
                target = (p.parent / expand(name, p, project, properties)).resolve(strict=True)
                refs.extend(items(target, project, visited, properties))
        return refs

    def visit(p):
        p = p.resolve(strict=True)
        relative = p.relative_to(root) if _under(p, root) else Path(p.name)
        assert not any(_under(p, f) for f in forbidden), "authoring project reached: " + relative.as_posix()
        assert "authoring" not in p.stem.casefold() and "authoring" not in [x.casefold() for x in relative.parts[:-1]], "authoring project reached: " + relative.as_posix()
        if p in seen:
            return
        seen.add(p)
        visited, properties, refs = set(), {}, []
        props = inherited(p, "Directory.Build.props")
        if props:
            refs.extend(items(props, p, visited, properties))
        refs.extend(items(p, p, visited, properties))
        targets = inherited(p, "Directory.Build.targets")
        if targets:
            refs.extend(items(targets, p, visited, properties))
        for target in sorted(set(refs)):
            edges.append([str(p), str(target)])
            visit(target)

    assert projects, "nonempty configured runtime roots required"
    for rel in projects:
        visit(root / rel)
    return {"roots": list(projects), "projects": sorted(map(str, seen)), "edges": edges, "inputs": inputs}


def _canonical(data):
    try:
        value = json.loads(data.decode("utf-8-sig"))
    except (UnicodeError, ValueError):
        return None
    if not isinstance(value, (dict, list)) or not value:
        return None
    return hashlib.sha256(json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def _private_fingerprints(roots):
    exact, canonical, inputs = set(), set(), {}
    for root in roots:
        p = Path(root).resolve(strict=True)
        assert p.is_dir() and not _linked(p), "private data root must be a regular directory"
        for f in sorted(p.rglob("*.json")):
            assert not any(_linked(x) for x in [f, *f.parents]), "linked private input needs review"
            data = f.read_bytes()
            digest = hashlib.sha256(data).hexdigest()
            inputs[str(f)] = digest
            exact.add(digest)
            c = _canonical(data)
            if c:
                canonical.add(c)
    return exact, canonical, inputs


def _assert_public(root, report, markers, data_roots=()):
    assert report["schema"] == 1 and isinstance(report["files"], list)
    rows = [r for r in report["files"] if r["classification"] == "public"]
    assert rows and len(rows) == report["counts"]["public"], "empty/inconsistent public set"
    assert len({r["path"] for r in report["files"]}) == len(report["files"]), "duplicate census path"
    assert markers and all(isinstance(m, str) and m.strip() for m in markers)
    exact, canonical, private_inputs = _private_fingerprints(data_roots)
    checked = []
    for row in rows:
        rel = Path(row["path"])
        assert not rel.is_absolute() and ".." not in rel.parts, "unsafe census path"
        p = root / rel
        assert not any(_linked(x) for x in [p, *p.parents]), "linked public input"
        assert _under(p.resolve(strict=True), root), "public path escaped root"
        data = p.read_bytes()
        assert row["declared"] == "public" and not row["issues"] and not row["check_failure"], "public row retains findings"
        digest = hashlib.sha256(data).hexdigest()
        assert row["sha256"] == digest and row["bytes"] == len(data), "census/source bytes differ"
        content = data.decode("utf-8-sig", errors="replace").casefold()
        assert not any(m.casefold() in rel.as_posix().casefold() or m.casefold() in content for m in markers), "private marker in public path/content: " + rel.as_posix()
        assert digest not in exact and (_canonical(data) is None or _canonical(data) not in canonical), "private whole-document copy: " + rel.as_posix()
        checked.append({"path": rel.as_posix(), "sha256": digest, "bytes": len(data)})
    return {"public": checked, "private_data_inputs": private_inputs, "counts": report["counts"], "check_failures": report["check_failures"]}


def _census(settings, tmp_path):
    root = _repo(settings)
    configured = settings.get("private_list")
    if configured:
        private_list = Path(configured).resolve(strict=True)
    else:
        private_list = tmp_path / "synthetic-identifiers.json"
        private_list.write_text(json.dumps({"private_markers": ["VeiledWorkshopBoundary"]}), encoding="utf-8")
    markers = json.loads(private_list.read_text(encoding="utf-8-sig"))["private_markers"]
    result = subprocess.run([sys.executable, str(root / "tools/public_files.py"), str(root), "--private-list", str(private_list)], capture_output=True, text=True, encoding="utf-8", timeout=120, env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
    assert result.returncode == 0, "public census failed: " + str(result.returncode)
    report = json.loads(result.stdout)
    return report, _assert_public(root, report, markers, settings.get("private_data_roots", []))


def test_configured_runtime_project_graph_has_no_authoring_dependency():
    settings = _settings()
    name = settings.get("game_root")
    if not name or not Path(name).exists():
        pytest.skip("external game tree absent; supply game_root in AUTHORING_SEPARATION_CONFIG for the read-only source graph")
    _graph(Path(name), settings.get("game_projects", []), settings.get("authoring_roots", []))


def test_proposed_public_source_set_has_no_configured_private_name_path_or_whole_data(tmp_path):
    _census(_settings(), tmp_path)


def _put(root, name, text):
    p = root / name
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")
    return p


def test_graph_accepts_neutral_transitive_cycle_without_building(tmp_path):
    _put(tmp_path, "Client.csproj", '<Project><ItemGroup><ProjectReference Include="Bridge.csproj" /></ItemGroup></Project>')
    _put(tmp_path, "Bridge.csproj", '<Project><ItemGroup><ProjectReference Include="Client.csproj" /></ItemGroup></Project>')
    assert len(_graph(tmp_path, ["Client.csproj"])["projects"]) == 2


@pytest.mark.parametrize("route", ["direct", "transitive", "props", "targets", "import", "semicolon", "identity"])
def test_graph_controls_refuse_authoring_dependency(tmp_path, route):
    _put(tmp_path, "Workspace.Authoring.csproj", "<Project />")
    ref = '<ItemGroup><ProjectReference Include="Workspace.Authoring.csproj" /></ItemGroup>'
    if route == "identity":
        text = '<PropertyGroup><AssemblyName>Workspace.Authoring</AssemblyName></PropertyGroup>'
    elif route == "transitive":
        _put(tmp_path, "Bridge.csproj", "<Project>" + ref + "</Project>")
        text = '<ItemGroup><ProjectReference Include="Bridge.csproj" /></ItemGroup>'
    elif route in {"props", "targets"}:
        _put(tmp_path, "Directory.Build." + route, "<Project>" + ref + "</Project>")
        text = ""
    elif route == "import":
        _put(tmp_path, "extra.targets", "<Project>" + ref + "</Project>")
        text = '<Import Project="extra.targets" />'
    elif route == "semicolon":
        _put(tmp_path, "Bridge.csproj", "<Project />")
        text = '<ItemGroup><ProjectReference Include="Bridge.csproj;Workspace.Authoring.csproj" /></ItemGroup>'
    else:
        text = ref
    _put(tmp_path, "Client.csproj", "<Project>" + text + "</Project>")
    with pytest.raises(AssertionError, match="authoring"):
        _graph(tmp_path, ["Client.csproj"])


@pytest.mark.parametrize("include", ["Absent.csproj", "$(Unresolved)/Bridge.csproj", "*.csproj", ""])
def test_graph_controls_fail_closed_on_unresolved_reference(tmp_path, include):
    _put(tmp_path, "Client.csproj", '<Project><ItemGroup><ProjectReference Include="' + include + '" /></ItemGroup></Project>')
    with pytest.raises((AssertionError, FileNotFoundError)):
        _graph(tmp_path, ["Client.csproj"])


def _public_row(p, rel):
    data = p.read_bytes()
    return {"path": rel, "classification": "public", "declared": "public", "issues": [], "check_failure": False, "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}


def _report(row):
    return {"schema": 1, "files": [row], "counts": {"public": 1, "private": 0, "needs-change": 0}, "check_failures": 0}


@pytest.mark.parametrize("where", ["name", "content", "data", "reformatted-data", "hash", "issue", "escape", "empty"])
def test_independent_public_controls_catch_an_incorrectly_approved_row(tmp_path, where):
    root = tmp_path / "public"
    name = "src/VeiledWorkshop.cs" if where == "name" else "src/Neutral.cs"
    content = "VEILEDWORKSHOP" if where == "content" else '{"original_fixture":[1,2,3]}'
    p = _put(root, name, content)
    row = _public_row(p, name)
    roots = []
    if where in {"data", "reformatted-data"}:
        private = tmp_path / "private-data"
        _put(private, "original.json", content if where == "data" else '{ "original_fixture": [1, 2, 3] }')
        roots.append(private)
    elif where == "hash":
        row["sha256"] = "0" * 64
    elif where == "issue":
        row["issues"] = [{"kind": "machine-path"}]
    elif where == "escape":
        row["path"] = "../private.txt"
    report = _report(row)
    if where == "empty":
        report["files"] = []
        report["counts"]["public"] = 0
    with pytest.raises(AssertionError):
        _assert_public(root.resolve(), report, ["VeiledWorkshop"], roots)


def test_inherited_literal_property_and_import_relative_reference_are_resolved(tmp_path):
    _put(tmp_path, "Directory.Build.props", '<Project><PropertyGroup><TreeRoot>$(MSBuildThisFileDirectory)</TreeRoot></PropertyGroup></Project>')
    _put(tmp_path, "project/Client.csproj", '<Project><Import Project="../imports/dependencies.targets" /></Project>')
    _put(tmp_path, "imports/dependencies.targets", '<Project><ItemGroup><ProjectReference Include="$(TreeRoot)Bridge.csproj" /><ProjectReference Include="Local.csproj" /></ItemGroup></Project>')
    _put(tmp_path, "Bridge.csproj", '<Project />')
    _put(tmp_path, "project/Local.csproj", '<Project />')
    assert len(_graph(tmp_path, ["project/Client.csproj"])["projects"]) == 3


def test_indirect_assembly_identity_cannot_hide_an_authoring_project(tmp_path):
    _put(tmp_path, "Client.csproj", '<Project><PropertyGroup><Hidden>Workspace.Authoring</Hidden><AssemblyName>$(Hidden)</AssemblyName></PropertyGroup></Project>')
    with pytest.raises(AssertionError, match="authoring assembly"):
        _graph(tmp_path, ["Client.csproj"])


def test_absent_external_tree_has_an_explicit_skip_reason(tmp_path, monkeypatch):
    config = _put(tmp_path, "private.json", json.dumps({"game_root": str(tmp_path / "absent")}))
    monkeypatch.setenv("AUTHORING_SEPARATION_CONFIG", str(config))
    with pytest.raises(pytest.skip.Exception, match="external game tree absent"):
        test_configured_runtime_project_graph_has_no_authoring_dependency()
