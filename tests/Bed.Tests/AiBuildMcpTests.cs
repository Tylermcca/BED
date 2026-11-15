using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;
namespace Bed.Tests;
public sealed class AiBuildMcpTests
{
    [Fact]
    public void Build_has_no_command_arguments_and_forwards_the_shared_result_without_implicit_actions()
    {
        var root=Directory.CreateTempSubdirectory("bed-build-mcp-");
        try
        {
            var data=root.CreateSubdirectory("data");var schemas=root.CreateSubdirectory("schemas");
            var session=new AgentSession(DataProject.Open(data.FullName,schemas.FullName));var calls=0;
            var server=new McpServer(session,build:()=>{calls++;return new(){["error"]="Build failed.",["errors"]=new JsonArray(new JsonObject{["file"]="Broken.cs",["line"]=1,["what"]="Unknown name",["try"]="Check spelling"})};});
            JsonObject Call(JsonObject args)=>server.Handle(new JsonObject{["jsonrpc"]="2.0",["id"]=1,["method"]="tools/call",["params"]=new JsonObject{["name"]="build",["arguments"]=args}}.ToJsonString())!["result"]!.AsObject();
            Assert.True(Call(new(){["command"]="something else"})["isError"]!.GetValue<bool>());Assert.Equal(0,calls);
            var response=Call(new());Assert.True(response["isError"]!.GetValue<bool>());Assert.Equal(1,calls);
            var payload=JsonNode.Parse(response["content"]![0]!["text"]!.GetValue<string>())!;
            Assert.Equal("Broken.cs",payload["errors"]![0]!["file"]!.GetValue<string>());
            Assert.Empty(data.GetFiles());Assert.Empty(schemas.GetFiles());
        }
        finally{root.Delete(true);}
    }
}
