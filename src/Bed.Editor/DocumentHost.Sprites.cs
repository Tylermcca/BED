using System.Numerics;
using System.Text.Json;
namespace Bed.Editor;

/// <summary>Opaque delayed sprite placement, bound to the exact scene authority, revision and admitted image/default capture.</summary>
public sealed class SceneSpritePlacement
{
    internal SceneSpritePlacement(DocumentHost owner,string name,Scene2DocumentEntry entry,AssetCatalog.Selection selection,SceneSprite sprite,string? parent)
    {Owner=owner;DocumentName=name;Entry=entry;Revision=entry.Session.Revision;Selection=selection;Sprite=sprite;Parent=parent;}
    internal DocumentHost Owner {get;}
    internal Scene2DocumentEntry Entry {get;}
    internal long Revision {get;}
    internal AssetCatalog.Selection Selection {get;}
    internal SceneSprite Sprite {get;}
    internal string? Parent {get;}
    /// <summary>The admitted target file name, for active-view routing only.</summary>
    public string DocumentName {get;}
}

public sealed partial class DocumentHost
{
    /// <summary>Captures a currently open planar scene and an exact admitted PNG selection. Copies defaults once; performs no scene edit, import or save.</summary>
    public bool CaptureSpritePlacement(string name,AssetCatalog.Selection selection,out SceneSpritePlacement? placement,out string why,string? parent=null)
    {
        placement=null;
        if(assets is null){why="No admitted asset catalog is connected.";return false;}
        if(!scene2.TryGetValue(name,out var entry)){why="Open the admitted scene-v2 file first.";return false;}
        if(Scene2Refusal(name,entry) is {} refusal){why=refusal;return false;}
        if(entry.Session.Current.Space!=SceneSpace.TwoDXY){why="Sprites require a planar XY scene; mixed 2D/3D rendering is unsupported.";return false;}
        if(entry.Session.CheckDisk()!=DiskState.Unchanged){why="The saved scene changed; resolve its disk conflict.";return false;}
        if(SpriteFocusRefusal(entry,parent) is {} focus){why=focus;return false;}
        try
        {
            if(!SpriteAssetCapture.Resolve(assets,selection,out var lease,out var input,out why))return false;
            if(!SpriteAssetDefaults.TryRead(input!.ProducingRecord.Settings,out var defaults,out why))return false;
            var sprite=defaults!.CreateSprite(lease!.Width!.Value,lease.Height!.Value);
            placement=new(this,name,entry,selection,sprite,parent);why="";return true;
        }
        catch(Exception e)when(Scene2Source.Expected(e)||e is FormatException or OverflowException){why="Sprite placement refused: "+e.Message;return false;}
    }
    /// <summary>Revalidates capture and commits one checked add-sprite command/undo step at a local XY position (root coordinates when unparented).
    /// Stale scene, image, policy or owner refuses without changing history. This never saves or changes image metadata.</summary>
    public bool PlaceSprite(SceneSpritePlacement placement,string id,Vector2 position,out string why)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if(!ReferenceEquals(placement.Owner,this)||!scene2.TryGetValue(placement.DocumentName,out var entry)||!ReferenceEquals(entry,placement.Entry)||entry.Session.Revision!=placement.Revision)
        {why="The sprite target changed; start placement again.";return false;}
        if(Scene2Refusal(placement.DocumentName,entry) is {} refusal){why=refusal;return false;}
        if(SpriteFocusRefusal(entry,placement.Parent) is {} focus){why=focus;return false;}
        if(assets is null){why="No admitted image catalog is connected.";return false;}
        if(!assets.TryResolve(placement.Selection,out var lease,out why))return false;
        try
        {
            var s=placement.Sprite;
            var item=new Dictionary<string,object?>{{"id",id},{"asset",placement.Selection.AssetGuid},{"position",new[]{position.X,position.Y,0}},{"sprite",new{size=new[]{s.Size.X,s.Size.Y},pivot=new[]{s.Pivot.X,s.Pivot.Y},order=s.Order,filter=s.Filter==SceneSpriteFilter.Nearest?"nearest":"linear"}}};
            if(placement.Parent is not null)item.Add("parent",placement.Parent);
            var args=JsonSerializer.SerializeToElement(new{@object=item});var command=Scene2Kit.Command("add_sprite",args);
            var policy=entry.Editor.EditRefusal;
            if(policy?.Invoke(entry.Session.Current,[command],placement.Parent is null?[]:[placement.Parent]) is {} denied){why=denied;return false;}
            if(!scene2.TryGetValue(placement.DocumentName,out var current)||!ReferenceEquals(current,entry)||entry.Session.Revision!=placement.Revision||!ReferenceEquals(policy,entry.Editor.EditRefusal))
            {why="The scene or edit policy changed during placement checks; start again.";return false;}
            if(SpriteFocusRefusal(entry,placement.Parent) is {} changedFocus){why=changedFocus;return false;}
            if(!CaptureScene2Edit(placement.DocumentName,"add_sprite",args,out var plan,out why))return false;
            if(!assets.TryResolve(placement.Selection,out lease,out why))return false;
            if(!CommitScene2Edit(placement.DocumentName,plan!,out why))return false;
            entry.Editor.Select(id);GC.KeepAlive(lease);why=$"Placed {id}.";return true;
        }
        catch(Exception e)when(Scene2Source.Expected(e)||e is FormatException or OverflowException){why="Sprite placement refused: "+e.Message;return false;}
    }
    private static string? SpriteFocusRefusal(Scene2DocumentEntry entry,string? parent)
    {
        var objects=Scene2Resolver.Resolve(entry.Session.Current).Objects.ToDictionary(o=>o.Object.Id,o=>o.Object,StringComparer.Ordinal);
        if(parent is not null&&!objects.ContainsKey(parent))return "The selected sprite parent no longer exists.";
        if(entry.Editor.FocusedOn is not {} focused)return null;
        for(var at=parent;at is not null;at=objects[at].Parent)if(at==focused)return null;
        return "Step out of focus before placing outside the focused group.";
    }
}
