"""Prepared packaging-text tests only; never publish, run an application or install the runtime."""
from pathlib import Path

import pytest
import package_release as pr

REQUIREMENTS = (
    "Visual C++ Redistributable (x64)", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll",
    "https://aka.ms/vs/17/release/vc_redist.x64.exe",
)


def test_a378_source_instructions_name_the_prerequisite_without_claiming_nothing_to_install():
    text = (pr.ROOT / "docs" / "release" / "START_HERE.txt").read_text(encoding="utf-8")
    assert all(item in text for item in REQUIREMENTS)
    assert "Nothing to install" not in text


def test_a378_packaging_copies_instructions_but_no_installer(tmp_path: Path, monkeypatch):
    source = tmp_path / "source"
    document = source / "docs" / "release" / "START_HERE.txt"
    document.parent.mkdir(parents=True)
    text = "Synthetic instructions\n" + "\n".join(REQUIREMENTS) + "\n"
    document.write_text(text, encoding="utf-8")
    stage = tmp_path / "stage"
    stage.mkdir()
    monkeypatch.setattr(pr, "ROOT", source)
    pr.copy_start_here(stage)
    assert (stage / "START_HERE.txt").read_text(encoding="utf-8") == text
    assert sorted(path.name for path in stage.iterdir()) == ["START_HERE.txt"]


@pytest.mark.parametrize("missing", REQUIREMENTS)
def test_a378_missing_prerequisite_wording_fails_before_writing(tmp_path: Path, monkeypatch, missing):
    source = tmp_path / "source"
    document = source / "docs" / "release" / "START_HERE.txt"
    document.parent.mkdir(parents=True)
    document.write_text("\n".join(item for item in REQUIREMENTS if item != missing), encoding="utf-8")
    stage = tmp_path / "stage"
    stage.mkdir()
    monkeypatch.setattr(pr, "ROOT", source)
    with pytest.raises(RuntimeError, match=r"Visual C\+\+ prerequisite"):
        pr.copy_start_here(stage)
    assert list(stage.iterdir()) == []


def test_a378_contradictory_installation_claim_is_refused(tmp_path: Path, monkeypatch):
    source = tmp_path / "source"
    document = source / "docs" / "release" / "START_HERE.txt"
    document.parent.mkdir(parents=True)
    document.write_text("\n".join(REQUIREMENTS) + "\nNothing to install", encoding="utf-8")
    stage = tmp_path / "stage"
    stage.mkdir()
    monkeypatch.setattr(pr, "ROOT", source)
    with pytest.raises(RuntimeError):
        pr.copy_start_here(stage)
    assert list(stage.iterdir()) == []
