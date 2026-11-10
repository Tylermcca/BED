using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class NativeSaveTickTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("native-save-tick-").FullName;
    private readonly HeadlessHost host = new(1000, 700) { Drawing = false };
    private readonly DataProject project;
    private string? readOnlyReason;
    private readonly DataWorkspace workspace;
    public NativeSaveTickTests()
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        File.WriteAllText(Path.Combine(schemas, "values.schema.json"), "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}},\"required\":[\"value\"]}");
        foreach (var name in new[] { "a.json", "b.json" }) File.WriteAllText(Path.Combine(data, name), "{\"value\":1}\n");
        project = DataProject.Open(data, schemas, readOnlyFile: _ => readOnlyReason); project.SchemaFor = _ => "values.schema.json";
        workspace = new(project); EditorWindow<DataDocument>.Configure(ImGui.GetIO()); Assert.True(workspace.Show("a.json"));
    }
    public void Dispose() { host.Dispose(); Directory.Delete(root, recursive: true); }
    private DataFile Edit(string name = "a.json")
    {
        var file = project.Find(name)!; Assert.True(file.Open());
        Assert.True(file.Session!.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(2)))); return file;
    }
    [Fact]
    public void Native_dirty_save_notifies_after_commit_once_and_noop_is_silent()
    {
        var file = Edit(); var calls = 0; var owner = Environment.CurrentManagedThreadId; int observed = -1; bool? dirty = null; string? disk = null;
        project.FilesSaved += written => { calls++; observed = Environment.CurrentManagedThreadId; dirty = file.Dirty; disk = File.ReadAllText(written[0].Path); };
        Assert.True(workspace.Window!.Run("editor.save"), workspace.Window.Status); Assert.Equal(1, calls); Assert.Equal(owner, observed);
        Assert.False(dirty); Assert.Equal(file.Session!.ExpectedOnDisk, disk);
        Assert.True(workspace.Window.Run("editor.save")); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("host")]
    [InlineData("readonly")]
    [InlineData("outside")]
    [InlineData("deleted")]
    public void Native_refused_save_keeps_draft_and_never_notifies(string reason)
    {
        var file = Edit(); var calls = 0; project.FilesSaved += _ => calls++;
        if (reason == "host") project.SaveCheck = _ => "synthetic host refusal";
        if (reason == "readonly") readOnlyReason = "synthetic read-only policy";
        if (reason == "outside") File.WriteAllText(file.Path, "{\"value\":3}");
        if (reason == "deleted") File.Delete(file.Path);
        Assert.False(workspace.Window!.Run("editor.save")); Assert.True(file.Dirty); Assert.Equal(0, calls);
    }
    [Fact]
    public void Workspace_save_all_notifies_once_with_actual_files_and_noop_is_silent()
    {
        Edit(); Edit("b.json"); var calls = 0; string[] names = []; bool? clean = null;
        project.FilesSaved += files => { calls++; names = files.Select(f => f.Name).ToArray(); clean = project.Dirty.Count == 0; };
        Assert.True(workspace.SaveAll()); Assert.Equal(1, calls); Assert.True(clean); Assert.Equal(new[] { "a.json", "b.json" }, names);
        Assert.True(workspace.SaveAll()); Assert.Equal(1, calls);
    }
    [Fact]
    public void Background_completion_notifies_once_then_ticks_on_owner_and_contains_observer_errors()
    {
        var file = Edit(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        project.SaveCheck = _ => { entered.Set(); return release.Wait(TimeSpan.FromSeconds(10)) ? null : "test release timed out"; };
        workspace.BackgroundSaves = true;
        var saved = 0; var tickedAfterSave = false; var owner = Environment.CurrentManagedThreadId; var savedThread = -1; var tickThread = -1;
        project.FilesSaved += _ => { saved++; savedThread = Environment.CurrentManagedThreadId; };
        workspace.Ticked += () => throw new InvalidOperationException("synthetic observer error");
        workspace.Ticked += () => { tickThread = Environment.CurrentManagedThreadId; if (saved == 1 && !file.Dirty) tickedAfterSave = true; };
        Assert.False(workspace.Window!.Run("editor.save")); Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); Assert.Equal(0, saved);
        release.Set(); var timer = Stopwatch.StartNew();
        while (workspace.SavePending("a.json") && timer.Elapsed < TimeSpan.FromSeconds(10)) { workspace.FinishSaves(); Thread.Sleep(2); }
        Assert.False(workspace.SavePending("a.json")); Assert.Equal(1, saved); Assert.Equal(owner, savedThread); Assert.Equal(owner, tickThread); Assert.True(tickedAfterSave);
        var more = 0; workspace.Ticked += () => more++; workspace.FinishSaves(); workspace.FinishSaves(); Assert.Equal(2, more); Assert.Equal(1, saved);
    }
}
