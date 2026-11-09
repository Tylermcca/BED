using System.Reflection;
using System.Reflection.Emit;
using Brutal.Authoring.Editor.App;
using Xunit;

namespace Brutal.Authoring.Tests;

// Inspect the compiled host without opening GLFW, loading AccessKit or creating a GPU device.
public sealed class SceneFrameLifecycleTests
{
    private static MethodInfo Loop => typeof(EditorApp).GetMethod("Loop", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void Safe_scene_slot_is_prepared_before_native_UI_collection_and_draw_submission()
    {
        var calls = Calls(Loop);
        var prepare = Assert.Single(calls, c => c.Method.Name == "PrepareSceneFrame");
        var begin = Assert.Single(calls, c => c.Method.Name == "Begin" && c.Method.DeclaringType?.Name == "ImGuiFrame");
        var draw = Assert.Single(calls, c => c.Method.Name == "Invoke" && c.Method.DeclaringType == typeof(Action));
        var submit = Assert.Single(calls, c => c.Method.Name == "Draw" && c.Method.DeclaringType?.Name == "WindowRenderer");
        Assert.True(prepare.Offset < begin.Offset);
        Assert.True(begin.Offset < draw.Offset);
        Assert.True(draw.Offset < submit.Offset);
    }

    [Fact]
    public void Scene_shutdown_is_exception_safe_and_precedes_backend_and_context_disposal()
    {
        var body = Loop.GetMethodBody()!;
        var calls = Calls(Loop);
        var shutdown = Assert.Single(calls, c => c.Method.Name == "ShutdownScenes");
        var cleanup = Assert.Single(body.ExceptionHandlingClauses, c => c.Flags == ExceptionHandlingClauseOptions.Finally
            && c.HandlerOffset <= shutdown.Offset && shutdown.Offset < c.HandlerOffset + c.HandlerLength
            && calls.Any(call => call.Offset >= c.TryOffset && call.Offset < c.TryOffset + c.TryLength
                && call.Method.Name == "Dispose" && call.Method.DeclaringType?.Name == "WindowsFileDialogs"));
        Assert.Contains(calls, c => c.Offset >= cleanup.TryOffset && c.Offset < cleanup.TryOffset + cleanup.TryLength
            && c.Method.Name == "Dispose" && c.Method.DeclaringType?.Name == "WindowsFileDialogs");
        Assert.Contains(calls, c => c.Offset > shutdown.Offset && c.Method.Name == "Dispose"
            && c.Method.DeclaringType == typeof(IDisposable));
        var destroy = Assert.Single(calls, c => c.Method.Name == "DestroyContext");
        Assert.True(shutdown.Offset < destroy.Offset);
    }

    internal static List<(int Offset, MethodBase Method)> Calls(MethodInfo method)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => unchecked((ushort)o.Value));
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var result = new List<(int, MethodBase)>();
        for (var offset = 0; offset < bytes.Length;)
        {
            var start = offset;
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            var op = opcodes[value];
            if (op.OperandType == OperandType.InlineMethod)
                result.Add((start, method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))!));
            offset += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
        return result;
    }
}
