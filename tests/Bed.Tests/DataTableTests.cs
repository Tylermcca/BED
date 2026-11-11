using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>The data table view (docs/research/table_editing.md, first slice P0.1–P0.3): columns from the schema,
/// filter and sort as view state, cell edits as the document's own commands.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class DataTableTests
{
    private const string Schema = """
        {"type":"object","properties":{
           "wares":{"type":"array","items":{"type":"object","required":["id"],"additionalProperties":false,
             "properties":{"id":{"type":"string"},"name":{"type":"string","title":"Name"},"price":{"type":"number","minimum":0},
                           "size":{"enum":["S","M","L"]},"legal":{"type":"boolean"},"tags":{"type":"array"}}}},
           "crew":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"wage":{"type":"integer"}}}},
           "notes":{"type":"string"}}}
        """;

    private const string Text = """
        {
          "wares": [
            { "id": "rope", "name": "Rope", "price": 2, "size": "S", "legal": true, "tags": [] },
            { "id": "ore", "name": "Iron ore", "price": 12, "size": "L" },
            { "id": "salt", "price": 5 },
            { "id": "gems", "name": "Gems", "size": "S" }
          ],
          "crew": [ { "id": "ana", "wage": 40 } ],
          "notes": "x"
        }

        """;

    private static readonly JsonSchemas Schemas = new JsonSchemas().Add("t.schema.json", Schema);

    private static (EditorModel<DataDocument> Editor, DataTableView Table) Open(string text = Text)
    {
        var session = DataKit.Session(new DataFormat().Read(text), Schemas, "t.schema.json");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (editor, new DataTableView(editor, DataEditing.ObjectType(Schemas, "t.schema.json")));
    }

    [Fact]
    public void TheFirstListIsShownWithItsSchemasFieldsAsColumns()
    {
        var (_, table) = Open();
        Assert.Equal(["/wares", "/crew"], table.Lists());
        Assert.Equal("/wares", table.Shown);
        Assert.Equal(["/wares/0", "/wares/1", "/wares/2", "/wares/3"], table.Items());
        Assert.Equal(["id", "name", "price", "size", "legal", "tags"], table.Columns().Select(c => c.Id));   // editable first; tags (a list) last
        Assert.Equal("Name", table.Columns().Single(c => c.Id == "name").Label);   // the schema's title
        table.List = "/crew";
        Assert.Equal(["id", "wage"], table.Columns().Select(c => c.Id));
    }

    [Fact]
    public void TheFilterMatchesNamesAndValuesAndIsOnlyAView()
    {
        var (editor, table) = Open();
        table.Filter = "ore";
        Assert.Equal(["/wares/1"], table.Visible());   // "Iron ore", by its name
        table.Filter = "S";
        Assert.Equal(["/wares/0", "/wares/2", "/wares/3"], table.Visible());   // values S, "salt", "Gems"... any field containing s
        Assert.False(editor.Session.Dirty);
    }

    [Fact]
    public void SortingOrdersTheViewWithMissingValuesLastAndNeverMovesItems()
    {
        var (editor, table) = Open();
        var before = editor.Session.Format.Write(editor.Session.Current);
        table.Sort = ("price", false);
        Assert.Equal(["/wares/0", "/wares/2", "/wares/1", "/wares/3"], table.Visible());   // 2, 5, 12, then gems (no price)
        table.Sort = ("price", true);
        Assert.Equal(["/wares/1", "/wares/2", "/wares/0", "/wares/3"], table.Visible());   // missing stays last
        table.Sort = ("name", false);
        Assert.Equal(["/wares/3", "/wares/1", "/wares/0", "/wares/2"], table.Visible());   // Gems, Iron ore, Rope, (salt: no name)
        Assert.Equal(before, editor.Session.Format.Write(editor.Session.Current));        // the file's order is untouched
        Assert.Empty(editor.Session.Journal);
    }

    [Fact]
    public void ACellEditIsTheDocumentsCommandValidatedAndUndoable()
    {
        var (editor, table) = Open();
        Assert.True(table.SetCell("/wares/1", "price", 15.0));
        Assert.Equal(15, editor.Session.Current.At("/wares/1/price")!.Value.GetDouble());
        Assert.True(table.SetCell("/wares/1", "size", "M"));
        Assert.False(table.SetCell("/wares/1", "price", -3.0));
        Assert.Equal("Iron ore, price: must be at least 0", table.Said);   // the row and column, then the rule (U18)
        Assert.False(table.SetCell("/wares/0", "tags", "x"));   // a list: edited in the inspector
        Assert.Contains("use the inspector", table.Said);
        Assert.True(editor.Session.Undo());
        Assert.Equal("L", editor.Session.Current.At("/wares/1/size")!.Value.GetString());
    }

    [Fact]
    public void FocusAndPlayLocksApplyToTheTable()
    {
        var (editor, table) = Open();
        Assert.True(editor.FocusOn("/crew"));
        Assert.False(table.SetCell("/wares/0", "price", 3.0));
        Assert.Contains("outside the place you're focused on", table.Said);
    }

    [Fact]
    public void TheTableDrawsHeadlessDockedInTheMiddle()
    {
        var (editor, table) = Open();
        editor.Add(table.AsPanel());
        var window = new EditorWindow<DataDocument>(editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(Schemas, "t.schema.json")));
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        table.Sort = ("price", false);
        host.Frames(window.DrawDocked, 4);
        Assert.True(host.Covered(300, 60, 1100, 300) > 2000, "the table fills the middle");
        Assert.Equal(("price", false), table.Sort);   // the table's first frame didn't replace the sort set before it
        if (Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS") is { Length: > 0 } d)
            host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "30_data_table.png"));
    }

    // ---- the keyboard (A029 P0.7; A037 drives the keys themselves) ----

    [Fact]
    public void CopyGivesTheAnchorCellAsPasteReadsIt()
    {
        var (_, table) = Open();
        using var host = new HeadlessHost(400, 300);
        Assert.Null(table.Copy());   // nothing picked
        table.Anchor = ("/wares/1", "name");
        Assert.Equal("Iron ore", table.Copy());
        table.Anchor = ("/wares/3", "price");
        Assert.Equal("", table.Copy());   // not set copies as empty
        table.Anchor = ("/wares/0", "legal");
        Assert.Equal("true", table.Copy());
        Assert.Equal("true", Brutal.ImGuiApi.ImGui.GetClipboardText().ToString());
        Assert.Equal("Copied 1 cell.", table.Said);
    }

    [Fact]
    public void TheTablesActionsAreInThePaletteAndSayWhyWhenTheyCantRun()
    {
        var (editor, table) = Open();
        editor.Add(table.AsPanel() with { OpenAtStart = false });
        var window = new EditorWindow<DataDocument>(editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(Schemas, "t.schema.json")));
        using var host = new HeadlessHost(1400, 800);
        var copy = Assert.Single(window.PaletteEntries("copy the picked"));
        Assert.Equal("pick a cell first", copy.Unavailable);
        Assert.False(window.Run(copy.Id));
        Assert.Contains("pick a cell first", window.Status);
        var fill = Assert.Single(window.PaletteEntries("fill a column"));
        Assert.Null(fill.Unavailable);
        Assert.True(window.Run(fill.Id));
        Assert.True(editor.IsOpen("editor.table"));   // running it shows the table
    }

    // ---- fill and paste (A029 P0.4) ----

    private const string Tab = "\t";
    private const string Line = "\n";

    [Fact]
    public void FillingAColumnOfTheShownRowsIsOneUndoStepAndSkipsHiddenRows()
    {
        var (editor, table) = Open();
        table.Filter = "ore";   // only Iron ore
        Assert.Equal(["/wares/1"], table.Visible());
        table.Filter = "";
        table.Sort = null;
        table.Filter = "e";     // rope, ore, gems (names or values with an e); salt is hidden
        var shown = table.Visible();
        Assert.DoesNotContain("/wares/2", shown);
        var plan = table.PlanFill("price", "9.5");
        Assert.True(plan.Ok, string.Join("; ", plan.Problems));
        Assert.Equal(shown.Count, plan.Cells);
        Assert.True(table.Apply(plan), table.Said);
        Assert.All(shown, i => Assert.Equal(9.5, editor.Session.Current.At(i + "/price")!.Value.GetDouble()));
        Assert.Equal(5, editor.Session.Current.At("/wares/2/price")!.Value.GetDouble());   // salt was filtered out: untouched
        Assert.True(editor.Session.Undo());
        Assert.Equal(2, editor.Session.Current.At("/wares/0/price")!.Value.GetDouble());
        Assert.False(editor.Session.Undo());   // the whole fill was one step
    }

    [Fact]
    public void APastedGridLandsInShownOrderTypedCellByCell()
    {
        var (editor, table) = Open();
        table.Sort = ("price", false);   // shown: rope 2, salt 5, ore 12, gems
        var grid = "7" + Tab + "M" + Line + "13.25" + Tab + "S" + Line;   // two rows of (price, size), from salt down
        var plan = table.PlanPaste("/wares/2", "price", grid);
        Assert.True(plan.Ok, string.Join("; ", plan.Problems));
        Assert.True(table.Apply(plan));
        Assert.Equal((7.0, "M"), (editor.Session.Current.At("/wares/2/price")!.Value.GetDouble(), editor.Session.Current.At("/wares/2/size")!.Value.GetString()));
        Assert.Equal((13.25, "S"), (editor.Session.Current.At("/wares/1/price")!.Value.GetDouble(), editor.Session.Current.At("/wares/1/size")!.Value.GetString()));
        Assert.Equal("Set 4 cells as one step.", table.Said);
    }

    [Theory]
    [InlineData("price", "1,5", "isn't a number (use a dot for decimals)")]
    [InlineData("size", "XL", "isn't one of S, M, L")]
    [InlineData("price", "1|2/3", "different numbers of cells")]
    [InlineData("price", "1/2/3/4/5", "only 4 are shown from here")]
    public void ABadPasteIsRefusedWholeSayingWhy(string field, string grid, string why)
    {
        var (editor, table) = Open();
        var plan = table.PlanPaste("/wares/0", field, grid.Replace("|", Tab).Replace("/", Line));   // | is a tab, / a new row
        Assert.False(plan.Ok);
        Assert.Contains(plan.Problems, p => p.Contains(why));
        Assert.False(table.Apply(plan));
        Assert.Empty(editor.Session.Journal);
    }

    [Fact]
    public void AFillTheSchemaRefusesChangesNothingAndUnchangedCellsAreSkipped()
    {
        var (editor, table) = Open();
        Assert.False(table.Apply(table.PlanFill("price", "-1")));   // the schema's minimum, for every row at once
        Assert.Contains("must be at least 0", table.Said);
        Assert.Empty(editor.Session.Journal);
        var same = table.PlanPaste("/wares/0", "price", "2");   // rope's price is already 2
        Assert.True(same.Ok);
        Assert.Empty(same.Commands);
        Assert.True(table.Apply(same));
        Assert.Contains("Nothing to change", table.Said);
    }

    [Fact]
    public void FocusStopsAFillToo()
    {
        var (editor, table) = Open();
        Assert.True(editor.FocusOn("/crew"));
        Assert.False(table.Apply(table.PlanFill("price", "3")));
        Assert.Contains("outside the place you're focused on", table.Said);
    }
}
