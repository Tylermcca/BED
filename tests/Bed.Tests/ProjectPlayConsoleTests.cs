using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Launcher;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ProjectPlayConsoleTests
{
    private sealed class Session : IProjectPlaySession
    {
        private readonly Queue<ProjectPlayOutput> pending=new();
        public int Stops,Starts;
        public bool CanBuildAndPlay=>false;
        public bool HasCompleted {get;private set;}=true;
        public int? ExitCode=>null;
        public string Status=>"Console test program running.";
        public bool Start(bool dirty,bool build=false){Assert.False(dirty);Starts++;HasCompleted=false;return true;}
        public void RequestStop(){Stops++;HasCompleted=true;}
        public void Update(){}
        public IReadOnlyList<ProjectPlayOutput> DrainLogs(){var lines=pending.ToArray();pending.Clear();return lines;}
        public void Push(bool error,string text)=>pending.Enqueue(new(error,text));
        public void Draw(){}
        public void Dispose()=>HasCompleted=true;
    }
    private sealed class Factory : IProjectPlayHost
    {
        public readonly Session Session=new();
        public IProjectPlaySession Start(PlayLaunch launch)=>Session;
    }
    private sealed class Rig : IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("play-console-").FullName;
        public readonly HeadlessHost Host=new(1200,900){Drawing=false};
        public readonly Factory Factory=new();
        public readonly ProjectLauncher Launcher;
        public DataWorkspace Workspace=>Launcher.Workspace!;
        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));
            File.WriteAllText(Path.Combine(root,"data","item.json"),"{}");File.WriteAllText(Path.Combine(root,"schemas","item.schema.json"),"{}");
            File.WriteAllText(Path.Combine(root,"game.exe"),"inert; test factory only");PlaySettingsStore.Save(root,new("game.exe"));
            EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
            Launcher=Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local","recent.json"))){ProjectPlayHost=Factory});
            Assert.True(Launcher.OpenFolder(root),Launcher.Said);Assert.True(Workspace.Show("item.json"));
            Host.Frames(Workspace.Draw,3);Assert.True(Workspace.RequestProjectPlay(),Workspace.Said);Host.Frames(Workspace.Draw,3);
        }
        public void Key(ImGuiKey key,bool shift=false){Host.Press(Workspace.Draw,key,shift:shift);Host.Frames(Workspace.Draw,2);}
        public UiNode Node(string label)
        {
            var nodes=Ui.Snapshot().Nodes;
            var console=Assert.Single(nodes.Select((node,index)=>(node,index)),p=>p.node.Role==UiRole.Window&&p.node.Label=="Play console").index;
            var role=label is "Clear console" or "Copy all"?UiRole.Button:label is "Show stdout" or "Show stderr"?UiRole.Checkbox:UiRole.Text;
            bool InConsole(UiNode node)
            {
                for(var parent=node.Parent;parent>=0;parent=nodes[parent].Parent)if(parent==console)return true;
                return false;
            }
            return Assert.Single(nodes,n=>n.Role==role&&n.Label==label&&InConsole(n));
        }
        public void Click(string label){var n=Node(label);Host.Click(Workspace.Draw,n.Bounds.X+n.Bounds.Width/2,n.Bounds.Y+n.Bounds.Height/2);Host.Frames(Workspace.Draw,2);}
        public void Dispose()
        {
            Assert.Equal("{}",File.ReadAllText(Path.Combine(root,"data","item.json")));Assert.Empty(Workspace.Project.Dirty);
            Launcher.Dispose();Host.Dispose();Directory.Delete(root,true);
        }
    }
    [Fact]
    public void Console_controls_are_named_in_tree_and_Tab_activates_copy_filters_and_clear_without_stopping()
    {
        using var r=new Rig();r.Factory.Session.Push(false,"ordinary stdout");r.Factory.Session.Push(true,"ordinary stderr");r.Host.Frames(r.Workspace.Draw,2);
        foreach(var label in new[]{"Clear console","Copy all","Show stdout","Show stderr"})
        {var n=r.Node(label);Assert.True(n.Enabled);Assert.True((n.Actions&UiActions.Focus)!=0);Assert.False(n.Bounds.IsEmpty);}
        Assert.True(r.Node("Clear console").Focused,Ui.Snapshot().Text);
        r.Key(ImGuiKey.Tab);Assert.True(r.Node("Copy all").Focused);r.Key(ImGuiKey.Enter);
        var expected="stdout: ordinary stdout"+Environment.NewLine+"stderr: ordinary stderr";
        Assert.Equal(expected,ImGui.GetClipboardText().ToString());
        r.Key(ImGuiKey.Tab);Assert.True(r.Node("Show stdout").Focused);r.Key(ImGuiKey.Space);
        Assert.False(r.Node("Show stdout").Checked);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="ordinary stdout");_ = r.Node("stderr: ordinary stderr");
        r.Key(ImGuiKey.Tab);Assert.True(r.Node("Show stderr").Focused);r.Key(ImGuiKey.Space);
        Assert.False(r.Node("Show stderr").Checked);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="stderr: ordinary stderr");
        r.Key(ImGuiKey.Tab,true);r.Key(ImGuiKey.Tab,true);Assert.True(r.Node("Copy all").Focused);r.Key(ImGuiKey.Enter);
        Assert.Equal(expected,ImGui.GetClipboardText().ToString()); // All retained streams, even while both are hidden.
        r.Key(ImGuiKey.Tab,true);Assert.True(r.Node("Clear console").Focused,Ui.Snapshot().Text);r.Key(ImGuiKey.Enter);
        r.Key(ImGuiKey.Tab);r.Key(ImGuiKey.Enter);Assert.Equal("",ImGui.GetClipboardText().ToString());
        Assert.Equal(0,r.Factory.Session.Stops);Assert.Equal(1,r.Factory.Session.Starts);Assert.False(r.Factory.Session.HasCompleted);
        r.Click("Show stdout");r.Factory.Session.Push(false,"after clear");r.Host.Frames(r.Workspace.Draw,2);_ = r.Node("after clear");
    }
    [Fact]
    public void Console_retains_256_bounded_lines_reports_evictions_and_resets_drop_note_on_clear_and_new_run()
    {
        using var r=new Rig();
        for(var i=0;i<260;i++)r.Factory.Session.Push(i%2==1,$"line-{i:D3}");
        r.Factory.Session.Push(false,new string('x',5000));r.Host.Frames(r.Workspace.Draw,2);
        var lines=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Text&&(n.Label.StartsWith("line-")||n.Label.StartsWith("stderr: line-")||n.Label.StartsWith("xxx"))).ToArray();
        Assert.Equal(256,lines.Length);Assert.Equal("stderr: line-005",lines[0].Label);Assert.Equal(new string('x',4096),lines[^1].Label);
        const string dropped="5 lines dropped. Showing the most recent 256 lines.";_ = r.Node(dropped);
        r.Click("Copy all");var copy=ImGui.GetClipboardText().ToString()!;Assert.StartsWith(dropped+Environment.NewLine,copy);
        Assert.DoesNotContain("line-004",copy);Assert.Contains("stderr: line-005",copy);Assert.Contains("stdout: "+new string('x',4096),copy);
        r.Click("Clear console");Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.Contains("lines dropped"));
        r.Click("Copy all");Assert.Equal("",ImGui.GetClipboardText().ToString());
        r.Factory.Session.Push(false,"new line");r.Host.Frames(r.Workspace.Draw,2);_ = r.Node("new line");
        r.Factory.Session.RequestStop();Assert.True(r.Workspace.RequestProjectPlay());r.Host.Frames(r.Workspace.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="new line"||n.Label.Contains("lines dropped"));Assert.Equal(2,r.Factory.Session.Starts);
    }
}
