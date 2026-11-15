using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ChoiceLabels01Tests
{
    private const string Schema = """
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
      "id":{"type":"string"},"grade":{"title":"Grade","$ref":"#/$defs/grade","x-choices":{"bronze":"Common","silver":"Shiny"}},
      "weight":{"title":"Weight","enum":[10,20],"x-choices":{"10":"Light","20":"Heavy"}}}}}},"$defs":{"grade":{"enum":["bronze","silver"]}}}
    """;
    private const string Seed = """{"items":[{"id":"box","grade":"bronze","weight":10},{"id":"ball","grade":"silver","weight":20}]}""";
    private sealed class Rig : IDisposable
    {
        internal readonly string Root = Directory.CreateTempSubdirectory("bed-choice-labels-").FullName;
        internal readonly HeadlessHost Host = new(1600, 1000) { Drawing = false };
        internal readonly DataWorkspace Workspace;
        internal EditorWindow<DataDocument> Window => Workspace.Window!;
        internal DataTableView Table => Workspace.TableOf("items.json")!;
        internal string PathToFile => Path.Combine(Root, "data", "items.json");
        internal Rig(string? schema = null, string? text = null)
        {
            var data = Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(Root, "schemas")).FullName;
            File.WriteAllText(PathToFile, text ?? Seed);
            File.WriteAllText(Path.Combine(schemas, "items.schema.json"), schema ?? Schema);
            EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            Workspace = new(DataProject.Open(data, schemas)); Assert.True(Workspace.Show("items.json"));
            Assert.True(Window.Editor.Select("/items/0")); Frames();
        }
        internal void Frames() => Host.Frames(Workspace.Draw, 4);
        internal UiNode Combo(string label) => Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo && n.Label == label);
        internal void Click(UiNode node) { Host.Click(Workspace.Draw, node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y + node.Bounds.Height / 2); Frames(); }
        internal JsonElement Value(string field) => Window.Editor.Session.Current.At("/items/0/" + field)!.Value;
        public void Dispose()
        {
            Workspace.Dispose(); Host.Dispose();
            if (!System.IO.Path.GetFullPath(Root).StartsWith(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bed-choice-labels-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture path");
            Directory.Delete(Root, true);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Table_and_inspector_show_labels_but_choose_save_undo_and_redo_stored_values(bool inspector)
    {
        using var r = new Rig(); var caption = inspector ? "Grade" : "box: Grade";
        Assert.Equal("Common", r.Combo(caption).Value);
        var row = r.Window.Inspector!.Rows().Single(x => x.Id == "grade");
        Assert.Equal(new[] { "bronze", "silver" }, row.Choices); Assert.Equal("bronze", row.Value);
        r.Click(r.Combo(caption));
        var choice = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Shiny");
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "silver");
        r.Click(choice); Assert.Equal("silver", r.Value("grade").GetString()); Assert.Equal(1, r.Window.Editor.Session.History.UndoCount);
        Assert.True(r.Window.Run("editor.undo")); r.Frames(); Assert.Equal("bronze", r.Value("grade").GetString()); Assert.Equal("Common", r.Combo(caption).Value);
        Assert.DoesNotContain("bronze", r.Window.Status); Assert.DoesNotContain("silver", r.Window.Status);
        Assert.True(r.Window.Run("editor.redo")); r.Frames(); Assert.Equal("Shiny", r.Combo(caption).Value);
        r.Window.Editor.Session.Save();
        using var reopened = JsonDocument.Parse(File.ReadAllText(r.PathToFile)); Assert.Equal("silver", reopened.RootElement.GetProperty("items")[0].GetProperty("grade").GetString());
        Assert.DoesNotContain("Shiny", File.ReadAllText(r.PathToFile));
    }

    [Fact]
    public void Numeric_choice_labels_keep_numeric_JSON_and_plain_refusal_messages()
    {
        using var r = new Rig(); Assert.Equal("Light", r.Combo("box: Weight").Value);
        r.Click(r.Combo("box: Weight")); r.Click(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Heavy"));
        Assert.Equal(JsonValueKind.Number, r.Value("weight").ValueKind); Assert.Equal(20, r.Value("weight").GetInt32());
        Assert.False(r.Table.SetCell("/items/0", "weight", 30));
        Assert.Contains("Light", r.Table.Said); Assert.Contains("Heavy", r.Table.Said); Assert.DoesNotContain("10, 20", r.Table.Said);
        Assert.Equal(20, r.Value("weight").GetInt32());
    }

    [Fact]
    public void Referenced_enum_refusals_use_sibling_labels_without_accepting_the_labels_as_data()
    {
        using var r = new Rig();
        Assert.False(r.Table.SetCell("/items/0", "grade", "Shiny"));
        Assert.Contains("Common", r.Table.Said); Assert.Contains("Shiny", r.Table.Said);
        Assert.DoesNotContain("bronze", r.Table.Said); Assert.DoesNotContain("silver", r.Table.Said);
        Assert.Equal("bronze", r.Value("grade").GetString()); Assert.Equal(0, r.Window.Editor.Session.History.UndoCount);
    }

    [Fact]
    public void Duplicate_labels_have_distinct_actions_and_the_second_value_can_be_chosen()
    {
        using var r = new Rig(Schema.Replace("Common", "Same").Replace("Shiny", "Same"));
        r.Click(r.Combo("box: Grade")); var entries = Ui.Snapshot().Nodes.Where(n => n.Role == UiRole.ListItem && n.Label == "Same").ToArray();
        Assert.Equal(2, entries.Length); Assert.NotEqual(entries[0].Id, entries[1].Id);
        r.Click(entries[1]); Assert.Equal("silver", r.Value("grade").GetString());
    }

    [Fact]
    public void Missing_or_malformed_notes_fall_back_to_the_original_values_and_messages()
    {
        var schema = Schema.Replace("\"x-choices\":{\"bronze\":\"Common\",\"silver\":\"Shiny\"}", "\"x-choices\":{\"bronze\":null,\"silver\":\"\",\"extra\":\"Not an allowed value\"}");
        using var r = new Rig(schema); Assert.Equal("bronze", r.Combo("box: Grade").Value);
        Assert.False(r.Table.SetCell("/items/0", "grade", "other")); Assert.Contains("\"bronze\", \"silver\"", r.Table.Said);
    }

    [Fact]
    public void A_partial_note_labels_only_its_declared_values()
    {
        using var r = new Rig(Schema.Replace(",\"silver\":\"Shiny\"", ""));
        r.Click(r.Combo("box: Grade")); Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Common");
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "silver");
    }
    [Fact]
    public void Boolean_choices_keep_their_boolean_JSON_values()
    {
        var schema = Schema.Replace("\"$ref\":\"#/$defs/grade\"", "\"enum\":[true,false]")
            .Replace("\"bronze\":\"Common\",\"silver\":\"Shiny\"", "\"true\":\"On\",\"false\":\"Off\"");
        using var r = new Rig(schema, Seed.Replace("\"grade\":\"bronze\"", "\"grade\":true").Replace("\"grade\":\"silver\"", "\"grade\":false"));
        Assert.Equal("On", r.Combo("box: Grade").Value); r.Click(r.Combo("box: Grade"));
        r.Click(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Off"));
        Assert.Equal(JsonValueKind.False, r.Value("grade").ValueKind); Assert.Equal("Off", r.Combo("box: Grade").Value);
    }

    [Fact]
    public void Numeric_notes_use_JSON_spelling_even_under_a_comma_decimal_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var schema = Schema.Replace("[10,20]", "[1.5,2.5]").Replace("\"10\":", "\"1.5\":").Replace("\"20\":", "\"2.5\":");
            using var r = new Rig(schema, Seed.Replace("\"weight\":10", "\"weight\":1.5").Replace("\"weight\":20", "\"weight\":2.5"));
            Assert.Equal("Light", r.Combo("box: Weight").Value); r.Click(r.Combo("box: Weight"));
            r.Click(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Heavy"));
            Assert.Equal(2.5, r.Value("weight").GetDouble()); Assert.Equal("Heavy", r.Combo("box: Weight").Value);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Schema_reload_refreshes_labels_without_changing_the_document()
    {
        using var r = new Rig(); var before = r.Window.Editor.Session.TextToSave;
        var name = r.Workspace.Project.Find("items.json")!.Schema!;
        r.Workspace.Project.Schemas.Add(name, Schema.Replace("Common", "An expanded plain label")); r.Frames();
        Assert.Equal("An expanded plain label", r.Combo("box: Grade").Value);
        Assert.Equal("An expanded plain label", r.Combo("Grade").Value);
        Assert.Equal(before, r.Window.Editor.Session.TextToSave); Assert.Equal(0, r.Window.Editor.Session.History.UndoCount);
    }    [Fact]
    public void Numeric_enum_spelling_does_not_hide_its_label_or_create_a_redundant_edit()
    {
        var schema = Schema.Replace("[10,20]", "[1e1,20]").Replace("\"10\":", "\"1e1\":");
        using var r = new Rig(schema); Assert.Equal("Light", r.Combo("box: Weight").Value);
        r.Click(r.Combo("box: Weight")); var current = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Light");
        Assert.True(current.Selected); r.Click(current); Assert.Equal(0, r.Window.Editor.Session.History.UndoCount);
    }

    [Fact]
    public void Table_filter_finds_plain_labels_and_keeps_accepting_stored_words()
    {
        using var r = new Rig(); r.Table.Filter = "Shiny";
        Assert.Equal(new[] { "/items/1" }, r.Table.Visible());
        r.Table.Filter = "bronze"; Assert.Equal(new[] { "/items/0" }, r.Table.Visible());
        Assert.Equal(0, r.Window.Editor.Session.History.UndoCount);
    }    [Fact]
    public void Exact_integer_values_keep_distinct_labels_and_mixed_selection_above_double_precision()
    {
        var schema = Schema.Replace("[10,20]", "[9007199254740992,9007199254740993]")
            .Replace("\"10\":", "\"9007199254740992\":").Replace("\"20\":", "\"9007199254740993\":");
        var data = Seed.Replace("\"weight\":10", "\"weight\":9007199254740993").Replace("\"weight\":20", "\"weight\":9007199254740992");
        using var r = new Rig(schema, data); Assert.Equal("Heavy", r.Combo("box: Weight").Value);
        r.Click(r.Combo("box: Weight")); var current = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Heavy");
        Assert.True(current.Selected); r.Click(current); Assert.Equal(0, r.Window.Editor.Session.History.UndoCount);
        r.Window.Editor.Select("/items/0", "/items/1"); r.Frames();
        Assert.True(r.Window.Inspector!.Rows().Single(row => row.Id == "weight").Mixed);
        r.Window.Editor.Select("/items/0"); r.Frames(); r.Click(r.Combo("box: Weight"));
        r.Click(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Light"));
        Assert.Equal("9007199254740992", r.Value("weight").GetRawText());
    }}