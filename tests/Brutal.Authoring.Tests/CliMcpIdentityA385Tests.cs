using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class CliMcpIdentityA385Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("mcp-identity-").FullName;
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData(null, "BED")]
    [InlineData("Local tools", "Local tools")]
    public void Initialize_uses_BED_default_and_preserves_explicit_name(string? custom, string expected)
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        var session = new AgentSession(DataProject.Open(data, schemas));
        var server = custom is null ? new McpServer(session) : new McpServer(session, custom);
        var answer = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""")!;
        Assert.Equal(1, answer["id"]!.GetValue<int>());
        Assert.Equal("2025-06-18", answer["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal(expected, answer["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(Brutal.Authoring.BuildVersion.Current, answer["result"]!["serverInfo"]!["version"]!.GetValue<string>());
        Assert.Contains("describe", answer["result"]!["instructions"]!.GetValue<string>());
        Assert.NotNull(answer["result"]!["capabilities"]!["tools"]);
    }
}
