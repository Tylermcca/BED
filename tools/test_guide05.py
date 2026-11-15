"""New 0.5 reader guides: real controls, honest limits, plain words and usable links."""
from pathlib import Path
import re
import pytest

ROOT=Path(__file__).resolve().parent.parent
PAGES=("connect_an_ai_app.md","build_game.md","see_the_game.md","live_values.md","open_game_code.md","selling_a_build.md")
FORBIDDEN=re.compile(r"\b(?:headless|MCP|stdio|config(?:uration)?|JSON-RPC)\b",re.I)
def read(path):return (ROOT/path).read_text(encoding="utf-8-sig")

@pytest.mark.parametrize("name",PAGES)
def test_new_reader_page_has_plain_words_and_existing_links(name):
    path=ROOT/"docs/guide"/name;text=path.read_text(encoding="utf-8-sig")
    assert text.startswith("# ") and len(text.split())<500
    assert FORBIDDEN.search(text) is None
    for target in re.findall(r"\]\(([^)]+)\)",text):
        assert not re.match(r"[a-z]+:",target),"Reader steps should not depend on a changing external page"
        assert (path.parent/target.split("#")[0]).is_file(),target

def test_named_controls_and_release_files_are_present_in_current_sources():
    cases={
      "connect_an_ai_app.md": [("Connect an AI app","EditorWindow.cs"),("Refresh preview","AiConnectPanel.cs"),("Remove connection","AiConnectPanel.cs")],
      "build_game.md": [("Build saved files","ProjectBuildPanel.cs"),("Allow this exact command and build","ProjectBuildPanel.cs"),("Cancel build","ProjectBuildPanel.cs")],
      "live_values.md": [("Start live values","DataWorkspace.LiveValues.cs"),("Try jump strength (1-25)","samples/SceneStarterAdapter/LiveValues.cs"),("Keep this value","DataWorkspace.LiveValues.cs")],
      "open_game_code.md": [("Open game code","EditorWindow.cs"),("Code editor","Locations.cs")],
    }
    for name,controls in cases.items():
        guide=read("docs/guide/"+name)
        for label,source in controls:
            # A game-declared label (live values, since the opt-in adapter hook) lives in that game's adapter.
            assert label in guide and label in read(source if "/" in source else "src/Bed.Editor.ImGui/"+source),label
    guide=read("docs/guide/selling_a_build.md");source=read("src/Bed.Editor.App/ExportReleaseFiles.cs")
    for name in ("CREDITS.txt","NOTICE.txt","BRUTAL-RELEASE-CHECKLIST.txt","LOGO_BRUTAL_WHT.png","STEAM-AI-DISCLOSURE-DRAFT.txt"):
        assert name in guide and name in source,name

def test_trial_save_observation_and_windows_limits_remain_explicit():
    live=read("docs/guide/live_values.md")
    assert "garden sample" in live and "saved level stays unchanged" in live and "confirmed jump strength" in live and "build again" in live
    observation=read("docs/guide/see_the_game.md")
    assert "five seconds of game time" in observation and "pauses between requests" in observation and "does not prove" in observation
    assert "seconds>5" in read("src/Bed.Editor/PlayObservation.cs")
    code=read("docs/guide/open_game_code.md")
    assert "Windows has no shared way to pass a line number" in code and "file changed after the build" in code
    selling=read("docs/guide/selling_a_build.md")
    assert "unknown" in selling and "do not approve" in selling and "does not tell you whether AI made it" in selling

def test_preview_added_list_describes_each_new_feature_without_jargon():
    # These guides describe the 0.5 preview; its feature notes moved out of Unreleased when the draft was prepared.
    text=read("CHANGELOG.md").split("## [0.5.0-preview.1]",1)[1].split("\n## [",1)[0]
    assert "### Added" in text and "### Fixed" in text and FORBIDDEN.search(text) is None
    for feature in ("Connect an AI app","Build game","pictures","Live values","Open game code","disclosure worksheet"):
        assert feature in text,feature
