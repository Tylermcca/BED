using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
namespace Brutal.Authoring.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class AssetsPanelPerf09Tests(ITestOutputHelper output)
{
    [Fact]
    public void Two_thousand_OBJ_panel_first_draw_scroll_search_and_last_row_selection_report_frame_costs()
    {
        var root=Directory.CreateTempSubdirectory("assets-panel-perf09-").FullName;
        try
        {
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(data,"level.json"),new SceneFormat().Write(Scene.Empty));
            for(var i=0;i<2000;i++)
            {
                var path=Path.Combine(assets,$"mesh-{i:D4}.obj");File.WriteAllText(path,$"v {i} 0 0\nv {i+1} 0 0\nv {i} 1 0\nf 1 2 3\n");
                var record=new AssetRecord((i+1).ToString("x32"),Path.GetFileName(path),AssetRecord.HashOf(path),"obj",1,JsonSerializer.SerializeToElement(new{scale=1}),new("fixture","self-authored","generated",Declared:AssetOrigin.Ours),AiUse.Forbidden);
                File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());
            }
            var originals=new[]{0,999,1999}.SelectMany(i=>new[]{Path.Combine(assets,$"mesh-{i:D4}.obj"),Path.Combine(assets,$"mesh-{i:D4}.obj.asset.json")}).ToDictionary(path=>path,File.ReadAllBytes);
            var preparation=Stopwatch.StartNew();using var opened=new AssetCatalogProvisioner().Open(new(data,schemas){Root=root})!;preparation.Stop();Assert.Empty(opened.Problems);Assert.Equal(2000,opened.Browser.Entries.Count);
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.BindSceneAssets(opened.Browser,opened.Catalog));Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);Assert.True(workspace.Show("level.json"));
            var panel=Assert.Single(workspace.SceneWindow!.Editor.Panels,p=>p.Id=="scene.assets");
            using var host=new HeadlessHost(1000,800){Drawing=true};EditorWindow<Scene>.Configure(ImGui.GetIO());
            void Warm(){Ui.Begin("Font warmup"u8);Ui.Text("Warm the common renderer before timing the first Assets draw."u8);Ui.End();}host.Frames(Warm,3);
            void Draw(){ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(950,750));Ui.Begin("Assets performance fixture"u8);panel.Draw();Ui.End();}
            double Frame(){var clock=Stopwatch.StartNew();host.Frame(Draw);return clock.Elapsed.TotalMilliseconds;}
            var first=Frame();Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="2000 of 2000 assets");
            host.Frames(Draw,2);var list=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label.EndsWith("Asset list",StringComparison.Ordinal));host.MoveMouse(Draw,list.Bounds.X+40,list.Bounds.Y+40);
            var scroll=new List<double>();for(var i=0;i<3;i++){ImGui.GetIO().AddMouseWheelEvent(0,-1000000);scroll.Add(Frame());}
            host.Frames(Draw,2);var last=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="mesh-1999.obj");
            Assert.InRange(last.Bounds.Y,list.Bounds.Y,list.Bounds.Y+list.Bounds.Height-last.Bounds.Height);
            host.Click(Draw,last.Bounds.X+20,last.Bounds.Y+last.Bounds.Height/2);host.Frames(Draw,2);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Model: mesh-1999.obj, 1 part.");
            var search=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Search assets");host.Click(Draw,search.Bounds.X+30,search.Bounds.Y+search.Bounds.Height/2);
            var watch=Stopwatch.StartNew();host.Type(Draw,"1999");watch.Stop();var searching=watch.Elapsed.TotalMilliseconds;
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="1 of 2000 assets");Assert.Equal("mesh-1999.obj",Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem).Label);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Model: mesh-1999.obj, 1 part.");
            foreach(var (path,bytes) in originals)Assert.Equal(bytes,File.ReadAllBytes(path));Assert.False(workspace.SceneWindow.Editor.Session.Dirty);Assert.Empty(workspace.Project.Dirty);
            output.WriteLine($"2,000 generated OBJ with real prepared catalog. Preparation (outside frame timing): {preparation.Elapsed.TotalMilliseconds:F3} ms.");
            output.WriteLine($"First Assets panel frame after common-font warmup: {first:F3} ms; CPU rasterization enabled.");
            output.WriteLine("Actual wheel-scrolling frames: "+string.Join(", ",scroll.Select(ms=>ms.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)))+" ms.");
            output.WriteLine($"Search input + resulting frame: {searching:F3} ms. Last-row selection exposes real model part count; source samples and scene remain unchanged.");
        }
        finally{Directory.Delete(root,true);}
    }
}
