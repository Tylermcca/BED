using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
namespace Brutal.Authoring.Editor;

/// <summary>One game-owned dependency declaration. Bytes are copied into a pinned lease; dependencies are logical
/// input IDs, not inferred paths. DeclaredComplete is the producer's coverage claim, not BED's discovery result.</summary>
public sealed class GameContentNode
{
    private readonly byte[] bytes;
    /// <summary>Copies one bounded artifact and its bounded dependency list. Hash validation belongs to manifest admission.</summary>
    public GameContentNode(GameContentMapping output,ReadOnlySpan<byte> pinnedBytes,string sha256,IEnumerable<string> dependencies,bool declaredComplete,ExportSource? source=null)
    {
        ArgumentNullException.ThrowIfNull(output);ArgumentNullException.ThrowIfNull(dependencies);
        if(pinnedBytes.Length>64*1024*1024)throw new ArgumentException("An artifact exceeds 64 MiB.",nameof(pinnedBytes));
        var deps=dependencies.Take(1025).ToArray();if(deps.Length>1024)throw new ArgumentException("Too many artifact dependencies.",nameof(dependencies));
        if(deps.Any(d=>string.IsNullOrWhiteSpace(d)||d.Length>128))throw new ArgumentException("Dependency identity is missing or too long.",nameof(dependencies));
        Source=source;Output=output;bytes=pinnedBytes.ToArray();Sha256=sha256;Dependencies=deps.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();DeclaredComplete=declaredComplete;
    }
    /// <summary>Explicit producer provenance, never inferred from a filename. Missing provenance refuses origin review.</summary>
    public ExportSource? Source {get;}
    /// <summary>Game-declared logical identity, destination and runtime format.</summary>
    public GameContentMapping Output {get;}
    /// <summary>Expected lowercase SHA-256 of exactly these artifact bytes, not a catalog content-revision fingerprint.</summary>
    public string Sha256 {get;}
    /// <summary>All required logical dependencies declared by the game.</summary>
    public ImmutableArray<string> Dependencies {get;}
    /// <summary>Whether the producer explicitly declared this dependency set complete.</summary>
    public bool DeclaredComplete {get;}
    /// <summary>Owned byte count.</summary>
    public int Length=>bytes.Length;
    /// <summary>Copies the pinned artifact without exposing mutable storage.</summary>
    public void CopyTo(Span<byte> destination)=>bytes.AsSpan().CopyTo(destination);
    internal bool HashMatches()=>Sha256 is {Length:64} && Sha256.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')
        && Convert.ToHexStringLower(SHA256.HashData(bytes))==Sha256;
}

/// <summary>Closed, immutable game-declared export inventory. BED proves bounded graph/hash consistency, not that
/// an arbitrary game omitted no hidden dependency, granted redistribution rights, or can build/run this profile.</summary>
public sealed class GameContentManifest
{
    private GameContentManifest(GameBuildProfile profile,string owner,string revision,ImmutableArray<GameContentNode> nodes,string hash,string? sourceManifestHash)
    {Profile=profile;InventoryOwner=owner;InventoryRevision=revision;Nodes=nodes;Hash=hash;SourceManifestHash=sourceManifestHash;}
    /// <summary>Exact immutable profile covered by this manifest.</summary>
    public GameBuildProfile Profile {get;}
    /// <summary>Game-owned producer identifier; provenance, not an authentication credential.</summary>
    public string InventoryOwner {get;}
    /// <summary>Game-owned immutable inventory revision at capture, not live editor freshness.</summary>
    public string InventoryRevision {get;}
    /// <summary>All and only the declared dependency closure of the profile roots, in logical identity order.</summary>
    public ImmutableArray<GameContentNode> Nodes {get;}
    /// <summary>SHA-256 of a length-prefixed v2 envelope covering profile, producer/revision, roots, outputs, hashes and edges.</summary>
    public string Hash {get;}
    /// <summary>Exact admitted source inventory for built outputs; null for an original inventory.</summary>
    public string? SourceManifestHash {get;}
    /// <summary>Refuses absent coverage, missing/extra nodes, cycles, path collisions and mismatched artifact hashes.
    /// No file reads, parser, importer, game process, settings change or export publication occurs.</summary>
    public static GameContentManifest Create(GameBuildProfile profile,string inventoryOwner,string inventoryRevision,bool declaredComplete,IEnumerable<GameContentNode> inventory,string? sourceManifestHash=null)
    {
        ArgumentNullException.ThrowIfNull(profile);ArgumentNullException.ThrowIfNull(inventory);
        if(sourceManifestHash is not null && (sourceManifestHash.Length!=64||!sourceManifestHash.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')))throw new ArgumentException("Invalid source manifest hash.");
        if(!declaredComplete)throw new ArgumentException("The game has not declared complete inventory coverage.");
        if(!Label(inventoryOwner)||!Label(inventoryRevision))throw new ArgumentException("Inventory owner and captured revision must be bounded nonempty text.");
        var nodes=inventory.Take(1025).ToArray();if(nodes.Length>1024||nodes.Any(n=>n is null))throw new ArgumentException("Inventory exceeds its bound or contains a missing node.");
        if(nodes.Sum(n=>(long)n.Length)>256L*1024*1024 || nodes.Sum(n=>(long)n.Dependencies.Length)>8192)throw new ArgumentException("Inventory byte/edge budget exceeded.");
        // Reuse the profile's actual path/identity/collision contract for every dependency output as well.
        _=GameBuildProfile.Create(profile.Id,profile.Project,profile.Target,nodes.Select(n=>n.Output));
        var map=nodes.ToDictionary(n=>n.Output.InputId,StringComparer.Ordinal);
        foreach(var node in nodes)
        {
            if(!node.DeclaredComplete)throw new ArgumentException($"Dependency coverage is incomplete for {node.Output.InputId}.");
            if(!node.HashMatches())throw new ArgumentException($"Artifact hash does not match pinned bytes for {node.Output.InputId}.");
            foreach(var dep in node.Dependencies)if(!map.ContainsKey(dep))throw new ArgumentException($"Missing required dependency {dep}.");
        }
        foreach(var root in profile.Content)
            if(!map.TryGetValue(root.InputId,out var node)||node.Output!=root)throw new ArgumentException($"Profile root mapping is missing or different: {root.InputId}.");
        var visited=new HashSet<string>(StringComparer.Ordinal);var pending=new Stack<string>(profile.Content.Select(r=>r.InputId));
        while(pending.TryPop(out var id))if(visited.Add(id))foreach(var dep in map[id].Dependencies)pending.Push(dep);
        if(visited.Count!=map.Count)throw new ArgumentException("Inventory contains content outside the declared root closure.");
        var remaining=nodes.ToDictionary(n=>n.Output.InputId,n=>n.Dependencies.Length,StringComparer.Ordinal);
        var dependents=nodes.ToDictionary(n=>n.Output.InputId,_=>new List<string>(),StringComparer.Ordinal);
        foreach(var node in nodes)foreach(var dep in node.Dependencies)dependents[dep].Add(node.Output.InputId);
        var ready=new Queue<string>(remaining.Where(p=>p.Value==0).Select(p=>p.Key));var count=0;
        while(ready.TryDequeue(out var id)){count++;foreach(var parent in dependents[id])if(--remaining[parent]==0)ready.Enqueue(parent);}
        if(count!=nodes.Length)throw new ArgumentException("The declared dependency graph contains a cycle.");
        var ordered=nodes.OrderBy(n=>n.Output.InputId,StringComparer.Ordinal).ToImmutableArray();
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(string value){var bytes=Encoding.UTF8.GetBytes(value);Span<byte> length=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(length,bytes.Length);hash.AppendData(length);hash.AppendData(bytes);}
        Field("game-export-manifest-v2");Field(profile.Id);Field(profile.Project);Field(profile.RuntimeIdentifier);Field(profile.Configuration);Field(profile.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));Field(inventoryOwner);Field(inventoryRevision);
        Field(profile.Content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));foreach(var root in profile.Content)Field(root.InputId);
        Field(ordered.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));foreach(var n in ordered){Field(n.Output.InputId);Field(n.Output.Destination);Field(n.Output.RuntimeFormat);Field(n.Sha256);Field(n.Source is null ? "missing" : n.Source.IsGameOwned ? "game-owned" : "catalog");Field(n.Source?.AssetGuid??"");Field(n.Source?.SourceHash??"");Field(n.Dependencies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));foreach(var dep in n.Dependencies)Field(dep);}
        if(sourceManifestHash is not null){Field("built-from-source-v1");Field(sourceManifestHash);}
        return new(profile,inventoryOwner,inventoryRevision,ordered,Convert.ToHexStringLower(hash.GetHashAndReset()),sourceManifestHash);
    }
    private static bool Label(string? text)=>text is {Length:>0 and <=256}&&!string.IsNullOrWhiteSpace(text)&&!text.Any(char.IsControl);
}
