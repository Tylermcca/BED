using System.Text.Json;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// Source-prepared tests only; no C#/native/headless execution receipt is claimed.
[Collection(ImGuiCollection.Name)]
public sealed class CheckOptionsFormA375Tests
{
    private const string Schema = """
        {"type":"object","required":["title","mode"],"properties":{
          "count":{"type":"integer","default":12,"minimum":1},
          "mode":{"type":"string","enum":["narrow","wide"],"default":"narrow"},
          "title":{"type":"string"},"ratio":{"type":"number","default":0.5},
          "flag":{"type":"boolean","default":false}},"additionalProperties":false}
        """;

    private sealed class Probe(string schema) : ICheck
    {
        public int Calls;
        public CheckSnapshot? Seen;
        public CheckDefinition Definition { get; } = new("acme.audit", "Acme audit", "Reviews the chosen ledger scope.",
            TimeSpan.FromSeconds(5), schema);
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel)
        {
            Calls++;
            Seen = snapshot;
            return Task.FromResult(new CheckReport(true,
                [new CheckFinding(Issue.Warning("Review this row."), new CheckPlace("ledger.json", At: "/items/0"), "Review its title.")], "Reviewed."));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("acme-options-").FullName;
        public DataWorkspace Workspace { get; }
        public Probe Check { get; }
        public Fixture(string schema = Schema)
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
            File.WriteAllText(Path.Combine(data, "ledger.json"), "{\"items\":[{\"id\":\"one\"}]}");
            File.WriteAllText(Path.Combine(schemas, "ledger.schema.json"), "{}");
            Workspace = new DataWorkspace(DataProject.Open(data, schemas));
            Check = new Probe(schema);
            Workspace.Checks.Add(Check);
        }
        public CheckOptionsForm Form => Workspace.OptionsForCheck(Check.Definition.Id)!;
        public void Dispose() { Workspace.Dispose(); Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Required_missing_empty_and_whitespace_fields_block_Run_without_a_snapshot()
    {
        using var f = new Fixture();
        Assert.False(f.Workspace.CanRunCheck("acme.audit", out var why));
        Assert.Contains("title", why);
        Assert.False(f.Workspace.RunCheck("acme.audit"));
        Assert.True(f.Form.Inspector!.Set("title", ""));
        Assert.False(f.Workspace.CanRunCheck("acme.audit", out _));
        Assert.True(f.Form.Inspector.Set("title", "  "));
        Assert.False(f.Workspace.RunCheck("acme.audit"));
        Assert.Equal(0, f.Check.Calls);
        Assert.Null(f.Workspace.Checks.Last("acme.audit"));
        Assert.True(f.Form.Inspector.Set("title", "Review"));
        Assert.True(f.Workspace.CanRunCheck("acme.audit", out _));
    }

    [Fact]
    public async Task Enum_and_all_scalar_choices_reach_the_real_snapshot_and_hash()
    {
        using var f = new Fixture();
        Assert.True(f.Form.Inspector!.Set("title", "Review"));
        Assert.True(f.Form.Inspector.Set("mode", "wide"));
        Assert.True(f.Form.Inspector.Set("count", 3L));
        Assert.True(f.Form.Inspector.Set("ratio", 0.25));
        Assert.True(f.Form.Inspector.Set("flag", true));
        var baseline = CheckSnapshot.Capture(f.Workspace.Project, options: "{\"title\":\"Review\",\"mode\":\"narrow\",\"count\":3,\"ratio\":0.25,\"flag\":true}");
        Assert.True(f.Workspace.RunCheck("acme.audit"));
        var run = f.Workspace.Checks.Last("acme.audit")!;
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(CheckState.Completed, run.State);
        Assert.Equal("wide", f.Check.Seen!.Options.GetProperty("mode").GetString());
        Assert.Equal("Review", run.Snapshot.Options.GetProperty("title").GetString());
        Assert.Equal(3, run.Snapshot.Options.GetProperty("count").GetInt32());
        Assert.Equal(0.25, run.Snapshot.Options.GetProperty("ratio").GetDouble());
        Assert.True(run.Snapshot.Options.GetProperty("flag").GetBoolean());
        Assert.NotEqual(baseline.InputHash, run.Snapshot.InputHash);
        Assert.Equal(1, f.Check.Calls);
    }

    [Fact]
    public async Task Changed_options_mark_the_panel_stale_and_block_old_finding_navigation()
    {
        using var f = new Fixture();
        Assert.True(f.Form.Inspector!.Set("title", "Review"));
        Assert.True(f.Workspace.RunCheck("acme.audit"));
        var run = f.Workspace.Checks.Last("acme.audit")!;
        await run.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain("(stale)", f.Workspace.CheckVerdict("acme.audit"));
        var finding = Assert.Single(run.Report!.Findings);
        Assert.True(f.Form.Inspector.Set("mode", "wide"));
        Assert.Contains("(stale)", f.Workspace.CheckVerdict("acme.audit"));
        Assert.False(f.Workspace.Reveal(finding));
        Assert.Equal("narrow", run.Snapshot.Options.GetProperty("mode").GetString());
        Assert.Equal(1, f.Check.Calls);
        Assert.True(f.Form.Inspector.Set("mode", "narrow"));
        Assert.DoesNotContain("(stale)", f.Workspace.CheckVerdict("acme.audit"));
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"properties\":{\"extra\":{\"type\":\"object\"}}}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"extra\":{\"type\":\"array\"}}}")]
    [InlineData("{\"allOf\":[{\"type\":\"object\"}]}")]
    public void Unsupported_schema_stays_refused_without_running_hidden_empty_options(string schema)
    {
        using var f = new Fixture(schema);
        Assert.False(f.Form.Supported);
        Assert.Equal(CheckOptionsForm.UnsupportedReason, f.Form.Problem);
        Assert.False(f.Workspace.CanRunCheck("acme.audit", out var why));
        Assert.Equal(CheckOptionsForm.UnsupportedReason, why);
        Assert.False(f.Workspace.RunCheck("acme.audit"));
        Assert.Equal(0, f.Check.Calls);
        Assert.Null(f.Workspace.Checks.Last("acme.audit"));
    }

    [Fact]
    public void A_visible_default_is_initialized_once_and_never_reappears_after_emptying()
    {
        using var f = new Fixture("{\"type\":\"object\",\"required\":[\"title\"],\"properties\":{\"title\":{\"type\":\"string\",\"default\":\"Review\"}},\"additionalProperties\":false}");
        var form = f.Form;
        Assert.Equal("Review", form.Options.GetProperty("title").GetString());
        Assert.True(form.Inspector!.Set("title", ""));
        Assert.Same(form, f.Workspace.OptionsForCheck("acme.audit"));
        Assert.Equal("", form.Options.GetProperty("title").GetString());
        Assert.False(f.Workspace.RunCheck("acme.audit"));
        Assert.Equal(0, f.Check.Calls);
    }

    [Fact]
    public async Task An_emptied_keyboard_numeric_draft_blocks_the_hidden_committed_default()
    {
        using var f = new Fixture("{\"type\":\"object\",\"required\":[\"count\"],\"properties\":{\"count\":{\"type\":\"integer\",\"default\":12}},\"additionalProperties\":false}");
        Assert.True(f.Workspace.RunCheck("acme.audit"));
        var previousRun = f.Workspace.Checks.Last("acme.audit")!;
        await previousRun.Ended.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(CheckState.Completed, previousRun.State);
        Assert.False(previousRun.IsStale(f.Workspace.Project));
        using var host = new HeadlessHost(800, 600) { Drawing = false };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        void Draw()
        {
            ImGui.SetNextWindowSize(new float2(700, 500));
            ImGui.Begin("Check options");
            f.Form.Draw();
            ImGui.End();
        }
        host.Frames(Draw, 3);
        host.Press(Draw, ImGuiKey.Tab); // the only editable field, ordinary inspector navigation.
        host.Press(Draw, ImGuiKey.End);
        host.Press(Draw, ImGuiKey.Backspace);
        host.Press(Draw, ImGuiKey.Backspace);
        host.Press(Draw, ImGuiKey.Enter);
        host.Frames(Draw, 2);
        Assert.True(f.Form.HasPendingEdits); // positive control: real typed text was retained as refused.
        Assert.Equal(12, f.Form.Options.GetProperty("count").GetInt32());
        Assert.False(f.Workspace.CanRunCheck("acme.audit", out _));
        Assert.False(f.Workspace.RunCheck("acme.audit"));
        Assert.Equal(1, f.Check.Calls); // the prior valid run is never replaced by the emptied draft.
        Assert.Same(previousRun, f.Workspace.Checks.Last("acme.audit"));
        Assert.Contains("(stale)", f.Workspace.CheckVerdict("acme.audit"));
        Assert.Equal(12, previousRun.Snapshot.Options.GetProperty("count").GetInt32());
        host.Frames(Draw, 3);
        Assert.True(f.Form.HasPendingEdits);
        Assert.False(f.Workspace.CanRunCheck("acme.audit", out _));
    }
}
