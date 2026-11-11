"""Pin user-visible troubleshooting wording to actual current Play/Export code; no builds or game launches."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent.parent
GUIDE = ROOT / "docs/guide/troubleshooting.md"
MESSAGES = [
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'Set the program in Tools > Play settings', 'Set the program in Tools > Play settings'),
    ('src/Bed.Editor.ImGui/PlaySettingsForm.cs', 'The program does not exist. Choose it or configure its build command.', 'The program does not exist. Choose it or configure its build command.'),
    ('src/Bed.Editor/PlaySettingsStore.cs', 'The Play working folder does not exist.', 'The Play working folder does not exist.'),
    ('src/Bed.Editor/PlaySettingsStore.cs', 'Build tool was not found on PATH: ', 'Build tool was not found on PATH: <tool>'),
    ('src/Bed.Editor/PlaySettingsStore.cs', 'Build input patterns must stay inside the project.', 'Build input patterns must stay inside the project.'),
    ('src/Bed.Editor.ImGui/PlaySettingsForm.cs', ' must be a JSON list of quoted strings. Use [] for no items.', '<field> must be a JSON list of quoted strings. Use [] for no items.'),
    ('src/Bed.Editor/PlaySettingsStore.cs', 'Needs your OK: ', 'Needs your OK: <command> (working folder: <folder>)'),
    ('src/Bed.Editor/PlaySettingsStore.cs', 'No build command is configured.', 'No build command is configured.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'Play runs saved files. Save changes before Play?', 'Play runs saved files. Save changes before Play?'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'A save check is still running; try Play after it finishes.', 'A save check is still running; try Play after it finishes.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', '{file.Name} was changed outside BED and you have unsaved changes to it; choose which version to keep in the Changed outside BED box, then Play.', '<file> was changed outside BED and you have unsaved changes to it; choose which version to keep in the Changed outside BED box, then Play.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'Documents changed; choose Play again.', 'Documents changed; choose Play again.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'The documents or Play command changed; choose Play again.', 'The documents or Play command changed; choose Play again.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'Changes are still waiting to be saved.', 'Changes are still waiting to be saved.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlaySettings.cs', 'Stop the running program first.', 'Stop the running program first.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'The program is missing or older than its build inputs. Choose Build and Play.', 'The program is missing or older than its build inputs. Choose Build and Play.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'The program is missing. Set its program or build command in Play settings.', 'The program is missing. Set its program or build command in Play settings.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'Build succeeded but the program is still missing or stale.', 'Build succeeded but the program is still missing or stale.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'The build exceeded its declared deadline.', 'The build exceeded its declared deadline.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'The game stopped with error code {ExitCode}.', 'The game stopped with error code <number>.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'Play failed: ', 'Play failed: <reason>'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'Requested polite close; waiting up to 5 seconds.', 'Requested polite close; waiting up to 5 seconds.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'The program has no window to close, so BED ended it.', 'The program has no window to close, so BED ended it.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'Stopped the owned process tree after the 5-second grace period.', 'Stopped the owned process tree after the 5-second grace period.'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'Save changes before building.', 'Save changes before building.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.Export.cs', 'The saved file was changed outside BED; choose which version to keep before exporting.', 'The saved file was changed outside BED; choose which version to keep before exporting.'),
    ('src/Bed.Editor.ImGui/DataWorkspace.Export.cs', 'Open a file before exporting.', 'Open a file before exporting.'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'The build failed. Check the build output below.', 'The build failed. Check the build output below.'),
    ('src/Bed.Editor.App/GameBuildHost.cs', 'The build stopped with error code ', 'The build stopped with error code <number>.'),
    ('src/Bed.Editor.App/GameBuildHost.cs', 'Build exceeded its time limit.', 'Build exceeded its time limit.'),
    ('src/Bed.Editor.App/GameBuildHost.cs', 'Build did not produce every allowlisted output.', 'Build did not produce every allowlisted output.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', "This game hasn't enabled frame control yet.", "This game hasn't enabled frame control yet."),
    ('src/Bed.Editor.ImGui/DataWorkspace.PlayStatus.cs', 'Waiting for simulation acknowledgement.', 'Waiting for simulation acknowledgement.'),
    ('src/Bed.Editor.App/ProjectFrameServer.cs', 'Frame control acknowledgement timed out; simulation state is unknown.', 'Frame control acknowledgement timed out; simulation state is unknown.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'The test launch of the exported game stopped with error code ', 'The test launch of the exported game stopped with error code <number>.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Smoke exceeded its time limit.', 'Smoke exceeded its time limit.'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'A successful unpacked check is required before copying.', 'A successful unpacked check is required before copying.'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'Export canceled. Your edits are unchanged.', 'Export canceled. Your edits are unchanged.'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'Choose a destination folder before copying.', 'Choose a destination folder before copying.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Choose an absolute new or empty folder.', 'Choose an absolute new or empty folder.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Choose a new or empty folder; existing files will not be replaced.', 'Choose a new or empty folder; existing files will not be replaced.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Choose a folder outside the owned build output.', 'Choose a folder outside the owned build output.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Copy destination contains a link.', 'Copy destination contains a link.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Build output changed since verification.', 'Build output changed since verification.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Build changed before copy: ', 'Build changed before copy: <file>'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Copy destination is no longer empty.', 'Copy destination is no longer empty.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Copy canceled; destination unchanged.', 'Copy canceled; destination unchanged.'),
    ('src/Bed.Editor.App/ProjectPlayHost.cs', 'Running.', 'Running.'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'Building saved files...', 'Building saved files...'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'Checking the unpacked build...', 'Checking the unpacked build...'),
    ('src/Bed.Editor.ImGui/ExportBuildDialog.cs', 'Copying verified build...', 'Copying verified build...'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Build and unpacked smoke verified; ready to copy.', 'Build and unpacked smoke verified; ready to copy.'),
    ('src/Bed.Editor.App/VerifiedGameBuild.cs', 'Build copied to ', 'Build copied to <folder>'),
]

class TroubleshootingDoc11Tests(unittest.TestCase):
    def test_each_guide_message_has_its_actual_source_literal(self):
        guide = GUIDE.read_text(encoding="utf-8")
        sources = {}
        for relative, literal, display in MESSAGES:
            with self.subTest(message=display, source=relative):
                if relative not in sources:
                    sources[relative] = (ROOT / relative).read_text(encoding="utf-8")
                source = sources[relative]
                self.assertIn('"' + literal + '"', source, "Message changed: review the guide's advice too")
                self.assertIn(display, guide, "Actual message missing from troubleshooting guide")

    def test_every_troubleshooting_row_has_an_action_and_links_resolve(self):
        guide = GUIDE.read_text(encoding="utf-8")
        rows = [line for line in guide.splitlines() if line.startswith("| `")]
        self.assertEqual(len(MESSAGES) - 6, len(rows))
        for row in rows:
            with self.subTest(row=row):
                cells = row.strip("|").split("|")
                self.assertEqual(2, len(cells))
                self.assertGreater(len(cells[1].split()), 5)
        for target in re.findall(r"\[[^\]]+\]\(([^)]+)\)", guide):
            self.assertTrue((GUIDE.parent / target.split("#")[0]).is_file(), target)
        self.assertIn("docs/guide/troubleshooting.md", (ROOT / "README.md").read_text(encoding="utf-8"))
        for sample in (ROOT / "samples").iterdir():
            if sample.is_dir() and len(sample.name) > 3:
                self.assertNotIn(sample.name, guide)

if __name__ == "__main__":
    unittest.main()
