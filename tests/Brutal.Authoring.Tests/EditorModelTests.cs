using System.Collections.Immutable;
using Brutal.Authoring.Editor;
using Toy;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The editor model (second slice, no UI): selection, workspaces, the palette and focus, over the core session.</summary>
public sealed class EditorModelTests
{
    private static readonly FlatOutline<Board> BoardOutline = new(b => b.Items.Select(i => (i.Id, i.Label)));

    private static EditorModel<Board> Board(params Item[] items) => new(BoardKit.Session(new Board(items.ToImmutableArray())), BoardOutline);

    // A small nested document, defined here, to test focus: places hold places and objects.
    private sealed record Node(string Id, string Label, string? Parent);

    private sealed record Tree(ImmutableArray<Node> Nodes);

    private sealed class TreeOutline : IDocumentOutline<Tree>
    {
        public IEnumerable<string> ObjectIds(Tree d) => d.Nodes.Select(n => n.Id);
        public string? Label(Tree d, string id) => d.Nodes.FirstOrDefault(n => n.Id == id)?.Label;
        public string? Parent(Tree d, string id) => d.Nodes.FirstOrDefault(n => n.Id == id)?.Parent;
    }

    private sealed record Rename(string Id, string Label) : ICommand<Tree>
    {
        public CommandResult<Tree> Apply(Tree d) => d.Nodes.Any(n => n.Id == Id)
            ? CommandResult<Tree>.Done(new Tree(d.Nodes.Select(n => n.Id == Id ? n with { Label = Label } : n).ToImmutableArray()))
            : CommandResult<Tree>.Refused(Issue.Error($"no '{Id}'", Id));
    }

    private sealed record Remove(string Id) : ICommand<Tree>
    {
        public CommandResult<Tree> Apply(Tree d) => CommandResult<Tree>.Done(new Tree(d.Nodes.RemoveAll(n => n.Id == Id || n.Parent == Id)));
    }

    private sealed class TreeFormat : IDocumentFormat<Tree>
    {
        public string Kind => "test.tree";
        public int Version => 1;
        public Tree Read(string text) => throw new NotSupportedException();
        public string Write(Tree d) => string.Join("\n", d.Nodes.Select(n => $"{n.Id}|{n.Label}|{n.Parent}")) + "\n";
    }

    private sealed class AnyTree : IValidator<Tree>
    {
        public IReadOnlyList<Issue> Validate(Tree d) => [];
    }

    private static EditorModel<Tree> World()
    {
        var tree = new Tree([
            new("world", "World", null),
            new("north", "North region", "world"),
            new("crypt", "Crypt", "north"),
            new("table", "Table", "crypt"),
            new("south", "South region", "world"),
            new("well", "Well", "south"),
        ]);
        var catalog = new CommandCatalog<Tree>().Register<Rename>("test.rename", 1, "Rename").Register<Remove>("test.remove", 1, "Remove");
        return new EditorModel<Tree>(new DocumentSession<Tree>(new TreeFormat(), new AnyTree(), catalog, tree), new TreeOutline());
    }

    [Fact]
    public void SelectionIgnoresUnknownIdsAndKeepsOrder()
    {
        var e = Board(new Item("a", "A", 0, 0), new Item("b", "B", 0, 0));
        Assert.True(e.Select("b", "ghost", "a"));
        Assert.Equal(["b", "a"], e.Selection);
        Assert.Equal("a", e.Primary);
        Assert.False(e.Select("b", "a"));   // no change, no event
        Assert.True(e.Toggle("b"));
        Assert.Equal(["a"], e.Selection);
    }

    [Fact]
    public void ViewStateNeverDirtiesUndoesOrJournals()
    {
        var e = Board(new Item("a", "A", 0, 0));
        var (revision, journal) = (e.Session.Revision, e.Session.Journal.Count);
        e.Select("a");
        e.Add(new Workspace("toy.layout", "Layout", ["toy.move"]));
        e.Use("toy.layout");
        e.FocusOn("a");
        Assert.Equal(revision, e.Session.Revision);
        Assert.Equal(journal, e.Session.Journal.Count);
        Assert.False(e.Session.History.CanUndo);
    }

    [Fact]
    public void SelectionDropsObjectsThatGoAway()
    {
        var e = Board(new Item("a", "A", 0, 0));
        Assert.True(e.Session.Execute(new AddItem("b", "B", 0, 0)));
        e.Select("a", "b");
        e.Session.Undo();   // b is gone
        Assert.Equal(["a"], e.Selection);
    }

    [Fact]
    public void WorkspacesMustNameRealCommandsAndHaveUniqueIds()
    {
        var e = Board();
        e.Add(new Workspace("toy.layout", "Layout", ["toy.move"]));
        Assert.Throws<ArgumentException>(() => e.Add(new Workspace("toy.layout", "Again", [])));
        Assert.Throws<ArgumentException>(() => e.Add(new Workspace("toy.paint", "Paint", ["toy.paint_all"])));
        Assert.Throws<ArgumentException>(() => e.Use("toy.nowhere"));
    }

    [Fact]
    public void ThePaletteRanksMatchesAndTheWorkspaceLeads()
    {
        var e = Board();
        Assert.Equal(["toy.move", "toy.remove"], e.Search("move").Select(p => p.Command.Id));   // starts-with beats contains
        Assert.Equal(["toy.add", "toy.move", "toy.rename", "toy.remove"], e.Search("").Select(p => p.Command.Id));
        Assert.Equal(["toy.add", "toy.move", "toy.rename", "toy.remove"], e.Search("item").Select(p => p.Command.Id));   // word start, catalogue order

        e.Add(new Workspace("toy.tidy", "Tidy up", ["toy.remove", "toy.rename"]));
        e.Use("toy.tidy");
        Assert.Equal(["toy.remove", "toy.rename", "toy.add", "toy.move"], e.Search("item").Select(p => p.Command.Id));   // in the workspace's own order
        Assert.Empty(e.Search("teleport"));
    }

    [Fact]
    public void UnavailableCommandsStayFindableWithAReason()
    {
        var e = Board();
        var found = e.Search("move", c => c.Id == "toy.move" ? "select an item first" : null);
        var move = Assert.Single(found, p => p.Command.Id == "toy.move");
        Assert.False(move.Available);
        Assert.Equal("select an item first", move.Unavailable);
    }

    [Fact]
    public void FocusBuildsABreadcrumbAndStepsBackOut()
    {
        var e = World();
        Assert.True(e.FocusOn("crypt"));
        Assert.Equal(["world", "north", "crypt"], e.Focus);
        Assert.True(e.InFocus("table"));
        Assert.False(e.InFocus("well"));
        Assert.True(e.FocusOut());
        Assert.Equal("north", e.FocusedOn);
        Assert.True(e.FocusOut());
        Assert.True(e.FocusOut());
        Assert.False(e.FocusOut());
        Assert.True(e.InFocus("well"));
        Assert.False(e.FocusOn("nowhere"));
    }

    [Fact]
    public void A_batch_is_one_step_and_a_focus_refuses_it_if_any_part_reaches_outside()
    {
        var e = World();
        e.FocusOn("crypt");
        var revision = e.Session.Revision;
        Assert.False(e.ExecuteBatch([new Rename("table", "Long table"), new Rename("well", "Deep well")], "table", "well"));
        Assert.Contains(e.Refused, i => i.ObjectId == "well" && i.Message.Contains("outside the place"));
        Assert.Equal(revision, e.Session.Revision);
        Assert.Equal("Table", e.Outline.Label(e.Session.Current, "table"));

        while (e.FocusOut()) { }
        var steps = e.Session.History.UndoCount;
        Assert.True(e.ExecuteBatch([new Rename("table", "Long table"), new Rename("well", "Deep well")], "table", "well"));
        Assert.Empty(e.Refused);
        Assert.Equal(steps + 1, e.Session.History.UndoCount);
    }

    [Fact]
    public void Playing_refuses_a_batch_too()
    {
        var e = World().Add(new PlayMode<Tree>("t.play", "Trial", _ => new NoGame()));
        Assert.True(e.Play("t.play"));
        var revision = e.Session.Revision;
        Assert.False(e.ExecuteBatch([new Rename("table", "Long table")], "table"));
        Assert.Contains(e.Refused, i => i.Message.Contains("is playing"));
        Assert.Equal(revision, e.Session.Revision);
    }

    private sealed class NoGame : IPlaySession
    {
        public void Draw() { }
        public string Status => "";
        public void Dispose() { }
    }

    [Fact]
    public void EditsOutsideTheFocusAreRefusedAndChangeNothing()
    {
        var e = World();
        e.FocusOn("crypt");
        Assert.True(e.Execute(new Rename("table", "Long table"), "table"));
        var revision = e.Session.Revision;
        Assert.False(e.Execute(new Rename("well", "Deep well"), "well"));
        Assert.Contains(e.Refused, i => i.ObjectId == "well" && i.Message.Contains("outside the place"));
        Assert.Equal(revision, e.Session.Revision);
        Assert.True(e.Session.Undo());   // edits made in focus are ordinary, undoable commands
        Assert.Equal("Table", e.Outline.Label(e.Session.Current, "table"));
    }

    [Fact]
    public void FocusLetsGoOfAPlaceThatGoesAway()
    {
        var e = World();
        e.FocusOn("crypt");
        e.Select("table");
        Assert.True(e.Session.Execute(new Remove("north")));   // removes north and what it directly holds
        Assert.Equal(["world"], e.Focus);   // focus steps back out to the nearest place that still exists
    }
}
