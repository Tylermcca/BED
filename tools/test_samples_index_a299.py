# SPDX-License-Identifier: MIT
"""Synthetic tests of the static checker; no sample or SDK execution."""
import os
from pathlib import Path
import tempfile
import unittest
from check_samples_index import check_membership, check_readme, inspect


class SampleIndexTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get("SAMPLES_TEST_ROOT"))
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "samples/Small").mkdir(parents=True)
        (self.root / "samples/Small/Small.csproj").write_text("<Project />", encoding="utf-8")
        (self.root / "Brutal.Authoring.slnx").write_text(
            '<Solution><Folder Name="/tests/"><Project Path="samples/Small/Small.csproj" /></Folder></Solution>', encoding="utf-8")

    def test_projects_count_even_under_tests_solution_folder(self):
        self.assertEqual((["samples/Small/Small.csproj"], []), check_membership(self.root))

    def test_omitted_sample_is_reported_by_exact_path(self):
        (self.root / "samples/Small/Unlisted.csproj").write_text("<Project />", encoding="utf-8")
        projects, errors = check_membership(self.root)
        self.assertEqual(2, len(projects))
        self.assertEqual(["Sample project is absent from solution: samples/Small/Unlisted.csproj"], errors)

    def test_nested_source_projects_count_but_bin_obj_do_not(self):
        for folder in ["nested", "bin", "obj"]:
            (self.root / "samples/Small" / folder).mkdir()
            (self.root / "samples/Small" / folder / "Extra.csproj").write_text("<Project />", encoding="utf-8")
        projects, errors = check_membership(self.root)
        self.assertEqual(2, len(projects))
        self.assertEqual(["Sample project is absent from solution: samples/Small/nested/Extra.csproj"], errors)


    def test_missing_command_path_is_not_silently_accepted(self):
        errors, reports = check_readme(self.root, "`dotnet run --project samples/Missing/Missing.csproj`", "sample")
        self.assertEqual(["sample:1: missing source path: samples/Missing/Missing.csproj"], errors)
        self.assertEqual([], reports)

    def test_generated_output_needs_owner_and_prior_documented_build(self):
        build = "dotnet build samples/Small/Small.csproj -c Release"
        load = "BED --adapter samples/Small/bin/Release/net10.0/Small.dll --settings <user-settings>"
        errors, reports = check_readme(self.root, "```text\n" + build + "\n" + load + "\n```", "sample")
        self.assertEqual([], errors)
        self.assertEqual(1, sum(": GENERATED " in r for r in reports))
        self.assertEqual(1, sum(": USER-SUPPLIED " in r for r in reports))
        self.assertFalse((self.root / "samples/Small/bin").exists())
        errors, _ = check_readme(self.root, "```text\n" + load + "\n" + build + "\n```", "sample")
        self.assertEqual(1, len(errors))
        self.assertIn("prior documented build", errors[0])

    def test_generated_file_existing_does_not_replace_build_prerequisite(self):
        folder = self.root / "samples/Small/bin/Release/net10.0"
        folder.mkdir(parents=True)
        (folder / "Small.dll").write_text("synthetic text, not a binary", encoding="utf-8")
        errors, _ = check_readme(self.root, "`BED --adapter samples/Small/bin/Release/net10.0/Small.dll`", "sample")
        self.assertEqual(1, len(errors))
        self.assertIn("prior documented build", errors[0])

    def test_building_different_project_does_not_authorize_generated_path(self):
        (self.root / "samples/Other").mkdir()
        (self.root / "samples/Other/Other.csproj").write_text("<Project />", encoding="utf-8")
        text = "```text\ndotnet build samples/Other/Other.csproj\nBED --adapter samples/Small/bin/Small.dll\n```"
        errors, _ = check_readme(self.root, text, "sample")
        self.assertEqual(1, len(errors))
        self.assertIn("prior documented build", errors[0])

    def test_quoted_source_paths_and_placeholders_are_classified(self):
        (self.root / "samples/Small/data space").mkdir()
        text = '`BED --data "samples/Small/data space" --settings <my-settings>`'
        errors, reports = check_readme(self.root, text, "sample")
        self.assertEqual([], errors)
        self.assertEqual(["sample:1: SOURCE samples/Small/data space",
                          "sample:1: USER-SUPPLIED <my-settings> (existence not claimed)"], reports)

    def test_unknown_fenced_commands_and_escape_paths_fail_closed(self):
        errors, _ = check_readme(self.root, "```text\nunknown-tool path\n```", "sample")
        self.assertIn("unsupported command block", errors[0])
        errors, _ = check_readme(self.root, "`dotnet build ../outside.csproj`", "sample")
        self.assertIn("escapes the repository", errors[0])

    def test_index_must_include_each_actual_project(self):
        (self.root / "samples/README.md").write_text("# Samples\nNo paths yet", encoding="utf-8")
        report = inspect(self.root)
        self.assertEqual(["Sample index omits project path: samples/Small/Small.csproj"], report["errors"])


class RepositorySamplesTests(unittest.TestCase):
    def test_every_source_sample_is_listed_and_readme_command_paths_are_valid(self):
        root = Path(os.environ.get("SAMPLES_REPO_ROOT", Path(__file__).resolve().parents[1]))
        report = inspect(root, os.environ.get("SAMPLES_OVERLAY"))
        self.assertEqual([], report["errors"], "\n".join(report["errors"]))


if __name__ == "__main__":
    unittest.main()
