using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.Text;
using Brutal.ImGuiApi;
using ImCode.Text;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

// INVENTED hostile, self-authored fixtures. BORROWED: the public text/session contract and
// the accepted text-panel measurement protocol. No game data or OS window/input.
[Collection(ImGuiCollection.Name)]
public sealed class HostileTextProofTests(ITestOutputHelper output)
{
    public static readonly string[] Names = ["five_mb_line", "depth_5000", "one_mb_string", "objects_100000", "empty",
        "bom", "crlf", "mixed_endings", "tabs", "unicode_valid", "lone_surrogate", "escaped_nul", "raw_nul", "invalid_utf8",
        "duplicate_keys", "huge_integer", "negative_zero", "exponent_400", "trailing_comma", "comments",
        "paste_one_mb", "save_race", "undo_refused", "huge_number_change", "duplicate_change", "unicode_error"];
    public static IEnumerable<object[]> Cases => Names.Select(n => new object[] { n });
    private static string Input(string name) => name switch
    {
        "five_mb_line" => "{\"value\":\"" + new string('a', 5_000_000 - 12) + "\"}",
        "depth_5000" => new string('[', 5000) + "0" + new string(']', 5000),
        "one_mb_string" or "paste_one_mb" => "{\"value\":\"" + new string('a', 1_000_000) + "\"}",
        "objects_100000" => "{\"value\":[" + string.Join(',', Enumerable.Repeat("{\"x\":0}", 100000)) + "]}",
        "empty" => "", "bom" => "\uFEFF{\"value\":1}", "crlf" => "{\r\n\"value\":1\r\n}",
        "mixed_endings" => "{\r\n\"value\":1,\n\"other\":2\r\n}", "tabs" => "{\n\t\"value\":\t1\n}",
        "unicode_valid" => "{\"value\":\"\U0001F680 e\u0301\"}", "lone_surrogate" => "{\"value\":\"\\ud800\"}",
        "escaped_nul" => "{\"value\":\"a\\u0000b\"}", "raw_nul" => "{\"value\":\"a\0b\"}",
        "invalid_utf8" => "{\"value\":\"a\uFFFDb\"}", "duplicate_keys" or "duplicate_change" => "{\"value\":1,\"value\":2}",
        "huge_integer" => "{\"value\":" + new string('9', 400) + "}", "negative_zero" => "{\"value\":-0}",
        "exponent_400" => "{\"value\":1e400}", "huge_number_change" => "{\"value\":2e400}",
        "trailing_comma" => "{\n\"value\":1,\n}", "comments" => "{\n// comment\n\"value\":1\n}",
        "unicode_error" => "{\"value\":\"\U0001F680e\u0301\",}",
        "undo_refused" => "{\n\"value\":1,\n}", "save_race" => "{\"value\":2}", _ => throw new ArgumentException(name)
    };
    private static byte[] Bytes(string name) => name == "invalid_utf8"
        ? [.. Encoding.UTF8.GetBytes("{\"value\":\"a"), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("b\",\"other\":0}")]
        : Encoding.UTF8.GetBytes(Input(name));
    private const string Seed = "{\"value\":0}";
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "hostile-text-" + Guid.NewGuid().ToString("N"));
        internal string File => Path.Combine(Root, "data", "test.json");
        internal readonly DataWorkspace Workspace;
        internal EditorWindow<DataDocument> Window => Workspace.Window!;
        internal DocumentSession<DataDocument> Session => Window.Editor.Session;
        internal JsonTextPane Pane => Workspace.TextOf("test.json")!;
        internal Fixture(byte[] bytes)
        {
            Directory.CreateDirectory(Path.Combine(Root, "data")); Directory.CreateDirectory(Path.Combine(Root, "schemas"));
            System.IO.File.WriteAllBytes(File, bytes);
            System.IO.File.WriteAllText(Path.Combine(Root, "schemas", "test.schema.json"), "{}");
            Workspace = new DataWorkspace(DataProject.Open(Path.Combine(Root, "data"), Path.Combine(Root, "schemas"))).AddTextPanel();
        }
        internal bool Open()
        {
            var opened = Workspace.Show("test.json");
            if (opened) { foreach (var p in Window.Editor.Panels) Window.Editor.SetOpen(p.Id, false); Window.Editor.SetOpen(JsonTextPane.PanelId, true); }
            return opened;
        }
        public void Dispose()
        {
            var boundary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(boundary, Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
    private sealed class Observation(string name)
    {
        public string Name { get; } = name;
        public string Phase { get; set; } = "starting";
        public Dictionary<string, object?> Values { get; } = [];
        public List<string> Faults { get; } = [];
    }
    private void Write(Observation o)
    {
        var json = JsonSerializer.Serialize(o); output.WriteLine(json);
        if (Environment.GetEnvironmentVariable("A202_OBSERVATIONS") is { Length: > 0 } dir)
        { Directory.CreateDirectory(dir); System.IO.File.WriteAllText(Path.Combine(dir, o.Name + ".json"), json); }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    private static bool ParseRefusal(string n) => n is "depth_5000" or "empty" or "bom" or "raw_nul" or "trailing_comma" or "comments" or "undo_refused" or "unicode_error";

    [Theory, MemberData(nameof(Cases))]
    public void Hostile_input_preserves_the_draft_document_history_and_disk_and_records_frame_cost(string name)
    {
        var o = new Observation(name); var bytes = Bytes(name); o.Values["inputBytes"] = bytes.Length; o.Values["inputSha256"] = Digest(bytes); Write(o);
        // Disk opening and pasting are different routes: BOM is consumed by the disk reader but a pasted BOM is JSON text.
        using (var disk = new Fixture(bytes))
        {
            o.Phase = "disk-open"; Write(o);
            try
            {
                var opened = disk.Open(); o.Values["diskOpened"] = opened; o.Values["diskSaid"] = disk.Workspace.Said;
                if (name == "lone_surrogate" && disk.Workspace.Said.Contains("fault in the rule", StringComparison.Ordinal)) o.Faults.Add("A malformed string is blamed on project rules even though this fixture has none.");
                if (opened)
                {
                    o.Values["diskTextLength"] = disk.Pane.Text.Length;
                    if (name == "invalid_utf8")
                    {
                        o.Values["decodedValue"] = disk.Session.Current.At("/value")?.GetString();
                        disk.Pane.Edit(disk.Pane.Text.Replace("\"other\":0", "\"other\":1")); Assert.True(disk.Pane.Apply(), disk.Pane.Said);
                        Assert.Equal("1", disk.Session.Current.At("/other")?.GetRawText());
                        var saved = disk.Window.Run("editor.save"); o.Values["invalidUtf8Save"] = saved;
                        o.Values["invalidUtf8SaveSaid"] = disk.Window.Status;
                        o.Values["unchangedStringAfterSave"] = disk.Session.Current.At("/value")?.GetString();
                        if (saved) o.Faults.Add("Invalid UTF-8 opened without a diagnostic; saving an unrelated field replaced invalid bytes in the unchanged string.");
                    }
                }
            }
            catch (Exception e) { o.Values["diskException"] = e.ToString(); o.Faults.Add("Disk open/text escaped: " + e.GetType().Name + ": " + e.Message); }
            var after = System.IO.File.ReadAllBytes(disk.File); o.Values["diskAfterSha256"] = Digest(after); o.Values["diskAfterBytes"] = after.Length; o.Values["diskChangedOnOpen"] = !bytes.SequenceEqual(after);
            if (name != "invalid_utf8" && !bytes.SequenceEqual(after)) o.Faults.Add("Opening/text wrote the disk.");
        }
        var seed = name == "huge_number_change" ? "{\"value\":1e400}" : name == "duplicate_change" ? "{\"value\":1,\"value\":2}" : Seed;
        using var f = new Fixture(Encoding.UTF8.GetBytes(seed)); Assert.True(f.Open(), f.Workspace.Said);
        using var host = new HeadlessHost(1600, 1200) { Drawing = false }; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        // Keep a known applied edit behind the refused text, so Undo has real work to do.
        if (name is "undo_refused" or "save_race") { f.Pane.Edit("{\"value\":1}"); Assert.True(f.Pane.Apply(), f.Pane.Said); }
        var current = f.Session.Current; var revision = f.Session.Revision; var undo = f.Session.History.UndoCount; var journal = f.Session.Journal.Count;
        var beforeDisk = System.IO.File.ReadAllBytes(f.File); var raw = Input(name);
        try
        {
            o.Phase = "draft-render-and-performance"; Write(o);
            f.Pane.Edit(raw); host.Frames(f.Workspace.Draw, 5); f.Pane.Code!.Focus(); host.Frames(f.Workspace.Draw, 3);
            Assert.True(f.Pane.Code.HasFocus); Assert.Equal(raw.Replace("\r\n", "\n").Replace('\r', '\n'), f.Pane.Text);
            if (name == "paste_one_mb")
            {
                // A real widget paste, through this host's private clipboard, not 1 MB of synthetic key events.
                f.Pane.Revert(); host.Frames(f.Workspace.Draw, 2); f.Pane.Code.Focus(); host.Frames(f.Workspace.Draw, 2);
                host.Press(f.Workspace.Draw, ImGuiKey.A, ctrl: true); ImGui.SetClipboardText(raw); host.Press(f.Workspace.Draw, ImGuiKey.V, ctrl: true);
                Assert.Equal(raw, f.Pane.Text); o.Values["realPasteCharacters"] = raw.Length;
            }
            var rendered = f.Pane.Rendered;
            Measure(o, "idle", () => host.Frame(f.Workspace.Draw)); Assert.Equal(rendered, f.Pane.Rendered);
            // Setup/reset and focus are outside the timed callback. Restore the same hostile draft each sample.
            void Reset() { f.Pane.Edit(raw); host.Frames(f.Workspace.Draw, 2); f.Pane.Code.Focus(); host.Frames(f.Workspace.Draw, 2); }
            Measure(o, "key", () => { var length = f.Pane.Text.Length; host.Type(f.Workspace.Draw, " "); Assert.Equal(length + 1, f.Pane.Text.Length); }, Reset);
            Assert.Same(current, f.Session.Current); Assert.Equal(revision, f.Session.Revision); Assert.Equal(journal, f.Session.Journal.Count);
            f.Pane.Edit(raw); o.Values["draftPreservedBeforeApply"] = f.Pane.Text == raw.Replace("\r\n", "\n").Replace('\r', '\n');
            o.Phase = "apply"; Write(o);
            if (name == "save_race") Race(f, host, o, beforeDisk);
            else
            {
                var applied = f.Pane.Apply(); o.Values["applied"] = applied; o.Values["said"] = f.Pane.Said;
                o.Values["errorAt"] = f.Pane.ErrorAt is { } at ? new[] { at.Line, at.Column } : null;
                o.Values["squiggle"] = f.Pane.Squiggle is { } sq ? new[] { sq.Range.Start.Line, sq.Range.Start.Col, sq.Range.End.Line, sq.Range.End.Col } : null;
                if (ParseRefusal(name))
                {
                    if (applied || f.Pane.ErrorAt is null || f.Pane.Squiggle is null) o.Faults.Add("Malformed text was accepted or refused without a line/column and squiggle.");
                    var expectedAt = name switch { "depth_5000" => (1, 65), "trailing_comma" or "undo_refused" => (3, 1), "comments" => (2, 1), "raw_nul" => (1, 12), "unicode_error" => (1, raw.Length), _ => (1, 1) };
                    Assert.Equal(expectedAt, f.Pane.ErrorAt); o.Values["refusalPlaceMatchesInput"] = true;
                }
                if (!applied)
                {
                    Assert.Same(current, f.Session.Current); Assert.Equal(revision, f.Session.Revision); Assert.Equal(undo, f.Session.History.UndoCount); Assert.Equal(journal, f.Session.Journal.Count);
                    Assert.Equal(raw.Replace("\r\n", "\n").Replace('\r', '\n'), f.Pane.Text); o.Values["refusalPreservesAll"] = true;
                }
                else
                {
                    using var expected = JsonDocument.Parse(raw);
                    o.Values["acceptedValueMatchesInput"] = JsonElement.DeepEquals(expected.RootElement, f.Session.Current.Root);
                    if (!JsonElement.DeepEquals(expected.RootElement, f.Session.Current.Root)) o.Faults.Add("Apply said success but the document differs from the submitted JSON value.");
                    var valueText = f.Session.Current.At("/value") is { } value && value.ValueKind != JsonValueKind.Array ? value.GetRawText() : null;
                    o.Values["resultRawValue"] = valueText is { Length: > 500 } ? "<" + valueText.Length + " characters; sha256=" + Digest(Encoding.UTF8.GetBytes(valueText)) + ">" : valueText;
                    if (name == "huge_number_change" && f.Session.Current.At("/value")?.GetRawText() != "2e400") o.Faults.Add("Apply said success but 1e400 remained after the draft changed it to 2e400.");
                    if (!ReferenceEquals(current, f.Session.Current))
                    { Assert.Equal(undo + 1, f.Session.History.UndoCount); Assert.True(f.Session.Undo()); Assert.Same(current, f.Session.Current); o.Values["oneUndoRestores"] = true; }
                }
                if (name == "undo_refused")
                {
                    Assert.True(f.Session.Undo()); Assert.Equal("0", f.Session.Current.At("/value")?.GetRawText());
                    Assert.True(f.Pane.Stale); Assert.False(f.Pane.Apply()); Assert.Contains("changed", f.Pane.Said);
                    Assert.Equal(raw, f.Pane.Text); o.Values["undoAfterRefusalPreservesDraft"] = true;
                }
                host.Frame(f.Workspace.Draw); o.Values["visibleAfterApply"] = Log(host, f.Workspace.Draw);
            }
        }
        catch (Exception e) { o.Values["draftException"] = e.ToString(); o.Faults.Add(o.Phase + " escaped: " + e.GetType().Name + ": " + e.Message); }
        var finalDisk = System.IO.File.ReadAllBytes(f.File); o.Values["draftDiskUnchanged"] = beforeDisk.SequenceEqual(finalDisk);
        if (!beforeDisk.SequenceEqual(finalDisk)) o.Faults.Add("Draft/Apply/refused save wrote the disk.");
        o.Phase = "complete"; Write(o); Assert.True(o.Faults.Count == 0, string.Join("\n", o.Faults));
    }
    private static string Log(HeadlessHost host, Action draw)
    {
        host.Frame(() => { ImGui.LogToClipboard(); draw(); ImGui.LogFinish(); }); return ImGui.GetClipboardText().ToString();
    }
    private static void Race(Fixture f, HeadlessHost host, Observation o, byte[] before)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var ended = new ManualResetEventSlim();
        f.Workspace.Project.SaveCheck = _ => { entered.Set(); try { if (!release.Wait(TimeSpan.FromSeconds(15))) return "test release timed out"; return null; } finally { ended.Set(); } };
        f.Workspace.BackgroundSaves = true;
        try
        {
            Assert.False(f.Window.Run("editor.save")); Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); Assert.True(f.Workspace.SavePending("test.json"));
            Assert.True(f.Pane.Apply(), f.Pane.Said); o.Values["appliedDuringSave"] = true;
        }
        finally { release.Set(); Assert.True(ended.Wait(TimeSpan.FromSeconds(5))); }
        var wait = Stopwatch.StartNew(); while (f.Workspace.SavePending("test.json") && wait.Elapsed < TimeSpan.FromSeconds(5)) { f.Workspace.FinishSaves(); Thread.Yield(); }
        Assert.False(f.Workspace.SavePending("test.json")); Assert.Contains("changed while", f.Workspace.Said);
        Assert.Equal("2", f.Session.Current.At("/value")?.GetRawText()); Assert.True(f.Session.Dirty); Assert.Equal(before, System.IO.File.ReadAllBytes(f.File));
        o.Values["said"] = f.Workspace.Said; o.Values["visibleAfterApply"] = Log(host, f.Workspace.Draw); o.Values["staleSaveRefused"] = true;
    }
    private void Measure(Observation o, string kind, Action action, Action? reset = null)
    {
        for (var i = 0; i < 20; i++) { action(); reset?.Invoke(); }
        var times = new double[150]; var allocations = new long[150];
        for (var i = 0; i < 150; i++)
        { var bytes = GC.GetAllocatedBytesForCurrentThread(); var t = Stopwatch.GetTimestamp(); action(); times[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds; allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes; reset?.Invoke(); }
        var sorted = times.Order().ToArray(); var alloc = allocations.Order().ToArray();
        o.Values[kind] = new { warmups = 20, samples = 150, medianMs = sorted[75], p95Ms = sorted[142], maxMs = sorted[^1], medianBytes = alloc[75], p95Bytes = alloc[142], maxBytes = alloc[^1], times, allocations };
        o.Values["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; o.Values["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        o.Values["processors"] = Environment.ProcessorCount; o.Values["stopwatchFrequency"] = Stopwatch.Frequency; Write(o);
    }
}
