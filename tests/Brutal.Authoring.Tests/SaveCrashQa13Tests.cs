using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class SaveCrashQa13Tests
{
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("save-crash-qa13-").FullName;
        public string Data=>Path.Combine(Root,"data");
        public string FilePath=>Path.Combine(Data,"level.json");
        public readonly byte[] Original;
        public readonly DateTime Stamp;
        public Fixture()
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(FilePath,new SceneFormat().Write(new Scene([SceneObject.At("crate",null,Vector3.Zero)])));
            Original=File.ReadAllBytes(FilePath);Stamp=File.GetLastWriteTimeUtc(FilePath);
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    private sealed class TempWrites:IDisposable
    {
        private readonly FileSystemWatcher watcher;
        private readonly ConcurrentQueue<(WatcherChangeTypes Kind,string Path)> events=new();
        private Exception? error;
        public TempWrites(string directory)
        {
            watcher=new(directory,".level.json.*.tmp"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.Size|NotifyFilters.LastWrite};
            void Saw(object sender,FileSystemEventArgs e)=>events.Enqueue((e.ChangeType,e.FullPath));
            watcher.Created+=Saw;watcher.Changed+=Saw;watcher.Deleted+=Saw;watcher.Error+=(_,e)=>error=e.GetException();watcher.EnableRaisingEvents=true;
        }
        public void AssertWroteThenCleaned()
        {
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromSeconds(3)&&!events.Select(e=>e.Kind).ToHashSet().IsSupersetOf(new[]{WatcherChangeTypes.Created,WatcherChangeTypes.Changed,WatcherChangeTypes.Deleted}))Thread.Sleep(2);
            Assert.Null(error);var snapshot=events.ToArray();Assert.NotEmpty(snapshot);
            var name=Assert.Single(snapshot.Select(e=>e.Path).Distinct());
            Assert.Contains(snapshot,e=>e.Kind==WatcherChangeTypes.Created);Assert.Contains(snapshot,e=>e.Kind==WatcherChangeTypes.Changed);Assert.Contains(snapshot,e=>e.Kind==WatcherChangeTypes.Deleted);
            Assert.False(File.Exists(name));
        }
        public void Dispose()=>watcher.Dispose();
    }
    [Fact]
    public void Actual_atomic_replace_failure_occurs_after_temp_write_and_preserves_the_saved_scene_and_exact_draft()
    {
        using var f=new Fixture();var session=DocumentSession<Scene>.Open(f.FilePath,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
        Assert.True(session.Execute(new MoveObject("crate",new(5,2,1))));var draft=session.Current;var journal=session.Journal.ToArray();var revision=session.Revision;var baseline=session.ExpectedOnDisk;
        using var observed=new TempWrites(f.Data);
        // Reads still work, but Windows refuses replacing this name. AtomicFile must first write and flush its sibling temp.
        using(var held=new FileStream(f.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var failure=Assert.Throws<UnauthorizedAccessException>(()=>session.Save());Assert.Contains("AtomicFile.Write",failure.StackTrace);Assert.Contains("FileSystem.MoveFile",failure.StackTrace);
            Assert.Equal(f.Original,File.ReadAllBytes(f.FilePath));Assert.Equal(f.Stamp,File.GetLastWriteTimeUtc(f.FilePath));
            Assert.Same(draft,session.Current);Assert.True(session.Dirty);Assert.Equal(revision,session.Revision);Assert.Equal(journal,session.Journal);Assert.Equal(baseline,session.ExpectedOnDisk);
        }
        observed.AssertWroteThenCleaned();Assert.Empty(Directory.GetFiles(f.Data,"*.tmp"));
        session.Save();Assert.False(session.Dirty);Assert.Equal(new Vector3(5,2,1),new SceneFormat().Read(File.ReadAllText(f.FilePath)).Find("crate")!.Position);
    }
    [Fact]
    public void Keyboard_scene_save_reports_replace_failure_and_retains_both_undo_redo_then_retry_saves_only_that_draft()
    {
        using var f=new Fixture();using var workspace=new DataWorkspace(DataProject.Open(f.Data,Path.Combine(f.Root,"schemas")));
        Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);Assert.True(workspace.Show("level.json"));
        var window=workspace.SceneWindow!;var session=window.Editor.Session;
        Assert.True(session.Execute(new MoveObject("crate",new(5,2,1))));Assert.True(session.Execute(new MoveObject("crate",new(6,3,2))));Assert.True(session.Undo());
        var draft=session.Current;var revision=session.Revision;var journal=session.Journal.ToArray();var undo=session.History.UndoCount;var redo=session.History.CanRedo;var baseline=session.ExpectedOnDisk;
        using var host=new HeadlessHost(1400,900){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());host.Frames(workspace.Draw,4);
        using var observed=new TempWrites(f.Data);
        using(var held=new FileStream(f.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            host.Press(workspace.Draw,ImGuiKey.S,ctrl:true);host.Frames(workspace.Draw,2);
            Assert.Contains("level.json",window.Status);Assert.Contains("can't be written",window.Status);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("level.json",StringComparison.Ordinal)&&n.Label.Contains("can't be written",StringComparison.Ordinal));
            Assert.Equal(f.Original,File.ReadAllBytes(f.FilePath));Assert.Equal(f.Stamp,File.GetLastWriteTimeUtc(f.FilePath));
            Assert.Same(draft,session.Current);Assert.True(session.Dirty);Assert.Equal(revision,session.Revision);Assert.Equal(journal,session.Journal);
            Assert.Equal(undo,session.History.UndoCount);Assert.Equal(redo,session.History.CanRedo);Assert.Equal(baseline,session.ExpectedOnDisk);
        }
        observed.AssertWroteThenCleaned();Assert.Empty(Directory.GetFiles(f.Data,"*.tmp"));
        host.Press(workspace.Draw,ImGuiKey.S,ctrl:true);host.Frames(workspace.Draw,2);Assert.False(session.Dirty);Assert.Equal("Saved level.json.",window.Status);
        Assert.Same(draft,session.Current);Assert.Equal(new Vector3(5,2,1),new SceneFormat().Read(File.ReadAllText(f.FilePath)).Find("crate")!.Position);
        Assert.Equal(undo,session.History.UndoCount);Assert.Equal(redo,session.History.CanRedo);
        Assert.True(session.Redo());Assert.Equal(new Vector3(6,3,2),session.Current.Find("crate")!.Position);Assert.True(session.Dirty);
        Assert.True(session.Undo());Assert.False(session.Dirty);Assert.True(session.Undo());Assert.True(session.Dirty);Assert.True(session.Redo());Assert.False(session.Dirty);
    }
}
