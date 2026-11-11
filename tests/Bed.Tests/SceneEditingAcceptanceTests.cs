using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Bed;
using Bed.Editor;
using Xunit;
using SceneCli = SceneTool.Program;

namespace Bed.Tests;

// INVENTED independent fixtures/oracles. Promises are BORROWED from the cited public docs.
// Written before reading SceneEditingTests.cs or SceneToolTests.cs; no implementation oracle.
public sealed class SceneEditingAcceptanceTests
{
    private static readonly SceneFormat Format = new();
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static Scene SceneOf(params SceneObject[] objects) => new(objects.ToImmutableArray());
    private static SceneObject Obj(string id, string? parent = null) => SceneObject.At(id, null, Vector3.Zero) with { Parent = parent };
    private static DocumentSession<Scene> Session(Scene? scene = null) => new(Format, new SceneValidator(), SceneKit.Catalog(), scene ?? SceneOf(Obj("item"), Obj("group")));
    private static (EditorModel<Scene> Editor, Inspector<Scene> Inspector) Edit(Scene? scene = null, params string[] selected)
    {
        var e = new EditorModel<Scene>(Session(scene), SceneEditing.Outline);
        e.Select(selected.Length == 0 ? ["item"] : selected);
        return (e, new Inspector<Scene>(e).Add(SceneEditing.ObjectType()));
    }
    private static string Key(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static InspectorRow Row(Inspector<Scene> i, string concept) => Assert.Single(i.Rows(), r => Key(r.Id) == Key(concept) || Key(r.Label) == Key(concept));
    private static double Number(InspectorRow r) => Convert.ToDouble(r.Value, CultureInfo.InvariantCulture);
    private static void Near(double expected, double actual, double tolerance = 0.001) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    private static Quaternion Rotation(double yaw, double pitch, double roll) => Quaternion.CreateFromYawPitchRoll((float)(yaw * Math.PI / 180), (float)(pitch * Math.PI / 180), (float)(roll * Math.PI / 180));
    private static void Unchanged(DocumentSession<Scene> s, Scene before, string journal, long revision)
    {
        Assert.Equal(Format.Write(before), Format.Write(s.Current));
        Assert.Equal(journal, JournalFile.Write(s.Journal));
        Assert.Equal(revision, s.Revision);
    }
    private static CommandInfo Info(DocumentSession<Scene> s, string id) => Assert.Single(s.Catalog.Commands, c => c.Id == id);

    // VectorJson.cs:8-12: every vector type, strict finite array count, including command arguments.
    public sealed record Probe(string Text);
    public sealed record VectorArgument<T>(T Value) : ICommand<Probe>
    {
        public CommandResult<Probe> Apply(Probe d) => CommandResult<Probe>.Done(new(Describe(Value)));
    }
    public sealed class ProbeFormat : IDocumentFormat<Probe>
    {
        public string Kind => "acceptance.vector";
        public int Version => 1;
        public Probe Read(string text) => JsonSerializer.Deserialize<Probe>(text)!;
        public string Write(Probe document) => JsonSerializer.Serialize(document);
    }
    public sealed class ProbeValidator : IValidator<Probe>
    {
        public IReadOnlyList<Issue> Validate(Probe d) => [];
    }
    private static string Describe<T>(T value) => value switch
    {
        Vector2 v => FormattableString.Invariant($"{v.X},{v.Y}"),
        Vector3 v => FormattableString.Invariant($"{v.X},{v.Y},{v.Z}"),
        Vector4 v => FormattableString.Invariant($"{v.X},{v.Y},{v.Z},{v.W}"),
        Quaternion v => FormattableString.Invariant($"{v.X},{v.Y},{v.Z},{v.W}"),
        _ => throw new InvalidOperationException("unknown test vector")
    };
    private static void VectorRoundTrip<T>(T value, string array, string expected)
    {
        var cat = new CommandCatalog<Probe>().Register<VectorArgument<T>>("probe.vector", 1, "Vector");
        var f = new ProbeFormat();
        var v = new ProbeValidator();
        var s = new DocumentSession<Probe>(f, v, cat, new("base"));
        Assert.True(s.Execute(new VectorArgument<T>(value)));
        var entry = Assert.Single(s.Journal);
        var actual = entry.Args!.Value.GetProperty("value");
        Assert.Equal(JsonValueKind.Array, actual.ValueKind);
        Assert.Equal(Json(array).EnumerateArray().Select(x => x.GetDouble()), actual.EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(value, Assert.IsType<VectorArgument<T>>(cat.FromEntry(entry)).Value);
        var replay = DocumentSession<Probe>.Replay(new("base"), JournalFile.Read(JournalFile.Write(s.Journal)), f, v, cat);
        Assert.Equal(expected, replay.Current.Text);
        Assert.Equal(expected, s.Current.Text);
    }
    [Fact] public void Vector2JournalRoundTrip() => VectorRoundTrip(new Vector2(1.25f, -2.5f), "[1.25,-2.5]", "1.25,-2.5");
    [Fact] public void Vector3JournalRoundTrip() => VectorRoundTrip(new Vector3(1.25f, -2.5f, 4), "[1.25,-2.5,4]", "1.25,-2.5,4");
    [Fact] public void Vector4JournalRoundTrip() => VectorRoundTrip(new Vector4(1.25f, -2.5f, 4, 8), "[1.25,-2.5,4,8]", "1.25,-2.5,4,8");
    [Fact] public void QuaternionJournalRoundTrip() => VectorRoundTrip(new Quaternion(.5f, -.25f, 0, 1), "[0.5,-0.25,0,1]", "0.5,-0.25,0,1");

    private static void BadVector<T>(string array)
    {
        var cat = new CommandCatalog<Probe>().Register<VectorArgument<T>>("probe.vector", 1, "Vector");
        var entry = JournalEntry.Do("probe.vector", 1, Json("{\"value\":" + array + "}"));
        var error = Assert.Throws<DocumentFormatException>(() => cat.FromEntry(entry));
        Assert.StartsWith("$.args.value", error.Path);
    }
    [Theory]
    [InlineData("[]")] [InlineData("[1]")] [InlineData("[1,2,3]")] [InlineData("[1,\"two\"]")] [InlineData("[1,1e1000]")]
    public void Vector2BadArraysNameArgumentPath(string a) => BadVector<Vector2>(a);
    [Theory]
    [InlineData("[1,2]")] [InlineData("[1,2,3,4]")] [InlineData("[1,null,3]")] [InlineData("[1,2,1e1000]")]
    public void Vector3BadArraysNameArgumentPath(string a) => BadVector<Vector3>(a);
    [Theory]
    [InlineData("[1,2,3]")] [InlineData("[1,2,3,4,5]")] [InlineData("[1,2,true,4]")] [InlineData("[1,2,3,1e1000]")]
    public void Vector4BadArraysNameArgumentPath(string a) => BadVector<Vector4>(a);
    [Theory]
    [InlineData("[1,2,3]")] [InlineData("[1,2,3,4,5]")] [InlineData("[1,2,\"three\",4]")] [InlineData("[1,2,3,1e1000]")]
    public void QuaternionBadArraysNameArgumentPath(string a) => BadVector<Quaternion>(a);

    private static void NonFinite<T>(T value)
    {
        var cat = new CommandCatalog<Probe>().Register<VectorArgument<T>>("probe.vector", 1, "Vector");
        var s = new DocumentSession<Probe>(new ProbeFormat(), new ProbeValidator(), cat, new("base"));
        Assert.False(s.Execute(new VectorArgument<T>(value)));
        Assert.Equal("base", s.Current.Text);
        Assert.Empty(s.Journal);
        Assert.Equal(0, s.Revision);
        Assert.NotEmpty(s.LastIssues);
    }
    [Fact] public void Vector2NonFiniteWritingIsRefused() => NonFinite(new Vector2(float.NaN, 1));
    [Fact] public void Vector3NonFiniteWritingIsRefused() => NonFinite(new Vector3(1, float.PositiveInfinity, 3));
    [Fact] public void Vector4NonFiniteWritingIsRefused() => NonFinite(new Vector4(1, 2, 3, float.NegativeInfinity));
    [Fact] public void QuaternionNonFiniteWritingIsRefused() => NonFinite(new Quaternion(0, float.NaN, 0, 1));

    // Scene.cs:30-31,63-66: journal objects use the file's strict shape and defaults.
    [Fact]
    public void AddArgumentsUseOptionalSceneObjectShape()
    {
        var cat = SceneKit.Catalog();
        var add = Assert.IsType<AddObject>(cat.FromEntry(JournalEntry.Do("scene.add", 1, Json("""{"object":{"id":"cup"}}"""))));
        Assert.Equal(Format.Write(SceneOf(Obj("cup"))), Format.Write(SceneOf(add.Object)));
        var s = Session(Scene.Empty);
        Assert.True(s.Execute(add));
        var arg = Assert.Single(s.Journal).Args!.Value.GetProperty("object");
        Assert.Equal(JsonValueKind.Array, arg.GetProperty("position").ValueKind);
        Assert.Equal(JsonValueKind.Array, arg.GetProperty("rotation").ValueKind);
        Assert.Equal(JsonValueKind.Array, arg.GetProperty("scale").ValueKind);
        var replay = DocumentSession<Scene>.Replay(Scene.Empty, JournalFile.Read(JournalFile.Write(s.Journal)), Format, new SceneValidator(), cat);
        Assert.Equal(Format.Write(s.Current), Format.Write(replay.Current));
    }
    [Fact]
    public void AddArgumentsRejectUnknownObjectFieldAtItsPath()
    {
        var ex = Assert.Throws<DocumentFormatException>(() => SceneKit.Catalog().FromEntry(
            JournalEntry.Do("scene.add", 1, Json("""{"object":{"id":"cup","typo":3}}"""))));
        Assert.Equal("$.args.object.typo", ex.Path);
    }
    [Theory]
    [InlineData("move", "position")] [InlineData("rotate", "rotation")] [InlineData("scale", "scale")]
    public void SceneVectorCommandsSurviveJournalAndReplay(string operation, string field)
    {
        var s = Session();
        ICommand<Scene> c = operation switch
        {
            "move" => new MoveObject("item", new(1.25f, -2.5f, 3)),
            "rotate" => new RotateObject("item", new(0, 0, 1, 0)),
            _ => new ScaleObject("item", new(-2, 3, .5f))
        };
        var before = s.Current;
        Assert.True(s.Execute(c));
        Assert.Equal(JsonValueKind.Array, Assert.Single(s.Journal).Args!.Value.GetProperty(field).ValueKind);
        var replay = DocumentSession<Scene>.Replay(before, JournalFile.Read(JournalFile.Write(s.Journal)), Format, new SceneValidator(), s.Catalog);
        Assert.Equal(Format.Write(s.Current), Format.Write(replay.Current));
        var o = replay.Current.Find("item")!;
        if (operation == "move") Assert.Equal(new Vector3(1.25f, -2.5f, 3), o.Position);
        else if (operation == "rotate") Assert.Equal(new Quaternion(0, 0, 1, 0), o.Rotation);
        else Assert.Equal(new Vector3(-2, 3, .5f), o.Scale);
    }

    // Scene.cs:253-300: rotation/scale/parent/asset/reference promises.
    [Fact]
    public void UnitRotationIsExactAndUndoable()
    {
        var s = Session();
        var q = Rotation(25, 15, -35);
        Assert.True(s.Execute(new RotateObject("item", q)));
        Assert.Equal(q, s.Current.Find("item")!.Rotation);
        Assert.True(s.Undo()); Assert.Equal(Quaternion.Identity, s.Current.Find("item")!.Rotation);
        Assert.True(s.Redo()); Assert.Equal(q, s.Current.Find("item")!.Rotation);
    }
    // Un-skipped by Claude on accept: the doc now states the tolerance (length within 0.001 of 1), so a rotation just
    // inside it is kept exactly as given and one just outside is refused, never normalised.
    [Fact]
    public void NearlyUnitButNotUnitRotationIsRefusedWithoutNormalising()
    {
        var s = Session(); var before = s.Current;
        Assert.False(s.Execute(new RotateObject("item", new(0, 0, 0, 1.002f))));
        Unchanged(s, before, JournalFile.Write([]), 0);
        Assert.True(s.Execute(new RotateObject("item", new(0, 0, 0, 1.000001f))));
        Assert.Equal(new Quaternion(0, 0, 0, 1.000001f), s.Current.Find("item")!.Rotation);   // kept as given
    }
    [Theory] [InlineData(0)] [InlineData(2)]
    public void NonUnitRotationIsRefused(int w)
    {
        var s = Session(); var before = s.Current;
        Assert.False(s.Execute(new RotateObject("item", new(0, 0, 0, w))));
        Unchanged(s, before, JournalFile.Write([]), 0);
    }
    [Fact]
    public void NegativeScaleMirrorsWithoutNormalising()
    {
        var s = Session();
        Assert.True(s.Execute(new ScaleObject("item", new(-2, 3, .5f))));
        Assert.Equal(new Vector3(-2, 3, .5f), s.Current.Find("item")!.Scale);
    }
    [Theory] [InlineData(0, 1, 1)] [InlineData(1, 0, 1)] [InlineData(1, 1, 0)]
    public void EachZeroScaleAxisIsRefused(float x, float y, float z)
    {
        var s = Session(); var before = s.Current;
        Assert.False(s.Execute(new ScaleObject("item", new(x, y, z))));
        Unchanged(s, before, JournalFile.Write([]), 0);
    }
    [Fact]
    public void SelfParentIsRefusedWhole()
    {
        var s = Session(); var before = s.Current;
        Assert.False(s.Execute(new SetParent("item", "item")));
        Unchanged(s, before, JournalFile.Write([]), 0);
    }
    [Fact]
    public void MissingParentIsRefusedWhole()
    {
        var s = Session(); var before = s.Current;
        Assert.False(s.Execute(new SetParent("item", "absent")));
        Unchanged(s, before, JournalFile.Write([]), 0);
    }
    [Fact]
    public void ParentCycleIsRefusedWhole()
    {
        var s = Session(SceneOf(Obj("grand"), Obj("parent", "grand"), Obj("child", "parent")));
        var before = s.Current;
        Assert.False(s.Execute(new SetParent("grand", "child")));
        Unchanged(s, before, JournalFile.Write([]), 0);
    }
    [Fact]
    public void NullParentReturnsObjectToTopLevel()
    {
        var s = Session(SceneOf(Obj("group"), Obj("item", "group")));
        Assert.True(s.Execute(new SetParent("item", null)));
        Assert.Null(s.Current.Find("item")!.Parent);
        Assert.True(s.Undo()); Assert.Equal("group", s.Current.Find("item")!.Parent);
    }
    [Fact]
    public void MissingAssetIsKeptAndNullMakesAnEmpty()
    {
        var s = Session();
        Assert.True(s.Execute(new SetAsset("item", "models/unavailable.glb")));
        Assert.Equal("models/unavailable.glb", s.Current.Find("item")!.Asset);
        Assert.Single(SceneAssets.Check(s.Current, _ => false));
        Assert.True(s.Execute(new SetAsset("item", null)));
        Assert.Null(s.Current.Find("item")!.Asset);
    }
    [Fact]
    public void ReferenceSetsRemovesAndReplays()
    {
        var s = Session(); var before = s.Current;
        Assert.True(s.Execute(new SetReference("item", "look", "group")));
        Assert.Equal("group", s.Current.Find("item")!.References["look"]);
        Assert.True(s.Execute(new SetReference("item", "look", null)));
        Assert.Empty(s.Current.Find("item")!.References);
        Assert.True(s.Undo());
        var replay = DocumentSession<Scene>.Replay(before, JournalFile.Read(JournalFile.Write(s.Journal)), Format, new SceneValidator(), s.Catalog);
        Assert.Equal("group", replay.Current.Find("item")!.References["look"]);
    }
    [Fact]
    public void MissingReferenceTargetIsRefusedWhole()
    {
        var s = Session(); var before = s.Current;
        Assert.False(s.Execute(new SetReference("item", "look", "absent")));
        Unchanged(s, before, JournalFile.Write([]), 0);
    }

    // SceneEditing.cs:13-22; Inspector.cs:104-106,133-151.
    [Fact]
    public void OutlineKeepsDocumentOrderNestsAndLabelsNames()
    {
        var scene = SceneOf(Obj("child", "parent") with { Properties = Obj("child").Properties.Add("name", Json("\"Étoile 雨\"")) },
            Obj("parent"), Obj("fallback") with { Properties = Obj("fallback").Properties.Add("name", Json("17")) });
        var o = SceneEditing.Outline;
        Assert.Equal(new[] { "child", "parent", "fallback" }, o.ObjectIds(scene));
        Assert.Equal("Étoile 雨", o.Label(scene, "child"));
        Assert.Equal("fallback", o.Label(scene, "fallback"));
        Assert.Equal("parent", o.Parent(scene, "child"));
        Assert.Null(o.Parent(scene, "parent")); Assert.Null(o.Label(scene, "absent"));
    }
    [Fact]
    public void OutlineAllowsFocusOnAnyObjectWithChildren()
    {
        var (e, _) = Edit(SceneOf(Obj("outer"), Obj("inner", "outer"), Obj("item", "inner"), Obj("outside")));
        Assert.True(e.FocusOn("inner"));
        Assert.Equal(new[] { "outer", "inner" }, e.Focus);
        Assert.True(e.Editable("item")); Assert.False(e.Editable("outside"));
        Assert.False(e.Session.Dirty); Assert.Empty(e.Session.Journal);
    }
    [Fact]
    public void ObjectInspectorHasIdAndEveryTransformAxis()
    {
        var (_, i) = Edit();
        Assert.Equal("scene.object", i.SelectedType!.Id);
        Assert.False(Row(i, "id").Editable);
        foreach (var concept in new[] { "asset", "parent", "positionx", "positiony", "positionz", "yaw", "pitch", "roll", "scalex", "scaley", "scalez" })
            Assert.True(Row(i, concept).Editable, concept);
    }
    [Theory] [InlineData("yaw", 42)] [InlineData("pitch", -23)] [InlineData("roll", 18)]
    public void EditingOneRotationAngleKeepsTheOtherTwo(string angle, double value)
    {
        var (e, i) = Edit(SceneOf(Obj("item") with { Rotation = Rotation(25, 15, -35) }));
        Assert.True(i.Set(Row(i, angle).Id, value));
        var a = SceneEditing.Angles(e.Session.Current.Find("item")!.Rotation);
        Near(angle == "yaw" ? value : 25, a.Yaw);
        Near(angle == "pitch" ? value : 15, a.Pitch);
        Near(angle == "roll" ? value : -35, a.Roll);
        Assert.Single(e.Session.Journal);
    }
    [Fact]
    public void MultiRotationEditKeepsEachObjectsOtherAnglesAndUndoesOnce()
    {
        var start = SceneOf(Obj("a") with { Rotation = Rotation(10, 20, 30) }, Obj("b") with { Rotation = Rotation(-15, -25, 5) });
        var (e, i) = Edit(start, "a", "b");
        Assert.True(Row(i, "yaw").Mixed);
        Assert.True(i.Set(Row(i, "yaw").Id, 45d));
        var a = SceneEditing.Angles(e.Session.Current.Find("a")!.Rotation);
        var b = SceneEditing.Angles(e.Session.Current.Find("b")!.Rotation);
        Near(45, a.Yaw); Near(20, a.Pitch); Near(30, a.Roll);
        Near(45, b.Yaw); Near(-25, b.Pitch); Near(5, b.Roll);
        Assert.Single(e.Session.Journal); Assert.True(e.Session.Undo());
        Assert.Equal(Format.Write(start), Format.Write(e.Session.Current)); Assert.False(e.Session.Undo());
    }
    [Fact]
    public void MultiPositionEditPreservesOtherAxesAndUndoesOnce()
    {
        var start = SceneOf(Obj("a") with { Position = new(1, 2, 3) }, Obj("b") with { Position = new(4, 5, 6) });
        var (e, i) = Edit(start, "a", "b");
        Assert.True(Row(i, "positionx").Mixed);
        Assert.True(i.Set(Row(i, "positionx").Id, 9d));
        Assert.Equal(new Vector3(9, 2, 3), e.Session.Current.Find("a")!.Position);
        Assert.Equal(new Vector3(9, 5, 6), e.Session.Current.Find("b")!.Position);
        Assert.True(e.Session.Undo()); Assert.Equal(Format.Write(start), Format.Write(e.Session.Current));
        Assert.False(e.Session.Undo());
    }
    [Fact]
    public void ScaleRowsHaveUnitDefaultAndResetOnlyTheirAxis()
    {
        var (e, i) = Edit(SceneOf(Obj("item") with { Scale = new(-2, 3, 4) }));
        var x = Row(i, "scalex");
        Assert.True(x.HasDefault); Assert.True(x.Overridden); Near(-2, Number(x));
        Assert.True(i.Reset(x.Id));
        Assert.Equal(new Vector3(1, 3, 4), e.Session.Current.Find("item")!.Scale);
        Assert.False(Row(i, "scalex").Overridden);
    }
    [Fact]
    public void BlankInspectorAssetAndParentMeanNone()
    {
        var (e, i) = Edit(SceneOf(Obj("group"), Obj("item", "group") with { Asset = "models/mesh.glb" }));
        Assert.True(i.Set(Row(i, "asset").Id, ""));
        Assert.True(i.Set(Row(i, "parent").Id, ""));
        Assert.Null(e.Session.Current.Find("item")!.Asset); Assert.Null(e.Session.Current.Find("item")!.Parent);
    }
    [Fact]
    public void FocusLocksAnOutsideSelectionAndLeavesHistoryUntouched()
    {
        var start = SceneOf(Obj("group"), Obj("item", "group"), Obj("outside"));
        var (e, i) = Edit(start, "item", "outside");
        Assert.True(e.FocusOn("group"));
        Assert.All(i.Rows(), r => Assert.False(r.Editable));
        Assert.False(i.Set(Row(i, "positionx").Id, 7d));
        Assert.NotEmpty(i.LastIssues); Unchanged(e.Session, start, JournalFile.Write([]), 0);
    }
    [Fact]
    public void InspectorReadonlyIdCannotBeEdited()
    {
        var (e, i) = Edit(); var start = e.Session.Current;
        Assert.False(i.Set(Row(i, "id").Id, "renamed"));
        Unchanged(e.Session, start, JournalFile.Write([]), 0);
    }
    [Fact]
    public void CommandForRemoveRequiresExactlyOneSelection()
    {
        var (e, _) = Edit(); var info = Info(e.Session, "scene.remove");
        Assert.Equal("item", Assert.IsType<RemoveObject>(SceneEditing.CommandFor(info, e)).Id);
        e.Select(); Assert.Null(SceneEditing.CommandFor(info, e));
        e.Select("item", "group"); Assert.Null(SceneEditing.CommandFor(info, e));
    }
    [Fact]
    public void CommandForDuplicateUsesTheWholeSelection()
    {
        var (e, _) = Edit(null, "group", "item");
        var info = Info(e.Session, "scene.duplicate");
        Assert.Equal(new[] { "group", "item" }, Assert.IsType<DuplicateGroup>(SceneEditing.CommandFor(info, e)).Ids);
        e.Select(); Assert.Null(SceneEditing.CommandFor(info, e));
    }
    [Theory]
    [InlineData("scene.add")] [InlineData("scene.move")] [InlineData("scene.rotate")] [InlineData("scene.scale")] [InlineData("scene.set_parent")] [InlineData("scene.set_asset")] [InlineData("scene.set_reference")]
    public void CommandForReturnsNullWhenMoreInputIsNeeded(string id)
    {
        var (e, _) = Edit();
        Assert.Null(SceneEditing.CommandFor(Info(e.Session, id), e));
    }

    // SceneTool Program.cs:6-38: only usage and documented exit codes, no CLI implementation oracle.
    private sealed class Temp : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "scene-edit-acceptance-" + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(Root);
        public string File(string relative) => Path.Combine(Root, relative);
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var code = SceneCli.Run(args, output, errors);
        return (code, output.ToString(), errors.ToString());
    }
    private static void WriteScene(string path, Scene scene, string kind = "scene") => File.WriteAllText(path, new SceneFormat(kind).Write(scene));
    [Fact]
    public void ToolNewMakesAnEmptySceneAndWillNotOverwrite()
    {
        using var t = new Temp(); var p = t.File("room.json");
        Assert.Equal(0, Run("new", p).Code);
        Assert.Empty(Format.Read(File.ReadAllText(p)).Objects);
        var bytes = File.ReadAllBytes(p);
        Assert.NotEqual(0, Run("new", p).Code); // Usage promises no overwrite, not a particular refusal category.
        Assert.Equal(bytes, File.ReadAllBytes(p));
    }
    [Fact]
    public void ToolTreeShowsObjectsNestedByParent()
    {
        using var t = new Temp(); var p = t.File("room.json");
        WriteScene(p, SceneOf(Obj("group"), Obj("child", "group") with { Properties = Obj("child").Properties.Add("name", Json("\"Étoile 雨\"")) }));
        var r = Run("tree", p); Assert.Equal(0, r.Code);
        Assert.Contains("group", r.Out); Assert.Contains("child", r.Out);
        var lines = r.Out.Split('\n').Where(l => l.Contains("group") || l.Contains("child")).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.True(lines[1].TakeWhile(char.IsWhiteSpace).Count() > lines[0].TakeWhile(char.IsWhiteSpace).Count());
    }
    [Fact]
    public void ToolDoEditsAndRefusalLeavesBytesIdentical()
    {
        using var t = new Temp(); var p = t.File("room.json");
        WriteScene(p, SceneOf(Obj("item"), Obj("group")));
        Assert.Equal(0, Run("do", p, "scene.move", """{"id":"item","position":[1,2,3]}""").Code);
        Assert.Equal(new Vector3(1, 2, 3), Format.Read(File.ReadAllText(p)).Find("item")!.Position);
        var bytes = File.ReadAllBytes(p);
        var r = Run("do", p, "scene.set_parent", """{"id":"item","parent":"item"}""");
        Assert.Equal(1, r.Code); Assert.NotEmpty(r.Error); Assert.Equal(bytes, File.ReadAllBytes(p));
    }
    [Fact]
    public void ToolApplyAndReplayAgreeIncludingUndoRedo()
    {
        using var t = new Temp(); var b = t.File("base.json"); var a = t.File("apply.json"); var o = t.File("out.json"); var j = t.File("journal.json");
        var s = Session(); WriteScene(b, s.Current); WriteScene(a, s.Current);
        Assert.True(s.Execute(new MoveObject("item", new(2, 3, 4))));
        Assert.True(s.Execute(new ScaleObject("item", new(-2, 1, 3))));
        Assert.True(s.Undo()); Assert.True(s.Redo());
        File.WriteAllText(j, JournalFile.Write(s.Journal));
        Assert.Equal(0, Run("apply", a, j).Code); Assert.Equal(0, Run("replay", b, j, o).Code);
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(o));
        Assert.Equal(Format.Write(s.Current), File.ReadAllText(o));
    }
    [Fact]
    public void ToolRefusedApplyDoesNotSaveEarlierSteps()
    {
        using var t = new Temp(); var p = t.File("room.json"); var j = t.File("journal.json");
        WriteScene(p, SceneOf(Obj("item"))); var bytes = File.ReadAllBytes(p);
        File.WriteAllText(j, JournalFile.Write([
            JournalEntry.Do("scene.move", 1, Json("""{"id":"item","position":[2,3,4]}""")),
            JournalEntry.Do("scene.set_parent", 1, Json("""{"id":"item","parent":"missing"}"""))]));
        Assert.Equal(1, Run("apply", p, j).Code); Assert.Equal(bytes, File.ReadAllBytes(p));
    }
    [Fact]
    public void ToolCheckAssetsSearchesNestedFolderAndReportsMissing()
    {
        using var t = new Temp(); var root = t.File("assets"); var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models); File.WriteAllText(Path.Combine(models, "chair.obj"), "# self-authored placeholder\n");
        var p = t.File("room.json"); WriteScene(p, SceneOf(Obj("chair") with { Asset = "models/chair.obj" }));
        var before = File.ReadAllBytes(p);
        Assert.Equal(0, Run("check", p, "--assets", root).Code);
        File.Delete(Path.Combine(models, "chair.obj"));
        var missing = Run("check", p, "--assets", root);
        Assert.Equal(1, missing.Code); Assert.Contains("chair", missing.Out + missing.Error);
        Assert.Contains("models/chair.obj", missing.Out + missing.Error);
        Assert.Equal(before, File.ReadAllBytes(p));
    }
    [Fact]
    public void ToolCheckWithoutAssetsDoesNotRefusePlaceholders()
    {
        using var t = new Temp(); var p = t.File("room.json");
        WriteScene(p, SceneOf(Obj("item") with { Asset = "absent.glb" }));
        Assert.Equal(0, Run("check", p).Code);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ToolKindCanAppearAtEveryNewArgumentBoundary(int index)
    {
        using var t = new Temp(); var p = t.File("room.json");
        var args = new List<string> { "new", p }; args.InsertRange(index, ["--kind", "acceptance.room"]);
        Assert.Equal(0, Run(args.ToArray()).Code);
        Assert.Empty(new SceneFormat("acceptance.room").Read(File.ReadAllText(p)).Objects);
        Assert.Equal(0, Run("check", p, "--kind", "acceptance.room").Code);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ToolKindCanAppearAtEveryDoArgumentBoundary(int index)
    {
        using var t = new Temp(); var p = t.File("room.json");
        WriteScene(p, SceneOf(Obj("item")), "acceptance.room");
        var args = new List<string> { "do", p, "scene.move", """{"id":"item","position":[3,2,1]}""" };
        args.InsertRange(index, ["--kind", "acceptance.room"]);
        Assert.Equal(0, Run(args.ToArray()).Code);
        Assert.Equal(new Vector3(3, 2, 1), new SceneFormat("acceptance.room").Read(File.ReadAllText(p)).Find("item")!.Position);
    }
    [Fact]
    public void ToolRefusesNonAsciiIdButKeepsNonAsciiName()
    {
        using var t = new Temp(); var p = t.File("room.json");
        Assert.Equal(0, Run("new", p).Code);
        Assert.Equal(0, Run("do", p, "scene.add", """{"object":{"id":"item","properties":{"name":"Étoile 雨"}}}""").Code);
        Assert.Equal("Étoile 雨", Format.Read(File.ReadAllText(p)).Find("item")!.Properties["name"].GetString());
        var bytes = File.ReadAllBytes(p);
        Assert.Equal(1, Run("do", p, "scene.add", """{"object":{"id":"café"}}""").Code);
        Assert.Equal(bytes, File.ReadAllBytes(p));
    }
    [Fact]
    public void ToolCommandsListsAndSearchesNewCommands()
    {
        var all = Run("commands"); Assert.Equal(0, all.Code);
        foreach (var id in new[] { "scene.rotate", "scene.scale", "scene.set_parent", "scene.set_asset", "scene.set_reference" }) Assert.Contains(id, all.Out);
        var filtered = Run("commands", "rotate");
        Assert.Equal(0, filtered.Code); Assert.Contains("scene.rotate", filtered.Out); Assert.DoesNotContain("scene.scale", filtered.Out);
    }
    [Fact]
    public void ToolBadInputHasCodeTwoAndCreatesNoOutput()
    {
        using var t = new Temp(); var p = t.File("missing.json"); var o = t.File("out.json"); var j = t.File("bad.json");
        File.WriteAllText(j, "{");
        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("unknown").Code);
        Assert.Equal(2, Run("tree", p).Code);
        Assert.Equal(2, Run("--kind").Code);
        WriteScene(p, SceneOf(Obj("item")));
        Assert.Equal(2, Run("replay", p, j, o).Code);
        Assert.False(File.Exists(o));
    }
    [Fact]
    public void ToolBadDoArgumentsAndUnknownCommandLeaveBytesIdentical()
    {
        using var t = new Temp(); var p = t.File("room.json"); WriteScene(p, SceneOf(Obj("item"))); var bytes = File.ReadAllBytes(p);
        Assert.Equal(2, Run("do", p, "scene.move", """{"id":"item","position":[1,2]}""").Code);
        Assert.Equal(bytes, File.ReadAllBytes(p));
        Assert.Equal(2, Run("do", p, "scene.unknown", "{}").Code);
        Assert.Equal(bytes, File.ReadAllBytes(p));
    }
    [Fact]
    public void ToolDocumentsDistinctExitCodeConstants()
    {
        Assert.Equal(0, SceneCli.Done); Assert.Equal(1, SceneCli.Refused);
        Assert.Equal(2, SceneCli.BadInput); Assert.Equal(3, SceneCli.Conflict);
        // Conflict cannot be forced deterministically through Run's documented seams without racing an external writer.
        // Codes 0/1/2 are exercised above; this checks the public 3 contract without a flaky timing test.
    }
}
