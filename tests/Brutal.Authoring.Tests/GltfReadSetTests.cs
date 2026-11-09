using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Import;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class GltfReadSetTests
{
    internal static string WriteFixture(string root,float x=1,bool embedded=false,string bufferName="vertices.bin")
    {
        byte[] buffer=new byte[42];float[] positions=[0,0,0,x,0,0,0,1,0];
        for(var i=0;i<positions.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i*4),positions[i]);
        for(ushort i=0;i<3;i++)BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(36+i*2),i);
        var bufferPath=Path.Combine(root,bufferName);Directory.CreateDirectory(Path.GetDirectoryName(bufferPath)!);File.WriteAllBytes(bufferPath,buffer);
        var uri=embedded?"data:application/octet-stream;base64,"+Convert.ToBase64String(buffer):string.Join("/",bufferName.Split('/').Select(Uri.EscapeDataString));
        var json=JsonSerializer.Serialize(new {asset=new{version="2.0"},buffers=new[]{new{uri,byteLength=42}},
            bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=36,target=34962},new{buffer=0,byteOffset=36,byteLength=6,target=34963}},
            accessors=new object[]{new{bufferView=0,componentType=5126,count=3,type="VEC3",min=new[]{0,0,0},max=new[]{x,1f,0}},new{bufferView=1,componentType=5123,count=3,type="SCALAR"}},
            meshes=new[]{new{primitives=new[]{new{attributes=new{POSITION=0},indices=1}}}},nodes=new[]{new{mesh=0}},scenes=new[]{new{nodes=new[]{0}}},scene=0});
        var path=Path.Combine(root,"self.gltf");File.WriteAllText(path,json);return path;
    }
    [Fact]
    public void External_receipt_hashes_exact_supplied_bytes_without_exposing_absolute_paths_or_mutable_membership()
    {
        var root=Directory.CreateTempSubdirectory("as12-read-").FullName;
        try
        {
            var path=WriteFixture(root);var result=GltfImporter.Import(path,ProjectFrame.Gltf);Assert.NotEqual(ImportOutcome.Refused,result.Outcome);
            var read=Assert.Single(result.ExternalReads);Assert.Equal("vertices.bin",read.RelativePath);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"vertices.bin")))),read.SourceHash);
            Assert.False(Path.IsPathRooted(read.RelativePath));Assert.Throws<NotSupportedException>(()=>((IList<ImportedSourceRead>)result.ExternalReads).Clear());
            var first=read.SourceHash;WriteFixture(root,2);var changed=GltfImporter.Import(path,ProjectFrame.Gltf);
            Assert.NotEqual(first,Assert.Single(changed.ExternalReads).SourceHash);Assert.Equal(first,read.SourceHash);
            Assert.Equal(2,changed.Asset!.Meshes[0].Positions.Max(p=>p.X));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Embedded_buffer_is_not_misreported_as_an_external_file_read()
    {
        var root=Directory.CreateTempSubdirectory("as12-embedded-").FullName;
        try{var result=GltfImporter.Import(WriteFixture(root,1,true),ProjectFrame.Gltf);Assert.NotEqual(ImportOutcome.Refused,result.Outcome);Assert.Empty(result.ExternalReads);}
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Receipt_uses_the_actual_normalized_local_path_for_an_escaped_nested_resource()
    {
        var root=Directory.CreateTempSubdirectory("as12-nested-").FullName;
        try
        {
            const string name="buffers/some data.bin";var result=GltfImporter.Import(WriteFixture(root,1,false,name),ProjectFrame.Gltf);
            Assert.NotEqual(ImportOutcome.Refused,result.Outcome);var read=Assert.Single(result.ExternalReads);Assert.Equal(name,read.RelativePath);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,name)))),read.SourceHash);
        }
        finally{Directory.Delete(root,true);}
    }

}
