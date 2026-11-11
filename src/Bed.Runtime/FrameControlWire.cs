using System.Buffers.Binary;
using System.Text.Json;
namespace Bed.FrameTransport;

// Private shared transport source, compiled into the runtime and app. No public wire API.
internal static class FrameControlWire
{
    internal const int MaxBytes=8192;
    internal sealed record Message(int Version,string Token,long Id,string Op,long Frames,string State);
    internal static async Task Write(Stream stream,Message message,CancellationToken cancel)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(message);
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Frame control message is too large.");
        var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await stream.WriteAsync(header,cancel).ConfigureAwait(false);await stream.WriteAsync(bytes,cancel).ConfigureAwait(false);await stream.FlushAsync(cancel).ConfigureAwait(false);
    }
    internal static async Task<Message> Read(Stream stream,string token,CancellationToken cancel)
    {
        var header=new byte[4];await stream.ReadExactlyAsync(header,cancel).ConfigureAwait(false);
        var length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length is <2 or >MaxBytes)throw new InvalidDataException("Invalid frame control length.");
        var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,cancel).ConfigureAwait(false);
        using var json=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=4});var root=json.RootElement;
        if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid frame control envelope.");
        var names=root.EnumerateObject().Select(p=>p.Name).ToArray();
        if(names.Length!=6||names.Distinct(StringComparer.Ordinal).Count()!=6||names.Except(new[]{"Version","Token","Id","Op","Frames","State"},StringComparer.Ordinal).Any())throw new InvalidDataException("Invalid frame control fields.");
        if(!root.GetProperty("Version"u8).TryGetInt32(out var version)||version!=1||root.GetProperty("Token"u8).GetString()!=token
            ||!root.GetProperty("Id"u8).TryGetInt64(out var id)||id<0||!root.GetProperty("Frames"u8).TryGetInt64(out var frames)||frames<0)throw new InvalidDataException("Wrong frame control identity or counter.");
        return new(1,token,id,root.GetProperty("Op"u8).GetString()??"",frames,root.GetProperty("State"u8).GetString()??"");
    }
}
