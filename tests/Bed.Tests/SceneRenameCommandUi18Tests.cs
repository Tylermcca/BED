using Bed.Agent;
using Bed.Data;
using Xunit;
namespace Bed.Tests;
public sealed class SceneRenameCommandUi18Tests
{
    [Theory]
    [InlineData("")][InlineData("Bad")][InlineData("bad-id")][InlineData("two words")][InlineData("valid\n")][InlineData("éclair")][InlineData("1name")][InlineData("taken")][InlineData(null)]
    public void Invalid_or_existing_id_is_refused_before_a_scene_is_returned(string? next)
    {
        var scene=new Scene([SceneObject.At("old",null,default),SceneObject.At("taken",null,default)]);
        var result=new RenameObject("old",next!).Apply(scene);Assert.False(result.Ok);Assert.Null(result.Document);Assert.NotEmpty(result.Issues);
        Assert.Equal(new[]{"old","taken"},scene.Objects.Select(o=>o.Id));
    }
    [Fact] public void Missing_source_is_refused_and_catalog_serializes_the_approved_v1_arguments()
    {
        var command=new RenameObject("old","new_id");Assert.False(command.Apply(Scene.Empty).Ok);
        var entry=SceneKit.Catalog().ToEntry(command);Assert.Equal("scene.rename",entry.Command);Assert.Equal(1,entry.Version);
        Assert.Equal("old",entry.Args!.Value.GetProperty("id").GetString());Assert.Equal("new_id",entry.Args.Value.GetProperty("newId").GetString());
    }
    [Fact] public void Agent_description_lists_the_same_scene_catalog_as_the_CLI_and_states_its_execution_scope()
    {
        var root=Directory.CreateTempSubdirectory("rename-description-ui18-").FullName;
        try
        {
            var data=Directory.CreateDirectory(Path.Combine(root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(root,"schemas")).FullName;
            var description=new AgentSession(DataProject.Open(data,schemas)).Describe();var tool=description["sceneTool"]!;
            Assert.Contains("SceneTool",tool["note"]!.GetValue<string>());Assert.Contains("scene do",tool["invoke"]!.GetValue<string>());
            var commands=tool["commands"]!.AsArray();Assert.Equal(SceneKit.Catalog().Commands.Count,commands.Count);
            var rename=Assert.Single(commands,n=>n!["id"]!.GetValue<string>()=="scene.rename")!;
            Assert.Equal(1,rename["version"]!.GetValue<int>());Assert.Equal("Rename object",rename["label"]!.GetValue<string>());
        }
        finally{Directory.Delete(root,true);}
    }
}
