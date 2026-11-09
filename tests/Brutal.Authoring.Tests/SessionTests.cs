using System.Collections.Immutable;
using Toy;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The first slice's core proof (worksheet_r2, "The first slice"). Behaviour BORROWED from a BRUTAL game's editor tests and
/// SessionTests (accepted A001 review, AT-010/AT-011), rebuilt on a neutral document.</summary>
public sealed class SessionTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("brutal-authoring-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static DocumentSession<Board> Fresh() => BoardKit.Session(Board.Empty);

    [Fact]
    public void EditsApplyAndUndoRedoStepThroughThem()
    {
        var s = Fresh();
        Assert.True(s.Execute(new AddItem("lamp", "Lamp", 1, 2)));
        Assert.True(s.Execute(new MoveItem("lamp", 3, 4)));
        Assert.Equal((3.0, 4.0), (s.Current.Find("lamp")!.X, s.Current.Find("lamp")!.Y));
        Assert.True(s.Undo());
        Assert.Equal(1.0, s.Current.Find("lamp")!.X);
        Assert.True(s.Undo());
        Assert.Empty(s.Current.Items);
        Assert.False(s.Undo());
        Assert.True(s.Redo());
        Assert.True(s.Redo());
        Assert.Equal(3.0, s.Current.Find("lamp")!.X);
        Assert.False(s.Redo());
    }

    [Fact]
    public void ARefusedEditChangesNothing()
    {
        var s = Fresh();
        s.Execute(new AddItem("lamp", "Lamp", 1, 2));
        s.Execute(new MoveItem("lamp", 5, 5));
        s.Undo();   // leaves a redo step
        var (doc, revision, journal, canUndo, canRedo) = (s.Current, s.Revision, s.Journal.Count, s.History.UndoCount, s.History.CanRedo);

        Assert.False(s.Execute(new MoveItem("ghost", 0, 0)));                       // the command refuses
        Assert.False(s.Execute(new RenameItem("lamp", "")));                        // the validator refuses
        Assert.False(s.Execute(new AddItem("Bad Id", "x", 0, 0)));                 // the validator refuses

        Assert.Same(doc, s.Current);
        Assert.Equal(revision, s.Revision);
        Assert.Equal(journal, s.Journal.Count);
        Assert.Equal(canUndo, s.History.UndoCount);
        Assert.Equal(canRedo, s.History.CanRedo);   // a refused edit doesn't clear redo
        Assert.Contains(s.LastIssues, i => i.Severity == Severity.Error && i.ObjectId == "Bad Id");
    }

    [Fact]
    public void ABatchIsAllOrNothingAndOneUndoStep()
    {
        var s = Fresh();
        Assert.False(s.ExecuteBatch([new AddItem("a", "A", 0, 0), new AddItem("b", "B", 0, 0), new MoveItem("ghost", 1, 1)]));
        Assert.Empty(s.Current.Items);
        Assert.Empty(s.Journal);

        Assert.True(s.ExecuteBatch([new AddItem("a", "A", 0, 0), new AddItem("b", "B", 0, 0)]));
        Assert.Equal(2, s.Current.Items.Length);
        Assert.Equal(1, s.History.UndoCount);
        s.Undo();
        Assert.Empty(s.Current.Items);
    }

    [Fact]
    public void ANewEditClearsRedo()
    {
        var s = Fresh();
        s.Execute(new AddItem("a", "A", 0, 0));
        s.Undo();
        Assert.True(s.History.CanRedo);
        s.Execute(new AddItem("b", "B", 0, 0));
        Assert.False(s.History.CanRedo);
    }

    [Fact]
    public void HistoryKeepsOnlyItsCapacity()
    {
        var s = new DocumentSession<Board>(BoardKit.Format, BoardKit.Validator, BoardKit.Catalog(), Board.Empty, historyCapacity: 3);
        for (var i = 0; i < 5; i++) s.Execute(new AddItem(null, "Item", i, 0));
        Assert.Equal(3, s.History.UndoCount);
        while (s.Undo()) { }
        Assert.Equal(2, s.Current.Items.Length);   // the two oldest steps were forgotten, not undone
    }

    [Fact]
    public void HoldingTheDocumentCantChangeTheSession()
    {
        var s = Fresh();
        s.Execute(new AddItem("a", "A", 0, 0));
        var held = s.Current;
        s.Execute(new MoveItem("a", 9, 9));
        Assert.Equal(0.0, held.Find("a")!.X);   // immutable: the old snapshot is untouched
        Assert.Equal(9.0, s.Current.Find("a")!.X);
    }

    [Fact]
    public void GeneratedIdsAreRecordedSoReplaysMatch()
    {
        var s = Fresh();
        s.Execute(new AddItem(null, "First", 0, 0));
        Assert.Equal("item_1", s.Current.Items[0].Id);
        Assert.Contains("\"id\": \"item_1\"", JournalFile.Write(s.Journal));
    }

    [Fact]
    public void SaveLoadSaveIsByteIdentical()
    {
        var path = Path.Combine(dir, "board.json");
        var s = Fresh();
        s.ExecuteBatch([new AddItem("lamp", "Lamp ünd Ölbild", 0.1, -2.5e-7), new AddItem("chair", "Chair", 1e21, 3)]);
        s.Save(path);
        var first = File.ReadAllBytes(path);
        var again = BoardKit.Open(path);
        Assert.False(again.Dirty);
        again.Save(Path.Combine(dir, "board2.json"));
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(dir, "board2.json")));
        var text = File.ReadAllText(path);
        Assert.EndsWith("}\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.Contains("Lamp ünd Ölbild", text);   // non-ASCII stays readable (goal 12)
    }

    [Fact]
    public void AReplayedJournalGivesTheSameBytesTwice()
    {
        var s = Fresh();
        s.Execute(new AddItem(null, "Lamp", 1, 2));
        s.Execute(new AddItem(null, "Chair", 3, 4));
        s.Execute(new MoveItem("item_1", 5, 6));
        s.Undo();
        s.Redo();
        s.ExecuteBatch([new RenameItem("item_2", "Big chair"), new RemoveItem("item_1")]);
        s.Undo();
        var journal = JournalFile.Read(JournalFile.Write(s.Journal));   // through text, as a file would go

        var a = DocumentSession<Board>.Replay(Board.Empty, journal, BoardKit.Format, BoardKit.Validator, BoardKit.Catalog());
        var b = DocumentSession<Board>.Replay(Board.Empty, journal, BoardKit.Format, BoardKit.Validator, BoardKit.Catalog());
        Assert.Equal(BoardKit.Format.Write(s.Current), BoardKit.Format.Write(a.Current));
        Assert.Equal(BoardKit.Format.Write(a.Current), BoardKit.Format.Write(b.Current));
        Assert.Equal(JournalFile.Write(s.Journal), JournalFile.Write(a.Journal));
    }

    [Fact]
    public void AReplayStopsAtTheFirstRefusedEntryAndNamesIt()
    {
        var journal = new[]
        {
            BoardKit.Catalog().ToEntry(new AddItem("a", "A", 0, 0)),
            BoardKit.Catalog().ToEntry(new MoveItem("ghost", 1, 1)),
        };
        var e = Assert.Throws<ReplayException>(() =>
            DocumentSession<Board>.Replay(Board.Empty, journal, BoardKit.Format, BoardKit.Validator, BoardKit.Catalog()));
        Assert.Equal(1, e.Entry);
        Assert.Contains(e.Issues, i => i.ObjectId == "ghost");
    }

    [Theory]
    [InlineData("""{"kind":"journal","version":1,"entries":[{"op":"do","command":"toy.fly","version":1,"args":{}}]}""", "$.entries[0].command")]
    [InlineData("""{"kind":"journal","version":1,"entries":[{"op":"do","command":"toy.move","version":2,"args":{"id":"a","x":0,"y":0}}]}""", "$.entries[0].version")]
    [InlineData("""{"kind":"journal","version":1,"entries":[{"op":"do","command":"toy.move","version":1,"args":{"id":"a","x":0,"y":0,"z":1}}]}""", "$.entries[0].args")]
    [InlineData("""{"kind":"journal","version":1,"entries":[{"op":"do","command":"toy.move","version":1,"args":{"id":"a","x":"far","y":0}}]}""", "$.entries[0].args")]
    [InlineData("""{"kind":"journal","version":1,"entries":[{"op":"jump"}]}""", "$.entries[0].op")]
    [InlineData("""{"kind":"journal","version":2,"entries":[]}""", "$.version")]
    public void BadJournalsAreRefusedWithTheirPath(string text, string path)
    {
        var e = Assert.ThrowsAny<Exception>(() => DocumentSession<Board>.Replay(
            Board.Empty, JournalFile.Read(text), BoardKit.Format, BoardKit.Validator, BoardKit.Catalog()));
        var where = e switch { ReplayException r => r.Issues[0].Path, DocumentFormatException f => f.Path, _ => null };
        Assert.NotNull(where);
        Assert.StartsWith(path, where);
    }

    [Theory]
    [InlineData("""{"kind":"toy.board","version":1,"items":[{"id":"a","label":"A","x":0,"y":0,"z":1}]}""", "$.items[0].z")]
    [InlineData("""{"kind":"toy.board","version":1,"items":[{"id":"a","label":"A","x":0}]}""", "$.items[0].y")]
    [InlineData("""{"kind":"toy.board","version":2,"items":[]}""", "$.version")]
    [InlineData("""{"kind":"toy.board","version":1,"version":1,"items":[]}""", "$.version")]
    [InlineData("""{"kind":"toy.map","version":1,"items":[]}""", "$.kind")]
    [InlineData("""not json""", "$")]
    public void BadBoardsAreRefusedWithTheirPath(string text, string path) =>
        Assert.Equal(path, Assert.Throws<DocumentFormatException>(() => BoardKit.Format.Read(text)).Path);

    [Fact]
    public void AnInvalidFileNeverBecomesASession()
    {
        var path = Path.Combine(dir, "dupes.json");
        File.WriteAllText(path, """{"kind":"toy.board","version":1,"items":[{"id":"a","label":"A","x":0,"y":0},{"id":"a","label":"B","x":0,"y":0}]}""");
        var e = Assert.Throws<InvalidDocumentException>(() => BoardKit.Open(path));
        Assert.Contains(e.Issues, i => i.ObjectId == "a" && i.Message.Contains("used twice"));
    }

    [Fact]
    public void DirtyTracksSavedState()
    {
        var path = Path.Combine(dir, "board.json");
        var s = Fresh();
        s.Save(path);
        Assert.False(s.Dirty);
        s.Execute(new AddItem("a", "A", 0, 0));
        Assert.True(s.Dirty);
        s.Undo();
        Assert.False(s.Dirty);   // back to what was saved
        s.Redo();
        s.Save();
        Assert.False(s.Dirty);
    }

    [Fact]
    public void AFileWithWindowsLineEndingsOpensClean()
    {
        var path = Path.Combine(dir, "crlf.json");
        File.WriteAllText(path, BoardKit.Format.Write(new Board([new Item("a", "A", 1, 2)])).Replace("\n", "\r\n"));
        var s = BoardKit.Open(path);
        Assert.False(s.Dirty);
        Assert.Equal(DiskState.Unchanged, s.CheckDisk());
    }

    [Fact]
    public void AFailedSaveKeepsTheOldFile()
    {
        var path = Path.Combine(dir, "board.json");
        var s = Fresh();
        s.Execute(new AddItem("a", "A", 0, 0));
        s.Save(path);
        var before = File.ReadAllBytes(path);
        s.Execute(new AddItem("b", "B", 0, 0));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))   // hold the file locked
        {
            var e = Assert.ThrowsAny<Exception>(() => s.Save());
            Assert.True(e is IOException or UnauthorizedAccessException, e.GetType().Name);   // Windows reports a locked target either way
        }
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(s.Dirty);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void OutsideEditsAreNoticedAndOwnSavesAreNot()
    {
        var path = Path.Combine(dir, "board.json");
        var s = Fresh();
        s.Save(path);
        Assert.Equal(DiskState.Unchanged, s.CheckDisk());
        s.Execute(new AddItem("a", "A", 0, 0));
        s.Save();
        Assert.Equal(DiskState.Unchanged, s.CheckDisk());

        var text = File.ReadAllText(path).Replace("\"A\"", "\"Z\"");   // same length, different content
        File.WriteAllText(path, text);
        Assert.Equal(DiskState.ChangedOnDisk, s.CheckDisk());
        File.Delete(path);
        Assert.Equal(DiskState.Missing, s.CheckDisk());
    }

    [Fact]
    public void RevertLoadsTheDiskAndRefusesABadFile()
    {
        var path = Path.Combine(dir, "board.json");
        var s = Fresh();
        s.Execute(new AddItem("a", "A", 0, 0));
        s.Save(path);
        s.Execute(new MoveItem("a", 7, 7));

        File.WriteAllText(path, "{ broken");
        Assert.False(s.Revert());
        Assert.Equal(7.0, s.Current.Find("a")!.X);   // the bad file changed nothing
        Assert.NotEmpty(s.LastIssues);

        File.WriteAllText(path, BoardKit.Format.Write(new Board([new Item("a", "A", 2, 2)])));
        Assert.True(s.Revert());
        Assert.Equal(2.0, s.Current.Find("a")!.X);
        Assert.False(s.History.CanUndo);
        Assert.False(s.Dirty);
    }

    [Fact]
    public void TheRevisionCountsEveryChange()
    {
        var s = Fresh();
        var r0 = s.Revision;
        s.Execute(new AddItem("a", "A", 0, 0));
        s.Undo();
        s.Redo();
        Assert.Equal(r0 + 3, s.Revision);
        s.Execute(new MoveItem("nobody", 0, 0));
        Assert.Equal(r0 + 3, s.Revision);
    }

    [Fact]
    public void CommandsCanBeFoundByName()
    {
        var catalog = BoardKit.Catalog();
        Assert.Equal(["toy.move", "toy.remove"], catalog.Search("move").Select(c => c.Id));   // "Remove" contains "move" too
        Assert.Equal(["toy.move"], catalog.Search("toy.move").Select(c => c.Id));
        Assert.Equal(["toy.rename"], catalog.Search("RENAME").Select(c => c.Id));
        Assert.Equal(4, catalog.Search("").Count());
        var hidden = new CommandCatalog<Board>().Register<MoveItem>("toy.secret_move", 1, "Secret move", isInternal: true);
        Assert.Empty(hidden.Search("move"));
    }

    [Fact]
    public void TheCatalogueRefusesBadRegistrations()
    {
        Assert.Throws<ArgumentException>(() => new CommandCatalog<Board>().Register<MoveItem>("Move", 1, "Move"));
        Assert.Throws<ArgumentException>(() => BoardKit.Catalog().Register<MoveItem>("toy.move2", 1, "Move again"));
        Assert.Throws<ArgumentException>(() => BoardKit.Catalog().Register<MoveItem>("toy.move", 1, "Move"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandCatalog<Board>().Register<MoveItem>("toy.move", 0, "Move"));
    }

    [Fact]
    public void AnUnregisteredCommandIsAProgrammingError()
    {
        var s = new DocumentSession<Board>(BoardKit.Format, BoardKit.Validator, new CommandCatalog<Board>(), Board.Empty);
        Assert.Throws<InvalidOperationException>(() => s.Execute(new AddItem("a", "A", 0, 0)));
        Assert.Empty(s.Current.Items);
    }
}
