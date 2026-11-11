"""Bounded stdlib preparation tests; no SDK, package bytes or C# execution."""
from pathlib import Path
import copy
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET

HERE=Path(__file__).resolve().parent
def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path)
    module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
    return module

starter=load("a293_starter_under_test",HERE/"prepare_package_first_use.py")
contract_directory=Path(os.environ.get("A293_CONTRACT_DIR",str(HERE))).resolve(strict=True)
contract=load("a293_frozen_contract",contract_directory/"package_consumer_smoke.py")

class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.parent=Path(os.environ.get("A293_TEST_ROOT",tempfile.gettempdir())).resolve(strict=True)
        self.temp=tempfile.TemporaryDirectory(prefix="a293-first-use-",dir=self.parent)
        self.root=Path(self.temp.name).resolve(strict=True)
        self.assertEqual(self.parent,self.root.parent);self.assertTrue(self.root.name.startswith("a293-first-use-"))
        self.feed=self.root/"feed";self.feed.mkdir()
        self.output=self.root/"starter"
        # Explicit input source/template location, never a source-repository search.
        self.program=Path(os.environ.get("A293_PROGRAM",str(HERE.parent/"examples/PackageFirstUse/Program.cs"))).resolve(strict=True)
        self.template=Path(os.environ.get("A293_README",str(HERE.parent/"PACKAGE-README.md"))).resolve(strict=True)
        source_hash=starter.sha(self.program.read_bytes())
        self.manifest={
            "format":1,"review":"synthetic-only, not approval","source_revision":"a"*40,
            "sdk":"10.0.100","repository_url":"https://packages.synthetic.test/project","version":"1.2.3",
            "source_files":{"Data.csproj":"1"*64,"Core.csproj":"2"*64,"Tests.csproj":"3"*64,
                            "provenance/Consumer.cs":source_hash},
            "dependency_files":{"Synthetic.Dependency.1.0.nupkg":"f"*64},
            "packages":[
                {"id":"Fixture.Data","project":"Data.csproj","entries":["lib/net10.0/Fixture.Data.dll","lib/net10.0/Fixture.Data.xml","LICENSE.txt"],"notices":{"LICENSE.txt":"4"*64}},
                {"id":"Fixture.Core","project":"Core.csproj","entries":["lib/net10.0/Fixture.Core.dll","lib/net10.0/Fixture.Core.xml","LICENSE.txt"],"notices":{"LICENSE.txt":"5"*64}}],
            "tests":[{"project":"Tests.csproj","filter":"FullyQualifiedName~SyntheticTests"}],
            "consumer":{"framework":"net10.0","files":["provenance/Consumer.cs"]}}
        self.start="https://docs.synthetic.test/start"
        self.adapter="https://docs.synthetic.test/adapter"

    def tearDown(self):
        resolved=self.root.resolve(strict=True)
        self.assertEqual(self.parent,resolved.parent);self.assertTrue(resolved.name.startswith("a293-first-use-"))
        self.temp.cleanup()

    def prepare(self,**changes):
        values={"manifest":self.manifest,"program":self.program,"readme":self.template,
                "data_package_id":"Fixture.Data","feed":self.feed,"out":self.output,
                "start_tutorial_url":self.start,"adapter_tutorial_url":self.adapter,"contract":contract}
        values.update(changes)
        return starter.prepare(**values)

    def test_prepares_text_only_without_mutating_manifest_or_environment(self):
        before=copy.deepcopy(self.manifest)
        with mock.patch.dict(os.environ,{"A293_ENV_SENTINEL":"keep"}):
            receipt=self.prepare()
            self.assertEqual("keep",os.environ["A293_ENV_SENTINEL"])
        self.assertEqual(before,self.manifest)
        self.assertEqual("prepared-text-only",receipt["status"])
        for field in ["SDK_run","packages_read","consumer_compiled","consumer_run","approval_inferred"]:
            self.assertFalse(receipt[field])
        self.assertEqual([],list(self.feed.iterdir()))
        self.assertEqual(self.program.read_bytes(),(self.output/"Input0.cs").read_bytes())

    def test_project_is_exact_frozen_contract_rendering_and_keeps_all_selected_references(self):
        self.prepare()
        self.assertEqual(contract.consumer_project(self.manifest)+"\n",(self.output/"Consumer.csproj").read_text())
        xml=ET.parse(self.output/"Consumer.csproj")
        self.assertFalse(list(xml.iter("ProjectReference")))
        self.assertEqual([("Fixture.Data","[1.2.3]"),("Fixture.Core","[1.2.3]")],
                         [(x.get("Include"),x.get("Version")) for x in xml.iter("PackageReference")])
        self.assertEqual(["Input0.cs"],[x.get("Include") for x in xml.iter("Compile")])

    def test_same_pinned_consumer_source_composes_without_checkout_files(self):
        self.assertFalse((self.root/"Data.csproj").exists())
        self.assertFalse((self.root/"provenance/Consumer.cs").exists())
        receipt=self.prepare()
        self.assertEqual(self.manifest["source_files"]["provenance/Consumer.cs"],receipt["consumer_source_sha256"])
        for path in self.output.iterdir():
            if path.is_file():self.assertNotIn("provenance/Consumer.cs",path.read_text())
        self.assertFalse((self.root/"Data.csproj").exists())

    def test_local_feed_and_empty_boundary_files_are_explicit(self):
        self.prepare()
        xml=ET.parse(self.output/"NuGet.Config")
        self.assertEqual(1,len(list(xml.iter("add"))));self.assertEqual(str(self.feed.resolve()),next(xml.iter("add")).get("value"))
        self.assertEqual(2,len(list(xml.iter("clear"))))
        for name in ["Directory.Build.props","Directory.Build.targets","Directory.Packages.props"]:
            self.assertEqual("<Project />\n",(self.output/name).read_text())
        sdk=json.loads((self.output/"global.json").read_text())["sdk"]
        self.assertEqual("10.0.100",sdk["version"]);self.assertEqual("disable",sdk["rollForward"])

    def test_cache_environment_is_data_only_and_paths_are_contained(self):
        receipt=self.prepare()
        env=json.loads((self.output/"build-environment.json").read_text())
        self.assertEqual("false",env["DOTNET_GENERATE_ASPNET_CERTIFICATE"])
        for key in ["NUGET_PACKAGES","NUGET_HTTP_CACHE_PATH","NUGET_PLUGINS_CACHE_PATH","DOTNET_CLI_HOME","TEMP","TMP"]:
            self.assertTrue(Path(env[key]).is_relative_to(self.output));self.assertTrue(Path(env[key]).is_dir())
            self.assertEqual([],list(Path(env[key]).iterdir()))
        self.assertNotIn("HOME",env);self.assertNotIn("PATH",env);self.assertNotIn("USERPROFILE",env)
        self.assertEqual(5,len(receipt["empty_directories"]))

    def test_no_execute_package_inspection_source_copy_or_subprocess_route_is_called(self):
        with mock.patch.object(contract,"run",side_effect=AssertionError("must not run")) as run, \
             mock.patch.object(contract,"inspect_package",side_effect=AssertionError("must not inspect")) as inspect, \
             mock.patch.object(contract,"pinned_files",side_effect=AssertionError("must not follow source paths")) as pinned, \
             mock.patch.object(contract.subprocess,"run",side_effect=AssertionError("must not launch")) as command:
            self.prepare()
        run.assert_not_called();inspect.assert_not_called();pinned.assert_not_called();command.assert_not_called()

    def test_readme_slots_are_resolved_to_reviewed_inputs_and_exact_original_source(self):
        self.prepare()
        text=(self.output/"README.md").read_text()
        self.assertNotIn("{{PROGRAM}}",text);self.assertNotIn("{{VERSION}}",text)
        self.assertIn("Fixture.Data",text);self.assertIn("1.2.3",text)
        self.assertIn(self.start,text);self.assertIn(self.adapter,text)
        self.assertIn(self.program.read_text().rstrip(),text)
        self.assertIn("not captured runtime output",text)
        self.assertIn("deliberately never launches",text)

    def test_existing_output_is_not_overwritten(self):
        self.output.mkdir();sentinel=self.output/"keep.txt";sentinel.write_text("keep")
        with self.assertRaises(ValueError):self.prepare()
        self.assertEqual("keep",sentinel.read_text());self.assertEqual(["keep.txt"],[p.name for p in self.output.iterdir()])

    def test_source_hash_mismatch_refuses_before_output(self):
        self.manifest["source_files"]["provenance/Consumer.cs"]="0"*64
        with self.assertRaises(ValueError):self.prepare()
        self.assertFalse(self.output.exists())

    def test_unknown_role_or_unfilled_identity_refuses_before_output(self):
        for role in ["Other.Data","APPROVED_DATA_PACKAGE_ID"]:
            with self.subTest(role=role),self.assertRaises(ValueError):self.prepare(data_package_id=role)
        self.assertFalse(self.output.exists())

    def test_missing_reviewed_release_values_are_not_defaulted(self):
        for field,value in [("review",""),("version","0.0.0-local.trial"),("sdk","REQUIRED_SDK")]:
            changed=copy.deepcopy(self.manifest);changed[field]=value
            with self.subTest(field=field),self.assertRaises(ValueError):self.prepare(manifest=changed)
        self.assertFalse(self.output.exists())

    def test_multiple_consumer_sources_need_another_reviewed_route(self):
        self.manifest["source_files"]["Other.cs"]="9"*64;self.manifest["consumer"]["files"].append("Other.cs")
        with self.assertRaises(ValueError):self.prepare()
        self.assertFalse(self.output.exists())

    def test_wrong_framework_and_insufficient_sdk_are_not_claimed_supported(self):
        changed=copy.deepcopy(self.manifest);changed["consumer"]["framework"]="net9.0"
        with self.assertRaises(ValueError):self.prepare(manifest=changed)
        changed=copy.deepcopy(self.manifest);changed["sdk"]="9.0.100"
        with self.assertRaises(ValueError):self.prepare(manifest=changed)
        self.assertFalse(self.output.exists())

    def test_missing_or_placeholder_tutorial_urls_refuse_without_network(self):
        for value in ["REVIEWED_START_URL","https://example.invalid/start","http://docs.synthetic.test/start"]:
            with self.subTest(value=value),self.assertRaises(ValueError):self.prepare(start_tutorial_url=value)
        self.assertFalse(self.output.exists())

    def test_missing_template_slot_refuses_before_output(self):
        bad=self.root/"bad.md";bad.write_text(self.template.read_text().replace("{{PROGRAM}}",""))
        with self.assertRaises(ValueError):self.prepare(readme=bad)
        self.assertFalse(self.output.exists())

    def test_unexpected_template_marker_refuses_before_output(self):
        bad=self.root/"extra.md";bad.write_text(self.template.read_text()+"\n{{UNREVIEWED_INPUT}}\n")
        with self.assertRaises(ValueError):self.prepare(readme=bad)
        self.assertFalse(self.output.exists())

    def test_output_feed_overlap_and_missing_feed_refuse_without_reading_packages(self):
        with self.assertRaises(ValueError):self.prepare(out=self.feed/"starter")
        with self.assertRaises(OSError):self.prepare(feed=self.root/"missing-feed")
        self.assertFalse(self.output.exists());self.assertEqual([],list(self.feed.iterdir()))

    def test_receipt_output_hashes_match_only_text_outputs(self):
        receipt=self.prepare()
        for name,digest in receipt["outputs"].items():
            self.assertEqual(digest,starter.sha((self.output/name).read_bytes()))
            self.assertNotIn(Path(name).suffix,{".dll",".exe",".nupkg",".pdb"})
        self.assertEqual(receipt,json.loads((self.output/"preparation-receipt.json").read_text()))

    def test_execute_flag_does_not_exist(self):
        with mock.patch("sys.stderr"),self.assertRaises(SystemExit) as raised:starter.main(["--execute"])
        self.assertEqual(2,raised.exception.code);self.assertFalse(self.output.exists())

    def test_public_template_and_program_have_no_checkout_or_private_path_dependency(self):
        text=self.template.read_text()+self.program.read_text()
        for marker in ["C:\\code","C:/code","codex/","../../src","BRUTAL_AUTHORING","Bed.slnx","/home/","/Users/"]:
            self.assertNotIn(marker,text)
        self.assertIn("DataKit.Session",text);self.assertIn("DataKit.Open",text)
        self.assertIn("session.Undo()",text);self.assertIn("session.Redo()",text)
        self.assertIn("session.Save(file)",text)

if __name__=="__main__":unittest.main()
