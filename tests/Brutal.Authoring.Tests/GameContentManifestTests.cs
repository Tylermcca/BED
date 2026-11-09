using System.Security.Cryptography;
using Brutal.Authoring.Editor;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class GameContentManifestTests
{
    private static GameContentNode Node(string id, string[]? deps=null,byte value=1,bool complete=true,string? hash=null)=>new(new(id,"content/"+id+".bin","test-v1"),[value],hash??Convert.ToHexStringLower(SHA256.HashData(new[]{value})),deps??[],complete);
    private static GameBuildProfile Profile()=>GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,[Node("root").Output]);
    private static GameContentManifest Build(params GameContentNode[] nodes)=>GameContentManifest.Create(Profile(),"game-inventory-v1","revision-1",true,nodes);
    [Fact] public void Closed_graph_is_deterministic_and_bytes_are_owned()
    {
        var bytes=new byte[]{7};var child=new GameContentNode(Node("child").Output,bytes,Convert.ToHexStringLower(SHA256.HashData(bytes)),[],true);bytes[0]=9;
        var a=Build(Node("root",["child"]),child);var b=Build(child,Node("root",["child"]));Assert.Equal(a.Hash,b.Hash);Assert.Equal("game-inventory-v1",a.InventoryOwner);
        byte[] copy=new byte[1];a.Nodes[0].CopyTo(copy);Assert.Equal(7,copy[0]);copy[0]=99;a.Nodes[0].CopyTo(copy);Assert.Equal(7,copy[0]);
    }
    [Fact] public void Missing_or_incomplete_coverage_never_admits_a_complete_export()
    {
        Assert.Throws<ArgumentException>(()=>Build(Node("root",["missing"])));
        Assert.Throws<ArgumentException>(()=>Build(Node("root",complete:false)));
        Assert.Throws<ArgumentException>(()=>GameContentManifest.Create(Profile(),"game","revision",false,[Node("root")]));
        Assert.Throws<ArgumentException>(()=>Build());
    }
    [Fact] public void Cycles_and_unreachable_extras_refuse()
    {
        Assert.Throws<ArgumentException>(()=>Build(Node("root",["child"]),Node("child",["root"])));
        Assert.Throws<ArgumentException>(()=>Build(Node("root",["root"])));
        Assert.Throws<ArgumentException>(()=>Build(Node("root"),Node("extra")));
    }
    [Fact] public void Hash_mismatch_and_output_collision_refuse()
    {
        Assert.Throws<ArgumentException>(()=>Build(Node("root",hash:new string('0',64))));
        var child=new GameContentNode(new("child","CONTENT/root.bin","test-v1"),[1],Node("root").Sha256,[],true);
        Assert.Throws<ArgumentException>(()=>Build(Node("root",["child"]),child));
    }
    [Fact] public void Changed_bytes_revision_or_mapping_changes_manifest_identity()
    {
        var a=Build(Node("root"));Assert.NotEqual(a.Hash,Build(Node("root",value:2)).Hash);
        Assert.NotEqual(a.Hash,GameContentManifest.Create(Profile(),"game-inventory-v1","revision-2",true,[Node("root")]).Hash);
        var p=GameBuildProfile.Create("another","Game.csproj",GameBuildTarget.WindowsX64,Profile().Content);
        Assert.NotEqual(a.Hash,GameContentManifest.Create(p,"game-inventory-v1","revision-1",true,[Node("root")]).Hash);
    }
    [Fact] public void Root_mapping_and_duplicate_id_must_match_exactly()
    {
        var wrong=new GameContentNode(new("root","other.bin","test-v1"),[1],Node("root").Sha256,[],true);
        Assert.Throws<ArgumentException>(()=>Build(wrong));Assert.Throws<ArgumentException>(()=>Build(Node("root"),Node("root")));
    }
    [Fact] public void Bound_is_checked_before_unbounded_enumeration()
    {
        var read=0;IEnumerable<GameContentNode> More(){while(true){read++;yield return Node("x"+read);}}
        Assert.Throws<ArgumentException>(()=>GameContentManifest.Create(Profile(),"game","rev",true,More()));Assert.Equal(1025,read);
    }
}
