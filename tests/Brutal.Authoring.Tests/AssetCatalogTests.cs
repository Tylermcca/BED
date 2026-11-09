using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Brutal.Authoring.Tests;
public sealed class AssetCatalogTests
{
    private const string A="11111111111111111111111111111111", B="22222222222222222222222222222222", C="33333333333333333333333333333333";
    private static AssetRecord Record(string id, byte[] source, string settings="{}", AiUse ai=AiUse.Forbidden, string name="item.bin") =>
        new(id,name,Convert.ToHexStringLower(SHA256.HashData(source)),"synthetic",1,JsonDocument.Parse(settings).RootElement,
            new("Fixture","self-authored","test"),ai);
    private static AssetCatalog.Input Input(string id=A, byte value=1, string[]? dependencies=null, string settings="{}", AiUse ai=AiUse.Forbidden, string name="item.bin")
    { var bytes=new[]{value};return new(Record(id,bytes,settings,ai,name),bytes,bytes,dependencies??[],true,"test","no extra approval"); }
    private static void Publish(AssetCatalog catalog, params AssetCatalog.Input[] input)
    { Assert.True(catalog.Publish(catalog.BeginRefresh(),input,out var why),why); }

    [Fact]
    public void Pinned_bytes_survive_caller_mutation_refresh_and_close()
    {
        var bytes=new byte[]{9}; var input=new AssetCatalog.Input(Record(A,bytes),bytes,bytes,[],true,"test",""); bytes[0]=4;
        using var catalog=new AssetCatalog(); Publish(catalog,input); var selected=catalog.Select(A)!;
        Assert.True(catalog.TryResolve(selected,out var lease,out _)); var copy=new byte[1];lease!.CopyTo(copy);Assert.Equal(9,copy[0]);
        var refresh=catalog.BeginRefresh();Assert.False(catalog.TryResolve(selected,out _,out _));Assert.Equal(AssetCatalog.State.Stale,catalog.Resolve(A).State);
        catalog.Dispose();Assert.False(catalog.Publish(refresh,[input],out _));lease.CopyTo(copy);Assert.Equal(9,copy[0]);
        using var reopened=new AssetCatalog();Publish(reopened,input);Assert.False(reopened.TryResolve(selected,out _,out _));
    }
    [Fact]
    public void Unrelated_generation_or_path_move_allows_selection_but_content_or_policy_change_refuses()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var selected=catalog.Select(A)!;
        Publish(catalog,Input(name:"moved.bin"),Input(B));Assert.True(catalog.TryResolve(selected,out _,out _));
        Publish(catalog,Input(ai:AiUse.Allowed));Assert.False(catalog.TryResolve(selected,out _,out _));
        var changed=catalog.Select(A)!;Assert.Equal(selected.ContentRevision,changed.ContentRevision);Assert.NotEqual(selected.PolicyRevision,changed.PolicyRevision);
        Publish(catalog,Input(value:2,ai:AiUse.Allowed));Assert.False(catalog.TryResolve(changed,out _,out _));
    }
    [Fact]
    public void Dependencies_invalidate_transitively_and_unready_or_cycles_never_resolve()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input(A,dependencies:[B]),Input(B,dependencies:[C]),Input(C));var selected=catalog.Select(A)!;
        Publish(catalog,Input(A,dependencies:[B]),Input(B,dependencies:[C]),Input(C,2));Assert.False(catalog.TryResolve(selected,out _,out _));
        Publish(catalog,Input(A,dependencies:[B]));Assert.Null(catalog.Select(A));Assert.Contains("not ready",catalog.Resolve(A).Reason);
        Publish(catalog,Input(A,dependencies:[B]),Input(B,dependencies:[A]));Assert.Null(catalog.Select(A));Assert.Null(catalog.Select(B));
    }
    [Fact]
    public void Duplicate_missing_and_failed_keep_diagnostics_and_last_good_distinct()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());Publish(catalog,Input(),Input());
        Assert.Equal(AssetCatalog.State.Ambiguous,catalog.Resolve(A).State);Assert.Null(catalog.Resolve(A).Current);Assert.NotNull(catalog.Resolve(A).LastGood);
        Publish(catalog);Assert.Equal(AssetCatalog.State.Missing,catalog.Resolve(A).State);Assert.NotNull(catalog.Resolve(A).LastGood);
        Publish(catalog,new AssetCatalog.Input(Record(A,[1]),[1],null,[],true,"test","",AssetCatalog.State.Failed,"Synthetic failure"));
        Assert.Equal("Synthetic failure",catalog.Resolve(A).Reason);Assert.Null(catalog.Select(A));Assert.NotNull(catalog.Resolve(A).LastGood);
    }
    [Fact]
    public void Latest_refresh_only_and_foreign_refresh_cannot_publish()
    {
        using var catalog=new AssetCatalog();using var other=new AssetCatalog();
        var old=catalog.BeginRefresh();var current=catalog.BeginRefresh();Assert.False(catalog.Publish(old,[Input()],out _));
        Assert.False(other.Publish(current,[Input()],out _));Assert.True(catalog.Publish(current,[Input()],out _));Assert.False(catalog.Publish(current,[Input()],out _));
    }
    [Fact]
    public void Settings_are_canonical_and_unknown_dependencies_or_changed_source_are_not_current()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input(settings:"{\"b\":2,\"a\":1}"));var selected=catalog.Select(A)!;
        Publish(catalog,Input(settings:"{\"a\":1,\"b\":2}"));Assert.True(catalog.TryResolve(selected,out _,out _));
        Publish(catalog,new AssetCatalog.Input(Record(A,[1]),[2],[2],[],true,"test",""));Assert.Equal(AssetCatalog.State.Stale,catalog.Resolve(A).State);
        Publish(catalog,new AssetCatalog.Input(Record(A,[1]),[1],[1],[],false,"test",""));Assert.Null(catalog.Select(A));
    }
    [Fact]
    public void Bad_candidate_keeps_old_snapshot_and_pending_state_without_partial_publication()
    {
        using var catalog=new AssetCatalog();Publish(catalog,Input());var old=catalog.Resolve(A).LastGood;
        var ticket=catalog.BeginRefresh();Assert.Throws<ArgumentException>(()=>catalog.Publish(ticket,[Input(settings:"{\"a\":1,\"a\":2}")],out _));
        Assert.Same(old,catalog.Resolve(A).LastGood);Assert.Null(catalog.Resolve(A).Current);
        Assert.True(catalog.Publish(ticket,[Input()],out _));
    }
    [Fact]
    public void Excessive_dependency_depth_refuses_without_unbounded_recursion()
    {
        var ids=Enumerable.Range(1,258).Select(i=>i.ToString("x32")).ToArray();
        var inputs=ids.Select((id,i)=>Input(id,dependencies:i+1<ids.Length?[ids[i+1]]:[])).ToArray();
        using var catalog=new AssetCatalog();Publish(catalog,inputs);Assert.Null(catalog.Select(ids[0]));
        Assert.Contains("depth",catalog.Resolve(ids[0]).Reason);
    }}

