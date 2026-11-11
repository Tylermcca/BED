using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;

public sealed class SpriteImageAdmissionTests
{
    [Fact]
    public void Startup_admits_existing_image_metadata_without_applying_model_settings_or_writing_sources()
    {
        var root=Directory.CreateTempSubdirectory("sprite-admission-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;
            var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(root,"assets")).FullName;
            var path=Path.Combine(assets,"sign.png");var bytes=Png();File.WriteAllBytes(path,bytes);
            const string id="11111111111111111111111111111111";
            var record=new AssetRecord(id,"sign.png",Convert.ToHexStringLower(SHA256.HashData(bytes)),"png",1,
                JsonSerializer.SerializeToElement(new{imageNote="owned by its image importer"}),
                new("Fixture","self-authored","generated test",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());
            File.WriteAllBytes(Path.Combine(assets,"not-imported.png"),bytes);
            using var admitted=new AssetCatalogProvisioner().Open(new(data,schemas))!;
            Assert.Empty(admitted.Problems);Assert.Contains("not-imported.png",admitted.Browser.NotImported);
            Assert.Equal(id,Assert.Single(admitted.Browser.Entries).Guid);
            var input=Assert.Single(admitted.Catalog.CurrentInputs());Assert.Equal(record.Write(),input.ProducingRecord.Write());
            var lease=admitted.Catalog.Resolve(id).Current!;Assert.NotNull(lease.Texture);
            var pixels=new byte[lease.Length];lease.CopyTo(pixels);Assert.Equal(new byte[]{1,2,3,127},pixels);
            Assert.Equal(bytes,File.ReadAllBytes(path));Assert.Equal(record.Write(),File.ReadAllText(AssetRecord.SidecarFor(path)));
            Assert.False(File.Exists(AssetRecord.SidecarFor(Path.Combine(assets,"not-imported.png"))));
        }
        finally{Directory.Delete(root,true);}
    }
    // A one-pixel RGBA PNG authored entirely here; no external artwork or fixture download.
    internal static byte[] Png()
    {
        using var stream=new MemoryStream();stream.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string name,byte[] data)
        {
            var tag=System.Text.Encoding.ASCII.GetBytes(name);Span<byte> number=stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number,data.Length);stream.Write(number);stream.Write(tag);stream.Write(data);
            uint crc=0xffffffff;foreach(var value in tag.Concat(data)){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number,~crc);stream.Write(number);
        }
        Chunk("IHDR",[0,0,0,1,0,0,0,1,8,6,0,0,0]);
        using var compressed=new MemoryStream();using(var z=new System.IO.Compression.ZLibStream(compressed,System.IO.Compression.CompressionLevel.SmallestSize,true))z.Write(new byte[]{0,1,2,3,127});
        Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);return stream.ToArray();
    }
}
