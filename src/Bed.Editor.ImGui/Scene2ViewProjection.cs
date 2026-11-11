using System.Runtime.CompilerServices;
namespace Bed.Editor.ImGui;

// A weakly held read-only projection of one immutable authoring value. Never a session or write target.
internal sealed class Scene2ViewProjection
{
    private static readonly ConditionalWeakTable<Scene2,Scene2ViewProjection> values=new();
    internal static Scene2ViewProjection For(Scene2 scene)=>values.GetValue(scene,s=>new(s));
    internal readonly Scene2Resolution Resolved;
    internal readonly Scene Flat;
    internal readonly IReadOnlyDictionary<string,Scene2Object> ById;
    internal readonly IReadOnlyDictionary<string,PrefabInstance> Instances;
    private Scene2ViewProjection(Scene2 scene)
    {Resolved=Scene2Resolver.Resolve(scene);Flat=new([..Resolved.Objects.Select(o=>o.Object)]);ById=Resolved.Objects.ToDictionary(o=>o.Object.Id,StringComparer.Ordinal);Instances=scene.Instances.ToDictionary(i=>i.Key,StringComparer.Ordinal);}
}
