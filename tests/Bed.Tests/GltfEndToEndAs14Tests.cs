using System.Diagnostics;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram=Bed.Launcher.Program;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class GltfEndToEndAs14Tests
{
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../.."));
    private static async Task Generate(string script,string assets,bool check=false)
    {
        var start=new ProcessStartInfo("python"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(script);start.ArgumentList.Add("--out");start.ArgumentList.Add(assets);if(check)start.ArgumentList.Add("--check");
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
        try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));Assert.True(child.ExitCode==0,await stdout+await stderr);}
        finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
    }
    // Observe the real supervisor and smoke; return their exact operations and copy authority unchanged.
    private sealed class ObservedExport(IGameExportHost inner):IGameExportHost
    {
        public GameExportPlan? Plan;
        public IGameBuildOperation? Operation;
        public GameBuildReceipt? Receipt;
        public GameExportSmokeResult? SmokeResult;
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)
        {Plan=plan;return Operation=inner.Start(spec,root,dirty,plan);}
        public async Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec command,CancellationToken cancellation)
        {Receipt=receipt;return SmokeResult=await inner.Smoke(plan,receipt,outputs,command,cancellation);}
    }
    [Fact]
    public async Task Generated_textured_GLB_imports_places_captures_material_Plays_and_exports_without_changing_shipped_level()
    {
        using var owned=new CapturedExportTests.Owned();
        var shippedLevel=File.ReadAllBytes(Path.Combine(SourceRoot(),"samples","SceneStarterProject","data","level.json"));
        var root=Directory.CreateDirectory(Path.Combine(owned.Root,"project")).FullName;
        var sample=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
        var originals=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var relative in new[]{"reference_map.json","data/level.json","schemas/scene.schema.json"}
            .Concat(Directory.GetFiles(Path.Combine(sample,"assets"),"*",SearchOption.AllDirectories).Select(file=>Path.GetRelativePath(sample,file))))
        {
            var bytes=File.ReadAllBytes(Path.Combine(sample,relative));originals.Add(relative,bytes);
            var copiedInput=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(copiedInput)!);File.WriteAllBytes(copiedInput,bytes);
        }
                var script=Path.Combine(SourceRoot(),"samples","SceneStarterProject","generate_textured_glb.py");
        await Generate(script,Path.Combine(root,"assets"));await Generate(script,Path.Combine(root,"assets"),check:true);
        foreach(var name in new[]{"textured-quad.glb","textured-quad.png","textured-quad.glb.asset.json","textured-quad.png.asset.json"})
            originals.Add("assets/"+name,File.ReadAllBytes(Path.Combine(root,"assets",name)));
        Directory.CreateDirectory(Path.Combine(root,"game"));
        foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)
            File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(root,"game",name));
        PlaySettingsStore.Save(root,new("game/SceneStarter.exe",["--project",".","--seconds","0"]));
        var settings=File.ReadAllBytes(Path.Combine(root,".bed","project.json"));
        using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var adapters=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(adapters.Problems);
        using var launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(owned.Root,"local","recent.json"))){Loaded=adapters.Loaded});
        var play=new SamplePlayJourneyTests.ObservedHost(Assert.IsType<ProjectPlayBridge>(launcher.ProjectPlayHost));launcher.ProjectPlayHost=play;
        var export=new ObservedExport(Assert.IsType<ExportBuildHost>(launcher.GameExportHost));launcher.GameExportHost=export;
        Assert.True(launcher.OpenFolder(root),launcher.Said);Assert.Equal("SceneStarter",launcher.Using!.Name);
        var workspace=launcher.Workspace!;host.Frames(launcher.Draw,4);
        void Frames()=>host.Frames(launcher.Draw,2);
        void Key(ImGuiKey key,bool ctrl=false){host.Press(launcher.Draw,key,ctrl:ctrl);Frames();}
        void Click(UiNode node)
        {Assert.True(node.Enabled,node.DisabledReason);Assert.False(node.Bounds.IsEmpty);host.Click(launcher.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);Frames();}
        void Button(string label)=>Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label));
        void Until(Func<bool> done,string step)
        {var timer=Stopwatch.StartNew();while(!done()&&timer.Elapsed<TimeSpan.FromSeconds(10)){host.Frame(launcher.Draw);Thread.Sleep(5);}Assert.True(done(),step+": "+workspace.Said+"; "+string.Join(" | ",Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Text).Select(n=>n.Label)));}
        Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.Contains("level.json",StringComparison.Ordinal)));
        var window=workspace.SceneWindow!;Assert.NotNull(window);var initialIds=window.Editor.Session.Current.Objects.Select(o=>o.Id).ToHashSet();
        Assert.Equal(originals["data/level.json"],File.ReadAllBytes(Path.Combine(root,"data","level.json")));
        var pointer=File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json"));
        const string crate="a5140000000000000000000000000001",texture="a5140000000000000000000000000002";
        Assert.NotNull(workspace.SceneAssets!.Select(crate));Assert.NotNull(workspace.SceneAssets.Select(texture));
        Key(ImGuiKey.Insert);Assert.True(window.Editor.IsOpen("scene.assets"));
        Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="textured-quad.glb"));
        Button("Add selected object");Assert.False(window.Editor.IsOpen("scene.assets"));
        var added=Assert.Single(window.Editor.Session.Current.Objects,o=>!initialIds.Contains(o.Id));Assert.Equal(crate,added.Asset);Assert.Equal(added.Id,window.Editor.Primary);
                var view=workspace.SceneViewport!;
        host.Drawing=true;host.Frame(launcher.Draw);host.Drawing=false;
        var item=Assert.Single(view.RenderRequest!.Items,i=>i.Id==added.Id);var payload=Assert.Single(item.Payloads);
        Assert.Equal(6,payload.Indices.Count);Assert.Equal(texture,payload.Material.TextureAssetGuid);
        Assert.Equal(System.Numerics.Vector4.One,payload.Material.BaseColor);
        Assert.Equal(workspace.SceneAssets!.Resolve(texture).Current!.SourceHash,payload.Material.TextureSourceHash);
        var mesh=Assert.Single(view.Source.Items(window.Editor.Session.Current),i=>i.Id==added.Id).Mesh;
        Assert.Equal(MeshUv0Origin.TopLeft,mesh.Uv0Origin);Assert.Equal(mesh.Uv0,payload.Uv0);
        Assert.Contains("Material textures are not displayed.",view.RenderStatus); // Honest CPU/offscreen limitation, not a textured-pixel success claim.
        var imported=SceneStarter.SavedScene.Open(root).Assets;
        var part=Assert.Single(imported.ReadMesh(crate).Parts);Assert.Equal(Guid.ParseExact(texture,"N"),part.Material!.Texture);
        var pixels=imported.ReadTexture(part.Material.Texture!.Value);Assert.Equal(8,pixels.Width);Assert.Equal(8,pixels.Height);
        var colors=new[]{new byte[]{240,24,32,255},new byte[]{24,224,40,255},new byte[]{24,40,232,255},new byte[]{240,224,24,255}};
        for(var y=0;y<8;y++)for(var x=0;x<8;x++)Assert.Equal(colors[(y/4)*2+x/4],pixels.Rgba.Skip((y*8+x)*4).Take(4));
        Assert.True(AssetPreview.TryCreate(8,8,pixels.Rgba,256,1024,8,256,out var preview,out var previewWhy),previewWhy);
        var camera=new Camera(new System.Numerics.Vector3(0,1,0),0,0,4);
        var nativeRequest=SceneRenderRequest.Capture(new(1,1,1,1,1,System.Numerics.Vector2.Zero,new(128,128),System.Numerics.Vector2.One),camera,[new("quad",System.Numerics.Matrix4x4.Identity,mesh,0xffffffff)]);
        // GitHub's machines have no graphics driver, so the real draw is checked only where one exists; the rest of the walk (place, save,
        // Play, export) still runs there. Anywhere else a missing driver fails the test, so a broken driver here can't pass quietly.
        byte[]? nativePixels=null;
        try{nativePixels=GltfOffscreenAs14.Draw(nativeRequest,preview!);}
        catch(Brutal.VulkanApi.VkResultError e)when(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true"&&e.Message.Contains("VK_ERROR_INCOMPATIBLE_DRIVER")){}
        var points=new[]{new System.Numerics.Vector3(-.5f,1.5f,0),new System.Numerics.Vector3(.5f,1.5f,0),new System.Numerics.Vector3(-.5f,.5f,0),new System.Numerics.Vector3(.5f,.5f,0)};
        if(nativePixels is not null)for(var quadrant=0;quadrant<4;quadrant++)
        {
            var point=camera.Project(points[quadrant],new(128,128))!.Value.Screen;var index=((int)point.Y*128+(int)point.X)*4;
            for(var channel=0;channel<4;channel++)Assert.InRange((int)nativePixels[index+channel],Math.Max(0,colors[quadrant][channel]-3),Math.Min(255,colors[quadrant][channel]+3));
        }
        void Text(string label,string value,ImGuiKey finish=ImGuiKey.Escape)
        {
            Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==label));
            Key(ImGuiKey.A,ctrl:true);host.Type(launcher.Draw,value);Key(ImGuiKey.Enter);Key(finish);
        }
        Text("Position X","4.5");
        var moved=window.Editor.Session.Current.Find(added.Id)!;Assert.Equal(4.5f,moved.Position.X);Assert.True(window.Editor.Session.Dirty);
        Key(ImGuiKey.S,ctrl:true);Until(()=>!window.Editor.Session.Dirty,"Save");
        var saved=File.ReadAllBytes(Path.Combine(root,"data","level.json"));Assert.NotEqual(originals["data/level.json"],saved);
        var expected=window.Editor.Session.Current;var savedLevel=SceneStarter.SavedScene.Open(root);
        Assert.Equal(new SceneFormat().Write(expected),new SceneFormat().Write(savedLevel.Level.Scene));
        Button("Play");Assert.False(workspace.ProjectPlaySavePending);
        Until(()=>play.Session is {HasCompleted:true,ExitCode:not null},"Play exit");
        Assert.Equal(0,play.Session!.ExitCode);Assert.Equal(1,play.Starts);Assert.Equal(0,play.Session.Stops);
        var loadedLine=$"SceneStarter loaded saved level: {expected.Objects.Length} objects, {savedLevel.Meshes.Count} meshes.";
        Assert.Equal(File.ReadAllText(Path.Combine(root,"data","level.json")),play.Session.SavedAtStart);
        Assert.Contains(play.Session.Logs,line=>!line.Error&&line.Text==loadedLine);
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(root,"data","level.json")));Assert.False(window.Editor.Session.Dirty);
        Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="File"));Click(Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Export build..."));
        Button("Build saved files");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder..."&&n.Enabled),"Export build and smoke");
        Assert.NotNull(export.Operation);Assert.NotNull(export.Receipt);Assert.True(export.Receipt.Succeeded,export.Receipt.Reason);Assert.Equal(0,export.Receipt.ExitCode);
        Assert.NotNull(export.SmokeResult!.Build);
        var destination=Path.Combine(owned.Root,"standalone");
        // Escape now closes the export dialog. Tab commits the field and reaches its copy control.
        Text("Destination folder",destination,ImGuiKey.Tab);
        Assert.Equal(destination,Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Destination folder").Value);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder...").Focused);
        Button("Copy build to folder...");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Status&&n.Label=="Export status: Build copied to "+destination),"Export copy");
        Assert.True(File.Exists(Path.Combine(destination,"SceneStarter.exe")));Assert.False(File.Exists(Path.Combine(destination,"scene.json")));Assert.False(File.Exists(Path.Combine(destination,"payload.bin")));
        foreach(var node in export.Plan!.Admission.Source.Nodes)
        {var bytes=new byte[node.Length];node.CopyTo(bytes);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(destination,node.Output.Destination)));}
        var standalone=SceneStarter.SavedScene.Open(destination);Assert.Equal(savedLevel.Assets.Id,standalone.Assets.Id);
        Assert.Equal(moved.Position,standalone.Level.Scene.Find(added.Id)!.Position);
        Assert.Equal(savedLevel.Assets.ReadAsset(crate),standalone.Assets.ReadAsset(crate));Assert.Equal(savedLevel.Assets.ReadAsset(texture),standalone.Assets.ReadAsset(texture));
        var start=new ProcessStartInfo(Path.Combine(destination,"SceneStarter.exe")){WorkingDirectory=destination,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"--project",".","--seconds","0"})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal(0,process.ExitCode);
            Assert.Contains(loadedLine,await output);Assert.Equal("",await error);
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        foreach(var (relative,bytes) in originals.Where(pair=>pair.Key!="data/level.json"))Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(root,relative)));
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(root,"data","level.json")));Assert.Equal(settings,File.ReadAllBytes(Path.Combine(root,".bed","project.json")));
        Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json")));Assert.False(window.Editor.Session.Dirty);
        Assert.Equal(shippedLevel,File.ReadAllBytes(Path.Combine(SourceRoot(),"samples","SceneStarterProject","data","level.json")));
    }
}
