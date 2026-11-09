using System.Text.Json;
using Brutal.Authoring.Agent.Cli;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiSnapshotCliTests
{
    [Fact]
    public void Start_screen_returns_completed_frame_and_honest_recording_limit()
    {
        var output = new StringWriter(); var errors = new StringWriter();
        Assert.Equal(0, Program.Run(["ui", "--json"], new StringReader(""), output, errors));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("frame").GetInt64() > 0);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("nodes").ValueKind);
        Assert.Contains("only recorded controls", errors.ToString());
        // No assertion of whole-screen coverage: unconverted shell controls are absent from this base's recorder.
    }

    [Theory]
    [InlineData("--save")]
    [InlineData("--read-only")]
    [InlineData("--script")]
    public void Unsupported_modes_refuse_before_a_host_is_created(string flag)
    {
        var output = new StringWriter(); var errors = new StringWriter();
        Assert.Equal(2, Program.Run(["ui", flag], new StringReader(""), output, errors));
        Assert.Equal("", output.ToString());
        Assert.Contains("ui accepts only", errors.ToString());
    }

    [Fact]
    public void Project_snapshot_preserves_source_bytes_and_does_not_create_local_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "a447-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data"); var schemas = Path.Combine(root, "schemas");
        const string original = "{\"name\":\"Invented recipe\"}";
        try
        {
            Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data, "recipe.json"), original);
            File.WriteAllText(Path.Combine(schemas, "recipe.schema.json"), "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray();
            var output = new StringWriter(); var errors = new StringWriter();
            Assert.Equal(0, Program.Run(["ui", "--data", data, "--schemas", schemas, "--json"], new StringReader(""), output, errors));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.True(json.RootElement.GetProperty("frame").GetInt64() > 0);
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("nodes").ValueKind);
            Assert.Equal(original, File.ReadAllText(Path.Combine(data, "recipe.json")));
            Assert.Equal(before, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray());
        }
        finally
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(root);
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("a447-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup boundary.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
