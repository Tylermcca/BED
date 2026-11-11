using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private bool projectPlayWasBuilding,projectPlayStopRequested;
    private void DrawProjectPlayStatus(IProjectPlaySession session)
    {
        var status=session.Status;
        string? detail=null;
        if(status.StartsWith("Building",StringComparison.Ordinal))projectPlayWasBuilding=true;
        if(status.StartsWith("Running",StringComparison.Ordinal))projectPlayWasBuilding=false;
        if(session.HasCompleted)
        {
            if(projectPlayStopRequested){detail=status;status="Stopped"+(session.ExitCode is {} stoppedCode&&stoppedCode!=0?$" (code {stoppedCode})":"")+".";}
            else if(session.ExitCode is {} code)
            {detail=status;status=projectPlayWasBuilding&&code!=0?$"Build failed (error code {code}).":code==0?"Finished normally.":$"Stopped with error code {code}.";}
        }
        else if(!projectPlayStopRequested && !projectPlayWasBuilding && session is IProjectFrameControlSession control)
        {
            var frame=control.FrameControl;
            status=!frame.Available
                ?frame.Status.Contains("unknown",StringComparison.OrdinalIgnoreCase)?"Frame control unknown.":"Running. Frame control unavailable."
                :frame.Pending?"Waiting for simulation acknowledgement."
                :frame.Paused?$"Paused after {frame.CompletedFrames} ticks.":"Running.";
            if(frame.Status!=status)detail=frame.Status;
        }
        // One semantic identity survives text and layout changes; the native projection sends only changed values.
        Ui.Status(status,"project.play.status");
        // Full refusal/error detail stays readable without becoming a second live announcement.
        if(detail is not null && detail!=status)Ui.TextWrapped(detail,"project.play.status.detail");
    }
}
