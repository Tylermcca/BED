using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brutal.Authoring;

namespace SceneStarter;

/// <summary>Sample-owned immutable catalog bytes; no filesystem access, importer or Editor dependency.</summary>
internal static class PinnedScenePayload
{
    internal const int MaxAssets=256;
    internal const long MaxDerived=64L*1024*1024;
    internal static SceneRuntimeReadResult Read(byte[] payload)
    {
        using var json=JsonDocument.Parse(payload,new JsonDocumentOptions {MaxDepth=16});
        var root=json.RootElement;Only(root,"scene","assets");
        var text=root.GetProperty("scene").GetString()??throw new InvalidOperationException("A saved scene is required.");
        if(Encoding.UTF8.GetByteCount(text)>1024*1024) throw new InvalidOperationException("Scene exceeds one MiB.");
        var list=root.GetProperty("assets");
        if(list.ValueKind!=JsonValueKind.Array || list.GetArrayLength()>MaxAssets) throw new InvalidOperationException("Too many asset pins.");
        var pinned=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
        foreach(var pin in list.EnumerateArray())
        {
            Only(pin,"guid","sourceHash","contentRevision","policyRevision","derivedHash","derived");
            var id=Hex(pin,"guid",32);
            // Source/revision hashes are the admitted host's metadata. Derived bytes are independently checked here;
            // this console peer does not invent source acquisition or authenticate a hostile producer.
            Hex(pin,"sourceHash",64);Hex(pin,"contentRevision",64);Hex(pin,"policyRevision",64);
            var expected=Hex(pin,"derivedHash",64);
            var encoded=pin.GetProperty("derived").GetString()??throw new InvalidOperationException("Missing asset bytes.");
            if(encoded.Length>((MaxDerived-total+2)/3)*4) throw new InvalidOperationException("Derived assets exceed 64 MiB.");
            var bytes=Convert.FromBase64String(encoded);total+=bytes.LongLength;
            if(total>MaxDerived || Convert.ToHexStringLower(SHA256.HashData(bytes))!=expected)
                throw new InvalidOperationException("Derived asset hash or budget differs.");
            if(!pinned.TryAdd(id,bytes)) throw new InvalidOperationException("Repeated asset GUID.");
        }
        var result=SceneRuntimeReader.Read(text,assetExists:pinned.ContainsKey);
        if(result.AssetIssues.Count!=0) throw new InvalidOperationException("Scene references an unpinned asset.");
        GC.KeepAlive(pinned);return result;
    }
    private static string Hex(JsonElement item,string property,int length)
    {
        var value=item.GetProperty(property).GetString();
        if(value is null || value.Length!=length || value.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException("Invalid asset pin identity or hash.");
        return value;
    }
    private static void Only(JsonElement item,params string[] fields)
    {
        if(item.ValueKind!=JsonValueKind.Object) throw new InvalidOperationException("Expected one asset payload object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in item.EnumerateObject())
            if(!fields.Contains(property.Name,StringComparer.Ordinal) || !seen.Add(property.Name)) throw new InvalidOperationException("Unknown or repeated asset payload field.");
        if(seen.Count!=fields.Length) throw new InvalidOperationException("Missing asset payload field.");
    }
}
