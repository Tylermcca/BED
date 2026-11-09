using Brutal.Authoring.Editor;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class GameBuildProfileTests
{
    private static GameContentMapping Map(string id="level",string path="content/level.bin")=>new(id,path,"scene-v1");
    [Fact] public void Structured_profile_copies_mapping_and_has_no_command_or_live_path_side_effect()
    {
        var rows=new[]{Map("z","content/z.bin"),Map("a","content/a.bin")};var p=GameBuildProfile.Create("desktop","src/Game/Game.csproj",GameBuildTarget.WindowsX64,rows);
        rows[0]=Map("changed","changed.bin");Assert.Equal("a",p.Content[0].InputId);Assert.Equal("z",p.Content[1].InputId);
        Assert.Equal("win-x64",p.RuntimeIdentifier);Assert.Equal("Release",p.Configuration);Assert.Equal(PlayProtocol.Version,p.ProtocolVersion);
    }
    [Theory]
    [InlineData("../escape.csproj")][InlineData("C:/Game.csproj")][InlineData("/Game.csproj")][InlineData("src\\Game.csproj")]
    [InlineData("src//Game.csproj")][InlineData("src/CON.csproj")][InlineData("src/game.csproj ")][InlineData("dotnet build")]
    public void Invalid_project_declarations_refuse(string path)=>Assert.Throws<ArgumentException>(()=>GameBuildProfile.Create("desktop",path,GameBuildTarget.WindowsX64,[]));
    [Theory]
    [InlineData("../data.bin")][InlineData("content/AUX.txt")][InlineData("content/file:stream")][InlineData("content/trailing.")]
    public void Invalid_output_declarations_refuse(string path)=>Assert.Throws<ArgumentException>(()=>GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,[Map(path:path)]));
    [Fact] public void Collision_and_file_directory_alias_refuse()
    {
        foreach(var rows in new[]{new[]{Map("a","a.bin"),Map("b","A.bin")},new[]{Map("a","content"),Map("b","content/a.bin")},new[]{Map("same","a.bin"),Map("same","b.bin")}})
            Assert.Throws<ArgumentException>(()=>GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,rows));
    }
    [Fact] public void Unknown_target_and_unbounded_mapping_refuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>GameBuildProfile.Create("desktop","Game.csproj",(GameBuildTarget)99,[]));
        Assert.Throws<ArgumentException>(()=>GameBuildProfile.Create("desktop","Game.csproj",GameBuildTarget.WindowsX64,Enumerable.Range(0,1025).Select(i=>Map("x"+i,"x"+i))));
    }
}
