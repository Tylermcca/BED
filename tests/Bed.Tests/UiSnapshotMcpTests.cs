using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

public sealed class UiSnapshotMcpTests
{
    [Fact]
    public void Read_only_provider_returns_latest_snapshot_without_actions()
    {
        var root = Directory.CreateTempSubdirectory("snapshot-mcp-");
        try
        {
            var data = root.CreateSubdirectory("data"); var schemas = root.CreateSubdirectory("schemas");
            var session = new AgentSession(DataProject.Open(data.FullName, schemas.FullName));
            var calls = 0;
            var snapshot = new JsonObject { ["frame"] = 5, ["nodes"] = new JsonArray() };
            var server = new McpServer(session, uiSnapshot: () => { calls++; return snapshot; });
            var response = Call(server, new JsonObject());
            Assert.False(response["isError"]!.GetValue<bool>());
            Assert.Equal(5, Payload(response)["frame"]!.GetValue<int>());
            snapshot["frame"] = 6;
            Assert.Equal(6, Payload(Call(server, new JsonObject()))["frame"]!.GetValue<int>());
            Assert.Equal(2, calls);
            foreach (var field in new[] { "click", "type", "key", "select", "script" })
            {
                Assert.True(Call(server, new JsonObject { [field] = "unsafe" })["isError"]!.GetValue<bool>());
                Assert.Equal(2, calls);
            }
            Assert.Empty(data.GetFiles());
            var unavailable = Call(new McpServer(session), new JsonObject());
            Assert.True(unavailable["isError"]!.GetValue<bool>());
            Assert.Contains("unavailable", Payload(unavailable)["error"]!.GetValue<string>());
        }
        finally { root.Delete(true); }
    }

    private static JsonObject Payload(JsonObject response) => JsonNode.Parse(response["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
    private static JsonObject Call(McpServer server, JsonObject args) => server.Handle(new JsonObject
    {
        ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
        ["params"] = new JsonObject { ["name"] = "ui_snapshot", ["arguments"] = args }
    }.ToJsonString())!["result"]!.AsObject();
}
