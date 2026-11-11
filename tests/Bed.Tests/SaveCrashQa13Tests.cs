using System.Numerics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
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
        private readonly Func<IAtomicFileSystem,IAtomicFileSystem>? previous=AtomicFile.FileSystemObserver.Value;
        private readonly Func<IAtomicFileSystem,IAtomicFileSystem> observer;
        private readonly List<string> calls=[];
        private readonly string directory;private readonly byte[] expected;
        private string? temporary;private bool disposed;
        public TempWrites(string directory,string text)
        {
            this.directory=Path.GetFullPath(directory);expected=new System.Text.UTF8Encoding(false).GetBytes(text);
            observer=disk=>new ObservedDisk(this,disk);AtomicFile.FileSystemObserver.Value=observer;
        }
        private sealed class ObservedDisk(TempWrites owner,IAtomicFileSystem disk):IAtomicFileSystem
        {
            private bool tracked;
            public Stream CreateNew(string path)
            {
                var stream=disk.CreateNew(path);tracked=Path.GetDirectoryName(path)==owner.directory&&Path.GetFileName(path).StartsWith(".level.json.",StringComparison.Ordinal)&&path.EndsWith(".tmp",StringComparison.Ordinal);
                if(tracked){Assert.Null(owner.temporary);owner.temporary=path;owner.calls.Add("created");}return stream;
            }
            public void FlushToDisk(Stream stream){disk.FlushToDisk(stream);if(tracked){Assert.Equal(owner.expected.Length,stream.Length);owner.calls.Add("flushed");}}
            public void MoveOverwrite(string source,string target)
            {
                if(tracked){Assert.Equal(owner.expected,File.ReadAllBytes(source));owner.calls.Add("closed-complete-before-replace");}
                disk.MoveOverwrite(source,target);
            }
            public bool Exists(string path)=>disk.Exists(path);
            public void Delete(string path){disk.Delete(path);if(tracked){Assert.False(File.Exists(path));owner.calls.Add("deleted");}}
        }
        public void AssertWroteThenCleaned()
        {
            Assert.Equal(new[]{"created","flushed","closed-complete-before-replace","deleted"},calls);Assert.NotNull(temporary);Assert.False(File.Exists(temporary));Dispose();
        }
        public void Dispose(){if(disposed)return;Assert.Same(observer,AtomicFile.FileSystemObserver.Value);AtomicFile.FileSystemObserver.Value=previous;disposed=true;}
    }
    [Fact]
    public void Actual_atomic_replace_failure_occurs_after_temp_write_and_preserves_the_saved_scene_and_exact_draft()
    {
        using var f=new Fixture();var session=DocumentSession<Scene>.Open(f.FilePath,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
        Assert.True(session.Execute(new MoveObject("crate",new(5,2,1))));var draft=session.Current;var journal=session.Journal.ToArray();var revision=session.Revision;var baseline=session.ExpectedOnDisk;
        using var observed=new TempWrites(f.Data,session.TextToSave);
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
    public async Task Save_observations_are_isolated_from_parallel_execution_flows()
    {
        using var parent=new Fixture();using var observed=new TempWrites(parent.Data,"parent draft");using var ready=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task other;
        using(ExecutionContext.SuppressFlow())other=Task.Run(()=>
        {
            using var child=new Fixture();using var childObserved=new TempWrites(child.Data,"child draft");ready.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)),"The parent save did not finish.");using(var held=new FileStream(child.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))Assert.Throws<UnauthorizedAccessException>(()=>AtomicFile.Write(child.FilePath,"child draft"));childObserved.AssertWroteThenCleaned();Assert.Equal(child.Original,File.ReadAllBytes(child.FilePath));
        });
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)),"The parallel observer did not start.");using(var held=new FileStream(parent.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))Assert.Throws<UnauthorizedAccessException>(()=>AtomicFile.Write(parent.FilePath,"parent draft"));observed.AssertWroteThenCleaned();Assert.Equal(parent.Original,File.ReadAllBytes(parent.FilePath));
        }
        finally{release.Set();}
        await other.WaitAsync(TimeSpan.FromSeconds(10));
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
        using var observed=new TempWrites(f.Data,session.TextToSave);
        using(var held=new FileStream(f.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            host.Press(workspace.Draw,ImGuiKey.S,ctrl:true);host.Frames(workspace.Draw,2);
            Assert.Contains("level.json",window.Status);Assert.Contains("can't be written",window.Status);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.IdKind==UiIdKind.Semantic&&n.Label.Contains("level.json",StringComparison.Ordinal)&&n.Label.Contains("can't be written",StringComparison.Ordinal));
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
