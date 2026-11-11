using System.Text.Json.Nodes;
using Xunit;
namespace Bed.Tests;
public sealed class Scene2CliCommandTests
{
    private static (int Code,JsonObject? Reply,string Error) Run(Scene2HostCommandTests.Rig r,string action,string args,params string[] flags)
    {
        using var output=new StringWriter();using var error=new StringWriter();
        var code=Bed.Agent.Cli.Program.Run(["--data",r.Data,"--schemas",Path.Combine(r.Root,"schemas"),"scene","level.json",action,args,..flags],TextReader.Null,output,error);
        return(code,output.ToString().Length==0?null:JsonNode.Parse(output.ToString())!.AsObject(),error.ToString());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void CLI_preview_wins_over_save_and_checked_save_replays_the_exact_v2_candidate(bool saveAlso)
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);var args="""{"id":"frame_a_root","field":"/properties/locked","value":false}""";
        var preview=Run(r,"set",args,saveAlso?["--preview","--save"]:[]);Assert.Equal(0,preview.Code);Assert.True(preview.Reply!["preview"]!.GetValue<bool>());Assert.False(preview.Reply["saved"]!.GetValue<bool>());Assert.Equal(1,preview.Reply["undoSteps"]!.GetValue<int>());Assert.Equal(r.Original,File.ReadAllText(r.Disk));
        var saved=Run(r,"set",args,"--save");Assert.Equal(0,saved.Code);Assert.True(saved.Reply!["saved"]!.GetValue<bool>());
        var format=new Scene2Format();var candidate=format.Read(saved.Reply["candidate"]!.ToJsonString());Assert.Equal(format.Write(candidate),File.ReadAllText(r.Disk));
        var replay=DocumentSession<Scene2>.Replay(format.Read(r.Original),JournalFile.Read(saved.Reply["journal"]!.ToJsonString()),format,new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(format.Write(candidate),replay.TextToSave);
    }
    [Fact]
    public void CLI_source_update_uses_admitted_GUID_and_journals_accepted_content()
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);r.ChangeSource();var result=Run(r,"update","""{"instance":"22222222222222222222222222222222"}""","--save");Assert.Equal(0,result.Code);Assert.Contains("Revised saved source",File.ReadAllText(r.Disk));
        File.Delete(r.Source);var format=new Scene2Format();var replay=DocumentSession<Scene2>.Replay(format.Read(r.Original),JournalFile.Read(result.Reply!["journal"]!.ToJsonString()),format,new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(File.ReadAllText(r.Disk),replay.TextToSave);
    }
    [Fact]
    public void CLI_planar_sprite_edit_preserves_space_hierarchy_and_other_sprite_metadata()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json",open:false);
        var result=Run(r,"set_sprite","""{"id":"sign","sprite":{"size":[2,3],"pivot":[0.5,0],"order":9,"filter":"nearest"}}""","--save");Assert.Equal(0,result.Code);
        var scene=new Scene2Format().Read(File.ReadAllText(r.Disk));Assert.Equal(SceneSpace.TwoDXY,scene.Space);var sign=scene.Objects.Single(o=>o.Object.Id=="sign");Assert.Equal(9,sign.Sprite!.Order);Assert.Equal(new System.Numerics.Vector2(2,3),sign.Sprite.Size);Assert.Equal("room",scene.Objects.Single(o=>o.Object.Id=="door").Object.Parent);
    }
    [Theory][InlineData("--read-only")][InlineData("bad-source")][InlineData("inline-source")]
    public void CLI_refusals_never_write(string kind)
    {
        using var r=new Scene2HostCommandTests.Rig(open:false);var args=kind=="inline-source"?"""{"instance":"22222222222222222222222222222222","definition":{}}""":kind=="bad-source"?"""{"source":"../outside.json","placement":{}}""":"""{"instance":"22222222222222222222222222222222"}""";
        var result=Run(r,kind=="bad-source"?"instantiate":"update",args,kind=="--read-only"?["--save","--read-only"]:["--save"]);Assert.Equal(1,result.Code);Assert.NotNull(result.Reply!["error"]);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
    }
}
