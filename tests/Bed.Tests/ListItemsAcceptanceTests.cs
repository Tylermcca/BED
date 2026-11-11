using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;

namespace Bed.Tests;

// A119 revision 2. INVENTED neutral documents and independent preservation/history oracles.
// The three non-record append tests now prove the revised list-row editing route, preserving stronger data/history assertions.
// BORROWED: Inspector's published AddHere/RemoveHere contract, DataProject's save-only read-only contract,
// and the existing NestedEditingAcceptanceTests headless keyboard-navigation pattern (adapted here).
// No game inputs, windows, new dependencies, or changes to the product or existing tests.
[Collection(ImGuiCollection.Name)]
public sealed class ListItemsAcceptanceTests
{
    private sealed class Rig
    {
        internal readonly DocumentSession<DataDocument> Session;
        internal readonly EditorModel<DataDocument> Editor;
        internal readonly Inspector<DataDocument> Inspector;
        internal readonly EditorWindow<DataDocument> Window;
        internal Rig(string text,string schema="{}",bool allPaths=false)
        {
            var schemas=new JsonSchemas().Add("audit",schema);
            Session=DataKit.Session(new DataFormat().Read(text),schemas,"audit");
            Editor=new(Session,allPaths ? new EveryNode() : DataEditing.Outline);
            Inspector=new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(schemas,"audit"));
            Window=new(Editor,Inspector);
            Assert.DoesNotContain(Session.Validator.Validate(Session.Current),i=>i.Severity==Severity.Error);
        }
        internal JsonElement At(string p)=>Session.Current.At(p)!.Value;
        internal string Text=>Session.Current.Root.GetRawText();
        internal void Select(string p)=>Assert.True(Editor.Select(p),p);
    }
    // A game may supply an outline exposing scalar/nested/root items, unlike the default object-only outline.
    private sealed class EveryNode:IDocumentOutline<DataDocument>
    {
        public IEnumerable<string> ObjectIds(DataDocument d)=>Walk(d.Root,"");
        private static IEnumerable<string> Walk(JsonElement value,string at)
        {
            yield return at;
            if(value.ValueKind==JsonValueKind.Object)
                foreach(var p in value.EnumerateObject())
                    foreach(var child in Walk(p.Value,at+Pointer.Join(new[]{p.Name})))yield return child;
            if(value.ValueKind==JsonValueKind.Array)
                for(var i=0;i<value.GetArrayLength();i++)
                    foreach(var child in Walk(value[i],at+"/"+i))yield return child;
        }
        public string? Label(DataDocument d,string id)=>d.At(id) is null?null:id;
        public string? Parent(DataDocument d,string id)=>id==""?null:Pointer.Join(Pointer.Split(id).SkipLast(1));
    }
    private static string ArraySchema(string item,string limits="") =>
        "{\"type\":\"object\",\"properties\":{\"list\":{\"type\":\"array\",\"items\":"+item+limits+"}}}";
    private static void Same(string expected,Rig r)=>Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected),JsonNode.Parse(r.Text)),r.Text);
    private static void Refuses(Rig r,Func<bool> action)
    {
        var before=r.Session.Current;var rev=r.Session.Revision;var journal=r.Session.Journal.Count;var undo=r.Session.History.UndoCount;
        Assert.False(action());Assert.Same(before,r.Session.Current);Assert.Equal(rev,r.Session.Revision);
        Assert.Equal(journal,r.Session.Journal.Count);Assert.Equal(undo,r.Session.History.UndoCount);Assert.NotEmpty(r.Inspector.LastIssues);
    }
    private sealed class Quiet:IPlaySession { public string Status=>"paused";public void Draw(){}public void Dispose(){} }
    private sealed class Ui:IDisposable
    {
        internal readonly Rig R; internal readonly HeadlessHost Host=new(1280,850){Drawing=false};
        internal Ui(Rig r){R=r;EditorWindow<DataDocument>.Configure(ImGui.GetIO());Host.Frames(Draw,5);}
        internal void Draw()=>R.Window.DrawDocked();
        internal void Click(InspectorPart part)
        {
            var p=R.Window.InspectorPlace("",part);Assert.NotNull(p);
            Assert.InRange(p!.Value.Y,0,835);Host.Click(Draw,p.Value.X+8,p.Value.Y+6);Host.Frames(Draw,3);
        }
        internal void KeyButton(string label)
        {
            var pane=ImGui.Internal.FindWindowByName("Inspector###inspector"u8);Assert.False(pane.IsNull());
            var wanted=pane.GetID(Encoding.UTF8.GetBytes(label)).Value;
            for(var window=0;window<8;window++)
            {
                var ctx=ImGui.GetCurrentContext();
                var name=ctx.NavWindow.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)ctx.NavWindow.Name)??"";
                if(name.Contains("###inspector",StringComparison.Ordinal))
                    for(var n=0;n<160;n++)
                    {
                        if(ImGui.GetCurrentContext().NavId.Value==wanted){Host.Press(Draw,ImGuiKey.Space);Host.Frames(Draw,3);return;}
                        Host.Press(Draw,ImGuiKey.Tab);
                    }
                Host.Press(Draw,ImGuiKey.Tab,ctrl:true);
            }
            Assert.Fail("Keyboard cannot reach "+label);
        }
        internal void Type(string row,string text)
        {
            var p=R.Window.InspectorPlace(row);Assert.NotNull(p);
            Host.Click(Draw,p!.Value.X+12,p.Value.Y+7);
            Assert.True(ImGui.GetIO().WantTextInput);
            Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,text);Host.Press(Draw,ImGuiKey.Enter);Host.Frames(Draw,3);
        }
        public void Dispose()=>Host.Dispose();
    }

    // "Adds ... as one undo step, and selects it." Also preserve all unedited fields.
    [Fact] public void Object_clone_renumbers_id_and_preserves_nested_opaque_fields_through_history()
    {
        var r=new Rig("""{"list":[{"id":"crate","opaque":{"a":[1,null,"x"]}},{"id":"crate_2","opaque":{"a":[3]}}],"other":{"stay":8}}""");
        var before=r.Text;r.Select("/list");Assert.True(r.Inspector.AddHere());
        Assert.Equal("crate_2_2",r.At("/list/2/id").GetString());Assert.Equal("[3]",r.At("/list/2/opaque/a").GetRawText());
        Assert.Equal(new[]{"/list/2"},r.Editor.Selection);Assert.Equal(1,r.Session.History.UndoCount);
        Assert.True(r.Session.Undo());Same(before,r);Assert.True(r.Session.Redo());Assert.Equal(3,r.At("/list").GetArrayLength());
    }
    [Fact] public void Many_suffix_collisions_do_not_duplicate_an_existing_string_id()
    {
        var r=new Rig("""{"list":[{"id":"a_2"},{"id":"a_3"},{"id":"a"}]}""");r.Select("/list");Assert.True(r.Inspector.AddHere());
        var ids=r.At("/list").EnumerateArray().Select(x=>x.GetProperty("id").GetString()).ToArray();Assert.Equal(4,ids.Distinct().Count());
    }
    [Fact] public void Empty_record_list_uses_required_schema_defaults()
    {
        var r=new Rig("""{"list":[]}""",ArraySchema("""{"type":"object","required":["mass"],"properties":{"mass":{"type":"integer","default":7}}}"""));
        r.Select("/list");Assert.True(r.Inspector.AddHere());Assert.Equal(7,r.At("/list/0/mass").GetInt32());Assert.Equal("/list/0",r.Editor.Primary);
    }
    [Fact] public void Primitive_string_add_keeps_list_selected_and_the_new_value_can_be_typed_and_undone()
    {
        var r=new Rig("""{"list":["first","last"],"other":4}""");var before=r.Text;r.Select("/list");using var ui=new Ui(r);
        ui.Click(InspectorPart.Add);Assert.Equal("/list",r.Editor.Primary);Assert.Equal("last_2",r.At("/list/2").GetString());
        var added=Assert.Single(r.Inspector.Rows(),row=>DataEditing.PlaceOfRow(row.Id)=="/2");Assert.True(added.Editable);
        ui.Type(added.Id,"new last");Assert.Equal("new last",r.At("/list/2").GetString());Assert.Equal("first",r.At("/list/0").GetString());Assert.Equal("last",r.At("/list/1").GetString());Assert.Equal(4,r.At("/other").GetInt32());
        Assert.Equal(2,r.Session.History.UndoCount);Assert.True(r.Session.Undo());Assert.Equal("last_2",r.At("/list/2").GetString());Assert.True(r.Session.Undo());Same(before,r);
        Assert.True(r.Session.Redo());Assert.True(r.Session.Redo());Assert.Equal("new last",r.At("/list/2").GetString());
    }
    [Fact] public void A_list_of_lists_add_keeps_list_selected_and_a_nested_value_is_editable_without_aliasing()
    {
        var r=new Rig("""{"list":[[1],[2,3]]}""",ArraySchema("""{"type":"array","items":{"type":"integer"}}"""));var before=r.Text;r.Select("/list");using var ui=new Ui(r);ui.Click(InspectorPart.Add);
        Assert.Equal("/list",r.Editor.Primary);var added=Assert.Single(r.Inspector.Rows(),row=>DataEditing.PlaceOfRow(row.Id)=="/2");
        var child=Assert.Single(r.Inspector.Nested(added),row=>DataEditing.PlaceOfRow(row.Id)=="/2/0");Assert.True(child.Editable);
        ui.Type(child.Id,"9");Assert.Equal(9,r.At("/list/2/0").GetInt32());Assert.Equal(2,r.At("/list/1/0").GetInt32());Assert.Equal(3,r.At("/list/1/1").GetInt32());Assert.Equal(1,r.At("/list/0/0").GetInt32());
        Assert.True(r.Session.Undo());Assert.True(r.Session.Undo());Same(before,r);
    }
    [Fact] public void Mixed_shape_append_keeps_list_selected_and_exposes_the_new_nested_value_for_editing()
    {
        var r=new Rig("""{"list":[1,null,{"x":2},["tail"]]}""");var before=r.Text;r.Select("/list");using var ui=new Ui(r);ui.Click(InspectorPart.Add);
        Assert.Equal("""[1,null,{"x":2},["tail"],["tail"]]""",r.At("/list").GetRawText());Assert.Equal("/list",r.Editor.Primary);
        var added=Assert.Single(r.Inspector.Rows(),row=>DataEditing.PlaceOfRow(row.Id)=="/4");
        var child=Assert.Single(r.Inspector.Nested(added),row=>DataEditing.PlaceOfRow(row.Id)=="/4/0");Assert.True(child.Editable);
        ui.Type(child.Id,"new tail");Assert.Equal("new tail",r.At("/list/4/0").GetString());Assert.Equal("tail",r.At("/list/3/0").GetString());Assert.Equal(2,r.At("/list/2/x").GetInt32());Assert.Equal(JsonValueKind.Null,r.At("/list/1").ValueKind);
        Assert.True(r.Session.Undo());Assert.True(r.Session.Undo());Same(before,r);
    }
    [Fact] public void Enum_list_add_chooses_an_unused_value()
    {
        var r=new Rig("""{"list":["red"]}""",ArraySchema("""{"type":"string","enum":["red","blue"]}""",",\"uniqueItems\":true"));
        r.Select("/list");Assert.True(r.Inspector.AddHere());Assert.Equal("blue",r.At("/list/1").GetString());
    }
    [Fact] public void Exhausted_unique_enum_refuses_without_history()
    {
        var r=new Rig("""{"list":["red"]}""",ArraySchema("""{"type":"string","enum":["red"]}""",",\"uniqueItems\":true"));
        r.Select("/list");Refuses(r,r.Inspector.AddHere);
    }
    [Fact] public void MaxItems_refusal_is_atomic_for_selected_top_level_list()
    {
        var r=new Rig("""{"list":[{"id":"x"}]}""",ArraySchema("""{"type":"object"}""",",\"maxItems\":1"));r.Select("/list");Refuses(r,r.Inspector.AddHere);
    }
    [Fact] public void UniqueItems_refusal_preserves_the_original_primitive()
    {
        var r=new Rig("""{"list":[4]}""",ArraySchema("""{"type":"integer"}""",",\"uniqueItems\":true"));r.Select("/list");Refuses(r,r.Inspector.AddHere);
    }
    [Fact] public void MinItems_refusal_does_not_change_selection_or_history()
    {
        var r=new Rig("""{"list":[{"id":"only"}]}""",ArraySchema("""{"type":"object"}""",",\"minItems\":1"));r.Select("/list/0");Refuses(r,r.Inspector.RemoveHere);Assert.Equal("/list/0",r.Editor.Primary);
    }
    [Fact] public void Remove_middle_preserves_survivors_and_selects_the_list()
    {
        var r=new Rig("""{"list":[{"id":"a","n":1},{"id":"b","n":2},{"id":"c","n":3}],"unrelated":[8]}""");
        var before=r.Text;r.Select("/list/1");Assert.True(r.Inspector.RemoveHere());Assert.Equal("/list",r.Editor.Primary);
        Assert.Equal("c",r.At("/list/1/id").GetString());Assert.Equal(3,r.At("/list/1/n").GetInt32());Assert.Equal("[8]",r.At("/unrelated").GetRawText());
        r.Select("/unrelated");Assert.True(r.Session.Undo());Same(before,r);Assert.True(r.Session.Redo());Assert.Equal(2,r.At("/list").GetArrayLength());
    }
    [Fact] public void Null_item_is_removable_and_undo_restores_it()
    {
        var r=new Rig("""{"list":[null,7]}""",allPaths:true);r.Select("/list/0");Assert.True(r.Inspector.RemoveHere());Assert.Equal("[7]",r.At("/list").GetRawText());Assert.True(r.Session.Undo());Assert.Equal(JsonValueKind.Null,r.At("/list/0").ValueKind);
    }
    [Fact] public void Removing_from_inner_list_preserves_its_sibling_list()
    {
        var r=new Rig("""{"list":[[1,2],[7,8]]}""",allPaths:true);r.Select("/list/0/0");Assert.True(r.Inspector.RemoveHere());Assert.Equal("/list/0",r.Editor.Primary);Assert.Equal("[[2],[7,8]]",r.At("/list").GetRawText());
    }
    [Fact] public void Root_array_add_selects_append_and_root_item_can_be_removed()
    {
        var r=new Rig("""[{"id":"a"}]""",allPaths:true);r.Select("");Assert.True(r.Inspector.AddHere());Assert.Equal("/1",r.Editor.Primary);
        Assert.True(r.Inspector.RemoveHere());Assert.Equal(1,r.At("").GetArrayLength());
    }
    [Fact] public void Locked_focus_refuses_AddHere()
    {
        var r=new Rig("""{"list":[1],"elsewhere":{}}""");r.Select("/list");Assert.True(r.Editor.FocusOn("/elsewhere"));Assert.False(r.Inspector.CanAddHere);Refuses(r,r.Inspector.AddHere);
    }
    [Fact] public void Locked_focus_refuses_RemoveHere()
    {
        var r=new Rig("""{"list":[{"id":"a"}],"elsewhere":{}}""");r.Select("/list/0");Assert.True(r.Editor.FocusOn("/elsewhere"));Assert.False(r.Inspector.CanRemoveHere);Refuses(r,r.Inspector.RemoveHere);
    }
    [Fact] public void Playing_refuses_both_structural_operations_and_stop_restores_editing()
    {
        var r=new Rig("""{"list":[1]}""");r.Editor.Add(new PlayMode<DataDocument>("quiet","Preview",_=>new Quiet()));Assert.True(r.Editor.Play("quiet"));
        try{r.Select("/list");Refuses(r,r.Inspector.AddHere);r.Select("/list/0");Refuses(r,r.Inspector.RemoveHere);}
        finally{Assert.True(r.Editor.Stop());}
        r.Select("/list");Assert.True(r.Inspector.AddHere());
    }
    [Fact] public void Multiple_lists_or_items_never_apply_an_ambiguous_mutation()
    {
        var r=new Rig("""{"list":[1,2],"second":[3]}""");Assert.True(r.Editor.Select("/list","/second"));Assert.False(r.Inspector.CanAddHere);Refuses(r,r.Inspector.AddHere);
        Assert.True(r.Editor.Select("/list/0","/list/1"));Assert.False(r.Inspector.CanRemoveHere);Refuses(r,r.Inspector.RemoveHere);
    }
    [Fact] public void Declared_reference_is_not_rewritten_or_silently_retargeted_after_removal_and_undo()
    {
        var schema="""{"type":"object","properties":{"list":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"}}}},"owners":{"type":"array","items":{"type":"object","properties":{"ref":{"type":"string","x-ref":"/list"}}}}}}""";
        var r=new Rig("""{"list":[{"id":"a"},{"id":"b"}],"owners":[{"ref":"a"}]}""",schema);
        var schemas=new JsonSchemas().Add("audit",schema);var table=new DataTableView(r.Editor,DataEditing.ObjectType(schemas,"audit")){List="/owners",References=DataEditing.SchemaReferences(schemas,"audit")};
        Assert.Equal("/list/0",table.Target("/owners/0","ref"));r.Select("/list/0");Assert.True(r.Inspector.RemoveHere());
        Assert.Equal("a",r.At("/owners/0/ref").GetString());Assert.Null(table.Target("/owners/0","ref"));Assert.True(r.Session.Undo());Assert.Equal("/list/0",table.Target("/owners/0","ref"));
    }
    [Fact] public void Long_list_Add_selects_the_actual_last_record_and_is_one_step()
    {
        var r=new Rig(JsonSerializer.Serialize(new{list=Enumerable.Range(0,1200).Select(i=>new{id="box"+i,n=i}).ToArray()}));
        var before=r.Text;r.Select("/list");Assert.True(r.Inspector.AddHere());Assert.Equal("/list/1200",r.Editor.Primary);Assert.Equal(1199,r.At("/list/1200/n").GetInt32());Assert.Equal(1,r.Session.History.UndoCount);Assert.True(r.Session.Undo());Same(before,r);
    }
    [Fact] public void Real_mouse_Add_and_Remove_complete_an_undoable_round_trip()
    {
        var r=new Rig("""{"list":[{"id":"a","n":1}],"stay":7}""");var before=r.Text;r.Select("/list");using var ui=new Ui(r);
        ui.Click(InspectorPart.Add);Assert.Equal(2,r.At("/list").GetArrayLength());Assert.Equal("/list/1",r.Editor.Primary);
        ui.Click(InspectorPart.Remove);Same(before,r);Assert.True(r.Session.Undo());Assert.Equal(2,r.At("/list").GetArrayLength());Assert.True(r.Session.Undo());Same(before,r);
    }
    [Fact] public void Real_keyboard_reaches_and_activates_Add_item()
    {
        var r=new Rig("""{"list":[{"id":"a"}]}""");r.Select("/list");using var ui=new Ui(r);ui.KeyButton("Add item");Assert.Equal(2,r.At("/list").GetArrayLength());Assert.Equal("/list/1",r.Editor.Primary);
    }
    [Fact] public void Real_keyboard_reaches_and_activates_Remove_this_item()
    {
        var r=new Rig("""{"list":[{"id":"a"},{"id":"b"}]}""");r.Select("/list/0");using var ui=new Ui(r);ui.KeyButton("Remove this item");Assert.Equal(1,r.At("/list").GetArrayLength());Assert.Equal("b",r.At("/list/0/id").GetString());
    }
    [Fact] public void Enter_in_a_text_field_commits_without_accidentally_removing_the_record()
    {
        var r=new Rig("""{"list":[{"id":"a","name":"old"},{"id":"b","name":"stay"}]}""");r.Select("/list/0");using var ui=new Ui(r);ui.Type("name","typed");Assert.Equal(2,r.At("/list").GetArrayLength());Assert.Equal("typed",r.At("/list/0/name").GetString());Assert.Equal("stay",r.At("/list/1/name").GetString());
    }
    [Fact] public void After_removing_middle_real_field_input_edits_the_survivor_at_the_shifted_path()
    {
        var r=new Rig("""{"list":[{"id":"a","name":"one"},{"id":"b","name":"two"},{"id":"c","name":"three"}]}""");r.Select("/list/1");using var ui=new Ui(r);
        ui.Click(InspectorPart.Remove);r.Select("/list/1");ui.Host.Frames(ui.Draw,4);ui.Type("name","changed");
        Assert.Equal("a",r.At("/list/0/id").GetString());Assert.Equal("c",r.At("/list/1/id").GetString());Assert.Equal("changed",r.At("/list/1/name").GetString());Assert.True(r.Session.Undo());Assert.True(r.Session.Undo());Assert.Equal("b",r.At("/list/1/id").GetString());
    }
    private sealed class ProjectRig:IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"a119-"+Guid.NewGuid().ToString("N"));
        internal readonly string Original="""{"list":[{"id":"a"},{"id":"b"}],"stay":7}""";
        internal string FilePath=>Path.Combine(Root,"data","doc.json");
        internal readonly DataProject Project;internal readonly DataWorkspace Workspace;
        internal ProjectRig(bool readOnly=false,string? text=null)
        {
            if(text is not null)Original=text;
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(FilePath,Original);File.WriteAllText(Path.Combine(Root,"schemas","doc.schema.json"),"{}");
            Project=DataProject.Open(Path.Combine(Root,"data"),Path.Combine(Root,"schemas"),readOnly);
            Workspace=new(Project);Assert.True(Workspace.Show("doc.json"));
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact] public void Read_only_project_never_saves_structural_add_drafts()
    {
        using var r=new ProjectRig(true);var w=r.Workspace.Window!;Assert.True(w.Editor.Select("/list"));Assert.True(w.Inspector!.AddHere());Assert.False(w.Run("editor.save"));Assert.Contains("read-only",w.Status);Assert.Equal(r.Original,File.ReadAllText(r.FilePath));Assert.Equal(3,w.Editor.Session.Current.At("/list")!.Value.GetArrayLength());
    }
    [Fact] public void Read_only_project_never_saves_structural_remove_drafts()
    {
        using var r=new ProjectRig(true);var w=r.Workspace.Window!;Assert.True(w.Editor.Select("/list/0"));Assert.True(w.Inspector!.RemoveHere());Assert.False(w.Run("editor.save"));Assert.Equal(r.Original,File.ReadAllText(r.FilePath));Assert.True(w.Editor.Session.Undo());Assert.Equal(2,w.Editor.Session.Current.At("/list")!.Value.GetArrayLength());
    }
    [Fact] public void SaveCheck_refusal_keeps_the_structural_draft_history_and_disk_then_retry_saves()
    {
        using var r=new ProjectRig();var w=r.Workspace.Window!;Assert.True(w.Editor.Select("/list/0"));Assert.True(w.Inspector!.RemoveHere());
        r.Project.SaveCheck=_=>"INVENTED safety refusal";Assert.False(w.Run("editor.save"));Assert.Equal(r.Original,File.ReadAllText(r.FilePath));Assert.Equal(1,w.Editor.Session.History.UndoCount);
        r.Project.SaveCheck=null;Assert.True(w.Run("editor.save"));Assert.Equal("b",JsonDocument.Parse(File.ReadAllText(r.FilePath)).RootElement.GetProperty("list")[0].GetProperty("id").GetString());
    }
    [Fact] public void Structural_changes_save_reopen_and_undo_without_touching_unrelated_data()
    {
        using var r=new ProjectRig();var w=r.Workspace.Window!;Assert.True(w.Editor.Select("/list"));Assert.True(w.Inspector!.AddHere());Assert.True(w.Run("editor.save"));
        var reopened=DataProject.Open(Path.Combine(r.Root,"data"),Path.Combine(r.Root,"schemas"));var f=reopened.Find("doc.json")!;Assert.True(f.Open());Assert.Equal(3,f.Session!.Current.At("/list")!.Value.GetArrayLength());Assert.Equal(7,f.Session.Current.At("/stay")!.Value.GetInt32());
        Assert.True(w.Editor.Session.Undo());Assert.True(w.Run("editor.save"));Assert.True(JsonNode.DeepEquals(JsonNode.Parse(r.Original),JsonNode.Parse(File.ReadAllText(r.FilePath))));
    }

    // BORROWED LinkCheck's declared-map/snapshot contract; no automatic deletion warning is promised.
    [Fact] public async Task Declared_map_reports_the_removed_target_at_its_owner_without_retargeting_and_undo_clears_it()
    {
        using var r=new ProjectRig(text:"""{"list":[{"id":"a"},{"id":"b"}],"owners":[{"ref":"a"}],"stay":7}""");
        var map=ReferenceMap.Read("""{"game":"INVENTED A119","labels":[],"references":[{"file":"doc.json","at":"/owners/*/ref","target":{"file":"doc.json","list":"/list","key":"id"},"evidence":"INVENTED neutral link"}]}""");
        var check=new LinkCheck();var first=CheckSnapshot.Capture(r.Project,map);
        Assert.Empty((await check.RunAsync(first,new Progress<CheckProgress>(),CancellationToken.None)).Findings);
        var w=r.Workspace.Window!;Assert.True(w.Editor.Select("/list/0"));Assert.True(w.Inspector!.RemoveHere());
        var removed=CheckSnapshot.Capture(r.Project,map);Assert.NotEqual(first.InputHash,removed.InputHash);
        var report=await check.RunAsync(removed,new Progress<CheckProgress>(),CancellationToken.None);Assert.True(report.Complete);
        var finding=Assert.Single(report.Findings);Assert.Equal("doc.json",finding.Place!.File);Assert.Equal("/owners/0/ref",finding.Place.At);Assert.Contains("names nothing",finding.Problem.Message);Assert.NotEmpty(finding.Next);
        Assert.Equal("a",w.Editor.Session.Current.At("/owners/0/ref")!.Value.GetString());Assert.Equal(r.Original,File.ReadAllText(r.FilePath));
        Assert.True(w.Editor.Session.Undo());var restored=CheckSnapshot.Capture(r.Project,map);
        Assert.Equal(first.InputHash,restored.InputHash);Assert.Empty((await check.RunAsync(restored,new Progress<CheckProgress>(),CancellationToken.None)).Findings);
    }
}

