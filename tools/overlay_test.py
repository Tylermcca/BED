"""Run the repo's .NET tests with a Codex outbox laid over a copy of the repo, so a card's tests can be checked without
writing anything outside its outbox. The copy, its build output and its temp files all live in <outbox>/.gate/.

    python tools/overlay_test.py codex/outbox/A004      exit 0 = dotnet test passed on the copy"""
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COPY = ["src", "samples", "tests", "tools", "docs", "fixtures", "build", "NOTICE.txt", "LICENSE.txt", "THIRD-PARTY-NOTICES.md", "Brutal.Authoring.slnx", "Directory.Build.props", "Directory.Packages.props", "global.json"]
SKIP = {"bin", "obj", "__pycache__", ".gate"}
# Shared project files a card may carry a copy of: their project lines are merged into the repo's current file, never
# replace it, so a copy made before someone else's addition can't erase that addition.
MERGED = {"Brutal.Authoring.slnx": r'<Project Path="[^"]+" />', "Brutal.Authoring.Tests.csproj": r'<ProjectReference Include="[^"]+" />'}


def merge(card_file, repo_file, pattern):
    """Adds the card's project lines missing from the repo's file, each after the repo's last line of that kind."""
    repo = repo_file.read_text(encoding="utf-8")
    have = {m.replace("\\", "/") for m in re.findall(pattern, repo)}
    extra = [m for m in re.findall(pattern, card_file.read_text(encoding="utf-8")) if m.replace("\\", "/") not in have]
    if extra:
        last = list(re.finditer(pattern, repo))[-1]
        indent = repo[repo.rfind("\n", 0, last.start()) + 1:last.start()]
        repo = repo[:last.end()] + "".join("\n" + indent + e for e in extra) + repo[last.end():]
        repo_file.write_text(repo, encoding="utf-8")
    return extra


def lay(src, dst, merge_shared=False):
    if src.is_dir():
        for p in src.rglob("*"):
            rel = p.relative_to(src)
            if p.is_dir() or any(part in SKIP for part in rel.parts) or rel.name in ("REPORT.md", "messages.md", "CLAIM.md", "OPINION.md"):
                continue
            if merge_shared and rel.name in MERGED and (dst / rel).is_file():
                for line in merge(p, dst / rel, MERGED[rel.name]):
                    print("merged into", rel.as_posix() + ":", line)
                continue
            (dst / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, dst / rel)
    else:
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)


def main(outbox):
    outbox = Path(outbox).resolve()
    gate = outbox / ".gate"
    repo, tmp = gate / "repo", gate / "tmp"
    # Only the gate's own working copy: a card's author keeps evidence in .gate too, and a review run erased A088's.
    shutil.rmtree(repo, ignore_errors=True)
    shutil.rmtree(tmp, ignore_errors=True)
    for name in COPY:
        lay(ROOT / name, repo / name)
    lay(outbox, repo, merge_shared=True)   # the card's files, at their final paths; shared project files merged
    tmp.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, TEMP=str(tmp), TMP=str(tmp), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    r = subprocess.run(["dotnet", "test", str(repo / "Brutal.Authoring.slnx")], cwd=repo, env=env, capture_output=True, text=True)
    tail = [l for l in r.stdout.splitlines() if "Passed!" in l or "Failed!" in l or " error " in l or "[FAIL]" in l]
    print("\n".join(tail) or r.stdout[-2000:])
    ok = r.returncode == 0 and any("Passed!" in l for l in tail)
    print("OK" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
