using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class FlatCameraPreferenceTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Root:IDisposable
    {internal readonly string Path=Directory.CreateTempSubdirectory("flat-camera-").FullName;public void Dispose()=>Directory.Delete(Path,true);}
    private sealed class Store
    {
        private readonly object value;private readonly Type type=typeof(ScenePresentation).Assembly.GetType("Bed.Editor.ImGui.FlatCameraPreferences",true)!;
        internal Store(string project,string level,string prefs)=>value=Activator.CreateInstance(type,Private,null,[project,level,prefs],null)!;
        internal string FileName=>(string)type.GetProperty("FileName",Private)!.GetValue(value)!;
        internal string Note=>(string)type.GetProperty("Note",Private)!.GetValue(value)!;
        internal (Vector2 Centre,float Zoom) Read()=>((Vector2,float))type.GetMethod("Read",Private)!.Invoke(value,null)!;
        internal void Remember(Vector2 centre,float zoom)=>type.GetMethod("Remember",Private)!.Invoke(value,[centre,zoom]);
    }
    [Fact]
    public void Saved_camera_is_strict_bounded_project_and_level_scoped_and_unchanged_remember_writes_nothing()
    {
        using var root=new Root();var project=System.IO.Path.Combine(root.Path,"project");var prefs=System.IO.Path.Combine(root.Path,"prefs");var store=new Store(project,"level.json",prefs);
        Assert.Equal((Vector2.Zero,64f),store.Read());Assert.False(File.Exists(store.FileName));store.Remember(new(2.5f,-8),128);
        Assert.InRange(new FileInfo(store.FileName).Length,1,4096);Assert.Empty(Directory.GetFiles(prefs,"*.tmp",SearchOption.AllDirectories));Assert.Equal((new Vector2(2.5f,-8),128f),new Store(project,"level.json",prefs).Read());
        var stamp=new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc);File.SetLastWriteTimeUtc(store.FileName,stamp);store.Remember(new(2.5f,-8),128);Assert.Equal(stamp,File.GetLastWriteTimeUtc(store.FileName));
        Assert.NotEqual(store.FileName,new Store(project,"other.json",prefs).FileName);Assert.NotEqual(store.FileName,new Store(project+"-other","level.json",prefs).FileName);
        if(OperatingSystem.IsWindows())Assert.Equal(store.FileName,new Store(project.ToUpperInvariant()+System.IO.Path.DirectorySeparatorChar,"LEVEL.JSON",prefs).FileName);
    }
    [Theory]
    [InlineData("{}")][InlineData("[]")][InlineData("{\"Version\":2,\"Centre\":[0,0],\"PixelsPerMetre\":64}")]
    [InlineData("{\"Version\":1,\"Centre\":[0,0,0],\"PixelsPerMetre\":64}")]
    [InlineData("{\"Version\":1,\"Centre\":[0,0],\"PixelsPerMetre\":1}")]
    [InlineData("{\"Version\":1,\"Centre\":[0,0],\"PixelsPerMetre\":2049}")]
    [InlineData("{\"Version\":1,\"Centre\":[1e100,0],\"PixelsPerMetre\":64}")]
    [InlineData("{\"Version\":1,\"Centre\":[0,0],\"PixelsPerMetre\":64,\"Version\":1}")]
    [InlineData("{\"Version\":1,\"Centre\":[0,0],\"PixelsPerMetre\":64,\"Extra\":0}")]
    [InlineData("{\"Version\":1,\"Centre\":[0,0],\"PixelsPerMetre\":64,}")]
    public void Invalid_private_preferences_restore_default_with_a_plain_reason(string text)
    {
        using var root=new Root();var store=new Store(root.Path,"level.json",root.Path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FileName)!);File.WriteAllText(store.FileName,text);
        Assert.Equal((Vector2.Zero,64f),store.Read());Assert.Contains("default view",store.Note);
    }
    [Fact]
    public void Oversize_and_failed_writes_do_not_block_editing_or_retry_for_each_unchanged_frame()
    {
        using var root=new Root();var store=new Store(root.Path,"level.json",root.Path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FileName)!);File.WriteAllText(store.FileName,new string(' ',4097));Assert.Equal((Vector2.Zero,64f),store.Read());Assert.NotEmpty(store.Note);
        File.Delete(store.FileName);Directory.CreateDirectory(store.FileName);store.Remember(new(1,2),64);Assert.Contains("Could not remember",store.Note);Directory.Delete(store.FileName);store.Remember(new(1,2),64);Assert.False(File.Exists(store.FileName));store.Remember(new(2,3),64);Assert.True(File.Exists(store.FileName));Assert.Equal("",store.Note);
    }
    [Fact]
    public void Actual_pan_and_zoom_save_only_after_gesture_end_and_leave_history_clean()
    {
        using var root=new Root();using var r=new Scene2FlatHandleTests.Rig();r.View.GetType().GetMethod("BindPreferences",Private)!.Invoke(r.View,[root.Path,"level.json",root.Path]);r.Ui.Frames(r.Draw,2);Assert.Empty(Directory.GetFiles(root.Path,"*.json",SearchOption.AllDirectories));
        var canvas=r.Node("Flat XY");var at=new Vector2(canvas.Bounds.X+100,canvas.Bounds.Y+100);var io=ImGui.GetIO();io.AddMousePosEvent(at.X,at.Y);r.Ui.Frame(r.Draw);io.AddMouseButtonEvent(1,true);r.Ui.Frame(r.Draw);io.AddMousePosEvent(at.X+40,at.Y-80);r.Ui.Frames(r.Draw,2);Assert.Empty(Directory.GetFiles(root.Path,"*.json",SearchOption.AllDirectories));
        io.AddMouseButtonEvent(1,false);r.Ui.Frames(r.Draw,2);var file=Assert.Single(Directory.GetFiles(root.Path,"*.json",SearchOption.AllDirectories));var store=new Store(root.Path,"level.json",root.Path);Assert.Equal((new Vector2(-.625f,-1.25f),64f),store.Read());
        var stamp=new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc);File.SetLastWriteTimeUtc(file,stamp);r.Ui.Frames(r.Draw,3);Assert.Equal(stamp,File.GetLastWriteTimeUtc(file));io.AddMouseWheelEvent(0,1);r.Ui.Frame(r.Draw);Assert.Equal(stamp,File.GetLastWriteTimeUtc(file));r.Ui.Frame(r.Draw);Assert.NotEqual(stamp,File.GetLastWriteTimeUtc(file));Assert.True(store.Read().Zoom>64);Assert.Empty(r.Editor.Session.Journal);Assert.False(r.Editor.Session.Dirty);
    }
    [Fact]
    public void Corrupt_camera_reason_is_visible_above_the_canvas_without_blocking_or_dirtying_the_scene()
    {
        using var root=new Root();var store=new Store(root.Path,"level.json",root.Path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FileName)!);File.WriteAllText(store.FileName,"{broken");using var r=new Scene2FlatHandleTests.Rig();r.View.GetType().GetMethod("BindPreferences",Private)!.Invoke(r.View,[root.Path,"level.json",root.Path]);r.Ui.Frames(r.Draw,2);
        var reason=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("using the default view",StringComparison.Ordinal));Assert.False(reason.Bounds.IsEmpty);Assert.True(reason.Bounds.Y<r.Node("Flat XY").Bounds.Y);Assert.Empty(r.Editor.Session.Journal);Assert.False(r.Editor.Session.Dirty);
    }
    [Fact]
    public void Retained_inactive_flat_view_cannot_pan_or_write_its_former_preferences()
    {
        using var root=new Root();using var r=new Scene2FlatHandleTests.Rig();var type=r.View.GetType();type.GetMethod("BindPreferences",Private)!.Invoke(r.View,[root.Path,"level.json",root.Path]);type.GetProperty("IsCurrent",Private)!.SetValue(r.View,(Func<bool>)(()=>false));
        var io=ImGui.GetIO();io.AddMousePosEvent(200,400);io.AddMouseButtonEvent(1,true);r.Ui.Frame(r.Draw);io.AddMousePosEvent(300,500);r.Ui.Frame(r.Draw);io.AddMouseButtonEvent(1,false);r.Ui.Frames(r.Draw,2);Assert.Empty(Directory.GetFiles(root.Path,"*.json",SearchOption.AllDirectories));Assert.Empty(r.Editor.Session.Journal);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("no longer current"));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Workspace_preferences_restore_on_reopen_and_when_binding_after_the_flat_entry_opens(bool late)
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json");using var root=new Root();var store=new Store(r.Project.DataFolder,"level.json",root.Path);store.Remember(new(12,-3),96);
        using var workspace=new DataWorkspace(r.Project);
        if(!late)workspace.BindPlaySettings(r.Root,null,root.Path);Assert.True(workspace.Show("level.json"));if(late)workspace.BindPlaySettings(r.Root,null,root.Path);
        (Vector2,float) Camera()
        {var windows=(IDictionary)typeof(DataWorkspace).GetField("scene2Windows",Private)!.GetValue(workspace)!;var native=windows["level.json"]!;var flat=native.GetType().GetField("flatView",Private)!.GetValue(native)!;return ((Vector2)flat.GetType().GetField("centre",Private)!.GetValue(flat)!,(float)flat.GetType().GetField("pixelsPerMetre",Private)!.GetValue(flat)!);}
        Assert.Equal((new Vector2(12,-3),96f),Camera());Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(workspace.Show("level.json"));Assert.Equal((new Vector2(12,-3),96f),Camera());Assert.False(r.Entry.Session.Dirty);Assert.Empty(r.Entry.Session.Journal);
    }
}
