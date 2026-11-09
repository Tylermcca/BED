using System.Globalization;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

// BORROWED: the previous eager implementation is a bounded result oracle, not a benchmark.
public sealed class DataSearchTraversalTests
{
    private static DataDocument Doc(string json, int maxDepth = 64)
    {
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maxDepth });
        return new DataDocument(parsed.RootElement.Clone());
    }

    [Theory]
    [InlineData("needle", -1)]
    [InlineData("needle", 0)]
    [InlineData("needle", 1)]
    [InlineData("needle", 2)]
    [InlineData("needle", 100)]
    [InlineData("TRUE", 1)]
    [InlineData("null", 1)]
    [InlineData("  ", 1)]
    public void Results_and_more_match_the_original_walk(string query, int limit)
    {
        var document = Doc("""{"needle":{"a/b~c":["NEEDLE",null,true,12.50]},"same":"needle","same":"last needle","empty":[]}""");
        var expected = EagerReference.Find(document, query, limit);
        var actual = DataSearch.Find(document, query, limit);
        Assert.Equal(expected.More, actual.More);
        Assert.Equal(expected.Places.ToArray(), actual.Places.ToArray());
    }

    [Fact]
    public void Preorder_preserves_container_hits_escaped_paths_and_duplicate_property_order()
    {
        var document = Doc("""{"needle":{"a/b~c":"needle"},"same":"needle","same":"last needle"}""");
        var result = DataSearch.Find(document, "needle");
        Assert.False(result.More);
        Assert.Equal(new[] {
            new FoundPlace("/needle", "/needle"),
            new FoundPlace("/needle/a~1b~0c", "/needle/a~1b~0c = needle"),
            new FoundPlace("/same", "/same = needle"),
            new FoundPlace("/same", "/same = last needle")
        }, result.Places.ToArray());
    }

    [Fact]
    public void Early_stop_does_not_change_later_searches_or_the_document()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("\"needle\"", 128)) + "]";
        var document = Doc(json);
        var first = DataSearch.Find(document, "needle", 1);
        Assert.True(first.More);
        Assert.Equal(new FoundPlace("/0", "/0 = needle"), Assert.Single(first.Places));
        var all = DataSearch.Find(document, "needle", 128);
        Assert.False(all.More);
        Assert.Equal(128, all.Places.Count);
        Assert.Equal("/127", all.Places[^1].Pointer);
        Assert.Equal(json, document.Root.GetRawText());
    }

    [Fact]
    public void Deep_walk_root_values_and_preview_truncation_keep_their_contracts()
    {
        var json = new string('[', 128) + "\"needle\"" + new string(']', 128);
        var deep = Assert.Single(DataSearch.Find(Doc(json, 256), "needle").Places);
        Assert.Equal(string.Concat(Enumerable.Repeat("/0", 128)), deep.Pointer);
        var value = new string('a', 56) + "\uD83D\uDE00" + new string('z', 10);
        var root = Doc(JsonSerializer.Serialize(value));
        Assert.Equal(new FoundPlace("", " = " + new string('a', 56) + "..."), Assert.Single(DataSearch.Find(root, "a").Places));
        Assert.False(DataSearch.Find(Doc("[]"), "needle", 0).More);
    }

    private static class EagerReference
    {
        /// <summary>The first <paramref name="limit"/> places holding <paramref name="text"/>, in document order, and whether there
        /// were more. Empty text finds nothing.</summary>
        public static (IReadOnlyList<FoundPlace> Places, bool More) Find(DataDocument document, string text, int limit = 100)
        {
            var found = new List<FoundPlace>();
            var wanted = text.Trim();
            if (wanted.Length == 0) return (found, false);
            var more = false;
            var stack = new Stack<(JsonElement Value, string At, string? Name)>();
            stack.Push((document.Root, "", null));
            while (stack.TryPop(out var here))
            {
                var (value, at, name) = here;
                var plain = Plain(value);
                var hit = (name is not null && name.Contains(wanted, StringComparison.OrdinalIgnoreCase)) || (plain is not null && plain.Contains(wanted, StringComparison.OrdinalIgnoreCase));
                if (hit)
                {
                    if (found.Count == limit) { more = true; break; }
                    found.Add(new FoundPlace(at, plain is null ? at : $"{at} = {Short(plain)}"));
                }
                if (value.ValueKind == JsonValueKind.Object)
                    foreach (var p in value.EnumerateObject().Reverse()) stack.Push((p.Value, at + Pointer.Join([p.Name]), p.Name));
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    var items = value.EnumerateArray().ToList();
                    for (var i = items.Count - 1; i >= 0; i--) stack.Push((items[i], at + "/" + i.ToString(CultureInfo.InvariantCulture), null));
                }
            }
            return (found, more);
        }
    
        private static string? Plain(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => null,
        };
    
        // Never through the middle of a surrogate pair, which can't be encoded (found by A096).
        private static string Short(string text)
        {
            if (text.Length <= 60) return text;
            return text[..(char.IsHighSurrogate(text[56]) ? 56 : 57)] + "...";
        }
    }
}
