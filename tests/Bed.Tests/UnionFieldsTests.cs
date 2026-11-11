using Bed.Data;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

/// <summary>A field's choices, limits and help inside anyOf/oneOf. Found by A129 on a game's data: an owner showed no choices, and an effect's count no limit, so a bad value was refused only with "matches none of the allowed shapes".</summary>
public sealed class UnionFieldsTests
{
    private const string Schema = """
        {"type":"object","$defs":{"faction":{"enum":["navy","traders"]}},"properties":{
          "owner":{"description":"Who holds it.","anyOf":[{"$ref":"#/$defs/faction"},{"enum":["none","pirates"]}]},
          "effect":{"oneOf":[
            {"type":"object","properties":{"stat":{"enum":["hp","speed"]},"n":{"type":"integer","minimum":2}},"required":["stat","n"],"additionalProperties":false},
            {"type":"object","properties":{"multiply":{"type":"number","exclusiveMinimum":0,"maximum":10}},"required":["multiply"],"additionalProperties":false}]},
          "either":{"anyOf":[{"type":"object","properties":{"k":{"type":"integer","maximum":5}}},{"type":"object","properties":{"k":{"type":"integer","maximum":9}}}]},
          "anchors":{"type":"array","items":{"type":"number","description":"Height in metres."}}
        }}
        """;

    private static (DocumentSession<DataDocument> Session, EditorModel<DataDocument> Editor, Inspector<DataDocument> Inspector) Rig(string text)
    {
        var schemas = new JsonSchemas().Add("doc", Schema);
        var session = DataKit.Session(new DataFormat().Read(text), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (session, editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc")));
    }

    [Fact]
    public void A_choice_split_across_alternatives_offers_them_all_and_keeps_its_help()
    {
        var (session, editor, inspector) = Rig("""{"owner":"navy"}""");
        Assert.True(editor.Select(""));
        var owner = inspector.Rows().Single(r => r.Id == "owner");
        Assert.Equal(PropertyKind.Choice, owner.Kind);
        Assert.Equal(["navy", "traders", "none", "pirates"], owner.Choices);
        Assert.Equal("Who holds it.", owner.Help);
        Assert.True(inspector.Set("owner", "pirates"), string.Join(" ", inspector.LastIssues));
        Assert.Equal("pirates", session.Current.At("/owner")!.Value.GetString());
        Assert.False(inspector.Set("owner", "aliens"));
    }

    [Fact]
    public void The_alternative_a_record_matches_gives_its_fields_choices_and_limits()
    {
        var (session, editor, inspector) = Rig("""{"effect":{"stat":"hp","n":3}}""");
        Assert.True(editor.Select("/effect"));
        var rows = inspector.Rows();
        var stat = rows.Single(r => r.Id == "stat");
        Assert.Equal(PropertyKind.Choice, stat.Kind);
        Assert.Equal(["hp", "speed"], stat.Choices);
        var n = rows.Single(r => r.Id == "n");
        Assert.Equal(PropertyKind.Integer, n.Kind);
        Assert.Equal(new NumberRange(2, false, null, false), n.Range);
        Assert.True(inspector.Set("n", 4), string.Join(" ", inspector.LastIssues));
        Assert.Equal(4, session.Current.At("/effect/n")!.Value.GetInt32());
        Assert.False(inspector.Set("n", 1));
        Assert.False(inspector.Set("n", 4.5));   // not rounded to a whole number (A129)
        Assert.False(inspector.Set("n", "4.5"));
        Assert.True(inspector.Set("n", 5.0), string.Join(" ", inspector.LastIssues));
        Assert.Equal(5, session.Current.At("/effect/n")!.Value.GetInt32());
        var (_, other, otherInspector) = Rig("""{"effect":{"multiply":2}}""");
        Assert.True(other.Select("/effect"));
        Assert.Equal(new NumberRange(0, true, 10, false), otherInspector.Rows().Single(r => r.Id == "multiply").Range);
    }

    [Fact]
    public void When_several_alternatives_match_no_limit_is_guessed()
    {
        var (_, editor, inspector) = Rig("""{"either":{"k":1}}""");
        Assert.True(editor.Select("/either"));
        Assert.Null(inspector.Rows().Single(r => r.Id == "k").Range);
    }

    [Fact]
    public void A_list_item_shows_its_help()
    {
        var (_, editor, inspector) = Rig("""{"anchors":[1.5,2]}""");
        Assert.True(editor.Select("/anchors"));
        Assert.All(inspector.Rows(), r => Assert.Equal("Height in metres.", r.Help));
    }

    // A132 found a reference that only refers to itself overflowed the stack, even in an alternative nobody uses.
    [Fact]
    public void A_reference_loop_is_a_schema_fault_not_a_crash()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","$defs":{"loop":{"$ref":"#/$defs/loop"}},"properties":{"v":{"anyOf":[{"const":"none"},{"$ref":"#/$defs/loop"}]},"w":{"$ref":"#/$defs/loop"}}}""");
        var doc = new DataFormat().Read("""{"v":"none"}""");
        Assert.NotNull(schemas.At(doc.Root, "doc", "/v"));
        Assert.Empty(new DataValidator(schemas, "doc").Validate(doc));
        var looped = new DataValidator(schemas, "doc").Validate(new DataFormat().Read("""{"w":1}"""));
        Assert.Contains(looped, i => i.Message.Contains("loop", StringComparison.Ordinal));
    }

    // A legitimately recursive schema still checks deep data.
    [Fact]
    public void A_recursive_schema_still_checks_deep_data()
    {
        var schemas = new JsonSchemas().Add("doc", """{"$defs":{"node":{"type":"object","properties":{"n":{"type":"integer"},"next":{"$ref":"#/$defs/node"}}}},"$ref":"#/$defs/node"}""");
        var deep = "{\"n\":1}";
        for (var i = 0; i < 40; i++) deep = $"{{\"n\":1,\"next\":{deep}}}";
        Assert.Empty(new DataValidator(schemas, "doc").Validate(new DataFormat().Read(deep)));
        Assert.NotEmpty(new DataValidator(schemas, "doc").Validate(new DataFormat().Read(deep.Replace("{\"n\":1}", "{\"n\":1.5}"))));
    }
}
