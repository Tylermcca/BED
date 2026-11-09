using System.Diagnostics;
using System.Text;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram = Brutal.Authoring.Launcher.Program;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ReleaseJourneyBoundaryTests
{
    // Copies only the three declared, self-authored project inputs. No generated schema or replacement scene.
    private sealed class Rig : IDisposable
    {
        private static readonly string[] Inputs = ["reference_map.json", "data/level.json", "schemas/scene.schema.json"];
        private readonly string root = Directory.CreateTempSubdirectory("bed-qa02-").FullName;
        private readonly Dictionary<string, byte[]> original = [];
        public string ProjectFolder { get; }
        public readonly HeadlessHost Host = new(1200, 900) { Drawing = false };
        public readonly ProjectLauncher Launcher;
        public readonly FolderChoice Dialog;
        public EditorWindow<Scene> Window => Launcher.Workspace!.SceneWindow!;
        public string Saved => Encoding.UTF8.GetString(original["data/level.json"]);
        public Rig()
        {
            ProjectFolder = Path.Combine(root, "project");
            foreach (var relative in Inputs)
            {
                var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", "scene-starter", relative));
                original.Add(relative, bytes);
                var destination = Path.Combine(ProjectFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            }
            var loaded = Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory, "adapters")));
            Assert.Empty(loaded.Problems);
            Assert.Single(loaded.Loaded, adapter => adapter.Name == "SceneStarter");
            Dialog = new(ProjectFolder);
            Launcher = BedProgram.SetUp(new(new RecentProjects(Path.Combine(root, "recent.json")))
                { Loaded = loaded.Loaded, FileDialogs = Dialog });
            EditorWindow<Scene>.Configure(ImGui.GetIO());
        }
        public void OpenByKeys()
        {
            Assert.Null(Launcher.Workspace);
            Host.Frames(Launcher.Draw, 3);
            Host.Press(Launcher.Draw, ImGuiKey.O, ctrl: true);
            Host.Frames(Launcher.Draw, 3);
            Assert.Equal(1, Dialog.Calls);
            Assert.NotNull(Launcher.Workspace);
            Assert.Equal("SceneStarter", Launcher.Using!.Name);
            // Typed files are listed, not auto-opened through the ordinary data-session path.
            Assert.Null(Launcher.Workspace.Active);
            var file = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label.Contains("level.json", StringComparison.Ordinal));
            Click(file);
            Assert.Equal("level.json", Launcher.Workspace.Active!.Name);
            Assert.NotNull(Launcher.Workspace.SceneWindow);
            Assert.Equal(2, Window.Editor.Session.Current.Objects.Length);
            Assert.Equal("root", Window.Editor.Session.Current.Find("child")!.Parent);
            Assert.False(Window.Editor.Session.Dirty);
            Assert.Single(Launcher.Recent.Items);
            Unchanged();
        }
        public void Click(string label)
        {
            Click(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button && n.Label == label));
        }
        private void Click(UiNode node)
        {
            Assert.True(node.Enabled, node.DisabledReason); Assert.False(node.Bounds.IsEmpty);
            Host.Click(Launcher.Draw, node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y + node.Bounds.Height / 2);
            Host.Frames(Launcher.Draw, 2);
        }
        public void Until(Func<bool> done, LinkedPlayPreview preview)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5)) { Host.Frame(Launcher.Draw); Thread.Sleep(5); }
            Assert.True(done(), preview.Status);
        }
        public void Unchanged()
        {
            foreach (var (relative, bytes) in original) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(ProjectFolder, relative)));
            Assert.Equal(Inputs.Order(), Directory.GetFiles(ProjectFolder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(ProjectFolder, file).Replace('\\', '/')).Order());
        }
        public void Dispose()
        {
            Launcher.Dispose(); Host.Dispose();
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, true);
        }
    }
    private sealed class FolderChoice(string folder) : IFileDialogs
    {
        public int Calls;
        public string? PickFolder(string title, string? start) { Calls++; return folder; }
        public string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start) => throw new InvalidOperationException();
        public string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName) => throw new InvalidOperationException();
    }

    [Fact]
    public void Packaged_scene_project_opens_through_CtrlO_and_automatic_adapter_without_rewriting_its_inputs()
    {
        using var rig = new Rig(); rig.OpenByKeys();
        Assert.IsType<LinkedPlayHost>(rig.Launcher.LinkedPlayHost);
        Assert.Null(rig.Window.Editor.Game);
        Assert.Equal(0, rig.Window.Editor.Session.History.UndoCount);
        Assert.Empty(rig.Window.Editor.Session.Journal);
        rig.Unchanged();
    }

    [Fact]
    public void Clean_packaged_scene_Play_button_reaches_real_peer_ready_then_Stop_without_any_save()
    {
        using var rig = new Rig(); rig.OpenByKeys();
        var session = rig.Window.Editor.Session; var document = session.Current; var revision = session.Revision;
        rig.Click("Play"); Assert.False(rig.Window.LinkedPlaySavePending);
        var preview = Assert.IsType<LinkedPlayPreview>(rig.Window.Editor.Game);
        Assert.Equal(PlayInputChoice.Saved, preview.Session.Snapshot.Choice);
        Assert.Equal(rig.Saved, Encoding.UTF8.GetString(preview.Session.Snapshot.CopyPayload()));
        rig.Until(() => preview.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted, preview);
        Assert.Equal(LinkedPlayState.Running, preview.Session.State); Assert.Null(preview.Session.Error);
        rig.Click("Stop"); rig.Until(() => rig.Window.Editor.Playing is null, preview);
        Assert.Equal(LinkedPlayState.Stopped, preview.Session.State); Assert.Null(preview.Session.Error);
        Assert.True(preview.HasCompleted); Assert.Same(document, session.Current); Assert.Equal(revision, session.Revision);
        Assert.False(session.Dirty); Assert.Equal(0, session.History.UndoCount); Assert.Empty(session.Journal);
        rig.Unchanged();
    }
}
