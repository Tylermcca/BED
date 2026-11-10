using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Import;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneStarterPinnedPlayTests
{
    private const string Id="c0bec0bec0bec0bec0bec0bec0bec0be";
    private const string Text="""{"kind":"scene","version":1,"objects":[{"id":"placed","asset":"c0bec0bec0bec0bec0bec0bec0bec0be","position":[2,3,4]}]}""";
    private static IProjectAdapter Adapter()
    {
        var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);
        Assert.Empty(loaded.Problems);return Assert.Single(loaded.Loaded);
    }
    private sealed class Rig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("sample01-pins-").FullName;
        private readonly HeadlessHost host=new(1000,800){Drawing=false};
        public readonly AssetCatalog Catalog=new();
        public readonly DataWorkspace Workspace;
        public readonly GameLaunchSpec Spec;
        public readonly PlaySnapshotConverter ConvertInput;
        public readonly string Level;
        public Rig(IProjectAdapter? adapter=null,string profile="sample")
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            Level=Path.Combine(data,"level.json");File.WriteAllText(Level,Text);
            var sourcePath=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter","assets","cube.obj");
            var source=File.ReadAllBytes(sourcePath);var record=AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(sourcePath)));
            var imported=ObjImporter.Import(sourcePath,ProjectFrame.Gltf);Assert.False(imported.Issues.HasErrors());
            var mesh=Assert.Single(imported.Asset!.Meshes);
            var derived=DerivedMeshCodec.Encode(new MeshData(mesh.Positions,mesh.Triangles));
            Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new(record,source,derived,[],true,profile,"self-authored sample")],out var why),why);
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.BindSceneAssets(new AssetBrowser(assets,".obj"),Catalog));
            adapter??=Adapter();
            // The generic interface no longer offers a protocol Play route. Retain these exact component
            // witnesses through the trusted sample's legacy concrete surface, without changing loader context.
            var type=adapter.GetType();
            Spec=(GameLaunchSpec)type.GetMethod("LinkedPlay",[typeof(DataWorkspace)])!.Invoke(adapter,[Workspace])!;Assert.NotNull(Spec);
            ConvertInput=(PlaySnapshotConverter)type.GetProperty("PlaySnapshot")!.GetValue(adapter)!;Assert.NotNull(ConvertInput);
        }
        public PlaySnapshotResult Capture(string text=Text)=>PlaySnapshot.Create(new("level.json",7,text,text),PlayInputChoice.Saved,ConvertInput);
        public void Dispose()
        {
            Workspace.Dispose();Catalog.Dispose();host.Dispose();
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);
        }
    }
    private static string Peer(PlaySnapshot snapshot,int expected=0)
    {
        var hello=PlayProtocol.Hello(snapshot.Correlate(Guid.NewGuid()));
        using var input=new StringReader(PlayProcessWire.Start(hello,snapshot,96*1024*1024)+"\n"+PlayProcessWire.Stop(new(1,hello.Correlation))+"\n");
        using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(expected,SceneStarter.LinkedScene.Run(input,output,error));
        if(expected==0)
        {
            Assert.Equal("",error.ToString());var lines=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            Assert.Equal(2,lines.Length);Assert.True(PlayProcessWire.TryRead(lines[0],8192,out var ready,out var why,hello.Correlation),why?.Message);
            Assert.IsType<PlayReady>(ready!.Message);Assert.True(PlayProcessWire.TryRead(lines[1],8192,out var stopped,out why,hello.Correlation),why?.Message);
            Assert.IsType<PlayStopped>(stopped!.Message);
        }
        else {Assert.NotEmpty(error.ToString());Assert.DoesNotContain("\"kind\":\"ready\"",output.ToString());}
        return output.ToString();
    }
    [Fact]
    public void Catalog_asset_input_is_workspace_bound_and_copied_before_later_catalog_close()
    {
        var adapter=Adapter();using var first=new Rig(adapter,"first");using var second=new Rig(adapter,"second");
        var result=first.Capture();Assert.True(result.Accepted,result.Error?.Message);var snapshot=result.Snapshot!;
        Assert.Equal(SceneStarter.LinkedScene.AssetFormat,snapshot.Format);
        using(var json=JsonDocument.Parse(snapshot.CopyPayload()))
        {
            Assert.Equal(Text,json.RootElement.GetProperty("scene").GetString());
            var pin=Assert.Single(json.RootElement.GetProperty("assets").EnumerateArray());Assert.Equal(Id,pin.GetProperty("guid").GetString());
            Assert.Equal(first.Catalog.Resolve(Id).ContentRevision,pin.GetProperty("contentRevision").GetString());
            Assert.NotEqual(second.Catalog.Resolve(Id).ContentRevision,pin.GetProperty("contentRevision").GetString());
            var bytes=pin.GetProperty("derived").GetBytesFromBase64();Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)),pin.GetProperty("derivedHash").GetString());
        }
        var before=snapshot.CopyPayload();first.Catalog.Dispose();Peer(snapshot);Assert.Equal(before,snapshot.CopyPayload());
        Assert.Equal(Text,File.ReadAllText(first.Level));Assert.Empty(first.Workspace.Project.Dirty);
        Assert.DoesNotContain(typeof(SceneStarter.Program).Assembly.GetReferencedAssemblies(),name=>name.Name!.Contains("Editor",StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("refresh")][InlineData("closed")][InlineData("missing")]
    public void Unavailable_catalog_never_publishes_a_play_snapshot(string reason)
    {
        using var rig=new Rig();
        if(reason=="refresh")rig.Catalog.BeginRefresh();else if(reason=="closed")rig.Catalog.Dispose();
        else Assert.True(rig.Catalog.Publish(rig.Catalog.BeginRefresh(),[],out _));
        var result=rig.Capture();Assert.False(result.Accepted);Assert.Null(result.Snapshot);Assert.NotNull(result.Error);
        Assert.Equal(Text,File.ReadAllText(rig.Level));Assert.Empty(rig.Workspace.Project.Dirty);
    }
    [Theory]
    [InlineData("bytes")][InlineData("duplicate")][InlineData("missing")][InlineData("sourceHash")][InlineData("field")][InlineData("count")]
    public void Runtime_refuses_corrupt_duplicate_missing_or_over_budget_pins_before_ready(string problem)
    {
        using var rig=new Rig();var good=rig.Capture().Snapshot!;var root=JsonNode.Parse(good.CopyPayload())!;var pins=root["assets"]!.AsArray();
        if(problem=="bytes")pins[0]!["derived"]=Convert.ToBase64String([8,9]);
        if(problem=="duplicate")pins.Add(pins[0]!.DeepClone());
        if(problem=="missing")pins.Clear();
        if(problem=="sourceHash")pins[0]!["sourceHash"]="not-a-source-hash";
        if(problem=="field")pins[0]!["path"]="not-a-filesystem-authority";
        if(problem=="count"){var pin=pins[0]!.DeepClone();while(pins.Count<257)pins.Add(pin.DeepClone());}
        var snapshot=PlaySnapshot.Create(new("level.json",7,Text,Text),PlayInputChoice.Saved,
            _=>new(SceneStarter.LinkedScene.AssetFormat,Encoding.UTF8.GetBytes(root.ToJsonString()))).Snapshot!;
        Peer(snapshot,3);Assert.Equal(Text,File.ReadAllText(rig.Level));
    }
    [Fact]
    public void Saved_asset_selection_ignores_the_draft_and_stale_source_bytes_refuse_a_new_capture()
    {
        using var rig=new Rig();var draft=Text.Replace(Id,"ffffffffffffffffffffffffffffffff",StringComparison.Ordinal);
        var source=new PlaySnapshotSource("level.json",7,Text,draft);
        var saved=PlaySnapshot.Create(source,PlayInputChoice.Saved,rig.ConvertInput);Assert.True(saved.Accepted,saved.Error?.Message);
        using(var json=JsonDocument.Parse(saved.Snapshot!.CopyPayload()))Assert.Equal(Text,json.RootElement.GetProperty("scene").GetString());
        Assert.False(PlaySnapshot.Create(source,PlayInputChoice.Draft,rig.ConvertInput).Accepted);
        var path=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter","assets","cube.obj");
        var record=AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(path)));
        Assert.True(rig.Catalog.Publish(rig.Catalog.BeginRefresh(),[new(record,[7],[9],[],true,"changed source","self-authored sample")],out _));
        Assert.Equal(AssetCatalog.State.Stale,rig.Catalog.Resolve(Id).State);
        var next=rig.Capture();Assert.False(next.Accepted);Assert.Null(next.Snapshot);
        Peer(saved.Snapshot);Assert.Equal(Text,File.ReadAllText(rig.Level));Assert.Empty(rig.Workspace.Project.Dirty);
    }
    [Fact]
    public void Real_hidden_sample_accepts_pinned_OBJ_then_stops_without_touching_saved_input()
    {
        using var rig=new Rig();var snapshot=rig.Capture().Snapshot!;
        using var preview=new LinkedPlayHost().Start(rig.Spec,snapshot);
        void Until(Func<bool> done)
        {
            var watch=Stopwatch.StartNew();while(!done() && watch.Elapsed<TimeSpan.FromSeconds(5)){preview.Update();Thread.Sleep(5);}
            Assert.True(done(),preview.Status);
        }
        Until(()=>preview.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted);
        Assert.Equal(LinkedPlayState.Running,preview.Session.State);Assert.Null(preview.Session.Error);
        Assert.True(preview.RequestStop());Until(()=>preview.HasCompleted);
        Assert.Equal(LinkedPlayState.Stopped,preview.Session.State);Assert.Null(preview.Session.Error);
        Assert.Equal(Text,File.ReadAllText(rig.Level));Assert.Empty(rig.Workspace.Project.Dirty);
    }
}
