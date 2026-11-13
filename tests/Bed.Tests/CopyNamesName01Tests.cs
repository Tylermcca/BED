using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class CopyNamesName01Tests:IDisposable
{
    private readonly CopyNameStyle previous=CopyNames.Style;
    public CopyNamesName01Tests()=>CopyNames.Style=CopyNameStyle.Parentheses;
    public void Dispose()=>CopyNames.Style=previous;
    [Theory]
    [InlineData(CopyNameStyle.Parentheses,"Cube (1)")]
    [InlineData(CopyNameStyle.Dot,"Cube.001")]
    [InlineData(CopyNameStyle.Underscore,"Cube_1")]
    [InlineData(CopyNameStyle.Number,"Cube2")]
    [InlineData(CopyNameStyle.Copy,"Cube copy")]
    public void Each_style_uses_the_next_free_name_without_changing_identities(CopyNameStyle style,string first)
    {
        Assert.Equal(first,CopyNames.Next("Cube",["Cube"],style));
        Assert.Equal(first,CopyNames.Example(style));
        var next=CopyNames.Next(first,["Cube",first],style);
        Assert.DoesNotContain(first+first,next);
        Assert.Equal(style switch{CopyNameStyle.Parentheses=>"Cube (2)",CopyNameStyle.Dot=>"Cube.002",CopyNameStyle.Underscore=>"Cube_2",CopyNameStyle.Number=>"Cube3",_=>"Cube copy 2"},next);
    }
    [Theory]
    [InlineData("Cube (1)")][InlineData("Cube.001")][InlineData("Cube_1")][InlineData("Cube2")][InlineData("Cube copy")][InlineData("Cube copy 3")][InlineData("Cube (1) copy 3")]
    public void Existing_styles_are_recognised_and_legacy_stacks_are_removed(string source)
        =>Assert.Equal("Cube.001",CopyNames.Next(source,[],CopyNameStyle.Dot));
    [Fact]
    public void First_free_number_is_used_across_styles_with_case_insensitive_collisions()
        =>Assert.Equal("Cube (2)",CopyNames.Next("Cube.003",["cube","cube copy","Cube.003"]));
    [Fact]
    public void Duplicate_and_clipboard_keep_the_id_policy_links_one_undo_and_frozen_name_on_replay()
    {
        var cube=SceneObject.At("cube",null,Vector3.Zero);cube=cube with{Properties=cube.Properties.Add("name",JsonSerializer.SerializeToElement("Cube"))};
        var initial=new Scene([cube]);var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),initial);
        var editor=new EditorModel<Scene>(session,SceneEditing.Outline);editor.Select("cube");CopyNames.Style=CopyNameStyle.Dot;
        Assert.True(SceneObjectOperations.Duplicate(editor,new DuplicateGroup(["cube"]),out var why),why);
        Assert.Equal("Cube.001",CopyNames.NameOf(session.Current.Find("copy_cube")!));Assert.Equal("copy_cube",editor.Primary);Assert.Equal(1,session.History.UndoCount);
        var clipboard=new SceneClipboard();Assert.True(clipboard.Copy(editor).Ok);Assert.True(clipboard.Paste(editor).Ok);
        Assert.Equal("Cube.002",CopyNames.NameOf(session.Current.Find("copy_copy_cube")!));
        CopyNames.Style=CopyNameStyle.Copy;var replay=DocumentSession<Scene>.Replay(initial,session.Journal,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
        Assert.Equal(session.TextToSave,replay.TextToSave);
    }
    [Fact]
    public void Shared_scene2_host_records_copy_labels_without_changing_identity_or_replay()
    {
        using var r=new Scene2HostCommandTests.Rig();CopyNames.Style=CopyNameStyle.Dot;
        var before=r.Entry.Session.Current;var owner=Scene2Resolver.Resolve(before).Objects.Single(o=>o.Object.Id=="owner").Object;
        Assert.True(r.Host.CaptureScene2Edit("level.json","duplicate",JsonSerializer.SerializeToElement(new{ids=new[]{"owner"}}),out var plan,out var why),why);
        Assert.True(r.Host.CommitScene2Edit("level.json",plan!,out why),why);
        var copied=Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id=="copy_owner").Object;
        Assert.Equal(CopyNames.Next(CopyNames.NameOf(owner),Scene2Resolver.Resolve(before).Objects.Select(o=>CopyNames.NameOf(o.Object))),CopyNames.NameOf(copied));
        Assert.Single(r.Entry.Session.Journal);CopyNames.Style=CopyNameStyle.Copy;
        var replay=DocumentSession<Scene2>.Replay(before,r.Entry.Session.Journal,new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());
        Assert.Equal(r.Entry.Session.TextToSave,replay.TextToSave);
    }

    [Fact]
    public void Optional_placement_label_round_trips_as_a_plain_name_and_duplicate_and_paste_record_it()
    {
        using var r=new Scene2HostCommandTests.Rig();var format=new Scene2Format();
        var json=System.Text.Json.Nodes.JsonNode.Parse(r.Original)!;json["instances"]![0]!["placement"]!["label"]="Cube / display only";
        var scene=format.Read(json.ToJsonString());Assert.Equal("Cube / display only",scene.Instances[0].Placement.Properties["name"].GetString());
        var written=format.Write(scene);Assert.Equal(written,format.Write(format.Read(written)));
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
        var schemas=new JsonSchemas().Add("scene",File.ReadAllText(Path.Combine(repo!.FullName,"schemas/scene-v2.schema.json")));
        Assert.Empty(schemas.Validate(JsonDocument.Parse(written).RootElement,"scene"));
        Assert.Equal("Cube / display only",System.Text.Json.Nodes.JsonNode.Parse(written)!["instances"]![0]!["placement"]!["label"]!.GetValue<string>());
        var before=r.Entry.Session.Current;var instance=before.Instances[0];var resolved=Scene2Resolver.Resolve(before);
        var ids=resolved.Objects.Where(o=>resolved.Origins[o.Object.Id].InstanceKey==instance.Key).ToDictionary(o=>o.Object.Id,o=>"new_"+o.Object.Id);
        Assert.True(r.Host.CaptureScene2Edit("level.json","duplicate",JsonSerializer.SerializeToElement(new{instance=instance.Key,newKey=new string('e',32),idMap=ids}),out var plan,out var why),why);
        Assert.True(r.Host.CommitScene2Edit("level.json",plan!,out why),why);
        Assert.Equal("frame_a (1)",Scene2Editing.Outline.Label(r.Entry.Session.Current,"new_frame_a"));
        foreach(var node in instance.NodeIds)
        {
            var sourceLabel=Scene2Editing.Outline.Label(before,node.Value)!;
            Assert.Equal(sourceLabel+" (1)",Scene2Editing.Outline.Label(r.Entry.Session.Current,ids[node.Value]));
        }
        var replay=DocumentSession<Scene2>.Replay(before,r.Entry.Session.Journal,format,new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(r.Entry.Session.TextToSave,replay.TextToSave);
        json["instances"]![0]!["placement"]!["label"]=42;Assert.Throws<DocumentFormatException>(()=>format.Read(json.ToJsonString()));
    }
    [Fact]
    public void Old_file_without_placement_label_reads_and_saves_byte_for_byte_without_adding_it()
    {
        using var r=new Scene2HostCommandTests.Rig();var before=File.ReadAllBytes(r.Disk);
        Assert.False(r.Entry.Session.Current.Instances[0].Placement.Properties.ContainsKey("name"));
        Assert.True(r.Host.SaveScene2("level.json",out var why),why);
        Assert.Equal(before,File.ReadAllBytes(r.Disk));Assert.DoesNotContain("\"label\"",System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(r.Disk))!["instances"]![0]!["placement"]!.ToJsonString());
    }

    [Fact]
    public void Options_live_example_saves_and_reloads_the_same_rule_for_non_window_consumers()
    {
        var root=Directory.CreateTempSubdirectory("copy-options-").FullName;
        try
        {
            var settings=Path.Combine(root,"settings.json");File.WriteAllText(settings,"{\"future\":true}");
            using(var host=new HeadlessHost(1200,900){Drawing=false})
            using(var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"recent.json"))))
            {
                EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());host.Frames(launcher.Draw,4);
                host.Press(launcher.Draw,ImGuiKey.Comma,ctrl:true);host.Frames(launcher.Draw,3);
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Options");
                var combo=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Combo&&n.Label=="Names for copies");
                host.Click(launcher.Draw,combo.Bounds.X+10,combo.Bounds.Y+10);host.Frames(launcher.Draw,2);
                var dot=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="Cube.001");
                var action=Ui.RequestAction(dot.Id,UiActions.Select);host.Frames(launcher.Draw,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);
                Assert.Equal(CopyNameStyle.Dot,CopyNames.ReadStyle(settings));
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Example: Cube becomes Cube.001");
            }
            CopyNames.Style=CopyNameStyle.Copy;
            using(var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"recent.json"))))Assert.Equal(CopyNameStyle.Dot,CopyNames.Style);
            Assert.True(JsonDocument.Parse(File.ReadAllText(settings)).RootElement.GetProperty("future").GetBoolean());
        }
        finally{Directory.Delete(root,true);}
    }
}
