using System.Numerics;
using Bed.Editor;
using Bed.Runtime;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

// Orthographic XY coordinates; all commands still target the immutable authoring session.
internal sealed class Scene2FlatView(EditorModel<Scene2> editor,Func<string?> refusal,Func<AssetCatalog?> catalog)
{
    private readonly string viewId="sprite."+SceneRenderRequest.NewViewGeneration();
    private Vector2 centre;private float pixelsPerMetre=64;private bool snap=true;private double grid=1;private string said="";private ViewportTool tool=ViewportTool.Move;
    private (Scene2 Document,long Revision,string[] Selection,string[] Focus,Vector2 Grab,Vector2 Camera,float Zoom,Vector2 Size)? drag;
    private enum HandleKind{None,MoveX,MoveY,ScaleX,ScaleY,ScaleXY,Rotate}
    private sealed record Handle(HandleKind Kind,string Label,Vector2 Anchor,Vector2 Tip,Vector2 Axis,float Radius);
    private HandleKind activeHandle;private Vector2 dragAxis;
    private Scene2? preview;
    private IReadOnlyList<ICommand<Scene2>> pending=[];
    private Vector2 lastOrigin,lastSize;
    private FlatCameraPreferences? cameraPreferences;
    internal Action<Vector3>? DropTarget{get;set;}
    internal Action<Scene2,Func<Vector3,Vector2?>>? BoundsOverlay{get;set;}
    internal Func<bool>? IsCurrent{get;set;}
    internal void BindPreferences(string project,string level,string folder)
    {
        try
        {
            var settings=new FlatCameraPreferences(project,level,folder);if(cameraPreferences?.FileName==settings.FileName)return;
            Cancel();cameraPreferences=settings;(centre,pixelsPerMetre)=settings.Read();
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {Cancel();cameraPreferences=null;centre=Vector2.Zero;pixelsPerMetre=64;said="Could not bind the saved flat camera; using the default view. "+e.Message;}
    }
    internal (Vector2 Origin,Vector2 Size) Rect=>(lastOrigin,lastSize);
    private static float2 F(Vector2 v)=>new(v.X,v.Y);
    private Vector2 Screen(Vector3 v,Vector2 origin,Vector2 size)=>origin+size/2+new Vector2(v.X-centre.X,centre.Y-v.Y)*pixelsPerMetre;
    private Vector2 World(Vector2 p,Vector2 origin,Vector2 size)=>centre+new Vector2(p.X-origin.X-size.X/2,origin.Y+size.Y/2-p.Y)/pixelsPerMetre;
    private static Scene Flat(Scene2 scene)=>Scene2ViewProjection.For(scene).Flat;
    private static IReadOnlyList<Vector3> Corners(Scene flat,Scene2Object item)=>item.Sprite is {} s?SpriteGeometry.Corners(s.Size,s.Pivot,SceneViewport.World(flat,item.Object.Id)):
        SpriteGeometry.Corners(new(.2f,.2f),new(.5f,.5f),SceneViewport.World(flat,item.Object.Id));
    private static bool Inside(Vector2 point,Vector2[] quad)
    {
        var positive=false;var negative=false;
        for(var i=0;i<4;i++){var a=quad[i];var b=quad[(i+1)%4];var cross=(b.X-a.X)*(point.Y-a.Y)-(b.Y-a.Y)*(point.X-a.X);positive|=cross>0;negative|=cross<0;}
        return (positive||negative)&&!(positive&&negative);
    }
    internal void CancelInput()=>Cancel();
    private void Cancel(){drag=null;preview=null;pending=[];activeHandle=HandleKind.None;dragAxis=Vector2.Zero;}
    private Handle[] Handles(Scene2 scene,Vector2 origin,Vector2 size)
    {
        if(editor.Selection.Count==0)return [];
        var flat=Flat(scene);var roots=SceneTranslation.World(flat,editor.Selection,Vector3.Zero).Roots;if(roots.Count==0)return [];
        var pivot=roots.Select(id=>SceneViewport.World(flat,id).Translation).Aggregate(Vector3.Zero,(a,b)=>a+b)/roots.Count;
        var anchor=Screen(pivot,origin,size);if(!float.IsFinite(anchor.X)||!float.IsFinite(anchor.Y))return [];
        var units=MathF.Max(1,ImGui.GetFontSize()/13);var length=MathF.Min(80*units,MathF.Min(size.X,size.Y)*.35f);
        if(tool==ViewportTool.Rotate){var radius=MathF.Min(64*units,MathF.Min(size.X,size.Y)*.35f);return [new(HandleKind.Rotate,"Rotate Z ring",anchor,anchor+new Vector2(radius,0),Vector2.Zero,radius)];}
        Vector2 Axis(Vector3 axis)
        {
            if(tool==ViewportTool.Scale)axis=Vector3.TransformNormal(axis,SceneViewport.World(flat,roots[0]));
            var xy=new Vector2(axis.X,axis.Y);return xy.LengthSquared()>0&&float.IsFinite(xy.LengthSquared())?Vector2.Normalize(xy):Vector2.Zero;
        }
        var x=Axis(Vector3.UnitX);var y=Axis(Vector3.UnitY);Vector2 Tip(Vector2 axis)=>anchor+new Vector2(axis.X,-axis.Y)*length;
        if(x==Vector2.Zero||y==Vector2.Zero)return [];
        if(tool==ViewportTool.Move)return [new(HandleKind.MoveX,"Move X axis",anchor,Tip(x),x,0),new(HandleKind.MoveY,"Move Y axis",anchor,Tip(y),y,0)];
        var diagonal=x+y;if(diagonal.LengthSquared()<.000001f)diagonal=new(1,1);diagonal=Vector2.Normalize(diagonal);
        return [new(HandleKind.ScaleX,"Scale local X axis",anchor,Tip(x),x,0),new(HandleKind.ScaleY,"Scale local Y axis",anchor,Tip(y),y,0),new(HandleKind.ScaleXY,"Scale XY handle",anchor,Tip(diagonal)*.7f+anchor*.3f,diagonal,0)];
    }
    private static bool Hit(Handle handle,Vector2 mouse,float tolerance)
    {
        if(handle.Kind==HandleKind.Rotate)return MathF.Abs(Vector2.Distance(mouse,handle.Anchor)-handle.Radius)<=tolerance;
        if(Vector2.Distance(mouse,handle.Tip)<=tolerance)return true;
        if(handle.Kind is not(HandleKind.MoveX or HandleKind.MoveY))return false;
        var line=handle.Tip-handle.Anchor;var fraction=Vector2.Dot(mouse-handle.Anchor,line)/line.LengthSquared();
        return fraction>=.25f&&fraction<=1&&Vector2.Distance(mouse,handle.Anchor+line*fraction)<=tolerance;
    }
    private void DrawHandles(Scene2 scene,Vector2 origin,Vector2 size,int canvas,bool disabled)
    {
        var draw=ImGui.GetWindowDrawList();var tolerance=MathF.Max(7,ImGui.GetFontSize()*.55f);
        foreach(var handle in Handles(scene,origin,size))
        {
            var colour=ImGui.GetColorU32(disabled?ImGuiCol.TextDisabled:activeHandle==handle.Kind?ImGuiCol.NavCursor:handle.Kind is HandleKind.MoveX or HandleKind.ScaleX?ImGuiCol.PlotLines:ImGuiCol.PlotHistogram);
            Vector2 min,max;
            if(handle.Kind==HandleKind.Rotate){draw.AddCircle(F(handle.Anchor),handle.Radius,colour,64,2);min=handle.Anchor-new Vector2(handle.Radius+tolerance);max=handle.Anchor+new Vector2(handle.Radius+tolerance);}
            else
            {
                draw.AddLine(F(handle.Anchor),F(handle.Tip),colour,2);
                if(handle.Kind is HandleKind.MoveX or HandleKind.MoveY)draw.AddCircleFilled(F(handle.Tip),tolerance*.6f,colour);
                else draw.AddRectFilled(F(handle.Tip-new Vector2(tolerance*.6f)),F(handle.Tip+new Vector2(tolerance*.6f)),colour);
                min=handle.Tip-new Vector2(tolerance);max=handle.Tip+new Vector2(tolerance);
            }
            min=Vector2.Max(min,origin);max=Vector2.Min(max,origin+size);
            if(max.X>min.X&&max.Y>min.Y)Ui.RecordCanvasItem(canvas,"flat.handle."+handle.Kind,handle.Label,new(min.X,min.Y,max.X-min.X,max.Y-min.Y),activeHandle==handle.Kind);
        }
    }
    private void Frame(Scene2 scene,bool all,Vector2 size)
    {
        var flat=Flat(scene);var points=Scene2ViewProjection.For(scene).Resolved.Objects.Where(o=>all||editor.Selection.Contains(o.Object.Id)).SelectMany(o=>Corners(flat,o)).ToArray();
        if(points.Length==0)return;var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);centre=new((float)(((double)min.X+max.X)/2),(float)(((double)min.Y+max.Y)/2));
        pixelsPerMetre=Math.Clamp(MathF.Min(size.X/MathF.Max(.5f,max.X-min.X),size.Y/MathF.Max(.5f,max.Y-min.Y))*.8f,2,2048);
    }
    private void Plan(Vector2 delta)
    {
        if(drag is not {} captured)return;
        if(activeHandle==HandleKind.MoveX)delta=new(delta.X,0);else if(activeHandle==HandleKind.MoveY)delta=new(0,delta.Y);
        if(delta==Vector2.Zero){pending=[];preview=null;return;}
        var effective=Flat(captured.Document);IReadOnlyList<ICommand<Scene2>> commands;
        if(tool==ViewportTool.Move)
        {
            var plan=SceneTranslation.World(effective,captured.Selection,new(delta,0));
            if(!plan.Valid){said=string.Join(" ",plan.Issues.Select(i=>i.Message));pending=[];preview=null;return;}
            commands=plan.Commands.Select(Scene2ViewportSource.Wrap).ToArray();
        }
        else
        {
            var roots=SceneTranslation.World(effective,captured.Selection,Vector3.Zero).Roots;
            if(roots.Count==0){pending=[];preview=null;return;}
            var pivot=roots.Select(id=>SceneViewport.World(effective,id).Translation).Aggregate(Vector3.Zero,(a,b)=>a+b)/roots.Count;
            var from=captured.Grab-new Vector2(pivot.X,pivot.Y);var to=captured.Grab+delta-new Vector2(pivot.X,pivot.Y);
            if(from.LengthSquared()<.000001f){said="Drag away from the selection centre to rotate or scale.";pending=[];preview=null;return;}
            var source=new Scene2ViewportSource(()=>{});ViewportTransformPlan<Scene2> plan;
            var snapping=snap^ImGui.GetIO().KeyAlt;
            if(tool==ViewportTool.Rotate)
            {
                var angle=MathF.Atan2(to.Y,to.X)-MathF.Atan2(from.Y,from.X);
                plan=source.Rotate(captured.Document,captured.Selection,pivot,Quaternion.Identity,Vector3.UnitZ,angle,snapping?MathF.PI/12:0);
            }
            else
            {
                var factor=activeHandle is HandleKind.ScaleX or HandleKind.ScaleY?Vector2.Dot(to,dragAxis)/Vector2.Dot(from,dragAxis):Vector2.Dot(to,from)/from.LengthSquared();if(snapping)factor=MathF.Round(factor*10)/10;
                plan=source.Scale(captured.Document,captured.Selection,activeHandle==HandleKind.ScaleX?new(factor,1,1):activeHandle==HandleKind.ScaleY?new(1,factor,1):new(factor,factor,1));
            }
            if(!plan.Valid){said=string.Join(" ",plan.Issues.Select(i=>i.Message));pending=[];preview=null;return;}
            commands=plan.Commands;
        }
        var candidate=captured.Document;
        foreach(var command in commands){var result=command.Apply(candidate);if(!result.Ok){said=string.Join(" ",result.Issues.Select(i=>i.Message));pending=[];preview=null;return;}candidate=result.Document!;}
        pending=commands;preview=candidate;
    }
    internal void Draw()
    {
        try{if(IsCurrent?.Invoke()==false){Cancel();Ui.TextWrapped("This flat view is no longer current; open the current level."u8);return;}DrawCore();}
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or OverflowException)
        {Cancel();Ui.TextWrapped("Flat view unavailable: "+e.Message);}
    }
    private void DrawCore()
    {
        if(cameraPreferences?.Note is {Length:>0} cameraNote)Ui.TextWrapped(cameraNote);
        Ui.TextWrapped(ViewportControls.Wer?"W: move | E: rotate | R: scale":"W: move | R: rotate | S: scale");
        if(ImGui.IsItemHovered())ImGui.SetTooltip("Drag to move. Middle/right drag pans; wheel zooms; F frames the selection; Home frames all. Escape cancels."u8);
        if(Ui.Button("Move"u8)){Cancel();tool=ViewportTool.Move;}ImGui.SameLine();
        if(Ui.Button("Rotate"u8)){Cancel();tool=ViewportTool.Rotate;}ImGui.SameLine();
        if(Ui.Button("Scale"u8)){Cancel();tool=ViewportTool.Scale;}
        Ui.Checkbox("Snap"u8,ref snap);ImGui.SameLine();ImGui.SetNextItemWidth(100);Ui.InputDouble("Grid (m)"u8,ref grid,0,0,"%g"u8);
        if(!double.IsFinite(grid)||grid<=0)grid=1;
        var origin=new Vector2(ImGui.GetCursorScreenPos().X,ImGui.GetCursorScreenPos().Y);var available=ImGui.GetContentRegionAvail();var size=new Vector2(MathF.Max(64,available.X),MathF.Max(64,available.Y-2*ImGui.GetTextLineHeightWithSpacing()));
        lastOrigin=origin;lastSize=size;var why=refusal();var previousCamera=centre;
        ImGui.InvisibleButton("##flat.xy"u8,F(size),ImGuiButtonFlags.MouseButtonLeft|ImGuiButtonFlags.MouseButtonRight|ImGuiButtonFlags.MouseButtonMiddle|ImGuiButtonFlags.EnableNav);var canvas=Ui.RecordCanvas("Flat XY",true);
        var hovered=ImGui.IsItemHovered();var focused=ImGui.IsItemFocused();var io=ImGui.GetIO();var mouse=new Vector2(io.MousePos.X,io.MousePos.Y);
        DropTarget?.Invoke(new(World(mouse,origin,size),0));
        if(drag is {} held&&(!ReferenceEquals(held.Document,editor.Session.Current)||held.Revision!=editor.Session.Revision||!held.Selection.SequenceEqual(editor.Selection)||!held.Focus.SequenceEqual(editor.Focus)||held.Camera!=centre||held.Zoom!=pixelsPerMetre||held.Size!=size||io.WantTextInput||why is not null||!ImGui.IsWindowFocused()))Cancel();
        if((hovered||focused)&&!io.WantTextInput)
        {
            if(ImGui.IsKeyPressed(ImGuiKey.Escape,false))Cancel();
            if(focused&&ImGui.IsKeyPressed(ImGuiKey.W,false)){Cancel();tool=ViewportTool.Move;}
            if(focused&&ImGui.IsKeyPressed(ViewportControls.Wer?ImGuiKey.E:ImGuiKey.R,false)){Cancel();tool=ViewportTool.Rotate;}
            if(focused&&ImGui.IsKeyPressed(ViewportControls.Wer?ImGuiKey.R:ImGuiKey.S,false)){Cancel();tool=ViewportTool.Scale;}
            if(focused&&ImGui.IsKeyPressed(ImGuiKey.F,false)){Cancel();Frame(editor.Session.Current,false,size);}
            if(focused&&ImGui.IsKeyPressed(ImGuiKey.Home,false)){Cancel();Frame(editor.Session.Current,true,size);}
            if(hovered&&(ImGui.IsMouseDragging(ImGuiMouseButton.Right)||ImGui.IsMouseDragging(ImGuiMouseButton.Middle))){Cancel();centre+=new Vector2(-io.MouseDelta.X,io.MouseDelta.Y)/pixelsPerMetre;}
            if(hovered&&io.MouseWheel!=0){Cancel();var before=World(mouse,origin,size);pixelsPerMetre=Math.Clamp(pixelsPerMetre*MathF.Pow(1.15f,io.MouseWheel),2,2048);centre+=before-World(mouse,origin,size);}
            if(focused&&why is null&&drag is null&&editor.Selection.Count>0&&tool==ViewportTool.Move)
            {
                var delta=new Vector2(ImGui.IsKeyPressed(ImGuiKey.RightArrow)?1:ImGui.IsKeyPressed(ImGuiKey.LeftArrow)?-1:0,ImGui.IsKeyPressed(ImGuiKey.UpArrow)?1:ImGui.IsKeyPressed(ImGuiKey.DownArrow)?-1:0);
                if(delta!=Vector2.Zero)
                {drag=(editor.Session.Current,editor.Session.Revision,editor.Selection.ToArray(),editor.Focus.ToArray(),Vector2.Zero,centre,pixelsPerMetre,size);Plan(delta*(float)grid);if(pending.Count>0&&!editor.ExecuteAt(drag.Value.Revision,pending,editor.Selection.ToArray()))said=string.Join(" ",editor.Refused.Select(i=>i.Message));Cancel();}
            }
        }
        if(!float.IsFinite(centre.X)||!float.IsFinite(centre.Y)){centre=previousCamera;said="Flat camera movement exceeds the coordinate range.";}
        if(!ImGui.IsMouseDown(ImGuiMouseButton.Right)&&!ImGui.IsMouseDown(ImGuiMouseButton.Middle)&&io.MouseWheel==0)cameraPreferences?.Remember(centre,pixelsPerMetre);
        var shown=preview??editor.Session.Current;var flat=Flat(shown);var objects=Scene2ViewProjection.For(shown).Resolved.Objects.ToArray();
        var ordered=SpriteGeometry.DrawOrder(objects.Select(o=>o.Sprite?.Order??int.MinValue).ToArray());
        var quads=objects.Select(o=>Corners(flat,o).Select(p=>Screen(p,origin,size)).ToArray()).ToArray();
        if(hovered&&ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var handle=why is null&&!io.WantTextInput?Handles(shown,origin,size).FirstOrDefault(h=>Hit(h,mouse,MathF.Max(7,ImGui.GetFontSize()*.55f))):null;
            var pick=ordered.Reverse().FirstOrDefault(i=>Inside(mouse,quads[i]),-1);
            if(handle is not null)
            {
                Cancel();activeHandle=handle.Kind;dragAxis=handle.Axis;
                drag=(editor.Session.Current,editor.Session.Revision,editor.Selection.ToArray(),editor.Focus.ToArray(),World(mouse,origin,size),centre,pixelsPerMetre,size);
                said="Dragging "+handle.Label+".";
            }
            else if(pick>=0)
            {
                var id=objects[pick].Object.Id;if(io.KeyCtrl)editor.Toggle(id);else if(!editor.Selection.Contains(id))editor.Select(id);
                said="Selected "+(editor.Outline.Label(shown,id)??id)+".";
                if(why is null&&editor.Selection.Contains(id))drag=(editor.Session.Current,editor.Session.Revision,editor.Selection.ToArray(),editor.Focus.ToArray(),World(mouse,origin,size),centre,pixelsPerMetre,size);
            }
            else editor.Select([]);
        }
        if(drag is {} capture&&ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta=World(mouse,origin,size)-capture.Grab;if(tool==ViewportTool.Move&&(snap^io.KeyAlt))delta=new(MathF.Round(delta.X/(float)grid)*(float)grid,MathF.Round(delta.Y/(float)grid)*(float)grid);Plan(delta);
        }
        if(drag is {} release&&ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {if(pending.Count>0&&!editor.ExecuteAt(release.Revision,pending,release.Selection))said=string.Join(" ",editor.Refused.Select(i=>i.Message));Cancel();}
        var draw=ImGui.GetWindowDrawList();draw.PushClipRect(F(origin),F(origin+size),true);draw.AddRectFilled(F(origin),F(origin+size),ImGui.GetColorU32(ImGuiCol.WindowBg));
        var step=(float)grid*pixelsPerMetre;
        if(step>=8&&step<float.MaxValue)
        {
            var zero=Screen(Vector3.Zero,origin,size);var x=origin.X+((zero.X-origin.X)%step+step)%step;var y=origin.Y+((zero.Y-origin.Y)%step+step)%step;
            for(;x<origin.X+size.X;x+=step)draw.AddLine(new(x,origin.Y),new(x,origin.Y+size.Y),ImGui.GetColorU32(ImGuiCol.Border));
            for(;y<origin.Y+size.Y;y+=step)draw.AddLine(new(origin.X,y),new(origin.X+size.X,y),ImGui.GetColorU32(ImGuiCol.Border));
        }
        var images=catalog();string imageStatus="";
        foreach(var i in ordered)
        {
            var o=objects[i];var q=quads[i];if(q.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y))){said="An object exceeds the flat view coordinate range.";continue;}var selected=editor.Selection.Contains(o.Object.Id);var colour=ImGui.GetColorU32(selected?ImGuiCol.NavCursor:ImGuiCol.Text);
            if(o.Sprite is {} sprite)
            {
                var selection=images?.Select(o.Object.Asset!);
                var status=selection is null?images?.Resolve(o.Object.Asset!).Reason??"No available image catalog.":"Image preview unavailable; showing sprite bounds.";
                if(selection is not null&&ScenePresentation.Current is {} presentation&&presentation.TrySpriteImage(viewId,images!,selection,sprite.Filter,out var texture,out status))
                    draw.AddImageQuad(texture,F(q[0]),F(q[1]),F(q[2]),F(q[3]),new(0,0),new(1,0),new(1,1),new(0,1),0xffffffff);
                if(status.Length>0&&imageStatus.Length==0)imageStatus=o.Object.Id+": "+status;
            }
            draw.AddQuad(F(q[0]),F(q[1]),F(q[2]),F(q[3]),colour,selected?2:1);
            var min=q.Aggregate(Vector2.Min);var max=q.Aggregate(Vector2.Max);Ui.RecordCanvasItem(canvas,o.Object.Id,editor.Outline.Label(shown,o.Object.Id)??o.Object.Id,new(min.X,min.Y,max.X-min.X,max.Y-min.Y),selected);
        }
        BoundsOverlay?.Invoke(shown,world=>Screen(world,origin,size));
        DrawHandles(shown,origin,size,canvas,why is not null);
        if(focused)draw.AddRect(F(origin+new Vector2(2)),F(origin+size-new Vector2(2)),ImGui.GetColorU32(ImGuiCol.NavCursor),0,ImDrawFlags.None,2);
        draw.PopClipRect();
        if(imageStatus.Length>0)Ui.TextWrapped(imageStatus);
        if(why is not null)Ui.TextWrapped(why);Ui.StatusWrapped(said,"scene2.flat.status");
    }
}
