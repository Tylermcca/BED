using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram=Brutal.Authoring.Launcher.Program;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UndoHistoryQa16Tests
{
    [Theory]
    [InlineData(false,"Save")][InlineData(false,"Don't Save")]
    [InlineData(true,"Save")][InlineData(true,"Don't Save")]
    public void Actual_Play_natural_exit_or_keyboard_Stop_preserves_history_and_keyboard_Undo_and_redo(bool stop,string saveChoice)
    {
        using var r=new SamplePlayJourneyTests.CommonRig(seconds:stop?60:1);
        var session=r.Window.Editor.Session;var current=session.Current;var revision=session.Revision;
        var journal=session.Journal.ToArray();var journalBase=session.JournalBase;var steps=session.History.UndoCount;
        var saved=saveChoice=="Save"?session.TextToSave:SamplePlayJourneyTests.Saved;
        r.Play();var game=r.Choose(saveChoice);
        if(stop)
        {
            r.Headless.Press(r.Draw,ImGuiKey.F5,shift:true);r.Until(()=>game.HasCompleted,game);r.Headless.Frames(r.Draw,2);
            Assert.Equal(1,game.Stops);Assert.Contains("ended it",game.Status,StringComparison.Ordinal);
        }
        else
        {
            r.Until(()=>game.HasCompleted,game);r.Headless.Frames(r.Draw,2);Assert.Equal(0,game.ExitCode);Assert.Equal(0,game.Stops);
        }
        Assert.Same(current,session.Current);Assert.Equal(revision,session.Revision);Assert.Same(journalBase,session.JournalBase);
        Assert.Equal(journal,session.Journal);Assert.Equal(steps,session.History.UndoCount);Assert.True(session.History.CanRedo);
        Assert.Equal(saved,File.ReadAllText(r.File));Assert.Equal(saved,game.SavedAtStart);
        Assert.Null(r.Window.Editor.Playing);Assert.False(r.Window.ProjectPlaying!());
        r.Headless.Press(r.Draw,ImGuiKey.Z,ctrl:true);r.Headless.Frames(r.Draw,2);
        Assert.Equal(new Vector3(2,3,4),session.Current.Find("root")!.Position);Assert.Equal(steps-1,session.History.UndoCount);
        Assert.Equal(saveChoice=="Save",session.Dirty);Assert.Equal(saved,File.ReadAllText(r.File));
        r.Headless.Press(r.Draw,ImGuiKey.Y,ctrl:true);r.Headless.Frames(r.Draw,2);
        Assert.Equal(new Vector3(9,8,7),session.Current.Find("root")!.Position);Assert.Equal(steps,session.History.UndoCount);
        Assert.True(session.History.CanRedo); // The redo that existed before Play must still be reachable too.
        r.Headless.Press(r.Draw,ImGuiKey.Y,ctrl:true);r.Headless.Frames(r.Draw,2);
        Assert.Equal(new Vector3(10,8,7),session.Current.Find("root")!.Position);Assert.Equal(steps+1,session.History.UndoCount);
        Assert.False(session.History.CanRedo);Assert.Equal(revision+3,session.Revision);
        Assert.Equal(journal,session.Journal.Take(journal.Length));Assert.Equal(journal.Length+3,session.Journal.Count);
        Assert.Same(journalBase,session.JournalBase);Assert.Equal(saved,File.ReadAllText(r.File));
    }
    private sealed class ObservedExport(IGameExportHost inner):IGameExportHost
    {
        public IGameBuildOperation? Operation;public GameBuildReceipt? Receipt;public GameExportSmokeResult? Checked;
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)=>Operation=inner.Start(spec,root,dirty,plan);
        public async Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec command,CancellationToken token)
        {Receipt=receipt;return Checked=await inner.Smoke(plan,receipt,outputs,command,token);}
    }
    [Theory]
    [InlineData("Save")][InlineData("Don't Save")]
    public void Actual_Export_build_check_and_copy_preserve_history_and_keyboard_Undo_and_existing_redo(string choice)
    {
        using var owned=new CapturedExportTests.Owned();var root=ReleasePlayProbeTests.CopyProject(owned.Root);
        var path=Path.Combine(root,"data","level.json");var original=File.ReadAllBytes(path);
        var assets=Directory.GetFiles(Path.Combine(root,"assets"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));
        using var host=new HeadlessHost(1500,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var loaded=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(loaded.Problems);
        using var launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(owned.Root,"local","recent.json"))){Loaded=loaded.Loaded});
        var export=new ObservedExport(Assert.IsType<ExportBuildHost>(launcher.GameExportHost));launcher.GameExportHost=export;
        Assert.True(launcher.OpenFolder(root),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("level.json"));
        host.Frames(launcher.Draw,4);var window=workspace.SceneWindow!;var session=window.Editor.Session;
        const string id="textured_crate";
        Assert.True(window.Editor.Execute(new MoveObject(id,new(2,0,0)),id));Assert.True(window.Editor.Execute(new MoveObject(id,new(3,0,0)),id));
        Assert.True(session.Undo());var draft=session.Current;var journal=session.Journal.ToArray();var journalBase=session.JournalBase;
        var steps=session.History.UndoCount;var revision=session.Revision;var pointer=File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json"));
        void Key(ImGuiKey key,bool ctrl=false,bool alt=false){host.Press(launcher.Draw,key,ctrl:ctrl,alt:alt);host.Frames(launcher.Draw,2);}
        Key(ImGuiKey.F,alt:true);var menu=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Export build...");
        Assert.True(menu.Enabled,menu.DisabledReason);Assert.True(Enum.TryParse<ImGuiKey>(menu.AccessKey,out var access));Key(access,alt:true);
        Choose(choice,"Export saved files");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Window&&n.Label=="Export build"),"Export save decision");
        Choose("Build saved files","Export build");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label=="Copy build to folder..."&&n.Enabled),"Verified export");
        Assert.True(export.Receipt!.Succeeded,export.Receipt.Reason);Assert.Equal(0,export.Receipt.ExitCode);Assert.NotNull(export.Checked!.Build);
        var destination=Path.Combine(owned.Root,"exported");var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Destination folder");
        var ticket=Ui.RequestAction(field.Id,UiActions.SetValue,destination);host.Frames(launcher.Draw,2);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);
        Choose("Copy build to folder...","Export build");
        Until(()=>Ui.Snapshot().Nodes.Any(n=>n.Label=="Export status: Build copied to "+destination),"Copied export");
        var exported=SceneStarter.SavedScene.Open(destination).Level.Scene;
        Assert.Equal(choice=="Save"?new Vector3(2,0,0):Vector3.Zero,exported.Find(id)!.Position);
        Key(ImGuiKey.Escape);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Export build");
        Assert.Same(draft,session.Current);Assert.Equal(revision,session.Revision);Assert.Equal(journal,session.Journal);
        Assert.Same(journalBase,session.JournalBase);Assert.Equal(steps,session.History.UndoCount);Assert.True(session.History.CanRedo);
        var saved=File.ReadAllBytes(path);if(choice=="Don't Save")Assert.Equal(original,saved);else Assert.NotEqual(original,saved);
        Key(ImGuiKey.Z,ctrl:true);Assert.Equal(Vector3.Zero,session.Current.Find(id)!.Position);Assert.Equal(steps-1,session.History.UndoCount);
        Assert.Equal(choice=="Save",session.Dirty);Key(ImGuiKey.Y,ctrl:true);Assert.Equal(new Vector3(2,0,0),session.Current.Find(id)!.Position);
        Assert.Equal(steps,session.History.UndoCount);Assert.True(session.History.CanRedo);
        Key(ImGuiKey.Y,ctrl:true);Assert.Equal(new Vector3(3,0,0),session.Current.Find(id)!.Position);Assert.Equal(steps+1,session.History.UndoCount);
        Assert.False(session.History.CanRedo);Assert.Equal(revision+3,session.Revision);Assert.Equal(journal.Length+3,session.Journal.Count);
        Assert.Equal(journal,session.Journal.Take(journal.Length));Assert.Same(journalBase,session.JournalBase);Assert.Equal(saved,File.ReadAllBytes(path));
        Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json")));
        foreach(var (file,hash) in assets)Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(file)));
        void Choose(string label,string owner)
        {
            for(var i=0;i<20;i++)
            {
                var nodes=Ui.Snapshot().Nodes;
                var button=Assert.Single(nodes,n=>n.Role==UiRole.Button&&n.Label==label&&Inside(n));
                if(button.Focused){Key(ImGuiKey.Enter);return;}Key(ImGuiKey.Tab);
                bool Inside(UiNode node)
                {for(var parent=node.Parent;parent>=0;parent=nodes[parent].Parent)if(nodes[parent].Role==UiRole.Window&&nodes[parent].Label==owner)return true;return false;}
            }
            Assert.Fail("Keyboard did not reach "+label+" in "+owner);
        }
        void Until(Func<bool> done,string step)
        {
            var watch=Stopwatch.StartNew();while(!done()&&watch.Elapsed<TimeSpan.FromSeconds(10)){host.Frame(launcher.Draw);Thread.Sleep(1);}
            Assert.True(done(),step+": "+workspace.Said+"; "+Ui.Snapshot().Text);
        }
    }
}
