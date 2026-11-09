using System.Reflection;
using Brutal.Authoring.Editor.App;
using Xunit;

namespace Brutal.Authoring.Tests;

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
        var begin = Assert.Single(calls, c => c.Method.DeclaringType?.Name == "ImGuiFrame" && c.Method.Name == "Begin");
        var render = Assert.Single(calls, c => c.Method.Name == "RenderSecondaryWindows");
        var detach = Assert.Single(calls, c => c.Method.DeclaringType?.Name == "AccessKitPlatformWindows" && c.Method.Name == "Dispose");
        var destroy = Assert.Single(calls, c => c.Method.Name == "DestroyContext");
        Assert.True(drain.Offset < begin.Offset && begin.Offset < render.Offset);
        Assert.True(render.Offset < detach.Offset && detach.Offset < destroy.Offset);
    }

    [Fact]
    public void Secondary_rendering_restores_GL_context_in_finally_and_main_publication_is_partitioned()
    {
        var render = typeof(EditorApp).GetMethod("RenderSecondaryWindows", BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = SceneFrameLifecycleTests.Calls(render);
        var update = Assert.Single(calls, c => c.Method.Name == "UpdatePlatformWindows");
        var publish = Assert.Single(calls, c => c.Method.Name == "Publish");
        var draw = Assert.Single(calls, c => c.Method.Name == "RenderPlatformWindowsDefault");
        var restore = Assert.Single(calls, c => c.Method.Name == "MakeContextCurrent");
        Assert.True(update.Offset < publish.Offset && publish.Offset < draw.Offset && draw.Offset < restore.Offset);
        Assert.Contains(render.GetMethodBody()!.ExceptionHandlingClauses, c => c.Flags == ExceptionHandlingClauseOptions.Finally
            && c.HandlerOffset <= restore.Offset && restore.Offset < c.HandlerOffset + c.HandlerLength);
        var main = typeof(EditorApp).GetMethod("PublishAccessibility", BindingFlags.NonPublic | BindingFlags.Static)!;
        var snapshot = Assert.Single(SceneFrameLifecycleTests.Calls(main), c => c.Method.Name == "Snapshot");
        Assert.Equal(typeof(uint), Assert.Single(snapshot.Method.GetParameters()).ParameterType);
    }
}
