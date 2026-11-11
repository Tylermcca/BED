using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using Cli = Bed.Agent.Cli.Program;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED numbered findings and projects; BORROWED assertions: A074, public XML and C17.
// No game content, additional package, external process or native window.
[Collection(ImGuiCollection.Name)]
public sealed class CheckPagingAcceptanceTests
{
    private sealed class Trial : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("a074-").FullName;
        public string Data => Path.Combine(Root, "data");
        public string Schemas => Path.Combine(Root, "schemas");
        public string MapFile => Path.Combine(Root, "reference_map.json");
        public DataProject Project { get; }
        public Trial(int brokenLinks = 3)
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Data, "lamps.json"), """{"lamps":[{"id":"amber","name":"Amber lamp"}]}""");
            File.WriteAllText(Path.Combine(Data, "paths.json"), JsonSerializer.Serialize(new { paths = Enumerable.Range(0, brokenLinks).Select(i => new { id = "path" + i, destination = "missing" + i }) }));
            File.WriteAllText(Path.Combine(Schemas, "lamps.schema.json"), """{"type":"object","properties":{"lamps":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}}}}""");
            File.WriteAllText(Path.Combine(Schemas, "paths.schema.json"), """{"type":"object","properties":{"paths":{"type":"array"}}}""");
            File.WriteAllText(MapFile, """{"game":"Invented paper lanterns","labels":[],"references":[{"file":"paths.json","at":"/paths/*/destination","target":{"file":"lamps.json","list":"/lamps","key":"id"},"evidence":"Self-authored A074 missing destinations."}]}""");
            Project = DataProject.Open(Data, Schemas);
        }
        public ReferenceMap Map => ReferenceMap.Read(File.ReadAllText(MapFile));
        public AgentSession Agent(ICheck check)
        {
            var a = new AgentSession(Project, Map); a.Checks.Add(check); return a;
        }
        public void Edit()
        {
            var f = Project.Find("lamps.json")!; Assert.True(f.Open());
            Assert.True(f.Session!.Execute(new SetValue("/lamps/0/name", JsonSerializer.SerializeToElement("Blue lamp"))));
        }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a074-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected A074 cleanup boundary.");
            Directory.Delete(full, true);
        }
    }
    private sealed class Numbered(int count, bool complete = true, bool generation = false) : ICheck
    {
        public int Calls;
        public List<CheckFinding> Items = [];
        public CheckDefinition Definition => new("paging.many", "Lantern audit", "Invented numbered findings.", TimeSpan.FromSeconds(5));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            var n = Interlocked.Increment(ref Calls);
            Items = Enumerable.Range(0, count).Select(i => new CheckFinding(
                Issue.Error((generation ? "run" + n + ":" : "") + "F" + i.ToString("D5")),
                new CheckPlace("lamps.json", "/lamps/0", "/lamps/0/name"), "Give this lamp a unique name.")).ToList();
            return Task.FromResult(new CheckReport(complete, Items, count + " lantern findings."));
        }
    }
    private sealed class Controlled(string mode) : ICheck
    {
        public int Calls;
        public TaskCompletionSource<CheckReport> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CheckDefinition Definition => new("paging.control", "Controlled lantern audit", "Controlled terminal state.", TimeSpan.FromMilliseconds(mode == "timeout" ? 25 : 5000));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Interlocked.Increment(ref Calls);
            if (mode == "fail") throw new InvalidOperationException("Invented paging failure");
            return Release.Task;
        }
    }
    private static int I(JsonNode o, string key) => o[key]!.GetValue<int>();
    private static bool B(JsonNode o, string key) => o[key]!.GetValue<bool>();
    private static string T(JsonNode o, string key) => o[key]!.GetValue<string>();
    private static string[] Messages(JsonNode page) => page["findings"]!.AsArray().Select(f => T(f!, "message")).ToArray();
    private static JsonObject Mcp(McpServer m, JsonObject args, out bool error)
    {
        var reply = m.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 17, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = "check", ["arguments"] = args } }.ToJsonString())!;
        var result = reply["result"]!; error = B(result, "isError");
        return JsonNode.Parse(T(result["content"]![0]!, "text"))!.AsObject();
    }
    private static (int Exit, JsonObject? Result, string Error) Command(Trial t, params string[] tail)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var args = new[] { "--data", t.Data, "--schemas", t.Schemas, "--map", t.MapFile }.Concat(tail).ToArray();
        var exit = Cli.Run(args, TextReader.Null, output, error);
        return (exit, output.ToString().TrimStart().StartsWith('{') ? JsonNode.Parse(output.ToString())!.AsObject() : null, error.ToString());
    }
    private static void Joined(int count, int size)
    {
        using var t = new Trial(); var check = new Numbered(count); var a = t.Agent(check);
        var whole = a.RunCheck("paging.many"); var expected = whole["findings"]!.AsArray();
        var page = a.RunCheck("paging.many", limit: size); var hash = T(page, "inputHash"); var actual = new List<JsonNode>();
        var ordinals = new List<int>(); var seen = new HashSet<int>(); var pages = 0;
        while (true)
        {
            Assert.Equal(hash, T(page, "inputHash")); Assert.Equal(count, I(page, "total")); Assert.False(B(page, "stale"));
            Assert.Equal(count == 0, B(page, "clean"));
            Assert.True(++pages <= count + 1, "Pagination must terminate even for size one.");
            foreach (var item in page["findings"]!.AsArray())
            {
                var f = item!.DeepClone().AsObject(); var n = I(f, "ordinal"); Assert.True(seen.Add(n), "Duplicate ordinal.");
                ordinals.Add(n); f.Remove("ordinal"); actual.Add(f);
            }
            if (page["nextFirst"] is null) break;
            var next = I(page, "nextFirst"); Assert.True(next > I(page, "first"));
            page = a.RunCheck("paging.many", first: next, limit: size);
        }
        Assert.Equal(Enumerable.Range(0, count), ordinals);
        Assert.Equal(expected.Count, actual.Count);
        for (var n = 0; n < actual.Count; n++) Assert.True(JsonNode.DeepEquals(expected[n], actual[n]), "Finding changed at " + n);
        Assert.Equal(2, check.Calls); // one unpaged oracle, one paged run; every later page reads that run.
    }

    // Source: A074 "pages joined back together equal the unpaged list, for page sizes 1, 7, 50 and 500".
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(14)] [InlineData(1203)]
    public void PagesOfOne_ReconstructEveryFieldAndOrdinal(int count) => Joined(count, 1);
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(14)] [InlineData(1203)]
    public void PagesOfSeven_ReconstructEveryFieldAndOrdinal(int count) => Joined(count, 7);
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(100)] [InlineData(1203)]
    public void PagesOfFifty_ReconstructEveryFieldAndOrdinal(int count) => Joined(count, 50);
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(1000)] [InlineData(1203)]
    public void PagesOfFiveHundred_ReconstructEveryFieldAndOrdinal(int count) => Joined(count, 500);
    [Fact] public void FirstReadsCachedRunWithoutExecutingAgain()
    {
        using var t = new Trial(); var c = new Numbered(12, generation: true); var a = t.Agent(c);
        a.RunCheck("paging.many", limit: 1);
        for (var n = 0; n < 12; n++) Assert.Equal("run1:F" + n.ToString("D5"), Messages(a.RunCheck("paging.many", first: n, limit: 1))[0]);
        Assert.Equal(1, c.Calls);
    }
    [Fact] public void NewCallWithoutFirst_ReplacesTheCachedRunAndLaterPagesReadIt()
    {
        using var t = new Trial(); var c = new Numbered(5, generation: true); var a = t.Agent(c);
        a.RunCheck("paging.many", limit: 1); a.RunCheck("paging.many", limit: 1);
        Assert.Equal("run2:F00001", Messages(a.RunCheck("paging.many", first: 1, limit: 1))[0]); Assert.Equal(2, c.Calls);
    }
    // Source: RunCheck XML "it runs the check only if there's no run to read."
    [Fact] public void FirstWithNoCachedRun_StartsOneAsTheDocumentedException()
    {
        using var t = new Trial(); var c = new Numbered(3); var a = t.Agent(c);
        Assert.Equal("F00001", Messages(a.RunCheck("paging.many", first: 1, limit: 1))[0]); Assert.Equal(1, c.Calls);
    }
    [Fact] public void FirstAtTheEnd_IsAnEmptyTerminalPage()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(7)); a.RunCheck("paging.many", limit: 7);
        var p = a.RunCheck("paging.many", first: 7, limit: 7); Assert.Empty(Messages(p)); Assert.False(B(p, "clean")); Assert.Null(p["nextFirst"]); Assert.Equal(7, I(p, "first"));
    }
    [Fact] public void FirstPastTheEnd_IsClampedWithoutInventingFindings()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(7)); a.RunCheck("paging.many");
        var p = a.RunCheck("paging.many", first: 999, limit: 1); Assert.Empty(Messages(p)); Assert.False(B(p, "clean")); Assert.Equal(7, I(p, "first")); Assert.Null(p["nextFirst"]);
    }
    [Fact] public void MinimumLimitReturnsExactlyOneAndACorrectContinuation()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(2)); var p = a.RunCheck("paging.many", limit: 1);
        Assert.Single(Messages(p)); Assert.Equal(1, I(p, "nextFirst")); Assert.Equal(0, I(p["findings"]![0]!, "ordinal"));
    }
    [Fact] public void MaximumLimitReturnsFiveHundredNotAnUnboundedList()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(501)); var p = a.RunCheck("paging.many", limit: 500);
        Assert.Equal(500, AgentSession.MostFindings); Assert.Equal(500, Messages(p).Length); Assert.Equal(500, I(p, "nextFirst"));
    }
    [Fact] public void NegativeFirstIsRefusedWithoutStartingACheck()
    {
        using var t = new Trial(); var c = new Numbered(2); var p = t.Agent(c).RunCheck("paging.many", first: -1);
        Assert.NotNull(p["error"]); Assert.Contains("0", T(p, "next")); Assert.Equal(0, c.Calls);
    }
    [Fact] public void NegativeLimitIsRefusedWithoutStartingACheck()
    {
        using var t = new Trial(); var c = new Numbered(2); Assert.NotNull(t.Agent(c).RunCheck("paging.many", limit: -1)["error"]); Assert.Equal(0, c.Calls);
    }
    [Fact] public void ZeroLimitIsRefusedWithoutStartingACheck()
    {
        using var t = new Trial(); var c = new Numbered(2); Assert.NotNull(t.Agent(c).RunCheck("paging.many", limit: 0)["error"]); Assert.Equal(0, c.Calls);
    }
    [Fact] public void AboveMaximumLimitIsRefusedRatherThanSilentlyTruncated()
    {
        using var t = new Trial(); var c = new Numbered(2); Assert.NotNull(t.Agent(c).RunCheck("paging.many", limit: 501)["error"]); Assert.Equal(0, c.Calls);
    }
    [Fact] public void MaximumFirstDoesNotOverflowOrdinalArithmetic()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(2)); a.RunCheck("paging.many");
        var p = a.RunCheck("paging.many", first: int.MaxValue, limit: 500); Assert.Equal(2, I(p, "first")); Assert.Empty(Messages(p));
    }
    [Fact] public void McpPagesPreserveTheSameRunAndAllFindingFields()
    {
        using var t = new Trial(); var c = new Numbered(15); var a = t.Agent(c); var m = new McpServer(a);
        var p = Mcp(m, new() { ["id"] = "paging.many", ["limit"] = 7 }, out var err); Assert.False(err);
        var next = Mcp(m, new() { ["id"] = "paging.many", ["first"] = 7, ["limit"] = 7 }, out err);
        Assert.False(err); Assert.Equal(7, I(next["findings"]![0]!, "ordinal")); Assert.Equal(14, I(next, "nextFirst"));
        Assert.Equal(T(p, "inputHash"), T(next, "inputHash")); Assert.Equal(1, c.Calls);
        Assert.Equal("/lamps/0/name", T(next["findings"]![0]!, "at"));
    }
    [Theory] [InlineData("first", "\"seven\"")] [InlineData("limit", "\"seven\"")]
    [InlineData("first", "1.25")] [InlineData("limit", "true")] [InlineData("first", "null")] [InlineData("limit", "[]")]
    public void McpWrongArgumentTypesAreReturnedAsToolErrorsWithoutExecution(string key, string json)
    {
        using var t = new Trial(); var c = new Numbered(2); var a = t.Agent(c);
        var args = new JsonObject { ["id"] = "paging.many", [key] = JsonNode.Parse(json) };
        var p = Mcp(new McpServer(a), args, out var err); Assert.True(err); Assert.NotNull(p["error"]); Assert.Equal(0, c.Calls);
    }
    [Theory] [InlineData("first", -1)] [InlineData("limit", 0)] [InlineData("limit", 501)]
    public void McpNumericRangeErrorsDoNotRunTheCheck(string key, int value)
    {
        using var t = new Trial(); var c = new Numbered(2); var a = t.Agent(c);
        var p = Mcp(new McpServer(a), new() { ["id"] = "paging.many", [key] = value }, out var err);
        Assert.True(err); Assert.NotNull(p["error"]); Assert.Equal(0, c.Calls);
    }
    // Source: A074 "a negative or zero number; a non-number through MCP and the command line".
    [Theory] [InlineData("-1")] [InlineData("0")] [InlineData("seven")] [InlineData("1.5")] [InlineData("2147483648")]
    public void CliInvalidLimitIsAnArgumentErrorNotAnIgnoredFlag(string bad)
    {
        using var t = new Trial(); var p = Command(t, "check", "links.all", "--limit", bad);
        Assert.Equal(2, p.Exit); Assert.Contains("usage:", p.Error); Assert.Null(p.Result);
    }
    [Theory] [InlineData("-1")] [InlineData("seven")] [InlineData("1.5")] [InlineData("2147483648")]
    public void CliInvalidFirstIsAnArgumentErrorNotAnIgnoredFlag(string bad)
    {
        using var t = new Trial(); var p = Command(t, "check", "links.all", "--first", bad);
        Assert.Equal(2, p.Exit); Assert.Contains("usage:", p.Error); Assert.Null(p.Result);
    }
    [Fact] public void CliValidLimitsAndOffsetsApplyToTheRealLinkCheck()
    {
        using var t = new Trial(19); var p = Command(t, "check", "links.all", "--first", "7", "--limit", "7");
        Assert.Equal(1, p.Exit); Assert.Empty(p.Error); Assert.Equal(7, p.Result!["findings"]!.AsArray().Count);
        Assert.Equal(7, I(p.Result, "first")); Assert.Equal(14, I(p.Result, "nextFirst")); Assert.Equal(19, I(p.Result, "total"));
    }
    [Fact] public void PlainCallRetainsLegacyShapeWithoutPagingFields()
    {
        using var t = new Trial(); var p = t.Agent(new Numbered(55)).RunCheck("paging.many");
        Assert.Equal(55, Messages(p).Length);
        foreach (var k in new[] { "first", "total", "stale", "nextFirst" }) Assert.False(p.ContainsKey(k), k);
        Assert.All(p["findings"]!.AsArray(), f => Assert.Null(f!["ordinal"]));
    }
    private static void Stale(string how)
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(10)); a.RunCheck("paging.many", limit: 1);
        if (how == "disk") File.WriteAllText(Path.Combine(t.Data, "lamps.json"), """{"lamps":[{"id":"amber","name":"Changed on disk much longer"}]}""");
        else { t.Edit(); if (how == "save") Assert.Empty(t.Project.SaveAll()); }
        var p = a.RunCheck("paging.many", first: 1, limit: 1);
        Assert.True(B(p, "stale")); Assert.False(B(p, "clean")); Assert.Contains("stale", T(p, "verdict"));
        Assert.True(T(p, "next").Contains("changed", StringComparison.OrdinalIgnoreCase) || T(p, "next").Contains("stale", StringComparison.OrdinalIgnoreCase),
            "The continuation hint must not pass an old page off as current: " + T(p, "next"));
    }
    // Source: A074 "stale, verdict, clean and next all agree".
    [Fact] public void UnsavedEditMakesEveryPagedStatusAndHintStale() => Stale("draft");
    [Fact] public void DiskChangeMakesEveryPagedStatusAndHintStale() => Stale("disk");
    [Fact] public void SavingAnEditDoesNotMakeTheOldPagedResultCurrent() => Stale("save");
    [Fact] public void PreviouslyCleanEmptyRunCannotCertifyAnEditedProject()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(0)); Assert.True(B(a.RunCheck("paging.many", limit: 1), "clean"));
        t.Edit(); var p = a.RunCheck("paging.many", first: 0, limit: 1);
        Assert.False(B(p, "clean")); Assert.True(B(p, "stale")); Assert.Contains("stale", T(p, "verdict")); Assert.Contains("changed", T(p, "next"));
    }
    private static async Task Terminal(string mode, bool cancel = false)
    {
        using var t = new Trial(); var c = new Controlled(mode); var a = t.Agent(c);
        var run = a.Checks.Start("paging.control", t.Project, out _)!;
        if (cancel) run.Cancel();
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(cancel ? CheckState.Cancelled : mode == "timeout" ? CheckState.TimedOut : CheckState.Failed, run.State);
        var p = a.RunCheck("paging.control", first: 0, limit: 7);
        Assert.False(B(p, "clean")); Assert.Null(p["findings"]); Assert.Null(p["nextFirst"]);
        Assert.Equal(run.State.ToString(), T(p, "state")); Assert.NotNull(p["problem"]);
        c.Release.TrySetResult(new CheckReport(true, [], "Late response"));
    }
    [Fact] public Task FailedRunPagesCannotInventFindingsOrBecomeClean() => Terminal("fail");
    [Fact] public Task TimedOutRunPagesCannotInventFindingsOrBecomeClean() => Terminal("timeout");
    [Fact] public Task CancelledRunPagesCannotInventFindingsOrBecomeClean() => Terminal("wait", cancel: true);
    [Fact] public void IncompleteRunPagesRetainFindingsButNeverCertify()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(8, complete: false)); var p = a.RunCheck("paging.many", limit: 7);
        Assert.False(B(p, "complete")); Assert.False(B(p, "clean")); Assert.Contains("incomplete", T(p, "verdict"));
        var last = a.RunCheck("paging.many", first: 7, limit: 7); Assert.Single(Messages(last)); Assert.False(B(last, "clean"));
    }
    [Fact] public async Task ReadingWhileRunIsActiveRefusesInsteadOfRerunningOrReturningPartialPages()
    {
        using var t = new Trial(); var c = new Controlled("wait"); var a = t.Agent(c);
        var run = a.Checks.Start("paging.control", t.Project, out _)!;
        try
        {
            var p = a.RunCheck("paging.control", first: 0, limit: 7); Assert.NotNull(p["error"]); Assert.Null(p["findings"]);
        }
        finally { c.Release.TrySetResult(new CheckReport(true, [], "Done")); await run.Ended.WaitAsync(TimeSpan.FromSeconds(8)); }
        Assert.Equal(1, c.Calls);
    }
    // Source: C17 "the terminal finding sequence is immutable".
    [Fact] public void TerminalFindingSequenceCannotBeReorderedByTheChecksRetainedList()
    {
        using var t = new Trial(); var c = new Numbered(8); var a = t.Agent(c); a.RunCheck("paging.many", limit: 1);
        c.Items.Reverse();
        Assert.Equal("F00001", Messages(a.RunCheck("paging.many", first: 1, limit: 1))[0]);
        Assert.Equal(1, c.Calls);
    }
    private static async Task<DataWorkspace> Workspace(Trial t, Numbered c)
    {
        var w = new DataWorkspace(t.Project); w.Checks.Add(c); Assert.True(w.Show("lamps.json"));
        Assert.True(w.RunCheck("paging.many")); await w.Checks.Last("paging.many")!.Ended.WaitAsync(TimeSpan.FromSeconds(8));
        return w;
    }
    private sealed class Screen : IDisposable
    {
        public HeadlessHost Host { get; } = new(1100, 3000);
        private readonly DataWorkspace w;
        public uint More;
        public Screen(DataWorkspace w, EditorThemeAppearance? appearance = null)
        {
            this.w = w; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            if (appearance is { } theme) EditorTheme.Apply(theme);
            Host.Frames(Draw, 3);
        }
        public void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(1100, 3000));
            ImGui.Begin("A074 report"); w.ChecksPanel.Draw();
            ImGui.PushID("paging.many");
            More = ImGui.Internal.FindWindowByName("A074 report").GetID("Show 100 more").Value;
            ImGui.PopID(); ImGui.End();
        }
        public string Text()
        {
            Host.Frame(() => { ImGui.LogToClipboard(); Draw(); ImGui.LogFinish(); });
            return ImGui.GetClipboardText().ToString() ?? "";
        }
        public void KeyboardMore()
        {
            Host.Drawing = false; // HeadlessHost documents this for long input scripts; UI logic is unchanged.
            for (var n = 0; n < 240; n++)
            {
                if (ImGui.GetCurrentContext().NavId.Value == More) { Host.Press(Draw, ImGuiKey.Enter); return; }
                Host.Press(Draw, ImGuiKey.Tab);
            }
            Assert.Fail("Show more was not reachable through actual Tab navigation.");
        }
        public void MouseMore()
        {
            // Measure actual font ink in a separate owned headless frame, then find that ink in the real button.
            float2 lo = default, hi = default;
            void Probe()
            {
                ImGui.SetNextWindowPos(new float2(0, 0)); ImGui.SetNextWindowSize(new float2(300, 80));
                ImGui.Begin("A074 font probe"); ImGui.Text("Show 100 more"); lo = ImGui.GetItemRectMin(); hi = ImGui.GetItemRectMax(); ImGui.End();
            }
            Host.Drawing = true; Host.Frames(Probe, 3);
            var width = (int)(hi.X - lo.X); var height = (int)(hi.Y - lo.Y);
            // Classify the same glyph coverage in either palette, relative to its actual surface.
            // The full boolean mask (including non-ink pixels) still has to match exactly.
            var colours = ImGui.GetStyle().Colors;
            var foreground = colours[(int)ImGuiCol.Text];
            var background = colours[(int)ImGuiCol.WindowBg];
            bool Ink(uint p)
            {
                var r = (p & 255) / 255f - background.X;
                var g = ((p >> 8) & 255) / 255f - background.Y;
                var b = ((p >> 16) & 255) / 255f - background.Z;
                var dr = foreground.X - background.X;
                var dg = foreground.Y - background.Y;
                var db = foreground.Z - background.Z;
                return (r * dr + g * dg + b * db) / (dr * dr + dg * dg + db * db) > 0.7f;
            }
            var mask = new List<(int X, int Y, bool Ink)>();
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                mask.Add((x, y, Ink(Host.Pixels[((int)lo.Y + y) * Host.Width + (int)lo.X + x])));
            Assert.Contains(mask, p => p.Ink);
            Assert.Contains(mask, p => !p.Ink);
            var first = mask.First(p => p.Ink);
            Host.Frames(Draw, 3);
            background = colours[(int)ImGuiCol.Button];
            (float X, float Y)? point = null;
            for (var y = 0; y < Host.Height - height && point is null; y++)
                for (var x = 0; x < Host.Width - width; x++)
                {
                    if (!Ink(Host.Pixels[(y + first.Y) * Host.Width + x + first.X])) continue;
                    if (mask.All(p => Ink(Host.Pixels[(y + p.Y) * Host.Width + x + p.X]) == p.Ink))
                    { point = (x + width / 2f, y + height / 2f); break; }
                }
            Host.Drawing = false; Assert.NotNull(point); Host.Click(Draw, point.Value.X, point.Value.Y); Host.Frames(Draw);
        }
        public void Dispose() => Host.Dispose();
    }
    // Source: FindingsShown XML "the panel always says how many there are".
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(100)] [InlineData(101)]
    public async Task PanelAlwaysExplainsShownAndTotalCounts(int total)
    {
        using var t = new Trial(); var w = await Workspace(t, new Numbered(total)); using var ui = new Screen(w);
        Assert.Contains("Showing " + Math.Min(100, total) + " of " + total, ui.Text());
    }
    [Theory]
    [InlineData(EditorThemeAppearance.Light)]
    [InlineData(EditorThemeAppearance.Dark)]
    public async Task PanelShowMoreRespondsToAnActualMouseClick(EditorThemeAppearance appearance)
    {
        using var t = new Trial(); var w = await Workspace(t, new Numbered(250)); using var ui = new Screen(w, appearance);
        Assert.Equal(100, w.FindingsShown("paging.many")); ui.MouseMore(); Assert.Equal(200, w.FindingsShown("paging.many"));
        Assert.Contains("Showing 200 of 250", ui.Text());
    }
    [Fact] public async Task PanelShowMoreIsReachableAndActivatedByRealKeyboardEvents()
    {
        using var t = new Trial(); var w = await Workspace(t, new Numbered(250)); using var ui = new Screen(w);
        ui.KeyboardMore(); Assert.Equal(200, w.FindingsShown("paging.many"));
    }
    [Theory]
    [InlineData(EditorThemeAppearance.Light)]
    [InlineData(EditorThemeAppearance.Dark)]
    public async Task PanelNewRunStartsBackAtItsFirstHundred(EditorThemeAppearance appearance)
    {
        using var t = new Trial(); var w = await Workspace(t, new Numbered(250)); using var ui = new Screen(w, appearance);
        // Isolate reset from the separately tested keyboard path, while still using actual UI input.
        ui.MouseMore(); Assert.Equal(200, w.FindingsShown("paging.many"));
        Assert.True(w.RunCheck("paging.many")); await w.Checks.Last("paging.many")!.Ended.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(100, w.FindingsShown("paging.many")); Assert.Contains("Showing 100 of 250", ui.Text());
    }
    [Fact] public async Task PanelFinalExpandedPageStillSaysHowMuchWasShown()
    {
        using var t = new Trial(); var w = await Workspace(t, new Numbered(101)); w.ShowMoreFindings("paging.many"); using var ui = new Screen(w);
        Assert.Equal(101, w.FindingsShown("paging.many")); Assert.Contains("Showing 101 of 101", ui.Text());
    }
    [Fact] public void McpNeitherOptionRetainsTheUnpagedReplyShape()
    {
        using var t = new Trial(); var a = t.Agent(new Numbered(60));
        var p = Mcp(new McpServer(a), new() { ["id"] = "paging.many" }, out var err); Assert.False(err);
        Assert.Equal(60, Messages(p).Length); Assert.Null(p["total"]); Assert.Null(p["first"]); Assert.Null(p["nextFirst"]);
    }
}
