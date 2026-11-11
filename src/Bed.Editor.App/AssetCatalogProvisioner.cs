using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Import;
namespace Bed.Editor.App;

/// <summary>Startup derivation from declared sidecars; publishes only the derived cache. Never saves source or sidecars.</summary>
public sealed class AssetCatalogProvisioner : IAssetCatalogProvisioner
{
    // Reopening a project in this host may retain at most 64 MiB each of source and derived bytes.
    // Only immutable producer inputs survive; every admission still rehashes the complete assets tree.
    private Reusable? reusable;
    private sealed record Reusable(string Root,string ProjectRoot,HashSet<string> Files,Dictionary<string,string> Pins,
        AssetCatalog.Input[] Inputs,string[] Problems,string[] Warnings);

    /// <inheritdoc />
    public ProjectAssetCatalog? Open(ProjectPaths project, string? assetsFolder = null)
    {
        using var preparation=StartupPreparationTiming.Create();
        var projectRoot=Path.GetFullPath(project.Root??project.Folder??Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Data)))!);
        var root = Path.GetFullPath(assetsFolder ?? Path.Combine(projectRoot, "assets"));
        if (!Directory.Exists(root))
        {
            // A missing assets folder is optional; no source folder is created to publish an empty project.
            if(Directory.Exists(projectRoot))
            {
                using var empty=new AssetCatalog();
                empty.Publish(empty.BeginRefresh(),[],out _);
                PublishStartup(projectRoot,empty,()=>{if(Directory.Exists(root))throw new InvalidOperationException("Assets appeared during startup; reopen the project.");});
            }
            return null;
        }
        // Reject links before the browser's recursive scan. This is a bounded admission, not a watcher.
        var files = Files(root);
        var browser = new AssetBrowser(root, ".obj", ".gltf", ".glb", ".wav", ".ogg", ".png");
        var catalog = new AssetCatalog();
        var inputs = new List<AssetCatalog.Input>();
        var problems = new List<string>();
        var warnings = new List<string>();
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] Read(string path)
        {
            path = Path.GetFullPath(path);
            if (!files.Contains(path)) throw new InvalidOperationException("That file is outside this project's assets.");
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidOperationException("Asset exceeds the 64 MiB startup bound.");
            var bytes = File.ReadAllBytes(path); pins[path] = Hash(bytes); return bytes;
        }
        bool ReuseInputs()
        {
            var previous=reusable;
            if(previous is null || !string.Equals(previous.Root,root,StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previous.ProjectRoot,projectRoot,StringComparison.OrdinalIgnoreCase) || !previous.Files.SetEquals(files))return false;
            foreach(var pin in previous.Pins)if(Hash(Read(pin.Key))!=pin.Value)return false;
            inputs.AddRange(previous.Inputs);problems.AddRange(previous.Problems);warnings.AddRange(previous.Warnings);
            return true;
        }
        try
        {
            if(!ReuseInputs())
            {
                foreach (var sidecar in files.Where(p => p.EndsWith(AssetRecord.Suffix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                {
                    AssetRecord? record = null;
                    byte[]? pinnedSource = null;
                    try
                    {
                        record = AssetRecord.Read(System.Text.Encoding.UTF8.GetString(Read(sidecar)));
                        var path = sidecar[..^AssetRecord.Suffix.Length];
                        if (record.Source != Path.GetFileName(path)) throw new InvalidOperationException("The asset's import file names a different source file.");
                        var source = pinnedSource = Read(path);
                        if (Hash(source) != record.SourceHash) throw new InvalidOperationException("Source changed since import; reimport it first.");
                        var frame = ProjectFrame.Gltf;
                        var extension = Path.GetExtension(path).ToLowerInvariant();
                        if(extension is ".wav" or ".ogg")
                        {
                            if(record.Importer != extension[1..]) throw new InvalidOperationException("Sound importer must match WAV or OGG.");
                            if(record.PrepareReimport(path,record.ImporterVersion).Write()!=record.Write())throw new InvalidOperationException("The sound file changed since it was imported; reimport it first.");
                            if(source.Length>WavePreviewDecoder.MaxEncodedBytes)throw new InvalidOperationException("Sound exceeds the 32 MiB preview limit.");
                            inputs.Add(new(record,source,source,[],true,"sound-preview-v1",record.Write()));continue;
                        }
                        if(extension is ".png" or ".jpg" or ".jpeg" or ".tga")
                        {
                            if(record.PrepareReimport(path,record.ImporterVersion).Write()!=record.Write())throw new InvalidOperationException("The texture file changed since it was imported; reimport it first.");
                            if(!TextureAssetGeneration.TryCreateSavedInput(record,source,extension,[],true,"BEDTEX1",record.Write(),out var texture,out var textureWhy))
                                throw new InvalidOperationException(textureWhy);
                            inputs.Add(texture!);continue;
                        }
                        var options = AssetReimportSettings.Parse(record.Settings, frame);
                        var result = extension == ".obj" && record.Importer == "obj" ? ObjImporter.Import(path, frame)
                            : extension is ".gltf" or ".glb" && record.Importer == "gltf" ? GltfImporter.Import(path, frame)
                            : throw new InvalidOperationException("Startup supports declared OBJ or glTF importers only.");
                        if (result.Outcome == ImportOutcome.Refused || result.Asset is null)
                            throw new InvalidOperationException(string.Join(" ", result.Issues.Select(i => i.Message)));
                        foreach (var issue in result.Issues) warnings.Add(Path.GetRelativePath(root, path) + ": " + issue.Message);
                        var bindings=MaterialTextureAdmission.PrepareForStartup(path,result.Asset,out var materialWarnings);
                        warnings.AddRange(materialWarnings.Select(w=>Path.GetRelativePath(root,path)+": "+w));
                        var reads = result.ExternalReads.Select(r => new AssetCatalog.SourceFile(r.RelativePath, r.SourceHash)).ToArray();
                        foreach (var read in reads)
                            if (Hash(Read(Path.Combine(Path.GetDirectoryName(path)!, read.RelativePath))) != read.SourceHash)
                                throw new InvalidOperationException("External resource changed during import.");
                        foreach(var texture in bindings.Select(b=>b.Texture).OfType<MaterialTextureAdmission.Texture>().Distinct())
                        {
                            texture.Revalidate();Read(texture.Path);Read(AssetRecord.SidecarFor(texture.Path));
                        }
                        var derived=ModelAssetGeneration.Encode(result.Asset,options.Transform(frame),extension==".obj"?MeshUv0Origin.ObjBottomLeft:MeshUv0Origin.TopLeft,bindings);
                        var dependencies=bindings.Select(b=>b.Texture?.Input.Guid).OfType<string>().Distinct(StringComparer.Ordinal);
                        inputs.Add(new(record, source, derived, dependencies, true, derived.AsSpan(0,8).SequenceEqual("BEDMESH2"u8)?"BEDMESH2":"BEDMESH1", record.Write(), sourceFiles: reads));
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or DocumentFormatException or OverflowException)
                    {
                        var reason = Path.GetRelativePath(root, sidecar) + ": " + e.Message;
                        problems.Add(reason);
                        if (record is not null) inputs.Add(new(record, pinnedSource, null, [], false, "BEDMESH1", record.Write(), AssetCatalog.State.Failed, reason));
                    }
                }
            }
            void VerifyInputs()
            {
                if (!Files(root).SetEquals(files) || pins.Any(p => Hash(File.ReadAllBytes(p.Key)) != p.Value))
                    throw new InvalidOperationException("Assets changed during startup; reopen the project.");
            }
            VerifyInputs();
            if (!catalog.Publish(catalog.BeginRefresh(), inputs, out var why)) throw new InvalidOperationException(why);
            // Standalone inspection callers may not supply an admitted existing project root.
            // Actual startup always does; never create a project folder as a side effect of inspection.
            if(Directory.Exists(projectRoot))PublishStartup(projectRoot,catalog,VerifyInputs);
            // No new on-disk format. A fresh BED process performs its first derivation as before.
            // Hash even unreferenced files: additions or changed licence/material observations invalidate reuse.
            if(inputs.All(i=>catalog.Resolve(i.Guid).Current is not null)
                && files.Sum(p=>new FileInfo(p).Length)<=64L*1024*1024
                && inputs.Sum(i=>(long)catalog.Resolve(i.Guid).Current!.Length)<=64L*1024*1024)
            {
                foreach(var file in files)if(!pins.ContainsKey(file))Read(file);
                VerifyInputs();
                reusable=new(root,projectRoot,files,new(pins,StringComparer.OrdinalIgnoreCase),inputs.ToArray(),problems.ToArray(),warnings.ToArray());
            }
            else reusable=null;
            return new(browser, catalog, catalog, problems.AsReadOnly()) { Warnings = warnings.AsReadOnly(), AudioPreview = new OpenAlAssetAudioPreview() };
        }
        catch { catalog.Dispose(); throw; }
    }
    private static void PublishStartup(string projectRoot,AssetCatalog catalog,Action verify)
    {
        var inputs=catalog.CurrentInputs();
        var artifacts=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var guid in inputs.Select(i=>i.Guid).Distinct(StringComparer.Ordinal))
        {
            if(catalog.Resolve(guid).Current is not {} lease)continue;
            artifacts.Add(guid+".bin",TextureAssetGeneration.StoredArtifact(lease));
        }
        // Same GUID/content/policy revision and artifact naming as the explicit AS11 reimport publisher.
        var identity=Hash(JsonSerializer.SerializeToUtf8Bytes(inputs.OrderBy(i=>i.Guid,StringComparer.Ordinal)
            .Select(i=>new{i.Guid,catalog.Resolve(i.Guid).ContentRevision,catalog.Resolve(i.Guid).PolicyRevision})));
        using var publisher=new AssetCatalogPublisher(projectRoot);
        verify();
        var current=publisher.Current;
        if(current?.Revision==identity&&current.Artifacts.Count==artifacts.Count
            &&current.Artifacts.All(a=>artifacts.TryGetValue(a.Name,out var bytes)&&a.Length==bytes.LongLength&&a.Sha256==Hash(bytes)))return;
        var stage=publisher.Prepare(identity,artifacts);
        verify();
        if(!publisher.Publish(stage,out var why))throw new InvalidOperationException(why);
    }
    private static HashSet<string> Files(string root)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root); var count = 0;
        while (pending.TryPop(out var folder))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked folders (shortcuts to elsewhere) can't hold assets.");
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                if (++count > 4096) throw new InvalidOperationException("Assets exceed the 4096-entry startup bound.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked files (shortcuts to elsewhere) can't be assets.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path); else files.Add(Path.GetFullPath(path));
            }
        }
        return files;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
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
}
