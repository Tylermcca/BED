using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Import;
namespace Brutal.Authoring.Editor.App;

/// <summary>One explicit project-owned model reimport producer. It starts no watcher, worker, GPU or document edit.
/// The host must give it sole publication authority for this catalog while an operation is in flight.</summary>
public sealed class AssetReimportController:IDisposable
{
    private sealed record Draft(string Path,string Sidecar,AssetRecord Original);
    private readonly string root;private readonly AssetCatalog catalog;private readonly ProjectFrame frame;
    private readonly AssetCatalogPublisher publisher;private readonly ModelPreview? preview;
    private readonly ConditionalWeakTable<AssetSettingsSession,Draft> drafts=new();private readonly SemaphoreSlim busy=new(1,1);private int closed;
    /// <summary>Opens the existing single-owner cache publisher. Catalog, documents and previews remain host-owned.</summary>
    public AssetReimportController(string projectRoot,AssetCatalog catalog,ProjectFrame frame,ModelPreview? preview=null)
    {root=Path.GetFullPath(projectRoot);this.catalog=catalog;this.frame=frame;this.preview=preview;publisher=new(root);}
    /// <summary>Last published storage generation, not proof of live filesystem freshness.</summary>
    public AssetCatalogPublisher.Snapshot? Published=>publisher.Current;
    /// <summary>Latest importer diagnostics, including nonblocking warnings.</summary>
    public IReadOnlyList<Issue> LastIssues {get;private set;}=[];
    /// <summary>Opens a validated draft tied to exact existing sidecar bytes. No asset identity is assigned here.</summary>
    public AssetSettingsSession OpenSettings(string path)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref closed)!=0,this);path=SourcePath(path);
        var text=File.ReadAllText(AssetRecord.SidecarFor(path));var record=AssetRecord.Read(text);
        if(record.Source!=Path.GetFileName(path))throw new InvalidOperationException("The asset record names another source.");
        var session=new AssetSettingsSession(record,new JsonSchemas().Add("asset-settings",AssetReimportSettings.Schema),"asset-settings");
        drafts.Add(session,new(path,text,record));return session;
    }
    /// <summary>Imports and publishes one same-GUID replacement using the complete immutable catalog generation.
    /// Storage publishes before the delayed sidecar save; owned rollback compensates sidecar failure, not a cross-file atomic transaction.</summary>
    public bool TryReimport(AssetSettingsSession settings,int importerVersion,out string why,CancellationToken cancellation=default)
    {
        why="";if(Volatile.Read(ref closed)!=0){why="The reimport controller is closed.";return false;}
        if(!busy.Wait(0)){why="A reimport is already in flight.";return false;}
        AssetCatalogPublisher.Staged? stage=null;bool published=false,sidecarSaved=false;Draft? draft=null;AssetRecord? updated=null;
        MaterialTextureReimport? materialPreparation=null;
        AssetCatalog.Selection? selected=preview?.Selection;var followPreview=selected?.Admission==catalog.Admission;
        try
        {
            if(!drafts.TryGetValue(settings,out draft))throw new InvalidOperationException("Open this settings draft through its project controller.");
            if(importerVersion<1)throw new ArgumentOutOfRangeException(nameof(importerVersion));
            var revision=settings.Revision;var candidate=settings.Candidate;var options=AssetReimportSettings.Parse(candidate.Settings,frame);
            void Check()
            {
                cancellation.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(Volatile.Read(ref closed)!=0,this);
                if(settings.Revision!=revision||File.ReadAllText(AssetRecord.SidecarFor(draft.Path))!=draft.Sidecar)
                    throw new InvalidOperationException("The asset/settings predecessor changed; reopen the draft.");
            }
            Check();if(followPreview&&selected?.AssetGuid==candidate.Guid)preview!.Invalidate("Reimporting...");
            var before=catalog.CurrentInputs();var original=before.SingleOrDefault(i=>i.Guid==candidate.Guid)
                ??throw new InvalidOperationException("This asset is not in the admitted complete generation.");
            var source=File.ReadAllBytes(draft.Path);updated=candidate.PrepareReimport(draft.Path,importerVersion);
            if(Hash(source)!=updated.SourceHash)throw new InvalidOperationException("Source changed during preparation.");
            var extension=Path.GetExtension(draft.Path).ToLowerInvariant();
            var imported=extension==".obj"&&candidate.Importer.Equals("obj",StringComparison.OrdinalIgnoreCase)?ObjImporter.Import(draft.Path,frame)
                :extension is ".gltf" or ".glb"&&candidate.Importer.Equals("gltf",StringComparison.OrdinalIgnoreCase)?GltfImporter.Import(draft.Path,frame)
                :throw new InvalidOperationException("This controller supports explicitly declared OBJ or glTF importers.");
            LastIssues=Array.AsReadOnly(imported.Issues.ToArray());
            if(imported.Outcome==ImportOutcome.Refused||imported.Asset is null)throw new InvalidOperationException(string.Join(" ",imported.Issues.Select(i=>i.Message)));
            materialPreparation=MaterialTextureReimport.Prepare(draft.Path,updated,imported.Asset);
            var bindings=materialPreparation.Bindings;
            var sourceFiles=imported.ExternalReads.Select(r=>new AssetCatalog.SourceFile(r.RelativePath,r.SourceHash)).ToArray();
            var derived=ModelAssetGeneration.Encode(imported.Asset,options.Transform(frame),extension==".obj"?MeshUv0Origin.ObjBottomLeft:MeshUv0Origin.TopLeft,bindings);
            var textures=bindings.Select(b=>b.Texture).OfType<MaterialTextureAdmission.Texture>().Distinct().ToArray();
            if(textures.GroupBy(t=>t.Input.Guid,StringComparer.Ordinal).Any(g=>g.Count()>1))throw new InvalidOperationException("Multiple texture files use the same asset GUID.");
            var textureIds=textures.Select(t=>t.Input.Guid).ToHashSet(StringComparer.Ordinal);
            var next=before.Where(i=>i.Guid!=candidate.Guid&&!textureIds.Contains(i.Guid)).Concat(textures.Select(t=>t.Input))
                .Append(original.WithReimportedContent(updated,source,derived,derived.AsSpan(0,8).SequenceEqual("BEDMESH2"u8)?"BEDMESH2":"BEDMESH1",sourceFiles,textureIds)).ToArray();
            using var validated=new AssetCatalog();if(!validated.Publish(validated.BeginRefresh(),next,out why))throw new InvalidOperationException(why);
            if(validated.Resolve(candidate.Guid).State!=AssetCatalog.State.Valid)throw new InvalidOperationException(validated.Resolve(candidate.Guid).Reason);
            var artifacts=new Dictionary<string,byte[]>();
            foreach(var input in next)
            {
                var resolution=validated.Resolve(input.Guid);if(resolution.Current is not { } lease)continue;
                artifacts.Add(input.Guid+".bin",TextureAssetGeneration.StoredArtifact(lease));
            }
            var identity=Hash(JsonSerializer.SerializeToUtf8Bytes(next.OrderBy(i=>i.Guid,StringComparer.Ordinal).Select(i=>new{i.Guid,validated.Resolve(i.Guid).ContentRevision,validated.Resolve(i.Guid).PolicyRevision})));
            stage=publisher.Prepare(identity,artifacts);Check();
            foreach(var read in sourceFiles)
            {
                cancellation.ThrowIfCancellationRequested();
                var resource=SourcePath(Path.Combine(Path.GetDirectoryName(draft.Path)!,read.RelativePath.Replace('/',Path.DirectorySeparatorChar)));
                if(Hash(File.ReadAllBytes(resource))!=read.SourceHash)throw new InvalidOperationException("External resource changed during reimport: "+read.RelativePath);
            }
            if(Hash(File.ReadAllBytes(draft.Path))!=updated.SourceHash||!catalog.CurrentInputs().SequenceEqual(before))
                throw new InvalidOperationException("Source or complete catalog generation changed during reimport.");
            Check();
            foreach(var texture in textures)texture.Revalidate();
            if(!publisher.Publish(stage,out why))throw new InvalidOperationException(why);published=true;
            updated.SaveReimport(draft.Path,draft.Sidecar);sidecarSaved=true;
            if(!catalog.Publish(catalog.BeginRefresh(),next,out why))throw new InvalidOperationException(why);
            materialPreparation.Commit();
            if(followPreview&&selected?.AssetGuid==candidate.Guid&&preview?.Selection==selected&&catalog.Select(candidate.Guid) is { } current)preview.Select(current);
            why="";return true;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException or JsonException or OperationCanceledException)
        {
            var undo=new List<string>();
            if(sidecarSaved&&draft is not null&&updated is not null)
            {try{draft.Original.SaveReimport(draft.Path,updated.Write());}catch(Exception restore) when(restore is IOException or UnauthorizedAccessException or InvalidOperationException){undo.Add(restore.Message);}}
            if(published&&stage is not null&&!publisher.Rollback(stage,out var failure))undo.Add(failure);
            else if(!published&&stage is not null)publisher.Cancel(stage);
            if(materialPreparation is not null&&!materialPreparation.Rollback(out var textureFailure))undo.Add(textureFailure);
            why=undo.Count==0?"Reimport failed: "+e.Message:"Reimport failed and could not be undone: "+string.Join(" ",undo);
            if(followPreview&&draft is not null&&selected?.AssetGuid==draft.Original.Guid&&preview?.Selection==selected)preview.Invalidate(why);return false;
        }
        finally{if(Volatile.Read(ref closed)!=0)publisher.Dispose();busy.Release();}
    }
    private string SourcePath(string path)
    {
        var full=Path.GetFullPath(path,root);var relative=Path.GetRelativePath(root,full);
        if(Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new ArgumentException("Source must be inside this admitted project.",nameof(path));
        for(var p=full;p is not null&&p.Length>=root.Length;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked source paths are not admitted here.");
        return full;
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static MeshData Flatten(ImportedAsset asset,Matrix4x4 transform,MeshUv0Origin origin)
    {
        if(asset.Meshes.Count==0)throw new InvalidOperationException("No mesh geometry was imported.");
        var normals=asset.Meshes[0].Normals.Length>0;var uv=asset.Meshes[0].Uv0.Length>0;
        if(asset.Meshes.Any(m=>(m.Normals.Length>0)!=normals||(m.Uv0.Length>0)!=uv))throw new InvalidOperationException("Mixed missing/present normal or UV streams cannot be encoded without losing attributes.");
        var vertices=asset.Meshes.Sum(m=>(long)m.Positions.Length);var count=asset.Meshes.Sum(m=>(long)m.Triangles.Length);
        if(vertices>1000000||count>3000000)throw new InvalidOperationException("Derived model exceeds its bounded counts.");
        var positions=new List<Vector3>();var ns=new List<Vector3>();var us=new List<Vector2>();var indices=new List<int>();
        foreach(var m in asset.Meshes)
        {
            var offset=positions.Count;positions.AddRange(m.Positions.Select(p=>Vector3.Transform(p,transform)));
            ns.AddRange(m.Normals.Select(n=>Vector3.Normalize(Vector3.TransformNormal(n,transform))));us.AddRange(m.Uv0);indices.AddRange(m.Triangles.Select(i=>checked(i+offset)));
        }
        return new(positions.ToArray(),indices.ToArray()){Normals=normals?ns.ToArray():null,Uv0=uv?us.ToArray():null,Uv0Origin=origin};
    }
    /// <summary>Closes publication admission; an in-flight operation observes closure before its commit point.</summary>
    public void Dispose(){Interlocked.Exchange(ref closed,1);if(busy.Wait(0)){try{publisher.Dispose();}finally{busy.Release();}}}
}
