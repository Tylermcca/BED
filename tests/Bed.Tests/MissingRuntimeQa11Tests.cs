using System.Diagnostics;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class MissingRuntimeQa11Tests
{
    private const int MissingFramework=unchecked((int)0x80008096);
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("missing-runtime-qa11-").FullName;
        public string Program=>Path.Combine(Root,"ProjectPlayTestGame.exe");
        public Fixture()
        {
            foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})
                File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(Root,"ProjectPlayTestGame"+suffix));
            File.WriteAllText(Path.Combine(Root,"saved.json"),"self-authored saved input");
        }
        public void Dispose(){Assert.Equal("self-authored saved input",File.ReadAllText(Path.Combine(Root,"saved.json")));Directory.Delete(Root,true);}
    }
    private static void Until(ProjectPlayHost host)
    {
        var watch=Stopwatch.StartNew();while((!host.HasCompleted||host.ExitCode is null)&&watch.Elapsed<TimeSpan.FromSeconds(5)){host.Update();Thread.Sleep(5);}host.Update();
        Assert.True(host.HasCompleted,host.Status);Assert.NotNull(host.ExitCode);
    }
    [Theory][InlineData("missing-runtime")][InlineData("missing-runtime-quiet")][InlineData("runtime-text-only")]
    public void Actual_Play_failure_names_missing_runtime_only_for_the_host_framework_code_and_keeps_the_real_exit(string mode)
    {
        using var fixture=new Fixture();var saved=File.ReadAllBytes(Path.Combine(fixture.Root,"saved.json"));
        var launch=new PlayLaunch(new(fixture.Program,fixture.Root,[mode]),null,fixture.Root,[]);
        using var host=new ProjectPlayHost(launch,TimeProvider.System,TimeSpan.FromSeconds(5));Assert.True(host.Start(false));Until(host);
        Assert.Equal(ProjectPlayState.Failed,host.State);Assert.Equal(mode=="runtime-text-only"?7:MissingFramework,host.ExitCode);
        var logs=host.DrainLogs();Assert.Contains(logs,l=>l.Error&&l.Text=="plain stderr");
        if(mode=="runtime-text-only"){Assert.Contains("code 7",host.Status);Assert.DoesNotContain("runtime is missing",host.Status);}
        else
        {
            Assert.Contains("compatible .NET runtime is missing",host.Status);Assert.Contains("Install",host.Status);Assert.DoesNotContain(MissingFramework.ToString(),host.Status);
            Assert.Contains(logs,l=>l.Error&&l.Text==host.Status);
            if(mode=="missing-runtime"){Assert.Contains("Microsoft.NETCore.App",host.Status);Assert.Contains("10.0.0",host.Status);Assert.Contains("x64",host.Status);}
        }
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(fixture.Root,"saved.json")));
    }
    [Fact]
    public void Missing_runtime_in_build_tool_is_plain_and_never_runs_the_game()
    {
        using var fixture=new Fixture();var input=Path.Combine(fixture.Root,"code.cs");File.WriteAllText(input,"self-authored input");File.SetLastWriteTimeUtc(input,new(2020,1,1,0,0,0,DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(fixture.Program,new(2019,1,1,0,0,0,DateTimeKind.Utc));var marker=Path.Combine(fixture.Root,"ran.txt");
        using var host=new ProjectPlayHost(new(new(fixture.Program,fixture.Root,["journey","--seconds","0",marker]),new(fixture.Program,fixture.Root,["missing-runtime"]),fixture.Root,["*.cs"]),TimeProvider.System,TimeSpan.FromSeconds(5));
        Assert.True(host.Start(false,true));Until(host);Assert.Equal(MissingFramework,host.ExitCode);Assert.Contains("Microsoft.NETCore.App",host.Status);Assert.False(File.Exists(marker));
        Assert.DoesNotContain(host.DrainLogs(),l=>l.Text=="Build succeeded.");
    }
    [Theory][InlineData("missing-runtime")][InlineData("missing-runtime-quiet")][InlineData("runtime-text-only")]
    public async Task Inventoried_export_smoke_explains_missing_runtime_and_never_grants_copy(string mode)
    {
        using var fixture=new Fixture();var input=CapturedExportTests.Input();var admission=CapturedExportTests.Plan(input,false).Admission;
        var output=Directory.CreateDirectory(Path.Combine(fixture.Root,"output")).FullName;
        var paths=new[]{"result.bin","ProjectPlayTestGame.exe","ProjectPlayTestGame.dll","ProjectPlayTestGame.deps.json","ProjectPlayTestGame.runtimeconfig.json"};
        File.WriteAllBytes(Path.Combine(output,"result.bin"),input.CopyPayload());foreach(var path in paths.Skip(1))File.Copy(Path.Combine(fixture.Root,path),Path.Combine(output,path));
        var mappings=paths.Select((path,i)=>new GameContentMapping("fixture-"+i,path,"self-authored-fixture")).ToArray();
        var declaration=admission.DeclareOutputs(GameBuildProfile.Create("fixture","Fixture.csproj",GameBuildTarget.WindowsX64,mappings),mappings.Select(map=>new BuildOutputItem(map,[])),[]);
        var plan=new GameExportPlan(admission,declaration);
        // Supply the checked fixture build inventory; SmokeAsync still starts and observes the real owned program.
        var inventory=BuildOutputInventory.Capture(output,declaration,Guid.NewGuid(),expectedHashes:admission.Source.Nodes.ToDictionary(node=>node.Output.Destination,node=>node.Sha256));
        var receipt=new GameBuildReceipt(admission.SnapshotHash,inventory.Manifest.Hash,output,0,true,"Self-authored fixture inventory.",inventory.Staging.Outputs);
        var before=paths.ToDictionary(path=>path,path=>File.ReadAllBytes(Path.Combine(output,path)));
        var result=await VerifiedGameBuild.SmokeAsync(plan,receipt,inventory.Manifest,new(Path.Combine(output,"ProjectPlayTestGame.exe"),output,[mode]),TimeSpan.FromSeconds(5));
        Assert.Null(result.Build);Assert.Contains(result.Logs,l=>l.Error&&l.Text=="plain stderr");
        if(mode=="runtime-text-only"){Assert.Contains("code 7",result.Reason);Assert.DoesNotContain("runtime is missing",result.Reason);}
        else{Assert.Contains("compatible .NET runtime is missing",result.Reason);Assert.Contains("Install",result.Reason);if(mode=="missing-runtime")Assert.Contains("Microsoft.NETCore.App",result.Reason);}
        foreach(var (path,bytes) in before)Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(output,path)));
    }
    [Fact]
    public void Real_Play_bridge_exposes_plain_runtime_failure_in_the_headless_UI_tree()
    {
        using var fixture=new Fixture();var data=Directory.CreateDirectory(Path.Combine(fixture.Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(fixture.Root,"schemas")).FullName;
        File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{}");
        PlaySettingsStore.Save(fixture.Root,new("ProjectPlayTestGame.exe",["missing-runtime"]));var settings=File.ReadAllBytes(Path.Combine(fixture.Root,".bed","project.json"));
        using var ui=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var workspace=new DataWorkspace(DataProject.Open(data,schemas));Assert.True(workspace.Show("item.json"));workspace.BindPlaySettings(fixture.Root,null,Path.Combine(fixture.Root,"local"));workspace.BindProjectPlay(new ProjectPlayBridge());
        ui.Frames(workspace.Draw,3);Assert.True(workspace.RequestProjectPlay());var session=workspace.ProjectPlay!;var watch=Stopwatch.StartNew();
        while((!session.HasCompleted||session.ExitCode is null)&&watch.Elapsed<TimeSpan.FromSeconds(5)){ui.Frame(workspace.Draw);Thread.Sleep(5);}ui.Frames(workspace.Draw,2);
        Assert.Equal(MissingFramework,session.ExitCode);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("compatible .NET runtime is missing",StringComparison.Ordinal)&&n.Label.Contains("Microsoft.NETCore.App",StringComparison.Ordinal));
        Assert.Equal(settings,File.ReadAllBytes(Path.Combine(fixture.Root,".bed","project.json")));Assert.False(File.Exists(Path.Combine(fixture.Root,"local","play-approvals.json")));
    }
}
