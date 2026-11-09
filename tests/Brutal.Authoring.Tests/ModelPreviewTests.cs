using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class ModelPreviewTests
{
    private const string Id="11111111111111111111111111111111";
    private static AssetCatalog.Input Input(byte b=1,AiUse ai=AiUse.Forbidden,string name="mesh.bin")
    {
        byte[] bytes=[b];var r=new AssetRecord(Id,name,Convert.ToHexStringLower(SHA256.HashData(bytes)),"synthetic",1,
            JsonDocument.Parse("{}").RootElement,new("Fixture","self-authored","test"),ai);
        return new(r,bytes,bytes,[],true,"test","");
    }
    private static void Publish(AssetCatalog catalog,params AssetCatalog.Input[] values)=>Assert.True(catalog.Publish(catalog.BeginRefresh(),values,out _));
    [Fact]
    public void Pending_and_missing_keep_pinned_last_good_but_never_return_it_as_current()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var preview=new ModelPreview(catalog);preview.Select(catalog.Select(Id)!);
        Assert.True(preview.TryCurrent(out var good,out _));var refresh=catalog.BeginRefresh();
        Assert.False(preview.TryCurrent(out var current,out var why));Assert.Null(current);Assert.NotEmpty(why);Assert.Same(good,preview.LastGood);
        Assert.True(catalog.Publish(refresh,[],out _));Assert.False(preview.TryCurrent(out current,out why));Assert.Null(current);Assert.NotEmpty(why);
        byte[] bytes=new byte[1];preview.LastGood!.CopyTo(bytes);Assert.Equal(1,bytes[0]);
    }
    [Fact]
    public void New_content_or_policy_requires_explicit_reselection()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var preview=new ModelPreview(catalog);var selected=catalog.Select(Id)!;preview.Select(selected);
        Assert.True(preview.TryCurrent(out var good,out _));Publish(catalog,Input(2));Assert.False(preview.TryCurrent(out _,out _));Assert.Equal(selected,preview.Selection);Assert.Same(good,preview.LastGood);
        preview.Select(catalog.Select(Id)!);Assert.Null(preview.LastGood);Assert.True(preview.TryCurrent(out _,out _));
        Publish(catalog,Input(2,AiUse.Allowed));Assert.False(preview.TryCurrent(out _,out _));
    }
    [Fact]
    public void Unrelated_generation_and_path_move_preserve_the_selected_revision()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var preview=new ModelPreview(catalog);preview.Select(catalog.Select(Id)!);
        Publish(catalog,Input(name:"moved.bin"));Assert.True(preview.TryCurrent(out var lease,out _));Assert.NotNull(lease);
        preview.Clear();Assert.Null(preview.LastGood);Assert.Null(preview.Selection);Assert.False(preview.TryCurrent(out _,out var why));Assert.Equal("Choose an asset to preview.",why);
        Assert.NotNull(catalog.Select(Id));
    }
    [Fact]
    public void Foreign_or_closed_admission_cannot_be_relabelled_as_a_current_preview()
    {
        using var first=new AssetCatalog();using var second=new AssetCatalog();Publish(first,Input());Publish(second,Input());
        var preview=new ModelPreview(second);preview.Select(first.Select(Id)!);Assert.False(preview.TryCurrent(out _,out _));Assert.Null(preview.LastGood);
        preview.Select(second.Select(Id)!);Assert.True(preview.TryCurrent(out var good,out _));second.Dispose();Assert.False(preview.TryCurrent(out _,out _));Assert.Same(good,preview.LastGood);
    }
}
