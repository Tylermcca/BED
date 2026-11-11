using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

// INVENTED fixtures/attacks against the root entry and public command/navigation contracts.
// BORROWED: the established HeadlessHost/temporary-project test pattern; no game data or extra dependency.
[Collection(ImGuiCollection.Name)]
public sealed class TopLevelAcceptanceTests(ITestOutputHelper output)
{
    private const string Schema = """
    {"type":"object","properties":{
      "note":{"type":"string","minLength":1},"amount":{"type":"integer","minimum":0},
      "items":{"type":"array","items":{"type":"object","properties":{"note":{"type":"string","maxLength":5},"amount":{"type":"integer","minimum":0},"link":{"type":"string"}}}},
      "settings":{"type":"object","properties":{"note":{"type":"string"},"amount":{"type":"integer"}}},
      "map":{"type":"object","additionalProperties":{"type":"string","default":"fresh"}}}}
    """;
    private const string Text = """{"note":"root","amount":1,"items":[{"note":"first","amount":2,"link":"root"},{"note":"next","amount":3}],"settings":{"note":"inner","amount":4},"map":{"one":"kept"}}""";
    private static JsonElement J(string text) { using var d=JsonDocument.Parse(text);return d.RootElement.Clone(); }
    private static string PointerOf(string key)=>Pointer.Join([key]);
    private static void Same(Rig r,DataDocument before,int steps=0)
    {
        Assert.Same(before,r.Session.Current);Assert.Equal(steps,r.Session.History.UndoCount);
    }

    [Fact]
    public void An_empty_id_is_one_selected_object_not_no_selection_or_two_duplicates()
    {
        using var r=new Rig();Assert.True(r.Editor.Select("",""));Assert.Single(r.Editor.Selection);Assert.Equal("",r.Editor.Primary);
        Assert.True(r.Editor.Toggle(""));Assert.Empty(r.Editor.Selection);Assert.Null(r.Editor.Primary);
        Assert.True(r.Editor.Toggle(""));Assert.Equal(new[]{""},r.Editor.Selection);
        Assert.True(r.Editor.Select());Assert.Empty(r.Editor.Selection);
    }

    [Theory]
    [InlineData("null")][InlineData("false")][InlineData("0")][InlineData("\"\"")]
    public void A_null_false_zero_or_empty_plain_value_keeps_the_root_reachable(string value)
    {
        using var r=new Rig("{\"solo\":"+value+",\"items\":[]}");
        Assert.Contains("",r.Editor.Outline.ObjectIds(r.Session.Current));r.Editor.Select("");
        var row=Assert.Single(r.Inspector.Rows(),x=>x.Id=="solo");Assert.Equal("solo",row.Label);
        Assert.True(r.Window.Reveal("/solo"),r.Window.Status);Assert.Equal("",r.Editor.Primary);
    }

    [Fact]
    public void Root_selection_does_not_hijack_a_tables_lists_rows_or_clipboard_target()
    {
        using var r=new Rig();r.Editor.Select("");var before=r.Session.Current;
        Assert.Equal(new[]{"/items"},r.Table.Lists());Assert.Equal(new[]{"/items/0","/items/1"},r.Table.Items());
        using var host=new HeadlessHost(){Drawing=false};ImGui.SetClipboardText("private sentinel");
        r.Table.Anchor=("","note");Assert.Null(r.Table.Copy());Assert.Equal("private sentinel",ImGui.GetClipboardText());
        var bad=r.Table.PlanPaste("","note","replacement");Assert.False(bad.Ok);Assert.False(r.Table.Apply(bad));Same(r,before);
    }

    [Fact]
    public void A_valid_table_copy_and_paste_while_root_is_selected_changes_only_the_grid_cell()
    {
        using var r=new Rig();using var host=new HeadlessHost(){Drawing=false};r.Editor.Select("");
        r.Table.Anchor=("/items/0","note");Assert.Equal("first",r.Table.Copy());
        var paste=r.Table.PlanPaste("/items/1","note",ImGui.GetClipboardText());Assert.True(paste.Ok);Assert.True(r.Table.Apply(paste),r.Table.Said);
        Assert.Equal("root",r.At("/note").GetString());Assert.Equal("first",r.At("/items/1/note").GetString());
        Assert.Equal("",r.Editor.Primary);Assert.Equal(1,r.Session.History.UndoCount);Assert.True(r.Session.Undo());Assert.Equal("next",r.At("/items/1/note").GetString());
    }

    [Theory]
    [InlineData("editor.copy")][InlineData("editor.paste")][InlineData("data.duplicate")][InlineData("scene.duplicate")]
    public void Unregistered_object_clipboard_commands_cannot_treat_the_root_as_a_scene_object(string command)
    {
        using var r=new Rig();r.Editor.Select("");var before=r.Session.Current;
        Assert.False(r.Window.Run(command));Assert.False(string.IsNullOrWhiteSpace(r.Window.Status));Same(r,before);
    }

    [Fact]
    public void Remove_here_and_add_here_cannot_delete_or_duplicate_the_whole_file()
    {
        using var r=new Rig();r.Editor.Select("");var before=r.Session.Current;
        Assert.False(r.Inspector.CanRemoveHere);Assert.False(r.Inspector.RemoveHere());
        Assert.False(r.Inspector.CanAddHere);Assert.False(r.Inspector.AddHere());Same(r,before);
        Assert.False(r.Editor.Execute(new SetValue("",null),""));Same(r,before);
    }

    [Fact]
    public void Search_replace_of_a_root_value_is_one_undo_and_preserves_the_other_hits()
    {
        using var r=new Rig("""{"note":"find","items":[{"note":"find"}],"settings":{"note":"find"}}""");
        var before=r.Session.Current;Assert.Equal(3,r.Window.Find("find"));
        var found=Assert.Single(r.Window.Found,x=>x.Pointer=="/note");Assert.True(r.Window.Reveal(found.Pointer),r.Window.Status);
        Assert.Equal("",r.Editor.Primary);Assert.True(r.Inspector.Set("note","replaced"));Assert.Equal("find",r.At("/items/0/note").GetString());
        Assert.Equal("find",r.At("/settings/note").GetString());Assert.Empty(r.Window.Found);
        Assert.True(r.Session.Undo());Assert.Same(before,r.Session.Current);Assert.Equal(3,r.Window.Find("find"));
    }

    [Theory]
    [InlineData("a/b")][InlineData("~1")][InlineData("/items/0/note")][InlineData("")][InlineData("(top level)")][InlineData("\u001f/note")]
    public void A_pointer_looking_plain_key_edits_that_literal_field_and_no_neighbour(string key)
    {
        var data=new JsonObject{["note"]="neighbour",["items"]=new JsonArray(new JsonObject{["note"]="child"})};data[key]="literal";
        using var r=new Rig(data.ToJsonString());r.Editor.Select("");
        var row=Assert.Single(r.Inspector.Rows(),x=>x.Label==key);
        Assert.True(r.Inspector.Set(row.Id,"changed"),string.Join(" ",r.Inspector.LastIssues));
        var expected=(JsonObject)data.DeepClone();expected[key]="changed";
        output.WriteLine("Literal-key result: "+r.Session.Current.Root.GetRawText());
        Assert.True(JsonNode.DeepEquals(expected,JsonNode.Parse(r.Session.Current.Root.GetRawText())),"A literal key was mistaken for a path or the root identity.");
        Assert.Equal(1,r.Session.History.UndoCount);Assert.True(r.Session.Undo());Assert.True(JsonNode.DeepEquals(data,JsonNode.Parse(r.Session.Current.Root.GetRawText())));
    }

    [Theory]
    [InlineData("a/b")][InlineData("~1")][InlineData("")][InlineData("/note")]
    public void Search_and_reveal_round_trip_literal_root_keys(string key)
    {
        using var r=new Rig(new JsonObject{[key]="unique target",["items"]=new JsonArray()}.ToJsonString());
        var found=Assert.Single(DataSearch.Find(r.Session.Current,"unique target").Places);Assert.Equal(PointerOf(key),found.Pointer);
        Assert.True(r.Window.Reveal(found.Pointer),r.Window.Status);Assert.Equal("",r.Editor.Primary);Assert.Equal(0,r.Session.History.UndoCount);
    }

    [Fact]
    public void Root_and_item_multi_edit_is_atomic_and_has_one_undo_step()
    {
        using var r=new Rig();r.Editor.Select("","/items/0");var before=r.Session.Current;
        Assert.False(r.Inspector.Set("note","too long"));Same(r,before);
        Assert.True(r.Inspector.Set("note","both"));Assert.Equal("both",r.At("/note").GetString());Assert.Equal("both",r.At("/items/0/note").GetString());
        Assert.Equal("next",r.At("/items/1/note").GetString());Assert.Equal(1,r.Session.History.UndoCount);Assert.True(r.Session.Undo());Assert.Same(before,r.Session.Current);
    }

    [Fact]
    public void Focus_on_a_list_locks_the_root_and_an_atomic_mixed_selection()
    {
        using var r=new Rig();Assert.True(r.Editor.FocusOn("/items"));r.Editor.Select("");var before=r.Session.Current;
        Assert.False(r.Inspector.Set("note","changed"));Same(r,before);
        r.Editor.Select("","/items/0");Assert.False(r.Inspector.Set("note","both"));Same(r,before);
        Assert.True(r.Window.Run("editor.focus_out"));Assert.True(r.Inspector.Set("note","both"));Assert.Equal(1,r.Session.History.UndoCount);
    }

    [Fact]
    public void An_issue_for_a_root_plain_field_can_be_followed_from_another_focus()
    {
        using var r=new Rig();r.Editor.Select("");Assert.False(r.Inspector.Set("amount",-1));
        var issue=Assert.Single(r.Inspector.LastIssues);output.WriteLine("Issue "+issue.ObjectId+" "+issue.Path+": "+issue.Message);
        Assert.True(r.Editor.FocusOn("/items"));r.Editor.Select("/items/0");
        Assert.True(r.Window.GoTo(issue),r.Window.Status);output.WriteLine("Issue navigation: "+r.Window.Status+"; selection="+JsonSerializer.Serialize(r.Editor.Selection));Assert.Equal("",r.Editor.Primary);Assert.True(r.Editor.InFocus(""));
        Assert.Equal(0,r.Session.History.UndoCount);
    }

    [Fact]
    public void Losing_the_only_plain_field_prunes_root_selection_and_focus_and_undo_restores_reachability()
    {
        using var r=new Rig("""{"solo":"value","items":[]}""");r.Editor.Select("");Assert.True(r.Editor.FocusOn(""));
        Assert.True(r.Session.Execute(new SetValue("/solo",J("{}"))));Assert.DoesNotContain("",r.Editor.Outline.ObjectIds(r.Session.Current));
        Assert.Empty(r.Editor.Selection);Assert.Empty(r.Editor.Focus);
        Assert.True(r.Session.Undo());Assert.Contains("",r.Editor.Outline.ObjectIds(r.Session.Current));Assert.True(r.Editor.Select(""));
        Assert.True(r.Session.Redo());Assert.Empty(r.Editor.Selection);Assert.Empty(r.Editor.Focus);
    }

    [Fact]
    public void Gaining_the_first_plain_field_then_undoing_does_not_keep_a_ghost_root()
    {
        using var r=new Rig("""{"items":[],"map":{}}""");Assert.DoesNotContain("",r.Editor.Outline.ObjectIds(r.Session.Current));
        Assert.True(r.Session.Execute(new SetValue("/solo",J("false"))));Assert.True(r.Editor.Select(""));Assert.True(r.Editor.FocusOn(""));
        Assert.True(r.Session.Undo());Assert.Empty(r.Editor.Selection);Assert.Empty(r.Editor.Focus);Assert.DoesNotContain("",r.Editor.Outline.ObjectIds(r.Session.Current));
        Assert.True(r.Session.Redo());Assert.Contains("",r.Editor.Outline.ObjectIds(r.Session.Current));Assert.True(r.Window.Reveal("/solo"),r.Window.Status);
    }

    [Fact]
    public void A_root_list_does_not_invent_the_object_only_root_outline_entry()
    {
        using var r=new Rig("""[{"note":"one"},{"note":"two"}]""","""{"type":"array","items":{"type":"object","properties":{"note":{"type":"string"}}}}""");
        var before=r.Session.Current;Assert.Empty(r.Editor.Outline.ObjectIds(before));Assert.False(r.Editor.Select(""));Assert.Null(r.Editor.Primary);
        Assert.Single(DataSearch.Find(before,"one").Places);Assert.False(r.Window.Reveal("/0/note"));Assert.False(string.IsNullOrWhiteSpace(r.Window.Status));Same(r,before);
        r.Table.List="";Assert.Equal(new[]{"/0","/1"},r.Table.Items());Assert.True(r.Table.SetCell("/1","note","changed"),r.Table.Said);
        Assert.Equal("one",r.At("/0/note").GetString());Assert.True(r.Session.Undo());Assert.Same(before,r.Session.Current);
    }

    [Theory]
    [InlineData("/items/0")][InlineData("~key")][InlineData("(top level)")]
    public void Named_entries_under_root_use_their_literal_keys_and_undo_cleanly(string key)
    {
        using var r=new Rig();r.Editor.Select("");var map=Assert.Single(r.Inspector.Rows(),x=>x.Id=="map");var before=r.Session.Current;
        Assert.True(r.Inspector.AddNamed(map,key),string.Join(" ",r.Inspector.LastIssues));
        Assert.Equal("kept",r.At("/map"+PointerOf(key)).GetString());Assert.Equal("root",r.At("/note").GetString());
        Assert.True(r.Session.Undo());Assert.Same(before,r.Session.Current);
    }

    [Theory]
    [InlineData("doc.json")][InlineData("other.json")]
    public void Workspace_navigation_to_a_root_field_and_back_preserves_the_real_empty_selection_id(string targetFile)
    {
        using var r=new Rig();r.Editor.Select("/items/0");var before=r.Session.Current;
        Assert.True(r.Workspace.GoTo(new ReferenceTarget(targetFile,"/note")),r.Workspace.Said);Assert.Equal("",r.Workspace.Window!.Editor.Primary);
        Assert.True(r.Workspace.Back());Assert.Equal("doc.json",r.Workspace.Active!.Name);Assert.Equal("/items/0",r.Editor.Primary);Same(r,before);
        r.Editor.Select("");Assert.True(r.Workspace.GoTo(new ReferenceTarget("other.json","/items/1")));
        Assert.True(r.Workspace.Back());Assert.Equal("",r.Editor.Primary);
    }

    [Fact]
    public void A_same_file_table_link_to_the_file_itself_selects_the_empty_id_and_back_returns()
    {
        using var r=new Rig();r.Editor.Select("/items/0");r.Table.Links=(_,_)=>new ReferenceTarget("doc.json","");
        Assert.True(r.Table.Follow("/items/0","link"),r.Table.Said);Assert.Equal("",r.Editor.Primary);
        Assert.True(r.Table.Back());Assert.Equal("/items/0",r.Editor.Primary);Assert.Equal(0,r.Session.History.UndoCount);
    }

    [Theory]
    [InlineData("doc.json")][InlineData("other.json")]
    public void Table_links_to_plain_fields_find_their_root_owner_in_same_and_other_files(string targetFile)
    {
        using var r=new Rig();r.Editor.Select("/items/0");r.Table.Links=(_,_)=>new ReferenceTarget(targetFile,"/note");
        Assert.True(r.Table.Follow("/items/0","link"),r.Table.Said);output.WriteLine("Table link "+targetFile+": "+r.Table.Said+"; selection="+JsonSerializer.Serialize(r.Workspace.Window!.Editor.Selection));Assert.Equal("",r.Workspace.Window!.Editor.Primary);
        Assert.True(r.Table.Back());Assert.Equal("doc.json",r.Workspace.Active!.Name);Assert.Equal("/items/0",r.Editor.Primary);
    }

    [Fact]
    public void A_stale_root_search_reveal_uses_the_new_structured_owner_without_mutation()
    {
        using var r=new Rig("""{"solo":"needle","items":[]}""");Assert.Equal(1,r.Window.Find("needle"));var old=r.Window.Found[0].Pointer;
        Assert.True(r.Session.Execute(new SetValue("/solo",J("{\"inner\":\"now structured\"}"))));Assert.Empty(r.Window.Found);
        Assert.True(r.Window.Reveal(old));Assert.Equal("/solo",r.Editor.Primary);var before=r.Session.Current;
        Assert.DoesNotContain(r.Inspector.Rows(),x=>x.Id=="solo");Assert.Equal("now structured",r.At("/solo/inner").GetString());Same(r,before,1);
    }

    [Fact]
    public void Actual_mouse_and_keys_edit_a_root_field_then_undo_and_save_reopen()
    {
        using var r=new Rig();using var host=new HeadlessHost(1400,1800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(r.Workspace.Draw,5);Assert.True(r.Window.Reveal("/note"),r.Window.Status);host.Frames(r.Workspace.Draw,4);
        var row=Assert.Single(r.Inspector.Rows(),x=>x.Id=="note");var at=r.Window.InspectorPlace(row.Id)!.Value;
        host.Click(r.Workspace.Draw,at.X+12,at.Y+7);Assert.True(ImGui.GetIO().WantTextInput);
        host.Press(r.Workspace.Draw,ImGuiKey.A,ctrl:true);host.Press(r.Workspace.Draw,ImGuiKey.Backspace);host.Type(r.Workspace.Draw,"typed");host.Press(r.Workspace.Draw,ImGuiKey.Enter);host.Frames(r.Workspace.Draw,3);
        Assert.Equal("typed",r.At("/note").GetString());Assert.True(r.Window.Run("editor.undo"));Assert.Equal("root",r.At("/note").GetString());
        Assert.True(r.Window.Run("editor.redo"));Assert.True(r.Window.Run("editor.save"),r.Window.Status);
        var fresh=r.Open();Assert.True(fresh.Show("doc.json"));Assert.Equal("typed",fresh.Active!.Session!.Current.At("/note")!.Value.GetString());
        Assert.Equal("first",fresh.Active.Session.Current.At("/items/0/note")!.Value.GetString());
    }

    private sealed class Rig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("top-level-acceptance-").FullName;
        public DataProject Project {get;}
        public DataWorkspace Workspace {get;}
        public EditorWindow<DataDocument> Window=>Workspace.Window!;
        public EditorModel<DataDocument> Editor=>Window.Editor;
        public Inspector<DataDocument> Inspector=>Window.Inspector!;
        public DocumentSession<DataDocument> Session=>Editor.Session;
        public DataTableView Table=>Workspace.TableOf("doc.json")!;
        public Rig(string text=Text,string schema=Schema)
        {
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));
            File.WriteAllText(Path.Combine(root,"schemas/r.schema.json"),schema);
            foreach(var name in new[]{"doc.json","other.json"})File.WriteAllText(Path.Combine(root,"data",name),text);
            Project=Make();Workspace=new(Project);Assert.True(Workspace.Show("doc.json"),Workspace.Said);
        }
        private DataProject Make(){var p=DataProject.Open(Path.Combine(root,"data"),Path.Combine(root,"schemas"));p.SchemaFor=_=>"r.schema.json";return p;}
        public DataWorkspace Open()=>new(Make());
        public JsonElement At(string pointer)=>Session.Current.At(pointer)!.Value;
        public void Dispose()=>Directory.Delete(root,recursive:true);
    }
}
