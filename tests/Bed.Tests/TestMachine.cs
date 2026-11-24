using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Sdk;

namespace Bed.Tests;

/// <summary>What the machine running the tests has: the build configuration these tests came from, and whether it can draw with OpenGL.</summary>
internal static class TestMachine
{
    /// <summary>The configuration (Debug or Release) this test assembly was built in. Other projects in the solution were built the same way.</summary>
    internal static string Configuration { get; } =
        typeof(TestMachine).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration is { Length: > 0 } configuration
            ? configuration
            : throw new InvalidOperationException("The test assembly doesn't say its build configuration.");

    /// <summary>The output folder of another project in this repository, for the same configuration as these tests.</summary>
    internal static string Output(string repo, string project) => Path.Combine(repo, project, "bin", Configuration, "net10.0");

    /// <summary>Set BED_TEST_NO_OPENGL=1 to act as a machine without OpenGL, so the skip path can be checked on a PC that has one.</summary>
    internal const string NoOpenGlVariable = "BED_TEST_NO_OPENGL";

    /// <summary>Why OpenGL tests can't run here, or null when they can.</summary>
    internal static string? NoOpenGl => noOpenGl.Value;
    private static readonly Lazy<string?> noOpenGl = new(FindNoOpenGl);

    private static string? FindNoOpenGl()
    {
        if (Environment.GetEnvironmentVariable(NoOpenGlVariable) == "1") return NoOpenGlVariable + "=1 says to act as a machine without OpenGL.";
        if (!OperatingSystem.IsWindows()) return null;
        // The same check GLFW makes before it says "WGL: The driver does not appear to support OpenGL": a window pixel
        // format that supports OpenGL and isn't Windows' own unaccelerated software one. Machines with no graphics driver
        // (GitHub's runners, some virtual machines) only have the software one.
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return "No screen to draw on, so no OpenGL.";
        try
        {
            var descriptor = new byte[40];
            var count = DescribePixelFormat(screen, 1, (uint)descriptor.Length, descriptor);
            for (var format = 1; format <= count; format++)
            {
                if (DescribePixelFormat(screen, format, (uint)descriptor.Length, descriptor) == 0) continue;
                var flags = BitConverter.ToUInt32(descriptor, 4);
                if ((flags & DrawToWindow) == 0 || (flags & SupportOpenGl) == 0) continue;
                if ((flags & GenericFormat) != 0 && (flags & GenericAccelerated) == 0) continue;
                if (descriptor[8] != RgbaPixels) continue;
                return null;
            }
            return "This machine has no OpenGL graphics driver (only Windows' software drawing), so a real OpenGL window can't open.";
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }

    private const uint DrawToWindow = 0x4, SupportOpenGl = 0x20, GenericFormat = 0x40, GenericAccelerated = 0x1000;
    private const byte RgbaPixels = 0;
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int DescribePixelFormat(IntPtr dc, int format, uint size, [Out] byte[] descriptor);
}

/// <summary>A test that opens a real OpenGL window or context; skipped, with the reason, on a machine without OpenGL.</summary>
public sealed class OpenGlFactAttribute : FactAttribute
{
    public OpenGlFactAttribute() { if (TestMachine.NoOpenGl is { } why) Skip = why; }
}

/// <summary>A theory whose every case opens a real OpenGL window or context; skipped, with the reason, on a machine without OpenGL.</summary>
public sealed class OpenGlTheoryAttribute : TheoryAttribute
{
    public OpenGlTheoryAttribute() { if (TestMachine.NoOpenGl is { } why) Skip = why; }
}

/// <summary>One theory case that opens a real OpenGL window or context; only that case is skipped on a machine without OpenGL.</summary>
public sealed class OpenGlInlineDataAttribute : DataAttribute
{
    private readonly object?[] data;
    public OpenGlInlineDataAttribute(params object?[] data) { this.data = data; if (TestMachine.NoOpenGl is { } why) Skip = why; }
    public override IEnumerable<object?[]> GetData(MethodInfo testMethod) => [data];
}
