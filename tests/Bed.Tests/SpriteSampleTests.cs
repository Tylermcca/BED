using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SpriteSampleTests
{
    private sealed class Copy:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("sprite-sample-").FullName;
        internal readonly string Settings=Directory.CreateTempSubdirectory("sprite-settings-").FullName;
        internal Copy()
        {
            var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
            var source=Path.Combine(repo!.FullName,"samples/SpriteLevelProject");
            foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
            {var relative=Path.GetRelativePath(source,file);if(relative.StartsWith("game"+Path.DirectorySeparatorChar)||relative.Contains("__pycache__")||relative.StartsWith(".bed"+Path.DirectorySeparatorChar+"cache"))continue;var target=Path.Combine(Root,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}
            var game=Directory.CreateDirectory(Path.Combine(Root,"game")).FullName;var runtime=Path.Combine(AppContext.BaseDirectory,"samples/SceneStarter");
            foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)File.Copy(Path.Combine(runtime,name),Path.Combine(game,name));
            var export=Directory.CreateDirectory(Path.Combine(game,"export")).FullName;foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)File.Copy(Path.Combine(runtime,name),Path.Combine(export,name));
        }
        public void Dispose(){Directory.Delete(Root,true);Directory.Delete(Settings,true);}
        internal void OpenAndCompile()
        {
            using var output=new StringWriter();using var error=new StringWriter();
            Assert.Equal(0,Bed.Launcher.Program.Run([Root,"--settings",Settings,"--shot",Path.Combine(Settings,"sample.png")],output,error,Settings));Assert.Empty(error.ToString());
            Assert.True(File.Exists(Path.Combine(Settings,"sample.png")));Assert.True(File.Exists(Path.Combine(Root,".bed/cache/current.json")));
            Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",Root],output,error));Assert.Empty(error.ToString());
        }
    }
    [Fact]
    public void Original_sample_opens_with_two_prefab_copies_seventeen_sprites_and_one_local_size_override()
    {
        using var copy=new Copy();var paths=ProjectPaths.In(copy.Root)!;var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);var adapter=Assert.Single(loaded.Loaded);
        var project=adapter.Open(paths);var host=DocumentHost.For(project);Assert.True(host.OpenScene2("level.json",out var entry,out var why),why);
        var scene=entry!.Session.Current;Assert.Equal(SceneSpace.TwoDXY,scene.Space);Assert.Null(scene.Physics);Assert.Equal(2,scene.Instances.Length);Assert.Single(scene.Bases);Assert.Empty(scene.Instances[0].Overrides);Assert.Single(scene.Instances[1].Overrides);
        var objects=Scene2Resolver.Resolve(scene).Objects;var sprites=objects.Where(o=>o.Sprite is not null).ToArray();Assert.Equal(17,sprites.Length);Assert.Equal(new[]{-20,-10,0,10},sprites.Select(o=>o.Sprite!.Order).Distinct().Order());
        Assert.Equal(new System.Numerics.Vector2(1.5f,2),objects.Single(o=>o.Object.Id=="post_1_flag").Sprite!.Size);Assert.Equal(new System.Numerics.Vector2(1.875f,2.5f),objects.Single(o=>o.Object.Id=="post_2_flag").Sprite!.Size);Assert.All(objects,o=>Assert.Null(o.Body));
    }
    [Fact]
    public void Saved_flat_sample_uses_the_real_launcher_Play_probe_and_exits_zero()
    {
        using var copy=new Copy();copy.OpenAndCompile();using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(0,Bed.Launcher.Program.Run(["--probe-play",copy.Root,"--probe-args","--seconds 0","--timeout","15"],output,errors,copy.Settings));Assert.Empty(errors.ToString());
        using var proof=JsonDocument.Parse(output.ToString());Assert.Equal("passed",proof.RootElement.GetProperty("status").GetString());Assert.True(proof.RootElement.GetProperty("completed").GetBoolean());Assert.Equal(0,proof.RootElement.GetProperty("exitCode").GetInt32());Assert.Contains(proof.RootElement.GetProperty("output").EnumerateArray(),line=>line.GetProperty("text").GetString()!.Contains("loaded saved level-v2: 19 objects, 0 meshes, 8 textures."));
    }
    [Fact]
    public void Saved_flat_sample_exports_through_the_real_launcher_menu_and_copied_runtime()
    {
        using var copy=new Copy();copy.OpenAndCompile();using var output=new StringWriter();using var errors=new StringWriter();var destination=Path.Combine(copy.Settings,"exported");
        Assert.Equal(0,Bed.Launcher.Program.Run(["--probe-export",copy.Root,"--destination",destination,"--timeout","20"],output,errors,copy.Settings));Assert.Empty(errors.ToString());
        using var proof=JsonDocument.Parse(output.ToString());Assert.Equal("passed",proof.RootElement.GetProperty("status").GetString());Assert.True(proof.RootElement.GetProperty("copied").GetBoolean());
        var exported=SceneStarter.SavedScene.Open(destination);Assert.Equal(17,exported.Level2!.Scene.Objects.Count(o=>o.Sprite is not null));Assert.Equal(8,exported.Textures.Count);Assert.False(Directory.Exists(Path.Combine(destination,"assets")));Assert.False(Directory.Exists(Path.Combine(destination,"data")));
        output.GetStringBuilder().Clear();Assert.Equal(0,SceneStarter.Program.Run(["--project",destination,"--seconds","0"],output,errors));Assert.Empty(errors.ToString());Assert.Contains("loaded saved level-v2",output.ToString());
    }
}
