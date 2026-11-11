using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneCloseSaveUi39Tests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("scene-close-save-").FullName;
        internal readonly HeadlessHost Host;internal readonly DataWorkspace Workspace;internal readonly DataProject Project;internal readonly string Kind;internal int Closes;
        internal string Data=>Path.Combine(Root,"data");internal string Level=>Path.Combine(Data,"level.json");internal string Plain=>Path.Combine(Data,"a.json");
        internal Rig(string kind="v2",bool drawing=false)
        {
            Kind=kind;Directory.CreateDirectory(Data);var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;File.WriteAllText(Plain,"{\"value\":0}");File.WriteAllText(Path.Combine(schemas,"a.schema.json"),"{}");
            File.WriteAllText(Level,kind=="v1"?new SceneFormat().Write(new Scene([SceneObject.At("one",null,Vector3.Zero)])):new Scene2Format().Write(new(kind=="xy"?SceneSpace.TwoDXY:SceneSpace.ThreeD,[new(SceneObject.At("one",null,Vector3.Zero),null)],[],[],null)));
            Host=new(1500,1100){Drawing=drawing};EditorWindow<Scene>.Configure(ImGui.GetIO());Project=DataProject.Open(Data,schemas);Workspace=new(Project){CloseProject=()=>{Closes++;return true;}};Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Project.Find("a.json")!.Open());Assert.True(Workspace.Show("level.json"));Frames();
        }
        internal object Window=>Kind=="v1"?Workspace.SceneWindow!:typeof(DataWorkspace).GetProperty("Scene2Window",Private)!.GetValue(Workspace)!;
        internal bool Dirty=>Kind=="v1"?((EditorWindow<Scene>)Window).Editor.Session.Dirty:((EditorWindow<Scene2>)Window).Editor.Session.Dirty;
        internal void Edit(float x=1){if(Kind=="v1")Assert.True(((EditorWindow<Scene>)Window).Editor.Execute(new MoveObject("one",new(x,0,0))));else Assert.True(((EditorWindow<Scene2>)Window).Editor.Execute(Scene2Kit.Command("object.move",JsonSerializer.SerializeToElement(new{id="one",position=new[]{x,0f,0f}}))));}
        internal void EditData()=>Assert.True(Project.Find("a.json")!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));
        internal void Frames()=>Host.Frames(Workspace.Draw,4);
        internal void Press(string label){var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);Assert.True(node.Enabled,node.DisabledReason);var ticket=Ui.RequestAction(node.Id,UiActions.Invoke);Frames();Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);}
        internal bool Run(string action)=>(bool)Window.GetType().GetMethod("Run")!.Invoke(Window,[action])!;
        public void Dispose(){Workspace.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public void Mixed_save_all_commits_scene_and_data_once_and_retains_their_histories(string kind)
    {
        using var r=new Rig(kind);var events=new List<string[]>();r.Project.FilesSaved+=files=>events.Add(files.Select(f=>f.Name).Order().ToArray());r.Edit();r.EditData();Assert.True(r.Workspace.SaveAll(),r.Workspace.Said);Assert.False(r.Dirty);Assert.False(r.Project.Find("a.json")!.Dirty);Assert.Equal(new[]{"a.json","level.json"},Assert.Single(events));Assert.Null(r.Project.Find("level.json")!.Session);Assert.Equal(2,JsonDocument.Parse(File.ReadAllText(r.Plain)).RootElement.GetProperty("value").GetInt32());Assert.Empty(r.Project.SaveAll());Assert.Single(events);
        if(kind=="v1")Assert.True(((EditorWindow<Scene>)r.Window).Editor.Session.Undo());else Assert.True(((EditorWindow<Scene2>)r.Window).Editor.Session.Undo());Assert.True(r.Dirty);
    }
    [Theory][InlineData("v2","ok")][InlineData("xy","ok")][InlineData("v2","policy")][InlineData("xy","edit")]
    public void Background_group_checks_never_write_a_member_before_the_complete_group_is_current_and_approved(string kind,string outcome)
    {
        using var r=new Rig(kind);using var release=new ManualResetEventSlim();var ownerThread=Environment.CurrentManagedThreadId;var plain=File.ReadAllText(r.Plain);var level=File.ReadAllText(r.Level);r.Edit();r.EditData();var events=new List<string[]>();r.Project.FilesSaved+=files=>{Assert.Equal(ownerThread,Environment.CurrentManagedThreadId);events.Add(files.Select(f=>f.Name).Order().ToArray());};
        r.Workspace.BackgroundSaves=true;r.Project.SaveCheck=file=>{Assert.NotEqual(ownerThread,Environment.CurrentManagedThreadId);Assert.Equal(new[]{"a.json"},r.Project.BeingSaved(file).Select(f=>f.Name));Assert.True(release.Wait(TimeSpan.FromSeconds(10)));return null;};var typedChecks=0;r.Workspace.Documents.Scene2SaveCheck=_=>{Assert.Equal(ownerThread,Environment.CurrentManagedThreadId);typedChecks++;return outcome=="policy"?"Fixture background scene refusal.":null;};
        try
        {
            Assert.False(r.Workspace.SaveAll());Assert.True(r.Workspace.SavePending("a.json"));r.Frames();Assert.Equal(plain,File.ReadAllText(r.Plain));Assert.Equal(level,File.ReadAllText(r.Level));Assert.Empty(events);Assert.Equal(0,typedChecks);
            if(outcome=="edit")Assert.True(r.Project.Find("a.json")!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(3))));release.Set();var clock=System.Diagnostics.Stopwatch.StartNew();while(r.Workspace.SavePending("a.json")&&clock.Elapsed<TimeSpan.FromSeconds(10)){r.Workspace.FinishSaves();Thread.Sleep(1);}Assert.False(r.Workspace.SavePending("a.json"));
            if(outcome=="ok"){Assert.False(r.Dirty);Assert.False(r.Project.Find("a.json")!.Dirty);Assert.Equal(new[]{"a.json","level.json"},Assert.Single(events));Assert.Equal(1,typedChecks);}
            else{Assert.True(r.Dirty);Assert.True(r.Project.Find("a.json")!.Dirty);Assert.Equal(plain,File.ReadAllText(r.Plain));Assert.Equal(level,File.ReadAllText(r.Level));Assert.Empty(events);Assert.Contains(outcome=="policy"?"Fixture background scene refusal.":"changed",r.Workspace.Said);}
        }
        finally{release.Set();}
    }
    [Fact]
    public void A_typed_save_policy_disposing_the_workspace_cannot_commit_the_captured_group()
    {
        using var r=new Rig();var plain=File.ReadAllText(r.Plain);var level=File.ReadAllText(r.Level);r.Edit();r.EditData();r.Workspace.Documents.Scene2SaveCheck=_=>{r.Workspace.Dispose();return null;};Assert.False(r.Workspace.SaveAll());Assert.Equal(plain,File.ReadAllText(r.Plain));Assert.Equal(level,File.ReadAllText(r.Level));Assert.True(r.Dirty);Assert.True(r.Project.Find("a.json")!.Dirty);Assert.Contains("no longer current",r.Workspace.Said);
    }
    [Fact]
    public void Standalone_close_finishes_its_approved_background_group_without_another_choice()
    {
        using var r=new Rig();using var release=new ManualResetEventSlim();r.Edit();r.EditData();r.Workspace.BackgroundSaves=true;r.Project.SaveCheck=_=>{Assert.True(release.Wait(TimeSpan.FromSeconds(10)));return null;};r.Workspace.Documents.Scene2SaveCheck=_=>null;
        try
        {
            Assert.False(r.Workspace.CloseProject!());r.Frames();r.Press("Save all and close");Assert.Equal(0,r.Closes);Assert.True(r.Workspace.SavePending("a.json"));release.Set();var clock=System.Diagnostics.Stopwatch.StartNew();while(r.Closes==0&&clock.Elapsed<TimeSpan.FromSeconds(10)){r.Frames();Thread.Sleep(1);}Assert.Equal(1,r.Closes);Assert.False(r.Dirty);Assert.False(r.Project.Find("a.json")!.Dirty);
        }
        finally{release.Set();}
    }
    [Theory][InlineData("v1","policy")][InlineData("v2","policy")][InlineData("xy","outside")][InlineData("v2","locked")][InlineData("v2","reentrant")]
    public void Mixed_group_refusal_never_partially_saves_plain_data_or_loses_newer_drafts(string kind,string refusal)
    {
        using var r=new Rig(kind);var level=File.ReadAllText(r.Level);var plain=File.ReadAllText(r.Plain);r.Edit();r.EditData();
        if(refusal=="policy"){if(kind=="v1")r.Workspace.Documents.SceneSaveCheck=_=>"Fixture refusal";else r.Workspace.Documents.Scene2SaveCheck=_=>"Fixture refusal";}
        if(refusal=="outside")File.AppendAllText(r.Level," ");
        if(refusal=="reentrant"){r.Workspace.Documents.Scene2SaveCheck=_=>null;r.Project.SaveCheck=_=>{r.Edit(2);return null;};}
        using var locked=refusal=="locked"?new FileStream(r.Level,FileMode.Open,FileAccess.Read,FileShare.Read):null;
        Assert.False(r.Workspace.SaveAll());Assert.Equal(plain,File.ReadAllText(r.Plain));Assert.Equal(refusal=="outside"?level+" ":level,File.ReadAllText(r.Level));Assert.True(r.Dirty);Assert.True(r.Project.Find("a.json")!.Dirty);
    }
    [Theory][InlineData("v1","Ctrl+W")][InlineData("v1","Ctrl+F4")][InlineData("v2","Ctrl+W")][InlineData("v2","Ctrl+F4")][InlineData("xy","Ctrl+W")][InlineData("xy","Ctrl+F4")]
    public void Keyboard_close_with_viewport_focus_preserves_dirty_scene_until_explicit_discard(string kind,string key)
    {
        using var r=new Rig(kind);r.Edit();r.Frames();r.Host.Press(r.Workspace.Draw,key.EndsWith("F4",StringComparison.Ordinal)?ImGuiKey.F4:ImGuiKey.W,ctrl:true);r.Frames();Assert.NotNull(r.Workspace.Active);Assert.True(r.Dirty);r.Press("Cancel");Assert.NotNull(r.Workspace.Active);Assert.True(r.Dirty);
        r.Host.Press(r.Workspace.Draw,key.EndsWith("F4",StringComparison.Ordinal)?ImGuiKey.F4:ImGuiKey.W,ctrl:true);r.Frames();r.Press("Don't Save");Assert.Null(r.Workspace.Active);Assert.True(r.Workspace.Show("a.json"));Assert.NotNull(r.Workspace.Window);
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public unsafe void Dock_tab_native_close_flag_uses_the_document_decision_instead_of_hiding_the_panel(string kind)
    {
        using var r=new Rig(kind);r.Edit();r.Frames();var name=kind=="xy"?"Flat XY###panel.scene2.flat":"Viewport###panel.editor.viewport";var window=ImGui.Internal.FindWindowByName(name);Assert.True(window.Ptr!=null);var dock=window.DockNode;Assert.True(dock.Ptr!=null);var style=ImGui.GetStyle();r.Host.Click(r.Workspace.Draw,dock.Pos.X+dock.Size.X-style.FramePadding.X-ImGui.GetFontSize()/2,dock.Pos.Y+style.FramePadding.Y+ImGui.GetFontSize()/2);r.Frames();Assert.True(r.Dirty);r.Press("Cancel");Assert.NotNull(r.Workspace.Active);Assert.True(r.Run("editor.close"));r.Frames();r.Press("Don't Save");Assert.Null(r.Workspace.Active);
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public unsafe void Floating_viewport_title_X_uses_the_scene_close_flow(string kind)
    {
        using var r=new Rig(kind);r.Edit();r.Frames();var name=kind=="xy"?"Flat XY###panel.scene2.flat":"Viewport###panel.editor.viewport";var window=ImGui.Internal.FindWindowByName(name);Assert.True(window.Ptr!=null);ImGui.Internal.SetWindowDock(window,0,ImGuiCond.Always);ImGui.SetWindowPos(name,new(360,220),ImGuiCond.Always);ImGui.SetWindowSize(name,new(600,500),ImGuiCond.Always);r.Frames();Assert.True(window.DockNode.Ptr==null);var style=ImGui.GetStyle();r.Host.Click(r.Workspace.Draw,window.Pos.X+window.Size.X-style.FramePadding.X-ImGui.GetFontSize()/2,window.Pos.Y+style.FramePadding.Y+ImGui.GetFontSize()/2);r.Frames();Assert.True(r.Dirty);r.Press("Don't Save");Assert.Null(r.Workspace.Active);Assert.True(r.Workspace.Show("a.json"));
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public void Floating_document_title_X_closes_through_the_same_scene_flow(string kind)
    {
        using var r=new Rig(kind);r.Edit();void Draw(){if(r.Workspace.Active?.Name=="level.json"){r.Window.GetType().GetMethod("Draw")!.Invoke(r.Window,null);typeof(DataWorkspace).GetMethod("DrawCloseChoices",Private)!.Invoke(r.Workspace,null);}else r.Workspace.Draw();}r.Host.Frames(Draw,4);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label.TrimStart(' ','*').StartsWith("level.json",StringComparison.Ordinal));var style=ImGui.GetStyle();r.Host.Click(Draw,node.Bounds.X+node.Bounds.Width-style.FramePadding.X-ImGui.GetFontSize()/2,node.Bounds.Y+style.FramePadding.Y+ImGui.GetFontSize()/2);r.Frames();r.Press("Don't Save");Assert.Null(r.Workspace.Active);
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public void Files_row_is_openable_by_its_typed_owner_and_file_menu_close_works(string kind)
    {
        using var r=new Rig(kind);Assert.False(r.Project.Find("level.json")!.Open());r.Frames();Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.Contains("level.json")&&n.Label.Contains("can't open"));r.Host.Press(r.Workspace.Draw,ImGuiKey.F,alt:true);var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Close");var ticket=Ui.RequestAction(item.Id,UiActions.Invoke);r.Frames();Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.Null(r.Workspace.Active);Assert.True(r.Workspace.Show("level.json"));Assert.Null(r.Project.Find("level.json")!.Session);
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public void File_list_Close_asks_before_discard_and_leaves_plain_files_usable(string kind)
    {
        using var r=new Rig(kind);r.Edit();r.Frames();var row=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.TrimStart(' ','*').StartsWith("level.json",StringComparison.Ordinal));var b=row.Bounds;r.Host.Click(r.Workspace.Draw,b.X+b.Width/2,b.Y+b.Height/2);r.Host.Press(r.Workspace.Draw,ImGuiKey.Menu);r.Frames();var close=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Close");var ticket=Ui.RequestAction(close.Id,UiActions.Invoke);r.Frames();Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);Assert.True(r.Dirty);r.Press("Don't Save");Assert.Null(r.Workspace.Active);Assert.True(r.Workspace.Show("a.json"));
    }
    [Theory][InlineData("v1")][InlineData("v2")][InlineData("xy")]
    public void Standalone_project_close_has_Save_Discard_Cancel_and_no_placeholder(string kind)
    {
        using var r=new Rig(kind);r.Edit();r.EditData();Assert.False(r.Workspace.CloseProject!());r.Frames();r.Press("Cancel");Assert.Equal(0,r.Closes);Assert.True(r.Dirty);Assert.False(r.Workspace.CloseProject());r.Frames();r.Press("Save all and close");Assert.Equal(1,r.Closes);Assert.False(r.Dirty);Assert.False(r.Project.Find("a.json")!.Dirty);r.Edit(3);Assert.False(r.Workspace.CloseProject());r.Frames();r.Press("Don't Save");Assert.Equal(2,r.Closes);
    }
    [Theory][InlineData("v2")][InlineData("xy")]
    public void Launcher_native_Save_all_close_Discard_reopen_journey_saves_headless_captures(string kind)
    {
        using var r=new Rig(kind,drawing:true);using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(r.Root,"recent.json")));Assert.True(launcher.Open(new(r.Data,r.Project.SchemaFolder)));var workspace=launcher.Workspace!;Assert.True(workspace.Documents.BindScene("level.json","scene",out _));Assert.True(workspace.Show("a.json"));Assert.True(workspace.Active!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));Assert.True(workspace.Show("level.json"));
        EditorWindow<Scene2> Window()=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",Private)!.GetValue(workspace)!;Assert.True(Window().Editor.Execute(Scene2Kit.Command("object.move",JsonSerializer.SerializeToElement(new{id="one",position=new[]{2,0,0}}))));r.Host.Frames(launcher.Draw,5);
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;var folder=Path.Combine(repo!.FullName,".validation/UI39/captures");void Capture(string suffix){var path=Path.Combine(folder,kind+suffix+".png");Directory.CreateDirectory(folder);File.WriteAllBytes(path.Replace(".png","-headless.png"),Png.Encode(r.Host.Width,r.Host.Height,r.Host.Pixels));}Capture("-before-save-all");
        void Press(string label){var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);var action=Ui.RequestAction(node.Id,UiActions.Invoke);r.Host.Frames(launcher.Draw,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);}
        Press("Save all");Assert.False(Window().Editor.Session.Dirty);Assert.False(workspace.Project.Find("a.json")!.Dirty);Assert.True(Window().Editor.Execute(Scene2Kit.Command("object.move",JsonSerializer.SerializeToElement(new{id="one",position=new[]{3,0,0}}))));r.Host.Frames(launcher.Draw,2);Press("Close project");Assert.True(launcher.SaveChangesPending);Press("Don't Save");Assert.Null(launcher.Workspace);Capture("-after-close-discard");
        Assert.True(launcher.Open(new(r.Data,r.Project.SchemaFolder)));workspace=launcher.Workspace!;Assert.True(workspace.Documents.BindScene("level.json","scene",out _));Assert.True(workspace.Show("level.json"));Assert.Equal(2,Window().Editor.Session.Current.Objects[0].Object.Position.X);r.Host.Frames(launcher.Draw,4);Capture("-after-reopen");
    }
}
