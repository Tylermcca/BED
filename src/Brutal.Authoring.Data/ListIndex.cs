using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Brutal.Authoring.Data;

/// <summary>
/// Reaches an item of a JSON list in constant time. <see cref="JsonElement"/>'s indexer walks a list of objects from its
/// start, so reaching every row of a 10,000-row list one by one cost O(n²): building the table's rows took a second
/// (measured). The last few long lists read on each thread keep their items in an array, recognised by where
/// the list sits in its document's memory. A disposed document is never matched, since reading it throws. INVENTED.
/// </summary>
internal static class ListIndex
{
    [ThreadStatic] private static (JsonElement List, JsonElement[]? Items)[]? recent;
    private const int Long = 64;   // shorter lists are cheap to walk

    /// <summary>Item <paramref name="i"/> of <paramref name="list"/>, which must be in range.</summary>
    public static JsonElement Item(JsonElement list, int i)
    {
        var n = list.GetArrayLength();
        if (n < Long) return list[i];
        recent ??= new (JsonElement, JsonElement[]?)[4];
        var raw = JsonMarshal.GetRawUtf8Value(list);
        foreach (var (cached, items) in recent)
            if (items is not null && items.Length == n && Same(cached, raw)) return items[i];
        var all = new JsonElement[n];
        var j = 0;
        foreach (var e in list.EnumerateArray()) all[j++] = e;
        System.Array.Copy(recent, 0, recent, 1, recent.Length - 1);
        recent[0] = (list, all);
        return all[i];
    }

    /// <summary>Whether two elements are the same place in the same live document (never a disposed one).</summary>
    public static bool SameElement(JsonElement a, JsonElement b)
    {
        try { return Same(a, JsonMarshal.GetRawUtf8Value(b)); }
        catch (ObjectDisposedException) { return false; }
    }

    private static bool Same(JsonElement cached, ReadOnlySpan<byte> raw)
    {
        try
        {
            var c = JsonMarshal.GetRawUtf8Value(cached);
            return c.Length == raw.Length && Unsafe.AreSame(ref MemoryMarshal.GetReference(c), ref MemoryMarshal.GetReference(raw));
        }
        catch (ObjectDisposedException) { return false; }
    }
}
