"""Pin the command-line guide to both argument parsers, including future additions."""
from pathlib import Path
import re

import pytest

ROOT = Path(__file__).resolve().parent.parent
BED = ROOT / "src/Bed.Launcher/Program.cs"
CLI = ROOT / "src/Bed.Agent.Cli/Program.cs"
GUIDE = ROOT / "docs/guide/command_line.md"
TICK = chr(96)


def section(text, heading):
    return text.split("### " + heading + "\n", 1)[1].split("\n### ", 1)[0].split("\n## ", 1)[0]


def cells(text):
    return re.findall(r"^\| " + TICK + r"([^" + TICK + r"]+)" + TICK + r" \|", text, re.M)


def parser_options(source):
    # Quoted literals pin actual accepted or explicitly refused flags, not prose in Usage.
    return set(re.findall(r'"(--[a-z][a-z-]*|-h|/\?)"', source))


def validate_options(source, documented):
    assert set(documented) == parser_options(source), (
        f"Undocumented: {sorted(parser_options(source) - set(documented))}; "
        f"not in parser: {sorted(set(documented) - parser_options(source))}"
    )
    assert len(documented) == len(set(documented)), "Duplicate option row"


@pytest.mark.parametrize("path, heading", [(BED, "BED options"), (CLI, "bed-cli options")])
def test_every_parser_option_and_alias_has_its_own_guide_row(path, heading):
    validate_options(path.read_text(encoding="utf-8-sig"),
                     cells(section(GUIDE.read_text(encoding="utf-8"), heading)))


def test_every_cli_dispatch_command_and_usage_signature_is_documented():
    source = CLI.read_text(encoding="utf-8-sig")
    text = GUIDE.read_text(encoding="utf-8")
    documented = cells(section(text, "bed-cli commands"))
    names = [row.split()[0] for row in documented]
    dispatch = set(re.findall(r'^\s*"([a-z][a-z-]*)" => session\.', source, re.M))
    dispatch.update(re.findall(r'rest\s*\[\s*0\s*\]\s*==\s*"([a-z][a-z-]*)"', source))
    assert set(names) == dispatch
    assert len(names) == len(set(names))
    usage = source.split('private const string Usage = """', 1)[1].split('""";', 1)[0]
    signatures = [re.split(r" {2,}", line[10:].strip(), maxsplit=1)[0]
                  for line in usage.splitlines() if re.match(r"^ {10}[a-z]", line)]
    # Wrapped explanations aren't commands; every dispatched command must have a Usage entry.
    expected = {row.split()[0]: " ".join(row.split()).replace("allowed|forbidden", "allowed or forbidden")
                for row in signatures if row.split()[0] in dispatch}
    assert set(expected) == dispatch
    assert dict(zip(names, documented)) == expected


def test_option_drift_rejects_both_a_missing_row_and_a_new_parser_flag():
    source = BED.read_text(encoding="utf-8-sig")
    documented = cells(section(GUIDE.read_text(encoding="utf-8"), "BED options"))
    with pytest.raises(AssertionError, match="Undocumented.*--shot"):
        validate_options(source, [row for row in documented if row != "--shot"])
    with pytest.raises(AssertionError, match="Undocumented.*--future-test-option"):
        validate_options(source + '\ncase "--future-test-option": break;', documented)


def test_guide_links_exist_and_probe_examples_use_the_real_argument_order():
    text = GUIDE.read_text(encoding="utf-8")
    for target in re.findall(r"\[[^\]]+\]\(([^)]+)\)", text):
        assert (GUIDE.parent / target).is_file(), target
    assert './BED.exe "./project" --shot "./frame.png"' in text
    assert './BED.exe --probe-play "./project" --probe-args "--seconds 0" --timeout 30' in text
    assert './BED.exe --probe-export "./project" --destination "./export" --timeout 60' in text
    assert "Import writes the asset record when accepted without requiring --save." in text
    assert "There is no separate one-shot save command." in text
    assert "must be used alone" in section(text, "BED options")


def test_scene_actions_and_json_shapes_match_the_shared_non_drawing_route():
    source = (ROOT / "src/Bed.Agent.Cli/SceneCommandCall.cs").read_text(encoding="utf-8")
    text = section(GUIDE.read_text(encoding="utf-8"), "Scene commands")
    actions = set(re.findall(r'"(add|move|duplicate|delete|rename|group)"', source))
    assert set(cells(text)) == actions == {"add", "move", "duplicate", "delete", "rename", "group"}
    assert "SceneObjectOperations.Group" in source
    assert "SceneObjectOperations.Duplicate" in source
    assert "EditorWindow" not in source
    for value in ["--preview", "--save", "world X", "Delete anyway", "scene", "version 1", '"newId"', '"ids"']:
        assert value in text
