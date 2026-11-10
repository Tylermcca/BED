using System.Numerics;

namespace Brutal.Authoring.Editor;

/// <summary>All host identities and coordinate inputs that can invalidate delayed scene work. Replacements increment their generation.</summary>
public sealed record SceneRenderIdentity(long DocumentRevision, long CameraGeneration, long SourceGeneration,
    long ViewGeneration, long TargetGeneration, Vector2 Origin, Vector2 Extent, Vector2 FramebufferScale);

/// <summary>One owned immutable copy of a CPU viewport item. No mutable mesh array or native resource escapes.</summary>
public sealed class SceneRenderItem
{
    internal SceneRenderItem(ViewportItem item)
    {
        Id = item.Id; World = item.World; Colour = item.Colour;
        MeshPayload Capture(MeshData mesh)
        {
            if (!Enum.IsDefined(mesh.Uv0Origin)) throw new ArgumentException("Unknown mesh UV origin.");
            var uv = mesh.Uv0 ?? [];
            return new(mesh.Positions, mesh.Normals ?? [],
                mesh.Uv0Origin == MeshUv0Origin.ObjBottomLeft ? uv.Select(p => new Vector2(p.X, 1f - p.Y)) : uv,
                mesh.Indices, mesh.Material ?? DisplayMaterial(item.Colour));
        }
        Payload = Capture(item.Mesh);
        if (item.Mesh.Parts is { } parts)
        {
            if (parts.Count == 0 || parts.Any(p => p is null || p.Parts is not null))
                throw new ArgumentException("Mesh parts must be a nonempty, non-nested list.");
            var captured = parts.Select(Capture).ToArray();
            var positions = captured.SelectMany(p => p.Positions);
            var offset = 0; var indices = new List<int>();
            foreach (var part in captured) { indices.AddRange(part.Indices.Select(i => checked(i + offset))); offset = checked(offset + part.Positions.Count); }
            if (!Payload.Positions.SequenceEqual(positions) || !Payload.Indices.SequenceEqual(indices))
                throw new ArgumentException("Combined mesh geometry must match its ordered parts.");
            Payloads = Array.AsReadOnly(captured);
        }
        else Payloads = Array.AsReadOnly(new[] { Payload });
        if (string.IsNullOrWhiteSpace(Id) || !Finite(World))
            throw new ArgumentException("A scene item requires a stable ID and finite transform.");
    }
    /// <summary>CPU-authoritative stable object ID.</summary>
    public string Id { get; }
    /// <summary>The captured world transform, unchanged by either consumer.</summary>
    public Matrix4x4 World { get; }
    /// <summary>The original packed display color, retained for existing CPU and triangle consumers.</summary>
    public uint Colour { get; }
    /// <summary>Combined immutable geometry for compatibility and CPU picking. Material consumers use Payloads for every part.</summary>
    public MeshPayload Payload { get; }
    /// <summary>All immutable ordered material parts. A single-part mesh contains exactly the existing Payload.</summary>
    public IReadOnlyList<MeshPayload> Payloads { get; }
    /// <summary>Owned model-space positions in source order, compatible with existing consumers.</summary>
    public IReadOnlyList<Vector3> Positions => Payload.Positions;
    /// <summary>Owned triangle indices in source order, compatible with existing consumers.</summary>
    public IReadOnlyList<int> Indices => Payload.Indices;
    private static MeshData Copy(MeshPayload payload) => new(payload.Positions.ToArray(), payload.Indices.ToArray())
    { Normals = payload.Normals.ToArray(), Uv0 = payload.Uv0.ToArray(), Uv0Origin = MeshUv0Origin.TopLeft, Material = payload.Material };
    internal ViewportItem MutableCopy() => new(Id, World, Copy(Payload) with
    { Parts = ReferenceEquals(Payloads[0], Payload) ? null : Array.AsReadOnly(Payloads.Select(Copy).ToArray()) }, Colour);
    private static SceneMaterial DisplayMaterial(uint colour)
    {
        static float Linear(uint channel)
        {
            var s = channel / 255f;
            return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
        return new(new(Linear(colour & 255), Linear((colour >> 8) & 255), Linear((colour >> 16) & 255), 1));
    }
    private static bool Finite(Matrix4x4 m) => new[] { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 }.All(float.IsFinite);
}

/// <summary>One captured triangle for the Vulkan consumer, retaining CPU object identity and source ordering.
/// Clip Y is inverted for the positive-height Vulkan viewport; depth remains the CPU camera's zero-to-one convention.</summary>
public sealed record SceneRenderTriangle(string ItemId, Vector4 A, Vector4 B, Vector4 C, uint Colour);

/// <summary>A CPU-authoritative hit or miss tied to the exact captured scene identities.</summary>
public sealed class ScenePickResult
{
    internal ScenePickResult(SceneRenderRequest request, string? id, float? distance) { Request = request; ItemId = id; Distance = distance; }
    /// <summary>The immutable scene used for the CPU ray test.</summary>
    public SceneRenderRequest Request { get; }
    /// <summary>Nearest object, or null for a miss.</summary>
    public string? ItemId { get; }
    /// <summary>Ray distance for a hit.</summary>
    public float? Distance { get; }
}

/// <summary>One immutable CPU-produced frame shared by scene rendering and picking. It is not asset admission or a GPU cache.</summary>
public sealed class SceneRenderRequest
{
    private static long nextViewGeneration;
    /// <summary>Allocates a process-unique view lifetime across document types; replacing a view never reuses an old epoch.</summary>
    public static long NewViewGeneration() => Interlocked.Increment(ref nextViewGeneration);
    private SceneRenderRequest(SceneRenderIdentity identity, Camera camera, SceneRenderItem[] items)
    { Identity = identity; Camera = camera; Items = Array.AsReadOnly(items); }
    /// <summary>The complete document/view/source/target and coordinate identity.</summary>
    public SceneRenderIdentity Identity { get; }
    /// <summary>The immutable captured CPU camera.</summary>
    public Camera Camera { get; }
    /// <summary>Captured items in CPU order, with unique stable IDs.</summary>
    public IReadOnlyList<SceneRenderItem> Items { get; }

    /// <summary>Copies source geometry once at frame capture. The caller increments source generation after an asset/source replacement.</summary>
    public static SceneRenderRequest Capture(SceneRenderIdentity identity, Camera camera, IEnumerable<ViewportItem> items)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(camera); ArgumentNullException.ThrowIfNull(items);
        static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
        if (!Finite(identity.Origin) || !Finite(identity.Extent) || !Finite(identity.FramebufferScale)
            || identity.Extent.X <= 0 || identity.Extent.Y <= 0 || identity.FramebufferScale.X <= 0 || identity.FramebufferScale.Y <= 0
            || !float.IsFinite(camera.Distance) || camera.Distance <= 0 || !float.IsFinite(camera.Yaw) || !float.IsFinite(camera.Pitch)
            || !float.IsFinite(camera.FieldOfView) || camera.FieldOfView <= 0 || camera.FieldOfView >= MathF.PI
            || !float.IsFinite(camera.Target.X) || !float.IsFinite(camera.Target.Y) || !float.IsFinite(camera.Target.Z))
            throw new ArgumentException("A scene frame needs finite coordinates, a valid camera and positive extents/scales.");
        var captured = items.Select(i => new SceneRenderItem(i)).ToArray();
        if (captured.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != captured.Length)
            throw new ArgumentException("Scene item IDs must be unique.");
        return new(identity, camera, captured);
    }

    /// <summary>Fresh mutable copies for the existing CPU draw/pick contracts; modifying them never changes the captured request.</summary>
    public IReadOnlyList<ViewportItem> CopyItems() => Array.AsReadOnly(Items.Select(i => i.MutableCopy()).ToArray());

    /// <summary>Produces GPU clip-space triangles only from captured IDs, transforms, geometry and camera, in CPU item/index order.</summary>
    public IReadOnlyList<SceneRenderTriangle> Triangles()
    {
        var result = new List<SceneRenderTriangle>();
        var viewProjection = Camera.View * Camera.Projection(Identity.Extent);
        foreach (var item in Items)
        {
            var matrix = item.World * viewProjection;
            Vector4 Clip(int index)
            { var p = Vector4.Transform(new Vector4(item.Positions[index], 1), matrix); return new(p.X, -p.Y, p.Z, p.W); }
            for (var i = 0; i < item.Indices.Count; i += 3)
                result.Add(new(item.Id, Clip(item.Indices[i]), Clip(item.Indices[i+1]), Clip(item.Indices[i+2]), item.Colour));
        }
        return result.AsReadOnly();
    }

    /// <summary>Tests a desktop logical coordinate through the same captured CPU camera and geometry.</summary>
    public ScenePickResult Pick(Vector2 screen)
    {
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y)) throw new ArgumentException("Pick coordinates must be finite.");
        var hit = Picking.Pick(CopyItems(), Camera.RayAt(screen - Identity.Origin, Identity.Extent));
        return new(this, hit?.Id, hit?.Distance);
    }

    /// <summary>Whether a delayed frame still has every current identity, coordinate and camera input.</summary>
    public bool IsCurrent(SceneRenderRequest current) => Identity == current.Identity && Camera == current.Camera;

    /// <summary>Applies only a current CPU result through existing selection methods. Stale misses leave newer selection untouched.
    /// The caller supplies the current request; focus and Play are checked again immediately before selection.</summary>
    public bool TrySelect<TDoc>(EditorModel<TDoc> editor, ScenePickResult result, bool toggle = false) where TDoc : class
    {
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(result);
        if (!result.Request.IsCurrent(this) || editor.Session.Revision != Identity.DocumentRevision || editor.Playing is not null) return false;
        if (result.ItemId is { } id)
        {
            if (!Items.Any(i => i.Id == id) || editor.Outline.Label(editor.Session.Current, id) is null || !editor.InFocus(id)) return false;
            if (toggle) editor.Toggle(id); else editor.Select(id);
        }
        else if (!toggle) editor.Select();
        return true;
    }
}
