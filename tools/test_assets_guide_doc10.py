"""Pin the novice asset guide's named controls and messages to the merged UI sources."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent


def test_named_asset_controls_and_messages_match_the_actual_wrappers():
    guide = (ROOT / "docs/guide/assets.md").read_text(encoding="utf-8-sig")
    panel = (ROOT / "src/Brutal.Authoring.Editor.ImGui/AssetsPanel.cs").read_text(encoding="utf-8-sig")
    labels = re.findall(r'Ui\.(?:InputText|Checkbox|TextWrapped)\("([^"\n]+)"u8', panel)
    for label in ("Search assets", "Problems only", "Not imported", "Source file found. Placement and export need separate checks."):
        assert label in labels, f"Assets UI label changed: {label}"
        assert f"**{label}**" in guide, f"Guide label changed: {label}"
    for message in ("No imported assets.", "No matching assets."):
        assert f'"{message}"' in panel
        assert f"**{message}**" in guide


def test_texture_and_part_details_are_the_real_displayed_prefixes_and_types():
    guide = (ROOT / "docs/guide/assets.md").read_text(encoding="utf-8-sig")
    panel = (ROOT / "src/Brutal.Authoring.Editor.ImGui/AssetsPanel.cs").read_text(encoding="utf-8-sig")
    for prefix in ("Texture thumbnail:", "Size:", "Texture preview unavailable:", "Model:"):
        assert re.search(r'\$"' + re.escape(prefix), panel), f"Details text changed: {prefix}"
        assert f"**{prefix}**" in guide
    for extension, label in ((".OBJ", "Model (OBJ)"), (".GLTF", "Model (glTF)"), (".GLB", "Model (GLB)")):
        assert f'"{extension}" => "{label}"' in panel
        assert f"**{label}**" in guide
    renderer = (ROOT / "src/Brutal.Authoring.Editor.ImGui/ViewportView.cs").read_text(encoding="utf-8-sig")
    fallback = "Material textures are not displayed."
    assert fallback in renderer
    assert f"**{fallback}**" in guide


def test_placement_label_is_still_a_real_button_and_the_guide_names_no_games():
    guide = (ROOT / "docs/guide/assets.md").read_text(encoding="utf-8-sig")
    placement = (ROOT / "src/Brutal.Authoring.Editor.ImGui/DataWorkspace.SceneAssets.cs").read_text(encoding="utf-8-sig")
    assert 'Ui.Button("Add selected object"u8)' in placement
    assert "**Add selected object**" in guide
    for name in ("SceneStarterProject", "SceneStarter"):
        assert name.casefold() not in guide.casefold()
    for target in re.findall(r'\]\(([^)]+)\)', guide):
        assert (ROOT / "docs/guide" / target).resolve().is_file(), f"Broken guide link: {target}"
