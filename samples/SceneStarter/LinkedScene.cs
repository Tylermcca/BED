using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bed;

namespace SceneStarter;

/// <summary>The public sample's headless version-one stdio peer. It consumes immutable scene text and catalog-pinned asset bytes;
/// it has no authoring, editor, process-launch or UI dependency.</summary>
public static class LinkedScene
{
    public const string Format=ScenePayload.Format;
    public const string AssetFormat=ScenePayload.AssetFormat;
    private const int MaxLine=128*1024*1024,MaxPayload=96*1024*1024;
    private sealed record Identity(string Session,long Revision,string Hash);

    public static int Run(TextReader input,TextWriter output,TextWriter error)
    {
        Identity? identity=null;var ready=false;
        try
        {
            using var hello=Read(input);var root=hello.RootElement;
            identity=Id(root);
            if(root.GetProperty("kind").GetString()!="hello") throw new InvalidOperationException("Expected linked Play hello.");
            var required=root.GetProperty("required").GetInt32();var offered=root.GetProperty("offered").GetInt32();
            if(required!=3 || (offered&3)!=3) throw new InvalidOperationException("Snapshot and Stop are required; no other required capability is supported.");
            var format=root.GetProperty("format").GetString();
            if(root.GetProperty("choice").GetString()!="Saved" || format is not (Format or AssetFormat))
                throw new InvalidOperationException("This sample accepts saved SceneStarter v1 or v2 input only.");
            var name=root.GetProperty("document").GetString();
            if(string.IsNullOrWhiteSpace(name) || name.Length>1024 || name.Any(char.IsControl))
                throw new InvalidOperationException("A logical scene document identity is required.");
            var bytes=root.GetProperty("payload").GetBytesFromBase64();
            if(bytes.Length>MaxPayload || (format==Format && bytes.Length>1024*1024)) throw new InvalidOperationException("The sample input exceeds its payload budget.");
            if(Hash(name,identity.Revision,bytes,format)!=identity.Hash) throw new InvalidOperationException("The scene snapshot hash differs from its envelope.");
            var loaded=ScenePayload.Read(bytes,format);
            if(loaded.AssetIssues.Count!=0) throw new InvalidOperationException("This sample needs every scene asset to be pinned.");
            // Readiness follows the actual core parse and graph validation, never mere pipe receipt.
            Write(output,new {kind="ready",version=1,session=identity.Session,revision=identity.Revision,hash=identity.Hash,supported=3});
            ready=true;
            using var stop=Read(input);
            if(stop.RootElement.GetProperty("kind").GetString()!="stop" || Id(stop.RootElement)!=identity)
                throw new InvalidOperationException("Stop must identify this exact loaded scene and session.");
            GC.KeepAlive(loaded);
            Write(output,new {kind="stopped",version=1,session=identity.Session,revision=identity.Revision,hash=identity.Hash});
            return 0;
        }
        catch(Exception exception) when(exception is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or DecoderFallbackException or IOException or DocumentFormatException or InvalidDocumentException)
        {
            if(identity is not null)
                Write(output,new {kind="failure",version=1,session=identity.Session,revision=identity.Revision,hash=identity.Hash,
                    code=ready ? "RuntimeFailed" : "LoadFailed",message="SceneStarter linked Play refused its input or stop request."});
            error.WriteLine("SceneStarter linked Play refused: "+exception.GetType().Name);
            return 3;
        }
    }

    private static JsonDocument Read(TextReader input)
    {
        var line=new StringBuilder();
        while(true)
        {
            var next=input.Read();
            if(next<0) throw new IOException("Linked Play input ended before the session finished.");
            if(next=='\n') break;
            if(line.Length>=MaxLine) throw new InvalidOperationException("Linked Play line exceeds the sample budget.");
            line.Append((char)next);
        }
        var document=JsonDocument.Parse(line.ToString(),new JsonDocumentOptions {MaxDepth=16});
        if(document.RootElement.ValueKind!=JsonValueKind.Object) {document.Dispose();throw new InvalidOperationException("Expected one protocol object.");}
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in document.RootElement.EnumerateObject())
            if(!names.Add(property.Name)) {document.Dispose();throw new InvalidOperationException("Repeated protocol field.");}
        return document;
    }
    private static Identity Id(JsonElement root)
    {
        if(root.GetProperty("version").GetInt32()!=1) throw new InvalidOperationException("Protocol version one is required.");
        var session=root.GetProperty("session").GetString();var revision=root.GetProperty("revision").GetInt64();
        var hash=root.GetProperty("hash").GetString();
        if(!Guid.TryParseExact(session,"N",out var id) || id==Guid.Empty || revision<0 || hash is not {Length:64}
            || hash.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException("Invalid linked Play identity.");
        return new(id.ToString("N"),revision,hash);
    }
    private static string Hash(string document,long revision,byte[] payload,string format)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(ReadOnlySpan<byte> bytes)
        {Span<byte> length=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(length,bytes.Length);hash.AppendData(length);hash.AppendData(bytes);}
        Field("linked-play-input-v1"u8);Field(Encoding.UTF8.GetBytes(document));Field("saved"u8);Field(Encoding.UTF8.GetBytes(format));
        Span<byte> value=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(value,revision);Field(value);Field(payload);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static void Write(TextWriter output,object message) {output.WriteLine(JsonSerializer.Serialize(message));output.Flush();}
}
