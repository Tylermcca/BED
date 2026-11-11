"""Keep the fixed-tick how-to tied to the executable sample and late-hello contract."""
from pathlib import Path
import re
import pytest

ROOT = Path(__file__).resolve().parent.parent


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def check_sample(guide, source):
    expected = source[source.index("while(watch.Elapsed.TotalSeconds<seconds)"):source.index('Console.WriteLine($"Completed')].strip()
    documented = re.search(r"<!-- sample-loop:start -->\n```csharp\n(.*?)\n```\n<!-- sample-loop:end -->", guide, re.S)[1]
    assert documented == expected, "Sample loop changed; review the guide"
    millis = re.search(r"Thread.Sleep\(([0-9]+)\)", expected)[1]
    assert f"{millis} ms tick" in guide
    assert source.index("RouteCatalog.Read(") < source.index("FrameControl.ConnectFromEnvironment()")


def test_documented_loop_is_the_real_saved_data_sample():
    check_sample(read("docs/guide/pause_step.md"), read("samples/RoutePuzzle.Game/Program.cs"))


def test_an_edited_loop_is_reported():
    with pytest.raises(AssertionError, match="Sample loop changed"):
        check_sample(read("docs/guide/pause_step.md").replace("Thread.Sleep(20)", "Thread.Sleep(30)"), read("samples/RoutePuzzle.Game/Program.cs"))


def test_initial_wait_and_command_deadline_match_the_server():
    guide = read("docs/guide/pause_step.md")
    server = read("src/Bed.Editor.App/ProjectFrameServer.cs")
    initial = server[server.index("private async Task Run("):server.index("private async Task WriteCommands(")]
    assert "WaitForConnectionAsync(lifetime.Token)" in initial
    assert "CancelAfter(" not in initial
    assert "This game hasn't enabled frame control yet." in server and "This game hasn't enabled frame control yet." in guide
    assert "as long as the owned child is alive" in guide
    deadline = re.search(r"sentAt>TimeSpan.FromSeconds\(([0-9]+)\)", server)[1]
    assert deadline == "5" and "five-second acknowledgement timeout" in guide
    assert "GetNamedPipeClientProcessId" in initial and "peer!=ownedProcessId" in initial


def test_permit_ack_and_keyboard_controls_are_the_shipped_contract():
    guide = read("docs/guide/pause_step.md")
    helper = read("src/Bed.Runtime/FrameControl.cs")
    bindings = read("src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs")
    for action, key in (("pause_simulation", "F9"), ("step_simulation", "F10"), ("resume_simulation", "F11")):
        assert f'Default("editor.{action}","{key}")' in bindings
        assert key in guide
    assert "public bool PollFrame()" in helper and "if(stepping is {} step)" in helper
    for claim in ("exactly one complete simulation tick", "next poll after a Step", "Extra render iterations", "empty permits", "Discard paused wall time", "each individual tick", "UI callbacks that mutate gameplay", "not retried", "Dispose the helper"):
        assert claim in guide


def test_local_guide_and_sample_links_resolve():
    for target in re.findall(r"\]\(([^)]+)\)", read("docs/guide/pause_step.md")):
        assert (ROOT / "docs/guide" / target.split("#")[0]).is_file(), target
