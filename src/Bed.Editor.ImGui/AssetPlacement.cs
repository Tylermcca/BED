using System.Numerics;
using Bed.Editor;
using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>One workspace-owned placement gesture shared by keyboard confirmation and native ImGui drag delivery.
/// The payload carries only a token; admitted content and captured scene authority stay in managed state.</summary>
public sealed class AssetPlacement(DataWorkspace workspace, AssetCatalog catalog)
{
    private static long nextToken;
    private long token;
    private SceneAssetPlacement? pending;
    private Func<Vector3,bool>? scene2Pending;
    private Vector3 position;
    private bool dragging;

    /// <summary>Whether a captured placement still awaits confirmation or delivery.</summary>
    public bool Pending => pending is not null||scene2Pending is not null;

    /// <summary>Captures the existing scene and catalog authority. A new gesture cancels the old one.</summary>
    public bool Begin(AssetCatalog.Selection selection, Vector3 at, string? parent = null)
    {
        Cancel();
        if(workspace.Scene2Window is not null)
        {if(!workspace.CaptureScene2AssetDrop(selection,at,out scene2Pending,parent))return false;}
        else if (!workspace.CaptureSceneAsset(selection, out pending, parent)) return false;
        position = at;
        token = Interlocked.Increment(ref nextToken);
        return true;
    }

    /// <summary>Commits once through the captured scene's checked command path; every attempted delivery consumes the token.</summary>
    public bool Commit()
    {
        var captured = pending;var capturedScene2=scene2Pending;
        Cancel();
        if(capturedScene2 is not null)return capturedScene2(position);
        if (captured is null || workspace.SceneWindow is not { } window) return false;
        var number = 1;
        while (window.Editor.Session.Current.Objects.Any(o => o.Id == $"object_{number}")) number++;
        return workspace.DropSceneAsset(captured, $"object_{number}", position);
    }

    /// <summary>Discards captured state without editing, saving, or adding an undo step.</summary>
    public void Cancel() { pending = null;scene2Pending=null; token = 0; }

    /// <summary>Call immediately after the browser's real selectable item.</summary>
    public unsafe void DrawSource(AssetEntry entry)
    {
        if (!ImGui.BeginDragDropSource()) return;
        try
        {
            if (!dragging)
            {
                var selection = catalog.Select(entry.Guid);
                var anchor=workspace.Scene2AssetAnchor();
                var parent = workspace.SceneWindow?.Editor.Primary??anchor.Parent;
                var at = parent is null ? workspace.SceneViewport?.Camera.Target??anchor.Position : Vector3.Zero;
                if (selection is null) Cancel();
                else Begin(selection, at, parent);
                dragging = true;
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false)) Cancel();
            var value = token;
            ImGui.SetDragDropPayload("BED_ASSET"u8, &value, (nuint)sizeof(long));
            Ui.TextWrapped(Pending ? $"Place {entry.RelativePath}" : "Asset placement unavailable");
        }
        finally { ImGui.EndDragDropSource(); }
    }

    /// <summary>Call immediately after the real viewport canvas item; only native delivery consumes a matching token.</summary>
    public void DrawTarget()=>DrawTargetAt(position);
    internal unsafe void DrawTargetAt(Vector3 at)
    {
        if (!ImGui.BeginDragDropTarget()) return;
        try
        {
            var payload = ImGui.AcceptDragDropPayload("BED_ASSET"u8);
            if (!payload.IsNull() && payload.Delivery && payload.DataSize == sizeof(long)
                && payload.Data != 0 && token != 0 && *(long*)payload.Data == token){position=at;Commit();}
        }
        finally { ImGui.EndDragDropTarget(); }
    }

    /// <summary>Call after all source/target windows. Escape or release outside a target cancels without a command.</summary>
    public void FinishFrame()
    {
        if (!dragging) return;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false)) Cancel();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) { Cancel(); dragging = false; }
    }
}
