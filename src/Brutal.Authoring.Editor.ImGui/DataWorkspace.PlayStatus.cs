using Brutal.ImGuiApi;
namespace Brutal.Authoring.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private bool projectPlayWasBuilding,projectPlayStopRequested;
    private void DrawProjectPlayStatus(IProjectPlaySession session)
    {
        var status=session.Status;
        if(status.StartsWith("Building",StringComparison.Ordinal))projectPlayWasBuilding=true;
        if(status.StartsWith("Running",StringComparison.Ordinal))projectPlayWasBuilding=false;
        if(session.HasCompleted)
        {
            if(projectPlayStopRequested)status="Stopped. "+status;
            else if(session.ExitCode is {} code)
                status=(projectPlayWasBuilding&&code!=0?$"Build failed (exit code {code}). ":$"Exited with code {code}. ")+status;
        }
        // One semantic identity survives text and layout changes; the native projection sends only changed values.
        ImGui.PushTextWrapPos(0);
        Ui.Status(status,"project.play.status");
        ImGui.PopTextWrapPos();
    }
}
