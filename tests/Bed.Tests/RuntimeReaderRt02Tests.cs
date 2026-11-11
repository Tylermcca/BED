using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Runtime;
using Xunit;

namespace Bed.Tests;

public sealed class RuntimeReaderRt02Tests
{
    private const string Asset="11111111111111111111111111111111";
    private static byte[] Mesh()=>DerivedMeshCodec.Encode(new MeshPart[]{
        new(new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]),new(0,Vector4.One,null))});
    private static byte[] Texture()
    {
        var bytes=new byte[20];"BEDTEX1\0"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12),1);
        bytes[16]=bytes[19]=255;return bytes;
    }
    private sealed class Fixture:IDisposable
    {
        public readonly string Owned=Directory.CreateTempSubdirectory("runtime-rt02-").FullName;
        public readonly AssetCatalogPublisher Publisher;
        public string Root=>Path.Combine(Owned,"project");
        public string Cache=>Path.Combine(Root,".bed","cache");
        public string Pointer=>Path.Combine(Cache,"current.json");
        public string Generation=>Path.Combine(Cache,"generations",Publisher.Current!.Id);
        public Fixture(byte[]? bytes=null)
        {
            Directory.CreateDirectory(Root);Publisher=new(Root);
            var stage=Publisher.Prepare("self-authored-rt02",new Dictionary<string,byte[]>{{Asset+".bin",bytes??Mesh()}});
            Assert.True(Publisher.Publish(stage,out var why),why);
        }
        public void ChangeManifest(Action<JsonObject> change)
        {
            var path=Path.Combine(Generation,"manifest.json");var json=JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            change(json);File.WriteAllText(path,json.ToJsonString());RefreshManifestHash();
        }
        public void RefreshManifestHash()
        {
            var pointer=JsonNode.Parse(File.ReadAllText(Pointer))!;
            pointer["ManifestHash"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Generation,"manifest.json"))));
            File.WriteAllText(Pointer,pointer.ToJsonString());
        }
        public void Dispose()
        {
            Publisher.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Owned,StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Owned,true);
        }
    }
    [Theory]
    [InlineData("mesh","truncated")][InlineData("mesh","oversized-count")][InlineData("mesh","wrong-version")]
    [InlineData("texture","truncated")][InlineData("texture","oversized-count")][InlineData("texture","wrong-version")]
    public void Hash_verified_corrupt_artifact_is_refused_by_its_runtime_reader_with_a_plain_data_exception(string kind,string corruption)
    {
        var bytes=kind=="mesh"?Mesh():Texture();
        if(corruption=="truncated")bytes=bytes[..^1];
        else if(corruption=="wrong-version")bytes[kind=="mesh"?7:6]=(byte)'9';
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(kind=="mesh"?16:8),uint.MaxValue);
        using var f=new Fixture(bytes);
        var assets=ProjectAssets.Open(f.Root);Assert.Equal(bytes,assets.ReadAsset(Asset));
        var before=bytes.ToArray();
        var error=Assert.Throws<InvalidDataException>(()=>
        {
            if(kind=="mesh")assets.ReadMesh(Asset);else assets.ReadTexture(Guid.ParseExact(Asset,"N"));
        });
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.DoesNotContain("IndexOutOfRange",error.Message,StringComparison.Ordinal);
        Assert.Equal(before,assets.ReadAsset(Asset));
    }
    [Theory][InlineData("mesh")][InlineData("texture")]
    public void Complete_artifact_over_byte_limit_is_refused_before_a_decoded_copy_is_allocated(string kind)
    {
        var bytes=new byte[BedMeshReader.MaxBytes+1];
        (kind=="mesh"?Mesh():Texture()).CopyTo(bytes,0);
        var before=GC.GetAllocatedBytesForCurrentThread();
        var error=Assert.Throws<InvalidDataException>(()=>
        {
            if(kind=="mesh")BedMeshReader.Decode(bytes);else BedTextureReader.Decode(bytes);
        });
        Assert.Contains("size",error.Message,StringComparison.Ordinal);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-before,0,1024*1024);
    }
    [Theory]
    [InlineData("pointer","truncated")][InlineData("pointer","oversized")]
    [InlineData("manifest","truncated")][InlineData("manifest","oversized")]
    public void Published_JSON_refuses_truncation_and_oversize_with_a_plain_data_exception(string file,string corruption)
    {
        using var f=new Fixture();var path=file=="pointer"?f.Pointer:Path.Combine(f.Generation,"manifest.json");
        if(corruption=="truncated")File.WriteAllText(path,"{");
        else using(var stream=new FileStream(path,FileMode.Open,FileAccess.Write))stream.SetLength(file=="pointer"?64*1024+1:1024*1024+1);
        if(file=="manifest")f.RefreshManifestHash();
        var error=Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root));
        Assert.Contains(corruption=="truncated"?"Invalid published cache JSON":"byte budget",error.Message,StringComparison.Ordinal);
    }
    [Fact]
    public void Wrong_cache_owner_version_is_refused_before_any_snapshot_is_returned()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Generation,"owner.txt"),"BED asset cache stage v2");
        Assert.Equal("Unowned cache allocation.",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Fact]
    public void Physical_artifact_larger_than_its_declaration_is_refused_without_allocating_its_payload()
    {
        using var f=new Fixture();
        using(var stream=new FileStream(Path.Combine(f.Generation,Asset+".bin"),FileMode.Open,FileAccess.Write))stream.SetLength(ProjectAssets.MaxArtifactBytes+1L);
        var before=GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal("Published cache file exceeds its byte budget.",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-before,0,1024*1024);
    }
    [Theory][InlineData("absolute")][InlineData("backslash")][InlineData("forward-slash")]
    public void Artifact_declarations_cannot_read_outside_the_cache_even_when_the_outside_file_exists(string route)
    {
        using var f=new Fixture();var outside=Path.Combine(f.Owned,"outside.bin");var sentinel=Encoding.UTF8.GetBytes("self-authored outside sentinel");
        File.WriteAllBytes(outside,sentinel);
        var name=route=="absolute"?outside:route=="backslash"?@"..\..\..\..\outside.bin":"../../../../outside.bin";
        f.ChangeManifest(json=>{var entry=json["Artifacts"]![0]!;entry["Name"]=name;entry["Length"]=sentinel.Length;entry["Sha256"]=Convert.ToHexStringLower(SHA256.HashData(sentinel));});
        using(var held=new FileStream(outside,FileMode.Open,FileAccess.Read,FileShare.None))
            Assert.Equal("Invalid artifact declaration.",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
        Assert.Equal(sentinel,File.ReadAllBytes(outside));
    }
    [Theory][InlineData("cache")][InlineData("generations")][InlineData("allocation")]
    public void Linked_cache_ancestor_is_refused_before_reading_a_locked_outside_pointer_or_artifact(string location)
    {
        using var f=new Fixture();
        var link=location=="cache"?f.Cache:location=="generations"?Path.Combine(f.Cache,"generations"):f.Generation;
        var target=Path.Combine(f.Owned,"outside-"+location);
        var owned=Path.GetFullPath(f.Owned)+Path.DirectorySeparatorChar;
        Assert.StartsWith(owned,Path.GetFullPath(link),StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(owned,Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase);
        f.Publisher.Dispose(); // Release owner.lock before relocating the fixture cache on Windows.
        Directory.Move(link,target);
        try
        {
            Link(link,target);
            var locked=location=="cache"?Path.Combine(target,"current.json"):
                location=="generations"?Path.Combine(target,f.Publisher.Current!.Id,Asset+".bin"):Path.Combine(target,Asset+".bin");
            using(var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None))
                Assert.Equal("Linked cache paths are not supported.",Assert.Throws<IOException>(()=>ProjectAssets.Open(f.Root)).Message);
        }
        finally
        {
            // Remove only the junction itself before moving the owned target back; never recurse through it.
            if(Directory.Exists(link))Directory.Delete(link);
            Directory.Move(target,link);
        }
        Assert.NotEmpty(ProjectAssets.Open(f.Root).ReadAsset(Asset));
    }
    private static void Link(string link,string target)
    {
        if(!OperatingSystem.IsWindows()){Directory.CreateSymbolicLink(link,target);return;}
        using var process=Process.Start(new ProcessStartInfo("cmd.exe")
        {ArgumentList={"/d","/c","mklink","/J",link,target},UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})!;
        var output=process.StandardOutput.ReadToEnd();var errors=process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(5000),"Junction creation timed out.");Assert.True(process.ExitCode==0,output+errors);
        Assert.True((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0);
    }
}
