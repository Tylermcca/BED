using Bed.Editor.ImGui;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.App;

// The native backends own input/device preparation; this sequence is shared with finite virtual-platform runs.
internal sealed class EditorFrame(Action draw, Action complete, Action publish, Action saveLayout, Action render, Action secondary)
{
    internal void Run()
    {
        ImGuiFrame.Begin();
        draw();
        complete();
        Im.Render();
        UiTree.EndFrame();
        publish();
        saveLayout();
        render();
        secondary();
    }
    internal void RunFrames(int count)
    {
        if(count is <1 or >256)throw new ArgumentOutOfRangeException(nameof(count));
        for(var frame=0;frame<count;frame++)Run();
    }

    internal static void RenderSecondary(AccessKitPlatformWindows? accessibility,Action? afterUpdate=null)
    {
        if(!Im.GetIO().ConfigFlags.HasFlag(Brutal.ImGuiApi.ImGuiConfigFlags.ViewportsEnable))return;
        Im.UpdatePlatformWindows();
        afterUpdate?.Invoke(); // Virtual backends can expose first-frame metrics before publication.
        accessibility?.ThrowPendingFailure();
        accessibility?.Publish(Ui.Snapshot());
        Im.RenderPlatformWindowsDefault();
    }

}
