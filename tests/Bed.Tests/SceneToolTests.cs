using System.Text.Json;
using Xunit;

namespace Bed.Tests;

/// <summary>The <c>scene</c> command-line tool: a scene made, edited one command at a time, checked, and replayed.</summary>
public sealed class SceneToolTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("bed-scenetool-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string P(string name) => Path.Combine(dir, name);

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        return (SceneTool.Program.Run(args, output, errors), output.ToString(), errors.ToString());
    }

    private static string Add(string id, string? parent = null, string? asset = null) => JsonSerializer.Serialize(new
    {
        @object = new { id, asset, position = new[] { 1, 0, 2 }, parent },
    }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    [Fact]
    public void AScriptBuildsARoomOneCommandAtATime()
    {
        var room = P("room.json");
        Assert.Equal(0, Run("new", room).Code);
        Assert.Equal(2, Run("new", room).Code);   // won't overwrite
        Assert.Equal(0, Run("do", room, "scene.add", Add("room")).Code);
        Assert.Equal(0, Run("do", room, "scene.add", Add("table", "room", "props/table.glb")).Code);
        Assert.Equal(0, Run("do", room, "scene.move", """{"id":"table","position":[3,0,4.5]}""").Code);
        Assert.Equal(0, Run("do", room, "scene.rotate", """{"id":"table","rotation":[0,0.70710677,0,0.70710677]}""").Code);
        var tree = Run("tree", room);
        Assert.Equal(0, tree.Code);
        Assert.Contains("room  (1, 0, 2)", tree.Out);
        Assert.Contains("  table  (3, 0, 4.5)  props/table.glb", tree.Out);   // nested under the room
        Assert.Contains("2 object(s)", tree.Out);
    }

    [Fact]
    public void ARefusedStepWritesNothingAndSaysWhy()
    {
        var room = P("room.json");
        Run("new", room);
        Run("do", room, "scene.add", Add("room"));
        var before = File.ReadAllText(room);
        var r = Run("do", room, "scene.set_parent", """{"id":"room","parent":"attic"}""");
        Assert.Equal(1, r.Code);
        Assert.Contains("was refused; nothing was written", r.Err);
        Assert.Equal(before, File.ReadAllText(room));
    }

    [Theory]
    [InlineData("scene.teleport", "{}", "unknown command 'scene.teleport'")]
    [InlineData("scene.move", "{not json", "aren't JSON")]
    [InlineData("scene.move", """{"id":"room","position":[1,2]}""", "expected 3 numbers")]
    [InlineData("scene.move", """{"id":"room"}""", "can't read it")]
    public void BadStepsAreBadInput(string command, string json, string why)
    {
        var room = P("room.json");
        Run("new", room);
        var r = Run("do", room, command, json);
        Assert.Equal(2, r.Code);
        Assert.Contains(why, r.Err);
    }

    [Fact]
    public void CheckReportsMissingAssetsOnlyWhenAskedAndKeepsTheScene()
    {
        var room = P("room.json");
        Run("new", room);
        Run("do", room, "scene.add", Add("table", null, "props/table.glb"));
        Run("do", room, "scene.add", Add("lamp", null, "props/lamp.glb"));
        Directory.CreateDirectory(P("assets/props"));
        File.WriteAllText(P("assets/props/table.glb"), "");
        Assert.Equal(0, Run("check", room).Code);
        var r = Run("check", room, "--assets", P("assets"));
        Assert.Equal(1, r.Code);
        Assert.Contains("1 asset(s) missing", r.Err);
        Assert.Contains("'props/lamp.glb' not found; retain placeholder", r.Err);
        Assert.Contains("props/lamp.glb", File.ReadAllText(room));
    }

    [Fact]
    public void AJournalAppliesAndReplaysToTheSameScene()
    {
        var room = P("room.json");
        Run("new", room);
        Run("do", room, "scene.add", Add("room"));
        File.Copy(room, P("base.json"));
        var journal = """
            {"kind":"journal","version":1,"entries":[
              {"op":"do","command":"scene.add","version":1,"args":{"object":{"id":"crate","position":[0,0,0],"parent":"room"}}},
              {"op":"do","command":"scene.scale","version":1,"args":{"id":"crate","scale":[2,2,2]}},
              {"op":"do","command":"scene.duplicate","version":1,"args":{"ids":["crate"]}}
            ]}
            """;
        File.WriteAllText(P("steps.json"), journal);
        var applied = Run("apply", room, P("steps.json"));
        Assert.Equal(0, applied.Code);
        Assert.Contains("applied 3 step(s), 3 object(s)", applied.Out);
        Assert.Equal(0, Run("replay", P("base.json"), P("steps.json"), P("out.json")).Code);
        Assert.Equal(File.ReadAllText(room), File.ReadAllText(P("out.json")));
    }

    [Fact]
    public void AGameKindIsReadAndWrittenAndAnotherKindIsRefused()
    {
        var level = P("level.json");
        Assert.Equal(0, Run("new", level, "--kind", "level").Code);
        Assert.Contains("\"level\"", File.ReadAllText(level));
        Assert.Equal(0, Run("check", level, "--kind", "level").Code);
        Assert.Equal(2, Run("check", level).Code);   // it isn't a plain scene
        Assert.Equal(2, Run("check", level, "--kind").Code);
    }

    [Fact]
    public void CommandsAreListedAndSearched()
    {
        var all = Run("commands");
        Assert.Contains("scene.set_reference", all.Out);
        Assert.Contains("scene.rename", all.Out);
        var some = Run("commands", "parent");
        Assert.Contains("scene.set_parent", some.Out);
        Assert.DoesNotContain("scene.move", some.Out);
        Assert.Equal(2, Run().Code);
    }

    [Fact]
    public void AnOutsideEditDuringAStepIsAConflictAndNothingIsWritten()
    {
        var room = P("room.json");
        Run("new", room);
        var outside = new SceneFormat().Write(new Scene([SceneObject.At("theirs", null, System.Numerics.Vector3.Zero)]));
        var errors = new StringWriter();
        var code = SceneTool.Program.Run(["do", room, "scene.add", Add("mine")], new StringWriter(), errors,
            afterApply: () => File.WriteAllText(room, outside));
        Assert.Equal(3, code);
        Assert.Contains("changed on disk", errors.ToString());
        Assert.Equal(outside, File.ReadAllText(room));   // their edit survives; ours wasn't written over it
    }
}
