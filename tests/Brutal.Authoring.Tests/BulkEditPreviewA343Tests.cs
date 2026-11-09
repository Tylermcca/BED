using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

// MIXED: real input routes borrowed from DataTableKeyboardTests; fixtures and assertions are self-authored.
// Prepared only: no C#/ImGui/input execution was permitted during A343.
[Collection(ImGuiCollection.Name)]
public sealed class BulkEditPreviewA343Tests
{
    private const string Schema = """
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
          "name":{"type":"string","title":"Name","minLength":1},
          "amount":{"type":"number","title":"Amount","minimum":0,"maximum":100},
          "count":{"type":"integer","title":"Count","minimum":0},
          "enabled":{"type":"boolean","title":"Enabled"},
          "mode":{"type":"string","title":"Mode","enum":["Quiet","Normal","Bright"]},
          "link":{"type":"string","title":"Link","x-ref":"/targets"}},
          "required":["name","amount","count","enabled","mode","link"]}},
          "targets":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}},"required":["id","name"]}}}}
        """;
    private const string Text = """
        {"items":[
          {"name":"Amber","amount":8,"count":1,"enabled":false,"mode":"Quiet","link":"target-a"},
          {"name":"Birch","amount":10,"count":2,"enabled":true,"mode":"Normal","link":"target-b"},
          {"name":"Cobalt","amount":20,"count":3,"enabled":false,"mode":"Bright","link":"target-a"}],
        "targets":[{"id":"target-a","name":"Beacon"},{"id":"target-b","name":"Shelter"}]}
        """;

    [Fact]
    public void Mixed_fill_counts_candidates_groups_refusals_and_cancels_without_partial_apply()
    {
        using var r = new Rig(mixed: true);
        var before = r.Session.Current;
        r.FillPreview("8");
        var plan = Assert.IsType<DataTableView.BatchPlan>(r.Table.Pending);
        Assert.Equal(4, plan.Cells);
        var counts = Assert.IsType<DataTableView.BatchCellCounts>(plan.Counts);
        Assert.Equal((1, 1, 2), (counts.Changed, counts.Unchanged, counts.Refused));
        var reason = Assert.Single(counts.Reasons);
        Assert.Equal("enter true or false", reason.Reason);
        Assert.Equal(2, reason.Cells);
        Assert.Equal(2, plan.Problems.Count);
        Assert.False(plan.Ok);
        Assert.Empty(plan.Commands); // no executable valid subset, even though one candidate would change
        var ui = r.Ui();
        Assert.Contains("Would change 1 cell; 1 unchanged; 2 refused.", ui);
        Assert.Contains("2 cells: enter true or false.", ui);
        Assert.Contains("Whole-document checks still run at Apply.", ui);
        r.Untouched(before);
        r.CancelPreview(applyEnabled: false); // disabled Apply is skipped; Cancel is the next enabled action
        Assert.Null(r.Table.Pending);
        r.Untouched(before);
    }

    [Fact]
    public void Fill_cancel_preserves_exact_bytes_revision_journal_and_history()
    {
        using var r = new Rig();
        var before = r.Session.Current;
        r.FillPreview("8");
        var counts = Assert.IsType<DataTableView.BatchCellCounts>(r.Table.Pending!.Counts);
        Assert.Equal((2, 1, 0), (counts.Changed, counts.Unchanged, counts.Refused));
        Assert.Empty(counts.Reasons);
        Assert.Contains("Would change 2 cells; 1 unchanged; 0 refused.", r.Ui());
        r.Untouched(before);
        r.CancelPreview(applyEnabled: true);
        Assert.Null(r.Table.Pending);
        r.Untouched(before);
    }

    [Fact]
    public void Fill_apply_reports_the_operation_and_one_document_undo_restores_every_original()
    {
        using var r = new Rig();
        var before = r.Session.Current;
        var original = r.Format.Write(before);
        r.FillPreview("8");
        r.ApplyPreview();
        Assert.Null(r.Table.Pending);
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(8d, r.Value(i, "amount").GetDouble()));
        Assert.Single(r.Session.Journal);
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.Equal("Filled 2 cells; one Undo reverses the whole fill.", r.Table.Said);
        Assert.Contains(r.Table.Said, r.Ui());
        r.Key(ImGuiKey.Z, ctrl: true);
        Assert.Same(before, r.Session.Current);
        Assert.Equal(original, r.Format.Write(r.Session.Current));
        Assert.Equal(0, r.Session.History.UndoCount);
        Assert.False(r.Session.History.CanUndo);
        Assert.False(r.Session.Dirty);
    }

    [Fact]
    public void Paste_preserves_shown_cell_order_counts_unchanged_and_undoes_as_one_step()
    {
        using var r = new Rig();
        var before = r.Session.Current;
        var original = r.Format.Write(before);
        r.Cell(0, 1);
        r.PastePreview("8\t1\n12\t2");
        var plan = Assert.IsType<DataTableView.BatchPlan>(r.Table.Pending);
        var counts = Assert.IsType<DataTableView.BatchCellCounts>(plan.Counts);
        Assert.Equal(4, plan.Cells);
        Assert.Equal((1, 3, 0), (counts.Changed, counts.Unchanged, counts.Refused));
        Assert.Single(plan.Commands);
        Assert.Contains("Would change 1 cell; 3 unchanged; 0 refused.", r.Ui());
        r.ApplyPreview();
        Assert.Equal(8d, r.Value(0, "amount").GetDouble());
        Assert.Equal(1, r.Value(0, "count").GetInt32());
        Assert.Equal(12d, r.Value(1, "amount").GetDouble());
        Assert.Equal(2, r.Value(1, "count").GetInt32());
        Assert.Equal(20d, r.Value(2, "amount").GetDouble());
        Assert.Single(r.Session.Journal);
        Assert.Equal(1, r.Session.History.UndoCount);
        Assert.Equal("Pasted 1 cell; one Undo reverses the whole paste.", r.Table.Said);
        r.Key(ImGuiKey.Z, ctrl: true);
        Assert.Same(before, r.Session.Current);
        Assert.Equal(original, r.Format.Write(r.Session.Current));
        Assert.Equal(0, r.Session.History.UndoCount);
    }

    [Fact]
    public void Paste_groups_distinct_bad_text_by_stable_reason_and_discards_the_valid_candidate()
    {
        using var r = new Rig();
        var before = r.Session.Current;
        r.Cell(0, 1);
        r.PastePreview("not-one\nnot-two\n8");
        var plan = Assert.IsType<DataTableView.BatchPlan>(r.Table.Pending);
        var counts = Assert.IsType<DataTableView.BatchCellCounts>(plan.Counts);
        Assert.Equal((1, 0, 2), (counts.Changed, counts.Unchanged, counts.Refused));
        var reason = Assert.Single(counts.Reasons);
        Assert.Equal("enter a number (use a dot for decimals)", reason.Reason);
        Assert.Equal(2, reason.Cells);
        Assert.Contains(plan.Problems, problem => problem.Contains("not-one", StringComparison.Ordinal));
        Assert.Contains(plan.Problems, problem => problem.Contains("not-two", StringComparison.Ordinal));
        Assert.False(plan.Ok);
        Assert.Empty(plan.Commands);
        Assert.Contains("2 cells: enter a number (use a dot for decimals).", r.Ui());
        r.Untouched(before);
    }

    [Fact]
    public void Ragged_paste_is_an_operation_refusal_without_fabricated_cell_counts()
    {
        using var r = new Rig();
        var before = r.Session.Current;
        r.Cell(0, 1);
        r.PastePreview("8\t1\n12");
        var plan = Assert.IsType<DataTableView.BatchPlan>(r.Table.Pending);
        Assert.False(plan.Ok);
        Assert.Null(plan.Counts);
        Assert.Empty(plan.Commands);
        Assert.Contains("different numbers of cells", Assert.Single(plan.Problems));
        Assert.Contains("Can't:", r.Ui());
        r.Untouched(before);
        r.CancelPreview(applyEnabled: false);
        Assert.Null(r.Table.Pending);
        r.Untouched(before);
    }

    [Fact]
    public void Scalar_valid_but_schema_invalid_fill_still_refuses_at_apply_without_a_history_step()
    {
        using var r = new Rig();
        var before = r.Session.Current;
        r.FillPreview("-1");
        var plan = Assert.IsType<DataTableView.BatchPlan>(r.Table.Pending);
        Assert.True(plan.Ok); // the preview is not a replacement for the session validator
        var counts = Assert.IsType<DataTableView.BatchCellCounts>(plan.Counts);
        Assert.Equal((3, 0, 0), (counts.Changed, counts.Unchanged, counts.Refused));
        Assert.Contains("Whole-document checks still run at Apply.", r.Ui());
        r.ApplyPreview();
        Assert.Null(r.Table.Pending);
        Assert.Contains("at least", r.Table.Said);
        Assert.Contains("amount", r.Table.Said, StringComparison.OrdinalIgnoreCase);
        r.Untouched(before);
    }

    [Fact]
    public void All_unchanged_fill_has_no_commands_and_cancel_still_commits_nothing()
    {
        using var r = new Rig(allEight: true);
        var before = r.Session.Current;
        r.FillPreview("8");
        var plan = Assert.IsType<DataTableView.BatchPlan>(r.Table.Pending);
        Assert.True(plan.Ok);
        var counts = Assert.IsType<DataTableView.BatchCellCounts>(plan.Counts);
        Assert.Equal((0, 3, 0), (counts.Changed, counts.Unchanged, counts.Refused));
        Assert.Empty(plan.Commands);
        Assert.Contains("Would change 0 cells; 3 unchanged; 0 refused.", r.Ui());
        r.CancelPreview(applyEnabled: false);
        Assert.Null(r.Table.Pending);
        r.Untouched(before);
    }

    [Fact]
    public void Original_three_argument_plan_constructor_and_deconstruction_stay_available()
    {
        var plan = new DataTableView.BatchPlan([], 0, []);
        var (commands, cells, problems) = plan;
        Assert.Empty(commands);
        Assert.Equal(0, cells);
        Assert.Empty(problems);
        Assert.True(plan.Ok);
        Assert.Null(plan.Counts);
        Assert.Equal("edit", plan.Operation);
    }

    private static (string Text, string Schema) Fixture(bool mixed, bool allEight)
    {
        var text = JsonNode.Parse(Text)!;
        var schema = JsonNode.Parse(Schema)!;
        var rows = text["items"]!.AsArray();
        if (allEight)
            foreach (var row in rows) row!["amount"] = 8;
        if (mixed)
        {
            rows[2]!["amount"] = false;
            var fourth = rows[2]!.DeepClone();
            fourth["name"] = "Dahlia";
            fourth["amount"] = true;
            rows.Add(fourth);
            var list = schema["properties"]!["items"]!.AsObject();
            var numberRow = list["items"]!.DeepClone();
            var toggleRow = numberRow.DeepClone();
            var amount = toggleRow["properties"]!["amount"]!.AsObject();
            amount["type"] = "boolean";
            amount.Remove("minimum");
            amount.Remove("maximum");
            list.Remove("items");
            list["prefixItems"] = new JsonArray(numberRow.DeepClone(), numberRow.DeepClone(), toggleRow.DeepClone(), toggleRow.DeepClone());
        }
        return (text.ToJsonString(), schema.ToJsonString());
    }

    private sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host = new(1600, 900) { Drawing = false };
        public readonly DataFormat Format = new();
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly DataTableView Table;
        public readonly EditorWindow<DataDocument> Window;
        private readonly string directory;
        public Action Draw => Window.DrawDocked;

        public Rig(bool mixed = false, bool allEight = false)
        {
            directory = Path.Combine(Path.GetTempPath(), "a343-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var fixture = Fixture(mixed, allEight);
                var schemas = new JsonSchemas().Add("a343", fixture.Schema);
                Session = DataKit.Session(Format.Read(fixture.Text), schemas, "a343");
                Editor = new(Session, DataEditing.OutlineFor(schemas, "a343"));
                var type = DataEditing.ObjectType(schemas, "a343");
                Table = new(Editor, type) { References = DataEditing.SchemaReferences(schemas, "a343") };
                Editor.Add(Table.AsPanel());
                Window = new(Editor, new Inspector<DataDocument>(Editor).Add(type), new Shortcuts());
                EditorWindow<DataDocument>.Configure(ImGui.GetIO());
                Host.Frames(Draw, 5);
            }
            catch
            {
                Host.Dispose();
                DeleteOwnedDirectory();
                throw;
            }
        }

        public void Key(ImGuiKey key, bool ctrl = false, bool shift = false) => Host.Press(Draw, key, ctrl, shift);
        private void Tabs(int count, bool shift = false)
        {
            for (var i = 0; i < count; i++) Key(ImGuiKey.Tab, shift: shift);
        }
        public void Cell(int row, int column)
        {
            var before = Session.Current;
            Key(ImGuiKey.DownArrow);
            Key(ImGuiKey.DownArrow);
            Key(ImGuiKey.Enter);
            Tabs(8 + 8 * row + column); // existing six-field table route, no focus-id injection
            Assert.Equal(($"/items/{row}", new[] { "name", "amount", "count", "enabled", "mode", "link" }[column]), Table.Anchor);
            Assert.Same(before, Session.Current);
            Assert.Empty(Session.Journal);
        }
        public void FillPreview(string text)
        {
            Tabs(1);
            Tabs(1);
            Key(ImGuiKey.Enter);
            Key(ImGuiKey.DownArrow); // Name -> Amount in the existing chooser
            Key(ImGuiKey.Enter);
            Tabs(1);
            Host.Type(Draw, text);
            Tabs(1);
            Key(ImGuiKey.Enter);
        }
        public void PastePreview(string text)
        {
            ImGui.SetClipboardText(text);
            Key(ImGuiKey.Escape);
            Key(ImGuiKey.Escape);
            Tabs(1, shift: true);
            Key(ImGuiKey.Enter);
        }
        public void ApplyPreview()
        {
            Tabs(1);
            Key(ImGuiKey.Enter);
        }
        public void CancelPreview(bool applyEnabled)
        {
            Tabs(applyEnabled ? 2 : 1);
            Key(ImGuiKey.Enter);
        }
        public JsonElement Value(int row, string field) => Session.Current.At($"/items/{row}/{field}")!.Value;
        public void Untouched(DataDocument before)
        {
            Assert.Same(before, Session.Current);
            Assert.Equal(Format.Write(before), Format.Write(Session.Current));
            Assert.Equal(0, Session.Revision);
            Assert.Empty(Session.Journal);
            Assert.Equal(0, Session.History.UndoCount);
            Assert.False(Session.History.CanRedo);
            Assert.False(Session.Dirty);
        }
        public string Ui()
        {
            var file = Path.Combine(directory, "ui.txt");
            Host.Frame(() =>
            {
                ImGui.LogToFile(0, file);
                Window.DrawDocked();
                ImGui.LogFinish();
            });
            return File.ReadAllText(file);
        }
        private void DeleteOwnedDirectory()
        {
            var full = Path.GetFullPath(directory);
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("a343-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected bulk-preview fixture path.");
            Directory.Delete(full, recursive: true);
        }
        public void Dispose()
        {
            Host.Dispose();
            DeleteOwnedDirectory();
        }
    }
}
