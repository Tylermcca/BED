using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
[Trait("Category","Slow")]
public sealed class DisplayPreferencesA11y08Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly HeadlessHost Host=new(1600,1000){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public Rig(string root)
        {
            EditorWindow<DataDocument>.Configure(Im.GetIO());
            Launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json"))){ProjectPlayHost=new NoProcessHost()});
            Host.Frames(Launcher.Draw,4);
        }
        public void Frames(int n=3)=>Host.Frames(Launcher.Draw,n);
        public void Key(ImGuiKey key)=>Host.Press(Launcher.Draw,key);
        public void Reach(string label)
        {
            for(var i=0;i<80;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Focused&&n.Label==label))return;
                Key(ImGuiKey.Tab);
            }
            Assert.Fail("Native keyboard did not reach "+label);
        }
        public void Choose(string label,int index)
        {Reach(label);Key(ImGuiKey.Space);Key(ImGuiKey.Home);for(var i=0;i<index;i++)Key(ImGuiKey.DownArrow);Key(ImGuiKey.Enter);Frames();}
        public void Dispose(){Launcher.Dispose();Host.Dispose();}
    }
    private sealed class NoProcessHost:IProjectPlayHost
    {public IProjectPlaySession Start(PlayLaunch launch)=>new NoProcessSession();}
    private sealed class NoProcessSession:IProjectPlaySession
    {
        public bool Start(bool dirty,bool buildAndPlay=false)=>true;
        public bool CanBuildAndPlay=>false;public bool HasCompleted=>true;public int? ExitCode=>0;public string Status=>"No process in this fixture.";
        public void RequestStop(){}public void Update(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];public void Draw(){}public void Dispose(){}
    }
    [Theory][InlineData(1f,0)][InlineData(1.5f,1)][InlineData(2f,2)]
    public void Native_preferences_choose_scale_and_high_contrast_and_reopen_preserves_unknown_keys(float scale,int index)
    {
        var root=Directory.CreateTempSubdirectory("display-a11y08-").FullName;var original=EditorTheme.Preference;
        try
        {
            var local=Directory.CreateDirectory(Path.Combine(root,"local")).FullName;var path=Path.Combine(local,"settings.json");
            File.WriteAllText(path,"""{"textScale":1,"theme":"System","future":{"keep":[1,true,null]}}""");
            using(var r=new Rig(root))
            {
                r.Reach("Preferences");r.Key(ImGuiKey.Enter);r.Frames();
                r.Choose("Theme",3);r.Choose("UI scale",index);
                Assert.Equal(EditorThemePreference.HighContrast,EditorTheme.Preference);Assert.Equal(scale,EditorWindow<DataDocument>.TextScale);
                var stored=JsonNode.Parse(File.ReadAllText(path))!;
                Assert.Equal("HighContrast",stored["theme"]!.GetValue<string>());Assert.Equal(scale,stored["textScale"]!.GetValue<float>());
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"keep":[1,true,null]}"""),stored["future"]));
                Assert.Null(r.Launcher.Workspace);Assert.False(File.Exists(r.Launcher.Recent.File));
            }
            var bytes=File.ReadAllBytes(path);
            using(var reopened=new Rig(root))
            {Assert.Equal(EditorThemePreference.HighContrast,EditorTheme.Preference);Assert.Equal(scale,EditorWindow<DataDocument>.TextScale);Assert.Equal(bytes,File.ReadAllBytes(path));}
        }
        finally{EditorTheme.Preference=original;Directory.Delete(root,true);}
    }
    [Fact]
    public void High_contrast_resolves_explicitly_and_text_focus_and_boundaries_have_measured_contrast()
    {
        var original=EditorTheme.Preference;
        try
        {
            Assert.Equal(0,(int)EditorThemePreference.System);Assert.Equal(1,(int)EditorThemePreference.Light);Assert.Equal(2,(int)EditorThemePreference.Dark);Assert.Equal(3,(int)EditorThemePreference.HighContrast);
            Assert.Equal(0,(int)EditorThemeAppearance.Light);Assert.Equal(1,(int)EditorThemeAppearance.Dark);Assert.Equal(2,(int)EditorThemeAppearance.HighContrast);
            Assert.Equal(EditorThemeAppearance.HighContrast,EditorTheme.Resolve(EditorThemePreference.HighContrast,1));
            using var host=new HeadlessHost(400,300){Drawing=false};EditorTheme.Preference=EditorThemePreference.HighContrast;EditorTheme.Refresh();var s=Im.GetStyle();
            static double L(Brutal.Numerics.float4 c)
            {static double C(float v)=>v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);return .2126*C(c.X)+.7152*C(c.Y)+.0722*C(c.Z);}
            double Ratio(ImGuiCol a,ImGuiCol b){var x=L(s.Colors[(int)a]);var y=L(s.Colors[(int)b]);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
            foreach(var surface in new[]{ImGuiCol.WindowBg,ImGuiCol.ChildBg,ImGuiCol.PopupBg,ImGuiCol.FrameBg,ImGuiCol.Button,ImGuiCol.ButtonHovered,ImGuiCol.ButtonActive,ImGuiCol.Header,ImGuiCol.HeaderHovered,ImGuiCol.HeaderActive,ImGuiCol.TableHeaderBg,ImGuiCol.TabSelected})
            {Assert.True(Ratio(ImGuiCol.Text,surface)>=7,$"Text on {surface}");Assert.True(Ratio(ImGuiCol.TextDisabled,surface)>=7,$"Muted on {surface}");}
            Assert.True(Ratio(ImGuiCol.NavCursor,ImGuiCol.WindowBg)>=7);Assert.True(Ratio(ImGuiCol.Border,ImGuiCol.WindowBg)>=7);
            Im.GetStyle().FontScaleMain=2;EditorTheme.Refresh();Assert.Equal(2,Im.GetStyle().FontScaleMain);
        }
        finally{EditorTheme.Preference=original;}
    }
    [Theory][InlineData(1f,false)][InlineData(1.5f,false)][InlineData(2f,false)][InlineData(1f,true)][InlineData(1.5f,true)][InlineData(2f,true)]
    public void Every_builtin_panel_draws_at_saved_scale_and_keyboard_focus_scrolls_into_its_visible_region(float scale,bool scene)
    {
        var root=Directory.CreateTempSubdirectory("display-panels-a11y08-").FullName;var original=EditorTheme.Preference;
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"local"));Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));Directory.CreateDirectory(Path.Combine(root,"assets"));
            File.WriteAllText(Path.Combine(root,"local/settings.json"),JsonSerializer.Serialize(new{textScale=scale,theme="HighContrast"}));
            var data=Path.Combine(root,"data/items.json");File.WriteAllText(data,JsonSerializer.Serialize(new{items=Enumerable.Range(0,60).Select(i=>new{id="entry_"+i,name="Entry "+i})}));
            File.WriteAllText(Path.Combine(root,"schemas/items.schema.json"),"{}");
            var level=Path.Combine(root,"data/scene.json");File.WriteAllText(level,new SceneFormat().Write(new Scene([SceneObject.At("lamp",null,new(0,0,0))])));
            File.WriteAllText(Path.Combine(root,"reference_map.json"),"""{"game":"Own fixture","labels":[],"references":[]}""");
            File.WriteAllText(Path.Combine(root,"game.exe"),"Never executed");PlaySettingsStore.Save(root,new("game.exe"));
            var bytes=new[]{data,level}.ToDictionary(p=>p,File.ReadAllBytes);
            using var r=new Rig(root);Assert.True(r.Launcher.OpenFolder(root),r.Launcher.Said);var w=r.Launcher.Workspace!;
            IReadOnlyList<Panel> panels;
            if(scene){Assert.True(w.Documents.BindScene("scene.json","scene",out var why),why);Assert.True(w.Show("scene.json"),w.Said);panels=w.SceneWindow!.Editor.Panels;w.SceneWindow.Editor.Select("lamp");}
            else{Assert.True(w.Show("items.json"),w.Said);w.Window!.Editor.Select("/items/0");panels=w.Window.Editor.Panels;}
            var names=new List<string>();
            foreach(var panel in panels){if(scene)w.SceneWindow!.Editor.SetOpen(panel.Id,true);else w.Window!.Editor.SetOpen(panel.Id,true);names.Add(panel.Title+"###panel."+panel.Id);}
            names.Add("Outliner###outliner");names.Add("Inspector###inspector");
            if(!scene){Assert.True(w.RequestProjectPlay(),w.Said);names.Add("Play console###project.play.console");}
            r.Frames(4);
            var checkedButtons=0;
            foreach(var name in names.Distinct())
            {
                var window=Im.Internal.FindWindowByName(name);Assert.False(window.IsNull(),name);
                Im.Internal.DockContextProcessUndockWindow(Im.GetCurrentContext(),window,true);
                Im.SetWindowPos(name,new(20,80),ImGuiCond.Always);Im.SetWindowSize(name,new(650,550),ImGuiCond.Always);Im.SetWindowFocus(name);r.Frames(3);
                var title=name.Split("###")[0];Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label==title);
                Assert.Equal(scale,Im.GetStyle().FontScaleMain);Assert.Equal(EditorThemePreference.HighContrast,EditorTheme.Preference);
                var innerHeight=window.InnerRect.Max.Y-window.InnerRect.Min.Y;
                Assert.True(window.ContentSize.Y<=innerHeight+2*Im.GetStyle().WindowPadding.Y+1||window.ScrollbarY,$"{name} overflows without a scrollbar");
                for(var i=0;i<12;i++)
                {
                    r.Key(ImGuiKey.Tab); // Press already supplies key-down, key-up and a settling frame.
                    if(!Im.GetCurrentContext().NavWindow.Equals(window))continue;
                    foreach(var node in Ui.Snapshot().Nodes.Where(n=>n.Focused&&n.Role==UiRole.Button))
                    {
                        checkedButtons++;
                        var clip=window.InnerClipRect;
                        Assert.True(node.Bounds.X>=clip.Min.X-1&&node.Bounds.X+node.Bounds.Width<=clip.Max.X+1,$"{name}: {node.Label} horizontally clipped: {node.Bounds}");
                        Assert.True(node.Bounds.Y+node.Bounds.Height>clip.Min.Y&&node.Bounds.Y<clip.Max.Y,$"{name}: focused {node.Label} never scrolled into view");
                    }
                }
            }
            Assert.True(checkedButtons>0,"The native focus/scroll checks must exercise real buttons.");
            foreach(var(path,content)in bytes)Assert.Equal(content,File.ReadAllBytes(path));Assert.Empty(w.Project.Dirty);
        }
        finally{EditorTheme.Preference=original;Directory.Delete(root,true);}
    }
    [Fact]
    public void High_contrast_scene_grid_and_selection_have_explicit_white_grey_and_yellow_vertices()
    {
        var original=EditorTheme.Preference;
        try
        {
            using var host=new HeadlessHost(900,700){Drawing=false};EditorTheme.Preference=EditorThemePreference.HighContrast;EditorTheme.Refresh();
            var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("crate",null,new(0,0,0))])),SceneEditing.Outline);editor.Select("crate");
            var view=new ViewportView<Scene>(editor,new SceneViewport());
            host.Frames(()=>{Im.SetNextWindowPos(new(0,0));Im.SetNextWindowSize(new(850,650));Ui.Begin("Scene contrast"u8);view.Draw();Ui.End();},4);
            var colors=new HashSet<uint>();var draw=Im.GetDrawData();for(var i=0;i<draw.CmdListsCount;i++)foreach(var vertex in draw.CmdLists[i].VtxBuffer)colors.Add(vertex.col);
            Assert.Contains(0xff000000u,colors);Assert.Contains(0xffffffffu,colors);Assert.Contains(0xffaaaaaau,colors);Assert.Contains(0xff00ffffu,colors);Assert.Empty(editor.Session.Journal);
        }
        finally{EditorTheme.Preference=original;}
    }
}
