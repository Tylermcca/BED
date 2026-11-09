using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

/// <summary>INVENTED performance guard on self-authored fixtures; all authoring uses public APIs.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class DataPerformanceTests(ITestOutputHelper output)
{
    private static readonly string[] Operations = ["read", "validate", "open", "first-false", "idle-false", "first-true", "idle-true", "filter", "sort", "cell", "fill", "paste", "undo", "save", "replay", "columns", "visible", "rows20", "rows-all"];

    // Frozen after baseline measurement; generous ceilings are not recalculated from the running test.
    private static double Budget(int rows, string op) => (rows, op) switch
    {
        (1000, "cell") => 723, // measured 72.2697 ms
        (1000, "columns") => 345, // measured 34.4593 ms
        (1000, "fill") => 4019, // measured 401.8040 ms
        (1000, "filter") => 826, // measured 82.5181 ms
        (1000, "first-false") => 367, // measured 36.6964 ms
        (1000, "first-true") => 1874, // measured 187.3539 ms
        (1000, "idle-false") => 100, // measured 0.5104 ms
        (1000, "idle-true") => 1522, // measured 152.1224 ms
        (1000, "open") => 734, // measured 73.3052 ms
        (1000, "paste") => 1057, // measured 105.6918 ms
        (1000, "read") => 100, // measured 2.6592 ms
        (1000, "replay") => 1386, // measured 138.5884 ms
        (1000, "rows-all") => 706, // measured 70.5561 ms
        (1000, "rows20") => 100, // measured 1.3776 ms
        (1000, "save") => 155, // measured 15.4406 ms
        (1000, "sort") => 821, // measured 82.0013 ms
        (1000, "undo") => 100, // measured 0.1153 ms
        (1000, "validate") => 646, // measured 64.5993 ms
        (1000, "visible") => 100, // measured 0.0916 ms
        (10000, "cell") => 6809, // measured 680.8858 ms
        (10000, "columns") => 359, // measured 35.8049 ms
        (10000, "fill") => 26678, // measured 2667.7235 ms
        (10000, "filter") => 13577, // measured 1357.6075 ms
        (10000, "first-false") => 418, // measured 41.7623 ms
        (10000, "first-true") => 1913, // measured 191.2270 ms
        (10000, "idle-false") => 100, // measured 1.1400 ms
        (10000, "idle-true") => 1532, // measured 153.1525 ms
        (10000, "open") => 7269, // measured 726.8605 ms
        (10000, "paste") => 7292, // measured 729.1455 ms
        (10000, "read") => 245, // measured 24.4993 ms
        (10000, "replay") => 13944, // measured 1394.3418 ms
        (10000, "rows-all") => 10300, // measured 1029.9168 ms
        (10000, "rows20") => 100, // measured 1.3699 ms
        (10000, "save") => 1285, // measured 128.4695 ms
        (10000, "sort") => 11899, // measured 1189.8498 ms
        (10000, "undo") => 100, // measured 1.0046 ms
        (10000, "validate") => 6402, // measured 640.1610 ms
        (10000, "visible") => 100, // measured 0.8135 ms
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "unknown performance operation"),
    };

    public static IEnumerable<object[]> Cases()
    {
        var diagnostic = Environment.GetEnvironmentVariable("AUTHORING_PERF_CASE");
        // Claude, on accept: the 1k cases run every time as regression guards; the 10k cases take minutes, so they run
        // when asked (AUTHORING_PERF_FULL=1, or AUTHORING_PERF_CASE naming them), keeping everyday green runs quick.
        var sizes = diagnostic is not null || Environment.GetEnvironmentVariable("AUTHORING_PERF_FULL") == "1" ? new[] { 1000, 10000 } : new[] { 1000 };
        foreach (var rows in sizes)
            foreach (var op in Operations)
                if (diagnostic is not null ? diagnostic.Split(',').Contains($"{rows}:{op}") : true) yield return [rows, op];   // 10k fill was a finding (2.7 s) until the list index (1.1 s)
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Median_budget_and_correctness(int rows, string op)
    {
        using var fixture = new Fixture(rows);
        var diagnostic = Environment.GetEnvironmentVariable("AUTHORING_PERF_PREFLIGHT") == "1";
        var budget = diagnostic ? double.PositiveInfinity : Budget(rows, op);
        var count = diagnostic ? 1 : 6; // one warm-up followed by five independent measurements
        var samples = new List<double>();
        var allocations = new List<long>();
        for (var run = 0; run < count; run++)
        {
            using var work = fixture.Prepare(op);
            Log(new { phase = "start", rows, op, run, diagnostic });
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            try { work.Run(); }
            catch (Exception e) { watch.Stop(); Log(new { phase = "error", rows, op, run, diagnostic, elapsedMs = watch.Elapsed.TotalMilliseconds, error = e.ToString() }); throw; }
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            work.Verify(); // correctness is mandatory, but not included in timings
            Log(new { phase = "sample", rows, op, run, ms = watch.Elapsed.TotalMilliseconds, allocated, diagnostic });
            if (diagnostic || run != 0) { samples.Add(watch.Elapsed.TotalMilliseconds); allocations.Add(allocated); }
        }
        samples.Sort(); allocations.Sort();
        var median = samples[samples.Count / 2];
        var summary = new { phase = "median", rows, op, ms = median, p95 = samples[^1], allocated = allocations[allocations.Count / 2], budget = diagnostic ? (double?)null : budget, diagnostic,
            machine = Environment.MachineName, cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), logicalProcessors = Environment.ProcessorCount,
            framework = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
#if DEBUG
            build = "Debug"
#else
            build = "Release"
#endif
        };
        output.WriteLine(JsonSerializer.Serialize(summary));
        Log(summary);
        Assert.True(median <= budget, $"{rows} {op}: median {median:F3} ms exceeds fixed {budget:F3} ms ceiling");
    }

    private static void Log(object record)
    {
        if (Environment.GetEnvironmentVariable("AUTHORING_PERF_LOG") is { Length: > 0 } file)
            File.AppendAllText(file, JsonSerializer.Serialize(record) + "\n");
    }

    private sealed record Work(Action Run, Action Verify, IDisposable? Resource = null) : IDisposable
    {
        public void Dispose() => Resource?.Dispose();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "authoring-perf-" + Guid.NewGuid().ToString("N"));
        private readonly string path;
        private readonly int count;
        private readonly JsonSchemas schemas;
        private readonly DataDocument original;
        private readonly DataFormat format = new();
        private const string Schema = "schema.json";

        public Fixture(int rows)
        {
            count = rows;
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "fixtures", "data-perf", "make_data.py"))) root = root.Parent;
            Assert.NotNull(root);
            Directory.CreateDirectory(folder);
            var start = new ProcessStartInfo("python") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { Path.Combine(root.FullName, "fixtures", "data-perf", "make_data.py"), folder, "--rows", rows.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stdout + stderr);
            path = Path.Combine(folder, $"rows-{rows}.json");
            schemas = new JsonSchemas().Add(Schema, File.ReadAllText(Path.Combine(folder, Schema)));
            original = format.Read(File.ReadAllText(path));
            Assert.Equal(rows, original.At("/rows")!.Value.GetArrayLength());
            var fields = original.At("/rows/0")!.Value.EnumerateObject().ToList();
            Assert.Equal(20, fields.Count(p => p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)));
            Assert.Equal(2, original.At("/rows/0/tags")!.Value.GetArrayLength());
        }

        private (EditorModel<DataDocument> Editor, IObjectType<DataDocument> Type, DataTableView Table) Open()
        {
            var session = DataKit.Open(path, schemas, Schema);
            var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
            var type = DataEditing.ObjectType(schemas, Schema);
            var table = new DataTableView(editor, type) { List = "/rows" };
            editor.Add(table.AsPanel());
            return (editor, type, table);
        }

        private static void Changed(EditorModel<DataDocument> editor) => Assert.Equal(-1, editor.Session.Current.At("/rows/0/score")!.Value.GetDouble());
        private static void Edit(DataTableView table) => Assert.True(table.SetCell("/rows/0", "score", -1d), table.Said);
        private void Unchanged(EditorModel<DataDocument> editor) => Assert.True(JsonElement.DeepEquals(original.Root, editor.Session.Current.Root));
        private void ExactEdits(EditorModel<DataDocument> editor, IEnumerable<(int Row, string Field, double Value)> edits)
        {
            var expected = JsonNode.Parse(original.Root.GetRawText())!;
            foreach (var (row, field, value) in edits) expected["rows"]![row]![field] = value;
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected), editor.Session.Current.Root));
        }

        // R2 promises one bulk command; allow the public journal's optional batch wrapper.
        private static void OneBulkCommand(EditorModel<DataDocument> editor)
        {
            static IEnumerable<JournalEntry> Leaves(JournalEntry entry)
            {
                if (entry.Batch is { } batch)
                {
                    foreach (var child in batch)
                        foreach (var leaf in Leaves(child)) yield return leaf;
                }
                else yield return entry;
            }
            var leaf = Assert.Single(Leaves(Assert.Single(editor.Session.Journal)));
            Assert.Equal("do", leaf.Op);
            Assert.Equal("data.set_many", leaf.Command);
        }

        public Work Prepare(string op)
        {
            if (op == "read")
            {
                DataDocument? result = null;
                return new(() => result = format.Read(File.ReadAllText(path)), () => Assert.True(JsonElement.DeepEquals(original.Root, result!.Root)));
            }
            if (op == "validate")
            {
                IReadOnlyList<Issue>? result = null;
                return new(() => result = schemas.Validate(original.Root, Schema), () => Assert.Empty(result!));
            }
            if (op == "open")
            {
                EditorWindow<DataDocument>? window = null;
                return new(() => { var e = Open(); window = new(e.Editor, new Inspector<DataDocument>(e.Editor).Add(e.Type)); },
                    () => { Assert.NotNull(window); Unchanged(window.Editor); Assert.Single(window.Editor.Panels, p => p.Id != BedAbout.Panel.Id && p.Id != KeysPanel.Id && p.Id != GettingStartedPanel.Id); });
            }
            var (editor, type, table) = Open();
            if (op.StartsWith("first-", StringComparison.Ordinal) || op.StartsWith("idle-", StringComparison.Ordinal))
            {
                var host = new HeadlessHost(1280, 800) { Drawing = op.EndsWith("true", StringComparison.Ordinal) };
                var draws = 0;
                void Draw()
                {
                    ImGui.SetNextWindowPos(new float2(0, 0));
                    ImGui.SetNextWindowSize(new float2(1200, 720));
                    ImGui.Begin("Performance table"u8);
                    table.Draw();
                    ImGui.End();
                    draws++;
                }
                try { if (op.StartsWith("idle-", StringComparison.Ordinal)) host.Frames(Draw, 2); }
                catch { host.Dispose(); throw; }
                var prior = draws;
                return new(() => host.Frame(Draw), () => { Assert.Equal(prior + 1, draws); Unchanged(editor); if (host.Drawing) Assert.True(host.Covered() > 100); }, host);
            }
            switch (op)
            {
                case "filter":
                    IReadOnlyList<string>? filtered = null;
                    return new(() => { table.Filter = "needle"; filtered = table.Visible(); }, () => { Assert.Equal(Enumerable.Range(0, count / 100).Select(i => $"/rows/{i * 100}"), filtered!); Unchanged(editor); });
                case "sort":
                    IReadOnlyList<string>? sorted = null;
                    return new(() => { table.Sort = ("n00", false); sorted = table.Visible(); }, () => { Assert.Equal(Enumerable.Range(0, count).Reverse().Select(i => $"/rows/{i}"), sorted!); Unchanged(editor); });
                case "cell":
                    return new(() => Edit(table), () => { Changed(editor); ExactEdits(editor, [(0, "score", -1d)]); Assert.Single(editor.Session.Journal); Assert.True(editor.Session.Undo()); Unchanged(editor); });
                case "fill":
                    return new(() => { var plan = table.PlanFill("score", "-2"); Assert.True(plan.Ok, string.Join(";", plan.Problems)); Assert.Equal(count, plan.Cells); Assert.Equal(count, plan.Commands.Count); Assert.True(table.Apply(plan), table.Said); },
                        () => { ExactEdits(editor, Enumerable.Range(0, count).Select(i => (i, "score", -2d))); OneBulkCommand(editor); Assert.True(editor.Session.Undo()); Unchanged(editor); });
                case "paste":
                    var tsv = string.Join("\n", Enumerable.Repeat("-3\t-3\t-3\t-3\t-3", 10));
                    return new(() => { var plan = table.PlanPaste("/rows/0", "n00", tsv); Assert.True(plan.Ok, string.Join(";", plan.Problems)); Assert.Equal(50, plan.Cells); Assert.Equal(50, plan.Commands.Count); Assert.True(table.Apply(plan), table.Said); },
                        () => { ExactEdits(editor, Enumerable.Range(0, 10).SelectMany(r => Enumerable.Range(0, 5).Select(c => (r, $"n{c:00}", -3d)))); OneBulkCommand(editor); Assert.True(editor.Session.Undo()); Unchanged(editor); });
                case "undo":
                    Edit(table);
                    return new(() => Assert.True(editor.Session.Undo()), () => { Unchanged(editor); Assert.Equal(2, editor.Session.Journal.Count); });
                case "save":
                    Edit(table);
                    var destination = Path.Combine(folder, "save-" + Guid.NewGuid().ToString("N") + ".json");
                    return new(() => editor.Session.Save(destination), () => { var reloaded = format.Read(File.ReadAllText(destination)); Assert.True(JsonElement.DeepEquals(editor.Session.Current.Root, reloaded.Root)); Assert.Empty(schemas.Validate(reloaded.Root, Schema)); });
                case "replay":
                    Edit(table);
                    var journal = JournalFile.Write(editor.Session.Journal);
                    DocumentSession<DataDocument>? replayed = null;
                    return new(() => replayed = DocumentSession<DataDocument>.Replay(original, JournalFile.Read(journal), format, new DataValidator(schemas, Schema), DataKit.Catalog()),
                        () => { Assert.True(JsonElement.DeepEquals(editor.Session.Current.Root, replayed!.Current.Root)); Assert.Single(replayed.Journal); });
                case "columns":
                    IReadOnlyList<InspectorRow>? columns = null;
                    return new(() => columns = table.Columns(), () => { Assert.Equal(22, columns!.Count); Assert.Equal(20, columns.Count(c => c.Editable)); });
                case "visible":
                    IReadOnlyList<string>? visible = null;
                    return new(() => visible = table.Visible(), () => { Assert.Equal(count, visible!.Count); Assert.Equal("/rows/0", visible[0]); });
                case "rows20":
                case "rows-all":
                    var ids = table.Visible().Take(op == "rows20" ? 20 : count).ToList();
                    var cells = 0;
                    return new(() => { foreach (var id in ids) cells += type.Rows(editor.Session.Current, [id]).Count; }, () => Assert.Equal(ids.Count * 22, cells));
                default: throw new ArgumentOutOfRangeException(nameof(op), op, "unknown performance operation");
            }
        }
        public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
