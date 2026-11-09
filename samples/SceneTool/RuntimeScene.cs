using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using Brutal.Authoring;

namespace SceneTool;

/// <summary>Synthetic application-owned consumer, independent of editor sessions, UI and rendering.
/// Builds local runtime identities in two passes so forward parents and cyclic named references retain identity.</summary>
public sealed class RuntimeScene
{
    private RuntimeScene(IReadOnlyList<RuntimeObject> objects, bool assetsChecked, IReadOnlyList<Issue> assetIssues)
    { Objects=objects; AssetsChecked=assetsChecked; AssetIssues=assetIssues; }
    public IReadOnlyList<RuntimeObject> Objects { get; }
    public bool AssetsChecked { get; }
    public IReadOnlyList<Issue> AssetIssues { get; }

    /// <summary>Acquisition belongs to the caller. All format/graph/asset-lookup failures propagate before any graph is published.
    /// Missing assets remain explicit references plus diagnostics; this is not permission to Play or export.</summary>
    public static RuntimeScene Load(string serialized, string kind="scene", Func<string,bool>? assetExists=null)
    {
        var read=SceneRuntimeReader.Read(serialized,kind,assetExists);
        var ordered=read.Scene.Objects.Select(source=>new RuntimeObject(source)).ToArray();
        var byId=ordered.ToDictionary(item=>item.Id,StringComparer.Ordinal);
        for(var i=0;i<ordered.Length;i++)
        {
            var source=read.Scene.Objects[i];
            ordered[i].Link(source.Parent is null?null:byId[source.Parent],
                source.References.ToDictionary(pair=>pair.Key,pair=>byId[pair.Value],StringComparer.Ordinal));
        }
        return new(Array.AsReadOnly(ordered),read.AssetsChecked,read.AssetIssues);
    }
}

/// <summary>One synthetic runtime object. Local values are deliberately not normalized or flattened into world space.</summary>
public sealed class RuntimeObject
{
    internal RuntimeObject(SceneObject source)
    {
        Id=source.Id;Asset=source.Asset;LocalPosition=source.Position;LocalRotation=source.Rotation;LocalScale=source.Scale;
        Properties=new ReadOnlyDictionary<string,JsonElement>(source.Properties.ToDictionary(p=>p.Key,p=>p.Value.Clone(),StringComparer.Ordinal));
    }
    public string Id { get; }
    public string? Asset { get; }
    public Vector3 LocalPosition { get; }
    public Quaternion LocalRotation { get; }
    public Vector3 LocalScale { get; }
    public IReadOnlyDictionary<string,JsonElement> Properties { get; }
    public RuntimeObject? Parent { get; private set; }
    public IReadOnlyDictionary<string,RuntimeObject> References { get; private set; } = new ReadOnlyDictionary<string,RuntimeObject>(new Dictionary<string,RuntimeObject>());
    internal void Link(RuntimeObject? parent, Dictionary<string,RuntimeObject> references)
    { Parent=parent;References=new ReadOnlyDictionary<string,RuntimeObject>(references); }
}
