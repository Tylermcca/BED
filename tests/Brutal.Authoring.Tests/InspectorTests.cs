using System.Collections.Immutable;
using Brutal.Authoring.Editor;
using Toy;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The inspector model (AT-017): rows from descriptors, edits through the board's own typed commands.</summary>
public sealed class InspectorTests
{
    // The board's item type, described for the inspector. Its default label is "Item" (a stand-in for an archetype value).
    private static readonly ObjectType<Board, Item> ItemType = new("toy.item", "Item", (b, id) => b.Find(id), [
        new("label", "Label", PropertyKind.Text, i => i.Label, (i, v) => new RenameItem(i.Id, (string)v!), Default: (_, _) => "Item",
            Help: "What people see."),
        new("x", "X", PropertyKind.Number, i => i.X, (i, v) => new MoveItem(i.Id, Convert.ToDouble(v), i.Y)),
        new("y", "Y", PropertyKind.Number, i => i.Y, (i, v) => new MoveItem(i.Id, i.X, Convert.ToDouble(v))),
        new("id", "Id", PropertyKind.Text, i => i.Id),   // read-only: ids never change
    ]);

    private static (EditorModel<Board> Editor, Inspector<Board> Inspector) Open(params Item[] items)
    {
        var editor = new EditorModel<Board>(BoardKit.Session(new Board(items.ToImmutableArray())), new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        return (editor, new Inspector<Board>(editor).Add(ItemType));
    }

    [Fact]
    public void RowsShowValuesOverridesAndReadOnlyFields()
    {
        var (e, i) = Open(new Item("lamp", "Lamp", 1, 2));
        Assert.Empty(i.Rows());
        e.Select("lamp");
        var rows = i.Rows().ToDictionary(r => r.Id);
        Assert.Equal("Lamp", rows["label"].Value);
        Assert.True(rows["label"].Overridden);   // "Lamp" isn't the default "Item"
        Assert.Equal(1.0, rows["x"].Value);
        Assert.False(rows["x"].HasDefault);
        Assert.False(rows["id"].Editable);
        Assert.Equal("What people see.", rows["label"].Help);
    }

    [Fact]
    public void EditsAreTheBoardsOwnCommandsAndUndoable()
    {
        var (e, i) = Open(new Item("lamp", "Lamp", 1, 2));
        e.Select("lamp");
        Assert.True(i.Set("x", 5.0));
        Assert.Equal(5.0, e.Session.Current.Find("lamp")!.X);
        Assert.Equal("toy.move", e.Session.Journal[^1].Command);   // a typed command, journaled
        Assert.True(e.Session.Undo());
        Assert.Equal(1.0, e.Session.Current.Find("lamp")!.X);
    }

    [Fact]
    public void ResetRestoresTheDefaultAsOneCommand()
    {
        var (e, i) = Open(new Item("lamp", "Lamp", 1, 2));
        e.Select("lamp");
        Assert.True(i.Reset("label"));
        Assert.Equal("Item", e.Session.Current.Find("lamp")!.Label);
        Assert.False(i.Rows().Single(r => r.Id == "label").Overridden);
        Assert.False(i.Reset("x"));   // no default
        Assert.Contains("no default", i.LastIssues[0].Message);
    }

    [Fact]
    public void MultiSelectShowsMixedValuesAndEditsAllAsOneStep()
    {
        var (e, i) = Open(new Item("a", "Chair", 0, 0), new Item("b", "Chair", 3, 0));
        e.Select("a", "b");
        var rows = i.Rows().ToDictionary(r => r.Id);
        Assert.Equal("Chair", rows["label"].Value);
        Assert.True(rows["x"].Mixed);
        Assert.Null(rows["x"].Value);
        Assert.True(i.Set("y", 7.0));
        Assert.All(e.Session.Current.Items, it => Assert.Equal(7.0, it.Y));
        Assert.Equal(1, e.Session.History.UndoCount);   // one step for the whole selection
    }

    [Fact]
    public void ARefusedEditReportsWhyAndChangesNothing()
    {
        var (e, i) = Open(new Item("lamp", "Lamp", 1, 2));
        e.Select("lamp");
        var revision = e.Session.Revision;
        Assert.False(i.Set("label", ""));   // the board's validator refuses an empty label
        Assert.Contains(i.LastIssues, x => x.ObjectId == "lamp" && x.Path == "$.items[0].label");
        Assert.Equal(revision, e.Session.Revision);
        Assert.False(i.Set("id", "new_id"));   // read-only
        Assert.False(i.Set("no_such_property", 1));
    }

    [Fact]
    public void IssuesForTheSelectionAreShownBesideIt()
    {
        var (e, i) = Open(new Item("lamp", "Lamp", 1, 2), new Item("vase", "Vase", 0, 0));
        e.Select("vase");
        Assert.Empty(i.Issues());
        Assert.Throws<ArgumentException>(() => i.Add(ItemType));   // types register once
    }

    [Fact]
    public void ANonFiniteNumberIsRefusedNotThrown()
    {
        var (e, i) = Open(new Item("a", "A", 0, 0), new Item("b", "B", 1, 1));
        e.Select("a", "b");
        var revision = e.Session.Revision;
        Assert.False(i.Set("x", double.NaN));   // the validator refuses before the journal tries to record NaN
        Assert.Contains(i.LastIssues, x => x.Message.Contains("finite"));
        Assert.Equal(revision, e.Session.Revision);
        Assert.Empty(e.Session.Journal);
    }
}
