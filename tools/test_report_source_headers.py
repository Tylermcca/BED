# SPDX-License-Identifier: MIT
"""Bounded synthetic contract tests; no repository scan or source writes."""
import unittest
from report_source_headers import has_header, reviewed_files


class HeaderReportTests(unittest.TestCase):
    def test_cs_standalone(self):
        self.assertTrue(has_header("// SPDX-License-Identifier: MIT\nnamespace Example;", ".cs"))

    def test_python_bom_shebang_coding(self):
        self.assertTrue(has_header("\ufeff#!/usr/bin/env python3\n# coding: utf-8\n\n # SPDX-License-Identifier: MIT \nprint(1)", ".py"))

    def test_missing(self):
        self.assertFalse(has_header("namespace Example;", ".cs"))

    def test_wrong_expression(self):
        self.assertFalse(has_header("// SPDX-License-Identifier: MIT OR Apache-2.0", ".cs"))

    def test_string_is_not_header(self):
        self.assertFalse(has_header('x = "# SPDX-License-Identifier: MIT"', ".py"))

    def test_late_comment_is_not_header(self):
        self.assertFalse(has_header("namespace Example;\n// SPDX-License-Identifier: MIT", ".cs"))

    def test_comment_prose_is_not_header(self):
        self.assertFalse(has_header("// example: // SPDX-License-Identifier: MIT", ".cs"))

    def test_comment_block_is_not_standalone_line(self):
        self.assertFalse(has_header("/* SPDX-License-Identifier: MIT */", ".cs"))

    def test_explicit_manifest(self):
        self.assertEqual(["a.cs"], reviewed_files({"scope":"one original sample", "excluded":"everything else", "files":["a.cs"]}))

    def test_bad_manifests(self):
        for files in [[], ["../a.cs"], ["/a.cs"], ["a.cs","A.cs"], ["a//b.cs"], ["C:/a.cs"]]:
            with self.subTest(files=files), self.assertRaises(ValueError):
                reviewed_files({"scope":"sample", "excluded":"rest", "files":files})


if __name__ == "__main__":
    unittest.main()
