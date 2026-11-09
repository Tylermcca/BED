using System.Numerics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneRendererLifecycleTests
{
    private static Camera Camera => new(Vector3.Zero,0,0,6);
    private static SceneRenderIdentity Identity(long revision=0) => new(revision,1,2,3,4,new(100,50),new(400,300),new(2,2));
    private static ViewportItem Item(string id,float z=0) => new(id,Matrix4x4.CreateTranslation(0,0,z),
        new([new(-1,-1,0),new(1,-1,0),new(0,1,0)],[0,1,2]),0xff4080c0);
    private static EditorModel<Scene> Editor() => new(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),
        new Scene([SceneObject.At("near",null,Vector3.Zero),SceneObject.At("far",null,new(0,0,2))])),SceneEditing.Outline);
    private static SceneRenderRequest Capture(SceneRenderIdentity? identity=null,params ViewportItem[] items) =>
        SceneRenderRequest.Capture(identity??Identity(),Camera,items.Length==0?[Item("near"),Item("far",2)]:items);

    [Fact]
    public void CPU_nearest_pick_and_GPU_packets_preserve_exact_item_order_world_transforms_and_ids()
    {
        var mirrored=Item("near") with { World=Matrix4x4.CreateScale(-2,1,1) };
        var far=Item("far",2);
        var request=Capture(null,far,mirrored);
        Assert.Equal(new[]{"far","near"},request.Items.Select(i=>i.Id));
        Assert.Equal(new[]{"far","near"},request.Triangles().Select(i=>i.ItemId));
        Assert.Equal(mirrored.World,request.Items[1].World);
        var position=request.Identity.Origin+request.Identity.Extent/2;
        var hit=request.Pick(position);
        Assert.Equal("near",hit.ItemId);
        Assert.Equal(Picking.Pick([far,mirrored],Camera.RayAt(request.Identity.Extent/2,request.Identity.Extent))!.Value.Distance,hit.Distance);
        var clip=Vector4.Transform(new Vector4(mirrored.Mesh.Positions[0],1),mirrored.World*Camera.View*Camera.Projection(request.Identity.Extent));
        Assert.Equal(new Vector4(clip.X,-clip.Y,clip.Z,clip.W),request.Triangles()[1].A);
        var projected=Camera.Project(Vector3.Transform(mirrored.Mesh.Positions[0],mirrored.World),request.Identity.Extent)!.Value.Screen;
        var gpu=request.Triangles()[1].A;
        Assert.InRange(Vector2.Distance(projected,new((gpu.X/gpu.W+1)*200,(gpu.Y/gpu.W+1)*150)),0,.001f);
    }

    [Fact]
    public void Capture_owns_geometry_and_returned_CPU_copies_cannot_mutate_the_request()
    {
        var item=Item("near");var request=Capture(null,item);var expected=request.Triangles().ToArray();
        item.Mesh.Positions[0]=new(999,999,999);item.Mesh.Indices[0]=2;
        var copy=Assert.Single(request.CopyItems());copy.Mesh.Positions[1]=new(999);copy.Mesh.Indices[1]=0;
        Assert.Equal(expected,request.Triangles());Assert.Equal(new Vector3(-1,-1,0),request.Items[0].Positions[0]);
        Assert.Throws<NotSupportedException>(()=>((IList<Vector3>)request.Items[0].Positions)[0]=Vector3.Zero);
    }

    [Theory]
    [InlineData("revision")][InlineData("camera")][InlineData("source")][InlineData("view")][InlineData("target")]
    [InlineData("origin")][InlineData("extent")][InlineData("scale")]
    public void Any_changed_frame_identity_rejects_delayed_hits_and_misses_without_selection_or_history_changes(string field)
    {
        var editor=Editor();var request=Capture();var hit=request.Pick(new(300,200));var miss=request.Pick(new(-1000,-1000));
        Assert.NotNull(hit.ItemId);Assert.Null(miss.ItemId);editor.Select("far");
        var identity=Identity();identity=field switch
        {
            "revision"=>identity with{DocumentRevision=1},"camera"=>identity with{CameraGeneration=2},
            "source"=>identity with{SourceGeneration=3},"view"=>identity with{ViewGeneration=4},"target"=>identity with{TargetGeneration=5},
            "origin"=>identity with{Origin=Vector2.Zero},"extent"=>identity with{Extent=new(800,300)},_=>identity with{FramebufferScale=Vector2.One}
        };
        var current=Capture(identity);var journal=editor.Session.Journal.Count;
        Assert.False(current.TrySelect(editor,hit));Assert.False(current.TrySelect(editor,miss));
        Assert.Equal(new[]{"far"},editor.Selection);Assert.Equal(journal,editor.Session.Journal.Count);Assert.False(editor.Session.History.CanUndo);
    }

    [Fact]
    public void Current_pick_uses_existing_select_toggle_and_miss_semantics_without_creating_edits()
    {
        var editor=Editor();var request=Capture();var hit=request.Pick(new(300,200));var miss=request.Pick(new(-1000,-1000));
        Assert.True(request.TrySelect(editor,hit));Assert.Equal(new[]{"near"},editor.Selection);
        Assert.True(request.TrySelect(editor,hit,true));Assert.Empty(editor.Selection);
        editor.Select("far");Assert.True(request.TrySelect(editor,miss,true));Assert.Equal(new[]{"far"},editor.Selection);
        Assert.True(request.TrySelect(editor,miss));Assert.Empty(editor.Selection);Assert.False(editor.Session.Dirty);
        Assert.Empty(editor.Session.Journal);Assert.False(editor.Session.History.CanUndo);
    }

    [Fact]
    public void Edit_undo_and_redo_revisions_never_accept_an_old_pick()
    {
        var editor=Editor();var request=Capture();var hit=request.Pick(new(300,200));
        Assert.True(editor.Session.Execute(new MoveObject("near",new(1,0,0))));
        Assert.False(request.TrySelect(editor,hit));Assert.True(editor.Session.Undo());Assert.False(request.TrySelect(editor,hit));
        Assert.True(editor.Session.Redo());Assert.False(request.TrySelect(editor,hit));Assert.Empty(editor.Selection);
    }

    private sealed class Play : IPlaySession { public string Status => "active"; public void Draw() { } public void Dispose() { } }

    [Fact]
    public void Delayed_selection_rechecks_current_document_focus_and_Play_without_edits()
    {
        var editor=Editor();var request=Capture();var hit=request.Pick(new(300,200));editor.Select("far");
        Assert.True(editor.FocusOn("far"));Assert.False(request.TrySelect(editor,hit));Assert.Equal(new[]{"far"},editor.Selection);
        var other=Editor();other.Select("far");other.Add(new PlayMode<Scene>("test","Test",_=>new Play()));
        Assert.True(other.Play("test"));Assert.False(request.TrySelect(other,hit));Assert.Equal(new[]{"far"},other.Selection);
        Assert.Empty(other.Session.Journal);Assert.False(other.Session.History.CanUndo);Assert.True(other.Stop());
    }

    [Fact]
    public void Compiled_GPU_consumer_uses_request_triangles_and_the_exact_push_constant_packet_layout()
    {
        var type=typeof(Brutal.Authoring.Editor.App.EditorApp).Assembly.GetType("Brutal.Authoring.Editor.App.SceneTrianglePipeline",true)!;
        var packet=typeof(Brutal.Authoring.Editor.App.SceneMaterialPacket);
        Assert.Equal(128,System.Runtime.InteropServices.Marshal.SizeOf(packet));
        string[] fields=["A","B","C","ColorA","ColorB","ColorC","UvAB","UvCMode"];
        Assert.Equal(fields.Length,packet.GetFields().Length);
        for(var i=0;i<fields.Length;i++)
        {
            Assert.Equal(typeof(Vector4),packet.GetField(fields[i])!.FieldType);
            Assert.Equal(i*16,System.Runtime.InteropServices.Marshal.OffsetOf(packet,fields[i]).ToInt32());
        }
        var draw=type.GetMethod("Draw",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        Assert.Contains(SceneFrameLifecycleTests.Calls(draw),c=>c.Method.DeclaringType==typeof(Brutal.Authoring.Editor.App.SceneMaterialDraws)&&c.Method.Name=="Build");
        Assert.Equal(typeof(SceneRenderRequest),draw.GetParameters()[1].ParameterType);
        var material=new SceneMaterial(new(.2f,.4f,.6f,1),new string('1',32),new string('a',64));
        var item=Item("material") with {World=Matrix4x4.CreateScale(-2,1,1)*Matrix4x4.CreateTranslation(1,2,3)};
        item=item with {Mesh=item.Mesh with {Uv0=[new(.1f,.2f),new(.7f,.3f),new(.2f,.9f)],Material=material}};
        var request=Capture(null,item);var triangle=Assert.Single(request.Triangles());
        var packed=Assert.Single(Brutal.Authoring.Editor.App.SceneMaterialDraws.Build(request));
        Assert.Equal(triangle.ItemId,packed.ItemId);Assert.Same(material,packed.Material);
        Assert.Equal(triangle.A,packed.Packet.A);Assert.Equal(triangle.B,packed.Packet.B);Assert.Equal(triangle.C,packed.Packet.C);
        Assert.Equal(material.BaseColor,packed.Packet.ColorA);Assert.Equal(material.BaseColor,packed.Packet.ColorB);Assert.Equal(material.BaseColor,packed.Packet.ColorC);
        Assert.Equal(new Vector4(.1f,.2f,.7f,.3f),packed.Packet.UvAB);Assert.Equal(new Vector4(.2f,.9f,1,0),packed.Packet.UvCMode);
        // Check the compiled shader's four struct members, not just the matching source declaration.
        using var stream=type.Assembly.GetManifestResourceStream("BED.Scene.vert.spv");Assert.NotNull(stream);
        using var buffer=new MemoryStream();stream.CopyTo(buffer);var bytes=buffer.ToArray();
        var offsets=new List<(uint Struct,uint Member,uint Offset)>();
        for(var at=20;at<bytes.Length;)
        {
            uint Read(int delta)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+delta,4));
            var instruction=Read(0);var words=(int)(instruction>>16);Assert.True(words>0&&at+words*4<=bytes.Length);
            if((instruction&65535)==72&&words==5&&Read(12)==35)offsets.Add((Read(4),Read(8),Read(16))); // OpMemberDecorate Offset
            at+=words*4;
        }
        var structure=Assert.Single(offsets.GroupBy(x=>x.Struct));
        Assert.Equal(new uint[]{0,1,2,3},structure.OrderBy(x=>x.Member).Select(x=>x.Member));
        Assert.Equal(new uint[]{0,48,96,112},structure.OrderBy(x=>x.Member).Select(x=>x.Offset));
    }

    [Fact]
    public void Duplicate_ids_bad_indices_and_nonfinite_geometry_are_refused_at_capture()
    {
        Assert.Throws<ArgumentException>(()=>Capture(null,Item("same"),Item("same")));
        var bad=Item("bad");bad.Mesh.Indices[0]=99;Assert.Throws<ArgumentException>(()=>Capture(null,bad));
        bad=Item("bad");bad.Mesh.Positions[0]=new(float.NaN,0,0);Assert.Throws<ArgumentException>(()=>Capture(null,bad));
    }

    private sealed class ChangingSource(EditorModel<Scene> editor) : IViewportSource<Scene>
    {
        public bool Change;
        public IEnumerable<ViewportItem> Items(Scene document)
        {
            if(Change){Change=false;Assert.True(editor.Session.Execute(new MoveObject("near",Vector3.One)));}
            return new SceneViewport().Items(document);
        }
        public ICommand<Scene>? MoveTo(Scene document,string id,Vector3 world)=>new MoveObject(id,world);
    }

    [Fact]
    public void A_source_that_changes_the_document_during_capture_cannot_publish_old_geometry_under_a_new_revision()
    {
        using var host=new HeadlessHost{Drawing=false};var editor=Editor();var source=new ChangingSource(editor);var view=new ViewportView<Scene>(editor,source);
        void Draw(){Ui.Begin("Changing scene"u8);view.Draw();Ui.End();}
        host.Frames(Draw,3);Assert.NotNull(view.RenderRequest);source.Change=true;host.Frame(Draw);
        Assert.Null(view.RenderRequest);Assert.Contains("changed during capture",view.Said);
        host.Frame(Draw);Assert.NotNull(view.RenderRequest);Assert.Equal(editor.Session.Revision,view.RenderRequest.Identity.DocumentRevision);
    }

    [Fact]
    public void Real_CPU_view_captures_displayed_frame_and_invalidates_delayed_results_on_camera_source_target_and_close()
    {
        using var host=new HeadlessHost{Drawing=false};var editor=Editor();var view=new ViewportView<Scene>(editor,new SceneViewport());
        void Draw(){Brutal.ImGuiApi.ImGui.SetNextWindowSize(new Brutal.Numerics.float2(500,400));Ui.Begin("Scene"u8);view.Draw();Ui.End();}
        host.Frames(Draw,3);var first=view.RenderRequest!;Assert.Equal(editor.Session.Revision,first.Identity.DocumentRevision);
        Assert.Equal(new[]{"near","far"},first.Items.Select(i=>i.Id));
        var hit=first.Pick(first.Identity.Origin+first.Identity.Extent/2);editor.Select("far");
        Assert.False(view.TrySelectRenderResult(hit));Assert.Equal(new[]{"far"},editor.Selection);
        host.Frame(Draw);var current=view.RenderRequest!;view.Camera=view.Camera.Orbit(.1f,0);Assert.False(view.TrySelectRenderResult(current.Pick(current.Identity.Origin)));
        host.Frame(Draw);current=view.RenderRequest!;view.SourceGeneration++;Assert.False(view.TrySelectRenderResult(current.Pick(current.Identity.Origin)));
        host.Frame(Draw);current=view.RenderRequest!;view.TargetGeneration++;Assert.False(view.TrySelectRenderResult(current.Pick(current.Identity.Origin)));
        view.InvalidateRenderRequest();Assert.Null(view.RenderRequest);Assert.False(view.TrySelectRenderResult(hit));
        var reopened=new ViewportView<Scene>(editor,new SceneViewport());
        host.Frame(()=>{Ui.Begin("Reopened"u8);reopened.Draw();Ui.End();});Assert.NotEqual(first.Identity.ViewGeneration,reopened.RenderRequest!.Identity.ViewGeneration);
    }
}
