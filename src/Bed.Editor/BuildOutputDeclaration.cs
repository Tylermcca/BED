using System.Collections.Immutable;
using System.Security.Cryptography;
namespace Bed.Editor;

/// <summary>Declared built artifact identity and complete output dependency edges; no binary is pre-pinned.</summary>
public sealed record BuildOutputItem(GameContentMapping Mapping, ImmutableArray<string> Dependencies);
/// <summary>Explicit notice artifact and the source receipt attributions it covers. Coverage is declared, not legal advice.</summary>
public sealed record BuildOutputNotice(GameContentNode Artifact, ImmutableArray<ExportOriginNotice> Covers);
/// <summary>Admitted immutable source-to-output plan. Construction requires exact source check and origin receipts.</summary>
public sealed class BuildOutputDeclaration
{
    private readonly ImmutableArray<BuildOutputItem> items;
    private readonly ImmutableArray<GameContentNode> notices;
    /// <summary>Validates source admission and copies the output graph and explicit notice coverage.</summary>
    public BuildOutputDeclaration(GameContentManifest source,IEnumerable<string> requiredChecks,IEnumerable<ExportCheckReceipt> checks,
        ExportOriginReceipt origin,GameBuildProfile outputProfile,IEnumerable<BuildOutputItem> outputs,IEnumerable<BuildOutputNotice> requiredNotices)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(outputProfile);
        ArgumentNullException.ThrowIfNull(outputs);ArgumentNullException.ThrowIfNull(requiredNotices);
        var gate=ExportGate.Evaluate(source,requiredChecks,checks,origin);
        if(!gate.CompleteExport)throw new ArgumentException(string.Join(" ",gate.Reasons));
        Source=source;Profile=outputProfile;
        var rows=outputs.Take(1025).ToArray();
        if(rows.Length>1024||rows.Any(r=>r is null||r.Dependencies.IsDefault))throw new ArgumentException("Invalid output declaration.");
        items=rows.Select(r=>r with {Dependencies=r.Dependencies.ToImmutableArray()}).ToImmutableArray();
        var declared=requiredNotices.Take(65).ToArray();
        if(declared.Length>64||declared.Any(n=>n is null||n.Artifact is null||n.Covers.IsDefault))throw new ArgumentException("Invalid notice declaration.");
        if(!declared.SelectMany(n=>n.Covers).ToHashSet().SetEquals(origin.Notices))throw new ArgumentException("Notice coverage differs from the source origin receipt.");
        notices=declared.Select(n=>n.Artifact).ToImmutableArray();
        // Exercise the existing graph, mapping and notice contracts with empty placeholders, never invented output hashes.
        var emptyHash=Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>()));
        var graph=GameContentManifest.Create(Profile,source.InventoryOwner,source.InventoryRevision,true,
            items.Select(i=>new GameContentNode(i.Mapping,[],emptyHash,i.Dependencies,true,ExportSource.BuiltFrom(source.Hash))),source.Hash);
        _=ExportStaging.Create(Guid.NewGuid(),graph,null,notices,true);
        Paths=items.Select(i=>i.Mapping.Destination).Concat(notices.Select(n=>n.Output.Destination)).Order(StringComparer.Ordinal).ToImmutableArray();
    }
    /// <summary>Admitted source inputs, including the hash to which admission receipts were bound.</summary>
    public GameContentManifest Source {get;}
    /// <summary>Exact declared output profile.</summary>
    public GameBuildProfile Profile {get;}
    /// <summary>All and only allowed output paths, including mandatory notices.</summary>
    public ImmutableArray<string> Paths {get;}
    /// <summary>Pins a complete captured post-build inventory, then applies unchanged EX04 byte checks.</summary>
    public (GameContentManifest Manifest,ExportStaging Staging) Pin(Guid attempt,IReadOnlyDictionary<string,byte[]> captured,string? priorGoodManifestHash=null)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if(captured.Count!=Paths.Length||!Paths.All(captured.ContainsKey))throw new ArgumentException("Build output inventory differs from the declaration.");
        var nodes=items.Select(i=>new GameContentNode(i.Mapping,captured[i.Mapping.Destination],
            Convert.ToHexStringLower(SHA256.HashData(captured[i.Mapping.Destination])),i.Dependencies,true,ExportSource.BuiltFrom(Source.Hash))).ToArray();
        var manifest=GameContentManifest.Create(Profile,Source.InventoryOwner,Source.InventoryRevision,true,nodes,Source.Hash);
        var staging=ExportStaging.Create(attempt,manifest,priorGoodManifestHash,notices,true);
        foreach(var path in Paths)if(!staging.Record(attempt,path,captured[path],out var why))throw new ArgumentException(why);
        return(manifest,staging);
    }
}
