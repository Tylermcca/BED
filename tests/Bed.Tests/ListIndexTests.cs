using System.Text.Json;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>Long lists are indexed in constant time (<c>ListIndex</c>, reached through <see cref="DataDocument.At"/>);
/// remembering a list's items must never hand back another list's.</summary>
public sealed class ListIndexTests
{
    private static string List(string tag, int n) =>
        "{\"rows\":[" + string.Join(",", Enumerable.Range(0, n).Select(i => $"{{\"id\":\"{tag}{i}\"}}")) + "]}";

    private static DataDocument Doc(string text) => new(JsonDocument.Parse(text).RootElement.Clone());

    [Fact]
    public void Lists_of_the_same_length_in_different_documents_are_never_confused()
    {
        var docs = Enumerable.Range(0, 6).Select(k => Doc(List("d" + k, 200))).ToList();
        for (var round = 0; round < 3; round++)
            foreach (var (d, k) in docs.Select((d, k) => (d, k)))
                foreach (var i in new[] { 0, 63, 64, 150, 199 })
                    Assert.Equal($"d{k}{i}", d.At($"/rows/{i}/id")!.Value.GetString());
    }

    [Fact]
    public void An_edited_document_sees_its_own_items()
    {
        var before = Doc(List("a", 100));
        Assert.Equal("a50", before.At("/rows/50/id")!.Value.GetString());
        var after = new SetValue("/rows/50/id", JsonSerializer.SerializeToElement("changed")).Apply(before).Document!;
        Assert.Equal("changed", after.At("/rows/50/id")!.Value.GetString());
        Assert.Equal("a50", before.At("/rows/50/id")!.Value.GetString());
        Assert.Null(after.At("/rows/100"));
        Assert.Null(after.At("/rows/-1"));
    }

    [Fact]
    public void A_disposed_document_is_never_matched()
    {
        using (var first = JsonDocument.Parse(List("x", 100)))
            Assert.Equal("x7", new DataDocument(first.RootElement).At("/rows/7/id")!.Value.GetString());
        using var second = JsonDocument.Parse(List("y", 100));   // may reuse the first one's pooled buffer
        Assert.Equal("y7", new DataDocument(second.RootElement).At("/rows/7/id")!.Value.GetString());
    }

    [Fact]
    public void The_schema_walk_reaches_late_rows_of_a_long_list()
    {
        var schemas = new JsonSchemas().Add("t.schema.json", """{"properties":{"rows":{"items":{"properties":{"id":{"type":"string","title":"Id"}}}}}}""");
        var d = Doc(List("r", 300));
        var at = Assert.Single(schemas.At(d.Root, "t.schema.json", "/rows/299/id"));
        Assert.Equal("Id", at.GetProperty("title").GetString());
    }
}
