using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Bed;

/// <summary>
/// One placed object in a <see cref="Scene"/>: where it is, what asset it shows, what it's called and how it relates to
/// other objects. General on purpose: a game's own meaning lives in its properties and in a game-side adapter.
/// </summary>
/// <param name="Id">A stable, readable id, unique in the scene.</param>
/// <param name="Asset">The asset it shows (a path or an asset GUID), or null for an empty (a group, a marker).</param>
/// <param name="Position">Where it is, in metres, in the project's frame.</param>
/// <param name="Rotation">Its rotation, as a unit quaternion (x, y, z, w).</param>
/// <param name="Scale">Its scale on each axis.</param>
/// <param name="Parent">The object it sits inside (a place, a group), or null at the top level.</param>
/// <param name="Properties">Named values (text, numbers, true/false), in a fixed order.</param>
/// <param name="References">Named links to other objects by id, in a fixed order.</param>
[System.Text.Json.Serialization.JsonConverter(typeof(SceneObjectJson))]
public sealed record SceneObject(string Id, string? Asset, Vector3 Position, Quaternion Rotation, Vector3 Scale, string? Parent,
    ImmutableSortedDictionary<string, JsonElement> Properties, ImmutableSortedDictionary<string, string> References)
{
    /// <summary>An object at <paramref name="position"/> with no rotation, unit scale, no parent, properties or references.</summary>
    public static SceneObject At(string id, string? asset, Vector3 position) => new(id, asset, position, Quaternion.Identity, Vector3.One, null,
        ImmutableSortedDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal),
        ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal));
}

/// <summary>Commands carry objects in the scene file's own shape (<see cref="SceneFormat"/>): optional fields may be left
/// out, and reading is just as strict.</summary>
internal sealed class SceneObjectJson : System.Text.Json.Serialization.JsonConverter<SceneObject>
{
    public override SceneObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        try { return SceneFormat.ReadObject(new StrictReader(doc.RootElement.Clone(), "$")); }
        catch (DocumentFormatException e) { throw new JsonException(e.Message, e); }   // the catalogue joins the paths
    }

    public override void Write(Utf8JsonWriter writer, SceneObject value, JsonSerializerOptions options)
    {
        foreach (var x in new[] { value.Position.X, value.Position.Y, value.Position.Z, value.Rotation.X, value.Rotation.Y, value.Rotation.Z,
                     value.Rotation.W, value.Scale.X, value.Scale.Y, value.Scale.Z })
            if (!float.IsFinite(x)) throw new ArgumentException($"object '{value.Id}' has a number that isn't finite, so it can't be recorded");
        SceneFormat.WriteObject(writer, value);
    }
}

/// <summary>A scene: placed objects, in document order. Immutable; commands return a new scene.</summary>
/// <param name="Objects">The placed objects in document order. The scene validator checks their validity separately.</param>
public sealed record Scene(ImmutableArray<SceneObject> Objects)
{
    /// <summary>An empty scene.</summary>
    public static readonly Scene Empty = new(ImmutableArray<SceneObject>.Empty);

    /// <summary>The object with <paramref name="id"/>, or null.</summary>
    public SceneObject? Find(string id) => Objects.FirstOrDefault(o => o.Id == id);

    internal Scene Replace(SceneObject o) => new(Objects.Select(x => x.Id == o.Id ? o : x).ToImmutableArray());
}

/// <summary>
/// Reads and writes scenes as canonical JSON: <c>{"kind": …, "version": 1, "objects": [...]}</c>. The kind is the caller's
/// (a game names its own); reading is strict, with every problem at its JSON path. Rotation, scale, parent, properties and
/// references may be left out (identity, one, none, empty), and so may position (the origin); writing always includes
/// them, so the text is canonical.
/// </summary>
public sealed class SceneFormat(string kind = "scene") : IDocumentFormat<Scene>
{
    /// <inheritdoc />
    public string Kind => kind;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public Scene Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "objects");
        if (r.String("kind") != Kind) throw new DocumentFormatException("$.kind", $"expected \"{Kind}\"");
        var version = r.Int("version");
        if (version != Version) throw new DocumentFormatException("$.version", $"scene version {version} is not supported (this tool reads version {Version})");
        return new Scene(r.Array("objects").Select(ReadObject).ToImmutableArray());
    }

    /// <summary>One object, as a scene file (and a command's arguments) give it; strict, optional fields defaulted.</summary>
    internal static SceneObject ReadObject(StrictReader o)
    {
        o.Only("id", "asset", "position", "rotation", "scale", "parent", "properties", "references");
        var props = ImmutableSortedDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        if (o.Has("properties"))
            foreach (var p in Obj(o, "properties"))
            {
                if (p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                    throw new DocumentFormatException($"{o.Path}.properties.{p.Name}", "expected text, a number or true/false");
                if (!props.TryAdd(p.Name, p.Value.Clone())) throw new DocumentFormatException($"{o.Path}.properties.{p.Name}", "property given twice");
            }
        var refs = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (o.Has("references"))
            foreach (var p in Obj(o, "references"))
            {
                if (p.Value.ValueKind != JsonValueKind.String) throw new DocumentFormatException($"{o.Path}.references.{p.Name}", "expected an object id");
                if (!refs.TryAdd(p.Name, p.Value.GetString()!)) throw new DocumentFormatException($"{o.Path}.references.{p.Name}", "property given twice");
            }
        var rot = o.Has("rotation") ? Floats(o, "rotation", 4) : [0, 0, 0, 1];
        var scale = o.Has("scale") ? Floats(o, "scale", 3) : [1, 1, 1];
        var pos = o.Has("position") ? Floats(o, "position", 3) : [0, 0, 0];
        return new SceneObject(o.String("id"), o.Has("asset") ? o.String("asset") : null, new Vector3(pos[0], pos[1], pos[2]),
            new Quaternion(rot[0], rot[1], rot[2], rot[3]), new Vector3(scale[0], scale[1], scale[2]), o.Has("parent") ? o.String("parent") : null,
            props.ToImmutable(), refs.ToImmutable());
    }

    private static JsonElement.ObjectEnumerator Obj(StrictReader o, string name)
    {
        var e = o.Element(name);
        return e.ValueKind == JsonValueKind.Object ? e.EnumerateObject() : throw new DocumentFormatException($"{o.Path}.{name}", "expected an object");
    }

    private static float[] Floats(StrictReader o, string name, int n)
    {
        var a = o.Element(name);
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != n) throw new DocumentFormatException($"{o.Path}.{name}", $"expected {n} numbers");
        var v = new float[n];
        for (var i = 0; i < n; i++)
            if (a[i].ValueKind != JsonValueKind.Number || !a[i].TryGetSingle(out v[i]) || !float.IsFinite(v[i]))
                throw new DocumentFormatException($"{o.Path}.{name}[{i}]", "expected a finite number");
        return v;
    }

    /// <inheritdoc />
    public string Write(Scene document)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind", Kind);
            w.WriteNumber("version", Version);
            w.WriteStartArray("objects");
            foreach (var o in document.Objects) WriteObject(w, o);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>One object, written canonically (every field but a null asset and parent).</summary>
    internal static void WriteObject(Utf8JsonWriter w, SceneObject o)
    {
        w.WriteStartObject();
        w.WriteString("id", o.Id);
        if (o.Asset is not null) w.WriteString("asset", o.Asset);
        Vec(w, "position", [o.Position.X, o.Position.Y, o.Position.Z]);
        Vec(w, "rotation", [o.Rotation.X, o.Rotation.Y, o.Rotation.Z, o.Rotation.W]);
        Vec(w, "scale", [o.Scale.X, o.Scale.Y, o.Scale.Z]);
        if (o.Parent is not null) w.WriteString("parent", o.Parent);
        w.WriteStartObject("properties");
        foreach (var (k, v) in o.Properties) { w.WritePropertyName(k); v.WriteTo(w); }
        w.WriteEndObject();
        w.WriteStartObject("references");
        foreach (var (k, v) in o.References) w.WriteString(k, v);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void Vec(Utf8JsonWriter w, string name, float[] v)
    {
        w.WriteStartArray(name);
        foreach (var x in v) w.WriteNumberValue(x);
        w.WriteEndArray();
    }
}

/// <summary>A scene's links (to <see cref="LinkValidator{TDoc}"/>): parents and named references, both between objects.</summary>
public sealed class SceneLinks : ILinkSchema<Scene>
{
    /// <summary>"is inside": a parent link; deleting the parent refuses while children remain; no cycles.</summary>
    public static readonly LinkKind Inside = new("is inside", LinkTarget.Object, AllowCycles: false, OnDelete: OnDelete.Refuse);

    /// <summary>"refers to": a named reference; deleting the target refuses while it's referenced.</summary>
    public static readonly LinkKind RefersTo = new("refers to", LinkTarget.Object, OnDelete: OnDelete.Refuse);

    /// <inheritdoc />
    public IEnumerable<string> ObjectIds(Scene d) => d.Objects.Select(o => o.Id);

    /// <inheritdoc />
    public IEnumerable<Link> Links(Scene d)
    {
        for (var i = 0; i < d.Objects.Length; i++)
        {
            var o = d.Objects[i];
            if (o.Parent is { } p) yield return new Link(Inside, o.Id, $"$.objects[{i}].parent", p);
            foreach (var (name, target) in o.References) yield return new Link(RefersTo, o.Id, $"$.objects[{i}].references.{name}", target);
        }
    }
}

/// <summary>What makes a scene valid: well-formed unique ids, and every parent and reference resolving (no parent cycles).</summary>
public sealed partial class SceneValidator : IValidator<Scene>
{
    private static readonly SceneLinks Schema = new();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial System.Text.RegularExpressions.Regex IdPattern();

    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(Scene document)
    {
        var issues = new List<Issue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < document.Objects.Length; i++)
        {
            var o = document.Objects[i];
            if (!IdPattern().IsMatch(o.Id))
                issues.Add(Issue.Error($"id '{o.Id}' must start with a letter and use only lowercase letters, digits and underscores", o.Id, $"$.objects[{i}].id"));
            else if (!seen.Add(o.Id))
                issues.Add(Issue.Error($"object id '{o.Id}' is duplicated; give one of them another id", o.Id, $"$.objects[{i}].id"));
        }
        issues.AddRange(new LinkValidator<Scene>(Schema).Validate(document));
        return issues;
    }
}

/// <summary>Checks a scene's assets without refusing the scene (IM-07): a missing asset is reported, the object is kept as
/// a placeholder, and its path survives saving.</summary>
public static class SceneAssets
{
    /// <summary>One issue per object whose asset <paramref name="exists"/> says is missing.</summary>
    public static IReadOnlyList<Issue> Check(Scene scene, Func<string, bool> exists) =>
        scene.Objects.Where(o => o.Asset is { } a && !exists(a))
            .Select(o => Issue.Error($"object '{o.Id}': asset '{o.Asset}' not found; retain placeholder", o.Id)).ToList();
}

/// <summary>Adds an object.</summary>
/// <param name="Object">The object to append. Applying the command is refused if its id is already present.</param>
public sealed record AddObject(SceneObject Object) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d) => d.Find(Object.Id) is null
        ? CommandResult<Scene>.Done(new Scene(d.Objects.Add(Object)))
        : CommandResult<Scene>.Refused(Issue.Error($"an object called '{Object.Id}' already exists", Object.Id));
}

/// <summary>Renames an object in place and remaps every parent and named reference to its new id.</summary>
/// <param name="Id">The existing object id.</param>
/// <param name="NewId">An unused id starting with a lowercase letter, followed by lowercase letters, digits or underscores.</param>
public sealed record RenameObject(string Id, string NewId) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d)
    {
        if(d.Find(Id) is null)return CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'",Id));
        if(string.IsNullOrEmpty(NewId)||NewId[0] is <'a' or >'z'||NewId.Any(c=>c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))
            return CommandResult<Scene>.Refused(Issue.Error("An object ID must start with a lowercase letter and use only lowercase letters, digits and underscores.",Id));
        if(d.Find(NewId) is not null)return CommandResult<Scene>.Refused(Issue.Error($"an object called '{NewId}' already exists; choose a different ID",Id));
        return CommandResult<Scene>.Done(new Scene([..d.Objects.Select(o=>o with
        {
            Id=o.Id==Id?NewId:o.Id,
            Parent=o.Parent==Id?NewId:o.Parent,
            References=o.References.ToImmutableSortedDictionary(kv=>kv.Key,kv=>kv.Value==Id?NewId:kv.Value,StringComparer.Ordinal)
        })]));
    }
}

/// <summary>Moves an object.</summary>
/// <param name="Id">The id of the existing object to move.</param>
/// <param name="Position">The replacement position in metres in the project coordinate frame, not an offset from the old position.</param>
public sealed record MoveObject(string Id, Vector3 Position) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d) => d.Find(Id) is { } o
        ? CommandResult<Scene>.Done(d.Replace(o with { Position = Position }))
        : CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
}

/// <summary>Turns an object to <see cref="Rotation"/>, a unit quaternion: its length must be within 0.001 of 1 (float
/// rounding from typed or computed angles stays inside that). Anything further off is refused, never quietly normalised.</summary>
public sealed record RotateObject(string Id, Quaternion Rotation) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d)
    {
        if (d.Find(Id) is not { } o) return CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
        if (!float.IsFinite(Rotation.Length()) || MathF.Abs(Rotation.Length() - 1) > 1e-3f)
            return CommandResult<Scene>.Refused(Issue.Error($"a rotation must be a unit quaternion; this one has length {Rotation.Length():0.###}", Id));
        return CommandResult<Scene>.Done(d.Replace(o with { Rotation = Rotation }));
    }
}

/// <summary>Scales an object. A zero or non-finite axis is refused (the object would vanish or break).</summary>
public sealed record ScaleObject(string Id, Vector3 Scale) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d)
    {
        if (d.Find(Id) is not { } o) return CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
        if (Scale.X == 0 || Scale.Y == 0 || Scale.Z == 0 || !float.IsFinite(Scale.X + Scale.Y + Scale.Z))
            return CommandResult<Scene>.Refused(Issue.Error("every scale axis must be a finite number other than zero", Id));
        return CommandResult<Scene>.Done(d.Replace(o with { Scale = Scale }));
    }
}

/// <summary>Puts an object inside <see cref="Parent"/>, or at the top level when it's null. A parent that doesn't exist,
/// or one that would make a circle, is refused by the scene's validator.</summary>
public sealed record SetParent(string Id, string? Parent) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d) => d.Find(Id) is { } o
        ? CommandResult<Scene>.Done(d.Replace(o with { Parent = Parent }))
        : CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
}

/// <summary>Changes the asset an object shows (a path or an asset GUID), or makes it an empty when it's null. A missing
/// asset isn't refused: it's reported by <see cref="SceneAssets"/> and kept as a placeholder.</summary>
public sealed record SetAsset(string Id, string? Asset) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d) => d.Find(Id) is { } o
        ? CommandResult<Scene>.Done(d.Replace(o with { Asset = Asset }))
        : CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
}

/// <summary>Points a named reference at another object, or removes it when <see cref="Target"/> is null. A target that
/// doesn't exist is refused by the scene's validator.</summary>
public sealed record SetReference(string Id, string Name, string? Target) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d) => d.Find(Id) is { } o
        ? CommandResult<Scene>.Done(d.Replace(o with { References = Target is { } t ? o.References.SetItem(Name, t) : o.References.Remove(Name) }))
        : CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
}

/// <summary>Sets a property (text, number or true/false), or removes it when <see cref="Value"/> is null.</summary>
public sealed record SetSceneProperty(string Id, string Name, JsonElement? Value) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d)
    {
        if (d.Find(Id) is not { } o) return CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
        if (Value is { } v && v.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            return CommandResult<Scene>.Refused(Issue.Error($"'{Name}' must be text, a number or true/false", Id));
        return CommandResult<Scene>.Done(d.Replace(o with { Properties = Value is { } x ? o.Properties.SetItem(Name, x.Clone()) : o.Properties.Remove(Name) }));
    }
}

/// <summary>Removes an object; refused while another object is inside it or refers to it (the message names them).</summary>
public sealed record RemoveObject(string Id) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d)
    {
        if (d.Find(Id) is null) return CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{Id}'", Id));
        var (blocking, delete, _) = LinkPlans.PlanDelete(new SceneLinks(), d, Id);
        if (blocking.Count > 0)
            return CommandResult<Scene>.Refused(Issue.Error($"'{Id}' can't be removed while {string.Join(", ", blocking.Select(b => $"'{b.From}' {b.Kind.Name} it"))}", Id));
        return CommandResult<Scene>.Done(new Scene(d.Objects.RemoveAll(o => delete.Contains(o.Id))));
    }
}

/// <summary>
/// Duplicates a group of objects (AT-101, IM-10). Each copy gets a new id (<see cref="Prefix"/> plus the original);
/// references and parents inside the group point to the copies, and references leaving the group keep their targets.
/// The ids chosen are recorded, so a replay makes the same copies.
/// </summary>
public sealed record DuplicateGroup(IReadOnlyList<string> Ids, string Prefix = "copy_", IReadOnlyDictionary<string, string>? Map = null) : ICommand<Scene>
{
    /// <inheritdoc />
    public CommandResult<Scene> Apply(Scene d)
    {
        var missing = Ids.Where(id => d.Find(id) is null).ToList();
        if (missing.Count > 0) return CommandResult<Scene>.Refused(Issue.Error($"there is no object called '{missing[0]}'", missing[0]));
        var map = Map ?? LinkPlans.PlanDuplicate(new SceneLinks(), d, Ids, (id, n) => n == 1 ? Prefix + id : $"{Prefix}{id}_{n}");
        string In(string id) => map.TryGetValue(id, out var c) ? c : id;
        var copies = Ids.Select(id => d.Find(id)!).Select(o => o with
        {
            Id = map[o.Id],
            Parent = o.Parent is { } p ? In(p) : null,
            References = o.References.ToImmutableSortedDictionary(kv => kv.Key, kv => In(kv.Value), StringComparer.Ordinal),
        });
        return CommandResult<Scene>.Done(new Scene(d.Objects.AddRange(copies)), this with { Map = map });
    }
}

/// <summary>The scene's format, validator and commands, registered by hand.</summary>
public static class SceneKit
{
    /// <summary>The commands every scene supports.</summary>
    public static CommandCatalog<Scene> Catalog() => new CommandCatalog<Scene>()
        .Register<AddObject>("scene.add", 1, "Add object")
        .Register<MoveObject>("scene.move", 1, "Move object")
        .Register<RenameObject>("scene.rename", 1, "Rename object")
        .Register<SetSceneProperty>("scene.set_property", 1, "Set property")
        .Register<RemoveObject>("scene.remove", 1, "Remove object")
        .Register<DuplicateGroup>("scene.duplicate", 1, "Duplicate")
        .Register<RotateObject>("scene.rotate", 1, "Rotate object")
        .Register<ScaleObject>("scene.scale", 1, "Scale object")
        .Register<SetParent>("scene.set_parent", 1, "Set parent")
        .Register<SetAsset>("scene.set_asset", 1, "Set asset")
        .Register<SetReference>("scene.set_reference", 1, "Set reference");
}
