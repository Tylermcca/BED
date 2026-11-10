using System.Collections.ObjectModel;
using System.Text;
using Brutal.Authoring;
using Brutal.Authoring.Runtime;
namespace SceneStarter;

/// <summary>Game-owned saved-level consumer shared by ordinary Play and copied project-layout exports.
/// It reads no drafts, derives nothing and has no editor, importer, protocol or rendering dependency.</summary>
public sealed class SavedScene
{
    private SavedScene(SceneRuntimeReadResult level,ProjectAssets assets,Dictionary<string,BedMesh> meshes)
    {Level=level;Assets=assets;Meshes=new ReadOnlyDictionary<string,BedMesh>(meshes);}
    public SceneRuntimeReadResult Level {get;}
    public ProjectAssets Assets {get;}
    public IReadOnlyDictionary<string,BedMesh> Meshes {get;}
    public static SavedScene Open(string projectRoot)
    {
        var root=Path.GetFullPath(projectRoot);var path=Path.Combine(root,"data","level.json");
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
