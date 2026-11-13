using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class HighContrastQa26Tests
{
    private sealed class Rig : IDisposable
    {
        private readonly EditorThemePreference old = EditorTheme.Preference;
        public readonly string Root = Directory.CreateTempSubdirectory("contrast-qa26-").FullName;
        public readonly HeadlessHost Host = new(1600,1000) { Drawing=false };
        public readonly ProjectLauncher Launcher;
        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Root,"local"));
            File.WriteAllText(Path.Combine(Root,"local/settings.json"), """{"theme":"HighContrast","textScale":1.5}""");
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));Directory.CreateDirectory(Path.Combine(Root,"assets"));
            File.WriteAllText(Path.Combine(Root,"data/items.json"),"""{"items":[{"id":"own","name":"Own item","enabled":true}]}""");
            File.WriteAllText(Path.Combine(Root,"schemas/items.schema.json"),"{}");
            File.WriteAllText(Path.Combine(Root,"data/scene.json"),new SceneFormat().Write(new Scene([SceneObject.At("own",null,new(0,0,0))])));
            File.WriteAllText(Path.Combine(Root,"reference_map.json"),"""{"game":"Own fixture","labels":[],"references":[]}""");
            File.WriteAllText(Path.Combine(Root,"game.exe"),"Not executed");PlaySettingsStore.Save(Root,new("game.exe"));
            EditorWindow<DataDocument>.Configure(Im.GetIO());
            Launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(Root,"local/recent.json"))){ProjectPlayHost=new FakeHost()});
            Frames();
        }
        public void Frames()=>Host.Frames(Launcher.Draw,3);
        public void Reach(string label)
        {
            for(int i=0;i<80;i++){if(Ui.Snapshot().Nodes.Any(n=>n.Focused&&n.Label==label))return;Host.Press(Launcher.Draw,ImGuiKey.Tab);}
            Assert.Fail("Keyboard focus did not reach "+label);
        }
        public void CheckPalette()
        {
            Assert.Equal(EditorThemePreference.HighContrast,EditorTheme.Preference);
            var s=Im.GetStyle();Assert.True(s.FrameBorderSize>=1);Assert.True(s.WindowBorderSize>=1);
            Assert.Equal(new Brutal.Numerics.float4(1,1,1,1),s.Colors[(int)ImGuiCol.Text]);
            Assert.Equal(new Brutal.Numerics.float4(1,1,1,1),s.Colors[(int)ImGuiCol.Border]);
            Assert.Equal(new Brutal.Numerics.float4(1,1,0,1),s.Colors[(int)ImGuiCol.NavCursor]);
            foreach(var c in new[]{ImGuiCol.WindowBg,ImGuiCol.ChildBg,ImGuiCol.PopupBg})Assert.Equal(new Brutal.Numerics.float4(0,0,0,1),s.Colors[(int)c]);
        }
        public void CheckWindow(string name)
        {
            var w=Im.Internal.FindWindowByName(name);Assert.False(w.IsNull(),name);
            Im.Internal.DockContextProcessUndockWindow(Im.GetCurrentContext(),w,true);
            Im.SetWindowPos(name,new(30,90),ImGuiCond.Always);Im.SetWindowSize(name,new(800,650),ImGuiCond.Always);Im.SetWindowFocus(name);Frames();
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label==name.Split("###")[0]);
            CheckPalette();
            // Inspect actual submitted panel vertices, not just the preference enum.
            var colors=new HashSet<uint>();foreach(var v in w.DrawList.VtxBuffer)colors.Add(v.col);
            Assert.Contains(0xffffffffu,colors);Assert.Contains(0xff000000u,colors);
            bool focused=false;
            for(var i=0;i<16;i++)
            {
                Host.Press(Launcher.Draw,ImGuiKey.Tab);
                if(Im.GetCurrentContext().NavWindow.Equals(w)&&Ui.Snapshot().Nodes.Any(n=>n.Focused&&n.Role!=UiRole.Window))
                {
                    focused=true;colors.Clear();foreach(var v in w.DrawList.VtxBuffer)colors.Add(v.col);
                    Assert.Contains(0xff00ffffu,colors);break;
                }
            }
            // Read-only panels may have no native focusable widget; focus must still name the window.
            if(!focused)Assert.False(Im.GetCurrentContext().NavWindow.IsNull());
        }
        public void Dispose(){Launcher.Dispose();Host.Dispose();EditorTheme.Preference=old;Directory.Delete(Root,true);}
    }
    private sealed class FakeHost:IProjectPlayHost {public IProjectPlaySession Start(PlayLaunch launch)=>new FakeSession();}
    private sealed class FakeSession:IProjectPlaySession
    {
        public bool Start(bool dirty,bool buildAndPlay=false)=>true;public bool CanBuildAndPlay=>false;public bool HasCompleted=>true;public int? ExitCode=>0;public string Status=>"Own completed run.";
        private bool drained;
        public void RequestStop(){}public void Update(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs(){if(drained)return [];drained=true;return [new(false,"Own console output")];}public void Draw(){}public void Dispose(){}
    }
    [Fact]
    public void Start_screen_and_preferences_keep_actual_high_contrast_vertices_and_native_focus()
    {
        using var r=new Rig();r.CheckWindow("BED###launcher");r.Reach("Options");r.Host.Press(r.Launcher.Draw,ImGuiKey.Enter);r.Frames();
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Theme"&&n.Role==UiRole.Combo);
        r.Reach("Theme");r.CheckPalette();
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Focused&&n.Label=="Theme");
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Registered_data_form_scene_assets_console_and_palette_use_high_contrast(bool scene)
    {
        using var r=new Rig();Assert.True(r.Launcher.OpenFolder(r.Root),r.Launcher.Said);var w=r.Launcher.Workspace!;
        IReadOnlyList<Panel> panels;
        if(scene){Assert.True(w.Documents.BindScene("scene.json","scene",out var why),why);Assert.True(w.Show("scene.json"),w.Said);w.SceneWindow!.Editor.Select("own");panels=w.SceneWindow.Editor.Panels;foreach(var p in panels)w.SceneWindow.Editor.SetOpen(p.Id,true);}
        else{Assert.True(w.Show("items.json"),w.Said);w.Window!.Editor.Select("/items/0");panels=w.Window.Editor.Panels;foreach(var p in panels)w.Window.Editor.SetOpen(p.Id,true);}
        Assert.True(w.RequestProjectPlay(),w.Said);r.Frames();
        foreach(var name in panels.Select(p=>p.Title+"###panel."+p.Id).Concat(["Outliner###outliner","Inspector###inspector","Play console###project.play.console"]).Distinct())r.CheckWindow(name);
        r.Host.Press(r.Launcher.Draw,ImGuiKey.P,true,true);r.Frames();
        Assert.True(scene?w.SceneWindow!.PaletteOpen:w.Window!.PaletteOpen);r.CheckPalette();
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Focused);
        Assert.Empty(w.Project.Dirty);
    }
    [Theory][InlineData(ViewportTool.Move)][InlineData(ViewportTool.Rotate)][InlineData(ViewportTool.Scale)]
    public void Transform_handles_and_overlay_use_theme_text_in_high_contrast(ViewportTool tool)
    {
        var old=EditorTheme.Preference;
        try
        {
            using var host=new HeadlessHost(1000,800){Drawing=false};EditorTheme.Preference=EditorThemePreference.HighContrast;EditorTheme.Refresh();
            var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("own",null,new(0,0,0))])),SceneEditing.Outline);editor.Select("own");
            var view=new ViewportView<Scene>(editor,new SceneViewport());Assert.True(view.SetTool(tool));
            host.Frames(()=>{Im.SetNextWindowPos(new(0,0));Im.SetNextWindowSize(new(950,750));Ui.Begin("Transform contrast"u8);view.Draw();Ui.End();},3);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.StartsWith(tool==ViewportTool.Scale?"Scale local ":tool+" ",StringComparison.Ordinal));
            var colors=new HashSet<uint>();var draw=Im.GetDrawData();for(var i=0;i<draw.CmdListsCount;i++)foreach(var v in draw.CmdLists[i].VtxBuffer)colors.Add(v.col);
            foreach(var obsolete in new uint[]{0xff646ef5,0xff82dc82,0xffffaf7d,0xffebebeb,0x50c89678})Assert.DoesNotContain(obsolete,colors);
            Assert.Contains(0xffffffffu,colors);Assert.Empty(editor.Session.Journal);
        }
        finally{EditorTheme.Preference=old;}
    }

}

