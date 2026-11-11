using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
// A level's panels dock into the editor layout even when another document built the layout first, so "Prefab and images"
// never floats over the inspector or the centre view.
[Collection(ImGuiCollection.Name)]
public sealed class Scene2DockLayoutTests
{
    private static UiBounds Window(string title)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label==title&&!n.Bounds.IsEmpty).Bounds;
    private static bool Overlaps(UiBounds a,UiBounds b)=>a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;
    [Theory]
    [InlineData("scene-3d.example.json",false)][InlineData("scene-3d.example.json",true)]
    [InlineData("scene-2d.example.json",false)][InlineData("scene-2d.example.json",true)]
    public void Prefab_and_images_docks_clear_of_the_inspector_and_centre(string fixture,bool otherDocumentFirst)
    {
        using var r=new Scene2HostCommandTests.Rig(fixture);using var workspace=new DataWorkspace(r.Project);
        using var host=new HeadlessHost(1616,939){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());new DpiScale().Apply(1f);
        if(otherDocumentFirst){Assert.True(workspace.Show("prefab.json"));host.Frames(workspace.Draw,4);}
        Assert.True(workspace.Show("level.json"));host.Frames(workspace.Draw,6);
        var prefabs=Window("Prefab and images");var files=Window("Files");
        Assert.False(Overlaps(prefabs,files),$"Prefab and images {prefabs} covers Files {files}");
        foreach(var dock in new[]{"Inspector","Outliner"})Assert.False(Overlaps(prefabs,Window(dock)),$"Prefab and images {prefabs} covers {dock}");
        Assert.True(DockedCentre.TryGet(out var at,out var size));var centre=new UiBounds(at.X,at.Y,size.X,size.Y);
        Assert.False(Overlaps(prefabs,centre),$"Prefab and images {prefabs} covers the centre view {centre}");
    }
    // The path BED.exe takes: the launcher opens the project folder (it shows the level itself) after the app has loaded
    // its saved layout file. A layout saved by an earlier version holds "Prefab and images" floating, because back then a
    // level panel could never dock into a layout another document had built.
    private static string Repo(){var at=new DirectoryInfo(AppContext.BaseDirectory);while(at is not null&&!File.Exists(Path.Combine(at.FullName,"Bed.slnx")))at=at.Parent;return at!.FullName;}
    private static void CopyTree(string from,string to){foreach(var file in Directory.GetFiles(from,"*",SearchOption.AllDirectories)){var target=Path.Combine(to,Path.GetRelativePath(from,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}}
    private static void AssertDocked()
    {
        var prefabs=Window("Prefab and images");
        foreach(var dock in new[]{"Files","Inspector","Outliner"})Assert.False(Overlaps(prefabs,Window(dock)),$"Prefab and images {prefabs} covers {dock} {Window(dock)}");
        Assert.True(DockedCentre.TryGet(out var at,out var size));var centre=new UiBounds(at.X,at.Y,size.X,size.Y);
        Assert.False(Overlaps(prefabs,centre),$"Prefab and images {prefabs} covers the centre view {centre}");
    }
    private static void Launch(string root,Action<HeadlessHost>? beforeOpen,Action<ProjectLauncher,HeadlessHost> check)
    {
        using var host=new HeadlessHost(1616,939){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());new DpiScale().Apply(1f);
        beforeOpen?.Invoke(host);
        using var launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"settings","recent.json"))));
        // As BED.exe does: the adapters beside the app are loaded and the sample's adapter opens its level straight away.
        var (loaded,problems)=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(problems);launcher.Loaded=loaded;
        Assert.True(launcher.OpenFolder(Path.Combine(root,"project")),launcher.Said);host.Frames(launcher.Draw,8);Assert.Equal("level.json",launcher.Workspace!.Active?.Name);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Prefab and images");check(launcher,host);
    }
    [Fact]
    public void Launcher_path_docks_Prefab_and_images_with_no_layout_and_after_an_earlier_versions_floating_layout()
    {
        var root=Directory.CreateTempSubdirectory("dock-launch-").FullName;
        try
        {
            CopyTree(Path.Combine(Repo(),"samples","PrefabPhysicsProject"),Path.Combine(root,"project"));
            string saved="";
            Launch(root,null,(_,_)=>{AssertDocked();saved=ImGui.SaveIniSettingsToMemory().ToString()!;});
            // What an earlier version saved: the panel floating at the right edge, over the inspector.
            var floating=System.Text.RegularExpressions.Regex.Replace(saved,@"\[Window\]\[###panel\.scene2\.prefabs\][^\[]*","[Window][###panel.scene2.prefabs]\nPos=1180,430\nSize=420,450\nCollapsed=0\n\n");
            Assert.DoesNotContain("###panel.scene2.prefabs]\nPos=1180,430\nSize=420,450\nCollapsed=0\nDockId",floating);
            var path=Path.Combine(root,"settings","layout.ini");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var body=System.Text.Encoding.UTF8.GetBytes(floating);
            File.WriteAllText(path,"; BED layout v1 "+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body))+"\n"+floating);
            Launch(root,_=>Assert.True(new EditorLayoutStore(path).LoadCurrent()),(_,_)=>AssertDocked());
            // A layout this version saved keeps a panel the person floated on purpose.
            Assert.True(new EditorLayoutStore(path).TryWrite(floating));
            Launch(root,_=>Assert.True(new EditorLayoutStore(path).LoadCurrent()),(_,_)=>{var prefabs=Window("Prefab and images");Assert.InRange(prefabs.X,1179,1181);Assert.InRange(prefabs.Y,429,431);});
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void An_explicit_settings_folder_also_holds_the_window_layout()
    {
        var settings=Path.Combine(Path.GetTempPath(),"bed-trial");
        Assert.Equal(Path.Combine(settings,"layout.ini"),Bed.Launcher.Program.LayoutPathFor(["project","--settings",settings],settings));
        Assert.Null(Bed.Launcher.Program.LayoutPathFor(["project"],settings));
    }
}
