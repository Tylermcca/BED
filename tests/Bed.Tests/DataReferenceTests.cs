using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>Following id references in the data table (A029 P0.5): declared with x-ref, or supplied by a game; never
/// guessed from a field's name.</summary>
public sealed class DataReferenceTests
{
    private const string Schema = """
        {"type":"object","properties":{
           "wares":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"price":{"type":"number"}}}},
           "crew":{"type":"array","items":{"type":"object","properties":{
             "id":{"type":"string"},
             "favourite":{"type":"string","x-ref":"/wares"},
             "rival":{"type":"string","x-ref":"/crew","x-ref-key":"call_sign"},
             "ware":{"type":"string"},
             "call_sign":{"type":"string"}}}}}}
        """;

    private const string Text = """
        {
          "wares": [ { "id": "rope", "price": 2 }, { "id": "ore", "price": 12 }, { "id": "ore", "price": 13 } ],
          "crew": [
            { "id": "ana", "favourite": "rope", "rival": "Hawk", "ware": "rope", "call_sign": "Swift" },
            { "id": "bo", "favourite": "ore", "call_sign": "Hawk" },
            { "id": "cy", "favourite": "salt", "call_sign": "Owl" }
          ]
        }

        """;

    private static readonly JsonSchemas Schemas = new JsonSchemas().Add("r.schema.json", Schema);

    private static (EditorModel<DataDocument> Editor, DataTableView Table) Open()
    {
        var editor = new EditorModel<DataDocument>(DataKit.Session(new DataFormat().Read(Text), Schemas, "r.schema.json"), DataEditing.Outline);
        var table = new DataTableView(editor, DataEditing.ObjectType(Schemas, "r.schema.json"))
        {
            List = "/crew",
            References = DataEditing.SchemaReferences(Schemas, "r.schema.json"),
        };
        return (editor, table);
    }

    [Fact]
    public void ADeclaredReferenceFindsItsItemAndBackReturns()
    {
        var (editor, table) = Open();
        Assert.Equal("/wares/0", table.Target("/crew/0", "favourite"));
        Assert.True(table.Follow("/crew/0", "favourite"));
        Assert.Equal("/wares", table.Shown);
        Assert.Equal(["/wares/0"], editor.Selection);
        Assert.True(table.Back());
        Assert.Equal("/crew", table.Shown);
        Assert.Equal(["/crew/0"], editor.Selection);
        Assert.False(table.Back());   // nowhere left to go back to
        Assert.Empty(editor.Session.Journal);   // following never edits
    }

    [Fact]
    public void ADifferentKeyFieldCanBeNamed() =>
        Assert.Equal("/crew/1", Open().Table.Target("/crew/0", "rival"));   // matched on call_sign, not id

    [Fact]
    public void MissingDuplicateAndUndeclaredReferencesResolveToNothing()
    {
        var (_, table) = Open();
        Assert.Null(table.Target("/crew/2", "favourite"));   // no ware called salt
        Assert.Null(table.Target("/crew/1", "favourite"));   // two wares called ore: ambiguous, so nothing
        Assert.Null(table.Target("/crew/0", "ware"));        // looks like a ware id, but the schema doesn't say so
        Assert.False(table.Follow("/crew/2", "favourite"));
        Assert.Equal("favourite doesn't name an item here.", table.Said);
    }

    [Fact]
    public void AGameCanSupplyItsOwnResolver()
    {
        var (_, table) = Open();
        table.References = (doc, field, value) => field.EndsWith("/ware") ? "/wares/1" : null;   // a game's own rule
        Assert.Equal("/wares/1", table.Target("/crew/0", "ware"));
        Assert.Null(table.Target("/crew/0", "favourite"));
    }
}
