using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Agent.Cli;
using Bed.Data;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class AiShotTests
{
    [Fact]
    public void Sprite_sample_uses_one_persistent_headless_Play_process_and_returns_real_images_after_input()
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();using var observation=new AgentPlayObservation();
        var launch=new PlayLaunch(new(Path.Combine(copy.Root,"game","SceneStarter.exe"),copy.Root,["--project","."]),null,copy.Root,[]);
        var before=Directory.GetFiles(copy.Root,"*",SearchOption.AllDirectories).Order().ToArray();
        var hello=observation.Start(launch,new(copy.Settings),false);Assert.Equal(0,hello["frame"]!.GetValue<long>());Assert.False(hello.ContainsKey("error"));
        var first=observation.Request("shot");Assert.Equal(0,first["frame"]!.GetValue<long>());
        var moved=observation.Request("hold","right",.5);Assert.Equal(hello["processId"]!.GetValue<int>(),moved["processId"]!.GetValue<int>());Assert.Equal(30,moved["frame"]!.GetValue<long>());Assert.Equal(30,moved["advancedFrames"]!.GetValue<long>());
        Assert.NotEqual(first["description"]!.GetValue<string>(),moved["description"]!.GetValue<string>());
        Assert.NotEqual(first["sha256"]!.GetValue<string>(),moved["sha256"]!.GetValue<string>());
        var repeat=observation.Request("shot");Assert.Equal(30,repeat["frame"]!.GetValue<long>());Assert.Equal(moved["sha256"]!.GetValue<string>(),repeat["sha256"]!.GetValue<string>());
        Assert.Throws<ArgumentException>(()=>observation.Request("hold","global-key",1));
        Assert.Throws<ArgumentException>(()=>observation.Request("hold","right",double.NaN));
        Assert.Throws<ArgumentException>(()=>observation.Request("hold","right",6));
        Assert.Equal(30,observation.Request("shot")["frame"]!.GetValue<long>());
        var bytes=AgentPlayObservation.ValidatePng(moved["imageData"]!.GetValue<string>());Assert.True(bytes.Length>10000);
        Assert.DoesNotContain("passed",moved.ToJsonString(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before,Directory.GetFiles(copy.Root,"*",SearchOption.AllDirectories).Order());
        Assert.Contains("Stopped",observation.Request("stop")["status"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(()=>observation.Request("shot"));
    }
    [Theory]
    [InlineData("oversize")][InlineData("sequence")][InlineData("extra-field")]
    public async Task Malformed_sample_requests_stop_the_owned_headless_child(string mode)
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var child=new SupervisedProcess(new(Path.Combine(copy.Root,"game","SceneStarter.exe"),copy.Root,[]),1024,1500000,
            (line,error)=>{if(!error)ready.TrySetResult();},_=>{},new Dictionary<string,string>{{"BED_AI_OBSERVE","1"},{"BED_PROJECT",copy.Root}});
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var request=mode switch
        {
            "oversize"=>new string('x',513),
            "sequence"=>"{\"id\":2,\"op\":\"shot\",\"key\":null,\"seconds\":0}",
            _=>"{\"id\":1,\"op\":\"shot\",\"key\":null,\"seconds\":0,\"extra\":true}"
        };
        await child.Send(request);Assert.Equal(3,await child.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public void Unsupported_games_and_unapproved_external_commands_refuse()
    {
        var root=Directory.CreateTempSubdirectory("shot-refusal-");
        try
        {
            using var observation=new AgentPlayObservation();var approvals=new PlayApprovals(Path.Combine(root.FullName,"preferences"));
            var program=Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame.exe");
            var launch=new PlayLaunch(new(program,root.FullName,["wait"]),null,root.FullName,[]);
            Assert.Contains("Save",Assert.Throws<InvalidOperationException>(()=>observation.Start(launch,approvals,true)).Message);
            Assert.Contains("Needs your OK",Assert.Throws<InvalidOperationException>(()=>observation.Start(launch,approvals,false)).Message);
            approvals.Approve(launch,false);
            Assert.ThrowsAny<Exception>(()=>observation.Start(launch,approvals,false));
            Assert.Throws<InvalidOperationException>(()=>observation.Request("shot"));
        }
        finally{root.Delete(true);}
    }
    [Theory]
    [InlineData("not base64")] [InlineData("AAAA")]
    public void Invalid_images_are_refused(string data)=>Assert.ThrowsAny<Exception>(()=>AgentPlayObservation.ValidatePng(data));
    [Fact]
    public void Mcp_returns_native_image_content_and_rejects_invalid_keys_before_the_provider()
    {
        var root=Directory.CreateTempSubdirectory("bed-shot-mcp-");
        try
        {
            var project=DataProject.Open(root.CreateSubdirectory("data").FullName,root.CreateSubdirectory("schemas").FullName);var calls=0;
            var server=new McpServer(new AgentSession(project)){PlayObservation=(tool,args)=>{calls++;return new(){["status"]="Observed a game frame; no gameplay verdict.",["frame"]=30,["imageData"]="aW1hZ2U="};}};
            JsonObject Call(string name,JsonObject args)=>server.Handle(new JsonObject{["jsonrpc"]="2.0",["id"]=1,["method"]="tools/call",["params"]=new JsonObject{["name"]=name,["arguments"]=args}}.ToJsonString())!["result"]!.AsObject();
            Assert.True(Call("play_hold_key",new(){["key"]="desktop-hotkey",["seconds"]=1})["isError"]!.GetValue<bool>());Assert.Equal(0,calls);
            Assert.True(Call("play_hold_key",new(){["key"]="right",["seconds"]=0})["isError"]!.GetValue<bool>());Assert.Equal(0,calls);
            var result=Call("play_screenshot",new());Assert.False(result["isError"]!.GetValue<bool>());Assert.Equal(1,calls);
            Assert.Equal("image",result["content"]![1]!["type"]!.GetValue<string>());Assert.Equal("image/png",result["content"]![1]!["mimeType"]!.GetValue<string>());
            Assert.DoesNotContain("imageData",result["content"]![0]!["text"]!.GetValue<string>());
        }
        finally{root.Delete(true);}
    }
}
