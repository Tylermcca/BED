using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A nested object's fields are edited in the inspector, under the object that holds them: with their schema's
/// limits, one undo step at a time, and without changing the rows or the table for anyone else.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class NestedFieldsTests
{
    private static (DocumentSession<DataDocument> Session, Inspector<DataDocument> Inspector, EditorModel<DataDocument> Editor) Rig()
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
              "id":{"type":"string"},
              "price":{"type":"object","title":"Price","required":["min","max"],"properties":{
                "min":{"type":"integer","minimum":0,"description":"The lowest."},
                "max":{"type":"integer","minimum":0},
                "odd/key":{"type":"integer"},
                "deep":{"type":"object","properties":{"note":{"type":"string"}}},
                "tags":{"type":"array"}}}}}}}}
            """);
        var document = new DataFormat().Read("""
            {"items":[{"id":"ore","price":{"min":1,"max":9,"odd/key":3,"deep":{"note":"a"},"tags":[]}},
                      {"id":"tin","price":{"min":2,"max":8,"odd/key":4,"deep":{"note":"b"},"tags":[]}}]}
            """);
        var session = DataKit.Session(document, schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (session, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample")), editor);
    }

    private static InspectorRow Price(Inspector<DataDocument> inspector) => inspector.Rows().Single(r => r.Id == "price");

    [Fact]
    public void A_nested_objects_fields_come_from_its_schema_and_the_plain_rows_are_unchanged()
    {
        var (_, inspector, editor) = Rig();
        Assert.True(editor.Select("/items/0"));
        Assert.Equal(["id", "price"], inspector.Rows().Select(r => r.Id));   // nothing new at the top: tables are built from these
        Assert.False(Price(inspector).Editable);   // the object itself is still never overwritten by a typed value

        var inside = inspector.Nested(Price(inspector));
        Assert.Equal(["min", "max", "odd/key", "deep", "tags"], inside.Select(r => r.Label));
        var min = inside[0];
        Assert.Equal(1L, min.Value);
        Assert.True(min.Editable);
        Assert.Equal("The lowest.", min.Help);
        Assert.NotNull(min.Range);
        Assert.False(inside[4].Editable);   // a list stays a summary
        Assert.Empty(inspector.Nested(min));   // a plain value has nothing inside
    }

    [Fact]
    public void Editing_one_is_one_undo_step_and_the_schema_still_refuses()
    {
        var (session, inspector, editor) = Rig();
        Assert.True(editor.Select("/items/0"));
        var min = inspector.Nested(Price(inspector))[0];

        Assert.True(inspector.Set(min.Id, 5L));
        Assert.Equal(5, session.Current.At("/items/0/price/min")!.Value.GetInt32());
        Assert.Equal(9, session.Current.At("/items/0/price/max")!.Value.GetInt32());
        Assert.Equal(1, session.History.UndoCount);

        Assert.False(inspector.Set(min.Id, -1L));   // the schema's minimum
        Assert.NotEmpty(inspector.LastIssues);
        Assert.Equal(5, session.Current.At("/items/0/price/min")!.Value.GetInt32());

        Assert.True(session.Undo());
        Assert.Equal(1, session.Current.At("/items/0/price/min")!.Value.GetInt32());
        Assert.True(session.Redo());
        Assert.Equal(5, session.Current.At("/items/0/price/min")!.Value.GetInt32());
    }

    [Fact]
    public void A_key_with_a_slash_and_a_deeper_object_each_reach_the_right_place()
    {
        var (session, inspector, editor) = Rig();
        Assert.True(editor.Select("/items/0"));
        var inside = inspector.Nested(Price(inspector));
        Assert.True(inspector.Set(inside[2].Id, 7L));
        Assert.Equal(7, session.Current.Root.GetProperty("items")[0].GetProperty("price").GetProperty("odd/key").GetInt32());

        var deeper = Assert.Single(inspector.Nested(inside[3]));
        Assert.Equal("a", deeper.Value);
        Assert.True(inspector.Set(deeper.Id, "z"));
        Assert.Equal("z", session.Current.At("/items/0/price/deep/note")!.Value.GetString());
    }

    [Fact]
    public void Several_selected_objects_show_mixed_values_and_change_together_as_one_step()
    {
        var (session, inspector, editor) = Rig();
        Assert.True(editor.Select("/items/0", "/items/1"));
        var min = inspector.Nested(Price(inspector))[0];
        Assert.True(min.Mixed);
        Assert.True(inspector.Set(min.Id, 6L));
        Assert.Equal(6, session.Current.At("/items/0/price/min")!.Value.GetInt32());
        Assert.Equal(6, session.Current.At("/items/1/price/min")!.Value.GetInt32());
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void An_outside_key_of_the_same_spelling_as_a_nested_id_edits_only_the_top_level_field()
    {
        var (session, inspector, editor) = Rig();
        Assert.True(editor.Select("/items/0"));
        Assert.True(inspector.Set("id", "iron"));
        Assert.Equal("iron", session.Current.At("/items/0/id")!.Value.GetString());
        Assert.Equal(1, session.Current.At("/items/0/price/min")!.Value.GetInt32());
    }

    private static (DocumentSession<DataDocument> Session, Inspector<DataDocument> Inspector, EditorModel<DataDocument> Editor) ListRig(string json = """
        {"works":[{"id":"mill","methods":[
          {"id":"a","name":"Water","cycle":5,"inputs":[{"ware":"ore","qty":2}],"tags":["old","wet"]},
          {"id":"b","name":"Wind","cycle":9,"inputs":[],"tags":[]}]}]}
        """)
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","properties":{"works":{"type":"array","items":{"type":"object","properties":{
              "id":{"type":"string"},
              "methods":{"type":"array","maxItems":3,"items":{"type":"object","required":["id","cycle"],"properties":{
                "id":{"type":"string"},"name":{"type":"string"},"cycle":{"type":"number","exclusiveMinimum":0},
                "inputs":{"type":"array","maxItems":2,"items":{"type":"object","required":["ware","qty"],"properties":{
                  "ware":{"type":"string"},"qty":{"type":"number","minimum":1}}}},
                "tags":{"type":"array","items":{"type":"string"}}}}}}}}}}
            """);
        var session = DataKit.Session(new DataFormat().Read(json), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (session, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample")), editor);
    }

    private static InspectorRow Methods(Inspector<DataDocument> i) => i.Rows().Single(r => r.Id == "methods");

    [Fact]
    public void A_lists_items_open_under_it_down_to_their_own_fields_and_lists()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        Assert.True(Methods(inspector).CanAdd);
        var methods = inspector.Nested(Methods(inspector));
        Assert.Equal(["1. Water", "2. Wind"], methods.Select(r => r.Label));
        Assert.All(methods, r => Assert.True(r.CanRemove));

        var water = inspector.Nested(methods[0]);
        var cycle = water.Single(r => r.Label == "cycle");
        Assert.Equal(5.0, cycle.Value);
        Assert.NotNull(cycle.Range);
        Assert.True(inspector.Set(cycle.Id, 7.0));
        Assert.Equal(7, session.Current.At("/works/0/methods/0/cycle")!.Value.GetInt32());
        Assert.False(inspector.Set(cycle.Id, 0.0));   // the schema's limit, at this depth too

        var input = Assert.Single(inspector.Nested(water.Single(r => r.Label == "inputs")));
        var qty = inspector.Nested(input).Single(r => r.Label == "qty");
        Assert.True(inspector.Set(qty.Id, 4.0));
        Assert.Equal(4, session.Current.At("/works/0/methods/0/inputs/0/qty")!.Value.GetInt32());

        var tag = inspector.Nested(water.Single(r => r.Label == "tags"))[1];   // a plain value in a list is edited in place
        Assert.True(tag.Editable);
        Assert.True(inspector.Set(tag.Id, "dry"));
        Assert.Equal("dry", session.Current.At("/works/0/methods/0/tags/1")!.Value.GetString());
    }

    [Fact]
    public void Adding_copies_the_last_item_with_a_new_id_as_one_undo_step_and_the_schemas_limit_holds()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        Assert.True(inspector.AddItem(Methods(inspector)));
        Assert.Equal("b_2", session.Current.At("/works/0/methods/2/id")!.Value.GetString());
        Assert.Equal(9, session.Current.At("/works/0/methods/2/cycle")!.Value.GetInt32());
        Assert.Equal(1, session.History.UndoCount);

        Assert.False(inspector.AddItem(Methods(inspector)));   // maxItems 3
        Assert.NotEmpty(inspector.LastIssues);
        Assert.Equal(3, session.Current.At("/works/0/methods")!.Value.GetArrayLength());

        Assert.True(session.Undo());
        Assert.Equal(2, session.Current.At("/works/0/methods")!.Value.GetArrayLength());
    }

    [Fact]
    public void An_empty_list_starts_from_another_item_of_the_same_name()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        var wind = inspector.Nested(inspector.Nested(Methods(inspector))[1]);
        Assert.True(inspector.AddItem(wind.Single(r => r.Label == "inputs")));
        Assert.Equal("ore", session.Current.At("/works/0/methods/1/inputs/0/ware")!.Value.GetString());
    }

    [Fact]
    public void A_list_the_object_doesnt_have_yet_is_added_to_not_typed_into()
    {
        var (session, inspector, editor) = ListRig("""{"works":[{"id":"mill","methods":[{"id":"a","cycle":5,"inputs":[{"ware":"ore","qty":2}]},{"id":"b","cycle":9}]}]}""");
        Assert.True(editor.Select("/works/0"));
        var wind = inspector.Nested(inspector.Nested(Methods(inspector))[1]);
        var inputs = wind.Single(r => r.Label == "inputs");
        Assert.False(inputs.Editable);
        Assert.True(inputs.CanAdd);
        Assert.False(inspector.Set(inputs.Id, "text"));
        Assert.Null(session.Current.At("/works/0/methods/1/inputs"));

        Assert.True(inspector.AddItem(inputs));
        Assert.Equal("ore", session.Current.At("/works/0/methods/1/inputs/0/ware")!.Value.GetString());
        Assert.Equal(1, session.History.UndoCount);
        Assert.True(session.Undo());
        Assert.Null(session.Current.At("/works/0/methods/1/inputs"));   // undo takes the whole list away again
    }

    [Fact]
    public void Adding_to_a_list_of_choices_takes_the_first_it_doesnt_have()
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
              "kinds":{"type":"array","uniqueItems":true,"items":{"enum":["a","b","c"]}}}}}}}
            """);
        var session = DataKit.Session(new DataFormat().Read("""{"items":[{"kinds":["b"]}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        Assert.True(editor.Select("/items/0"));
        InspectorRow Kinds() => inspector.Rows().Single(r => r.Id == "kinds");
        Assert.True(inspector.AddItem(Kinds()));
        Assert.Equal("a", session.Current.At("/items/0/kinds/1")!.Value.GetString());
        Assert.True(inspector.AddItem(Kinds()));
        Assert.Equal("c", session.Current.At("/items/0/kinds/2")!.Value.GetString());
        Assert.False(inspector.AddItem(Kinds()));   // none left: a copy is refused by uniqueItems
        Assert.NotEmpty(inspector.LastIssues);
    }

    [Fact]
    public void Adding_to_a_list_of_names_gives_a_new_name_rather_than_a_refused_repeat()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        var tags = inspector.Nested(inspector.Nested(Methods(inspector))[0]).Single(r => r.Label == "tags");
        Assert.True(inspector.AddItem(tags));
        Assert.Equal("wet_2", session.Current.At("/works/0/methods/0/tags/2")!.Value.GetString());
    }

    [Fact]
    public void The_window_says_where_each_control_is_so_it_can_be_reached_by_name()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        using var host = new HeadlessHost(1400, 1400) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 5);

        var methods = Methods(inspector);
        var add = window.InspectorPlace(methods.Id, InspectorPart.Add);
        Assert.NotNull(add);
        host.Click(window.DrawDocked, add!.Value.X + 8, add.Value.Y + 6);
        host.Frames(window.DrawDocked, 3);
        Assert.Equal(3, session.Current.At("/works/0/methods")!.Value.GetArrayLength());   // a real click on the real button

        var water = inspector.Nested(Methods(inspector))[0];
        var remove = window.InspectorPlace(water.Id, InspectorPart.Remove);
        Assert.NotNull(remove);
        host.Click(window.DrawDocked, remove!.Value.X + 8, remove.Value.Y + 6);
        host.Frames(window.DrawDocked, 3);
        Assert.Equal("b", session.Current.At("/works/0/methods/0/id")!.Value.GetString());

        Assert.Null(window.InspectorPlace("no such row"));
        Assert.True(editor.Select("/works"));   // no rows for a list section
        host.Frames(window.DrawDocked, 2);
        Assert.Null(window.InspectorPlace(methods.Id, InspectorPart.Add));   // nothing stale
    }

    [Fact]
    public void A_long_list_is_drawn_a_page_at_a_time_and_a_new_item_shows_its_page()
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
              "tags":{"type":"array","items":{"type":"string"}}}}}}}
            """);
        var many = string.Join(",", Enumerable.Range(0, 120).Select(i => $"\"t{i}\""));
        var session = DataKit.Session(new DataFormat().Read($$"""{"items":[{"tags":[{{many}}]}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        InspectorRow Tags() => inspector.Rows().Single(r => r.Id == "tags");
        Assert.True(editor.Select("/items/0"));
        using var host = new HeadlessHost(1400, 2400) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 5);
        void Press(InspectorPart part, string rowId)
        {
            var at = window.InspectorPlace(rowId, part)!.Value;
            host.Click(window.DrawDocked, at.X + 8, at.Y + 6);
            host.Frames(window.DrawDocked, 3);
        }

        var arrow = window.InspectorPlace(Tags().Id)!.Value;
        host.Click(window.DrawDocked, arrow.X - 12, arrow.Y + 8);   // open it
        host.Frames(window.DrawDocked, 3);
        var items = inspector.Nested(Tags());
        Assert.NotNull(window.InspectorPlace(items[0].Id));
        Assert.NotNull(window.InspectorPlace(items[49].Id));
        Assert.Null(window.InspectorPlace(items[50].Id));

        Press(InspectorPart.Later, Tags().Id);
        Assert.Null(window.InspectorPlace(items[49].Id));
        Assert.NotNull(window.InspectorPlace(items[50].Id));
        Assert.NotNull(window.InspectorPlace(items[99].Id));

        Press(InspectorPart.Earlier, Tags().Id);
        Assert.NotNull(window.InspectorPlace(items[0].Id));

        Press(InspectorPart.Add, Tags().Id);
        var after = inspector.Nested(Tags());
        Assert.Equal(121, after.Count);
        Assert.NotNull(window.InspectorPlace(after[^1].Id));   // the page the new one's on
        Assert.Null(window.InspectorPlace(after[0].Id));
        Assert.Equal(1, session.History.UndoCount);

        Assert.True(session.Undo());
        host.Frames(window.DrawDocked, 3);
        Assert.NotNull(window.InspectorPlace(inspector.Nested(Tags())[119].Id));   // a list that shrank stays on a page it has
    }

    [Fact]
    public void A_long_list_jumps_to_its_ends_and_to_an_item_and_paging_through_it_holds_a_bounded_number_of_boxes()
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
              "tags":{"type":"array","items":{"type":"string"}}}}}}}
            """);
        var many = string.Join(",", Enumerable.Range(0, 8000).Select(i => $"\"t{i}\""));
        var session = DataKit.Session(new DataFormat().Read($$"""{"items":[{"tags":[{{many}}]}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        InspectorRow Tags() => inspector.Rows().Single(r => r.Id == "tags");
        Assert.True(editor.Select("/items/0"));
        using var host = new HeadlessHost(1400, 2400) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 5);
        void Press(InspectorPart part)
        {
            var at = window.InspectorPlace(Tags().Id, part)!.Value;
            host.Click(window.DrawDocked, at.X + 8, at.Y + 6);
            host.Frames(window.DrawDocked, 3);
        }
        var arrow = window.InspectorPlace(Tags().Id)!.Value;
        host.Click(window.DrawDocked, arrow.X - 12, arrow.Y + 8);
        host.Frames(window.DrawDocked, 3);
        var items = inspector.Nested(Tags());

        Press(InspectorPart.Last);
        Assert.NotNull(window.InspectorPlace(items[^1].Id));
        Press(InspectorPart.First);
        Assert.NotNull(window.InspectorPlace(items[0].Id));

        var box = window.InspectorPlace(Tags().Id, InspectorPart.GoTo)!.Value;
        host.Click(window.DrawDocked, box.X + 10, box.Y + 8);
        host.Frames(window.DrawDocked, 2);
        host.Type(window.DrawDocked, "5000");
        host.Press(window.DrawDocked, ImGuiKey.Enter);
        host.Frames(window.DrawDocked, 3);
        Assert.NotNull(window.InspectorPlace(items[4999].Id));

        Press(InspectorPart.First);
        for (var page = 0; page < 8000 / 50; page++)
        {
            var at = window.InspectorPlace(Tags().Id, InspectorPart.Later)!.Value;
            host.Click(window.DrawDocked, at.X + 8, at.Y + 6);
        }
        host.Frames(window.DrawDocked, 3);
        Assert.InRange(window.FieldsHeld, 1, 4096 + 60);   // not one for each of 8000
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_long_object_shows_the_fields_it_has_open_and_the_rest_are_a_button_away()
    {
        var fields = string.Join(",", Enumerable.Range(0, 80).Select(i => $"\"f{i}\":{{\"type\":\"number\"}}"));
        var schemas = new JsonSchemas().Add("sample", "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"details\":{\"type\":\"object\",\"properties\":{" + fields + "}}}}}}}");
        var session = DataKit.Session(new DataFormat().Read("""{"items":[{"details":{"f7":1,"f70":2}}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        Assert.True(editor.Select("/items/0"));
        using var host = new HeadlessHost(1400, 2400) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 5);

        var details = inspector.Rows().Single(r => r.Id == "details");
        var rows = inspector.Nested(details);
        Assert.Equal(80, rows.Count);
        InspectorRow Field(string name) => rows.Single(r => r.Id.EndsWith("/" + name, StringComparison.Ordinal));
        Assert.NotNull(window.InspectorPlace(Field("f7").Id));    // open at once
        Assert.NotNull(window.InspectorPlace(Field("f70").Id));   // and on the first page, wherever the schema lists it
        Assert.Null(window.InspectorPlace(Field("f8").Id));

        var button = window.InspectorPlace(details.Id, InspectorPart.Unset)!.Value;
        host.Click(window.DrawDocked, button.X + 8, button.Y + 6);
        host.Frames(window.DrawDocked, 3);
        Assert.NotNull(window.InspectorPlace(Field("f8").Id));   // nothing is out of reach
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void A_long_list_starts_folded_and_a_short_one_open()
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
              "tags":{"type":"array","items":{"type":"string"}}}}}}}
            """);
        var many = string.Join(",", Enumerable.Range(0, 60).Select(i => $"\"t{i}\""));
        var session = DataKit.Session(new DataFormat().Read($$"""{"items":[{"tags":[{{many}}]},{"tags":["a"]}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        InspectorRow Tags() => inspector.Rows().Single(r => r.Id == "tags");
        Assert.True(editor.Select("/items/0"));
        using var host = new HeadlessHost(1400, 1400) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 5);

        var items = inspector.Nested(Tags());
        Assert.Equal(60, items.Count);
        Assert.NotNull(window.InspectorPlace(Tags().Id, InspectorPart.Add));   // still reachable
        Assert.Null(window.InspectorPlace(items[0].Id));   // folded, so not drawn

        Assert.True(editor.Select("/items/1"));
        host.Frames(window.DrawDocked, 4);
        Assert.NotNull(window.InspectorPlace(Assert.Single(inspector.Nested(Tags())).Id));
    }

    [Fact]
    public void A_new_list_starts_from_another_list_described_by_the_same_definition()
    {
        var schemas = new JsonSchemas().Add("sample", """
            {"type":"object","$defs":{"amount":{"type":"object","required":["ware","qty"],"properties":{"ware":{"type":"string","minLength":1},"qty":{"type":"number"}}}},
             "properties":{"methods":{"type":"array","items":{"type":"object","properties":{
               "inputs":{"type":"array","items":{"$ref":"#/$defs/amount"}},
               "byproducts":{"type":"array","items":{"$ref":"#/$defs/amount"}},
               "notes":{"type":"array","items":{"type":"object","required":["ware"],"properties":{"ware":{"type":"string","minLength":1}}}}}}}}}
            """);
        var session = DataKit.Session(new DataFormat().Read("""{"methods":[{"inputs":[{"ware":"ore","qty":2}]},{}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        Assert.True(editor.Select("/methods/1"));

        Assert.True(inspector.AddItem(inspector.Rows().Single(r => r.Id == "byproducts")));
        Assert.Equal("ore", session.Current.At("/methods/1/byproducts/0/ware")!.Value.GetString());   // the same definition, another name

        Assert.False(inspector.AddItem(inspector.Rows().Single(r => r.Id == "notes")));   // a different one: nothing to copy, and the schema refuses an empty one
        Assert.Null(session.Current.At("/methods/1/notes"));
    }

    [Fact]
    public void Removing_an_item_is_one_undo_step_and_only_list_items_can_go()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        var water = inspector.Nested(Methods(inspector))[0];
        Assert.True(inspector.RemoveItem(water));
        Assert.Equal("b", session.Current.At("/works/0/methods/0/id")!.Value.GetString());
        Assert.True(session.Undo());
        Assert.Equal("a", session.Current.At("/works/0/methods/0/id")!.Value.GetString());

        var cycle = inspector.Nested(inspector.Nested(Methods(inspector))[0]).Single(r => r.Label == "cycle");
        Assert.False(cycle.CanRemove);
        Assert.False(inspector.RemoveItem(cycle));   // a field isn't a list item
        Assert.False(inspector.RemoveItem(Methods(inspector)));   // nor is a top-level field
    }

    [Fact]
    public void Several_selected_objects_show_no_list_items_to_avoid_editing_the_wrong_one()
    {
        var (_, inspector, editor) = ListRig("""{"works":[{"id":"x","methods":[]},{"id":"y","methods":[]}]}""");
        Assert.True(editor.Select("/works/0", "/works/1"));
        Assert.False(Methods(inspector).CanAdd);
        Assert.Empty(inspector.Nested(Methods(inspector)));
    }

    [Fact]
    public void Drawing_a_list_and_its_buttons_changes_nothing()
    {
        var (session, inspector, editor) = ListRig();
        Assert.True(editor.Select("/works/0"));
        using var host = new HeadlessHost(1400, 1100) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 4);
        Assert.Equal(2, session.Current.At("/works/0/methods")!.Value.GetArrayLength());   // drawing them changes nothing
    }

    [Fact]
    public void The_window_draws_them_and_a_focus_lock_makes_them_read_only()
    {
        var (_, inspector, editor) = Rig();
        Assert.True(editor.Select("/items/0"));
        using var host = new HeadlessHost(1200, 800) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 4);

        Assert.True(editor.FocusOn("/items/1"));
        Assert.All(inspector.Nested(Price(inspector)), r => Assert.False(r.Editable));
        host.Frames(window.DrawDocked, 2);
    }
}
