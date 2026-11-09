"""Build the release zip anyone can unpack and run (the user, 2026-11-01: "a packaged release: a zip with a one-page
start here").

    python tools/package_release.py [--out dist] [--rid win-x64]

It publishes BED.exe (the editor), bed-cli.exe (the command line) and play-checkers.exe as single self-contained files,
with the Visual C++ prerequisite explained in START_HERE.txt; adds the three samples under samples/, their adapters under adapters/, START_HERE.txt and
the licences; zips it; then unpacks the zip into a fresh folder and opens each sample there headless (--shot), so a zip that
doesn't run is never reported as built. Prints the zip's path and size, or why it failed (exit 1).
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NAME = "BED"

# What a release holds, relative to its folder: checked after packing, so a missing piece fails the build. The top level
# is kept to what a person needs (the user, 2026-11-08: "the folders should be cleaned up and more user friendly"): the
# programs are single files, with their libraries inside them, and everything else is in a folder.
REQUIRED = [
    "BED.exe",
    "accesskit.dll",
    "licenses/AccessKit-LICENSE-MIT.txt",
    "licenses/AccessKit-LICENSE-APACHE.txt",
    "bed-cli.exe",
    "START_HERE.txt",
    "guides/using_bed_with_ai.md",
    "LICENSE.txt",
    # What BRUTAL's licence asks to ship beside a program built with it (2.3, 4.0), and the .NET runtime's licence.
    "NOTICE.txt",
    "licenses/THIRD-PARTY-NOTICES.md",
    "licenses/BRUTAL-IMGUI-THIRD-PARTY-NOTICES.txt",
    "licenses/DOTNET-RUNTIME-LICENSE.txt",
    # The Text panel bundles the vendored ImCode inside BED.exe (THIRD-PARTY-NOTICES.md): its licence travels with it.
    "licenses/ImCode-LICENSE.txt",
    "samples/recipes/data/wares.json",
    "samples/recipes/data/recipes.json",
    "samples/recipes/schemas/wares.schema.json",
    "samples/recipes/reference_map.json",
    "samples/checkers/data/opening.json",
    "samples/checkers/schemas/board.schema.json",
    "samples/checkers/play-checkers.exe",
    "samples/first-task/data/settings.json",
    "samples/first-task/schemas/settings.schema.json",
    "samples/first-task/first_task.md",
    "adapters/starter/StarterAdapter.dll",
    "adapters/checkers/CheckersAdapter.dll",
    "adapters/first-task/FirstTaskAdapter.dll",
]

# Only these files and folders may sit at the top of a release.
TOP_LEVEL = {"accesskit.dll", "BED.exe", "bed-cli.exe", "START_HERE.txt", "LICENSE.txt", "NOTICE.txt", "licenses", "samples", "adapters", "guides"}

# Vendored source (third_party/) whose licence ships beside the programs, by its name in licenses/.
VENDORED_LICENCES = {"ImCode-LICENSE.txt": "third_party/ImCode/LICENSE"}


ACCESSKIT_DLL_SHA256 = "c8e2b528a2f4bbc5ee8e091ae48d55b43156518a0105e48e94c64e101bf1c29b"


def copy_accesskit(stage: Path, rid: str) -> None:
    """Stage the approved Windows x64 C ABI beside BED, without loading it or copying symbols/static libraries."""
    if rid != "win-x64":
        raise RuntimeError("AccessKit C 0.23.2 is vendored only for win-x64")
    source = ROOT / "third_party" / "accesskit" / "0.23.2"
    # A wrong or replaced native binary must not silently enter the release.
    import hashlib
    dll = source / "accesskit.dll"
    if hashlib.sha256(dll.read_bytes()).hexdigest() != ACCESSKIT_DLL_SHA256:
        raise RuntimeError("AccessKit DLL differs from the approved 0.23.2 Windows x64 release")
    stage.mkdir(parents=True, exist_ok=True)
    (stage / "licenses").mkdir(parents=True, exist_ok=True)
    shutil.copy2(dll, stage / "accesskit.dll")
    for name in ("MIT", "APACHE"):
        shutil.copy2(source / ("LICENSE-" + name), stage / "licenses" / ("AccessKit-LICENSE-" + name + ".txt"))


def run(cmd: list[str]) -> None:
    result = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(cmd)} failed:\n{result.stdout[-2000:]}\n{result.stderr[-2000:]}")


def missing(folder: Path) -> list[str]:
    """The required files a release folder lacks."""
    return [f for f in REQUIRED if not (folder / f).is_file()]


def publish(project: str, rid: str, into: Path) -> Path:
    """Publishes one program as a single self-contained file (its native libraries unpack themselves when it starts)
    into a scratch folder, and returns that folder."""
    run(["dotnet", "publish", project, "-c", "Release", "-r", rid, "--self-contained", "true", "-p:PublishSingleFile=true",
         "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true", "-p:DebugType=none", "-o", str(into), "--nologo", "-v:q"])
    return into


def copy_start_here(stage: Path) -> None:
    """Keep the release's prerequisite instructions with the source; never bundle an installer."""
    text = (ROOT / "docs" / "release" / "START_HERE.txt").read_text(encoding="utf-8")
    required = ("Visual C++ Redistributable (x64)", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll",
                "https://aka.ms/vs/17/release/vc_redist.x64.exe")
    if any(part not in text for part in required) or "Nothing to install" in text:
        raise RuntimeError("START_HERE.txt must explain the Visual C++ prerequisite and Microsoft's download link")
    (stage / "START_HERE.txt").write_text(text, encoding="utf-8")


def copy_ai_guide(stage: Path) -> None:
    """Ship the same novice guide linked from the source README."""
    guides = stage / "guides"
    guides.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / "docs" / "guide" / "using_bed_with_ai.md", guides / "using_bed_with_ai.md")


def build(stage: Path, rid: str) -> None:
    if rid != "win-x64":
        raise RuntimeError("AccessKit C 0.23.2 is vendored only for win-x64")
    with tempfile.TemporaryDirectory(prefix="bed-publish-") as tmp:
        app = publish("src/Brutal.Authoring.Launcher/Brutal.Authoring.Launcher.csproj", rid, Path(tmp) / "app")
        cli = publish("src/Brutal.Authoring.Agent.Cli/Brutal.Authoring.Agent.Cli.csproj", rid, Path(tmp) / "cli")
        game = publish("samples/Checkers.Game/Checkers.Game.csproj", rid, Path(tmp) / "game")   # play-checkers, the game itself
        stage.mkdir(parents=True)
        shutil.copy2(app / "BED.exe", stage / "BED.exe")
        shutil.copy2(cli / "bed-cli.exe", stage / "bed-cli.exe")
        shutil.copy2(app / "NOTICE.txt", stage / "NOTICE.txt")
        shutil.copytree(app / "licenses", stage / "licenses")
        shutil.copytree(ROOT / "samples" / "StarterProject", stage / "samples" / "recipes")
        shutil.copytree(ROOT / "samples" / "CheckersProject", stage / "samples" / "checkers")
        shutil.copy2(game / "play-checkers.exe", stage / "samples" / "checkers" / "play-checkers.exe")
    # Adapters keep only what BED.exe doesn't already carry: it holds BRUTAL and these tools' own libraries, which
    # adapters share; the checkers rules are the checkers adapter's own.
    for name, folder, project, own in [("starter", "StarterAdapter", "StarterAdapter", ["StarterAdapter."]),
                                       ("checkers", "CheckersAdapter", "CheckersAdapter", ["CheckersAdapter.", "Checkers."]),
                                       ("first-task", "FirstTask", "FirstTask", ["FirstTaskAdapter."])]:
        adapter_out = stage / "adapters" / name
        run(["dotnet", "build", f"samples/{folder}/{project}.csproj", "-c", "Release", "-o", str(adapter_out), "--nologo", "-v:q"])
        for f in adapter_out.iterdir():
            keep = f.is_file() and f.suffix == ".dll" and any(f.name.startswith(prefix) for prefix in own)
            if not keep:
                f.unlink() if f.is_file() else shutil.rmtree(f)
    first = stage / "samples" / "first-task"
    shutil.copytree(ROOT / "samples" / "FirstTask" / "data", first / "data")
    shutil.copytree(ROOT / "samples" / "FirstTask" / "schemas", first / "schemas")
    shutil.copy2(ROOT / "docs" / "guide" / "first_task.md", first / "first_task.md")
    copy_accesskit(stage, rid)
    copy_start_here(stage)
    copy_ai_guide(stage)
    shutil.copy2(ROOT / "THIRD-PARTY-NOTICES.md", stage / "licenses" / "THIRD-PARTY-NOTICES.md")
    shutil.copy2(ROOT / "LICENSE.txt", stage / "LICENSE.txt")
    for name, licence in VENDORED_LICENCES.items():
        shutil.copy2(ROOT / licence, stage / "licenses" / name)
    for f in list(stage.rglob("*")):
        if f.is_file() and f.suffix in (".pdb", ".xml"):
            f.unlink()


def untidy(folder: Path) -> list[str]:
    """Anything at the top of a release folder that isn't on the short list."""
    return sorted(f.name for f in folder.iterdir() if f.name not in TOP_LEVEL)


def verify_mcp(unpacked: Path) -> str:
    """Check the unpacked CLI over stdio only; never save or edit sample data."""
    requests = [
        {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2025-06-18", "capabilities": {},
            "clientInfo": {"name": "BED release check", "version": "1"}}},
        {"jsonrpc": "2.0", "method": "notifications/initialized"},
        {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
        {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "describe", "arguments": {}}},
    ]
    command = [str(unpacked / "bed-cli.exe"), "--data", "samples/recipes/data",
               "--schemas", "samples/recipes/schemas", "--map", "samples/recipes/reference_map.json", "mcp"]
    try:
        process = subprocess.Popen(command, cwd=unpacked, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, text=True, encoding="utf-8")
    except OSError as e:
        raise RuntimeError(f"MCP release check could not start bed-cli.exe: {e}") from e
    try:
        try:
            # The server consumes lines in order. EOF closes this bounded, read-only session.
            output, error = process.communicate("".join(json.dumps(r) + "\n" for r in requests), timeout=30)
        except subprocess.TimeoutExpired as e:
            raise RuntimeError("MCP release check timed out after 30 seconds") from e
        if process.returncode != 0:
            raise RuntimeError(f"MCP release check failed (exit {process.returncode}): {error[-2000:]}")
        try:
            replies = [json.loads(line) for line in output.splitlines() if line.strip()]
            if len(replies) != 3 or any(not isinstance(r, dict) or r.get("jsonrpc") != "2.0" or "error" in r for r in replies):
                raise ValueError("expected three successful JSON-RPC replies")
            if [r.get("id") for r in replies] != [1, 2, 3]:
                raise ValueError("unexpected or missing reply ids")
            initialized, listed, described = [r["result"] for r in replies]
            if initialized["serverInfo"]["name"] != "BED":
                raise ValueError("server name is not BED")
            required = {"describe", "find", "get", "set", "save", "check"}
            found = {t["name"] for t in listed["tools"]}
            if absent := required - found:
                raise ValueError("missing tools: " + ", ".join(sorted(absent)))
            if described.get("isError") is not False:
                raise ValueError("describe reported an error")
            content = described["content"]
            if len(content) != 1 or content[0]["type"] != "text":
                raise ValueError("describe did not return its JSON text result")
            description = json.loads(content[0]["text"])
            if not any(f["file"] == "recipes.json" for f in description["files"]):
                raise ValueError("describe did not name recipes.json")
        except (ValueError, KeyError, TypeError, AttributeError) as e:
            raise RuntimeError(f"MCP release check failed: {e}") from e
        return "MCP initialize, tools/list and describe pass."
    finally:
        # Always attempt termination, even after a malformed reply or timeout. Never wait unboundedly.
        try:
            try:
                process.kill()
            except ProcessLookupError:
                pass  # communicate already reaped a normally exited child
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired as e:
                raise RuntimeError("MCP release check could not reap bed-cli.exe after killing it (5 seconds)") from e
        finally:
            for stream in (process.stdin, process.stdout, process.stderr):
                if stream is not None:
                    stream.close()


def verify_ai_guide(unpacked: Path) -> Path:
    """Resolve the same relative guide link as AiSetupPanel under the unpacked app base."""
    root = unpacked.resolve()
    guide = (root / "guides" / "using_bed_with_ai.md").resolve()
    if not guide.is_relative_to(root):
        raise RuntimeError("AI guide link resolves outside the unpacked release")
    if not guide.is_file():
        raise RuntimeError("AI guide link is missing: guides/using_bed_with_ai.md")
    return guide


def verify_ai_setup(unpacked: Path, guide: Path) -> str:
    """Use the advertised command only; a command failure is never an unsupported-command skip."""
    exe = unpacked / "bed-cli.exe"
    try:
        help_result = subprocess.run([str(exe), "--help"], cwd=unpacked, capture_output=True,
                                     text=True, encoding="utf-8", timeout=30)
    except (OSError, subprocess.TimeoutExpired, UnicodeError) as e:
        raise RuntimeError(f"AI setup help check failed: {e}") from e
    # Existing CLI help is usage on stderr with exit 2, without opening a project.
    usage = help_result.stderr
    if help_result.returncode != 2 or help_result.stdout.strip() or "usage: bed-cli " not in usage:
        raise RuntimeError("AI setup help check failed: expected bed-cli usage on stderr (exit 2)")
    calls = {line.strip().split()[0] for line in usage.splitlines() if line.strip()}
    if not {"describe", "mcp"}.issubset(calls):
        raise RuntimeError("AI setup help check failed: incomplete command list")
    if "ai-setup" not in calls:
        return "AI setup check skipped: this CLI's help does not offer ai-setup yet."
    command = [str(exe), "ai-setup", "--data", "samples/recipes/data", "--schemas", "samples/recipes/schemas",
               "--map", "samples/recipes/reference_map.json", "--json"]
    try:
        result = subprocess.run(command, cwd=unpacked, capture_output=True, text=True, encoding="utf-8", timeout=30)
    except (OSError, subprocess.TimeoutExpired, UnicodeError) as e:
        raise RuntimeError(f"AI setup command failed: {e}") from e
    if result.returncode != 0:
        raise RuntimeError(f"AI setup command failed (exit {result.returncode}): {result.stderr[-2000:]}")
    try:
        values = json.loads(result.stdout)
        for key in ("command", "desktopJson", "claudeCode", "guidePath"):
            if not isinstance(values[key], str) or not values[key].strip():
                raise ValueError(f"missing or empty {key}")
        if Path(values["guidePath"]).resolve() != guide:
            raise ValueError("guidePath does not match the unpacked AI guide")
        desktop = json.loads(values["desktopJson"])["mcpServers"]["bed"]
        if Path(desktop["command"]).resolve() != exe.resolve():
            raise ValueError("desktop command does not name the unpacked CLI")
        expected = ["--data", str((unpacked / "samples/recipes/data").resolve()),
                    "--schemas", str((unpacked / "samples/recipes/schemas").resolve()),
                    "--map", str((unpacked / "samples/recipes/reference_map.json").resolve()), "mcp"]
        if desktop["args"] != expected:
            raise ValueError("desktop arguments do not name the unpacked recipes sample")
        quote = lambda value: "'" + value.replace("'", "''") + "'"
        if values["command"] != "& " + quote(str(exe.resolve())) + " " + " ".join(map(quote, expected[:-1] + ["describe"])):
            raise ValueError("CLI example does not describe the unpacked recipes sample")
        if values["claudeCode"] != "claude mcp add bed -- " + quote(str(exe.resolve())) + " " + " ".join(map(quote, expected)):
            raise ValueError("Claude Code example does not name the unpacked recipes sample")
    except (ValueError, KeyError, TypeError, AttributeError) as e:
        raise RuntimeError(f"AI setup output check failed: {e}") from e
    return "AI guide link and all three AI setup examples pass."


def verify(zip_path: Path) -> str:
    """Unpacks the zip into a fresh folder and opens the sample headless there. Returns what the app said."""
    with tempfile.TemporaryDirectory(prefix="bed-release-") as tmp:
        unpacked = Path(tmp) / NAME
        with zipfile.ZipFile(zip_path) as z:
            z.extractall(unpacked)
        guide = verify_ai_guide(unpacked)
        if lacking := missing(unpacked):
            raise RuntimeError("the zip lacks " + ", ".join(lacking))
        if extra := untidy(unpacked):
            raise RuntimeError("the zip's top folder has more than it should: " + ", ".join(extra))
        shot = Path(tmp) / "sample.png"
        settings = Path(tmp) / "settings"
        exe = unpacked / "BED.exe"
        result = subprocess.run([str(exe), str(unpacked / "samples" / "recipes"), "--settings", str(settings), "--shot", str(shot)], capture_output=True, text=True, timeout=120)
        if result.returncode != 0 or not shot.is_file() or shot.stat().st_size < 1000:
            raise RuntimeError(f"the unpacked app didn't open the sample (exit {result.returncode}):\n{result.stdout}\n{result.stderr}")
        said = result.stdout.strip().splitlines()[0]
        # Checkers: the app opens it with its adapter, and the game itself starts on a position.
        checkers = subprocess.run([str(exe), str(unpacked / "samples" / "checkers"), "--settings", str(settings), "--shot", str(Path(tmp) / "checkers.png")], capture_output=True,
                                  text=True, timeout=120)
        if checkers.returncode != 0 or "with Checkers" not in checkers.stdout:
            raise RuntimeError(f"the unpacked app didn't open checkers with its adapter:\n{checkers.stdout}\n{checkers.stderr}")
        game = subprocess.run([str(unpacked / "samples" / "checkers" / "play-checkers.exe"), str(unpacked / "samples" / "checkers" / "data" / "opening.json"), "--shot",
                               str(Path(tmp) / "game.png")], capture_output=True, text=True, timeout=120)
        if game.returncode != 0:
            raise RuntimeError(f"play-checkers didn't start:\n{game.stdout}\n{game.stderr}")
        first = subprocess.run([str(exe), str(unpacked / "samples" / "first-task"), "--settings", str(settings), "--shot", str(Path(tmp) / "first.png")],
                               capture_output=True, text=True, timeout=120)
        if first.returncode != 0 or "FirstTask" not in first.stdout and "first" not in first.stdout.lower():
            raise RuntimeError(f"the unpacked app didn't open the first-task sample with its adapter:\n{first.stdout}\n{first.stderr}")
        mcp = verify_mcp(unpacked)
        ai_setup = verify_ai_setup(unpacked, guide)
        return f"{said} {checkers.stdout.strip().splitlines()[0]} {first.stdout.strip().splitlines()[0]} play-checkers starts. {mcp} {ai_setup}"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default="dist", help="the folder the zip goes in (default dist)")
    parser.add_argument("--rid", default="win-x64", help="the platform to build for (default win-x64)")
    args = parser.parse_args(argv)
    out = (ROOT / args.out).resolve()
    stage = out / f"{NAME}-{args.rid}"
    zip_path = out / f"{NAME}-{args.rid}.zip"
    try:
        if stage.exists():
            shutil.rmtree(stage)
        build(stage, args.rid)
        if lacking := missing(stage):
            raise RuntimeError("the release lacks " + ", ".join(lacking))
        if zip_path.exists():
            zip_path.unlink()
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
            for f in sorted(stage.rglob("*")):
                if f.is_file() and f.suffix != ".pdb":
                    z.write(f, f.relative_to(stage))
        said = verify(zip_path)
    except (RuntimeError, OSError, subprocess.TimeoutExpired) as e:
        print(f"Release not built: {e}", file=sys.stderr)
        return 1
    print(f"Built {zip_path} ({zip_path.stat().st_size / 1e6:.1f} MB). Unpacked and checked: {said}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
