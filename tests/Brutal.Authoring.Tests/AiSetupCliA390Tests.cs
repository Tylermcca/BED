using System.Text.Json;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using CliProgram = Brutal.Authoring.Agent.Cli.Program;

namespace Brutal.Authoring.Tests;

// Prepared only. Fixtures contain synthetic text, never executable adapter code or real project data.
public sealed class AiSetupCliA390Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("ai-setup-synthetic-").FullName;
    public void Dispose() => Directory.Delete(root, recursive: true);
    private string P(string name) => Path.Combine(root, name);
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        var code = CliProgram.Run(args, TextReader.Null, output, errors);
        return (code, output.ToString(), errors.ToString());
    }

    [Fact]
    public void Json_has_exact_four_formatter_strings_and_does_not_open_map_project_or_adapter()
    {
        var data = P("Acme O'Brien $value` data"); var schemas = P("missing schemas");
        var map = P("not a map.json"); var adapter = P("Acme Adapter.dll");
        File.WriteAllText(map, "synthetic invalid JSON, deliberately not a map");
        File.WriteAllText(adapter, "synthetic text, deliberately not an assembly");
        var mapBefore = File.ReadAllBytes(map); var adapterBefore = File.ReadAllBytes(adapter);
        var result = Run("ai-setup", "--data", data, "--schemas", schemas, "--map", map, "--adapter", adapter, "--json");
        Assert.Equal(0, result.Code); Assert.Equal("", result.Err);
        using var json = JsonDocument.Parse(result.Out);
        Assert.Equal(new[] { "claudeCode", "command", "desktopJson", "guidePath" },
            json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        var expected = AiSetupSnippets.Create(AppContext.BaseDirectory, new(data, schemas, map) { AdapterPath = adapter });
        Assert.Equal(expected.Command, json.RootElement.GetProperty("command").GetString());
        Assert.Equal(expected.DesktopJson, json.RootElement.GetProperty("desktopJson").GetString());
        Assert.Equal(expected.ClaudeCode, json.RootElement.GetProperty("claudeCode").GetString());
        Assert.Equal(expected.GuidePath, json.RootElement.GetProperty("guidePath").GetString());
        Assert.Contains("O''Brien $value`", expected.Command);
        using var desktop = JsonDocument.Parse(json.RootElement.GetProperty("desktopJson").GetString()!);
        Assert.Contains(data, desktop.RootElement.GetProperty("mcpServers").GetProperty("bed").GetProperty("args")
            .EnumerateArray().Select(v => v.GetString()));
        Assert.False(Directory.Exists(data)); Assert.False(Directory.Exists(schemas));
        Assert.Equal(mapBefore, File.ReadAllBytes(map)); Assert.Equal(adapterBefore, File.ReadAllBytes(adapter));
        Assert.Equal(2, Directory.GetFiles(root).Length);
    }

    [Fact]
    public void Plain_text_includes_the_same_examples_and_guide_without_path_existence_requirements()
    {
        var data = P("data not created"); var schemas = P("schemas not created");
        var result = Run("ai-setup", "--data", data, "--schemas", schemas);
        Assert.Equal(0, result.Code); Assert.Equal("", result.Err);
        var expected = AiSetupSnippets.Create(AppContext.BaseDirectory, new(data, schemas));
        Assert.Contains(expected.Command, result.Out); Assert.Contains(expected.DesktopJson, result.Out);
        Assert.Contains(expected.ClaudeCode, result.Out); Assert.Contains(expected.GuidePath, result.Out);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void Missing_adapter_is_an_exact_plain_refusal_without_snippets()
    {
        var missing = P("missing adapter.dll");
        var result = Run("ai-setup", "--data", P("data"), "--schemas", P("schemas"), "--adapter", missing, "--json");
        Assert.Equal(2, result.Code); Assert.Equal("", result.Out);
        Assert.Equal($"No adapter file at {missing}." + Environment.NewLine, result.Err);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData("--data")]
    [InlineData("--schemas")]
    public void Missing_required_path_is_refused(string given)
    {
        var result = Run("ai-setup", given, P("synthetic"));
        Assert.Equal(2, result.Code); Assert.Equal("", result.Out);
        Assert.Equal("ai-setup needs --data and --schemas." + Environment.NewLine, result.Err);
    }

    [Theory]
    [InlineData("--save")]
    [InlineData("--preview")]
    [InlineData("--read-only")]
    [InlineData("--unknown")]
    public void Unrelated_flags_cannot_turn_setup_into_an_action(string flag)
    {
        var result = Run("ai-setup", "--data", P("data"), "--schemas", P("schemas"), flag);
        Assert.Equal(2, result.Code); Assert.Equal("", result.Out);
        Assert.Contains("ai-setup accepts only", result.Err);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void Existing_global_option_order_before_call_is_supported()
    {
        var result = Run("--data", P("data"), "--schemas", P("schemas"), "ai-setup", "--json");
        Assert.Equal(0, result.Code); Assert.Equal("", result.Err);
        using var json = JsonDocument.Parse(result.Out);
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("command").ValueKind);
    }

    [Fact]
    public void Invalid_path_is_a_plain_format_refusal_not_a_project_open()
    {
        var result = Run("ai-setup", "--data", "invalid\0path", "--schemas", P("schemas"));
        Assert.Equal(2, result.Code); Assert.Equal("", result.Out);
        Assert.StartsWith("Cannot format AI setup paths:", result.Err);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void Help_has_capability_marker_without_changing_existing_help_exit_behavior()
    {
        var result = Run("--help");
        Assert.Equal(2, result.Code); Assert.Equal("", result.Out);
        Assert.Contains("ai-setup [--json]", result.Err);
        Assert.Contains("no project opening or adapter loading; writes no configuration", result.Err);
    }
}
