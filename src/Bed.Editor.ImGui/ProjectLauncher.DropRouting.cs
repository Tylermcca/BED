using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class ProjectLauncher
{
    private long dropDrawnFrame = -1;
    private DataWorkspace? dropDrawnWorkspace;

    /// <summary>Routes a host-resolved target from the last completed frame to an existing drop receiver.
    /// Exact native window identities are used, never a visible-label fallback. A stale frame, workspace change,
    /// modal/menu or unknown target refuses. Editor folder replacement requires two explicit decisions when drafts are dirty.</summary>
    public bool DispatchDrop(IReadOnlyList<string> paths, UiSnapshot frame, UiNode? target)
    {
        bool Refuse(string why)
        {
            Workspace?.Note(why);
            return Say(false, why);
        }
        if (ImGui.GetCurrentContext().IsNull() || frame.Frame != dropDrawnFrame || frame.Frame != Ui.Snapshot().Frame
            || !ReferenceEquals(dropDrawnWorkspace, Workspace))
            return Refuse("The drop target changed. Drop it again on the current window.");
        if (target is not { } node || node.Role != UiRole.Window || !frame.Nodes.Contains(node))
            return Refuse("Drop a project folder on the start window, or a JSON file on Files.");
        if (ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
            return Refuse("Close the open menu or dialog before dropping.");
        bool Window(ImString name)
        {
            var native = ImGui.Internal.FindWindowByName(name);
            if(native.IsNull())return false;
            var expected=(((ulong)UiRole.Window+1)<<32)|native.ID.Value;
            var current=node;for(var depth=0;depth<64;depth++)
            {if(current.Id==expected)return true;if(current.Parent<0||current.Parent>=frame.Nodes.Count)break;current=frame.Nodes[current.Parent];}
            return false;
        }
        if (Workspace is null && Window("BED###launcher"u8)) return DropOnStart(paths);
        if (Workspace is { } workspace && Window("Files###panel.project.files"u8))
        {
            var accepted = workspace.DropOnFiles(paths);
            return Say(accepted, workspace.Said);
        }
        if (Workspace is not null && (Window("###editor"u8) || Window("Outliner###outliner"u8) || Window("Inspector###inspector"u8)))
            return DropOnEditor(paths);
        return Refuse("Drop a project folder on the start window, or a JSON file on Files.");
    }
}
