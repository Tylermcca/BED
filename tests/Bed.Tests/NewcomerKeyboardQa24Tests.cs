using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Agent.Cli;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

// MIXED: shipped original scene/model/game inputs and tree-script native keyboard events. No mouse/model-action bypass.
[Collection(ImGuiCollection.Name)]
public sealed class NewcomerKeyboardQa24Tests
{
    [Fact]
    public void Start_example_model_move_save_and_real_Play_are_reachable_with_visible_keyboard_focus()
    {
        var root=Directory.CreateTempSubdirectory("newcomer-qa24-").FullName;
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent!;
        var installed=Path.Combine(root,"installed","samples","scene-starter");var recipes=Path.Combine(root,"installed","samples","recipes");Directory.CreateDirectory(recipes);
        foreach(var relative in new[]{"data/level.json","schemas/scene.schema.json","reference_map.json","assets/cube.obj","assets/cube.obj.asset.json","assets/pillar.obj","assets/pillar.obj.asset.json","assets/textured-crate.obj","assets/textured-crate.obj.asset.json","assets/textured-crate.mtl","assets/textured-crate.png","assets/textured-crate.png.asset.json"})
        {var target=Path.Combine(installed,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(repo.FullName,"samples","SceneStarterProject",relative),target);}
        var game=Directory.CreateDirectory(Path.Combine(installed,"game")).FullName;
        foreach(var name in new[]{"SceneStarter.exe","SceneStarter.dll","SceneStarter.deps.json","SceneStarter.runtimeconfig.json","Bed.dll","Bed.Runtime.dll","LICENSE.txt","NOTICE.txt"})File.Copy(Path.Combine(AppContext.BaseDirectory,"SceneStarterReview",name),Path.Combine(game,name));
        PlaySettingsStore.Save(installed,new("game/SceneStarter.exe",["--project",".","--seconds","0"],"."));
        var original=Directory.GetFiles(installed,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(installed,p),p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
        using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        var adapter=Path.Combine(repo.FullName,"samples","SceneStarterAdapter","bin",new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name,"net10.0","SceneStarterAdapter.dll");
        var (loaded,problems)=Adapters.Load([adapter]);Assert.Empty(problems);
        using var launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"preferences","recent.json")),creator:new ProjectCreator(recipes),projectsFolder:Path.Combine(root,"personal")){Loaded=loaded});
        var trail=new List<string>();
        void Key(string chord){UiScript.Run(JsonSerializer.Serialize(new[]{new{key=chord}}),host,launcher.Draw,_=>{});trail.Add(chord);}
        void Type(string text)=>UiScript.Run(JsonSerializer.Serialize(new[]{new{type=text}}),host,launcher.Draw,_=>{});
        string Focused()=>string.Join(";",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>$"{n.Role}:{n.Label}"));
        UiNode Reach(string label,UiRole role,int limit=40)
        {
            for(var i=0;i<limit;i++)
            {
                var matches=Ui.Snapshot().Nodes.Where(n=>n.Label==label&&n.Role==role&&n.Focused).ToArray();
                if(matches.Length==1){Assert.True(matches[0].Enabled);Assert.False(matches[0].Bounds.IsEmpty);Assert.True(ImGui.GetCurrentContext().NavCursorVisible);return matches[0];}
                Key("Tab");
            }
            Assert.Fail($"Could not reach {role}:{label}; focused {Focused()}; trail {string.Join(',',trail.TakeLast(15))}");return default;
        }
        void Palette(string action){Key("Ctrl+Shift+P");Key("Ctrl+A");Type(action);Key("Enter");}
        try
        {
            host.Frames(launcher.Draw,4);Reach("Try the example",UiRole.Button);Key("Enter");
            var workspace=Assert.IsType<DataWorkspace>(launcher.Workspace);var window=Assert.IsType<EditorWindow<Scene>>(workspace.SceneWindow);
            var path=Path.Combine(workspace.Project.DataFolder,"level.json");var before=File.ReadAllText(path);var count=window.Editor.Session.Current.Objects.Length;
            Assert.StartsWith(Path.Combine(root,"personal"),launcher.Current!.Data,StringComparison.OrdinalIgnoreCase);
            Key("Insert");Assert.True(window.Editor.IsOpen("scene.assets"));Reach("Problems only",UiRole.Checkbox);Key("Tab");Key("Enter");
            var cube=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="cube.obj"&&n.Focused);Assert.True(cube.Enabled);Key("Enter");Key("Escape");Reach("Add selected object",UiRole.Button);Key("Enter");
            Assert.Equal(count+1,window.Editor.Session.Current.Objects.Length);var id=window.Editor.Primary!;var added=window.Editor.Session.Current.Find(id)!;Assert.NotNull(added.Asset);
            // Reopening the viewport through its palette command is also a keyboard-only way to focus that panel.
            Palette("editor.panel.editor.viewport");Palette("editor.panel.editor.viewport");Reach("Viewport",UiRole.Canvas,16);
            var position=added.Position;Key("X");Key("RightArrow");var moved=window.Editor.Session.Current.Find(id)!;Assert.NotEqual(position,moved.Position);Assert.Equal(position.Y,moved.Position.Y);Assert.Equal(position.Z,moved.Position.Z);Assert.Equal(before,File.ReadAllText(path));
            Key("Ctrl+S");var wait=Stopwatch.StartNew();while(window.Editor.Session.Dirty&&wait.Elapsed<TimeSpan.FromSeconds(4)){host.Frames(launcher.Draw,1);Thread.Sleep(5);}Assert.False(window.Editor.Session.Dirty);var saved=File.ReadAllText(path);Assert.NotEqual(before,saved);Assert.Equal(moved.Position,new SceneFormat().Read(saved).Find(id)!.Position);
            Key("F5");wait.Restart();while(workspace.ProjectPlay is not {HasCompleted:true,ExitCode:not null}&&wait.Elapsed<TimeSpan.FromSeconds(6)){host.Frames(launcher.Draw,1);Thread.Sleep(5);}Assert.NotNull(workspace.ProjectPlay);Assert.True(workspace.ProjectPlay.HasCompleted,workspace.ProjectPlay.Status);Assert.True(workspace.ProjectPlay.ExitCode==0,workspace.ProjectPlay.Status+"; "+workspace.Said+"; "+string.Join(" | ",Ui.Snapshot().Nodes.Select(n=>n.Label).Where(t=>t.Contains("refused")||t.StartsWith("error:"))));Assert.Equal(saved,File.ReadAllText(path));
            foreach(var item in original)Assert.Equal(item.Value,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(installed,item.Key)))));
            Assert.False(Directory.Exists(Path.Combine(installed,".bed","cache")));
        }
        finally{launcher.Dispose();Directory.Delete(root,true);}
    }
    [Fact]
    public void Incomplete_installed_scene_refuses_before_writing_and_never_replaces_an_existing_example()
    {
        var root=Directory.CreateTempSubdirectory("example-incomplete-").FullName;
        try
        {
            var recipes=Directory.CreateDirectory(Path.Combine(root,"installed","recipes")).FullName;
            Directory.CreateDirectory(Path.Combine(root,"installed","scene-starter"));var personal=Path.Combine(root,"personal");
            var creator=new ProjectCreator(recipes);var result=creator.TryExampleAt(personal);
            Assert.Equal(ProjectCreationStatus.Refused,result.Status);Assert.Contains("3D example",result.Reason);Assert.False(Directory.Exists(personal));
            Directory.CreateDirectory(Path.Combine(personal,"Example"));var sentinel=Path.Combine(personal,"Example","mine.txt");File.WriteAllText(sentinel,"existing personal file");
            result=creator.TryExampleAt(personal);Assert.Equal(ProjectCreationStatus.Refused,result.Status);Assert.Equal("existing personal file",File.ReadAllText(sentinel));Assert.False(Directory.Exists(Path.Combine(personal,"Example 2")));Assert.Single(Directory.GetFileSystemEntries(personal));
        }
        finally{Directory.Delete(root,true);}
    }

}
