using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// INVENTED: controlled schedules and disk/history oracles over self-authored two-file data.
// No sleeps decide a worker's readiness; each callback has entry/release/exit handshakes and a finite backstop.
[Collection(ImGuiCollection.Name)]
public sealed class BackgroundSaveAttackTests(ITestOutputHelper output)
{
    private sealed class Held : IDisposable
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false), Exited = new(false);
        public int Calls, Thread;
        public string? Captured;
        public Func<string?> Answer = () => null;
        public string? Check(DataFile file)
        {
            Interlocked.Increment(ref Calls);
            Thread = Environment.CurrentManagedThreadId;
            Captured = file.Session!.TextToSave;
            Entered.Set();
            try
            {
                if (!Release.Wait(TimeSpan.FromSeconds(45))) return "test backstop: check was never released";
                return Answer();
            }
            finally { Exited.Set(); }
        }
        public void Dispose() { Release.Set(); if (Entered.IsSet) Assert.True(Exited.Wait(TimeSpan.FromSeconds(10))); }
    }

    private sealed class Rig : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("background-attack-").FullName;
        public readonly Held A = new(), B = new();
        public DataProject Project { get; }
        public DataWorkspace Workspace { get; }
        public string Data => Path.Combine(Root, "data");
        public string Schema => Path.Combine(Root, "schemas");
        public string? LockedReason;
        public const string Original = """{"$schema":"items.schema.json","items":[{"id":"seed","value":0}]}""";
        public Rig(bool readOnly = false)
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Schema);
            System.IO.File.WriteAllText(Path.Combine(Schema, "items.schema.json"), """
                {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"value":{"type":"integer"}}}}}}
                """);
            foreach (var name in new[] { "a.json", "b.json" }) System.IO.File.WriteAllText(Path.Combine(Data, name), Original);
            Project = DataProject.Open(Data, Schema, readOnly, _ => LockedReason);
            Project.SaveCheck = file => (file.Name == "a.json" ? A : B).Check(file);
            Workspace = new DataWorkspace(Project) { BackgroundSaves = true };
            Assert.True(Workspace.Show("a.json"));
        }
        public DataFile File(string name = "a.json") => Project.Find(name)!;
        public string Disk(string name = "a.json") => System.IO.File.ReadAllText(Path.Combine(Data, name));
        public void Edit(int value, string name = "a.json")
        {
            Assert.True(Workspace.Show(name), Workspace.Said);
            Assert.True(File(name).Session!.Execute(new SetValue("/items/0/value", JsonSerializer.SerializeToElement(value))));
        }
        public void Start(string name = "a.json", bool wait = true)
        {
            Assert.True(Workspace.Show(name));
            Assert.False(Workspace.Window!.Run("editor.save"));
            Assert.True(Workspace.SavePending(name), Workspace.Said);
            if (wait) { var held = name == "a.json" ? A : B; Assert.True(held.Entered.Wait(TimeSpan.FromSeconds(10))); Assert.NotEqual(Environment.CurrentManagedThreadId, held.Thread); }
        }
        public void Finish(string name = "a.json")
        {
            var until = Stopwatch.StartNew();
            while (Workspace.SavePending(name) && until.Elapsed < TimeSpan.FromSeconds(12))
            {
                Workspace.FinishSaves();
                Thread.Sleep(2);
            }
            Assert.False(Workspace.SavePending(name), Workspace.Said);
        }
        public int Value(string name = "a.json") => File(name).Session!.Current.At("/items/0/value")!.Value.GetInt32();
        public void Dispose()
        {
            A.Dispose(); B.Dispose();
            foreach (var path in Directory.EnumerateFiles(Data)) System.IO.File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("undo")]
    [InlineData("undo-redo")]
    [InlineData("edit-undo")]
    public void History_changes_never_reuse_an_old_verdict_even_when_the_bytes_return(string action)
    {
        using var r = new Rig();
        r.Edit(1); r.Start();
        var checkedRevision = r.File().Session!.Revision;
        var checkedText = r.File().Session!.TextToSave;
        switch (action)
        {
            case "edit": r.Edit(2); break;
            case "undo": Assert.True(r.Workspace.Window!.Run("editor.undo")); break;
            case "undo-redo":
                Assert.True(r.Workspace.Window!.Run("editor.undo"));
                Assert.True(r.Workspace.Window!.Run("editor.redo")); break;
            case "edit-undo":
                r.Edit(2); Assert.True(r.Workspace.Window!.Run("editor.undo")); break;
        }
        Assert.True(r.File().Session!.Revision > checkedRevision);
        if (action is "undo-redo" or "edit-undo") Assert.Equal(checkedText, r.File().Session!.TextToSave);
        var retained = r.File().Session!.TextToSave;
        var journal = r.File().Session!.Journal.Count;
        r.A.Release.Set(); r.Finish();
        Assert.Equal(Rig.Original, r.Disk());
        Assert.Equal(retained, r.File().Session!.TextToSave);
        Assert.Equal(journal, r.File().Session!.Journal.Count);
        Assert.Contains("changed while", r.Workspace.Said);
        Assert.Equal(action != "undo", r.File().Dirty);
        output.WriteLine($"{action}: revision {checkedRevision}->{r.File().Session!.Revision}; old disk, exact draft/history retained; {r.Workspace.Said}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_outside_replacement_or_deletion_is_never_overwritten_or_resurrected(bool delete)
    {
        using var r = new Rig(); r.Edit(1); r.Start();
        var path = Path.Combine(r.Data, "a.json");
        var outside = Rig.Original.Replace(":0", ":99", StringComparison.Ordinal);
        if (delete) System.IO.File.Delete(path); else System.IO.File.WriteAllText(path, outside);
        r.A.Release.Set(); r.Finish();
        if (delete) Assert.False(System.IO.File.Exists(path)); else Assert.Equal(outside, r.Disk());
        Assert.True(r.File().Dirty); Assert.Equal(1, r.Value());
        Assert.Contains(delete ? "deleted" : "changed outside", r.Workspace.Said);
        output.WriteLine($"outside delete={delete}: disk preserved, draft=1 dirty; {r.Workspace.Said}");
    }

    [Theory]
    [InlineData("save-all")]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("reload")]
    public void Pending_file_operations_do_not_commit_an_unchecked_draft(string action)
    {
        using var r = new Rig(); r.Edit(1); r.Start();
        switch (action)
        {
            case "save-all": Assert.False(r.Workspace.SaveAll()); break;
            case "rename": Assert.False(r.Workspace.RenameFile("a.json", "renamed.json")); break;
            case "delete": Assert.False(r.Workspace.DeleteFile("a.json")); break;
            case "reload": Assert.True(r.File().Session!.Revert()); Assert.False(r.File().Dirty); break;
        }
        Assert.Equal(Rig.Original, r.Disk());
        Assert.False(System.IO.File.Exists(Path.Combine(r.Data, "renamed.json")));
        Assert.Equal(1, Volatile.Read(ref r.A.Calls));
        r.A.Release.Set(); r.Finish();
        if (action == "reload")
        {
            Assert.Equal(Rig.Original, r.Disk()); Assert.Equal(0, r.Value());
            Assert.Contains("changed while", r.Workspace.Said);
        }
        else { Assert.Equal(1, JsonDocument.Parse(r.Disk()).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32()); Assert.False(r.File().Dirty); }
        output.WriteLine($"{action}: refused while dirty (reload explicitly discarded), one callback, completion={r.Workspace.Said}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_refusal_or_throw_keeps_the_draft_and_allows_a_fresh_retry(bool throws)
    {
        using var r = new Rig(); r.Edit(1);
        r.A.Answer = throws ? () => throw new InvalidOperationException("A186 controlled callback fault") : () => "A186 controlled game refusal";
        r.Start(); r.A.Release.Set(); r.Finish();
        Assert.Contains(throws ? "controlled callback fault" : "controlled game refusal", r.Workspace.Said);
        Assert.Equal(Rig.Original, r.Disk()); Assert.True(r.File().Dirty); Assert.Equal(1, r.Value());
        var refusal = r.Workspace.Said;
        r.A.Answer = () => null;
        r.Start(wait: false); r.Finish();
        Assert.Equal(2, Volatile.Read(ref r.A.Calls)); Assert.False(r.File().Dirty);
        Assert.Equal(1, JsonDocument.Parse(r.Disk()).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        output.WriteLine($"throws={throws}: {refusal}; fresh retry checked once more and saved");
    }

    [Fact]
    public void A_held_check_does_not_block_frames_other_edits_or_make_duplicate_workers()
    {
        using var r = new Rig(); r.Edit(1); r.Start();
        using var host = new HeadlessHost(1000, 700) { Drawing = false };
        var watch = Stopwatch.StartNew();
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(r.Workspace.Draw, 4);
        for (var i = 0; i < 20; i++) Assert.False(r.Workspace.Window!.Run("editor.save"));
        r.Edit(7, "b.json"); // another document remains editable while A has no answer
        Assert.Equal(7, r.Value("b.json"));
        Assert.True(r.Workspace.SavePending("a.json"));
        Assert.Equal(1, Volatile.Read(ref r.A.Calls));
        Assert.Equal(Rig.Original, r.Disk());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "frames/actions waited for the held callback");
        r.A.Release.Set(); r.Finish();
        Assert.False(r.File().Dirty); Assert.True(r.File("b.json").Dirty);
        Assert.Equal(Rig.Original, r.Disk("b.json"));
        output.WriteLine($"held without answer: four offscreen frames + 20 duplicates + independent edit in {watch.ElapsedMilliseconds}ms; one A worker, no B write");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_files_finish_in_reverse_order_without_sharing_verdicts(bool firstRefuses)
    {
        using var r = new Rig(); r.Edit(1); r.Start(); r.Edit(2, "b.json"); r.Start("b.json");
        r.A.Answer = () => firstRefuses ? "A alone refused" : null;
        Assert.Equal(1, Volatile.Read(ref r.A.Calls)); Assert.Equal(1, Volatile.Read(ref r.B.Calls));
        r.B.Release.Set(); r.Finish("b.json");
        Assert.True(r.Workspace.SavePending("a.json"));
        Assert.Equal(Rig.Original, r.Disk()); Assert.False(r.File("b.json").Dirty);
        Assert.Equal(2, JsonDocument.Parse(r.Disk("b.json")).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        r.A.Release.Set(); r.Finish();
        Assert.Equal(firstRefuses, r.File().Dirty);
        if (firstRefuses) { Assert.Equal(Rig.Original, r.Disk()); Assert.Contains("A alone refused", r.Workspace.Said); }
        else Assert.Equal(1, JsonDocument.Parse(r.Disk()).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        output.WriteLine($"two workers, B saved first, A refused={firstRefuses}, no verdict crossed files");
    }

    [Fact]
    public void Read_only_project_still_allows_draft_edits_but_starts_no_save_check()
    {
        using var r = new Rig(readOnly: true); r.Edit(1);
        Assert.False(r.Workspace.Window!.Run("editor.save"));
        Assert.False(r.Workspace.SavePending("a.json")); Assert.Equal(0, Volatile.Read(ref r.A.Calls));
        Assert.Equal(Rig.Original, r.Disk()); Assert.True(r.File().Dirty);
        Assert.Contains("read-only", r.Workspace.Window!.Status);
        output.WriteLine("read-only project: edit retained, no callback/no write");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_read_only_bar_added_during_the_check_refuses_the_finish(bool diskAttribute)
    {
        using var r = new Rig(); r.Edit(1); r.Start();
        if (diskAttribute) System.IO.File.SetAttributes(Path.Combine(r.Data, "a.json"), FileAttributes.ReadOnly);
        else r.LockedReason = "A186 producer now owns this file";
        r.A.Release.Set(); r.Finish();
        Assert.Equal(Rig.Original, r.Disk()); Assert.True(r.File().Dirty); Assert.Equal(1, r.Value());
        Assert.DoesNotContain("Saved a.json.", r.Workspace.Said);
        output.WriteLine($"readonly attribute={diskAttribute}: {r.Workspace.Said}; draft retained");
    }

    [Fact]
    public void A_removed_file_editor_does_not_return_when_its_late_answer_arrives()
    {
        using var r = new Rig(); r.Edit(1); r.Start();
        Assert.True(r.File().Session!.Revert()); // explicitly drop edits before Delete, as the documented lifecycle requires
        Assert.True(r.Workspace.DeleteFile("a.json"), r.Workspace.Said);
        Assert.Null(r.Workspace.Window);
        r.A.Release.Set(); r.Finish();
        Assert.False(System.IO.File.Exists(Path.Combine(r.Data, "a.json")));
        Assert.Null(r.Workspace.Window);
        Assert.Contains("changed while", r.Workspace.Said);
        output.WriteLine("revert + delete removed the editor; late worker could neither save nor resurrect it");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Launcher_close_obeys_the_discard_choice_and_a_late_answer_does_not_save_itself(bool discard)
    {
        using var r = new Rig();
        var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(r.Root, "recent.json")))
        { SetUpWorkspace = w => w.BackgroundSaves = true };
        Assert.True(launcher.Open(new ProjectPaths(r.Data, r.Schema)), launcher.Said);
        var old = launcher.Workspace!;
        old.Project.SaveCheck = r.A.Check;
        Assert.True(old.Show("a.json"));
        Assert.True(old.Active!.Session!.Execute(new SetValue("/items/0/value", JsonSerializer.SerializeToElement(1))));
        Assert.False(old.Window!.Run("editor.save"));
        Assert.True(r.A.Entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(discard, launcher.Close(discard));
        if (discard) Assert.Null(launcher.Workspace); else Assert.Same(old, launcher.Workspace);
        r.A.Release.Set(); Assert.True(r.A.Exited.Wait(TimeSpan.FromSeconds(10)));
        // A worker only gives an answer; the current host frame is what delivers it.
        if (!discard)
        {
            var until = Stopwatch.StartNew();
            while (old.SavePending("a.json") && until.Elapsed < TimeSpan.FromSeconds(12)) { old.FinishSaves(); Thread.Sleep(2); }
            Assert.False(old.SavePending("a.json")); Assert.False(old.Active!.Dirty);
        }
        else
        {
            Assert.Equal(Rig.Original, r.Disk());
            Assert.True(launcher.Open(new ProjectPaths(r.Data, r.Schema)), launcher.Said);
            Assert.NotSame(old, launcher.Workspace);
            Assert.Equal(0, launcher.Workspace!.Active!.Session!.Current.At("/items/0/value")!.Value.GetInt32());
        }
        output.WriteLine($"launcher discard={discard}: refused close keeps current delivery; explicit discard leaves late answer without a host delivery, disk {(discard ? "original" : "saved")}");
    }
    [Fact]
    public void A_reopened_session_cannot_reuse_the_closed_sessions_pending_approval()
    {
        using var r = new Rig(); r.Edit(1); r.Start();
        var closed = r.File().Session!;
        var checkedRevision = closed.Revision;
        r.File().Close(); // documented lifecycle: deliberately drop the old draft
        Assert.True(r.Workspace.Show("a.json"), r.Workspace.Said);
        var reopened = r.File().Session!;
        Assert.NotSame(closed, reopened);
        Assert.True(reopened.Execute(new SetValue("/items/0/value", JsonSerializer.SerializeToElement(2))));
        Assert.Equal(checkedRevision, reopened.Revision); // same number, different session: an ABA schedule
        r.A.Release.Set(); r.Finish();
        output.WriteLine($"closed session revision={closed.Revision}, reopened revision={reopened.Revision}, old editor reused={ReferenceEquals(closed, r.Workspace.Window!.Editor.Session)}, disk={r.Disk()}, reopened draft={reopened.TextToSave}, status={r.Workspace.Said}");
        Assert.Equal(1, JsonDocument.Parse(r.A.Captured!).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        Assert.True(reopened.Dirty);
        Assert.Equal(2, reopened.Current.At("/items/0/value")!.Value.GetInt32());
        Assert.Equal(0, JsonDocument.Parse(r.Disk()).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        Assert.Equal(Rig.Original, r.Disk()); // no still-open request checked this newly opened session
        Assert.True(reopened.Dirty);
        Assert.Equal(2, reopened.Current.At("/items/0/value")!.Value.GetInt32());
    }

    [Fact]
    public void A_fresh_save_after_reopen_must_write_the_newly_checked_session()
    {
        using var r = new Rig(); r.Edit(1);
        r.A.Answer = () => "the closed draft was refused";
        r.Start();
        var closed = r.File().Session!;
        r.File().Close();
        Assert.True(r.Workspace.Show("a.json"));
        var reopened = r.File().Session!;
        Assert.NotSame(closed, reopened);
        Assert.True(reopened.Execute(new SetValue("/items/0/value", JsonSerializer.SerializeToElement(2))));
        r.A.Release.Set(); r.Finish();
        Assert.Contains("the closed draft was refused", r.Workspace.Said);
        Assert.Equal(Rig.Original, r.Disk());
        r.A.Answer = () => null;
        r.Start(wait: false); r.Finish();
        Assert.Equal(2, Volatile.Read(ref r.A.Calls));
        Assert.Equal(2, JsonDocument.Parse(r.A.Captured!).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        output.WriteLine($"fresh request checked value=2, old editor reused={ReferenceEquals(closed, r.Workspace.Window!.Editor.Session)}, disk={r.Disk()}, new draft dirty={reopened.Dirty}, status={r.Workspace.Said}");
        Assert.Equal(2, JsonDocument.Parse(r.Disk()).RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
        Assert.False(reopened.Dirty);
    }

}
