using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
namespace Brutal.Authoring.Tests;

public sealed class BedStartupPerf07Tests(ITestOutputHelper output)
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Real_BED_process_reports_first_offscreen_frame_for_start_screen_and_saved_level(bool withProject)
    {
        var root=Directory.CreateTempSubdirectory("bed-startup-perf07-").FullName;
        try
        {
            string? project=null;var inputs=new Dictionary<string,byte[]>();
            if(withProject)
            {
                project=Directory.CreateDirectory(Path.Combine(root,"project")).FullName;
                var source=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
                foreach(var relative in new[]{"reference_map.json","data/level.json","schemas/scene.schema.json"}
                    .Concat(Directory.GetFiles(Path.Combine(source,"assets"),"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(source,path))))
                {
                    var path=Path.Combine(project,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var bytes=File.ReadAllBytes(Path.Combine(source,relative));File.WriteAllBytes(path,bytes);inputs.Add(path,bytes);
                }
            }
            var shot=Path.Combine(root,"screen.png");
            var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"BED.exe"))
                {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            if(project is not null)start.ArgumentList.Add(project);
            foreach(var arg in new[]{"--settings",Path.Combine(root,"local"),"--shot",shot})start.ArgumentList.Add(arg);
            start.Environment["BED_HEADLESS_STARTUP_TIMING"]="1";
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));var text=await stdout;var errors=await stderr;
                Assert.True(process.ExitCode==0,text+errors);Assert.Equal("",errors);
                var line=Assert.Single(text.Split('\n',StringSplitOptions.RemoveEmptyEntries),line=>line.StartsWith("BED_STARTUP_TIMING ",StringComparison.Ordinal));
                using var record=JsonDocument.Parse(line["BED_STARTUP_TIMING ".Length..]);var result=record.RootElement;
                Assert.Equal(process.Id,result.GetProperty("processId").GetInt32());Assert.NotEqual(Environment.ProcessId,process.Id);
                Assert.InRange(result.GetProperty("frames").GetInt32(),1,4);Assert.True(result.GetProperty("vertices").GetInt32()>0);
                Assert.Equal(1600,result.GetProperty("width").GetInt32());Assert.Equal(900,result.GetProperty("height").GetInt32());
                var total=result.GetProperty("processToFirstFrameMs").GetDouble();var before=result.GetProperty("beforeHeadlessHostMs").GetDouble();
                var phases=result.GetProperty("phases");var sum=phases.EnumerateObject().Sum(phase=>phase.Value.GetDouble());
                Assert.True(total>0);Assert.InRange(total-before-sum,0,25);
                output.WriteLine($"{(withProject?"Saved level project":"No project/start screen")}: process to first drawn offscreen frame {total:F2} ms; pre-host {before:F2} ms; frames={result.GetProperty("frames").GetInt32()}.");
                foreach(var phase in phases.EnumerateObject())output.WriteLine($"  {phase.Name}: {phase.Value.GetDouble():F2} ms");
                var preparation=text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(line=>line.StartsWith("BED_STARTUP_PHASE ",StringComparison.Ordinal)).ToArray();
                Assert.Equal(withProject?2:0,preparation.Length);
                foreach(var phase in preparation)
                {
                    using var step=JsonDocument.Parse(phase["BED_STARTUP_PHASE ".Length..]);
                    output.WriteLine($"  {step.RootElement.GetProperty("name").GetString()}: {step.RootElement.GetProperty("milliseconds").GetDouble():F2} ms (within pre-host time).");
                }
                Assert.Contains("Drew the screen into",text);var png=File.ReadAllBytes(shot);Assert.True(png.Length>1000);Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png.Take(8));
                if(withProject)
                {
                    Assert.Contains("Opened project with SceneStarter",text);
                    var assets=Brutal.Authoring.Runtime.ProjectAssets.Open(project!);Assert.Equal(4,assets.ArtifactNames.Count);
                    Assert.True(assets.ContainsAsset("c8a7e000000000000000000000000001"));
                    Assert.Equal(8,assets.ReadTexture(Guid.ParseExact("c8a7e000000000000000000000000002","N")).Width);
                }
                else
                {
                    Assert.DoesNotContain("Opened project with",text);
                    start.Environment.Remove("BED_HEADLESS_STARTUP_TIMING");
                    using var ordinary=Process.Start(start)!;var quietOutput=ordinary.StandardOutput.ReadToEndAsync();var quietError=ordinary.StandardError.ReadToEndAsync();
                    try
                    {
                        await ordinary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.Equal(0,ordinary.ExitCode);
                        var normal=await quietOutput;Assert.DoesNotContain("BED_STARTUP_",normal);Assert.Contains("Drew the screen into",normal);Assert.Equal("",await quietError);
                    }
                    finally{if(!ordinary.HasExited){ordinary.Kill(true);await ordinary.WaitForExitAsync();}}
                }
            }
            finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
            foreach(var (path,bytes) in inputs)Assert.Equal(bytes,File.ReadAllBytes(path));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),root,StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
}
