using Brutal.Authoring.Editor.ImGui;
using Xunit;
using Brutal.Authoring.Data;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class FileDialogsA428Tests
{
    // A strict script: wrong order, wrong arguments or an extra call is a failure.
    private sealed class Scripted : IFileDialogs
    {
        private readonly Queue<(string Method, string Title, string? Input,
            FileDialogFilter[] Filters, string? Result)> steps = new();
        public void Expect(string method, string title, string? input,
            FileDialogFilter[] filters, string? result) => steps.Enqueue((method, title, input, filters, result));
        private string? Next(string method, string title, string? input, IReadOnlyList<FileDialogFilter> filters)
        {
            Assert.NotEmpty(steps);
            var step = steps.Dequeue();
            Assert.Equal(step.Method, method);
            Assert.Equal(step.Title, title);
            Assert.Equal(step.Input, input);
            Assert.Equal(step.Filters, filters.ToArray());
            return step.Result;
        }
        public string? PickFolder(string title, string? start) => Next("folder", title, start, []);
        public string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start) => Next("open", title, start, filters);
        public string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName) => Next("save", title, suggestedName, filters);
        public void Complete() => Assert.Empty(steps);
    }

    [Fact]
    public void Scripted_paths_preserve_arguments_results_and_cancellation()
    {
        var script = new Scripted();
        FileDialogFilter[] filters = [new("JSON", "*.json"), new("All files", "*.*")];
        script.Expect("folder", "Choose data", "start", [], "chosen folder");
        script.Expect("open", "Open", "chosen folder", filters, "selected.json");
        script.Expect("save", "Save", "suggested.json", filters, null);
        IFileDialogs dialogs = script;
        Assert.Equal("chosen folder", dialogs.PickFolder("Choose data", "start"));
        Assert.Equal("selected.json", dialogs.OpenFile("Open", filters, "chosen folder"));
        Assert.Null(dialogs.SaveFile("Save", filters, "suggested.json"));
        script.Complete();
    }

    [Fact]
    public void Folder_cancellation_is_null_without_fabricating_a_path()
    {
        var script = new Scripted();
        script.Expect("folder", "Choose", null, [], null);
        Assert.Null(script.PickFolder("Choose", null));
        script.Complete();
    }

    private static void Reach(HeadlessHost host, Action draw, string label)
    {
        var window = ImGui.Internal.FindWindowByName("BED###launcher");
        Assert.False(window.IsNull());
        var wanted = window.GetID(label).Value;
        for (var i = 0; i < 40; i++)
        {
            if (ImGui.GetCurrentContext().NavId.Value == wanted) return;
            host.Press(draw, ImGuiKey.Tab);
        }
        Assert.Fail("Tab did not reach " + label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Launcher_folder_choice_uses_host_and_preserves_cancel(bool cancel)
    {
        var root = Directory.CreateTempSubdirectory("file-dialog-a428-").FullName;
        try
        {
            var old = Project(root, "old");
            var chosen = Project(root, "chosen");
            var script = new Scripted();
            script.Expect("folder", "Data folder", old.Data, [], cancel ? null : Path.GetDirectoryName(chosen.Data));
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json")), old);
            using var host = new HeadlessHost(1600, 900) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            host.MoveMouse(launcher.Draw, -100, -100);
            host.Frames(launcher.Draw, 5);
            host.Press(launcher.Draw, ImGuiKey.O, ctrl: true);
            Reach(host, launcher.Draw, "Browse...##browse0");
            launcher.FileDialogs = script;
            host.Press(launcher.Draw, ImGuiKey.Enter);
            script.Complete();
            Assert.Null(launcher.Workspace);
            Assert.Empty(launcher.Recent.Items);
            var picker = ImGui.Internal.FindWindowByName("Choose a folder###picker");
            Assert.True(picker.IsNull() || !picker.Active, "Native choice must not also open the fallback browser.");
            Reach(host, launcher.Draw, "Open");
            host.Press(launcher.Draw, ImGuiKey.Enter);
            Assert.NotNull(launcher.Workspace);
            Assert.Equal(cancel ? old : chosen, launcher.Current);
            Assert.Equal("{\"items\":[]}", File.ReadAllText(Path.Combine(old.Data, "items.json")));
            Assert.Equal("{\"items\":[]}", File.ReadAllText(Path.Combine(chosen.Data, "items.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Launcher_without_service_keeps_existing_folder_browser()
    {
        var root = Directory.CreateTempSubdirectory("file-dialog-a428-").FullName;
        try
        {
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json")));
            using var host = new HeadlessHost(1600, 900) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            host.MoveMouse(launcher.Draw, -100, -100); host.Frames(launcher.Draw, 5);
            Assert.Null(launcher.FileDialogs);
            host.Press(launcher.Draw, ImGuiKey.O, ctrl: true);
            Reach(host, launcher.Draw, "Browse...##browse0");
            host.Press(launcher.Draw, ImGuiKey.Tab, shift: true);
            host.Type(launcher.Draw, root); host.Press(launcher.Draw, ImGuiKey.Tab);
            host.Press(launcher.Draw, ImGuiKey.Enter);
            var picker = ImGui.Internal.FindWindowByName("Choose a folder###picker");
            Assert.False(picker.IsNull()); Assert.True(picker.Active);
            Assert.Null(launcher.Workspace); Assert.Empty(launcher.Recent.Items);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FailedDialogs : IFileDialogs
    {
        public string? PickFolder(string title, string? start) => throw new System.Runtime.InteropServices.COMException("scripted dialog failure");
        public string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start) => throw new NotSupportedException();
        public string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName) => throw new NotSupportedException();
    }

    [Fact]
    public void Launcher_reports_dialog_failure_without_opening_or_remembering_a_project()
    {
        var root = Directory.CreateTempSubdirectory("file-dialog-a428-").FullName;
        try
        {
            var paths = Project(root, "original");
            using var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json")), paths);
            using var host = new HeadlessHost(1600, 900) { Drawing = false };
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            host.MoveMouse(launcher.Draw, -100, -100); host.Frames(launcher.Draw, 5);
            host.Press(launcher.Draw, ImGuiKey.O, ctrl: true);
            Reach(host, launcher.Draw, "Browse...##browse0");
            launcher.FileDialogs = new FailedDialogs();
            host.Press(launcher.Draw, ImGuiKey.Enter);
            Assert.Contains("scripted dialog failure", launcher.Said);
            Assert.Null(launcher.Workspace); Assert.Empty(launcher.Recent.Items);
            Reach(host, launcher.Draw, "Open"); host.Press(launcher.Draw, ImGuiKey.Enter);
            Assert.Equal(paths, launcher.Current);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ProjectPaths Project(string root, string name)
    {
        var folder = Path.Combine(root, name);
        var data = Directory.CreateDirectory(Path.Combine(folder, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(folder, "schemas")).FullName;
        File.WriteAllText(Path.Combine(data, "items.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(schemas, "items.schema.json"), "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}}}");
        return new(data, schemas);
    }
}
