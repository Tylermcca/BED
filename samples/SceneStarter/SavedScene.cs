using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Text;
using Bed;
using Bed.Runtime;
namespace SceneStarter;

/// <summary>Game-owned saved-level consumer shared by ordinary Play and copied project-layout exports.
/// It reads no drafts, derives nothing and has no editor, importer, protocol or rendering dependency.</summary>
public sealed class SavedScene
{
    private SavedScene(SceneRuntimeReadResult level,ProjectAssets assets,Dictionary<string,BedMesh> meshes,Scene2RuntimeReadResult? level2=null,Dictionary<string,BedTexture>? textures=null)
    {Level=level;Assets=assets;Meshes=new ReadOnlyDictionary<string,BedMesh>(meshes);Level2=level2;Textures=new ReadOnlyDictionary<string,BedTexture>(textures??new());}
    public SceneRuntimeReadResult Level {get;}
    /// <summary>Complete flattened v2 metadata when a checked runtime descriptor is present.</summary>
    public Scene2RuntimeReadResult? Level2 {get;}
    /// <summary>Owned published sprite and model-material textures indexed by exact GUID.</summary>
    public IReadOnlyDictionary<string,BedTexture> Textures {get;}
    public ProjectAssets Assets {get;}
    public IReadOnlyDictionary<string,BedMesh> Meshes {get;}
    public static SavedScene Open(string projectRoot)
    {
        var root=Path.GetFullPath(projectRoot);
        var descriptor=Path.Combine(root,Scene2RuntimeInput.Descriptor);var config=Path.Combine(root,".bed","project.json");
        var hasConfig=File.Exists(config);
        var capability=hasConfig?Scene2BuildSettings.ReadProject(new UTF8Encoding(false,true).GetString(Scene2RuntimeInput.Read(root,".bed/project.json",Scene2BuildSettings.MaxProjectBytes))):null;
        if(capability is not null||(!hasConfig&&File.Exists(descriptor)))
        {
            if(!File.Exists(descriptor))throw new InvalidDataException("Build the saved scene-v2 before Play: its runtime descriptor is missing.");
            var prepared=Scene2RuntimeInput.Open(root);
            var legacy=new SceneRuntimeReadResult(new Scene(prepared.Level.Scene.Objects.Select(o=>o.Object).ToImmutableArray()),true,[]);
            return new(legacy,prepared.Assets,prepared.Meshes,prepared.Level,prepared.Textures);
        }
        var path=Path.Combine(root,"data","level.json");
        for(var at=path;at is not null;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked saved level paths are not supported.");
        byte[] bytes;
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
        {if(file.Length>1024*1024)throw new InvalidOperationException("The saved level exceeds one MiB.");bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);}
        var assets=ProjectAssets.Open(root);var level=SceneRuntimeReader.Read(new UTF8Encoding(false,true).GetString(bytes),assetExists:assets.ContainsAsset);
        if(level.AssetIssues.Count!=0)throw new InvalidOperationException("The saved level references an asset outside its published generation.");
        var meshes=level.Scene.Objects.Select(item=>item.Asset).OfType<string>().Distinct(StringComparer.Ordinal)
            .ToDictionary(id=>id,assets.ReadMesh,StringComparer.Ordinal);
        foreach(var mesh in meshes.Values)
            foreach(var part in mesh.Parts)
                if(part.Material?.Texture is {} texture)_=assets.ReadTexture(texture);
        return new(level,assets,meshes);
    }
}
