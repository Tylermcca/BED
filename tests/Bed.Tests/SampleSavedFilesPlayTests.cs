using System.Diagnostics;
using System.Numerics;
using Bed;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Xunit;
namespace Bed.Tests;

public sealed class SampleSavedFilesPlayTests
{
    private const string Id="0123456789abcdef0123456789abcdef";
    private const string Text="""{"kind":"scene","version":1,"objects":[{"id":"root","position":[2,3,4]},{"id":"child","parent":"root","asset":"0123456789abcdef0123456789abcdef","position":[1,0,0],"references":{"anchor":"root"}}]}""";
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("saved-sample-").FullName;
        public readonly string Level;
        public Fixture(bool publish=true)
        {
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            Level=Path.Combine(Root,"data","level.json");File.WriteAllText(Level,Text);
            if(publish)
            {
                using var producer=new AssetCatalogPublisher(Root);
                var mesh=DerivedMeshCodec.Encode(new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]) {Uv0=[new(-2,.2f),Vector2.Zero,Vector2.One],Uv0Origin=MeshUv0Origin.ObjBottomLeft});
                var stage=producer.Prepare("self-authored-generation",new Dictionary<string,byte[]>{{Id+".bin",mesh}});Assert.True(producer.Publish(stage,out var why),why);
            }
        }
        public void Dispose(){Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Root,StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Fact]
    public void Saved_level_and_published_geometry_use_the_runtime_without_editor_dependencies_or_protocol()
    {
        using var f=new Fixture();var saved=SceneStarter.SavedScene.Open(f.Root);var child=Assert.Single(saved.Level.Scene.Objects,item=>item.Id=="child");
        Assert.Equal("root",child.Parent);Assert.Equal("root",child.References["anchor"]);Assert.Equal(new Vector3(1,0,0),child.Position);
        var mesh=Assert.Single(saved.Meshes).Value;Assert.Equal(BedMeshUv0Origin.ObjBottomLeft,mesh.Uv0Origin);Assert.Equal(new Vector2(-2,.2f),mesh.Uv0![0]);
        Assert.Equal(new[]{0,1,2},mesh.Indices);Assert.Equal(Text,File.ReadAllText(f.Level));
        Assert.DoesNotContain(typeof(SceneStarter.Program).Assembly.GetReferencedAssemblies(),name=>name.Name!.Contains("Editor",StringComparison.Ordinal));
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(0,SceneStarter.Program.Run(["--project",f.Root,"--seconds","0"],output,error));
        Assert.Contains("2 objects, 1 meshes",output.ToString());Assert.Empty(error.ToString());Assert.DoesNotContain("ready",output.ToString(),StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Copied_project_generation_layout_uses_the_identical_reader_and_retains_identity_transforms_and_hashes()
    {
        using var f=new Fixture();using var copy=new Fixture(false);
        foreach(var path in Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).Where(path=>Path.GetFileName(path)!="owner.lock"))
        {var destination=Path.Combine(copy.Root,Path.GetRelativePath(f.Root,path));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination,true);}
        var original=SceneStarter.SavedScene.Open(f.Root);var loaded=SceneStarter.SavedScene.Open(copy.Root);
        Assert.Equal(original.Assets.Id,loaded.Assets.Id);Assert.Equal(original.Assets.Revision,loaded.Assets.Revision);
        Assert.Equal(original.Assets.ReadAsset(Id),loaded.Assets.ReadAsset(Id));Assert.Equal(original.Meshes[Id].Positions,loaded.Meshes[Id].Positions);
        Assert.Equal(original.Level.Scene.Objects.Select(item=>(item.Id,item.Parent,item.Position,item.Rotation,item.Scale)),loaded.Level.Scene.Objects.Select(item=>(item.Id,item.Parent,item.Position,item.Rotation,item.Scale)));
        Assert.Equal("root",loaded.Level.Scene.Objects.Single(item=>item.Id=="child").References["anchor"]);
    }
    [Fact]
    public void Missing_generation_refuses_without_saving_or_creating_a_cache()
    {
        using var f=new Fixture(false);using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(3,SceneStarter.Program.Run(["--project",f.Root,"--seconds","0"],output,error));Assert.Contains(ProjectAssets.MissingGeneration,error.ToString());
        Assert.Empty(output.ToString());Assert.False(Directory.Exists(Path.Combine(f.Root,".bed")));Assert.Equal(Text,File.ReadAllText(f.Level));
    }
    [Fact]
    public void Changed_derived_bytes_refuse_without_repair_or_false_success()
    {
        using var f=new Fixture();var asset=Assert.Single(Directory.GetFiles(Path.Combine(f.Root,".bed","cache","generations"),Id+".bin",SearchOption.AllDirectories));
        File.WriteAllBytes(asset,[99]);using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(3,SceneStarter.Program.Run(["--project",f.Root,"--seconds","0"],output,error));Assert.Contains("refused",error.ToString());Assert.Empty(output.ToString());
        Assert.Equal(new byte[]{99},File.ReadAllBytes(asset));Assert.Equal(Text,File.ReadAllText(f.Level));
    }
    [Fact]
    public void Adapter_default_uses_project_owned_game_and_needs_no_external_approval()
    {
        using var f=new Fixture();var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);
        var adapter=Assert.Single(loaded.Loaded);var settings=Assert.IsType<PlaySettings>(adapter.DefaultPlay(new(Path.Combine(f.Root,"data"),Path.Combine(f.Root,"schemas"),null)));
        Assert.Equal(new[]{"--project","."},settings.Arguments);Assert.Null(settings.Build);Assert.Equal(".",settings.WorkingFolder);
        Assert.True(PlaySettingsResolver.TryResolve(f.Root,settings,out var launch,out var why),why);
        Assert.Equal(Path.Combine(f.Root,"game","SceneStarter.exe"),launch!.Run.Executable);
        Assert.Equal(f.Root,launch.Run.WorkingDirectory);Assert.Null(new PlayApprovals(Path.Combine(f.Root,"preferences")).NeedsApproval(launch,false));
    }
    [Fact]
    public void Generic_adapter_surface_no_longer_declares_protocol_launch_or_snapshot_members()
    {
        Assert.DoesNotContain(typeof(IProjectAdapter).GetMembers(),member=>member.Name is "LinkedPlay" or "PlaySnapshot");
        Assert.Contains(typeof(IProjectAdapter).GetMethods(),method=>method.Name=="DefaultPlay");
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void Actual_owned_sample_process_uses_plain_saved_files_and_reports_exit_without_ready(bool prepared)
    {
        using var f=new Fixture(prepared);var program=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter","SceneStarter.exe");
        using var host=new ProjectPlayHost(new(new(program,f.Root,["--project",f.Root,"--seconds","0"]),null,f.Root,[]),TimeProvider.System,TimeSpan.FromSeconds(2));
        Assert.True(host.Start(false),host.Status);Assert.Equal(ProjectPlayState.Running,host.State);
        var time=Stopwatch.StartNew();while(!host.HasCompleted&&time.Elapsed<TimeSpan.FromSeconds(5)){host.Update();Thread.Sleep(5);}host.Update();
        Assert.True(host.HasCompleted,host.Status);Assert.Equal(prepared?ProjectPlayState.Stopped:ProjectPlayState.Failed,host.State);Assert.Equal(prepared?0:3,host.ExitCode);
        var lines=host.DrainLogs();Assert.DoesNotContain(lines,line=>line.Text.Contains("\"kind\"",StringComparison.Ordinal)||line.Text.Contains("Ready",StringComparison.Ordinal));
        if(prepared)Assert.Contains(lines,line=>line.Text.Contains("loaded saved level",StringComparison.Ordinal));else Assert.Contains(lines,line=>line.Error&&line.Text.Contains(ProjectAssets.MissingGeneration,StringComparison.Ordinal));
        Assert.Equal(Text,File.ReadAllText(f.Level));
    }
}
