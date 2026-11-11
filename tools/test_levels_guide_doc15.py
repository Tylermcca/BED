"""Keep the scene-editing guide tied to the actual merged controls and command bindings."""
from pathlib import Path
import re

import pytest

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "src/Bed.Editor.ImGui"


def read(path):
    return path.read_text(encoding="utf-8-sig")


@pytest.mark.parametrize("action,chord,label", [
    ("scene.duplicate", "Ctrl+D", "Duplicate"),
    ("scene.copy", "Ctrl+C", "Copy"),
    ("scene.paste", "Ctrl+V", "Paste"),
    ("scene.group", "Ctrl+G", "Group"),
    ("scene.ungroup", "Ctrl+Shift+G", "Ungroup"),
    ("editor.rename_object", "F2", "Rename object"),
    ("scene.remove", "Delete", "Delete"),
])
def test_every_named_edit_command_retains_its_actual_default_and_menu_label(action, chord, label):
    guide = read(ROOT / "docs/guide/levels.md")
    sources = read(UI / "EditorWindow.cs") + read(UI / "DataWorkspace.Clipboard.cs")
    assert re.search(r'\.Default\("' + re.escape(action) + r'"\s*,\s*"' + re.escape(chord) + r'"\)', sources), action
    assert re.search(r'(?:Item|Choice)\("' + re.escape(label) + r'"[^\n]*"' + re.escape(action) + r'"', sources)
    assert f"**{chord}**" in guide
    assert f"**{label}**" in guide or f"**Edit > {label}**" in guide


@pytest.mark.parametrize("file,label,wrapper", [
    ("ViewportView.cs", "Move (G)", "SmallButton"),
    ("ViewportView.cs", "Rotate (R)", "SmallButton"),
    ("ViewportView.cs", "Scale (S)", "SmallButton"),
    ("ViewportView.cs", "Snap (B)", "Checkbox"),
    ("ViewportView.cs", "Grid step (m)", "BeginCombo"),
    ("EditorWindow.cs", "Search objects", "InputText"),
    ("SceneDeleteUi.cs", "Delete anyway", "Button"),
    ("SceneDeleteUi.cs", "Cancel", "Button"),
    ("DataWorkspace.SceneAssets.cs", "Add selected object", "Button"),
    ("DataWorkspace.SceneAssets.cs", "Cancel placement", "Button"),
])
def test_named_labels_are_still_actual_controls(file, label, wrapper):
    guide = read(ROOT / "docs/guide/levels.md")
    source = read(UI / file)
    if label == "Search objects":
        # The same named input now has a full-width field and its visible caption above it.
        named = re.search(r'Ui.NameNextItem\("([^"\n]+)"u8,\s*"Search objects"\)', source)
        assert named and f'Ui.InputText("{named[1]}"u8' in source
        assert 'Ui.TextWrapped("Search objects"u8)' in source
    else:
        assert f'Ui.{wrapper}("{label}"u8' in source
    assert f"**{label}**" in guide


def test_rename_field_search_and_delete_decision_keep_their_real_names():
    guide = read(ROOT / "docs/guide/levels.md")
    assert '"Object ID"' in read(UI / "SceneRenameUi.cs")
    assert "**Object ID**" in guide
    assert 'Ui.Begin("Delete referenced object?###scene.delete.decision"u8' in read(UI / "SceneDeleteUi.cs")
    assert "**Delete referenced object?**" in guide
    assert "Ctrl+F searches names, assets and properties. Escape clears." in read(UI / "EditorWindow.cs")
    assert "**Ctrl+F**" in guide and "**Escape** clears the filter" in guide
    assert "Shift+Up/Down extends selection. Ctrl+Space toggles the focused object." in read(UI / "EditorWindow.cs")
    assert "**Shift+Up/Down**" in guide and "**Ctrl+Space**" in guide


def test_grid_choices_and_nudge_snapping_match_the_implemented_viewport():
    guide = read(ROOT / "docs/guide/levels.md")
    viewport = read(UI / "ViewportView.cs")
    assert "new[]{.1f,.25f,.5f,1f}" in viewport.replace(" ", "")
    assert "ImGuiKey.B, false" in viewport and "MathF.PI/12" in viewport
    assert "Grid.Size.X : 0.5f" in viewport
    for phrase in ("0.1, 0.25, 0.5 and 1 metre", "15 degrees", "0.5 metre", "**Alt**"):
        assert phrase in guide


def test_world_offset_and_project_clipboard_promises_keep_their_implementation():
    duplicate = read(UI / "SceneDuplicateUi.cs")
    if "SceneObjectOperations.Duplicate(" in duplicate:
        # The CLI/editor planner refactor retains the same offset and selection promise.
        assert "=>SceneObjectOperations.Duplicate(editor,duplicate,out why);" in duplicate
        duplicate = read(ROOT / "src/Bed.Editor/SceneObjectOperations.cs")
    clipboard = read(UI / "SceneClipboard.cs")
    assert "Vector3.UnitX" in duplicate and "Vector3.UnitX" in clipboard
    assert "editor.Select(copies)" in duplicate and "editor.Select(ids)" in clipboard
    workspace = read(UI / "DataWorkspace.cs")
    assert "ReleaseObjectClipboard()" in workspace
    assert "objectClipboard.Clear()" in read(UI / "DataWorkspace.Clipboard.cs")
    guide = read(ROOT / "docs/guide/levels.md")
    assert "1 metre along world X" in guide
    assert "Opening another project clears this clipboard." in guide


def test_guide_links_resolve_and_sample_names_do_not_leak_into_prose():
    guide = read(ROOT / "docs/guide/levels.md")
    # Public sample identifiers are not needed in this task guide either.
    for project in ("SceneStarterProject", "SceneStarter"):
        assert project.casefold() not in guide.casefold()
    for target in re.findall(r'\]\(([^)]+)\)', guide):
        assert (ROOT / "docs/guide" / target.split("#")[0]).resolve().is_file(), target
