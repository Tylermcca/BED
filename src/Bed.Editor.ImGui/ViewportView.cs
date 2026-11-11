using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>
/// A 3D viewport over an editor model (worksheet AT-030/031). It draws each item's triangles on the CPU through ImGui's
/// draw list, sorted far to near and shaded by a fixed light, over a ground grid, so it runs the same in a window and in
/// headless tests, with no graphics driver (MIXED: the GPU path in docs/research/viewport_plan.md comes when scenes
/// outgrow this; the camera and picking maths carry over). Click picks and selects (Ctrl toggles); left-drag on a
/// selected object moves it across the horizontal plane at its height, snapped to <see cref="Grid"/>, and lets go as
/// one command (one undo step); right-drag orbits, Shift+right-drag pans; the wheel zooms. While a game plays it takes no input at all (it's
/// still drawn if a host draws it). Selected objects also have fixed-screen-size axis and plane move handles.
/// Handles move selected roots together, with preview before one atomic release; X/Y/Z constrains arrow nudges,
/// R chooses rotation rings and S chooses local scale (uniform until X/Y/Z); negative scale factors mirror.
/// Transform sources preview detached plans, then release one revision-guarded undo step; numeric inspector fields
/// remain the precision fallback. Move-only sources keep those extra tools disabled.
/// Ctrl-arrow orbit and Alt snap override remain available. <see cref="MoveBasis"/> is an explicitly supplied rotation
/// basis (world axes by default), with no inferred scale removal. Add it to a window with <see cref="AsPanel"/>.
/// <para>It is also a keyboard control: Tab reaches it and shows the focus cursor round it. Then Tab picks the next
/// object in Outliner order (Shift+Tab the one before), Enter frames the selection, Escape then Tab leaves, the arrows move the picked object one grid cell across the ground, the way the
/// camera faces (one undo step a press; Alt inverts snapping, as when dragging), Ctrl and the arrows orbit, and + and - zoom. With nothing picked, the arrows orbit
/// too. F frames the selected world-space geometry while the canvas has focus, without editing the document.
/// BORROWED: arrow nudging from 2D and 3D editors (Blender, Unity); INVENTED: focused keyboard object cycling.</para>
/// </summary>
public sealed partial class ViewportView<TDoc> where TDoc : class
{
    private (string Id, Vector3 Start, Vector3 Grab, float Height)? drag;   // an object being moved
    private Vector3? dragTo;
    private ViewportGesture<TDoc>? gesture;
    private ViewportGesture<TDoc>? moveGesture;
    private (MoveHandle Handle, Vector3 Pivot, Ray Grab, Quaternion Basis, Grid Grid, Camera Camera, Vector2 Size)? moveCapture;
    private ViewportTransformGesture<TDoc>? rotationGesture;
    private (RotationRing Ring, RotationRingDrag Angle, Quaternion Basis, float Snap, Camera Camera, Vector2 Size)? rotationCapture;
    private IReadOnlyList<RotationRing> rotationTargets = [];
    private ViewportTransformGesture<TDoc>? scaleGesture;
    private (ScaleHandle Handle,Vector2 Start,float Snap,Camera Camera,Vector2 Size)? scaleCapture;
    private IReadOnlyList<ScaleHandle> scaleTargets=[];
    /// <summary>Last visible local-axis and uniform scale targets, relative to LastRect.</summary>
    public IReadOnlyList<ScaleHandle> ScaleTargets=>scaleTargets;
    /// <summary>Scale-factor delta snap; 0.1 means ten percent. Nonpositive disables it; Alt toggles it.</summary>
    public float ScaleSnap { get; set; }=.1f;
    /// <summary>Owner-thread policy for transform tools: null permits the viewport's normal move/rotate/scale checks;
    /// a reason disables those tools and their gestures, cancels an active capture, and is shown as status and tooltip.
    /// Default null. Camera navigation and snapping remain available. The owner may change the result as its state changes.</summary>
    public Func<string?>? TransformRefusal { get; set; }
    private bool MayTransform()
    {
        if(TransformRefusal?.Invoke() is not {} why)return true;
        Said=why;return false;
    }
    private bool focusCanvas;
    private bool canvasHadFocus,tabLeavesCanvas;
    /// <summary>Current implemented tool; switch through SetTool so an active capture is cancelled.</summary>
    public ViewportTool Tool { get; private set; }
    /// <summary>Rotation snap step in radians; nonpositive means off. Alt overrides it during a gesture.</summary>
    public float RotationSnap { get; set; } = MathF.PI/12;
    /// <summary>The last visible rotation targets, relative to LastRect.</summary>
    public IReadOnlyList<RotationRing> RotationTargets => rotationTargets;
    /// <summary>Chooses a supported tool, retaining move-only sources and cancelling any prior capture.</summary>
    public bool SetTool(ViewportTool tool)
    {
        if(tool is not (ViewportTool.Move or ViewportTool.Rotate or ViewportTool.Scale)) return false;
        if(Editor.Playing is not null) { Said="Stop Play before changing tools."; return false; }
        if(!MayTransform()) return false;
        if(tool!=ViewportTool.Move && Source is not IViewportTransformSource<TDoc>)
        { Said="This view only supports moving objects."; return false; }
        if(Tool!=tool) CancelInput();
        Tool=tool;
        if(tool==ViewportTool.Scale) keyConstraint=null; // A fresh S/Scale choice starts uniformly; X/Y/Z then constrains it.
        Said=""; focusCanvas=true; return true;
    }
    private MoveHandleKind? keyConstraint;
    private IReadOnlyList<MoveHandle> moveTargets = [];
    /// <summary>Explicit caller-supplied rotation basis for local handles; identity gives r1 world axes.
    /// Changing this during a capture cancels it, never silently removes scale from an object's transform.</summary>
    public Quaternion MoveBasis { get; set; } = Quaternion.Identity;
    /// <summary>The last projected move targets, relative to LastRect. Empty without a movable selection.</summary>
    public IReadOnlyList<MoveHandle> MoveTargets => moveTargets;
    /// <summary>Cancel before changing a source's configuration or assets in place; no resolver/version is invented here.</summary>
    public void CancelInput() { CancelDrag(); CancelMove(); CancelRotation(); CancelScale(); }


    /// <summary>A viewport over <paramref name="editor"/>, showing what <paramref name="source"/> says.</summary>
    public ViewportView(EditorModel<TDoc> editor, IViewportSource<TDoc> source)
    {
        Editor = editor;
        Source = source;
    }

    /// <summary>The editor it shows and edits.</summary>
    public EditorModel<TDoc> Editor { get; }

    /// <summary>What it draws, and how moves become commands.</summary>
    public IViewportSource<TDoc> Source { get; }

    /// <summary>Optional host drop target, invoked on the canvas item before other controls. Null does nothing.</summary>
    public Action? DropTarget { get; set; }
    internal Action<TDoc,Func<Vector3,Vector2?>>? BoundsOverlay {get;set;}
    private AssetCatalog? materialCatalog;
    /// <summary>Optional admitted catalog for explicit base-color GUID/hash references. Replacing it invalidates delayed view work; null never triggers file lookup.</summary>
    public AssetCatalog? MaterialCatalog
    {
        get=>materialCatalog;
        set{if(!ReferenceEquals(materialCatalog,value)){materialCatalog=value;renderViewGeneration=SceneRenderRequest.NewViewGeneration();}}
    }

    private readonly string renderViewId = "scene." + SceneRenderRequest.NewViewGeneration();
    private ScenePresentation? renderPresenter;
    private Vector2 renderExtent, renderScale;
    private uint renderNativeViewportId;
    private ImGuiContextPtr renderContext;
    private long renderTargetInput, effectiveTargetGeneration;
    /// <summary>Why GPU presentation fell back to the CPU view in the last frame; empty without a host presenter.</summary>
    public string RenderStatus { get; private set; } = "";
    private long renderViewGeneration = SceneRenderRequest.NewViewGeneration();
    private long cameraGeneration;
    private Camera camera = Camera.Default;
    private string[] renderFocus = [], renderSelection = [];
    /// <summary>The camera. Changing it invalidates delayed render/pick results.</summary>
    public Camera Camera
    {
        get => camera;
        set { ArgumentNullException.ThrowIfNull(value); if (camera != value) { camera = value; cameraGeneration++; } }
    }
    /// <summary>Host-supplied source/asset epoch; increment when replacing or changing a mesh resolver's content.</summary>
    public long SourceGeneration { get; set; }
    /// <summary>Host-supplied target epoch; increment on native target replacement, including recovery.</summary>
    public long TargetGeneration { get; set; }
    /// <summary>The last immutable CPU-produced scene frame. Rendering consumers use this request, never re-enumerate the source.</summary>
    public SceneRenderRequest? RenderRequest { get; private set; }
    private void SyncRenderView()
    {
        if (!renderFocus.SequenceEqual(Editor.Focus) || !renderSelection.SequenceEqual(Editor.Selection))
        { renderViewGeneration = SceneRenderRequest.NewViewGeneration(); renderFocus = [.. Editor.Focus]; renderSelection = [.. Editor.Selection]; }
    }
    private SceneRenderIdentity RenderIdentity()
    {
        var presenter=ScenePresentation.Current;
        var viewport=ImGui.FindViewportByID(new ImGuiID(renderNativeViewportId));
        var scale=viewport.Equals(default(ImGuiViewportPtr)) ? Vector2.Zero
            : new Vector2(viewport.FramebufferScale.X,viewport.FramebufferScale.Y);
        // A single/main viewport (including the headless host) may leave this newer native field unset.
        // IO is authoritative for that one window only; never substitute its scale for a secondary viewport.
        if(!viewport.Equals(default(ImGuiViewportPtr)) && viewport.ID.Value==ImGui.GetMainViewport().ID.Value
            && (scale.X<=0||scale.Y<=0))
            scale=new(ImGui.GetIO().DisplayFramebufferScale.X,ImGui.GetIO().DisplayFramebufferScale.Y);
        if(!ReferenceEquals(presenter,renderPresenter)||renderExtent!=LastRect.Size||renderScale!=scale||renderTargetInput!=TargetGeneration)
        {
            effectiveTargetGeneration=SceneRenderRequest.NewViewGeneration();renderPresenter=presenter;
            renderExtent=LastRect.Size;renderScale=scale;renderTargetInput=TargetGeneration;
        }
        return new(Editor.Session.Revision,cameraGeneration,SourceGeneration,renderViewGeneration,effectiveTargetGeneration,
            LastRect.Origin,LastRect.Size,scale);
    }
    /// <summary>Revalidates delayed CPU results against the current view and selection authority. Stale misses never clear selection.</summary>
    public bool TrySelectRenderResult(ScenePickResult result, bool toggle = false)
    {
        var context=ImGui.GetCurrentContext();
        if(context.Equals(default(ImGuiContextPtr))||!context.Equals(renderContext))return false;
        SyncRenderView();
        return RenderRequest is { } current && current.Identity == RenderIdentity() && current.Camera == Camera
            && current.TrySelect(Editor, result, toggle);
    }
    /// <summary>Invalidates requests when a host closes/replaces this view, without touching its document or selection.</summary>
    public void InvalidateRenderRequest()
    {
        renderViewGeneration=SceneRenderRequest.NewViewGeneration();RenderRequest=null;
        cpuFrameItems=null;cpuFrameItemsUnchanged=false;cpuGeometryRequest=null;cpuGeometryCamera=null;
        cpuGeometryTriangles=null;cpuGeometryBounds=null;cpuSelected=[];
    }


    /// <summary>Moves snap to this grid (off: <see cref="Bed.Grid.None"/>). Holding Alt while dragging
    /// inverts it for that move.</summary>
    public Grid Grid { get; set; } = Grid.Uniform(0.5f);

    /// <summary>Where the viewport was last drawn: its top-left corner and size, in screen pixels.</summary>
    public (Vector2 Origin, Vector2 Size) LastRect { get; private set; }

    /// <summary>The screen position of a world point, as last drawn, or null when it's behind the camera.</summary>
    public Vector2? ScreenOf(Vector3 world) => Camera.Project(world, LastRect.Size) is { } p ? LastRect.Origin + p.Screen : null;

    /// <summary>What the viewport last said (what it picked, moved or couldn't do).</summary>
    public string Said { get; private set; } = "";

    /// <summary>This viewport as a window panel.</summary>
    public Panel AsPanel(string id = "editor.viewport", string title = "Viewport") => new(id, title, Draw, Width: 640, Height: 480);

    /// <summary>Draws the viewport into the current ImGui window and handles its mouse input.</summary>
    public void Draw()
    {
        if (TransformRefusal?.Invoke() is not null) CancelInput();
        var context=ImGui.GetCurrentContext();
        if(!context.Equals(renderContext)){InvalidateRenderRequest();renderContext=context;}
        if (gesture is not null && !gesture.Validate(Source)) CancelDrag();
        if (moveGesture is not null && (!moveGesture.Validate(Source) || moveCapture is not { } captured
            || captured.Basis != MoveBasis || captured.Grid != Grid || captured.Camera != Camera)) CancelMove();
        if (rotationGesture is not null && (!rotationGesture.Validate(Source) || rotationCapture is not { } turn
            || turn.Basis != MoveBasis || turn.Snap != RotationSnap || turn.Camera != Camera)) CancelRotation();
        if(scaleGesture is not null && (!scaleGesture.Validate(Source) || scaleCapture is not { } scaling
            || scaling.Snap!=ScaleSnap || scaling.Camera!=Camera)) CancelScale();
        var toolbar = ToV(ImGui.GetCursorScreenPos());
        var header = 2*ImGui.GetFrameHeightWithSpacing();
        var origin = toolbar + new Vector2(0,header);
        var size = Vector2.Max(ToV(ImGui.GetContentRegionAvail())-new Vector2(0,header), new Vector2(64, 64));
        if(moveCapture is { } move && move.Size!=size) CancelMove();
        if(rotationCapture is { } rotate && rotate.Size!=size) CancelRotation();
        if(scaleCapture is { } scalingSize && scalingSize.Size!=size) CancelScale();
        ImGui.SetCursorScreenPos(ToF(origin));
        if(focusCanvas) { if(Editor.Playing is null) ImGui.SetKeyboardFocusHere(); focusCanvas=false; }
        LastRect = (origin, size);
        renderNativeViewportId = ImGui.GetWindowViewport().ID.Value;
        ImGui.InvisibleButton("##viewport"u8, ToF(size), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.EnableNav);
        var treeCanvas = Ui.RecordCanvas("Viewport", Editor.Playing is null, "a game is playing");
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
        var focused = ImGui.IsItemFocused();
        var wasFocused=canvasHadFocus;canvasHadFocus=focused;
        if(!focused)tabLeavesCanvas=false;
        // Camera input (mouse here, keys below) runs before the scene request is captured: a camera changed after it makes
        // the host skip drawing the image it already accepted (SceneTarget.Record), so each moving frame showed an empty target.
        cameraNavigating=false;
        var navigated=Editor.Playing is null&&NavigateCamera(hovered,size);
        if (Editor.Playing is null) DropTarget?.Invoke();
        if (gesture is not null && !gesture.ValidateInput(ImGui.IsWindowFocused(),
            ImGui.IsMouseDown(ImGuiMouseButton.Left), ImGui.IsMouseReleased(ImGuiMouseButton.Left))) CancelDrag();
        if (moveGesture is not null && !moveGesture.ValidateInput(ImGui.IsWindowFocused(),
            ImGui.IsMouseDown(ImGuiMouseButton.Left), ImGui.IsMouseReleased(ImGuiMouseButton.Left))) CancelMove();
        if (rotationGesture is not null && !rotationGesture.ValidateInput(ImGui.IsWindowFocused(),
            ImGui.IsMouseDown(ImGuiMouseButton.Left),ImGui.IsMouseReleased(ImGuiMouseButton.Left))) CancelRotation();
        if(scaleGesture is not null && !scaleGesture.ValidateInput(ImGui.IsWindowFocused(),
            ImGui.IsMouseDown(ImGuiMouseButton.Left),ImGui.IsMouseReleased(ImGuiMouseButton.Left))) CancelScale();
        if (focused) Keyboard(wasFocused);   // before the capture too: its zoom, orbit, framing and nudges change the frame
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(ToF(origin), ToF(origin + size), true);
        draw.AddRectFilled(ToF(origin), ToF(origin + size), (EditorTheme.HighContrastActive ? new ImColor8(0,0,0,255) : new ImColor8(38, 40, 44, 255)), 0, ImDrawFlags.None);
        DrawGround(draw, origin, size);
        var captureDocument = Editor.Session.Current;
        var captureRevision = Editor.Session.Revision;
        var captureSource = SourceGeneration;
        var displayed = scaleGesture is { Active:true } scalingPreview ? scalingPreview.PreviewDocument
            : rotationGesture is { Active:true } rotating ? rotating.PreviewDocument : Editor.Session.Current;
        var items = Source.Items(displayed).ToList();
        var selectionSummary=SelectionSummary(ReferenceEquals(displayed,Editor.Session.Current)?items:Source.Items(Editor.Session.Current).ToList());
        if (drag is { } d && dragTo is { } to)   // show the object where it's being dragged to
            items = items.Select(i => i.Id == d.Id ? i with { World = i.World * Matrix4x4.CreateTranslation(to - d.Start) } : i).ToList();
        if (moveGesture is { Active: true } moving)
        {
            var delta = moving.Target - moving.Start;
            var movingRoots=moving.RootIds.ToHashSet(StringComparer.Ordinal);
            bool Follows(string id)
            {
                if(movingRoots.Contains(id))return true;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (string? node = Editor.Outline.Parent(Editor.Session.Current,id); node is not null && seen.Add(node); node = Editor.Outline.Parent(Editor.Session.Current, node))
                    if (movingRoots.Contains(node)) return true;
                return false;
            }
            items = items.Select(item => Follows(item.Id) ? item with { World = item.World * Matrix4x4.CreateTranslation(delta) } : item).ToList();
        }
        SyncRenderView();
        if (RenderRequest is { } previous && (previous.Items.Count != items.Count
            || previous.Items.Where((item, index) => item.Id != items[index].Id || item.World != items[index].World).Any()))
            renderViewGeneration = SceneRenderRequest.NewViewGeneration(); // Transient transform previews are also different displayed frames.
        if (!ReferenceEquals(captureDocument, Editor.Session.Current) || captureRevision != Editor.Session.Revision || captureSource != SourceGeneration)
        {
            InvalidateRenderRequest();
            Said = "Scene inputs changed during capture; try again.";
        }
        else
        {
            var identity=RenderIdentity();
            if(identity.FramebufferScale.X<=0||identity.FramebufferScale.Y<=0)InvalidateRenderRequest();
            else
            {
                RenderRequest = SceneRenderRequest.Capture(identity, Camera, items);
                cpuFrameItemsUnchanged=cpuFrameItems is {} known&&SameCpuGeometry(known.Request,RenderRequest);
                if(cpuFrameItemsUnchanged)items=cpuFrameItems!.Value.Items;
                else{items=CopyCpuItems(RenderRequest);cpuFrameItems=(RenderRequest,items);}
            }
        }
        var gpu=false; RenderStatus="";
        if(RenderRequest is { } frame && ScenePresentation.Current is { } presenter)
        {
            if(presenter.TryImage(renderViewId,frame,RenderIdentity,out var image,out var why,MaterialCatalog))
            {
                draw.AddImage(image,ToF(origin),ToF(origin+size));gpu=true;RenderStatus=why;
                if(MaterialCatalog is null&&frame.Items.Any(i=>i.Payloads.Any(p=>p.Material.TextureAssetGuid is not null)))
                    RenderStatus="Showing a stand-in texture: this project's assets aren't available.";
            }
            else RenderStatus="CPU view: "+why;
        }
        if(!gpu&&RenderRequest?.Items.Any(i=>i.Payloads.Any(p=>p.Material.TextureAssetGuid is not null))==true)
            RenderStatus+=(RenderStatus.Length==0?"CPU view: ":" ")+"Material textures are not displayed.";
        DrawItems(draw, items, origin, size, treeCanvas,paint:!gpu);
        BoundsOverlay?.Invoke(Editor.Session.Current,world=>Camera.Project(world,size) is {} projected?origin+projected.Screen:null);
        DrawMoveHandles(draw, items, origin, size, treeCanvas);
        if (focused && ImGui.GetCurrentContext().NavCursorVisible)   // inside the canvas, where nothing drawn over it hides it
            draw.AddRect(ToF(origin + new Vector2(2, 2)), ToF(origin + size - new Vector2(2, 2)), ImGui.GetColorU32(ImGuiCol.NavCursor), 0, ImDrawFlags.None, 2);
        DrawNavigationStatus(draw, origin, size, treeCanvas);
        DrawSelectionStatus(draw,origin,size,treeCanvas,selectionSummary);
        draw.PopClipRect();
        Input(hovered, origin, size, navigated);
        var after=ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(ToF(toolbar));
        Ui.BeginDisabled(Editor.Playing is not null,"a game is playing");
        var transformWhy = TransformRefusal?.Invoke();
        Ui.BeginDisabled(transformWhy is not null, transformWhy);
        if(ViewportControls.Wer?Ui.SmallButton("Move (W)"u8):Ui.SmallButton("Move (G)"u8))SetTool(ViewportTool.Move);
        ImGui.SameLine();
        Ui.BeginDisabled(transformWhy is null && Source is not IViewportTransformSource<TDoc>,"This view only supports moving objects.");
        if(ViewportControls.Wer?Ui.SmallButton("Rotate (E)"u8):Ui.SmallButton("Rotate (R)"u8))SetTool(ViewportTool.Rotate);
        ImGui.SameLine();
        if(ViewportControls.Wer?Ui.SmallButton("Scale (R)"u8):Ui.SmallButton("Scale (S)"u8))SetTool(ViewportTool.Scale);
        Ui.EndDisabled();
        Ui.EndDisabled();
        var snapping=Grid.Enabled;
        if(Ui.Checkbox("Snap (B)"u8,ref snapping)){SetSnapping(snapping);focusCanvas=false;}
        ImGui.SameLine();ImGui.SetNextItemWidth(MathF.Max(100,ImGui.GetFontSize()*5));
        var step=Grid.Size.X.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" m";
        if(Ui.BeginCombo("Grid step (m)"u8,step))
        {
            foreach(var choice in new[]{.1f,.25f,.5f,1f})
                if(Ui.Selectable(choice.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" m",Grid.Size==new Vector3(choice)))
                {CancelInput();Grid=Grid with{Size=new Vector3(choice)};focusCanvas=true;}
            Ui.EndCombo();
        }
        Ui.EndDisabled();
        ImGui.SetCursorScreenPos(after); ImGui.Dummy(new float2(0,0));
    }

    internal void RequestKeyboardFocus(){if(Editor.Playing is null)focusCanvas=true;}
    private void SetSnapping(bool enabled)
    {
        CancelInput();
        var size=Grid.Size;
        if(!float.IsFinite(size.X)||!float.IsFinite(size.Y)||!float.IsFinite(size.Z)||size.X<=0||size.Y<=0||size.Z<=0)size=new(.5f);
        Grid=Grid with{Enabled=enabled,Size=size};RotationSnap=enabled?MathF.PI/12:0;
        Said=enabled?"Move snapping on; rotation snaps to 15 degrees.":"Move and rotation snapping off.";focusCanvas=true;
    }

    // The keys while it has the keyboard focus (see the class summary). Arrows are its own: ImGui's move to a neighbour
    // is cancelled, as the table does for its cells.
    private void Keyboard(bool wasFocused)
    {
        if (cameraNavigating || Editor.Playing is not null || drag is not null || moveGesture is not null || rotationGesture is not null || scaleGesture is not null) return;
        var keys = ImGui.GetIO();
        if (!keys.KeyCtrl && !keys.KeyAlt && !keys.KeySuper && !keys.WantTextInput)
        {
            if(ImGui.IsKeyPressed(ImGuiKey.Escape,false)){tabLeavesCanvas=true;Said="Tab leaves the viewport.";return;}
            if(wasFocused&&!tabLeavesCanvas&&ImGui.IsKeyPressed(ImGuiKey.Tab,false))
            {ImGui.Internal.NavMoveRequestCancel();PickNext(keys.KeyShift?-1:1);}
            if(ImGui.IsKeyPressed(ImGuiKey.Enter,false)||ImGui.IsKeyPressed(ImGuiKey.KeypadEnter,false))FrameSelected();
            if (ImGui.IsKeyPressed(ImGuiKey.B, false)) SetSnapping(!Grid.Enabled);
            if (ImGui.IsKeyPressed(ImGuiKey.F, false)) FrameSelected();
            if (ImGui.IsKeyPressed(ViewportControls.Wer?ImGuiKey.W:ImGuiKey.G, false)) SetTool(ViewportTool.Move);
            if (ImGui.IsKeyPressed(ViewportControls.Wer?ImGuiKey.E:ImGuiKey.R, false)) SetTool(ViewportTool.Rotate);
            if (ImGui.IsKeyPressed(ViewportControls.Wer?ImGuiKey.R:ImGuiKey.S, false)) SetTool(ViewportTool.Scale);
            if (ImGui.IsKeyPressed(ImGuiKey.X, false)) keyConstraint = MoveHandleKind.X;
            if (ImGui.IsKeyPressed(ImGuiKey.Y, false)) keyConstraint = MoveHandleKind.Y;
            if (ImGui.IsKeyPressed(ImGuiKey.Z, false)) keyConstraint = MoveHandleKind.Z;
        }
        if (CanvasKeys.Zoom() is not 0 and var by) Camera = Camera.Zoom(by > 0 ? 0.85f : 1 / 0.85f);
        var (x, y) = CanvasKeys.Arrow();
        if (x == 0 && y == 0) return;
        ImGui.Internal.NavMoveRequestCancel();
        if (ImGui.GetIO().KeyCtrl || Editor.Primary is null) Camera = Camera.Orbit(-x * 0.1f, -y * 0.1f);
        else if(Tool==ViewportTool.Scale) NudgeScale(x+y);
        else if(Tool==ViewportTool.Rotate)
        {
            if(keyConstraint is { } axis) NudgeRotation(axis,x+y);
            else Said="Choose X, Y or Z before turning with the arrows.";
        }
        else if (keyConstraint is { } constraint) NudgeConstraint(constraint, x + y);
        else Nudge(Editor.Primary, x, y);
    }

    private void FrameSelected()
    {
        var selected=Editor.Selection.ToHashSet(StringComparer.Ordinal);
        var points=Source.Items(Editor.Session.Current).Where(i=>selected.Contains(i.Id))
            .SelectMany(i=>i.Mesh.Positions.Select(p=>Vector3.Transform(p,i.World))).ToArray();
        if(points.Length==0) { Said="Select an object to frame."; return; }
        if(points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)))
        { Said="The selection's size can't be worked out."; return; }
        var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);
        var halfAngle=MathF.Min(Camera.FieldOfView/2,MathF.Atan(MathF.Tan(Camera.FieldOfView/2)*LastRect.Size.X/LastRect.Size.Y));
        var distance=MathF.Max(.5f,Vector3.Distance(min,max)/2/MathF.Sin(halfAngle)*1.2f);
        if(!float.IsFinite(distance)||distance>1000) { Said="The selection is too large to frame."; return; }
        Camera=Camera.Frame(min,max) with {Distance=distance};
        Said="Framed selection.";
    }

    private void DrawNavigationStatus(ImDrawListPtr draw,Vector2 origin,Vector2 size,int canvas)
    {
        var step=Grid.Size.X>0?Grid.Size.X:.5f;
        var status="F: frame selected | Enter: frame selected | Tab/Shift+Tab: select | Escape then Tab: leave\nRight-drag: orbit | Shift+right-drag: pan | Alt+left: orbit | Middle: pan | Right+WASD/QE: fly | Wheel or +/-: zoom | Grid: "+step.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" m | Snap: "+(Grid.Enabled?"on":"off")+" (B)";
        if(Editor.Playing is not null) status="View navigation unavailable during Play.\nGrid: "+step.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" m";
        var visible="Help";
        var text=ImGui.CalcTextSize(visible);var at=origin+new Vector2(MathF.Max(8,size.X-text.X-8),MathF.Max(8,size.Y-text.Y-8));
        ulong id=(Ui.Tree.At(canvas).Id^0x533568UL)*1099511628211UL;
        DrawOverlayStatus(draw,status,at,origin,size,canvas,id,visibleText:visible);
    }

    private string SelectionSummary(IReadOnlyList<ViewportItem> items)
    {
        if(Editor.Primary is not {} id||items.FirstOrDefault(item=>item.Id==id) is not {} selected)return "No object selected.";
        var name=Editor.Outline.Label(Editor.Session.Current,id)??id;var position=selected.World.Translation;
        string Number(float value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        return (Editor.Selection.Count>1?Editor.Selection.Count+" objects selected. Primary: ":"Selected: ")+name+
            ". World position: X "+Number(position.X)+", Y "+Number(position.Y)+", Z "+Number(position.Z)+" metres.";
    }
    private void DrawSelectionStatus(ImDrawListPtr draw,Vector2 origin,Vector2 size,int canvas,string summary)
    {
        var parent=Ui.Tree.At(canvas);Ui.Tree.Update(canvas,parent with{Value=summary});
        var at=origin+new Vector2(8,8);
        var id=(parent.Id^0xA1103UL)*1099511628211UL;
        var name=Editor.Primary is {} primary?"Selected: "+(Editor.Outline.Label(Editor.Session.Current,primary)??primary):"No selection";
        if(ImGui.CalcTextSize(name).X>MathF.Max(1,size.X-16))name=$"{Editor.Selection.Count} selected";
        DrawOverlayStatus(draw,summary,at,origin,size,canvas,id,selection:true,visibleText:name);
    }
    // Follow the same hierarchy traversal as the Outliner, restricted to displayed source items and focus.
    private void PickNext(int step)
    {
        var displayed=Source.Items(Editor.Session.Current).Select(i=>i.Id).ToHashSet(StringComparer.Ordinal);
        var ids=OutlineOrder.Rows(Editor).Select(row=>row.Id).Where(id=>displayed.Contains(id)&&Editor.InFocus(id)).ToList();
        if (ids.Count == 0) return;
        var at = Editor.Primary is { } p ? ids.IndexOf(p) : -1;
        var next = ids[CanvasKeys.Next(at, step, ids.Count)];
        Editor.Select(next);
        Said = $"Picked {Editor.Outline.Label(Editor.Session.Current,next)??next}.";
    }

    // One grid cell along the ground axis nearest the camera's right (Left, Right) or its facing (Up, Down).
    private void Nudge(string id, int x, int y)
    {
        if(!MayTransform())return;
        if (Source.Items(Editor.Session.Current).FirstOrDefault(i => i.Id == id) is not { } item) return;
        var right = new Vector3(MathF.Cos(Camera.Yaw), 0, -MathF.Sin(Camera.Yaw));
        var ahead = new Vector3(MathF.Sin(Camera.Yaw), 0, MathF.Cos(Camera.Yaw));
        var way = x != 0 ? right * x : ahead * y;
        var axis = MathF.Abs(way.X) >= MathF.Abs(way.Z) ? new Vector3(MathF.Sign(way.X), 0, 0) : new Vector3(0, 0, MathF.Sign(way.Z));
        var invert = ImGui.GetIO().KeyAlt;   // Alt inverts snapping, as when dragging
        var snaps = Grid.Enabled != invert;
        var cell = new Vector3(snaps && Grid.Size.X > 0 ? Grid.Size.X : 0.5f, 0, snaps && Grid.Size.Z > 0 ? Grid.Size.Z : 0.5f);
        var start = item.World.Translation;
        var to = Grid.Snap(start + axis * cell, invert);
        if (to == start) return;
        var command = Source.MoveTo(Editor.Session.Current, id, to);
        Said = command is null ? $"'{id}' can't be moved here."
            : Editor.Execute(command, id) ? $"Moved {id} to ({to.X:0.##}, {to.Y:0.##}, {to.Z:0.##})."
            : string.Join(" ", (Editor.Refused.Count > 0 ? Editor.Refused : Editor.Session.LastIssues).Select(i => i.Message));
    }

    private void Input(bool hovered, Vector2 origin, Vector2 size, bool navigated)
    {
        if (Editor.Playing is not null)   // it steps aside while a game plays: no picking, moving, orbiting or zooming
        {
            CancelInput();
            return;
        }
        if (navigated)return;
        if (ScaleInput(hovered,origin,size)) return;
        if (RotationInput(hovered,origin,size)) return;
        if (MoveInput(hovered, origin, size)) return;
        if (gesture is not null && ImGui.IsKeyPressed(ImGuiKey.Escape)) { CancelDrag(); return; }
        var io = ImGui.GetIO();
        var mouse = ToV(io.MousePos) - origin;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) StartDrag(mouse, size,Tool==ViewportTool.Move);
        if (drag is { } d && ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2)
            && Camera.RayAt(mouse, size).HitPlaneY(d.Height) is { } t)
        {
            var hit = Camera.RayAt(mouse, size).At(t);
            var target = Grid.Snap(d.Start + (hit - d.Grab), invert: io.KeyShift);
            if (gesture?.Preview(target, Source) == true) dragTo = target;
            else CancelDrag();
        }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && drag is { } done)
        {
            if (dragTo is { } to && to != done.Start)
            {
                Said = gesture?.Commit(Source) == true ? $"Moved {done.Id} to ({to.X:0.##}, {to.Y:0.##}, {to.Z:0.##})."
                    : gesture?.Reason ?? "Gesture cancelled.";
            }
            CancelDrag();
        }
    }

    // A click picks: select what's under the mouse (Ctrl toggles), or clear the selection on empty space. Pressing on
    // something already selected starts a move.
    private void StartDrag(Vector2 mouse, Vector2 size, bool move = true)
    {
        CancelDrag();
        var ray = Camera.RayAt(mouse, size);
        var request = RenderRequest;
        if (request is null || request.Identity != RenderIdentity() || request.Camera != Camera) return;
        var items = request.CopyItems().ToList();
        var picked = request.Pick(mouse + LastRect.Origin);
        if (picked.ItemId is not { } pickedId || picked.Distance is not { } pickedDistance)
        {
            if (!ImGui.GetIO().KeyCtrl && Editor.Selection.Count > 0) Editor.Select();
            Said = "";
            return;
        }
        var hit = (Id: pickedId, Distance: pickedDistance);
        if (ImGui.GetIO().KeyCtrl) Editor.Toggle(hit.Id);
        else if (!Editor.Selection.Contains(hit.Id)) Editor.Select(hit.Id);
        Said = $"Picked {hit.Id}.";
        if(!move||!MayTransform()) return;
        var item = items.First(i => i.Id == hit.Id);
        var start = item.World.Translation;
        var grab = ray.At(hit.Distance);
        gesture = ViewportGesture<TDoc>.Begin(Editor, Source, hit.Id);
        if (gesture is null) return;
        drag = (hit.Id, start, ray.HitPlaneY(start.Y) is { } t ? ray.At(t) : grab, start.Y);
    }

    private void CancelDrag()
    {
        gesture?.Dispose(); gesture = null; drag = null; dragTo = null;
    }

    private float HandleScale => EditorWindow<TDoc>.TextScale * ImGui.GetStyle().FontScaleDpi;

    private void CancelMove()
    {
        moveGesture?.Dispose(); moveGesture = null; moveCapture = null;
    }

    private void DrawMoveHandles(ImDrawListPtr draw, List<ViewportItem> items, Vector2 origin, Vector2 size, int canvas)
    {
        if(TransformRefusal?.Invoke() is { } refusal)
        {
            moveTargets=[];rotationTargets=[];scaleTargets=[];
            DrawToolStatus(draw,refusal,origin,size,canvas);
            return;
        }
        moveTargets = []; rotationTargets=[]; scaleTargets=[];
        var item = Editor.Primary is { } primary ? items.FirstOrDefault(value => value.Id == primary) : null;
        if (item is not null && Editor.Playing is null && Tool==ViewportTool.Move) moveTargets = MoveHandles.Layout(Camera, item.World.Translation, size, MoveBasis, HandleScale);
        foreach (var handle in moveTargets)
        {
            var active = moveCapture?.Handle.Kind == handle.Kind;
            var colour = EditorTheme.HighContrastActive ? ImGui.GetColorU32(active ? ImGuiCol.NavCursor : ImGuiCol.Text) : active ? new ImColor8(255,230,100,255) : handle.Kind switch
            {
                MoveHandleKind.X or MoveHandleKind.YZ => new ImColor8(245,110,100,255),
                MoveHandleKind.Y or MoveHandleKind.XZ => new ImColor8(130,220,130,255),
                _ => new ImColor8(125,175,255,255),
            };
            Vector2 min, max, label;
            if (handle.IsPlane)
            {
                var points = handle.Corners.Select(point => origin + point).ToArray();
                draw.AddQuadFilled(ToF(points[0]),ToF(points[1]),ToF(points[2]),ToF(points[3]),(EditorTheme.HighContrastActive ? ImGui.GetColorU32(ImGuiCol.Header) : new ImColor8(120,150,200,80)));
                for (var i=0;i<4;i++) draw.AddLine(ToF(points[i]),ToF(points[(i+1)%4]),colour,1.5f*HandleScale);
                min=points.Aggregate(Vector2.Min); max=points.Aggregate(Vector2.Max); label=(min+max)/2;
            }
            else
            {
                var a=origin+handle.Start; var b=origin+handle.End;
                draw.AddLine(ToF(a),ToF(b),colour,2*HandleScale); draw.AddCircleFilled(ToF(b),4*HandleScale,colour);
                min=Vector2.Min(a,b)-new Vector2(8*HandleScale); max=Vector2.Max(a,b)+new Vector2(8*HandleScale); label=b;
            }
            draw.AddText(ToF(label),colour,handle.Kind.ToString());
            Ui.RecordCanvasItem(canvas,"move.handle."+handle.Kind,"Move "+handle.Kind,
                new(min.X,min.Y,max.X-min.X,max.Y-min.Y),active || keyConstraint==handle.Kind);
        }
        if(item is not null && Editor.Playing is null && Tool==ViewportTool.Rotate
            && Source is IViewportTransformSource<TDoc>) DrawRotationRings(draw,item.World.Translation,origin,size,canvas);
        if(item is not null && Editor.Playing is null && Tool==ViewportTool.Scale
            && Source is IViewportTransformSource<TDoc>) DrawScaleHandles(draw,item.World,origin,size,canvas);
        if (item is null && keyConstraint is null && RenderStatus.Length==0) return;
        var kind = rotationCapture?.Ring.Axis ?? moveCapture?.Handle.Kind ?? keyConstraint;
        var tool = Tool.ToString();
        var status = Editor.Playing is not null ? tool+" unavailable during Play."
            : kind is { } selected ? $"{tool} {selected}: arrows edit; Alt toggles snap." : tool+": drag a handle; X/Y/Z constrains arrows.";
        if(Tool==ViewportTool.Rotate)
        {
            var detail=rotationGesture is { Reason.Length:>0 } refused ? refused.Reason : Said;
            if(detail.Length>0) status=(kind is { } axis ? $"Rotate {axis}: " : "Rotate: ")+detail;
        }
        if(Tool==ViewportTool.Scale)
        {
            var axis=scaleCapture?.Handle.Kind.ToString() ?? keyConstraint?.ToString() ?? "Uniform";
            status=$"Scale local {axis}: arrows edit; snap {(ScaleSnap>0 ? ScaleSnap.ToString("0.###") : "off")}; Alt toggles snap.";
            var detail=scaleGesture is { Reason.Length:>0 } refusedScale ? refusedScale.Reason : Said;
            if(detail.Length>0) status+=" "+detail;
            if(Editor.Playing is not null) status="Scale unavailable during Play.";
        }
        if(RenderStatus.Length>0)status=item is null&&keyConstraint is null ? RenderStatus : status+" "+RenderStatus;
        DrawToolStatus(draw,status,origin,size,canvas);
    }

    private void DrawToolStatus(ImDrawListPtr draw,string status,Vector2 origin,Vector2 size,int canvas)
    {
        var room=MathF.Max(1,size.X-32-ImGui.CalcTextSize("Help"u8).X);
        var visible=status;
        if(ImGui.CalcTextSize(visible).X>room)
            visible=status.Contains("read-only",StringComparison.OrdinalIgnoreCase)?"Read-only":RenderStatus.Length>0?"Preview notice":Tool+" help";
        if(ImGui.CalcTextSize(visible).X>room)visible="Notice";
        var at=origin+new Vector2(8,MathF.Max(8,size.Y-ImGui.GetTextLineHeight()-8));
        ulong id=(14695981039346656037UL ^ Ui.Tree.At(canvas).Id)*1099511628211UL;
        foreach(var c in "move.constraint.status") id=(id^c)*1099511628211UL;
        DrawOverlayStatus(draw,status,at,origin,size,canvas,id,visibleText:visible);
    }

    private void DrawOverlayStatus(ImDrawListPtr draw,string full,Vector2 at,Vector2 origin,Vector2 size,int canvas,ulong id,bool selection=false,string? visibleText=null)
    {
        var width=MathF.Max(1,size.X-16);var visible=visibleText??full;
        var painted=ImGui.CalcTextSize(visible,false,width);
        at.Y=Math.Clamp(at.Y,origin.Y+8,MathF.Max(origin.Y+8,origin.Y+size.Y-painted.Y-8));
        // Canvas clear colour stays dark in both themes: give every overlay its own opaque paper.
        var paper=ImGui.GetColorU32(ImGuiCol.WindowBg);var ink=ImGui.GetColorU32(ImGuiCol.Text);
        draw.AddRectFilled(ToF(at-new Vector2(3,2)),ToF(at+new Vector2(MathF.Min(painted.X,width)+3,painted.Y+2)),paper);
        draw.AddText(ImGui.GetFont(),ImGui.GetFontSize(),ToF(at),ink,visible,width);
        var bounds=new UiBounds(at.X,at.Y,MathF.Min(painted.X,width),painted.Y);
        var enabled=selection||Editor.Playing is null;
        var clipMin=draw.GetClipRectMin();var clipMax=draw.GetClipRectMax();
        var clipped=at.X<clipMin.X||at.Y<clipMin.Y||at.X+painted.X>clipMax.X||at.Y+painted.Y>clipMax.Y;
        Ui.Tree.Record(new(id,UiRole.Status,full,bounds,enabled,enabled?null:"a game is playing",false,false,false,null,null,null,canvas,UiActions.None,UiIdKind.Semantic,
            TextEvidence:new(new(clipMin.X,clipMin.Y,MathF.Max(0,clipMax.X-clipMin.X),MathF.Max(0,clipMax.Y-clipMin.Y)),new(at.X,at.Y,painted.X,painted.Y),width,clipped),
            Tooltip:full,ViewportId:Ui.Tree.At(canvas).ViewportId));
        if(ImGui.IsMouseHoveringRect(new(bounds.X,bounds.Y),new(bounds.X+bounds.Width,bounds.Y+bounds.Height))
            &&ImGui.IsItemHovered(ImGuiHoveredFlags.ForTooltip|ImGuiHoveredFlags.AllowWhenDisabled))ImGui.SetTooltip(full);
    }

    private bool MoveInput(bool hovered, Vector2 origin, Vector2 size)
    {
        if(!MayTransform())return false;
        var io=ImGui.GetIO(); var mouse=ToV(io.MousePos)-origin;
        if (moveGesture is not null)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape,false)) { Said="Move cancelled."; CancelMove(); return true; }
            if (moveCapture is not { } capture) { CancelMove(); return true; }
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsMouseDragging(ImGuiMouseButton.Left,2))
            {
                if (!capture.Handle.TryDelta(capture.Pivot,capture.Grab,Camera.RayAt(mouse,size),out var delta)
                    || !MoveHandles.TrySnapDelta(capture.Handle.Kind,delta,capture.Basis,capture.Grid,io.KeyShift,out delta)
                    || !moveGesture.Preview(moveGesture.Start+delta,Source))
                { Said="This move cannot be projected here."; CancelMove(); return true; }
            }
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                Said=moveGesture.Commit(Source) ? "Moved the selected roots." : moveGesture.Reason;
                CancelMove();
            }
            return true; // The captured move owns pointer, wheel, orbit and keyboard until release/cancel.
        }
        if (drag is not null || !hovered || io.KeyCtrl || !ImGui.IsItemClicked(ImGuiMouseButton.Left)) return false;
        var handle=moveTargets.OrderByDescending(target=>target.IsPlane).FirstOrDefault(target=>target.Hit(mouse,8*HandleScale));
        if(handle is null) return false;
        var captured=ViewportGesture<TDoc>.BeginSelection(Editor,Source);
        if(captured is null) { Said="This selection cannot be moved."; return true; }
        var ray=Camera.RayAt(mouse,size);
        if(!handle.TryDelta(captured.Start,ray,ray,out _)) { captured.Dispose(); Said="This move is parallel to the view."; return true; }
        moveGesture=captured; moveCapture=(handle,captured.Start,ray,MoveBasis,Grid,Camera,size);
        if(!handle.IsPlane) keyConstraint=handle.Kind;
        Said="Move "+handle.Kind+"; release to commit or Escape to cancel.";
        return true;
    }

    private void CancelRotation()
    {
        rotationGesture?.Dispose(); rotationGesture=null; rotationCapture=null;
    }

    private float RotationStep(bool invert)
    {
        if(!float.IsFinite(RotationSnap)) return float.NaN;
        return (RotationSnap>0)!=invert ? (RotationSnap>0 ? RotationSnap : MathF.PI/12) : 0;
    }

    private static Vector3 AxisVector(MoveHandleKind kind)=>kind==MoveHandleKind.X ? Vector3.UnitX
        : kind==MoveHandleKind.Y ? Vector3.UnitY : Vector3.UnitZ;

    private void DrawRotationRings(ImDrawListPtr draw,Vector3 pivot,Vector2 origin,Vector2 size,int canvas)
    {
        if(TransformRefusal?.Invoke() is not null){rotationTargets=[];return;}
        rotationTargets=RotationRings.Layout(Camera,pivot,size,MoveBasis,HandleScale);
        foreach(var ring in rotationTargets)
        {
            var active=rotationCapture?.Ring.Axis==ring.Axis;
            var colour=EditorTheme.HighContrastActive ? ImGui.GetColorU32(active ? ImGuiCol.NavCursor : ImGuiCol.Text) : active ? new ImColor8(255,230,100,255) : ring.Axis==MoveHandleKind.X
                ? new ImColor8(245,110,100,255) : ring.Axis==MoveHandleKind.Y
                ? new ImColor8(130,220,130,255) : new ImColor8(125,175,255,255);
            for(var i=0;i<ring.Points.Count;i++)
                draw.AddLine(ToF(origin+ring.Points[i]),ToF(origin+ring.Points[(i+1)%ring.Points.Count]),colour,(active?3:2)*HandleScale);
            var label=ring.Points.MaxBy(point=>Vector2.DistanceSquared(point,ring.Centre));
            draw.AddText(ToF(origin+label),colour,ring.Axis.ToString());
            var min=ring.Points.Aggregate(Vector2.Min)+origin-new Vector2(8*HandleScale);
            var max=ring.Points.Aggregate(Vector2.Max)+origin+new Vector2(8*HandleScale);
            Ui.RecordCanvasItem(canvas,"rotate.handle."+ring.Axis,"Rotate "+ring.Axis,
                new(min.X,min.Y,max.X-min.X,max.Y-min.Y),active || keyConstraint==ring.Axis);
        }
    }

    private bool RotationInput(bool hovered,Vector2 origin,Vector2 size)
    {
        if(!MayTransform())return false;
        var io=ImGui.GetIO(); var mouse=ToV(io.MousePos)-origin;
        if(rotationGesture is not null)
        {
            if(ImGui.IsKeyPressed(ImGuiKey.Escape,false)) { Said="Rotation cancelled."; CancelRotation(); return true; }
            if(rotationCapture is not { } capture || Source is not IViewportTransformSource<TDoc> source)
            { CancelRotation(); return true; }
            if(ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsMouseDragging(ImGuiMouseButton.Left,2))
            {
                if(!capture.Angle.Update(Camera.RayAt(mouse,size))) { Said="This ring cannot be projected here."; CancelRotation(); return true; }
                rotationGesture.Preview((doc,selected)=>source.Rotate(doc,selected,capture.Ring.Pivot,capture.Basis,
                    AxisVector(capture.Ring.Axis),capture.Angle.Radians,RotationStep(io.KeyShift)));
            }
            if(ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                Said=rotationGesture.Commit() ? "Rotated the selected roots." : rotationGesture.Reason;
                CancelRotation();
            }
            return true;
        }
        if(Tool!=ViewportTool.Rotate || !hovered || io.KeyCtrl || !ImGui.IsItemClicked(ImGuiMouseButton.Left)
            || Source is not IViewportTransformSource<TDoc> transforms) return false;
        var ring=rotationTargets.FirstOrDefault(target=>target.Hit(mouse));
        if(ring is null) return false;
        var angle=RotationRingDrag.Begin(ring,Camera.RayAt(mouse,size));
        var gesture=ViewportTransformGesture<TDoc>.Begin(Editor,Source);
        if(angle is null || gesture is null) { gesture?.Dispose(); Said="This selection cannot be rotated here."; return true; }
        if(!gesture.Preview((doc,selected)=>transforms.Rotate(doc,selected,ring.Pivot,MoveBasis,AxisVector(ring.Axis),0,RotationStep(io.KeyShift))))
        { Said=gesture.Reason; gesture.Dispose(); return true; }
        rotationGesture=gesture; rotationCapture=(ring,angle,MoveBasis,RotationSnap,Camera,size);
        Said=""; keyConstraint=ring.Axis; return true;
    }

    private void NudgeRotation(MoveHandleKind axis,int sign)
    {
        if(!MayTransform())return;
        if(Source is not IViewportTransformSource<TDoc> source || Editor.Primary is not { } primary
            || Source.Items(Editor.Session.Current).FirstOrDefault(item=>item.Id==primary) is not { } item) return;
        using var gesture=ViewportTransformGesture<TDoc>.Begin(Editor,Source);
        if(gesture is null) { Said="This selection cannot be rotated here."; return; }
        var step=RotationStep(ImGui.GetIO().KeyAlt);
        var angle=sign*(step>0 ? step : MathF.PI/180);
        if(!gesture.Preview((doc,selected)=>source.Rotate(doc,selected,item.World.Translation,MoveBasis,AxisVector(axis),angle,step)))
        { Said=gesture.Reason; return; }
        Said=gesture.Commit() ? "Rotated the selected roots." : gesture.Reason;
    }

    private void CancelScale()
    {
        scaleGesture?.Dispose(); scaleGesture=null; scaleCapture=null;
    }

    private float ScaleStep(bool invert)=>!float.IsFinite(ScaleSnap) ? float.NaN
        : (ScaleSnap>0)!=invert ? (ScaleSnap>0 ? ScaleSnap : .1f) : 0;

    private void DrawScaleHandles(ImDrawListPtr draw,Matrix4x4 world,Vector2 origin,Vector2 size,int canvas)
    {
        if(TransformRefusal?.Invoke() is not null){scaleTargets=[];return;}
        // Keep the captured axes fixed while the preview crosses zero into a mirror.
        scaleTargets=ScaleHandles.Layout(Camera,scaleGesture is { } captured
            && Editor.Primary is { } id ? Source.Items(captured.Document).First(item=>item.Id==id).World : world,size,HandleScale);
        foreach(var handle in scaleTargets)
        {
            var active=scaleCapture?.Handle.Kind==handle.Kind;
            var colour=EditorTheme.HighContrastActive ? ImGui.GetColorU32(active ? ImGuiCol.NavCursor : ImGuiCol.Text) : active ? new ImColor8(255,230,100,255) : handle.Kind switch
            {
                ScaleHandleKind.X=>new ImColor8(245,110,100,255),
                ScaleHandleKind.Y=>new ImColor8(130,220,130,255),
                ScaleHandleKind.Z=>new ImColor8(125,175,255,255),
                _=>new ImColor8(235,235,235,255),
            };
            var end=origin+handle.End; var half=new Vector2(5*HandleScale);
            if(handle.Kind!=ScaleHandleKind.Uniform) draw.AddLine(ToF(origin+handle.Centre),ToF(end),colour,2*HandleScale);
            draw.AddRectFilled(ToF(end-half),ToF(end+half),colour);
            draw.AddText(ToF(end+new Vector2(7*HandleScale,0)),colour,handle.Kind==ScaleHandleKind.Uniform ? "All" : handle.Kind.ToString());
            var bounds=new UiBounds(end.X-8*HandleScale,end.Y-8*HandleScale,16*HandleScale,16*HandleScale);
            Ui.RecordCanvasItem(canvas,"scale.handle."+handle.Kind,"Scale local "+handle.Kind,bounds,active);
        }
    }

    private bool ScaleInput(bool hovered,Vector2 origin,Vector2 size)
    {
        if(!MayTransform())return false;
        var io=ImGui.GetIO(); var mouse=ToV(io.MousePos)-origin;
        if(scaleGesture is not null)
        {
            if(ImGui.IsKeyPressed(ImGuiKey.Escape,false)) { Said="Scale cancelled."; CancelScale(); return true; }
            if(scaleCapture is not { } capture || Source is not IViewportTransformSource<TDoc> source)
            { CancelScale(); return true; }
            if(ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsMouseDragging(ImGuiMouseButton.Left,2))
            {
                if(!capture.Handle.TryFactors(capture.Start,mouse,ScaleStep(io.KeyShift),out var factors))
                { Said="This scale factor is not finite."; CancelScale(); return true; }
                var valid=scaleGesture.Preview((doc,selected)=>source.Scale(doc,selected,factors));
                Said=valid ? $"Factors ({factors.X:0.###}, {factors.Y:0.###}, {factors.Z:0.###})." : scaleGesture.Reason;
            }
            if(ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                Said=scaleGesture.Commit() ? "Scaled the selected roots." : scaleGesture.Reason;
                CancelScale();
            }
            return true;
        }
        if(Tool!=ViewportTool.Scale || !hovered || io.KeyCtrl || !ImGui.IsItemClicked(ImGuiMouseButton.Left)
            || Source is not IViewportTransformSource<TDoc> transforms) return false;
        var handle=scaleTargets.FirstOrDefault(target=>target.Hit(mouse));
        if(handle is null) return false;
        var captured=ViewportTransformGesture<TDoc>.Begin(Editor,Source);
        if(captured is null) { Said="This selection cannot be scaled."; return true; }
        if(!captured.Preview((doc,selected)=>transforms.Scale(doc,selected,Vector3.One)))
        { Said=captured.Reason; captured.Dispose(); return true; }
        scaleGesture=captured; scaleCapture=(handle,mouse,ScaleSnap,Camera,size);
        keyConstraint=handle.Kind==ScaleHandleKind.Uniform ? null : (MoveHandleKind)((int)handle.Kind-1);
        Said=""; return true;
    }

    private void NudgeScale(int sign)
    {
        if(!MayTransform())return;
        if(Source is not IViewportTransformSource<TDoc> source) return;
        using var gesture=ViewportTransformGesture<TDoc>.Begin(Editor,Source);
        if(gesture is null) { Said="This selection cannot be scaled."; return; }
        var step=ScaleStep(ImGui.GetIO().KeyAlt);
        var factor=1+sign*(step>0 ? step : .01f);
        if(!float.IsFinite(step)) factor=float.NaN;
        var factors=keyConstraint switch
        {
            MoveHandleKind.X=>new Vector3(factor,1,1), MoveHandleKind.Y=>new(1,factor,1),
            MoveHandleKind.Z=>new(1,1,factor), _=>new(factor),
        };
        if(!gesture.Preview((doc,selected)=>source.Scale(doc,selected,factors))) { Said=gesture.Reason; return; }
        Said=gesture.Commit() ? "Scaled the selected roots." : gesture.Reason;
    }

    private void NudgeConstraint(MoveHandleKind kind, int sign)
    {
        if(!MayTransform())return;
        var io=ImGui.GetIO(); var snapped=Grid.Enabled!=io.KeyAlt;
        var index=kind==MoveHandleKind.X ? 0 : kind==MoveHandleKind.Y ? 1 : 2;
        var cell=snapped && Grid.Size[index]>0 ? Grid.Size[index] : .5f;
        var components=index==0 ? new Vector3(sign*cell,0,0) : index==1 ? new(0,sign*cell,0) : new(0,0,sign*cell);
        var world=Vector3.Transform(components,MoveBasis);
        if(!MoveHandles.TrySnapDelta(kind,world,MoveBasis,Grid,io.KeyAlt,out world)) { Said="The move basis or grid is invalid."; return; }
        using var captured=ViewportGesture<TDoc>.BeginSelection(Editor,Source);
        Said=captured is null ? "This selection cannot be moved."
            : captured.Preview(captured.Start+world,Source) && captured.Commit(Source) ? "Moved the selected roots." : captured.Reason;
    }

    private void DrawGround(ImDrawListPtr draw, Vector2 origin, Vector2 size)
    {
        var step = Grid.Enabled && Grid.Size.X > 0 ? Grid.Size.X : 1f;
        var half = MathF.Ceiling(10 / step) * step;
        for (var v = -half; v <= half + 1e-4f; v += step)
        {
            var axis = MathF.Abs(v) < 1e-4f;
            var colour = EditorTheme.HighContrastActive
                ? (axis ? new ImColor8(255,255,255,255) : new ImColor8(170,170,170,255))
                : axis ? new ImColor8(90, 95, 105, 255) : new ImColor8(58, 61, 66, 255);
            Line(draw, origin, size, new Vector3(v, 0, -half), new Vector3(v, 0, half), colour);
            Line(draw, origin, size, new Vector3(-half, 0, v), new Vector3(half, 0, v), colour);
        }
    }

    private void Line(ImDrawListPtr draw, Vector2 origin, Vector2 size, Vector3 a, Vector3 b, ImColor8 colour)
    {
        if (Camera.Project(a, size) is { } pa && Camera.Project(b, size) is { } pb)
            draw.AddLine(ToF(origin + pa.Screen), ToF(origin + pb.Screen), colour, 1);
    }

    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.4f, 1, -0.6f));

    private (SceneRenderRequest Request,List<ViewportItem> Items)? cpuFrameItems;
    private bool cpuFrameItemsUnchanged;
    private SceneRenderRequest? cpuGeometryRequest;
    private Camera? cpuGeometryCamera;
    private Vector2 cpuGeometryOrigin,cpuGeometrySize;
    private bool cpuGeometryPaint;
    private List<(float Depth,Vector2 A,Vector2 B,Vector2 C,ImColor8 Colour,ImColor8 SelectedColour,int Item)>? cpuGeometryTriangles;
    private List<(string Id,UiBounds Bounds)>? cpuGeometryBounds;
    private bool[] cpuSelected=[];

    // Private consumers never mutate these arrays. Share their owned copies within this displayed capture;
    // public SceneRenderRequest.CopyItems still returns independently mutable meshes for every item.
    private static List<ViewportItem> CopyCpuItems(SceneRenderRequest request)
    {
        var meshes=new Dictionary<MeshPayload,MeshData>(ReferenceEqualityComparer.Instance);
        MeshData Copy(MeshPayload payload)
        {
            if(!meshes.TryGetValue(payload,out var mesh))
            {
                mesh=new(payload.Positions.ToArray(),payload.Indices.ToArray())
                {Normals=payload.Normals.ToArray(),Uv0=payload.Uv0.ToArray(),Uv0Origin=MeshUv0Origin.TopLeft,Material=payload.Material};
                meshes.Add(payload,mesh);
            }
            return mesh;
        }
        var result=new List<ViewportItem>(request.Items.Count);
        foreach(var item in request.Items)
        {
            var mesh=Copy(item.Payload);
            if(!ReferenceEquals(item.Payloads[0],item.Payload))mesh=mesh with{Parts=Array.AsReadOnly(item.Payloads.Select(Copy).ToArray())};
            result.Add(new(item.Id,item.World,mesh,item.Colour));
        }
        return result;
    }

    // Compare owned captures exactly, sharing each geometry comparison across instances of a mesh.
    // Mutable source arrays are recaptured every frame before this comparison; no producer is trusted.
    private static bool SameCpuGeometry(SceneRenderRequest before,SceneRenderRequest after)
    {
        if(before.Items.Count!=after.Items.Count)return false;
        var checkedParts=new HashSet<(MeshPayload,MeshPayload)>();
        bool SamePayload(MeshPayload x,MeshPayload y)
        {
            if(!checkedParts.Add((x,y)))return true;
            return x.Material.BaseColor==y.Material.BaseColor&&x.Material.TextureAssetGuid==y.Material.TextureAssetGuid
                &&x.Material.TextureSourceHash==y.Material.TextureSourceHash&&x.Positions.SequenceEqual(y.Positions)
                &&x.Indices.SequenceEqual(y.Indices)&&x.Normals.SequenceEqual(y.Normals)&&x.Uv0.SequenceEqual(y.Uv0);
        }
        for(var i=0;i<before.Items.Count;i++)
        {
            var a=before.Items[i];var b=after.Items[i];
            if(a.Id!=b.Id||a.World!=b.World||a.Colour!=b.Colour||a.Payloads.Count!=b.Payloads.Count||!SamePayload(a.Payload,b.Payload))return false;
            for(var j=0;j<a.Payloads.Count;j++)if(!SamePayload(a.Payloads[j],b.Payloads[j]))return false;
        }
        return true;
    }

    // Painter's algorithm: every visible triangle, far to near, flat-shaded by one light.
    private void DrawItems(ImDrawListPtr draw, List<ViewportItem> items, Vector2 origin, Vector2 size, int treeCanvas, bool paint = true)
    {
        var tris = new List<(float Depth, Vector2 A, Vector2 B, Vector2 C, ImColor8 Colour,ImColor8 SelectedColour,int Item)>();
        var bounds=new List<(string Id,UiBounds Bounds)>();
        var eye = Camera.Eye;
        var selection = Editor.Selection.ToHashSet(StringComparer.Ordinal);
        var reuse=RenderRequest is not null&&cpuGeometryRequest is not null&&cpuFrameItemsUnchanged&&cpuGeometryCamera==Camera
            &&cpuGeometryOrigin==origin&&cpuGeometrySize==size&&cpuGeometryPaint==paint;
        if(reuse){tris=cpuGeometryTriangles!;bounds=cpuGeometryBounds!;}
        else
        {
            // Selection changes only shading/borders. Projection, depth order and bounds stay valid.
            var viewProjection=Camera.View*Camera.Projection(size);
            Vector3[] world=[];
            (Vector2 Screen,float Depth)?[] projected=[];
            var materialColours=new Dictionary<SceneMaterial,uint>(ReferenceEqualityComparer.Instance);
            for(var itemIndex=0;itemIndex<items.Count;itemIndex++)
            {
                var item=items[itemIndex];
                UiBounds itemBounds = default;
                foreach(var mesh in item.Mesh.Parts ?? new[] { item.Mesh })
                {
                    var colour=item.Colour;
                    if(mesh.Material is {} material&&!materialColours.TryGetValue(material,out colour))
                        materialColours.Add(material,colour=PackColour(material.BaseColor));
                    // Reuse scratch storage across all mesh instances in this rebuild; Camera's matrix is invariant here.
                    if(world.Length<mesh.Positions.Length){world=new Vector3[mesh.Positions.Length];projected=new (Vector2 Screen,float Depth)?[mesh.Positions.Length];}
                    for(var vertex=0;vertex<mesh.Positions.Length;vertex++)
                    {
                        world[vertex]=Vector3.Transform(mesh.Positions[vertex],item.World);
                        var clip=Vector4.Transform(new Vector4(world[vertex],1),viewProjection);
                        if(clip.W<=1e-6f){projected[vertex]=null;continue;}
                        var ndc=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
                        projected[vertex]=(new Vector2((ndc.X+1)/2*size.X,(1-ndc.Y)/2*size.Y),ndc.Z);
                    }
                    for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                    {
                        var (a, b, c) = (world[mesh.Indices[i]], world[mesh.Indices[i + 1]], world[mesh.Indices[i + 2]]);
                        var normal = Vector3.Cross(b - a, c - a);
                        if (normal.LengthSquared() < 1e-12f) continue;
                        if (projected[mesh.Indices[i]] is not { } pa || projected[mesh.Indices[i + 1]] is not { } pb || projected[mesh.Indices[i + 2]] is not { } pc) continue;
                        var min = Vector2.Min(pa.Screen, Vector2.Min(pb.Screen, pc.Screen)) + origin;
                        var max = Vector2.Max(pa.Screen, Vector2.Max(pb.Screen, pc.Screen)) + origin;
                        itemBounds = itemBounds.Union(new(min.X, min.Y, max.X-min.X, max.Y-min.Y));
                        // Keep the same semantic bounds and complete GPU request. A CPU triangle wholly
                        // outside the canvas (plus its outline feather) cannot contribute visible pixels.
                        if (max.X < origin.X - 2 || max.Y < origin.Y - 2
                            || min.X > origin.X + size.X + 2 || min.Y > origin.Y + size.Y + 2) continue;
                        normal = Vector3.Normalize(normal);
                        var centre = (a + b + c) / 3;
                        if (Vector3.Dot(normal, eye - centre) <= 0) normal = -normal;   // two-sided: mirrored scales still show
                        var light = 0.35f + 0.65f * MathF.Max(0, Vector3.Dot(normal, Light));
                        if(paint) tris.Add((Vector3.DistanceSquared(eye, centre), pa.Screen, pb.Screen, pc.Screen,
                            Shade(colour,light,false),Shade(colour,light,true),itemIndex));
                    }
                }
                bounds.Add((item.Id,itemBounds));
            }
        }
        if(!reuse)
        {
            // Keep the original stable depth ordering, including equal-depth painter ties.
            tris=tris.OrderByDescending(t=>t.Depth).ToList();
            cpuGeometryRequest=RenderRequest;cpuGeometryCamera=Camera;cpuGeometryOrigin=origin;cpuGeometrySize=size;
            cpuGeometryPaint=paint;cpuGeometryTriangles=tris;cpuGeometryBounds=bounds;
        }
        if(cpuSelected.Length<bounds.Count)cpuSelected=new bool[bounds.Count];
        for(var i=0;i<bounds.Count;i++)
        {
            var item=bounds[i];cpuSelected[i]=selection.Contains(item.Id);
            Ui.RecordCanvasItem(treeCanvas,item.Id,item.Id,item.Bounds,cpuSelected[i]);
        }
        // No anti-aliased fill for solid surfaces: each triangle's feathered edge would overlap its neighbour's and draw
        // a seam down every quad's diagonal. The grid's lines keep theirs.
        var flags = draw.Flags;
        draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        if(paint)foreach(var t in tris)
        {
            draw.AddTriangleFilled(ToF(origin+t.A),ToF(origin+t.B),ToF(origin+t.C),cpuSelected[t.Item]?t.SelectedColour:t.Colour);
        }
        draw.Flags = flags;
        // One screen-space bound per selected object keeps dense meshes readable. Draw it after
        // both CPU surfaces and the borrowed GPU image, outside the projected geometry itself.
        var margin = 3 * HandleScale;
        for(var i=0;i<bounds.Count;i++)
        {
            var item=bounds[i];if(!cpuSelected[i]||item.Bounds.IsEmpty)continue;
            draw.AddRect(new(item.Bounds.X-margin,item.Bounds.Y-margin),
                new(item.Bounds.X+item.Bounds.Width+margin,item.Bounds.Y+item.Bounds.Height+margin),
                (EditorTheme.HighContrastActive?new ImColor8(255,255,0,255):new ImColor8(255,200,60,255)),0,ImDrawFlags.None,1.5f*HandleScale);
        }
    }

    private static uint PackColour(Vector4 linear)
    {
        static uint Channel(float x) => (uint)Math.Clamp((int)MathF.Round(255 * (x <= .0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1 / 2.4f) - .055f)), 0, 255);
        return Channel(linear.X) | Channel(linear.Y) << 8 | Channel(linear.Z) << 16 | 0xff000000;
    }

    private static ImColor8 Shade(uint colour, float light, bool selected)
    {
        float r = colour & 0xFF, g = (colour >> 8) & 0xFF, b = (colour >> 16) & 0xFF;
        if (selected) (r, g, b) = (r * 0.5f + 255 * 0.5f, g * 0.5f + 200 * 0.5f, b * 0.5f + 60 * 0.5f);   // a warm highlight
        return new ImColor8((byte)Math.Clamp(r * light, 0, 255), (byte)Math.Clamp(g * light, 0, 255), (byte)Math.Clamp(b * light, 0, 255), 255);
    }

    private static Vector2 ToV(float2 v) => new(v.X, v.Y);

    private static float2 ToF(Vector2 v) => new(v.X, v.Y);
}
