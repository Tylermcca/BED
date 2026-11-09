using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DropDispatchA453Tests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"drop-dispatch-"+Guid.NewGuid().ToString("N"));
        public readonly HeadlessHost Host=new(1400,900) { Drawing=false };
        public ProjectLauncher Launcher { get; }
        public Action? Extra;
        public string CurrentFolder { get; }
        public string Source { get; }
        public string Destination=>Path.Combine(CurrentFolder,"data","items.json");
        public Rig(bool open=true)
        {
            Directory.CreateDirectory(Root);
            CurrentFolder=Project("Current");
            var input=Directory.CreateDirectory(Path.Combine(Root,"input")).FullName;
            Source=Path.Combine(input,"items.json"); File.WriteAllText(Source,"{\"count\":2}");
            Launcher=new(new RecentProjects(Path.Combine(Root,"settings","recent.json")),
                creator:StartWindowTestProjects.Creator(Root),projectsFolder:Path.Combine(Root,"projects"));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            if(open) Assert.True(Launcher.OpenFolder(CurrentFolder),Launcher.Said);
            Host.Frames(Draw,3);
        }
        public string Project(string name)
        {
            var folder=Path.Combine(Root,name);
            Directory.CreateDirectory(Path.Combine(folder,"data")); Directory.CreateDirectory(Path.Combine(folder,"schemas"));
            File.WriteAllText(Path.Combine(folder,"schemas","items.schema.json"),
                """{"type":"object","required":["count"],"properties":{"count":{"type":"integer","minimum":0}}}""");
            return folder;
        }
        public void Draw() { Launcher.Draw(); Extra?.Invoke(); }
        public (WindowDrop Drop,UiSnapshot Frame) Capture(string window,params string[] paths)
        {
            Host.Frames(Draw,2); var frame=Ui.Snapshot();
            var native=Brutal.ImGuiApi.ImGui.Internal.FindWindowByName(window);
            Assert.False(native.IsNull());
            var id=(((ulong)UiRole.Window+1)<<32)|native.ID.Value;
            var node=Assert.Single(frame.Nodes,n=>n.Id==id && n.Role==UiRole.Window);
            var b=node.Bounds; using var queue=new WindowDropQueue();
            Assert.True(queue.Post(paths,b.X+b.Width/2,b.Y+b.Height/2));
            Assert.True(queue.TryTakeDrop(out var drop)); Assert.NotNull(drop);
            return(drop,frame);
        }
        public bool Dispatch((WindowDrop Drop,UiSnapshot Frame) captured)=>
            Launcher.DispatchDrop(captured.Drop.Paths,captured.Frame,captured.Drop.TargetWindow(captured.Frame));
        public void Click(string label)
        {
            var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label==label);
            var b=button.Bounds; Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2); Host.Frames(Draw,2);
        }
        public void NoCopy()
        {
            Host.Frames(Draw,2);
            Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Copy it");
            Assert.False(File.Exists(Destination)); Assert.Equal("{\"count\":2}",File.ReadAllText(Source));
        }
        public void Dispose()
        {
            Launcher.Dispose(); Host.Dispose();
            var full=Path.GetFullPath(Root); var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Path.GetDirectoryName(full)!=temp || !Path.GetFileName(full).StartsWith("drop-dispatch-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected routing-test cleanup boundary.");
            Directory.Delete(full,true);
        }
    }

    [Fact]
    public void Completed_start_window_target_opens_only_the_existing_folder_receiver()
    {
        using var r=new Rig(open:false); var before=File.ReadAllBytes(Path.Combine(r.CurrentFolder,"schemas","items.schema.json"));
        var captured=r.Capture("BED###launcher",r.CurrentFolder);
        Assert.True(r.Dispatch(captured),r.Launcher.Said);
        Assert.Equal(Path.Combine(r.CurrentFolder,"data"),r.Launcher.Current!.Data); Assert.Single(r.Launcher.Recent.Items);
        Assert.False(r.Dispatch(captured)); // The same start frame cannot replace the now-open workspace.
        Assert.Equal(before,File.ReadAllBytes(Path.Combine(r.CurrentFolder,"schemas","items.schema.json")));
    }

    [Fact]
    public void Completed_Files_target_only_stages_copy_and_real_confirmation_preserves_the_source()
    {
        using var r=new Rig(); var workspace=r.Launcher.Workspace;
        Assert.True(r.Dispatch(r.Capture("Files###panel.project.files",r.Source)),r.Launcher.Said);
        Assert.False(File.Exists(r.Destination)); r.Host.Frames(r.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Copy items.json into this project's data folder?");
        r.Click("Copy it");
        Assert.Same(workspace,r.Launcher.Workspace); Assert.Equal("{\"count\":2}",File.ReadAllText(r.Source));
        Assert.Equal(File.ReadAllText(r.Source),File.ReadAllText(r.Destination)); Assert.Single(r.Launcher.Recent.Items);
    }

    [Fact]
    public void A_folder_on_Files_never_replaces_an_open_project()
    {
        using var r=new Rig(); var replacement=r.Project("Another"); var workspace=r.Launcher.Workspace; var current=r.Launcher.Current;
        Assert.False(r.Dispatch(r.Capture("Files###panel.project.files",replacement)));
        Assert.Same(workspace,r.Launcher.Workspace); Assert.Same(current,r.Launcher.Current);
        r.NoCopy(); Assert.Contains("Folders",workspace!.Said);
    }

    [Fact]
    public void Visible_Files_label_on_an_unrelated_window_is_not_a_fallback_target()
    {
        using var r=new Rig();
        r.Extra=()=>
        {
            Brutal.ImGuiApi.ImGui.SetNextWindowPos(new float2(1000,40));
            Brutal.ImGuiApi.ImGui.SetNextWindowSize(new float2(300,240));
            Ui.Begin("Files###unrelated.drop.window"u8); Ui.TextWrapped("Invented unrelated window"u8); Ui.End();
        };
        Assert.False(r.Dispatch(r.Capture("Files###unrelated.drop.window",r.Source)));
        r.NoCopy(); Assert.Contains("JSON file on Files",r.Launcher.Said);
    }

    [Fact]
    public void Old_completed_frame_cannot_start_a_confirmation_after_a_new_frame()
    {
        using var r=new Rig(); var captured=r.Capture("Files###panel.project.files",r.Source);
        r.Host.Frames(r.Draw,1); Assert.False(r.Dispatch(captured));
        Assert.Contains("target changed",r.Launcher.Said); r.NoCopy();
    }

    [Fact]
    public void A_changed_workspace_cannot_inherit_the_old_workspaces_Files_target()
    {
        using var r=new Rig(); var captured=r.Capture("Files###panel.project.files",r.Source);
        var second=r.Project("Second"); Assert.True(r.Launcher.OpenFolder(second));
        var workspace=r.Launcher.Workspace; Assert.False(r.Dispatch(captured));
        Assert.Same(workspace,r.Launcher.Workspace); Assert.Equal(Path.Combine(second,"data"),r.Launcher.Current!.Data);
        Assert.False(File.Exists(Path.Combine(second,"data","items.json"))); r.NoCopy();
    }

    [Fact]
    public void An_open_popup_blocks_a_background_Files_drop()
    {
        using var r=new Rig();
        r.Extra=()=>
        {
            Brutal.ImGuiApi.ImGui.SetNextWindowPos(new float2(1000,40));
            Brutal.ImGuiApi.ImGui.SetNextWindowSize(new float2(300,240));
            Ui.Begin("Popup host###drop.popup.host"u8);
            Brutal.ImGuiApi.ImGui.OpenPopup("Drop blocker"u8);
            if(Ui.BeginPopup("Drop blocker"u8)) { Ui.TextWrapped("A menu is open"u8); Ui.EndPopup(); }
            Ui.End();
        };
        var captured=r.Capture("Files###panel.project.files",r.Source);
        Assert.False(r.Dispatch(captured)); Assert.Contains("menu or dialog",r.Launcher.Said); r.NoCopy();
    }
}
