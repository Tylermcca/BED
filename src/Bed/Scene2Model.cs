using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
namespace Bed;

/// <summary>Coordinate profile of a version-two scene or definition.</summary>
public enum SceneSpace
{
    /// <summary>Three-dimensional placement.</summary>
    ThreeD,
    /// <summary>Planar XY placement and Z rotation.</summary>
    TwoDXY,
}

/// <summary>Sampling requested by an admitted sprite.</summary>
public enum SceneSpriteFilter
{
    /// <summary>Nearest texel sampling.</summary>
    Nearest,
    /// <summary>Linear texel sampling.</summary>
    Linear,
}

/// <summary>Immutable sprite dimensions and display order.</summary>
/// <param name="Size">Positive dimensions in metres.</param>
/// <param name="Pivot">Bottom-left normalized pivot.</param>
/// <param name="Order">Ascending draw order; document ordinal breaks ties.</param>
/// <param name="Filter">Texture sampling mode.</param>
public sealed record SceneSprite(Vector2 Size, Vector2 Pivot, int Order, SceneSpriteFilter Filter);

/// <summary>One immutable effective object with optional sprite metadata.</summary>
/// <param name="Object">Existing scene object content; v1 types remain unchanged.</param>
/// <param name="Sprite">Sprite description, or null for a marker/model.</param>
public sealed record Scene2Object(SceneObject Object, SceneSprite? Sprite)
{
    /// <summary>Optional authored collision and motion description; does not create a body.</summary>
    public SceneBody? Body {get;init;}
    /// <summary>Owned content, including detached property values.</summary>
    public SceneObject Object { get; } = Object with
    { Properties=Object.Properties.ToImmutableSortedDictionary(p=>p.Key,p=>p.Value.Clone(),StringComparer.Ordinal),
      References=Object.References.ToImmutableSortedDictionary(StringComparer.Ordinal) };
}

/// <summary>A definition reference to one node or one exposed slot.</summary>
/// <param name="Node">Target node key, or null when using Slot.</param>
/// <param name="Slot">Target slot key, or null when using Node.</param>
public sealed record PrefabReference(string? Node, string? Slot);

/// <summary>Immutable content of one definition node.</summary>
/// <param name="Key">Immutable node key.</param>
/// <param name="Label">Display label.</param>
/// <param name="ParentKey">Parent node key; absent only for the root.</param>
/// <param name="Asset">Asset reference or null.</param>
/// <param name="Position">Local metre position.</param>
/// <param name="Rotation">Local rotation components.</param>
/// <param name="Scale">Local scale.</param>
/// <param name="Properties">Owned scalar values.</param>
/// <param name="References">Node or slot references.</param>
/// <param name="Sprite">Optional planar sprite.</param>
public sealed record PrefabNode(string Key, string Label, string? ParentKey, string? Asset, Vector3 Position, Quaternion Rotation, Vector3 Scale, ImmutableSortedDictionary<string, JsonElement> Properties, ImmutableSortedDictionary<string, PrefabReference> References, SceneSprite? Sprite)
{
    /// <summary>Optional body included in the definition content hash.</summary>
    public SceneBody? Body {get;init;}
    /// <summary>Detached immutable property values.</summary>
    public ImmutableSortedDictionary<string,JsonElement> Properties {get;}=Properties.ToImmutableSortedDictionary(p=>p.Key,p=>p.Value.Clone(),StringComparer.Ordinal);
    /// <summary>Ordinal immutable reference names.</summary>
    public ImmutableSortedDictionary<string,PrefabReference> References {get;}=References.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

/// <summary>An exposed definition reference slot.</summary>
/// <param name="Key">Immutable slot key.</param>
/// <param name="Label">Display label.</param>
/// <param name="Required">Whether every instance must bind the slot.</param>
public sealed record PrefabSlot(string Key, string Label, bool Required);

/// <summary>Validated immutable definition plus its exact supplied JSON before defaults. Read through PrefabDefinitionFormat.</summary>
public sealed class PrefabDefinition
{
    /// <summary>Immutable source identity.</summary>
    public string Guid {get;}
    /// <summary>Coordinate profile.</summary>
    public SceneSpace Space {get;}
    /// <summary>Root node key.</summary>
    public string Root {get;}
    /// <summary>Nodes in supplied order.</summary>
    public ImmutableArray<PrefabNode> Nodes {get;}
    /// <summary>Exposed reference slots.</summary>
    public ImmutableArray<PrefabSlot> Slots {get;}
    internal JsonElement Original {get;}
    internal PrefabDefinition(string guid,SceneSpace space,string root,ImmutableArray<PrefabNode> nodes,ImmutableArray<PrefabSlot> slots,JsonElement original)
    {Guid=guid;Space=space;Root=root;Nodes=nodes;Slots=slots;Original=original.Clone();}
}

/// <summary>One accepted immutable definition revision.</summary>
/// <param name="Id">Readable base identity.</param>
/// <param name="Revision">SHA-256 of the original definition JCS bytes.</param>
/// <param name="Definition">Accepted snapshot, independent of source discovery.</param>
public sealed record PrefabBase(string Id, string Revision, PrefabDefinition Definition);

/// <summary>Explicit local override operation.</summary>
public enum PrefabOverrideOperation
{
    /// <summary>Set the typed value.</summary>
    Set,
    /// <summary>Remove an asset, property or reference.</summary>
    Unset,
}

/// <summary>An explicit atomic field override.</summary>
/// <param name="Node">Accepted node key.</param>
/// <param name="Field">Escaped JSON Pointer field address.</param>
/// <param name="Operation">Set or unset.</param>
/// <param name="Value">Detached set value; absent for unset.</param>
public sealed record PrefabOverride(string Node, string Field, PrefabOverrideOperation Operation, JsonElement? Value)
{
    /// <summary>Owned set value.</summary>
    public JsonElement? Value {get;}=Value?.Clone();
}

/// <summary>One placed instance of an accepted definition.</summary>
/// <param name="Key">Immutable instance key.</param>
/// <param name="Source">Definition GUID.</param>
/// <param name="Base">Accepted base id.</param>
/// <param name="Placement">Wrapper transform, optional parent and optional display name in Properties["name"].</param>
/// <param name="NodeIds">Exact accepted-node to scene-id map.</param>
/// <param name="Bindings">Slot to final scene-id bindings.</param>
/// <param name="Overrides">Explicit local atomic fields.</param>
public sealed record PrefabInstance(string Key, string Source, string Base, SceneObject Placement, ImmutableSortedDictionary<string, string> NodeIds, ImmutableSortedDictionary<string, string> Bindings, ImmutableArray<PrefabOverride> Overrides)
{
    /// <summary>Ordinal immutable node-key map.</summary>
    public ImmutableSortedDictionary<string,string> NodeIds {get;}=NodeIds.ToImmutableSortedDictionary(StringComparer.Ordinal);
    /// <summary>Ordinal immutable slot-key map.</summary>
    public ImmutableSortedDictionary<string,string> Bindings {get;}=Bindings.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

/// <summary>Immutable version-two authoring authority; never a mutable flattened shadow.</summary>
/// <param name="Space">3D or planar XY profile.</param>
/// <param name="Objects">Ordinary objects in document order.</param>
/// <param name="Bases">Accepted snapshots.</param>
/// <param name="Instances">Instance placements and local values.</param>
/// <param name="ObjectOrder">Optional exact expanded-id permutation.</param>
public sealed record Scene2(SceneSpace Space, ImmutableArray<Scene2Object> Objects, ImmutableArray<PrefabBase> Bases, ImmutableArray<PrefabInstance> Instances, ImmutableArray<string>? ObjectOrder)
{
    /// <summary>Explicit optional world settings; required when any resolved object has a body.</summary>
    public ScenePhysics? Physics {get;init;}
}

/// <summary>Owner of an effective scene object.</summary>
public enum Scene2OriginKind
{
    /// <summary>Ordinary authored object.</summary>
    Ordinary,
    /// <summary>Instance placement wrapper.</summary>
    Placement,
    /// <summary>Accepted definition node with local overrides.</summary>
    PrefabNode,
}

/// <summary>Exact authoring ownership of an effective object.</summary>
/// <param name="Kind">Ordinary, placement or prefab node.</param>
/// <param name="InstanceKey">Instance key when linked.</param>
/// <param name="NodeKey">Definition node key when linked.</param>
/// <param name="SourcePointer">Source JSON Pointer in the authoring scene.</param>
/// <param name="FieldOwners">Field pointer to source/override value pointer.</param>
public sealed record Scene2Origin(Scene2OriginKind Kind, string? InstanceKey, string? NodeKey, string SourcePointer, ImmutableSortedDictionary<string, string> FieldOwners);

/// <summary>Pure resolved objects and immutable authoring provenance.</summary>
/// <param name="Objects">Effective objects in authoritative order.</param>
/// <param name="Origins">Origin for every effective scene id.</param>
public sealed record Scene2Resolution(ImmutableArray<Scene2Object> Objects, ImmutableSortedDictionary<string, Scene2Origin> Origins);
