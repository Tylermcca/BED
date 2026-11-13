using System.Buffers.Binary;
using Brutal.StbApi;

namespace Bed.Editor.App;

/// <summary>CPU-only bounded PNG/JPEG/TGA decoding from caller-pinned bytes. No path, policy approval or GPU ownership.</summary>
public static class AssetImageDecoder
{
    /// <summary>Encoded input cap, in bytes (64 MiB).</summary>
    public const int MaxEncodedBytes = 64 * 1024 * 1024;
    /// <summary>Maximum width or height.</summary>
    public const int MaxDimension = 8192;
    /// <summary>Decoded RGBA8 payload cap (256 MiB), not a process working-set guarantee.</summary>
    public const int MaxDecodedBytes = 256 * 1024 * 1024;
    /// <summary>Owned decoded pixels; callers may mutate their copy. No native resource escapes.</summary>
    public sealed record Image(int Width, int Height, byte[] Rgba);

    /// <summary>Inspects supported headers and budgets without decoding or allocating pixel storage.</summary>
    public static bool Inspect(ReadOnlySpan<byte> bytes, string format, out int width, out int height, out string why)
    {
        width = height = 0;
        if (bytes.Length == 0 || bytes.Length > MaxEncodedBytes) { why = "Encoded image exceeds the limit or is empty."; return false; }
        format = format.TrimStart('.').ToLowerInvariant();
        if (format == "png")
        {
            ReadOnlySpan<byte> signature = [137,80,78,71,13,10,26,10];
            if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature) || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12,4).SequenceEqual("IHDR"u8))
            { why = "Invalid PNG header."; return false; }
            var w=BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);var h=BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
            if (w>MaxDimension || h>MaxDimension) { why="PNG dimensions exceed the limit."; return false; }
            width=(int)w;height=(int)h;
        }
        else if (format is "jpg" or "jpeg")
        {
            if (bytes.Length<4 || bytes[0]!=255 || bytes[1]!=216) { why="Invalid JPEG header.";return false; }
            var offset=2;
            while (offset<bytes.Length)
            {
                if (bytes[offset++]!=255) break;
                while(offset<bytes.Length && bytes[offset]==255) offset++;
                if(offset>=bytes.Length) break;
                var marker=bytes[offset++];
                if(marker is 0xda or 0xd9) break;
                if(marker is 0x01 or >=0xd0 and <=0xd8) continue;
                if(offset+2>bytes.Length) break;
                var length=BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
                if(length<2 || length>bytes.Length-offset) break;
                if(marker is 0xc0 or 0xc1 or 0xc2)
                {
                    if(length<8) break;
                    height=BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset+3)..]);width=BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset+5)..]);break;
                }
                offset+=length;
            }
        }
        else if(format=="tga")
        {
            if(bytes.Length<18 || bytes[1]>1 || bytes[2] is not (1 or 2 or 3 or 9 or 10 or 11))
            { why="Invalid or unsupported TGA header.";return false; }
            width=BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);height=BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
            var pixelBytes=(bytes[16]+7)/8;
            var paletteBytes=bytes[1]==0?0:(long)BinaryPrimitives.ReadUInt16LittleEndian(bytes[5..])*((bytes[7]+7)/8);
            var start=18L+bytes[0]+paletteBytes;
            var needed=(long)width*height;
            if(pixelBytes is <1 or >4 || start>bytes.Length) { why="Invalid TGA pixel layout.";return false; }
            if(bytes[2]<9)
            {
                if(needed*pixelBytes>bytes.Length-start) { why="Truncated TGA pixels.";return false; }
            }
            else
            {
                var offset=(int)start;long remaining=needed;
                while(remaining>0)
                {
                    if(offset>=bytes.Length) { why="Truncated TGA RLE packet.";return false; }
                    var packet=bytes[offset++];var count=(packet&127)+1;
                    var length=(packet&128)!=0?pixelBytes:count*pixelBytes;
                    if(count>remaining || length>bytes.Length-offset) { why="Invalid TGA RLE packet.";return false; }
                    offset+=length;remaining-=count;
                }
            }
        }
        else { why="Only PNG, JPEG and TGA previews are supported.";return false; }
        if(width<=0 || height<=0 || width>MaxDimension || height>MaxDimension || (long)width*height*4>MaxDecodedBytes)
        { why="Image dimensions are invalid or exceed the preview budget.";return false; }
        why="";return true;
    }

    /// <summary>Checks headers before native pixel allocation, decodes RGBA8, rechecks dimensions, then copies and disposes native memory.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> encoded, string format, out Image? result, out string why)
    {
        result=null;
        if(!Inspect(encoded,format,out var width,out var height,out why)) return false;
        // Private stable buffer prevents caller mutation between header inspection and decode.
        var owned=encoded.ToArray();
        if(!Inspect(owned,format,out width,out height,out why)) return false;
        using var image=Stb.LoadFromMemory(owned,4);
        if(image.Width!=width || image.Height!=height) { why="Decoded dimensions do not match the inspected header.";return false; }
        var pixels=image.AsSpan();
        if(pixels.Length!=(long)width*height*4) { why="Decoded RGBA8 length is invalid.";return false; }
        result=new(width,height,pixels.ToArray());why="";return true;
    }
}

