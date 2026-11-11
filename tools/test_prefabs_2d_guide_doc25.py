"""Keep the partial native prefab/flat guides tied to merged controls and limits."""
from pathlib import Path
import re
import pytest

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "src/Bed.Editor.ImGui"


def read(path):
    return path.read_text(encoding="utf-8-sig")


@pytest.mark.parametrize("native,accessible", [
    ("Refresh prefabs", "Refresh prefab sources"),
    ("Review update", "Review source update"),
    ("Duplicate##instance", "Duplicate instance"),
    ("Add empty", "Add empty object"),
])
def test_short_visible_labels_and_full_accessible_names_match_the_guide(native, accessible):
    source = read(UI / "DataWorkspace.Scene2.cs")
    guide = read(ROOT / "docs/guide/prefabs.md")
    assert f'Ui.NameNextItem("{native}"u8,"{accessible}")' in source
    assert f'Ui.Button("{native}"u8)' in source
    visible = native.split("##")[0]
    assert f"**{visible}**" in guide
    assert f"**{accessible}**" in guide


@pytest.mark.parametrize("label", ["Place prefab", "Break Link", "Delete instance", "Group selection", "Duplicate objects", "Rename object", "Delete object", "Accept update", "Cancel update", "Back"])
def test_prefab_actions_are_still_real_native_buttons(label):
    assert f'Ui.Button("{label}"u8)' in read(UI / "DataWorkspace.Scene2.cs")
    assert f"**{label}**" in read(ROOT / "docs/guide/prefabs.md")


def test_slots_revert_and_update_review_keep_their_actual_meanings():
    native = read(UI / "DataWorkspace.Scene2.cs")
    inspector = read(UI / "InspectorController.cs")
    guide = read(ROOT / "docs/guide/prefabs.md")
    assert 'Choose("Prefab",ref prefabIndex,prefabNames)' in native
    assert 'Choose("Bind "+slot.Label+(slot.Required?" (required)":" (optional)")' in native
    assert 'missingSlot.Label' in native and 'slotBindings.ContainsKey(s.Key)' in native
    assert 'Ui.NameNextItem("Revert"u8,"Revert "+row.Label)' in inspector
    assert 'Ui.Begin("Review prefab update###scene2.update"u8' in native
    assert 'const int pageSize=32,textSize=1500' in native
    assert 'changes.Add(text)' in native
    for label in ('Next changes', 'Previous changes', 'Next text', 'Previous text'):
        assert f'Ui.Button("{label}"u8)' in native and f'**{label}**' in guide
    for text in ("**Prefab**", "**Bind**", "**(required)**", "**Choose target**", "**Revert Position X**", "**Review prefab update**", "32 per page", "1,500 characters"):
        assert text in guide
    assert "one undo step" in guide and "never silently replaces" in guide


@pytest.mark.parametrize("label", ["Sprite width", "Sprite height", "Pivot X", "Pivot Y", "Draw order", "Filter", "Rotation Z", "Inside"])
def test_saved_sprite_field_names_stay_in_the_native_inspector(label):
    assert f'"{label}"' in read(UI / "Scene2InspectorType.cs")
    assert f"**{label}**" in read(ROOT / "docs/guide/flat_levels.md")


@pytest.mark.parametrize("key", ["W", "R", "S", "F", "Home", "Escape"])
def test_flat_canvas_keys_have_actual_handlers(key):
    assert f'ImGuiKey.{key},false' in read(UI / "Scene2FlatView.cs")
    assert f"**{key}**" in read(ROOT / "docs/guide/flat_levels.md")


def test_flat_controls_density_and_rendering_limits_are_current():
    native = read(UI / "DataWorkspace.Scene2.cs")
    flat = read(UI / "Scene2FlatView.cs")
    defaults = read(ROOT / "src/Bed.Editor/SpriteAssetDefaults.cs")
    guide = read(ROOT / "docs/guide/flat_levels.md")
    for label in ("Refresh images", "Add sprite"):
        assert f'Ui.Button("{label}"u8)' in native
        assert f"**{label}**" in guide
    assert 'Choose("Image",ref imageIndex,imageNames)' in native
    assert 'Ui.Checkbox("Snap"u8' in flat and 'Ui.InputDouble("Grid (m)"u8' in flat
    assert 'MathF.PI/12' in flat and 'MathF.Round(factor*10)/10' in flat
    assert 'new(100,new(.5f,.5f),SceneSpriteFilter.Nearest)' in defaults
    assert 'Image preview unavailable; showing sprite bounds.' in flat
    assert 'presentation.TrySpriteImage' in flat and 'draw.AddImageQuad' in flat
    for phrase in ("Vulkan flat view", "transparent overlap", "OpenGL fallback", "100 pixels per metre", "**Grid (m)**", "15 degrees", "tenths", "transparent-pixel picks", "Mixed 2D/3D rendering"):
        assert phrase in guide
    assert "No asset is found by guessing a filename" in guide


def test_new_guides_are_linked_and_every_relative_guide_link_resolves():
    index = read(ROOT / "docs/guide/README.md")
    levels = read(ROOT / "docs/guide/levels.md")
    for name in ("prefabs.md", "flat_levels.md"):
        assert f"]({name})" in index and f"]({name})" in levels
        guide = read(ROOT / "docs/guide" / name)
        assert "version-two" in guide
        for link in re.findall(r"\]\(([^)]+)\)", guide):
            assert (ROOT / "docs/guide" / link.split("#")[0]).resolve().is_file(), link


def test_flat_handles_and_personal_camera_match_the_native_editor():
    flat = read(UI / "Scene2FlatView.cs")
    native = read(UI / "DataWorkspace.Scene2.cs")
    guide = read(ROOT / "docs/guide/flat_levels.md")
    for label in ("Move X axis", "Move Y axis", "Scale local X axis", "Scale local Y axis", "Scale XY handle", "Rotate Z ring"):
        assert label in flat
    assert "world XY" in guide and "local axes" in guide and "Z ring" in guide
    assert "cameraPreferences?.Remember" in flat and "BindFlatCamera" in native
    assert "personal view preferences" in guide and "undo history" in guide
    assert "DropTarget=at=>Owner.scenePlacement?.DrawTargetAt(at)" in native
    assert "drag an admitted PNG" in guide and "3D models use" in guide


def test_scene2_clipboard_is_registered_and_guides_keep_complete_snapshot_and_reference_rules():
    native = read(UI / "DataWorkspace.Clipboard.cs")
    core = read(ROOT / "src/Bed/Scene2Commands.cs")
    guide = read(ROOT / "docs/guide/prefabs.md")
    assert "BindScene2Clipboard" in native and "scene2Clipboard.Paste" in native
    assert '.Register<Scene2Paste>("scene2.paste",1' in core
    for phrase in ("**Ctrl+C**", "**Ctrl+V**", "whole linked piece", "one undo step", "external target", "Parent links are never silently detached", "same 3D or flat", "system text clipboard"):
        assert phrase in guide
    assert "| `paste` |" in read(ROOT / "docs/guide/api_overview.md")
    assert "`scene2.paste`" in read(ROOT / "docs/api/0_2_1_api_review.md")


def test_model_drag_guide_uses_the_existing_token_and_checked_scene2_plan():
    native = read(UI / "DataWorkspace.Scene2.cs")
    drop = read(UI / "DataWorkspace.Scene2AssetDrop.cs")
    token = read(UI / "AssetPlacement.cs")
    guide = read(ROOT / "docs/guide/prefabs.md")
    assert "Viewport.DropTarget=Owner.scenePlacement" in native
    assert "workspace.CaptureScene2AssetDrop" in token and "workspace.Scene2AssetAnchor" in token
    assert "model.TryParts" in drop
    assert "Documents.CaptureScene2Edit" in drop and "Documents.CommitScene2Edit" in drop
    for phrase in ("**Assets**", "**Viewport**", "view centre captured", "local position zero", "internal part refuses", "one undo step", "never saves"):
        assert phrase in guide
    assert "model dragging into 3D levels is not connected" not in read(ROOT / "docs/guide/flat_levels.md")
