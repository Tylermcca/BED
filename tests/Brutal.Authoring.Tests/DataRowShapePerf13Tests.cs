using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class DataRowShapePerf13Tests
{
    [Fact]
    public void Row_descriptions_keep_each_instances_active_branch_and_invalidate_after_schema_replacement()
    {
        const string schema="""{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","properties":{"mode":{"type":"string"}},"if":{"properties":{"mode":{"const":"hot"}}},"then":{"properties":{"value":{"type":"integer","title":"Hot value","minimum":10,"default":10}}},"else":{"properties":{"value":{"type":"number","title":"Cold value","maximum":3,"description":"Cold help"}}}}}}}""";
        var schemas=new JsonSchemas().Add("s",schema);var type=DataEditing.ObjectType(schemas,"s");
        var doc=new DataFormat().Read("""{"rows":[{"mode":"hot","value":12},{"mode":"cold","value":2.5}]}""");
        var hot=Assert.Single(type.Rows(doc,["/rows/0"]),r=>r.Id=="value");
        Assert.Equal("Hot value",hot.Label);Assert.Equal(PropertyKind.Integer,hot.Kind);Assert.Equal(12L,hot.Value);Assert.True(hot.Overridden);Assert.Equal(10,hot.Range!.Min);
        var cold=Assert.Single(type.Rows(doc,["/rows/1"]),r=>r.Id=="value");
        Assert.Equal("Cold value",cold.Label);Assert.Equal(PropertyKind.Number,cold.Kind);Assert.Equal(2.5d,cold.Value);Assert.False(cold.Overridden);Assert.Equal(3,cold.Range!.Max);Assert.Equal("Cold help",cold.Help);
        Assert.Equal(hot,Assert.Single(type.Rows(doc,["/rows/0"]),r=>r.Id=="value"));
        schemas.Add("s",schema.Replace("Hot value","Heated value").Replace("\"minimum\":10","\"minimum\":11"));
        var changed=Assert.Single(type.Rows(doc,["/rows/0"]),r=>r.Id=="value");Assert.Equal("Heated value",changed.Label);Assert.Equal(11,changed.Range!.Min);
    }
    [Fact]
    public void Untyped_and_missing_values_keep_per_instance_kinds_and_list_add_rules()
    {
        var schemas=new JsonSchemas().Add("s","""{"properties":{"rows":{"items":{"properties":{"value":{},"list":{"type":"array"},"nullable":{"default":null}}}}}}""");
        var type=DataEditing.ObjectType(schemas,"s");var doc=new DataFormat().Read("""{"rows":[{"value":2},{"value":true},{"value":"two","nullable":null}]}""");
        var number=Assert.Single(type.Rows(doc,["/rows/0"]),r=>r.Id=="value");Assert.Equal(PropertyKind.Number,number.Kind);Assert.Equal(2d,number.Value);
        var toggle=Assert.Single(type.Rows(doc,["/rows/1"]),r=>r.Id=="value");Assert.Equal(PropertyKind.Toggle,toggle.Kind);Assert.Equal(true,toggle.Value);
        var text=Assert.Single(type.Rows(doc,["/rows/2"]),r=>r.Id=="value");Assert.Equal(PropertyKind.Text,text.Kind);Assert.Equal("two",text.Value);
        var list=Assert.Single(type.Rows(doc,["/rows/2"]),r=>r.Id=="list");Assert.False(list.Editable);Assert.True(list.CanAdd);Assert.Null(list.Value);
        var nullable=Assert.Single(type.Rows(doc,["/rows/2"]),r=>r.Id=="nullable");Assert.Equal("null",nullable.Value);Assert.False(nullable.Editable);Assert.False(nullable.Overridden);
        var missing=Assert.Single(type.Rows(doc,["/rows/0"]),r=>r.Id=="nullable");Assert.Null(missing.Value);Assert.True(missing.Editable);Assert.False(missing.Overridden);
    }
    [Fact]
    public void Choice_intersections_and_exact_long_values_survive_repeated_rows_and_multi_selection()
    {
        var schemas=new JsonSchemas().Add("s","""{"properties":{"rows":{"items":{"properties":{"value":{"type":"integer"},"choice":{"allOf":[{"enum":["a","b"]},{"enum":["b","c"]}]}}}}}}""");
        var type=DataEditing.ObjectType(schemas,"s");var doc=new DataFormat().Read("""{"rows":[{"value":9007199254740993,"choice":"b"},{"value":9007199254740992,"choice":"b"}]}""");
        foreach(var index in new[]{0,1,0})
        {
            var rows=type.Rows(doc,[$"/rows/{index}"]);Assert.Equal(9007199254740993L-index,Assert.Single(rows,r=>r.Id=="value").Value);
            var choice=Assert.Single(rows,r=>r.Id=="choice");Assert.Equal(PropertyKind.Choice,choice.Kind);Assert.Equal(new[]{"b"},choice.Choices);Assert.Equal("b",choice.Value);
        }
        var multi=type.Rows(doc,["/rows/0","/rows/1"]);Assert.True(Assert.Single(multi,r=>r.Id=="value").Mixed);Assert.False(Assert.Single(multi,r=>r.Id=="choice").Mixed);
    }
    [Fact]
    public void Field_conditionals_through_refs_keep_value_specific_titles_and_missing_branch()
    {
        var schemas = new JsonSchemas().Add("s", """
            {"properties":{"rows":{"items":{"properties":{"value":{"$ref":"#/$defs/value"}}}}},
             "$defs":{"value":{"if":{"type":"integer"},"then":{"type":"integer","title":"Whole"},
                                "else":{"type":"string","title":"Word"}}}}
            """);
        var type = DataEditing.ObjectType(schemas, "s");
        var doc = new DataFormat().Read("""{"rows":[{"value":2},{"value":"two"},{}, {"value":3}]}""");
        foreach (var (index, label, kind) in new[] { (0, "Whole", PropertyKind.Integer), (1, "Word", PropertyKind.Text),
                     (2, "value", PropertyKind.Text), (3, "Whole", PropertyKind.Integer), (1, "Word", PropertyKind.Text) })
        {
            var row = Assert.Single(type.Rows(doc, [$"/rows/{index}"]));
            Assert.Equal(label, row.Label); Assert.Equal(kind, row.Kind);
            if (index == 2) Assert.Null(row.Value);
        }
    }

    [Fact]
    public void Field_alternatives_do_not_reuse_another_values_kind_or_nested_summary()
    {
        var schemas = new JsonSchemas().Add("s", """
            {"properties":{"rows":{"items":{"properties":{"value":{"anyOf":[{"type":"integer"},{"type":"string"},
                {"type":"object","properties":{"x":{"type":"boolean"}},"required":["x"],"title":"Record"}]}}}}}}
            """);
        var type = DataEditing.ObjectType(schemas, "s");
        var doc = new DataFormat().Read("""{"rows":[{"value":5},{"value":"five"},{"value":{"x":true}},{"value":6}]}""");
        var whole = Assert.Single(type.Rows(doc, ["/rows/0"])); Assert.Equal(PropertyKind.Integer, whole.Kind); Assert.Equal(5L, whole.Value);
        var word = Assert.Single(type.Rows(doc, ["/rows/1"])); Assert.Equal(PropertyKind.Text, word.Kind); Assert.Equal("five", word.Value);
        var record = Assert.Single(type.Rows(doc, ["/rows/2"])); Assert.Equal("Record", record.Label); Assert.Equal("(1 fields)", record.Value); Assert.False(record.Editable);
        Assert.Equal(6L, Assert.Single(type.Rows(doc, ["/rows/3"])).Value);
    }

    [Fact]
    public void Reused_shapes_keep_actual_fields_map_permissions_and_current_values_after_edit_and_undo()
    {
        var schemas = new JsonSchemas().Add("s", """
            {"properties":{"rows":{"items":{"properties":{"fixed":{"type":"integer"}},
              "additionalProperties":{"type":"string","title":"Entry"}}}}}
            """);
        var type = DataEditing.ObjectType(schemas, "s");
        var doc = new DataFormat().Read("""{"rows":[{"fixed":1,"extra":"one"},{"fixed":2,"other":"two"},{"extra":"three","fixed":3}]}""");
        foreach (var index in new[] { 0, 1, 2, 0 })
        {
            var rows = type.Rows(doc, [$"/rows/{index}"]);
            Assert.Equal(new[] { "fixed", index == 1 ? "other" : "extra" }, rows.Select(r => r.Id));
            Assert.False(rows[0].CanRemove); Assert.True(rows[1].CanRemove); Assert.Equal("Entry", rows[1].Label);
            Assert.Equal((long)index + 1, rows[0].Value);
        }
        var session = DataKit.Session(doc, schemas, "s");
        Assert.True(session.Execute(new SetValue("/rows/0/fixed", System.Text.Json.JsonSerializer.SerializeToElement(9))));
        Assert.Equal(9L, type.Rows(session.Current, ["/rows/0"])[0].Value);
        Assert.True(session.Undo()); Assert.Equal(1L, type.Rows(session.Current, ["/rows/0"])[0].Value);
    }
}
