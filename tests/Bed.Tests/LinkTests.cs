using Xunit;

namespace Bed.Tests;

/// <summary>Validated links (AT-113) on a neutral test document: props resting on supports, using meshes, following rules.</summary>
public sealed class LinkTests
{
    private static readonly LinkKind RestsOn = new("rests on", LinkTarget.Object, AllowCycles: false, OnDelete: OnDelete.Cascade);
    private static readonly LinkKind Faces = new("faces", LinkTarget.Object, OnDelete: OnDelete.Refuse);
    private static readonly LinkKind LitBy = new("is lit by", LinkTarget.Object, OnDelete: OnDelete.Clear);
    private static readonly LinkKind Uses = new("uses", LinkTarget.Asset);
    private static readonly LinkKind Follows = new("follows rule", LinkTarget.External, Registry: "rules");

    private sealed record Prop(string Id, string? RestsOn = null, string? Faces = null, string? LitBy = null, string? Mesh = null, string? Rule = null);

    private sealed record Scene(IReadOnlyList<Prop> Props);

    private sealed class Schema : ILinkSchema<Scene>
    {
        public IEnumerable<string> ObjectIds(Scene d) => d.Props.Select(p => p.Id);

        public IEnumerable<Link> Links(Scene d)
        {
            for (var i = 0; i < d.Props.Count; i++)
            {
                var p = d.Props[i];
                if (p.RestsOn is { } r) yield return new(RestsOn, p.Id, $"$.props[{i}].restsOn", r);
                if (p.Faces is { } f) yield return new(Faces, p.Id, $"$.props[{i}].faces", f);
                if (p.LitBy is { } l) yield return new(LitBy, p.Id, $"$.props[{i}].litBy", l);
                if (p.Mesh is { } m) yield return new(Uses, p.Id, $"$.props[{i}].mesh", m);
                if (p.Rule is { } u) yield return new(Follows, p.Id, $"$.props[{i}].rule", u);
            }
        }
    }

    private static readonly Schema S = new();
    private static readonly Dictionary<string, IReadOnlySet<string>> Rules = new() { ["rules"] = new HashSet<string> { "fall", "burn" } };

    private static Scene Room() => new([
        new("floor"),
        new("table", RestsOn: "floor", Rule: "burn"),
        new("vase", RestsOn: "table", Faces: "door", LitBy: "lamp"),
        new("door"),
        new("lamp", RestsOn: "table"),
    ]);

    [Fact]
    public void AGoodSceneHasNoIssues() => Assert.Empty(new LinkValidator<Scene>(S, registries: Rules).Validate(Room()));

    [Fact]
    public void ABrokenLinkNamesBothObjectsAndTheField()
    {
        var scene = new Scene([.. Room().Props, new("chair", RestsOn: "rug")]);
        var e = Assert.Single(new LinkValidator<Scene>(S, registries: Rules).Validate(scene));
        Assert.Equal("chair", e.ObjectId);
        Assert.Equal("$.props[5].restsOn", e.Path);
        Assert.Contains("'rug'", e.Message);
    }

    [Fact]
    public void CyclesAreRefusedOnlyWhereTheKindForbidsThem()
    {
        var cycle = new Scene([new("a", RestsOn: "b"), new("b", RestsOn: "c"), new("c", RestsOn: "a")]);
        var e = Assert.Single(new LinkValidator<Scene>(S).Validate(cycle));
        Assert.Contains("a → b → c → a", e.Message);

        var facing = new Scene([new("a", Faces: "b"), new("b", Faces: "a")]);   // "faces" allows cycles
        Assert.Empty(new LinkValidator<Scene>(S).Validate(facing));
    }

    [Fact]
    public void OutsideListsAndAssetsAreChecked()
    {
        var scene = new Scene([new("table", Rule: "explode", Mesh: "0123456789abcdef0123456789abcdef")]);
        Assert.Contains(new LinkValidator<Scene>(S, registries: Rules).Validate(scene), i => i.Message.Contains("'rules' doesn't have"));
        Assert.Contains(new LinkValidator<Scene>(S).Validate(scene), i => i.Message.Contains("isn't loaded"));

        var dir = Directory.CreateTempSubdirectory("bed-links-").FullName;
        try
        {
            var index = AssetIndex.Scan(dir);
            Assert.Contains(new LinkValidator<Scene>(S, index, Rules).Validate(scene), i => i.Message.Contains("can't be found"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeletingFollowsCascadesRefusalsAndClears()
    {
        // Deleting the table cascades to what rests on it (vase, lamp). The vase is lit by the lamp, but both go, so
        // nothing is left to clear; nothing outside faces them, so nothing blocks.
        var (blocking, delete, clear) = LinkPlans.PlanDelete(S, Room(), "table");
        Assert.Empty(blocking);
        Assert.Equal(["lamp", "table", "vase"], delete.Order());
        Assert.Empty(clear);

        // Deleting the door is refused while the vase faces it.
        var door = LinkPlans.PlanDelete(S, Room(), "door");
        Assert.Equal("vase", Assert.Single(door.Blocking).From);

        // Deleting the lamp alone clears the vase's light.
        var lamp = LinkPlans.PlanDelete(S, Room(), "lamp");
        Assert.Equal("vase", Assert.Single(lamp.Clear).From);
        Assert.Equal(["lamp"], lamp.Delete);
    }

    [Fact]
    public void DuplicatingAGroupRemapsInsideLinksAndKeepsOutsideOnes()
    {
        var map = LinkPlans.PlanDuplicate(S, Room(), ["table", "vase"], (id, n) => n == 1 ? id + "_copy" : $"{id}_copy{n}");
        Assert.Equal("table_copy", map["table"]);
        var links = S.Links(Room()).ToList();
        var vaseOnTable = links.Single(l => l.From == "vase" && l.Kind == RestsOn);
        var vaseFacesDoor = links.Single(l => l.From == "vase" && l.Kind == Faces);
        Assert.Equal("table_copy", LinkPlans.TargetInCopy(vaseOnTable, map));   // inside the group: remapped
        Assert.Equal("door", LinkPlans.TargetInCopy(vaseFacesDoor, map));        // leaving the group: kept

        var crowded = new Scene([.. Room().Props, new("table_copy")]);
        Assert.Equal("table_copy2", LinkPlans.PlanDuplicate(S, crowded, ["table"], (id, n) => n == 1 ? id + "_copy" : $"{id}_copy{n}")["table"]);
        Assert.Throws<ArgumentException>(() => LinkPlans.PlanDuplicate(S, Room(), ["ghost"], (id, n) => id + n));
    }

    [Fact]
    public void ValidatorsCombine()
    {
        var bad = new Scene([new("a", RestsOn: "nowhere")]);
        var all = new AllOf<Scene>(new LinkValidator<Scene>(S), new LinkValidator<Scene>(S));
        Assert.Equal(2, all.Validate(bad).Count);
    }
}
