using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Remembered list items, past their limit: going over it costs some re-checks, not all of them.</summary>
public sealed class ValidationCacheTests
{
    private const string Shape = """
        {"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","required":["id"],"properties":{
          "id":{"type":"integer","minimum":0},"name":{"type":"string","maxLength":40},"more":{"type":"object","properties":{"n":{"type":"integer"}}}}}}}}
        """;

    private static DataDocument Rows(int count)
    {
        var text = new StringBuilder("""{"rows":[""");
        for (var i = 0; i < count; i++) text.Append(i > 0 ? "," : "").Append($"{{\"id\":{i},\"name\":\"row {i}\",\"more\":{{\"n\":1}}}}");
        return new DataDocument(JsonDocument.Parse(text.Append("]}").ToString()).RootElement.Clone());
    }

    private static long Time(Action run)
    {
        var clock = Stopwatch.StartNew();
        run();
        return clock.ElapsedMilliseconds;
    }

    [Theory]
    [InlineData(120_000)]   // over the limit, as the file that took 4 s was
    [InlineData(40_000)]    // well under it
    public void Checking_a_file_again_is_much_cheaper_than_the_first_time(int count)
    {
        var validator = new DataValidator(new JsonSchemas().Add("rows.schema.json", Shape), "rows.schema.json");
        var document = Rows(count);
        var first = Time(() => Assert.Empty(validator.Validate(document)));
        Assert.Empty(validator.Validate(document));   // settled, the way a file is after its first few edits
        var again = Enumerable.Range(0, 3).Min(_ => Time(() => Assert.Empty(validator.Validate(document))));
        Assert.True(again * 2 < first, $"{count} rows: first {first} ms, again {again} ms");
    }

    [Fact]
    public void A_bad_row_is_found_under_and_over_the_limit_and_a_remembered_answer_is_the_right_one()
    {
        var validator = new DataValidator(new JsonSchemas().Add("rows.schema.json", Shape), "rows.schema.json");
        var document = Rows(101_000);
        Assert.Empty(validator.Validate(document));
        Assert.Empty(validator.Validate(document));
        var bad = new DataDocument(JsonDocument.Parse("""{"rows":[{"id":1,"name":"fine"},{"id":-1}]}""").RootElement.Clone());
        var issues = validator.Validate(bad);
        Assert.Contains(issues, i => i.Path is { } p && p.Contains("[1]", StringComparison.Ordinal));
        Assert.Equal(issues.Count, validator.Validate(bad).Count);
    }
}
