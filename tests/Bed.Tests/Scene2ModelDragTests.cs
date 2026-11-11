using System.Collections;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Scene2ModelDragTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private const string Id="55555555555555555555555555555555";
    private sealed class Rig:IDisposable
    {
        internal readonly Scene2HostCommandTests.Rig Files=new();internal readonly AssetCatalog Catalog=new();internal readonly DataWorkspace Workspace;internal readonly Scene2DocumentEntry Entry;internal readonly EditorWindow<Scene2> Window;internal readonly ViewportView<Scene2> View;internal readonly AssetPlacement Placement;internal readonly AssetsPanel Picker;internal readonly HeadlessHost Ui=new(1200,800){Drawing=false};internal readonly string Name;private readonly string assets;internal int Policies;
        internal Rig(string mode)
        {
            assets=Directory.CreateDirectory(Path.Combine(Files.Root,"assets")).FullName;Name=mode=="image"?"image.png":"model.obj";Publish(mode);
            Workspace=new(Files.Project);Assert.True(Workspace.BindSceneAssets(new AssetBrowser(assets,".obj",".png"),Catalog));Assert.True(Workspace.Show("level.json"));Entry=Files.Entry;
            Window=(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",Private)!.GetValue(Workspace)!;
            var views=(IDictionary)typeof(DataWorkspace).GetField("scene2Windows",Private)!.GetValue(Workspace)!;var native=views["level.json"]!;View=(ViewportView<Scene2>)native.GetType().GetField("viewport",Private)!.GetValue(native)!;
            Picker=(AssetsPanel)typeof(DataWorkspace).GetField("sceneAssetPicker",Private)!.GetValue(Workspace)!;Placement=(AssetPlacement)typeof(DataWorkspace).GetField("scenePlacement",Private)!.GetValue(Workspace)!;
            EditorWindow<Scene2>.Configure(ImGui.GetIO());Ui.Frames(Workspace.Draw,2);Assert.NotNull(View.DropTarget);View.Camera=View.Camera with{Target=new(2,3,4)};Window.Editor.Select(mode=="parent"?["owner"]:mode=="wrapper-parent"?["frame_a"]:mode=="inherited-parent"?["frame_a_root"]:[]);
            if(mode.StartsWith("edit-",StringComparison.Ordinal))Window.Editor.EditRefusal=(_,_,_)=>
            {
                Policies++;
                switch(mode)
                {
                    case "edit-deny":return "fixture edit refusal";case "edit-throw":throw new InvalidOperationException("fixture policy error");case "edit-asset":Catalog.BeginRefresh();break;
                    case "edit-scene":ChangeScene();break;case "edit-replace":Window.Editor.EditRefusal=null;break;case "edit-readonly":Files.Blocked=true;break;case "edit-play":Window.ProjectPlaying=()=>true;break;
                }
                return null;
            };
        }
        internal void Publish(string mode="deliver",byte value=1,string policy="initial")
        {
            byte[] source=[value,2,3];var record=new AssetRecord(Id,Name,Convert.ToHexStringLower(SHA256.HashData(source)),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("Fixture","self-authored","synthetic model generation",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllBytes(Path.Combine(assets,Name),source);File.WriteAllText(Path.Combine(assets,Name)+AssetRecord.Suffix,record.Write());var derived=mode=="image"?new byte[]{1,2,3,255}:mode=="opaque"?new byte[]{1,2,3}:DerivedMeshCodec.Encode(MeshData.Cube);
            Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[new AssetCatalog.Input(record,source,derived,[],true,"fixture",policy,texture:mode=="image"?new(1,1,AssetCatalog.TextureEncoding.Rgba8Srgb):null)],out var why),why);
        }
        internal void ChangeScene()=>Assert.True(Entry.Session.Execute(Scene2Kit.Command("set",JsonSerializer.SerializeToElement(new{id="owner",field="/position",value=new[]{9,0,0}}))));
        internal void Draw()
        {
            ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(500,650));Bed.Editor.ImGui.Ui.Begin("Model source"u8);Picker.Draw();Bed.Editor.ImGui.Ui.End();
            ImGui.SetNextWindowPos(new(510,0));ImGui.SetNextWindowSize(new(500,650));Bed.Editor.ImGui.Ui.Begin("Model target"u8);View.Draw();Bed.Editor.ImGui.Ui.End();Placement.FinishFrame();
        }
        public void Dispose(){Workspace.Dispose();Catalog.Dispose();Ui.Dispose();Files.Dispose();}
    }
    [Theory]
    [InlineData("deliver")][InlineData("camera")][InlineData("parent")][InlineData("wrapper-parent")][InlineData("inherited-parent")]
    [InlineData("outside")][InlineData("escape")][InlineData("asset")][InlineData("policy-stale")][InlineData("scene")][InlineData("read-only")][InlineData("disk")][InlineData("closed")][InlineData("focus")][InlineData("workspace")][InlineData("play")]
    [InlineData("edit-deny")][InlineData("edit-throw")][InlineData("edit-asset")][InlineData("edit-scene")][InlineData("edit-replace")][InlineData("edit-readonly")][InlineData("edit-play")]
    [InlineData("image")][InlineData("opaque")]
    public void Native_model_delivery_uses_one_captured_checked_plan_and_refuses_cancelled_stale_or_reentrant_changes(string mode)
    {
        using var r=new Rig(mode);var before=r.Entry.Session.Current;r.Ui.Frames(r.Draw,3);var item=Assert.Single(Bed.Editor.ImGui.Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label==r.Name).Bounds;var canvas=Assert.Single(Bed.Editor.ImGui.Ui.Snapshot().Nodes,n=>n.Role==UiRole.Canvas&&n.Label=="Viewport").Bounds;
        var target=mode=="outside"?new Brutal.Numerics.float2(1150,750):new Brutal.Numerics.float2(canvas.X+canvas.Width*.4f,canvas.Y+canvas.Height*.35f);var frame=0;var captured=false;
        void Drag()
        {
            if(++frame==6)
            {
                captured=r.Placement.Pending;
                switch(mode)
                {
                    case "camera":r.View.Camera=r.View.Camera with{Target=new(8,9,10)};break;case "escape":ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,true);break;case "asset":r.Catalog.BeginRefresh();break;case "policy-stale":r.Publish(policy:"changed");break;
                    case "scene":r.ChangeScene();break;case "read-only":r.Files.Blocked=true;break;case "disk":File.AppendAllText(r.Files.Disk," ");break;case "closed":Assert.True(r.Files.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));break;
                    case "focus":Assert.True(r.Window.Editor.FocusOn("frame_a"));break;case "workspace":r.Workspace.Dispose();break;case "play":r.Window.ProjectPlaying=()=>true;break;
                }
            }
            r.Draw();
        }
        r.Ui.Drag(Drag,new(item.X+item.Width/2,item.Y+item.Height/2),target,steps:12);Assert.Equal(mode is not("image" or "opaque" or "inherited-parent"),captured);Assert.False(r.Placement.Pending);Assert.False(r.Placement.Commit());Assert.Equal(r.Files.Original+(mode=="disk"?" ":""),File.ReadAllText(r.Files.Disk));
        var copies=r.Entry.Session.Current.Objects.Where(o=>o.Object.Asset==Id).ToArray();
        if(mode is "deliver" or "camera" or "parent" or "wrapper-parent")
        {
            var copy=Assert.Single(copies);Assert.Null(copy.Sprite);Assert.Equal(mode=="parent"?"owner":mode=="wrapper-parent"?"frame_a":null,copy.Object.Parent);Assert.Equal(copy.Object.Parent is null?new Vector3(2,3,4):Vector3.Zero,copy.Object.Position);Assert.Equal(copy.Object.Id,r.Window.Editor.Primary);Assert.Single(r.Entry.Session.Journal);Assert.Equal(1,r.Entry.Session.History.UndoCount);
            var restored=r.Entry.Session.Current with{Objects=r.Entry.Session.Current.Objects.Remove(copy),ObjectOrder=r.Entry.Session.Current.ObjectOrder?.Where(id=>id!=copy.Object.Id).ToImmutableArray()};Assert.Equal(new Scene2Format().Write(before),new Scene2Format().Write(restored));
            var replay=DocumentSession<Scene2>.ReplayFrom(r.Entry.Session.JournalBase,JournalFile.Read(JournalFile.Write(r.Entry.Session.Journal)),new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(r.Entry.Session.TextToSave,replay.TextToSave);Assert.True(r.Entry.Session.Undo());Assert.Same(before,r.Entry.Session.Current);Assert.True(r.Entry.Session.Redo());Assert.Single(r.Entry.Session.Current.Objects,o=>o.Object.Asset==Id);
        }
        else
        {
            Assert.Empty(copies);Assert.Equal(mode is "scene" or "edit-scene"?1:0,r.Entry.Session.Journal.Count);if(mode is not("scene" or "edit-scene"))Assert.Same(before,r.Entry.Session.Current);
            if(mode is not("outside" or "escape"))Assert.NotEmpty(r.Workspace.Said);
        }
        if(mode.StartsWith("edit-",StringComparison.Ordinal))Assert.Equal(1,r.Policies);
    }
}
