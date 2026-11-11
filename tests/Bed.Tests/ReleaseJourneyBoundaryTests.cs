using System.Diagnostics;
using System.Text;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram = Bed.Launcher.Program;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ReleaseJourneyBoundaryTests
{
    // Copies the declared self-authored inputs and real game, with bounded saved-file Play settings.
    private sealed class Rig : IDisposable
    {
        private static readonly string[] Inputs = ["reference_map.json", "data/level.json", "schemas/scene.schema.json"];
        private readonly string root = Directory.CreateTempSubdirectory("bed-qa02-").FullName;
        private readonly Dictionary<string, byte[]> original = [];
        public string ProjectFolder { get; }
        public readonly HeadlessHost Host = new(1200, 900) { Drawing = false };
        public readonly ProjectLauncher Launcher;
        public readonly SamplePlayJourneyTests.ObservedHost PlayHost;
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
            // The shipped level places the textured crate: copy its complete source closure so
            // normal startup publishes the same generation as the package, and hold it unchanged.
            var assets = Path.Combine(AppContext.BaseDirectory, "samples", "scene-starter", "assets");
            foreach (var path in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
            {
                var relative = "assets/" + Path.GetRelativePath(assets, path).Replace('\\', '/');
                var bytes = File.ReadAllBytes(path);
                original.Add(relative, bytes);
                var destination = Path.Combine(ProjectFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            }
            Directory.CreateDirectory(Path.Combine(ProjectFolder,"game"));
            foreach(var name in SceneStarter.ExportInventory.Required.Where(name=>name!="scene.json"))
            {
                var relative="game/"+name;
                var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name));
                original.Add(relative,bytes);File.WriteAllBytes(Path.Combine(ProjectFolder,relative),bytes);
            }
            PlaySettingsStore.Save(ProjectFolder,new("game/SceneStarter.exe",["--project",".","--seconds","0"]));
            original.Add(".bed/project.json",File.ReadAllBytes(Path.Combine(ProjectFolder,".bed","project.json")));
            var loaded = Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory, "adapters")));
            Assert.Empty(loaded.Problems);
            Assert.Single(loaded.Loaded, adapter => adapter.Name == "SceneStarter");
            Dialog = new(ProjectFolder);
            Launcher = BedProgram.SetUp(new(new RecentProjects(Path.Combine(root, "recent.json")))
                { Loaded = loaded.Loaded, FileDialogs = Dialog });
            PlayHost=new(Assert.IsType<ProjectPlayBridge>(Launcher.ProjectPlayHost));Launcher.ProjectPlayHost=PlayHost;
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
            // The level opens immediately through its typed owner, without an ordinary data session.
            Assert.Equal("level.json",Launcher.Workspace.Active!.Name);
            Assert.Null(Launcher.Workspace.Active.Session);
            var file = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label.Contains("level.json", StringComparison.Ordinal));
            Click(file);
            Assert.Equal("level.json", Launcher.Workspace.Active!.Name);
            Assert.NotNull(Launcher.Workspace.SceneWindow);
            Assert.Equal(3, Window.Editor.Session.Current.Objects.Length); // root, child and the placed textured crate
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
        public void Until(Func<bool> done, IProjectPlaySession preview)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5)) { Host.Frame(Launcher.Draw); Thread.Sleep(5); }
            Assert.True(done(), preview.Status);
        }
        public void Unchanged()
        {
            foreach (var (relative, bytes) in original) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(ProjectFolder, relative)));
            var published = Bed.Runtime.ProjectAssets.Open(ProjectFolder);
            // The placed crate is published. Outside the cache, the files are exactly the saved inputs;
            // inside it, only the pointer, lock and the one current generation.
            Assert.NotEmpty(published.ArtifactNames);
            var files = Directory.GetFiles(ProjectFolder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(ProjectFolder, file).Replace('\\', '/')).ToArray();
            Assert.Equal(original.Keys.Order(), files.Where(file => !file.StartsWith(".bed/cache/", StringComparison.Ordinal)).Order());
            var generation = $".bed/cache/generations/{published.Id}/";
            foreach (var required in new[] { ".bed/cache/current.json", ".bed/cache/owner.lock", generation + "manifest.json", generation + "owner.txt" })
                Assert.Contains(required, files);
            Assert.All(files.Where(file => file.StartsWith(".bed/cache/", StringComparison.Ordinal)
                && file is not ".bed/cache/current.json" and not ".bed/cache/owner.lock"), file => Assert.StartsWith(generation, file));
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
        Assert.Null(rig.Launcher.LinkedPlayHost);
        Assert.IsType<ProjectPlayBridge>(rig.PlayHost.Bridge);
        Assert.NotNull(rig.Launcher.Workspace!.PlaySettings);
        Assert.Equal(0,rig.PlayHost.Starts);
        Assert.Null(rig.Window.Editor.Game);
        Assert.Equal(0, rig.Window.Editor.Session.History.UndoCount);
        Assert.Empty(rig.Window.Editor.Session.Journal);
        rig.Unchanged();
    }

    [Fact]
    public void Clean_packaged_scene_Play_button_loads_saved_level_and_exits_zero_without_any_save()
    {
        using var rig = new Rig(); rig.OpenByKeys();
        var session = rig.Window.Editor.Session; var document = session.Current; var revision = session.Revision;
        rig.Click("Play"); Assert.False(rig.Launcher.Workspace!.ProjectPlaySavePending);
        var preview=Assert.IsType<SamplePlayJourneyTests.ObservedSession>(rig.Launcher.Workspace.ProjectPlay);
        Assert.Equal(1,rig.PlayHost.Starts);Assert.Equal(1,preview.Starts);
        Assert.Equal(rig.Saved,preview.SavedAtStart);
        Assert.Null(rig.Window.Editor.Game);
        rig.Until(()=>preview.HasCompleted&&preview.Logs.Any(line=>line.Text=="The process exited normally (code 0)."),preview);
        Assert.Equal("The process exited normally (code 0).",preview.Status);
        Assert.Contains(preview.Logs,line=>!line.Error&&line.Text=="SceneStarter loaded saved level: 3 objects, 1 meshes.");
        Assert.Equal(0,preview.Stops);Assert.False(rig.Window.ProjectPlaying!());
        Assert.Null(rig.Window.Editor.Playing);
        Assert.Same(document,session.Current);Assert.Equal(revision,session.Revision);
        Assert.False(session.Dirty); Assert.Equal(0, session.History.UndoCount); Assert.Empty(session.Journal);
        rig.Unchanged();
    }
}
