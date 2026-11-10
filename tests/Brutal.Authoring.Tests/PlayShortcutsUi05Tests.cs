using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlayShortcutsUi05Tests
{
    private sealed class Session : IProjectPlaySession
    {
        public bool HasCompleted{get;private set;}=true;
        public bool CanBuildAndPlay=>false;
        public int? ExitCode=>null;
        public string Status=>HasCompleted?"Stopped.":"Running.";
        public int Stops;public bool Built;
        public bool Start(bool dirty,bool buildAndPlay=false){Assert.False(dirty);Built=buildAndPlay;HasCompleted=false;return true;}
        public void RequestStop(){Stops++;HasCompleted=true;}
        public void Update() { }
        public void Draw() { }
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
        public void Dispose()=>HasCompleted=true;
    }
    private sealed class Factory : IProjectPlayHost
    {
        public readonly List<Session> Sessions=[];
        public IProjectPlaySession Start(PlayLaunch launch){var session=new Session();Sessions.Add(session);return session;}
    }
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("play-shortcuts-ui05-").FullName;
        public readonly HeadlessHost UiHost=new(1400,950){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly Factory Host=new();
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string ScenePath=>Path.Combine(Root,"data","room.json");
        public Rig(bool configure=true,bool build=true)
        {
            EditorWindow<Scene>.Configure(ImGui.GetIO());
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File.WriteAllText(ScenePath,new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)])));
            var project=DataProject.Open(data,schemas);Assert.True(DocumentHost.For(project).BindScene("room.json","scene",out _));
            Workspace=new(project);Assert.True(Workspace.Show("room.json"));
            File.WriteAllText(Path.Combine(Root,"game.exe"),"inert fixture");File.WriteAllText(Path.Combine(Root,"build.exe"),"inert fixture");
            if(configure)PlaySettingsStore.Save(Root,new("game.exe",build:build?new("./build.exe"):null));
            Workspace.BindPlaySettings(Root,null,Path.Combine(Root,"local"));Workspace.BindProjectPlay(Host);UiHost.Frames(Workspace.Draw,4);
        }
        public void Key(bool ctrl=false,bool shift=false)=>UiHost.Press(Workspace.Draw,ImGuiKey.F5,ctrl:ctrl,shift:shift);
        public UiNode Button(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);
        public void OpenTools(){UiHost.Press(Workspace.Draw,ImGuiKey.T,alt:true);UiHost.Frames(Workspace.Draw,2);}
        public void Dispose(){Workspace.Dispose();UiHost.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void F5_starts_once_ShiftF5_stops_and_CtrlF5_keeps_the_exact_build_approval_route()
    {
        using var r=new Rig();var saved=File.ReadAllBytes(r.ScenePath);
        Assert.Equal("Play (F5)",r.Button("Play").Tooltip);Assert.Equal("Build and Play (Ctrl+F5)",r.Button("Build and Play").Tooltip);
        r.Key();var first=Assert.Single(r.Host.Sessions);Assert.False(first.Built);Assert.False(first.HasCompleted);
        Assert.Equal("Stop (Shift+F5)",r.Button("Stop").Tooltip);
        r.Key(shift:true);Assert.True(first.HasCompleted);Assert.Equal(1,first.Stops);
        r.Key(ctrl:true);Assert.True(r.Workspace.ProjectPlayApprovalPending);Assert.Single(r.Host.Sessions);
        Assert.Contains("build.exe",r.Workspace.Said);Assert.True(r.Workspace.ApproveProjectPlay());
        Assert.Equal(2,r.Host.Sessions.Count);Assert.True(r.Host.Sessions[1].Built);
        Assert.Equal(saved,File.ReadAllBytes(r.ScenePath));Assert.False(r.Window.Editor.Session.Dirty);
    }
    [Fact]
    public void Tools_menu_exposes_all_three_current_shortcuts_and_plain_stop_refusal()
    {
        using var r=new Rig();r.OpenTools();
        var play=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Play");
        var build=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Build and Play");
        var stop=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Stop");
        Assert.True(play.Enabled);Assert.Equal("Play (F5)",play.Tooltip);
        Assert.True(build.Enabled);Assert.Equal("Build and Play (Ctrl+F5)",build.Tooltip);
        Assert.False(stop.Enabled);Assert.Equal("Nothing is playing.",stop.DisabledReason);
        Assert.Equal("Shift+F5",r.Window.Shortcuts.For("editor.stop")!.Value.ToString());
        Assert.Empty(r.Host.Sessions);
    }
    [Fact]
    public void Missing_program_has_plain_tree_guidance_and_F5_opens_settings_without_launching()
    {
        using var r=new Rig(configure:false);var play=r.Button("Play");
        Assert.False(play.Enabled);Assert.Equal("Set the program in Tools > Play settings",play.DisabledReason);
        Assert.Equal(play.DisabledReason,play.Tooltip);r.Key();
        Assert.True(r.Workspace.PlaySettings!.Open);Assert.Equal("Set the program in Tools > Play settings",r.Workspace.Said);
        Assert.Empty(r.Host.Sessions);Assert.False(File.Exists(Path.Combine(r.Root,".bed","project.json")));
    }
    [Fact]
    public void CtrlF5_without_a_build_command_does_not_run_the_existing_game()
    {
        using var r=new Rig(build:false);Assert.False(r.Button("Build and Play").Enabled);
        r.Key(ctrl:true);Assert.Empty(r.Host.Sessions);Assert.Equal("Set a build command in Tools > Play settings",r.Window.Status);
    }
    [Fact]
    public void F5_is_available_while_a_text_field_is_focused_without_changing_its_draft()
    {
        using var r=new Rig();var draft=new ImInputString(100,"kept draft"u8);var focus=true;
        void Draw()
        {
            r.Workspace.Draw();
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(300,300));ImGui.SetNextWindowSize(new Brutal.Numerics.float2(350,150));
            Ui.Begin("Shortcut draft fixture"u8);
            if(focus){ImGui.SetNextWindowFocus();ImGui.SetKeyboardFocusHere();focus=false;}
            Ui.InputText("Draft"u8,draft);Ui.End();
        }
        r.UiHost.Frames(Draw,4);Assert.True(ImGui.GetIO().WantTextInput);
        r.UiHost.Press(Draw,ImGuiKey.F5);Assert.Single(r.Host.Sessions);Assert.Equal("kept draft",draft.ToString());
    }
    [Fact]
    public void A_person_can_rebind_Play_and_the_tooltip_uses_the_current_binding()
    {
        using var r=new Rig();r.Window.Shortcuts.Set("editor.play.project","F6");r.UiHost.Frames(r.Workspace.Draw,2);
        Assert.Equal("Play (F6)",r.Button("Play").Tooltip);r.Key();Assert.Empty(r.Host.Sessions);
        r.UiHost.Press(r.Workspace.Draw,ImGuiKey.F6);Assert.Single(r.Host.Sessions);
    }
}
