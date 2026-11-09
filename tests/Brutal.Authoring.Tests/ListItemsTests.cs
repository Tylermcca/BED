using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Adding a whole new record to a top-level list, and removing one, from the inspector. Found on a game's data: you could add inside a record, but not a record.</summary>
public sealed class ListItemsTests
{
    private const string Schema = """
        {"type":"object","properties":{
          "things":{"type":"array","minItems":1,"items":{"type":"object","required":["id"],"properties":{"id":{"type":"string"},"n":{"type":"integer","default":1}}}},
          "empty":{"type":"array","items":{"type":"object","required":["n"],"properties":{"n":{"type":"integer","default":4}}}},
          "single":{"type":"object","properties":{"a":{"type":"integer"}}}
        }}
        """;

    private static (DocumentSession<DataDocument> Session, EditorModel<DataDocument> Editor, Inspector<DataDocument> Inspector) Rig(string text = """{"things":[{"id":"a","n":3},{"id":"b"}],"empty":[],"single":{"a":1}}""")
    {
        var schemas = new JsonSchemas().Add("doc", Schema);
        var session = DataKit.Session(new DataFormat().Read(text), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (session, editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc")));
    }

    [Fact]
    public void A_selected_list_can_have_an_item_added_as_one_undo_step_with_its_own_id_and_it_becomes_selected()
    {
        var (session, editor, inspector) = Rig();
        Assert.True(editor.Select("/things"));
        Assert.True(inspector.CanAddHere);
        Assert.False(inspector.CanRemoveHere);
        Assert.True(inspector.AddHere());
        Assert.Equal(3, session.Current.At("/things")!.Value.GetArrayLength());
        Assert.Equal("b_2", session.Current.At("/things/2/id")!.Value.GetString());
        Assert.Equal(["/things/2"], editor.Selection);
        Assert.Equal(1, session.History.UndoCount);
        Assert.True(session.Undo());
        Assert.Equal(2, session.Current.At("/things")!.Value.GetArrayLength());
    }

    [Fact]
    public void An_empty_list_starts_from_its_schema()
    {
        var (session, editor, inspector) = Rig();
        Assert.True(editor.Select("/empty"));
        Assert.True(inspector.AddHere());
        Assert.Equal(4, session.Current.At("/empty/0/n")!.Value.GetInt32());
    }

    [Fact]
    public void A_selected_item_can_be_removed_and_undone_and_the_list_becomes_selected()
    {
        var (session, editor, inspector) = Rig();
        Assert.True(editor.Select("/things/0"));
        Assert.True(inspector.CanRemoveHere);
        Assert.False(inspector.CanAddHere);
        Assert.True(inspector.RemoveHere());
        Assert.Equal("b", session.Current.At("/things/0/id")!.Value.GetString());
        Assert.Equal(["/things"], editor.Selection);
        Assert.True(session.Undo());
        Assert.Equal("a", session.Current.At("/things/0/id")!.Value.GetString());
    }

    [Fact]
    public void Removing_what_the_schema_needs_is_refused_and_says_why()
    {
        var (session, editor, inspector) = Rig("""{"things":[{"id":"a"}],"empty":[],"single":{"a":1}}""");
        Assert.True(editor.Select("/things/0"));
        Assert.False(inspector.RemoveHere());
        Assert.NotEmpty(inspector.LastIssues);
        Assert.Equal(1, session.Current.At("/things")!.Value.GetArrayLength());
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_list_of_plain_values_shows_them_so_an_added_one_can_be_seen_and_typed_into()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string"}}}}""");
        var session = DataKit.Session(new DataFormat().Read("""{"tags":["a","b"]}"""), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc"));
        Assert.True(editor.Select("/tags"));
        Assert.Equal(2, inspector.Rows().Count);
        Assert.True(inspector.AddHere());
        Assert.Equal(["/tags"], editor.Selection);
        var rows = inspector.Rows();
        Assert.Equal(3, rows.Count);
        Assert.True(inspector.Set(rows[2].Id, "typed"), string.Join(" ", inspector.LastIssues));
        Assert.Equal("typed", session.Current.At("/tags/2")!.Value.GetString());
        Assert.True(rows[0].CanRemove);
        Assert.True(inspector.RemoveItem(rows[0]));
        Assert.Equal("b", session.Current.At("/tags/0")!.Value.GetString());
    }

    [Fact]
    public void A_plain_object_offers_neither_and_nor_does_several_selected()
    {
        var (_, editor, inspector) = Rig();
        Assert.True(editor.Select("/single"));
        Assert.False(inspector.CanAddHere);
        Assert.False(inspector.CanRemoveHere);
        Assert.False(inspector.AddHere());
        Assert.False(inspector.RemoveHere());
        Assert.True(editor.Select("/things/0", "/things/1"));
        Assert.False(inspector.CanRemoveHere);
    }
}
