using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Runtime;
using Xunit;
namespace Bed.Tests;

public sealed class RuntimeProjectAssetsTests
{
    private const string Guid="0123456789abcdef0123456789abcdef";
    private static byte[] Triangle()=>DerivedMeshCodec.Encode(new([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2])
        {Uv0=[new(-2,.2f),Vector2.Zero,Vector2.One],Uv0Origin=MeshUv0Origin.ObjBottomLeft});
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("runtime-assets-").FullName;
        public readonly AssetCatalogPublisher Publisher;
        public string Cache=>Path.Combine(Root,".bed","cache");
        public string Pointer=>Path.Combine(Cache,"current.json");
        public string Generation=>Path.Combine(Cache,"generations",Publisher.Current!.Id);
        public Fixture(){Publisher=new(Root);Publish(Triangle());}
        public void Publish(byte[] bytes)
        {var stage=Publisher.Prepare("self-authored-revision",new Dictionary<string,byte[]>{{Guid+".bin",bytes}});Assert.True(Publisher.Publish(stage,out var why),why);}
        public void Manifest(Action<JsonObject> change)
        {
            var path=Path.Combine(Generation,"manifest.json");var node=JsonNode.Parse(File.ReadAllText(path))!.AsObject();change(node);
            var text=node.ToJsonString();File.WriteAllText(path,text);var pointer=JsonNode.Parse(File.ReadAllText(Pointer))!;
            pointer["ManifestHash"]=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));File.WriteAllText(Pointer,pointer.ToJsonString());
        }
        public Dictionary<string,string> Files()=>Directory.GetFiles(Root,"*",SearchOption.AllDirectories)
            .Where(path=>Path.GetFileName(path)!="owner.lock").ToDictionary(path=>Path.GetRelativePath(Root,path),path=>Convert.ToHexString(File.ReadAllBytes(path)));
        public void Dispose(){Publisher.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Real_publisher_generation_opens_read_only_while_owner_lock_is_held_and_uses_the_single_decoder()
    {
        using var f=new Fixture();var before=f.Files();var paths=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories);
        var assets=ProjectAssets.Open(f.Root);Assert.Equal(f.Publisher.Current!.Id,assets.Id);Assert.Equal(1,assets.Generation);
        Assert.Equal("self-authored-revision",assets.Revision);Assert.Equal(new[]{Guid+".bin"},assets.ArtifactNames);Assert.True(assets.ContainsAsset(Guid));
        var mesh=assets.ReadMesh(Guid);Assert.Equal(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},mesh.Positions);Assert.Equal(new[]{0,1,2},mesh.Indices);
        Assert.Equal(BedMeshUv0Origin.ObjBottomLeft,mesh.Uv0Origin);Assert.Equal(new Vector2(-2,.2f),mesh.Uv0![0]);Assert.Null(mesh.Normals);
        Assert.Equal(paths,Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories));Assert.Equal(before.OrderBy(p=>p.Key),f.Files().OrderBy(p=>p.Key));
    }
    [Fact]
    public void Opened_snapshot_and_each_byte_copy_are_owned_and_do_not_follow_new_publications()
    {
        using var f=new Fixture();var first=ProjectAssets.Open(f.Root);var copy=first.ReadAsset(Guid);Array.Fill(copy,(byte)0);
        Assert.Equal(Triangle(),first.ReadAsset(Guid));f.Publish([1,2,3]);var next=ProjectAssets.Open(f.Root);
        Assert.NotEqual(first.Id,next.Id);Assert.Equal(2,next.Generation);Assert.Equal(new byte[]{1,2,3},next.ReadAsset(Guid));Assert.Equal(Triangle(),first.ReadAsset(Guid));
    }
    [Fact]
    public void Missing_publication_refuses_plainly_and_creates_no_cache_or_owner_lock()
    {
        var root=Directory.CreateTempSubdirectory("runtime-empty-").FullName;
        try{var error=Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(root));Assert.Equal(ProjectAssets.MissingGeneration,error.Message);Assert.Empty(Directory.GetFileSystemEntries(root));}
        finally{Directory.Delete(root);}
    }
    [Fact]
    public void A_copied_generation_layout_needs_no_editor_owner_lock_or_source_assets()
    {
        using var f=new Fixture();var exported=Path.Combine(f.Root,"exported");var cache=Path.Combine(exported,".bed","cache");
        var generation=Path.Combine(cache,"generations",f.Publisher.Current!.Id);Directory.CreateDirectory(generation);
        File.Copy(f.Pointer,Path.Combine(cache,"current.json"));foreach(var file in Directory.GetFiles(f.Generation))File.Copy(file,Path.Combine(generation,Path.GetFileName(file)));
        var before=Directory.GetFiles(exported,"*",SearchOption.AllDirectories);var assets=ProjectAssets.Open(exported);Assert.Equal(Triangle(),assets.ReadAsset(Guid));
        Assert.False(File.Exists(Path.Combine(cache,"owner.lock")));Assert.False(Directory.Exists(Path.Combine(exported,"assets")));Assert.Equal(before,Directory.GetFiles(exported,"*",SearchOption.AllDirectories));
    }
    [Theory][InlineData("../outside")][InlineData("UPPERCASE")]
    public void Pointer_allocation_identity_is_not_a_path(string id)
    {
        using var f=new Fixture();var pointer=JsonNode.Parse(File.ReadAllText(f.Pointer))!;pointer["Id"]=id;File.WriteAllText(f.Pointer,pointer.ToJsonString());
        Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root));
    }
    [Fact]
    public void Manifest_hash_is_checked_before_using_its_declarations()
    {
        using var f=new Fixture();File.AppendAllText(Path.Combine(f.Generation,"manifest.json")," ");
        Assert.Contains("manifest changed",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Fact]
    public void Artifact_length_and_hash_are_checked_before_a_snapshot_is_returned()
    {
        using var f=new Fixture();File.WriteAllBytes(Path.Combine(f.Generation,Guid+".bin"),[1,2,3]);
        Assert.Contains("changed artifact",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Theory][InlineData("../outside")][InlineData("COM1.bin")][InlineData("OWNER.TXT")]
    public void Artifact_names_keep_the_existing_flat_publisher_contract(string name)
    {
        using var f=new Fixture();f.Manifest(node=>node["Artifacts"]![0]!["Name"]=name);
        Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root));
    }
    [Fact]
    public void Duplicate_case_insensitive_artifact_names_refuse()
    {
        using var f=new Fixture();f.Manifest(node=>{var list=node["Artifacts"]!.AsArray();var second=list[0]!.DeepClone();second["Name"]=(Guid+".bin").ToUpperInvariant();list.Add(second);});
        Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root));
    }
    [Fact]
    public void Oversized_declared_artifact_refuses_before_its_bytes_are_allocated()
    {
        using var f=new Fixture();f.Manifest(node=>node["Artifacts"]![0]!["Length"]=(long)ProjectAssets.MaxArtifactBytes+1);
        Assert.Contains("declaration",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Fact]
    public void Total_declared_budget_refuses_before_reading_or_allocating_artifact_payloads()
    {
        using var f=new Fixture();f.Manifest(node=>{var list=node["Artifacts"]!.AsArray();var template=list[0]!.DeepClone();list.Clear();
            for(var i=0;i<5;i++){var entry=template.DeepClone();entry["Name"]="bounded-"+i+".bin";entry["Length"]=ProjectAssets.MaxArtifactBytes;list.Add(entry);}});
        Assert.Contains("runtime byte budget",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Undeclared_file_or_subdirectory_is_not_silently_admitted(bool directory)
    {
        using var f=new Fixture();var path=Path.Combine(f.Generation,"extra");if(directory)Directory.CreateDirectory(path);else File.WriteAllText(path,"self-authored extra");
        Assert.Contains("Undeclared",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Fact]
    public void Owner_marker_and_generation_identity_remain_required()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Generation,"owner.txt"),"not an owned allocation");
        Assert.Contains("Unowned",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
        File.WriteAllText(Path.Combine(f.Generation,"owner.txt"),"BED asset cache stage v1");f.Manifest(node=>node["Id"]=new string('a',32));
        Assert.Contains("identity",Assert.Throws<InvalidDataException>(()=>ProjectAssets.Open(f.Root)).Message);
    }
    [Fact]
    public void Guid_lookup_is_exact_and_has_no_source_path_or_basename_fallback()
    {
        using var f=new Fixture();var assets=ProjectAssets.Open(f.Root);Assert.False(assets.ContainsAsset("cube.obj"));
        Assert.Throws<ArgumentException>(()=>assets.ReadAsset("cube.obj"));Assert.Throws<ArgumentException>(()=>assets.ReadArtifact("../"+Guid+".bin"));
        Assert.Throws<InvalidDataException>(()=>assets.ReadAsset(new string('f',32)));Assert.Throws<InvalidDataException>(()=>assets.ReadArtifact("unrelated.bin"));
    }
    [Fact]
    public void Runtime_mesh_decode_owns_geometry_independently_of_input_bytes()
    {
        var bytes=Triangle();var mesh=BedMeshReader.Decode(bytes);Array.Fill(bytes,(byte)0);
        Assert.Equal(Vector3.UnitX,mesh.Positions[1]);Assert.Equal(2,mesh.Indices[2]);Assert.Equal(new Vector2(-2,.2f),mesh.Uv0![0]);
        Assert.Throws<InvalidDataException>(()=>BedMeshReader.Decode(bytes));
    }
}
