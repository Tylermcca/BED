using System.Numerics;
namespace Bed;

// Called only after the existing graph checks. Does not read assets or produce native bodies.
internal static class Scene2BodyTransforms
{
    internal static void Check(IReadOnlyDictionary<string,SceneObject> objects,IReadOnlySet<string> bodies,Func<string,string,string> at)
    {
        var worlds=new Dictionary<string,(Matrix4x4 World,bool BodyInChain)>(StringComparer.Ordinal);
        foreach(var id in bodies)
        {
            var item=objects[id];
            if(!Scene2Numeric.Unit(item.Rotation))throw Scene2Read.Bad(at(id,"/rotation"),"a body requires a finite unit rotation");
            if(item.Scale.X<=0||item.Scale.Y<=0||item.Scale.Z<=0)throw Scene2Read.Bad(at(id,"/scale"),"a body cannot use signed or zero scale");
            var chain=new Stack<SceneObject>();string? current=id;
            while(current is not null&&!worlds.ContainsKey(current)){chain.Push(objects[current]);current=objects[current].Parent;}
            var world=current is null?Matrix4x4.Identity:worlds[current].World;var hasBody=current is not null&&worlds[current].BodyInChain;
            while(chain.TryPop(out var node))
            {
                if(bodies.Contains(node.Id)&&hasBody)throw Scene2Read.Bad(at(node.Id,"/body"),"a body cannot have another body as an ancestor");
                world=Matrix4x4.CreateScale(node.Scale)*Matrix4x4.CreateFromQuaternion(node.Rotation)*Matrix4x4.CreateTranslation(node.Position)*world;
                hasBody|=bodies.Contains(node.Id);worlds.Add(node.Id,(world,hasBody));
            }
            if(!Matrix4x4.Decompose(world,out var scale,out var rotation,out var position)||!Scene2Numeric.Finite(position)||!Scene2Numeric.Finite(scale)||!Scene2Numeric.Unit(rotation)||scale.X<=0||scale.Y<=0||scale.Z<=0)
                throw Scene2Read.Bad(at(id,"/body"),"body world pose must have finite position, unit rotation and positive scale");
            static bool Same(float a,float b)=>MathF.Abs(a-b)<=Scene2Numeric.TransformTolerance*MathF.Max(MathF.Abs(a),MathF.Abs(b));
            if(!Same(scale.X,scale.Y)||!Same(scale.X,scale.Z))throw Scene2Read.Bad(at(id,"/scale"),"body world scale must be uniform");
            var rebuilt=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(position);
            // Normalize the basis so tiny positive scales cannot hide shear under an absolute tolerance floor.
            var normalized=Basis(world,scale.X);var represented=Basis(rebuilt,scale.X);
            if(!Scene2Numeric.Close(normalized,represented)||!Scene2Numeric.Close(world,rebuilt))throw Scene2Read.Bad(at(id,"/body"),"body world pose cannot contain shear or reflection");
        }
    }
    private static Matrix4x4 Basis(Matrix4x4 value,float scale)
    {
        value.M11/=scale;value.M12/=scale;value.M13/=scale;value.M21/=scale;value.M22/=scale;value.M23/=scale;value.M31/=scale;value.M32/=scale;value.M33/=scale;
        value.M41=value.M42=value.M43=0;return value;
    }
}
