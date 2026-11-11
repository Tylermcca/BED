using System.Diagnostics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Launcher;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class BuildAndPlayJourneyTests
{
    private sealed class Rig : IDisposable
    {
        private readonly string temporary=Directory.CreateTempSubdirectory("build-play-journey-").FullName;
        public readonly string Root,Local,Marker,Source,SettingsFile;
        public readonly byte[] SavedSettings;
        public readonly ProjectLauncher Launcher;
        public readonly HeadlessHost UiHost=new(1200,900){Drawing=false};
        public DataWorkspace Workspace=>Launcher.Workspace!;
        public Rig(bool successfulBuild)
        {
            Root=Directory.CreateDirectory(Path.Combine(temporary,"project with spaces")).FullName;
            Local=Path.Combine(temporary,"local preferences");Marker=Path.Combine(Root,"game-ran.txt");
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Path.Combine(Root,"data","item.json"),"{}");File.WriteAllText(Path.Combine(Root,"schemas","item.schema.json"),"{}");
            Source=Path.Combine(Root,"src","Sample","code.cs");Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            File.WriteAllText(Source,"self-authored stale-build input");File.SetLastWriteTimeUtc(Source,new(2020,1,1,0,0,0,DateTimeKind.Utc));
            foreach(var folder in new[]{"tool","game"})
            {
                var target=Directory.CreateDirectory(Path.Combine(Root,folder)).FullName;
                foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})
                    File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(target,"ProjectPlayTestGame"+suffix));
            }
            File.SetLastWriteTimeUtc(Path.Combine(Root,"game","ProjectPlayTestGame.exe"),new(2019,1,1,0,0,0,DateTimeKind.Utc));
            PlaySettingsStore.Save(Root,new("game/ProjectPlayTestGame.exe",["journey","--seconds","0",Marker],".",
                new("./tool/ProjectPlayTestGame.exe",[successfulBuild?"build":"build-fail",Path.Combine(Root,"game")],["src/Sample/**/*.cs"])));
            SettingsFile=Path.Combine(Root,".bed","project.json");SavedSettings=File.ReadAllBytes(SettingsFile);
            EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
            Launcher=Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(Local,"recent.json"))));
            Assert.True(Launcher.OpenFolder(Root),Launcher.Said);Assert.True(Workspace.Show("item.json"));
            UiHost.Frames(Workspace.Draw,3);
        }
        public void Click(string label,string? window=null)
        {
            var snapshot=Ui.Snapshot();
            bool InWindow(UiNode node)
            {
                for(var parent=node.Parent;parent>=0;parent=snapshot.Nodes[parent].Parent)
                    if(snapshot.Nodes[parent].Role==UiRole.Window && snapshot.Nodes[parent].Label==window)return true;
                return false;
            }
            // A toolbar and the Play console may both offer Build and Play. Target the intended surface exactly.
            var button=Assert.Single(snapshot.Nodes,n=>n.Role==UiRole.Button&&n.Label==label&&(window is null||InWindow(n)));
            Assert.True(button.Enabled,button.DisabledReason);Assert.False(button.Bounds.IsEmpty);
            UiHost.Click(Workspace.Draw,button.Bounds.X+button.Bounds.Width/2,button.Bounds.Y+button.Bounds.Height/2);
            UiHost.Frames(Workspace.Draw,2);
        }
        public void Until(Func<bool> condition)
        {
            var watch=Stopwatch.StartNew();
            while(!condition()&&watch.Elapsed<TimeSpan.FromSeconds(8)){UiHost.Frame(Workspace.Draw);Thread.Sleep(5);}
            Assert.True(condition(),Workspace.ProjectPlay?.Status??Workspace.Said);UiHost.Frame(Workspace.Draw);
        }
        public void Dispose(){Launcher.Dispose();UiHost.Dispose();Directory.Delete(temporary,true);}
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Stale_program_builds_only_after_exact_approval_and_failed_build_never_runs_game(bool successfulBuild)
    {
        using var rig=new Rig(successfulBuild);
        rig.Click("Play");
        var stale=rig.Workspace.ProjectPlay!;Assert.True(stale.CanBuildAndPlay);Assert.True(stale.HasCompleted);Assert.Null(stale.ExitCode);
        Assert.Contains("older than its build inputs",stale.Status);Assert.False(File.Exists(rig.Marker));
        Assert.True(rig.Workspace.PlaySettings!.TryLaunch(out var launch,out var why),why);
        var question=rig.Workspace.PlaySettings.ApprovalNeeded(launch!,true);Assert.NotNull(question);
        rig.Click("Build and Play","Play console");Assert.True(rig.Workspace.ProjectPlayApprovalPending);
        Assert.Same(stale,rig.Workspace.ProjectPlay);Assert.False(File.Exists(rig.Marker));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==question);
        Assert.Contains(launch!.Build!.CommandLine(),question);Assert.Contains(launch.Build.WorkingDirectory,question);
        Assert.False(File.Exists(Path.Combine(rig.Local,"play-approvals.json")));
        rig.Click("Allow this exact command and continue","Play saved files");Assert.False(rig.Workspace.ProjectPlayApprovalPending);
        var session=rig.Workspace.ProjectPlay!;Assert.NotSame(stale,session);
        rig.Until(()=>session.HasCompleted&&session.ExitCode is not null);
        Assert.Equal(successfulBuild?0:9,session.ExitCode);
        Assert.Null(rig.Workspace.PlaySettings.ApprovalNeeded(launch,true));
        Assert.True(File.Exists(Path.Combine(rig.Local,"play-approvals.json")));
        var text=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.Text).Select(n=>n.Label).ToArray();
        if(successfulBuild)
        {
            Assert.Equal("game ran once\n",File.ReadAllText(rig.Marker));
            Assert.Contains("self-authored build fixture complete",text);Assert.Contains("Build succeeded.",text);
            Assert.Contains("journey game ran with --seconds 0",text);
            Assert.True(Array.IndexOf(text,"self-authored build fixture complete")<Array.IndexOf(text,"journey game ran with --seconds 0"));
            Assert.Equal("The process exited normally (code 0).",session.Status);
        }
        else
        {
            Assert.False(File.Exists(rig.Marker));Assert.Contains("code 9",session.Status);
            Assert.DoesNotContain("Build succeeded.",text);Assert.DoesNotContain("Running.",text);
            Assert.DoesNotContain("journey game ran with --seconds 0",text);
        }
        Assert.Equal(rig.SavedSettings,File.ReadAllBytes(rig.SettingsFile));
        Assert.Equal("self-authored stale-build input",File.ReadAllText(rig.Source));
        Assert.Equal("{}",File.ReadAllText(Path.Combine(rig.Root,"data","item.json")));Assert.Empty(rig.Workspace.Project.Dirty);
    }
}
