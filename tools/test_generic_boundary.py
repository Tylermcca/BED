# Bounded synthetic source/model negatives, Python standard library only.
from pathlib import Path
import importlib.util
import json
import os
import sys
import tempfile
import unittest

spec=importlib.util.spec_from_file_location("a291_boundary_under_test",Path(__file__).with_name("check_generic_boundary.py"))
boundary=importlib.util.module_from_spec(spec)
sys.modules[spec.name]=boundary
spec.loader.exec_module(boundary)
ROOTS=["Zorbit","Quill","Gizmo","Acme","CardBash"]

class SourceIdentityTests(unittest.TestCase):
    def codes(self,text):return [f.code for f in boundary.scan_source(text,"synthetic.cs",ROOTS)[0]]
    def test_known_consumers_in_namespace_type_member_and_using_fail(self):
        cases=["namespace Zorbit.Tools; class Node {}", "class AcmeCast {}", "record QuillPiece(int Value);",
               "namespace Generic; class GizmoAdapter {}", "using Imported = CardBash.Board;",
               "class Generic { void ReadZorbitRecipes() {} }", "namespace Generic.Acme; class Table {}"]
        for source in cases:
            with self.subTest(source=source):self.assertIn("GB001",self.codes(source))
    def test_comments_xml_docs_and_required_credits_remain_allowed(self):
        source='''// BORROWED from Zorbit, adapted for generic documents.
/// <summary>Acme, Gizmo and Quill plug into these tools.</summary>
class Generic { const string Credits = "Copyright Zorbit; BRUTAL by Ahwoo Limited";
const string Help = "Acme and Zorbit are example consumers, not library dependencies."; }
'''
        self.assertEqual([],self.codes(source))
    def test_literal_comment_delimiters_do_not_turn_text_into_code(self):
        self.assertEqual([],self.codes('class Generic { string x = "/* Acme */ // Zorbit"; char c = \'x\'; }'))
    def test_verbatim_and_raw_prose_remain_allowed(self):
        self.assertEqual([],self.codes('class Generic { string x = @"Credits: ""Zorbit"""; string y = """Acme // is an example"""; }'))
    def test_interpolation_expression_still_catches_a_consumer_use(self):
        self.assertIn("GB001",self.codes('class Generic { string x = $"Credits {AcmeCatalog.Name}"; }'))
    def test_literal_dispatch_and_game_identity_field_fail(self):
        cases=['class Generic { const string GameName = "Acme"; }',
               'class Generic { bool Is(string name) => name == "Zorbit"; }',
               'class Generic { bool Is(string name) => name is not "Acme"; }',
               'class Generic { bool Is(string name) => "Quill.Core" != name; }',
               'class Generic { void Pick(string name) { switch(name) { case "Gizmo": break; } } }',
               r'class Generic { const string GameId = "\u0041cme"; }']
        for source in cases:
            with self.subTest(source=source):self.assertIn("GB006",self.codes(source))
    def test_full_title_in_explicit_game_name_field_fails_but_credit_prose_does_not(self):
        source = "class Generic { const string GameName = \"Acme's Book of Games\"; }"
        self.assertIn("GB006", self.codes(source))
        self.assertEqual([], self.codes('class Generic { const string Credits = "Acme\'s Book of Games; copyright notice"; }'))
    def test_other_identity_literal_is_visible_for_review_not_a_false_game_failure(self):
        failures,notes=boundary.scan_source('class Generic { const string Credit = "Acme"; }',"synthetic.cs",ROOTS)
        self.assertEqual([],failures);self.assertEqual(1,len(notes))
    def test_generic_words_and_brutal_names_are_not_consumer_roots(self):
        self.assertEqual([],self.codes('namespace Bed; class ReadOnlyData { int Zorbitglyph; int Acmestic; }'))
    def test_escaped_identifier_and_interpolated_raw_string_require_review(self):
        for source in [r'class \u0041cme {}','class Generic { string x = $$"""{{AcmeCatalog.Name}}"""; }']:
            with self.subTest(source=source):self.assertIn("GB900",self.codes(source))

class ProjectScopeTests(unittest.TestCase):
    def setUp(self):
        self.parent=Path(os.environ.get("AUTHORING_BOUNDARY_TEST_ROOT",tempfile.gettempdir())).resolve(strict=True)
        self.temp=tempfile.TemporaryDirectory(prefix="a291-source-",dir=self.parent)
        self.root=Path(self.temp.name).resolve(strict=True)
        self.assertEqual(self.parent,self.root.parent)
        self.assertTrue(self.root.name.startswith("a291-source-"))
        self.scope={"version":1,"status":"synthetic test only","consumer_roots":ROOTS,
                    "projects":[{"path":"src/Generic/Generic.csproj","assembly":"Generic"}],"support_projects":[]}
        self.put("Directory.Build.props","<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>")
        self.put("src/Generic/Generic.csproj",'<Project Sdk="Microsoft.NET.Sdk" />')
        self.put("src/Generic/Generic.cs","namespace Generic; public class Document {}")
    def tearDown(self):
        resolved=self.root.resolve(strict=True)
        self.assertEqual(self.parent,resolved.parent)
        self.assertTrue(resolved.name.startswith("a291-source-"))
        self.temp.cleanup()
    def put(self,name,text):
        p=self.root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,encoding="utf-8")
    def inspect(self,include_sources=True):return boundary.inspect_repo(self.root,self.scope,include_sources=include_sources)
    def test_clean_literal_scope_and_credit_names_have_no_findings(self):
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup><EmbeddedResource Include="Acme-NOTICE.txt" /><None Include="Zorbit-LICENSE.txt" Pack="true" /></ItemGroup></Project>')
        result=self.inspect();self.assertEqual([],result["findings"]);self.assertEqual(1,result["source_files"])
        self.assertFalse(result["binary_inspected"]);self.assertFalse(result["package_outputs_inspected"])
    def test_external_game_reference_is_rejected_without_reading_target(self):
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup><ProjectReference Include="../../../Acme/Acme.Core.csproj" /></ItemGroup></Project>')
        result=self.inspect();self.assertIn("GB102",[x["code"] for x in result["findings"]])
        self.assertFalse(any("Acme" in name for name in result["inputs"]))
    def test_unlisted_project_is_not_followed_even_when_generic_named(self):
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup><ProjectReference Include="../Other/Other.csproj" /></ItemGroup></Project>')
        self.put("src/Other/Other.csproj","not XML; MUST NOT BE READ")
        result=self.inspect();self.assertIn("GB102",[x["code"] for x in result["findings"]]);self.assertNotIn("src/Other/Other.csproj",result["inputs"])
    def test_package_assembly_and_namespace_game_identities_fail(self):
        self.put("src/Generic/Generic.csproj",'<Project><PropertyGroup><AssemblyName>Acme.Core</AssemblyName><RootNamespace>Zorbit.Model</RootNamespace></PropertyGroup><ItemGroup><PackageReference Include="Gizmo.Runtime" /><Reference Include="Quill.Game, Version=1.0" /></ItemGroup></Project>')
        self.assertEqual(4,len([x for x in self.inspect()["findings"] if x["code"]=="GB101"]))
    def test_sdk_and_generated_using_game_identities_are_rejected(self):
        self.put("src/Generic/Generic.csproj",'<Project Sdk="Acme.Sdk"><ItemGroup><Using Include="Zorbit.Rules" /></ItemGroup></Project>')
        self.assertEqual(2,len([x for x in self.inspect()["findings"] if x["code"]=="GB101"]))
    def test_conditional_references_are_not_silently_pruned(self):
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup Condition="false"><ProjectReference Include="../Acme/Acme.csproj" /></ItemGroup></Project>')
        self.assertIn("GB102",[x["code"] for x in self.inspect()["findings"]])
    def test_nonliteral_graph_and_compile_mutations_need_review(self):
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup><ProjectReference Include="$(Other)/Other.csproj" /><Compile Include="../shared/*.cs" /></ItemGroup></Project>')
        result=self.inspect()
        self.assertEqual({"GB900","GB103"},{x["code"] for x in result["findings"]})
        self.assertFalse(result["coverage_complete"])
    def test_inherited_reference_is_checked(self):
        self.put("Directory.Build.props",'<Project><ItemGroup><Reference Include="CardBash.Rules" /></ItemGroup></Project>')
        self.assertIn("GB101",[x["code"] for x in self.inspect()["findings"]])
    def test_resource_identity_is_checked_but_asset_never_opened(self):
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup><EmbeddedResource Include="Acme.rules.json" /></ItemGroup></Project>')
        self.put("src/Generic/Acme.rules.json","NOT JSON; contents must not be read")
        result=self.inspect();self.assertIn("GB104",[x["code"] for x in result["findings"]]);self.assertNotIn("src/Generic/Acme.rules.json",result["inputs"])
    def test_source_filename_detected_even_with_generic_declaration(self):
        self.put("src/Generic/AcmeData.cs","class GenericName {}")
        self.assertIn("GB001",[x["code"] for x in self.inspect()["findings"]])
    def test_support_project_graph_checked_without_vendored_source_reads(self):
        self.scope["support_projects"]=["third_party/Helper/Helper.csproj"]
        self.put("src/Generic/Generic.csproj",'<Project><ItemGroup><ProjectReference Include="../../third_party/Helper/Helper.csproj" /></ItemGroup></Project>')
        self.put("third_party/Helper/Helper.csproj",'<Project><ItemGroup><Reference Include="Acme.Game" /></ItemGroup></Project>')
        self.put("third_party/Helper/Unknown.cs","MUST NOT BE READ")
        result=self.inspect();self.assertIn("GB101",[x["code"] for x in result["findings"]]);self.assertNotIn("third_party/Helper/Unknown.cs",result["inputs"])
    def test_scope_must_be_nonempty_and_inside_repository(self):
        path=self.root/"scope.json"
        for scope in [{"version":1,"consumer_roots":ROOTS,"projects":[]},
                      {"version":1,"consumer_roots":ROOTS,"projects":[{"path":"../Acme/x.csproj","assembly":"x"}]}]:
            path.write_text(json.dumps(scope),encoding="utf-8")
            with self.subTest(scope=scope),self.assertRaises(ValueError):boundary.load_scope(path)
    def test_metadata_only_is_explicitly_incomplete_and_does_not_read_source(self):
        self.put("src/Generic/Generic.cs","MUST NOT BE READ")
        result=self.inspect(include_sources=False)
        self.assertEqual(0,result["source_files"]);self.assertFalse(result["coverage_complete"])
        self.assertNotIn("src/Generic/Generic.cs",result["inputs"])
        self.assertIn("src/Generic/Generic.cs",result["omitted_sources"])
    def test_held_source_prefix_is_omitted_even_if_source_scanning_requested(self):
        self.scope["source_exclusions"]=["src/Generic/Held.cs"]
        self.put("src/Generic/Held.cs","MUST NOT BE READ")
        result=self.inspect()
        self.assertNotIn("src/Generic/Held.cs",result["inputs"]);self.assertFalse(result["coverage_complete"])
        self.assertIn("src/Generic/Held.cs",result["omitted_sources"])

if __name__=="__main__":unittest.main()
