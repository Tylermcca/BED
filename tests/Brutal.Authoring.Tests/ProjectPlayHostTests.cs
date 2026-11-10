using System.Diagnostics;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class ProjectPlayHostTests
{
    private sealed class Clock:TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>1000;
        public override long GetTimestamp()=>ticks;
        public void Advance(TimeSpan amount)=>ticks+=(long)amount.TotalMilliseconds;
    }
    private sealed class Fixture:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("one-play-").FullName;
        public readonly Clock Clock=new();
        public ProjectPlayHost? Host;
        public Fixture()
        {
            var folder=Directory.CreateDirectory(Path.Combine(Root,"tool")).FullName;
            foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})
                File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(folder,"ProjectPlayTestGame"+suffix));
            File.WriteAllText(Path.Combine(Root,"saved.json"),"self-authored saved files");
        }
        public ProjectPlayHost Make(string mode="ok",int maxLine=4096,int maxPending=256,PlaySettings? settings=null,PlayLaunch? launch=null)
        {
            settings??=new("tool/ProjectPlayTestGame.exe",[mode],".");
            // These self-authored fixture commands are explicitly admitted by the test, not discovered by the host.
            launch??=new(new(Path.GetFullPath(Path.Combine(Root,settings.Program)),Path.GetFullPath(Path.Combine(Root,settings.WorkingFolder)),settings.Arguments),
                settings.Build is {} build?new(Path.GetFullPath(Path.Combine(Root,build.Command)),Root,build.Arguments):null,Root,settings.Build?.Inputs??[]);
            return Host=new(launch,Clock,TimeSpan.FromSeconds(2),maxLine,maxPending);
        }
        public void Until(Func<bool> done)
        {
            var time=Stopwatch.StartNew();while(!done()&&time.Elapsed<TimeSpan.FromSeconds(5)){Host!.Update();Thread.Sleep(5);}
            Assert.True(done(),Host!.Status);
        }
        public void Source(string relative="src/Sample/code.cs")
        {
            var path=Path.Combine(Root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"self-authored input");
            File.SetLastWriteTimeUtc(path,new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc));
        }
        public PlaySettings Build(string mode="build")=>new("game/ProjectPlayTestGame.exe",["ok"],".",
            new("tool/ProjectPlayTestGame.exe",[mode,Path.Combine(Root,"game")],["src/Sample/**/*.cs"]));
        public void Dispose()
        {
            Host?.Dispose();var time=Stopwatch.StartNew();while(Host is {HasCompleted:false}&&time.Elapsed<TimeSpan.FromSeconds(3))Thread.Sleep(5);
            Assert.True(Host?.HasCompleted??true,"Owned helper did not finish after disposal.");
            Assert.Equal("self-authored saved files",File.ReadAllText(Path.Combine(Root,"saved.json")));
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);
        }
    }
    [Fact]
    public void Unsaved_gate_refuses_before_any_process_and_never_saves()
    {
        using var f=new Fixture();var host=f.Make();Assert.False(host.Start(true));Assert.Equal(ProjectPlayState.Created,host.State);
        Assert.Contains("Save changes",host.Status);Assert.True(host.HasCompleted);Assert.Empty(host.DrainLogs());
    }
    [Fact]
    public void Plain_stdout_stderr_and_child_environment_are_observed_without_protocol_or_parent_changes()
    {
        using var f=new Fixture();var project=Environment.GetEnvironmentVariable("BED_PROJECT");var play=Environment.GetEnvironmentVariable("BED_PLAY");
        var host=f.Make();Assert.True(host.Start(false),host.Status);Assert.Equal(ProjectPlayState.Running,host.State);
        Assert.Equal("Running.",host.Status);f.Until(()=>host.State==ProjectPlayState.Stopped);
        Assert.Equal(0,host.ExitCode);var logs=host.DrainLogs();
        Assert.Contains(logs,line=>!line.Error&&line.Text=="plain stdout");Assert.Contains(logs,line=>line.Error&&line.Text=="plain stderr");
        Assert.Contains(logs,line=>line.Text=="BED_PROJECT="+f.Root);Assert.Contains(logs,line=>line.Text=="BED_PLAY=1");
        Assert.Equal(project,Environment.GetEnvironmentVariable("BED_PROJECT"));Assert.Equal(play,Environment.GetEnvironmentVariable("BED_PLAY"));
        Assert.DoesNotContain(logs,line=>line.Text.Contains("Ready",StringComparison.Ordinal));
    }
    private static string OemText(byte[] bytes)
    {
        // Query the OS default, independently of the game's configured text encoding.
        var page=GetOEMCP();var encoding=System.Text.CodePagesEncodingProvider.Instance.GetEncoding((int)page);
        return (encoding??System.Text.Encoding.UTF8).GetString(bytes);
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern uint GetOEMCP();
    [Theory]
    [InlineData("encoded",0,ProjectPlayState.Stopped)]
    [InlineData("encoded-fail",7,ProjectPlayState.Failed)]
    public void Per_line_UTF8_and_OEM_output_preserves_both_channels_and_the_real_exit_code(string mode,int exit,ProjectPlayState state)
    {
        using var f=new Fixture();var host=f.Make(mode,maxLine:256);Assert.True(host.Start(false),host.Status);
        f.Until(()=>host.HasCompleted);host.Update();Assert.Equal(state,host.State);Assert.Equal(exit,host.ExitCode);
        var logs=host.DrainLogs();Assert.Contains(logs,l=>!l.Error&&l.Text=="utf8: "+new string('界',120)+" \U0001F680");
        Assert.Contains(logs,l=>!l.Error&&l.Text=="oem: "+OemText([0xf6,0x82,0x9c]));
        Assert.Contains(logs,l=>l.Error&&l.Text=="err: "+OemText([0xf6,0x82,0x9c]));
        Assert.Contains(logs,l=>!l.Error&&l.Text=="utf8-after: caf\u00e9");
        Assert.Contains(logs,l=>!l.Error&&l.Text=="tail: "+OemText([0xf6]));
        Assert.DoesNotContain(logs,l=>l.Text.Contains("Could not read process output",StringComparison.Ordinal));
    }
    [Fact]
    public void OEM_build_output_survives_then_the_built_program_runs_and_exits_zero()
    {
        using var f=new Fixture();f.Source();var host=f.Make(settings:f.Build("build-encoded"));Assert.True(host.Start(false,true),host.Status);
        f.Until(()=>host.State==ProjectPlayState.Stopped);Assert.Equal(0,host.ExitCode);var logs=host.DrainLogs();
        Assert.Contains(logs,l=>!l.Error&&l.Text=="oem: "+OemText([0xf6,0x82,0x9c]));
        Assert.Contains(logs,l=>l.Error&&l.Text=="err: "+OemText([0xf6,0x82,0x9c]));
        Assert.Contains(logs,l=>l.Text=="Build succeeded.");Assert.Contains(logs,l=>l.Text=="Running.");
        Assert.Equal(2,logs.Count(l=>l.Text=="plain stdout"));
    }
    [Fact]
    public void OEM_output_from_a_failed_build_preserves_its_exit_code_and_never_launches_the_game()
    {
        using var f=new Fixture();f.Source();var host=f.Make(settings:f.Build("encoded-fail"));Assert.True(host.Start(false,true),host.Status);
        f.Until(()=>host.State==ProjectPlayState.Failed);Assert.Equal(7,host.ExitCode);var logs=host.DrainLogs();
        Assert.Contains(logs,l=>!l.Error&&l.Text=="oem: "+OemText([0xf6,0x82,0x9c]));
        Assert.Contains(logs,l=>l.Error&&l.Text=="err: "+OemText([0xf6,0x82,0x9c]));
        Assert.DoesNotContain(logs,l=>l.Text=="Running."||l.Text=="Build succeeded.");
        Assert.False(Directory.Exists(Path.Combine(f.Root,"game")));
    }
    [Fact]
    public void Plain_program_receives_no_handshake_snapshot_or_other_stdin_bytes()
    {
        using var f=new Fixture();var host=f.Make("stdin");Assert.True(host.Start(false),host.Status);
        f.Until(()=>host.State==ProjectPlayState.Stopped);Assert.Equal(0,host.ExitCode);
        Assert.Contains(host.DrainLogs(),line=>line.Text=="stdin-bytes=0");
    }
    [Fact]
    public void Nonzero_exit_is_a_plain_failure_and_does_not_exit_the_editor_process()
    {
        using var f=new Fixture();var before=Environment.ExitCode;var host=f.Make("crash");Assert.True(host.Start(false));
        f.Until(()=>host.State==ProjectPlayState.Failed);Assert.Equal(7,host.ExitCode);Assert.Contains("code 7",host.Status);
        Assert.Equal(before,Environment.ExitCode);
    }
    [Fact]
    public void Windowless_program_is_ended_immediately_with_the_exact_plain_explanation()
    {
        using var f=new Fixture();var host=f.Make("ignore");Assert.True(host.Start(false));Assert.True(host.RequestStop());Assert.False(host.RequestStop());
        f.Until(()=>host.State==ProjectPlayState.Stopped);Assert.True(host.HasCompleted);
        Assert.Equal(TimeSpan.FromSeconds(5),ProjectPlayHost.StopGrace); // Applies to actual owned windows; no GUI is created here.
        Assert.Equal("The program has no window to close, so BED ended it.",host.Status);
        Assert.Contains(host.DrainLogs(),line=>line.Text==host.Status);
    }
    [Fact]
    public void Missing_program_offers_build_then_zero_build_launches_actual_helper_only_after_success()
    {
        using var f=new Fixture();f.Source();var host=f.Make(settings:f.Build());Assert.False(host.Start(false));
        Assert.Equal(ProjectPlayState.NeedsBuild,host.State);Assert.True(host.CanBuildAndPlay);Assert.False(Directory.Exists(Path.Combine(f.Root,"game")));
        Assert.True(host.Start(false,true),host.Status);Assert.Equal(ProjectPlayState.Building,host.State);f.Until(()=>host.State==ProjectPlayState.Stopped);
        var logs=host.DrainLogs();Assert.Contains(logs,line=>line.Text=="self-authored build fixture complete");
        Assert.Contains(logs,line=>line.Text=="Build succeeded.");Assert.Contains(logs,line=>line.Text=="Running.");Assert.Equal(0,host.ExitCode);
    }
    [Fact]
    public void Failed_build_never_launches_program_or_claims_running()
    {
        using var f=new Fixture();f.Source();var host=f.Make(settings:f.Build("build-fail"));Assert.True(host.Start(false,true));
        f.Until(()=>host.State==ProjectPlayState.Failed);Assert.Equal(9,host.ExitCode);Assert.Contains("code 9",host.Status);
        Assert.False(Directory.Exists(Path.Combine(f.Root,"game")));Assert.DoesNotContain(host.DrainLogs(),line=>line.Text=="Running.");
    }
    [Fact]
    public void Newer_declared_glob_input_refuses_existing_program_without_automatic_build()
    {
        using var f=new Fixture();f.Source();File.SetLastWriteTimeUtc(Path.Combine(f.Root,"tool","ProjectPlayTestGame.exe"),new DateTime(2019,1,1,0,0,0,DateTimeKind.Utc));
        var host=f.Make(settings:new("tool/ProjectPlayTestGame.exe",["ok"],".",new("tool/ProjectPlayTestGame.exe",[],["src/Sample/**/*.cs"])));
        Assert.False(host.Start(false));Assert.Equal(ProjectPlayState.NeedsBuild,host.State);Assert.True(host.HasCompleted);Assert.True(host.CanBuildAndPlay);
    }
    [Fact]
    public void Admitted_resolved_command_and_working_folder_are_used_without_host_reresolution()
    {
        using var f=new Fixture();using var admitted=new Fixture();
        var run=new GameLaunchSpec(Path.Combine(admitted.Root,"tool","ProjectPlayTestGame.exe"),admitted.Root,["ok"]);
        var host=f.Make(launch:new(run,null,f.Root,[]));Assert.True(host.Start(false),host.Status);
        f.Until(()=>host.State==ProjectPlayState.Stopped);Assert.Equal(0,host.ExitCode);
        Assert.Contains(host.DrainLogs(),line=>line.Text=="BED_PROJECT="+f.Root);
    }
    [Fact]
    public void Invalid_declared_input_refuses_before_build_even_when_the_program_is_missing()
    {
        using var f=new Fixture();var settings=new PlaySettings("game/ProjectPlayTestGame.exe",[],".",
            new("tool/ProjectPlayTestGame.exe",["build",Path.Combine(f.Root,"game")],["../unadmitted/**/*.cs"]));
        var host=f.Make(settings:settings);Assert.False(host.Start(false,true));Assert.Equal(ProjectPlayState.Failed,host.State);
        Assert.True(host.HasCompleted);Assert.Contains("globs must stay inside",host.Status);Assert.False(Directory.Exists(Path.Combine(f.Root,"game")));
        Assert.DoesNotContain(host.DrainLogs(),line=>line.Text=="plain stdout");
    }
    [Fact]
    public void Missing_program_without_build_command_explains_settings_instead_of_offering_unavailable_build()
    {
        using var f=new Fixture();var host=f.Make(settings:new("missing.exe"));Assert.False(host.Start(false));
        Assert.Equal(ProjectPlayState.NeedsBuild,host.State);Assert.False(host.CanBuildAndPlay);Assert.True(host.HasCompleted);
        Assert.Contains("Set its program or build command",host.Status);
    }
    [Fact]
    public void Build_deadline_ends_only_owned_helper_without_launching_program()
    {
        using var f=new Fixture();f.Source();var host=f.Make(settings:f.Build("ignore"));Assert.True(host.Start(false,true));
        f.Clock.Advance(TimeSpan.FromSeconds(2));host.Update();Assert.Equal(ProjectPlayState.Failed,host.State);
        Assert.Contains("declared deadline",host.Status);f.Until(()=>host.HasCompleted);
        Assert.False(Directory.Exists(Path.Combine(f.Root,"game")));Assert.DoesNotContain(host.DrainLogs(),line=>line.Text=="Running.");
    }
    [Fact]
    public void Invalid_executable_start_is_contained_and_does_not_change_saved_data()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Root,"invalid.exe"),"self-authored invalid executable");
        var host=f.Make(settings:new("invalid.exe"));Assert.False(host.Start(false));Assert.Equal(ProjectPlayState.Failed,host.State);
        Assert.True(host.HasCompleted);Assert.StartsWith("Play failed:",host.Status);Assert.Contains(host.DrainLogs(),line=>line.Error);
    }
    [Fact]
    public void Plain_console_evicts_oldest_lines_at_the_declared_bound_and_reports_count()
    {
        using var f=new Fixture();var host=f.Make("many-lines",maxPending:4);Assert.True(host.Start(false));
        f.Until(()=>host.State==ProjectPlayState.Stopped);var lines=host.DrainLogs();Assert.Equal(4,lines.Count);
        Assert.True(host.DiscardedLogLines>=90);Assert.Contains(lines,line=>line.Text=="fixture-line-99");
        Assert.Contains(lines,line=>line.Text=="The process exited normally (code 0).");Assert.Equal(0,host.ExitCode);
    }
    [Fact]
    public void Build_input_patterns_are_copied_before_caller_list_mutation()
    {
        using var f=new Fixture();var patterns=new[]{"missing/**/*.cs"};
        var launch=new PlayLaunch(new(Path.Combine(f.Root,"tool","ProjectPlayTestGame.exe"),f.Root,["ok"]),null,f.Root,patterns);
        var host=f.Make(launch:launch);patterns[0]="../unadmitted/**/*.cs";Assert.True(host.Start(false),host.Status);
        f.Until(()=>host.State==ProjectPlayState.Stopped);Assert.Equal(0,host.ExitCode);
    }
    [Fact]
    public void Excessive_output_stops_owned_helper_with_a_bounded_visible_reason()
    {
        using var f=new Fixture();var host=f.Make("long-line",128,4);Assert.True(host.Start(false));f.Until(()=>host.State==ProjectPlayState.Failed);
        Assert.Contains("output line exceeded",host.Status);var logs=host.DrainLogs();Assert.InRange(logs.Count,1,4);Assert.All(logs,line=>Assert.InRange(line.Text.Length,0,128));
    }
}
