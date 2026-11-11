using Toy;
using Xunit;

namespace Bed.Tests;

/// <summary>The `toy` command line, run in-process: the headless half of the first slice (goal 2), with its exit codes.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("bed-cli-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string P(string name) => Path.Combine(dir, name);

    private static (int Code, string Out, string Err) Toy(params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = Program.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    private const string Journal = """
        {"kind": "journal", "version": 1, "entries": [
          {"op": "do", "command": "toy.add", "version": 1, "args": {"id": null, "label": "Lamp", "x": 1, "y": 2}},
          {"op": "do", "command": "toy.add", "version": 1, "args": {"id": "chair", "label": "Chair", "x": 0, "y": 0}},
          {"op": "do", "command": "toy.move", "version": 1, "args": {"id": "item_1", "x": 4, "y": 4}},
          {"op": "undo"},
          {"op": "redo"},
          {"op": "batch", "batch": [
            {"op": "do", "command": "toy.rename", "version": 1, "args": {"id": "chair", "label": "Big chair"}},
            {"op": "do", "command": "toy.move", "version": 1, "args": {"id": "chair", "x": 9, "y": 9}}
          ]}
        ]}
        """;

    [Fact]
    public void NewApplyCheckAndReplayRoundTrip()
    {
        File.WriteAllText(P("j.json"), Journal);
        Assert.Equal(0, Toy("new", P("board.json")).Code);
        var applied = Toy("apply", P("board.json"), P("j.json"));
        Assert.Equal(0, applied.Code);
        Assert.Contains("2 item(s)", applied.Out);
        Assert.Equal(0, Toy("check", P("board.json")).Code);

        Assert.Equal(0, Toy("new", P("base.json")).Code);
        Assert.Equal(0, Toy("replay", P("base.json"), P("j.json"), P("r1.json")).Code);
        Assert.Equal(0, Toy("replay", P("base.json"), P("j.json"), P("r2.json")).Code);
        Assert.Equal(File.ReadAllBytes(P("board.json")), File.ReadAllBytes(P("r1.json")));
        Assert.Equal(File.ReadAllBytes(P("r1.json")), File.ReadAllBytes(P("r2.json")));
        Assert.Contains("\"Big chair\"", File.ReadAllText(P("r1.json")));
    }

    [Fact]
    public void ARefusedJournalWritesNothingAndSaysWhere()
    {
        Toy("new", P("board.json"));
        var before = File.ReadAllBytes(P("board.json"));
        File.WriteAllText(P("j.json"), """
            {"kind": "journal", "version": 1, "entries": [
              {"op": "do", "command": "toy.add", "version": 1, "args": {"id": "a", "label": "A", "x": 0, "y": 0}},
              {"op": "do", "command": "toy.rename", "version": 1, "args": {"id": "a", "label": ""}}
            ]}
            """);
        var r = Toy("apply", P("board.json"), P("j.json"));
        Assert.Equal(Program.Refused, r.Code);
        Assert.Contains("journal entry 1", r.Err);
        Assert.Contains("[a $.items[0].label]", r.Err);   // which object, which field
        Assert.Equal(before, File.ReadAllBytes(P("board.json")));
    }

    [Fact]
    public void BadInputIsCodeTwo()
    {
        Assert.Equal(Program.BadInput, Toy().Code);
        Assert.Equal(Program.BadInput, Toy("fly").Code);
        Assert.Equal(Program.BadInput, Toy("check", P("missing.json")).Code);
        File.WriteAllText(P("bad.json"), "{");
        var r = Toy("check", P("bad.json"));
        Assert.Equal(Program.BadInput, r.Code);
        Assert.Contains("can't read it", r.Err);
        Toy("new", P("board.json"));
        Assert.Equal(Program.BadInput, Toy("new", P("board.json")).Code);   // won't overwrite
    }

    [Fact]
    public void AnInvalidBoardIsRefused()
    {
        File.WriteAllText(P("board.json"), """{"kind":"toy.board","version":1,"items":[{"id":"A!","label":"x","x":0,"y":0}]}""");
        var r = Toy("check", P("board.json"));
        Assert.Equal(Program.Refused, r.Code);
        Assert.Contains("must start with a letter", r.Err);
    }

    [Fact]
    public void CommandsAreListedAndSearchable()
    {
        Assert.Contains("toy.move", Toy("commands").Out);
        var found = Toy("commands", "rename").Out;
        Assert.Contains("toy.rename", found);
        Assert.DoesNotContain("toy.move", found);
    }

    [Fact]
    public void AnAddWithNoIdPicksOneAndRecordsIt()
    {
        Toy("new", P("board.json"));
        File.WriteAllText(P("j.json"), """{"kind":"journal","version":1,"entries":[{"op":"do","command":"toy.add","version":1,"args":{"label":"Lamp","x":0,"y":0}}]}""");
        Assert.Equal(0, Toy("apply", P("board.json"), P("j.json")).Code);
        Assert.Contains("\"item_1\"", File.ReadAllText(P("board.json")));
    }

    [Theory]
    [InlineData("""{"op":"do","command":"toy.fly","version":1,"args":{}}""")]
    [InlineData("""{"op":"do","command":"toy.move","version":2,"args":{"id":"a","x":0,"y":0}}""")]
    [InlineData("""{"op":"do","command":"toy.move","version":1,"args":{"id":"a","x":"far","y":0}}""")]
    public void AJournalEntryThatCantBeReadIsBadInput(string entry)
    {
        Toy("new", P("board.json"));
        File.WriteAllText(P("j.json"), "{\"kind\":\"journal\",\"version\":1,\"entries\":[" + entry + "]}");
        var r = Toy("apply", P("board.json"), P("j.json"));
        Assert.Equal(Program.BadInput, r.Code);
        Assert.Contains("$.entries[0]", r.Err);
    }

    [Fact]
    public void AFileChangedDuringApplyIsAConflictAndNothingIsWritten()
    {
        Toy("new", P("board.json"));
        File.WriteAllText(P("j.json"), Journal);
        const string outside = "{\"kind\":\"toy.board\",\"version\":1,\"items\":[]}  ";
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = Program.Run(["apply", P("board.json"), P("j.json")], o, e, afterApply: () => File.WriteAllText(P("board.json"), outside));
        Assert.Equal(Program.Conflict, code);
        Assert.Equal(outside, File.ReadAllText(P("board.json")));   // the outside edit survives
    }
}
