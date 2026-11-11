using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class StartWindowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "start-window-" + Guid.NewGuid().ToString("N"));
    private string Installed => Path.Combine(root, "installed");
    private string Projects => Path.Combine(root, "projects");
    private readonly HeadlessHost host = new(1400, 900) { Drawing = false };
    public StartWindowTests()
    {
        Directory.CreateDirectory(Path.Combine(Installed, "data")); Directory.CreateDirectory(Path.Combine(Installed, "schemas"));
        File.WriteAllText(Path.Combine(Installed, "README.md"), "Invented recipes fixture");
        File.WriteAllText(Path.Combine(Installed, "reference_map.json"), """{"game":"Acme","labels":[],"references":[]}""");
        foreach (var name in new[] { "recipes", "wares" })
        {
            File.WriteAllText(Path.Combine(Installed, "data", name + ".json"), "{\"" + name + "\":[]}");
            File.WriteAllText(Path.Combine(Installed, "schemas", name + ".schema.json"), """{"type":"object"}""");
        }
        EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
    }
    private ProjectLauncher Launcher() => new(new RecentProjects(Path.Combine(root, "recent.json")),
        ProjectPaths.In(Installed), new ProjectCreator(Installed), Projects);
    public void Dispose() { host.Dispose(); Directory.Delete(root, recursive: true); }
    private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(Installed, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(Installed, f), File.ReadAllBytes);

    [Fact] public void Example_is_a_personal_copy_and_shows_the_guide_only_after_opening()
    {
        var before = Snapshot(); using var launcher = Launcher();
        Assert.True(launcher.TryExample(), launcher.Said);
        Assert.Equal(Path.Combine(Projects, "Example", "data"), launcher.Current!.Data);
        Assert.True(launcher.Workspace!.Window!.Editor.IsOpen(GettingStartedPanel.Id));
        Assert.Contains(Path.Combine(Projects, "Example"), launcher.Said);
        foreach (var (name, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Installed, name)));
        Assert.Single(launcher.Recent.Items);
        Assert.True(launcher.Close()); Assert.True(launcher.TryExample(), launcher.Said);
        Assert.Equal(Path.Combine(Projects, "Example 2", "data"), launcher.Current!.Data);
        Assert.False(launcher.Workspace!.Window!.Editor.IsOpen(GettingStartedPanel.Id));
    }
    [Fact] public void Failed_creation_never_opens_or_records_a_recent_project()
    {
        using var launcher = Launcher(); Assert.False(launcher.CreateProject("empty", Projects, "../bad"));
        Assert.Null(launcher.Workspace); Assert.Empty(launcher.Recent.Items); Assert.False(Directory.Exists(Projects));
    }
    [Fact] public void Search_matches_name_folder_and_kind_without_opening_or_reordering()
    {
        using var launcher = Launcher(); Assert.True(launcher.Open(ProjectPaths.In(Installed)!)); Assert.True(launcher.Close());
        Assert.Single(launcher.SearchRecent("installed")); Assert.Single(launcher.SearchRecent("Data only"));
        Assert.Empty(launcher.SearchRecent("missing")); Assert.Single(launcher.Recent.Items); Assert.Null(launcher.Workspace);
    }
    [Fact] public void CtrlO_uses_the_existing_host_dialog_and_opens_the_selected_folder()
    {
        using var launcher = Launcher(); var dialogs = new Dialogs { Chosen = Installed }; launcher.FileDialogs = dialogs;
        host.Frames(launcher.Draw, 4); host.Press(launcher.Draw, ImGuiKey.O, ctrl: true);
        Assert.Equal(1, dialogs.Calls); Assert.NotNull(launcher.Workspace); Assert.Single(launcher.Recent.Items);
    }
    [Fact] public void Cancelled_native_folder_choice_does_not_open_or_record_anything()
    {
        using var launcher = Launcher(); var dialogs = new Dialogs(); launcher.FileDialogs = dialogs;
        host.Frames(launcher.Draw, 4); host.Press(launcher.Draw, ImGuiKey.O, ctrl: true);
        Assert.Equal(1, dialogs.Calls); Assert.Null(launcher.Workspace); Assert.Empty(launcher.Recent.Items);
    }
    [Fact] public void Keyboard_new_project_uses_real_fields_and_opens_the_validated_starter()
    {
        using var launcher = Launcher(); host.Frames(launcher.Draw, 4);
        host.Press(launcher.Draw, ImGuiKey.N, ctrl: true, shift: true);
        host.Press(launcher.Draw, ImGuiKey.A, ctrl: true); host.Type(launcher.Draw, "Acme");
        host.Press(launcher.Draw, ImGuiKey.Tab); host.Press(launcher.Draw, ImGuiKey.A, ctrl: true); host.Type(launcher.Draw, Projects);
        for (var i = 0; i < 3; i++) host.Press(launcher.Draw, ImGuiKey.Tab);
        host.Press(launcher.Draw, ImGuiKey.Enter); host.Frames(launcher.Draw, 2);
        Assert.NotNull(launcher.Workspace); Assert.Equal(Path.Combine(Projects, "Acme", "data"), launcher.Current!.Data);
        Assert.Equal("settings.json", launcher.Workspace.Active!.Name); Assert.Single(launcher.Recent.Items);
    }
    [Fact] public void CtrlO_without_native_dialog_renders_the_manual_folder_step()
    {
        using var launcher = Launcher(); host.MoveMouse(launcher.Draw, -100, -100); host.Frames(launcher.Draw, 5);
        host.Press(launcher.Draw, ImGuiKey.O, ctrl: true); host.Frames(launcher.Draw, 2);
        host.Frame(() => { ImGui.LogToClipboard(); launcher.Draw(); ImGui.LogFinish(); });
        var text = ImGui.GetClipboardText().ToString() ?? "";
        Assert.Contains("Rules folder (schema)", text); Assert.Contains("Data folder", text); Assert.Null(launcher.Workspace);
    }
    private sealed class Dialogs : IFileDialogs
    {
        public string? Chosen; public int Calls;
        public string? PickFolder(string title, string? start) { Calls++; return Chosen; }
        public string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start) => throw new InvalidOperationException();
        public string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName) => throw new InvalidOperationException();
    }
}

internal static class StartWindowTestProjects
{
    internal static ProjectCreator Creator(string root)
    {
        var at = Path.Combine(root, "installed-recipes");
        Directory.CreateDirectory(Path.Combine(at, "data")); Directory.CreateDirectory(Path.Combine(at, "schemas"));
        File.WriteAllText(Path.Combine(at, "README.md"), "Invented example");
        File.WriteAllText(Path.Combine(at, "reference_map.json"), """{"game":"Acme","labels":[],"references":[]}""");
        foreach (var name in new[] { "recipes", "wares" })
        {
            File.WriteAllText(Path.Combine(at, "data", name + ".json"), "{\"" + name + "\":[]}");
            File.WriteAllText(Path.Combine(at, "schemas", name + ".schema.json"), """{"type":"object"}""");
        }
        return new ProjectCreator(at);
    }
}
