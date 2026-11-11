using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Cli = ImportCheck.Program;

namespace Bed.Tests;

public sealed class ImportCheckTests
{
    private static string Fixtures()
    {
        for (DirectoryInfo? d=new(AppContext.BaseDirectory);d is not null;d=d.Parent)
        {
            string path=Path.Combine(d.FullName,"fixtures","import");
            if(File.Exists(Path.Combine(path,"expected.json")))return path;
        }
        throw new InvalidOperationException("Fixture directory was not copied into the isolated test root.");
    }
    private static string Fixture(string file)=>Path.Combine(Fixtures(),file);
    private static (int Code,string Out,string Err) Run(params string[] args)
    {
        using var o=new StringWriter();using var e=new StringWriter();int code=Cli.Run(args,o,e);
        return(code,o.ToString().Replace("\r\n","\n",StringComparison.Ordinal),e.ToString().Replace("\r\n","\n",StringComparison.Ordinal));
    }
    private static string[] Lines(string text)=>text.Split('\n',StringSplitOptions.RemoveEmptyEntries);
    public static IEnumerable<object[]> GltfFixtures()
    {
        using var data=JsonDocument.Parse(File.ReadAllText(Fixture("expected.json")));
        foreach(var c in data.RootElement.GetProperty("cases").EnumerateArray())
        {
            var file=c.GetProperty("file");
            if(file.ValueKind!=JsonValueKind.String)continue;
            string name=file.GetString()!;
            if(Path.GetExtension(name) is not (".glb" or ".gltf"))continue;
            string outcome=c.GetProperty("outcome").GetString()!;
            yield return [name,outcome,c.GetProperty("diagnostics").EnumerateArray().Select(x=>x.GetString()!).ToArray()];
        }
    }

    [Theory]
    [MemberData(nameof(GltfFixtures))]
    public void EveryNamedGltfFixtureMatchesItsExpectedOutcomeCodeAndAllDiagnostics(string file,string outcome,string[] diagnostics)
    {
        var r=Run(Fixture(file));
        Assert.Equal(outcome switch{"imports"=>0,"imports-with-warnings"=>1,_=>2},r.Code);
        Assert.Equal("outcome: "+outcome.Replace("-"," ",StringComparison.Ordinal),Lines(r.Out)[0]);
        Assert.Empty(r.Err);
        string[] actual=Lines(r.Out).Where(x=>x.StartsWith("warning:",StringComparison.Ordinal)||x.StartsWith("error:",StringComparison.Ordinal)).ToArray();
        Assert.Equal(diagnostics.Order(StringComparer.Ordinal),actual);
    }

    [Theory]
    [InlineData("gltf")]
    [InlineData("y_up_left_handed")]
    [InlineData("z_up_right_handed")]
    public void AllThreeFramesPlaceArrowMarkerAsExpectedJsonSays(string frame)
    {
        using var data=JsonDocument.Parse(File.ReadAllText(Fixture("expected.json")));
        var checks=data.RootElement.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("file").ValueKind==JsonValueKind.String&&x.GetProperty("file").GetString()=="im05_arrow.glb").GetProperty("checks");
        var expected=(frame=="gltf"?checks.GetProperty("input"):checks.GetProperty(frame)).GetProperty("marker").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
        var r=Run(Fixture("im05_arrow.glb"),"--frame",frame);Assert.Equal(0,r.Code);Assert.Empty(r.Err);
        string line=Assert.Single(Lines(r.Out),x=>x.StartsWith("marker: \"RightMarker\";",StringComparison.Ordinal));
        var match=Regex.Match(line,@"position=\(([^)]+)\)");Assert.True(match.Success);
        var actual=match.Groups[1].Value.Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(3,actual.Length);
        for(int i=0;i<3;i++)Assert.InRange(Math.Abs(expected[i]-actual[i]),0,checks.GetProperty("tolerance").GetDouble());
    }

    private static void Bad((int Code,string Out,string Err) r)
    {
        Assert.Equal(3,r.Code);Assert.Empty(r.Out);Assert.Contains("usage: importcheck",r.Err);Assert.Contains("error:",r.Err);
    }

    [Fact]
    public void MissingFileIsBadArgumentsWithUsage()
    {
        using var s=new Scratch();Bad(Run(s.Path("missing.glb")));
    }
    [Fact]
    public void NoArgumentsIsBadArgumentsWithUsage()=>Bad(Run());
    [Fact]
    public void UnknownFrameIsBadArgumentsWithUsage()=>Bad(Run(Fixture("im01_cube.glb"),"--frame","invented"));
    [Fact]
    public void MissingFrameValueIsBadArgumentsWithUsage()=>Bad(Run(Fixture("im01_cube.glb"),"--frame"));
    [Fact]
    public void ExtraArgumentIsBadArgumentsWithUsage()=>Bad(Run(Fixture("im01_cube.glb"),"--frame","gltf","extra"));
    [Fact]
    public void UnknownOptionIsBadArgumentsWithUsage()=>Bad(Run(Fixture("im01_cube.glb"),"--unknown","gltf"));
    [Fact]
    public void DirectoryIsNotAcceptedAsAFile()=>Bad(Run(Fixtures()));

    private sealed class Scratch : IDisposable
    {
        public string Root {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"A014-"+Guid.NewGuid().ToString("N"));
        public Scratch()=>Directory.CreateDirectory(Root);
        public string Path(string name)=>System.IO.Path.Combine(Root,name);
        public string Write(string name,string text){string p=Path(name);File.WriteAllText(p,text);return p;}
        public void Dispose()
        {
            string parent=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
            if(!System.IO.Path.GetFullPath(Root).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe scratch.");
            Directory.Delete(Root,true);
        }
    }
    private const string Obj="v 0 0 0\nv 1 0 0\nv 0 1 0\n";
    private const string Attributes="vt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\n";

    [Fact]
    public void ObjWithNormalsAndUvsImportsWithExitZero()
    {
        using var s=new Scratch();var r=Run(s.Write("model.obj",Obj+Attributes+"f 1/1/1 2/2/1 3/3/1\n"));
        Assert.Equal(0,r.Code);Assert.Empty(r.Err);Assert.StartsWith("outcome: imports\n",r.Out);
        Assert.Contains("normals=yes; uv0=yes",r.Out);Assert.Contains("triangles=1",r.Out);
    }

    [Fact]
    public void ObjWithoutUvsImportsWithWarningAndExitOne()
    {
        using var s=new Scratch();var r=Run(s.Write("model.OBJ",Obj+"f 1 2 3\n"));
        Assert.Equal(1,r.Code);Assert.Empty(r.Err);Assert.Contains("warning: model.OBJ: UV0 is absent",r.Out);
        Assert.Contains("normals=no; uv0=no",r.Out);
    }

    [Fact]
    public void ExistingFbxIsRefusedWithExitTwoAndExportAdvice()
    {
        using var s=new Scratch();string path=s.Path("unsupported.fbx");File.Copy(Fixture("im01_cube.glb"),path);
        var r=Run(path);Assert.Equal(2,r.Code);Assert.Contains("outcome: refused",r.Out);
        Assert.Contains("FBX is not supported; export glTF 2.0",r.Out);Assert.Empty(r.Err);
    }

    [Fact]
    public void TruncatedGlbIsRefusedWithoutAFalseMeshSummary()
    {
        using var s=new Scratch();byte[] bytes=File.ReadAllBytes(Fixture("im01_cube.glb"));
        string path=s.Path("truncated.glb");File.WriteAllBytes(path,bytes[..^11]);
        var r=Run(path);Assert.Equal(2,r.Code);Assert.Contains("length",r.Out,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mesh:",r.Out);Assert.DoesNotContain("outcome: imports",r.Out);Assert.Empty(r.Err);
    }

    [Fact]
    public void CubeSummaryReportsEveryRequiredMeshField()
    {
        var r=Run(Fixture("im01_cube.glb"));Assert.Equal(0,r.Code);
        Assert.Contains("node=\"Cube\"",r.Out);Assert.Contains("triangles=12",r.Out);
        Assert.Contains("bounds=(-0.5,-0.5,-0.5)..(0.5,0.5,0.5)",r.Out);
        Assert.Contains("normals=yes; uv0=yes; winding_flipped=no",r.Out);
    }

    [Fact]
    public void EmbeddedTextureSummaryNamesRolesColourSpacesAndSources()
    {
        var r=Run(Fixture("im02_embedded.glb"));Assert.Equal(0,r.Code);
        Assert.Contains("role=\"BaseColor\"; colour_space=sRGB; source=\"embedded\"; missing=no",r.Out);
        Assert.Contains("role=\"Normal\"; colour_space=linear; source=\"embedded\"; missing=no",r.Out);
    }

    [Fact]
    public void MissingTextureIsMarkedAndItsDiagnosticIsRetained()
    {
        var r=Run(Fixture("im07_missing_texture.glb"));Assert.Equal(1,r.Code);
        Assert.Contains("source=\"missing_normal.png\"; missing=yes",r.Out);
        Assert.Contains("warning: im07_missing_texture.glb: optional normal texture 'missing_normal.png' not found",r.Out);
    }

    [Fact]
    public void SkinJointAndClipCountsAndDurationArePrinted()
    {
        var r=Run(Fixture("im03_skin.glb"));Assert.Equal(1,r.Code);
        Assert.Contains("skins: 1\n",r.Out);Assert.Contains("joints: 3\n",r.Out);Assert.Contains("clips: 1\n",r.Out);
        Assert.Contains("clip: \"OneSecond\"; seconds=1\n",r.Out);
    }

    [Fact]
    public void RepeatedRunIsByteIdenticalAndMeshRowsAreOrdinallySorted()
    {
        string path=Fixture("im04_transforms.glb");var one=Run(path);var two=Run(path);
        Assert.Equal(one,two);
        var rows=Lines(one.Out).Where(x=>x.StartsWith("mesh:",StringComparison.Ordinal)).ToArray();
        Assert.True(rows.Length>=3);Assert.Equal(rows.Order(StringComparer.Ordinal),rows);
    }

    [Fact]
    public void NumericOutputDoesNotFollowTheCallersDecimalCommaLocale()
    {
        var old=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("en-US");var en=Run(Fixture("im05_arrow.glb"));
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");var de=Run(Fixture("im05_arrow.glb"));
            Assert.Equal(en,de);Assert.Contains("position=(-0.35,0.2,0.7)",de.Out);
        }
        finally{CultureInfo.CurrentCulture=old;}
    }

    [Fact]
    public void RelativeTextureNamesAreReportedAsFiles()
    {
        var r=Run(Fixture("im02_relative.glb"));Assert.Equal(0,r.Code);
        Assert.Contains("source=\"base.png\"; missing=no",r.Out);Assert.Contains("source=\"normal.png\"; missing=no",r.Out);
    }

    [Fact]
    public void DegenerateRepairWarnsAndReportsOnlyTheSurvivingTriangle()
    {
        var r=Run(Fixture("im06_degenerate.glb"));Assert.Equal(1,r.Code);
        Assert.Contains("removed 1 degenerate triangle",r.Out);Assert.Contains("triangles=1",r.Out);
    }

    [Fact]
    public void RefusedRequiredCompressionDoesNotPrintFakeSuccessfulGeometry()
    {
        var r=Run(Fixture("im06_draco.glb"));Assert.Equal(2,r.Code);
        Assert.Contains("KHR_draco_mesh_compression",r.Out);Assert.DoesNotContain("meshes:",r.Out);Assert.Empty(r.Err);
    }

    [Fact]
    public void SpaceInPathRemainsOneFileArgument()
    {
        using var s=new Scratch();string path=s.Path("a model with spaces.glb");File.Copy(Fixture("im01_cube.glb"),path);
        var r=Run(path);Assert.Equal(0,r.Code);Assert.Contains("triangles=12",r.Out);Assert.Empty(r.Err);
    }

    [Fact]
    public void InspectingAFileNeverWritesBesideOrChangesIt()
    {
        using var s=new Scratch();string path=s.Path("cube.glb");File.Copy(Fixture("im01_cube.glb"),path);byte[] before=File.ReadAllBytes(path);
        Assert.Equal(0,Run(path).Code);Assert.Equal([path],Directory.GetFiles(s.Root));Assert.Equal(before,File.ReadAllBytes(path));
    }
}
