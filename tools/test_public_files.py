"""INVENTED fixture trees only; no real private identifiers or machine records."""
import importlib.util
import json
from pathlib import Path
import sys

import pytest

spec = importlib.util.spec_from_file_location("public_files_under_test", Path(__file__).with_name("public_files.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
MARKER = "VeiledWorkshop"  # invented test identifier, not a repository client


def put(root, name, content="original neutral source"):
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content if isinstance(content, bytes) else content.encode("utf-8"))
    return path


def row(report, name):
    return next(r for r in report["files"] if r["path"] == name)


def policy(tmp_path, markers=None):
    path = tmp_path / "reviewed-identifiers.json"
    path.write_text(json.dumps({"private_markers": markers or [MARKER]}), encoding="utf-8")
    return path


def test_known_public_code_has_reason_and_no_check_failure(tmp_path):
    put(tmp_path, "src/Neutral.cs")
    report = module.classify(tmp_path, [MARKER])
    result = row(report, "src/Neutral.cs")
    assert result["classification"] == "public"
    assert result["rule"] == "src/**" and result["reason"]
    assert report["check_failures"] == 0


def test_unknown_file_is_not_silently_approved(tmp_path):
    put(tmp_path, "unreviewed.extension")
    result = row(module.classify(tmp_path, [MARKER]), "unreviewed.extension")
    assert result["classification"] == "needs-change"
    assert result["rule"] is None and result["check_failure"]
    assert any(i["kind"] == "no-rule" for i in result["issues"])


def test_private_process_content_is_not_opened(tmp_path, monkeypatch):
    target = put(tmp_path, "handoff/note.md", MARKER)
    original = Path.read_bytes

    def read(path):
        assert path != target, "private process content should not be inspected"
        return original(path)

    monkeypatch.setattr(Path, "read_bytes", read)
    result = row(module.classify(tmp_path, [MARKER]), "handoff/note.md")
    assert result["classification"] == "private"
    assert not result["check_failure"]


def test_private_marker_is_case_insensitive_and_values_are_not_echoed(tmp_path):
    put(tmp_path, "src/Neutral.cs", "line one\n" + MARKER.upper() + "\n")
    report = module.classify(tmp_path, [MARKER])
    result = row(report, "src/Neutral.cs")
    assert result["classification"] == "needs-change" and result["declared"] == "public"
    finding = next(i for i in result["issues"] if i["kind"] == "private-marker")
    assert finding["lines"] == [2]
    assert MARKER.casefold() not in json.dumps(report).casefold()


def test_private_marker_in_a_filename_is_detected(tmp_path):
    name = "src/" + MARKER + ".cs"
    put(tmp_path, name)
    finding = row(module.classify(tmp_path, [MARKER]), name)
    assert finding["check_failure"]
    assert any(i["location"] == "path" and i["kind"] == "private-marker" for i in finding["issues"])


def test_marker_metacharacters_are_literal(tmp_path):
    put(tmp_path, "src/Neutral.cs", "VeiledXWorkshop")
    assert module.classify(tmp_path, ["Veiled.Workshop"])["check_failures"] == 0
    put(tmp_path, "src/Neutral.cs", "Veiled.Workshop")
    assert module.classify(tmp_path, ["Veiled.Workshop"])["check_failures"] == 1


def test_drive_path_is_detected_without_echoing_it(tmp_path):
    machine = "R" + ":" + chr(92) + "operator" + chr(92) + "private"
    put(tmp_path, "docs/guide/example.md", machine)
    report = module.classify(tmp_path, [MARKER])
    assert any(i["kind"] == "machine-path" for i in row(report, "docs/guide/example.md")["issues"])
    assert machine not in json.dumps(report)


def test_profile_path_and_network_path_are_detected(tmp_path):
    profile = "/" + "home" + "/operator/work"
    network = chr(92) * 2 + "workstation" + chr(92) + "share"
    put(tmp_path, "src/Neutral.cs", profile + "\n" + network)
    issues = row(module.classify(tmp_path, [MARKER]), "src/Neutral.cs")["issues"]
    assert next(i for i in issues if i["kind"] == "machine-path")["lines"] == [1, 2]


def test_email_is_detected_without_disclosing_the_value(tmp_path):
    address = "@".join(["account", "example.invalid"])
    put(tmp_path, "docs/guide/contact.md", address)
    report = module.classify(tmp_path, [MARKER])
    assert any(i["kind"] == "email" for i in row(report, "docs/guide/contact.md")["issues"])
    assert address not in json.dumps(report)


def test_known_needs_change_is_withheld_not_mistaken_for_public(tmp_path):
    put(tmp_path, "README.md", MARKER)
    report = module.classify(tmp_path, [MARKER])
    result = row(report, "README.md")
    assert result["classification"] == "needs-change" and result["declared"] == "needs-change"
    assert result["issues"]
    assert report["check_failures"] == 0  # zero is not release clearance


def test_pruned_metadata_and_build_trees_are_never_read(tmp_path, monkeypatch):
    for name in [".git/private", "codex/outbox/private", "src/Module/obj/private", "artifacts/private"]:
        put(tmp_path, name, MARKER)
    put(tmp_path, "src/Neutral.cs")
    original = Path.read_bytes

    def read(path):
        assert path.name != "private"
        return original(path)

    monkeypatch.setattr(Path, "read_bytes", read)
    report = module.classify(tmp_path, [MARKER])
    assert [r["path"] for r in report["files"]] == ["src/Neutral.cs"]
    assert len(report["excluded_trees"]) == 4


def test_internal_tool_rule_precedes_generic_tools_rule(tmp_path):
    put(tmp_path, "tools/check_handoff_move.py", MARKER)
    result = row(module.classify(tmp_path, [MARKER]), "tools/check_handoff_move.py")
    assert result["classification"] == "private" and not result["check_failure"]


def test_candidate_paths_are_sorted_and_all_are_classified(tmp_path):
    for name in ["src/Z.cs", "src/A.cs", "LICENSE.txt", "handoff/note.md", "unknown"]:
        put(tmp_path, name)
    report = module.classify(tmp_path, [MARKER])
    paths = [r["path"] for r in report["files"]]
    assert paths == sorted(paths)
    assert report["candidate_files"] == 5
    assert sum(report["counts"].values()) == 5
    assert all(r["classification"] in {"public", "private", "needs-change"} for r in report["files"])


def test_scan_is_read_only_and_deterministic(tmp_path):
    target = put(tmp_path, "src/Neutral.cs")
    before = (target.read_bytes(), target.stat().st_mtime_ns)
    first = module.classify(tmp_path, [MARKER])
    second = module.classify(tmp_path, [MARKER])
    assert first == second
    assert (target.read_bytes(), target.stat().st_mtime_ns) == before
    assert sorted(p.relative_to(tmp_path).as_posix() for p in tmp_path.rglob("*") if p.is_file()) == ["src/Neutral.cs"]


def test_simulated_link_is_never_followed(tmp_path, monkeypatch):
    target = put(tmp_path, "src/link.cs", "not scanned")
    original = Path.read_bytes
    monkeypatch.setattr(module, "_link", lambda path: path == target)

    def read(path):
        assert path != target, "a classified link must never be read"
        return original(path)

    monkeypatch.setattr(Path, "read_bytes", read)
    result = row(module.classify(tmp_path, [MARKER]), "src/link.cs")
    assert result["classification"] == "needs-change" and result["check_failure"]
    assert result["issues"][0]["kind"] == "link"


def test_unrecognized_binary_is_not_public_by_folder_alone(tmp_path):
    put(tmp_path, "src/unreviewed.bin", b"\0\1\2")
    result = row(module.classify(tmp_path, [MARKER]), "src/unreviewed.bin")
    assert result["classification"] == "needs-change" and result["check_failure"]
    assert any(i["kind"] == "binary-review" for i in result["issues"])


def test_read_error_is_reported_without_exception_text(tmp_path, monkeypatch):
    target = put(tmp_path, "src/Neutral.cs")
    original = Path.read_bytes

    def read(path):
        if path == target:
            raise PermissionError(MARKER)
        return original(path)

    monkeypatch.setattr(Path, "read_bytes", read)
    report = module.classify(tmp_path, [MARKER])
    assert row(report, "src/Neutral.cs")["check_failure"]
    assert MARKER not in json.dumps(report)


def test_private_list_can_live_outside_the_candidate_tree(tmp_path):
    root = tmp_path / "tree"
    root.mkdir()
    configured = policy(tmp_path)
    assert module.read_private_list(root, configured) == [MARKER]


def test_private_list_can_live_in_an_excluded_private_area(tmp_path):
    configured = put(tmp_path, "codex/private-identifiers.json", json.dumps({"private_markers": [MARKER]}))
    assert module.read_private_list(tmp_path, configured) == [MARKER]


def test_private_list_cannot_be_a_public_source_file(tmp_path):
    configured = put(tmp_path, "src/identifiers.json", json.dumps({"private_markers": [MARKER]}))
    with pytest.raises(ValueError, match="outside"):
        module.read_private_list(tmp_path, configured)


@pytest.mark.parametrize("value", [
    {}, {"private_markers": []}, {"private_markers": [" "]}, {"private_markers": [4]},
    {"private_markers": ["Example", "example"]}, {"private_markers": [" trailing "]},
    {"private_markers": [MARKER], "typo": True},
])
def test_invalid_private_policy_fails_closed(tmp_path, value):
    configured = tmp_path / "config.json"
    configured.write_text(json.dumps(value), encoding="utf-8")
    with pytest.raises(ValueError):
        module.read_private_list(tmp_path / "tree", configured)


def test_cli_check_exits_one_for_public_leak(tmp_path, capsys):
    root = tmp_path / "tree"
    put(root, "src/Neutral.cs", MARKER)
    configured = policy(tmp_path)
    assert module.main([str(root), "--private-list", str(configured), "--check"]) == 1
    captured = capsys.readouterr()
    assert MARKER not in captured.out and MARKER not in captured.err
    assert json.loads(captured.out)["check_failures"] == 1


def test_cli_check_exits_one_for_no_rule(tmp_path, capsys):
    root = tmp_path / "tree"
    put(root, "unclassified.file")
    assert module.main([str(root), "--private-list", str(policy(tmp_path)), "--check"]) == 1
    assert json.loads(capsys.readouterr().out)["files"][0]["rule"] is None


def test_cli_success_reports_original_file_and_never_writes(tmp_path, capsys):
    root = tmp_path / "tree"
    target = put(root, "src/Neutral.cs")
    before = target.read_bytes()
    assert module.main([str(root), "--private-list", str(policy(tmp_path)), "--check"]) == 0
    assert json.loads(capsys.readouterr().out)["counts"]["public"] == 1
    assert target.read_bytes() == before


def test_cli_bad_config_is_two_and_never_echoes_private_content(tmp_path, capsys):
    root = tmp_path / "tree"
    root.mkdir()
    configured = tmp_path / "bad.json"
    configured.write_text(MARKER, encoding="utf-8")
    assert module.main([str(root), "--private-list", str(configured), "--check"]) == 2
    captured = capsys.readouterr()
    assert MARKER not in captured.out + captured.err
    assert json.loads(captured.err)["error"]


def test_report_has_no_absolute_root_or_configuration_contents(tmp_path):
    put(tmp_path, "src/Neutral.cs")
    report = json.dumps(module.classify(tmp_path, [MARKER]))
    assert str(tmp_path) not in report
    assert "private_markers" not in report


def test_web_schema_urls_are_not_mistaken_for_drive_paths(tmp_path):
    put(tmp_path, "docs/spec/example.json", "https://example.invalid/schema\nhttp://example.invalid/ref")
    report = module.classify(tmp_path, [MARKER])
    assert report["check_failures"] == 0
    assert row(report, "docs/spec/example.json")["classification"] == "public"


def test_report_hash_pins_the_exact_reviewed_public_bytes(tmp_path):
    import hashlib
    target = put(tmp_path, "src/Neutral.cs", "reviewed original bytes")
    result = row(module.classify(tmp_path, [MARKER]), "src/Neutral.cs")
    assert result["sha256"] == hashlib.sha256(target.read_bytes()).hexdigest()
    assert result["bytes"] == len(target.read_bytes())


def test_csharp_unicode_escape_fixture_is_not_a_network_path(tmp_path):
    escaped = chr(92) * 2 + "u0022"
    put(tmp_path, "tests/Fixture.cs", 'var text = "' + escaped + "neutral" + escaped + '";')
    assert module.classify(tmp_path, [MARKER])["check_failures"] == 0


def test_csharp_escaped_network_path_is_still_detected_on_original_line(tmp_path):
    network_literal = chr(92) * 4 + "workstation" + chr(92) * 2 + "share"
    put(tmp_path, "src/Neutral.cs", 'var n = 1;\nvar text = "' + network_literal + '";\n')
    issues = row(module.classify(tmp_path, [MARKER]), "src/Neutral.cs")["issues"]
    assert next(i for i in issues if i["kind"] == "machine-path")["lines"] == [2]


def test_csharp_comments_and_raw_strings_do_not_hide_paths(tmp_path):
    drive = "R" + ":" + chr(92) + "operator"
    put(tmp_path, "src/Neutral.cs", '// "' + drive + '"\nvar text = """' + drive + '""";')
    issues = row(module.classify(tmp_path, [MARKER]), "src/Neutral.cs")["issues"]
    assert next(i for i in issues if i["kind"] == "machine-path")["lines"] == [1, 2]


def test_csharp_verbatim_network_path_remains_detectable(tmp_path):
    network = chr(92) * 2 + "workstation" + chr(92) + "share"
    put(tmp_path, "src/Neutral.cs", 'var text = @"' + network + '";')
    assert module.classify(tmp_path, [MARKER])["check_failures"] == 1


def test_stale_entry_guide_is_withheld_even_without_minimum_detector_hits(tmp_path):
    put(tmp_path, "docs/guide/api_overview.md", "generic introduction awaiting its known editorial update")
    report = module.classify(tmp_path, [MARKER])
    result = row(report, "docs/guide/api_overview.md")
    assert result["classification"] == "needs-change"
    assert not result["issues"] and report["check_failures"] == 0
