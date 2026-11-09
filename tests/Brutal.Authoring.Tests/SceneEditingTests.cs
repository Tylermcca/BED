using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The general scene in the editor: vectors that survive the journal, the transform, parent, asset and reference
/// commands, the nesting outline and focus, the inspector type, palette commands on the selection, and the window.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class SceneEditingTests
{
    private static readonly SceneFormat Format = new();

    // A room holding a table and a lamp; a crate outside it.
    private static Scene Room() => new([
        SceneObject.At("room", null, Vector3.Zero) with { Properties = Props(("name", "\"Kitchen\"")) },
        SceneObject.At("table", "props/table.glb", new Vector3(1, 0, 2)) with { Parent = "room" },
        SceneObject.At("lamp", "props/lamp.glb", new Vector3(1, 1, 2)) with { Parent = "table" },
        SceneObject.At("crate", "props/crate.glb", new Vector3(9, 0, 9)),
    ]);

    private static ImmutableSortedDictionary<string, JsonElement> Props(params (string Name, string Json)[] p) =>
        p.ToImmutableSortedDictionary(x => x.Name, x => JsonDocument.Parse(x.Json).RootElement.Clone(), StringComparer.Ordinal);

    private static DocumentSession<Scene> Session(Scene? s = null) => new(Format, new SceneValidator(), SceneKit.Catalog(), s ?? Room());

    private static EditorModel<Scene> Editor(DocumentSession<Scene>? s = null) => new(s ?? Session(), SceneEditing.Outline);

    [Fact]
    public void MovesRotationsAndScalesSurviveTheJournalAndReplayExactly()
    {
        var s = Session();
        var turn = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.5f, 0.25f, -0.125f));
        Assert.True(s.Execute(new MoveObject("crate", new Vector3(1.5f, -2.25f, 3.125f))));
        Assert.True(s.Execute(new RotateObject("crate", turn)));
        Assert.True(s.Execute(new ScaleObject("crate", new Vector3(2, 0.5f, 1))));
        var text = JournalFile.Write(s.Journal);
        var position = s.Journal[0].Args!.Value.GetProperty("position");
        Assert.Equal(JsonValueKind.Array, position.ValueKind);   // recorded as the scene file writes them, never {}
        Assert.Equal([1.5f, -2.25f, 3.125f], position.EnumerateArray().Select(x => x.GetSingle()));
        var replay = DocumentSession<Scene>.Replay(Room(), JournalFile.Read(text), Format, new SceneValidator(), SceneKit.Catalog());
        Assert.Equal(Format.Write(s.Current), Format.Write(replay.Current));
        Assert.Equal(new Vector3(1.5f, -2.25f, 3.125f), replay.Current.Find("crate")!.Position);
        Assert.Equal(turn, replay.Current.Find("crate")!.Rotation);
    }

    [Theory]
    [InlineData("[1,2]", "expected 3 numbers, got 2")]
    [InlineData("[1,2,3,4]", "got more")]
    [InlineData("{\"x\":1}", "expected an array")]
    [InlineData("[1,\"2\",3]", "element 1 must be a finite number")]
    public void ABadVectorInAJournalIsRefusedWithItsPath(string position, string why)
    {
        var entry = JournalEntry.Do("scene.move", 1, JsonDocument.Parse($"{{\"id\":\"crate\",\"position\":{position}}}").RootElement);
        var e = Assert.Throws<DocumentFormatException>(() => SceneKit.Catalog().FromEntry(entry));
        Assert.StartsWith("$.args", e.Path);
        Assert.Contains(why, e.Message);
    }

    [Fact]
    public void AnAddedObjectIsJournaledInTheSceneFilesShapeAndBadFieldsNameTheirPath()
    {
        var s = Session();
        var o = SceneObject.At("vase", "props/vase.glb", new Vector3(0, 1, 0)) with { Parent = "table", Properties = Props(("name", "\"Vase é\"")) };
        Assert.True(s.Execute(new AddObject(o)));
        var args = s.Journal[0].Args!.Value.GetProperty("object");
        Assert.Equal("Vase é", args.GetProperty("properties").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, args.GetProperty("scale").ValueKind);
        var replay = DocumentSession<Scene>.Replay(Room(), s.Journal, Format, new SceneValidator(), SceneKit.Catalog());
        Assert.Equal(Format.Write(s.Current), Format.Write(replay.Current));

        var c = SceneKit.Catalog();
        var brief = c.FromEntry(JournalEntry.Do("scene.add", 1, JsonDocument.Parse("""{"object":{"id":"cup"}}""").RootElement));
        Assert.Equal(SceneObject.At("cup", null, Vector3.Zero).Scale, Assert.IsType<AddObject>(brief).Object.Scale);   // optional fields default
        var e = Assert.Throws<DocumentFormatException>(() => c.FromEntry(JournalEntry.Do("scene.add", 1,
            JsonDocument.Parse("""{"object":{"id":"cup","scale":[1,1]}}""").RootElement)));
        Assert.Equal("$.args.object.scale", e.Path);
        var typo = Assert.Throws<DocumentFormatException>(() => c.FromEntry(JournalEntry.Do("scene.add", 1,
            JsonDocument.Parse("""{"object":{"id":"cup","typo":3}}""").RootElement)));
        Assert.Equal("$.args.object.typo", typo.Path);   // found by Codex in A017
    }

    [Fact]
    public void ANonFiniteMoveIsRefusedAndNothingIsRecorded()
    {
        var s = Session();
        Assert.False(s.Execute(new MoveObject("crate", new Vector3(float.NaN, 0, 0))));
        Assert.Empty(s.Journal);
        Assert.False(s.Dirty);
    }

    [Fact]
    public void RotationsMustBeUnitAndScalesNonZero()
    {
        var s = Session();
        Assert.False(s.Execute(new RotateObject("crate", new Quaternion(0, 0, 0, 2))));
        Assert.Contains("unit quaternion", s.LastIssues[0].Message);
        Assert.False(s.Execute(new ScaleObject("crate", new Vector3(1, 0, 1))));
        Assert.Contains("other than zero", s.LastIssues[0].Message);
        Assert.True(s.Execute(new ScaleObject("crate", new Vector3(-1, 1, 1))));   // mirroring is allowed
        Assert.False(s.Execute(new RotateObject("ghost", Quaternion.Identity)));
    }

    [Fact]
    public void ParentsMayMoveButNeverMakeACircleOrPointNowhere()
    {
        var s = Session();
        Assert.True(s.Execute(new SetParent("crate", "room")));
        Assert.Equal("room", s.Current.Find("crate")!.Parent);
        Assert.False(s.Execute(new SetParent("room", "lamp")));   // room > table > lamp > room
        Assert.Contains(s.LastIssues, i => i.Message.Contains("circle"));
        Assert.False(s.Execute(new SetParent("crate", "attic")));
        Assert.True(s.Execute(new SetParent("crate", null)));
        Assert.Null(s.Current.Find("crate")!.Parent);
    }

    [Fact]
    public void ReferencesAndAssetsCanBeSetAndCleared()
    {
        var s = Session();
        Assert.True(s.Execute(new SetReference("lamp", "lights", "table")));
        Assert.Equal("table", s.Current.Find("lamp")!.References["lights"]);
        Assert.False(s.Execute(new SetReference("lamp", "lights", "attic")));   // must resolve
        Assert.False(s.Execute(new RemoveObject("table")));                    // still referred to (and a parent)
        Assert.True(s.Execute(new SetReference("lamp", "lights", null)));
        Assert.Empty(s.Current.Find("lamp")!.References);
        Assert.True(s.Execute(new SetAsset("crate", "props/missing.glb")));     // a missing asset is a placeholder, not a refusal
        Assert.Single(SceneAssets.Check(s.Current, a => a != "props/missing.glb"));
        Assert.True(s.Execute(new SetAsset("crate", null)));
        Assert.Null(s.Current.Find("crate")!.Asset);
    }

    [Fact]
    public void TheOutlineNestsByParentAndFocusLocksTheRest()
    {
        var e = Editor();
        Assert.Equal("Kitchen", e.Outline.Label(e.Session.Current, "room"));   // its name property
        Assert.Equal("table", e.Outline.Label(e.Session.Current, "table"));    // otherwise its id
        Assert.True(e.FocusOn("table"));
        Assert.Equal(["room", "table"], e.Focus);
        Assert.True(e.InFocus("lamp"));
        Assert.False(e.InFocus("crate"));
        Assert.False(e.Execute(new MoveObject("crate", Vector3.One), "crate"));
        Assert.True(e.Execute(new MoveObject("lamp", Vector3.One), "lamp"));
    }

    [Fact]
    public void TheInspectorEditsOneAxisOfSeveralObjectsAsOneUndoStep()
    {
        var e = Editor();
        var inspector = new Inspector<Scene>(e).Add(SceneEditing.ObjectType());
        e.Select("table", "crate");
        var rows = inspector.Rows();
        Assert.True(rows.Single(r => r.Id == "position.x").Mixed);
        Assert.Equal(0.0, rows.Single(r => r.Id == "position.y").Value);
        Assert.False(rows.Single(r => r.Id == "id").Editable);
        Assert.True(inspector.Set("position.x", 4.0));
        Assert.Equal(new Vector3(4, 0, 2), e.Session.Current.Find("table")!.Position);   // its own Z kept
        Assert.Equal(new Vector3(4, 0, 9), e.Session.Current.Find("crate")!.Position);
        Assert.True(e.Session.Undo());
        Assert.Equal(new Vector3(1, 0, 2), e.Session.Current.Find("table")!.Position);
        Assert.Equal(new Vector3(9, 0, 9), e.Session.Current.Find("crate")!.Position);
    }

    [Fact]
    public void ScaleShowsAsAnOverrideAndResetsToOne()
    {
        var e = Editor();
        var inspector = new Inspector<Scene>(e).Add(SceneEditing.ObjectType());
        e.Select("crate");
        Assert.True(inspector.Set("scale.y", 3.0));
        Assert.True(inspector.Rows().Single(r => r.Id == "scale.y").Overridden);
        Assert.True(inspector.Reset("scale.y"));
        Assert.Equal(Vector3.One, e.Session.Current.Find("crate")!.Scale);
    }

    [Fact]
    public void TheParentAndAssetRowsTreatBlankAsNone()
    {
        var e = Editor();
        var inspector = new Inspector<Scene>(e).Add(SceneEditing.ObjectType());
        e.Select("table");
        Assert.True(inspector.Set("parent", "  "));
        Assert.Null(e.Session.Current.Find("table")!.Parent);
        Assert.True(inspector.Set("asset", ""));
        Assert.Null(e.Session.Current.Find("table")!.Asset);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(-120, 45, 10)]
    [InlineData(170, -60, -90)]
    public void RotationRowsReadBackTheAnglesTyped(double yaw, double pitch, double roll)
    {
        var e = Editor();
        var inspector = new Inspector<Scene>(e).Add(SceneEditing.ObjectType());
        e.Select("crate");
        Assert.True(inspector.Set("rotation.yaw", yaw));
        Assert.True(inspector.Set("rotation.pitch", pitch));
        Assert.True(inspector.Set("rotation.roll", roll));
        var a = SceneEditing.Angles(e.Session.Current.Find("crate")!.Rotation);
        Assert.Equal(yaw, a.Yaw, 2);
        Assert.Equal(pitch, a.Pitch, 2);
        Assert.Equal(roll, a.Roll, 2);
        Assert.Equal(1f, e.Session.Current.Find("crate")!.Rotation.Length(), 4);
    }

    [Fact]
    public void PaletteCommandsActOnTheSelection()
    {
        var e = Editor();
        var catalog = SceneKit.Catalog();
        CommandInfo C(string id) => catalog.Commands.Single(c => c.Id == id);
        e.Select("crate");
        Assert.Equal(new RemoveObject("crate"), SceneEditing.CommandFor(C("scene.remove"), e));
        e.Select("table", "lamp");
        Assert.Null(SceneEditing.CommandFor(C("scene.remove"), e));   // remove takes one object
        var dup = Assert.IsType<DuplicateGroup>(SceneEditing.CommandFor(C("scene.duplicate"), e));
        Assert.Equal(["table", "lamp"], dup.Ids);
        Assert.Null(SceneEditing.CommandFor(C("scene.move"), e));     // needs a position
    }

    [Fact]
    public void TheWindowDrawsASceneHeadlessAndRunsPaletteCommands()
    {
        var e = Editor();
        var w = new EditorWindow<Scene>(e, new Inspector<Scene>(e).Add(SceneEditing.ObjectType()), new Shortcuts(), SceneEditing.CommandFor);
        EditorWindowTests.Frames(w.Draw);
        e.Select("table", "lamp");
        EditorWindowTests.Frames(w.Draw);
        Assert.True(w.Run("scene.duplicate"), w.Status);
        Assert.NotNull(e.Session.Current.Find("copy_lamp"));
        Assert.Equal("copy_table", e.Session.Current.Find("copy_lamp")!.Parent);   // remapped inside the group
        e.FocusOn("room");
        EditorWindowTests.Frames(w.Draw);
    }
}
