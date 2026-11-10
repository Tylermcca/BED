using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Launcher;
using Brutal.Authoring.Runtime;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ReleaseExportProbeTests
{
    private static Dictionary<string,string> Hashes(string project)=>Directory.EnumerateFiles(project,"*",SearchOption.AllDirectories)
        .Where(p=>
        {
            var relative=Path.GetRelativePath(project,p).Replace('\\','/');
            // Existing startup and export owners create only these cache allocations; source/settings remain pinned.
            return !relative.StartsWith(".bed/cache/",StringComparison.Ordinal)&&!relative.StartsWith("data/.bed/cache/export/",StringComparison.Ordinal);
        })
        .ToDictionary(p=>Path.GetRelativePath(project,p),p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    [Fact]
    public void Real_crate_project_exports_through_the_workspace_verifies_copies_and_runs_without_BED()
    {
        using var temporary=new CapturedExportTests.Owned();var project=ReleasePlayProbeTests.CopyProject(temporary.Root);
        var before=Hashes(project);var destination=Path.Combine(temporary.Root,"export with spaces");
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(0,Program.Run(["--probe-export",project,"--destination",destination,"--timeout","30"],output,errors,"unused-export-settings"));
        Assert.Equal("",errors.ToString());using var json=JsonDocument.Parse(output.ToString());var result=json.RootElement;
        Assert.Equal("passed",result.GetProperty("status").GetString());Assert.True(result.GetProperty("verified").GetBoolean());Assert.True(result.GetProperty("copied").GetBoolean());
        Assert.Equal("level.json",result.GetProperty("document").GetString());Assert.Equal(destination,result.GetProperty("destination").GetString());
        Assert.Equal(17,result.GetProperty("fileCount").GetInt32());Assert.Equal(17,result.GetProperty("files").GetArrayLength()); // EX13 adds README.txt
        var assets=ProjectAssets.Open(destination);var part=Assert.Single(assets.ReadMesh("c8a7e000000000000000000000000001").Parts);
        var texture=assets.ReadTexture(part.Material!.Texture!.Value);Assert.Equal(8,texture.Width);Assert.Equal(8,texture.Height);
        foreach(var file in result.GetProperty("files").EnumerateArray())
        {
            var path=Path.Combine(destination,file.GetProperty("path").GetString()!);
            Assert.Equal(file.GetProperty("length").GetInt64(),new FileInfo(path).Length);
            Assert.Equal(file.GetProperty("sha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
        var start=new ProcessStartInfo(Path.Combine(destination,"SceneStarter.exe")){WorkingDirectory=destination,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"--project",".","--seconds","0"})start.ArgumentList.Add(value);
        using var game=Process.Start(start)!;
        try
        {Assert.True(game.WaitForExit(10000));Assert.Equal(0,game.ExitCode);Assert.Contains("loaded saved level: 3 objects, 1 meshes.",game.StandardOutput.ReadToEnd());Assert.Equal("",game.StandardError.ReadToEnd());}
        finally{if(!game.HasExited)game.Kill(true);}
        Assert.Equal(before.OrderBy(p=>p.Key),Hashes(project).OrderBy(p=>p.Key));
        Assert.False(File.Exists(Path.Combine(project,".bed","project.json")));Assert.False(File.Exists(Path.Combine(project,"play-approvals.json")));
    }
    [Fact]
    public void Published_project_build_uses_the_project_owned_game_program_and_working_folder()
    {
        using var temporary=new CapturedExportTests.Owned();var project=ReleasePlayProbeTests.CopyProject(temporary.Root);
        using var headless=new Brutal.Authoring.Editor.ImGui.HeadlessHost(){Drawing=false};
        var loaded=Brutal.Authoring.Editor.ImGui.Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);
        Assert.Empty(loaded.Problems);var adapter=Assert.Single(loaded.Loaded);
        var paths=Brutal.Authoring.Editor.ImGui.ProjectPaths.In(project)!;Assert.NotNull(adapter.DefaultPlay(paths));
        using var workspace=new Brutal.Authoring.Editor.ImGui.DataWorkspace(adapter.Open(paths));var build=adapter.Build(workspace)!;
        Assert.Equal(Path.Combine(project,"game","SceneStarter.exe"),build.Executable);Assert.Equal(Path.Combine(project,"game"),build.WorkingDirectory);
        Assert.Equal(new[]{"--build","{input}","{output}"},build.Arguments);Assert.True(File.Exists(build.Executable));
        var exportRuntime=Path.Combine(project,"game","export");Directory.CreateDirectory(exportRuntime);
        foreach(var file in SceneStarter.ExportInventory.RuntimeFiles)File.Copy(Path.Combine(project,"game",file),Path.Combine(exportRuntime,file));
        var explicitBuild=adapter.Build(workspace)!;
        Assert.Equal(Path.Combine(exportRuntime,"SceneStarter.exe"),explicitBuild.Executable);Assert.Equal(exportRuntime,explicitBuild.WorkingDirectory);
    }
    [Fact]
    public void Nonempty_destination_refuses_before_opening_the_project_and_preserves_existing_files()
    {
        using var temporary=new CapturedExportTests.Owned();var target=Path.Combine(temporary.Root,"occupied");Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target,"keep.txt"),"keep me");using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(["--probe-export","missing-project","--destination",target],output,errors,"unused-export-settings"));
        Assert.Equal("",output.ToString());Assert.Contains("new or empty destination",errors.ToString());Assert.Equal("keep me",File.ReadAllText(Path.Combine(target,"keep.txt")));Assert.Single(Directory.GetFiles(target));
    }
    [Theory]
    [InlineData("--probe-export")]
    [InlineData("--probe-export|project")]
    [InlineData("--probe-export|project|--destination|out|--timeout|0")]
    [InlineData("--probe-export|project|--destination|out|--timeout|301")]
    [InlineData("--probe-export|project|--destination|out|--destination|other")]
    public void Malformed_probe_arguments_are_a_plain_failure_with_no_success_evidence(string arguments)
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(1,Program.Run(arguments.Split('|'),output,errors,"unused-export-settings"));Assert.Equal("",output.ToString());Assert.Contains("usage: BED --probe-export",errors.ToString());
    }
}
