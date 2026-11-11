using System.Text.Json;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;

public sealed class PlaySettingsStoreTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }=Directory.CreateTempSubdirectory("play-settings-").FullName;
        public string Project=>Path.Combine(Root,"project");public string Local=>Path.Combine(Root,"local");public string Tools=>Path.Combine(Root,"tools");
        public Fixture(){Directory.CreateDirectory(Project);Directory.CreateDirectory(Tools);}
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void Project_store_preserves_unrelated_JSON_and_missing_read_creates_nothing()
    {
        using var f=new Fixture();Assert.Null(PlaySettingsStore.Load(f.Project));Assert.False(Directory.Exists(Path.Combine(f.Project,".bed")));
        Directory.CreateDirectory(Path.Combine(f.Project,".bed"));var file=Path.Combine(f.Project,".bed","project.json");
        File.WriteAllText(file,"""{"scene":{"grid":2},"play":{"future":true}}""");
        PlaySettingsStore.Save(f.Project,new("game.exe",["--background"]));
        using var json=JsonDocument.Parse(File.ReadAllText(file));Assert.Equal(2,json.RootElement.GetProperty("scene").GetProperty("grid").GetInt32());
        Assert.True(json.RootElement.GetProperty("play").GetProperty("future").GetBoolean());
        Assert.Equal("game.exe",PlaySettingsStore.Load(f.Project)!.Program);Assert.False(Directory.Exists(f.Local));
    }
    [Fact]
    public void Malformed_project_is_never_silently_replaced()
    {
        using var f=new Fixture();Directory.CreateDirectory(Path.Combine(f.Project,".bed"));var file=Path.Combine(f.Project,".bed","project.json");
        File.WriteAllText(file,"broken");Assert.ThrowsAny<JsonException>(()=>PlaySettingsStore.Save(f.Project,new("game.exe")));
        Assert.Equal("broken",File.ReadAllText(file));
    }
    [Fact]
    public void In_project_Play_needs_no_extra_approval_and_missing_program_reaches_build_freshness_logic()
    {
        using var f=new Fixture();Assert.True(PlaySettingsResolver.TryResolve(f.Project,new("bin/game.exe",["--background"]),out var launch,out var why),why);
        Assert.Equal(Path.Combine(f.Project,"bin","game.exe"),launch!.Run.Executable);Assert.False(File.Exists(launch.Run.Executable));
        Assert.Null(new PlayApprovals(f.Local).NeedsApproval(launch,false));Assert.False(Directory.Exists(f.Local));
    }
    [Fact]
    public void External_values_need_local_exact_command_approval_and_argument_folder_root_changes_revoke_it()
    {
        using var f=new Fixture();var executable=Path.Combine(f.Tools,"game.exe");File.WriteAllText(executable,"self-authored inert fixture, never executed");
        PlayLaunch Resolve(string[] args,string folder)=>PlaySettingsResolver.TryResolve(f.Project,new(executable,args,folder),out var l,out var why)?l!:throw new Exception(why);
        var launch=Resolve(["one"],f.Tools);var approvals=new PlayApprovals(f.Local);
        Assert.Contains("Needs your OK",approvals.NeedsApproval(launch,false));Assert.Contains(executable,approvals.NeedsApproval(launch,false));
        approvals.Approve(launch,false);Assert.Null(new PlayApprovals(f.Local).NeedsApproval(launch,false));
        Assert.NotNull(approvals.NeedsApproval(Resolve(["two"],f.Tools),false));Assert.NotNull(approvals.NeedsApproval(Resolve(["one"],f.Project),false));
        Assert.NotNull(approvals.NeedsApproval(launch with{ProjectRoot=Directory.CreateDirectory(Path.Combine(f.Root,"another-project")).FullName},false));Assert.False(Directory.Exists(Path.Combine(f.Project,".bed")));
    }
    [Fact]
    public void Bare_build_tool_resolves_current_PATH_and_each_exact_build_requires_approval()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Tools,"tool.exe"),"inert");
        var settings=new PlaySettings("game.exe",build:new("tool",["build","sample.csproj"],["src/**/*.cs"]));
        Assert.True(PlaySettingsResolver.TryResolve(f.Project,settings,out var launch,out var why,f.Tools),why);
        Assert.Equal(Path.Combine(f.Tools,"tool.exe"),launch!.Build!.Executable);Assert.Equal(f.Project,launch.Build.WorkingDirectory);
        var approvals=new PlayApprovals(f.Local);Assert.Contains("sample.csproj",approvals.NeedsApproval(launch,true));approvals.Approve(launch,true);Assert.Null(approvals.NeedsApproval(launch,true));
        var second=Directory.CreateDirectory(Path.Combine(f.Root,"other-tools")).FullName;File.WriteAllText(Path.Combine(second,"tool.exe"),"inert");
        Assert.True(PlaySettingsResolver.TryResolve(f.Project,settings,out var changed,out _,second));Assert.NotNull(approvals.NeedsApproval(changed!,true));
        Assert.False(PlaySettingsResolver.TryResolve(f.Project,settings,out _,out _,""));
    }
    [Theory]
    [InlineData("../outside/*.cs")][InlineData("C:/outside/*.cs")]
    public void Build_inputs_cannot_walk_outside_the_project(string glob)
    {using var f=new Fixture();Assert.False(PlaySettingsResolver.TryResolve(f.Project,new("game.exe",build:new("",inputs:[glob])),out var launch,out var why));Assert.Null(launch);Assert.Contains("inside the project",why);}
    [Fact]
    public void Corrupt_local_approval_data_does_not_approve_external_execution()
    {
        using var f=new Fixture();Directory.CreateDirectory(f.Local);File.WriteAllText(Path.Combine(f.Local,"play-approvals.json"),"broken");
        Assert.True(PlaySettingsResolver.TryResolve(f.Project,new(Path.Combine(f.Tools,"game.exe")),out var launch,out _));
        Assert.NotNull(new PlayApprovals(f.Local).NeedsApproval(launch!,false));
    }
}
