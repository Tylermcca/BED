using System.Numerics;
using Bed;
using Bed.Runtime;
namespace SceneStarter.Garden;

/// <summary>What a sprite does in the garden game. The rule is a naming and layer convention, documented in the
/// sprite sample's README: the player is <c>round_friend</c>, ids starting with <c>coin</c> are coins, ids containing
/// <c>flag</c> are goals, draw order 0 is the solid layer, and everything else is scenery.</summary>
public enum GardenRole { Scenery, Solid, Coin, Goal, Player }

/// <summary>An axis-aligned box in metres (y up).</summary>
public readonly record struct Box(float X0, float Y0, float X1, float Y1)
{
    public float Width=>X1-X0;
    public float Height=>Y1-Y0;
    public bool Overlaps(Box other,float margin=0)=>X0<other.X1-margin&&other.X0<X1-margin&&Y0<other.Y1-margin&&other.Y0<Y1-margin;
    public Box Offset(float x,float y)=>new(X0+x,Y0+y,X1+x,Y1+y);
    public Box Union(Box other)=>new(MathF.Min(X0,other.X0),MathF.Min(Y0,other.Y0),MathF.Max(X1,other.X1),MathF.Max(Y1,other.Y1));
}

/// <summary>One drawn sprite: its picture rectangle, the box it touches with, and its role.</summary>
public sealed record GardenSprite(string Id,string TextureId,Box Picture,Box Touch,int Order,int Ordinal,GardenRole Role);

/// <summary>The saved level read as a game: every sprite in drawing order plus the camera area. Built only from the
/// compiled level and its published pictures, so moving things in BED and pressing Play changes the game.</summary>
public sealed class GardenLevel
{
    public const string PlayerId="round_friend";
    public IReadOnlyList<GardenSprite> Sprites {get;}
    public IReadOnlyDictionary<string,BedTexture> Textures {get;}
    public Box Bounds {get;}
    public GardenSprite Player {get;}
    private GardenLevel(List<GardenSprite> sprites,IReadOnlyDictionary<string,BedTexture> textures)
    {
        Sprites=sprites;Textures=textures;
        Player=sprites.SingleOrDefault(s=>s.Role==GardenRole.Player)??throw new InvalidDataException("The level has no "+PlayerId+" sprite to play as.");
        Bounds=sprites.Select(s=>s.Picture).Aggregate((a,b)=>a.Union(b));
    }

    public static GardenRole RoleOf(string id,int order)
    {
        if(id==PlayerId)return GardenRole.Player;
        if(id.StartsWith("coin",StringComparison.OrdinalIgnoreCase))return GardenRole.Coin;
        if(id.Contains("flag",StringComparison.OrdinalIgnoreCase))return GardenRole.Goal;
        return order==0?GardenRole.Solid:GardenRole.Scenery;
    }

    /// <summary>True when the saved level is a flat sprite level with a player; false for 3D or player-less levels.</summary>
    public static bool CanPlay(SavedScene saved)=>saved.Level2 is {} v2&&v2.Scene.Space==SceneSpace.TwoDXY
        &&v2.Scene.Objects.Any(o=>o.Object.Id==PlayerId&&o.Sprite is not null);

    public static GardenLevel From(SavedScene saved)
    {
        var scene=(saved.Level2??throw new InvalidDataException("Build the saved sprite level first.")).Scene;
        if(scene.Space!=SceneSpace.TwoDXY)throw new InvalidDataException("The garden game plays flat (2D) sprite levels only.");
        return From(scene.Objects,saved.Textures);
    }

    public static GardenLevel From(IEnumerable<Scene2Object> objects,IReadOnlyDictionary<string,BedTexture> textures)
    {
        var all=objects.ToList();var byId=all.ToDictionary(o=>o.Object.Id,StringComparer.Ordinal);
        var world=new Dictionary<string,(Vector3 Position,Quaternion Rotation,Vector3 Scale)>(StringComparer.Ordinal);
        (Vector3 Position,Quaternion Rotation,Vector3 Scale) World(SceneObject item,int depth)
        {
            if(world.TryGetValue(item.Id,out var known))return known;
            if(depth>64)throw new InvalidDataException("The level's parents form a loop.");
            var result=(item.Position,item.Rotation,item.Scale);
            if(item.Parent is {} parentId&&byId.TryGetValue(parentId,out var parent))
            {
                var p=World(parent.Object,depth+1);
                result=(p.Position+Vector3.Transform(p.Scale*item.Position,p.Rotation),p.Rotation*item.Rotation,p.Scale*item.Scale);
            }
            world[item.Id]=result;return result;
        }
        var bounds=new Dictionary<string,(Box Tight,Box Any)>(StringComparer.Ordinal);
        var sprites=new List<GardenSprite>();
        for(var ordinal=0;ordinal<all.Count;ordinal++)
        {
            var item=all[ordinal];
            if(item.Sprite is not {} sprite||item.Object.Asset is not {} asset||!textures.TryGetValue(asset,out var texture))continue;
            var (position,_,scale)=World(item.Object,0);
            float w=sprite.Size.X*MathF.Abs(scale.X),h=sprite.Size.Y*MathF.Abs(scale.Y);
            float x0=position.X-sprite.Pivot.X*w,y0=position.Y-sprite.Pivot.Y*h;
            var picture=new Box(x0,y0,x0+w,y0+h);
            if(!bounds.TryGetValue(asset,out var fractions))bounds[asset]=fractions=(Opaque(texture,0.9f),Opaque(texture,0f));
            var role=RoleOf(item.Object.Id,sprite.Order);
            // Solid things are stood on at their filled part (not grass tips or rounded corners); the rest touch with
            // everything visible in their picture.
            var f=role==GardenRole.Solid?fractions.Tight:fractions.Any;
            var touch=new Box(x0+f.X0*w,y0+f.Y0*h,x0+f.X1*w,y0+f.Y1*h);
            sprites.Add(new(item.Object.Id,asset,picture,touch,sprite.Order,ordinal,role));
        }
        if(sprites.Count==0)throw new InvalidDataException("The level has no sprites.");
        sprites.Sort((a,b)=>a.Order!=b.Order?a.Order.CompareTo(b.Order):a.Ordinal.CompareTo(b.Ordinal));
        return new(sprites,textures);
    }

    /// <summary>The part of a picture that is filled, as fractions of its size (y up). With <paramref name="fill"/> 0 it is
    /// every mostly-opaque pixel; with 0.9 only rows and columns at least 90% filled count, which skips grass
    /// tufts and rounded corners.</summary>
    public static Box Opaque(BedTexture texture,float fill)
    {
        int w=texture.Width,h=texture.Height;var rows=new int[h];var columns=new int[w];int minX=w,minY=h,maxX=-1,maxY=-1;
        for(var y=0;y<h;y++)for(var x=0;x<w;x++)
            if(texture.Rgba[(y*w+x)*4+3]>=128){rows[y]++;columns[x]++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
        if(maxX<0)return new(0,0,1,1);
        if(fill>0)
        {
            float needRow=fill*(maxX-minX+1),needColumn=fill*(maxY-minY+1);
            int top=Array.FindIndex(rows,r=>r>=needRow),bottom=Array.FindLastIndex(rows,r=>r>=needRow);
            int left=Array.FindIndex(columns,c=>c>=needColumn),right=Array.FindLastIndex(columns,c=>c>=needColumn);
            if(top>=0&&left>=0){minY=top;maxY=bottom;minX=left;maxX=right;}
        }
        return new((float)minX/w,1-(float)(maxY+1)/h,(float)(maxX+1)/w,1-(float)minY/h);
    }
}
