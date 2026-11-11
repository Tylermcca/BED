using System.Numerics;
using System.Text.Json;
using Bed.Editor;
namespace Bed.Editor.ImGui;

public sealed partial class DataWorkspace
{
    // Same root/parent anchor as v1. Planar native delivery supplies its actual XY cursor separately.
    internal (Vector3 Position,string? Parent) Scene2AssetAnchor()
    {
        if(Scene2Window is not {} window||window.Editor.Session.Current.Space!=SceneSpace.ThreeD||Active is not {} file||!scene2Windows.TryGetValue(file.Name,out var view))return(Vector3.Zero,null);
        return window.Editor.Primary is {} parent?(Vector3.Zero,parent):(view.Viewport.Camera.Target,null);
    }
    internal bool CaptureScene2AssetDrop(AssetCatalog.Selection selection,Vector3 at,out Func<Vector3,bool>? commit,string? parent=null)
    {
        commit=null;
        if(Scene2Window is not {} window||Active is not {} file||!scene2Windows.TryGetValue(file.Name,out var view))return false;
        if(view.Guard() is {} refusal){Said=refusal;return false;}
        if(window.ProjectPlaying?.Invoke()==true){Said="Stop Play before placing an asset.";return false;}
        if(view.Entry.Session.Current.Space==SceneSpace.TwoDXY)return CaptureFlatSpriteDrop(view,selection,parent,out commit);
        if(sceneAssets is not {} catalog){Said="No assets are available for this project.";return false;}
        bool Current()=>!scene2WorkspaceClosed&&Scene2OwnerCurrent?.Invoke(this)!=false&&ReferenceEquals(view.Owner,this)&&ReferenceEquals(Scene2Window,window)&&ReferenceEquals(sceneAssets,catalog);
        string? ParentRefusal()
        {
            if(parent is not null&&!Scene2ViewProjection.For(view.Entry.Session.Current).ById.ContainsKey(parent))return "The selected model parent no longer exists.";
            if((parent is null&&window.Editor.FocusedOn is not null)||(parent is not null&&!window.Editor.InFocus(parent)))return "Step out of focus before placing outside the focused group.";
            return null;
        }
        if(!Current()){Said="The model target admission changed; start dragging again.";return false;}
        if(ParentRefusal() is {} focus){Said=focus;return false;}
        try
        {
            // Decode only admitted pinned model bytes. Paths, image metadata and opaque non-model bytes grant nothing.
            if(!catalog.TryResolve(selection,out var initial,out var initialWhy)){Said=initialWhy;return false;}
            if(initial!.Texture is not null){Said="Images require a flat sprite level; choose an available model.";return false;}
            var model=new ModelPreview(catalog);model.Select(selection);
            if(!model.TryParts(out _,out var why)||!catalog.TryResolve(selection,out var lease,out why)){Said=why;return false;}
            var ids=Scene2ViewProjection.For(view.Entry.Session.Current).ById;var number=1;while(ids.ContainsKey("object_"+number))number++;var id="object_"+number;
            var position=parent is null?at:Vector3.Zero;
            var item=new Dictionary<string,object?>{{"id",id},{"asset",selection.AssetGuid},{"position",new[]{position.X,position.Y,position.Z}}};if(parent is not null)item.Add("parent",parent);
            var args=JsonSerializer.SerializeToElement(new{@object=item});var command=Scene2Kit.Command("object.add",args);
            if(!Documents.CaptureScene2Edit(file.Name,"object.add",args,out var plan,out why)){Said=why;return false;}
            var policy=window.Editor.EditRefusal;
            commit=delivered=>
            {
                try
                {
                    if(!Current()||view.Entry.Session.Revision!=plan!.ExpectedRevision||parent is null&&delivered!=position){Said="The model target changed; start dragging again.";return false;}
                    if(view.Guard() is {} blocked){Said=blocked;return false;}
                    if(window.ProjectPlaying?.Invoke()==true){Said="Stop Play before placing a model.";return false;}
                    if(ParentRefusal() is {} parentWhy){Said=parentWhy;return false;}
                    if(!ReferenceEquals(policy,window.Editor.EditRefusal)){Said="The scene edit policy changed; start dragging again.";return false;}
                    if(policy?.Invoke(window.Editor.Session.Current,[command],parent is null?[]:[parent]) is {} denied){Said=denied;return false;}
                    if(!Current()||view.Entry.Session.Revision!=plan.ExpectedRevision||!ReferenceEquals(policy,window.Editor.EditRefusal)){Said="The scene admission or edit policy changed during placement checks.";return false;}
                    if(ParentRefusal() is {} changedFocus){Said=changedFocus;return false;}
                    if(view.Guard() is {} changedGuard){Said=changedGuard;return false;}
                    if(window.ProjectPlaying?.Invoke()==true){Said="Stop Play before placing a model.";return false;}
                    if(!catalog.TryResolve(selection,out var current,out var reason)){Said=reason;return false;}
                    var ok=Documents.CommitScene2Edit(file.Name,plan,out reason);Said=ok?$"Placed {id}.":reason;
                    if(ok)window.Editor.Select(id);GC.KeepAlive(lease);GC.KeepAlive(current);return ok;
                }
                catch(Exception error)when(error is InvalidOperationException or ArgumentException or JsonException or IOException or OverflowException)
                {Said="Model placement refused: "+error.Message;return false;}
            };
            return true;
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or JsonException or IOException or OverflowException)
        {Said="Model placement refused: "+error.Message;return false;}
    }
    private bool CaptureFlatSpriteDrop(NativeScene2 view,AssetCatalog.Selection selection,string? parent,out Func<Vector3,bool>? commit)
    {
        commit=null;var window=view.Window;var name=view.Name;
        if(!Documents.CaptureSpritePlacement(name,selection,out var captured,out var why,parent)){Said=why;return false;}
        commit=position=>
        {
            if(position.Z!=0||!float.IsFinite(position.X)||!float.IsFinite(position.Y)){Said="A sprite has to go on the flat level (Z at 0).";return false;}
            if(!ReferenceEquals(Scene2Window,window)){Said="The sprite target changed; start dragging again.";return false;}
            if(view.Guard() is {} blocked){Said=blocked;return false;}
            var ids=Scene2ViewProjection.For(view.Entry.Session.Current).ById;var number=1;while(ids.ContainsKey("object_"+number))number++;
            var ok=Documents.PlaceSprite(captured!,"object_"+number,new(position.X,position.Y),out var message);Said=message;return ok;
        };
        return true;
    }
}
