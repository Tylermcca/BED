"""Bounded synthetic tests, unittest and pytest collectable. No repository census or Git."""
from pathlib import Path
import contextlib
import importlib.util
import io
import json
import os
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("public_text_a372", Path(__file__).with_name("check_public_text.py"))
guard = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = guard
spec.loader.exec_module(guard)

class PublicTextTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        (Path(__file__).parents[2] / '.gate').mkdir(exist_ok=True)

    def test_paths_and_names_in_comments_and_prose_have_lines_without_excerpts(self):
        text = "ordinary\n// Q:/somewhere\n/Users/example/file\n/home/example/config\nZorbitAdapter\n"
        rows = guard.scan_text(text, "guide.md", ["Zorbit"])
        self.assertEqual([(2,"PT001"),(3,"PT002"),(4,"PT002"),(5,"PT003")], [(r.line,r.code) for r in rows])
        self.assertTrue(all("Zorbit" not in r.message and "example" not in r.message for r in rows))

    def test_backslash_drive_and_case_insensitive_private_literal(self):
        rows = guard.scan_text(r"q:\somewhere\file ACME", "fixture.txt", ["Acme"])
        self.assertEqual(["PT001", "PT003"], [r.code for r in rows])

    def test_urls_relative_paths_and_generic_words_are_not_drive_paths(self):
        self.assertEqual([], guard.scan_text("https://example.invalid/docs relative/data.json", "guide.md", []))

    def test_private_names_are_literal_not_regex(self):
        self.assertEqual([], guard.scan_text("AcmeXTools", "x.txt", ["Acme.Tools"]))
        self.assertEqual("PT003", guard.scan_text("Acme.Tools", "x.txt", ["Acme.Tools"])[0].code)

    def test_supplied_lists_accept_nul_and_lines_but_reject_unsafe_scope(self):
        self.assertEqual(["a.cs", "docs/a b.md"], guard.tracked_paths(b"a.cs\0docs/a b.md\0"))
        self.assertEqual(["a.cs", "b.md"], guard.tracked_paths(b"a.cs\r\nb.md\r\n", "lines"))
        for raw in [b"", b"../outside.cs\0", b"/root.cs\0", b"a.cs\0a.cs\0", b"a.cs\0\0", b"a\nname.cs\0"]:
            with self.subTest(raw=raw), self.assertRaises(ValueError): guard.tracked_paths(raw)

    def test_only_supplied_text_is_read_and_binary_is_not_opened(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parents[2] / '.gate') as folder:
            root = Path(folder)
            (root / "safe.md").write_text("generic")
            (root / "not-listed.md").write_text("Zorbit")
            (root / "image.png").write_bytes(b"\xff\x00")
            result = guard.inspect(root, ["safe.md", "image.png"], ["Zorbit"], [])
            self.assertEqual([], result["findings"])
            self.assertEqual(["safe.md"], list(result["inputs"]))
            self.assertEqual(1, len(result["omitted"]))

    def test_missing_unknown_and_invalid_encoding_are_review_errors(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parents[2] / '.gate') as folder:
            root = Path(folder)
            (root / "opaque.unknown").write_bytes(b"contents")
            (root / "bad.md").write_bytes(b"\xff")
            result = guard.inspect(root, ["missing.md", "opaque.unknown", "bad.md"], [], [])
            self.assertEqual(3, len(result["errors"]))

    def test_allowance_binds_exact_line_hash_code_and_reason(self):
        line = "Q:/somewhere"
        with tempfile.TemporaryDirectory(dir=Path(__file__).parents[2] / '.gate') as folder:
            root = Path(folder); (root / "fixture.txt").write_text(line)
            row = {"file":"fixture.txt", "line":1, "code":"PT001", "line_sha256":guard.digest(line.encode()), "reason":"Invented negative fixture for the guard."}
            config = root / "allow.json"; config.write_text(json.dumps({"version":1,"entries":[row]}))
            allow = guard.load_allowlist(config)
            result = guard.inspect(root,["fixture.txt"],[],allow)
            self.assertEqual(1,len(result["allowed"]));self.assertEqual([],result["findings"])
            (root / "fixture.txt").write_text(line + "/changed")
            result = guard.inspect(root,["fixture.txt"],[],allow)
            self.assertEqual(1,len(result["findings"]));self.assertEqual(1,len(result["stale_allowlist"]))
            row["reason"] = ""
            config.write_text(json.dumps({"version":1,"entries":[row]}))
            with self.assertRaises(ValueError): guard.load_allowlist(config)

    def test_optional_local_names_and_cli_exit_codes(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parents[2] / '.gate') as folder:
            root = Path(folder); (root / "tools").mkdir()
            local = root / "tools/generic_boundary_local.json"
            self.assertEqual([],guard.private_names(local))
            local.write_text(json.dumps({"consumer_roots":["Zorbit"]}))
            (root / "guide.md").write_text("Zorbit")
            manifest = root / "tracked.txt";manifest.write_text("guide.md\n")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                code,result=guard.run(root,manifest,"lines")
            self.assertEqual(1,code); self.assertIn("guide.md:1: PT003",output.getvalue())
            self.assertNotIn("Zorbit",output.getvalue())
            self.assertEqual(1,result["private_names_loaded"])
            local.write_text('{"consumer_roots": [1]}')
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(2,guard.main(["--repo",str(root),"--tracked-files",str(manifest),"--tracked-format","lines"]))

    def test_exact_reviewed_false_positives_and_both_segment_boundaries(self):
        # Invented short names that a raw substring matcher would find inside these exact review controls.
        for text,name in [("ProtectedNames","Ted"),("FailedNames","Led"),("SessionTests","Ion")]:
            self.assertEqual([], guard.scan_text(text,"fixture.cs",[name]))
        for text in ["Ted", "ted-app", "TedGame", "ReadTedData", "TEDGame", "Ted_Adapter"]:
            self.assertTrue(guard.contains_name(text,"Ted"),text)
        for text in ["Teddy", "Unted", "ProtectedNames", "pretedpost"]:
            self.assertFalse(guard.contains_name(text,"Ted"),text)

    def test_acronyms_multiword_names_and_unicode_segments(self):
        self.assertTrue(guard.contains_name("ReadXMLData", "XML"))
        self.assertTrue(guard.contains_name("AcmeTools", "acmetools"))
        self.assertTrue(guard.contains_name("Acme.Tools", "AcmeTools"))
        self.assertFalse(guard.contains_name("AcmeExtraTools", "AcmeTools"))
        self.assertTrue(guard.contains_name("ÖrbitalGame", "Örbital"))
        self.assertFalse(guard.contains_name("Örbitalized", "Örbital"))

    def test_proposal_and_only_exact_known_latin1_file(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parents[2] / '.gate') as folder:
            root=Path(folder);directory=root/'tests/Brutal.Authoring.Tests';directory.mkdir(parents=True)
            (root/'change.proposal').write_text('Zorbit')
            (directory/'UntypedFieldsTests.cs').write_bytes(b'// caf\xe9 Zorbit')
            (directory/'OtherTests.cs').write_bytes(b'// caf\xe9 Zorbit')
            result=guard.inspect(root,['change.proposal','tests/Brutal.Authoring.Tests/UntypedFieldsTests.cs','tests/Brutal.Authoring.Tests/OtherTests.cs'],['Zorbit'],[])
            self.assertEqual(2,len(result['findings']))
            self.assertEqual(1,len(result['errors']))
            self.assertEqual('latin-1 (explicit known-file fallback)',result['decodings']['tests/Brutal.Authoring.Tests/UntypedFieldsTests.cs'])

    def test_upstream_omission_is_exact_reasoned_and_reported_without_opening(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parents[2] / '.gate') as folder:
            root=Path(folder); config=root/'allow.json'
            row={'prefix':'fixtures/json-schema-suite/','reason':'Upstream conformance fixtures; deliberate omission, not first-party prose.'}
            config.write_text(json.dumps({'version':1,'entries':[],'omissions':[row]}))
            omissions=guard.load_omissions(config)
            result=guard.inspect(root,['fixtures/json-schema-suite/unknown.md'],[],[],omissions)
            self.assertEqual(1,len(result['omitted']));self.assertEqual({},result['inputs'])
            self.assertEqual([],result['errors'])
            row['prefix']='fixtures/'
            config.write_text(json.dumps({'version':1,'entries':[],'omissions':[row]}))
            with self.assertRaises(ValueError):guard.load_omissions(config)

if __name__ == "__main__": unittest.main()
