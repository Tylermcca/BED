using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PhysicsJourneyTests(ITestOutputHelper output)
{
    private const string Image="1234567890abcdef1234567890abcdef",Model="abcdef1234567890abcdef1234567890";
    private const string Definition="91827364556473829182736455647382",RootKey="11112222333344445555666677778888",ChildKey="88887777666655554444333322221111";
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Completion(Task task,string step)
    {
        if(task.IsCompleted)return;using var ready=new ManualResetEventSlim();
        task.GetAwaiter().OnCompleted(()=>{try{ready.Set();}catch(ObjectDisposedException){}});
        Assert.True(ready.Wait(TimeSpan.FromSeconds(15)),step+" did not complete");Assert.True(task.IsCompleted);
    }
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("scene2-journey-").FullName;
        internal readonly string Logs=Directory.CreateTempSubdirectory("physics-journey-logs-").FullName;
        internal readonly string Copy=Directory.CreateTempSubdirectory("scene2-journey-copy-").FullName;
        internal readonly HeadlessHost Ui=new(1600,1100){Drawing=false};internal readonly AssetCatalog Catalog=new();internal readonly DataWorkspace Workspace;internal readonly IProjectAdapter Adapter;internal readonly bool Flat;internal readonly byte[] Pixels=[255,32,16,128,16,240,48,255,48,64,220,0,240,192,16,64];
        internal string Data=>Path.Combine(Root,"data");internal string Level=>Path.Combine(Data,"level.json");internal string Source=>Path.Combine(Data,"beacon.json");internal string Schemas=>Path.Combine(Root,"schemas");
        internal Scene2DocumentEntry Entry{get{Assert.True(Workspace.Documents.TryGetOpenScene2("level.json",out var e));return e!;}}
        internal EditorWindow<Scene2> Window=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",Private)!.GetValue(Workspace)!;
        internal Rig(bool flat)
        {
            Flat=flat;Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);Directory.CreateDirectory(Path.Combine(Root,"assets"));Directory.CreateDirectory(Path.Combine(Root,".bed"));
            File.WriteAllText(Path.Combine(Schemas,"beacon.schema.json"),"{}");
            var initial=new Scene2(flat?SceneSpace.TwoDXY:SceneSpace.ThreeD,[new Scene2Object(SceneObject.At("floor",null,new(0,-.5f,0)),null){Body=new(SceneBodyMotion.Static,new SceneBoxShape(new(20,.5f,20)),.6f)}],[],[],null){Physics=new(new(0,-10,0),.01f)};File.WriteAllText(Level,new Scene2Format().Write(initial));
            var node=new JsonObject{{"key",RootKey},{"label","Beacon"},{"asset",flat?Image:Model},{"position",new JsonArray(0,3,0)},{"properties",new JsonObject{{"charge",1}}},{"references",new JsonObject()}};
            node["body"]=JsonNode.Parse(flat?"{\"motion\":\"dynamic\",\"massKg\":2,\"friction\":0.4,\"shape\":{\"kind\":\"box\",\"halfExtents\":[0.5,0.5,0.5]}}":"{\"motion\":\"dynamic\",\"massKg\":2,\"friction\":0.4,\"shape\":{\"kind\":\"mesh-bounds\"}}");
            if(flat)node["sprite"]=JsonNode.Parse("""{"size":[1,1],"pivot":[0.25,0.75],"order":4,"filter":"linear"}""");
            var marker=new JsonObject{{"key",ChildKey},{"label","Lens"},{"parentKey",RootKey},{"position",new JsonArray(0,1,0)},{"references",new JsonObject{{"beacon",new JsonObject{{"node",RootKey}}}}}};
            var definition=new JsonObject{{"kind","prefab-definition"},{"version",1},{"space",flat?"2d-xy":"3d"},{"guid",Definition},{"root",RootKey},{"nodes",new JsonArray(node,marker)},{"slots",new JsonArray()}};File.WriteAllText(Source,definition.ToJsonString());
            byte[] bytes,derived;AssetCatalog.TextureInfo? texture=null;var id=flat?Image:Model;var name=flat?"beacon.png":"beacon.obj";
            if(flat)
            {
                uint[] colours=Enumerable.Range(0,4).Select(i=>(uint)(Pixels[4*i]|Pixels[4*i+1]<<8|Pixels[4*i+2]<<16|Pixels[4*i+3]<<24)).ToArray();bytes=Png.Encode(2,2,colours);
                Assert.True(AssetImageDecoder.TryDecode(bytes,"png",out var decoded,out var why),why);Assert.Equal(Pixels,decoded!.Rgba);derived=decoded.Rgba;texture=new(2,2,AssetCatalog.TextureEncoding.Rgba8Srgb);
            }
            else {bytes=Encoding.UTF8.GetBytes("o beacon\nv -0.5 -0.5 -0.5\nv 0.5 -0.5 -0.5\nv 0.5 0.5 0.5\nv -0.5 0.5 0.5\nf 1 2 3\nf 1 3 4\n");derived=DerivedMeshCodec.Encode(MeshData.Cube);}
            var record=new AssetRecord(id,name,Convert.ToHexStringLower(SHA256.HashData(bytes)),"journey-fixture",1,new SpriteAssetDefaults(2,new(.5f,.5f),SceneSpriteFilter.Nearest).WithSettings(JsonSerializer.SerializeToElement(new{})),new("Fixture author","self-authored","original generated two-by-two pixels or positive-volume primitive",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllBytes(Path.Combine(Root,"assets",name),bytes);File.WriteAllText(Path.Combine(Root,"assets",name)+AssetRecord.Suffix,record.Write());Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,derived,[],true,"journey-fixture","own",texture:texture)],out var published),published);
            Assert.True(Catalog.TryResolve(Catalog.Select(id)!,out var lease,out _));using(var publisher=new AssetCatalogPublisher(Root))Assert.True(publisher.Publish(publisher.Prepare("original-journey",new Dictionary<string,byte[]>{{id+".bin",TextureAssetGeneration.StoredArtifact(lease!)}}),out published),published);
            File.WriteAllText(Path.Combine(Root,".bed/project.json"),Scene2BuildSettings.WriteProject("{}",new("data/level.json",".bed/generated/scenes/level.json",[initial.Space]){SupportsPhysics=true}));
            var inventory=(string[])typeof(PhysicsStarter.Program).Assembly.GetType("SceneStarter.PhysicsRuntimeInventory")!.GetField("Files",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;foreach(var file in inventory){var target=Path.Combine(Root,"game",file);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(AppContext.BaseDirectory,"samples/PhysicsStarter",file),target);}
            var loaded=Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);Adapter=Assert.Single(loaded.Loaded);var paths=new ProjectPaths(Data,Schemas){Root=Root};var defaults=Adapter.DefaultPlay(paths)!;
            Workspace=new(Adapter.Open(paths));Assert.True(Workspace.BindSceneAssets(new AssetBrowser(Path.Combine(Root,"assets"),".png",".obj"),Catalog));Workspace.BindPlaySettings(Root,new PlaySettings(defaults.Program,["--project",".","--seconds","0.1"],defaults.WorkingFolder,defaults.Build),Path.Combine(Root,"preferences"));Workspace.BindProjectPlay(new ProjectPlayBridge());
            Workspace.BindExport(Adapter.Build(Workspace)!,Adapter.Export(Workspace)!,new ExportBuildHost(4096,32,TimeSpan.FromSeconds(12),Logs));Assert.True(Workspace.Show("level.json"));EditorWindow<Scene2>.Configure(ImGui.GetIO());Frames();
        }
        internal void Frames()=>Ui.Frames(Workspace.Draw,3);
        internal UiNode Button(string label)=>Assert.Single(Bed.Editor.ImGui.Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);
        internal void Press(string label)
        {var n=Button(label);Assert.True(n.Enabled,n.DisabledReason);Assert.False(n.Bounds.IsEmpty,label+" is clipped");var action=Bed.Editor.ImGui.Ui.RequestAction(n.Id,UiActions.Invoke);Frames();Assert.Equal(UiActionOutcome.Applied,action.Outcome);}
        internal void Select(string id){Window.Editor.Select(id);Frames();}
        internal Scene2Object Object(string id)=>Scene2Resolver.Resolve(Entry.Session.Current).Objects.Single(o=>o.Object.Id==id);
        internal ExportBuildDialog Export=>(ExportBuildDialog)typeof(DataWorkspace).GetField("exportDialog",Private)!.GetValue(Workspace)!;
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();Ui.Dispose();foreach(var path in new[]{Root,Copy,Logs}){Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase);if(Directory.Exists(path))Directory.Delete(path,true);}}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Prefab_body_override_source_review_checked_save_native_Play_and_export_are_one_journey(bool flat)
    {
        using var r=new Rig(flat);var total=Stopwatch.StartNew();
        r.Window.Editor.Select([]);r.Frames();r.Press("Place prefab");var first=Assert.Single(r.Entry.Session.Current.Instances);r.Window.Editor.Select([]);r.Frames();r.Press("Place prefab");Assert.Equal(2,r.Entry.Session.Current.Instances.Length);var second=r.Entry.Session.Current.Instances[1];var root=first.NodeIds[RootKey];r.Select(root);
        Assert.True(r.Window.Inspector!.Set("position.0",3d));Assert.Equal(3,r.Object(root).Object.Position.X);Assert.Equal(0,r.Object(second.NodeIds[RootKey]).Object.Position.X);r.Frames();Assert.Contains(Bed.Editor.ImGui.Ui.Snapshot().Nodes,n=>n.Label.Contains("local changes",StringComparison.Ordinal));
        Assert.True(r.Window.Inspector!.Set("body.mass",7d));Assert.Equal(7,r.Object(root).Body!.MassKg);Assert.Equal(2,r.Object(second.NodeIds[RootKey]).Body!.MassKg);var beforeSource=r.Entry.Session.Current;Assert.True(r.Workspace.Show("beacon.json"));var source=r.Workspace.Project.Find("beacon.json")!;Assert.True(source.Session!.Execute(new SetValue("/nodes/0/position/0",JsonSerializer.SerializeToElement(5))));Assert.True(source.Session.Execute(new SetValue("/nodes/0/body/friction",JsonSerializer.SerializeToElement(.8f))));Assert.True(r.Workspace.Window!.Run("editor.save"),r.Workspace.Window.Status);Assert.False(source.Dirty);Assert.True(r.Workspace.Show("level.json"));r.Select(root);Assert.Same(beforeSource,r.Entry.Session.Current);
        var history=r.Entry.Session.History.UndoCount;r.Press("Review source update");r.Press("Cancel update");Assert.Same(beforeSource,r.Entry.Session.Current);Assert.Equal(history,r.Entry.Session.History.UndoCount);r.Press("Review source update");r.Press("Accept update");Assert.Equal(history+1,r.Entry.Session.History.UndoCount);Assert.Equal(3,r.Object(root).Object.Position.X);Assert.Equal(0,r.Object(second.NodeIds[RootKey]).Object.Position.X);Assert.Equal(2,r.Entry.Session.Current.Bases.Length);
        Assert.Equal(7,r.Object(root).Body!.MassKg);Assert.Equal(.8f,r.Object(root).Body!.Friction);Assert.Equal(.4f,r.Object(second.NodeIds[RootKey]).Body!.Friction);Assert.True(r.Window.Inspector!.Reset("body.mass"));Assert.Equal(2,r.Object(root).Body!.MassKg);Assert.True(r.Entry.Session.Undo());Assert.Equal(7,r.Object(root).Body!.MassKg);Assert.True(r.Entry.Session.Redo());Assert.Equal(2,r.Object(root).Body!.MassKg);
        var replay=DocumentSession<Scene2>.ReplayFrom(r.Entry.Session.JournalBase,JournalFile.Read(JournalFile.Write(r.Entry.Session.Journal)),new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(r.Entry.Session.TextToSave,replay.TextToSave);Assert.True(r.Window.Run("editor.save"),r.Window.Status);var saved=File.ReadAllText(r.Level);Assert.False(r.Entry.Session.Dirty);Assert.Null(r.Workspace.Project.Find("level.json")!.Session);
        var old=r.Window;Assert.True(old.CloseDocument!());r.Frames();Assert.True(r.Workspace.Show("level.json"));r.Frames();Assert.NotSame(old,r.Window);Assert.Equal(saved,r.Entry.Session.TextToSave);
        Assert.True(r.Workspace.Documents.CloseScene2("level.json",SceneCloseDecision.Discard,out _));using(var stdout=new StringWriter())using(var stderr=new StringWriter())
        {
            var args=JsonSerializer.Serialize(new{id=root,field="/properties/journey",value="cli-checked"});var code=Bed.Agent.Cli.Program.Run(["--data",r.Data,"--schemas",r.Schemas,"scene","level.json","set",args,"--save"],TextReader.Null,stdout,stderr);Assert.Equal(0,code);Assert.Empty(stderr.ToString());Assert.True(JsonNode.Parse(stdout.ToString())!["saved"]!.GetValue<bool>());
        }
        Assert.True(r.Workspace.Show("level.json"));r.Frames();Assert.Equal("cli-checked",r.Object(root).Object.Properties["journey"].GetString());r.Select(first.Placement.Id);r.Select(root);Assert.True(r.Window.Inspector!.Set("body.mass",9d));Assert.True(r.Workspace.RequestProjectPlay(true));Assert.True(r.Workspace.ProjectPlaySavePending);Assert.False(r.Workspace.AnswerProjectPlay(SaveChangesChoice.Cancel));Assert.Null(r.Workspace.ProjectPlay);Assert.True(r.Entry.Session.Dirty);
        Assert.True(r.Workspace.RequestProjectPlay(true));Assert.False(r.Workspace.AnswerProjectPlay(SaveChangesChoice.Save));Assert.True(r.Workspace.ProjectPlayApprovalPending);Assert.False(r.Entry.Session.Dirty);r.Frames();for(var approval=0;approval<2&&r.Workspace.ProjectPlayApprovalPending;approval++)r.Press("Allow this exact command and continue");Assert.False(r.Workspace.ProjectPlayApprovalPending);var play=r.Workspace.ProjectPlay!;var owned=(ProjectPlayHost)play.GetType().GetField("host",Private)!.GetValue(play)!;var logs=new List<ProjectPlayLog>();var playTime=Stopwatch.StartNew();
        for(var phase=0;phase<4&&owned.State is ProjectPlayState.Building or ProjectPlayState.Running;phase++){Completion(owned.OwnedCompletion,"Play phase");owned.Update();logs.AddRange(owned.DrainLogs());}
        playTime.Stop();Assert.Equal(ProjectPlayState.Stopped,owned.State);Assert.Equal(0,owned.ExitCode);Assert.Contains(logs,l=>l.Text.Contains("loaded saved physics: 3 bodies",StringComparison.Ordinal));r.Frames();var expected=Scene2Kit.Flatten(r.Entry.Session.Current);var generated=new Scene2Format().Read(File.ReadAllText(Path.Combine(r.Root,".bed/generated/scenes/level.json")));Assert.Empty(generated.Instances);Assert.Empty(generated.Bases);Assert.Equal(9,generated.Objects.Single(o=>o.Object.Id==root).Body!.MassKg);Assert.Equal(.8f,generated.Objects.Single(o=>o.Object.Id==root).Body!.Friction);Assert.Equal(new Vector3(.5f),Assert.IsType<SceneBoxShape>(generated.Objects.Single(o=>o.Object.Id==root).Body!.Shape).HalfExtents);Assert.Equal(r.Entry.Session.Current.Physics,generated.Physics);
        var exportTime=Stopwatch.StartNew();r.Window.ExportBuildAction!();r.Frames();r.Press("Build saved files");
        for(var phase=0;phase<4&&r.Export.Busy;phase++)
        {
            var operation=(IGameBuildOperation?)typeof(ExportBuildDialog).GetField("operation",Private)!.GetValue(r.Export);var smoke=(Task?)typeof(ExportBuildDialog).GetField("smoke",Private)!.GetValue(r.Export);Completion(operation?.Completion??smoke??Task.CompletedTask,"Export phase");r.Export.Pump();
        }
        Assert.True(r.Export.CanCopy,r.Export.Status);r.Frames();var destination=Assert.Single(Bed.Editor.ImGui.Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Destination folder");var set=Bed.Editor.ImGui.Ui.RequestAction(destination.Id,UiActions.SetValue,r.Copy);r.Frames();Assert.Equal(UiActionOutcome.Applied,set.Outcome);r.Press("Copy build to folder...");
        if(typeof(ExportBuildDialog).GetField("copy",Private)!.GetValue(r.Export) is Task copy)Completion(copy,"Copy phase");r.Export.Pump();Assert.False(r.Export.Busy);Assert.True(File.Exists(Path.Combine(r.Copy,"PhysicsStarter.exe")),r.Export.Status);Assert.False(Directory.Exists(Path.Combine(r.Copy,"data")));Assert.False(Directory.Exists(Path.Combine(r.Copy,"assets")));Assert.DoesNotContain(Directory.GetFiles(r.Copy,"*.dll"),f=>Path.GetFileName(f).Contains("Editor",StringComparison.Ordinal));
        var declaration=r.Adapter.Export(r.Workspace)!;var captured=PlaySnapshot.Create(new("level.json",2,File.ReadAllText(r.Level),"saved"),PlayInputChoice.Saved,declaration.ConvertSaved);Assert.Null(captured.Error);var levelBytes=File.ReadAllBytes(r.Level);File.AppendAllText(r.Level," ");Assert.Throws<InvalidOperationException>(()=>declaration.Prepare(captured.Snapshot!));File.WriteAllBytes(r.Level,levelBytes);var cacheIndex=Path.Combine(r.Root,".bed/cache/current.json");var cacheBytes=File.ReadAllBytes(cacheIndex);File.AppendAllText(cacheIndex," ");Assert.Throws<InvalidOperationException>(()=>declaration.Prepare(captured.Snapshot!));File.WriteAllBytes(cacheIndex,cacheBytes);r.Catalog.BeginRefresh();Assert.Throws<InvalidOperationException>(()=>declaration.Prepare(captured.Snapshot!));
        File.Delete(r.Level);File.Delete(r.Source);var cache=Path.GetFullPath(Path.Combine(r.Root,".bed/cache"));var offline=Path.GetFullPath(Path.Combine(r.Root,"cache-offline"));Assert.StartsWith(r.Root+Path.DirectorySeparatorChar,cache,StringComparison.OrdinalIgnoreCase);Assert.StartsWith(r.Root+Path.DirectorySeparatorChar,offline,StringComparison.OrdinalIgnoreCase);Directory.Move(cache,offline);
        var info=new ProcessStartInfo(Path.Combine(r.Copy,"PhysicsStarter.exe")){WorkingDirectory=Path.GetTempPath(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"--project",r.Copy,"--seconds","0.1"})info.ArgumentList.Add(arg);using var child=Process.Start(info)!;if(!child.WaitForExit(12000)){child.Kill(entireProcessTree:true);Assert.Fail("Copied runtime did not exit within twelve seconds.");}var text=child.StandardOutput.ReadToEnd();Assert.Equal(0,child.ExitCode);Assert.Empty(child.StandardError.ReadToEnd());Assert.Contains("loaded saved physics: 3 bodies",text);
        var exported=new Scene2Format().Read(File.ReadAllText(Path.Combine(r.Copy,".bed/generated/scenes/level.json")));Assert.Equal(new Scene2Format().Write(generated),new Scene2Format().Write(exported));
        output.WriteLine(JsonSerializer.Serialize(new{profile=flat?"flat":"3d",objects=expected.Objects.Length,playMs=playTime.ElapsedMilliseconds,exportMs=exportTime.ElapsedMilliseconds,totalMs=total.ElapsedMilliseconds,stdout=text,savedHash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(new Scene2Format().Write(expected))))}));
    }
}
