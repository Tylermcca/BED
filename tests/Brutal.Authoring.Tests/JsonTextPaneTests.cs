using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.Text;
using Brutal.ImGuiApi;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

/// <summary>The Text panel (ImCode, vendored): a data file's JSON as a draft that only Apply turns into one validated,
/// undoable edit; refusals leave the document alone; outside changes never overwrite a draft; an unchanged frame writes
/// nothing out again. INVENTED neutral data, headless throughout.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class JsonTextPaneTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jsontext-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");

    public JsonTextPaneTests()
    {
        var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(schemas);
        File.WriteAllText(Path.Combine(schemas, "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"price":{"type":"number","minimum":0}}}}}}""");
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{\n  \"wares\": [\n    { \"name\": \"Rope\", \"price\": 2 },\n    { \"name\": \"Tar\", \"price\": 1.50 }\n  ]\n}\n");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private (DataWorkspace Workspace, EditorWindow<DataDocument> Window, JsonTextPane Pane) Open(bool readOnly = false)
    {
        var w = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas"), readOnly)).AddTextPanel();
        Assert.True(w.Show("wares.json"), w.Said);
        return (w, w.Window!, w.TextOf("wares.json")!);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string? Name(EditorWindow<DataDocument> w, int i) => w.Editor.Session.Current.At($"/wares/{i}/name")?.GetString();

    [Fact]
    public void The_text_panel_starts_closed_and_shows_the_documents_json_unsaved_edits_included()
    {
        var (w, window, pane) = Open();
        Assert.Contains(window.Editor.Panels, p => p.Id == JsonTextPane.PanelId && p.Title == "Text");
        Assert.False(window.Editor.IsOpen(JsonTextPane.PanelId));
        Assert.True(window.Run(EditorWindow<DataDocument>.PanelAction + JsonTextPane.PanelId));
        Assert.True(window.Editor.IsOpen(JsonTextPane.PanelId));

        Assert.True(window.Editor.Session.Execute(new SetValue("/wares/0/price", Json("5"))));
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(w.Draw, 4);
        Assert.Equal(window.Editor.Session.TextToSave, pane.Text);   // the file as it would be written, edit included
        Assert.Contains("{ \"name\": \"Rope\", \"price\": 5 }", pane.Text);
        Assert.Contains("\"price\": 1.50", pane.Text);
        Assert.False(pane.HasDraft);
        if (Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS") is { Length: > 0 } d)
            host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "40_text_panel.png"));
    }

    [Fact]
    public void Valid_json_applied_changes_the_document_in_one_undo_step_and_keeps_the_rest_of_the_file()
    {
        var (_, window, pane) = Open();
        var session = window.Editor.Session;
        var journal = session.Journal.Count;
        Assert.True(pane.Edit(pane.Text.Replace("\"Rope\"", "\"Cord\"").Replace("\"Tar\"", "\"Pitch\"")));
        Assert.True(pane.HasDraft);
        Assert.Equal("Rope", Name(window, 0));   // a draft changes nothing yet
        Assert.Equal(0, session.History.UndoCount);

        Assert.True(pane.Apply(), pane.Said);
        Assert.Equal("Cord", Name(window, 0));
        Assert.Equal("Pitch", Name(window, 1));
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(journal + 1, session.Journal.Count);
        Assert.False(pane.HasDraft);
        // Whole-file replacement keeps the file's own style where nothing changed (the number's spelling, one-line items).
        Assert.Contains("{ \"name\": \"Pitch\", \"price\": 1.50 }", session.TextToSave);

        // Undo restores the document in one step, and the panel (no draft) follows it.
        Assert.True(window.Run("editor.undo"));
        Assert.Equal("Rope", Name(window, 0));
        Assert.Equal("Tar", Name(window, 1));
        Assert.Equal(0, session.History.UndoCount);
        Assert.Contains("\"Rope\"", pane.Text);
        Assert.False(session.Dirty);
    }

    [Fact]
    public void A_pasted_lone_surrogate_is_refused_at_its_value_and_the_document_is_unchanged()
    {
        // A202: valid JSON whose string isn't text was refused at the edit's own payload ($.Value), not at the place in the text.
        var (_, window, pane) = Open();
        var before = window.Editor.Session.Current;
        Assert.True(pane.Edit(pane.Text.Replace("\"Rope\"", "\"\\ud800\"")));
        Assert.False(pane.Apply());
        Assert.StartsWith("Not applied: $.wares[0].name holds a \\u escape", pane.Said);
        Assert.Same(before, window.Editor.Session.Current);
        Assert.True(pane.HasDraft);   // the person's text is kept to fix
    }

    [Fact]
    public void Invalid_json_is_refused_with_its_line_and_column_and_the_document_is_unchanged()
    {
        var (_, window, pane) = Open();
        var before = window.Editor.Session.Current;
        var broken = "{\n  \"wares\": [\n    { \"name\": \"Rope\" \"price\": 2 }\n  ]\n}\n";   // a comma missing on line 3
        Assert.True(pane.Edit(broken));
        Assert.False(pane.Apply());
        Assert.Equal((3, 22), pane.ErrorAt);   // where "price" starts, unexpected
        Assert.Contains("line 3, column 22", pane.Said);
        Assert.Equal(2, pane.Squiggle!.Range.Start.Line);   // the squiggle, on line 3 (0-based 2)
        Assert.Same(before, window.Editor.Session.Current);
        Assert.Equal(0, window.Editor.Session.History.UndoCount);
        Assert.True(pane.HasDraft);   // the draft stays, to be fixed
        Assert.Equal(broken, pane.Text);
    }

    [Fact]
    public void Schema_invalid_json_is_refused_by_the_validator_and_the_document_is_unchanged()
    {
        var (_, window, pane) = Open();
        var before = window.Editor.Session.Current;
        Assert.True(pane.Edit(pane.Text.Replace("\"price\": 2", "\"price\": -3")));
        Assert.False(pane.Apply());
        Assert.StartsWith("Not applied: ", pane.Said);
        Assert.Contains("unchanged", pane.Said);
        Assert.Null(pane.ErrorAt);   // it's JSON; the schema refused it
        Assert.Same(before, window.Editor.Session.Current);
        Assert.Equal(0, window.Editor.Session.History.UndoCount);
        Assert.True(pane.HasDraft);
    }

    [Fact]
    public void Revert_throws_the_draft_away()
    {
        var (_, window, pane) = Open();
        var original = pane.Text;
        Assert.True(pane.Edit("{}"));
        Assert.True(pane.HasDraft);
        pane.Revert();
        Assert.False(pane.HasDraft);
        Assert.Equal(original, pane.Text);
        Assert.Equal(0, window.Editor.Session.History.UndoCount);
    }

    [Fact]
    public void An_outside_edit_refreshes_the_text_without_a_draft_and_never_overwrites_one()
    {
        var (_, window, pane) = Open();
        _ = pane.Text;
        Assert.True(window.Editor.Session.Execute(new SetValue("/wares/0/name", Json("\"Line\""))));
        Assert.Contains("\"Line\"", pane.Text);   // no draft: it follows
        Assert.False(pane.Stale);

        var draft = pane.Text.Replace("\"Tar\"", "\"Mine\"");
        Assert.True(pane.Edit(draft));
        Assert.True(window.Run("editor.undo"));   // the document changes under the draft
        Assert.True(pane.Stale);
        Assert.Equal(draft, pane.Text);           // kept, not overwritten
        Assert.False(pane.Apply());
        Assert.Contains("changed since this draft began", pane.Said);
        Assert.Equal("Rope", Name(window, 0));

        pane.Revert();
        Assert.False(pane.Stale);
        Assert.Contains("\"Rope\"", pane.Text);
        Assert.Contains("\"Tar\"", pane.Text);
    }

    [Fact]
    public void A_read_only_file_shows_its_text_but_cant_apply()
    {
        var (_, window, pane) = Open(readOnly: true);
        Assert.Contains("\"Rope\"", pane.Text);
        Assert.True(pane.ReadOnly);
        Assert.False(pane.Edit("{}"));
        Assert.False(pane.Apply());
        Assert.Contains("read-only", pane.Said);
        var apply = Assert.Single(window.PaletteEntries("apply the text"));
        Assert.Equal("the project is open read-only", apply.Unavailable);
        Assert.Equal(0, window.Editor.Session.History.UndoCount);
    }

    [Fact]
    public void Typing_in_the_panel_then_ctrl_enter_applies()
    {
        var (_, window, pane) = Open();
        using var host = new HeadlessHost(900, 700);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(900, 700), ImGuiCond.Always);
            ImGui.Begin("Text panel test"u8);
            pane.Draw();
            ImGui.End();
        }
        host.Frames(Draw, 3);
        host.Click(Draw, 400, 300);
        host.Press(Draw, ImGuiKey.A, ctrl: true);
        host.Type(Draw, """{"wares":[{"name":"Typed","price":4}]}""");
        host.Frames(Draw, 2);
        Assert.True(pane.HasDraft, pane.Text);
        Assert.Equal("Rope", Name(window, 0));
        host.Press(Draw, ImGuiKey.Enter, ctrl: true);
        Assert.Equal("Typed", Name(window, 0));
        Assert.Equal(1, window.Editor.Session.History.UndoCount);
        Assert.False(pane.HasDraft);
    }

    [Fact]
    public void An_unchanged_frame_writes_nothing_out_again_and_an_edit_writes_once()
    {
        var (_, window, pane) = Open();
        using var host = new HeadlessHost(900, 700) { Drawing = false };
        var drawn = 0;
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(900, 700), ImGuiCond.Always);
            ImGui.Begin("Text panel frames"u8);
            pane.Draw();
            drawn++;
            ImGui.End();
        }
        host.Frames(Draw, 4);
        Assert.Equal(1, pane.Rendered);
        host.Frames(Draw, 20);
        Assert.Equal(24, drawn);
        Assert.Equal(1, pane.Rendered);   // 20 more frames, the same document: written out once

        Assert.True(window.Editor.Session.Execute(new SetValue("/wares/0/price", Json("7"))));
        host.Frames(Draw, 5);
        Assert.Equal(2, pane.Rendered);   // a new document: once more
        Assert.Contains("{ \"name\": \"Rope\", \"price\": 7 }", pane.Text);
    }

    [Fact]
    public void The_core_editor_carries_no_imcode_only_the_text_package_does()
    {
        static IEnumerable<string> Refs(Type t) => t.Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(Refs(typeof(DataWorkspace)), n => n.StartsWith("ImCode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ImCode", Refs(typeof(JsonTextPane)));
        Assert.Contains("Brutal.Authoring.Editor.ImGui", Refs(typeof(JsonTextPane)));
    }

    [Fact]
    public void A_workspace_without_the_text_panel_has_none_and_adding_it_twice_adds_one()
    {
        var plain = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas")));
        Assert.True(plain.Show("wares.json"), plain.Said);
        Assert.DoesNotContain(plain.Window!.Editor.Panels, p => p.Id == JsonTextPane.PanelId);
        Assert.Null(plain.TextOf("wares.json"));

        var twice = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas"))).AddTextPanel().AddTextPanel();
        Assert.True(twice.Show("wares.json"), twice.Said);
        Assert.Single(twice.Window!.Editor.Panels, p => p.Id == JsonTextPane.PanelId);
        Assert.NotNull(twice.TextOf("wares.json"));
    }

    [Fact]
    public void A_host_panel_that_throws_costs_that_panel_never_the_file()
    {
        var w = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas")));
        w.FilePanels.Add((_, _) => throw new InvalidOperationException("boom"));
        w.AddTextPanel();
        Assert.True(w.Show("wares.json"), w.Said);
        Assert.Contains("boom", w.AdapterProblem);
        Assert.Contains(w.Window!.Editor.Panels, p => p.Id == JsonTextPane.PanelId);   // the next one still came
    }

    [Fact]
    public void The_general_app_adds_the_text_panel_to_every_file_it_opens()
    {
        var launcher = Brutal.Authoring.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root, "recent.json"))));
        Assert.True(launcher.Open(new ProjectPaths(Data, Path.Combine(root, "schemas"))), launcher.Said);
        Assert.Contains(launcher.Workspace!.Window!.Editor.Panels, p => p.Id == JsonTextPane.PanelId && p.Title == "Text");
        Assert.NotNull(launcher.Workspace.TextOf("wares.json"));
    }
}
