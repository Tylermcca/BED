using Bed.Data;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

/// <summary>A number field's limits, read from its schema and shown before typing (S10: bounded controls).</summary>
public sealed class NumberRangeTests
{
    [Theory]
    [InlineData(0d, true, 5d, false, "0 < x <= 5")]
    [InlineData(0d, false, null, false, "x >= 0")]
    [InlineData(null, false, 1d, true, "x < 1")]
    [InlineData(-2.5d, false, 2.5d, false, "-2.5 <= x <= 2.5")]
    public void A_range_reads_as_a_person_writes_it(double? min, bool minEx, double? max, bool maxEx, string text) =>
        Assert.Equal(text, new NumberRange(min, minEx, max, maxEx).ToString());

    [Fact]
    public void Allows_follows_each_end_included_or_not()
    {
        var r = new NumberRange(0, true, 5, false);
        Assert.False(r.Allows(0));
        Assert.True(r.Allows(0.001));
        Assert.True(r.Allows(5));
        Assert.False(r.Allows(5.001));
    }

    [Fact]
    public void The_inspector_row_carries_the_schemas_tightest_limits()
    {
        var schemas = new JsonSchemas().Add("t.schema.json", """
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
          "thrust":{"type":"number","exclusiveMinimum":0,"maximum":5},
          "count":{"type":"integer","minimum":1,"allOf":[{"minimum":3}]},
          "name":{"type":"string","minLength":1},
          "free":{"type":"number"}}}}}}
        """);
        var doc = new DataFormat().Read("""{"items":[{"thrust":1,"count":4,"name":"a","free":2}]}""");
        var rows = DataEditing.ObjectType(schemas, "t.schema.json").Rows(doc, ["/items/0"]).ToDictionary(r => r.Id);
        Assert.Equal("0 < x <= 5", rows["thrust"].Range!.ToString());
        Assert.Equal("x >= 3", rows["count"].Range!.ToString());   // allOf's tighter minimum wins
        Assert.Null(rows["name"].Range);
        Assert.Null(rows["free"].Range);
    }
}
