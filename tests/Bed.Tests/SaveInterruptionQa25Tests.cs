using System.Numerics;
using System.Text;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SaveInterruptionQa25Tests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "data", "scene", "kit" })
            foreach (var stop in Enum.GetValues<Stop>()) yield return [kind, stop];
    }
    public enum Stop { BeforeCreate, AfterCreate, PartialWrite, AfterWrite, AfterFlush, AfterClose, BeforeReplace, AfterReplace, Complete }
    private sealed class PowerLost : IOException { }

    // A stopped filesystem refuses all later calls, including the writer's finally cleanup.
    // Rename is an atomic namespace switch, not an emulated copy of the destination.
    private sealed class Files(Stop stop) : IAtomicFileSystem
    {
        public readonly Dictionary<string, byte[]> Disk = new(StringComparer.Ordinal);
        private bool dead;
        private readonly Stop stopPoint = stop;
        private void Alive() { if (dead) throw new PowerLost(); }
        private void Cut(Stop point) { Alive(); if (stopPoint == point) { dead = true; throw new PowerLost(); } }
        public Stream CreateNew(string path)
        {
            Cut(Stop.BeforeCreate); Assert.False(Disk.ContainsKey(path)); Disk.Add(path, []);
            Cut(Stop.AfterCreate); return new Output(this, path);
        }
        public void FlushToDisk(Stream stream) { ((Output)stream).Flushed = true; Cut(Stop.AfterFlush); }
        public void MoveOverwrite(string source, string target)
        {
            Cut(Stop.BeforeReplace); Assert.True(Closed); Assert.True(Flushed);
            Disk[target] = Disk[source]; Disk.Remove(source); Cut(Stop.AfterReplace);
        }
        public bool Exists(string path) { Alive(); return Disk.ContainsKey(path); }
        public void Delete(string path) { Alive(); Disk.Remove(path); }
        public bool Closed, Flushed;
        private sealed class Output(Files files, string path) : MemoryStream
        {
            public bool Flushed;
            public override void Write(byte[] buffer, int offset, int count)
            {
                files.Alive();
                if (files.stopPoint == Stop.PartialWrite)
                {
                    files.Disk[path] = buffer.AsSpan(offset, Math.Max(1, count / 2)).ToArray();
                    files.Cut(Stop.PartialWrite);
                }
                base.Write(buffer, offset, count); files.Disk[path] = ToArray(); files.Cut(Stop.AfterWrite);
            }
            protected override void Dispose(bool disposing)
            {
                // A real process kill never executes dispose; leave the pre-kill disk snapshot unchanged.
                if (files.dead) return;
                files.Closed = true; files.Flushed = Flushed; base.Dispose(disposing); files.Cut(Stop.AfterClose);
            }
        }
    }
    private static (byte[] Old, byte[] New) Content(string kind)
    {
        string old, next;
        if (kind == "scene")
        {
            var format = new SceneFormat();
            old = format.Write(new Scene([SceneObject.At("box", null, Vector3.Zero)]));
            next = format.Write(new Scene([SceneObject.At("box", null, new Vector3(3, 2, 1))]));
        }
        else if (kind == "kit")
        {
            old = "{\"parts\":[{\"id\":\"beam\",\"offset\":0}]}";
            next = "{\"parts\":[{\"id\":\"beam\",\"offset\":3}]}";
        }
        else { old = "{\"rows\":[{\"id\":\"row\",\"value\":1}]}"; next = "{\"rows\":[{\"id\":\"row\",\"value\":2}]}"; }
        return (Encoding.UTF8.GetBytes(old), Encoding.UTF8.GetBytes(next));
    }
    [Theory, MemberData(nameof(Cases))]
    public void Interrupted_writer_preserves_whole_target_and_reopen_reports_leftovers(string kind, Stop stop)
    {
        var root = Directory.CreateTempSubdirectory("save-qa25-").FullName;
        try
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            var path = Path.Combine(data, kind + ".json"); var (old, next) = Content(kind);
            var files = new Files(stop); files.Disk[path] = old;
            if (stop == Stop.Complete) AtomicFile.Write(path, next, files);
            else Assert.Throws<PowerLost>(() => AtomicFile.Write(path, next, files));
            var replaced = stop is Stop.AfterReplace or Stop.Complete;
            Assert.Equal(replaced ? next : old, files.Disk[path]);
            using var parsed = JsonDocument.Parse(files.Disk[path]);
            var temp = files.Disk.Keys.Where(p => p != path).ToArray();
            Assert.Equal(stop is Stop.BeforeCreate or Stop.AfterReplace or Stop.Complete ? 0 : 1, temp.Length);
            if (stop == Stop.PartialWrite) Assert.InRange(files.Disk[temp[0]].Length, 1, next.Length - 1);
            if (stop is Stop.AfterFlush or Stop.AfterClose or Stop.BeforeReplace) Assert.Equal(next, files.Disk[temp[0]]);
            foreach (var pair in files.Disk) File.WriteAllBytes(pair.Key, pair.Value);
            File.WriteAllText(Path.Combine(schemas, kind + ".schema.json"), "{\"type\":\"object\"}");
            var before = Directory.GetFiles(data).ToDictionary(p => p, File.ReadAllBytes);
            using var host = new HeadlessHost(1200, 800) { Drawing = false };
            EditorWindow<Scene>.Configure(ImGui.GetIO());
            using var workspace = new DataWorkspace(DataProject.Open(data, schemas));
            if (kind == "scene") Assert.True(workspace.Documents.BindScene(kind + ".json", "scene", out _));
            Assert.True(workspace.Show(kind + ".json"), workspace.Said);
            Assert.Equal(temp.Length > 0, workspace.Said.Contains("may have been interrupted", StringComparison.Ordinal));
            if (temp.Length > 0)
            {
                Assert.Contains("may still be running", workspace.Said);
                Assert.Contains("Opened the saved file only", workspace.Said);
                Assert.Contains("left untouched", workspace.Said);
            }
            if (stop == Stop.AfterFlush)
            {
                host.Frames(workspace.Draw, 3);
                Assert.Contains(Ui.Snapshot().Nodes, n => n.Label.Contains("may have been interrupted", StringComparison.Ordinal));
            }
            if (kind == "scene")
            {
                Assert.False(workspace.SceneWindow!.Editor.Session.Dirty);
                Assert.Equal(replaced ? new Vector3(3, 2, 1) : Vector3.Zero, workspace.SceneWindow.Editor.Session.Current.Find("box")!.Position);
            }
            else Assert.False(workspace.Active!.Session!.Dirty);
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(data).Order());
            foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Similar_unowned_temp_names_are_ignored_and_no_files_are_deleted()
    {
        var root = Directory.CreateTempSubdirectory("save-qa25-names-").FullName;
        try
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            File.WriteAllBytes(Path.Combine(data, "data.json"), Content("data").Old);
            File.WriteAllText(Path.Combine(schemas, "data.schema.json"), "{\"type\":\"object\"}");
            foreach (var name in new[] { ".data.json.not-a-guid.tmp", ".other.json.11111111111111111111111111111111.tmp", ".data.json.11111111-1111-1111-1111-111111111111.tmp" })
                File.WriteAllText(Path.Combine(data, name), "unrelated");
            using var host = new HeadlessHost(1200,800) { Drawing = false }; EditorWindow<Scene>.Configure(ImGui.GetIO());
            using var workspace = new DataWorkspace(DataProject.Open(data, schemas));
            Assert.True(workspace.Show("data.json")); Assert.Equal("Showing data.json.", workspace.Said);
            Assert.Equal(4, Directory.GetFiles(data).Length);
        }
        finally { Directory.Delete(root, true); }
    }
}
