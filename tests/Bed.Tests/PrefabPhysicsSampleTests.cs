using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Launcher;
using Xunit;
namespace Bed.Tests;

/// <summary>The shipped prefab-physics sample opens through the SceneStarter adapter, keeps its prefab copies, override
/// and three body kinds, and Plays through the physics starter without changing saved files.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class PrefabPhysicsSampleTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private const string CratePrefab="ba5e0000000000000000000000000ab1",CrateRoot="ba5e00000000000000000000000000b0";
    private static string Sample([CallerFilePath] string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../samples/PrefabPhysicsProject"));
    private static string CopyProject(string root)
    {
        var project=Path.Combine(root,"prefab physics");var source=Sample();
        foreach(var path in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        {
            var relative=Path.GetRelativePath(source,path).Replace('\\','/');
            if(relative.StartsWith("game/",StringComparison.Ordinal)||relative.StartsWith(".bed/cache/",StringComparison.Ordinal)||relative.StartsWith(".bed/generated/",StringComparison.Ordinal))continue;
            var target=Path.Combine(project,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(path,target);
        }
        // Install the physics game the way the release does: the exact runtime inventory under the project's game folder.
        var inventory=(string[])typeof(PhysicsStarter.Program).Assembly.GetType("SceneStarter.PhysicsRuntimeInventory")!.GetField("Files",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        foreach(var file in inventory){var target=Path.Combine(project,"game",file);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(AppContext.BaseDirectory,"samples/PhysicsStarter",file),target);}
        return project;
    }
    private static Dictionary<string,string> Saved(string project)=>new[]{"data","assets","schemas"}.SelectMany(folder=>Directory.EnumerateFiles(Path.Combine(project,folder),"*",SearchOption.AllDirectories))
        .Append(Path.Combine(project,".bed","project.json")).Append(Path.Combine(project,"reference_map.json"))
        .ToDictionary(path=>Path.GetRelativePath(project,path).Replace('\\','/'),path=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    [Fact]
    public void Sample_opens_with_prefab_copies_override_and_three_body_kinds_without_changing_saved_files()
    {
        using var temporary=new CapturedExportTests.Owned();var project=CopyProject(temporary.Root);var before=Saved(project);
        var settings=Scene2BuildSettings.ReadProject(File.ReadAllText(Path.Combine(project,".bed","project.json")))!;
        Assert.True(settings.SupportsPhysics);Assert.Equal("data/level.json",settings.Source);Assert.Equal([SceneSpace.ThreeD],settings.Spaces);
        Assert.Equal(Scene2BuildSettings.WriteProject("{}",new("data/level.json",".bed/generated/scenes/level.json",[SceneSpace.ThreeD]){SupportsPhysics=true}).ReplaceLineEndings("\n")+"\n",
            File.ReadAllText(Path.Combine(project,".bed","project.json")));

        var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);var adapter=Assert.Single(loaded.Loaded);
        var paths=new ProjectPaths(Path.Combine(project,"data"),Path.Combine(project,"schemas")){Root=project,Map=Path.Combine(project,"reference_map.json")};
        Assert.True(adapter.Recognises(paths));Assert.Equal("game/PhysicsStarter.exe",adapter.DefaultPlay(paths)!.Program);
        var data=adapter.Open(paths);
        Assert.Contains(data.Files,f=>f.Name=="level.json");Assert.Contains(data.Files,f=>f.Name=="crate.json");

        var text=File.ReadAllText(Path.Combine(project,"data","level.json"));var level=new Scene2Format().Read(text);
        Assert.Equal(text,new Scene2Format().Write(level));
        Assert.Equal(SceneSpace.ThreeD,level.Space);Assert.Equal(new System.Numerics.Vector3(0,-9.8f,0),level.Physics!.Gravity);Assert.Equal(.01f,level.Physics.FixedStepSeconds);
        Assert.Equal(3,level.Instances.Length);Assert.All(level.Instances,i=>Assert.Equal(CratePrefab,i.Source));
        var source=new PrefabDefinitionFormat().Read(File.ReadAllText(Path.Combine(project,"data","crate.json")));
        var accepted=Assert.Single(level.Bases).Definition;
        Assert.Equal(new PrefabDefinitionFormat().Write(source),new PrefabDefinitionFormat().Write(accepted));
        var overridden=Assert.Single(level.Instances,i=>i.Overrides.Length>0);var edit=Assert.Single(overridden.Overrides);
        Assert.Equal(CrateRoot,edit.Node);Assert.Equal("/body/massKg",edit.Field);

        var resolved=Scene2Resolver.Resolve(level).Objects.ToDictionary(o=>o.Object.Id);
        var crates=level.Instances.Select(i=>resolved[i.NodeIds[CrateRoot]]).ToArray();
        Assert.All(crates,c=>{Assert.Equal(SceneBodyMotion.Dynamic,c.Body!.Motion);Assert.Equal("ba5e0000000000000000000000000c01",c.Object.Asset);Assert.Equal(.6f,c.Body.Friction);});
        Assert.Equal([1f,1f,5f],crates.Select(c=>c.Body!.MassKg!.Value));
        Assert.Equal(SceneBodyMotion.Static,resolved["floor"].Body!.Motion);Assert.Equal(SceneBodyMotion.Kinematic,resolved["paddle"].Body!.Motion);
        Assert.Equal(5,resolved.Values.Count(o=>o.Body is not null));
        // Scenery (fence, trees, ball) is drawn but has no body, so the physics game sees only the five bodies.
        Assert.All(new[]{"fence","tree","tree_small","ball"},id=>{Assert.Null(resolved[id].Body);Assert.NotNull(resolved[id].Object.Asset);});
        Assert.Equal(before,Saved(project));
    }

    [Fact]
    public void Packaged_route_Play_runs_the_physics_starter_on_saved_files_and_exits_zero()
    {
        using var temporary=new CapturedExportTests.Owned();var project=CopyProject(temporary.Root);var before=Saved(project);
        // A fresh copy has no generated scene: plain Play refuses and asks for Build and Play. Opening publishes the asset cache.
        using(var stale=new StringWriter())using(var refused=new StringWriter())
        {Assert.Equal(1,Program.Run(["--probe-play",project,"--timeout","60"],stale,refused,"unused-probe-settings"));Assert.Contains("Build and Play",refused.ToString());}
        // The build step BED asks the person to allow once; the packaged check runs the same command directly.
        var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(project,"game","PhysicsStarter.exe")){WorkingDirectory=project,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add("--compile-scene2");info.ArgumentList.Add(".");
        using(var compile=System.Diagnostics.Process.Start(info)!)
        {var built=compile.StandardOutput.ReadToEnd();var why=compile.StandardError.ReadToEnd();Assert.True(compile.WaitForExit(30000));Assert.True(compile.ExitCode==0,built+why);}
        using var output=new StringWriter();using var errors=new StringWriter();
        var code=Program.Run(["--probe-play",project,"--timeout","60","--probe-args","--seconds 0"],output,errors,"unused-probe-settings");Assert.True(code==0,errors.ToString());
        Assert.Equal("",errors.ToString());
        using var json=JsonDocument.Parse(Assert.Single(output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("passed",json.RootElement.GetProperty("status").GetString());Assert.Equal(0,json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("level.json",json.RootElement.GetProperty("document").GetString());
        Assert.Contains(json.RootElement.GetProperty("output").EnumerateArray(),line=>!line.GetProperty("error").GetBoolean()
            &&line.GetProperty("text").GetString()!.StartsWith("PhysicsStarter loaded saved physics: 5 bodies",StringComparison.Ordinal));
        Assert.Equal(before,Saved(project));
        // A bounded run of exactly 300 fixed steps (3 simulated seconds): the stack falls and the paddle slides.
        var run=new System.Diagnostics.ProcessStartInfo(Path.Combine(project,"game","PhysicsStarter.exe")){WorkingDirectory=project,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"--project",".","--frames","300"})run.ArgumentList.Add(arg);
        string said;using(var game=System.Diagnostics.Process.Start(run)!){said=game.StandardOutput.ReadToEnd();log.WriteLine(said);var why=game.StandardError.ReadToEnd();Assert.True(game.WaitForExit(30000));Assert.True(game.ExitCode==0,said+why);}
        Assert.Contains("PhysicsStarter loaded saved physics: 5 bodies, 300 fixed ticks",said);Assert.Contains("Simulated 3.00 seconds.",said);
        var rest=System.Text.RegularExpressions.Regex.Match(said,@"Top dynamic body crate_3_box started at \(0\.30, 3\.00, 0\.00\) and came to rest at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)\.");
        Assert.True(rest.Success,said);Assert.True(double.Parse(rest.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture)<2.9,said);
        var slid=System.Text.RegularExpressions.Regex.Match(said,@"Kinematic body paddle slid along X and travelled ([\d.]+) m\.");
        // Three quarters of a 4-second, 1.5 m sine: out 1.5, back 1.5, out 1.5 the other way.
        Assert.True(slid.Success,said);Assert.InRange(double.Parse(slid.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture),4.4,4.6);
        Assert.Equal(before,Saved(project));
    }
}
