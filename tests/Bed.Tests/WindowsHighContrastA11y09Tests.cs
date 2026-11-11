using System.Reflection;
using Bed.Editor.App;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class WindowsHighContrastA11y09Tests
{
    private sealed class Readers:IDisposable
    {
        private readonly FieldInfo high=typeof(EditorTheme).GetField("readHighContrast",BindingFlags.Static|BindingFlags.NonPublic)!;
        private readonly FieldInfo mode=typeof(EditorTheme).GetField("readAppMode",BindingFlags.Static|BindingFlags.NonPublic)!;
        private readonly FieldInfo clock=typeof(EditorTheme).GetField("systemTicks",BindingFlags.Static|BindingFlags.NonPublic)!;
        private readonly object oldHigh,oldMode,oldClock;
        private readonly EditorThemePreference original=EditorTheme.Preference;
        internal bool? High;internal int? AppMode=1;internal int Reads;internal long Ticks=100;
        public Readers(){oldHigh=high.GetValue(null)!;oldMode=mode.GetValue(null)!;oldClock=clock.GetValue(null)!;clock.SetValue(null,(Func<long>)(()=>Ticks));high.SetValue(null,(Func<bool?>)(()=>{Reads++;return High;}));mode.SetValue(null,(Func<int?>)(()=>AppMode));EditorTheme.Preference=EditorThemePreference.System;}
        public void Dispose(){high.SetValue(null,oldHigh);mode.SetValue(null,oldMode);clock.SetValue(null,oldClock);EditorTheme.Preference=original;}
    }
    private sealed class Native:IWindowsThemeNotificationApi
    {
        internal ThemeWindowProcedure? Procedure;internal nuint Id;internal int Adds,Removes,Forwards;internal bool Added=true,Removed=true,ThrowRemove;
        public bool Attach(nint w,ThemeWindowProcedure p,nuint id){Adds++;Procedure=p;Id=id;return Added;}
        public bool Detach(nint w,ThemeWindowProcedure p,nuint id){Assert.Same(Procedure,p);Assert.Equal(Id,id);Removes++;if(ThrowRemove)throw new InvalidOperationException("Fixture detach failure");return Removed;}
        public nint Next(nint w,uint m,nuint wp,nint lp){Forwards++;return 77;}
        public nint Send(uint message)=>Procedure!(1,message,0,0,Id,0);
    }
    [Theory][InlineData(true,1)][InlineData(false,1)][InlineData(null,1)][InlineData(false,0)]
    public void System_resolves_fake_high_contrast_before_app_mode_without_changing_the_preference_or_text_metrics(bool? high,int mode)
    {
        using var readers=new Readers{High=high,AppMode=mode};using var host=new HeadlessHost(500,400){Drawing=false};
        var style=ImGui.GetStyle();style.FontScaleMain=1.5f;style.FontScaleDpi=2;var padding=style.WindowPadding;
        EditorTheme.Refresh();var expected=high==true?0:mode==1?0xF7/255f:0x1B/255f;
        Assert.Equal(expected,style.Colors[(int)ImGuiCol.WindowBg].X,4);Assert.Equal(EditorThemePreference.System,EditorTheme.Preference);
        Assert.Equal(1.5f,style.FontScaleMain);Assert.Equal(2,style.FontScaleDpi);Assert.Equal(padding,style.WindowPadding);
        var reads=readers.Reads;EditorTheme.Refresh();Assert.Equal(reads,readers.Reads);
    }
    [Fact]
    public void Both_settings_notifications_invalidate_cache_and_refresh_the_start_screen_on_its_next_frame()
    {
        using var readers=new Readers{High=false};using var host=new HeadlessHost(1000,700){Drawing=false};
        var root=Directory.CreateTempSubdirectory("hc-notification-").FullName;
        try
        {
            using var launcher=new ProjectLauncher(new RecentProjects(Path.Combine(root,"local/recent.json")));host.Frames(launcher.Draw,3);
            var api=new Native();using var watcher=new WindowsThemeNotifications(1,api,WindowsThemeNotifications.Changed);
            var reads=readers.Reads;readers.High=true;Assert.Equal((nint)77,api.Send(0x001A));Assert.Equal(reads,readers.Reads);
            host.Frame(launcher.Draw);Assert.Equal(reads+1,readers.Reads);Assert.Equal(0,ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].X);
            Assert.Equal(EditorThemePreference.System,EditorTheme.Preference);Assert.False(File.Exists(launcher.SettingsFile));
            reads=readers.Reads;readers.High=false;api.Send(0x031A);host.Frame(launcher.Draw);Assert.Equal(reads+1,readers.Reads);Assert.True(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].X>.9f);
            reads=readers.Reads;api.Send(0x0200);host.Frame(launcher.Draw);Assert.Equal(reads,readers.Reads);Assert.Equal(3,api.Forwards);
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory][InlineData(EditorThemePreference.Light)][InlineData(EditorThemePreference.Dark)][InlineData(EditorThemePreference.HighContrast)]
    public void Explicit_choices_do_not_query_or_change_on_Windows_notifications(EditorThemePreference choice)
    {
        using var readers=new Readers{High=true};using var host=new HeadlessHost(500,400){Drawing=false};EditorTheme.Preference=choice;EditorTheme.Refresh();var count=readers.Reads;
        var color=ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];var api=new Native();using var watcher=new WindowsThemeNotifications(1,api,WindowsThemeNotifications.Changed);
        api.Send(0x001A);api.Send(0x031A);EditorTheme.Refresh();Assert.Equal(count,readers.Reads);Assert.Equal(choice,EditorTheme.Preference);Assert.Equal(color,ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg]);
    }
    [Fact]
    public void Observer_forwards_even_when_notifier_throws_and_detaches_once_without_a_window()
    {
        var api=new Native();var calls=0;var watcher=new WindowsThemeNotifications(1,api,()=>{calls++;throw new InvalidOperationException("Fixture notifier");});
        Assert.True(watcher.Retained);Assert.Equal((nint)77,api.Send(0x001A));Assert.Equal(1,calls);Assert.Equal(1,api.Forwards);
        watcher.Dispose();watcher.Dispose();Assert.Equal(1,api.Removes);Assert.False(watcher.Retained);
        api.Send(0x001A);Assert.Equal(1,calls);Assert.Equal(2,api.Forwards);
    }
    [Fact]
    public void Failed_detach_keeps_callback_alive_until_owned_HWND_destruction()
    {
        var api=new Native{Removed=false};var calls=0;var watcher=new WindowsThemeNotifications(1,api,()=>calls++);watcher.Dispose();Assert.True(watcher.Retained);
        api.Send(0x001A);Assert.Equal(0,calls);api.Send(0x0082);Assert.False(watcher.Retained);Assert.Equal(2,api.Forwards);
    }
    [Fact]
    public void Failed_install_releases_callback_and_both_hosts_wire_observer_before_running_the_shared_frame()
    {
        var retained=WindowsThemeNotifications.RetainedCount;Assert.Throws<InvalidOperationException>(()=>new WindowsThemeNotifications(1,new Native{Added=false},()=>{}));Assert.Equal(retained,WindowsThemeNotifications.RetainedCount);
        foreach(var name in new[]{"Loop","RunOpenGl"})
        {
            var method=typeof(EditorApp).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!;var calls=SceneFrameLifecycleTests.Calls(method);
            var attach=Assert.Single(calls,c=>c.Method.DeclaringType==typeof(WindowsThemeNotifications)&&c.Method.Name=="Attach");
            var run=Assert.Single(calls,c=>c.Method.DeclaringType==SceneFrameLifecycleTests.FrameType&&c.Method.Name=="Run");
            var local=Assert.Single(method.GetMethodBody()!.LocalVariables,v=>v.LocalType==typeof(WindowsThemeNotifications)).LocalIndex;
            var il=method.GetMethodBody()!.GetILAsByteArray()!;
            bool Receiver(int offset) => local<4&&offset>=1&&il[offset-1]==0x06+local
                || offset>=2&&il[offset-2]==0x11&&il[offset-1]==local
                || offset>=4&&il[offset-4]==0xfe&&il[offset-3]==0x0c&&BitConverter.ToUInt16(il,offset-2)==local;
            var dispose=Assert.Single(calls,c=>c.Method.DeclaringType==typeof(IDisposable)&&c.Method.Name=="Dispose"&&Receiver(c.Offset));
            Assert.True(attach.Offset<run.Offset&&run.Offset<dispose.Offset);
        }
    }
    [Fact]
    public void Fallback_poll_rechecks_after_one_fake_second_without_sleeping_or_a_notification()
    {
        using var readers=new Readers{High=false};using var host=new HeadlessHost(400,300){Drawing=false};var count=readers.Reads;
        readers.High=true;readers.Ticks+=999;EditorTheme.Refresh();Assert.Equal(count,readers.Reads);
        readers.Ticks+=1;EditorTheme.Refresh();Assert.Equal(count+1,readers.Reads);Assert.Equal(0,ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].X);
    }
    [Fact]
    public void Wrong_thread_disposal_refuses_and_detach_exceptions_keep_the_callback_until_destroy()
    {
        var api=new Native();var watcher=new WindowsThemeNotifications(1,api,()=>{});
        Exception? error=null;var foreign=new Thread(()=>error=Record.Exception(watcher.Dispose));foreign.Start();Assert.True(foreign.Join(TimeSpan.FromSeconds(5)));Assert.IsType<InvalidOperationException>(error);Assert.Equal(0,api.Removes);Assert.True(watcher.Retained);
        api.ThrowRemove=true;Assert.Null(Record.Exception(watcher.Dispose));Assert.True(watcher.Retained);Assert.Equal((nint)77,api.Send(0x0082));Assert.False(watcher.Retained);
    }

    [Fact]
    public void System_high_contrast_scene_uses_the_resolved_grid_and_selection_palette()
    {
        using var readers=new Readers{High=true};using var host=new HeadlessHost(900,700){Drawing=false};
        var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),new Scene([SceneObject.At("crate",null,new(0,0,0))])),SceneEditing.Outline);editor.Select("crate");
        var view=new ViewportView<Scene>(editor,new SceneViewport());
        host.Frames(()=>{ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(850,650));Ui.Begin("System scene contrast"u8);view.Draw();Ui.End();},4);
        var colors=new HashSet<uint>();var draw=ImGui.GetDrawData();for(var i=0;i<draw.CmdListsCount;i++)foreach(var vertex in draw.CmdLists[i].VtxBuffer)colors.Add(vertex.col);
        Assert.Equal(EditorThemePreference.System,EditorTheme.Preference);Assert.Contains(0xff000000u,colors);Assert.Contains(0xffaaaaaau,colors);Assert.Contains(0xff00ffffu,colors);Assert.Empty(editor.Session.Journal);
    }

}
