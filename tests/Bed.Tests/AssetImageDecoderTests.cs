using Bed.Editor.App;
using Xunit;

namespace Bed.Tests;
public sealed class AssetImageDecoderTests
{
    private static byte[] Tga(int width=1,int height=1)
    {
        var data=new byte[21];data[2]=2;data[12]=(byte)width;data[13]=(byte)(width>>8);data[14]=(byte)height;data[15]=(byte)(height>>8);
        data[16]=24;data[17]=32;data[18]=3;data[19]=2;data[20]=1;return data;
    }
    [Fact]
    public void Synthetic_tga_decodes_rgba_without_file_or_gpu()
    {
        Assert.True(AssetImageDecoder.TryDecode(Tga(),"tga",out var image,out var why),why);
        Assert.Equal(1,image!.Width);Assert.Equal(1,image.Height);Assert.Equal(new byte[]{1,2,3,255},image.Rgba);
    }
    [Theory]
    [InlineData(8193,1)] [InlineData(1,8193)] [InlineData(0,1)]
    public void Oversize_or_empty_dimensions_refuse_before_decode(int width,int height)
    { Assert.False(AssetImageDecoder.TryDecode(Tga(width,height),"tga",out var result,out var why));Assert.Null(result);Assert.NotEmpty(why); }
    [Fact]
    public void Unsupported_and_truncated_headers_refuse()
    {
        Assert.False(AssetImageDecoder.TryDecode(Tga(),"gif",out _,out _));
        Assert.False(AssetImageDecoder.Inspect([137,80,78],"png",out _,out _,out _));
        Assert.False(AssetImageDecoder.Inspect([255,216,255,192,0,250],"jpeg",out _,out _,out _));
    }
    [Fact]
    public void Boundary_dimensions_fit_output_cap_without_allocating_pixels()
    {
        var header=new byte[33]; new byte[]{137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82}.CopyTo(header,0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16),8192); System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20),8192);
        Assert.True(AssetImageDecoder.Inspect(header,"png",out var w,out var h,out _));
        Assert.Equal(AssetImageDecoder.MaxDecodedBytes,(long)w*h*4);
    }
    [Fact]
    public void Truncated_pixel_payload_is_a_plain_refusal()
    { Assert.False(AssetImageDecoder.TryDecode(Tga()[..18],"tga",out var result,out var why));Assert.Null(result);Assert.NotEmpty(why); }
    [Fact]
    public void Synthetic_png_decodes_rgba()
    {
        using var stream=new MemoryStream();stream.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string name,byte[] data)
        {
            var tag=System.Text.Encoding.ASCII.GetBytes(name); Span<byte> number=stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number,data.Length);stream.Write(number);stream.Write(tag);stream.Write(data);
            uint crc=0xffffffff;foreach(var value in tag.Concat(data)) { crc^=value;for(int bit=0;bit<8;bit++) crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1; }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number,~crc);stream.Write(number);
        }
        Chunk("IHDR",[0,0,0,1,0,0,0,1,8,6,0,0,0]);
        using var compressed=new MemoryStream();using(var z=new System.IO.Compression.ZLibStream(compressed,System.IO.Compression.CompressionLevel.SmallestSize,true)) z.Write(new byte[]{0,1,2,3,255});
        Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);
        Assert.True(AssetImageDecoder.TryDecode(stream.ToArray(),"png",out var image,out var why),why);Assert.Equal(new byte[]{1,2,3,255},image!.Rgba);
    }    [Fact]
    public void Self_authored_jpeg_decodes_without_real_asset_input()
    {
        var file=Path.Combine(Path.GetTempPath(),"bed-as07-"+Guid.NewGuid().ToString("N")+".jpg");
        try
        {
            Brutal.StbApi.Stb.WriteJpg(file,1,1,3,new byte[]{80,80,80},95);
            Assert.True(AssetImageDecoder.TryDecode(File.ReadAllBytes(file),"jpeg",out var image,out var why),why);
            Assert.Equal(1,image!.Width);Assert.Equal(1,image.Height);Assert.Equal(4,image.Rgba.Length);Assert.Equal(255,image.Rgba[3]);
        }
        finally { if(File.Exists(file)) File.Delete(file); }
    }}



