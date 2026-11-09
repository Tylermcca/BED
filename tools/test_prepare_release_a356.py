"""Prepared only: fake packaging and Git calls, never a real release or upload."""
import hashlib
from pathlib import Path

import pytest
import prepare_release as pr


@pytest.fixture
def fake(tmp_path, monkeypatch):
    root = tmp_path / "repo"
    (root / "tools").mkdir(parents=True)
    (root / "tools/package_release.py").write_text("# fake marker\n", encoding="utf-8")
    calls = []

    def command(args, *, cwd, timeout):
        calls.append((args, cwd, timeout))
        assert cwd == root
        if args[:2] == ["git", "--no-pager"]:
            if args[2] == "rev-parse":
                return "a" * 40
            if args[2] == "describe":
                return "v0.1"
            assert args[2:] == ["log", "--no-merges", "--format=%s", "v0.1.." + "a" * 40, "--"]
            return "Make lists easier to edit\nA356: internal work\nFix plain error wording"
        assert args[1] == str(root / "tools/package_release.py")
        assert args[-2:] == ["--rid", "win-x64"]
        output = Path(args[args.index("--out") + 1])
        (output / "BED-win-x64.zip").write_bytes(b"synthetic archive bytes")
        return "Built fake candidate"

    monkeypatch.setattr(pr, "run_command", command)
    return root, calls


def test_fake_package_checksum_and_public_draft(tmp_path, fake):
    root, calls = fake
    output = pr.prepare("1.2.3-preview", tmp_path / "out", root=root)
    assert (output / "SHA256SUMS.txt").read_text() == hashlib.sha256(b"synthetic archive bytes").hexdigest() + "  BED-win-x64.zip\n"
    notes = (output / "RELEASE_NOTES.md").read_text()
    assert "Make lists easier to edit" in notes and "Fix plain error wording" in notes
    assert "A356" not in notes and "internal work" not in notes
    assert "binary and package version metadata has not been changed" in notes
    assert len(calls) == 5
    assert calls[3][2] == 1800


@pytest.mark.parametrize("version", ["../1", "1/2", "1\\2", "", "--version", "1 2", "1" * 65, "1-A356"])
def test_bad_version_before_any_command(tmp_path, monkeypatch, version):
    monkeypatch.setattr(pr, "run_command", lambda *a, **k: pytest.fail("No process permitted"))
    with pytest.raises(ValueError):
        pr.prepare(version, tmp_path)


def test_existing_candidate_never_overwritten(tmp_path, fake):
    root, calls = fake
    output = tmp_path / "BED-1"
    output.mkdir()
    marker = output / "keep.txt"
    marker.write_text("unchanged")
    with pytest.raises(FileExistsError):
        pr.prepare("1", tmp_path, root=root)
    assert marker.read_text() == "unchanged" and calls == []


def test_missing_tag_stops_before_package(tmp_path, fake, monkeypatch):
    root, calls = fake
    original = pr.run_command
    def command(args, **kwargs):
        if args[0] == "git" and args[2] == "describe":
            raise RuntimeError("No tags")
        return original(args, **kwargs)
    monkeypatch.setattr(pr, "run_command", command)
    with pytest.raises(RuntimeError, match="No tags"):
        pr.prepare("1", tmp_path / "out", root=root)
    assert not (tmp_path / "out").exists()


@pytest.mark.parametrize("mode", ["failure", "missing", "empty", "changed-head"])
def test_failed_or_inconsistent_package_has_no_success_notes(tmp_path, fake, monkeypatch, mode):
    root, calls = fake
    original = pr.run_command
    packaged = False
    def command(args, **kwargs):
        nonlocal packaged
        if args[0] != "git":
            packaged = True
            if mode == "failure":
                raise RuntimeError("Package gate failed")
            if mode == "missing":
                return "success without file"
            result = original(args, **kwargs)
            if mode == "empty":
                (Path(args[args.index("--out") + 1]) / "BED-win-x64.zip").write_bytes(b"")
            return result
        if packaged and mode == "changed-head" and args[2] == "rev-parse":
            return "b" * 40
        return original(args, **kwargs)
    monkeypatch.setattr(pr, "run_command", command)
    with pytest.raises(RuntimeError):
        pr.prepare("1", tmp_path / "out", root=root)
    output = tmp_path / "out/BED-1"
    assert not (output / "SHA256SUMS.txt").exists()
    assert not (output / "RELEASE_NOTES.md").exists()


def test_internal_subjects_omitted_not_fragmented():
    notes = pr.draft_notes("1", "A12 fix\nT175-r2 work\nP5 change\nC030 cleanup\nPlain useful change\nPlain useful change")
    assert "A12" not in notes and "T175" not in notes and "P5" not in notes and "C030" not in notes
    assert notes.count("- Plain useful change") == 1


def test_first_without_tags_uses_whole_history_and_still_excludes_merges(tmp_path, fake, monkeypatch):
    root, calls = fake
    original = pr.run_command

    def command(args, **kwargs):
        if args[:3] == ["git", "--no-pager", "describe"]:
            raise RuntimeError("No tags")
        if args[:3] == ["git", "--no-pager", "log"]:
            assert args[2:] == ["log", "--no-merges", "--format=%s", "a" * 40, "--"]
            calls.append((args, kwargs["cwd"], kwargs["timeout"]))
            return "Improve checked editing\nA982: private work\nKeep clear error messages"
        return original(args, **kwargs)

    monkeypatch.setattr(pr, "run_command", command)
    output = pr.prepare("0.1", tmp_path / "out", root=root, first=True)
    notes = (output / "RELEASE_NOTES.md").read_text(encoding="utf-8")
    assert "first release" in notes.lower() and "whole history" in notes
    assert "Improve checked editing" in notes and "Keep clear error messages" in notes
    assert "A982" not in notes and "private work" not in notes
    assert not any(args[:3] == ["git", "--no-pager", "describe"] for args, _, _ in calls)
    assert len(calls) == 4  # HEAD, full non-merge history, unchanged packager, HEAD consistency.
    assert (output / "SHA256SUMS.txt").read_text(encoding="utf-8") == hashlib.sha256(b"synthetic archive bytes").hexdigest() + "  BED-win-x64.zip\n"


@pytest.mark.parametrize("extra, expected", [([], False), (["--first"], True)])
def test_cli_first_is_only_an_explicit_flag(tmp_path, monkeypatch, extra, expected):
    seen = {}

    def prepare(version, parent, *, first=False):
        seen.update(version=version, parent=parent, first=first)
        return tmp_path / "not-created"

    monkeypatch.setattr(pr, "prepare", prepare)
    assert pr.main(["0.1", "--out", str(tmp_path), *extra]) == 0
    assert seen == {"version": "0.1", "parent": tmp_path, "first": expected}
    assert not (tmp_path / "not-created").exists()


def test_first_notes_keep_subjects_untrusted_and_default_notes_unchanged():
    subjects = "A982: private work\n<plain> `literal` [label]\nUseful change\nUseful change"
    notes = pr.draft_notes("0.1", subjects, first=True)
    assert "first release" in notes.lower() and "whole history" in notes
    assert "A982" not in notes and "private work" not in notes
    assert "`" not in notes and "<" not in notes and ">" not in notes
    assert "[" not in notes and "]" not in notes
    assert notes.count("- Useful change") == 1
    assert "Not published or approved" in notes
    assert "first release" not in pr.draft_notes("0.1", "Useful change").lower()
