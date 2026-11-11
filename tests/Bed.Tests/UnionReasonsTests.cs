using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>What a refusal inside anyOf/oneOf says, in A135's order (docs/plan/union_reasons.md), instead of "matches none of the N allowed shapes".</summary>
public sealed class UnionReasonsTests
{
    private static List<Issue> Check(string fieldSchema, string value, string defs = "{}")
    {
        var schemas = new JsonSchemas().Add("doc", "{\"type\":\"object\",\"$defs\":" + defs + ",\"properties\":{\"v\":" + fieldSchema + "}}");
        return new DataValidator(schemas, "doc").Validate(new DataFormat().Read("{\"v\":" + value + "}")).ToList();
    }

    private static Issue Only(string fieldSchema, string value, string defs = "{}") => Assert.Single(Check(fieldSchema, value, defs));

    [Fact]
    public void Fixed_choices_are_named_up_to_three_and_counted_beyond()
    {
        Assert.Equal("must be 'navy', 'traders' or 'none'", Only("""{"anyOf":[{"enum":["navy","traders"]},{"const":"none"}]}""", "\"aliens\"").Message);
        Assert.Equal("must be one of its 4 choices, such as 'navy'", Only("""{"anyOf":[{"enum":["navy","traders"]},{"enum":["none","pirates"]}]}""", "\"aliens\"").Message);
        Assert.Equal("must be 'alpha' or 'beta', or nothing", Only("""{"anyOf":[{"enum":["alpha","beta"]},{"type":"null"}]}""", "\"gamma\"").Message);
    }

    [Fact]
    public void A_selector_field_picks_the_form_and_its_own_problem_is_named()
    {
        const string effect = """{"oneOf":[{"type":"object","required":["kind","n"],"properties":{"kind":{"const":"stat"},"n":{"type":"integer","minimum":2}}},{"type":"object","required":["kind","by"],"properties":{"kind":{"const":"mul"},"by":{"type":"number"}}}]}""";
        var stat = Only(effect, """{"kind":"stat","n":1}""");
        Assert.Contains("2", stat.Message);
        Assert.Equal("$.v.n", stat.Path);
        Assert.Equal("kind must be 'stat' or 'mul'", Only(effect, """{"kind":"zzz"}""").Message);
    }

    [Fact]
    public void The_one_form_of_the_values_kind_explains_itself_and_never_the_one_with_fewer_errors()
    {
        // The record form has three missing fields; the text form only one kind error. The value is a record, so the record form speaks.
        var wrongNearest = Only("""{"anyOf":[{"type":"object","required":["a","b","c"]},{"type":"string"}]}""", "{}");
        Assert.Equal("'a' is required", wrongNearest.Message);
        Assert.Contains("2", Only("""{"anyOf":[{"type":"integer","minimum":2},{"type":"string","const":"none"}]}""", "1").Message);
    }

    [Fact]
    public void When_no_form_is_of_the_values_kind_the_kinds_are_named()
    {
        Assert.Equal("expected text or a number, found true", Only("""{"anyOf":[{"type":"string"},{"type":"number"}]}""", "true").Message);
    }

    [Fact]
    public void An_overlap_says_so_rather_than_picking_one()
    {
        Assert.Equal("fits 2 of its choices; it must fit exactly one", Only("""{"oneOf":[{"type":"number"},{"type":"integer"}]}""", "3").Message);
    }

    [Fact]
    public void A_fault_in_the_rules_comes_before_any_claim_about_the_value()
    {
        Assert.StartsWith("its rules couldn't be checked", Only("""{"anyOf":[{"$ref":"#/$defs/missing"},{"type":"string"}]}""", "1").Message);
    }

    [Fact]
    public void Several_plausible_forms_get_a_short_summary_and_every_message_is_one_short_line()
    {
        Assert.Equal("doesn't fit any of its 2 allowed forms", Only("""{"anyOf":[{"minimum":5},{"maximum":1}]}""", "3").Message);
        var longName = new string('x', 200);
        var issue = Only($$"""{"anyOf":[{"enum":["{{longName}}"]},{"const":"none"}]}""", "\"other\"");
        Assert.True(issue.Message.Length <= 96, issue.Message);
        Assert.DoesNotContain('\n', issue.Message);
    }
}
