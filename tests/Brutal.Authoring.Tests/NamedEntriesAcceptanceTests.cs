using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using ImGui = Brutal.ImGuiApi.ImGui;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>INVENTED neutral schemas and interaction sequences. Acceptance oracles are declared schema constraints,
/// the public inspector command contract and literal JSON-key identity, never private game records.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class NamedEntriesAcceptanceTests
{
    private const string Numbers = """{"type":"object","additionalProperties":{"type":"integer","default":5}}""";

    [Fact]
    public void N01_An_empty_typed_map_starts_with_its_declared_default()
    {
        using var r=new Rig(Numbers,"{}");r.Add("first");Assert.Equal(5,r.Value("first")!.Value.GetInt32());
        Assert.Equal(1,r.Session.History.UndoCount);r.OnlyMapChanged();
    }

    // Found by A161 on a game's story conditions: a map of lists that must hold at least one couldn't get its first entry.
    [Fact]
    public void N01b_A_map_of_non_empty_lists_gets_its_first_entry_with_a_row_to_type_into()
    {
        const string lists="""{"type":"object","additionalProperties":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string"}}}""";
        using var r=new Rig(lists,null);r.Add("faction");
        var added=r.Value("faction")!.Value;Assert.Equal(JsonValueKind.Array,added.ValueKind);Assert.Equal(1,added.GetArrayLength());
        Assert.Equal(1,r.Session.History.UndoCount);r.OnlyMapChanged();
        using var two=new Rig("""{"type":"object","additionalProperties":{"type":"array","minItems":2,"items":{"type":"integer","default":3}}}""","{}");two.Add("pair");
        Assert.Equal([3,3],two.Value("pair")!.Value.EnumerateArray().Select(x=>x.GetInt32()));
    }

    [Fact]
    public void N02_A_missing_map_is_created_with_one_named_entry()
    {
        using var r=new Rig(Numbers,null);Assert.True(r.Map.NamedEntries);
        r.Add("first");Assert.Equal(5,r.Value("first")!.Value.GetInt32());r.OnlyMapChanged();
    }

    [Fact]
    public void N03_An_empty_pattern_only_map_uses_the_matching_entry_default()
    {
        using var r=new Rig("""{"type":"object","patternProperties":{"^n-":{"type":"integer","default":7}},"additionalProperties":false}""","{}");
        Assert.True(r.Map.NamedEntries);r.Add("n-first");Assert.Equal(7,r.Value("n-first")!.Value.GetInt32());r.OnlyMapChanged();
    }

    [Fact]
    public void N04_Several_patterns_choose_the_new_names_type_instead_of_copying_an_unrelated_last_value()
    {
        using var r=new Rig("""{"type":"object","patternProperties":{"^s-":{"type":"string","default":"fresh"},"^n-":{"type":"integer","default":7}},"additionalProperties":false}""","""{"s-keep":"retained"}""");
        r.Add("n-first");Assert.Equal(7,r.Value("n-first")!.Value.GetInt32());Assert.Equal("retained",r.Value("s-keep")!.Value.GetString());r.OnlyMapChanged();
    }

    [Fact]
    public void N05_An_entry_matching_two_patterns_meets_both_constraints()
    {
        using var r=new Rig("""{"type":"object","patternProperties":{"^n":{"type":"integer","minimum":2,"default":4},"z$":{"type":"integer","maximum":8}},"additionalProperties":false}""","{}");
        r.Add("nz");Assert.InRange(r.Value("nz")!.Value.GetInt32(),2,8);r.OnlyMapChanged();
    }

    [Fact]
    public void N06_A_false_additional_policy_still_allows_a_declared_pattern_and_rejects_other_names()
    {
        using var r=new Rig("""{"type":"object","patternProperties":{"^ok-":{"type":"integer","default":3}},"additionalProperties":false}""","""{"ok-old":2}""");
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"wrong"));r.Unchanged(before);Assert.NotEmpty(r.Inspector.LastIssues);
        r.Add("ok-new");Assert.Equal(2,r.Value("ok-new")!.Value.GetInt32());
    }

    [Fact]
    public void N07_A_mixed_object_does_not_use_a_declared_metadata_field_as_a_dynamic_entry_sample()
    {
        using var r=new Rig("""{"type":"object","properties":{"header":{"type":"string"}},"additionalProperties":{"type":"integer","default":3}}""","""{"old":1,"header":"retained metadata"}""");
        r.Add("new");Assert.Equal(JsonValueKind.Number,r.Value("new")!.Value.ValueKind);
        Assert.Equal("retained metadata",r.Value("header")!.Value.GetString());r.OnlyMapChanged();
    }

    [Fact]
    public void N08_An_empty_object_entry_can_be_built_when_every_required_field_has_a_default()
    {
        using var r=new Rig("""{"type":"object","additionalProperties":{"type":"object","required":["label","enabled"],"properties":{"label":{"type":"string","default":"fresh"},"enabled":{"type":"boolean","default":true}},"additionalProperties":false}}""","{}");
        r.Add("first");Assert.Equal("fresh",r.Session.Current.At("/units/0/entries/first/label")!.Value.GetString());
        Assert.True(r.Session.Current.At("/units/0/entries/first/enabled")!.Value.GetBoolean());r.OnlyMapChanged();
    }

    [Fact]
    public void N09_References_to_the_map_and_its_value_keep_the_named_entry_contract()
    {
        using var r=new Rig("""{"$ref":"#/$defs/map"}""","{}",defs:"""{"map":{"type":"object","additionalProperties":{"$ref":"#/$defs/value"}},"value":{"type":"integer","default":6}}""");
        Assert.True(r.Map.NamedEntries);r.Add("first");Assert.Equal(6,r.Value("first")!.Value.GetInt32());
    }

    [Fact]
    public void N10_AllOf_keeps_declared_fields_protected_while_dynamic_entries_remain_removable()
    {
        using var r=new Rig("""{"allOf":[{"type":"object","properties":{"fixed":{"type":"integer"}}},{"type":"object","additionalProperties":{"type":"integer","default":1}}]}""","""{"fixed":1,"loose":2}""");
        var fixedRow=r.Entry("fixed");Assert.False(fixedRow.CanRemove);
        var before=r.Snapshot();Assert.False(r.Inspector.RemoveItem(fixedRow));r.Unchanged(before);
        Assert.True(r.Entry("loose").CanRemove);Assert.True(r.Inspector.RemoveItem(r.Entry("loose")));Assert.NotNull(r.Value("fixed"));
    }

    [Fact]
    public void N11_An_optional_declared_field_is_not_mistaken_for_a_removable_map_entry()
    {
        using var r=new Rig("""{"type":"object","properties":{"fixed":{"type":"integer"}},"additionalProperties":{"type":"integer"}}""","""{"fixed":1,"loose":2}""");
        var before=r.Snapshot();Assert.False(r.Entry("fixed").CanRemove);
        Assert.False(r.Inspector.RemoveItem(r.Entry("fixed")));r.Unchanged(before);
    }

    [Fact]
    public void N12_A_fixed_object_offers_no_named_entry_add_or_remove()
    {
        using var r=new Rig("""{"type":"object","properties":{"fixed":{"type":"integer"}},"additionalProperties":false}""","""{"fixed":1}""");
        Assert.False(r.Map.NamedEntries);Assert.False(r.Entry("fixed").CanRemove);
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"new"));r.Unchanged(before);
    }

    [Fact]
    public void N13_An_untyped_open_object_does_not_offer_a_typed_value_it_cannot_construct()
    {
        using var r=new Rig("""{"type":"object","additionalProperties":true}""","{}");
        Assert.False(r.Map.NamedEntries);
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"new"));r.Unchanged(before);
    }

    [Fact]
    public void N14_An_existing_null_valued_name_is_still_taken_and_never_overwritten()
    {
        using var r=new Rig("""{"type":"object","additionalProperties":{"type":["integer","null"],"default":0}}""","""{"taken":null}""");
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"taken"));r.Unchanged(before);
        Assert.Equal(JsonValueKind.Null,r.Value("taken")!.Value.ValueKind);
    }

    [Fact]
    public void N15_Slash_and_tilde_names_are_one_literal_key_on_add_and_remove()
    {
        using var r=new Rig();r.Add("a/b~c");Assert.Equal(1,r.Value("a/b~c")!.Value.GetInt32());
        Assert.Null(r.Session.Current.At("/units/0/entries/a"));
        Assert.True(r.Inspector.RemoveItem(r.Entry("a/b~c")));Assert.Null(r.Value("a/b~c"));Assert.NotNull(r.Value("keep"));
    }

    [Fact]
    public void N16_Case_distinct_names_remain_distinct_json_keys()
    {
        using var r=new Rig();r.Add("KEEP");Assert.NotNull(r.Value("keep"));Assert.NotNull(r.Value("KEEP"));
        Assert.Equal(2,r.Keys().Length);Assert.True(r.Inspector.RemoveItem(r.Entry("KEEP")));Assert.NotNull(r.Value("keep"));
    }

    [Fact]
    public void N17_A_long_unicode_api_name_roundtrips_without_truncation_or_key_splitting()
    {
        using var r=new Rig();var name=new string('q',1024)+"/~☄";
        r.Add(name);Assert.NotNull(r.Value(name));Assert.True(r.File.Save(),r.Why);
        Assert.Equal(new[]{"keep",name},r.KeysOnDisk());r.Reopen();Assert.NotNull(r.Value(name));
    }

    [Fact]
    public void N18_MaxProperties_refusal_changes_neither_document_history_journal_nor_disk()
    {
        using var r=new Rig("""{"type":"object","maxProperties":1,"additionalProperties":{"type":"integer"}}""","""{"keep":1}""");
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"new"));Assert.NotEmpty(r.Inspector.LastIssues);r.Unchanged(before);
    }

    [Fact]
    public void N19_MinProperties_refuses_removing_the_last_entry_without_losing_it()
    {
        using var r=new Rig("""{"type":"object","minProperties":1,"additionalProperties":{"type":"integer"}}""","""{"keep":1}""");
        var before=r.Snapshot();Assert.False(r.Inspector.RemoveItem(r.Entry("keep")));Assert.NotEmpty(r.Inspector.LastIssues);r.Unchanged(before);
    }

    [Fact]
    public void N20_Multiple_selection_offers_no_single_map_add_or_remove_and_api_refuses()
    {
        using var r=new Rig(second:true);r.Select(0,1);Assert.False(r.Map.NamedEntries);
        Assert.All(r.Inspector.Nested(r.Map),row=>Assert.False(row.CanRemove));
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"new"));r.Unchanged(before);
    }

    [Fact]
    public void N21_An_out_of_focus_map_hides_mutation_flags_and_refuses_api_commands()
    {
        using var r=new Rig(second:true);Assert.True(r.Editor.FocusOn("/units/0"));r.Select(1);
        Assert.False(r.Map.NamedEntries);Assert.All(r.Inspector.Nested(r.Map),x=>Assert.False(x.CanRemove));
        var before=r.Snapshot();Assert.False(r.Inspector.AddNamed(r.Map,"new"));r.Unchanged(before);
    }

    [Fact]
    public void N22_Undo_redo_save_reopen_preserve_key_order_existing_numbers_and_unrelated_content()
    {
        using var r=new Rig(Numbers,"""{"z":1.00,"a":2e0}""");
        var originalZ=r.Value("z")!.Value.GetRawText();r.Add("m");Assert.Equal(new[]{"z","a","m"},r.Keys());
        Assert.True(r.Session.Undo());Assert.Equal(new[]{"z","a"},r.Keys());
        Assert.True(r.Session.Redo());Assert.True(r.File.Save(),r.Why);
        Assert.Equal(new[]{"z","a","m"},r.KeysOnDisk());r.Reopen();Assert.Equal(originalZ,r.Value("z")!.Value.GetRawText());r.OnlyMapChanged();
    }

    [Fact]
    public void N23_Readonly_project_cannot_persist_named_entry_edits_and_keeps_recoverable_work()
    {
        using var r=new Rig(readOnly:true);var disk=File.ReadAllBytes(r.File.Path);
        r.Add("new");Assert.False(r.File.Save());Assert.True(r.Session.Dirty);Assert.Equal(disk,File.ReadAllBytes(r.File.Path));Assert.NotNull(r.Value("new"));
    }

    [Fact]
    public void U01_Enter_in_the_real_name_box_adds_once_and_is_undoable()
    {
        using var r=new Rig(ui:true);r.TypeName("alpha");Assert.NotNull(r.Value("alpha"));Assert.Equal(1,r.Session.History.UndoCount);
        Assert.True(r.Window!.Run("editor.undo"));Assert.Null(r.Value("alpha"));Assert.True(r.Window.Run("editor.redo"));Assert.NotNull(r.Value("alpha"));r.OnlyMapChanged();
    }

    [Fact]
    public void U02_The_real_Add_button_takes_the_typed_name_and_clears_it_after_success()
    {
        using var r=new Rig(ui:true);r.TypeName("alpha",enter:false);r.Click(r.Map,InspectorPart.Add);
        Assert.NotNull(r.Value("alpha"));var before=r.Snapshot();r.Click(r.Map,InspectorPart.Add);r.Unchanged(before);
        Assert.Contains("name",r.Window!.Status,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void U03_A_pattern_refusal_is_actionable_and_the_same_box_can_be_corrected()
    {
        using var r=new Rig("""{"type":"object","propertyNames":{"pattern":"^[a-z]+$"},"additionalProperties":{"type":"integer","default":1}}""","{}",ui:true);
        var before=r.Snapshot();r.TypeName("INVALID!");r.Unchanged(before);Assert.NotEmpty(r.Inspector.LastIssues);
        Assert.Contains("'INVALID!' isn't an allowed name here",r.Window!.Status,StringComparison.Ordinal);
        r.TypeName("valid");Assert.NotNull(r.Value("valid"));Assert.Equal(1,r.Session.History.UndoCount);
    }

    [Fact]
    public void U04_Whitespace_only_input_reports_a_name_prompt_without_creating_a_key()
    {
        using var r=new Rig(ui:true);var before=r.Snapshot();r.TypeName("   ");r.Unchanged(before);
        Assert.Contains("Type a name",r.Window!.Status,StringComparison.Ordinal);
    }


    [Fact]
    public void U05_Leading_and_trailing_spaces_required_by_the_schema_are_literal_key_characters()
    {
        using var r=new Rig("""{"type":"object","propertyNames":{"pattern":"^ padded $"},"additionalProperties":{"type":"integer","default":1}}""","{}",ui:true);
        r.TypeName(" padded ");Assert.NotNull(r.Value(" padded "));Assert.Null(r.Value("padded"));r.OnlyMapChanged();
    }

    [Fact]
    public void U06_A_valid_long_name_can_be_entered_without_silently_using_a_shorter_key()
    {
        using var r=new Rig("""{"type":"object","propertyNames":{"minLength":300,"maxLength":400},"additionalProperties":{"type":"integer","default":1}}""","{}",ui:true);
        var name=new string('x',320);r.TypeName(name);Assert.NotNull(r.Value(name));Assert.Equal(new[]{name},r.Keys());r.OnlyMapChanged();
    }

    [Fact]
    public void U07_Real_unicode_slash_and_tilde_input_saves_one_exact_name()
    {
        using var r=new Rig(ui:true);const string name="café/雪~entry";r.TypeName(name);
        Assert.NotNull(r.Value(name));Assert.True(r.Window!.Run("editor.save"),r.Window.Status);Assert.Contains(name,r.KeysOnDisk());
        r.Reopen();Assert.NotNull(r.Value(name));
    }

    [Fact]
    public void U08_Typing_in_one_selection_cannot_leak_into_another_objects_name_box()
    {
        using var r=new Rig(ui:true,second:true);r.TypeName("firstdraft",enter:false);r.Select(1);
        r.Click(r.Map,InspectorPart.NewName);var before=r.Snapshot();r.Key(ImGuiKey.Enter);r.Unchanged(before);
        Assert.Contains("name",r.Window!.Status,StringComparison.OrdinalIgnoreCase);
        r.TypeName("second");Assert.NotNull(r.Value("second",1));Assert.Null(r.Value("firstdraftsecond",1));
        Assert.Null(r.Value("firstdraft",0));Assert.Null(r.Value("second",0));
    }

    [Fact]
    public void U09_Multiple_selection_draws_no_name_box_and_does_not_reuse_the_old_one()
    {
        using var r=new Rig(ui:true,second:true);r.TypeName("pending",enter:false);r.Select(0,1);r.Frames();
        Assert.False(r.Map.NamedEntries);Assert.Null(r.Window!.InspectorPlace(r.Map.Id,InspectorPart.NewName));
        var before=r.Snapshot();r.Key(ImGuiKey.Enter);r.Unchanged(before);
    }

    [Fact]
    public void U10_A_focus_locked_selection_draws_no_name_box_or_remove_button()
    {
        using var r=new Rig(ui:true,second:true);Assert.True(r.Editor.FocusOn("/units/0"));r.Select(1);r.Frames();
        Assert.False(r.Map.NamedEntries);Assert.Null(r.Window!.InspectorPlace(r.Map.Id,InspectorPart.NewName));
        Assert.All(r.Inspector.Nested(r.Map),row=>Assert.False(row.CanRemove));
        var before=r.Snapshot();r.Key(ImGuiKey.Enter);r.Unchanged(before);
    }

    [Fact]
    public void U11_A_pending_nested_name_stays_with_its_field_when_an_earlier_entry_is_removed()
    {
        using var r=new Rig("""{"type":"object","additionalProperties":{"type":"object","additionalProperties":{"type":"integer","default":1}}}""","""{"first":{"a":1},"second":{"b":2}}""",ui:true);
        var second=r.Entry("second");r.TypeNameAt(second,"new",enter:false);
        Assert.True(r.Inspector.RemoveItem(r.Entry("first")));r.Frames();
        second=r.Entry("second");r.Click(second,InspectorPart.NewName);r.Key(ImGuiKey.Enter);
        Assert.NotNull(r.Session.Current.At("/units/0/entries/second/new"));Assert.Null(r.Value("first"));
        Assert.Null(r.Value("new"));Assert.Equal(2,r.Session.History.UndoCount);
    }

    [Fact]
    public void U12_Folding_a_map_does_not_turn_a_pending_name_into_an_edit()
    {
        using var r=new Rig(ui:true);r.TypeName("pending",enter:false);var before=r.Snapshot();
        Assert.NotNull(r.Window!.InspectorPlace(r.Entry("keep").Id,InspectorPart.Remove));
        r.FoldMap();Assert.Null(r.Window.InspectorPlace(r.Entry("keep").Id,InspectorPart.Remove));r.Unchanged(before);
        Assert.Null(r.Value("pending"));Assert.Equal(0,r.Session.History.UndoCount);
    }

    private sealed record State(string Raw,long Revision,int Undo,int Journal,bool Dirty,byte[] Disk);

    private sealed class Rig : IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("named-entry-acceptance-").FullName;
        private readonly HeadlessHost? host;
        private readonly string initial;
        private int selected;
        public DataProject Project { get; }
        public DataFile File { get; }
        public DocumentSession<DataDocument> Session => File.Session!;
        public EditorModel<DataDocument> Editor { get; private set; }
        public Inspector<DataDocument> Inspector { get; private set; }
        public DataWorkspace? Workspace { get; }
        public EditorWindow<DataDocument>? Window => Workspace?.Window;
        public InspectorRow Map => Inspector.Rows().Single(x=>x.Id=="entries");
        public string Why => string.Join(" ",File.Problems.Select(x=>x.Message).Concat(Inspector.LastIssues.Select(x=>x.Message)));

        public Rig(string map=Numbers,string? value="""{"keep":1}""",bool ui=false,bool second=false,bool readOnly=false,string? defs=null)
        {
            var properties=new JsonObject { ["id"]=new JsonObject {["type"]="string"},["entries"]=JsonNode.Parse(map) };
            var unitSchema=new JsonObject { ["type"]="object",["properties"]=properties,["required"]=new JsonArray("id"),["additionalProperties"]=false };
            var schema=new JsonObject {["type"]="object",["properties"]=new JsonObject {["units"]=new JsonObject {["type"]="array",["items"]=unitSchema}},["required"]=new JsonArray("units"),["additionalProperties"]=false};
            if(defs is not null)schema["$defs"]=JsonNode.Parse(defs);
            JsonObject Unit(string id)
            {
                var u=new JsonObject {["id"]=id};
                if(value is not null)u["entries"]=JsonNode.Parse(value);
                return u;
            }
            var units=new JsonArray(Unit("one"));if(second)units.Add(Unit("two"));
            initial=new JsonObject {["units"]=units}.ToJsonString();
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));
            System.IO.File.WriteAllText(Path.Combine(root,"data/doc.json"),initial,new UTF8Encoding(false));
            System.IO.File.WriteAllText(Path.Combine(root,"schemas/doc.schema.json"),schema.ToJsonString(),new UTF8Encoding(false));
            Project=DataProject.Open(Path.Combine(root,"data"),Path.Combine(root,"schemas"),readOnly);
            File=Project.Find("doc.json")!;Assert.True(File.Open(),string.Join(" ",File.Problems.Select(x=>x.Message)));
            if(ui)
            {
                host=new HeadlessHost(1400,2000){Drawing=false};
                Workspace=new DataWorkspace(Project);Assert.True(Workspace.Show("doc.json"),Workspace.Said);
                EditorWindow<DataDocument>.Configure(ImGui.GetIO());Editor=Window!.Editor;Inspector=Window.Inspector!;
                Assert.True(Editor.Select("/units/0"));Frames();
            }
            else
            {
                Editor=new EditorModel<DataDocument>(Session,DataEditing.Outline);
                Inspector=new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(Project.Schemas,"doc.schema.json"));
                Assert.True(Editor.Select("/units/0"));
            }
        }

        public InspectorRow Entry(string name) => Inspector.Nested(Map).Single(x=>x.Label==name);
        public JsonElement? Value(string name,int? unit=null) => Session.Current.At($"/units/{unit??selected}/entries"+Pointer.Join([name]));
        public string[] Keys() => Session.Current.At($"/units/{selected}/entries")!.Value.EnumerateObject().Select(x=>x.Name).ToArray();
        public string[] KeysOnDisk()
        {
            using var d=JsonDocument.Parse(System.IO.File.ReadAllText(File.Path));
            return d.RootElement.GetProperty("units")[selected].GetProperty("entries").EnumerateObject().Select(x=>x.Name).ToArray();
        }
        public void Add(string name) => Assert.True(Inspector.AddNamed(Map,name),Why);
        public State Snapshot() => new(Session.Current.Root.GetRawText(),Session.Revision,Session.History.UndoCount,Session.Journal.Count,Session.Dirty,System.IO.File.ReadAllBytes(File.Path));
        public void Unchanged(State s)
        {
            Assert.Equal(s.Raw,Session.Current.Root.GetRawText());Assert.Equal(s.Revision,Session.Revision);
            Assert.Equal(s.Undo,Session.History.UndoCount);Assert.Equal(s.Journal,Session.Journal.Count);
            Assert.Equal(s.Dirty,Session.Dirty);Assert.Equal(s.Disk,System.IO.File.ReadAllBytes(File.Path));
        }
        public void OnlyMapChanged()
        {
            var wanted=JsonNode.Parse(initial)!;
            wanted["units"]![selected]!["entries"]=Session.Current.At($"/units/{selected}/entries") is {} now ? JsonNode.Parse(now.GetRawText()) : null;
            Assert.True(JsonNode.DeepEquals(wanted,JsonNode.Parse(Session.Current.Root.GetRawText())),"An unrelated field changed.");
        }
        public void Reopen()
        {
            File.Close();Assert.True(File.Open(),Why);
            if(Workspace is null)
            {
                Editor=new EditorModel<DataDocument>(Session,DataEditing.Outline);
                Inspector=new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(Project.Schemas,"doc.schema.json"));
                Assert.True(Editor.Select($"/units/{selected}"));
            }
        }
        public void Select(params int[] ids)
        {
            if(!Editor.Selection.SequenceEqual(ids.Select(i=>$"/units/{i}")))Assert.True(Editor.Select(ids.Select(i=>$"/units/{i}").ToArray()));
            selected=ids[0];Frames();
        }
        private void Draw() => Workspace!.Draw();
        public void Frames() { if(host is not null)host.Frames(Draw,4); }
        public void Key(ImGuiKey key,bool ctrl=false) { host!.Press(Draw,key,ctrl:ctrl);Frames(); }
        private float2 Place(InspectorRow row,InspectorPart part)
        {
            host!.Frame(()=>{ImGui.SetWindowFocus("Inspector###inspector"u8);Draw();});Frames();
            var at=Window!.InspectorPlace(row.Id,part);
            if(at is null)
            {
                var fold=Window.InspectorPlace(Map.Id,InspectorPart.Field);Assert.NotNull(fold);
                host.Click(Draw,fold!.Value.X+5,fold.Value.Y+6);Frames();at=Window.InspectorPlace(row.Id,part);
            }
            Assert.True(at is not null,$"No drawn {part} for {row.Label}.");
            var top=Window.InspectorPlace(Inspector.Rows()[0].Id)!.Value.Y;
            for(var n=0;n<100&&(at!.Value.Y<top||at.Value.Y>1980);n++)
            {
                host.MoveMouse(Draw,at.Value.X+12,top+50);ImGui.GetIO().AddMouseWheelEvent(0,at.Value.Y>1980?-3:3);
                Frames();at=Window.InspectorPlace(row.Id,part);
            }
            Assert.InRange(at!.Value.Y,top,1980);return at.Value;
        }
        public void Click(InspectorRow row,InspectorPart part)
        {
            var at=Place(row,part);host!.Click(Draw,at.X+6,at.Y+6);Frames();
        }
        public void FoldMap()
        {
            host!.Frame(()=>{ImGui.SetWindowFocus("Inspector###inspector"u8);Draw();});Frames();
            var pane=ImGui.Internal.FindWindowByName("Inspector###inspector"u8);Assert.False(pane.IsNull());
            var rowId=pane.GetID(Encoding.UTF8.GetBytes(Map.Id));
            var wanted=ImGui.Internal.GetIDWithSeed("##inside"u8,rowId).Value;
            for(var i=0;i<180&&ImGui.GetCurrentContext().NavId.Value!=wanted;i++)host.Press(Draw,ImGuiKey.Tab);
            Assert.Equal(wanted,ImGui.GetCurrentContext().NavId.Value);Key(ImGuiKey.Space);
        }
        public void TypeName(string name,bool enter=true) => TypeNameAt(Map,name,enter);
        public void TypeNameAt(InspectorRow row,string name,bool enter=true)
        {
            Click(row,InspectorPart.NewName);Assert.True(ImGui.GetIO().WantTextInput,"Name box did not receive the click.");
            Key(ImGuiKey.A,ctrl:true);Key(ImGuiKey.Backspace);host!.Type(Draw,name);
            if(enter)Key(ImGuiKey.Enter);else Frames();
        }
        public void Dispose()
        {
            host?.Dispose();
            var full=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath());
            Assert.StartsWith(temp,full,StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("named-entry-acceptance-",Path.GetFileName(full),StringComparison.Ordinal);
            Directory.Delete(full,recursive:true);
        }
    }
}

