using System.Text;
using System.Text.Json;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

public sealed class AiConnectTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "bed-connect-" + Guid.NewGuid().ToString("N"));
    private readonly AiConnectionLocations locations;
    private readonly AiConnectionFiles files;
    public AiConnectTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "bed-cli.exe"), "fake executable, never run");
        locations = new(Path.Combine(root, "profile"), Path.Combine(root, "roaming"));
        files = new(AiSetupSnippets.Create(root, new ProjectPaths(Path.Combine(root, "project data"), Path.Combine(root, "schemas"))));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Preview_does_not_create_config_and_explicit_apply_can_be_undone(int app)
    {
        var target = locations.Targets[app];
        var change = files.Preview(target);
        Assert.True(change.CanApply, change.Refusal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(target.File)));
        Assert.Contains("bed-cli.exe", change.Preview);
        Assert.True(files.Apply(change).Success);
        var added = File.ReadAllBytes(target.File);
        if (!target.Toml)
        {
            using var json = JsonDocument.Parse(added);
            var entry = json.RootElement.GetProperty("mcpServers").GetProperty("bed");
            Assert.EndsWith("bed-cli.exe", entry.GetProperty("command").GetString());
            Assert.Equal("mcp", entry.GetProperty("args").EnumerateArray().Last().GetString());
            if (target.ExplicitStdio) Assert.Equal("stdio", entry.GetProperty("type").GetString());
        }
        var undo = files.Preview(target);
        Assert.True(undo.Removing);
        Assert.True(files.Apply(undo).Success);
        Assert.Equal(added, File.ReadAllBytes(undo.Backup!));
        Assert.DoesNotContain("bed-cli.exe", File.ReadAllText(target.File));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void Json_keeps_existing_entries_comments_secrets_and_backup(int app)
    {
        var target = locations.Targets[app];
        const string original = "{\r\n// , keep this\r\n\"secret\": \"hidden\", \"mcpServers\": {\"bed\": {\"command\":\"other\"},},\r\n}";
        Write(target, original);
        var add = files.Preview(target);
        Assert.DoesNotContain("hidden", add.Preview);
        Assert.Contains("bed-2", add.Preview);
        Assert.True(files.Apply(add).Success);
        Assert.Equal(original, File.ReadAllText(add.Backup!));
        Assert.Contains("// , keep this\r\n", File.ReadAllText(target.File));
        var undo = files.Preview(target);
        Assert.True(files.Apply(undo).Success);
        using var json = JsonDocument.Parse(File.ReadAllText(target.File), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        Assert.Equal("hidden", json.RootElement.GetProperty("secret").GetString());
        Assert.Single(json.RootElement.GetProperty("mcpServers").EnumerateObject());
    }
    [Theory]
    [InlineData("[]")] [InlineData("{bad}")] [InlineData("{\"mcpServers\":null}")] [InlineData("{\"a\":1,\"a\":2}")]
    public void Bad_json_is_refused_without_a_write(string text)
    {
        var target = locations.Targets[0]; Write(target, text);
        var change = files.Preview(target); Assert.False(change.CanApply);
        if (text == "{bad}") Assert.Equal("The settings file could not be read. Fix it in the AI app, then refresh the preview.", change.Refusal);
        Assert.False(files.Apply(change).Success); Assert.Equal(text, File.ReadAllText(target.File));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target.File)!));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Config_changed_since_preview_is_never_overwritten(bool existing)
    {
        var target = locations.Targets[0]; if (existing) Write(target, "{}");
        var change = files.Preview(target); Write(target, "{\"new\":true}");
        Assert.False(files.Apply(change).Success);
        Assert.Equal("{\"new\":true}", File.ReadAllText(target.File));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target.File)!));
    }
    [Fact]
    public void Codex_preserves_multiline_prompt_and_occupied_name()
    {
        var target = locations.Targets[2];
        const string original = "instructions = '''\n[mcp_servers.bed-2]\n# BED connection bed\n'''\n[mcp_servers.bed]\ncommand = 'other'\n";
        Write(target, original);
        var change = files.Preview(target); Assert.Contains("[mcp_servers.bed-2]", change.Preview);
        Assert.True(files.Apply(change).Success); Assert.StartsWith(original, File.ReadAllText(target.File));
        Assert.True(files.Apply(files.Preview(target)).Success); Assert.Equal(original, File.ReadAllText(target.File));
    }
    [Theory]
    [InlineData("mcp_servers = {}")]
    [InlineData("[mcp_servers]\nbed = {}")]
    [InlineData("instructions = '''unfinished")]
    public void Complex_or_incomplete_toml_is_refused(string text)
    {
        var target = locations.Targets[2]; Write(target, text);
        Assert.False(files.Preview(target).CanApply); Assert.Equal(text, File.ReadAllText(target.File));
    }
    [Fact]
    public void Bom_is_retained_and_custom_codex_home_is_used()
    {
        var custom = new AiConnectionLocations(root, root, Path.Combine(root, "custom"));
        Assert.Equal(Path.Combine(root, "custom", "config.toml"), custom.Targets[2].File);
        var target = locations.Targets[0]; Directory.CreateDirectory(Path.GetDirectoryName(target.File)!);
        File.WriteAllBytes(target.File, [0xef,0xbb,0xbf, ..Encoding.UTF8.GetBytes("{}")]);
        Assert.True(files.Apply(files.Preview(target)).Success);
        Assert.Equal(new byte[] {0xef,0xbb,0xbf}, File.ReadAllBytes(target.File)[..3]);
    }
    [Fact]
    public void Removing_a_codex_entry_with_added_settings_is_refused()
    {
        var target = locations.Targets[2];
        Assert.True(files.Apply(files.Preview(target)).Success);
        File.AppendAllText(target.File, "env = { TOKEN = 'keep' }\n");
        var before = File.ReadAllBytes(target.File);
        var removal = files.Preview(target);
        Assert.False(removal.CanApply);
        Assert.Equal(before, File.ReadAllBytes(target.File));
    }
    private static void Write(AiConnectionTarget target, string text)
    { Directory.CreateDirectory(Path.GetDirectoryName(target.File)!); File.WriteAllText(target.File, text); }
    public void Dispose() => Directory.Delete(root, true);
}
