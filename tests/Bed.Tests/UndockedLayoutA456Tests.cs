using System.Numerics;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class UndockedLayoutA456Tests
{
    private sealed class Files : IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"bed-layout-test-"+Guid.NewGuid().ToString("N"));
        public EditorLayoutStore Store {get;}
        public Files(){Directory.CreateDirectory(Root);Store=new(Path.Combine(Root,"layout.ini"));}
        public void Dispose(){foreach(var f in Directory.GetFiles(Root))File.Delete(f);Directory.Delete(Root);}
    }
    [Fact]
    public void Bounded_store_round_trips_and_rejects_corruption_oversize_and_missing()
    {
        using var f=new Files();Assert.False(f.Store.TryRead(out _));
        const string ini="[Window][Test]\nPos=25,40\nSize=320,180\nCollapsed=0\n";
        Assert.True(f.Store.TryWrite(ini));Assert.True(f.Store.TryRead(out var read));Assert.Equal(ini,read);
        var good=File.ReadAllBytes(f.Store.Path);
        Assert.False(f.Store.TryWrite(new string('x',EditorLayoutStore.MaxBytes+1)));Assert.Equal(good,File.ReadAllBytes(f.Store.Path));
        File.AppendAllText(f.Store.Path,"bad");Assert.False(f.Store.TryRead(out _));
        File.WriteAllBytes(f.Store.Path,new byte[EditorLayoutStore.MaxBytes+100]);Assert.False(f.Store.TryRead(out _));
        File.WriteAllText(f.Store.Path,"not ini");Assert.False(f.Store.TryRead(out _));
    }
    [Fact]
    public void Failed_write_is_nonfatal_and_does_not_replace_existing_directory()
    {
        using var f=new Files();var directory=new EditorLayoutStore(f.Root);
        Assert.False(directory.TryWrite("[Window][x]\n"));Assert.True(Directory.Exists(f.Root));
        Assert.False(directory.TryRead(out _));Assert.Empty(Directory.GetFiles(f.Root));
    }
    [Fact]
    public void Native_ini_round_trip_restores_floating_window_position_without_os_windows()
    {
        using var f=new Files();
        using(var host=new HeadlessHost(1200,900){Drawing=false})
        {
            void Draw(){ImGui.SetNextWindowPos(new float2(317,211),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(333,222),ImGuiCond.Always);Ui.Begin("Floating test");Ui.Text("body");Ui.End();}
            host.Frames(Draw,3);Assert.True(f.Store.SaveCurrent());
            Assert.False(ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable));
        }
        using(var host=new HeadlessHost(1200,900){Drawing=false})
        {
            Assert.True(f.Store.LoadCurrent());float2 pos=default,size=default;
            void Draw(){Ui.Begin("Floating test");pos=ImGui.GetWindowPos();size=ImGui.GetWindowSize();Ui.Text("body");Ui.End();}
            host.Frames(Draw,3);Assert.Equal(317,pos.X);Assert.Equal(211,pos.Y);Assert.Equal(333,size.X);Assert.Equal(222,size.Y);
        }
    }
    private static EditorWindow<Scene> Window()
    {
        var scene=new Scene([SceneObject.At("object",null,Vector3.Zero)]);
        var model=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        return new(model,new Inspector<Scene>(model).Add(SceneEditing.ObjectType()));
    }
    [Fact]
    public void New_editor_adopts_existing_dock_tree_and_reset_rebuilds_without_editing_document()
    {
        using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var first=Window();host.Frames(first.DrawDocked,3);
        ImGuiID space=default,original=default,custom=default;
        void Customize()
        {
            space=ImGui.GetID("brutal.authoring.dock"u8);
            original=ImGui.Internal.DockBuilderGetCentralNode(space).ID;
            ImGui.Internal.DockBuilderSplitNode(original,ImGuiDir.Up,.35f,out _,out custom);
            ImGui.Internal.DockBuilderFinish(space);
        }
        host.Frames(Customize,1);
        var second=Window();var before=second.Editor.Session.Current;host.Frames(second.DrawDocked,3);
        Assert.Equal(custom.Value,ImGui.Internal.DockBuilderGetCentralNode(space).ID.Value);
        Assert.True(second.UiActions().Single(a=>a.Id=="editor.reset_layout").Invoke());host.Frames(second.DrawDocked,3);
        Assert.Equal(original.Value,ImGui.Internal.DockBuilderGetCentralNode(space).ID.Value);
        Assert.Same(before,second.Editor.Session.Current);Assert.Equal(0,second.Editor.Session.History.UndoCount);
        Assert.False(second.Editor.Session.Dirty);
    }
    [Fact]
    public void Saved_custom_dock_tree_is_adopted_after_a_new_context()
    {
        using var files=new Files();uint saved=0;
        using(var host=new HeadlessHost(1200,900){Drawing=false})
        {
            EditorWindow<Scene>.Configure(ImGui.GetIO());var window=Window();host.Frames(window.DrawDocked,3);
            void Split()
            {
                var space=ImGui.GetID("brutal.authoring.dock"u8);
                var center=ImGui.Internal.DockBuilderGetCentralNode(space);
                ImGui.Internal.DockBuilderSplitNode(center.ID,ImGuiDir.Up,.35f,out var top,out var rest);
                ImGui.Internal.DockBuilderDockWindow("Inspector###inspector"u8,top);
                saved=rest.Value;ImGui.Internal.DockBuilderFinish(space);
            }
            host.Frames(Split,1);host.Frames(window.DrawDocked,2);Assert.True(files.Store.SaveCurrent());
        }
        using(var host=new HeadlessHost(1200,900){Drawing=false})
        {
            EditorWindow<Scene>.Configure(ImGui.GetIO());Assert.True(files.Store.LoadCurrent());
            var window=Window();host.Frames(window.DrawDocked,3);
            uint actual=0;
            void Inspect(){actual=ImGui.Internal.DockBuilderGetCentralNode(ImGui.GetID("brutal.authoring.dock"u8)).ID.Value;}
            host.Frames(Inspect,1);files.Store.TryRead(out var snapshot);Assert.True(saved==actual,$"Expected {saved}, got {actual}; saved settings: {snapshot}");
        }
    }}
