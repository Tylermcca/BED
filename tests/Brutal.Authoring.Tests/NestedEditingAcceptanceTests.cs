using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// A079. INVENTED neutral crate documents and independent acceptance oracles.
// BORROWED contracts: Inspector XML, goals 1/12/15 and usability profile U05/U06/U12/U18/U20/U22.
// Headless input is real ImGui input. No product source, native window, game record or extra dependency.
[Collection(ImGuiCollection.Name)]
public sealed class NestedEditingAcceptanceTests(ITestOutputHelper output)
{
    private sealed class Rig
    {
        internal readonly JsonSchemas Schemas;
        internal readonly DocumentSession<DataDocument> Session;
        internal readonly EditorModel<DataDocument> Editor;
        internal readonly Inspector<DataDocument> Inspector;
        internal readonly EditorWindow<DataDocument> Window;
        internal Rig(string unit, string unitSchema="{}", string? whole=null, string? wholeSchema=null)
        {
            Schemas=new JsonSchemas().Add("audit",wholeSchema??"{\"type\":\"object\",\"properties\":{\"unit\":"+unitSchema+"}}");
            Session=DataKit.Session(new DataFormat().Read(whole??"{\"unit\":"+unit+"}"),Schemas,"audit");
            Editor=new EditorModel<DataDocument>(Session,DataEditing.Outline);
            Inspector=new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(Schemas,"audit"));
            Editor.Select("/unit"); Window=new(Editor,Inspector);
            Assert.DoesNotContain(Session.Validator.Validate(Session.Current),x=>x.Severity==Severity.Error);
        }
        internal InspectorRow Row(string id)=>Inspector.Rows().Single(r=>r.Id==id);
        internal InspectorRow[] Items(string id="list")=>Inspector.Nested(Row(id)).ToArray();
        internal JsonElement At(string path)=>Session.Current.At(path)!.Value;
        internal int Count(string path="/unit/list")=>At(path).GetArrayLength();
        internal string Text=>Session.Current.Root.GetRawText();
    }
    private static string ListSchema(string item="{}",string limits="")=>
        "{\"type\":\"object\",\"properties\":{\"list\":{\"type\":\"array\",\"items\":"+item+limits+"}}}";
    private static void Refusal(Rig r,Func<bool> act)
    {
        var before=r.Session.Current;var rev=r.Session.Revision;var journal=r.Session.Journal.Count;
        Assert.False(act()); Assert.Same(before,r.Session.Current);
        Assert.Equal(rev,r.Session.Revision);Assert.Equal(journal,r.Session.Journal.Count);Assert.NotEmpty(r.Inspector.LastIssues);
    }
    private sealed class QuietPlay:IPlaySession
    { public string Status=>"paused"; public void Draw(){} public void Dispose(){} }

    private sealed class Ui:IDisposable
    {
        internal readonly Rig R;
        internal readonly HeadlessHost Host;
        internal Ui(Rig r,int width=1280,int height=800,float scale=1)
        {
            R=r;Host=new(width,height){Drawing=false};
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            EditorWindow<DataDocument>.SetTextScale(scale);
            Host.Frames(Draw,5);
        }
        internal void Draw()=>R.Window.DrawDocked();
        private ImGuiWindowPtr Pane()=>ImGui.Internal.FindWindowByName("Inspector###inspector"u8);
        private ImGuiID Id(IEnumerable<string> stack,string caption)
        {
            var window=Pane();Assert.False(window.IsNull());
            var seed=window.GetID(Encoding.UTF8.GetBytes(stack.First()));
            foreach(var row in stack.Skip(1))seed=ImGui.Internal.GetIDWithSeed(Encoding.UTF8.GetBytes(row),seed);
            return ImGui.Internal.GetIDWithSeed(Encoding.UTF8.GetBytes(caption),seed);
        }
        internal (float X0,float Y0,float X1,float Y1,float C0,float C1,float C2,float C3) Focus(IEnumerable<string> stack,params string[] captions)
        {
            var wanted=captions.Select(s=>Id(stack,s).Value).ToHashSet();
            // Ctrl+Tab is the public keyboard window route; Tab walks actual submitted controls. Never set NavId/focus.
            for(var window=0;window<6;window++)
            {
                var context=ImGui.GetCurrentContext();
                var name=context.NavWindow.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)context.NavWindow.Name)??"";
                if(name.Contains("###inspector",StringComparison.Ordinal))
                    for(var item=0;item<48;item++)
                    {
                        context=ImGui.GetCurrentContext();
                        if(wanted.Contains(context.NavId.Value))
                        {
                            var w=context.NavWindow;var rect=w.NavRectRel[(int)context.NavLayer];var o=w.DC.CursorStartPos;var clip=w.InnerClipRect;
                            return(o.X+rect.Min.X,o.Y+rect.Min.Y,o.X+rect.Max.X,o.Y+rect.Max.Y,clip.Min.X,clip.Min.Y,clip.Max.X,clip.Max.Y);
                        }
                        Host.Press(Draw,ImGuiKey.Tab);
                    }
                Host.Press(Draw,ImGuiKey.Tab,ctrl:true);
            }
            Assert.Fail("Keyboard cannot reach inspector control: "+string.Join(" or ",captions));return default;
        }
        internal void Button(string[] stack,string caption,bool mouse=false,bool requireVisible=false)
        {
            var b=Focus(stack,caption);
            if(requireVisible)Assert.True(b.X0>=b.C0-1&&b.Y0>=b.C1-1&&b.X1<=b.C2+1&&b.Y1<=b.C3+1,
                $"Button '{caption}' bounds [{b.X0},{b.Y0},{b.X1},{b.Y1}] clipped by [{b.C0},{b.C1},{b.C2},{b.C3}]");
            if(mouse)Host.Click(Draw,(b.X0+b.X1)/2,(b.Y0+b.Y1)/2);
            else Host.Press(Draw,ImGuiKey.Space);
        }
        internal void Field(string[] stack,string caption,string value)
        {
            // Field identity stays on its exact row when override markers or wrapping change its caption.
            Focus(stack,caption+"###field","##"+caption+"###field");
            if(!ImGui.GetIO().WantTextInput)Host.Press(Draw,ImGuiKey.Enter);
            Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,value);
        }
        public void Dispose()=>Host.Dispose();
    }

    [Fact] public void Adding_an_object_copies_last_values_and_gives_its_string_id_a_free_suffix()
    {
        var r=new Rig("""{"list":[{"id":"crate","box":{"weight":4}},{"id":"crate_2","box":{"weight":9}}]}""");
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(3,r.Count());
        Assert.Equal("crate_2_2",r.At("/unit/list/2/id").GetString());Assert.Equal(9,r.At("/unit/list/2/box/weight").GetInt32());
        Assert.True(r.Session.Undo());Assert.Equal(2,r.Count());Assert.True(r.Session.Redo());Assert.Equal(3,r.Count());
    }
    [Fact] public void String_id_suffix_skips_all_collisions_in_the_destination()
    {
        var r=new Rig("""{"list":[{"id":"c_2"},{"id":"c_3"},{"id":"c"}]}""");
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal("c_4",r.At("/unit/list/3/id").GetString());
    }
    [Theory][InlineData("17")][InlineData("true")][InlineData("null")]
    public void Non_string_ids_are_preserved_without_inventing_a_new_id_policy(string id)
    {
        var r=new Rig("{\"list\":[{\"id\":"+id+",\"weight\":4}]}");
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(id,r.At("/unit/list/1/id").GetRawText());
    }
    [Fact] public void Objects_without_ids_are_copied_without_an_added_id()
    {
        var r=new Rig("""{"list":[{"weight":4}]}""");Assert.True(r.Inspector.AddItem(r.Row("list")));
        Assert.Equal("""{"weight":4}""",r.At("/unit/list/1").GetRawText());
    }
    [Fact] public void An_empty_string_list_can_start_from_its_schema()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"string"}"""));
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal("",r.At("/unit/list/0").GetString());
    }
    [Fact] public void Schema_default_is_used_for_an_empty_list()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"integer","default":7}"""));
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(7,r.At("/unit/list/0").GetInt32());
    }
    [Fact] public void Schema_enum_supplies_a_valid_first_item_and_choices()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"string","enum":["bronze","silver"]}"""));
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal("bronze",r.At("/unit/list/0").GetString());
        var row=Assert.Single(r.Items());Assert.Equal(PropertyKind.Choice,row.Kind);Assert.Equal(new[]{"bronze","silver"},row.Choices);
    }
    [Fact] public void Compatible_elsewhere_sample_can_seed_an_empty_list()
    {
        var r=new Rig("""{"list":[],"other":{"list":[{"id":"seed","weight":5}]}}""");
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal("seed",r.At("/unit/list/0/id").GetString());
    }
    [Fact] public void A_different_context_sample_must_not_override_a_valid_target_default()
    {
        var r=new Rig("""{"list":[],"other":{"list":["alien"]}}""",ListSchema("""{"type":"integer","default":7}"""));
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(7,r.At("/unit/list/0").GetInt32());
    }
    [Fact] public void Required_object_fields_with_defaults_can_seed_a_new_item()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"object","properties":{"mass":{"type":"integer","default":3}},"required":["mass"],"additionalProperties":false}"""));
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(3,r.At("/unit/list/0/mass").GetInt32());
    }
    [Fact] public void A_list_of_lists_can_start_with_an_empty_list_from_schema()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"array","items":{"type":"integer"}}"""));
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(JsonValueKind.Array,r.At("/unit/list/0").ValueKind);
    }
    [Fact] public void Referenced_item_schema_reaches_nested_enum_choices()
    {
        var wholeSchema="""{"type":"object","$defs":{"metal":{"type":"string","enum":["copper","tin"]}},"properties":{"unit":{"type":"object","properties":{"list":{"type":"array","items":{"$ref":"#/$defs/metal"}}}}}}""";
        var r=new Rig("""{"list":["copper"]}""",wholeSchema:wholeSchema);
        Assert.Equal(PropertyKind.Choice,r.Items()[0].Kind);Assert.Equal(new[]{"copper","tin"},r.Items()[0].Choices);
        Assert.True(r.Inspector.Set(r.Items()[0].Id,"tin"));Assert.Equal("tin",r.At("/unit/list/0").GetString());
    }
    [Fact] public void OneOf_keeps_invalid_nested_edits_atomic()
    {
        var r=new Rig("""{"list":[{"n":2}]}""",ListSchema("""{"oneOf":[{"type":"object","properties":{"n":{"type":"integer","minimum":1}},"required":["n"],"additionalProperties":false},{"type":"string","enum":["empty"]}]}"""));
        var item=r.Items()[0];var n=r.Inspector.Nested(item).Single(x=>x.Label=="n");
        Refusal(r,()=>r.Inspector.Set(n.Id,"-1"));
    }
    [Fact] public void AnyOf_allows_a_valid_nested_branch_edit()
    {
        var r=new Rig("""{"list":[{"name":"a"}]}""",ListSchema("""{"anyOf":[{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]},{"type":"integer"}]}"""));
        var name=Assert.Single(r.Inspector.Nested(r.Items()[0]));Assert.True(r.Inspector.Set(name.Id,"b"));Assert.Equal("b",r.At("/unit/list/0/name").GetString());
    }
    [Fact] public void Additional_properties_schema_controls_a_nested_scalar()
    {
        var r=new Rig("""{"box":{"copper":2}}""","""{"type":"object","properties":{"box":{"type":"object","additionalProperties":{"type":"integer","minimum":0}}}}""");
        var row=Assert.Single(r.Inspector.Nested(r.Row("box")));Assert.Equal(PropertyKind.Integer,row.Kind);
        Assert.True(r.Inspector.Set(row.Id,"3"));Assert.Equal(3,r.At("/unit/box/copper").GetInt32());Refusal(r,()=>r.Inspector.Set(row.Id,"-1"));
    }
    [Fact] public void MaxItems_refusal_keeps_history_and_document()
    {
        var r=new Rig("""{"list":[1]}""",ListSchema("""{"type":"integer"}""",",\"maxItems\":1"));
        Refusal(r,()=>r.Inspector.AddItem(r.Row("list")));
    }
    [Fact] public void MinItems_refusal_keeps_history_and_document()
    {
        var r=new Rig("""{"list":[1]}""",ListSchema("""{"type":"integer"}""",",\"minItems\":1"));
        Refusal(r,()=>r.Inspector.RemoveItem(r.Items()[0]));
    }
    [Fact] public void UniqueItems_refuses_a_duplicate_primitive_without_recording_a_step()
    {
        var r=new Rig("""{"list":[4]}""",ListSchema("""{"type":"integer"}""",",\"uniqueItems\":true"));
        Refusal(r,()=>r.Inspector.AddItem(r.Row("list")));
    }
    [Fact] public void Item_default_reset_uses_its_own_schema_value()
    {
        var r=new Rig("""{"list":[{"weight":9}]}""",ListSchema("""{"type":"object","properties":{"weight":{"type":"integer","default":3}}}"""));
        var row=Assert.Single(r.Inspector.Nested(r.Items()[0]));Assert.True(row.HasDefault);
        Assert.True(r.Inspector.Reset(row.Id));Assert.Equal(3,r.At("/unit/list/0/weight").GetInt32());Assert.True(r.Session.Undo());Assert.Equal(9,r.At("/unit/list/0/weight").GetInt32());
    }
    [Theory][InlineData("a/b")][InlineData("~")][InlineData("灯台")][InlineData("")]
    public void Unusual_keys_at_each_depth_edit_only_the_named_leaf(string key)
    {
        var doc=new JsonObject{[key]=new JsonObject{[key]=new JsonObject{[key]="old"}}};
        var r=new Rig(doc.ToJsonString());var a=r.Row(key);var b=Assert.Single(r.Inspector.Nested(a));var c=Assert.Single(r.Inspector.Nested(b));
        Assert.True(r.Inspector.Set(c.Id,"new"));
        Assert.Equal("new",r.At("/unit"+Pointer.Join(new[]{key,key,key})).GetString());Assert.True(r.Session.Undo());
    }
    [Fact] public void A_slash_key_is_distinct_from_a_similarly_spelled_nested_pointer()
    {
        var r=new Rig("""{"a/b":"flat","a":{"b":"nested"}}""");
        Assert.True(r.Inspector.Set("a/b","changed"));Assert.Equal("nested",r.At("/unit/a/b").GetString());Assert.Equal("changed",r.At("/unit/a~1b").GetString());
    }
    [Fact] public void Null_items_are_read_only_but_removable()
    {
        var r=new Rig("""{"list":[null,2]}""");var row=r.Items()[0];Assert.False(row.Editable);Assert.True(row.CanRemove);
        Assert.True(r.Inspector.RemoveItem(row));Assert.Equal(2,r.At("/unit/list/0").GetInt32());Assert.True(r.Session.Undo());Assert.Equal(JsonValueKind.Null,r.At("/unit/list/0").ValueKind);
    }
    [Fact] public void Mixed_type_removal_preserves_all_survivor_values()
    {
        var r=new Rig("""{"list":[1,"two",null,{"x":4},[5]]}""");
        Assert.True(r.Inspector.RemoveItem(r.Items()[2]));Assert.Equal("""[1,"two",{"x":4},[5]]""",r.At("/unit/list").GetRawText());
        Assert.True(r.Session.Undo());Assert.Equal(5,r.Count());
    }
    [Fact] public void Inner_list_add_remove_and_undo_stay_in_the_right_list()
    {
        var r=new Rig("""{"list":[[1,2],[8]]}""");var inner=r.Items()[0];
        Assert.True(r.Inspector.AddItem(inner));Assert.Equal("""[[1,2,2],[8]]""",r.At("/unit/list").GetRawText());
        Assert.True(r.Inspector.RemoveItem(r.Inspector.Nested(inner)[1]));Assert.Equal("""[[1,2],[8]]""",r.At("/unit/list").GetRawText());
        Assert.True(r.Session.Undo());Assert.Equal(3,r.At("/unit/list/0").GetArrayLength());
    }
    [Fact] public void Several_selected_objects_edit_nested_scalars_as_one_undo_step()
    {
        var r=new Rig("{}",whole:"""{"unit":{"box":{"mass":1}},"other":{"box":{"mass":2}}}""",wholeSchema:"""{"type":"object","additionalProperties":{"type":"object","properties":{"box":{"type":"object","properties":{"mass":{"type":"integer"}}}}}}""");
        r.Editor.Select("/unit","/other");var row=Assert.Single(r.Inspector.Nested(r.Row("box")));
        Assert.True(row.Mixed);Assert.True(r.Inspector.Set(row.Id,"7"));Assert.Equal(7,r.At("/unit/box/mass").GetInt32());Assert.Equal(7,r.At("/other/box/mass").GetInt32());
        Assert.Single(r.Session.Journal);Assert.True(r.Session.Undo());Assert.Equal(1,r.At("/unit/box/mass").GetInt32());Assert.Equal(2,r.At("/other/box/mass").GetInt32());
    }
    [Fact] public void Several_selected_lists_do_not_offer_ambiguous_add_or_remove()
    {
        var r=new Rig("{}",whole:"""{"unit":{"list":[1]},"other":{"list":[2,3]}}""");r.Editor.Select("/unit","/other");
        var row=r.Row("list");Assert.False(row.CanAdd);Assert.Empty(r.Inspector.Nested(row));Refusal(r,()=>r.Inspector.AddItem(row));
    }
    [Fact] public void Locked_focus_refuses_list_mutation_atomically()
    {
        var r=new Rig("{}",whole:"""{"unit":{"list":[1]},"other":{}}""");var list=r.Row("list");var item=r.Items()[0];
        Assert.True(r.Editor.FocusOn("/other"));Refusal(r,()=>r.Inspector.AddItem(list));Refusal(r,()=>r.Inspector.RemoveItem(item));Assert.Contains(r.Inspector.LastIssues,x=>x.Message.Contains("focus"));
    }
    [Fact] public void Locked_focus_does_not_advertise_active_list_mutation_buttons()
    {
        var r=new Rig("{}",whole:"""{"unit":{"list":[1]},"other":{}}""");Assert.True(r.Editor.FocusOn("/other"));
        Assert.False(r.Row("list").CanAdd);Assert.All(r.Items(),x=>Assert.False(x.CanRemove));
    }
    [Fact] public void Play_mode_refuses_list_mutation_and_returns_to_editing()
    {
        var r=new Rig("""{"list":[1]}""");var list=r.Row("list");var item=r.Items()[0];
        r.Editor.Add(new PlayMode<DataDocument>("audit.pause","Preview",_=>new QuietPlay()));Assert.True(r.Editor.Play("audit.pause"));
        try{Refusal(r,()=>r.Inspector.AddItem(list));Refusal(r,()=>r.Inspector.RemoveItem(item));}
        finally{Assert.True(r.Editor.Stop());}
        Assert.True(r.Inspector.AddItem(r.Row("list")));Assert.Equal(2,r.Count());
    }
    [Fact] public void Undo_removing_the_item_being_shown_leaves_no_phantom_children()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"object","default":{"mass":1}}"""));
        Assert.True(r.Session.Execute(new InsertItem("/unit/list",0,JsonSerializer.SerializeToElement(new { mass=1 }))));Assert.NotEmpty(r.Inspector.Nested(r.Items()[0]));Assert.True(r.Session.Undo());
        Assert.Empty(r.Items());using var ui=new Ui(r);ui.Host.Frames(ui.Draw,3);Assert.Equal(0,r.Count());
    }
    [Fact] public void Real_mouse_click_adds_exactly_one_item_and_one_undo_step()
    {
        var r=new Rig("""{"list":[1,2]}""");using var ui=new Ui(r);
        ui.Button(["list"],"Add",mouse:true);Assert.Equal(3,r.Count());Assert.Single(r.Session.Journal);Assert.True(r.Session.Undo());Assert.Equal(2,r.Count());
    }
    [Fact] public void Real_mouse_removal_of_a_middle_item_does_not_retarget_later_rows()
    {
        var r=new Rig("""{"list":[{"id":"a","mass":1},{"id":"b","mass":2},{"id":"c","mass":3}]}""");using var ui=new Ui(r);
        ui.Button(["list","\u001f/list/1"],"Remove",mouse:true);
        ui.Host.Frames(ui.Draw,3);Assert.Equal(2,r.Count());Assert.Equal("c",r.At("/unit/list/1/id").GetString());Assert.Equal(3,r.At("/unit/list/1/mass").GetInt32());
        Assert.True(r.Session.Undo());ui.Host.Frames(ui.Draw,2);Assert.Equal("b",r.At("/unit/list/1/id").GetString());
    }
    [Fact] public void Keyboard_alone_adds_one_item()
    {
        var r=new Rig("""{"list":[1,2]}""");using var ui=new Ui(r);
        ui.Button(["list"],"Add");Assert.Equal(3,r.Count());Assert.Single(r.Session.Journal);
    }
    [Fact] public void Keyboard_alone_removes_the_named_item()
    {
        var r=new Rig("""{"list":[1,2,3]}""");using var ui=new Ui(r);
        ui.Button(["list","\u001f/list/1"],"Remove");Assert.Equal("""[1,3]""",r.At("/unit/list").GetRawText());Assert.Single(r.Session.Journal);
    }
    [Fact] public void Keyboard_maxItems_refusal_keeps_the_document_and_reports_why()
    {
        var r=new Rig("""{"list":[1]}""",ListSchema("""{"type":"integer"}""",",\"maxItems\":1"));using var ui=new Ui(r);var before=r.Text;
        ui.Button(["list"],"Add");Assert.Equal(before,r.Text);Assert.Empty(r.Session.Journal);Assert.NotEmpty(r.Window.Status);Assert.NotEmpty(r.Inspector.LastIssues);
    }
    [Fact] public void Repeated_real_add_clicks_do_not_duplicate_string_ids()
    {
        var r=new Rig("""{"list":[{"id":"crate"}]}""");using var ui=new Ui(r);
        ui.Button(["list"],"Add",mouse:true);ui.Button(["list"],"Add",mouse:true);
        var ids=r.At("/unit/list").EnumerateArray().Select(x=>x.GetProperty("id").GetString()).ToArray();
        Assert.Equal(3,ids.Distinct().Count());Assert.Equal(2,r.Session.Journal.Count);
    }
    [Fact] public void An_uncommitted_field_below_a_changed_list_keeps_its_own_text()
    {
        var r=new Rig("""{"list":[1],"note":"memo"}""");using var ui=new Ui(r);
        ui.Field(["note"],"note","half typed");Assert.Equal("memo",r.At("/unit/note").GetString());
        Assert.True(r.Inspector.AddItem(r.Row("list")));ui.Host.Frames(ui.Draw,2);ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        Assert.Equal("half typed",r.At("/unit/note").GetString());Assert.Equal(2,r.Count());
    }
    [Fact] public void Refused_text_on_a_removed_positional_item_does_not_attach_to_its_successor()
    {
        var r=new Rig("""{"list":[{"id":"a","mass":1},{"id":"b","mass":2},{"id":"c","mass":3}]}""",ListSchema("""{"type":"object","properties":{"id":{"type":"string"},"mass":{"type":"integer"}}}"""));
        using var ui=new Ui(r);
        ui.Field(["list","\u001f/list/1","\u001f/list/1/mass"],"mass","unfinished");
        ui.Host.Press(ui.Draw,ImGuiKey.Enter);Assert.Equal(2,r.At("/unit/list/1/mass").GetInt32());
        ui.Button(["list","\u001f/list/0"],"Remove",mouse:true);Assert.Equal("c",r.At("/unit/list/1/id").GetString());
        ui.Focus(["list","\u001f/list/1","\u001f/list/1/mass"],"mass###field","##mass###field");
        if(!ImGui.GetIO().WantTextInput)ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        ui.Host.Press(ui.Draw,ImGuiKey.A,ctrl:true);ui.Host.Press(ui.Draw,ImGuiKey.C,ctrl:true);
        Assert.Equal("3",ImGui.GetClipboardText().ToString());Assert.Equal(3,r.At("/unit/list/1/mass").GetInt32());
    }
    [Theory][InlineData(1280,1f)][InlineData(900,2f)]
    public void Add_and_remove_remain_fully_visible_at_normal_and_large_text(int width,float scale)
    {
        var r=new Rig("""{"list":[1,2]}""");using var ui=new Ui(r,width,800,scale);
        ui.Focus(["list","\u001f/list/0"],"Remove");
        var dir=Path.Combine(Path.GetTempPath(),"A079-evidence");Directory.CreateDirectory(dir);
        ui.Host.Drawing=true;ui.Host.Frame(ui.Draw);ui.Host.Save(Path.Combine(dir,$"inspector-{width}-{scale}.png"));ui.Host.Drawing=false;
        ui.Button(["list","\u001f/list/0"],"Remove",requireVisible:true);Assert.Equal(1,r.Count());
        ui.Button(["list"],"Add",requireVisible:true);Assert.Equal(2,r.Count());
    }
    [Fact] public void Deep_nested_fields_are_reachable_through_the_same_public_rows()
    {
        JsonNode node=JsonValue.Create("leaf")!;
        for(var n=0;n<24;n++)node=new JsonObject{["branch"]=node};
        var r=new Rig(node.ToJsonString());var row=r.Row("branch");
        for(var n=1;n<24;n++)row=Assert.Single(r.Inspector.Nested(row));
        Assert.True(row.Editable);Assert.True(r.Inspector.Set(row.Id,"changed"));Assert.True(r.Session.Undo());
        using var ui=new Ui(r);ui.Host.Frames(ui.Draw,2);Assert.Contains("leaf",r.Text);
    }
    [Fact] public void Thousand_item_inspector_records_frame_cost_without_inventing_a_speed_claim()
    {
        var values=new JsonArray(Enumerable.Range(0,1000).Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray());
        var r=new Rig(new JsonObject{["list"]=values}.ToJsonString());Assert.Equal(1000,r.Items().Length);
        using var ui=new Ui(r,1280,800);var times=new List<double>();var before=GC.GetTotalAllocatedBytes();
        for(var i=0;i<21;i++){var clock=Stopwatch.StartNew();ui.Host.Frame(ui.Draw);times.Add(clock.Elapsed.TotalMilliseconds);}
        times.Sort();output.WriteLine($"A079 profile: 1000 scalar items, 1280x800, scale 1, CPU-only UI submission, no rasterisation; median={times[10]:F3}ms max={times[^1]:F3}ms allocated={GC.GetTotalAllocatedBytes()-before} bytes over 21 frames; {Environment.OSVersion}, {Environment.ProcessorCount} logical processors.");
        Assert.Equal(1000,r.Count());Assert.Empty(r.Session.Journal);
    }

    [Fact] public void Copied_object_nested_edits_do_not_change_the_original()
    {
        var r=new Rig("""{"list":[{"id":"a","box":{"name":"old"}}]}""");Assert.True(r.Inspector.AddItem(r.Row("list")));
        var box=r.Inspector.Nested(r.Items()[1]).Single(x=>x.Label=="box");var name=Assert.Single(r.Inspector.Nested(box));
        Assert.True(r.Inspector.Set(name.Id,"copy"));Assert.Equal("old",r.At("/unit/list/0/box/name").GetString());Assert.Equal("copy",r.At("/unit/list/1/box/name").GetString());
    }
    [Fact] public void Invalid_schema_default_is_refused_instead_of_bypassing_validation()
    {
        var r=new Rig("""{"list":[]}""",ListSchema("""{"type":"integer","minimum":5,"default":2}"""));
        Refusal(r,()=>r.Inspector.AddItem(r.Row("list")));
    }
    [Fact] public void Long_nested_labels_keep_their_field_and_buttons_reachable_at_twice_size()
    {
        const string label="Mass of this invented copper crate, including its carefully wrapped contents";
        var itemSchema=new JsonObject{["type"]="object",["properties"]=new JsonObject{["mass"]=new JsonObject{["type"]="integer",["title"]=label}}}.ToJsonString();
        var r=new Rig("""{"list":[{"mass":1}]}""",ListSchema(itemSchema));using var ui=new Ui(r,900,800,2);
        ui.Field(["list","\u001f/list/0","\u001f/list/0/mass"],label,"8");ui.Host.Press(ui.Draw,ImGuiKey.Enter);Assert.Equal(8,r.At("/unit/list/0/mass").GetInt32());
        ui.Button(["list","\u001f/list/0"],"Remove",requireVisible:true);Assert.Equal(0,r.Count());
    }
}
