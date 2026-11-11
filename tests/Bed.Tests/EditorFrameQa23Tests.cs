using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Bed.Data;
using Bed.Editor;
using Xunit.Abstractions;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed unsafe class EditorFrameQa23Tests(ITestOutputHelper output)
{
    private static readonly List<string> lifecycle=[];
    [StructLayout(LayoutKind.Sequential)] private struct NativeVector { public int Size,Capacity; public nint Data; }
    private sealed class NoProcessHost:IProjectPlayHost
    {
        public IProjectPlaySession Start(PlayLaunch launch)=>new NoProcessSession();
    }
    private sealed class NoProcessSession:IProjectPlaySession
    {
        public bool Start(bool dirty,bool buildAndPlay=false){Assert.False(dirty);return true;}
        public bool CanBuildAndPlay=>false;public bool HasCompleted=>true;public int? ExitCode=>0;public string Status=>"Completed virtual game";
        public void RequestStop(){}public void Update(){}public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];public void Draw(){}public void Dispose(){}
    }
    private sealed class VirtualWindow(AccessKitNativeTreeA457Tests.Fake tree):IAccessKitWindowApi
    {
        private Func<nint>? activate; public int Frees;
        public nint NewSubclass(nint handle,Func<nint> initial,Action<AccessKitNativeActionRequest> receive){activate=initial;lifecycle.Add("attach:"+handle);return handle;}
        public void FreeSubclass(nint adapter){Frees++;lifecycle.Add("free:"+adapter);activate=null;}
        public nint UpdateIfActive(nint adapter,AccessKitEncodedUpdate update){tree.FreeUpdate(update.Transfer());return 0;}
        public void RaiseEvents(nint events)=>throw new InvalidOperationException("No native events in virtual backend.");
        public void Read(){var update=activate?.Invoke()??0;if(update!=0)tree.FreeUpdate(update);}
    }
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void Create(ImGuiViewport* v){v->PlatformHandleRaw=(nint)v->ID.Value;v->PlatformHandle=v->PlatformHandleRaw;v->PlatformUserData=(nint)1;lifecycle.Add("create:"+v->PlatformHandleRaw);}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void Destroy(ImGuiViewport* v){lifecycle.Add("destroy:"+v->PlatformHandleRaw);v->PlatformHandleRaw=0;v->PlatformHandle=0;v->PlatformUserData=0;}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void Show(ImGuiViewport* v){lifecycle.Add("show:"+v->PlatformHandleRaw);}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void Nothing(ImGuiViewport* v){}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void Title(ImGuiViewport* v,byte* s){}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void SetPos(ImGuiViewport* v,float2 p){v->Pos=p;}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void SetSize(ImGuiViewport* v,float2 p){v->Size=p;}
    // Pinned Windows x64 C++ ImVec2 return ABI uses an explicit hidden result pointer.
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static float2* Pos(float2* result,ImGuiViewport* v){*result=v->Pos;return result;}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static float2* Size(float2* result,ImGuiViewport* v){*result=v->Size.X>0&&v->Size.Y>0?v->Size:new(300,300);return result;}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static float2* Scale(float2* result,ImGuiViewport* v){*result=new(1,1);return result;}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static byte Focus(ImGuiViewport* v)=>1;
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static byte Minimized(ImGuiViewport* v)=>0;
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static void Render(ImGuiViewport* v,void* arg){}
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])] private static float Dpi(ImGuiViewport* v)=>1;
    [Theory][InlineData("fixture")][InlineData("data")][InlineData("scene")][InlineData("invalid-scale")][InlineData("invalid-size")][InlineData("scene-cold")][InlineData("scene2-cold")][InlineData("xy-cold")]
    public void Production_frame_sequence_runs_eight_frames_with_a_real_secondary_viewport_without_windows(string mode)
    {
        lifecycle.Clear();
        using var host=new HeadlessHost(800,600){Drawing=false};host.Frame(()=>{});
        var io=Im.GetIO();var platform=Im.GetPlatformIO();
        platform.Platform_CreateWindow=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void>)&Create;
        platform.Platform_DestroyWindow=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void>)&Destroy;
        platform.Platform_ShowWindow=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void>)&Show;
        platform.Platform_SetWindowTitle=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,byte*,void>)&Title;
        platform.Platform_SetWindowPos=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,float2,void>)&SetPos;
        platform.Platform_SetWindowSize=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,float2,void>)&SetSize;
        platform.Platform_GetWindowPos=(nint)(delegate* unmanaged[Cdecl]<float2*,ImGuiViewport*,float2*>)&Pos;
        platform.Platform_GetWindowSize=(nint)(delegate* unmanaged[Cdecl]<float2*,ImGuiViewport*,float2*>)&Size;
        platform.Platform_GetWindowFramebufferScale=(nint)(delegate* unmanaged[Cdecl]<float2*,ImGuiViewport*,float2*>)&Scale;
        platform.Platform_SetWindowFocus=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void>)&Nothing;
        platform.Platform_GetWindowFocus=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,byte>)&Focus;
        platform.Platform_GetWindowMinimized=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,byte>)&Minimized;
        platform.Renderer_RenderWindow=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void*,void>)&Render;
        platform.Renderer_SwapBuffers=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void*,void>)&Render;
        var monitors=(NativeVector*)((byte*)(ImGuiPlatformIO*)platform+(int)Marshal.OffsetOf<ImGuiPlatformIO>("Monitors"));
        Assert.Equal(16,Marshal.SizeOf<NativeVector>());
        monitors->Data=(nint)Im.MemAlloc((nuint)sizeof(ImGuiPlatformMonitor));monitors->Size=monitors->Capacity=1;
        platform.Monitors.Span[0]=new ImGuiPlatformMonitor{MainPos=new(0,0),MainSize=new(4000,2000),WorkPos=new(0,0),WorkSize=new(4000,2000),DpiScale=1};
        platform.Platform_GetWindowDpiScale=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,float>)&Dpi;
        platform.Renderer_CreateWindow=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void>)&Nothing;
        platform.Renderer_DestroyWindow=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,void>)&Nothing;
        platform.Renderer_SetWindowSize=(nint)(delegate* unmanaged[Cdecl]<ImGuiViewport*,float2,void>)&SetSize;
        Im.GetMainViewport().PlatformHandle=(nint)1;Im.GetMainViewport().PlatformHandleRaw=(nint)1;
        EditorWindow<DataDocument>.Configure(io);
        io.BackendFlags|=ImGuiBackendFlags.PlatformHasViewports|ImGuiBackendFlags.RendererHasViewports;
        io.ConfigFlags|=ImGuiConfigFlags.ViewportsEnable;io.ConfigViewportsNoAutoMerge=true;
        var root=Directory.CreateTempSubdirectory("qa23-panels-").FullName;
        ProjectLauncher? launcher=null;
        var order=new List<string>();var targets=new List<string>();string? current=null;
        Action paint=()=>{Im.SetNextWindowSize(new(300,300));Ui.Begin("Floating fixture"u8);Ui.Text("Secondary content"u8);Ui.End();};
        if(mode is "fixture" or "invalid-scale" or "invalid-size")
        {
            targets.Add("Floating fixture");
            var layout=new EditorLayoutStore(Path.Combine(root,"layout.ini"));
            Assert.True(layout.TryWrite("[Window][Floating fixture]\nPos=1200,100\nSize=300,300\nCollapsed=0\n"));layout.LoadCurrent();
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));Directory.CreateDirectory(Path.Combine(root,"assets"));
            File.WriteAllText(Path.Combine(root,"data/items.json"),"{\"items\":[{\"id\":\"lamp\",\"name\":\"Lamp\"}]}");
            File.WriteAllText(Path.Combine(root,"schemas/items.schema.json"),"{}");
            File.WriteAllText(Path.Combine(root,"data/scene.json"),mode is "scene2-cold" or "xy-cold"?new Scene2Format().Write(new(mode=="xy-cold"?SceneSpace.TwoDXY:SceneSpace.ThreeD,[new(SceneObject.At("lamp",null,new(0,0,0)),null)],[],[],null)):new SceneFormat().Write(new Scene([SceneObject.At("lamp",null,new(0,0,0))])));
            File.WriteAllText(Path.Combine(root,"reference_map.json"),"{\"game\":\"Own fixture\",\"labels\":[],\"references\":[]}");
            File.WriteAllText(Path.Combine(root,"game.exe"),"Never executed; virtual host only");PlaySettingsStore.Save(root,new("game.exe"));
            launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json"))){ProjectPlayHost=new NoProcessHost()});
            Assert.True(launcher.OpenFolder(root),launcher.Said);var workspace=launcher.Workspace!;
            if(mode=="scene"||mode.EndsWith("-cold",StringComparison.Ordinal))
            {
                Assert.True(workspace.Documents.BindScene("scene.json","scene",out var why),why);Assert.True(workspace.Show("scene.json"),workspace.Said);
                if(mode is "scene2-cold" or "xy-cold")
                {
                    var scene2=(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(workspace)!;
                    foreach(var panel in scene2.Editor.Panels){scene2.Editor.SetOpen(panel.Id,true);targets.Add(panel.Title+"###panel."+panel.Id);}
                }
                else foreach(var panel in workspace.SceneWindow!.Editor.Panels){workspace.SceneWindow.Editor.SetOpen(panel.Id,true);targets.Add(panel.Title+"###panel."+panel.Id);}
            }
            else
            {
                Assert.True(workspace.Show("items.json"),workspace.Said);workspace.Window!.Editor.Select("/items/0");
                foreach(var panel in workspace.Window.Editor.Panels){workspace.Window.Editor.SetOpen(panel.Id,true);targets.Add(panel.Title+"###panel."+panel.Id);}
                Assert.True(workspace.RequestProjectPlay(),workspace.Said);targets.Add("Play console###project.play.console");
            }
            targets.Add("Outliner###outliner");targets.Add("Inspector###inspector");
            paint=()=>workspace.Draw();
        }
        if(mode.EndsWith("-cold",StringComparison.Ordinal))
        {
            // Fresh context, no first-use settings: load an actual persisted floating layout before any scene UI frame.
            var layout=new EditorLayoutStore(Path.Combine(root,"layout.ini"));
            Assert.True(layout.TryWrite(string.Join("\n",targets.Distinct().Select(target=>$"[Window][{target}]\nPos=1200,100\nSize=650,500\nCollapsed=0\n"))));
            Assert.True(layout.LoadCurrent());
        }
        Action draw=()=>{paint();if(current is not null){var window=Im.Internal.FindWindowByName(current);if(!window.IsNull()){Im.Internal.DockContextProcessUndockWindow(Im.GetCurrentContext(),window,true);Im.SetWindowPos(current,new(1200,100),ImGuiCond.Always);Im.SetWindowSize(current,new(650,500),ImGuiCond.Always);Im.SetWindowFocus(current);}}order.Add("draw");};
        var made=new List<(nint Handle,AccessKitNativeTreeA457Tests.Fake Tree,VirtualWindow Window)>();
        using var bridges=new AccessKitViewportBridges((handle,title)=>{var tree=new AccessKitNativeTreeA457Tests.Fake();var window=new VirtualWindow(tree);made.Add((handle,tree,window));return new AccessKitWindowBridge(handle,title,window,tree);});
        var hooksType=typeof(EditorApp).Assembly.GetType("Bed.Editor.App.AccessKitPlatformWindows",true)!;
        using var hooks=(IDisposable)Activator.CreateInstance(hooksType,BindingFlags.Instance|BindingFlags.NonPublic,null,["Virtual panel",bridges],null)!;
        var secondaryFrame=typeof(EditorApp).Assembly.GetType("Bed.Editor.App.EditorFrame",true)!.GetMethod("RenderSecondary",BindingFlags.Static|BindingFlags.NonPublic)!;
        var invalidInjected=new HashSet<uint>();var invalidSkipped=false;var coldFrames=new Dictionary<uint,int>();var cold=mode.EndsWith("-cold",StringComparison.Ordinal);var initialSceneBytes=File.Exists(Path.Combine(root,"data/scene.json"))?File.ReadAllBytes(Path.Combine(root,"data/scene.json")):[];
        Action complete=()=>order.Add("complete"),publish=()=>order.Add("publish"),save=()=>order.Add("save"),render=()=>{typeof(HeadlessHost).GetMethod("UpdateTextures",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,[Im.GetDrawData()]);order.Add("render");},secondary=()=>{
            Action inject=()=>{if(cold)
                foreach(var view in Im.GetPlatformIO().Viewports)
                    if(view.ID!=Im.GetMainViewport().ID)
                    {
                        coldFrames.TryGetValue(view.ID.Value,out var age);coldFrames[view.ID.Value]=age+1;
                        if(age<4){view.FramebufferScale=new(0,0);if(age==0)Thread.Sleep(100);}
                    }
                if(mode is "invalid-scale" or "invalid-size")
                foreach(var view in Im.GetPlatformIO().Viewports)
                    if(view.ID!=Im.GetMainViewport().ID&&invalidInjected.Add(view.ID.Value))
                    {if(mode=="invalid-scale")view.FramebufferScale=new(0,0);else view.Size=new(0,0);}
            };
            secondaryFrame.Invoke(null,[hooks,inject]);
            foreach(var view in Im.GetPlatformIO().Viewports)
            {
                if(view.ID==Im.GetMainViewport().ID)continue;
                var entry=made.Single(e=>e.Handle==view.PlatformHandleRaw);
                if(view.Size.X<=0||view.Size.Y<=0||view.FramebufferScale.X<=0||view.FramebufferScale.Y<=0)
                {Assert.Equal(0,entry.Tree.NewUpdates);invalidSkipped=true;}
                else entry.Window.Read();
            }
            order.Add("secondary");
        };
        var type=typeof(EditorApp).Assembly.GetType("Bed.Editor.App.EditorFrame",true)!;
        var frame=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[draw,complete,publish,save,render,secondary],null)!;
        var run=type.GetMethod("RunFrames",BindingFlags.Instance|BindingFlags.NonPublic)!;
        try
        {
            foreach(var target in targets.Distinct())
            {
                current=target;
                run.Invoke(frame,[8]);
                var label=target.Split("###")[0];
                var floating=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label==label);
                Assert.NotEqual(0u,floating.ViewportId);
                var published=Assert.Single(made,e=>e.Handle==(nint)floating.ViewportId);
                Assert.True(published.Tree.NewUpdates>0,target+" has no accessibility publication.");
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.ViewportId==floating.ViewportId&&n.Role!=UiRole.Window);
                output.WriteLine(mode+": "+target+" viewport="+floating.ViewportId+" updates="+published.Tree.NewUpdates);
            }
            Assert.Equal(Enumerable.Range(0,targets.Distinct().Count()*8).SelectMany(_=>new[]{"draw","complete","publish","save","render","secondary"}),order);
            if(mode is "invalid-scale" or "invalid-size"||cold)Assert.True(invalidSkipped);
            if(launcher?.Workspace is {} owner)
            {
                Assert.Empty(owner.Project.Dirty);
                Assert.Equal("{\"items\":[{\"id\":\"lamp\",\"name\":\"Lamp\"}]}",File.ReadAllText(Path.Combine(root,"data/items.json")));
                Assert.Equal(initialSceneBytes,File.ReadAllBytes(Path.Combine(root,"data/scene.json")));
                if(owner.SceneWindow is {} scene)Assert.Empty(scene.Editor.Session.Journal);
                if(mode is "scene2-cold" or "xy-cold"){Assert.Equal("scene.json",owner.Active!.Name);Assert.True(owner.Documents.TryGetOpenScene2("scene.json",out var typed));Assert.Empty(typed!.Session.Journal);Assert.False(typed.Session.Dirty);}
                if(owner.Window is {} data)Assert.Empty(data.Editor.Session.Journal);
            }
        }
        finally
        {
            hooks.Dispose();io.ConfigFlags&=~ImGuiConfigFlags.ViewportsEnable;
            foreach(var entry in made)
            {
                Assert.Equal(1,entry.Window.Frees);
                Assert.True(lifecycle.IndexOf("create:"+entry.Handle)<lifecycle.IndexOf("attach:"+entry.Handle));
                Assert.True(lifecycle.IndexOf("attach:"+entry.Handle)<lifecycle.IndexOf("show:"+entry.Handle));
                Assert.True(lifecycle.IndexOf("free:"+entry.Handle)<lifecycle.IndexOf("destroy:"+entry.Handle));
            }
            launcher?.Dispose();Directory.Delete(root,true);
        }

    }
}
