using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class SceneAddObjectTests
{
    private sealed class Rig : IDisposable
    {
        public const string Id = "11111111111111111111111111111111";
        public readonly string Root = Directory.CreateTempSubdirectory("scene-add-").FullName;
        public readonly AssetCatalog Catalog = new();
        public readonly HeadlessHost Host = new(1600, 1000) { Drawing = false };
        public readonly DataWorkspace Workspace;
        public readonly AssetBrowser Browser;
        public EditorWindow<Scene> Window => Workspace.SceneWindow!;
        public Rig(bool bind = true, bool admit = true)
        {
            Directory.CreateDirectory(Path.Combine(Root,"data"));
            Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            var assets = Directory.CreateDirectory(Path.Combine(Root,"assets")).FullName;
            File.WriteAllText(Path.Combine(Root,"data","room.json"),new SceneFormat().Write(Scene.Empty));
            byte[] bytes = [1,2,3];
            var record = new AssetRecord(Id,"item.bin",Convert.ToHexStringLower(SHA256.HashData(bytes)),"fixture",1,
                JsonSerializer.SerializeToElement(new {}),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllBytes(Path.Combine(assets,"item.bin"),bytes);
            File.WriteAllText(Path.Combine(assets,"item.bin")+AssetRecord.Suffix,record.Write());
            Browser = new(assets,".bin");
            if(admit) Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,bytes,[],true,"fixture","initial")],out _));
            Workspace = new(DataProject.Open(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")));
            Assert.True(Workspace.Documents.BindScene("room.json","scene",out _));
            if(bind) Assert.True(Workspace.BindSceneAssets(Browser,Catalog));
            Assert.True(Workspace.Show("room.json"));
            EditorWindow<Scene>.Configure(ImGui.GetIO());
            Host.Frames(Workspace.Draw,3);
        }
        public void OpenPicker()
        {
            Assert.True(Window.HandleKey(Chord.Parse("Insert")));
            Assert.True(Window.Editor.IsOpen("scene.assets"));
            Host.Frames(Workspace.Draw,3);
        }
        public void Click(string label, UiRole role)
        {
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==role);
            var b=node.Bounds;Host.Click(Workspace.Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Workspace.Draw,2);
        }
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }

    [Theory][InlineData(1f)][InlineData(2f)]
    public void Picker_header_list_and_pinned_actions_are_reachable_by_native_Tab_at_each_text_size(float scale)
    {
        using var r=new Rig();EditorWindow<Scene>.SetTextScale(scale);r.Host.Frames(r.Workspace.Draw,4);r.OpenPicker();
        void Key(ImGuiKey key)=>r.Host.Press(r.Workspace.Draw,key);
        void Reach(string name,UiRole role)
        {
            for(var n=0;n<40;n++)
            {
                if(Ui.Snapshot().Nodes.Any(item=>item.Role==role&&item.Label==name&&item.Focused&&item.Enabled))return;
                Key(ImGuiKey.Tab);
            }
            Assert.Fail($"Native Tab did not reach {role}:{name} at {scale}x; "+string.Join(", ",Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Role+":"+n.Label)));
        }
        Reach("Search assets",UiRole.TextField);Reach("Problems only",UiRole.Checkbox);
        Key(ImGuiKey.Tab);Key(ImGuiKey.Enter);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="item.bin"&&n.Focused);
        Key(ImGuiKey.Enter);Key(ImGuiKey.Escape);Reach("Add selected object",UiRole.Button);Reach("Cancel placement",UiRole.Button);
        Assert.Empty(r.Window.Editor.Session.Journal);Assert.False(r.Window.Editor.Session.Dirty);
    }
    [Fact]
    public void No_catalog_keeps_registered_add_disabled_with_exact_reason()
    {
        using var r=new Rig(bind:false);
        var action=Assert.Single(r.Window.PaletteEntries("Add object."),e=>e.Id=="editor.panel_do.scene.assets/add");
        Assert.Equal("No assets for this project",action.Unavailable);
        Assert.False(r.Window.HandleKey(Chord.Parse("Insert")));
        Assert.False(r.Window.Editor.IsOpen("scene.assets"));
        Assert.Empty(r.Window.Editor.Session.Current.Objects);
    }

    [Fact]
    public void Insert_opens_real_picker_and_adds_at_view_centre_with_one_undo_without_saving()
    {
        using var r=new Rig();
        var path=Path.Combine(r.Root,"data","room.json");var saved=File.ReadAllText(path);
        var target=new Vector3(2,3,4);r.Workspace.SceneViewport!.Camera=r.Workspace.SceneViewport.Camera with {Target=target};
        r.OpenPicker();r.Click("item.bin",UiRole.ListItem);r.Click("Add selected object",UiRole.Button);
        Assert.True(r.Window.Editor.Session.Current.Objects.Length == 1, r.Workspace.Said);
        var item=Assert.Single(r.Window.Editor.Session.Current.Objects);
        Assert.Equal(Rig.Id,item.Asset);Assert.Equal(target,item.Position);Assert.Null(item.Parent);
        Assert.Equal(item.Id,r.Window.Editor.Primary);Assert.Equal(saved,File.ReadAllText(path));
        Assert.True(r.Window.HandleKey(Chord.Parse("Ctrl+Z")));
        Assert.Empty(r.Window.Editor.Session.Current.Objects);Assert.False(r.Window.Editor.Session.History.CanUndo);
    }

    [Fact]
    public void Bound_picker_drag_reaches_the_workspace_viewport_callback_and_one_undo()
    {
        using var r=new Rig();
        var saved=File.ReadAllText(Path.Combine(r.Root,"data","room.json"));
        r.Workspace.SceneViewport!.Camera=r.Workspace.SceneViewport.Camera with {Target=new Vector3(2,3,4)};
        r.OpenPicker();
        Assert.NotNull(r.Workspace.SceneViewport.DropTarget);
        var picker=Assert.Single(r.Window.Editor.Panels,p=>p.Id=="scene.assets");
        var viewport=Assert.Single(r.Window.Editor.Panels,p=>p.Id=="editor.viewport");
        // Exercise the registered production draws in adjacent windows, independent of the user's dock layout.
        // Callback attachment above was performed by the real workspace Draw, not by this fixture.
        void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0,0),ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(500,650),ImGuiCond.Always);
            Ui.Begin("Bound assets"u8);picker.Draw();Ui.End();
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(510,0),ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(700,650),ImGuiCond.Always);
            Ui.Begin("Bound scene"u8);viewport.Draw();Ui.End();
        }
        r.Host.Frames(Draw,3);
        var source=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="item.bin").Bounds;
        r.Host.Drag(Draw,new Brutal.Numerics.float2(source.X+source.Width/2,source.Y+source.Height/2),
            new Brutal.Numerics.float2(850,300),steps:12);
        var item=Assert.Single(r.Window.Editor.Session.Current.Objects);
        Assert.Equal(Rig.Id,item.Asset);Assert.Equal(new Vector3(2,3,4),item.Position);Assert.Null(item.Parent);
        Assert.Equal(saved,File.ReadAllText(Path.Combine(r.Root,"data","room.json")));
        Assert.True(r.Window.Editor.Session.Undo());Assert.Empty(r.Window.Editor.Session.Current.Objects);
        Assert.False(r.Window.Editor.Session.History.CanUndo);
    }

    [Fact]
    public void Listed_browser_asset_without_catalog_admission_cannot_be_placed()
    {
        using var r=new Rig(admit:false);r.OpenPicker();r.Click("item.bin",UiRole.ListItem);r.Click("Add selected object",UiRole.Button);
        Assert.Contains("isn't in this project's assets",r.Workspace.Said);
        Assert.Empty(r.Window.Editor.Session.Current.Objects);Assert.False(r.Window.Editor.Session.History.CanUndo);
    }

    [Fact]
    public void Scene_menu_opens_the_same_picker_without_editing()
    {
        using var r=new Rig();
        r.Click("Scene",UiRole.Menu);
        r.Click("Add object.",UiRole.MenuItem);
        Assert.True(r.Window.Editor.IsOpen("scene.assets"));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="item.bin");
        Assert.Empty(r.Window.Editor.Session.Current.Objects);Assert.False(r.Window.Editor.Session.History.CanUndo);
    }

    [Fact]
    public void Catalog_refresh_after_selection_refuses_instead_of_reacquiring_admission()
    {
        using var r=new Rig();r.OpenPicker();r.Click("item.bin",UiRole.ListItem);
        r.Catalog.BeginRefresh();
        r.Click("Add selected object",UiRole.Button);
        Assert.Empty(r.Window.Editor.Session.Current.Objects);
        Assert.False(r.Window.Editor.Session.History.CanUndo);
        Assert.Contains("refresh",r.Workspace.Said,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Picker_captures_parent_and_places_at_local_zero_with_one_undo()
    {
        using var r=new Rig();
        Assert.True(r.Window.Editor.Session.Execute(new AddObject(SceneObject.At("parent",null,new Vector3(10,20,30)))));
        Assert.True(r.Window.Editor.Session.Execute(new AddObject(SceneObject.At("other",null,Vector3.One))));
        r.Window.Editor.Select("parent");r.OpenPicker();
        r.Window.Editor.Select("other"); // Selection changes are not document edits; the captured parent remains authoritative.
        r.Click("item.bin",UiRole.ListItem);r.Click("Add selected object",UiRole.Button);
        var child=Assert.Single(r.Window.Editor.Session.Current.Objects,o=>o.Asset==Rig.Id);
        Assert.Equal("parent",child.Parent);Assert.Equal(Vector3.Zero,child.Position);
        Assert.True(r.Window.HandleKey(Chord.Parse("Ctrl+Z")));
        Assert.Equal(new[]{"parent","other"},r.Window.Editor.Session.Current.Objects.Select(o=>o.Id));
        Assert.True(r.Window.HandleKey(Chord.Parse("Ctrl+Y")));
        Assert.Equal(child,r.Window.Editor.Session.Current.Find(child.Id));
    }

    [Theory]
    [InlineData("missing")][InlineData("cycle")][InlineData("revision")][InlineData("focus")]
    public void Captured_parent_refusals_do_not_add_history(string mode)
    {
        using var r=new Rig();var editor=r.Window.Editor;
        Assert.True(editor.Session.Execute(new AddObject(SceneObject.At("parent",null,Vector3.Zero))));
        Assert.True(editor.Session.Execute(new AddObject(SceneObject.At("other",null,Vector3.One))));
        var before=editor.Session.Current;var revision=editor.Session.Revision;
        if(mode=="missing")
            Assert.False(r.Workspace.CaptureSceneAsset(r.Catalog.Select(Rig.Id)!,out _,"absent"));
        else
        {
            Assert.True(r.Workspace.CaptureSceneAsset(r.Catalog.Select(Rig.Id)!,out var pending,"parent"));
            if(mode=="revision")
            {
                Assert.True(editor.Session.Execute(new RemoveObject("parent")));
                before=editor.Session.Current;revision=editor.Session.Revision;
            }
            if(mode=="focus") Assert.True(editor.FocusOn("other"));
            Assert.False(r.Workspace.DropSceneAsset(pending!,mode=="cycle"?"parent":"child",Vector3.One));
        }
        Assert.Equal(before,editor.Session.Current);Assert.Equal(revision,editor.Session.Revision);
        Assert.NotEmpty(r.Workspace.Said);
    }

    [Fact]
    public void Scene_change_while_picker_open_refuses_commit()
    {
        using var r=new Rig();r.OpenPicker();r.Click("item.bin",UiRole.ListItem);
        Assert.True(r.Window.Editor.Session.Execute(new AddObject(SceneObject.At("other",null,Vector3.Zero))));
        r.Host.Frames(r.Workspace.Draw,2);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="The scene changed; start Add object again");
        r.Click("Add selected object",UiRole.Button);
        Assert.Equal("other",Assert.Single(r.Window.Editor.Session.Current.Objects).Id);
        Assert.True(r.Window.Editor.Session.Undo());Assert.False(r.Window.Editor.Session.History.CanUndo);
    }
}
