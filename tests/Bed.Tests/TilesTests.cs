using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Tiles;
using Xunit;

namespace Bed.Tests;

// INVENTED: acceptance probes for the tile sample's explicit row-major and resize contracts.
public sealed class TilesTests
{
    private static DocumentSession<TileMap> Ready(int width = 3, int height = 2)
    {
        var s = TilesKit.Session(TileMap.Empty(width, height, .5));
        Assert.True(s.Execute(new AddKind("grass", "Grass")));
        return s;
    }
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("tiles-test-").FullName;
        public string File(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private static (int Code, string Out, string Err) Cli(params string[] args)
    {
        using var o = new StringWriter(); using var e = new StringWriter();
        return (Tiles.Program.Run(args, o, e), o.ToString(), e.ToString());
    }
    private static string Journal(string id, object args) => JournalFile.Write([
        JournalEntry.Do(id, 1, JsonSerializer.SerializeToElement(args))]);
    private static void RefusedUnchanged(DocumentSession<TileMap> s, ICommand<TileMap> command)
    {
        string before = TilesKit.Format.Write(s.Current), journal = JournalFile.Write(s.Journal);
        long revision = s.Revision; int count = s.History.UndoCount; bool redo = s.History.CanRedo;
        Assert.False(s.Execute(command));
        Assert.Equal(before, TilesKit.Format.Write(s.Current)); Assert.Equal(journal, JournalFile.Write(s.Journal));
        Assert.Equal(revision, s.Revision); Assert.Equal(count, s.History.UndoCount); Assert.Equal(redo, s.History.CanRedo);
        Assert.NotEmpty(s.LastIssues);
        Assert.All(s.LastIssues, i => { Assert.NotNull(i.ObjectId); Assert.NotNull(i.Path); });
    }
    [Fact] public void EmptyMapHasFixedRowMajorCellsAndMetreSize()
    {
        var m = TileMap.Empty(3, 2, .25);
        Assert.Equal(3, m.Width); Assert.Equal(2, m.Height); Assert.Equal(.25, m.CellSize);
        Assert.Equal(6, m.Cells.Length); Assert.All(m.Cells, Assert.Null); Assert.Empty(m.Kinds);
    }
    [Fact] public void ValidatorRejectsUnknownCellReferenceWithObjectAndIndex()
    {
        var m = TileMap.Empty() with { Cells = ImmutableArray.Create<string?>("missing", null, null, null) };
        var issue = Assert.Single(TilesKit.Validator.Validate(m));
        Assert.Equal("cell:0,0", issue.ObjectId); Assert.Equal("$.cells[0]", issue.Path);
    }
    [Fact] public void ValidatorRejectsWrongGridLength()
    {
        var m = TileMap.Empty() with { Cells = [] };
        Assert.Contains(TilesKit.Validator.Validate(m), i => i.Path == "$.cells");
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void CellSizeMustBePositiveAndFinite(double size)
    {
        var m = TileMap.Empty() with { CellSize = size };
        Assert.Contains(TilesKit.Validator.Validate(m), i => i.Path == "$.cell_size" && i.ObjectId == "map");
    }
    [Theory]
    [InlineData("Upper")] [InlineData("1bad")] [InlineData("bad-id")] [InlineData("")] [InlineData("bad\n")]
    public void KindIdsUseTheDocumentedPattern(string id)
    {
        RefusedUnchanged(Ready(), new AddKind(id, "Label"));
    }
    [Fact] public void DuplicateKindCannotCommit()
    {
        RefusedUnchanged(Ready(), new AddKind("grass", "Other"));
    }
    [Fact] public void RenameKeepsIdentityAndPaintedReferences()
    {
        var s = Ready(); s.Execute(new PaintCell(1, 1, "grass"));
        Assert.True(s.Execute(new RenameKind("grass", "Meadow")));
        Assert.Equal("grass", s.Current.Cells[4]); Assert.Equal("Meadow", s.Current.Kinds[0].Label);
        Assert.True(s.Undo()); Assert.Equal("Grass", s.Current.Kinds[0].Label);
    }
    [Fact] public void InvalidRenameRetainsRedoAsWellAsState()
    {
        var s = Ready(); s.Execute(new RenameKind("grass", "Meadow")); s.Undo();
        RefusedUnchanged(s, new RenameKind("grass", " "));
        Assert.True(s.Redo()); Assert.Equal("Meadow", s.Current.Kinds[0].Label);
    }
    [Fact] public void RemoveUsedKindReportsCountAndCoordinates()
    {
        var s = Ready(); s.Execute(new PaintCell(0, 0, "grass")); s.Execute(new PaintCell(2, 1, "grass"));
        RefusedUnchanged(s, new RemoveKind("grass"));
        string message = Assert.Single(s.LastIssues).Message;
        Assert.Contains("2 cell(s)", message); Assert.Contains("(0,0)", message); Assert.Contains("(2,1)", message);
        Assert.Contains("Clear or repaint", message);
    }
    [Fact] public void ClearThenRemoveUnusedKindSucceeds()
    {
        var s = Ready(); s.Execute(new PaintCell(1, 0, "grass"));
        Assert.True(s.Execute(new ClearCell(1, 0))); Assert.Null(s.Current.Cells[1]);
        Assert.True(s.Execute(new RemoveKind("grass"))); Assert.Empty(s.Current.Kinds);
    }
    [Fact] public void PaintRequiresAnExistingKind()
    {
        RefusedUnchanged(Ready(), new PaintCell(0, 0, "missing"));
    }
    [Theory]
    [InlineData(-1, 0)] [InlineData(0, -1)] [InlineData(3, 0)] [InlineData(0, 2)]
    public void PaintRejectsOutOfBoundsCells(int x, int y) => RefusedUnchanged(Ready(), new PaintCell(x, y, "grass"));
    [Fact] public void FillRectangleIsOneRevisionUndoAndJournalStep()
    {
        var s = Ready(); long revision = s.Revision; int journal = s.Journal.Count;
        Assert.True(s.Execute(new FillRectangle(1, 0, 2, 2, "grass")));
        Assert.Equal(new string?[] { null, "grass", "grass", null, "grass", "grass" }, s.Current.Cells.ToArray());
        Assert.Equal(revision + 1, s.Revision); Assert.Equal(journal + 1, s.Journal.Count);
        Assert.True(s.Undo()); Assert.All(s.Current.Cells, Assert.Null);
        Assert.True(s.Redo()); Assert.Equal("grass", s.Current.Cells[5]);
    }
    [Fact] public void FillMayClearCellsWithNull()
    {
        var s = Ready(); s.Execute(new FillRectangle(0, 0, 3, 2, "grass"));
        Assert.True(s.Execute(new FillRectangle(0, 0, 1, 2, null)));
        Assert.Null(s.Current.Cells[0]); Assert.Null(s.Current.Cells[3]); Assert.Equal("grass", s.Current.Cells[1]);
    }
    [Theory]
    [InlineData(0, 0, 0, 1)] [InlineData(-1, 0, 1, 1)] [InlineData(2, 0, 2, 1)]
    [InlineData(0, 0, int.MaxValue, 2)]
    public void InvalidRectangleCannotPartlyPaint(int x, int y, int width, int height) =>
        RefusedUnchanged(Ready(), new FillRectangle(x, y, width, height, "grass"));
    [Fact] public void ResizePreservesOverlapAndAddsEmptyCells()
    {
        var s = Ready(2, 2); s.Execute(new PaintCell(1, 1, "grass"));
        Assert.True(s.Execute(new ResizeMap(3, 3)));
        Assert.Equal(9, s.Current.Cells.Length); Assert.Equal("grass", s.Current.Cells[4]);
        Assert.Null(s.Current.Cells[8]); Assert.Equal(.5, s.Current.CellSize);
    }
    [Fact] public void ShrinkCropsButOneUndoRestoresTheOriginalGrid()
    {
        var s = Ready(); s.Execute(new PaintCell(2, 1, "grass"));
        string before = TilesKit.Format.Write(s.Current);
        Assert.True(s.Execute(new ResizeMap(1, 1))); Assert.Single(s.Current.Cells);
        Assert.True(s.Undo()); Assert.Equal(before, TilesKit.Format.Write(s.Current));
    }
    [Fact] public void OversizedResizeIsRefusedBeforeAllocation()
    {
        RefusedUnchanged(Ready(), new ResizeMap(int.MaxValue, int.MaxValue));
    }
    [Fact] public void HeldImmutableArrayCannotChangeTheSession()
    {
        var s = Ready(); var held = s.Current;
        var changed = held with { Cells = held.Cells.SetItem(0, "grass") };
        Assert.Equal("grass", changed.Cells[0]); Assert.Null(s.Current.Cells[0]);
    }
    [Fact] public void CanonicalMapHasVersionHeaderReadableUnicodeAndFinalLf()
    {
        var s = Ready(); s.Execute(new RenameKind("grass", "Prairie café"));
        string text = TilesKit.Format.Write(s.Current);
        Assert.Contains("\"kind\": \"tiles.map\"", text); Assert.Contains("\"version\": 1", text);
        Assert.Contains("Prairie café", text); Assert.EndsWith("\n", text); Assert.DoesNotContain("\r", text);
        Assert.Equal(text, TilesKit.Format.Write(TilesKit.Format.Read(text)));
    }
    [Fact] public void SaveLoadSaveIsByteIdentical()
    {
        using var scratch = new Scratch(); var s = Ready(); s.Execute(new PaintCell(1, 1, "grass"));
        string one = scratch.File("one.json"), two = scratch.File("two.json");
        s.Save(one); TilesKit.Open(one).Save(two);
        Assert.Equal(File.ReadAllBytes(one), File.ReadAllBytes(two));
    }
    [Fact] public void ReplayTwiceMatchesDocumentAndJournalBytes()
    {
        var s = Ready(); s.Execute(new FillRectangle(0, 0, 2, 2, "grass")); s.Undo(); s.Redo();
        var one = DocumentSession<TileMap>.Replay(TileMap.Empty(3, 2, .5), s.Journal, TilesKit.Format, TilesKit.Validator, TilesKit.Catalog());
        var two = DocumentSession<TileMap>.Replay(TileMap.Empty(3, 2, .5), s.Journal, TilesKit.Format, TilesKit.Validator, TilesKit.Catalog());
        Assert.Equal(TilesKit.Format.Write(s.Current), TilesKit.Format.Write(one.Current));
        Assert.Equal(Encoding.UTF8.GetBytes(TilesKit.Format.Write(one.Current)), Encoding.UTF8.GetBytes(TilesKit.Format.Write(two.Current)));
        Assert.Equal(JournalFile.Write(one.Journal), JournalFile.Write(two.Journal));
    }
    [Theory]
    [InlineData("extra", "$.extra")] [InlineData("future", "$.version")]
    [InlineData("kind", "$.kind")] [InlineData("cell", "$.cells[0]")]
    public void StrictFormatNamesTheFaultyPath(string mutation, string path)
    {
        var node = JsonNode.Parse(TilesKit.Format.Write(TileMap.Empty()))!.AsObject();
        if (mutation == "extra") node["extra"] = 1;
        if (mutation == "future") node["version"] = 999;
        if (mutation == "kind") node["kind"] = "other";
        if (mutation == "cell") node["cells"]![0] = 42;
        Assert.Equal(path, Assert.Throws<DocumentFormatException>(() => TilesKit.Format.Read(node.ToJsonString())).Path);
    }
    [Fact] public void StrictFormatRejectsDuplicateProperties()
    {
        string text = TilesKit.Format.Write(TileMap.Empty()).Replace("\"version\": 1", "\"version\": 1, \"version\": 1");
        Assert.Equal("$.version", Assert.Throws<DocumentFormatException>(() => TilesKit.Format.Read(text)).Path);
    }
    [Fact] public void CatalogueExposesAllSevenStableCommands()
    {
        var commands = TilesKit.Catalog().Commands;
        Assert.Equal(7, commands.Count); Assert.All(commands, c => { Assert.StartsWith("tiles.", c.Id); Assert.Equal(1, c.Version); });
        Assert.Single(TilesKit.Catalog().Search("Fill rectangle"));
    }
    [Fact] public void CliNewAndCheckGiveZeroAndNeverOverwrite()
    {
        using var scratch = new Scratch(); string path = scratch.File("map.json");
        Assert.Equal(0, Cli("new", path, "3", "2", "0.25").Code);
        Assert.Contains("3 x 2", Cli("check", path).Out);
        byte[] before = File.ReadAllBytes(path); Assert.Equal(2, Cli("new", path).Code);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
    [Fact] public void CliApplyAndReplayMatchExactly()
    {
        using var scratch = new Scratch();
        string basis = scratch.File("base.json"), work = scratch.File("work.json"), replay = scratch.File("replay.json"), journal = scratch.File("journal.json");
        Cli("new", basis); File.Copy(basis, work);
        File.WriteAllText(journal, Journal("tiles.add_kind", new { id = "stone", label = "Stone" }));
        Assert.Equal(0, Cli("apply", work, journal).Code); Assert.Equal(0, Cli("replay", basis, journal, replay).Code);
        Assert.Equal(File.ReadAllBytes(work), File.ReadAllBytes(replay));
    }
    [Fact] public void CliRefusedEditReturnsOneAndLeavesFileUnchanged()
    {
        using var scratch = new Scratch(); string path = scratch.File("map.json"), journal = scratch.File("journal.json");
        Cli("new", path); byte[] before = File.ReadAllBytes(path);
        File.WriteAllText(journal, Journal("tiles.paint", new { x = 0, y = 0, kindId = "absent" }));
        Assert.Equal(1, Cli("apply", path, journal).Code); Assert.Equal(before, File.ReadAllBytes(path));
    }
    [Fact] public void CliMalformedJournalAndWrongArgumentsReturnTwo()
    {
        using var scratch = new Scratch(); string path = scratch.File("map.json"), journal = scratch.File("journal.json");
        Cli("new", path); File.WriteAllText(journal, Journal("tiles.unknown", new { }));
        Assert.Equal(2, Cli("apply", path, journal).Code);
        Assert.Equal(2, Cli("check", scratch.File("missing.json")).Code); Assert.Equal(2, Cli("new").Code);
        Assert.Equal(2, Cli("new", scratch.File("bad.json"), "3", "2", "-1").Code);
    }
    [Fact] public void CliConflictReturnsThreeWithoutOverwritingExternalContent()
    {
        using var scratch = new Scratch(); string path = scratch.File("map.json"), journal = scratch.File("journal.json");
        Cli("new", path); File.WriteAllText(journal, Journal("tiles.add_kind", new { id = "stone", label = "Stone" }));
        string outside = File.ReadAllText(path) + " ";
        using var o = new StringWriter(); using var e = new StringWriter();
        Assert.Equal(3, Tiles.Program.Run(["apply", path, journal], o, e, () => File.WriteAllText(path, outside)));
        Assert.Equal(outside, File.ReadAllText(path)); Assert.Empty(o.ToString());
    }

    [Fact] public void BlankIdentifiersStillProduceUsableIssueLocations()
    {
        var invalid = TileMap.Empty() with { Kinds = ImmutableArray.Create(new TileKind("", "")) };
        var issues = TilesKit.Validator.Validate(invalid);
        Assert.NotEmpty(issues);
        Assert.All(issues, i => { Assert.False(string.IsNullOrWhiteSpace(i.ObjectId)); Assert.NotNull(i.Path); });
        var s = Ready();
        foreach (var command in new ICommand<TileMap>[] { new RenameKind("", "Name"), new RemoveKind(""), new FillRectangle(0, 0, 1, 1, "") })
        {
            Assert.False(s.Execute(command));
            Assert.All(s.LastIssues, i => { Assert.False(string.IsNullOrWhiteSpace(i.ObjectId)); Assert.NotNull(i.Path); });
        }
    }
}
