using System.Text;
using System.Text.Json;
using Bed;
namespace SceneStarter;

/// <summary>The one sample-owned reader used by Play and copied export payloads.</summary>
public static class ScenePayload
{
    public const string Format="scene-starter.scene.v1";
    public const string AssetFormat="scene-starter.scene.v2";
    public static SceneRuntimeReadResult Read(byte[] bytes,string? format=null)
    {
        if(bytes.Length>96*1024*1024)throw new InvalidOperationException("The sample input exceeds its payload budget.");
        if(format is null)
        {
            using var json=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});
            format=json.RootElement.TryGetProperty("scene",out _)?AssetFormat:Format;
        }
        var loaded=format switch
        {
            AssetFormat=>PinnedScenePayload.Read(bytes),
            Format when bytes.Length<=1024*1024=>SceneRuntimeReader.Read(new UTF8Encoding(false,true).GetString(bytes),assetExists:_=>false),
            _=>throw new InvalidOperationException("Unsupported SceneStarter payload format or size.")
        };
        if(loaded.AssetIssues.Count!=0)throw new InvalidOperationException("This sample needs every scene asset to be pinned.");
        return loaded;
    }
}
