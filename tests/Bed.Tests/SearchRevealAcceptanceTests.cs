using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED: generated neutral documents and event sequences. No DataSearchTests bodies or game records.
// MIXED: acceptance oracles from the public contracts and A094 C20-C22, quoted at each test.
// Only the small headless input idioms are reused from our A093 acceptance harness.
[Collection(ImGuiCollection.Name)]
public sealed class SearchRevealAcceptanceTests
{
    // DataSearch: "every field's name and every plain value, however deep, ignoring case."
    [Fact] public void S01_Recursive_search_finds_a_deep_scalar_without_an_open_inspector()
    {
        var d = Read("""{"section":{"nested":[{"text":"cold"},{"text":"Ember Finch"}]}}""");
        var result = DataSearch.Find(d, "EMBER");
        Assert.Single(result.Places); Assert.Equal("/section/nested/1/text", result.Places[0].Pointer);
        Assert.False(result.More); Assert.Equal("Ember Finch", d.At(result.Places[0].Pointer)!.Value.GetString());
    }

    // C20: "Include strings, numbers, booleans and explicit null".
    [Fact] public void S02_Searches_actual_scalar_types_and_distinguishes_explicit_null()
    {
        var d = Read("""{"bag":{"s":"needle","n":314.25,"yes":true,"no":false,"nil":null}}""");
        foreach (var (query, pointer) in new[] { ("needle","/bag/s"), ("314.25","/bag/n"), ("true","/bag/yes"), ("false","/bag/no"), ("null","/bag/nil") })
            Assert.Equal(pointer, Assert.Single(DataSearch.Find(d, query).Places).Pointer);
    }

    // C21: "JSON Pointer escaping ... slash/tilde/unicode keys."
    [Fact] public void S03_Escaped_slash_tilde_unicode_and_numeric_keys_resolve_to_the_hit()
    {
        var d = Read("""{"section":{"a/b":{"~leaf":"needle"},"雪":{"0":"needle"}}}""");
        var hits = DataSearch.Find(d, "needle").Places;
        Assert.Equal(new[]{"/section/a~1b/~0leaf","/section/雪/0"}, hits.Select(h=>h.Pointer));
        Assert.All(hits, h=>Assert.Equal("needle",d.At(h.Pointer)!.Value.GetString()));
    }

    // DataSearch: "every field's name".
    [Fact] public void S04_Container_field_names_are_hits_without_invented_scalar_values()
    {
        var d = Read("""{"section":{"needleBox":{"child":"plain"}}}""");
        var hit = Assert.Single(DataSearch.Find(d,"needle").Places);
        Assert.Equal("/section/needleBox",hit.Pointer); Assert.Equal(hit.Pointer,hit.Preview);
    }

    // C20: "Do not search imagined default values as if authored."
    [Fact] public void S05_Offered_schema_default_is_not_a_search_hit()
    {
        var schema = ReadNode("""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"absent":{"type":"string","default":"imaginary"}}}}}}""");
        using var r=new Rig(schema:schema); var before=r.Stamp();
        Assert.Equal(0,r.Window.Find("imaginary")); Assert.Empty(r.Window.Found); r.Unchanged(before);
    }

    // DataSearch.Find: "Empty text finds nothing."
    [Fact] public void S06_Empty_and_whitespace_queries_clear_previous_results()
    {
        using var r=new Rig(); Assert.Equal(1,r.Window.Find("destinationNeedle"));
        Assert.Equal(0,r.Window.Find("   \t")); Assert.Empty(r.Window.Found);
        Assert.Empty(DataSearch.Find(Read("""{"x":"needle"}"""),"").Places);
    }

    // Find: "The first ... limit ... and whether there were more."
    [Fact] public void S07_Cap_distinguishes_exactly_one_hundred_from_one_more()
    {
        var exact=DataSearch.Find(Read(new JsonObject{["values"]=Strings(100,"needle")}.ToJsonString()),"needle");
        var extra=DataSearch.Find(Read(new JsonObject{["values"]=Strings(101,"needle")}.ToJsonString()),"needle");
        Assert.Equal(100,exact.Places.Count); Assert.False(exact.More);
        Assert.Equal(100,extra.Places.Count); Assert.True(extra.More);
        Assert.Equal("/values/99",extra.Places[^1].Pointer);
    }

    // Find: "in document order".
    [Fact] public void S08_Hits_keep_authored_order_and_do_not_duplicate_name_plus_value_match()
    {
        var hits=DataSearch.Find(Read("""{"markObject":{"markZ":"mark","markA":"mark"},"tail":"mark"}"""),"mark").Places;
        Assert.Equal(new[]{"/markObject","/markObject/markZ","/markObject/markA","/tail"},hits.Select(h=>h.Pointer));
    }

    // C20: "bounded snippets/results".
    [Fact] public void S09_Long_value_preview_is_bounded_while_search_still_reaches_its_end()
    {
        var value=new string('x',10000)+"needle";
        var hit=Assert.Single(DataSearch.Find(Read(new JsonObject{["section"]=new JsonObject{["text"]=value}}.ToJsonString()),"needle").Places);
        Assert.Equal("/section/text",hit.Pointer); Assert.EndsWith("...",hit.Preview);
        Assert.True(hit.Preview.Length<90); Assert.DoesNotContain("needle",hit.Preview);
    }

    // A096: "unicode"; C20: actual scalar leaves with bounded snippets.
    [Fact] public void S10_Preview_truncation_does_not_split_a_unicode_scalar()
    {
        var value=new string('a',56)+"🪐"+new string('b',100)+"needle";
        var hit=Assert.Single(DataSearch.Find(Read(new JsonObject{["section"]=new JsonObject{["text"]=value}}.ToJsonString()),"needle").Places);
        Assert.Null(Record.Exception(()=>new UTF8Encoding(false,true).GetBytes(hit.Preview)));
    }

    // A096: "a root that's an array".
    [Fact] public void S11_Root_array_search_produces_real_pointers_including_nested_arrays()
    {
        var d=Read("""[{"v":"needle"},["needle"],null]""");
        Assert.Equal(new[]{"/0/v","/1/0"},DataSearch.Find(d,"needle").Places.Select(h=>h.Pointer));
        Assert.Equal("/2",Assert.Single(DataSearch.Find(d,"null").Places).Pointer);
    }

    // A096: "deep nesting at the limit".
    [Fact] public void S12_Search_handles_a_near_parser_limit_without_recursive_stack_walks()
    {
        JsonNode node=JsonValue.Create("needle")!;
        for(var i=0;i<60;i++)node=new JsonObject{["node"]=node};
        var hit=Assert.Single(DataSearch.Find(Read(node.ToJsonString()),"needle").Places);
        Assert.Equal(string.Concat(Enumerable.Repeat("/node",60)),hit.Pointer);
    }

    // Card: "synchronous, on Enter"; EditorWindow.Find: "Changes nothing in it."
    [Fact] public void S13_Real_search_box_waits_for_Enter_and_does_not_edit()
    {
        using var r=new Rig(); var before=r.Stamp();
        r.TypeQuery("destinationNeedle",enter:false); Assert.Empty(r.Window.Found);
        r.Key(ImGuiKey.Enter); r.Frames();
        Assert.Equal("/items/1/title",Assert.Single(r.Window.Found).Pointer); r.Unchanged(before);
    }

    // C22: "On result activation ... select ... scroll the precise control into view".
    [Fact] public void S14_A_real_result_click_selects_the_holder_and_shows_the_exact_field()
    {
        using var r=new Rig(); var before=r.Stamp(); r.TypeQuery("destinationNeedle"); r.ClickFirstResult();
        Assert.Equal("/items/1",r.Editor.Primary); r.AssertVisible("/items/1/title"); r.Unchanged(before);
    }

    // A096: "100 hits drawn every frame".
    [Fact] public void S15_One_hundred_results_draw_and_the_last_can_be_revealed()
    {
        using var r=new Rig(document:Document(new JsonObject{["values"]=Strings(101,"needle")}));
        r.TypeQuery("needle"); Assert.Equal(100,r.Window.Found.Count); r.Frames();
        var last=r.Window.Found[^1].Pointer; Assert.Equal("/items/0/values/99",last);
        Assert.True(r.Window.Reveal(last)); r.Frames(); r.AssertVisible(last);
    }

    // Reveal: "False, saying why in Status, when no row shows that place."
    [Fact] public void S16_A_window_without_an_inspector_refuses_with_a_visible_reason()
    {
        using var r=new Rig(); var window=new EditorWindow<DataDocument>(r.Editor);
        Assert.False(window.Reveal("/items/0/title")); Assert.False(string.IsNullOrWhiteSpace(window.Status));
    }

    // C22: "Missing/deleted/unsupported targets return a visible reason."
    [Fact] public void S17_Root_array_reveal_is_explicitly_unsupported_and_does_not_edit()
    {
        using var r=new Rig(document:ReadNode("""[{"title":"needle"}]""")); var before=r.Stamp();
        Assert.Equal("/0/title",Assert.Single(DataSearch.Find(r.Session.Current,"needle").Places).Pointer);
        Assert.False(r.Window.Reveal("/0/title")); Assert.False(string.IsNullOrWhiteSpace(r.Window.Status)); r.Unchanged(before);
    }

    // C22: "set each list's page from its actual index".
    [Fact] public void S18_Reveal_turns_to_the_last_list_page_and_scrolls_the_target()
    {
        using var r=new Rig(); var before=r.Stamp();
        Assert.True(r.Window.Reveal("/items/0/values/122")); r.Frames(); r.AssertVisible("/items/0/values/122"); r.Unchanged(before);
    }

    // A096: "an object inside a list" and "a long list inside a long object".
    [Fact] public void S19_Reveal_walks_two_list_levels_and_an_object_field()
    {
        var outer=new JsonArray();
        for(var i=0;i<63;i++)outer.Add(new JsonObject{["inner"]=Strings(74,"leaf")});
        using var r=new Rig(document:Document(new JsonObject{["grid"]=outer})); var before=r.Stamp();
        const string target="/items/0/grid/62/inner/73";
        Assert.True(r.Window.Reveal(target)); r.Frames(); r.AssertVisible(target); r.Unchanged(before);
    }

    // C21/C22: escaped pointer identity, never a display label.
    [Fact] public void S20_Reveal_uses_structural_slash_tilde_and_unicode_keys()
    {
        using var r=new Rig(document:Document(ReadNode("""{"a/b":{"~field":{"雪":"value"}}}""")));
        const string target="/items/0/a~1b/~0field/雪";
        Assert.True(r.Window.Reveal(target)); r.Frames(); r.AssertVisible(target);
    }

    // C22: "expose any needed Unset presentation ... scroll the precise control".
    [Fact] public void S21_Late_unset_field_is_revealed_on_its_actual_page_without_creation()
    {
        var (doc,schema)=Wide(offered:123,set:1); using var r=new Rig(document:doc,schema:schema); var before=r.Stamp();
        Assert.True(r.Window.Reveal("/items/0/bundle/p110")); r.Frames(); r.AssertVisible("/items/0/bundle/p110"); r.Unchanged(before);
    }

    // Reveal: "turns to its page"; object fields are not array indices.
    [Fact] public void S22_Long_all_set_object_reveals_a_late_named_field()
    {
        var (doc,schema)=Wide(offered:75,set:75); using var r=new Rig(document:doc,schema:schema);
        Assert.True(r.Window.Reveal("/items/0/bundle/p070")); r.Frames(); r.AssertVisible("/items/0/bundle/p070");
    }

    // A096: "keys that look like numbers"; C22 structurally distinguish objects/lists.
    [Fact] public void S23_Numeric_object_key_uses_its_property_position_not_its_spelling()
    {
        var fields=new JsonObject(); for(var i=0;i<65;i++)fields["p"+i]="v"; fields["0"]="needle";
        using var r=new Rig(document:Document(new JsonObject{["bundle"]=fields}));
        Assert.True(r.Window.Reveal("/items/0/bundle/0")); r.Frames(); r.AssertVisible("/items/0/bundle/0");
    }

    // C22: "Repeated human labels must not alias view state".
    [Fact] public void S24_Equal_schema_titles_do_not_open_or_page_an_unrelated_list()
    {
        var schema=ReadNode("""{"properties":{"items":{"items":{"properties":{"left":{"title":"Shared"},"right":{"title":"Shared"}}}}}}""");
        using var r=new Rig(document:Document(new JsonObject{["left"]=Strings(70,"L"),["right"]=Strings(70,"R")}),schema:schema);
        Assert.True(r.Window.Reveal("/items/0/right/60")); r.Frames(); r.AssertVisible("/items/0/right/60");
        Assert.Null(r.Window.InspectorPlace(r.RowAt("/items/0/left/60").Id));
    }

    // C22: "Lists and objects must remain structurally distinguishable when read-only."
    [Fact] public void S25_Read_only_list_can_be_revealed_without_restoring_edit_controls()
    {
        using var r=new Rig(readOnly:true); var before=r.Stamp();
        Assert.True(r.Window.Reveal("/items/0/values/70")); r.Frames(); r.AssertVisible("/items/0/values/70");
        Assert.False(r.RowAt("/items/0/values/70").Editable); r.Unchanged(before);
    }

    // C22: "Respect focus/read-only boundaries ... rather than bypassing edit locks."
    [Fact] public void S26_Outside_focus_navigation_does_not_unlock_the_target()
    {
        using var r=new Rig(); Assert.True(r.Editor.FocusOn("/items/0")); var before=r.Stamp();
        var ok=r.Window.Reveal("/items/1/title"); r.Frames();
        if(ok){Assert.Equal("/items/1",r.Editor.Primary);Assert.False(r.RowAt("/items/1/title").Editable);Assert.False(r.Inspector.Set("title","forbidden"));}
        else Assert.False(string.IsNullOrWhiteSpace(r.Window.Status));
        Assert.Equal("/items/0",r.Editor.FocusedOn); r.Unchanged(before);
    }

    // C22 view action; editor commands remain refused during play.
    [Fact] public void S27_Reveal_while_playing_never_unlocks_document_edits()
    {
        using var r=new Rig();r.Editor.Add(new PlayMode<DataDocument>("neutral","Neutral",_=>new Play()));
        Assert.True(r.Editor.Play("neutral"));var before=r.Stamp();
        r.Window.Reveal("/items/1/title"); Assert.False(r.Inspector.Set("title","forbidden")); r.Unchanged(before);
        Assert.True(r.Editor.Stop());
    }

    // Found: "Empty once the document has changed".
    [Fact] public void S28_A_successful_edit_invalidates_results_immediately()
    {
        using var r=new Rig();r.Window.Find("destinationNeedle");Assert.Single(r.Window.Found);
        Assert.True(r.Session.Execute(new SetValue("/items/1/title",JsonSerializer.SerializeToElement("new"))));
        Assert.Empty(r.Window.Found);
    }

    // C21: "Reject/rerun after ... undo/redo".
    [Fact] public void S29_Undo_and_redo_never_revive_an_old_index_result()
    {
        using var r=new Rig();r.Window.Find("destinationNeedle");var searched=r.Session.Revision;
        Assert.True(r.Session.Execute(new SetValue("/items/1/title",JsonSerializer.SerializeToElement("different"))));
        Assert.True(r.Session.Undo());Assert.True(r.Session.Revision>searched);Assert.Empty(r.Window.Found);
        Assert.True(r.Session.Redo());Assert.Empty(r.Window.Found);
        Assert.Equal(0,r.Window.Find("destinationNeedle"));
    }

    // Found: source revision, not a failed candidate.
    [Fact] public void S30_A_refused_edit_leaves_results_valid_and_document_untouched()
    {
        using var r=new Rig(schema:TitleSchema(20));r.Window.Find("destinationNeedle");var before=r.Stamp();
        Assert.False(r.Session.Execute(new SetValue("/items/0/title",JsonSerializer.SerializeToElement(new string('x',25)))));
        Assert.Single(r.Window.Found);r.Unchanged(before);
    }

    // C22: "finish or safely refuse the active text edit first".
    [Fact] public void S31_Host_reveal_commits_or_refuses_pending_typing_without_loss()
    {
        using var r=new Rig();r.Replace("/items/0/title","pending",enter:false);
        var ok=r.Window.Reveal("/items/1/title");r.Frames();
        if(ok)Assert.Equal("pending",r.Text("/items/0/title"));
        else{Assert.Equal("/items/0",r.Editor.Primary);Assert.Equal("pending",r.Copy("/items/0/title"));Assert.False(string.IsNullOrWhiteSpace(r.Window.Status));}
        Assert.Equal("destinationNeedle",r.Text("/items/1/title"));
    }

    // Card: "Typing lost without a word ... is always a gap."
    [Fact] public void S32_Real_result_click_does_not_drop_pending_typing_on_another_page()
    {
        using var r=new Rig();r.Session.Execute(new SetValue("/items/0/values/122",JsonSerializer.SerializeToElement("uniqueNeedle")));
        r.TypeQuery("uniqueNeedle");Assert.True(r.Window.Reveal("/items/0/values/0"));r.Frames();
        r.Replace("/items/0/values/0","pending",enter:false);r.ClickFirstResult();r.Frames();
        Assert.Equal("pending",r.Text("/items/0/values/0"));Assert.Equal("uniqueNeedle",r.Text("/items/0/values/122"));
    }

    // C22: "navigation must not discard" uncommitted text; real mouse result activation.
    [Fact] public void S33_Real_result_click_does_not_drop_pending_typing_on_another_holder()
    {
        using var r=new Rig();r.TypeQuery("destinationNeedle");r.Replace("/items/0/title","pending",enter:false);
        r.ClickFirstResult();r.Frames();
        Assert.Equal("pending",r.Text("/items/0/title"));Assert.Equal("destinationNeedle",r.Text("/items/1/title"));
        // Committing first can stale the old result; that safe refusal is allowed by C22.
        if(r.Editor.Primary!="/items/1"){Assert.Equal("/items/0",r.Editor.Primary);Assert.Empty(r.Window.Found);}
    }

    // C22: "Retain refused/uncommitted text with a reason".
    [Fact] public void S34_Reveal_refuses_navigation_that_would_hide_a_refused_edit()
    {
        using var r=new Rig(schema:TitleSchema(20));var typed=new string('z',30);
        r.Replace("/items/0/title",typed,enter:true);Assert.Equal("origin",r.Text("/items/0/title"));
        Assert.False(r.Window.Reveal("/items/1/title"));
        Assert.Equal("/items/0",r.Editor.Primary);Assert.Equal(typed,r.Copy("/items/0/title"));
        Assert.False(string.IsNullOrWhiteSpace(r.Window.Status));
    }

    // C22: "Opening an unset field does not create a parent or default."
    [Fact] public void S35_Early_all_unset_field_reveal_keeps_the_document_clean()
    {
        var (doc,schema)=Wide(offered:64,set:0);using var r=new Rig(document:doc,schema:schema);var before=r.Stamp();
        Assert.True(r.Window.Reveal("/items/0/bundle/p001"));r.Frames();r.AssertVisible("/items/0/bundle/p001");
        Assert.Null(r.Session.Current.At("/items/0/bundle/p001"));r.Unchanged(before);
    }

    // Reveal: "selects what holds it"; Select returning false for unchanged selection is not failure.
    [Fact] public void S36_Already_selected_holder_still_reveals_its_field()
    {
        using var r=new Rig();Assert.Equal("/items/0",r.Editor.Primary);
        Assert.True(r.Window.Reveal("/items/0/title"));r.Frames();r.AssertVisible("/items/0/title");
    }

    // C22: "unsupported targets return a visible reason."
    [Fact] public void S37_An_unmapped_generic_provider_refuses_precisely_without_editing()
    {
        using var r=new Rig(opaque:true);var before=r.Stamp();
        Assert.False(r.Window.Reveal("/items/0/title"));Assert.Contains("row",r.Window.Status,StringComparison.OrdinalIgnoreCase);r.Unchanged(before);
    }

    // Reveal: false with Status "when no row shows that place".
    [Fact] public void S38_Missing_field_reports_failure_instead_of_claiming_success()
    {
        using var r=new Rig();var before=r.Stamp();
        Assert.False(r.Window.Reveal("/items/1/missing"));Assert.Contains("row",r.Window.Status,StringComparison.OrdinalIgnoreCase);r.Unchanged(before);
    }


    // A096: "an empty file" (empty valid JSON containers, not malformed JSON).
    [Fact] public void S39_Empty_document_containers_have_no_hits_and_draw_without_selection()
    {
        foreach(var json in new[]{"{}","[]"})
        {
            using var r=new Rig(document:ReadNode(json));var before=r.Stamp();
            r.TypeQuery("absent");Assert.Empty(r.Window.Found);Assert.Null(r.Editor.Primary);r.Unchanged(before);
        }
    }

    // Find: "The first ... limit ... and whether there were more."
    [Fact] public void S40_Zero_limit_returns_no_hits_and_reports_more_only_when_a_match_exists()
    {
        var d=Read("""{"bag":{"text":"needle"}}""");
        var some=DataSearch.Find(d,"needle",0);Assert.Empty(some.Places);Assert.True(some.More);
        var none=DataSearch.Find(d,"absent",0);Assert.Empty(none.Places);Assert.False(none.More);
    }

    // C20: "No regex ... is needed for the first slice."
    [Fact] public void S41_Query_metacharacters_are_literal_text_not_a_pattern()
    {
        var d=Read("""{"bag":{"literal":"a.*b","other":"axb"}}""");
        Assert.Equal("/bag/literal",Assert.Single(DataSearch.Find(d,".*").Places).Pointer);
    }

    // C22: "Repeated human labels must not alias view state; use structural row/pointer identity."
    [Fact] public void S42_Duplicate_item_names_do_not_share_their_nested_list_page()
    {
        var groups=new JsonArray(new JsonObject{["name"]="Twin",["values"]=Strings(70,"left")},
            new JsonObject{["name"]="Twin",["values"]=Strings(70,"right")});
        using var r=new Rig(document:Document(new JsonObject{["groups"]=groups}));
        Assert.True(r.Window.Reveal("/items/0/groups/1/values/60"));r.Frames();
        r.AssertVisible("/items/0/groups/1/values/60");
        Assert.Null(r.Window.InspectorPlace(r.RowAt("/items/0/groups/0/values/60").Id));
    }

    // C21/C22: escaped JSON Pointer identity, never concatenated decoded field names.
    [Fact] public void S43_Slash_key_and_nested_path_do_not_share_fold_or_page_state()
    {
        using var r=new Rig(document:Document(new JsonObject{["a/b"]=Strings(70,"left"),
            ["a"]=new JsonObject{["b"]=Strings(70,"right")}}));
        Assert.True(r.Window.Reveal("/items/0/a/b/60"));r.Frames();r.AssertVisible("/items/0/a/b/60");
        Assert.Null(r.Window.InspectorPlace(r.RowAt("/items/0/a~1b/60").Id));
    }

    // Card: "a reveal that lands on the wrong thing, is always a gap"; C22 pending-edit transaction.
    [Fact] public void S44_Enter_after_host_reveal_cannot_apply_the_old_text_to_the_new_holder()
    {
        using var r=new Rig();r.Replace("/items/0/title","pending",enter:false);
        Assert.True(r.Window.Reveal("/items/1/title"));r.Frames();
        r.Key(ImGuiKey.Enter);r.Frames();
        Assert.Equal("pending",r.Text("/items/0/title"));
        Assert.Equal("destinationNeedle",r.Text("/items/1/title"));
    }

    private static DataDocument Read(string json)=>new DataFormat().Read(json);
    private static JsonNode ReadNode(string json)=>JsonNode.Parse(json)!;
    private static JsonArray Strings(int count,string prefix)=>new(Enumerable.Range(0,count).Select(i=>(JsonNode?)JsonValue.Create(prefix+i.ToString("000"))).ToArray());
    private static JsonNode Document(JsonNode body)
    {
        var first=new JsonObject{["title"]="origin"};
        foreach(var p in body.AsObject())first[p.Key]=p.Value?.DeepClone();
        return new JsonObject{["items"]=new JsonArray(first,new JsonObject{["title"]="destinationNeedle"})};
    }
    private static JsonNode TitleSchema(int max)=>new JsonObject{["type"]="object",["properties"]=new JsonObject{
        ["items"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object",["properties"]=new JsonObject{
            ["title"]=new JsonObject{["type"]="string",["maxLength"]=max}}}}}};
    private static (JsonNode Document,JsonNode Schema) Wide(int offered,int set)
    {
        var values=new JsonObject();var props=new JsonObject();
        for(var i=0;i<offered;i++){props[$"p{i:000}"]=new JsonObject{["type"]="string"};if(i<set)values[$"p{i:000}"]="value"+i;}
        var schema=new JsonObject{["properties"]=new JsonObject{["items"]=new JsonObject{["items"]=new JsonObject{
            ["properties"]=new JsonObject{["bundle"]=new JsonObject{["type"]="object",["properties"]=props}}}}}};
        return (Document(new JsonObject{["bundle"]=values}),schema);
    }
    private sealed class Play:IPlaySession{public string Status=>"Neutral";public void Draw(){}public void Dispose(){}}
    private sealed class Locked(IObjectType<DataDocument> inner):IObjectType<DataDocument>
    {
        public string Id=>"neutral.locked";public string Label=>"Locked";public long? RowsVersion=>inner.RowsVersion;
        private static IReadOnlyList<InspectorRow> Lock(IReadOnlyList<InspectorRow> rows)=>rows.Select(x=>x with{Editable=false,CanAdd=false,CanRemove=false}).ToList();
        public IReadOnlyList<InspectorRow> Rows(DataDocument d,IReadOnlyList<string> ids)=>Lock(inner.Rows(d,ids));
        public IReadOnlyList<InspectorRow> NestedRows(DataDocument d,IReadOnlyList<string> ids,string id)=>Lock(inner.NestedRows(d,ids,id));
        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d,IReadOnlyList<string> ids,string p,object? value,bool reset)=>null;
    }
    private sealed class Opaque:IObjectType<DataDocument>
    {
        public string Id=>"neutral.opaque";public string Label=>"Opaque";
        public IReadOnlyList<InspectorRow> Rows(DataDocument d,IReadOnlyList<string> ids)=>
            [new("native.title","Title",PropertyKind.Text,"origin",false,false,false,false,[],"")];
        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d,IReadOnlyList<string> ids,string p,object? value,bool reset)=>null;
    }
    private sealed record ViewStamp(string Text,long Revision,int Undo,bool Redo,int Journal,bool Dirty);
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1700,1400){Drawing=false};
        public readonly JsonSchemas Schemas=new();
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly Inspector<DataDocument> Inspector;
        public readonly EditorWindow<DataDocument> Window;
        public Action Draw=>Window.DrawDocked;
        public Rig(JsonNode? document=null,JsonNode? schema=null,bool readOnly=false,bool opaque=false)
        {
            document??=Document(new JsonObject{["values"]=Strings(123,"row"),["bundle"]=new JsonObject{["caption"]="plain"}});
            Schemas.Add("neutral",(schema??new JsonObject()).ToJsonString());
            Session=DataKit.Session(Read(document.ToJsonString()),Schemas,"neutral");
            Editor=new EditorModel<DataDocument>(Session,DataEditing.Outline);
            IObjectType<DataDocument> type=opaque?new Opaque():DataEditing.ObjectType(Schemas,"neutral");
            Inspector=new Inspector<DataDocument>(Editor).Add(readOnly?new Locked(type):type);
            Window=new EditorWindow<DataDocument>(Editor,Inspector){Searcher=(d,text)=>DataSearch.Find(d,text)};
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            if(Editor.Outline.ObjectIds(Session.Current).Contains("/items/0"))Editor.Select("/items/0");
            Host.Frames(Draw,5);Focus();
        }
        public void Dispose(){Editor.Stop();Host.Dispose();}
        public void Frames()=>Host.Frames(Draw,4);
        private void Focus(){Host.Frame(()=>{ImGui.SetWindowFocus("Inspector###inspector"u8);Draw();});Frames();}
        public ViewStamp Stamp()=>new(Session.Current.Root.GetRawText(),Session.Revision,Session.History.UndoCount,Session.History.CanRedo,Session.Journal.Count,Session.Dirty);
        public void Unchanged(ViewStamp before)=>Assert.Equal(before,Stamp());
        public string? Text(string pointer)=>Session.Current.At(pointer)!.Value.GetString();
        public void Key(ImGuiKey key,bool ctrl=false)=>Host.Press(Draw,key,ctrl:ctrl);
        public InspectorRow RowAt(string absolute)
        {
            var holder=Editor.Primary??throw new Exception("No selection");
            Assert.True(absolute.StartsWith(holder+"/",StringComparison.Ordinal),$"Target {absolute} outside {holder}");
            var parts=Pointer.Split(absolute[holder.Length..]);var rows=Inspector.Rows();var at="";InspectorRow? row=null;
            foreach(var part in parts)
            {
                at+=Pointer.Join([part]);row=rows.SingleOrDefault(x=>DataEditing.PlaceOfRow(x.Id)==at);
                Assert.True(row is not null,$"No structural provider row for {absolute} at {at}");
                rows=Inspector.Nested(row!);
            }
            return row!;
        }
        public void AssertVisible(string absolute)
        {
            var row=RowAt(absolute);var at=Window.InspectorPlace(row.Id);
            Assert.True(at is not null,$"Reveal reported success but did not draw {absolute}");
            Assert.InRange(at!.Value.Y,25,Host.Height-35);
        }
        private float2 Visible(string absolute)
        {
            Focus();var row=RowAt(absolute);var at=Window.InspectorPlace(row.Id);
            Assert.True(at is not null,$"Input control missing: {absolute}");
            for(var i=0;i<130&&(at!.Value.Y<30||at.Value.Y>Host.Height-40);i++)
            {
                Host.MoveMouse(Draw,at.Value.X+8,300);ImGui.GetIO().AddMouseWheelEvent(0,at.Value.Y>Host.Height-40?-5:5);Frames();
                at=Window.InspectorPlace(row.Id);Assert.NotNull(at);
            }
            Assert.InRange(at!.Value.Y,30,Host.Height-40);return at.Value;
        }
        public void Replace(string absolute,string typed,bool enter)
        {
            var at=Visible(absolute);Host.Click(Draw,at.X+6,at.Y+7);
            Assert.True(ImGui.GetIO().WantTextInput,$"Expected a real text control at {absolute}");
            Key(ImGuiKey.A,true);Key(ImGuiKey.Backspace);Host.Type(Draw,typed);
            if(enter){Key(ImGuiKey.Enter);Frames();}
        }
        public string Copy(string absolute)
        {
            var at=Visible(absolute);Host.Click(Draw,at.X+6,at.Y+7);Key(ImGuiKey.A,true);Key(ImGuiKey.C,true);
            return ImGui.GetClipboardText().ToString()??"";
        }
        public void TypeQuery(string query,bool enter=true)
        {
            Focus();Host.MoveMouse(Draw,1550,200);ImGui.GetIO().AddMouseWheelEvent(0,100);Frames();
            var at=Window.InspectorPlace("",InspectorPart.Find);Assert.NotNull(at);
            Host.Click(Draw,at.Value.X+8,at.Value.Y+7);Key(ImGuiKey.A,true);Key(ImGuiKey.Backspace);Host.Type(Draw,query);
            if(enter){Key(ImGuiKey.Enter);Frames();}
        }
        public void ClickFirstResult()
        {
            var at=Window.InspectorPlace("",InspectorPart.Find);Assert.NotNull(at);
            var y=at.Value.Y+ImGui.GetFrameHeight()+ImGui.GetStyle().ItemSpacing.Y+ImGui.GetTextLineHeight()/2;
            Assert.InRange(y,20,Host.Height-35);
            Host.Click(Draw,at.Value.X+10,y);Frames();
        }
    }
}
