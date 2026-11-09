using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Brutal.Authoring.Editor.App;

/// <summary>Supported pinned AccessKit C action ordinals.</summary>
public enum AccessKitNativeActionKind : byte
{
    /// <summary>Default click, mapped to the wrapper's invoke/select/toggle path.</summary>
    Click = 0,
    /// <summary>Native keyboard focus.</summary>
    Focus = 1,
    /// <summary>Replace a field value through its wrapper.</summary>
    SetValue = 20,
}
/// <summary>An owned managed copy of a root-tree request. It contains no native pointers.</summary>
/// <param name="Target">Projected node identity within this HWND.</param>
/// <param name="Action">One supported native action.</param>
/// <param name="Value">Bounded replacement value, never included in diagnostics.</param>
public sealed record AccessKitNativeActionRequest(ulong Target, AccessKitNativeActionKind Action, string? Value = null);

/// <summary>Decodes a borrowed, valid AccessKit C 0.23.2 x64 request before the callback frees it. No native API is called.</summary>
public static class AccessKitNativeRequestDecoder
{
    // Header: uint8 action; byte tree[16]; pad7; uint64 node; bool data; pad7;
    // data tag (C int), pad4; union (32 bytes, alignment8). Total80, offsets24/32/40/48.
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    private struct Request
    {
        [FieldOffset(0)] public byte Action;
        [FieldOffset(1)] public Guid Tree;
        [FieldOffset(24)] public ulong Node;
        [FieldOffset(32)] public byte HasData;
        [FieldOffset(40)] public int Tag;
        [FieldOffset(48)] public nint Value;
        [FieldOffset(48)] public double Number;
    }
    /// <summary>Copies only Focus, Click and bounded SetValue requests for TreeId::ROOT (nil UUID). Unsupported data returns null; caller still owns/frees the request.</summary>
    public static AccessKitNativeActionRequest? Decode(nint borrowed)
    {
        if (borrowed == 0) return null;
        var request = Marshal.PtrToStructure<Request>(borrowed);
        if (request.Tree != Guid.Empty || request.Action is not (0 or 1 or 20)) return null;
        if (request.Action != 20) return new(request.Node, (AccessKitNativeActionKind)request.Action);
        if (request.HasData != 1) return null;
        if (request.Tag == 2)
            return double.IsFinite(request.Number) ? new(request.Node, AccessKitNativeActionKind.SetValue, request.Number.ToString("R", CultureInfo.InvariantCulture)) : null;
        if (request.Tag != 1 || request.Value == 0) return null;
        var length = 0;
        while (length <= 65536 && Marshal.ReadByte(request.Value, length) != 0) length++;
        if (length > 65536) return null;
        var bytes = new byte[length]; Marshal.Copy(request.Value, bytes, 0, length);
        try { return new(request.Node, AccessKitNativeActionKind.SetValue, new UTF8Encoding(false, true).GetString(bytes)); }
        catch (DecoderFallbackException) { return null; }
    }
}
