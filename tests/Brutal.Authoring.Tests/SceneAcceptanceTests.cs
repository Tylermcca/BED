using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A015 independent public-doc scene checks. INVENTED neutral objects plus the repository's original A005 bundles.
/// No SceneTests.cs implementation was read before this draft.</summary>
public sealed class SceneAcceptanceTests
{
    private static readonly SceneFormat Format = new("accept.scene");
    private static readonly SceneValidator Validator = new();
    private static SceneObject Object(string id,string? parent=null) => SceneObject.At(id,null,Vector3.Zero) with {Parent=parent};
    private static Scene Of(params SceneObject[] objects) => new(objects.ToImmutableArray());
    private static DocumentSession<Scene> Session(Scene? scene=null) => new(Format,Validator,SceneKit.Catalog(),scene??Scene.Empty);
    private static JsonElement Json(string text) {using var j=JsonDocument.Parse(text);return j.RootElement.Clone();}
    private static void Refused(DocumentSession<Scene> s,ICommand<Scene> command)
    {
        string before=Format.Write(s.Current),journal=JournalFile.Write(s.Journal);long revision=s.Revision;int undo=s.History.UndoCount;
        Assert.False(s.Execute(command));Assert.Equal(before,Format.Write(s.Current));
        Assert.Equal(journal,JournalFile.Write(s.Journal));Assert.Equal(revision,s.Revision);Assert.Equal(undo,s.History.UndoCount);
        Assert.NotEmpty(s.LastIssues);
    }

    private static string FixtureRoot()
    {
        for(DirectoryInfo? d=new(AppContext.BaseDirectory);d is not null;d=d.Parent)
        {
            string p=Path.Combine(d.FullName,"fixtures","import");
            if(File.Exists(Path.Combine(p,"make_fixtures.py")))return p;
        }
        throw new InvalidOperationException("Missing original fixture generator.");
    }
    private static readonly Lazy<Dictionary<string,string>> Bundles = new(() =>
    {
        var info=new ProcessStartInfo("python"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment["PYTHONDONTWRITEBYTECODE"]="1";info.ArgumentList.Add("-c");
        info.ArgumentList.Add("""
        import importlib.util,json,sys
        sys.dont_write_bytecode=True
        spec=importlib.util.spec_from_file_location('a015_original_fixtures',sys.argv[1])
        m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
        data={}
        for name in ['roundtrip_documents','missing_references','clone_identity']:
            data.update(getattr(m,name)())
        print(json.dumps({k:v.decode('utf-8') for k,v in data.items()}))
        """);
        info.ArgumentList.Add(Path.Combine(FixtureRoot(),"make_fixtures.py"));
        using var process=Process.Start(info)!;
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree:true);
            throw new TimeoutException("Original fixture generator did not finish.");
        }
        string text=stdout.GetAwaiter().GetResult(),error=stderr.GetAwaiter().GetResult();
        Assert.True(process.ExitCode==0,error);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(text)!;
    });
    private sealed class Scratch : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"A015-"+Guid.NewGuid().ToString("N"));
        public Scratch()=>Directory.CreateDirectory(Root);
        public string Bundle(string name)
        {
            string path=Path.Combine(Root,name);File.WriteAllText(path,Bundles.Value[name]);return path;
        }
        public void Dispose()
        {
            string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(Root).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe scratch.");
            Directory.Delete(Root,true);
        }
    }

    [Fact]
    public void EmptySceneRoundTripsCanonicallyAndIsValid()
    {
        string text=Format.Write(Scene.Empty);Assert.Equal(text,Format.Write(Format.Read(text)));
        Assert.Empty(Validator.Validate(Format.Read(text)));Assert.EndsWith("\n",text);Assert.DoesNotContain("\r",text);
    }

    [Fact]
    public void OptionalObjectFieldsDefaultAsThePublicContractSays()
    {
        var d=Format.Read("""{"kind":"accept.scene","version":1,"objects":[{"id":"empty"}]}""");
        var o=Assert.Single(d.Objects);Assert.Equal(Vector3.Zero,o.Position);Assert.Equal(Quaternion.Identity,o.Rotation);
        Assert.Equal(Vector3.One,o.Scale);Assert.Null(o.Asset);Assert.Null(o.Parent);Assert.Empty(o.Properties);Assert.Empty(o.References);
        Assert.Equal(Format.Write(d),Format.Write(Format.Read(Format.Write(d))));
    }

    [Fact]
    public void CallerChoosesTheSceneKindInBothReadAndWrite()
    {
        var f=new SceneFormat("thirdparty.neutral");
        string text=f.Write(Of(Object("one")));Assert.Equal("thirdparty.neutral",JsonNode.Parse(text)!["kind"]!.GetValue<string>());
        Assert.Equal("one",Assert.Single(f.Read(text).Objects).Id);
        Assert.Equal("$.kind",Assert.Throws<DocumentFormatException>(()=>Format.Read(text)).Path);
    }

    [Theory]
    [InlineData("future","$.version")]
    [InlineData("root","$.extra")]
    [InlineData("object","$.objects[0].extra")]
    [InlineData("vector","$.objects[0].position")]
    public void StrictReadingLocatesUnsupportedVersionUnknownFieldsAndWrongVectors(string mutation,string path)
    {
        var g=JsonNode.Parse(Format.Write(Of(Object("a"))))!.AsObject();
        if(mutation=="future")g["version"]=999;
        if(mutation=="root")g["extra"]=true;
        if(mutation=="object")g["objects"]![0]!["extra"]=true;
        if(mutation=="vector")g["objects"]![0]!["position"]=new JsonArray(1,2);
        Assert.StartsWith(path,Assert.Throws<DocumentFormatException>(()=>Format.Read(g.ToJsonString())).Path,StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateJsonPropertyIsRefusedAtItsPath()
    {
        string text="""{"kind":"accept.scene","version":1,"version":1,"objects":[]}""";
        Assert.Equal("$.version",Assert.Throws<DocumentFormatException>(()=>Format.Read(text)).Path);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    public void NonFiniteFileNumbersAreRefusedWithAFormatDiagnostic(string number)
    {
        string text="""{"kind":"accept.scene","version":1,"objects":[{"id":"a","position":[NUMBER,0,0]}]}""".Replace("NUMBER",number,StringComparison.Ordinal);
        var e=Assert.Throws<DocumentFormatException>(()=>Format.Read(text));Assert.StartsWith("$",e.Path);Assert.NotEmpty(e.Message);
        if(number=="1e999")Assert.StartsWith("$.objects[0].position",e.Path,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void FilePropertiesRejectNonPlainValuesAtTheNamedProperty(string value)
    {
        string text="""{"kind":"accept.scene","version":1,"objects":[{"id":"a","properties":{"payload":VALUE}}]}""".Replace("VALUE",value,StringComparison.Ordinal);
        Assert.StartsWith("$.objects[0].properties.payload",Assert.Throws<DocumentFormatException>(()=>Format.Read(text)).Path,StringComparison.Ordinal);
    }

    [Fact]
    public void NonAsciiAndDistinctUnicodeSequencesRoundTripWithoutNormalization()
    {
        var o=Object("a") with {Properties=Object("a").Properties.SetItem("label",Json("\"café 東京 e\\u0301 é\""))};
        string text=Format.Write(Of(o));var copy=Format.Read(text);
        Assert.Equal("café 東京 e\u0301 é",copy.Find("a")!.Properties["label"].GetString());
        Assert.Equal(text,Format.Write(copy));
    }

    [Fact]
    public void DuplicateObjectIdsAreValidationErrors()
    {
        Assert.Contains(Validator.Validate(Of(Object("same"),Object("same"))),i=>i.Severity==Severity.Error);
        Assert.Throws<InvalidDocumentException>(()=>Session(Of(Object("same"),Object("same"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad id")]
    [InlineData(" ")]
    public void ClearlyMalformedIdsAreRefused(string id)
    {
        Assert.Contains(Validator.Validate(Of(Object(id))),i=>i.Severity==Severity.Error);
    }

    [Fact]
    public void ParentCyclesAreRefusedButNamedReferenceCyclesAreAllowed()
    {
        Assert.Contains(Validator.Validate(Of(Object("a","b"),Object("b","a"))),i=>i.Severity==Severity.Error);
        var a=Object("a");var b=Object("b");
        Assert.Empty(Validator.Validate(Of(a with {References=a.References.Add("peer","b")},b with {References=b.References.Add("peer","a")})));
    }

    [Fact]
    public void DanglingParentNamesTheChildAndMissingTarget()
    {
        var issue=Assert.Single(Validator.Validate(Of(Object("child","gone"))));
        Assert.Equal("child",issue.ObjectId);Assert.Contains("gone",issue.Message);Assert.Contains("parent",issue.Path!,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DanglingReferenceNamesItsOwnerFieldAndTarget()
    {
        var a=Object("a");var issue=Assert.Single(Validator.Validate(Of(a with {References=a.References.Add("peer","gone")})));
        Assert.Equal("a",issue.ObjectId);Assert.Contains("gone",issue.Message);Assert.Contains("peer",issue.Path!);
    }

    [Fact]
    public void SelfReferenceIsAllowedWhileSelfParentIsNot()
    {
        var a=Object("a");Assert.Empty(Validator.Validate(Of(a with {References=a.References.Add("self","a")})));
        Assert.Contains(Validator.Validate(Of(a with {Parent="a"})),i=>i.Severity==Severity.Error);
    }

    [Fact]
    public void DeepParentChainRemainsValidAndKeepsDocumentOrder()
    {
        var objects=Enumerable.Range(0,128).Select(n=>Object("n"+n,n==0?null:"n"+(n-1))).ToArray();
        var d=Of(objects);Assert.Empty(Validator.Validate(d));
        Assert.Equal(objects.Select(o=>o.Id),Format.Read(Format.Write(d)).Objects.Select(o=>o.Id));
    }

    [Fact]
    public void RemovingAParentIsRefusedAndNamesItsChild()
    {
        var s=Session(Of(Object("root"),Object("child","root")));Refused(s,new RemoveObject("root"));
        Assert.Contains(s.LastIssues,i=>i.Message.Contains("child",StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingAReferencedTargetIsRefusedAndNamesTheReferrer()
    {
        var owner=Object("owner");var s=Session(Of(owner with {References=owner.References.Add("peer","target")},Object("target")));
        Refused(s,new RemoveObject("target"));Assert.Contains(s.LastIssues,i=>i.Message.Contains("owner",StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingTheOwnerOfOutgoingReferencesIsUndoable()
    {
        var a=Object("a");var d=Of(a with {References=a.References.Add("peer","b")},Object("b"));var s=Session(d);
        Assert.True(s.Execute(new RemoveObject("a")));Assert.Equal("b",Assert.Single(s.Current.Objects).Id);
        Assert.True(s.Undo());Assert.Equal(Format.Write(d),Format.Write(s.Current));
    }

    [Fact]
    public void MovingAnObjectIsImmutableUndoableAndJournaledAsAnArray()
    {
        var s=Session(Of(Object("a")));var held=s.Current;var position=new Vector3(.125f,.375f,.625f);
        Assert.True(s.Execute(new MoveObject("a",position)));Assert.Equal(Vector3.Zero,held.Find("a")!.Position);
        Assert.Equal(position,s.Current.Find("a")!.Position);
        var args=JsonNode.Parse(JournalFile.Write(s.Journal))!["entries"]![0]!["args"]!;
        Assert.Equal([.125f,.375f,.625f],args["position"]!.AsArray().Select(n=>n!.GetValue<float>()));
        Assert.True(s.Undo());Assert.Equal(Vector3.Zero,s.Current.Find("a")!.Position);
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("4.25")]
    [InlineData("true")]
    public void SettingPlainPropertiesIsUndoableAndPreservesJsonValue(string value)
    {
        var s=Session(Of(Object("a")));var json=Json(value);
        Assert.True(s.Execute(new SetSceneProperty("a","value",json)));
        Assert.Equal(json.GetRawText(),s.Current.Find("a")!.Properties["value"].GetRawText());
        Assert.True(s.Undo());Assert.Empty(s.Current.Find("a")!.Properties);
    }

    [Fact]
    public void NullCommandValueRemovesTheProperty()
    {
        var a=Object("a") with {Properties=Object("a").Properties.Add("value",Json("true"))};var s=Session(Of(a));
        Assert.True(s.Execute(new SetSceneProperty("a","value",null)));Assert.Empty(s.Current.Find("a")!.Properties);
        Assert.True(s.Undo());Assert.True(s.Current.Find("a")!.Properties["value"].GetBoolean());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void PropertyCommandsRefuseNonPlainValuesWithoutMutation(string value)
    {
        var s=Session(Of(Object("a")));Refused(s,new SetSceneProperty("a","value",Json(value)));
    }

    private static Scene Family()
    {
        var root=Object("root");var child=Object("child","root");
        return Of(root with {References=root.References.Add("child","child").Add("outside","outside")},
            child with {References=child.References.Add("root","root")},Object("outside"));
    }

    [Fact]
    public void DuplicateWholeGroupRemapsInternalParentsAndReferencesOnly()
    {
        var s=Session(Family());Assert.True(s.Execute(new DuplicateGroup(["root","child"])));
        Assert.Equal("copy_root",s.Current.Find("copy_child")!.Parent);
        Assert.Equal("copy_child",s.Current.Find("copy_root")!.References["child"]);
        Assert.Equal("copy_root",s.Current.Find("copy_child")!.References["root"]);
        Assert.Equal("outside",s.Current.Find("copy_root")!.References["outside"]);Assert.Equal(5,s.Current.Objects.Length);
    }

    [Fact]
    public void DuplicateParentWithoutChildKeepsTheChildReferenceOutside()
    {
        var s=Session(Family());Assert.True(s.Execute(new DuplicateGroup(["root"])));
        Assert.Equal("child",s.Current.Find("copy_root")!.References["child"]);
        Assert.Equal("root",s.Current.Find("child")!.Parent);Assert.Null(s.Current.Find("copy_child"));
    }

    [Fact]
    public void DuplicateChildWithoutParentKeepsTheOriginalParentOutside()
    {
        var s=Session(Family());Assert.True(s.Execute(new DuplicateGroup(["child"])));
        Assert.Equal("root",s.Current.Find("copy_child")!.Parent);
        Assert.Equal("root",s.Current.Find("copy_child")!.References["root"]);Assert.Null(s.Current.Find("copy_root"));
    }

    [Fact]
    public void DuplicateChosenIdsAreRecordedAndReplayMatches()
    {
        var basis=Family();var s=Session(basis);Assert.True(s.Execute(new DuplicateGroup(["root","child"])));
        var recorded=Assert.IsType<DuplicateGroup>(s.Catalog.FromEntry(Assert.Single(s.Journal)));
        Assert.NotNull(recorded.Map);Assert.Equal(2,recorded.Map.Count);
        var replay=DocumentSession<Scene>.Replay(basis,JournalFile.Read(JournalFile.Write(s.Journal)),Format,Validator,s.Catalog);
        Assert.Equal(Format.Write(s.Current),Format.Write(replay.Current));
    }

    [Fact]
    public void DuplicateRetriesExistingPrefixCollisions()
    {
        var d=Family() with {Objects=Family().Objects.Add(Object("copy_root"))};var s=Session(d);
        Assert.True(s.Execute(new DuplicateGroup(["root"])));
        Assert.Equal(5,s.Current.Objects.Length);Assert.Equal(5,s.Current.Objects.Select(o=>o.Id).Distinct().Count());
        Assert.NotNull(s.Current.Find("copy_root"));Assert.Empty(Validator.Validate(s.Current));
        var resolved=Assert.IsType<DuplicateGroup>(s.Catalog.FromEntry(Assert.Single(s.Journal)));
        Assert.NotNull(resolved.Map);Assert.NotEqual("copy_root",resolved.Map["root"]);
        var replay=DocumentSession<Scene>.Replay(d,JournalFile.Read(JournalFile.Write(s.Journal)),Format,Validator,s.Catalog);
        Assert.Equal(Format.Write(s.Current),Format.Write(replay.Current));
    }

    [Fact]
    public void DuplicatingUnknownIdRefusesWithoutMutation()
    {
        var s=Session(Family());Refused(s,new DuplicateGroup(["gone"]));
    }

    [Fact]
    public void GeneratedRoundtripFixtureRetainsOffGridPositionAndUnicode()
    {
        using var scratch=new Scratch();string path=scratch.Bundle("roundtrip.scene.json");var f=new SceneFormat("fixture-scene");
        var d=f.Read(File.ReadAllText(path));Assert.Empty(Validator.Validate(d));
        Assert.Equal(new Vector3(.125f,.375f,.625f),d.Find("first")!.Position);
        Assert.Equal("café",d.Find("first")!.Properties["label"].GetString());
        Assert.Equal(f.Write(d),f.Write(f.Read(f.Write(d))));
    }

    [Theory]
    [InlineData("unknown_property.scene.json","$.unexpected")]
    [InlineData("duplicate_property.scene.json","$.version")]
    [InlineData("future_version.scene.json","$.version")]
    public void GeneratedStrictNegativeFixtureNamesItsPath(string file,string path)
    {
        using var scratch=new Scratch();var f=new SceneFormat("fixture-scene");
        Assert.Equal(path,Assert.Throws<DocumentFormatException>(()=>f.Read(File.ReadAllText(scratch.Bundle(file)))).Path);
    }

    [Fact]
    public void GeneratedDuplicateIdFixtureFailsWholeDocumentValidation()
    {
        using var scratch=new Scratch();var f=new SceneFormat("fixture-scene");
        var d=f.Read(File.ReadAllText(scratch.Bundle("duplicate_ids.scene.json")));
        Assert.Contains(Validator.Validate(d),i=>i.Severity==Severity.Error);
    }

    [Fact]
    public void GeneratedMissingAssetFixtureKeepsAllObjectsAndOriginalPath()
    {
        using var scratch=new Scratch();var f=new SceneFormat("fixture-scene");
        var d=f.Read(File.ReadAllText(scratch.Bundle("missing.scene.json")));Assert.Equal(3,d.Objects.Length);
        Assert.Empty(Validator.Validate(d));var issues=SceneAssets.Check(d,asset=>asset!="missing_asset.glb");
        Assert.Equal("placeholder",Assert.Single(issues).ObjectId);
        var copy=f.Read(f.Write(d));Assert.Equal("missing_asset.glb",copy.Find("placeholder")!.Asset);
        Assert.Equal(["healthy","placeholder","textured"],copy.Objects.Select(o=>o.Id));
    }

    [Fact]
    public void GeneratedCloneBundleMatchesItsExpectedInternalAndExternalLinks()
    {
        using var scratch=new Scratch();var f=new SceneFormat("fixture-group");
        var source=f.Read(File.ReadAllText(scratch.Bundle("source.group.json")));
        var expected=f.Read(File.ReadAllText(scratch.Bundle("expected_clone.group.json")));
        var d=source with {Objects=source.Objects.Add(Object("outside"))};var s=new DocumentSession<Scene>(f,Validator,SceneKit.Catalog(),d);
        Assert.True(s.Execute(new DuplicateGroup(["root","child"])));
        var copies=new Scene(s.Current.Objects.Where(o=>o.Id.StartsWith("copy_",StringComparison.Ordinal)).ToImmutableArray());
        Assert.Equal(f.Write(expected),f.Write(copies));
    }

    [Fact]
    public void MissingAssetIssuesArePerObjectAndEmptyGroupsAreNotAssets()
    {
        var d=Of(SceneObject.At("a","missing.glb",Vector3.Zero),SceneObject.At("b","missing.glb",Vector3.One),Object("group"));
        int calls=0;var issues=SceneAssets.Check(d,_=>{calls++;return false;});
        Assert.Equal(2,calls);Assert.Equal(["a","b"],issues.Select(i=>i.ObjectId));
        Assert.Equal(3,d.Objects.Length);Assert.Null(d.Find("group")!.Asset);
    }

    [Fact]
    public void MinimalAddObjectJournalUsesTheDocumentShapeAndDefaults()
    {
        string text="""{"kind":"journal","version":1,"entries":[{"op":"do","command":"scene.add","version":1,"args":{"object":{"id":"cup"}}}]}""";
        var s=Session();s.Apply(JournalFile.Read(text));var o=Assert.Single(s.Current.Objects);
        Assert.Equal("cup",o.Id);Assert.Equal(Vector3.Zero,o.Position);Assert.Equal(Quaternion.Identity,o.Rotation);Assert.Equal(Vector3.One,o.Scale);
    }

    [Fact]
    public void RotationRefusesNonUnitQuaternionRatherThanNormalizingIt()
    {
        var s=Session(Of(Object("a")));Refused(s,new RotateObject("a",new Quaternion(0,0,0,2)));
        Assert.Equal(Quaternion.Identity,s.Current.Find("a")!.Rotation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ScaleRefusesZeroOrNonFiniteAxes(float x)
    {
        var s=Session(Of(Object("a")));Refused(s,new ScaleObject("a",new Vector3(x,1,1)));
    }

    [Fact]
    public void SetParentRejectsACycleAndClearsAnExistingParent()
    {
        var s=Session(Of(Object("root"),Object("child","root")));Refused(s,new SetParent("root","child"));
        Assert.True(s.Execute(new SetParent("child",null)));Assert.Null(s.Current.Find("child")!.Parent);
    }

    [Fact]
    public void SetAssetKeepsMissingAssetAsAPlaceholderAndCanClearIt()
    {
        var s=Session(Of(Object("a")));Assert.True(s.Execute(new SetAsset("a","missing.glb")));
        Assert.Equal("missing.glb",s.Current.Find("a")!.Asset);Assert.Single(SceneAssets.Check(s.Current,_=>false));
        Assert.True(s.Execute(new SetAsset("a",null)));Assert.Null(s.Current.Find("a")!.Asset);
    }

    [Fact]
    public void SetReferenceRejectsMissingTargetAndNullRemovesAnExistingLink()
    {
        var s=Session(Of(Object("a"),Object("b")));Refused(s,new SetReference("a","peer","gone"));
        Assert.True(s.Execute(new SetReference("a","peer","b")));Assert.Equal("b",s.Current.Find("a")!.References["peer"]);
        Assert.True(s.Execute(new SetReference("a","peer",null)));Assert.Empty(s.Current.Find("a")!.References);
    }

    [Fact]
    public void SceneLinksExposeParentsAndReferencesButNotOpaqueAssetPaths()
    {
        var a=Object("a","b") with {Asset="b"};
        a=a with {References=a.References.Add("peer","b")};var d=Of(a,Object("b"));var schema=new SceneLinks();
        Assert.Equal(["a","b"],schema.ObjectIds(d));
        var links=schema.Links(d).ToArray();Assert.Equal(2,links.Length);
        Assert.Contains(links,l=>l.Kind==SceneLinks.Inside&&l.From=="a"&&l.To=="b");
        Assert.Contains(links,l=>l.Kind==SceneLinks.RefersTo&&l.From=="a"&&l.To=="b");
        Assert.All(links,l=>Assert.Equal(LinkTarget.Object,l.Kind.Target));
    }
}
