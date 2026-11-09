using System.Text.Json;
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A game's last word before a file is written: every route to saving meets it, and nothing is written or lost.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class SaveCheckTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("save-check-").FullName;
    private string Data => Path.Combine(root, "data");
    private string File1 => Path.Combine(Data, "wares.json");
    private const string Text = """
        {"wares":[{"id":"ore"}]}
        """;

    public SaveCheckTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        File.WriteAllText(Path.Combine(root, "schemas", "wares.schema.json"), "{}");
        File.WriteAllText(File1, Text);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataProject Project(Func<DataFile, string?>? check)
    {
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        project.SaveCheck = check;
        return project;
    }

    private static void Edit(DataFile file) =>
        Assert.True(file.Session!.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement("tin"))));

    [Fact]
    public void A_refused_save_writes_nothing_says_why_and_keeps_the_edit()
    {
        var project = Project(_ => "the game says no");
        var file = project.Find("wares.json")!;
        Assert.True(file.Open());
        Edit(file);

        Assert.False(file.Save());
        Assert.Contains("the game says no", file.Problems[0].Message);
        Assert.Equal(Text, File.ReadAllText(File1));
        Assert.True(file.Dirty);
        Assert.Equal("tin", file.Session!.Current.At("/wares/0/id")!.Value.GetString());
    }

    [Fact]
    public void It_isnt_asked_about_a_file_with_nothing_to_save_and_a_yes_saves()
    {
        var asked = 0;
        var project = Project(_ => { asked++; return null; });
        var file = project.Find("wares.json")!;
        Assert.True(file.Open());
        Assert.True(file.Save());
        Assert.Equal(0, asked);

        Edit(file);
        Assert.True(file.Save());
        Assert.Equal(1, asked);
        Assert.Contains("tin", File.ReadAllText(File1));
    }

    [Fact]
    public void A_game_can_say_which_schema_a_file_uses()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "shared.schema.json"), """{"type":"object","required":["wares"],"additionalProperties":false,"properties":{"wares":{"type":"array"}}}""");
        File.WriteAllText(Path.Combine(Data, "more.json"), Text);
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        project.SchemaFor = name => name == "more.json" ? "shared.schema.json" : null;
        var file = project.Find("more.json")!;
        Assert.True(file.Open(), string.Join(" ", file.Problems.Select(p => p.Message)));
        Assert.Equal("shared.schema.json", file.Schema);
        Assert.False(file.Session!.Execute(new SetValue("/extra", JsonSerializer.SerializeToElement(1))));   // that schema's rule holds
        var other = project.Find("wares.json")!;
        Assert.True(other.Open());
        Assert.Equal("wares.schema.json", other.Schema);   // the usual way, for the rest
    }

    [Fact]
    public void A_checks_copy_of_the_project_chooses_schemas_the_same_way()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "shared.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Data, "more.json"), Text);
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        project.SchemaFor = name => name == "more.json" ? "shared.schema.json" : null;
        var folder = Path.Combine(root, "copy");
        var copy = CheckSnapshot.Capture(project).OpenCopy(folder, out _);
        var file = copy.Find("more.json")!;
        Assert.True(file.Open(), string.Join(" ", file.Problems.Select(p => p.Message)));
        Assert.Equal("shared.schema.json", file.Schema);
    }

    [Fact]
    public void A_file_check_can_stop_making_renaming_deleting_and_restoring_and_a_preview_meets_it()
    {
        File.WriteAllText(Path.Combine(Data, "more.json"), Text);
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        project.SchemaFor = _ => "wares.schema.json";   // or the rename stops first, over its schema
        var asked = new List<FileChange>();
        string? answer = "not in this game";
        project.FileCheck = change => { asked.Add(change); return answer; };
        var more = Path.Combine(Data, "more.json");
        var before = File.ReadAllText(more);

        Assert.False(project.Delete("more.json", out var why));
        Assert.Equal("not in this game", why);
        Assert.False(project.Delete("more.json", out _, preview: true));
        Assert.False(project.Rename("more.json", "other.json", out _));
        Assert.False(project.Create("wares2.json", "wares.schema.json", out _));
        Assert.Equal(before, File.ReadAllText(more));
        Assert.Equal([FileChangeKind.Delete, FileChangeKind.Delete, FileChangeKind.Rename, FileChangeKind.Create], asked.Select(c => c.Kind));
        Assert.Equal("other.json", asked[2].NewName);
        Assert.False(string.IsNullOrEmpty(asked[3].Text));
        Assert.False(File.Exists(Path.Combine(Data, "wares2.json")));

        answer = null;
        Assert.True(project.Delete("more.json", out why), why);
        answer = "stays gone";
        Assert.False(project.Restore("more.json", out why));
        Assert.Equal("stays gone", why);
        Assert.False(File.Exists(more));
        Assert.Equal(before, asked[^1].Text);
        answer = null;
        Assert.True(project.Restore("more.json", out why), why);   // it was kept
        Assert.Equal(before, File.ReadAllText(more));
    }

    [Fact]
    public void A_file_check_that_throws_is_a_refusal_and_changes_nothing()
    {
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        project.FileCheck = _ => throw new InvalidOperationException("the rules are broken");
        Assert.False(project.Delete("wares.json", out var why));
        Assert.Contains("the rules are broken", why);
        Assert.True(File.Exists(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void A_check_sees_one_file_being_saved_alone_and_all_of_them_in_save_all()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "more.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Data, "more.json"), "{\"wares\":[]}");
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        var seen = new List<string>();
        project.SaveCheck = file =>
        {
            seen.Add(string.Join("+", project.BeingSaved(file).Select(f => f.Name).Order(StringComparer.Ordinal)));
            return null;
        };
        var (a, b) = (project.Find("wares.json")!, project.Find("more.json")!);
        Assert.True(a.Open());
        Assert.True(b.Open());
        foreach (var f in new[] { a, b }) Assert.True(f.Session!.Execute(new InsertItem("/wares", 0, JsonSerializer.SerializeToElement(new { id = "new" }))), string.Join(" ", f.Session.LastIssues.Select(i => i.Message)));

        Assert.True(a.Save());
        Assert.Equal(["wares.json"], seen);   // alone: b's unsaved edit won't be on disk, so it doesn't count
        seen.Clear();
        Assert.True(a.Session!.Execute(new RemoveItem("/wares/0")));
        Assert.Empty(project.SaveAll());
        Assert.Equal(["more.json+wares.json", "more.json+wares.json"], seen);   // together: each sees the other
        Assert.Equal([a], project.BeingSaved(a));   // and it's over
    }

    [Fact]
    public void A_file_refused_in_save_all_drops_out_and_the_rest_are_judged_again_without_it()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "more.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Data, "more.json"), "{\"wares\":[]}");
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        var (a, b) = (project.Find("wares.json")!, project.Find("more.json")!);
        // b's draft is fine only if a's goes to disk with it.
        project.SaveCheck = file => file == b && !project.BeingSaved(file).Contains(a) ? "it needs the other one" : null;
        Assert.True(a.Open());
        Assert.True(b.Open());
        foreach (var f in new[] { a, b }) Assert.True(f.Session!.Execute(new InsertItem("/wares", 0, JsonSerializer.SerializeToElement(new { id = "new" }))));
        var (aDisk, bDisk) = (File.ReadAllText(a.Path), File.ReadAllText(b.Path));

        // The one it depends on is changed outside, so it's refused: and so, then, is the one depending on it.
        File.WriteAllText(a.Path, aDisk + "\n");
        File.SetLastWriteTimeUtc(a.Path, DateTime.UtcNow.AddMinutes(1));
        var failed = project.SaveAll();
        Assert.Contains(a, failed);
        Assert.Contains(b, failed);
        Assert.Equal(bDisk, File.ReadAllText(b.Path));
        Assert.True(b.Dirty);
        Assert.Contains(b.Problems, p => p.Message.Contains("needs the other", StringComparison.Ordinal));
        Assert.Contains(a.Problems, p => p.Message.Contains("outside", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_another_program_has_open_drops_out_before_anything_that_needs_it_is_written()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "more.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Data, "more.json"), "{\"wares\":[]}");
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"));
        var (a, b) = (project.Find("wares.json")!, project.Find("more.json")!);
        project.SaveCheck = file => file == b && !project.BeingSaved(file).Contains(a) ? "it needs the other one" : null;
        Assert.True(a.Open());
        Assert.True(b.Open());
        foreach (var f in new[] { a, b }) Assert.True(f.Session!.Execute(new InsertItem("/wares", 0, JsonSerializer.SerializeToElement(new { id = "new" }))));
        var bDisk = File.ReadAllText(b.Path);

        IReadOnlyList<DataFile> failed;
        using (new FileStream(a.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            failed = project.SaveAll();
        Assert.Contains(a, failed);
        Assert.Contains(b, failed);
        Assert.Equal(bDisk, File.ReadAllText(b.Path));
        Assert.True(a.Dirty && b.Dirty);

        // Let go of, and it all goes.
        Assert.Empty(project.SaveAll());
        Assert.False(a.Dirty || b.Dirty);
    }

    [Fact]
    public void A_file_that_wont_write_at_the_last_moment_puts_back_what_went_before_it()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "a.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(root, "schemas", "b.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(root, "schemas", "z.schema.json"), """{"type":"object"}""");
        foreach (var name in new[] { "a", "b", "z" }) File.WriteAllText(Path.Combine(Data, $"{name}.json"), "{\"wares\":[]}");
        File.Delete(File1);
        var armed = false;
        var calls = 0;
        FileStream? late = null;
        var project = DataProject.Open(Data, Path.Combine(root, "schemas"), readOnlyFile: name =>
        {
            // z's policy, the last thing asked before the writing: an ordinary lock on b, which has already passed.
            if (name == "z.json" && armed && ++calls == 2) late = new FileStream(Path.Combine(Data, "b.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            return null;
        });
        var (a, b, z) = (project.Find("a.json")!, project.Find("b.json")!, project.Find("z.json")!);
        project.SaveCheck = file =>
        {
            if (file == z) armed = true;
            return file == a && !project.BeingSaved(file).Contains(b) ? "it needs b" : null;
        };
        foreach (var f in new[] { a, b, z })
        {
            Assert.True(f.Open());
            Assert.True(f.Session!.Execute(new InsertItem("/wares", 0, JsonSerializer.SerializeToElement(new { id = "new" }))));
        }
        var (aDisk, bDisk) = (File.ReadAllText(a.Path), File.ReadAllText(b.Path));
        try
        {
            var failed = project.SaveAll();
            Assert.NotNull(late);
            Assert.Contains(a, failed);
            Assert.Contains(b, failed);
            Assert.DoesNotContain(z, failed);
            Assert.Equal(aDisk, File.ReadAllText(a.Path));   // written first, and put back
            Assert.Equal(bDisk, File.ReadAllText(b.Path));
            Assert.True(a.Dirty && b.Dirty);
            Assert.False(z.Dirty);
            Assert.Contains("new", File.ReadAllText(z.Path), StringComparison.Ordinal);
            Assert.Equal(DiskState.Unchanged, a.Session!.CheckDisk());
            Assert.Contains(b.Problems, p => p.Message.Contains("b.json", StringComparison.Ordinal) && p.Message.Contains("close that", StringComparison.Ordinal));
            Assert.Contains(a.Problems, p => p.Message.Contains("needs b", StringComparison.Ordinal));
        }
        finally { late?.Dispose(); }

        // Let go of, and they both go.
        Assert.Empty(project.SaveAll());
        Assert.False(a.Dirty || b.Dirty);
    }

    [Fact]
    public void Agents_meet_it()
    {
        var project = Project(_ => "the game says no");
        var agent = new AgentSession(project);
        Assert.True(agent.Set("wares.json", "/wares/0/id", "\"tin\"")["applied"]!.GetValue<bool>());
        var saved = agent.Save("wares.json");
        Assert.Contains("the game says no", saved["refused"]![0]!["why"]!.GetValue<string>());
        Assert.Equal(Text, File.ReadAllText(File1));
    }

    [Fact]
    public void The_editors_save_and_save_all_meet_it()
    {
        var project = Project(_ => "the game says no");
        var workspace = new DataWorkspace(project);
        Assert.True(workspace.Show("wares.json"));
        Edit(project.Find("wares.json")!);

        Assert.False(workspace.Window!.Run("editor.save"));
        Assert.Contains("the game says no", workspace.Window!.Status);
        Assert.False(workspace.SaveAll());
        Assert.Equal(Text, File.ReadAllText(File1));

        project.SaveCheck = null;
        Assert.True(workspace.Window!.Run("editor.save"), workspace.Window!.Status);
        Assert.Contains("tin", File.ReadAllText(File1));
    }

    // A workspace whose save check waits on a gate, saving in the background.
    private (DataWorkspace Workspace, DataFile File, ManualResetEventSlim Gate) Background(string? answer = null)
    {
        var gate = new ManualResetEventSlim(false);
        var project = Project(_ => { Assert.True(gate.Wait(TimeSpan.FromSeconds(30))); return answer; });
        var workspace = new DataWorkspace(project) { BackgroundSaves = true };
        Assert.True(workspace.Show("wares.json"));
        var file = project.Find("wares.json")!;
        Edit(file);
        return (workspace, file, gate);
    }

    private static void Finish(DataWorkspace workspace)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (workspace.SavePending("wares.json") && DateTime.UtcNow < until) { workspace.FinishSaves(); Thread.Sleep(10); }
        Assert.False(workspace.SavePending("wares.json"));
    }

    [Fact]
    public void A_background_save_waits_for_its_check_without_stopping_the_editor_then_saves_itself()
    {
        var (workspace, file, gate) = Background();
        Assert.False(workspace.Window!.Run("editor.save"));                    // it answers at once: the check runs on a worker
        Assert.Contains("Checking wares.json", workspace.Said);
        Assert.True(workspace.SavePending("wares.json"));
        workspace.FinishSaves();
        Assert.Equal(Text, File.ReadAllText(File1));                          // nothing's written until the check passes
        Assert.Contains("still being checked", workspace.Window!.Run("editor.save") ? "" : workspace.Window!.Status);
        Assert.Contains(file, workspace.Project.SaveAll());                 // a save of all doesn't slip in under a check
        Assert.Equal(Text, File.ReadAllText(File1));
        gate.Set();
        Finish(workspace);
        Assert.Contains("tin", File.ReadAllText(File1));
        Assert.Equal("Saved wares.json.", workspace.Said);
        Assert.False(file.Dirty);
    }

    [Fact]
    public void A_save_of_all_asked_the_moment_a_background_check_starts_never_runs_a_check_of_its_own()
    {
        // The check counts as running from the moment it's asked, before its worker starts (found by A185).
        var gate = new ManualResetEventSlim(false);
        var asked = 0;
        var project = Project(_ => { Interlocked.Increment(ref asked); Assert.True(gate.Wait(TimeSpan.FromSeconds(30))); return null; });
        var workspace = new DataWorkspace(project) { BackgroundSaves = true };
        Assert.True(workspace.Show("wares.json"));
        Edit(project.Find("wares.json")!);
        Assert.False(workspace.Window!.Run("editor.save"));
        Assert.Contains(project.Find("wares.json")!, project.SaveAll());       // refused at once, without asking the check
        Assert.True(Volatile.Read(ref asked) <= 1);
        gate.Set();
        Finish(workspace);
        Assert.Equal(1, Volatile.Read(ref asked));
        Assert.Contains("tin", File.ReadAllText(File1));
    }

    private static void SetId(DataFile file, string id) =>
        Assert.True(file.Session!.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement(id))));

    [Fact]
    public void A_file_closed_and_opened_again_gets_a_window_on_its_new_session_so_a_save_writes_the_new_draft()
    {
        // found by A186: the old window, kept by name, saved the closed draft and said "Saved"
        var project = Project(null);
        var workspace = new DataWorkspace(project);
        Assert.True(workspace.Show("wares.json"));
        var file = project.Find("wares.json")!;
        SetId(file, "closed draft");
        file.Close();
        Assert.True(workspace.Show("wares.json"));
        SetId(file, "new draft");
        Assert.True(workspace.Window!.Run("editor.save"), workspace.Window!.Status);
        Assert.Contains("new draft", File.ReadAllText(File1));
        Assert.DoesNotContain("closed draft", File.ReadAllText(File1));
        Assert.False(file.Dirty);
    }

    [Fact]
    public void A_window_whose_file_was_closed_and_not_opened_again_saves_nothing()
    {
        // found by A186: the old window still answered "Saved." and wrote the closed draft, with no check run
        var asked = 0;
        var project = Project(_ => { asked++; return null; });
        var workspace = new DataWorkspace(project);
        Assert.True(workspace.Show("wares.json"));
        var file = project.Find("wares.json")!;
        SetId(file, "closed draft");
        var window = workspace.Window!;
        file.Close();
        Assert.False(window.Run("editor.save"));
        Assert.Contains("was closed", window.Status);
        Assert.Equal(Text, File.ReadAllText(File1));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_pending_save_of_a_closed_draft_never_lands_in_the_session_opened_after_it()
    {
        // found by A186: the reopened session's revision matched the checked one by number
        var (workspace, file, gate) = Background();
        Assert.False(workspace.Window!.Run("editor.save"));
        file.Close();
        Assert.True(workspace.Show("wares.json"));
        SetId(file, "tin");                                                   // same revision number as the checked draft
        gate.Set();
        Finish(workspace);
        Assert.Contains("changed while it was being checked", workspace.Said);
        Assert.Equal(Text, File.ReadAllText(File1));
        Assert.True(file.Dirty);
    }

    [Fact]
    public void An_edit_made_while_its_check_runs_is_not_saved_unchecked_and_is_kept()
    {
        var (workspace, file, gate) = Background();
        Assert.False(workspace.Window!.Run("editor.save"));
        Assert.True(file.Session!.Execute(new SetValue("/wares/0/id", JsonSerializer.SerializeToElement("lead"))));
        gate.Set();
        Finish(workspace);
        Assert.Contains("changed while it was being checked", workspace.Said);
        Assert.Equal(Text, File.ReadAllText(File1));
        Assert.Contains("lead", JsonSerializer.Serialize(file.Session.Current.Root));
    }

    [Fact]
    public void A_background_check_that_refuses_says_why_and_writes_nothing()
    {
        var (workspace, file, gate) = Background("the game says no");
        Assert.False(workspace.Window!.Run("editor.save"));
        gate.Set();
        Finish(workspace);
        Assert.Contains("the game says no", workspace.Said);
        Assert.Equal(Text, File.ReadAllText(File1));
        Assert.True(file.Dirty);
    }
}
