using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

public sealed class SceneHostConnectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "typed-host-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private string ScenePath => Path.Combine(Data, "room.json");
    public SceneHostConnectionTests()
    {
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
        File.WriteAllText(ScenePath, new SceneFormat("custom.room").Write(Scene.Empty));
        File.WriteAllText(Path.Combine(Data, "values.json"), "{}");
        File.WriteAllText(Path.Combine(Schemas, "values.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Schemas, "room.schema.json"), "{}");
    }
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Explicit_binding_opens_one_scene_authority_across_workspaces_and_refuses_data_routes()
    {
        var project = DataProject.Open(Data, Schemas);
        var host = DocumentHost.For(project);
        Assert.Same(host, DocumentHost.For(project));
        Assert.True(host.BindScene("room.json", "custom.room", out var why), why);
        using var first = new DataWorkspace(project);
        using var second = new DataWorkspace(project);
        Assert.True(first.Show("room.json"), first.Said);
        Assert.True(second.Show("room.json"), second.Said);
        Assert.Null(first.Window);
        Assert.NotNull(first.SceneWindow);
        Assert.Same(first.SceneWindow.Editor.Session, second.SceneWindow!.Editor.Session);
        Assert.Null(project.Find("room.json")!.Session);
        Assert.False(project.Find("room.json")!.Open());
        Assert.False(project.Find("room.json")!.Repair());
        Assert.Contains(project.Find("room.json")!.Problems, p => p.Message.Contains("typed document host"));
        project.SaveCheck = _ => throw new InvalidOperationException("A scene must not use the legacy check.");
        Assert.False(first.SceneWindow.Run("editor.save"));
        Assert.Contains("explicit scene save policy", first.SceneWindow.Status);
    }

    [Fact]
    public void Wrong_kind_failure_retains_binding_and_previous_active_data_draft_then_can_retry()
    {
        var project = DataProject.Open(Data, Schemas);
        using var workspace = new DataWorkspace(project);
        Assert.True(workspace.Show("values.json"));
        var before = workspace.Window;
        Assert.True(workspace.Documents.BindScene("room.json", "other.kind", out _));
        Assert.False(workspace.Show("room.json"));
        Assert.Same(before, workspace.Window);
        Assert.Equal("values.json", workspace.Active!.Name);
        Assert.False(project.Find("room.json")!.Open());
        Assert.False(workspace.Documents.BindScene("room.json", "custom.room", out _));
        File.WriteAllText(ScenePath, new SceneFormat("other.kind").Write(Scene.Empty));
        Assert.True(workspace.Show("room.json"), workspace.Said);
        Assert.NotNull(workspace.SceneWindow);
        Assert.True(workspace.Show("values.json"));
        Assert.Same(before, workspace.Window);
    }

    [Fact]
    public void Existing_data_session_and_unadmitted_names_cannot_be_rebound()
    {
        var project = DataProject.Open(Data, Schemas);
        Assert.True(project.Find("room.json")!.Open());
        var session = project.Find("room.json")!.Session;
        var host = DocumentHost.For(project);
        Assert.False(host.BindScene("room.json", "custom.room", out var why));
        Assert.Contains("data session", why);
        Assert.Same(session, project.Find("room.json")!.Session);
        Assert.False(host.BindScene("../room.json", "custom.room", out _));
        Assert.False(host.OpenScene("values.json", out var entry, out _));
        Assert.Null(entry);
    }

    [Fact]
    public void Reserved_scene_file_operations_refuse_and_clean_save_all_is_noop()
    {
        var project = DataProject.Open(Data, Schemas);
        Assert.True(DocumentHost.For(project).BindScene("room.json", "custom.room", out _));
        var before = File.ReadAllText(ScenePath);
        Assert.False(project.Delete("room.json", out _));
        Assert.False(project.Rename("room.json", "other.json", out _));
        Assert.False(project.CreateFromText("room.json", "{}", out var why));
        Assert.Contains("typed document host", why);
        Assert.False(project.Create("room.json", "room.schema.json", out _));
        Assert.Empty(project.SaveAll());
        Assert.Equal(before, File.ReadAllText(ScenePath));
        Assert.Empty(project.Deleted);
    }

    [Fact]
    public void Switching_keeps_scene_draft_history_and_selection_and_save_all_never_partially_saves_data()
    {
        var project = DataProject.Open(Data, Schemas);
        using var workspace = new DataWorkspace(project);
        Assert.True(workspace.Documents.BindScene("room.json", "custom.room", out _));
        Assert.True(workspace.Show("room.json"));
        var scene = workspace.SceneWindow!;
        Assert.True(scene.Editor.Session.Execute(new AddObject(SceneObject.At("item", null, System.Numerics.Vector3.Zero))));
        scene.Editor.Select("item");
        Assert.True(workspace.Show("values.json"));
        Assert.True(workspace.Window!.Editor.Session.Execute(new SetValue("/value", System.Text.Json.JsonSerializer.SerializeToElement(3))));
        workspace.Documents.SceneSaveCheck=_=>"Fixture scene save refusal.";
        Assert.False(workspace.SaveAll());
        Assert.Equal("{}", File.ReadAllText(Path.Combine(Data, "values.json")));
        Assert.True(workspace.Show("room.json"));
        Assert.Same(scene, workspace.SceneWindow);
        Assert.Equal("item", scene.Editor.Primary);
        Assert.True(scene.Editor.Session.Dirty);
        Assert.True(scene.Editor.Session.History.CanUndo);
        Assert.Empty(new SceneFormat("custom.room").Read(File.ReadAllText(ScenePath)).Objects);
        Assert.False(workspace.GoTo(new ReferenceTarget("room.json", "/objects")));
        Assert.False(scene.SaveAs(Path.Combine(Data, "another.json")));
        Assert.False(File.Exists(Path.Combine(Data, "another.json")));
        workspace.Documents.SceneSaveCheck=null;Assert.True(workspace.SaveAll());Assert.False(scene.Editor.Session.Dirty);Assert.Equal(3,System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Data,"values.json"))).RootElement.GetProperty("value").GetInt32());Assert.Single(new SceneFormat("custom.room").Read(File.ReadAllText(ScenePath)).Objects);
    }

    [Fact]
    public void Clean_close_callback_is_invoked_for_bound_scenes_and_data_only_projects()
    {
        var project = DataProject.Open(Data, Schemas);
        var calls = 0;
        using var workspace = new DataWorkspace(project) { CloseProject = () => { calls++; return true; } };
        Assert.True(workspace.CloseProject!());
        Assert.Equal(1, calls);
        Assert.True(workspace.Documents.BindScene("room.json", "custom.room", out _));
        Assert.True(workspace.CloseProject!());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Failed_data_open_with_retained_schema_cannot_repair_after_typed_reservation()
    {
        File.WriteAllText(Path.Combine(Schemas, "room.schema.json"), """{"type":"object","required":["missing"]}""");
        var project = DataProject.Open(Data, Schemas);
        var file = project.Find("room.json")!;
        Assert.False(file.Open());
        Assert.NotNull(file.Schema);
        Assert.True(DocumentHost.For(project).BindScene("room.json", "custom.room", out _));
        Assert.False(file.Repair());
        Assert.Null(file.Session);
        Assert.Contains(file.Problems, p => p.Message.Contains("typed document host"));
    }

    [Fact]
    public void Malformed_scene_does_not_publish_an_entry_or_erase_its_binding()
    {
        var project = DataProject.Open(Data, Schemas);
        var host = DocumentHost.For(project);
        Assert.True(host.BindScene("room.json", "custom.room", out _));
        File.WriteAllText(ScenePath, "{");
        Assert.False(host.OpenScene("room.json", out var failed, out var why));
        Assert.Null(failed);
        Assert.NotEmpty(why);
        Assert.True(host.IsScene("room.json"));
        File.WriteAllText(ScenePath, new SceneFormat("custom.room").Write(Scene.Empty));
        Assert.True(host.OpenScene("room.json", out var entry, out why), why);
        Assert.True(host.OpenScene("room.json", out var again, out _));
        Assert.Same(entry, again);
    }
}
