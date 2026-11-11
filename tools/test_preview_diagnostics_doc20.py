"""Pin the guides to the diagnostic helper, checked navigation and admitted audio UI."""
from pathlib import Path
import json
import re
import pytest

ROOT = Path(__file__).resolve().parent.parent


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def validate_diagnostic_example(guide, formatter, parser):
    prefix = re.search(r'const string prefix="([^"]+)"', parser)[1]
    limit = int(re.search(r"GetByteCount\(result\)>([0-9]+)", formatter)[1])
    assert f"At most {limit} UTF-8 bytes" in guide, "Diagnostic byte-limit drift"
    assert f'"{prefix}"' in formatter
    wire = re.search(r"```text\n([^\n]+)\n```", guide)[1]
    assert wire.startswith(prefix)
    obj = json.loads(wire[len(prefix):])
    assert set(obj) == {"file", "pointer", "message"} and all(isinstance(v, str) for v in obj.values())
    arguments = re.search(r'ProjectDiagnostic.Format\(\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)', guide).groups()
    assert arguments == tuple(obj[k] for k in ("file", "pointer", "message")), "Helper and wire examples differ"
    assert len(wire.encode("utf-8")) <= limit
    assert "using Bed.Runtime;" in guide
    assert "public static string Format(string file,string pointer,string message)" in formatter
    assert "fields.Length!=3" in parser and "JsonValueKind.String" in parser


def test_diagnostic_helper_wire_example_and_byte_limit_agree():
    validate_diagnostic_example(read("docs/guide/play_diagnostics.md"),
        read("src/Bed.Runtime/ProjectDiagnostic.cs"),
        read("src/Bed.Editor.ImGui/PlayDiagnostic.cs"))


def test_changed_limit_or_one_sided_example_edit_is_reported():
    guide = read("docs/guide/play_diagnostics.md")
    formatter = read("src/Bed.Runtime/ProjectDiagnostic.cs")
    parser = read("src/Bed.Editor.ImGui/PlayDiagnostic.cs")
    with pytest.raises(AssertionError, match="byte-limit drift"):
        validate_diagnostic_example(guide, formatter.replace("4096", "4095"), parser)
    with pytest.raises(AssertionError, match="examples differ"):
        validate_diagnostic_example(guide.replace('"/items/0/name"', '"/items/1/name"', 1), formatter, parser)


def test_diagnostic_navigation_keeps_saved_input_and_admission_guards():
    guide = read("docs/guide/play_diagnostics.md")
    source = read("src/Bed.Editor.ImGui/DataWorkspace.PlayConsole.cs")
    for anchor in ("ProjectDirty()", "CheckSnapshot.SignatureOf(Project)", "Project.Find(file) is null", "Project.Paths.Refusal(file)", "RevealFindingPlace", "Ui.TextLink", "Copy all"):
        assert anchor in source
    for claim in ("saved input has changed", "Only files admitted", "Don't Save", "Back to previous location", "Copy all", "malformed", "~0", "~1", "BED_PROJECT"):
        assert claim in guide


def test_sound_controls_formats_and_limits_match_the_shipped_preview():
    guide = read("docs/guide/sound_preview.md")
    panel = read("src/Bed.Editor.ImGui/AssetsPanel.cs")
    wav = read("src/Bed.Editor.App/WavePreviewDecoder.cs")
    ogg = read("src/Bed.Editor.App/OpenAlAssetAudioPreview.cs")
    for label in ("Play sound", "Stop sound"):
        assert f'Ui.Button("{label}"u8)' in panel and f"**{label}**" in guide
    for key in ("F6", "Escape"):
        assert f"ImGuiKey.{key}" in panel and f"**{key}**" in guide
    for field in ("Encoded", "Decoded"):
        mib = re.search(rf"Max{field}Bytes=([0-9]+)\*1024\*1024", wav)[1]
        assert f"At most {mib} MiB" in guide
    assert "rate is 0 or >192000" in wav and "reader.SampleRate is <1 or >192000" in ogg
    assert "1 to 192000 Hz" in guide
    assert "tag==1&&bits is 8 or 16" in wav and "tag==3&&bits==32" in wav
    for claim in ("PCM 8-bit or 16-bit", "IEEE float32", "Opus", "chained streams", "Mono or stereo", "Selection alone does not start", "closing the project", "without audio preview"):
        assert claim in guide
    assert 'browser.Index.Resolve(entry.Guid)' in panel
    assert 'reader.Streams.Count != 1 || reader.FindNextStream()' in ogg


def test_guide_links_and_import_command_are_real():
    for name in ("play_diagnostics.md", "sound_preview.md"):
        for target in re.findall(r"\]\(([^)]+)\)", read("docs/guide/" + name)):
            assert (ROOT / "docs/guide" / target.split("#")[0]).is_file()
    cli = read("src/Bed.Agent.Cli/Program.cs")
    assert '"import" => session.Import' in cli
    assert '"--assets"' in cli and '"--ai"' in cli
    assert "bed-cli --data data --schemas schemas --assets assets import chirp.wav ours --ai forbidden" in read("docs/guide/sound_preview.md")
