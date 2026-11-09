"""Fast checks of the release packer; the full build runs with tools/package_release.py itself, which unpacks and opens
the sample before reporting success."""

from pathlib import Path

import package_release as pr


def test_a_folder_with_every_required_file_lacks_nothing(tmp_path: Path) -> None:
    for f in pr.REQUIRED:
        (tmp_path / f).parent.mkdir(parents=True, exist_ok=True)
        (tmp_path / f).write_text("x")
    assert pr.missing(tmp_path) == []


def test_a_missing_piece_is_named(tmp_path: Path) -> None:
    assert "START_HERE.txt" in pr.missing(tmp_path)
    assert "adapters/starter/StarterAdapter.dll" in pr.missing(tmp_path)


def test_the_top_of_a_release_holds_only_the_short_list(tmp_path: Path) -> None:
    (tmp_path / "BED.exe").write_text("x")
    (tmp_path / "samples").mkdir()
    assert pr.untidy(tmp_path) == []
    (tmp_path / "Brutal.Core.dll").write_text("x")
    assert pr.untidy(tmp_path) == ["Brutal.Core.dll"]


def test_every_vendored_licence_exists_and_a_release_must_carry_it() -> None:
    assert pr.VENDORED_LICENCES, "ImCode's licence ships with a release"
    for name, source in pr.VENDORED_LICENCES.items():
        text = (pr.ROOT / source).read_text(encoding="utf-8")
        assert text.startswith("MIT License") and "Copyright (c) 2026 aproposmath" in text
        assert "licenses/" + name in pr.REQUIRED
    notices = (pr.ROOT / "THIRD-PARTY-NOTICES.md").read_text(encoding="utf-8")
    assert "98e69303054540c43fd67909b6a30975a19aca8b" in notices
    assert "98e69303054540c43fd67909b6a30975a19aca8b" in (pr.ROOT / "third_party" / "ImCode" / "PINNED.md").read_text(encoding="utf-8")


def test_a_release_carries_the_text_package_and_the_imcode_licence() -> None:
    # BED.exe is a single file: the Text panel and the vendored ImCode are inside it, so only the licence is a file.
    assert "licenses/ImCode-LICENSE.txt" in pr.REQUIRED
    project = (pr.ROOT / "src" / "Brutal.Authoring.Editor.Text" / "Brutal.Authoring.Editor.Text.csproj").read_text(encoding="utf-8")
    assert 'ImCode.csproj" PrivateAssets="all"' in project and "licenses" in project
    core = (pr.ROOT / "src" / "Brutal.Authoring.Editor.ImGui" / "Brutal.Authoring.Editor.ImGui.csproj").read_text(encoding="utf-8")
    assert "ImCode.csproj" not in core, "the core editor carries no third-party code"
    launcher = (pr.ROOT / "src" / "Brutal.Authoring.Launcher" / "Brutal.Authoring.Launcher.csproj").read_text(encoding="utf-8")
    assert "Brutal.Authoring.Editor.Text.csproj" in launcher, "the app the release publishes has the Text panel"


def test_the_start_page_and_the_sample_it_names_exist() -> None:
    start = (pr.ROOT / "docs" / "release" / "START_HERE.txt").read_text(encoding="utf-8")
    assert "Try the example" in start
    assert (pr.ROOT / "samples" / "StarterProject" / "reference_map.json").is_file()


def test_ai_guide_is_required_and_copied_without_changes(tmp_path: Path) -> None:
    assert "guides/using_bed_with_ai.md" in pr.REQUIRED
    assert "guides" in pr.TOP_LEVEL
    assert "guides/using_bed_with_ai.md" in pr.missing(tmp_path)
    pr.copy_ai_guide(tmp_path)
    source = pr.ROOT / "docs" / "guide" / "using_bed_with_ai.md"
    assert (tmp_path / "guides" / "using_bed_with_ai.md").read_bytes() == source.read_bytes()
    assert "guides/using_bed_with_ai.md" not in pr.missing(tmp_path)


# These tests use only a fake child. They do not launch BED or the CLI.
def _mcp_replies_a386(name="BED", missing=None, recipe="recipes.json"):
    import json
    names = [n for n in ["describe", "find", "get", "set", "save", "check"] if n != missing]
    results = [{"serverInfo": {"name": name}}, {"tools": [{"name": n} for n in names]},
               {"isError": False, "content": [{"type": "text", "text": json.dumps({"files": [{"file": recipe}]})}]}]
    return "".join(json.dumps({"jsonrpc": "2.0", "id": i, "result": result}) + "\n"
                   for i, result in enumerate(results, 1))


class _McpChildA386:
    def __init__(self, output, timeout=False, exit_code=0):
        import io
        self.output, self.timeout, self.returncode = output, timeout, exit_code
        self.stdin, self.stdout, self.stderr = io.StringIO(), io.StringIO(), io.StringIO()
        self.killed = False
        self.waits = []
        self.input = None

    def communicate(self, input, timeout):
        self.input = input
        assert timeout == 30
        if self.timeout:
            raise pr.subprocess.TimeoutExpired("fake", timeout)
        return self.output, "fake stderr"

    def kill(self):
        self.killed = True

    def wait(self, timeout):
        self.waits.append(timeout)
        return self.returncode


def _install_mcp_child_a386(monkeypatch, child, folder):
    def start(command, **kwargs):
        assert command == [str(folder / "bed-cli.exe"), "--data", "samples/recipes/data",
                           "--schemas", "samples/recipes/schemas", "--map", "samples/recipes/reference_map.json", "mcp"]
        assert kwargs == {"cwd": folder, "stdin": pr.subprocess.PIPE, "stdout": pr.subprocess.PIPE,
                          "stderr": pr.subprocess.PIPE, "text": True, "encoding": "utf-8"}
        return child
    monkeypatch.setattr(pr.subprocess, "Popen", start)


def _assert_mcp_cleanup_a386(child):
    assert child.killed
    assert child.waits == [5]
    assert all(s.closed for s in (child.stdin, child.stdout, child.stderr))


def test_mcp_release_check_passes_and_sends_only_read_calls(monkeypatch, tmp_path):
    import json
    child = _McpChildA386(_mcp_replies_a386())
    _install_mcp_child_a386(monkeypatch, child, tmp_path)
    assert "MCP initialize" in pr.verify_mcp(tmp_path)
    requests = [json.loads(line) for line in child.input.splitlines()]
    assert [r["method"] for r in requests] == ["initialize", "notifications/initialized", "tools/list", "tools/call"]
    assert "id" not in requests[1]
    assert requests[3]["params"] == {"name": "describe", "arguments": {}}
    _assert_mcp_cleanup_a386(child)


import pytest


@pytest.mark.parametrize("mode,reason", [("name", "server name is not BED"), ("tool", "missing tools: check"),
    ("timeout", "timed out"), ("recipe", "did not name recipes.json"), ("json", "MCP release check failed"),
    ("exit", "exit 3")])
def test_mcp_release_check_fails_plainly_and_cleans_up(monkeypatch, tmp_path, mode, reason):
    output = "not JSON" if mode == "json" else _mcp_replies_a386(
        name="wrong" if mode == "name" else "BED", missing="check" if mode == "tool" else None,
        recipe="other.json" if mode == "recipe" else "recipes.json")
    child = _McpChildA386(output, timeout=mode == "timeout", exit_code=3 if mode == "exit" else 0)
    _install_mcp_child_a386(monkeypatch, child, tmp_path)
    with pytest.raises(RuntimeError, match=reason):
        pr.verify_mcp(tmp_path)
    _assert_mcp_cleanup_a386(child)


def test_ai_guide_link_is_present_inside_unpacked_folder(tmp_path):
    guide = tmp_path / "guides" / "using_bed_with_ai.md"
    guide.parent.mkdir()
    guide.write_text("Sample guide", encoding="utf-8")
    assert pr.verify_ai_guide(tmp_path) == guide.resolve()


def test_missing_ai_guide_link_fails_plainly(tmp_path):
    with pytest.raises(RuntimeError, match="AI guide link is missing"):
        pr.verify_ai_guide(tmp_path)


def _ai_setup_json_a395(folder):
    import json
    exe = str((folder / "bed-cli.exe").resolve())
    args = ["--data", str((folder / "samples/recipes/data").resolve()),
            "--schemas", str((folder / "samples/recipes/schemas").resolve()),
            "--map", str((folder / "samples/recipes/reference_map.json").resolve()), "mcp"]
    quote = lambda value: "'" + value.replace("'", "''") + "'"
    return json.dumps({"command": "& " + quote(exe) + " " + " ".join(map(quote, args[:-1] + ["describe"])),
                      "desktopJson": json.dumps({"mcpServers": {"bed": {"command": exe, "args": args}}}),
                      "claudeCode": "claude mcp add bed -- " + quote(exe) + " " + " ".join(map(quote, args)),
                      "guidePath": str((folder / "guides/using_bed_with_ai.md").resolve())})


@pytest.mark.parametrize("mode", ["present", "absent", "failed", "timeout", "malformed", "bad-help", "wrong-guide"])
def test_ai_setup_capability_and_failures_are_distinct(monkeypatch, tmp_path, mode):
    import json
    calls = []
    guide = (tmp_path / "guides/using_bed_with_ai.md").resolve()
    def run(command, **kwargs):
        calls.append(command)
        assert kwargs == {"cwd": tmp_path, "capture_output": True, "text": True, "encoding": "utf-8", "timeout": 30}
        if command == [str(tmp_path / "bed-cli.exe"), "--help"]:
            help_text = "usage: bed-cli options\n  describe details\n  mcp stdio\n"
            if mode != "absent": help_text += "  ai-setup [--json] examples\n"
            return pr.subprocess.CompletedProcess(command, 1 if mode == "bad-help" else 2, "", help_text)
        assert command == [str(tmp_path / "bed-cli.exe"), "ai-setup", "--data", "samples/recipes/data",
                           "--schemas", "samples/recipes/schemas", "--map", "samples/recipes/reference_map.json", "--json"]
        if mode == "timeout": raise pr.subprocess.TimeoutExpired(command, 30)
        output = _ai_setup_json_a395(tmp_path)
        if mode == "malformed": output = "not JSON"
        if mode == "wrong-guide":
            value = json.loads(output); value["guidePath"] = str(tmp_path / "wrong.md"); output = json.dumps(value)
        return pr.subprocess.CompletedProcess(command, 2 if mode == "failed" else 0, output, "No adapter file at missing.dll.")
    monkeypatch.setattr(pr.subprocess, "run", run)
    if mode == "present": assert "all three" in pr.verify_ai_setup(tmp_path, guide)
    elif mode == "absent": assert "skipped" in pr.verify_ai_setup(tmp_path, guide)
    else:
        with pytest.raises(RuntimeError, match="AI setup"):
            pr.verify_ai_setup(tmp_path, guide)
    assert len(calls) == (1 if mode in ("absent", "bad-help") else 2)
