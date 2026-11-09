using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Data files in the editor, described by their schema: the outline, inspector rows from the schema, issues
/// linked to their items, the window drawing it headless, and the datacheck tool.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class DataEditingTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("brutal-authoring-data-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private const string Schema = """
        {"$id":"https://example.test/schemas/wares.schema.json","type":"object","required":["kind","wares"],"additionalProperties":false,
         "properties":{"kind":{"const":"wares"},
           "rules":{"type":"object","properties":{"tax":{"type":"number","minimum":0,"maximum":1,"description":"Share taken at sale.","default":0.1}}},
           "wares":{"type":"array","items":{"$ref":"#/$defs/ware"}}},
         "$defs":{"ware":{"type":"object","required":["id","price"],"additionalProperties":false,
           "properties":{"id":{"type":"string","pattern":"^[a-z_]+$"},"name":{"type":"string","description":"What players see."},
             "price":{"type":"number","minimum":0,"title":"Price (credits)"},"stack":{"type":"integer","minimum":1,"default":10},
             "size":{"enum":["S","M","L"]},"legal":{"type":"boolean"},"tags":{"type":"array","items":{"type":"string"}}}}}}
        """;

    private const string Text = """
        {
          "kind": "wares",
          "rules": { "tax": 0.2 },
          "wares": [
            { "id": "rope", "name": "Rope", "price": 2, "stack": 50, "size": "S", "legal": true, "tags": ["cheap"] },
            { "id": "ore", "price": 12, "size": "L" }
          ]
        }

        """;

    private static readonly JsonSchemas Schemas = new JsonSchemas().Add("wares.schema.json", Schema);

    private static (EditorModel<DataDocument> Editor, Inspector<DataDocument> Inspector) Open()
    {
        var session = DataKit.Session(new DataFormat("wares").Read(Text), Schemas, "wares.schema.json", "wares");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(Schemas, "wares.schema.json")));
    }

    [Fact]
    public void TheOutlineShowsSectionsAndNamedItems()
    {
        var (editor, _) = Open();
        var d = editor.Session.Current;
        Assert.Equal(["", "/rules", "/wares", "/wares/0", "/wares/1"], editor.Outline.ObjectIds(d));   // "": the file's own plain fields (A126)
        Assert.Equal("Rope", editor.Outline.Label(d, "/wares/0"));   // its name
        Assert.Equal("ore", editor.Outline.Label(d, "/wares/1"));    // no name: its id
        Assert.Equal("wares", editor.Outline.Label(d, "/wares"));
        Assert.Equal("/wares", editor.Outline.Parent(d, "/wares/1"));
        Assert.True(editor.FocusOn("/wares"));
        Assert.False(editor.InFocus("/rules"));
    }

    [Fact]
    public void InspectorRowsComeFromTheSchema()
    {
        var (editor, inspector) = Open();
        editor.Select("/wares/0");
        var rows = inspector.Rows().ToDictionary(r => r.Id);
        Assert.Equal(["id", "name", "price", "stack", "size", "legal", "tags"], inspector.Rows().Select(r => r.Id));   // schema order
        Assert.Equal((PropertyKind.Text, "Rope", "What players see."), (rows["name"].Kind, rows["name"].Value, rows["name"].Help));
        Assert.Equal((PropertyKind.Number, (object?)2.0), (rows["price"].Kind, rows["price"].Value));
        Assert.Equal(("Price (credits)", "stack"), (rows["price"].Label, rows["stack"].Label));   // the schema's title, else the key
        Assert.Equal((PropertyKind.Integer, (object?)50L), (rows["stack"].Kind, rows["stack"].Value));
        Assert.Equal(PropertyKind.Choice, rows["size"].Kind);
        Assert.Equal(["S", "M", "L"], rows["size"].Choices);
        Assert.Equal((PropertyKind.Toggle, (object?)true), (rows["legal"].Kind, rows["legal"].Value));
        Assert.Equal("(a list of 1)", rows["tags"].Value);
        Assert.False(rows["tags"].Editable);   // lists are edited as items, not as one field
        Assert.True(rows["stack"].Overridden && rows["stack"].HasDefault);   // 50 differs from its default 10
    }

    [Fact]
    public void EditingAFieldRunsTheDocumentsCommandsAndTheSchemaChecksIt()
    {
        var (editor, inspector) = Open();
        editor.Select("/wares/0");
        Assert.True(inspector.Set("price", 3.5));
        Assert.Equal(3.5, editor.Session.Current.At("/wares/0/price")!.Value.GetDouble());
        Assert.True(inspector.Set("size", "M"));
        Assert.False(inspector.Set("price", -1.0));   // the schema's minimum
        Assert.Contains("must be at least 0", inspector.LastIssues[0].Message);
        Assert.True(inspector.Reset("stack"));
        Assert.Equal(10, editor.Session.Current.At("/wares/0/stack")!.Value.GetInt32());
        Assert.True(editor.Session.Undo());
        Assert.Equal(50, editor.Session.Current.At("/wares/0/stack")!.Value.GetInt32());
    }

    [Fact]
    public void SettingAFieldThatIsntThereYetWritesItsSchemasType()
    {
        var (editor, inspector) = Open();
        editor.Select("/wares/1");   // ore has no stack yet
        Assert.Null(inspector.Rows().Single(r => r.Id == "stack").Value);
        Assert.True(inspector.Set("stack", 5L));
        var stack = editor.Session.Current.At("/wares/1/stack")!.Value;
        Assert.Equal(System.Text.Json.JsonValueKind.Number, stack.ValueKind);   // a number, not the text "5"
        Assert.Equal(5, stack.GetInt32());
    }

    [Fact]
    public void ASetOnSeveralItemsIsOneUndoStepAndMixedValuesShowAsMixed()
    {
        var (editor, inspector) = Open();
        editor.Select("/wares/0", "/wares/1");
        Assert.True(inspector.Rows().Single(r => r.Id == "price").Mixed);
        Assert.True(inspector.Set("price", 7.0));
        Assert.Equal([7.0, 7.0], new[] { "/wares/0/price", "/wares/1/price" }.Select(p => editor.Session.Current.At(p)!.Value.GetDouble()));
        Assert.True(editor.Session.Undo());
        Assert.Equal(12, editor.Session.Current.At("/wares/1/price")!.Value.GetDouble());
    }

    [Fact]
    public void NestedObjectsGetRowsTooFromTheirSchema()
    {
        var (editor, inspector) = Open();
        editor.Select("/rules");
        var tax = Assert.Single(inspector.Rows(), r => r.Id == "tax");
        Assert.Equal(("Share taken at sale.", (object?)0.2), (tax.Help, tax.Value));
        Assert.True(inspector.Reset("tax"));
        Assert.Equal(0.1, editor.Session.Current.At("/rules/tax")!.Value.GetDouble());
    }

    [Fact]
    public void IssuesNameTheItemTheyreIn()
    {
        var bad = Text.Replace("\"price\": 12", "\"price\": -4");
        var issues = new DataValidator(Schemas, "wares.schema.json").Validate(new DataFormat().Read(bad));
        var issue = Assert.Single(issues);
        Assert.Equal(("/wares/1", "$.wares[1].price"), (issue.ObjectId, issue.Path));
        Assert.Equal("/a/b c/0", Pointer.FromPath("$.a['b c'][0]"));
        Assert.Null(Pointer.FromPath("x.y"));
    }

    [Fact]
    public void TheWindowDrawsADataFileHeadless()
    {
        var (editor, inspector) = Open();
        var window = new EditorWindow<DataDocument>(editor, inspector);
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        editor.Select("/wares/0");
        host.Frames(window.DrawDocked, 4);
        Assert.True(host.Covered() > 1400 * 800 / 10);
    }

    [Fact]
    public void DatacheckReportsEveryProblemAcrossAFolder()
    {
        var data = Directory.CreateDirectory(Path.Combine(dir, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(dir, "schemas")).FullName;
        File.WriteAllText(Path.Combine(schemas, "wares.schema.json"), Schema);
        File.WriteAllText(Path.Combine(data, "wares.json"), Text);
        var output = new StringWriter();
        Assert.Equal(0, DataCheck.Program.Run([data, schemas], output, new StringWriter()));
        Assert.Contains("1 file(s) checked, 0 with problems.", output.ToString());

        File.WriteAllText(Path.Combine(data, "wares.json"), Text.Replace("\"price\": 12", "\"price\": -4").Replace("\"size\": \"L\"", "\"size\": \"XL\""));
        output = new StringWriter();
        Assert.Equal(1, DataCheck.Program.Run([data, schemas], output, new StringWriter()));
        Assert.Contains("wares.json $.wares[1].price: must be at least 0", output.ToString());
        Assert.Contains("wares.json $.wares[1].size: must be one of", output.ToString());

        File.WriteAllText(Path.Combine(data, "other.json"), "{}");   // no schema for it
        Assert.Equal(2, DataCheck.Program.Run([data, schemas], new StringWriter(), new StringWriter()));
        Assert.Equal(2, DataCheck.Program.Run(["missing"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void ASchemaCanNameTheFieldThatLabelsItsItems()
    {
        var schema = Schema.Replace("\"ware\":{\"type\":\"object\",", "\"ware\":{\"type\":\"object\",\"x-label\":\"id\",");
        var schemas = new JsonSchemas().Add("wares.schema.json", schema);
        var doc = new DataFormat().Read(Text);
        Assert.Empty(new DataValidator(schemas, "wares.schema.json").Validate(doc));   // x- keywords are notes, not errors
        var outline = DataEditing.OutlineFor(schemas, "wares.schema.json");
        Assert.Equal("rope", outline.Label(doc, "/wares/0"));             // its id, as the schema says, not its name "Rope"
        Assert.Equal("Rope", DataEditing.Outline.Label(doc, "/wares/0")); // without the schema: name first
    }

    [Theory]
    [InlineData("What players see.", "What players see.")]
    [InlineData("Where it stands. Rock or world.", "Where it stands.")]
    [InlineData("Where it stands: rock, world or belt.", "Where it stands")]
    public void LongHelpShowsItsFirstSentence(string help, string shown) => Assert.Equal(shown, EditorWindow<DataDocument>.FirstSentence(help));
}
