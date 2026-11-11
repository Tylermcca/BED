using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

// INVENTED data/counterexamples; BORROWED: the existing DataTableKeyboardTests keyboard-only
// entry route and HeadlessHost seam. No game data, platform window, clipboard or focus injection.
[Collection(ImGuiCollection.Name)]
public sealed class TableAfterFixesAcceptanceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_untyped_boolean_cell_toggles_as_a_boolean_and_round_trips(bool initial)
    {
        using var r = new Rig(enabled: initial, enabledSchema: "{}");
        Assert.Equal(PropertyKind.Toggle, r.Table.Cell("/items/0", "enabled")!.Kind);
        r.Cell(3); r.Key(ImGuiKey.Space);
        Assert.Equal(!initial, r.Value("enabled").GetBoolean());
        Assert.Equal(initial ? JsonValueKind.False : JsonValueKind.True, r.Value("enabled").ValueKind);
        r.CheckOneEditAndRoundTrip("enabled");
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(16384)]
    public void A_long_unicode_cell_draws_and_an_appended_character_is_preserved(int repeats)
    {
        var text = string.Concat(Enumerable.Repeat("café λ ", repeats));
        using var r = new Rig(amountSchema: "{\"type\":\"string\"}", amount: text);
        r.Cell(1); if (!ImGui.GetIO().WantTextInput) r.Key(ImGuiKey.Enter);
        r.Key(ImGuiKey.End); r.Type("!"); r.Key(ImGuiKey.Enter);
        Assert.Equal(text + "!", r.Value("amount").GetString());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Theory]
    [InlineData("")]
    [InlineData("integer")]
    [InlineData("number")]
    public void A_numeric_choice_cell_selects_and_saves_a_number(string type)
    {
        var schema = type.Length == 0 ? "{\"enum\":[0,90,180]}" : "{\"type\":\"" + type + "\",\"enum\":[0,90,180]}";
        using var r = new Rig(amountSchema: schema, amount: 0);
        r.ChooseAmount("90");
        Assert.Equal(JsonValueKind.Number, r.Value("amount").ValueKind);
        Assert.Equal(90, r.Value("amount").GetInt32());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void A_fraction_typed_in_a_whole_number_cell_is_refused_without_rounding_or_history()
    {
        using var r = new Rig(); r.Cell(2); r.Replace("1.5");
        r.Untouched();
        Assert.Contains("whole", r.Table.Said, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, r.Value("count").GetInt32());
    }

    [Fact]
    public void A_whole_number_cell_accepts_a_valid_integer_as_one_history_step()
    {
        using var r = new Rig(); r.Cell(2); r.Replace("7");
        Assert.Equal(7, r.Value("count").GetInt32());
        r.CheckOneEditAndRoundTrip("count");
    }

    [Fact]
    public void The_table_setter_does_not_round_a_fraction_into_a_whole_number()
    {
        using var r = new Rig();
        Assert.False(r.Table.SetCell("/items/0", "count", 1.5));
        r.Untouched();
        Assert.Contains("whole", r.Table.Said, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    public void A_nullable_union_cell_offers_its_string_choices_and_commits_the_selected_choice(string keyword)
    {
        using var r = new Rig(amountSchema: "{\"" + keyword + "\":[{\"type\":\"string\",\"enum\":[\"alpha\",\"beta\"]},{\"type\":\"null\"}]}", amount: "alpha");
        Assert.Equal(new[] { "alpha", "beta" }, r.Table.Cell("/items/0", "amount")!.Choices);
        r.ChooseAmount("beta");
        Assert.Equal("beta", r.Value("amount").GetString());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void A_numeric_union_cell_filters_outer_limits_and_keeps_the_selected_number_numeric()
    {
        using var r = new Rig(amountSchema: "{\"minimum\":90,\"anyOf\":[{\"enum\":[0,90,180]},{\"type\":\"null\"}]}", amount: 90);
        Assert.Equal(new[] { "90", "180" }, r.Table.Cell("/items/0", "amount")!.Choices);
        r.ChooseAmount("180");
        Assert.Equal(JsonValueKind.Number, r.Value("amount").ValueKind);
        Assert.Equal(180, r.Value("amount").GetInt32());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void A_oneOf_cell_does_not_offer_a_number_valid_in_both_branches()
    {
        using var r = new Rig(amountSchema: "{\"oneOf\":[{\"enum\":[0,90]},{\"enum\":[90,180]}]}", amount: 0);
        Assert.Equal(new[] { "0", "180" }, r.Table.Cell("/items/0", "amount")!.Choices);
        r.ChooseAmount("180");
        Assert.Equal(180, r.Value("amount").GetInt32());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void Invalid_union_choice_refusal_leaves_the_original_document_and_history()
    {
        using var r = new Rig(amountSchema: "{\"anyOf\":[{\"enum\":[0,90]},{\"type\":\"null\"}]}", amount: 0);
        Assert.DoesNotContain("45", r.Table.Cell("/items/0", "amount")!.Choices);
        Assert.False(r.Table.SetCell("/items/0", "amount", "45")); r.Untouched();
    }

    [Fact]
    public void Numeric_choices_pasted_through_the_table_plan_still_store_numbers()
    {
        using var r = new Rig(amountSchema: "{\"enum\":[0,90,180]}", amount: 0);
        var plan = r.Table.PlanPaste("/items/0", "amount", "90");
        Assert.True(plan.Ok, string.Join(" ", plan.Problems));
        Assert.True(r.Table.Apply(plan), r.Table.Said);
        Assert.Equal(JsonValueKind.Number, r.Value("amount").ValueKind);
        Assert.Equal(90, r.Value("amount").GetInt32());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void A_fractional_integer_paste_is_refused_before_any_command_is_applied()
    {
        using var r = new Rig(); var plan = r.Table.PlanPaste("/items/0", "count", "1.5");
        Assert.False(plan.Ok); Assert.False(r.Table.Apply(plan)); r.Untouched();
        Assert.Contains("whole", r.Table.Said, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    public void A_union_string_cell_can_still_be_typed_validated_and_saved(string keyword)
    {
        using var r = new Rig(amountSchema: "{\"" + keyword + "\":[{\"type\":\"string\",\"enum\":[\"alpha\",\"beta\"]},{\"type\":\"null\"}]}", amount: "alpha");
        r.ChooseAmount("beta");
        Assert.Equal("beta", r.Value("amount").GetString());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void A_numeric_union_cell_can_still_be_typed_as_a_number_and_saved()
    {
        using var r = new Rig(amountSchema: "{\"minimum\":90,\"anyOf\":[{\"enum\":[0,90,180]},{\"type\":\"null\"}]}", amount: 90);
        r.ChooseAmount("180");
        Assert.Equal(JsonValueKind.Number, r.Value("amount").ValueKind);
        Assert.Equal(180, r.Value("amount").GetInt32());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    public void Explicit_finite_union_choices_reach_the_table_and_save_the_selected_string(string keyword)
    {
        using var r = new Rig(amountSchema: "{\"" + keyword + "\":[{\"enum\":[\"alpha\"]},{\"enum\":[\"beta\"]}]}", amount: "alpha");
        Assert.Equal(new[] { "alpha", "beta" }, r.Table.Cell("/items/0", "amount")!.Choices);
        r.ChooseAmount("beta"); Assert.Equal("beta", r.Value("amount").GetString());
        r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void Explicit_numeric_union_choices_in_the_table_respect_the_outer_bound()
    {
        using var r = new Rig(amountSchema: "{\"minimum\":90,\"anyOf\":[{\"enum\":[0,90]},{\"enum\":[180]}]}", amount: 90);
        Assert.Equal(new[] { "90", "180" }, r.Table.Cell("/items/0", "amount")!.Choices);
        r.ChooseAmount("180"); Assert.Equal(JsonValueKind.Number, r.Value("amount").ValueKind);
        Assert.Equal(180, r.Value("amount").GetInt32()); r.CheckOneEditAndRoundTrip("amount");
    }

    [Fact]
    public void The_type_null_choice_limitation_is_shared_with_the_inspector()
    {
        using var r = new Rig(amountSchema: "{\"anyOf\":[{\"type\":\"string\",\"enum\":[\"alpha\",\"beta\"]},{\"type\":\"null\"}]}", amount: "alpha");
        var editor = new EditorModel<DataDocument>(r.Session, DataEditing.OutlineFor(r.Schemas, "table"));
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(r.Schemas, "table"));
        Assert.True(editor.Select("/items/0"));
        var row = inspector.Rows().Single(x => x.Id == "amount");
        Assert.Equal(row.Kind, r.Table.Cell("/items/0", "amount")!.Kind);
        Assert.Equal(row.Choices, r.Table.Cell("/items/0", "amount")!.Choices);
        r.Untouched();
    }

    sealed class Rig : IDisposable
    {
        public readonly HeadlessHost Host = new(1600, 900) { Drawing = false };
        public readonly DataFormat Format = new();
        public readonly JsonSchemas Schemas;
        public readonly DataDocument Before;
        public readonly DocumentSession<DataDocument> Session;
        public readonly EditorModel<DataDocument> Editor;
        public readonly DataTableView Table;
        public readonly EditorWindow<DataDocument> Window;
        public readonly string Dir;
        public Action Draw => Window.DrawDocked;
        public Rig(string amountSchema = "{\"type\":\"number\"}", object? amount = null, bool enabled = false, string enabledSchema = "{\"type\":\"boolean\"}")
        {
            Dir = Path.Combine(Path.GetTempPath(), "a134-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            var fields = new Dictionary<string, JsonElement> {
                ["name"] = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"string\"}"),
                ["amount"] = JsonSerializer.Deserialize<JsonElement>(amountSchema),
                ["count"] = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"integer\",\"minimum\":0}"),
                ["enabled"] = JsonSerializer.Deserialize<JsonElement>(enabledSchema),
                ["mode"] = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"string\",\"enum\":[\"Quiet\",\"Bright\"]}"),
                ["link"] = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"string\",\"x-ref\":\"/targets\"}") };
            var schema = JsonSerializer.Serialize(new { type = "object", properties = new {
                items = new { type = "array", items = new { type = "object", properties = fields, required = fields.Keys.ToArray() } },
                targets = new { type = "array", items = new { type = "object" } } } });
            var row = new { name = "Amber", amount = amount ?? 20, count = 4, enabled, mode = "Quiet", link = "beacon" };
            var text = JsonSerializer.Serialize(new { items = new[] { row, row with { name = "Birch" } }, targets = new[] { new { id = "beacon", name = "Beacon" } } });
            Schemas = new JsonSchemas().Add("table", schema);
            Session = DataKit.Session(Format.Read(text), Schemas, "table"); Before = Session.Current;
            Editor = new(Session, DataEditing.OutlineFor(Schemas, "table"));
            var type = DataEditing.ObjectType(Schemas, "table");
            Table = new(Editor, type) { References = DataEditing.SchemaReferences(Schemas, "table") };
            Editor.Add(Table.AsPanel()); Window = new(Editor, new Inspector<DataDocument>(Editor).Add(type));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO()); Host.Frames(Draw, 5);
        }
        public void Key(ImGuiKey key, bool ctrl = false, bool shift = false) => Host.Press(Draw, key, ctrl, shift);
        public void Type(string text) => Host.Type(Draw, text);
        public void Cell(int column)
        {
            Key(ImGuiKey.DownArrow); Key(ImGuiKey.DownArrow); Key(ImGuiKey.Enter);
            for (int i = 0; i < 8 + column; i++) Key(ImGuiKey.Tab);
            Assert.Equal(("/items/0", new[] { "name", "amount", "count", "enabled", "mode", "link" }[column]), Table.Anchor);
            Untouched();
        }
        public void Replace(string text)
        {
            if (!ImGui.GetIO().WantTextInput) Key(ImGuiKey.Enter);
            Key(ImGuiKey.A, ctrl: true); Type(text); Key(ImGuiKey.Enter);
        }
        public void ChooseAmount(string choice)
        {
            var cell = Table.Cell("/items/0", "amount")!; Assert.Equal(PropertyKind.Choice, cell.Kind);
            var choices = cell.Choices.ToList(); Assert.Contains(choice, choices);
            Cell(1); Key(ImGuiKey.Enter); Key(ImGuiKey.Home);
            for (int i = 0; i < choices.IndexOf(choice); i++) Key(ImGuiKey.DownArrow);
            Key(ImGuiKey.Enter);
        }
        public JsonElement Value(string field) => Session.Current.At("/items/0/" + field)!.Value;
        public void Untouched()
        {
            Assert.Same(Before, Session.Current); Assert.Empty(Session.Journal);
            Assert.Equal(0, Session.History.UndoCount); Assert.False(Session.Dirty);
            Assert.Equal(Format.Write(Before), Format.Write(Session.Current));
        }
        public void CheckOneEditAndRoundTrip(string field)
        {
            var changed = Session.Current; Assert.Single(Session.Journal); Assert.Equal(1, Session.History.UndoCount);
            Assert.NotEqual(Before.At("/items/0/" + field)!.Value.GetRawText(), Value(field).GetRawText());
            Assert.Equal(Before.At("/items/1")!.Value.GetRawText(), changed.At("/items/1")!.Value.GetRawText());
            Assert.Equal(Before.At("/targets")!.Value.GetRawText(), changed.At("/targets")!.Value.GetRawText());
            foreach (var property in Before.At("/items/0")!.Value.EnumerateObject().Where(p => p.Name != field))
                Assert.Equal(property.Value.GetRawText(), Value(property.Name).GetRawText());
            Assert.True(Session.Undo()); Assert.Same(Before, Session.Current);
            Assert.True(Session.Redo()); Assert.Same(changed, Session.Current);
            var path = Path.Combine(Dir, "saved.json"); Session.Save(path);
            var reopened = DataKit.Session(Format.Read(File.ReadAllText(path)), Schemas, "table");
            Assert.Equal(Format.Write(changed), Format.Write(reopened.Current));
            Host.Frames(Draw, 2);
        }
        public void Dispose()
        {
            Host.Dispose();
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "a134-"));
            if (!Path.GetFullPath(Dir).StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected private fixture path.");
            Directory.Delete(Dir, true);
        }
    }
}
