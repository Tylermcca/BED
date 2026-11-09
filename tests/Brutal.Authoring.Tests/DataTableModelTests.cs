using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

// INVENTED: regression witnesses through the unchanged facade, using self-authored documents.
[Collection(ImGuiCollection.Name)]
public sealed class DataTableModelTests
{
    private static (EditorModel<DataDocument> Editor, DataTableView Table) Open()
    {
        const string fields = """{"type":"object","properties":{"id":{"type":"string"},"count":{"type":"integer","minimum":0},"note":{"type":"string"}}}""";
        var schemas = new JsonSchemas().Add("table", "{\"type\":\"object\",\"properties\":{\"rows\":{\"type\":\"array\",\"items\":" + fields + "},\"archive\":{\"type\":\"array\",\"items\":" + fields + "}}}");
        var document = new DataFormat().Read("""
            {"rows":[{"id":"a","count":9007199254740993,"note":"old"},
                     {"id":"b","count":9007199254740992,"note":"old"},
                     {"id":"c","note":"last"}],"archive":[{"id":"d","count":3}]}
            """);
        var editor = new EditorModel<DataDocument>(DataKit.Session(document, schemas, "table"), DataEditing.Outline);
        return (editor, new DataTableView(editor, DataEditing.ObjectType(schemas, "table")));
    }

    [Fact]
    public void Query_caches_are_per_view_and_keep_exact_sort_and_missing_last()
    {
        var (editor, first) = Open();
        var second = new DataTableView(editor, first.Type) { List = "/archive", Filter = "d" };
        first.Sort = ("count", false);
        Assert.Equal(new[] { "/rows/1", "/rows/0", "/rows/2" }, first.Visible());
        Assert.Same(first.Items(), first.Items());
        Assert.Same(first.Columns(), first.Columns());
        Assert.Same(first.Visible(), first.Visible());
        Assert.Same(first.Cell("/rows/0", "count"), first.Cell("/rows/0", "count"));
        Assert.Equal(new[] { "/archive/0" }, second.Visible());
        first.Sort = ("count", true);
        Assert.Equal(new[] { "/rows/0", "/rows/1", "/rows/2" }, first.Visible());
        Assert.Equal("/archive", second.List);
        Assert.Equal("d", second.Filter);
        Assert.Null(second.Sort);
        Assert.False(editor.Session.Dirty);
        Assert.Empty(editor.Session.Journal);
    }

    [Fact]
    public void Plans_are_nonmutating_and_apply_is_still_one_validated_undo_step()
    {
        var (editor, table) = Open();
        var before = editor.Session.Current;
        var invalid = table.PlanFill("count", "-1");
        Assert.True(invalid.Ok); // Kind checking is planning; schema checking remains at Apply.
        Assert.False(table.Apply(invalid));
        Assert.Same(before, editor.Session.Current);
        Assert.Empty(editor.Session.Journal);
        var plan = table.PlanFill("count", "7");
        Assert.True(plan.Ok);
        Assert.Equal(3, plan.Cells);
        Assert.Equal(3, plan.Commands.Count);
        Assert.Same(before, editor.Session.Current);
        Assert.True(table.Apply(plan), table.Said);
        Assert.Equal(1, editor.Session.History.UndoCount);
        Assert.All(table.Items(), item => Assert.Equal(7, editor.Session.Current.At(item + "/count")!.Value.GetInt64()));
        Assert.True(editor.Session.Undo());
        Assert.Equal(9007199254740993L, editor.Session.Current.At("/rows/0/count")!.Value.GetInt64());
        Assert.Null(editor.Session.Current.At("/rows/2/count"));
        Assert.False(editor.Session.Undo());
    }

    [Fact]
    public void One_bad_pasted_cell_discards_every_planned_command_without_mutation()
    {
        var (editor, table) = Open();
        var before = editor.Session.Current;
        var plan = table.PlanPaste("/rows/0", "count", "4\nnot-an-integer");
        Assert.False(plan.Ok);
        Assert.Equal(2, plan.Cells);
        Assert.Empty(plan.Commands);
        Assert.Contains(plan.Problems, p => p.Contains("not-an-integer") && p.Contains("whole number"));
        Assert.False(table.Apply(plan));
        Assert.Same(before, editor.Session.Current);
        Assert.Empty(editor.Session.Journal);
    }

    [Fact]
    public void Quoted_grid_remains_one_cell_and_refreshes_cached_rows_after_apply_and_undo()
    {
        var (editor, table) = Open();
        var old = table.Cell("/rows/1", "note");
        var visible = table.Visible();
        var plan = table.PlanPaste("/rows/1", "note", "\"line\tone\n\"\"quoted\"\"\"");
        Assert.True(plan.Ok, string.Join("; ", plan.Problems));
        Assert.Equal(1, plan.Cells);
        Assert.Single(plan.Commands);
        Assert.Equal("old", old!.Value);
        Assert.True(table.Apply(plan), table.Said);
        Assert.Equal("line\tone\n\"quoted\"", table.Cell("/rows/1", "note")!.Value);
        Assert.NotSame(visible, table.Visible());
        Assert.True(editor.Session.Undo());
        Assert.Equal("old", table.Cell("/rows/1", "note")!.Value);
    }

    [Fact]
    public void Filtering_changes_plan_targets_but_not_document_or_other_view_state()
    {
        var (editor, table) = Open();
        table.Filter = "last";
        var before = editor.Session.Current;
        Assert.Equal(new[] { "/rows/2" }, table.Visible());
        var plan = table.PlanFill("note", "replaced");
        Assert.True(plan.Ok);
        Assert.Equal(1, plan.Cells);
        Assert.Same(before, editor.Session.Current);
        Assert.True(table.Apply(plan), table.Said);
        Assert.Equal("old", editor.Session.Current.At("/rows/0/note")!.Value.GetString());
        Assert.Equal("old", editor.Session.Current.At("/rows/1/note")!.Value.GetString());
        Assert.Equal("replaced", editor.Session.Current.At("/rows/2/note")!.Value.GetString());
        Assert.Empty(table.Visible());
    }
}
