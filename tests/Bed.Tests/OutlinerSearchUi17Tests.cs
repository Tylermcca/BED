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
public sealed class OutlinerSearchUi17Tests
{
    internal sealed class Rig:IDisposable
    {
        public const string AssetId="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public readonly string Root=Directory.CreateTempSubdirectory("outliner-search-ui17-").FullName;
        public readonly HeadlessHost Host=new(1400,1000){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly AssetCatalog Catalog=new();public readonly string Original;
        public EditorWindow<Scene> Window=>Workspace.SceneWindow!;
        public string Disk=>Path.Combine(Root,"data","level.json");
        public Rig(bool properties=false)
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;var assets=Directory.CreateDirectory(Path.Combine(Root,"assets")).FullName;
            var scene=new Scene([SceneObject.At("group_root",null,Vector3.Zero),SceneObject.At("ship_alpha",AssetId,Vector3.UnitX) with{Parent="group_root"},SceneObject.At("engine_child","models/engine.obj",Vector3.UnitY) with{Parent="ship_alpha"},SceneObject.At("crate_beta","models/Crate.obj",Vector3.UnitZ),SceneObject.At("unrelated",null,new(4,0,0))]);
            if(properties)
            {
                var child=scene.Find("engine_child")!;
                scene=scene.Replace(child with{Properties=child.Properties.Add("purpose",JsonSerializer.SerializeToElement("Rescue café")).Add("capacity",JsonSerializer.SerializeToElement(42.5)).Add("powered",JsonSerializer.SerializeToElement(true)).Add("sealed",JsonSerializer.SerializeToElement(false))});
                var other=scene.Find("unrelated")!;scene=scene.Replace(other with{Properties=other.Properties.Add("name",JsonSerializer.SerializeToElement("Landing pad"))});
            }
            Original=new SceneFormat().Write(scene);File.WriteAllText(Disk,Original);Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));
            Publish("models/Blue cruiser.glb");Assert.True(Workspace.BindSceneAssets(new AssetBrowser(assets),Catalog));Assert.True(Workspace.Show("level.json"));
            EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Workspace.Draw,4);
        }
        public void Publish(string name)
        {
            byte[] source=[1,2,3];var mesh=DerivedMeshCodec.Encode(new MeshData([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]));
            var record=new AssetRecord(AssetId,name,Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","script",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new(record,source,mesh,[],true,"fixture","")],out var why),why);
        }
        public static bool Inside(UiSnapshot tree,UiNode node,string window)
        {for(var parent=node.Parent;parent>=0;parent=tree.Nodes[parent].Parent)if(tree.Nodes[parent].Role==UiRole.Window&&tree.Nodes[parent].Label==window)return true;return false;}
        public string[] Rows()
        {var tree=Ui.Snapshot();return tree.Nodes.Where(n=>n.Role==UiRole.ListItem&&Inside(tree,n,"Outliner")).Select(n=>n.Label.StartsWith("> ",StringComparison.Ordinal)?n.Label[2..]:n.Label).ToArray();}
        public UiNode Search()=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Search objects");
        public void Key(ImGuiKey key,bool ctrl=false){Host.Press(Workspace.Draw,key,ctrl:ctrl);Host.Frames(Workspace.Draw,2);}
        public void FocusOutliner()
        {
            var tree=Ui.Snapshot();var row=tree.Nodes.First(n=>n.Role==UiRole.ListItem&&Inside(tree,n,"Outliner"));Host.Click(Workspace.Draw,row.Bounds.X+row.Bounds.Width/2,row.Bounds.Y+row.Bounds.Height/2);
        }
        public void Query(string text)
        {Key(ImGuiKey.F,ctrl:true);Assert.True(Search().Focused);Key(ImGuiKey.A,ctrl:true);Host.Type(Workspace.Draw,text);Host.Frames(Workspace.Draw,3);Assert.Equal(text,Search().Value);}
        public void Unchanged(){Assert.Equal(Original,File.ReadAllText(Disk));Assert.False(Window.Editor.Session.Dirty);Assert.Empty(Window.Editor.Session.Journal);Assert.False(Window.Editor.Session.History.CanUndo);}
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData("SHIP_ALPHA","group_root,ship_alpha")][InlineData("ENGINE.OBJ","group_root,ship_alpha,engine_child")][InlineData("crate.obj","crate_beta")][InlineData("blue cruiser","group_root,ship_alpha")]
    public void Native_CtrlF_filters_ID_asset_path_and_imported_source_name_with_parent_context(string query,string expected)
    {
        using var r=new Rig();r.FocusOutliner();var selection=r.Window.Editor.Selection.ToArray();r.Query(query);
        Assert.Equal(expected.Split(','),r.Rows());Assert.Equal(selection,r.Window.Editor.Selection);r.Unchanged();
        r.Key(ImGuiKey.Escape);Assert.Equal("",r.Search().Value);Assert.Equal(5,r.Rows().Length);Assert.Equal(selection,r.Window.Editor.Selection);r.Unchanged();
    }
    [Fact] public void No_match_has_plain_tree_text_keeps_selection_and_Escape_does_not_step_out_of_focus()
    {
        using var r=new Rig();Assert.True(r.Window.Editor.FocusOn("group_root"));r.Host.Frames(r.Workspace.Draw,3);r.FocusOutliner();var focus=r.Window.Editor.Focus.ToArray();r.Query("nothing_matches");
        Assert.Empty(r.Rows());Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="No objects match this search.");r.Key(ImGuiKey.Escape);
        Assert.Equal("",r.Search().Value);Assert.Equal(focus,r.Window.Editor.Focus);Assert.Equal(new[]{"group_root","ship_alpha","engine_child"},r.Rows());r.Unchanged();
    }
    [Fact] public void Catalog_source_name_refresh_invalidates_search_without_editing_the_scene()
    {
        using var r=new Rig();r.FocusOutliner();r.Query("renamed");Assert.Empty(r.Rows());r.Publish("models/Renamed cruiser.glb");r.Host.Frames(r.Workspace.Draw,3);
        Assert.Equal(new[]{"group_root","ship_alpha"},r.Rows());r.Unchanged();
    }
    [Fact] public void Escape_from_a_filtered_object_row_clears_without_changing_selection()
    {
        using var r=new Rig();r.FocusOutliner();r.Query("engine");var selected=r.Window.Editor.Selection.ToArray();r.Key(ImGuiKey.Tab);
        var tree=Ui.Snapshot();Assert.Contains(tree.Nodes,n=>n.Role==UiRole.ListItem&&n.Focused&&Rig.Inside(tree,n,"Outliner"));
        r.Key(ImGuiKey.Escape);Assert.Equal("",r.Search().Value);Assert.Equal(5,r.Rows().Length);Assert.Equal(selected,r.Window.Editor.Selection);r.Unchanged();
    }
    [Fact] public void Fixed_Outliner_CtrlF_takes_precedence_over_a_global_rebinding_without_changing_model_focus()
    {
        using var r=new Rig();Assert.True(r.Window.Editor.FocusOn("group_root"));r.Host.Frames(r.Workspace.Draw,3);r.FocusOutliner();
        r.Window.Shortcuts.Set("editor.focus_out","Ctrl+F");var focus=r.Window.Editor.Focus.ToArray();r.Query("ship_alpha");
        Assert.Equal(focus,r.Window.Editor.Focus);Assert.Equal(new[]{"group_root","ship_alpha"},r.Rows());r.Unchanged();
    }
    [Fact] public void CtrlF_in_Inspector_does_not_take_focus_into_the_Outliner()
    {
        using var r=new Rig();r.Window.Editor.Select("crate_beta");r.Host.Frames(r.Workspace.Draw,3);var field=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Position X");
        r.Host.Click(r.Workspace.Draw,field.Bounds.X+field.Bounds.Width/2,field.Bounds.Y+field.Bounds.Height/2);r.Key(ImGuiKey.F,ctrl:true);
        Assert.False(r.Search().Focused);Assert.Equal("",r.Search().Value);Assert.Equal(5,r.Rows().Length);r.Unchanged();
    }
}
