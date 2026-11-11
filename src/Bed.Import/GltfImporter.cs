using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace Bed.Import;

/// <summary>How an import went (worksheet AT-022): every outcome is one of these, and a refused file is never shown as a success.</summary>
public enum ImportOutcome
{
    /// <summary>Imported with nothing to report.</summary>
    Imports,
    /// <summary>Imported, with warnings worth reading.</summary>
    ImportsWithWarnings,
    /// <summary>Refused: nothing usable came out, and nothing was written.</summary>
    Refused,
}

/// <summary>One mesh as placed in the scene, baked to world space and converted to the project frame.</summary>
/// <param name="Node">The node that places it.</param>
/// <param name="Mesh">The mesh's name (several nodes can share one mesh).</param>
/// <param name="Positions">Vertex positions.</param>
/// <param name="Normals">Vertex normals, unit length, or empty when the file has none.</param>
/// <param name="Uv0">The first UV set, or empty.</param>
/// <param name="Triangles">Vertex indices, three per triangle, wound counter-clockwise seen from outside in the project frame.</param>
/// <param name="Material">The material's name, if any.</param>
/// <param name="WorldDeterminant">The node's world transform determinant (negative: mirrored).</param>
/// <param name="WindingFlipped">True when the winding was flipped once to stay outward (combined node and frame mirroring).</param>
/// <param name="Skinned">True for a skinned mesh: shown in its bind pose, not animated.</param>
public sealed record ImportedMesh(string Node, string Mesh, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, int[] Triangles,
    string? Material, float WorldDeterminant, bool WindingFlipped, bool Skinned)
{
    /// <summary>Source material identity and linear base colour; texture paths are source references, never asset identities.</summary>
    public ImportedMaterial? MaterialInfo { get; init; }
    /// <summary>The axis-aligned bounds of <see cref="Positions"/>.</summary>
    public (Vector3 Min, Vector3 Max) Bounds => (
        Positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min),
        Positions.Aggregate(new Vector3(float.MinValue), Vector3.Max));
}

/// <summary>A point of interest from the file (an empty node), such as a socket or a dock, with optional directions from its extras.</summary>
public sealed record ImportedMarker(string Node, Vector3 Position, Vector3? Up, Vector3? Outward);

/// <summary>A texture a material uses, by role, with its colour space (sRGB for colour, linear for data such as normals).</summary>
/// <param name="Material">The material using it.</param>
/// <param name="Role">The glTF channel (BaseColor, Normal, â€¦).</param>
/// <param name="ColourSpace"><c>sRGB</c> or <c>linear</c>.</param>
/// <param name="Source">The relative file it comes from, or <c>embedded</c>.</param>
/// <param name="Missing">True when the file couldn't be found (the material falls back; a warning says which).</param>
public sealed record ImportedTexture(string Material, string Role, string ColourSpace, string Source, bool Missing);

/// <summary>Everything an import found.</summary>
public sealed record ImportedAsset(IReadOnlyList<ImportedMesh> Meshes, IReadOnlyList<ImportedMarker> Markers, IReadOnlyList<ImportedTexture> Textures,
    IReadOnlyList<string> Materials, int Skins, int Joints, IReadOnlyList<(string Name, float Seconds)> Clips, int LogicalMeshes);

/// <summary>One actual external file read by this importer, relative to the source folder; no source payload escapes.</summary>
/// <param name="RelativePath">Normalized relative local file path returned by the existing guarded reader.</param>
/// <param name="SourceHash">Lowercase SHA-256 of the exact bytes supplied to the parser.</param>
public sealed record ImportedSourceRead(string RelativePath,string SourceHash);

/// <summary>An import's outcome, what it found, and every diagnostic.</summary>
public sealed record ImportResult(ImportOutcome Outcome, ImportedAsset? Asset, IReadOnlyList<Issue> Issues)
{
    /// <summary>Actual external reads, deduplicated by path/hash and immutable at return. The primary source is already pinned separately.</summary>
    public IReadOnlyList<ImportedSourceRead> ExternalReads {get;init;}=[];
    /// <summary>Diagnostics as <c>severity: message</c>, the form the fixtures' expected results use.</summary>
    public IEnumerable<string> Diagnostics => Issues.Select(i => $"{i.Severity.ToString().ToLowerInvariant()}: {i.Message}");
}

/// <summary>
/// Reads glTF 2.0 (<c>.glb</c>, <c>.gltf</c>) into an <see cref="ImportedAsset"/> in a project's frame (AT-020..023):
/// <list type="bullet">
///   <item>node transforms are baked, with inverse-transpose normals, and winding is corrected once for the combined determinant;</item>
///   <item>a singular (zero-scale) node refuses the import;</item>
///   <item>skins and clips are reported, and skinned meshes shown in their bind pose;</item>
///   <item>a missing optional texture warns, and the material falls back;</item>
///   <item>zero-area triangles are removed with a count;</item>
///   <item>truncated containers, unsupported required extensions, out-of-range indices and other formats are refused, naming the reason.</item>
/// </list>
/// Relative resources must stay inside the file's folder (AT-102). The importer never writes anything.
/// </summary>
public static class GltfImporter
{
    /// <summary>The importer's version, recorded in asset records; a newer importer re-imports.</summary>
    public const int Version = 1;

    private static readonly string[] Supported = [];   // required extensions this importer understands (none yet)
    private const float Degenerate = 1e-12f;

    /// <summary>Imports <paramref name="path"/> into <paramref name="frame"/>.</summary>
    public static ImportResult Import(string path, ProjectFrame frame)
    {
        var reads=new Dictionary<string,ImportedSourceRead>(StringComparer.Ordinal);
        var result=ImportCore(path,frame,reads);
        return result with{ExternalReads=Array.AsReadOnly(reads.Values.OrderBy(r=>r.RelativePath,StringComparer.Ordinal).ToArray())};
    }
    private static ImportResult ImportCore(string path,ProjectFrame frame,Dictionary<string,ImportedSourceRead> reads)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".fbx") return Refuse($"{name}: FBX is not supported; export glTF 2.0");
        if (ext is not (".glb" or ".gltf")) return Refuse($"{name}: '{ext}' is not supported; export glTF 2.0 (.glb or .gltf)");

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Refuse($"{name}: can't read it: {e.Message}"); }

        JsonElement json;
        if (ext == ".glb")
        {
            if (bytes.Length < 20 || Encoding.ASCII.GetString(bytes, 0, 4) != "glTF") return Refuse($"{name}: not a GLB container");
            var declared = BitConverter.ToUInt32(bytes, 8);
            if (declared > bytes.Length) return Refuse($"{name}: declared GLB length exceeds available bytes");
            long jsonLength = BitConverter.ToUInt32(bytes, 12);
            if (20 + jsonLength > bytes.Length) return Refuse($"{name}: its JSON chunk length ({jsonLength} bytes) is more than the file holds");
            try { json = JsonDocument.Parse(bytes.AsMemory(20, (int)jsonLength)).RootElement.Clone(); }
            catch (JsonException e) { return Refuse($"{name}: not valid glTF JSON: {e.Message}"); }
        }
        else
        {
            try { json = JsonDocument.Parse(bytes).RootElement.Clone(); }
            catch (JsonException e) { return Refuse($"{name}: not valid glTF JSON: {e.Message}"); }
        }

        if (json.ValueKind != JsonValueKind.Object) return Refuse($"{name}: the glTF root must be a JSON object");
        if (CheckLayout(json, bytes, ext == ".glb", name) is { } layout) return Refuse(layout);

        if (json.TryGetProperty("extensionsRequired", out var required))
            foreach (var r in required.EnumerateArray().Select(r => r.GetString() ?? "").Where(r => !Supported.Contains(r)))
                return Refuse(r == "KHR_draco_mesh_compression" || r == "EXT_meshopt_compression"
                    ? $"{name}: required compression {r} is unsupported"
                    : $"{name}: required extension {r} is unsupported");

        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var missingImages = new HashSet<string>(StringComparer.Ordinal);
        if (json.TryGetProperty("images", out var images))
            foreach (var img in images.EnumerateArray())
                if (img.TryGetProperty("uri", out var uri) && uri.GetString() is { } u && !u.StartsWith("data:", StringComparison.Ordinal))
                {
                    var full = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(u)));
                    if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return Refuse($"{name}: resource '{u}' is outside the file's folder");
                    if (!File.Exists(full)) missingImages.Add(u);
                }

        ModelRoot model;
        try
        {
            var context = ReadContext.Create(file =>
            {
                if (file == name) return new ArraySegment<byte>(bytes);
                if (missingImages.Contains(file)) return new ArraySegment<byte>(Placeholder.Png);
                var full = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(file)));
                if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException($"resource '{file}' is outside the file's folder");
                var data=File.ReadAllBytes(full);
                var relative=Path.GetRelativePath(folder,full).Replace(Path.DirectorySeparatorChar,'/');
                var observed=new ImportedSourceRead(relative,Convert.ToHexStringLower(SHA256.HashData(data)));
                if(reads.TryGetValue(relative,out var previous)&&previous.SourceHash!=observed.SourceHash)
                    throw new IOException($"resource '{relative}' changed during import");
                reads[relative]=observed;
                return new ArraySegment<byte>(data);
            });
            context.Validation = ValidationMode.Skip;   // this importer does its own checks and names them
            model = context.ReadSchema2(name);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Refuse($"{name}: can't read it: {e.Message}");
        }

        try { return Walk(model, json, name, frame, missingImages); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Refuse($"{name}: can't read it: {e.Message}");
        }
    }

    private static ImportResult Walk(ModelRoot model, JsonElement json, string name, ProjectFrame frame, HashSet<string> missingImages)
    {
        var issues = new List<Issue>();
        // Out-of-range indices refuse the whole file.
        foreach (var mesh in model.LogicalMeshes)
            foreach (var prim in mesh.Primitives)
            {
                var count = prim.GetVertexAccessor("POSITION")?.Count ?? 0;
                if (prim.IndexAccessor is { } ia)
                    foreach (var i in ia.AsIndicesArray())
                        if (i >= count) return Refuse($"{name}: index {i} exceeds POSITION count {count}");
            }

        var meshes = new List<ImportedMesh>();
        var markers = new List<ImportedMarker>();
        var removed = 0;
        foreach (var node in model.DefaultScene?.VisualChildren.SelectMany(Walk) ?? model.LogicalNodes)
        {
            var world = node.WorldMatrix;
            var det = world.GetDeterminant();
            if (node.Mesh is null)
            {
                if (node.IsSkinJoint) continue;
                markers.Add(new ImportedMarker(node.Name ?? $"node_{node.LogicalIndex}", frame.Point(world.Translation),
                    Vec(node.Extras, "up") is { } up ? frame.Direction(up) : null,
                    Vec(node.Extras, "outward") is { } o ? frame.Direction(o) : null));
                continue;
            }
            var skinned = node.Skin is not null;
            if (!skinned && MathF.Abs(det) < 1e-9f)
                return Refuse($"singular zero scale on node '{node.Name}'; choose a non-zero scale", node.Name);
            var place = skinned ? Matrix4x4.Identity : world;   // glTF: a skinned mesh's node transform doesn't place it
            var placeDet = skinned ? 1f : det;
            if (placeDet < 0)
                issues.Add(Issue.Warning($"negative world determinant on node '{node.Name}'; winding corrected once", node.Name));
            if (skinned)
                issues.Add(Issue.Warning($"skinning not displayed (deferred) on mesh '{node.Name}'", node.Name));   // the skinned instance
            Matrix4x4.Invert(place, out var inv);
            var normalMatrix = Matrix4x4.Transpose(inv);
            var flip = placeDet * frame.Determinant < 0;
            foreach (var prim in node.Mesh.Primitives)
            {
                var pos = prim.GetVertexAccessor("POSITION").AsVector3Array().Select(p => frame.Point(Vector3.Transform(p, place))).ToArray();
                var nrm = prim.GetVertexAccessor("NORMAL")?.AsVector3Array().Select(n => Vector3.Normalize(frame.Direction(Vector3.TransformNormal(n, normalMatrix)))).ToArray() ?? [];
                var uv = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array().ToArray() ?? [];
                var tris = new List<int>();
                foreach (var (a, b, c) in Triangles(prim))
                {
                    if (Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]).LengthSquared() <= Degenerate) { removed++; continue; }
                    if (flip) tris.AddRange([a, c, b]); else tris.AddRange([a, b, c]);
                }
                meshes.Add(new ImportedMesh(node.Name ?? "", node.Mesh.Name ?? "", pos, nrm, uv, [.. tris], prim.Material?.Name, placeDet, flip, skinned)
                { MaterialInfo = prim.Material is { } material ? ImportedMaterial.FromGltf(json, material.LogicalIndex, material.Name ?? "") : null });
            }
        }
        if (removed > 0) issues.Add(Issue.Warning($"{name}: removed {removed} degenerate triangle{(removed == 1 ? "" : "s")}"));

        var textures = new List<ImportedTexture>();
        foreach (var m in model.LogicalMaterials)
            foreach (var ch in m.Channels)
                if (ch.Texture?.PrimaryImage is { } img)
                {
                    var source = img.Content.SourcePath is { Length: > 0 } sp ? Path.GetFileName(sp) : FindUri(json, img.LogicalIndex) ?? "embedded";
                    var missing = missingImages.Contains(source);
                    var role = ch.Key;
                    textures.Add(new ImportedTexture(m.Name ?? "", role, role is "BaseColor" or "Emissive" ? "sRGB" : "linear", source, missing));
                    if (missing)
                        issues.Add(Issue.Warning($"{name}: optional {role.ToLowerInvariant()} texture '{source}' not found", m.Name));
                }

        var clips = model.LogicalAnimations.Select(a => (a.Name ?? $"clip_{a.LogicalIndex}", a.Duration)).ToList();
        var asset = new ImportedAsset(meshes, markers, textures, model.LogicalMaterials.Select(m => m.Name ?? "").ToList(),
            model.LogicalSkins.Count, model.LogicalSkins.Sum(s => s.JointsCount), clips, model.LogicalMeshes.Count);
        return new ImportResult(issues.Count == 0 ? ImportOutcome.Imports : ImportOutcome.ImportsWithWarnings, asset, issues);
    }

    private static readonly Dictionary<string, int> Components = new() { ["SCALAR"] = 1, ["VEC2"] = 2, ["VEC3"] = 3, ["VEC4"] = 4, ["MAT2"] = 4, ["MAT3"] = 9, ["MAT4"] = 16 };

    // Every buffer view inside its buffer, every accessor inside its view: a count or offset that runs past the data is
    // refused by name before anything reads it (A010). Embedded (data:) buffers and the GLB binary chunk are measured.
    private static string? CheckLayout(JsonElement json, byte[] bytes, bool glb, string name)
    {
        var sizes = new List<long>();
        if (json.TryGetProperty("buffers", out var buffers))
            for (var b = 0; b < buffers.GetArrayLength(); b++)
            {
                var buf = buffers[b];
                var declared = buf.TryGetProperty("byteLength", out var bl) && bl.TryGetInt64(out var n) ? n : -1;
                if (declared < 0) return $"{name}: buffer {b} has no byteLength";
                long actual = declared;
                if (buf.TryGetProperty("uri", out var uri) && uri.GetString() is { } u && u.StartsWith("data:", StringComparison.Ordinal))
                {
                    var comma = u.IndexOf(',');
                    try { actual = comma < 0 ? 0 : Convert.FromBase64String(u[(comma + 1)..]).LongLength; }
                    catch (FormatException) { return $"{name}: buffer {b}'s embedded data isn't valid base64"; }
                }
                else if (glb && b == 0 && !buf.TryGetProperty("uri", out _))
                {
                    long jsonLength = BitConverter.ToUInt32(bytes, 12);
                    var bin = 20 + jsonLength;
                    actual = bin + 8 <= bytes.Length ? BitConverter.ToUInt32(bytes, (int)bin) : 0;
                }
                if (actual < declared) return $"{name}: buffer {b} holds {actual} bytes but declares {declared}";
                sizes.Add(declared);
            }
        var views = new List<(long Length, long Stride)>();
        if (json.TryGetProperty("bufferViews", out var bufferViews))
            for (var v = 0; v < bufferViews.GetArrayLength(); v++)
            {
                var view = bufferViews[v];
                var buffer = view.GetProperty("buffer").GetInt32();
                var offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt64() : 0;
                var length = view.GetProperty("byteLength").GetInt64();
                if (buffer < 0 || buffer >= sizes.Count || offset < 0 || length < 0 || offset + length > sizes[buffer])
                    return $"{name}: buffer view {v} runs past its buffer";
                views.Add((length, view.TryGetProperty("byteStride", out var st) ? st.GetInt64() : 0));
            }
        if (json.TryGetProperty("accessors", out var accessors))
            for (var a = 0; a < accessors.GetArrayLength(); a++)
            {
                var acc = accessors[a];
                if (!acc.TryGetProperty("bufferView", out var bv)) continue;   // sparse-only or zero-filled: nothing to overrun
                var v = bv.GetInt32();
                if (v < 0 || v >= views.Count) return $"{name}: accessor {a} names buffer view {v}, which doesn't exist";
                var count = acc.GetProperty("count").GetInt64();
                var comp = acc.GetProperty("componentType").GetInt32() switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };
                var element = comp * (Components.TryGetValue(acc.GetProperty("type").GetString() ?? "", out var c) ? c : 1);
                var stride = views[v].Stride > 0 ? views[v].Stride : element;
                var offset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt64() : 0;
                if (count < 0 || (count > 0 && offset + stride * (count - 1) + element > views[v].Length))
                    return $"{name}: accessor {a}'s count of {count} needs more than its buffer view's {views[v].Length} bytes";
            }
        return null;
    }

    private static ImportResult Refuse(string message, string? objectId = null) =>
        new(ImportOutcome.Refused, null, [Issue.Error(message, objectId)]);

    // Triangle lists are read raw, so a triangle that repeats an index is seen (and counted when removed); SharpGLTF's own
    // enumerator skips those silently. Strips and fans go through SharpGLTF.
    private static IEnumerable<(int A, int B, int C)> Triangles(MeshPrimitive prim)
    {
        if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES) return prim.GetTriangleIndices();
        if (prim.IndexAccessor is null)   // no index list: the vertices themselves, three at a time
            return Enumerable.Range(0, (prim.GetVertexAccessor("POSITION")?.Count ?? 0) / 3).Select(t => (3 * t, 3 * t + 1, 3 * t + 2));
        var idx = prim.GetIndices();
        return Enumerable.Range(0, idx.Count / 3).Select(t => ((int)idx[3 * t], (int)idx[3 * t + 1], (int)idx[3 * t + 2]));
    }

    private static IEnumerable<Node> Walk(Node n) => new[] { n }.Concat(n.VisualChildren.SelectMany(Walk));

    private static string? FindUri(JsonElement json, int image) =>
        json.TryGetProperty("images", out var images) && image < images.GetArrayLength() && images[image].TryGetProperty("uri", out var u) ? u.GetString() : null;

    private static Vector3? Vec(System.Text.Json.Nodes.JsonNode? extras, string key) =>
        extras?[key] is System.Text.Json.Nodes.JsonArray a && a.Count == 3
            ? new Vector3(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>())
            : null;

    private static class Placeholder
    {
        // A 1Ã—1 transparent PNG, standing in for a missing optional image so the rest of the file can be read.
        public static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    }
}
