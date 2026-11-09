using System.Text;
using System.Text.Json;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Xunit;
using System.Diagnostics;

namespace Brutal.Authoring.Tests;

public sealed class GameProcessHostTests
{
    private static PlaySnapshot Snapshot()=>PlaySnapshot.Create(new("invented.scene",2,null,"self-authored input"),PlayInputChoice.Draft,
        input=>new("invented.utf8",Encoding.UTF8.GetBytes(input.Text))).Snapshot!;
    [Fact]
    public void Wire_start_preserves_exact_payload_identity_and_stop_has_no_process_or_command()
    {
        var snapshot=Snapshot();var hello=PlayProtocol.Hello(snapshot.Correlate(Guid.NewGuid()));
        var text=PlayProcessWire.Start(hello,snapshot,1000);
        using var json=JsonDocument.Parse(text);var root=json.RootElement;
        Assert.Equal("hello",root.GetProperty("kind").GetString());
        Assert.Equal(hello.Correlation.SnapshotHash,root.GetProperty("hash").GetString());
        Assert.Equal(snapshot.CopyPayload(),Convert.FromBase64String(root.GetProperty("payload").GetString()!));
        Assert.DoesNotContain('\n',text);
        using var stop=JsonDocument.Parse(PlayProcessWire.Stop(new(1,hello.Correlation)));
        Assert.Equal("stop",stop.RootElement.GetProperty("kind").GetString());
        Assert.Throws<ArgumentException>(()=>PlayProcessWire.Start(hello,snapshot,1));
        Assert.Throws<ArgumentException>(()=>PlayProcessWire.Start(hello with { Correlation=hello.Correlation with { Revision=99 } },snapshot,1000));
    }

    [Theory]
    [InlineData("ready")] [InlineData("stopped")] [InlineData("failure")] [InlineData("log")]
    public void Wire_decodes_only_correlated_typed_protocol_or_console_records(string kind)
    {
        var snapshot=Snapshot();var id=snapshot.Correlate(Guid.NewGuid());
        var text=JsonSerializer.Serialize(new {kind,version=1,session=id.SessionId.ToString("N"),revision=id.Revision,hash=id.SnapshotHash,
            supported=7,code="LoadFailed",severity="Warning",message="Invented output",document="invented.scene",@object="box",line=2});
        Assert.True(PlayProcessWire.TryRead(text,4096,out var record,out var error));Assert.Null(error);Assert.NotNull(record);
        Assert.Equal(id,record.Correlation);
        if(kind=="log") {Assert.Null(record.Message);Assert.Equal(PlayLogSeverity.Warning,record.Severity);Assert.Equal(new PlaySourceLink("invented.scene","box",2),record.Source);}
        else Assert.NotNull(record.Message);
    }

    [Fact]
    public void Wire_refuses_oversized_malformed_duplicate_and_unknown_output_without_partial_record()
    {
        foreach(var text in new[]{"not json","[]","{\"kind\":\"x\",\"kind\":\"y\"}",new string('x',600)})
        {
            Assert.False(PlayProcessWire.TryRead(text,512,out var record,out var error));Assert.Null(record);Assert.NotNull(error);
        }
    }
    private sealed class Owned : IDisposable
    {
        public readonly PlayLogBuffer Logs;
        public readonly GameProcessHost Host;
        public readonly LinkedPlaySession Session;
        public Owned(string mode="ok",IEnumerable<string>? extra=null,string? executable=null)
        {
            var snapshot=Snapshot();var id=Guid.NewGuid();Logs=new(snapshot.Correlate(id),16,4096,16384);
            var helper=executable ?? Path.Combine(AppContext.BaseDirectory,"LinkedPlayTestGame","LinkedPlayTestGame.exe");
            var spec=new GameLaunchSpec(helper,AppContext.BaseDirectory,new[]{mode}.Concat(extra ?? []));
            Host=new(spec,65536,8192,32,Logs);
            Session=new(Host,snapshot,id,TimeProvider.System,TimeSpan.FromSeconds(4),TimeSpan.FromSeconds(2));
        }
        public bool Start()
        {
            var result=Session.Start();
            if(!OperatingSystem.IsWindows())
            {
                Assert.False(result);Assert.Contains("Play in a separate process needs Windows for now",Session.Error!.Message);
                Assert.Null(Host.Completion);return false;
            }
            return result;
        }
        public async Task Until(Func<bool> done)
        {
            var watch=Stopwatch.StartNew();
            while(!done() && watch.Elapsed<TimeSpan.FromSeconds(6))
            {
                Host.Pump(Session);Session.Tick();await Task.Delay(10);
            }
            Assert.True(done(),"The bounded owned-helper condition did not complete: "+Session.State+" "+Session.Error?.Message);
        }
        public void Dispose() {Session.Dispose();Host.Dispose();}
    }

    [Fact]
    public async Task Process_success_preserves_literal_argv_and_stop_is_owned_and_bounded()
    {
        string[] args=["two words","a\\\"quote","tail\\\\","%PATH%","& whoami",""];
        using var owned=new Owned(extra:args);
        if(!owned.Start()) {Assert.False(OperatingSystem.IsWindows(),owned.Session.Error?.Message);return;}
        await owned.Until(()=>owned.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted);
        Assert.Equal(LinkedPlayState.Running,owned.Session.State);
        await owned.Until(()=>owned.Logs.Count>0);
        var echoed=JsonSerializer.Deserialize<string[]>(owned.Logs.Snapshot()[0].Message);
        Assert.Equal(new[]{"ok"}.Concat(args),echoed);
        Assert.True(owned.Session.RequestStop());
        await owned.Until(()=>owned.Session.State is LinkedPlayState.Stopped or LinkedPlayState.Faulted);
        Assert.Equal(LinkedPlayState.Stopped,owned.Session.State);
        await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Process_start_failure_does_not_fall_back_to_another_program()
    {
        using var owned=new Owned(executable:Path.Combine(AppContext.BaseDirectory,"LinkedPlayTestGame","NeverCreated-Invented.exe"));
        Assert.False(owned.Start());Assert.Equal(LinkedPlayState.Faulted,owned.Session.State);
        Assert.Null(owned.Host.Completion);Assert.NotNull(owned.Session.Error);
    }

    [Theory]
    [InlineData("start-fail")] [InlineData("crash")]
    public async Task Process_declared_helper_start_failure_or_crash_is_reported(string mode)
    {
        using var owned=new Owned(mode);
        if(!owned.Start()) {Assert.False(OperatingSystem.IsWindows(),owned.Session.Error?.Message);return;}
        await owned.Until(()=>owned.Session.State==LinkedPlayState.Faulted);
        Assert.NotNull(owned.Session.Error);
        if(mode=="start-fail") Assert.Contains("Invented helper start failure",owned.Session.Error.Message);
        else Assert.Contains("23",owned.Session.Error.Message);
        await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Process_hang_is_killed_by_owned_job_when_start_deadline_expires()
    {
        using var owned=new Owned("hang");
        if(!owned.Start()) {Assert.False(OperatingSystem.IsWindows(),owned.Session.Error?.Message);return;}
        await owned.Until(()=>owned.Session.State==LinkedPlayState.Faulted);
        Assert.Contains("timed out",owned.Session.Error!.Message);
        await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(3));
        owned.Host.AbortOwned();owned.Host.AbortOwned();
    }

    [Fact]
    public async Task Process_stale_ready_does_not_fault_or_rebind_the_current_session()
    {
        using var owned=new Owned("stale");
        if(!owned.Start()) {Assert.False(OperatingSystem.IsWindows(),owned.Session.Error?.Message);return;}
        await owned.Until(()=>owned.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted);
        Assert.Equal(LinkedPlayState.Running,owned.Session.State);Assert.Null(owned.Session.Error);
        owned.Session.Dispose();await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Process_duplicate_stop_calls_do_not_queue_duplicate_pipe_messages()
    {
        using var owned=new Owned("detect-duplicate-stop");
        if(!owned.Start()) {Assert.False(OperatingSystem.IsWindows(),owned.Session.Error?.Message);return;}
        await owned.Until(()=>owned.Session.State is LinkedPlayState.Running or LinkedPlayState.Faulted);
        Assert.Equal(LinkedPlayState.Running,owned.Session.State);
        Assert.True(owned.Session.RequestStop());
        owned.Host.RequestStop(new(1,owned.Session.Correlation));
        owned.Host.RequestStop(new(1,owned.Session.Correlation));
        await owned.Until(()=>owned.Session.State is LinkedPlayState.Stopped or LinkedPlayState.Faulted);
        Assert.True(owned.Session.State==LinkedPlayState.Stopped,"Expected cooperative stop, got "+owned.Session.State+": "+owned.Session.Error?.Message);
        Assert.Null(owned.Session.Error);
        await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Argument_declaration_is_copied_and_refuses_shell_paths_or_unbounded_input()
    {
        var exe=Path.Combine(AppContext.BaseDirectory,"LinkedPlayTestGame","LinkedPlayTestGame.exe");
        var args=new List<string>{"literal space","a\\\"b","trailing\\\\"};
        var declaration=new GameLaunchSpec(exe,AppContext.BaseDirectory,args);args[0]="changed";
        Assert.Equal("literal space",declaration.Arguments[0]);
        Assert.Throws<ArgumentException>(()=>new GameLaunchSpec("helper.exe",AppContext.BaseDirectory,[]));
        Assert.Throws<ArgumentException>(()=>new GameLaunchSpec(exe,"relative",[]));
        Assert.Throws<ArgumentException>(()=>new GameLaunchSpec(exe,AppContext.BaseDirectory,[new string('x',8193)]));
    }
}
