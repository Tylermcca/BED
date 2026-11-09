using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using BedProgram=Brutal.Authoring.Launcher.Program;

namespace Brutal.Authoring.Tests;

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
    public void Public_adapter_and_program_host_reach_ready_and_stop_without_saving_the_scene_draft()
    {
        var root=Directory.CreateTempSubdirectory("bed-linked-sample-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var level=Path.Combine(data,"level.json");File.WriteAllText(level,SceneText);
            File.WriteAllText(Path.Combine(schemas,"scene.schema.json"),"""{"type":"object","required":["kind","version","objects"],"properties":{"kind":{"const":"scene"},"version":{"const":1},"objects":{"type":"array"}}}""");
            File.WriteAllText(Path.Combine(root,"reference_map.json"),"""{"game":"SceneStarter","labels":[],"references":[]}""");
            var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);
            Assert.Empty(loaded.Problems);var adapter=Assert.Single(loaded.Loaded);
            using var headless=new HeadlessHost(1000,800) {Drawing=false};
            using var launcher=BedProgram.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"recent.json"))) {Loaded=loaded.Loaded});
            Assert.IsType<LinkedPlayHost>(launcher.LinkedPlayHost);
            Assert.True(launcher.Open(ProjectPaths.In(root)!),launcher.Said);Assert.Same(adapter,launcher.Using);
            var workspace=launcher.Workspace!;Assert.True(workspace.Show("level.json"),workspace.Said);
            var window=workspace.SceneWindow!;EditorWindow<Scene>.Configure(ImGui.GetIO());
            headless.Frames(window.DrawDocked,2);
            Assert.True(window.Editor.Execute(new MoveObject("root",new Vector3(9,8,7)),"root"));
            var draft=window.Editor.Session.Current;var revision=window.Editor.Session.Revision;
            var undo=window.Editor.Session.History.UndoCount;var journal=window.Editor.Session.Journal.Count;
            Assert.True(window.Run(EditorWindow<Scene>.PlayAction+EditorWindow<Scene>.LinkedPlayModeId));
            Assert.True(window.LinkedPlaySavePending);Assert.True(window.AnswerLinkedPlay(SaveChangesChoice.DontSave),window.Status);
            var preview=Assert.IsType<LinkedPlayPreview>(window.Editor.Game);
            Assert.Equal(PlayInputChoice.Saved,preview.Session.Snapshot.Choice);
            Assert.Equal(SceneText,Encoding.UTF8.GetString(preview.Session.Snapshot.CopyPayload()));
            void Until(Func<bool> done)
            {
                var clock=Stopwatch.StartNew();
                while(!done() && clock.Elapsed<TimeSpan.FromSeconds(5)) {headless.Frame(window.DrawDocked);Thread.Sleep(5);}
                Assert.True(done(),preview.Status);
            }
            Until(()=>preview.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted);
            Assert.Equal(LinkedPlayState.Running,preview.Session.State);Assert.Null(preview.Session.Error);
            Assert.True(window.Run("editor.stop"));
            Until(()=>window.Editor.Playing is null);
            Assert.Equal(LinkedPlayState.Stopped,preview.Session.State);Assert.Null(preview.Session.Error);Assert.True(preview.HasCompleted);
            Assert.Same(draft,window.Editor.Session.Current);Assert.Equal(revision,window.Editor.Session.Revision);
            Assert.True(window.Editor.Session.Dirty);Assert.Equal(undo,window.Editor.Session.History.UndoCount);
            Assert.Equal(journal,window.Editor.Session.Journal.Count);Assert.Equal(SceneText,File.ReadAllText(level));
        }
        finally
        {
            var owned=Path.GetFullPath(root);var temporary=Path.GetFullPath(Path.GetTempPath());
            Assert.StartsWith(temporary,owned,StringComparison.OrdinalIgnoreCase);
            Directory.Delete(owned,true);
        }
    }
}
