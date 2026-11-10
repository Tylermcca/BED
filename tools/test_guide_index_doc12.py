"""Keep the newcomer index complete as guides are added, renamed or removed."""
from pathlib import Path, PurePosixPath
import re

import pytest

ROOT = Path(__file__).resolve().parent.parent
ENTRY = re.compile(r"^(\d+)\. \[([^\]\n]+)\]\(([^)\n]+)\) - (\S[^\n]*)$", re.MULTILINE)


def validate_index(root, text):
    folder = Path(root) / "docs/guide"
    expected = {path.relative_to(folder).as_posix() for path in folder.rglob("*.md") if path != folder / "README.md"}
    entries = ENTRY.findall(text)
    assert entries, "No one-line guide entries found"
    targets = [entry[2] for entry in entries]
    assert len(targets) == len(set(targets)), "A guide is listed twice"
    for target in targets:
        path = PurePosixPath(target)
        assert not path.is_absolute() and ".." not in path.parts and ":" not in target, f"Guide link escapes the guide folder: {target}"
        assert (folder / target).is_file(), f"Broken guide link: {target}"
    assert set(targets) == expected, f"Missing guides: {sorted(expected - set(targets))}; extra links: {sorted(set(targets) - expected)}"
    assert [int(entry[0]) for entry in entries] == list(range(1, len(entries) + 1)), "Keep the newcomer order numbered consecutively"
    return targets


def test_every_guide_has_one_existing_index_entry_and_readme_links_to_it():
    text = (ROOT / "docs/guide/README.md").read_text(encoding="utf-8-sig")
    targets = validate_index(ROOT, text)
    assert targets.index("editing_data.md") < targets.index("levels.md") < targets.index("playing.md") < targets.index("exporting.md")
    assert targets.index("exporting.md") < targets.index("api_overview.md") < targets.index("writing_a_plugin.md")
    assert "[guide index](docs/guide/README.md)" in (ROOT / "README.md").read_text(encoding="utf-8-sig")


def test_new_guide_missing_from_index_is_reported(tmp_path):
    folder = tmp_path / "docs/guide"
    folder.mkdir(parents=True)
    (folder / "first.md").write_text("# First\n", encoding="utf-8")
    validate_index(tmp_path, "1. [First](first.md) - Begin here.\n")
    (folder / "next.md").write_text("# Next\n", encoding="utf-8")
    with pytest.raises(AssertionError, match="Missing guides.*next.md"):
        validate_index(tmp_path, "1. [First](first.md) - Begin here.\n")


@pytest.mark.parametrize("entry, message", [
    ("1. [Gone](gone.md) - Missing file.\n", "Broken guide link"),
    ("1. [First](first.md) - Begin.\n2. [Again](first.md) - Duplicate.\n", "listed twice"),
    ("1. [Outside](../outside.md) - Wrong folder.\n", "escapes"),
])
def test_index_refuses_broken_duplicate_or_outside_entries(tmp_path, entry, message):
    folder = tmp_path / "docs/guide"
    folder.mkdir(parents=True)
    (folder / "first.md").write_text("# First\n", encoding="utf-8")
    with pytest.raises(AssertionError, match=message):
        validate_index(tmp_path, entry)
