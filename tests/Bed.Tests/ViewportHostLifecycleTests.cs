using System.Reflection;
using Bed.Editor.App;
using Xunit;

namespace Bed.Tests;

public sealed class ViewportHostLifecycleTests
{
    [Theory]
    [InlineData("Loop")]
    [InlineData("RunOpenGl")]
    public void Both_hosts_drain_before_collection_render_secondaries_and_detach_before_context_disposal(string name)
    {
        var method = typeof(EditorApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = SceneFrameLifecycleTests.Calls(method);
        var drain = Assert.Single(calls, c => c.Method.DeclaringType?.Name == "AccessKitPlatformWindows" && c.Method.Name == "DrainRequests");
        var run = Assert.Single(calls, c => c.Method.DeclaringType == SceneFrameLifecycleTests.FrameType && c.Method.Name == "Run");
        _ = SceneFrameLifecycleTests.FrameCallback(method, "RenderSecondaryWindows", "EditorApp");
        var begin = Assert.Single(SceneFrameLifecycleTests.Calls(SceneFrameLifecycleTests.FrameRun), c => c.Method.DeclaringType?.Name == "ImGuiFrame" && c.Method.Name == "Begin");
        Assert.True(begin.Offset < SceneFrameLifecycleTests.FrameAction("draw"));
        Assert.True(SceneFrameLifecycleTests.FrameAction("draw") < SceneFrameLifecycleTests.FrameAction("secondary"));
        var detach = Assert.Single(calls, c => c.Method.DeclaringType?.Name == "AccessKitPlatformWindows" && c.Method.Name == "Dispose");
        var destroy = Assert.Single(calls, c => c.Method.Name == "DestroyContext");
        Assert.True(drain.Offset < run.Offset);
        Assert.True(run.Offset < detach.Offset && detach.Offset < destroy.Offset);
    }

    [Fact]
    public void Secondary_rendering_restores_GL_context_in_finally_and_main_publication_is_partitioned()
    {
        var render = typeof(EditorApp).GetMethod("RenderSecondaryWindows", BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = SceneFrameLifecycleTests.Calls(render);
        var shared = Assert.Single(calls, c => c.Method.DeclaringType == SceneFrameLifecycleTests.FrameType && c.Method.Name == "RenderSecondary");
        var inner = SceneFrameLifecycleTests.Calls((MethodInfo)shared.Method);
        var update = Assert.Single(inner, c => c.Method.Name == "UpdatePlatformWindows");
        var publish = Assert.Single(inner, c => c.Method.Name == "Publish");
        var draw = Assert.Single(inner, c => c.Method.Name == "RenderPlatformWindowsDefault");
        var restore = Assert.Single(calls, c => c.Method.Name == "MakeContextCurrent");
        Assert.True(update.Offset < publish.Offset && publish.Offset < draw.Offset);
        Assert.True(shared.Offset < restore.Offset);
        Assert.Contains(render.GetMethodBody()!.ExceptionHandlingClauses, c => c.Flags == ExceptionHandlingClauseOptions.Finally
            && c.HandlerOffset <= restore.Offset && restore.Offset < c.HandlerOffset + c.HandlerLength);
        var main = typeof(EditorApp).GetMethod("PublishAccessibility", BindingFlags.NonPublic | BindingFlags.Static)!;
        var snapshot = Assert.Single(SceneFrameLifecycleTests.Calls(main), c => c.Method.Name == "Snapshot");
        Assert.Equal(typeof(uint), Assert.Single(snapshot.Method.GetParameters()).ParameterType);
    }
}
