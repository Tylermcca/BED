using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

public sealed class SceneAssetPlacementTests : IDisposable
{
    private const string Id="11111111111111111111111111111111";
    private readonly string root=Directory.CreateTempSubdirectory("scene-placement-").FullName;
    private readonly AssetCatalog catalog=new();
    private readonly DataWorkspace workspace;
    private readonly SceneDocumentEntry entry;
    private string ScenePath=>Path.Combine(root,"data","room.json");
    public SceneAssetPlacementTests()
    {
        Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"schemas"));
        File.WriteAllText(ScenePath,new SceneFormat().Write(Scene.Empty));
        var project=DataProject.Open(Path.Combine(root,"data"),Path.Combine(root,"schemas"));
        workspace=new(project);
        Assert.True(workspace.Documents.BindScene("room.json","scene",out _));
        Assert.True(workspace.Documents.BindAssetCatalog(catalog,out _));
        Assert.True(workspace.Show("room.json"));
        Assert.True(workspace.Documents.TryGetOpenScene("room.json",out var opened));entry=opened!;
        Publish();
    }
    public void Dispose(){workspace.Dispose();catalog.Dispose();Directory.Delete(root,true);}
    private void Publish(byte source=1,string policy="initial")
    {
        byte[] bytes=[source];
        var record=new AssetRecord(Id,"synthetic.bin",Convert.ToHexStringLower(SHA256.HashData(bytes)),"synthetic",1,
            JsonDocument.Parse("{}").RootElement,new("Fixture","self-authored","test"),AiUse.Forbidden);
        Assert.True(catalog.Publish(catalog.BeginRefresh(),[new AssetCatalog.Input(record,bytes,bytes,[],true,"test",policy)],out _));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Drop_and_keyboard_receivers_share_one_add_and_one_undo(bool keyboard)
    {
        var original=File.ReadAllText(ScenePath);var selected=catalog.Select(Id)!;
        if(keyboard)Assert.True(workspace.InsertSceneAsset(selected,"item",Vector3.One),workspace.Said);
        else
        {
            Assert.True(workspace.CaptureSceneAsset(selected,out var pending));
            Assert.Empty(entry.Session.Current.Objects);Assert.False(entry.Session.History.CanUndo);
            Assert.True(workspace.DropSceneAsset(pending!,"item",Vector3.One),workspace.Said);
        }
        var item=Assert.Single(entry.Session.Current.Objects);Assert.Equal(Id,item.Asset);Assert.Equal(Vector3.One,item.Position);
        Assert.Equal("item",entry.Editor.Primary);Assert.Equal(original,File.ReadAllText(ScenePath));
        Assert.True(entry.Session.Undo());Assert.Empty(entry.Session.Current.Objects);Assert.False(entry.Session.History.CanUndo);
        Assert.True(entry.Session.Redo());Assert.Single(entry.Session.Current.Objects);
    }

    [Theory]
    [InlineData("pending")][InlineData("content")][InlineData("policy")][InlineData("missing")][InlineData("closed")]
    public void Stale_asset_refuses_without_edit_or_history(string change)
    {
        Assert.True(workspace.CaptureSceneAsset(catalog.Select(Id)!,out var pending));
        switch(change)
        {
            case "pending":catalog.BeginRefresh();break;
            case "content":Publish(2);break;
            case "policy":Publish(policy:"changed");break;
            case "missing":Assert.True(catalog.Publish(catalog.BeginRefresh(),[],out _));break;
            case "closed":catalog.Dispose();break;
        }
        Assert.False(workspace.DropSceneAsset(pending!,"item",Vector3.Zero));Assert.NotEmpty(workspace.Said);
        Assert.Empty(entry.Session.Current.Objects);Assert.False(entry.Session.History.CanUndo);
    }

    [Fact]
    public void Foreign_admission_and_rebinding_are_refused()
    {
        using var other=new AssetCatalog();
        Assert.False(workspace.Documents.BindAssetCatalog(other,out _));
        var forged=catalog.Select(Id)! with { Admission=other.Admission };
        Assert.False(workspace.CaptureSceneAsset(forged,out _));Assert.Empty(entry.Session.Current.Objects);
    }

    [Fact]
    public void Changed_scene_and_reopened_session_refuse_captured_drop()
    {
        Assert.True(workspace.CaptureSceneAsset(catalog.Select(Id)!,out var pending));
        Assert.True(entry.Session.Execute(new AddObject(SceneObject.At("other",null,Vector3.Zero))));
        Assert.False(workspace.DropSceneAsset(pending!,"item",Vector3.Zero));
        Assert.Single(entry.Session.Current.Objects);
        Assert.True(entry.Session.Undo());Assert.False(workspace.DropSceneAsset(pending!,"item",Vector3.Zero));
        Assert.True(workspace.Documents.CloseScene("room.json",SceneCloseDecision.Discard,out _));
        Assert.True(workspace.Show("room.json"));Assert.False(workspace.DropSceneAsset(pending!,"item",Vector3.Zero));
        Assert.Empty(workspace.SceneWindow!.Editor.Session.Current.Objects);
    }

    [Fact]
    public void Invalid_position_or_duplicate_id_never_adds_a_history_step()
    {
        var selected=catalog.Select(Id)!;
        Assert.False(workspace.InsertSceneAsset(selected,"item",new(float.NaN,0,0)));
        Assert.Empty(entry.Session.Current.Objects);Assert.False(entry.Session.History.CanUndo);
        Assert.True(workspace.InsertSceneAsset(selected,"item",Vector3.Zero));
        Assert.False(workspace.InsertSceneAsset(selected,"item",Vector3.One));
        Assert.True(entry.Session.Undo());Assert.Empty(entry.Session.Current.Objects);Assert.False(entry.Session.History.CanUndo);
    }

    [Fact]
    public void Missing_injection_and_read_only_project_refuse()
    {
        var p=DataProject.Open(Path.Combine(root,"data"),Path.Combine(root,"schemas"),readOnly:true);
        using var w=new DataWorkspace(p);
        Assert.True(w.Documents.BindScene("room.json","scene",out _));Assert.True(w.Show("room.json"));
        Assert.False(w.InsertSceneAsset(catalog.Select(Id)!,"item",Vector3.Zero));
        Assert.True(w.Documents.BindAssetCatalog(catalog,out _));
        Assert.False(w.InsertSceneAsset(catalog.Select(Id)!,"item",Vector3.Zero));Assert.Contains("read-only",w.Said);
        Assert.Empty(w.SceneWindow!.Editor.Session.Current.Objects);
    }
}
