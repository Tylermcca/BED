"""INVENTED metadata, bytes and licences; no downloaded/private fixture or live build is used."""
import copy
import importlib.util
import json
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

import pytest

spec = importlib.util.spec_from_file_location("dependency_record_under_test", Path(__file__).with_name("dependency_record.py"))
record = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = record
spec.loader.exec_module(record)


def write_package(cache, name, version, licence="MIT", kind="expression", *, declared=True, file=True):
    root = cache / name.lower() / version.lower()
    root.mkdir(parents=True, exist_ok=True)
    xml = ET.Element("package")
    meta = ET.SubElement(xml, "metadata")
    ET.SubElement(meta, "id").text = name
    ET.SubElement(meta, "version").text = version
    if declared:
        ET.SubElement(meta, "license", type=kind).text = licence
    ET.SubElement(meta, "licenseUrl").text = "https://invalid.example/licence-is-not-an-inference"
    (root / (name.lower()+".nuspec")).write_bytes(ET.tostring(xml))
    if kind == "file" and file and ".." not in licence:
        p = root / licence
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text("INVENTED fixture terms\nNot a legal licence fixture.\n")
    return root


def fixture(tmp_path):
    cache = tmp_path / "cache"
    stage = tmp_path / "stage"
    stage.mkdir(parents=True)
    entries = {}
    libraries = {}
    for name, version in [("Copper.Native", "2.3.4"), ("Copper.Helpers", "1.2.0")]:
        folder = write_package(cache, name, version)
        asset = f"lib/net10.0/{name}.dll"
        p = folder / asset
        p.parent.mkdir(parents=True)
        p.write_bytes(("invented managed "+name).encode())
        (stage / p.name).write_bytes(p.read_bytes())
        entries[name+"/"+version] = {"runtime": {asset: {}}}
        libraries[name+"/"+version] = {"type": "package", "path": name.lower()+"/"+version, "sha512": "synthetic"}
    entries["Copper.Native/2.3.4"]["dependencies"] = {"Copper.Helpers": "1.2.0"}
    native = "runtimes/win-x64/native/copper.dll"
    p = cache / "copper.native/2.3.4" / native
    p.parent.mkdir(parents=True)
    p.write_bytes(b"invented native bytes; never execute")
    (stage / "copper.dll").write_bytes(p.read_bytes())
    entries["Copper.Native/2.3.4"]["native"] = {native: {}}
    entries["demo/1.0.0"] = {"dependencies": {"Copper.Native": "2.3.4"}, "runtime": {"demo.dll": {}}}
    libraries["demo/1.0.0"] = {"type": "project"}
    (stage / "demo.exe").write_bytes(b"invented apphost")
    (stage / "demo.dll").write_bytes(b"invented project assembly")
    doc = {"runtimeTarget": {"name": "net10.0/win-x64"}, "targets": {"net10.0/win-x64": entries}, "libraries": libraries}
    (stage / "demo.deps.json").write_text(json.dumps(doc))
    return stage, cache, doc


def rewrite(stage, doc):
    (stage / "demo.deps.json").write_text(json.dumps(doc))


def test_direct_transitive_and_exact_versions(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    r = record.collect(stage, [cache])
    assert r["packages"]["Copper.Native/2.3.4"]["uses"] == {"demo": "direct"}
    assert r["packages"]["Copper.Helpers/1.2.0"]["uses"] == {"demo": "transitive"}
    assert r["packages"]["Copper.Native/2.3.4"]["version"] == "2.3.4"


def test_every_staged_manifest_is_included_without_project_name_assumptions(tmp_path):
    stage, cache, doc = fixture(tmp_path)
    adapter = stage / "adapters" / "widget"
    adapter.mkdir(parents=True)
    (adapter / "WidgetAdapter.deps.json").write_text(json.dumps(doc))
    result = record.collect(stage, [cache])
    assert {p["name"] for p in result["programs"]} == {"demo", "adapters/widget/WidgetAdapter"}
    assert "adapters/widget/WidgetAdapter" in result["packages"]["Copper.Helpers/1.2.0"]["uses"]


def test_expression_and_local_licence_file_are_distinct(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    write_package(cache, "Copper.Native", "2.3.4", "LICENSE.txt", "file")
    r = record.collect(stage, [cache])["packages"]["Copper.Native/2.3.4"]
    assert r["licence_kind"] == "file" and r["licence"] == "LICENSE.txt"
    assert r["licence_heading"] == "INVENTED fixture terms"
    assert r["licence_files"] == ["LICENSE.txt"]


def test_no_licence_declaration_is_unknown_even_with_url_and_licence_named_file(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    root = write_package(cache, "Copper.Native", "2.3.4", declared=False)
    (root / "LICENSE.txt").write_text("MIT is mentioned in this invented untrusted text")
    r = record.collect(stage, [cache])
    assert r["packages"]["Copper.Native/2.3.4"]["licence_kind"] == "unknown"
    assert "UNKNOWN (no licence declaration)" in record.render(r)


def test_declared_missing_licence_file_is_reported_for_a_person(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    write_package(cache, "Copper.Native", "2.3.4", "ABSENT.txt", "file", file=False)
    r = record.collect(stage, [cache])
    assert r["packages"]["Copper.Native/2.3.4"]["missing_declared_file"]
    assert "declared licence file is missing" in record.render(r)


def test_cache_fallback_is_read_only(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    before = {str(p.relative_to(cache)): p.read_bytes() for p in cache.rglob("*") if p.is_file()}
    empty = tmp_path / "empty"
    empty.mkdir()
    assert record.collect(stage, [empty, cache])["packages"]
    assert before == {str(p.relative_to(cache)): p.read_bytes() for p in cache.rglob("*") if p.is_file()}


def test_missing_cache_package_is_an_error_instead_of_guessed_metadata(tmp_path):
    stage, _, _ = fixture(tmp_path)
    with pytest.raises(record.RecordError, match="local package unavailable"):
        record.collect(stage, [tmp_path / "absent"])


def test_package_path_cannot_escape_cache(tmp_path):
    stage, cache, d = fixture(tmp_path)
    d["libraries"]["Copper.Native/2.3.4"]["path"] = "../elsewhere"
    rewrite(stage, d)
    with pytest.raises(record.RecordError, match="unsafe"):
        record.collect(stage, [cache])


def test_declared_licence_file_cannot_escape_package(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    write_package(cache, "Copper.Native", "2.3.4", "../private.txt", "file")
    with pytest.raises(record.RecordError, match="unsafe"):
        record.collect(stage, [cache])


def test_nuspec_identity_must_match_built_identity(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    p = cache / "copper.native/2.3.4/copper.native.nuspec"
    p.write_text(p.read_text().replace("2.3.4", "9.0.0"))
    with pytest.raises(record.RecordError, match="identity does not match"):
        record.collect(stage, [cache])


def test_native_file_is_matched_to_package_bytes_and_program(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    r = record.collect(stage, [cache])
    item = next(iter(r["native"].values()))
    assert item["name"] == "copper.dll" and item["version"] == "2.3.4"
    assert item["uses"] == {"demo"} and item["published"] == ["copper.dll"]


@pytest.mark.parametrize("mode", ["missing", "changed"])
def test_native_presence_and_identity_are_not_assumed_from_deps(tmp_path, mode):
    stage, cache, _ = fixture(tmp_path)
    p = stage / "copper.dll"
    if mode == "missing":
        p.unlink()
    else:
        p.write_bytes(b"other binary")
    with pytest.raises(record.RecordError, match="native"):
        record.collect(stage, [cache])


@pytest.mark.parametrize("mode", ["missing", "changed"])
def test_managed_presence_and_identity_are_verified(tmp_path, mode):
    stage, cache, _ = fixture(tmp_path)
    p = stage / "Copper.Helpers.dll"
    if mode == "missing":
        p.unlink()
    else:
        p.write_bytes(b"other managed binary")
    with pytest.raises(record.RecordError, match="managed"):
        record.collect(stage, [cache])


def test_rid_mismatch_fails_clearly(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    with pytest.raises(record.RecordError, match="identifiers disagree"):
        record.collect(stage, [cache], "linux-x64")


def test_framework_dependent_only_requires_an_explicit_rid(tmp_path):
    stage, cache, d = fixture(tmp_path)
    d["runtimeTarget"]["name"] = "net10.0"
    d["targets"]["net10.0"] = d["targets"].pop("net10.0/win-x64")
    rewrite(stage, d)
    with pytest.raises(record.RecordError, match="specify --rid"):
        record.collect(stage, [cache])
    assert record.collect(stage, [cache], "win-x64")["rid"] == "win-x64"


def test_empty_release_is_not_an_empty_success(tmp_path):
    with pytest.raises(record.RecordError, match="no published"):
        record.collect(tmp_path, [])


def test_an_executable_without_manifest_is_not_silently_ignored(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    (stage / "extra.exe").write_bytes(b"unattributed app")
    with pytest.raises(record.RecordError, match="no dependency manifest"):
        record.collect(stage, [cache])


def test_unknown_library_type_is_not_silently_ignored(tmp_path):
    stage, cache, d = fixture(tmp_path)
    d["libraries"]["Copper.Native/2.3.4"]["type"] = "mystery"
    rewrite(stage, d)
    with pytest.raises(record.RecordError, match="unsupported library"):
        record.collect(stage, [cache])


def test_check_is_read_only_and_exits_one_for_stale_or_missing_output(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    out = tmp_path / "record.md"
    args = ["--published", str(stage), "--cache", str(cache), "--out", str(out)]
    assert record.main(args+["--check"]) == 1 and not out.exists()
    assert record.main(args) == 0
    before = out.read_bytes()
    assert record.main(args+["--check"]) == 0 and out.read_bytes() == before
    out.write_text("stale")
    assert record.main(args+["--check"]) == 1 and out.read_text() == "stale"


def test_metadata_changes_make_the_record_stale(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    out = tmp_path / "record.md"
    args = ["--published", str(stage), "--cache", str(cache), "--out", str(out)]
    assert record.main(args) == 0
    write_package(cache, "Copper.Helpers", "1.2.0", "Apache-2.0")
    assert record.main(args+["--check"]) == 1


def test_record_is_portable_and_deterministic_across_machine_paths(tmp_path):
    a, ac, _ = fixture(tmp_path / "a")
    b, bc, _ = fixture(tmp_path / "different-location")
    left, right = record.render(record.collect(a, [ac])), record.render(record.collect(b, [bc]))
    assert left == right
    assert str(tmp_path) not in left


def test_notice_metadata_continuations_and_individual_stb_versions(tmp_path):
    p = tmp_path / "notice.txt"
    p.write_text("  Name        stb_rect_pack, stb_textedit, stb_truetype\n  Version     1.01, 1.14, 1.26 respectively\n  SPDX        MIT OR Unlicense\n\n")
    parts = record.notice_sections(p)
    assert [(r["name"], r["version"]) for r in parts] == [("stb_rect_pack", "1.01"), ("stb_textedit", "1.14"), ("stb_truetype", "1.26")]
    assert all(r["licence"] == "MIT OR Unlicense" for r in parts)


def test_sibling_and_platform_notices_do_not_invent_shipped_components(tmp_path):
    assert record.applicability("Ahwoo.BRUTAL.ImGui", {"name": "ImPlot", "shipped": ""}, "win-x64").startswith("not selected")
    assert record.applicability("Ahwoo.BRUTAL.ImGui.Backends", {"name": "stb_truetype", "shipped": ""}, "win-x64").startswith("not selected")
    assert record.applicability("Ahwoo.BRUTAL.Glfw", {"name": "Wayland", "shipped": "libglfw.so (linux-x64) only"}, "win-x64").startswith("not selected")


def test_runtime_pack_is_distinguished_and_keeps_its_exact_version(tmp_path):
    stage, cache, d = fixture(tmp_path)
    write_package(cache, "Microsoft.NETCore.App.Runtime.win-x64", "10.0.13")
    key = "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.13"
    d["libraries"][key] = {"type": "runtimepack"}
    d["targets"]["net10.0/win-x64"][key] = {}
    rewrite(stage, d)
    r = record.collect(stage, [cache])["packages"][key]
    assert r["type"] == "runtimepack" and r["version"] == "10.0.13"
    assert r["id"] == "Microsoft.NETCore.App.Runtime.win-x64"


def test_unstructured_notices_and_unattributed_dlls_remain_visible(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    folder = cache / "copper.native/2.3.4"
    (folder / "THIRD-PARTY-NOTICES.txt").write_text("Unstructured invented notice; review rather than infer.")
    (stage / "orphan.dll").write_bytes(b"unattributed")
    text = record.render(record.collect(stage, [cache]))
    assert "Unstructured notice:" in text and "UNKNOWN binary origin/licence/version: orphan.dll" in text


def test_markdown_metadata_is_escaped_and_open_section_is_last(tmp_path):
    stage, cache, _ = fixture(tmp_path)
    write_package(cache, "Copper.Helpers", "1.2.0", "Custom|<terms>\nline")
    text = record.render(record.collect(stage, [cache]))
    assert "Custom\\|&lt;terms&gt; line" in text
    assert text.rfind("\n## ") == text.index("\n## Open for a person")


def test_cli_returns_two_for_bad_inputs_without_writing_output(tmp_path):
    out = tmp_path / "output.md"
    assert record.main(["--published", str(tmp_path), "--out", str(out)]) == 2
    assert not out.exists()

def test_declared_native_helper_exe_is_verified_as_a_component_not_a_missing_program(tmp_path):
    stage, cache, d = fixture(tmp_path)
    asset = "runtimes/win-x64/native/dump-helper.exe"
    p = cache / "copper.native/2.3.4" / asset
    p.write_bytes(b"invented helper")
    (stage / "dump-helper.exe").write_bytes(p.read_bytes())
    d["targets"]["net10.0/win-x64"]["Copper.Native/2.3.4"]["native"][asset] = {}
    rewrite(stage, d)
    r = record.collect(stage, [cache])
    assert any(v["name"] == "dump-helper.exe" for v in r["native"].values())

def test_non_nuget_reference_is_recorded_with_unknown_terms_instead_of_dropped(tmp_path):
    stage, cache, d = fixture(tmp_path)
    key = "Local.Assembly.Reference/1.0.0.0"
    d["libraries"][key] = {"type": "reference"}
    d["targets"]["net10.0/win-x64"][key] = {"runtime": {"local.dll": {}}}
    (stage / "local.dll").write_bytes(b"invented local reference")
    rewrite(stage, d)
    text = record.render(record.collect(stage, [cache]))
    assert key in text and "UNKNOWN (non-NuGet assembly reference" in text

def test_unrelated_program_folder_is_not_a_runtime_search_path(tmp_path):
    stage, cache, doc = fixture(tmp_path)
    other = stage / "other-program"
    other.mkdir()
    (other / "other.deps.json").write_text(json.dumps(doc))
    with pytest.raises(record.RecordError, match="managed file absent"):
        record.collect(stage, [cache])
