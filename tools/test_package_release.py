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


def test_scene_sample_staging_uses_published_peer_and_exact_public_assets(tmp_path: Path) -> None:
    peer = tmp_path / "published"
    peer.mkdir()
    for name in ("SceneStarter.exe", "LICENSE.txt", "NOTICE.txt"):
        (peer / name).write_bytes(("synthetic staging fixture: " + name).encode())
    stage = tmp_path / "stage"
    pr.copy_scene_sample(stage, peer)
    for name in ("SceneStarter.exe", "LICENSE.txt", "NOTICE.txt"):
        assert (stage / "samples" / "scene-starter" / "game" / name).read_bytes() == (peer / name).read_bytes()
    source = pr.ROOT / "samples" / "SceneStarterProject"
    for relative in ("data/level.json", "schemas/scene.schema.json", "reference_map.json",
                     "assets/cube.obj", "assets/cube.obj.asset.json", "assets/pillar.obj", "assets/pillar.obj.asset.json"):
        assert (stage / "samples" / "scene-starter" / relative).read_bytes() == (source / relative).read_bytes()
        assert "samples/scene-starter/" + relative in pr.REQUIRED
    assert "adapters/scene-starter/SceneStarterAdapter.dll" in pr.REQUIRED


def test_scene_sample_missing_published_peer_fails_before_staging(tmp_path: Path) -> None:
    import pytest
    peer = tmp_path / "published"
    peer.mkdir()
    with pytest.raises(RuntimeError, match="SceneStarter.exe"):
        pr.copy_scene_sample(tmp_path / "stage", peer)
    assert not (tmp_path / "stage").exists()


def test_level_start_steps_cover_saved_level_play_and_standalone_export_journey() -> None:
    text = (pr.ROOT / "docs/release/START_HERE.txt").read_text(encoding="utf-8")
    headings = ("1. OPEN THE LEVEL", "2. ADD THE CRATE", "3. MOVE AND SAVE", "4. TRY PLAY",
                "5. EXPORT A BUILD", "6. RUN THE EXPORTED GAME")
    assert [text.index(heading) for heading in headings] == sorted(text.index(heading) for heading in headings)
    for part in ("Try the example", "samples\\scene-starter", "level.json", "Ctrl+O", "Ctrl+W", "Ctrl+S",
                 "textured-crate.obj", "Add selected object", "Position X", "Save, Don't Save or Cancel",
                 "Build and Play", "Build saved files", "Copy build to folder...", "new or empty Destination folder",
                 '.\\SceneStarter.exe --project . --seconds 0', "SceneStarter loaded saved level:",
                 "code 0", ".NET 10 runtime", "does not need BED", "no separate game window", "self-authored"):
        assert part in text
    for retired in ("serialized Play input", "16 MiB", "untextured models", "peer runs", "Ready then Stop"):
        assert retired not in text


def _scene_probe_reply():
    return {"status": "passed", "completed": True, "exitCode": 0, "document": "level.json",
            "output": [{"error": False, "text": "SceneStarter loaded saved level: 2 objects, 1 meshes."}]}


def test_packaged_scene_probe_uses_exact_unpacked_host_project_and_bounded_lifetime(tmp_path, monkeypatch):
    import subprocess
    import json
    def run(command, **options):
        assert command == [str(tmp_path / "BED.exe"), "--probe-play", str(tmp_path / "samples/scene-starter"), "--probe-args", "--seconds 0", "--timeout", "30"]
        assert options == dict(cwd=tmp_path, capture_output=True, text=True, timeout=40)
        return subprocess.CompletedProcess(command, 0, json.dumps(_scene_probe_reply()), '')
    monkeypatch.setattr(pr.subprocess, "run", run)
    assert "loads saved files and exits with code 0" in pr.verify_scene_play(tmp_path)


def test_packaged_scene_probe_refuses_retired_handshake_without_completion_evidence(tmp_path, monkeypatch):
    import subprocess
    import pytest
    monkeypatch.setattr(pr.subprocess, "run", lambda *args, **kwargs: subprocess.CompletedProcess(args[0], 0, '{"ready":true}', ''))
    with pytest.raises(RuntimeError, match="invalid evidence"):
        pr.verify_scene_play(tmp_path)


@pytest.mark.parametrize("field,value", [("status", "failed"), ("completed", False), ("exitCode", None),
    ("exitCode", 7), ("exitCode", False), ("document", "other.json"), ("output", []),
    ("output", [{"error": True, "text": "SceneStarter loaded saved level: 2 objects, 1 meshes."}]),
    ("output", [{"error": False, "text": "Running."}])])
def test_packaged_scene_probe_requires_successful_exit_and_saved_level_output(tmp_path, monkeypatch, field, value):
    import json
    reply = _scene_probe_reply()
    reply[field] = value
    monkeypatch.setattr(pr.subprocess, "run", lambda *args, **kwargs: pr.subprocess.CompletedProcess(args[0], 0, json.dumps(reply), ""))
    with pytest.raises(RuntimeError, match="invalid evidence"):
        pr.verify_scene_play(tmp_path)


def test_packaged_scene_probe_propagates_failure_and_timeout(tmp_path, monkeypatch):
    import subprocess
    import pytest
    monkeypatch.setattr(pr.subprocess, "run", lambda *args, **kwargs: subprocess.CompletedProcess(args[0], 1, '', 'bad catalog'))
    with pytest.raises(RuntimeError, match="bad catalog"):
        pr.verify_scene_play(tmp_path)
    def timeout(*args, **kwargs):
        raise subprocess.TimeoutExpired(args[0], 40)
    monkeypatch.setattr(pr.subprocess, "run", timeout)
    with pytest.raises(RuntimeError, match="failed to complete"):
        pr.verify_scene_play(tmp_path)


def test_scene_staging_keeps_saved_settings_but_not_a_prior_local_generation(tmp_path, monkeypatch):
    root = tmp_path / "source"
    project = root / "samples" / "SceneStarterProject"
    (project / ".bed" / "cache" / "generation").mkdir(parents=True)
    (project / ".bed" / "cache" / "current.json").write_text("old pointer")
    (project / ".bed" / "cache" / "owner.lock").write_text("old lock")
    (project / ".bed" / "cache" / "generation" / "mesh.bin").write_bytes(b"old mesh")
    (project / "game").mkdir()
    (project / "game" / "SceneStarter.exe").write_bytes(b"stale development binary")
    (project / "game" / "development.pdb").write_bytes(b"development symbols")
    settings = '{"play":{"program":"game.exe"},"unrelated":true}'
    (project / ".bed" / "project.json").write_text(settings)
    (project / "data").mkdir()
    (project / "data" / "level.json").write_text("self-authored saved scene")
    runtime = tmp_path / "published"
    runtime.mkdir()
    for name in ("SceneStarter.exe", "LICENSE.txt", "NOTICE.txt"):
        (runtime / name).write_bytes(("fixture: " + name).encode())
    monkeypatch.setattr(pr, "ROOT", root)
    stage = tmp_path / "stage"
    pr.copy_scene_sample(stage, runtime)
    copied = stage / "samples" / "scene-starter"
    assert (copied / ".bed" / "project.json").read_text() == settings
    assert (copied / "data" / "level.json").read_bytes() == (project / "data" / "level.json").read_bytes()
    assert not (copied / ".bed" / "cache").exists()
    assert sorted(p.name for p in (copied / "game").iterdir()) == ["LICENSE.txt", "NOTICE.txt", "SceneStarter.exe"]
    assert (project / "game" / "SceneStarter.exe").read_bytes() == b"stale development binary"
    assert (project / "game" / "development.pdb").read_bytes() == b"development symbols"
    assert (project / ".bed" / "cache" / "current.json").read_text() == "old pointer"
    assert (project / ".bed" / "cache" / "owner.lock").read_text() == "old lock"
    assert (project / ".bed" / "cache" / "generation" / "mesh.bin").read_bytes() == b"old mesh"
    for name in ("SceneStarter.exe", "LICENSE.txt", "NOTICE.txt"):
        assert (stage / "samples" / "scene-starter" / "game" / name).read_bytes() == (runtime / name).read_bytes()


def _saved_export_fixture(tmp_path):
    import hashlib
    import json
    import shutil
    project = tmp_path / "project"
    generation = project / ".bed/cache/generations" / ("a" * 32)
    generation.mkdir(parents=True)
    (project / "data").mkdir()
    (project / "data/level.json").write_bytes(b'{"saved":"self-authored level"}')
    artifacts = []
    for name, data in [("model.bin", b"self-authored model"), ("texture.bin", b"self-authored texture")]:
        (generation / name).write_bytes(data)
        artifacts.append(dict(Name=name, Length=len(data), Sha256=hashlib.sha256(data).hexdigest()))
    manifest = json.dumps(dict(Id="a" * 32, Generation=1, Revision="b" * 64, Artifacts=artifacts)).encode()
    (generation / "manifest.json").write_bytes(manifest)
    (generation / "owner.txt").write_bytes(b"BED asset cache stage v1")
    (project / ".bed/cache/current.json").write_text(json.dumps(dict(Id="a" * 32, ManifestHash=hashlib.sha256(manifest).hexdigest())))
    exported = tmp_path / "exported"
    shutil.copytree(project, exported)
    return project, exported


def test_saved_export_compares_level_pointer_manifest_owner_and_each_artifact_at_the_same_path(tmp_path):
    project, exported = _saved_export_fixture(tmp_path)
    assert pr.verify_saved_export(project, exported) == 6


@pytest.mark.parametrize("change", ["level", "pointer", "manifest", "owner", "model", "texture", "missing", "extra", "payload", "scene"])
def test_saved_export_refuses_any_changed_missing_or_extra_saved_file(tmp_path, change):
    project, exported = _saved_export_fixture(tmp_path)
    generation = exported / ".bed/cache/generations" / ("a" * 32)
    files = {"level": exported / "data/level.json", "pointer": exported / ".bed/cache/current.json",
             "manifest": generation / "manifest.json", "owner": generation / "owner.txt",
             "model": generation / "model.bin", "texture": generation / "texture.bin"}
    if change in files:
        files[change].write_bytes(b"changed output")
    elif change == "missing":
        (generation / "texture.bin").unlink()
    elif change == "extra":
        (generation / "extra.bin").write_bytes(b"undeclared output")
    else:
        (exported / ("payload.bin" if change == "payload" else "scene.json")).write_bytes(b"retired output")
    with pytest.raises(RuntimeError, match="saved-file verification failed"):
        pr.verify_saved_export(project, exported)


def test_saved_export_refuses_broken_source_generation_proof_even_if_the_copy_matches(tmp_path):
    project, exported = _saved_export_fixture(tmp_path)
    relative = ".bed/cache/generations/" + "a" * 32 + "/texture.bin"
    (project / relative).write_bytes(b"changed source")
    (exported / relative).write_bytes(b"changed source")
    with pytest.raises(RuntimeError, match="source artifact differs"):
        pr.verify_saved_export(project, exported)


@pytest.mark.parametrize("failure", [None, "unverified", "bad_hash", "game_exit", "changed_settings"])
def test_packaged_export_uses_real_host_verified_inventory_and_copied_game(tmp_path, monkeypatch, failure):
    import hashlib
    import json
    import shutil
    import subprocess
    source, _ = _saved_export_fixture(tmp_path)
    unpacked = tmp_path / "unpacked"
    project = unpacked / "samples/scene-starter"
    shutil.copytree(source, project)
    (project / "data/level.json").write_text(json.dumps({"objects": [{"id": "textured_crate", "asset": "c8a7e000000000000000000000000001"}]}))
    calls = []
    def run(command, **options):
        calls.append(command)
        if command[1] == "--probe-export":
            destination = Path(command[4])
            assert command == [str(unpacked / "BED.exe"), "--probe-export", str(project), "--destination", str(destination), "--timeout", "60"]
            assert options == dict(cwd=unpacked, capture_output=True, text=True, encoding="utf-8", timeout=75)
            shutil.copytree(project, destination)
            (destination / "SceneStarter.exe").write_bytes(b"self-authored executable fixture; subprocess is mocked")
            files = [{"path": p.relative_to(destination).as_posix(), "length": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
                     for p in destination.rglob("*") if p.is_file()]
            reply = dict(status="passed", verified=True, copied=True, document="level.json", destination=str(destination),
                         fileCount=len(files), totalBytes=sum(p["length"] for p in files), files=files)
            if failure == "unverified": reply["verified"] = False
            if failure == "bad_hash": reply["files"][0]["sha256"] = "0" * 64
            if failure == "changed_settings": (project / ".bed/project.json").write_text('{"changed":true}')
            return subprocess.CompletedProcess(command, 0, json.dumps(reply), "")
        assert command[1:] == ["--project", ".", "--seconds", "0"]
        assert options["cwd"] == Path(command[0]).parent
        return subprocess.CompletedProcess(command, 7 if failure == "game_exit" else 0,
                                           "SceneStarter loaded saved level: 3 objects, 1 meshes.\n", "")
    monkeypatch.setattr(pr.subprocess, "run", run)
    if failure:
        with pytest.raises(RuntimeError, match="export journey failed"):
            pr.verify_scene_export(unpacked)
    else:
        assert "6 saved level/cache files match exactly" in pr.verify_scene_export(unpacked)
        assert len(calls) == 2


def test_packaged_export_refuses_a_level_without_the_crate_before_launching_a_probe(tmp_path, monkeypatch):
    import json
    project = tmp_path / "samples/scene-starter/data"
    project.mkdir(parents=True)
    (project / "level.json").write_text(json.dumps({"objects": []}))
    def unexpected(*args, **kwargs): raise AssertionError("no command should run for the wrong sample")
    monkeypatch.setattr(pr.subprocess, "run", unexpected)
    with pytest.raises(RuntimeError, match="does not place the textured crate"):
        pr.verify_scene_export(tmp_path)


def test_scene_package_keeps_self_contained_play_and_explicit_export_runtime_separate(tmp_path):
    runtime = tmp_path / "play"
    export_runtime = tmp_path / "export-runtime"
    runtime.mkdir(); export_runtime.mkdir()
    for name in ("SceneStarter.exe", "LICENSE.txt", "NOTICE.txt"):
        (runtime / name).write_bytes(("self-contained play fixture " + name).encode())
    for name in pr.SCENE_EXPORT_RUNTIME:
        (export_runtime / name).write_bytes(("explicit export fixture " + name).encode())
    stage = tmp_path / "stage"
    pr.copy_scene_sample(stage, runtime, export_runtime)
    game = stage / "samples/scene-starter/game"
    assert (game / "SceneStarter.exe").read_bytes() == (runtime / "SceneStarter.exe").read_bytes()
    for name in pr.SCENE_EXPORT_RUNTIME:
        assert (game / "export" / name).read_bytes() == (export_runtime / name).read_bytes()
        assert "samples/scene-starter/game/export/" + name in pr.REQUIRED


def test_scene_package_missing_export_runtime_refuses_before_creating_the_stage(tmp_path):
    runtime = tmp_path / "play"
    export_runtime = tmp_path / "export-runtime"
    runtime.mkdir(); export_runtime.mkdir()
    for name in ("SceneStarter.exe", "LICENSE.txt", "NOTICE.txt"):
        (runtime / name).write_bytes(b"fixture")
    with pytest.raises(RuntimeError, match="export runtime is missing"):
        pr.copy_scene_sample(tmp_path / "stage", runtime, export_runtime)
    assert not (tmp_path / "stage").exists()
