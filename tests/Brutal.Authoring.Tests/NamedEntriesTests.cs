using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>An object whose entries are named by whoever adds them, such as a map from a slot's name to what's in it: added to and removed from
/// in the inspector, with the schema judging both the name and the value.</summary>
public sealed class NamedEntriesTests
{
    private const string Schema = """
        {"type":"object","properties":{"unit":{"type":"object","properties":{
          "slots":{"type":"object","propertyNames":{"pattern":"^[a-z]+\\.[0-9]+$"},"additionalProperties":{"type":"object","required":["what"],"properties":{"what":{"type":"string","default":"nothing"}}},"minProperties":1},
          "fixed":{"type":"object","properties":{"a":{"type":"number"}}},
          "open":{"type":"object","additionalProperties":{"type":"number"}}
        }}}}
        """;

    private static (DocumentSession<DataDocument> Session, Inspector<DataDocument> Inspector, Func<InspectorRow> Slots) Rig(string unit)
    {
        var schemas = new JsonSchemas().Add("doc", Schema);
        var session = DataKit.Session(new DataFormat().Read("{\"unit\":" + unit + "}"), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc"));
        editor.Select("/unit");
        return (session, inspector, () => inspector.Rows().Single(r => r.Id == "slots"));
    }

    [Fact]
    public void An_entry_is_added_by_name_as_a_copy_of_the_last_and_undone()
    {
        var (session, inspector, slots) = Rig("""{"slots":{"bow.1":{"what":"gun"}}}""");
        Assert.True(slots().NamedEntries);
        Assert.True(inspector.AddNamed(slots(), "bow.2"));
        Assert.Equal("gun", session.Current.At("/unit/slots/bow.2/what")!.Value.GetString());
        Assert.Equal(1, session.History.UndoCount);
        Assert.True(session.Undo());
        Assert.Null(session.Current.At("/unit/slots/bow.2"));
    }

    [Fact]
    public void A_name_the_schema_refuses_or_one_already_taken_changes_nothing()
    {
        var (session, inspector, slots) = Rig("""{"slots":{"bow.1":{"what":"gun"}}}""");
        Assert.False(inspector.AddNamed(slots(), "NOT A NAME"));
        Assert.False(inspector.AddNamed(slots(), "bow.1"));
        Assert.Contains(inspector.LastIssues, i => i.Message.Contains("bow.1", StringComparison.Ordinal));
        Assert.False(inspector.AddNamed(slots(), ""));
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_name_with_a_slash_in_it_is_one_name()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","properties":{"unit":{"type":"object","properties":{"slots":{"type":"object","additionalProperties":{"type":"number"}}}}}}""");
        var session = DataKit.Session(new DataFormat().Read("""{"unit":{"slots":{"a/b":1}}}"""), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc"));
        editor.Select("/unit");
        var row = inspector.Rows().Single(r => r.Id == "slots");
        Assert.True(inspector.AddNamed(row, "c/d"));
        Assert.Equal(1, session.Current.At("/unit/slots/c~1d")!.Value.GetInt32());
        var entry = inspector.Nested(inspector.Rows().Single(r => r.Id == "slots")).Single(r => r.Label == "c/d");
        Assert.True(entry.CanRemove);
        Assert.True(inspector.RemoveItem(entry));
        Assert.Null(session.Current.At("/unit/slots/c~1d"));
        Assert.NotNull(session.Current.At("/unit/slots/a~1b"));
    }

    [Fact]
    public void An_entry_is_removed_and_undone_and_the_last_one_stays_when_the_schema_needs_it()
    {
        var (session, inspector, slots) = Rig("""{"slots":{"bow.1":{"what":"gun"},"bow.2":{"what":"saw"}}}""");
        var entry = inspector.Nested(slots()).Single(r => r.Label == "bow.2");
        Assert.True(entry.CanRemove);
        Assert.True(inspector.RemoveItem(entry));
        Assert.Null(session.Current.At("/unit/slots/bow.2"));
        Assert.True(session.Undo());
        Assert.NotNull(session.Current.At("/unit/slots/bow.2"));
        Assert.True(inspector.RemoveItem(inspector.Nested(slots()).Single(r => r.Label == "bow.2")));
        Assert.False(inspector.RemoveItem(inspector.Nested(slots()).Single(r => r.Label == "bow.1")));   // at least one, its schema says
        Assert.NotNull(session.Current.At("/unit/slots/bow.1"));
    }

    [Fact]
    public void An_object_with_fixed_fields_offers_neither()
    {
        var (_, inspector, _) = Rig("""{"slots":{"bow.1":{"what":"gun"}},"fixed":{"a":1}}""");
        var row = inspector.Rows().Single(r => r.Id == "fixed");
        Assert.False(row.NamedEntries);
        Assert.False(inspector.AddNamed(row, "b"));
        var a = inspector.Nested(row).Single();
        Assert.False(a.CanRemove);
        Assert.False(inspector.RemoveItem(a));
    }

    [Fact]
    public void An_empty_map_or_one_not_there_yet_starts_from_its_schema()
    {
        var (session, inspector, _) = Rig("""{"open":{}}""");
        var open = inspector.Rows().Single(r => r.Id == "open");
        Assert.True(open.NamedEntries);
        Assert.True(inspector.AddNamed(open, "first"));
        Assert.Equal(JsonValueKind.Number, session.Current.At("/unit/open/first")!.Value.ValueKind);
    }
}
