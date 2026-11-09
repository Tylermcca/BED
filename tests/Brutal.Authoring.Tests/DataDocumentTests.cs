using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Any JSON data file plus its schema as an editor document: commands, validation, undo, and saving that keeps the
/// file's own style so an edit changes only its own lines. (Also checked read-only on a real game's 50 data files: all
/// round-trip byte for byte, and one-value edits change exactly one line; that check stays outside this repo.)</summary>
public sealed class DataDocumentTests
{
    private const string Schema = """
        {"type":"object","required":["kind","wares"],"additionalProperties":false,
         "properties":{"kind":{"const":"wares"},"notes":{"type":"string"},
           "wares":{"type":"array","items":{"type":"object","required":["id","price"],"additionalProperties":false,
             "properties":{"id":{"type":"string","pattern":"^[a-z_]+$"},"price":{"type":"number","minimum":0},
                           "sizes":{"type":"array","items":{"enum":["S","M","L"]}}}}}}}
        """;

    // Hand-formatted: a list kept on one line, a number spelled 1.0, a non-ASCII character.
    private const string File = """
        {
          "kind": "wares",
          "notes": "Prix en crédits",
          "wares": [
            {
              "id": "rope",
              "price": 1.0,
              "sizes": ["S", "M"]
            },
            {
              "id": "ore",
              "price": 12
            }
          ]
        }

        """;

    private static readonly JsonSchemas Schemas = new JsonSchemas().Add("wares.schema.json", Schema);

    private static DocumentSession<DataDocument> Session() => DataKit.Session(new DataFormat("wares").Read(Text()), Schemas, "wares.schema.json", "wares");

    private static string Text() => File.Replace("\r\n", "\n");

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IEnumerable<string> Changed(string a, string b) =>
        a.Split('\n').Zip(b.Split('\n')).Where(t => t.First != t.Second).Select(t => t.Second);

    [Fact]
    public void AFileRoundTripsByteForByteKeepingItsHandFormatting()
    {
        var f = new DataFormat();
        Assert.Equal(Text(), f.Write(f.Read(Text())));
    }

    [Fact]
    public void ChangingOneValueChangesOnlyItsLine()
    {
        var s = Session();
        Assert.True(s.Execute(new SetValue("/wares/1/price", J("15"))));
        Assert.Equal(["      \"price\": 15"], Changed(Text(), s.Format.Write(s.Current)));
    }

    [Fact]
    public void AnEditInsideAOneLineListKeepsItOnOneLine()
    {
        var s = Session();
        Assert.True(s.Execute(new SetValue("/wares/0/sizes/1", J("\"L\""))));
        Assert.Equal(["      \"sizes\": [\"S\", \"L\"]"], Changed(Text(), s.Format.Write(s.Current)));
    }

    [Fact]
    public void InsertingAnItemLeavesTheOthersTextAlone()
    {
        var s = Session();
        Assert.True(s.Execute(new InsertItem("/wares", 0, J("""{"id":"fish","price":3}"""))));
        var after = s.Format.Write(s.Current);
        Assert.Contains("\"price\": 1.0", after);           // the old item's spelling survives its move
        Assert.Contains("\"sizes\": [\"S\", \"M\"]", after);
        Assert.Contains("Prix en crédits", after);           // characters as themselves, not \u escapes
        Assert.Equal("fish", s.Current.At("/wares/0/id")!.Value.GetString());
    }

    [Fact]
    public void TheSchemaRefusesBadEditsWithTheirPathAndNothingChanges()
    {
        var s = Session();
        Assert.False(s.Execute(new SetValue("/wares/0/price", J("-1"))));
        Assert.Contains(s.LastIssues, i => i.Path == "$.wares[0].price" && i.Message == "must be at least 0");
        Assert.False(s.Execute(new SetValue("/wares/0/colour", J("\"red\""))));
        Assert.Contains(s.LastIssues, i => i.Message == "'colour' isn't a known field here");
        Assert.False(s.Execute(new InsertItem("/wares", 2, J("""{"id":"Bad Id","price":1}"""))));
        Assert.False(s.Dirty);
        Assert.Empty(s.Journal);
    }

    [Fact]
    public void RemoveMoveUndoAndRedoWork()
    {
        var s = Session();
        Assert.True(s.Execute(new MoveItem("/wares", 0, 1)));
        Assert.Equal("ore", s.Current.At("/wares/0/id")!.Value.GetString());
        Assert.True(s.Execute(new RemoveItem("/wares/1")));
        Assert.Equal(1, s.Current.At("/wares")!.Value.GetArrayLength());
        Assert.True(s.Execute(new SetValue("/notes", null)));   // removes an optional field
        Assert.Null(s.Current.At("/notes"));
        Assert.True(s.Undo() && s.Undo() && s.Undo());
        Assert.Equal(Text(), s.Format.Write(s.Current));         // back to the very same text
        Assert.True(s.Redo());
        Assert.Equal("ore", s.Current.At("/wares/0/id")!.Value.GetString());
    }

    [Fact]
    public void ImpossibleEditsAreRefusedSayingWhy()
    {
        var s = Session();
        Assert.False(s.Execute(new SetValue("/nowhere/x", J("1"))));
        Assert.Contains("there's nothing at $.nowhere", s.LastIssues[0].Message);
        Assert.False(s.Execute(new SetValue("/wares/9", J("1"))));
        Assert.False(s.Execute(new SetValue("/wares/0", null)));
        Assert.Contains("use RemoveItem", s.LastIssues[0].Message);
        Assert.False(s.Execute(new InsertItem("/kind", 0, J("1"))));
        Assert.False(s.Execute(new RemoveItem("/wares/5")));
        Assert.False(s.Execute(new MoveItem("/wares", 0, 7)));
    }

    [Fact]
    public void AJournalOfDataEditsReplaysToTheSameText()
    {
        var s = Session();
        Assert.True(s.Execute(new SetValue("/wares/1/price", J("20"))));
        Assert.True(s.Execute(new InsertItem("/wares", 2, J("""{"id":"salt","price":2}"""))));
        var replay = DocumentSession<DataDocument>.Replay(new DataFormat().Read(Text()), JournalFile.Read(JournalFile.Write(s.Journal)),
            new DataFormat(), new DataValidator(Schemas, "wares.schema.json"), DataKit.Catalog());
        Assert.Equal(s.Format.Write(s.Current), replay.Format.Write(replay.Current));
    }

    [Theory]
    [InlineData("/a/b~1c/0", new[] { "a", "b/c", "0" }, "$.a['b/c'][0]")]
    [InlineData("", new string[0], "$")]
    public void PointersSplitJoinAndBecomePaths(string pointer, string[] parts, string path)
    {
        Assert.Equal(parts, Pointer.Split(pointer));
        Assert.Equal(pointer, Pointer.Join(parts));
        Assert.Equal(path, Pointer.ToPath(pointer));
    }

    [Fact]
    public void A_long_sessions_journal_stays_bounded_and_still_replays_exactly_undo_included()
    {
        // A204: every step was kept for ever; past its limit the journal now starts again from the session as it stands.
        var s = Session();
        s.JournalLimit = 5;
        for (var i = 0; i < 40; i++)
        {
            Assert.True(s.Execute(new SetValue("/wares/1/price", J((10 + i).ToString()))));
            if (i % 3 == 0) Assert.True(s.Undo());
            if (i % 6 == 0) Assert.True(s.Redo());
            Assert.InRange(s.Journal.Count, 0, 5);
        }
        var replay = DocumentSession<DataDocument>.ReplayFrom(s.JournalBase, s.Journal, s.Format, s.Validator, s.Catalog);
        Assert.Equal(s.Format.Write(s.Current), replay.Format.Write(replay.Current));
        // The base carries the undo steps, so an undo after replay lands where the session's own does.
        Assert.Equal(s.History.UndoCount, replay.History.UndoCount);
        Assert.True(s.Undo());
        Assert.True(replay.Undo());
        Assert.Equal(s.Format.Write(s.Current), replay.Format.Write(replay.Current));
    }

    [Fact]
    public void A_journal_past_its_byte_budget_moves_its_base_forward()
    {
        var s = Session();
        s.JournalBudget = 200;
        var big = "\"" + new string('x', 150) + "\"";
        Assert.True(s.Execute(new SetValue("/notes", J(big))));
        Assert.True(s.Execute(new SetValue("/notes", J("\"short\""))));
        Assert.True(s.Journal.Count <= 1);
        var replay = DocumentSession<DataDocument>.ReplayFrom(s.JournalBase, s.Journal, s.Format, s.Validator, s.Catalog);
        Assert.Equal(s.Format.Write(s.Current), replay.Format.Write(replay.Current));
    }
}
