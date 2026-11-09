using Brutal.Authoring.Editor.App;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class AccessKitRequestA457Tests
{
    // Synthetic, well-formed instances of the pinned header layout; never call or load the native DLL.
    private static nint Allocate(byte action, ulong target = 123)
    {
        var p = Marshal.AllocHGlobal(80); Marshal.Copy(new byte[80], 0, p, 80);
        Marshal.WriteByte(p, action); Marshal.WriteInt64(p, 24, unchecked((long)target)); return p;
    }
    [Theory]
    [InlineData((byte)0, AccessKitNativeActionKind.Click)]
    [InlineData((byte)1, AccessKitNativeActionKind.Focus)]
    public void Root_tree_requests_preserve_full_64_bit_identity(byte code, AccessKitNativeActionKind kind)
    {
        var p = Allocate(code, ulong.MaxValue);
        try { var request = AccessKitNativeRequestDecoder.Decode(p); Assert.NotNull(request); Assert.Equal(ulong.MaxValue, request.Target); Assert.Equal(kind, request.Action); Assert.Null(request.Value); }
        finally { Marshal.FreeHGlobal(p); }
    }
    [Fact]
    public void SetValue_copies_utf8_before_the_native_allocation_is_freed()
    {
        var p = Allocate(20); var value = Marshal.StringToCoTaskMemUTF8("naïve 表");
        AccessKitNativeActionRequest? request;
        try { Marshal.WriteByte(p, 32, 1); Marshal.WriteInt32(p, 40, 1); Marshal.WriteIntPtr(p, 48, value); request = AccessKitNativeRequestDecoder.Decode(p); }
        finally { Marshal.FreeCoTaskMem(value); Marshal.FreeHGlobal(p); }
        Assert.Equal("naïve 表", request!.Value);
    }
    [Fact]
    public void Cross_tree_unsupported_and_missing_value_data_are_not_dispatched()
    {
        var p = Allocate(20);
        try
        {
            Assert.Null(AccessKitNativeRequestDecoder.Decode(p));
            Marshal.WriteByte(p, 0, 2); Assert.Null(AccessKitNativeRequestDecoder.Decode(p));
            Marshal.WriteByte(p, 0, 0); Marshal.WriteByte(p, 1, 1); Assert.Null(AccessKitNativeRequestDecoder.Decode(p));
        }
        finally { Marshal.FreeHGlobal(p); }
        Assert.Null(AccessKitNativeRequestDecoder.Decode(0));
    }
    [Fact]
    public void Numeric_data_is_culture_invariant_and_nonfinite_values_are_refused()
    {
        var p = Allocate(20);
        try
        {
            Marshal.WriteByte(p, 32, 1); Marshal.WriteInt32(p, 40, 2);
            Marshal.WriteInt64(p, 48, BitConverter.DoubleToInt64Bits(3.5));
            Assert.Equal("3.5", AccessKitNativeRequestDecoder.Decode(p)!.Value);
            Marshal.WriteInt64(p, 48, BitConverter.DoubleToInt64Bits(double.NaN)); Assert.Null(AccessKitNativeRequestDecoder.Decode(p));
        }
        finally { Marshal.FreeHGlobal(p); }
    }
    [Fact]
    public void Oversized_or_invalid_utf8_values_are_refused_without_unbounded_scanning()
    {
        var p = Allocate(20); var bytes = Marshal.AllocHGlobal(65538);
        try
        {
            Marshal.Copy(Enumerable.Repeat((byte)'x', 65538).ToArray(), 0, bytes, 65538);
            Marshal.WriteByte(p, 32, 1); Marshal.WriteInt32(p, 40, 1); Marshal.WriteIntPtr(p, 48, bytes);
            Assert.Null(AccessKitNativeRequestDecoder.Decode(p));
            Marshal.WriteByte(bytes, 0, 0xff); Marshal.WriteByte(bytes, 1, 0); Assert.Null(AccessKitNativeRequestDecoder.Decode(p));
        }
        finally { Marshal.FreeHGlobal(bytes); Marshal.FreeHGlobal(p); }
    }
}
