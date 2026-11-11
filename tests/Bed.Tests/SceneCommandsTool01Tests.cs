using System.Numerics;
using System.Text.Json.Nodes;
using Xunit;
namespace Bed.Tests;

public sealed class SceneCommandsTool01Tests
{
    private sealed class Rig:IDisposable
    {
        private readonly string root=Directory.CreateTempSubdirectory("scene-cli-tool01-").FullName;
        public readonly string Data,Schemas,Path;public readonly Scene Original;
        public Rig()
        {
            Data=Directory.CreateDirectory(System.IO.Path.Combine(root,"data with spaces")).FullName;Schemas=Directory.CreateDirectory(System.IO.Path.Combine(root,"schemas")).FullName;
            Path=System.IO.Path.Combine(Data,"level.json");
            var parent=SceneObject.At("root",null,new(4,0,2)) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f),Scale=new(1.5f)};
            var child=SceneObject.At("child",null,Vector3.UnitX) with{Parent="root"};child=child with{References=child.References.Add("anchor","root")};
            Original=new([parent,child,SceneObject.At("free",null,new(-3,0,1))]);File.WriteAllText(Path,new SceneFormat().Write(Original));
        }
        public (int Code,JsonObject? Result,string Error) Run(string action,string json,params string[] flags)
        {
            using var output=new StringWriter();using var error=new StringWriter();
            var code=Bed.Agent.Cli.Program.Run(["--data",Data,"--schemas",Schemas,"scene","level.json",action,json,..flags],TextReader.Null,output,error);
            return(code,output.ToString().Length==0?null:JsonNode.Parse(output.ToString())!.AsObject(),error.ToString());
        }
        public void Dispose()=>Directory.Delete(root,true);
    }
    [Theory]
    [InlineData("add","{\"object\":{\"id\":\"added\",\"position\":[1,2,3]}}")]
    [InlineData("move","{\"id\":\"child\",\"position\":[7,2,-1]}")]
    [InlineData("duplicate","{\"ids\":[\"root\",\"child\"]}")]
    [InlineData("delete","{\"id\":\"free\"}")]
    [InlineData("rename","{\"id\":\"root\",\"newId\":\"new_root\"}")]
    [InlineData("group","{\"ids\":[\"root\",\"free\"]}")]
    public void Every_action_previews_without_writing_then_saves_the_same_candidate_with_one_replayable_undo(string action,string json)
    {
        using var r=new Rig();var before=File.ReadAllBytes(r.Path);var dry=r.Run(action,json);
        Assert.Equal(0,dry.Code);Assert.Equal("",dry.Error);Assert.True(dry.Result!["preview"]!.GetValue<bool>());Assert.False(dry.Result["saved"]!.GetValue<bool>());Assert.Equal(before,File.ReadAllBytes(r.Path));
        var forced=r.Run(action,json,"--save","--preview");Assert.Equal(0,forced.Code);Assert.Equal(before,File.ReadAllBytes(r.Path));
        var saved=r.Run(action,json,"--save");Assert.Equal(0,saved.Code);Assert.Equal("",saved.Error);Assert.True(saved.Result!["saved"]!.GetValue<bool>());Assert.False(saved.Result["preview"]!.GetValue<bool>());
        Assert.Equal(dry.Result["candidate"]!.ToJsonString(),saved.Result["candidate"]!.ToJsonString());Assert.Equal(1,saved.Result["undoSteps"]!.GetValue<int>());
        var format=new SceneFormat();var candidate=format.Read(saved.Result["candidate"]!.ToJsonString());Assert.Equal(format.Write(candidate),File.ReadAllText(r.Path));Assert.Empty(new SceneValidator().Validate(candidate));
        var replay=DocumentSession<Scene>.Replay(r.Original,JournalFile.Read(saved.Result["journal"]!.ToJsonString()),format,new SceneValidator(),SceneKit.Catalog());
        Assert.Equal(format.Write(candidate),format.Write(replay.Current));Assert.True(replay.Undo());Assert.Equal(format.Write(r.Original),format.Write(replay.Current));
        switch(action)
        {
            case "add":Assert.Equal(new Vector3(1,2,3),candidate.Find("added")!.Position);break;
            case "move":Assert.Equal(new Vector3(7,2,-1),candidate.Find("child")!.Position);break;
            case "delete":Assert.Null(candidate.Find("free"));break;
            case "rename":Assert.Null(candidate.Find("root"));Assert.Equal("new_root",candidate.Find("child")!.Parent);Assert.Equal("new_root",candidate.Find("child")!.References["anchor"]);break;
            case "duplicate":
                Assert.Equal("copy_root",candidate.Find("copy_child")!.Parent);Assert.Equal("copy_root",candidate.Find("copy_child")!.References["anchor"]);
                foreach(var id in new[]{"root","child"})Near(Bed.Editor.SceneViewport.World(r.Original,id).Translation+Vector3.UnitX,Bed.Editor.SceneViewport.World(candidate,"copy_"+id).Translation);
                break;
            case "group":
                Assert.Equal("group",candidate.Find("root")!.Parent);Assert.Equal("group",candidate.Find("free")!.Parent);Assert.Equal("root",candidate.Find("child")!.Parent);
                Assert.Equal(new Vector3(.5f,0,1.5f),candidate.Find("group")!.Position);
                foreach(var item in r.Original.Objects)Near(Bed.Editor.SceneViewport.World(r.Original,item.Id).Translation,Bed.Editor.SceneViewport.World(candidate,item.Id).Translation);
                break;
        }
    }
    private static void Near(Vector3 expected,Vector3 actual)=>Assert.InRange(Vector3.Distance(expected,actual),0,.0001f);
    [Theory]
    [InlineData("delete","{\"id\":\"root\"}",1,"child")]
    [InlineData("rename","{\"id\":\"root\",\"newId\":\"Bad-ID\"}",1,"lowercase")]
    [InlineData("rename","{\"id\":\"root\",\"newId\":\"free\"}",1,"already exists")]
    [InlineData("move","{\"id\":\"absent\",\"position\":[1,2,3]}",1,"no object")]
    [InlineData("add","{\"object\":{\"id\":\"bad_child\",\"parent\":\"absent\"}}",1,"absent")]
    [InlineData("duplicate","{\"ids\":[\"absent\"]}",1,"existing")]
    [InlineData("group","{\"ids\":[\"root\",\"root\"]}",2,"repeat")]
    [InlineData("group","{\"ids\":[]}",2,"one or more")]
    [InlineData("group","{\"ids\":[\"root\"],\"extra\":true}",2,"exactly")]
    [InlineData("move","not json",2,"valid JSON")]
    [InlineData("teleport","{}",2,"Scene actions")]
    public void Invalid_commands_are_plain_refusals_or_bad_arguments_and_never_write(string action,string json,int code,string reason)
    {
        using var r=new Rig();var before=File.ReadAllBytes(r.Path);var result=r.Run(action,json,"--save");Assert.Equal(code,result.Code);
        Assert.Contains(reason,code==1?result.Result!["error"]!.GetValue<string>():result.Error,StringComparison.OrdinalIgnoreCase);Assert.Equal(before,File.ReadAllBytes(r.Path));
    }
    [Fact] public void Read_only_projects_cannot_be_written_and_only_admitted_scene_paths_are_used()
    {
        using var r=new Rig();var before=File.ReadAllBytes(r.Path);var refused=r.Run("move","{\"id\":\"free\",\"position\":[0,0,0]}","--save","--read-only");
        Assert.Equal(1,refused.Code);Assert.Contains("read",refused.Result!["error"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase);Assert.Equal(before,File.ReadAllBytes(r.Path));
        using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(1,Bed.Agent.Cli.Program.Run(["--data",r.Data,"--schemas",r.Schemas,"scene","../outside.json","delete","{\"id\":\"free\"}","--save"],TextReader.Null,output,error));
        Assert.Contains("admitted",output.ToString());Assert.Equal(before,File.ReadAllBytes(r.Path));
    }
    [Fact] public void Existing_duplicate_and_group_ids_are_never_reused_or_overwritten()
    {
        using var r=new Rig();var occupied=new[]{"copy_root","copy_child","group"}.Select((id,i)=>SceneObject.At(id,null,new(20+i,0,0))).ToArray();
        File.WriteAllText(r.Path,new SceneFormat().Write(new Scene(r.Original.Objects.AddRange(occupied))));
        var copied=r.Run("duplicate","{\"ids\":[\"root\",\"child\"]}","--save");Assert.Equal(0,copied.Code);
        var afterCopy=new SceneFormat().Read(File.ReadAllText(r.Path));Assert.NotNull(afterCopy.Find("copy_root_2"));Assert.Equal("copy_root_2",afterCopy.Find("copy_child_2")!.Parent);
        var grouped=r.Run("group","{\"ids\":[\"root\",\"free\"]}","--save");Assert.Equal(0,grouped.Code);
        var afterGroup=new SceneFormat().Read(File.ReadAllText(r.Path));Assert.Equal("group_2",afterGroup.Find("root")!.Parent);
        foreach(var item in occupied)Assert.Equal(item.Position,afterGroup.Find(item.Id)!.Position);
    }
}
