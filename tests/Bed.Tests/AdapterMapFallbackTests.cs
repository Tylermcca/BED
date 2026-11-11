using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

// Source-linked internal path selector: entirely self-authored temporary paths, not installed adapter files.
public sealed class AdapterMapFallbackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "adapter-map-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "project", "data");
    private string Local => Path.Combine(root, "project", "reference_map.json");
    private string Dll => Path.Combine(root, "adapter", "SyntheticAdapter.dll");
    private string Beside => Path.Combine(root, "adapter", "reference_map.json");
    public AdapterMapFallbackTests()
    {
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Path.GetDirectoryName(Dll)!);
    }
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public void Explicit_missing_map_is_not_hidden_by_either_fallback()
    {
        File.WriteAllText(Local, "{}"); File.WriteAllText(Beside, "{}");
        var missing = Path.Combine(root, "missing.json");
        Assert.Equal(missing, AdapterMap.Resolve(missing, Data, Dll));
    }
    [Fact] public void Root_map_wins_over_adapter_and_its_malformed_bytes_are_not_skipped()
    {
        File.WriteAllText(Local, "not JSON"); File.WriteAllText(Beside, "{}");
        Assert.Equal(Local, AdapterMap.Resolve(null, Data, Dll));
    }
    [Fact] public void Adapter_map_is_used_only_when_root_map_absent()
    {
        File.WriteAllText(Beside, "{}");
        Assert.Equal(Beside, AdapterMap.Resolve(null, Data, Dll));
        Assert.Null(AdapterMap.Resolve(null, Data, null));
    }
    [Fact] public void No_map_is_valid_and_another_adapter_does_not_contribute()
    {
        Assert.Null(AdapterMap.Resolve(null, Data, Dll));
        File.WriteAllText(Beside, "{}");
        var other = Path.Combine(root, "other", "OtherAdapter.dll");
        Assert.Null(AdapterMap.Resolve(null, Data, other));
    }
}
