using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneViewIndexPerf12Tests(ITestOutputHelper output)
{
    private static Matrix4x4 OriginalWorld(Scene scene,string id)
    {
        var result=Matrix4x4.Identity;var seen=new HashSet<string>(StringComparer.Ordinal);
        for(var item=scene.Find(id);item is not null&&seen.Add(item.Id);item=item.Parent is {} parent?scene.Find(parent):null)
            result*=Matrix4x4.CreateScale(item.Scale)*Matrix4x4.CreateFromQuaternion(item.Rotation)*Matrix4x4.CreateTranslation(item.Position);
        return result;
    }

    [Theory][InlineData("valid")][InlineData("duplicate")][InlineData("cycle")][InlineData("missing")]
    public void Indexed_worlds_preserve_exact_multiplication_order_and_first_match_repair_semantics(string mode)
    {
        var a=SceneObject.At("a",null,new(7.1f,2.7f,-4.3f)) with{Rotation=Quaternion.CreateFromYawPitchRoll(.17f,.33f,.61f),Scale=new(2.1f,.7f,3.9f)};
        var b=SceneObject.At("b",null,new(3.2f,-5.4f,8.6f)) with{Parent="a",Rotation=Quaternion.CreateFromYawPitchRoll(.4f,.9f,.2f),Scale=new(.8f,1.7f,2.4f)};
        var c=SceneObject.At("c",null,new(-1.2f,6.4f,4.5f)) with{Parent="b",Scale=new(1.2f,2.3f,.9f)};
        var scene=new Scene(mode switch
        {
            "duplicate"=>[a,b,c,a with{Position=new(999,999,999)}],
            "cycle"=>[a with{Parent="c"},b,c],
            "missing"=>[a,b with{Parent="gone"},c],
            _=>[a,b,c]
        });
        var source=new SceneViewport();var expected=scene.Objects.Select(o=>OriginalWorld(scene,o.Id)).ToArray();
        for(var repeat=0;repeat<3;repeat++)
        {
            Assert.Equal(expected,source.Items(scene).Select(i=>i.World));
            foreach(var item in scene.Objects)Assert.Equal(OriginalWorld(scene,item.Id),SceneViewport.World(scene,item.Id));
            Assert.Equal(Matrix4x4.Identity,SceneViewport.World(scene,"gone"));
            Assert.Equal(scene.Find("a")!.Position,SceneEditing.ObjectType().Find(scene,"a")!.Position);
        }
    }

    [Fact]
    public void Parent_label_and_transform_indexes_follow_preview_revision_undo_and_redo_without_stale_reads()
    {
        var original=new Scene([SceneObject.At("a",null,new(10,0,0)),SceneObject.At("b",null,new(20,0,0)),SceneObject.At("child",null,Vector3.UnitX) with{Parent="a"}]);
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),original);
        var outline=SceneEditing.Outline;
        Assert.Equal("a",outline.Label(original,"a"));Assert.Equal("a",outline.Parent(original,"child"));Assert.Equal(new Vector3(11,0,0),SceneViewport.World(original,"child").Translation);
        Assert.True(session.ExecuteBatch([new MoveObject("a",new(100,0,0)),new SetParent("child","b"),new SetSceneProperty("a","name",JsonSerializer.SerializeToElement("Renamed parent"))]));
        var changed=session.Current;
        Assert.Equal("Renamed parent",outline.Label(changed,"a"));Assert.Equal("b",outline.Parent(changed,"child"));Assert.Equal(new Vector3(21,0,0),SceneViewport.World(changed,"child").Translation);
        var preview=new Scene(changed.Objects.Select(o=>o.Id=="child"?o with{Position=new(5,0,0)}:o).ToImmutableArray());
        Assert.Equal(new Vector3(25,0,0),SceneViewport.World(preview,"child").Translation);
        Assert.Equal(new Vector3(21,0,0),SceneViewport.World(changed,"child").Translation);
        Assert.Null(outline.Label(changed,"gone"));Assert.Null(outline.Parent(changed,"gone"));
        Assert.True(session.Undo());Assert.Same(original,session.Current);Assert.Equal("a",outline.Label(session.Current,"a"));Assert.Equal(new Vector3(11,0,0),SceneViewport.World(session.Current,"child").Translation);
        Assert.True(session.Redo());Assert.Same(changed,session.Current);Assert.Equal("Renamed parent",outline.Label(session.Current,"a"));Assert.Equal("b",outline.Parent(session.Current,"child"));
        Assert.Equal(1,session.History.UndoCount);
    }

    [Fact]
    public void Projection_cache_preserves_exact_results_after_camera_clones_and_viewport_resize()
    {
        var basis=Camera.Default;
        foreach(var camera in new[]{basis,basis with{Target=new(4,1,2),Yaw=.7f},basis with{Distance=27,FieldOfView=.8f},basis})
            foreach(var size in new[]{new Vector2(800,600),new Vector2(1280,720),new Vector2(800,600)})
                foreach(var point in new[]{Vector3.Zero,new Vector3(2,3,4),new Vector3(-10,5,1)})
                {
                    var clip=Vector4.Transform(new Vector4(point,1),camera.View*camera.Projection(size));
                    var actual=camera.Project(point,size);
                    if(clip.W<=1e-6f){Assert.Null(actual);continue;}
                    var ndc=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
                    Assert.Equal(new Vector2((ndc.X+1)/2*size.X,(1-ndc.Y)/2*size.Y),actual!.Value.Screen);Assert.Equal(ndc.Z,actual.Value.Depth);
                    var screen=new Vector2(211,173);Matrix4x4.Invert(camera.View*camera.Projection(size),out var inverse);
                    var x=screen.X/size.X*2-1;var y=1-screen.Y/size.Y*2;
                    var near=Vector4.Transform(new Vector4(x,y,0,1),inverse);var far=Vector4.Transform(new Vector4(x,y,1,1),inverse);
                    var a=new Vector3(near.X,near.Y,near.Z)/near.W;var b=new Vector3(far.X,far.Y,far.Z)/far.W;
                    Assert.Equal(new Ray(camera.Eye,Vector3.Normalize(b-a)),camera.RayAt(screen,size));
                }
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Host_clones_replacing_scene_finder_or_descriptors_keep_live_rows(bool replaceFinder)
    {
        var item=SceneObject.At("one",null,Vector3.UnitX);var scene=new Scene([item]);
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);
        var editor=new EditorModel<Scene>(session,SceneEditing.Outline);Assert.True(editor.Select("one"));
        double value=1;var basis=SceneEditing.ObjectType();
        var type=replaceFinder
            ?basis with{Find=(_,_)=>item with{Position=new((float)value,0,0)}}
            :basis with{Properties=basis.Properties.Select(p=>p.Id=="position.x"?p with{Get=_=>value}:p).ToArray()};
        Assert.Null(((IObjectType<Scene>)type).RowsVersion);
        var inspector=new Inspector<Scene>(editor).Add(type);
        Assert.Equal(1d,inspector.Rows().Single(r=>r.Id=="position.x").Value);
        value=7;Assert.Equal(7d,inspector.Rows().Single(r=>r.Id=="position.x").Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene> ClosedScene()
    {
        var scene=new Scene([SceneObject.At("gone",null,new(1,2,3))]);
        _=new SceneViewport().Items(scene).ToArray();_=SceneEditing.Outline.Label(scene,"gone");
        return new(scene);
    }
    [Fact]
    public void View_indexes_do_not_keep_a_closed_scene_alive()
    {
        var scene=ClosedScene();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        Assert.False(scene.TryGetTarget(out _));
    }

    [Fact]
    public void Changed_five_thousand_object_F5_frame_and_visible_save_prompt_keep_all_selection_and_drafts()
    {
        var root=Directory.CreateTempSubdirectory("scene-view-perf12-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var path=Path.Combine(data,"level.json");var scene=new Scene(Enumerable.Range(0,5000).Select(i=>SceneObject.At("object_"+i,null,new(i%100,i/100,0))).ToImmutableArray());
            File.WriteAllText(path,new SceneFormat().Write(scene));var saved=File.ReadAllBytes(path);
            var game=Directory.CreateDirectory(Path.Combine(root,"game")).FullName;
            foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(game,"ProjectPlayTestGame"+suffix));
            PlaySettingsStore.Save(root,new("game/ProjectPlayTestGame.exe",["wait"]));
            using var workspace=new DataWorkspace(DataProject.Open(data,schemas));
            workspace.BindPlaySettings(root,null,Path.Combine(root,"local"));workspace.BindProjectPlay(new ProjectPlayBridge());
            Assert.True(workspace.Documents.BindScene("level.json","scene",out var why),why);Assert.True(workspace.Show("level.json"));
            var editor=workspace.SceneWindow!.Editor;var session=editor.Session;Assert.True(editor.Select(scene.Objects.Select(o=>o.Id).ToArray()));
            using var host=new HeadlessHost(1400,900){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());host.Frames(workspace.Draw,3);
            var changedFrames=new List<double>();var initialPromptFrames=new List<double>();var promptFrames=new List<double>();
            for(var run=0;run<3;run++)
            {
                var plan=SceneTranslation.World(session.Current,editor.Selection,Vector3.UnitX);
                Assert.True(plan.Valid);Assert.True(editor.ExecuteAt(session.Revision,plan.Commands,plan.Roots.ToArray()));
                var draft=session.Current;var revision=session.Revision;var journal=session.Journal.ToArray();
                ImGui.GetIO().AddKeyEvent(ImGuiKey.F5,true);var clock=Stopwatch.StartNew();host.Frame(workspace.Draw);clock.Stop();changedFrames.Add(clock.Elapsed.TotalMilliseconds);
                Assert.True(workspace.ProjectPlaySavePending);Assert.Null(workspace.ProjectPlay);
                ImGui.GetIO().AddKeyEvent(ImGuiKey.F5,false);clock.Restart();host.Frame(workspace.Draw);clock.Stop();initialPromptFrames.Add(clock.Elapsed.TotalMilliseconds);
                // Native auto-size/focus settles on the next frame; include it as a separate real sample.
                clock.Restart();host.Frame(workspace.Draw);clock.Stop();promptFrames.Add(clock.Elapsed.TotalMilliseconds);
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Play saved files"&&n.Bounds.Width>200);
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Save"&&n.Focused);
                var request=workspace.SceneViewport!.RenderRequest!;Assert.Equal(5000,request.Items.Count);
                for(var i=0;i<5000;i++){Assert.Equal(draft.Objects[i].Id,request.Items[i].Id);Assert.Equal(draft.Objects[i].Position,request.Items[i].World.Translation);}
                host.Press(workspace.Draw,ImGuiKey.Escape);Assert.False(workspace.ProjectPlaySavePending);Assert.Null(workspace.ProjectPlay);
                Assert.Same(draft,session.Current);Assert.Equal(revision,session.Revision);Assert.Equal(journal,session.Journal);Assert.Equal(5000,editor.Selection.Count);Assert.True(session.Dirty);
                Assert.Equal(saved,File.ReadAllBytes(path));Assert.Equal(run+1,session.History.UndoCount);
            }
            output.WriteLine("5,000 selected empty objects; full typed workspace, 1400x900, CPU rasterization off. Three fixed warm-up frames; each sample follows a fresh 5k-object move revision. Native key down is the changed F5 frame; key up begins native auto-size layout; the following frame has the visible/focused Save decision. No process starts because native Escape cancels each prompt.");
            output.WriteLine("Changed F5 frames (ms): "+string.Join(", ",changedFrames.Select(ms=>ms.ToString("F3")))+"; median "+changedFrames.Order().ElementAt(1).ToString("F3"));
            output.WriteLine("Initial prompt layout frames (ms): "+string.Join(", ",initialPromptFrames.Select(ms=>ms.ToString("F3")))+"; median "+initialPromptFrames.Order().ElementAt(1).ToString("F3"));
            output.WriteLine("Visible prompt frames (ms): "+string.Join(", ",promptFrames.Select(ms=>ms.ToString("F3")))+"; median "+promptFrames.Order().ElementAt(1).ToString("F3"));
        }
        finally{Directory.Delete(root,true);}
    }
}
