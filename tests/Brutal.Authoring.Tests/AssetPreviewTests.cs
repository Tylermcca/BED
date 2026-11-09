using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;
public sealed class AssetPreviewTests
{
    [Theory]
    [InlineData(0, 1, 0, 4, 4, 64)]
    [InlineData(-1, 1, 0, 4, 4, 64)]
    [InlineData(5, 1, 0, 4, 4, 64)]
    [InlineData(1, 5, 0, 4, 4, 64)]
    [InlineData(1, 1, 5, 4, 4, 64)]
    [InlineData(1, 1, -1, 4, 4, 64)]
    [InlineData(1, 1, 0, 0, 4, 64)]
    [InlineData(2, 2, 0, 4, 4, 15)]
    public void Invalid_size_or_budget_refuses_without_result(int w,int h,long encoded,long maxEncoded,int dimension,long decoded)
    {
        Assert.False(AssetPreview.TryByteCount(w,h,encoded,maxEncoded,dimension,decoded,out var count,out var why));
        Assert.Equal(0,count); Assert.NotEmpty(why);
    }
    [Fact]
    public void Extreme_dimensions_refuse_without_multiplication_overflow()
    {
        Assert.False(AssetPreview.TryByteCount(int.MaxValue,int.MaxValue,0,long.MaxValue,int.MaxValue,long.MaxValue,out _,out _));
    }
    [Fact]
    public void Exact_boundary_and_owned_copy_do_not_expose_caller_mutation()
    {
        var bytes = new byte[16]; bytes[0]=7;
        Assert.True(AssetPreview.TryCreate(2,2,bytes,8,8,2,16,out var preview,out _));
        bytes[0]=9; var copy=new byte[16]; preview!.CopyTo(copy);
        Assert.Equal(7,copy[0]); Assert.Equal(16,preview.ByteCount);
        copy[0]=3; preview.CopyTo(copy); Assert.Equal(7,copy[0]);
        Assert.False(AssetPreview.TryCreate(2,2,new byte[15],8,8,2,16,out var invalid,out _)); Assert.Null(invalid);
    }
    [Fact]
    public void Existing_generation_tracker_refuses_late_preview_and_keeps_last_good_identity()
    {
        Assert.True(AssetPreview.TryCreate(1,1,new byte[4],1,1,1,4,out var preview,out _));
        var queue=new AssetImportQueue<AssetPreview>();
        var first=queue.Request("r1"); Assert.True(queue.TryStart(out _)); Assert.True(queue.Complete(first,preview!,out _));
        var late=queue.Request("r2"); Assert.True(queue.TryStart(out _)); queue.Request("r3");
        Assert.False(queue.Complete(late,preview!,out _)); Assert.Equal("r1",queue.LastGood!.Revision);
        Assert.False(queue.LastGoodIsCurrent); Assert.True(queue.TryStart(out var current));
        queue.Close(); Assert.False(queue.Complete(current!,preview!,out _));
    }
}
