using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RecentMenuUi06Tests
{
    private sealed class Adapter:IProjectAdapter
    {
        public string Name=>"Scene fixture";
        public bool Recognises(ProjectPaths p)=>File.Exists(Path.Combine(p.Data,"level.json"));
        public DataProject Open(ProjectPaths p)
        {var project=DataProject.Open(p.Data,p.Schemas);Assert.True(DocumentHost.For(project).BindScene("level.json","scene",out _));return project;}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("recent-menu-ui06-").FullName;
        public readonly HeadlessHost Host=new(1200,900){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public string History=>Path.Combine(Root,"local preferences","recent.json");
        public Rig(){EditorWindow<DataDocument>.Configure(ImGui.GetIO());Launcher=new(new RecentProjects(History)){Loaded=[new Adapter()]};}
        public string Project(string name,bool scene=false)
        {
            var folder=Directory.CreateDirectory(Path.Combine(Root,name)).FullName;
            Directory.CreateDirectory(Path.Combine(folder,"data"));Directory.CreateDirectory(Path.Combine(folder,"schemas"));
            File.WriteAllText(Path.Combine(folder,"data",scene?"level.json":"item.json"),scene?new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)])):"{}");
            File.WriteAllText(Path.Combine(folder,"schemas",scene?"scene.schema.json":"item.schema.json"),"{\"type\":\"object\"}");
            return folder;
        }
        public void Key(ImGuiKey key,bool alt=false){Host.Press(Launcher.Draw,key,alt:alt);Host.Frames(Launcher.Draw,2);}
        public void OpenMenu()
        {
            Host.Frames(Launcher.Draw,3);Key(ImGuiKey.F,alt:true);
            var submenu=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Menu&&n.Label=="Recent projects");
            Assert.Equal("R",submenu.AccessKey);Key(ImGuiKey.R,alt:true);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Menu&&n.Label=="Recent projects"&&n.Expanded==true);
        }
        public UiNode[] Entries()=>Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.MenuItem&&n.Label.Length>2&&char.IsAsciiDigit(n.Label[0])&&n.Label[1]=='.').ToArray();
        public void Choose(string folder)
        {
            var wanted=Assert.Single(Entries(),n=>n.Label.Contains(folder,StringComparison.Ordinal));
            for(var i=0;i<25;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Id==wanted.Id&&n.Focused)){Key(ImGuiKey.Enter);return;}
                Key(ImGuiKey.DownArrow);
            }
            Assert.Fail("Arrow keys did not reach recent project "+folder+"\n"+Ui.Snapshot().Text);
        }
        public void Answer(string label)
        {
            for(var i=0;i<6;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label==label&&n.Focused)){Key(ImGuiKey.Enter);return;}
                Key(ImGuiKey.Tab);
            }
            Assert.Fail("Save decision did not focus "+label);
        }
        public void Dispose(){Launcher.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void File_recent_menu_lists_latest_eight_and_opens_an_exact_project_by_keyboard_with_only_local_history_writes()
    {
        using var r=new Rig();var projects=Enumerable.Range(0,10).Select(i=>r.Project("project-"+i)).ToArray();
        foreach(var folder in projects)Assert.True(r.Launcher.OpenFolder(folder),r.Launcher.Said);
        var inputs=projects.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,File.ReadAllBytes);
        r.OpenMenu();var entries=r.Entries();Assert.Equal(8,entries.Length);Assert.Equal(8,entries.Select(n=>n.Id).Distinct().Count());
        for(var i=0;i<8;i++)Assert.Contains(projects[9-i],entries[i].Label);
        r.Choose(projects[7]);Assert.Equal(Path.Combine(projects[7],"data"),r.Launcher.Current!.Data);
        Assert.Equal(Path.Combine(projects[7],"data"),new RecentProjects(r.History).Items[0].Data);
        foreach(var (path,bytes) in inputs)Assert.Equal(bytes,File.ReadAllBytes(path));
        Assert.All(projects,p=>Assert.False(Directory.Exists(Path.Combine(p,".bed"))));
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Missing_entries_are_removed_from_local_history_with_a_plain_menu_note(bool rememberedRoot)
    {
        using var r=new Rig();var good=r.Project("good");Assert.True(r.Launcher.OpenFolder(good));
        var missing=Path.Combine(r.Root,"missing");var entry=new ProjectPaths(Path.Combine(missing,"data"),Path.Combine(missing,"schemas")){Folder=rememberedRoot?missing:null};
        r.Launcher.Recent.Add(entry);r.OpenMenu();Assert.Single(r.Entries());
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("Removed missing projects",StringComparison.Ordinal)&&n.Label.Contains(missing,StringComparison.Ordinal));
        Assert.DoesNotContain(new RecentProjects(r.History).Items,p=>p.Folder==missing||p.Data==entry.Data);
        Assert.Equal("{}",File.ReadAllText(Path.Combine(good,"data","item.json")));
    }
    [Theory]
    [InlineData(SaveChangesChoice.Save)][InlineData(SaveChangesChoice.DontSave)][InlineData(SaveChangesChoice.Cancel)]
    public void Recent_project_switch_preserves_scene_draft_until_keyboard_save_discard_or_cancel(SaveChangesChoice choice)
    {
        using var r=new Rig();var other=r.Project("other");var scene=r.Project("scene",true);
        Assert.True(r.Launcher.OpenFolder(other));Assert.True(r.Launcher.OpenFolder(scene));Assert.True(r.Launcher.Workspace!.Show("level.json"),r.Launcher.Workspace.Said);
        var workspace=r.Launcher.Workspace!;var session=workspace.SceneWindow!.Editor.Session;
        Assert.True(session.Execute(new MoveObject("box",new(3,0,0))));var draft=session.Current;
        var path=Path.Combine(scene,"data","level.json");var original=File.ReadAllBytes(path);
        r.OpenMenu();r.Choose(other);Assert.True(r.Launcher.SaveChangesPending);Assert.Same(workspace,r.Launcher.Workspace);
        Assert.Equal(original,File.ReadAllBytes(path));Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="level.json");
        r.Answer(choice==SaveChangesChoice.Save?"Save":choice==SaveChangesChoice.DontSave?"Don't Save":"Cancel");
        Assert.False(r.Launcher.SaveChangesPending,r.Launcher.Said);
        if(choice==SaveChangesChoice.Cancel){Assert.Same(workspace,r.Launcher.Workspace);Assert.Same(draft,session.Current);Assert.True(session.Dirty);}
        else{Assert.NotSame(workspace,r.Launcher.Workspace);Assert.Equal(Path.Combine(other,"data"),r.Launcher.Current!.Data);}
        var saved=new SceneFormat().Read(File.ReadAllText(path));Assert.Equal(choice==SaveChangesChoice.Save?3:0,saved.Find("box")!.Position.X);
        if(choice!=SaveChangesChoice.Save)Assert.Equal(original,File.ReadAllBytes(path));
    }
    [Fact]
    public void Clean_scene_can_switch_to_a_recent_project_without_a_save_prompt()
    {
        using var r=new Rig();var other=r.Project("other");var scene=r.Project("scene",true);
        Assert.True(r.Launcher.OpenFolder(other));Assert.True(r.Launcher.OpenFolder(scene));Assert.True(r.Launcher.Workspace!.Show("level.json"),r.Launcher.Workspace.Said);r.OpenMenu();r.Choose(other);
        Assert.False(r.Launcher.SaveChangesPending);Assert.Equal(Path.Combine(other,"data"),r.Launcher.Current!.Data);
    }
    [Fact]
    public void Refused_typed_save_keeps_pending_transition_exact_draft_and_disk_until_cancel()
    {
        using var r=new Rig();var other=r.Project("other");var scene=r.Project("scene",true);
        Assert.True(r.Launcher.OpenFolder(other));Assert.True(r.Launcher.OpenFolder(scene));Assert.True(r.Launcher.Workspace!.Show("level.json"));
        var workspace=r.Launcher.Workspace!;var session=workspace.SceneWindow!.Editor.Session;
        Assert.True(session.Execute(new MoveObject("box",new(3,0,0))));var draft=session.Current;var journal=session.Journal.Count;
        workspace.Documents.SceneSaveCheck=_=>"Synthetic scene policy refusal.";
        var path=Path.Combine(scene,"data","level.json");var original=File.ReadAllBytes(path);
        r.OpenMenu();r.Choose(other);r.Answer("Save");
        Assert.True(r.Launcher.SaveChangesPending);Assert.Same(workspace,r.Launcher.Workspace);Assert.Same(draft,session.Current);
        Assert.Equal(journal,session.Journal.Count);Assert.True(session.Dirty);Assert.Equal(original,File.ReadAllBytes(path));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Synthetic scene policy refusal.");
        r.Answer("Cancel");Assert.False(r.Launcher.SaveChangesPending);Assert.Same(draft,session.Current);Assert.True(session.Dirty);
    }

}
