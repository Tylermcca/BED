using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Xunit;
using BedProgram=Bed.Launcher.Program;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class RotateScaleKeyboardQa12Tests
{
    [Fact]
    public void Temp_sample_crate_rotates_scales_undoes_redoes_saves_and_Plays_using_only_real_keys()
    {
        using var owned=new CapturedExportTests.Owned();
        var root=Directory.CreateDirectory(Path.Combine(owned.Root,"project with spaces")).FullName;
        var sample=Path.Combine(AppContext.BaseDirectory,"samples","scene-starter");
        var originals=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var relative in new[]{"reference_map.json","data/level.json","schemas/scene.schema.json"}
            .Concat(Directory.GetFiles(Path.Combine(sample,"assets"),"*",SearchOption.AllDirectories).Select(file=>Path.GetRelativePath(sample,file))))
        {
            var bytes=File.ReadAllBytes(Path.Combine(sample,relative));originals.Add(relative,bytes);
            var copy=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.WriteAllBytes(copy,bytes);
        }
        Directory.CreateDirectory(Path.Combine(root,"game"));
        foreach(var name in SceneStarter.ExportInventory.RuntimeFiles)
            File.Copy(Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter",name),Path.Combine(root,"game",name));
        PlaySettingsStore.Save(root,new("game/SceneStarter.exe",["--project",".","--seconds","0"]));
        var settings=File.ReadAllBytes(Path.Combine(root,".bed","project.json"));
        using var host=new HeadlessHost(1600,1000){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var adapters=Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));Assert.Empty(adapters.Problems);
        using var launcher=BedProgram.SetUp(new(new RecentProjects(Path.Combine(owned.Root,"local","recent.json"))){Loaded=adapters.Loaded});
        var play=new SamplePlayJourneyTests.ObservedHost(Assert.IsType<ProjectPlayBridge>(launcher.ProjectPlayHost));launcher.ProjectPlayHost=play;
        void Frames()=>host.Frames(launcher.Draw,2);
        void Key(ImGuiKey key,bool ctrl=false,bool shift=false,bool alt=false)
        {host.Press(launcher.Draw,key,ctrl,shift,alt);Frames();}
        host.Frames(launcher.Draw,4);Key(ImGuiKey.O,ctrl:true);host.Type(launcher.Draw,root);
        Key(ImGuiKey.Tab);Key(ImGuiKey.Tab);Key(ImGuiKey.Enter);
        var workspace=launcher.Workspace;Assert.NotNull(workspace);Assert.Equal("SceneStarter",launcher.Using!.Name);
        // Navigation follows QA08's real-key proof: observe the tree/native focus, never write focus or dispatch actions.
        string NativeName(ImGuiWindowPtr window)=>window.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)window.Name)??"";
        string WindowFor(UiNode node)
        {
            var nodes=Ui.Snapshot().Nodes;
            var name="";
            for(var parent=node.Parent;parent>=0;parent=nodes[parent].Parent)
                if(nodes[parent].Role==UiRole.Window)name=nodes[parent].Label;
            return name;
        }
        void InWindow(string name)
        {
            if(NativeName(ImGui.GetCurrentContext().NavWindow).Contains(name,StringComparison.Ordinal))return;
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                for(var i=0;i<25;i++)
                {
                    Key(ImGuiKey.Tab);
                    if(NativeName(ImGui.GetCurrentContext().NavWindowingTarget).Contains(name,StringComparison.Ordinal))break;
                }
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Frames();}
            Assert.Contains(name,NativeName(ImGui.GetCurrentContext().NavWindow),StringComparison.Ordinal);
        }
        void Reach(Func<UiNode,bool> match)
        {
            var target=Assert.Single(Ui.Snapshot().Nodes,n=>match(n));Assert.True(target.Enabled,target.DisabledReason);
            var window=WindowFor(target);Assert.NotEmpty(window);
            // Escape returns from native child-list navigation to the panel's controls.
            if(target.Role!=UiRole.ListItem&&NativeName(ImGui.GetCurrentContext().NavWindow).Contains('/'))Key(ImGuiKey.Escape);
            InWindow(window);
            for(var i=0;i<65;i++)
            {
                target=Assert.Single(Ui.Snapshot().Nodes,n=>match(n));
                if(target.Focused)return;
                var navigation=ImGui.GetCurrentContext();
                var navWindow=navigation.NavWindow;
                var rectangle=navWindow.IsNull()?default:navWindow.NavRectRel[(int)navigation.NavLayer];
                // Tab lands on the native child-list container; Enter enters its item navigation.
                if(target.Role==UiRole.ListItem&&rectangle.Max.Y-rectangle.Min.Y>80)Key(ImGuiKey.Enter);
                else Key(ImGuiKey.Tab);
            }
            Assert.Fail("Keyboard could not reach "+target.Role+" "+target.Label+" in "+window+"\n"+Ui.Snapshot().Text);
        }
        void Activate(Func<UiNode,bool> match){Reach(match);Key(ImGuiKey.Enter);}
        void Until(Func<bool> done,string step)
        {
            var timer=Stopwatch.StartNew();while(!done()&&timer.Elapsed<TimeSpan.FromSeconds(10)){host.Frame(launcher.Draw);Thread.Sleep(5);}
            Assert.True(done(),step+": "+workspace.Said+"; "+Ui.Snapshot().Text);
        }
        Activate(n=>n.Role==UiRole.ListItem&&n.Label.Contains("level.json",StringComparison.Ordinal));
        var window=workspace.SceneWindow!;Assert.NotNull(window);
        Activate(n=>n.Role==UiRole.ListItem&&n.Label=="textured_crate"&&WindowFor(n)=="Outliner");
        var session=window.Editor.Session;var initial=session.Current;var crate=initial.Find("textured_crate")!;
        Assert.NotNull(crate);Assert.Equal("c8a7e000000000000000000000000001",crate.Asset);
        Assert.Equal(crate.Id,window.Editor.Primary);Assert.Equal(0,session.History.UndoCount);
        var pointer=File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json"));
        void Edit(string label,string text)
        {
            Reach(n=>n.Role==UiRole.TextField&&n.Label==label);
            var id=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label==label).Id;
            Key(ImGuiKey.A,ctrl:true);host.Type(launcher.Draw,text);Key(ImGuiKey.Enter);Key(ImGuiKey.Escape);
            Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Id==id);
        }
        Edit("Yaw","37.5");Edit("Pitch","-12");Edit("Roll","8");
        var expectedRotation=Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(37.5f*MathF.PI/180,-12f*MathF.PI/180,8f*MathF.PI/180));
        var rotated=session.Current.Find(crate.Id)!;SameRotation(expectedRotation,rotated.Rotation);Assert.Equal(crate.Scale,rotated.Scale);
        Edit("Scale X","1.5");Edit("Scale Y","0.75");Edit("Scale Z","2.25");
        var expectedScale=new Vector3(1.5f,.75f,2.25f);
        Assert.Equal(expectedScale,session.Current.Find(crate.Id)!.Scale);Assert.Equal(6,session.History.UndoCount);
        Assert.Equal(originals["data/level.json"],File.ReadAllBytes(Path.Combine(root,"data","level.json")));
        Key(ImGuiKey.Z,ctrl:true);var undone=session.Current.Find(crate.Id)!;
        Assert.Equal(new Vector3(1.5f,.75f,crate.Scale.Z),undone.Scale);SameRotation(expectedRotation,undone.Rotation);
        Assert.Equal(5,session.History.UndoCount);Assert.True(session.History.CanRedo);
        Key(ImGuiKey.Y,ctrl:true);Assert.Equal(expectedScale,session.Current.Find(crate.Id)!.Scale);
        SameRotation(expectedRotation,session.Current.Find(crate.Id)!.Rotation);Assert.Equal(6,session.History.UndoCount);Assert.False(session.History.CanRedo);
        Assert.True(session.Dirty);Assert.Equal(originals["data/level.json"],File.ReadAllBytes(Path.Combine(root,"data","level.json")));
        Key(ImGuiKey.S,ctrl:true);Until(()=>!session.Dirty,"Save transform");
        var saved=File.ReadAllBytes(Path.Combine(root,"data","level.json"));Assert.NotEqual(originals["data/level.json"],saved);
        using(var json=JsonDocument.Parse(saved))
        {
            var item=Assert.Single(json.RootElement.GetProperty("objects").EnumerateArray(),o=>o.GetProperty("id").GetString()==crate.Id);
            var rotation=item.GetProperty("rotation").EnumerateArray().Select(v=>v.GetSingle()).ToArray();Assert.Equal(4,rotation.Length);
            SameRotation(expectedRotation,new(rotation[0],rotation[1],rotation[2],rotation[3]));
            Assert.Equal(new[]{1.5f,.75f,2.25f},item.GetProperty("scale").EnumerateArray().Select(v=>v.GetSingle()));
            Assert.Equal(crate.Asset,item.GetProperty("asset").GetString());
            Assert.Equal(new[]{crate.Position.X,crate.Position.Y,crate.Position.Z},item.GetProperty("position").EnumerateArray().Select(v=>v.GetSingle()));
        }
        var loaded=SceneStarter.SavedScene.Open(root);var loadedCrate=loaded.Level.Scene.Find(crate.Id)!;
        Assert.Equal(expectedScale,loadedCrate.Scale);SameRotation(expectedRotation,loadedCrate.Rotation);
        Assert.Equal(initial.Objects.Select(o=>o.Id),loaded.Level.Scene.Objects.Select(o=>o.Id));
        foreach(var other in initial.Objects.Where(o=>o.Id!=crate.Id))
            Assert.Equal(new SceneFormat().Write(new Scene([other])),new SceneFormat().Write(new Scene([loaded.Level.Scene.Find(other.Id)!])));
        Key(ImGuiKey.F5);Assert.False(workspace.ProjectPlaySavePending);
        Until(()=>play.Session is {HasCompleted:true,ExitCode:not null},"Saved transform Play exit");
        Assert.Equal(1,play.Starts);Assert.Equal(0,play.Session!.ExitCode);Assert.Equal(0,play.Session.Stops);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(saved),play.Session.SavedAtStart);
        Assert.Contains(play.Session.Logs,line=>!line.Error&&line.Text=="SceneStarter loaded saved level: 3 objects, 1 meshes.");
        Assert.Equal(6,session.History.UndoCount);Assert.False(session.History.CanRedo);Assert.False(session.Dirty);
        foreach(var (relative,bytes) in originals.Where(pair=>pair.Key!="data/level.json"))Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(root,relative)));
        Assert.Equal(saved,File.ReadAllBytes(Path.Combine(root,"data","level.json")));
        Assert.Equal(settings,File.ReadAllBytes(Path.Combine(root,".bed","project.json")));
        Assert.Equal(pointer,File.ReadAllBytes(Path.Combine(root,".bed","cache","current.json")));
        static void SameRotation(Quaternion expected,Quaternion actual)
        {Assert.InRange(MathF.Abs(expected.X-actual.X),0,1e-5f);Assert.InRange(MathF.Abs(expected.Y-actual.Y),0,1e-5f);Assert.InRange(MathF.Abs(expected.Z-actual.Z),0,1e-5f);Assert.InRange(MathF.Abs(expected.W-actual.W),0,1e-5f);}
    }
}
