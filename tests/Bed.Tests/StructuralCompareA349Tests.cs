using System.Diagnostics;
using System.Text.Json;
using Bed.Data;
using Bed.Launcher;
using Xunit;

namespace Bed.Tests;

// Self-authored in-memory values only. Prepared, UNRUN; the helper is source-linked for internal access.
public sealed class StructuralCompareA349Tests
{
    private static DataDocument Doc(string json) => new DataFormat().Read(json);

    [Fact]
    public void Added_removed_changed_and_reordered_items_keep_both_places()
    {
        var before = Doc("""{"rows":[{"id":"a","name":"Alpha","n":1},{"id":"b","n":2}]}""");
        var after = Doc("""{"rows":[{"id":"c","n":3},{"id":"a","name":"Alpha","n":4}]}""");
        var oldText = before.Root.GetRawText(); var newText = after.Root.GetRawText();
        var result = StructuralCompare.Compare(before, after);
        Assert.True(result.Complete);
        Assert.Equal(3, result.Changes.Count);
        Assert.Contains(result.Changes, c => c.Kind == "changed" && c.BeforeAt == "/rows/0/n" && c.AfterAt == "/rows/1/n" && c.Before == "1" && c.After == "4");
        Assert.Contains(result.Changes, c => c.Kind == "removed" && c.BeforeAt == "/rows/1" && c.AfterAt is null);
        Assert.Contains(result.Changes, c => c.Kind == "added" && c.BeforeAt is null && c.AfterAt == "/rows/0");
        Assert.Equal(oldText, before.Root.GetRawText()); Assert.Equal(newText, after.Root.GetRawText());
    }

    [Fact]
    public void Keyed_order_and_object_property_order_do_not_create_false_changes()
    {
        var result = StructuralCompare.Compare(Doc("""{"rows":[{"id":"a","v":1},{"id":"b","v":2}]}"""),
            Doc("""{"rows":[{"v":2,"id":"b"},{"v":1.0,"id":"a"}]}"""));
        Assert.True(result.Complete); Assert.Empty(result.Changes);
        Assert.Contains(result.Lists, l => l.Key == "id" && l.Mode == "keyed; order ignored");
    }

    [Theory]
    [InlineData("""[{"id":"a"},{"id":"a"}]""")]
    [InlineData("""[{"id":"a"},{}]""")]
    [InlineData("""[{"id":12}]""")]
    public void Bad_id_never_silently_matches_or_falls_back(string rows)
    {
        var result = StructuralCompare.Compare(Doc("{\"rows\":" + rows + "}"), Doc("""{"rows":[{"id":"a"}]}"""));
        Assert.False(result.Complete); Assert.NotEmpty(result.Problems); Assert.Empty(result.Changes);
        Assert.Contains(result.Lists, l => l.Mode == "incomplete key");
    }

    [Fact]
    public void Explicit_key_and_explicit_positional_choice_are_distinct()
    {
        var a = Doc("""{"rows":[{"code":"a","v":1},{"code":"b","v":2}]}""");
        var b = Doc("""{"rows":[{"code":"b","v":2},{"code":"a","v":1}]}""");
        var positional = StructuralCompare.Compare(a, b);
        Assert.True(positional.Complete); Assert.NotEmpty(positional.Changes);
        Assert.Contains(positional.Lists, l => l.Mode.StartsWith("positional", StringComparison.Ordinal));
        var keyed = StructuralCompare.Compare(a, b, new Dictionary<string, string?> { ["/rows"] = "code" });
        Assert.True(keyed.Complete); Assert.Empty(keyed.Changes);
        var bad = StructuralCompare.Compare(a, b, new Dictionary<string, string?> { ["/rows"] = "missing" });
        Assert.False(bad.Complete); Assert.Empty(bad.Changes);
    }

    [Fact]
    public void Nested_shapes_escaped_paths_and_null_versus_absence_are_reported()
    {
        var result = StructuralCompare.Compare(Doc("""{"a/b":{"~x":null},"list":[1,2]}"""),
            Doc("""{"a/b":{},"list":[1,3,4]}"""));
        Assert.True(result.Complete); Assert.Equal(3, result.Changes.Count);
        Assert.Contains(result.Changes, c => c.Kind == "removed" && c.BeforeAt == "/a~1b/~0x" && c.Before == "null" && c.After is null);
        Assert.Contains(result.Changes, c => c.Kind == "changed" && c.AfterAt == "/list/1");
        Assert.Contains(result.Changes, c => c.Kind == "added" && c.AfterAt == "/list/2");
    }

    [Fact]
    public void Five_thousand_keyed_rows_compare_within_one_second()
    {
        // Future single bounded acceptance witness, not a measured result in this card.
        var rows = Enumerable.Range(0, 5000).Select(i => new { id = "item-" + i, value = i }).ToArray();
        var a = Doc(JsonSerializer.Serialize(new { rows }));
        var b = Doc(JsonSerializer.Serialize(new { rows = rows.Reverse().ToArray() }));
        var watch = Stopwatch.StartNew();
        var result = StructuralCompare.Compare(a, b);
        watch.Stop();
        Assert.True(result.Complete); Assert.Empty(result.Changes);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"5000-row comparison took {watch.Elapsed}.");
    }
}
