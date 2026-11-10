using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Launcher;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ApprovalRestartQa10Tests(ITestOutputHelper output)
{
    private sealed class Fixture:IDisposable
    {
        public readonly string Temporary=Directory.CreateTempSubdirectory("approval-restart-qa10-").FullName;
        public string Root=>Path.Combine(Temporary,"project with spaces");
        public string Local=>Path.Combine(Temporary,"local preferences");
        public Fixture()
        {
            foreach(var folder in new[]{"data","schemas","src","game","tool"})Directory.CreateDirectory(Path.Combine(Root,folder));
            File.WriteAllText(Path.Combine(Root,"data","item.json"),"{}");File.WriteAllText(Path.Combine(Root,"schemas","item.schema.json"),"{}");
            File.WriteAllText(Path.Combine(Root,"src","input.cs"),"self-authored build input");File.SetLastWriteTimeUtc(Path.Combine(Root,"src","input.cs"),new(2020,1,1,0,0,0,DateTimeKind.Utc));
            foreach(var folder in new[]{"tool","game"})
                foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})
                    File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(Root,folder,"ProjectPlayTestGame"+suffix));
            PlaySettingsStore.Save(Root,new("game/ProjectPlayTestGame.exe",["journey","--seconds","0",Path.Combine(Root,"ran.txt")],".",
                new("tool/ProjectPlayTestGame.exe",["build",Path.Combine(Root,"game"),"token-A"],["src/**/*.cs"])));
        }
        public void Dispose(){Directory.Delete(Temporary,true);}
    }
    private sealed record Evidence(int ProcessId,string Phase,string Command,string? RevisedQuestion,int Runs);
    private static void Stale(string root)=>File.SetLastWriteTimeUtc(Path.Combine(root,"game","ProjectPlayTestGame.exe"),new(2019,1,1,0,0,0,DateTimeKind.Utc));
    private static Evidence Run(string temporary,string phase)
    {
        var root=Path.Combine(temporary,"project with spaces");var local=Path.Combine(temporary,"local preferences");
        var approval=Path.Combine(local,"play-approvals.json");var settingsPath=Path.Combine(root,".bed","project.json");
        var settingsBytes=File.ReadAllBytes(settingsPath);var sourceBytes=File.ReadAllBytes(Path.Combine(root,"src","input.cs"));
        using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        using var launcher=Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(local,"recent.json"))));
        Assert.True(launcher.OpenFolder(root),launcher.Said);var workspace=launcher.Workspace!;Assert.True(workspace.Show("item.json"));host.Frames(workspace.Draw,3);
        Assert.True(workspace.PlaySettings!.TryLaunch(out var launch,out var why),why);var command=launch!.Build!.CommandLine();
        var approved=phase=="restart"?File.ReadAllBytes(approval):null;
        if(phase=="approve")Assert.False(File.Exists(approval));
        Stale(root);workspace.RequestProjectPlay(true);host.Frames(workspace.Draw,2);
        if(phase=="approve")
        {
            Assert.True(workspace.ProjectPlayApprovalPending);Assert.Null(workspace.ProjectPlay);
            Assert.False(File.Exists(Path.Combine(root,"ran.txt")));Assert.False(File.Exists(approval));
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains(command,StringComparison.Ordinal));
            var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Allow this exact command and continue");
            Assert.True(button.Enabled);Assert.False(button.Bounds.IsEmpty);
            Assert.True(button.Focused,Ui.Snapshot().Text);host.Press(workspace.Draw,ImGuiKey.Enter);host.Frames(workspace.Draw,2);
        }
        Assert.False(workspace.ProjectPlayApprovalPending,workspace.Said+"\n"+Ui.Snapshot().Text);var session=workspace.ProjectPlay;Assert.NotNull(session);
        var watch=Stopwatch.StartNew();
        while((!session.HasCompleted||session.ExitCode is null)&&watch.Elapsed<TimeSpan.FromSeconds(8)){host.Frame(workspace.Draw);Thread.Sleep(5);}
        Assert.True(session.HasCompleted,session.Status);Assert.Equal("The process exited normally (code 0).",session.Status);Assert.Equal(0,session.ExitCode);host.Frames(workspace.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="self-authored build fixture complete");
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Build succeeded.");
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="journey game ran with --seconds 0");
        Assert.Null(workspace.PlaySettings.ApprovalNeeded(launch,true));Assert.True(File.Exists(approval));
        Assert.Equal(settingsBytes,File.ReadAllBytes(settingsPath));Assert.Equal(sourceBytes,File.ReadAllBytes(Path.Combine(root,"src","input.cs")));
        Assert.Equal("{}",File.ReadAllText(Path.Combine(root,"data","item.json")));
        if(approved is not null)Assert.Equal(approved,File.ReadAllBytes(approval));
        var marker=Path.Combine(root,"ran.txt");var runs=File.ReadAllLines(marker).Length;Assert.Equal(phase=="approve"?1:2,runs);
        string? revisedQuestion=null;
        if(phase=="restart")
        {
            var settings=PlaySettingsStore.Load(root)!;var build=settings.Build!;
            var arguments=build.Arguments.ToArray();Assert.Equal("token-A",arguments[2]);arguments[2]="token-B";
            PlaySettingsStore.Save(root,new(settings.Program,settings.Arguments,settings.WorkingFolder,new(build.Command,arguments,build.Inputs)));
            Assert.True(workspace.PlaySettings.TryLaunch(out var revised,out why),why);
            var changed=revised!.Build!.CommandLine();Assert.Equal(command.Length,changed.Length);Assert.Equal(1,command.Zip(changed).Count(pair=>pair.First!=pair.Second));
            Stale(root);workspace.RequestProjectPlay(true);host.Frames(workspace.Draw,3);
            Assert.True(workspace.ProjectPlayApprovalPending);Assert.Same(session,workspace.ProjectPlay);
            revisedQuestion=workspace.PlaySettings.ApprovalNeeded(revised,true);Assert.NotNull(revisedQuestion);Assert.Contains(changed,revisedQuestion);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==revisedQuestion);
            Assert.Equal(approved,File.ReadAllBytes(approval));Assert.Equal(runs,File.ReadAllLines(marker).Length);
            workspace.CancelProjectPlay();Assert.False(workspace.ProjectPlayApprovalPending);Assert.Equal(runs,File.ReadAllLines(marker).Length);
            Assert.Null(workspace.PlaySettings.ApprovalNeeded(launch,true));
        }
        return new(Environment.ProcessId,phase,command,revisedQuestion,runs);
    }
    // Also runnable on its own; the parent selects only this entry in each new testhost.
    [Fact]
    public void Fresh_process_probe()
    {
        var temporary=Environment.GetEnvironmentVariable("BED_QA10_TEMP");using var fixture=temporary is null?new Fixture():null;
        var evidence=Run(temporary??fixture!.Temporary,Environment.GetEnvironmentVariable("BED_QA10_PHASE")??"approve");
        if(Environment.GetEnvironmentVariable("BED_QA10_RESULT") is {} result)File.WriteAllText(result,JsonSerializer.Serialize(evidence));
        output.WriteLine(JsonSerializer.Serialize(evidence));
    }
    private static async Task<Evidence> Child(Fixture fixture,string phase)
    {
        var result=Path.Combine(fixture.Temporary,phase+".json");
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("vstest");start.ArgumentList.Add(typeof(ApprovalRestartQa10Tests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=Brutal.Authoring.Tests.ApprovalRestartQa10Tests.Fresh_process_probe");start.ArgumentList.Add("--logger:console;verbosity=normal");
        start.Environment["BED_QA10_TEMP"]=fixture.Temporary;start.Environment["BED_QA10_PHASE"]=phase;start.Environment["BED_QA10_RESULT"]=result;
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
        try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.True(child.ExitCode==0,await stdout+await stderr);return JsonSerializer.Deserialize<Evidence>(File.ReadAllText(result))!;}
        finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
    }
    [Fact]
    public async Task Approved_build_survives_two_fresh_processes_and_one_changed_character_prompts_again()
    {
        using var fixture=new Fixture();var first=await Child(fixture,"approve");var second=await Child(fixture,"restart");
        Assert.NotEqual(Environment.ProcessId,first.ProcessId);Assert.NotEqual(Environment.ProcessId,second.ProcessId);Assert.NotEqual(first.ProcessId,second.ProcessId);
        Assert.Equal(first.Command,second.Command);Assert.Null(first.RevisedQuestion);Assert.Contains("token-B",second.RevisedQuestion);Assert.Equal(2,second.Runs);
        Assert.False(File.Exists(Path.Combine(fixture.Root,".bed","play-approvals.json")));
        output.WriteLine($"Fresh testhost PIDs {first.ProcessId} -> {second.ProcessId}: original build ran once in each; token-A -> token-B prompted without executing or changing approval bytes.");
    }
}
