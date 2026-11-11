using System.Runtime.InteropServices;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlayStatusA11y01Tests
{
    private sealed class Session : IProjectPlaySession
    {
        public string Status{get;set;}="Running.";
        public bool HasCompleted{get;set;}
        public int? ExitCode{get;set;}
        public bool CanBuildAndPlay=>false;
        public bool Start(bool dirty,bool build=false){Assert.False(dirty);Status=build?"Building saved project files.":"Running.";HasCompleted=false;ExitCode=null;return true;}
        public void RequestStop()=>Status="Requested polite close; waiting up to 5 seconds.";
        public void Update() { }
        public void Draw() { }
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
        public void Dispose()=>HasCompleted=true;
    }
    private sealed class Factory : IProjectPlayHost
    {
        public Session Current=null!;
        public IProjectPlaySession Start(PlayLaunch launch)=>Current=new();
    }
    private sealed class Sink : IAccessKitUpdateSink
    {
        public readonly List<AccessKitProjectedUpdate> Updates=[];
        public bool TryApply(AccessKitProjectedUpdate update){Updates.Add(update);return true;}
    }
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("play-status-a11y01-").FullName;
        public readonly HeadlessHost Host=new(1300,950){Drawing=false};
        public readonly Factory Factory=new();public readonly DataWorkspace Workspace;
        public readonly AccessKitProjection Projection=new();public readonly Sink Sink=new();
        public Rig(bool build=false)
        {
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(data,"item.json"),"{}");File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{}");
            File.WriteAllText(Path.Combine(Root,"game.exe"),"inert fixture");File.WriteAllText(Path.Combine(Root,"build.exe"),"inert fixture");
            var settings=new PlaySettings("game.exe",build:new("./build.exe"));PlaySettingsStore.Save(Root,settings);
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Show("item.json"));
            var local=Path.Combine(Root,"local");Workspace.BindPlaySettings(Root,null,local);Workspace.BindProjectPlay(Factory);
            Host.Frames(Workspace.Draw,3);
            if(build)
            {
                Assert.True(PlaySettingsResolver.TryResolve(Root,settings,out var launch,out var why),why);
                new PlayApprovals(local).Approve(launch!,true);
            }
            Assert.True(Workspace.RequestProjectPlay(build),Workspace.Said);Frame(4);
        }
        public void Frame(int count=1)
        {
            Host.Frames(Workspace.Draw,count);
            Projection.Submit(Ui.Snapshot(),"Play status fixture",new(0,0,1300,950),1,1,Sink);
        }
        public UiNode Status
        {
            get
            {
                var snapshot=Ui.Snapshot();
                return Assert.Single(snapshot.Nodes,n=>n.Role==UiRole.Status&&n.Parent>=0&&snapshot.Nodes[n.Parent].Label=="Play console");
            }
        }
        public AccessKitProjectedNode LastStatus=>Sink.Updates.SelectMany(u=>u.ChangedNodes).Last(n=>n.Role==AccessKitProjectedRole.Status&&n.SourceId==Status.Id);
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Building_running_and_natural_exit_keep_one_status_identity_and_do_not_repeat_unchanged_values()
    {
        using var r=new Rig(build:true);var id=r.Status.Id;Assert.StartsWith("Building",r.Status.Label);
        var nativeId=r.LastStatus.Id;Assert.True(r.LastStatus.StableIdentity);
        var count=r.Sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.Role==AccessKitProjectedRole.Status&&n.SourceId==r.Status.Id);
        r.Frame(5);Assert.Equal(count,r.Sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.Role==AccessKitProjectedRole.Status&&n.SourceId==r.Status.Id));
        r.Factory.Current.Status="Running.";r.Frame();Assert.Equal(id,r.Status.Id);Assert.Equal(nativeId,r.LastStatus.Id);Assert.Equal("Running.",r.Status.Label);
        r.Factory.Current.Status="The process exited normally (code 0).";r.Factory.Current.ExitCode=0;r.Factory.Current.HasCompleted=true;r.Frame();
        Assert.StartsWith("Finished normally.",r.Status.Label);Assert.Equal(id,r.Status.Id);Assert.Equal(nativeId,r.LastStatus.Id);
        var terminal=r.Sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.Role==AccessKitProjectedRole.Status&&n.SourceId==r.Status.Id);
        r.Frame(5);Assert.Equal(terminal,r.Sink.Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.Role==AccessKitProjectedRole.Status&&n.SourceId==r.Status.Id));
        Assert.Equal(3,terminal);
    }
    [Fact]
    public void Failed_build_exposes_its_code_and_explicit_stop_is_announced_as_stopped()
    {
        using var build=new Rig(build:true);build.Factory.Current.Status="Play failed: The game stopped with error code 9.";
        build.Factory.Current.ExitCode=9;build.Factory.Current.HasCompleted=true;build.Frame();Assert.StartsWith("Build failed (error code 9).",build.Status.Label);
        Assert.True(build.Workspace.RequestProjectPlay(),build.Workspace.Said);build.Frame();
        Assert.True(build.Workspace.StopProjectPlay());build.Frame();Assert.Contains("Requested polite close",build.Status.Label);
        build.Factory.Current.Status="The program has no window to close, so BED ended it.";build.Factory.Current.ExitCode=1;build.Factory.Current.HasCompleted=true;build.Frame();
        Assert.StartsWith("Stopped (code 1).",build.Status.Label);Assert.DoesNotContain("Build failed",build.Status.Label);
    }
    [Fact]
    public void Native_status_nodes_are_polite_atomic_live_regions_and_ordinary_text_is_not()
    {
        var api=new AccessKitNativeTreeApi();
        var status=api.NewNode(AccessKitTreeEncoder.NativeRole(AccessKitProjectedRole.Status));
        try{var live=NodeLive(status);Assert.True(live.HasValue);Assert.Equal((byte)1,live.Value);Assert.True(IsAtomic(status));}
        finally{api.FreeNode(status);}
        var text=api.NewNode(AccessKitTreeEncoder.NativeRole(AccessKitProjectedRole.Label));
        try{Assert.False(NodeLive(text).HasValue);Assert.False(IsAtomic(text));}
        finally{api.FreeNode(text);}
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct OptionalLive{[MarshalAs(UnmanagedType.I1)]public bool HasValue;public byte Value;}
    [DllImport("accesskit.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="accesskit_node_live",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory|DllImportSearchPath.System32)]
    private static extern OptionalLive NodeLive(nint node);
    [DllImport("accesskit.dll",CallingConvention=CallingConvention.Cdecl,EntryPoint="accesskit_node_is_live_atomic",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory|DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.I1)]private static extern bool IsAtomic(nint node);
}
