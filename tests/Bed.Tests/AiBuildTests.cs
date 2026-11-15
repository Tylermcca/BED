using Bed.Editor;
using Xunit;
namespace Bed.Tests;
public sealed class AiBuildTests
{
    [Theory]
    [InlineData("Thing.cs(12,7): error CS0103: The name 'Missing' does not exist [game.csproj]", "Thing.cs",12,"CS0103")]
    [InlineData("C:\\My Project\\Thing.cs(1,2,1,3): error CS1002: ; expected [game.csproj]", "C:\\My Project\\Thing.cs",1,"CS1002")]
    public void Compiler_errors_retain_actual_location_and_plain_next_action(string text,string file,int line,string code)
    {
        var error=Assert.IsType<ProjectBuildError>(ProjectBuildCommand.ReadError(text));
        Assert.Equal(file,error.File);Assert.Equal(line,error.Line);Assert.Equal(code,error.Code);Assert.NotEmpty(error.What);Assert.NotEmpty(error.Try);
    }
    [Fact]
    public void Errors_without_a_location_and_non_errors_do_not_get_invented_locations()
    {
        var error=Assert.IsType<ProjectBuildError>(ProjectBuildCommand.ReadError("MSBUILD : error NETSDK1045: This SDK does not support the target."));
        Assert.Null(error.File);Assert.Null(error.Line);Assert.Contains("SDK",error.Try);
        Assert.Null(ProjectBuildCommand.ReadError("Thing.cs(1,2): warning CS0001: A warning"));
    }
    [Fact]
    public async Task Planted_sample_compile_error_has_file_line_what_and_try_and_never_runs_the_game()
    {
        var root=Path.Combine(Path.GetTempPath(),"bed-ai-build-"+Guid.NewGuid().ToString("N"));
        var project=Directory.CreateDirectory(Path.Combine(root,"project")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(project,"Broken.cs"),"public class Broken { public int Read() => MissingValue; }\n");
            File.WriteAllText(Path.Combine(project,"Fixture.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var dotnet=Environment.ProcessPath!;
            // Testhost isn't dotnet itself; use the SDK host already installed for this scoped test run.
            dotnet=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");
            var launch=new PlayLaunch(new(Path.Combine(project,"never-run.exe"),project,[]),new(dotnet,project,["build","Fixture.csproj","-c","Release","--nologo"]),project,[]);
            var approvals=new PlayApprovals(Path.Combine(root,"preferences"));
            var refused=await ProjectBuildCommand.RunAsync(launch,approvals,false);Assert.Null(refused.ExitCode);Assert.Contains("Needs your OK",refused.Summary);
            Assert.False(Directory.Exists(Path.Combine(project,"obj")));
            approvals.Approve(launch,true); // Explicit test-owned approval, never a real user's profile.
            var result=await ProjectBuildCommand.RunAsync(launch,approvals,false,timeout:TimeSpan.FromSeconds(30));
            Assert.False(result.Success);Assert.NotEqual(0,result.ExitCode);
            var error=Assert.Single(result.Errors,e=>e.Code=="CS0103");Assert.EndsWith("Broken.cs",error.File);Assert.Equal(1,error.Line);
            Assert.Contains("MissingValue",error.What);Assert.Contains("spelling",error.Try);
            var dirty=await ProjectBuildCommand.RunAsync(launch,approvals,true);Assert.Null(dirty.ExitCode);Assert.Contains("Save",dirty.Summary);
        }
        finally{Directory.Delete(root,true);}
    }
}
