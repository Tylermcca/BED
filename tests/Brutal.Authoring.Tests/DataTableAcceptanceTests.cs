using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

// INVENTED fixtures and assertions derived independently from public doc comments and A029 P0.1-P0.3.
// No DataTableTests or implementation method bodies were used as an oracle.
[Collection(ImGuiCollection.Name)]
public sealed class DataTableAcceptanceTests
{
    const string Schema = """
    {"type":"object","properties":{
      "records":{"type":"array","items":{"type":"object","properties":{
        "details":{"type":"object"},
        "name":{"type":"string","title":"Display name","description":"Readable identity."},
        "cost":{"type":"number","title":"Cost units","minimum":0,"default":1.5},
        "tags":{"type":"array","items":{"type":"string"}},
        "count":{"type":"integer","minimum":0},
        "enabled":{"type":"boolean"},
        "tier":{"type":"string","enum":["plain","rare"]},
        "note":{"type":"string"}
      }}},
      "other":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"cost":{"type":"number"}}}}
    }}
    """;
    const string Text = """
    {
      "records": [
        {"name":"Amber Kite","cost":10.0,"count":3,"enabled":true,"tier":"plain","note":"cedar","details":{"x":1},"tags":["north"]},
        {"name":"Blue Lamp","cost":2,"count":1,"enabled":false,"tier":"rare","note":"MARBLE","details":{},"tags":[]},
        {"name":"Copper Bell","cost":2.0,"count":2,"enabled":true,"tier":"plain","note":"cedar","details":{},"tags":[]}
      ],
      "other": [{"name":"West","cost":9},{"name":"East","cost":1}]
    }
    """;

    sealed class Rig
    {
        public readonly DataFormat Format = new();
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly IObjectType<DataDocument> Type;
        public readonly DataTableView Table;
        public Rig(string text = Text, string schema = Schema)
        {
            var schemas = new JsonSchemas().Add("acceptance", schema);
            Session = DataKit.Session(Format.Read(text), schemas, "acceptance");
            Editor = new(Session, DataEditing.OutlineFor(schemas, "acceptance"));
            Type = DataEditing.ObjectType(schemas, "acceptance");
            Table = new(Editor, Type);
        }
    }
    static string P(int index) => $"/records/{index}";
    static void Order(Rig r, params int[] indices) => Assert.Equal(indices.Select(P), r.Table.Visible());
    static void Untouched(Rig r, DataDocument before, string written)
    {
        Assert.Same(before, r.Session.Current);
        Assert.Equal(written, r.Format.Write(r.Session.Current));
        Assert.Equal(0, r.Session.Revision);
        Assert.Empty(r.Session.Journal);
        Assert.Equal(0, r.Session.History.UndoCount);
        Assert.False(r.Session.Dirty);
    }
    static void Refused(Rig r, string item, string field, object? value, string word)
    {
        var before = r.Session.Current;
        var text = r.Format.Write(before);
        Assert.False(r.Table.SetCell(item, field, value));
        Assert.Contains(word, r.Table.Said, StringComparison.OrdinalIgnoreCase);
        Untouched(r, before, text);
    }
    static void Changed(Rig r, string field, object value, Action<JsonElement> check)
    {
        var before = r.Session.Current;
        var written = r.Format.Write(before);
        Assert.True(r.Table.SetCell(P(0), field, value), r.Table.Said);
        check(r.Session.Current.At(P(0) + "/" + field)!.Value);
        Assert.Single(r.Session.Journal);
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.Equal(1, r.Session.Revision);
        Assert.False(string.IsNullOrWhiteSpace(r.Table.Said));
        Assert.True(r.Session.Undo());
        Assert.Equal(written, r.Format.Write(r.Session.Current));
        Assert.False(r.Session.History.CanUndo);
        Assert.True(r.Session.Redo());
        check(r.Session.Current.At(P(0) + "/" + field)!.Value);
    }
    static EditorWindow<DataDocument> Window(Rig r)
    {
        var inspector = new Inspector<DataDocument>(r.Editor).Add(r.Type);
        r.Editor.Add(r.Table.AsPanel());
        return new(r.Editor, inspector);
    }

    // DataTableView.Lists, Shown, Items: top-level object lists and document-order pointers.
    [Fact] public void ListsOfferBothTopLevelObjectCollections() { var r = new Rig(); Assert.Equal(new[]{"/records","/other"}, r.Table.Lists()); }
    [Fact] public void NullListShowsFirstObjectCollection() { var r = new Rig(); Assert.Null(r.Table.List); Assert.Equal("/records", r.Table.Shown); }
    [Fact] public void ExplicitListShowsItsOwnPointers() { var r = new Rig(); r.Table.List="/other"; Assert.Equal(new[]{"/other/0","/other/1"},r.Table.Items()); }
    [Fact] public void ItemsAlwaysKeepDocumentOrder() { var r = new Rig(); r.Table.Sort=("cost",false); Assert.Equal(new[]{P(0),P(1),P(2)},r.Table.Items()); }
    [Fact] public void ScalarsAndNestedListsAreNotTopLevelObjectLists()
    {
        var r=new Rig("""{"records":[{"name":"a","tags":[{}]}],"numbers":[1,2],"setting":{},"label":"x"}""", "{}");
        Assert.Equal(new[]{"/records"},r.Table.Lists());
    }
    [Fact] public void NoListsProducesNoRowsOrColumns() { var r=new Rig("{}","{}"); Assert.Empty(r.Table.Lists()); Assert.Null(r.Table.Shown); Assert.Empty(r.Table.Items()); Assert.Empty(r.Table.Columns()); Assert.Empty(r.Table.Visible()); }

    // Columns: editable first, schema order within each group; labels/kinds/help/choices from the inspector contract.
    [Fact] public void EditableColumnsPrecedeSummariesInSchemaOrder()
    {
        var r=new Rig(); Assert.Equal(new[]{"name","cost","count","enabled","tier","note","details","tags"},r.Table.Columns().Select(x=>x.Id));
    }
    [Fact] public void SchemaTitlesBecomeColumnLabels() { var r=new Rig(); Assert.Equal("Cost units",r.Table.Columns().Single(x=>x.Id=="cost").Label); }
    [Fact] public void KeysLabelUntitledColumns() { var r=new Rig(); Assert.Equal("count",r.Table.Columns().Single(x=>x.Id=="count").Label); }
    [Fact] public void SchemaDescriptionBecomesCellHelp() { var r=new Rig(); Assert.Equal("Readable identity.",r.Table.Cell(P(0),"name")!.Help); }
    [Fact] public void EnumChoicesAreKeptInSchemaOrder() { var r=new Rig(); Assert.Equal(new[]{"plain","rare"},r.Table.Cell(P(0),"tier")!.Choices); }
    [Fact] public void ScalarCellKindsMatchTheirSchemas()
    {
        var r=new Rig(); Assert.Equal(new[]{PropertyKind.Text,PropertyKind.Number,PropertyKind.Integer,PropertyKind.Toggle,PropertyKind.Choice},new[]{"name","cost","count","enabled","tier"}.Select(x=>r.Table.Cell(P(0),x)!.Kind));
    }
    [Fact] public void ObjectsAndListsAreReadOnlySummaryCells() { var r=new Rig(); Assert.False(r.Table.Cell(P(0),"details")!.Editable); Assert.False(r.Table.Cell(P(0),"tags")!.Editable); }
    [Fact] public void HeterogeneousRowsContributeAllColumns()
    {
        var r=Shapes(); Assert.Equal(new[]{"name","score","active"},r.Table.Columns().Select(x=>x.Id)); Assert.Null(r.Table.Cell(P(0),"active")); Assert.Null(r.Table.Cell(P(1),"score"));
    }
    static Rig Shapes() => new("""{"records":[{"name":"a","score":1.25},{"name":"b","active":true}]}""", """{"type":"object","properties":{"records":{"type":"array","prefixItems":[{"type":"object","properties":{"name":{"type":"string"},"score":{"type":"number","title":"Measured score"}}},{"type":"object","properties":{"name":{"type":"string"},"active":{"type":"boolean","title":"Active flag"}}}]}}}""");
    [Fact] public void HeterogeneousCellsResolveTheirActualItemSchemas()
    {
        var r=Shapes(); var score=r.Table.Cell(P(0),"score")!; var active=r.Table.Cell(P(1),"active")!;
        Assert.Equal(PropertyKind.Number,score.Kind); Assert.Equal("Measured score",score.Label); Assert.Equal(PropertyKind.Toggle,active.Kind); Assert.Equal("Active flag",active.Label);
        Assert.True(r.Table.SetCell(P(1),"active",false),r.Table.Said); Assert.False(r.Session.Current.At(P(1)+"/active")!.Value.GetBoolean());
    }
    [Fact] public void OneFieldCanHaveDifferentKindsAtDifferentItemPointers()
    {
        var r=new Rig("""{"records":[{"value":12},{"value":"twelve"}]}""", """{"properties":{"records":{"prefixItems":[{"properties":{"value":{"type":"integer"}}},{"properties":{"value":{"type":"string"}}}]}}}""");
        Assert.Equal(PropertyKind.Integer,r.Table.Cell(P(0),"value")!.Kind); Assert.Equal(PropertyKind.Text,r.Table.Cell(P(1),"value")!.Kind);
    }
    [Fact] public void MissingNullAndEmptyRemainDistinctInTheDocumentWhenViewed()
    {
        var r=new Rig("""{"records":[{}, {"value":null}, {"value":""}]}""", """{"properties":{"records":{"items":{"properties":{"value":{"type":["string","null"]}}}}}}""");
        var before=r.Session.Current; var written=r.Format.Write(before); _=r.Table.Columns(); foreach(var p in r.Table.Items()) _=r.Table.Cell(p,"value");
        Assert.Null(r.Session.Current.At(P(0)+"/value")); Assert.Equal(JsonValueKind.Null,r.Session.Current.At(P(1)+"/value")!.Value.ValueKind); Assert.Equal("",r.Session.Current.At(P(2)+"/value")!.Value.GetString()); Untouched(r,before,written);
    }

    // Filter's public contract: name OR any shown value, case insensitive, view state only.
    [Fact] public void FilterFindsPartOfName() { var r=new Rig(); r.Table.Filter="Kite"; Order(r,0); }
    [Fact] public void FilterFindsTextValue() { var r=new Rig(); r.Table.Filter="marble"; Order(r,1); }
    [Fact] public void FilterIgnoresNameCase() { var r=new Rig(); r.Table.Filter="cOPPER"; Order(r,2); }
    [Fact] public void EmptyFilterShowsEveryItem() { var r=new Rig(); r.Table.Filter="missing"; Assert.Empty(r.Table.Visible()); r.Table.Filter=""; Order(r,0,1,2); }
    [Fact] public void FilterFindsNumericShownValues() { var r=new Rig(); r.Table.Filter="10"; Order(r,0); }
    [Fact] public void FilterFindsChoiceValues() { var r=new Rig(); r.Table.Filter="RARE"; Order(r,1); }
    [Fact] public void FilterFindsAllMatchingRowsInFileOrder() { var r=new Rig(); r.Table.Filter="cedar"; Order(r,0,2); }
    [Fact] public void FilterNeverChangesSourceTextHistoryOrJournal() { var r=new Rig(); var before=r.Session.Current; var text=r.Format.Write(before); r.Table.Filter="blue"; _=r.Table.Visible(); r.Table.Filter=""; _=r.Table.Visible(); Untouched(r,before,text); }

    // Sort: numbers must compare numerically, strings as text; absent values last either direction.
    [Fact] public void NumericAscendingSortDoesNotUseTextOrder() { var r=new Rig(); r.Table.Sort=("cost",false); Order(r,1,2,0); }
    [Fact] public void NumericDescendingSortKeepsStableTies() { var r=new Rig(); r.Table.Sort=("cost",true); Order(r,0,1,2); }
    [Fact] public void TextAscendingSortUsesTextRatherThanNumberOrder()
    {
        var r=new Rig("""{"records":[{"name":"2"},{"name":"10"},{"name":"1"}]}"""); r.Table.Sort=("name",false); Order(r,2,1,0);
    }
    [Fact] public void TextDescendingSortUsesTextOrder()
    {
        var r=new Rig("""{"records":[{"name":"2"},{"name":"10"},{"name":"1"}]}"""); r.Table.Sort=("name",true); Order(r,0,1,2);
    }
    [Fact] public void MissingNumericValuesSortLastAscending()
    {
        var r=new Rig("""{"records":[{"name":"missing"},{"name":"ten","cost":10},{"name":"two","cost":2}]}"""); r.Table.Sort=("cost",false); Order(r,2,1,0);
    }
    [Fact] public void MissingNumericValuesSortLastDescending()
    {
        var r=new Rig("""{"records":[{"name":"missing"},{"name":"ten","cost":10},{"name":"two","cost":2}]}"""); r.Table.Sort=("cost",true); Order(r,1,2,0);
    }
    [Fact] public void StableTextTiesKeepFileOrder() { var r=new Rig(); r.Table.Sort=("note",false); var visible=r.Table.Visible().ToList(); Assert.Equal(3,visible.Count); Assert.Contains(P(0),visible); Assert.Contains(P(2),visible); Assert.True(visible.IndexOf(P(0))<visible.IndexOf(P(2))); }
    [Fact] public void ClearingSortRestoresFileOrder() { var r=new Rig(); r.Table.Sort=("cost",false); _=r.Table.Visible(); r.Table.Sort=null; Order(r,0,1,2); }
    [Fact] public void FilterAndSortComposeOnUnderlyingItemPointers() { var r=new Rig(); r.Table.Filter="cedar"; r.Table.Sort=("cost",false); Order(r,2,0); }
    [Fact] public void SortNeverChangesSourceTextHistoryOrJournal()
    {
        var r=new Rig(); var before=r.Session.Current; var text=r.Format.Write(before); r.Table.Sort=("cost",false); _=r.Table.Visible(); r.Table.Sort=("name",true); _=r.Table.Visible(); Assert.Equal(new[]{P(0),P(1),P(2)},r.Table.Items()); Untouched(r,before,text);
    }

    // P0.2: every cell kind commits through validation with one undo, never a display-index edit.
    [Fact] public void TextCellEditIsOneUndoableCommand() { var r=new Rig(); Changed(r,"name","New label",x=>Assert.Equal("New label",x.GetString())); }
    [Fact] public void NumberCellEditIsOneUndoableCommand() { var r=new Rig(); Changed(r,"cost",3.125,x=>Assert.Equal(3.125,x.GetDouble())); }
    [Fact] public void IntegerCellEditIsOneUndoableCommand() { var r=new Rig(); Changed(r,"count",7,x=>Assert.Equal(7,x.GetInt32())); }
    [Fact] public void ToggleCellEditIsOneUndoableCommand() { var r=new Rig(); Changed(r,"enabled",false,x=>Assert.False(x.GetBoolean())); }
    [Fact] public void ChoiceCellEditIsOneUndoableCommand() { var r=new Rig(); Changed(r,"tier","rare",x=>Assert.Equal("rare",x.GetString())); }
    [Fact] public void EditAfterSortingTargetsTheUnderlyingItem()
    {
        var r=new Rig(); r.Table.Sort=("cost",false); var item=r.Table.Visible()[0]; Assert.Equal(P(1),item); Assert.True(r.Table.SetCell(item,"cost",6.5),r.Table.Said); Assert.Equal(10,r.Session.Current.At(P(0)+"/cost")!.Value.GetDouble()); Assert.Equal(6.5,r.Session.Current.At(P(1)+"/cost")!.Value.GetDouble());
    }
    [Fact] public void SchemaMinimumRefusalHasAReasonAndLeavesEverythingUnchanged() { var r=new Rig(); Refused(r,P(0),"cost",-1,"0"); }
    [Fact] public void InvalidChoiceRefusalHasAReasonAndLeavesEverythingUnchanged() { var r=new Rig(); Refused(r,P(0),"tier","mythic","plain"); }
    [Fact] public void FocusRefusalHasAReasonAndLeavesEverythingUnchanged() { var r=new Rig(); Assert.True(r.Editor.FocusOn("/other")); Refused(r,P(0),"cost",5,"focus"); }
    [Fact] public void PlayRefusalHasAReasonAndLeavesEverythingUnchanged()
    {
        var r=new Rig(); r.Editor.Add(new PlayMode<DataDocument>("acceptance.play","Play",_=>new QuietGame())); Assert.True(r.Editor.Play("acceptance.play")); try { Refused(r,P(0),"cost",5,"play"); } finally { Assert.True(r.Editor.Stop()); }
    }
    sealed class QuietGame : IPlaySession { public string Status=>"running"; public void Draw(){} public void Dispose(){} }
    [Fact] public void ObjectSummaryCannotBeEditedAsACell() { var r=new Rig(); Refused(r,P(0),"details","replacement","edit"); }
    [Fact] public void ListSummaryCannotBeEditedAsACell() { var r=new Rig(); Refused(r,P(0),"tags","replacement","edit"); }
    [Fact] public void UnknownFieldCannotBeEditedAsACell() { var r=new Rig(); Assert.Null(r.Table.Cell(P(0),"unknown")); Assert.DoesNotContain(r.Table.Columns(),c=>c.Id=="unknown"); Refused(r,P(0),"unknown","replacement","edit"); }

    // Real ImGui context, no native window or game: drawing is view state, even with a selection.
    [Fact] public void DockedTableDrawsASelectedRowWithoutEditing()
    {
        var r=new Rig(); var before=r.Session.Current; var text=r.Format.Write(before); Assert.True(r.Editor.Select(P(1))); using var host=new HeadlessHost(); EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO()); var window=Window(r); host.Frames(window.DrawDocked,4); Assert.True(host.Covered()>1000); Assert.Equal(P(1),r.Editor.Primary); Assert.True(r.Editor.IsOpen("editor.table")); Untouched(r,before,text);
    }
    [Fact] public void SwitchingListsResetsSortWhenTheTableIsDrawn()
    {
        var r=new Rig(); using var host=new HeadlessHost(); EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO()); var window=Window(r); host.Frames(window.DrawDocked); r.Table.Sort=("cost",true); host.Frames(window.DrawDocked); r.Table.List="/other"; host.Frames(window.DrawDocked); Assert.Null(r.Table.Sort); Assert.Equal(new[]{"/other/0","/other/1"},r.Table.Visible()); Assert.Empty(r.Session.Journal);
    }
    [Fact] public void MissingTextValuesSortLastAscending()
    {
        var r=new Rig("""{"records":[{"name":"missing"},{"name":"z","note":"z"},{"name":"a","note":"a"}]}"""); r.Table.Sort=("note",false); Order(r,2,1,0);
    }
    [Fact] public void MissingTextValuesSortLastDescending()
    {
        var r=new Rig("""{"records":[{"name":"missing"},{"name":"z","note":"z"},{"name":"a","note":"a"}]}"""); r.Table.Sort=("note",true); Order(r,1,2,0);
    }
    // P0.1 says absent values must look different from null and empty. Compare glyphs rather than row backgrounds.
    [Fact]
    public void MissingNullAndEmptyHaveDistinctCellDisplays()
    {
        var r=new Rig("""{"records":[{"name":"Missing"},{"name":"Null","value":null},{"name":"Empty","value":""}]}""", """{"properties":{"records":{"items":{"properties":{"name":{"type":"string"},"value":{"type":["string","null"]}}}}}}""");
        using var host=new HeadlessHost(1000,650); EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(()=>DrawFixedTable(r),4);
        // Coordinates observed in this fixed headless layout, never obtained from source or private ImGui state.
        var missing=Glyphs(host.Pixels,host.Width,351,94,510,111);
        var jsonNull=Glyphs(host.Pixels,host.Width,351,117,510,134);
        var empty=Glyphs(host.Pixels,host.Width,351,140,510,157);
        Assert.True(missing.Count(x=>x)>20,"The missing marker must actually have visible glyphs.");
        Assert.False(missing.SequenceEqual(jsonNull),"Absent and JSON-null cells must have different visible glyphs.");
        Assert.False(missing.SequenceEqual(empty),"Absent and empty-string cells must have different visible glyphs.");
        Assert.False(jsonNull.SequenceEqual(empty),"JSON-null and empty-string cells must have different visible glyphs.");
    }
    static void DrawFixedTable(Rig r)
    {
        Brutal.ImGuiApi.ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0,0),ImGuiCond.Always);
        Brutal.ImGuiApi.ImGui.SetNextWindowSize(new Brutal.Numerics.float2(1000,650),ImGuiCond.Always);
        Brutal.ImGuiApi.ImGui.Begin("Acceptance table"); r.Table.Draw(); Brutal.ImGuiApi.ImGui.End();
    }
    static uint[] Crop(uint[] pixels,int width,int x0,int y0,int x1,int y1) =>
        Enumerable.Range(y0,y1-y0).SelectMany(y=>pixels.Skip(y*width+x0).Take(x1-x0)).ToArray();
    static bool[] Glyphs(uint[] pixels,int width,int x0,int y0,int x1,int y1) =>
        Crop(pixels,width,x0,y0,x1,y1).Select(p=>(p&255)>=100 && ((p>>8)&255)>=100 && ((p>>16)&255)>=100).ToArray();
    static (uint[] Before,uint[] Horizontal,uint[] Vertical) ScrollFrames()
    {
        var props=new Dictionary<string,object> {{"name",new {type="string"}}};
        for(var i=0;i<20;i++) props[$"field{i:00}"]=new {type="string",title=$"Header{i:00}"};
        var schema=JsonSerializer.Serialize(new {type="object",properties=new {records=new {type="array",items=new {type="object",properties=props}}}});
        var rows=Enumerable.Range(0,60).Select(row=> { var d=new Dictionary<string,object>{{"name",$"Identity {row:00}"}}; for(var col=0;col<20;col++) d[$"field{col:00}"]=$"Content_{row:00}_{col:00}"; return d; }).ToArray();
        var r=new Rig(JsonSerializer.Serialize(new {records=rows}),schema); var before=r.Session.Current; var text=r.Format.Write(before);
        using var host=new HeadlessHost(1000,650); EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        void Draw()=>DrawFixedTable(r);
        host.Frames(Draw,4); host.MoveMouse(Draw,500,300); var a=host.Pixels.ToArray();
        Brutal.ImGuiApi.ImGui.GetIO().AddMouseWheelEvent(-8,0); host.Frames(Draw,4); var b=host.Pixels.ToArray();
        Brutal.ImGuiApi.ImGui.GetIO().AddMouseWheelEvent(0,-8); host.Frames(Draw,4); var c=host.Pixels.ToArray();
        Untouched(r,before,text); return (a,b,c);
    }
    [Fact] public void FrozenIdentityColumnRetainsItsGlyphsWhileDataColumnsScrollHorizontally()
    {
        var (before,horizontal,_)=ScrollFrames();
        var identity=Crop(before,1000,12,95,165,610);
        Assert.True(Glyphs(before,1000,12,95,165,610).Count(x=>x)>100,"Identity labels must be visible before comparison.");
        Assert.Equal(identity,Crop(horizontal,1000,12,95,165,610));
        Assert.False(Crop(before,1000,350,95,950,610).SequenceEqual(Crop(horizontal,1000,350,95,950,610)),"Data columns must really move.");
    }
    [Fact] public void FrozenHeaderRetainsItsGlyphsWhileBodyRowsScrollVertically()
    {
        var (_,horizontal,vertical)=ScrollFrames();
        Assert.True(Glyphs(horizontal,1000,12,75,975,88).Count(x=>x)>50,"Header labels must be visible before comparison.");
        Assert.Equal(Crop(horizontal,1000,12,75,975,88),Crop(vertical,1000,12,75,975,88));
        Assert.False(Crop(horizontal,1000,12,95,165,610).SequenceEqual(Crop(vertical,1000,12,95,165,610)),"Body row identities must really move.");
    }
}