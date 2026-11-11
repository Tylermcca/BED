"""Keep user-facing accessibility routes tied to the shipped controls and bridge."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def test_scale_choices_and_theme_labels_match_both_preferences_surfaces():
    guide = read("docs/guide/accessibility.md")
    for path in ("src/Bed.Editor.ImGui/EditorWindow.cs",
                 "src/Bed.Editor.ImGui/ProjectLauncher.Start.cs"):
        source = read(path)
        choices = re.search(r"new\[\] \{ ([0-9, ]+) \}", source[source.index('"UI scale"'):])
        assert choices
        assert [int(x) for x in choices[1].split(",")] == [100, 150, 200]
        for percent in choices[1].split(","):
            assert f"**{percent.strip()}%**" in guide
    assert 'Choice("High contrast"' in read("src/Bed.Editor.ImGui/EditorWindow.cs")
    assert '"High contrast"' in read("src/Bed.Editor.ImGui/EditorTheme.cs")
    assert "**Edit > Preferences > UI scale**" in guide
    assert "**Edit > Preferences > Theme > High contrast**" in guide


def test_keyboard_walk_remains_backed_by_native_example_test():
    guide = read("docs/guide/accessibility.md")
    proof = read("tests/Bed.Tests/NewcomerKeyboardQa24Tests.cs")
    for label in ("Try the example", "Add selected object", "Viewport"):
        assert label in guide and label in proof
    for chord in ("Insert", "Ctrl+S", "F5"):
        assert chord in guide and f'Key("{chord}")' in proof
    assert 'Key("X");Key("RightArrow")' in proof
    assert "Press X, then Right Arrow" in guide
    assert "Enter to enter the list and Escape" in guide


def test_bridge_actions_and_support_limits_are_documented_without_old_denial():
    guide = read("docs/guide/accessibility.md")
    bridge = read("src/Bed.Editor.App/AccessKitWindowBridge.cs")
    for action in ("Focus", "SetValue", "Click"):
        assert f"AccessKitNativeActionKind.{action}" in bridge
    for action in ("Invoke", "Toggle", "Select"):
        assert f"UiActions.{action}" in bridge
    for claim in ("Windows x64", "AccessKit", "Windows UI Automation", "read-only",
                  "complete real Windows screen-reader walkthrough", "has not been completed",
                  "custom plug-in", "does not expose every piece of geometry",
                  "does not change Windows settings", "not certified"):
        assert claim in guide
    fallback = read("docs/guide/using_a_screen_reader.md")
    assert "[Accessibility](accessibility.md)" in fallback
    assert "has no accessibility tree" not in fallback
    assert "bridge remains future work" not in fallback


def test_accessibility_links_resolve():
    for name in ("accessibility.md", "using_a_screen_reader.md"):
        text = read("docs/guide/" + name)
        for target in re.findall(r"\]\(([^)]+)\)", text):
            assert (ROOT / "docs/guide" / target.split("#")[0]).is_file(), target
