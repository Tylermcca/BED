using System.Text.Json;
using System.Security.Cryptography;
using Bed.Runtime;
using Bed.Launcher;
using Bed.Editor;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ReleasePlayProbeTests
{
    internal static string CopyProject(string root)
    {
        var project=Path.Combine(root,"project with spaces");
        foreach(var relative in new[]{"reference_map.json","data/level.json","schemas/scene.schema.json"})
        {
            var destination=Path.Combine(project,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","scene-starter",relative),destination);
        }
        // The shipped level places the crate. Copy its complete authored source closure so normal
        // startup can publish the same model/texture generation as the actual package.
        var assets=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter","assets");
        foreach(var path in Directory.EnumerateFiles(assets,"*",SearchOption.AllDirectories))
        {
            var destination=Path.Combine(project,"assets",Path.GetRelativePath(assets,path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination);
        }
        Directory.CreateDirectory(Path.Combine(project,"game"));
        foreach(var name in SceneStarter.ExportInventory.Required.Where(name=>name!="scene.json"))
            File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(project,"game",name));
        return project;
    }
    private static Dictionary<string,string> SourceHashes(string project) => Directory.EnumerateFiles(Path.Combine(project,"assets"),"*",SearchOption.AllDirectories)
        .ToDictionary(path=>Path.GetRelativePath(project,path),path=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    [Theory]
    [InlineData("configured")][InlineData("probe")][InlineData("quoted")]
    public void Real_startup_sample_loads_saved_files_and_exits_zero_without_changing_saved_input(string mode)
    {
        using var temporary=new CapturedExportTests.Owned();var project=CopyProject(temporary.Root);
        string[] configured=mode=="configured"?["--project",".","--seconds","0"]:mode=="quoted"?["--seconds","0"]:["--project","."];
        PlaySettingsStore.Save(project,new("game/SceneStarter.exe",configured));
        var settings=File.ReadAllBytes(Path.Combine(project,".bed","project.json"));
        var level=Path.Combine(project,"data","level.json");
        var before=File.ReadAllBytes(level);var sources=SourceHashes(project);
        using var output=new StringWriter();using var errors=new StringWriter();
        string[] args=mode=="configured"?["--probe-play",project,"--timeout","10"]:
            ["--probe-play",project,"--timeout","10","--probe-args",mode=="quoted"?"--project \""+project+"\"":"--seconds 0"];
        Assert.Equal(0,Program.Run(args,output,errors,"unused-probe-settings"));
        Assert.Equal("",errors.ToString());
        var line=Assert.Single(output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries));
        using var json=JsonDocument.Parse(line);
        Assert.Equal("passed",json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("completed").GetBoolean());
        Assert.Equal(0,json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("The process exited normally (code 0).",json.RootElement.GetProperty("processStatus").GetString());
        Assert.Contains(json.RootElement.GetProperty("output").EnumerateArray(),line=>
            !line.GetProperty("error").GetBoolean()&&line.GetProperty("text").GetString()=="SceneStarter loaded saved level: 3 objects, 1 meshes.");
        Assert.Equal(settings,File.ReadAllBytes(Path.Combine(project,".bed","project.json")));
        Assert.Equal("level.json",json.RootElement.GetProperty("document").GetString());
        Assert.Equal(before,File.ReadAllBytes(level));
        Assert.Equal(sources.OrderBy(p=>p.Key),SourceHashes(project).OrderBy(p=>p.Key));
        var generation=ProjectAssets.Open(project);
        var crate=Assert.Single(generation.ReadMesh("c8a7e000000000000000000000000001").Parts);
        Assert.Equal(Guid.ParseExact("c8a7e000000000000000000000000002","N"),crate.Material!.Texture);
        var texture=generation.ReadTexture(crate.Material.Texture!.Value);Assert.Equal(8,texture.Width);Assert.Equal(8,texture.Height);
    }
    [Fact]
    public void Nonzero_game_exit_is_a_probe_failure_without_a_success_record_or_saved_arguments()
    {
        using var temporary=new CapturedExportTests.Owned();var project=CopyProject(temporary.Root);
        PlaySettingsStore.Save(project,new("game/SceneStarter.exe",["--project","."]));
        var settings=File.ReadAllBytes(Path.Combine(project,".bed","project.json"));
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-play",project,"--probe-args","--seconds 0 --exit-code 7"],output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("code 7",errors.ToString());
        Assert.Equal(settings,File.ReadAllBytes(Path.Combine(project,".bed","project.json")));
    }
    [Fact]
    public void Probe_arguments_do_not_approve_an_external_configured_program()
    {
        using var temporary=new CapturedExportTests.Owned();var project=CopyProject(temporary.Root);
        PlaySettingsStore.Save(project,new(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter","SceneStarter.exe"),["--project",project]));
        var settings=File.ReadAllBytes(Path.Combine(project,".bed","project.json"));
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-play",project,"--probe-args","--seconds 0"],output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("Needs your OK",errors.ToString());
        Assert.Equal(settings,File.ReadAllBytes(Path.Combine(project,".bed","project.json")));
        Assert.False(File.Exists(Path.Combine(project,".bed","play-approvals.json")));
    }
    [Fact]
    public void Missing_project_fails_without_a_success_record()
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,PlayProbe.Run(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")),TimeSpan.FromSeconds(5),output,errors));
        Assert.Equal("",output.ToString());Assert.Contains("Play probe failed:",errors.ToString());
    }
    [Fact]
    public void Invalid_deadline_fails_before_startup()
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,PlayProbe.Run("unused",TimeSpan.Zero,output,errors));
        Assert.Equal("",output.ToString());Assert.Contains("timeout",errors.ToString());
    }
    [Theory]
    [InlineData("--probe-play")]
    [InlineData("--probe-play", "project", "--timeout", "0")]
    [InlineData("--probe-play", "project", "--timeout", "no")]
    [InlineData("--probe-play", "project", "--shot", "image.png")]
    [InlineData("--probe-play", "project", "--probe-args")]
    [InlineData("--probe-play", "project", "--timeout", "10", "--timeout", "20")]
    [InlineData("--probe-play", "project", "--probe-args", "--seconds 0", "--probe-args", "extra")]
    [InlineData("--probe-play", "project", "--probe-args", "nul\0value")]
    public void Probe_arguments_fail_without_opening_a_window(params string[] args)
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(args,output,errors,"unused-probe-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("usage: BED --probe-play",errors.ToString());
    }

}
