using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ProjectMenusUi25Tests
{
    private sealed class Dialogs(string? chosen):IFileDialogs
    {
        public int Calls;
        public string? PickFolder(string title,string? start){Calls++;return chosen;}
        public string? OpenFile(string title,IReadOnlyList<FileDialogFilter> filters,string? start)=>throw new InvalidOperationException("Use admitted files.");
        public string? SaveFile(string title,IReadOnlyList<FileDialogFilter> filters,string? name)=>throw new InvalidOperationException();
    }
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("project-menus-ui25-").FullName;
        public readonly HeadlessHost Host=new(1400,900){Drawing=false};
        public ProjectLauncher Launcher {get;}
        public string First {get;}
        public string Second {get;}
        public string Projects=>Path.Combine(Root,"personal");
        public string Source=>Path.Combine(First,"data",Typed?"level.json":"values.json");
        public bool Typed {get;}
        public Rig(bool typed=false)
        {
            Typed=typed;First=Make("first");Second=Make("second");
            if(typed)
            {
                File.Delete(Path.Combine(First,"data","values.json"));File.Delete(Path.Combine(First,"data","other.json"));
                File.WriteAllText(Source,new SceneFormat().Write(new Scene([SceneObject.At("item",null,Vector3.Zero)])));
                File.WriteAllText(Path.Combine(First,"schemas","level.schema.json"),"""{"type":"object"}""");
            }
            Launcher=new(new RecentProjects(Path.Combine(Root,"local","recent.json")),ProjectPaths.In(First),new ProjectCreator(First),Projects);
            if(typed)Launcher.SetUpWorkspace=w=>Assert.True(w.Documents.BindScene("level.json","scene",out var why),why);
            Assert.True(Launcher.OpenFolder(First),Launcher.Said);Assert.True(Launcher.Workspace!.Show(typed?"level.json":"values.json"));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetIO().ConfigNavCursorVisibleAlways=true;Frames();
        }
        private string Make(string name)
        {
            var path=Path.Combine(Root,name);Directory.CreateDirectory(Path.Combine(path,"data"));Directory.CreateDirectory(Path.Combine(path,"schemas"));
            foreach(var file in new[]{"values","other"})
            {File.WriteAllText(Path.Combine(path,"data",file+".json"),"""{"value":1}""");File.WriteAllText(Path.Combine(path,"schemas",file+".schema.json"),"""{"type":"object","properties":{"value":{"type":"number"}}}""");}
            return path;
        }
        public void Frames()=>Host.Frames(Launcher.Draw,4);
        public void Dirty(int value=2)
        {
            if(Typed)Assert.True(Launcher.Workspace!.SceneWindow!.Editor.Session.Execute(new MoveObject("item",new(value,0,0))));
            else Assert.True(Launcher.Workspace!.Active!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(value))));
            Frames();
        }
        public UiNode Node(string label,UiRole role,string? window=null)
        {
            var snapshot=Ui.Snapshot();bool Inside(UiNode node)
            {
                if(window is null)return true;
                for(var parent=node.Parent;parent>=0;parent=snapshot.Nodes[parent].Parent)
                    if(snapshot.Nodes[parent].Role==UiRole.Window&&snapshot.Nodes[parent].Label.StartsWith(window,StringComparison.Ordinal))return true;
                return false;
            }
            return Assert.Single(snapshot.Nodes,n=>n.Role==role&&n.Label==label&&Inside(n));
        }
        public void Click(UiNode node)
        {Assert.True(node.Enabled,node.DisabledReason);Assert.False(node.Bounds.IsEmpty);Host.Click(Launcher.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);Frames();}
        public void Menu(string label,bool keyboard=false)
        {
            Click(Node("File",UiRole.Menu));Assert.True(Node(label,UiRole.MenuItem).Enabled);
            if(!keyboard){Click(Node(label,UiRole.MenuItem));return;}
            for(var step=0;!Node(label,UiRole.MenuItem).Focused&&step<20;step++)Host.Press(Launcher.Draw,ImGuiKey.DownArrow);
            Assert.True(Node(label,UiRole.MenuItem).Focused,"Native menu keys must reach "+label);Host.Press(Launcher.Draw,ImGuiKey.Enter);Frames();
        }
        public void Answer(SaveChangesChoice choice)
        {Click(Node(choice==SaveChangesChoice.DontSave?"Don't Save":choice.ToString(),UiRole.Button,"Save changes?"));}
        public int DiskValue()=>Typed?(int)new SceneFormat().Read(File.ReadAllText(Source)).Find("item")!.Position.X:JsonDocument.Parse(File.ReadAllText(Source)).RootElement.GetProperty("value").GetInt32();
        public void Dispose(){Launcher.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Root,StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }

    [Theory]
    [InlineData(false,SaveChangesChoice.Save)][InlineData(false,SaveChangesChoice.DontSave)][InlineData(false,SaveChangesChoice.Cancel)]
    [InlineData(true,SaveChangesChoice.Save)][InlineData(true,SaveChangesChoice.DontSave)][InlineData(true,SaveChangesChoice.Cancel)]
    public void Native_menu_confirms_before_new_or_open_and_each_choice_owns_the_draft(bool create,SaveChangesChoice choice)
    {
        using var r=new Rig();var old=r.Launcher.Workspace!;var session=old.Active!.Session!;var original=File.ReadAllBytes(r.Source);r.Dirty();
        var dialogs=new Dialogs(r.Second);r.Launcher.FileDialogs=dialogs;r.Menu(create?"New project...":"Open project...",keyboard:create);
        Assert.True(r.Launcher.SaveChangesPending);Assert.Same(old,r.Launcher.Workspace);Assert.Equal(0,dialogs.Calls);Assert.Equal(original,File.ReadAllBytes(r.Source));
        r.Answer(choice);
        if(choice==SaveChangesChoice.Cancel)
        {Assert.Same(old,r.Launcher.Workspace);Assert.Same(session,old.Active!.Session);Assert.True(session.Dirty);Assert.Equal(0,dialogs.Calls);Assert.Equal(original,File.ReadAllBytes(r.Source));return;}
        if(create){Assert.Same(old,r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);r.Click(r.Node("Create and open",UiRole.Button));}
        Assert.NotSame(old,r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.Equal(create?0:1,dialogs.Calls);
        Assert.Equal(choice==SaveChangesChoice.Save?2:1,r.DiskValue());if(choice==SaveChangesChoice.DontSave)Assert.Equal(original,File.ReadAllBytes(r.Source));
    }

    [Theory]
    [InlineData(SaveChangesChoice.Save)][InlineData(SaveChangesChoice.DontSave)][InlineData(SaveChangesChoice.Cancel)]
    public void Typed_scene_new_project_uses_the_existing_scene_save_authority(SaveChangesChoice choice)
    {
        using var r=new Rig(typed:true);var old=r.Launcher.Workspace!;var session=old.SceneWindow!.Editor.Session;var original=File.ReadAllBytes(r.Source);r.Dirty();r.Menu("New project...");r.Answer(choice);
        if(choice==SaveChangesChoice.Cancel){Assert.Same(old,r.Launcher.Workspace);Assert.True(session.Dirty);Assert.Equal(original,File.ReadAllBytes(r.Source));return;}
        r.Click(r.Node("Create and open",UiRole.Button));Assert.NotSame(old,r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.Equal(choice==SaveChangesChoice.Save?2:0,r.DiskValue());
    }

    [Fact]
    public void Cancelled_chooser_and_native_picker_keep_the_same_project_and_unsaved_history()
    {
        using var r=new Rig();var old=r.Launcher.Workspace!;var session=old.Active!.Session!;r.Dirty();r.Menu("New project...");r.Answer(SaveChangesChoice.DontSave);r.Click(r.Node("Cancel",UiRole.Button));
        Assert.Same(old,r.Launcher.Workspace);Assert.True(session.Dirty);Assert.Equal(1,session.History.UndoCount);Assert.Equal(1,r.DiskValue());
        var dialogs=new Dialogs(null);r.Launcher.FileDialogs=dialogs;r.Menu("Open project...");r.Answer(SaveChangesChoice.DontSave);Assert.Equal(1,dialogs.Calls);Assert.Same(old,r.Launcher.Workspace);Assert.True(session.Dirty);Assert.Equal(1,r.DiskValue());
    }

    [Fact]
    public void New_edits_after_approval_require_a_fresh_decision_before_creating_any_directory()
    {
        using var r=new Rig();var old=r.Launcher.Workspace!;r.Dirty();r.Menu("New project...");r.Answer(SaveChangesChoice.DontSave);
        Assert.True(old.Active!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(3))));r.Click(r.Node("Create and open",UiRole.Button));
        Assert.True(r.Launcher.SaveChangesPending);Assert.Same(old,r.Launcher.Workspace);Assert.False(Directory.Exists(Path.Combine(r.Projects,"My project")));Assert.Equal(1,r.DiskValue());
        r.Answer(SaveChangesChoice.Cancel);Assert.Same(old,r.Launcher.Workspace);Assert.Equal(3,old.Active.Session.Current.At("/value")!.Value.GetInt32());
    }

    [Fact]
    public void Refused_save_never_opens_a_picker_or_discards_the_current_draft()
    {
        using var r=new Rig();var old=r.Launcher.Workspace!;r.Dirty();old.Project.SaveCheck=_=>"Synthetic refusal.";var dialogs=new Dialogs(r.Second);r.Launcher.FileDialogs=dialogs;
        r.Menu("Open project...");r.Answer(SaveChangesChoice.Save);Assert.True(r.Launcher.SaveChangesPending);Assert.Same(old,r.Launcher.Workspace);Assert.True(old.Active!.Dirty);Assert.Equal(0,dialogs.Calls);Assert.Equal(1,r.DiskValue());Assert.Contains("Synthetic refusal",r.Launcher.Said);
    }

    [Fact]
    public void Open_file_reopens_and_focuses_the_admitted_list_without_a_project_transition()
    {
        using var r=new Rig();var old=r.Launcher.Workspace!;r.Dirty();old.Window!.Editor.SetOpen("project.files",false);r.Frames();r.Menu("Open file...",keyboard:true);
        Assert.Same(old,r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.True(old.Window.Editor.IsOpen("project.files"));Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.Contains("values.json",StringComparison.Ordinal)&&n.Focused);Assert.True(old.Active!.Dirty);Assert.Equal(1,r.DiskValue());
    }
}
