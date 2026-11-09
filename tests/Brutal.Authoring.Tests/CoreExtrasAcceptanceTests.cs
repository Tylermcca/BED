#pragma warning disable CS0618 // AssetRecord.Create is obsolete for imports; these cases test the low-level record writer (and old, undeclared records) on purpose.
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A011 independent public-doc acceptance checks. INVENTED graph/settings/text assets.
/// Drafted before reading LinkTests, PlacementTests, VariantTests, MigrationTests or AssetTests.</summary>
public sealed class CoreExtrasAcceptanceTests
{
    private sealed record Graph(string[] Ids, Link[] Edges);
    private sealed class Schema : ILinkSchema<Graph>
    {
        public IEnumerable<string> ObjectIds(Graph d) => d.Ids;
        public IEnumerable<Link> Links(Graph d) => d.Edges;
    }
    private static readonly Schema Links = new();
    private static LinkKind ObjectKind(OnDelete delete = OnDelete.Refuse, bool cycles = true, string name = "supports") =>
        new(name, LinkTarget.Object, AllowCycles: cycles, OnDelete: delete);
    private static Link Edge(LinkKind kind, string from = "a", string to = "b") => new(kind, from, "$.target", to);

    [Fact]
    public void EmptySchemaHasNoLinkProblemsOrDependents()
    {
        var d = new Graph([], []);
        Assert.Empty(new LinkValidator<Graph>(Links).Validate(d));
        Assert.Empty(LinkPlans.Dependents(Links, d, "absent"));
    }

    [Fact]
    public void BrokenObjectTargetDiagnosticNamesBothEndsAndField()
    {
        var d = new Graph(["a"], [Edge(ObjectKind(), to: "missing")]);
        var issue = Assert.Single(new LinkValidator<Graph>(Links).Validate(d));
        Assert.Equal(Severity.Error, issue.Severity); Assert.Equal("a", issue.ObjectId); Assert.Equal("$.target", issue.Path);
        Assert.Contains("a", issue.Message); Assert.Contains("missing", issue.Message);
    }

    [Fact]
    public void ExistingObjectTargetPassesValidation()
    {
        var d = new Graph(["a","b"], [Edge(ObjectKind())]); Assert.Empty(new LinkValidator<Graph>(Links).Validate(d));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelfLinkIsACycleOnlyWhenItsKindForbidsCycles(bool allowed)
    {
        var d = new Graph(["a"], [Edge(ObjectKind(cycles: allowed), to: "a")]);
        var issues = new LinkValidator<Graph>(Links).Validate(d);
        if (allowed) Assert.Empty(issues);
        else { Assert.NotEmpty(issues); Assert.Contains(issues, i => (i.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase) || i.Message.Contains("circle", StringComparison.OrdinalIgnoreCase))); }
    }

    [Fact]
    public void TwoObjectForbiddenCycleIsDetected()
    {
        var k = ObjectKind(cycles: false); var d = new Graph(["a","b"], [Edge(k), Edge(k,"b","a")]);
        Assert.Contains(new LinkValidator<Graph>(Links).Validate(d), i => i.Severity == Severity.Error && (i.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase) || i.Message.Contains("circle", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DifferentLinkKindsDoNotCreateAForbiddenCycleTogether()
    {
        var d = new Graph(["a","b"], [Edge(ObjectKind(cycles:false,name:"one")), Edge(ObjectKind(cycles:false,name:"two"),"b","a")]);
        Assert.Empty(new LinkValidator<Graph>(Links).Validate(d));
    }

    [Fact]
    public void ExternalTargetsResolveOnlyThroughTheNamedRegistry()
    {
        var k = new LinkKind("rule", LinkTarget.External, "rules");
        IReadOnlyDictionary<string,IReadOnlySet<string>> registries = new Dictionary<string,IReadOnlySet<string>>
        { ["rules"] = new HashSet<string> { "blue" }, ["other"] = new HashSet<string> { "red" } };
        var validator = new LinkValidator<Graph>(Links, registries: registries);
        Assert.Empty(validator.Validate(new Graph(["a"], [Edge(k,to:"blue")])));
        var issue = Assert.Single(validator.Validate(new Graph(["a"], [Edge(k,to:"red")])));
        Assert.Equal("a", issue.ObjectId); Assert.Contains("red",issue.Message); Assert.Contains("rules",issue.Message);
    }

    [Fact]
    public void MissingRegistryDoesNotSilentlyValidateAnExternalTarget()
    {
        var d = new Graph(["a"], [Edge(new LinkKind("rule",LinkTarget.External,"absent"),to:"x")]);
        Assert.Contains(new LinkValidator<Graph>(Links).Validate(d), i => i.Severity == Severity.Error);
    }

    [Fact]
    public void DeleteRefusalReturnsTheActualBlockingLink()
    {
        var link = Edge(ObjectKind()); var d = new Graph(["a","b"], [link]);
        var plan = LinkPlans.PlanDelete(Links,d,"b");
        Assert.Equal(link, Assert.Single(plan.Blocking)); Assert.Empty(plan.Clear);
    }

    [Fact]
    public void DeleteClearPlanKeepsOwnerAndNamesTheLinkToClear()
    {
        var link = Edge(ObjectKind(OnDelete.Clear)); var d = new Graph(["a","b"], [link]);
        var plan = LinkPlans.PlanDelete(Links,d,"b");
        Assert.Empty(plan.Blocking); Assert.Equal(new[] {"b"}, plan.Delete.Order()); Assert.Equal(link,Assert.Single(plan.Clear));
    }

    [Fact]
    public void DeepCascadeFollowsEveryDependentExactlyOnce()
    {
        var k = ObjectKind(OnDelete.Cascade); string[] ids = Enumerable.Range(0,64).Select(n => "n"+n).ToArray();
        var edges = Enumerable.Range(1,63).Select(n => Edge(k,ids[n],ids[n-1])).ToArray();
        var plan = LinkPlans.PlanDelete(Links,new Graph(ids,edges),"n0");
        Assert.Empty(plan.Blocking); Assert.Empty(plan.Clear); Assert.Equal(ids.Order(),plan.Delete.Order());
    }

    [Fact]
    public void CascadeCycleTerminatesWithBothObjectsScheduled()
    {
        var k = ObjectKind(OnDelete.Cascade); var d = new Graph(["a","b"],[Edge(k),Edge(k,"b","a")]);
        var plan = LinkPlans.PlanDelete(Links,d,"a");
        Assert.Empty(plan.Blocking); Assert.Equal(["a","b"],plan.Delete.Order());
    }

    [Fact]
    public void SelfLinkDoesNotCountAsAnExternalDependent()
    {
        var d = new Graph(["a"],[Edge(ObjectKind(),to:"a")]);
        Assert.Empty(LinkPlans.Dependents(Links,d,"a")); Assert.Empty(LinkPlans.PlanDelete(Links,d,"a").Blocking);
    }

    [Fact]
    public void DuplicateMapRetriesCollisionsWithExistingAndNewIds()
    {
        var d = new Graph(["a","b","a_copy"],[]);
        var calls = new Dictionary<string,int>();
        var map = LinkPlans.PlanDuplicate(Links,d,["a","b"], (id,n) =>
        {
            int count = calls.GetValueOrDefault(id) + 1; calls[id] = count;
            return count == 1 ? "a_copy" : count == 2 ? "shared_copy" : "copy_"+id;
        });
        Assert.Equal(2,calls["a"]); Assert.Equal(3,calls["b"]);
        Assert.Equal(2,map.Count); Assert.Equal(2,map.Values.Distinct().Count());
        Assert.All(map.Values, id => Assert.DoesNotContain(id,d.Ids));
    }

    [Fact]
    public void DuplicateRemapsInsideTargetsAndKeepsOutsideTargets()
    {
        var k = ObjectKind(); var d = new Graph(["a","b","outside"],[Edge(k),Edge(k,"b","outside")]);
        var map = LinkPlans.PlanDuplicate(Links,d,["a","b"],(id,n)=>id+"_new_"+n);
        Assert.Equal(map["b"],LinkPlans.TargetInCopy(d.Edges[0],map));
        Assert.Equal("outside",LinkPlans.TargetInCopy(d.Edges[1],map));
        Assert.Equal(["a","b","outside"],d.Ids); Assert.Equal("b",d.Edges[0].To);
    }

    private sealed class FixedIssues(params Issue[] issues) : IValidator<Graph>
    {
        public IReadOnlyList<Issue> Validate(Graph d) => issues;
    }

    [Fact]
    public void AllOfCombinesErrorsAndWarningsFromEveryValidator()
    {
        Issue e=Issue.Error("first","a"), w=Issue.Warning("second","b");
        var combined=new AllOf<Graph>(new FixedIssues(e),new FixedIssues(w)).Validate(new Graph([],[]));
        Assert.Equal([e,w],combined); Assert.Empty(new AllOf<Graph>().Validate(new Graph([],[])));
    }

    [Fact]
    public void GridSnapsEachAxisRelativeToItsOriginAndLeavesZeroAxisExact()
    {
        var g=new Grid(new Vector3(2,0,.5f),new Vector3(1,5,.25f));
        Assert.Equal(new Vector3(3,7.123f,1.25f),g.Snap(new Vector3(2.8f,7.123f,1.1f)));
    }

    [Fact]
    public void HeldKeyFreesEnabledGridAndSnapsDisabledGrid()
    {
        var p=new Vector3(.3f,.6f,.9f);
        Assert.Equal(p,Grid.Uniform(1).Snap(p,invert:true));
        Assert.Equal(p,Grid.Uniform(1,enabled:false).Snap(p));
        Assert.Equal(new Vector3(0,1,1),Grid.Uniform(1,enabled:false).Snap(p,invert:true));
    }

    [Fact]
    public void NoGridAndAllZeroAxesNeverRoundEvenWithInversion()
    {
        var p=new Vector3(.123f,-.456f,.789f);
        Assert.Equal(p,Grid.None.Snap(p)); Assert.Equal(p,Grid.None.Snap(p,invert:true));
        Assert.Equal(p,new Grid(Vector3.Zero,new Vector3(4)).Snap(p));
    }

    [Fact]
    public void OnGridUsesTheGeometryEvenWhenSnappingIsDisabled()
    {
        var g=Grid.Uniform(2,enabled:false);
        Assert.True(g.OnGrid(new Vector3(2,4,6))); Assert.False(g.OnGrid(new Vector3(2.25f,4,6)));
    }

    [Fact]
    public void OffGridPositionRemainsUnchangedUntilSnapIsAsked()
    {
        var position=new Vector3(.125f,.375f,.625f); var before=position; var g=Grid.Uniform(1);
        Assert.False(g.OnGrid(position)); Assert.Equal(before,position);
        var snapped=g.Snap(position); Assert.Equal(new Vector3(0,0,1),snapped); Assert.Equal(before,position);
    }

    [Fact]
    public void OnGridHonorsItsExplicitTolerance()
    {
        var g=Grid.Uniform(1); var p=new Vector3(1.0005f,2,3);
        Assert.False(g.OnGrid(p,.0001f)); Assert.True(g.OnGrid(p,.001f));
    }

    private static Origin[] Origins() =>
    [
        new(OriginKind.Centre),new(OriginKind.MinCorner),
        new(OriginKind.BottomCentre,Up:1),new(OriginKind.BottomCentre,Up:2),
        new(OriginKind.Pivot,Pivot:new Vector3(.25f,.5f,.75f))
    ];

    [Fact]
    public void EveryOriginReconstructsTheSameBounds()
    {
        var min=new Vector3(.125f,-2,.625f); var size=new Vector3(2,4,6);
        foreach(var o in Origins()) Assert.Equal((min,min+size),o.Bounds(o.PositionFor(min,size),size));
    }

    [Fact]
    public void EveryOriginPairConvertsExactlyWithoutChangingBounds()
    {
        var p=new Vector3(.125f,.375f,.625f);var size=new Vector3(2,4,6);
        foreach(var from in Origins()) foreach(var to in Origins())
        {
            var converted=from.Convert(p,size,to);
            Assert.Equal(from.Bounds(p,size),to.Bounds(converted,size)); Assert.Equal(p,to.Convert(converted,size,from));
        }
    }

    [Fact]
    public void BottomCentreChoosesTheDeclaredUpAxis()
    {
        var size=new Vector3(2,4,6);
        Assert.Equal(new Vector3(1,0,3),new Origin(OriginKind.BottomCentre,Up:1).OffsetIn(size));
        Assert.Equal(new Vector3(1,2,0),new Origin(OriginKind.BottomCentre,Up:2).OffsetIn(size));
    }

    [Fact]
    public void CustomPivotIsMeasuredFromMinimumCorner()
    {
        var o=new Origin(OriginKind.Pivot,Pivot:new Vector3(.25f,.5f,.75f));var p=new Vector3(1,2,3);
        Assert.Equal(p-o.Pivot,o.Bounds(p,new Vector3(2,4,6)).Min);
    }

    [Fact]
    public void OneVariantProvidesItsDefaultAndAnUnknownNameFallsBack()
    {
        var set=new VariantSet("accept.one","base",[("base","guid-a")]);
        Assert.Empty(set.Validate());Assert.Equal(["base"],set.Names);
        Assert.Equal("guid-a",set.AssetFor("base",null,out bool fallback));Assert.False(fallback);
        Assert.Equal("guid-a",set.AssetFor("unknown",null,out fallback));Assert.True(fallback);
    }

    [Fact]
    public void VariantNamesKeepDisplayOrderAndInvalidDefinitionsReportErrors()
    {
        var set=new VariantSet("accept.set","two",[("two","b"),("one","a")]);Assert.Equal(["two","one"],set.Names);
        Assert.Contains(new VariantSet("empty","base",[]).Validate(),i=>i.Severity==Severity.Error);
        Assert.Contains(new VariantSet("missing","absent",[("base","a")]).Validate(),i=>i.Severity==Severity.Error);
        Assert.Contains(new VariantSet("duplicate","base",[("base","a"),("base","b")]).Validate(),i=>i.Severity==Severity.Error);
    }

    [Fact]
    public void MissingVariantAssetWarnsWithVariantNameAndShowsDefaultAsset()
    {
        using var f=new Folder();var a=f.Import("base.mesh");var index=AssetIndex.Scan(f.Root);
        var set=new VariantSet("accept.set","base",[("base",a.Guid),("damaged","00000000000000000000000000000001")]);
        Assert.Contains(set.Validate(index),i=>i.Severity==Severity.Warning&&i.Message.Contains("damaged",StringComparison.Ordinal));
        Assert.Equal(a.Guid,set.AssetFor("damaged",index,out bool fallback));Assert.True(fallback);
    }

    [Fact]
    public void VariantPreviewDoesNotWriteAssetFilesOrSidecars()
    {
        using var f=new Folder();var a=f.Import("base.mesh");string path=AssetRecord.SidecarFor(Path.Combine(f.Root,"base.mesh"));
        byte[] before=File.ReadAllBytes(path);var set=new VariantSet("accept.set","base",[("base",a.Guid)]);
        set.AssetFor("unknown",AssetIndex.Scan(f.Root),out _);Assert.Equal(before,File.ReadAllBytes(path));Assert.Equal(2,Directory.GetFiles(f.Root).Length);
    }

    private sealed record Settings(string Label,int Count);
    private sealed class SettingsFormat : IDocumentFormat<Settings>
    {
        public string Kind=>"accept.settings";public int Version=>3;
        public Settings Read(string text)
        {
            var r=StrictReader.Parse(text);r.Only("kind","version","label","count");
            if(r.String("kind")!=Kind)throw new DocumentFormatException("$.kind","Wrong kind.");
            if(r.Int("version")!=Version)throw new DocumentFormatException("$.version","Wrong version.");
            return new Settings(r.String("label"),r.Int("count"));
        }
        public string Write(Settings d)=>"{\"kind\":\"accept.settings\",\"version\":3,\"label\":"+JsonSerializer.Serialize(d.Label)+",\"count\":"+d.Count+"}\n";
    }
    private sealed class ValidSettings : IValidator<Settings>
    {
        public IReadOnlyList<Issue> Validate(Settings d)=>[];
    }
    private static Migrations UpgradeSteps()=>new Migrations("accept.settings",3)
        .Step(1,"rename name to label",g=>{g["label"]=g["name"]!.DeepClone();g.Remove("name");g["version"]=2;return g;})
        .Step(2,"add count",g=>{g["count"]=7;g["version"]=3;return g;});
    private const string Old="{\"kind\":\"accept.settings\",\"version\":1,\"name\":\"Original\"}\n";

    [Fact]
    public void MigrationAppliesEveryStepInOrderAndReportsTheirReasons()
    {
        var r=UpgradeSteps().Upgrade(Old);var d=new SettingsFormat().Read(r.Text);
        Assert.Equal(new Settings("Original",7),d);Assert.Collection(r.Applied, step=>Assert.Contains("rename name to label",step), step=>Assert.Contains("add count",step));
    }

    [Fact]
    public void AlreadyCurrentMigrationHasNoAppliedSteps()
    {
        var text=new SettingsFormat().Write(new Settings("Current",9));var r=UpgradeSteps().Upgrade(text);
        Assert.Empty(r.Applied);Assert.Equal(new Settings("Current",9),new SettingsFormat().Read(r.Text));
    }

    [Fact]
    public void UpgradedOpenIsDirtyButDoesNotRewriteTheSourceUntilSave()
    {
        using var f=new Folder();string path=f.Write("settings.json",Old);byte[] original=File.ReadAllBytes(path);
        var format=new MigratingFormat<Settings>(new SettingsFormat(),UpgradeSteps());
        var session=DocumentSession<Settings>.Open(path,format,new ValidSettings(),new CommandCatalog<Settings>());
        Assert.True(session.Dirty);Assert.Equal(original,File.ReadAllBytes(path));Assert.Equal(2,format.LastApplied.Count);
        Assert.Empty(session.Journal);Assert.False(session.History.CanUndo);
        session.Save();Assert.False(session.Dirty);Assert.Equal(3,JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<int>());
        Assert.Equal(new Settings("Original",7),new SettingsFormat().Read(File.ReadAllText(path)));
    }

    [Fact]
    public void CurrentVersionOpenIsCleanAndLastAppliedResetsOnLaterReads()
    {
        var format=new MigratingFormat<Settings>(new SettingsFormat(),UpgradeSteps());
        format.Read(Old);Assert.NotEmpty(format.LastApplied);
        string current=new SettingsFormat().Write(new Settings("Current",3));format.Read(current);Assert.Empty(format.LastApplied);
        using var f=new Folder();string path=f.Write("current.json",current);
        var s=DocumentSession<Settings>.Open(path,format,new ValidSettings(),new CommandCatalog<Settings>());Assert.False(s.Dirty);
    }

    [Theory]
    [InlineData("future","$.version")]
    [InlineData("wrong-kind","$.kind")]
    [InlineData("missing-step","$.version")]
    public void UnsupportedMigrationPathHasAFieldDiagnostic(string mode,string path)
    {
        string text=Old;
        var migrations=UpgradeSteps();
        if(mode=="future")text="{\"kind\":\"accept.settings\",\"version\":99}";
        if(mode=="wrong-kind")text="{\"kind\":\"other\",\"version\":1}";
        if(mode=="missing-step")migrations=new Migrations("accept.settings",3).Step(1,"one",g=>g);
        var e=Assert.Throws<DocumentFormatException>(()=>migrations.Upgrade(text));Assert.Equal(path,e.Path);Assert.NotEmpty(e.Message);
    }

    [Fact]
    public void ThrowingMigrationNeverRewritesTheInputOrRunsLaterSteps()
    {
        using var f=new Folder();string path=f.Write("bad.json",Old);bool later=false;
        var migrations=new Migrations("accept.settings",3)
            .Step(1,"failure",_=>throw new InvalidOperationException("Deliberate fixture step failure."))
            .Step(2,"must not run",g=>{later=true;return g;});
        var format=new MigratingFormat<Settings>(new SettingsFormat(),migrations);
        Assert.ThrowsAny<Exception>(()=>DocumentSession<Settings>.Open(path,format,new ValidSettings(),new CommandCatalog<Settings>()));
        Assert.False(later);Assert.Equal(Old,File.ReadAllText(path));
    }

    private sealed class Folder : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"A011-"+Guid.NewGuid().ToString("N"));
        public Folder()=>Directory.CreateDirectory(Root);
        public string Write(string name,string text="INVENTED self-authored fixture")
        {string p=Path.Combine(Root,name);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,text,new UTF8Encoding(false));return p;}
        public AssetRecord Import(string name,AiUse ai=default)
        {
            string path=Write(name);using var j=JsonDocument.Parse("{\"scale\":1}");
            return AssetRecord.Create(path,"accept.text",1,j.RootElement,new Provenance("A011","self-authored","INVENTED text"),ai);
        }
        public void Dispose()
        {
            string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(Root).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe scratch.");
            Directory.Delete(Root,true);
        }
    }

    [Fact]
    public void FirstImportAssignsOnePermanentGuidAndRefusesASecondCreate()
    {
        using var f=new Folder();var a=f.Import("first.mesh");string path=Path.Combine(f.Root,"first.mesh");
        Assert.Matches("^[0-9a-f]{32}$",a.Guid);byte[] before=File.ReadAllBytes(AssetRecord.SidecarFor(path));
        using var j=JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(()=>AssetRecord.Create(path,"accept.text",1,j.RootElement,a.Provenance,AiUse.Allowed));
        Assert.Equal(before,File.ReadAllBytes(AssetRecord.SidecarFor(path)));
    }

    [Fact]
    public void MovingAnAssetWithItsSidecarKeepsGuidAndResolution()
    {
        using var f=new Folder();var a=f.Import("old.mesh");string old=Path.Combine(f.Root,"old.mesh"),next=Path.Combine(f.Root,"new.mesh");
        File.Move(old,next);File.Move(AssetRecord.SidecarFor(old),AssetRecord.SidecarFor(next));
        var index=AssetIndex.Scan(f.Root);Assert.Equal(next,index.Resolve(a.Guid));Assert.Equal(a.Guid,index.Record(a.Guid)!.Guid);
    }

    [Fact]
    public void DuplicateGuidResolvesNeitherCopyAndNamesBothLocations()
    {
        using var f=new Folder();var a=f.Import("one.mesh");string second=f.Write("two.mesh");
        File.WriteAllText(AssetRecord.SidecarFor(second),(a with {Source="two.mesh"}).Write());
        var index=AssetIndex.Scan(f.Root);Assert.Null(index.Resolve(a.Guid));Assert.Null(index.Record(a.Guid));
        string issues=string.Join("\n",index.Issues.Select(i=>i.Message));Assert.Contains("one.mesh",issues);Assert.Contains("two.mesh",issues);
    }

    [Fact]
    public void MissingGuidNeverRebindsToAReplacementAtTheSamePath()
    {
        using var f=new Folder();var old=f.Import("same.mesh");string p=Path.Combine(f.Root,"same.mesh");
        File.Delete(AssetRecord.SidecarFor(p));var replacement=f.Import("same.mesh");
        Assert.NotEqual(old.Guid,replacement.Guid);var index=AssetIndex.Scan(f.Root);
        Assert.Null(index.Resolve(old.Guid));Assert.Equal(p,index.Resolve(replacement.Guid));
    }

    [Theory]
    [InlineData(AiUse.Unknown,false)]
    [InlineData(AiUse.Forbidden,false)]
    [InlineData(AiUse.Allowed,true)]
    public void AiUseRequiresExplicitPermissionIncludingEnumDefault(AiUse ai,bool allowed)
    {
        using var f=new Folder();var record=f.Import("asset.mesh",ai);
        Assert.Equal(allowed,record.MayUseWithAi);Assert.Equal(allowed,AssetRecord.Read(record.Write()).MayUseWithAi);
        Assert.Equal(AiUse.Unknown,default(AiUse));
    }

    [Fact]
    public void ReimportKeepsGuidWhileUpdatingHashAndImporterVersion()
    {
        using var f=new Folder();var record=f.Import("asset.mesh");string p=Path.Combine(f.Root,"asset.mesh");File.AppendAllText(p," edited");
        var updated=record.Reimport(p,2);Assert.Equal(record.Guid,updated.Guid);Assert.NotEqual(record.SourceHash,updated.SourceHash);
        Assert.Equal(AssetRecord.HashOf(p),updated.SourceHash);Assert.Equal(2,updated.ImporterVersion);
        Assert.Equal(updated.Write(),AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(p))).Write());
    }

    [Fact]
    public void SidecarRoundTripRetainsSettingsProvenanceAndCanonicalBytes()
    {
        using var f=new Folder();var record=f.Import("asset.mesh");string text=record.Write();
        var copy=AssetRecord.Read(text);Assert.Equal(text,copy.Write());Assert.Equal(1,copy.Settings.GetProperty("scale").GetInt32());
        Assert.Equal(record.Provenance,copy.Provenance);Assert.EndsWith("\n",text);Assert.DoesNotContain("\r",text);
    }

    [Fact]
    public void WrongSourceNameWarnsButTheSidecarStillFollowsItsFile()
    {
        using var f=new Folder();var record=f.Import("actual.mesh");string p=Path.Combine(f.Root,"actual.mesh");
        File.WriteAllText(AssetRecord.SidecarFor(p),(record with {Source="wrong.mesh"}).Write());
        var index=AssetIndex.Scan(f.Root);Assert.Equal(p,index.Resolve(record.Guid));
        Assert.Contains(index.Issues,i=>i.Severity==Severity.Warning&&i.Message.Contains("wrong.mesh",StringComparison.Ordinal)&&i.Message.Contains("actual.mesh",StringComparison.Ordinal));
    }

    [Fact]
    public void ScanReportsMissingSourceChangedSourceUnimportedFileAndMissingLicence()
    {
        using var f=new Folder();var gone=f.Import("gone.mesh");File.Delete(Path.Combine(f.Root,"gone.mesh"));
        var changed=f.Import("changed.mesh");File.AppendAllText(Path.Combine(f.Root,"changed.mesh")," delta");f.Write("waiting.mesh");
        var unlicensed=f.Import("unlicensed.mesh");string p=Path.Combine(f.Root,"unlicensed.mesh");
        File.WriteAllText(AssetRecord.SidecarFor(p),(unlicensed with {Provenance=unlicensed.Provenance with {Licence=""}}).Write());
        var index=AssetIndex.Scan(f.Root,".mesh");
        Assert.Contains(index.Issues,i=>i.ObjectId==gone.Guid);Assert.Contains(index.Issues,i=>i.ObjectId==changed.Guid);
        Assert.Contains(index.Issues,i=>i.Message.Contains("waiting.mesh",StringComparison.Ordinal));Assert.Contains(index.Issues,i=>i.ObjectId==unlicensed.Guid);
    }

    [Fact]
    public void AssetLinkResolvesAnExistingGuidAndNamesMissingGuid()
    {
        using var f=new Folder();var a=f.Import("asset.mesh");var validator=new LinkValidator<Graph>(Links,AssetIndex.Scan(f.Root));
        var k=new LinkKind("mesh",LinkTarget.Asset);
        Assert.Empty(validator.Validate(new Graph(["a"],[Edge(k,to:a.Guid)])));
        var issue=Assert.Single(validator.Validate(new Graph(["a"],[Edge(k,to:"00000000000000000000000000000001")])));
        Assert.Equal("a",issue.ObjectId);Assert.Contains("00000000000000000000000000000001",issue.Message);
    }
}
