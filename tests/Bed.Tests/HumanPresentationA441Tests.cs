using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class HumanPresentationA441Tests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("a441-human-").FullName;
        public string Disk => Path.Combine(Root, "data", "sample.json");
        public DataWorkspace Workspace { get; }
        public HeadlessHost Host { get; } = new(1400, 1000) { Drawing = false };
        public string? ProposalId;
        public uint AcceptId, RejectId;
        public DocumentSession<DataDocument> Session => Workspace.Project.Find("sample.json")!.Session!;
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "data")); Directory.CreateDirectory(Path.Combine(Root, "schemas"));
            File.WriteAllText(Disk, "{\"value\":1}");
            File.WriteAllText(Path.Combine(Root, "schemas", "sample.schema.json"), "{\"type\":\"object\"}");
            Workspace = new(DataProject.Open(Path.Combine(Root, "data"), Path.Combine(Root, "schemas")));
            Assert.True(Workspace.Show("sample.json"));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        }
        public JsonObject Submit(params string[] checks)
        {
            var basis = Workspace.Proposals.List("sample.json");
            var result = Workspace.Proposals.Submit("sample.json", basis["session"]!.GetValue<string>(), basis["revision"]!.GetValue<long>(),
                [new("set", "/value", JsonSerializer.SerializeToElement(2))], checks);
            Assert.Null(result["error"]); ProposalId = result["id"]!.GetValue<string>(); return result;
        }
        public void Review()
        {
            ImGui.SetNextWindowPos(new float2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(1400, 1000), ImGuiCond.Always);
            if (ImGui.Begin("Suggested changes###a441-review"u8, ImGuiWindowFlags.NoSavedSettings))
            {
                Workspace.ProposalsPanel.Draw();
                if (ProposalId is { } id)
                {
                    ImGui.PushID(id); AcceptId = ImGui.GetID("Accept changes"u8).Value; RejectId = ImGui.GetID("Reject changes"u8).Value; ImGui.PopID();
                }
            }
            ImGui.End();
        }
        public string Text(Action draw)
        {
            Host.Frame(() => { ImGui.LogToClipboard(); draw(); ImGui.LogFinish(); });
            return ImGui.GetClipboardText().ToString() ?? "";
        }
        public void Reach(uint id, Action draw)
        {
            for (var i = 0; i < 12 && ImGui.GetCurrentContext().NavId.Value != id; i++) Host.Press(draw, ImGuiKey.Tab);
            Assert.Equal(id, ImGui.GetCurrentContext().NavId.Value);
        }
        public void Dispose() { Workspace.Dispose(); Host.Dispose(); Directory.Delete(Root, true); }
    }

    [Fact]
    public void Review_hides_machine_identity_but_retains_complete_changes_and_coverage_limits()
    {
        using var f = new Fixture(); var proposal = f.Submit(); f.Host.Frames(f.Review, 4);
        var text = f.Text(f.Review);
        Assert.Equal("Suggested changes", f.Workspace.ProposalsPanel.Title);
        Assert.Contains("Before:", text); Assert.Contains("After:", text);
        // ImGui logging adds layout line endings. Parse each complete displayed block; every JSON value must survive.
        var sections = text.Split(["Before:", "After:", "No checks selected."], StringSplitOptions.None);
        Assert.Equal(4, sections.Length);
        using var displayedBefore = JsonDocument.Parse(sections[1]); using var displayedAfter = JsonDocument.Parse(sections[2]);
        using var before = JsonDocument.Parse(proposal["before"]!.GetValue<string>()); using var after = JsonDocument.Parse(proposal["after"]!.GetValue<string>());
        Assert.True(JsonElement.DeepEquals(before.RootElement, displayedBefore.RootElement));
        Assert.True(JsonElement.DeepEquals(after.RootElement, displayedAfter.RootElement));
        Assert.Contains("No checks selected. Only the file validation rules were checked.", text);
        Assert.Contains("Accept changes", text); Assert.Contains("Reject changes", text);
        foreach (var key in new[] { "id", "digest", "inputHash", "session" }) Assert.DoesNotContain(proposal[key]!.ToString(), text);
        Assert.Contains("Waiting for review", text); Assert.Equal(0, f.Session.History.UndoCount);
        Assert.Equal("{\"value\":1}", File.ReadAllText(f.Disk));
    }

    [Fact]
    public void Keyboard_accept_uses_the_reviewed_suggestion_once_as_an_unsaved_undo_step()
    {
        using var f = new Fixture(); var proposal = f.Submit(); f.Host.Frames(f.Review, 4);
        f.Reach(f.AcceptId, f.Review); f.Host.Press(f.Review, ImGuiKey.Enter);
        Assert.Equal("accepted", f.Workspace.Proposals.Show(f.ProposalId!)["state"]!.GetValue<string>());
        Assert.Equal(2, f.Session.Current.At("/value")!.Value.GetInt32()); Assert.Equal(1, f.Session.History.UndoCount);
        Assert.Equal("{\"value\":1}", File.ReadAllText(f.Disk));
        Assert.Contains("Changes added to the unsaved file.", f.Text(f.Review));
        Assert.DoesNotContain(proposal["digest"]!.ToString(), f.Text(f.Review));
        f.Host.Press(f.Review, ImGuiKey.Enter); Assert.Equal(1, f.Session.History.UndoCount);
        Assert.True(f.Session.Undo()); Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32());
    }

    [Fact]
    public void Stale_suggestion_explains_refusal_and_keyboard_rejection_keeps_the_current_draft()
    {
        using var f = new Fixture(); f.Submit(); Assert.True(f.Session.Execute(new SetValue("/value", JsonSerializer.SerializeToElement(9))));
        f.Host.Frames(f.Review, 4);
        Assert.Contains("This file changed after the suggestion was made. Ask for a new suggestion.", f.Text(f.Review));
        f.Reach(f.RejectId, f.Review); f.Host.Press(f.Review, ImGuiKey.Enter);
        Assert.Equal("rejected", f.Workspace.Proposals.Show(f.ProposalId!)["state"]!.GetValue<string>());
        Assert.Contains("Suggestion rejected.", f.Text(f.Review)); Assert.Equal(9, f.Session.Current.At("/value")!.Value.GetInt32());
        Assert.Equal(1, f.Session.History.UndoCount); Assert.Equal("{\"value\":1}", File.ReadAllText(f.Disk));
    }

    private sealed class Probe : ICheck
    {
        public CheckDefinition Definition { get; } = new("tooling.internal-id", "Check item totals", "Invented no-work check", TimeSpan.FromSeconds(5));
        public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel) => Task.FromResult(new CheckReport(true, [], "Finished"));
    }

    [Fact]
    public void Review_uses_the_registered_check_label_instead_of_its_backend_id()
    {
        using var f = new Fixture(); f.Workspace.Checks.Add(new Probe());
        f.Submit("tooling.internal-id"); f.Host.Frames(f.Review, 4); var text = f.Text(f.Review);
        Assert.Contains("Selected checks: Check item totals", text);
        Assert.DoesNotContain("tooling.internal-id", text);
        Assert.Contains("These results cover only the selected checks.", text);
        Assert.Equal(1, f.Session.Current.At("/value")!.Value.GetInt32()); Assert.Equal(0, f.Session.History.UndoCount);
    }

    [Fact]
    public void Check_panel_uses_plain_not_run_and_unknown_save_coverage_words_without_changing_the_backend_verdict()
    {
        using var f = new Fixture(); f.Workspace.Checks.Add(new Probe());
        void Draw() { ImGui.Begin("Checks###a441-checks"u8); f.Workspace.ChecksPanel.Draw(); ImGui.End(); }
        f.Host.Frames(Draw, 3); var text = f.Text(Draw);
        Assert.Contains("Some checks may not have run when you saved.", text);
        Assert.Contains("Check item totals: Not run yet", text); Assert.DoesNotContain("Save coverage unknown", text);
        Assert.DoesNotContain("tooling.internal-id", text); Assert.DoesNotContain("wall clock", text);
        Assert.Equal("never run", f.Workspace.CheckVerdict("tooling.internal-id")); Assert.Null(f.Workspace.Checks.Last("tooling.internal-id"));
    }

    [Fact]
    public void Unsupported_options_explain_the_panel_limit_without_an_agent_or_command_line_prompt()
    {
        var form = new CheckOptionsForm(new("fixture.options", "Options", "Invented unsupported nested options", TimeSpan.FromSeconds(5),
            OptionsSchema: """{"type":"object","properties":{"nested":{"type":"object"}}}"""));
        Assert.False(form.Supported); Assert.Equal("This check has options that cannot be edited here.", form.Problem);
    }
}
