using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Brutal.Authoring.Editor;

// Scene and its object records are immutable. Identity is therefore a revision key, including
// transient gesture previews and old documents restored by Undo. Weak keys never retain a closed scene.
internal sealed class SceneViewIndex
{
    private static readonly ConditionalWeakTable<Scene, SceneViewIndex> revisions = new();
    private readonly Dictionary<string, SceneObject> objects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Matrix4x4> worlds = new(StringComparer.Ordinal);

    private SceneViewIndex(Scene scene)
    {
        // Repair documents may contain duplicate IDs: Scene.Find has always returned the first.
        foreach (var item in scene.Objects) objects.TryAdd(item.Id, item);
    }

    internal static SceneViewIndex For(Scene scene) => revisions.GetValue(scene, static key => new(key));
    internal SceneObject? Find(string? id) => id is not null && objects.TryGetValue(id, out var item) ? item : null;

    internal Matrix4x4 World(string id) => Find(id) is null ? Matrix4x4.Identity : worlds.GetOrAdd(id, ComputeWorld);

    private Matrix4x4 ComputeWorld(string id)
    {
        var result = Matrix4x4.Identity;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Preserve the old multiplication order exactly, including truncated missing/cyclic parents.
        // Reassociating a cached parent matrix would change floating-point results.
        for (var item = Find(id); item is not null && seen.Add(item.Id); item = Find(item.Parent))
            result *= Matrix4x4.CreateScale(item.Scale) * Matrix4x4.CreateFromQuaternion(item.Rotation)
                * Matrix4x4.CreateTranslation(item.Position);
        return result;
    }
}
