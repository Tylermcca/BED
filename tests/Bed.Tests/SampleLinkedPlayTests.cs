using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram=Bed.Launcher.Program;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SampleLinkedPlayTests
{
    private const string SceneText="""{"kind":"scene","version":1,"objects":[{"id":"root","position":[2,3,4]},{"id":"child","parent":"root","position":[1,0,0],"references":{"anchor":"root"}}]}""";
    private static PlaySnapshot Snapshot(string text=SceneText)=>PlaySnapshot.Create(new("level.json",4,text,text),PlayInputChoice.Saved,
        input=>new(SceneStarter.LinkedScene.Format,Encoding.UTF8.GetBytes(input.Text))).Snapshot!;
    private static (PlaySnapshot Snapshot,PlayHello Hello,string Start,string Stop) Wire(string text=SceneText)
    {
        var snapshot=Snapshot(text);var hello=PlayProtocol.Hello(snapshot.Correlate(Guid.NewGuid()));
        return(snapshot,hello,PlayProcessWire.Start(hello,snapshot,1024*1024),PlayProcessWire.Stop(new(1,hello.Correlation)));
    }

    [Fact]
    public void Pure_core_sample_peer_loads_exact_saved_input_before_ready_then_acknowledges_stop()
    {
        var wire=Wire();using var input=new StringReader(wire.Start+"\n"+wire.Stop+"\n");
        using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(0,SceneStarter.LinkedScene.Run(input,output,error));Assert.Equal("",error.ToString());
        var lines=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        Assert.Equal(2,lines.Length);
        Assert.True(PlayProcessWire.TryRead(lines[0],8192,out var first,out var problem,wire.Hello.Correlation),problem?.Message);
        var ready=Assert.IsType<PlayReady>(first!.Message);Assert.True(PlayProtocol.Negotiate(wire.Hello,ready).Accepted);
        Assert.Equal(PlayProtocol.Required,ready.Supported);
        Assert.True(PlayProcessWire.TryRead(lines[1],8192,out var last,out problem,wire.Hello.Correlation),problem?.Message);
        Assert.IsType<PlayStopped>(last!.Message);
        Assert.DoesNotContain(typeof(SceneStarter.Program).Assembly.GetReferencedAssemblies(),name=>name.Name!.Contains("Editor",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("payload")] [InlineData("version")] [InlineData("choice")] [InlineData("required")] [InlineData("scene")]
    public void Invalid_or_unconverted_scene_never_reports_ready(string failure)
    {
        var wire=Wire(failure=="scene" ? "{\"kind\":\"scene\",\"version\":1,\"objects\":[{\"id\":\"child\",\"parent\":\"missing\"}]}" : SceneText);
        var start=JsonNode.Parse(wire.Start)!;
        if(failure=="payload") start["payload"]=Convert.ToBase64String(Encoding.UTF8.GetBytes(SceneText+" "));
        if(failure=="version") start["version"]=2;
        if(failure=="choice") start["choice"]="Draft";
        if(failure=="required") start["required"]=7;
        using var input=new StringReader(start.ToJsonString()+"\n"+wire.Stop+"\n");
        using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(3,SceneStarter.LinkedScene.Run(input,output,error));Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain("\"kind\":\"ready\"",output.ToString());
    }

    [Fact]
    public void Stale_stop_does_not_acknowledge_another_session()
    {
        var wire=Wire();var stop=JsonNode.Parse(wire.Stop)!;stop["session"]=Guid.NewGuid().ToString("N");
        using var input=new StringReader(wire.Start+"\n"+stop.ToJsonString()+"\n");
        using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(3,SceneStarter.LinkedScene.Run(input,output,error));
        Assert.Contains("\"kind\":\"ready\"",output.ToString());Assert.DoesNotContain("\"kind\":\"stopped\"",output.ToString());
        Assert.Contains("\"code\":\"RuntimeFailed\"",output.ToString());
    }

    [Fact]
    public void Public_adapter_and_program_host_run_saved_files_and_stop_without_saving_the_scene_draft()
    {
        using var r=new SamplePlayJourneyTests.CommonRig();Assert.IsType<ProjectPlayBridge>(r.Factory.Bridge);Assert.Same(r.Adapter,r.Launcher.Using);
        r.Play();var game=r.Choose("Don't Save");r.AssertSavedInput(SceneText);r.Unchanged();
        Assert.Equal("Running.",game.StatusAtStart);Assert.True(r.Window.Editor.Session.Dirty);
        r.Stop(game);Assert.True(game.HasCompleted);Assert.False(r.Workspace.ProjectPlaySavePending);r.Unchanged();
        Assert.Same(r.Draft,r.Window.Editor.Session.Current);Assert.Equal(r.Revision,r.Window.Editor.Session.Revision);
        Assert.Equal(r.Undo,r.Window.Editor.Session.History.UndoCount);Assert.Equal(r.Journal,r.Window.Editor.Session.Journal.Count);Assert.Equal(SceneText,File.ReadAllText(r.File));
    }
}
