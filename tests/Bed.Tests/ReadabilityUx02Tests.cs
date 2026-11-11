using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed partial class ReadabilityUx02Tests
{
    private static string Repo(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p is not null&&!File.Exists(Path.Combine(p.FullName,"Bed.slnx")))p=p.Parent;return p!.FullName;}
    public static IEnumerable<object[]> Matrix(){foreach(var width in new[]{1280,1920})foreach(var scale in new[]{1f,1.5f,2f})foreach(var theme in new[]{EditorThemePreference.Light,EditorThemePreference.Dark})foreach(var mode in new[]{"start","data","v1","3d","xy"})yield return [width,width==1280?720:1080,scale,theme,mode];}
    public static IEnumerable<object[]> ExpandedMatrix(){foreach(var width in new[]{1280,1920})foreach(var scale in new[]{1f,1.5f,2f})foreach(var theme in new[]{EditorThemePreference.Light,EditorThemePreference.Dark})foreach(var mode in new[]{"start/preferences","start/new","start/open","start/guide","start/about","start/report","data/preferences","data/palette","data/saveas","data/menu-File","data/menu-Edit","data/menu-View","data/menu-Tools","data/menu-Help","data/panel-project.checks","data/panel-project.proposals","data/panel-bed.getting-started","data/panel-bed.keys","data/panel-bed.ai-setup","3d/panel-scene2.physics","3d/panel-scene2.assets"})yield return [width,width==1280?720:1080,scale,theme,mode];}
    public static IEnumerable<object[]> AuxiliaryMatrix(){foreach(var width in new[]{1280,1920})foreach(var scale in new[]{1f,1.5f,2f})foreach(var theme in new[]{EditorThemePreference.Light,EditorThemePreference.Dark})foreach(var mode in new[]{"data/panel-project.find_replace","data/panel-bed.about","data/panel-project.references","data/panel-editor.assets","data/panel-editor.linked-play.console","data/playsettings","data/export","data/file-new","data/file-rename","data/file-delete","data/close","data/panel-editor.text","data/panel-editor.compare"})yield return [width,width==1280?720:1080,scale,theme,mode];}
    [Theory][MemberData(nameof(AuxiliaryMatrix))]
    public void Auxiliary_panel_and_file_decision_labels_are_complete(int width,int height,float scale,EditorThemePreference theme,string mode)=>Audit(width,height,scale,theme,mode);
    private sealed class NoBuildHost:IGameExportHost
    {
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)=>throw new InvalidOperationException("No build in readability audit.");
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken token)=>throw new InvalidOperationException("No process in readability audit.");
    }
    [Theory][MemberData(nameof(Matrix))]
    public void Default_layout_visible_labels_are_complete(int width,int height,float scale,EditorThemePreference theme,string mode)=>Audit(width,height,scale,theme,mode);
    [Theory][MemberData(nameof(ExpandedMatrix))]
    public void Expanded_panel_menu_and_dialog_labels_are_complete(int width,int height,float scale,EditorThemePreference theme,string mode)=>Audit(width,height,scale,theme,mode);
    private static unsafe void Audit(int width,int height,float scale,EditorThemePreference theme,string mode)
    {
        var split=mode.Split('/');var surface=split[0];var stage=split.Length>1?split[1]:"";
        var root=Directory.CreateTempSubdirectory("readability-ux-").FullName;var previous=EditorTheme.Preference;
        try
        {
            using var host=new HeadlessHost(width,height){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());new DpiScale().Apply(scale);
            var settings=Directory.CreateDirectory(Path.Combine(root,"personal")).FullName;File.WriteAllText(Path.Combine(settings,"settings.json"),JsonSerializer.Serialize(new{theme=theme.ToString(),viewportKeymap="bed"}));
            using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(settings,"recent.json")));
            if(stage is "panel-editor.text" or "panel-editor.compare")Bed.Launcher.Program.SetUp(launcher);
            if(surface!="start")
            {
                var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
                File.WriteAllText(Path.Combine(data,"items.json"),"{\"items\":[{\"id\":\"crate\",\"amount\":2}]}");File.WriteAllText(Path.Combine(schemas,"items.schema.json"),stage=="selected"?"""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"amount":{"type":"number"}}}}}}""":"{}");
                if(surface!="data")File.WriteAllText(Path.Combine(data,"level.json"),surface=="v1"?new SceneFormat().Write(new([SceneObject.At("crate",null,Vector3.Zero)])):new Scene2Format().Write(new(surface=="xy"?SceneSpace.TwoDXY:SceneSpace.ThreeD,[new(SceneObject.At("crate",null,Vector3.Zero),null)],[],[],null)));
                Assert.True(launcher.Open(new(data,schemas)),launcher.Said);var w=launcher.Workspace!;
                if(surface!="data"){Assert.True(w.Documents.BindScene("level.json","scene",out _));Assert.True(w.Show("level.json"));}else Assert.True(w.Show("items.json"));
            }
            if(stage=="panel-project.references")launcher.Workspace!.Window!.Editor.Add(launcher.Workspace.ReferencesPanel);
            if(stage=="panel-editor.assets")launcher.Workspace!.Window!.Editor.Add(new AssetsPanel(new AssetBrowser(Directory.CreateDirectory(Path.Combine(root,"assets")).FullName,".png")).AsPanel());
            if(stage=="panel-editor.linked-play.console")launcher.Workspace!.Window!.Editor.Add(new ConsolePanel(new PlayLogBuffer(new(Guid.NewGuid(),0,new string('0',64)),20,500,5000)).AsPanel());
            if(stage=="export")launcher.Workspace!.BindExport(new(Path.Combine(root,"builder.exe"),root,["{output}"]),new(_=>throw new InvalidOperationException(),_=>throw new InvalidOperationException(),(_,_)=>throw new InvalidOperationException()),new NoBuildHost());
            host.Frames(launcher.Draw,6);
            void Click(string label,UiRole role=UiRole.Button){var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==role&&n.Label==label);host.Click(launcher.Draw,item.Bounds.X+item.Bounds.Width/2,item.Bounds.Y+item.Bounds.Height/2);host.Frames(launcher.Draw,4);}
            switch(stage)
            {
                case "panel-editor.compare":Assert.True(launcher.Workspace!.Active!.Session!.Execute(new SetValue("/items/0/amount",JsonSerializer.SerializeToElement(3))));Assert.True(launcher.Workspace.Window!.Run("editor.panel_do.editor.compare/editor.compare.saved"));var compare=(Bed.Launcher.CompareFilePanel)launcher.Workspace.Window.Editor.Panels.Single(p=>p.Id=="editor.compare").Draw.Target!;Assert.True(compare.Pending!.Wait(TimeSpan.FromSeconds(3)));break;
                case "selected":if(surface=="data")launcher.Workspace!.Window!.Editor.Select("/items/0");else if(surface=="v1")launcher.Workspace!.SceneWindow!.Editor.Select("crate");else launcher.Workspace!.Scene2Window!.Editor.Select("crate");break;
                case "preferences-theme":host.Press(launcher.Draw,ImGuiKey.Comma,ctrl:true);host.Frames(launcher.Draw,4);Click("Theme",UiRole.Combo);break;
                case "preferences-keymap":host.Press(launcher.Draw,ImGuiKey.Comma,ctrl:true);host.Frames(launcher.Draw,4);Click("Viewport keymap",UiRole.Combo);break;
                case "table-list":Click("List",UiRole.Combo);break;
                case "playsettings":launcher.Workspace!.PlaySettings!.Open=true;break;
                case "export":launcher.Workspace!.Window!.ExportBuildAction!();break;
                case "file-new":Assert.True(launcher.Workspace!.Window!.Run("editor.panel_do.project.files/new_file"));break;
                case "file-rename":Assert.True(launcher.Workspace!.Window!.Run("editor.panel_do.project.files/rename_file"));break;
                case "file-delete":Assert.True(launcher.Workspace!.Window!.Run("editor.panel_do.project.files/delete_file"));break;
                case "close":Assert.True(launcher.Workspace!.Active!.Session!.Execute(new SetValue("/items/0/amount",JsonSerializer.SerializeToElement(3))));Assert.False(launcher.Close());break;
                case "preferences":host.Press(launcher.Draw,ImGuiKey.Comma,ctrl:true);break;
                case "new":host.Press(launcher.Draw,ImGuiKey.N,ctrl:true,shift:true);break;
                case "open":host.Press(launcher.Draw,ImGuiKey.O,ctrl:true);break;
                case "guide":Click("Getting started");break;
                case "about":Click("About BED");break;
                case "report":Click("Report a problem");break;
                case "palette":Assert.True(launcher.Workspace!.Window!.Run("editor.palette"));break;
                case "saveas":Assert.True(launcher.Workspace!.Window!.Run("editor.file_save_as"));break;
            }
            if(stage.StartsWith("menu-",StringComparison.Ordinal))Click(stage[5..],UiRole.Menu);
            if(stage.StartsWith("panel-",StringComparison.Ordinal))
            {
                if(surface=="3d"){var editor=launcher.Workspace!.Scene2Window!.Editor;Assert.Contains(editor.Panels,p=>p.Id==stage[6..]);editor.SetOpen(stage[6..],true);}
                else {var editor=launcher.Workspace!.Window!.Editor;Assert.Contains(editor.Panels,p=>p.Id==stage[6..]);editor.SetOpen(stage[6..],true);}
            }
            host.Frames(launcher.Draw,5);host.Drawing=true;host.Frame(launcher.Draw);
            var folder=Path.Combine(Repo(),".validation/UX02");Directory.CreateDirectory(folder);var name=$"{mode.Replace('/','_')}-{width}-{scale:0.0}-{theme}";
            host.Save(Path.Combine(folder,name+".png"));var snapshot=Ui.Snapshot();File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(snapshot));
            AssertReadable(snapshot,scale,Path.Combine(folder,name+".txt"));
        }
        finally{EditorTheme.Preference=previous;Directory.Delete(root,true);}
    }
    internal static unsafe void AssertReadable(UiSnapshot snapshot,float scale,string evidence)
    {
            static bool Intersects(UiBounds a,UiBounds b)=>a.Width>0&&a.Height>0&&b.Width>0&&b.Height>0&&a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;
            // Buttons have a 24 logical pixel minimum; native fields and row navigation retain their existing metrics.
            var tiny=snapshot.Nodes.Where(n=>n.Enabled&&n.Role==UiRole.Button&&!n.Bounds.IsEmpty&&n.Bounds.Height+0.5f<24*scale).Select(n=>n.Label).ToArray();
            Assert.True(tiny.Length==0,"Small button targets: "+string.Join(", ",tiny));
            bool ScrollableBody(UiNode n,UiTextEvidence e)
            {
                if(n.Role!=UiRole.Text||n.Label.Length<80||e.FullBounds.X<e.Clip.X||e.FullBounds.X+e.FullBounds.Width>e.Clip.X+e.Clip.Width)return false;
                // Read-only prose/code can span a scrolling page. Labels and buttons must fit in full.
                var parent=n.Parent;
                while(parent is {} index&&index>=0)
                {
                    var owner=snapshot.Nodes[index];var native=ImGui.Internal.FindWindowByID(new ImGuiID((uint)owner.Id));
                    if(native.Ptr!=null&&native.ScrollMax.Y>0)return true;
                    parent=owner.Parent;
                }
                return false;
            }
            var clipped=snapshot.Nodes.Where(n=>n.TextEvidence is {Truncated:true} e&&Intersects(e.FullBounds,e.Clip)&&!ScrollableBody(n,e)).Select(n=>$"{n.Role}: {n.Label} [{n.TextEvidence}]").ToArray();
            File.WriteAllLines(evidence,clipped);Assert.True(clipped.Length==0,string.Join("\n",clipped));
            // Viewport papers must not obscure one another, even when every label fits its clip.
            foreach(var canvas in snapshot.Nodes.Where(n=>n.Role==UiRole.Canvas))
            {
                var index=Array.IndexOf(snapshot.Nodes.ToArray(),canvas);
                var papers=snapshot.Nodes.Where(n=>n.Parent==index&&n.Role==UiRole.Status&&!n.Bounds.IsEmpty).ToArray();
                for(var i=0;i<papers.Length;i++)for(var j=i+1;j<papers.Length;j++)
                {
                    UiBounds Paper(UiBounds b)=>new(b.X-3,b.Y-2,b.Width+6,b.Height+4);
                    Assert.False(Intersects(Paper(papers[i].Bounds),Paper(papers[j].Bounds)),
                        $"Viewport status papers overlap: {papers[i].Label} / {papers[j].Label}");
                }
            }

    }
}
