namespace Bed.Editor.ImGui;
/// <summary>Optional simulation control commands, applied only at the game's cooperative frame boundary.</summary>
public enum ProjectFrameCommand
{
    /// <summary>Pause at the next simulation boundary.</summary>
    Pause,
    /// <summary>Advance one simulation tick while paused.</summary>
    Step,
    /// <summary>Resume simulation without paused-time catch-up.</summary>
    Resume
}
/// <summary>Last acknowledged state; pending/unknown states never assert an effective pause.</summary>
/// <param name="Available">Authenticated helper is connected.</param>
/// <param name="Paused">Last acknowledged paused state; false while pending or unavailable.</param>
/// <param name="Pending">One command awaits acknowledgement.</param>
/// <param name="CompletedFrames">Helper's completed simulation ticks.</param>
/// <param name="Status">Plain control state/refusal.</param>
public sealed record ProjectFrameState(bool Available,bool Paused,bool Pending,long CompletedFrames,string Status);
/// <summary>Optional capability on a saved-files Play session; legacy games need not implement it.</summary>
public interface IProjectFrameControlSession
{
    /// <summary>Current acknowledged control state.</summary>
    ProjectFrameState FrameControl {get;}
    /// <summary>Requests one command; false when unavailable, busy or invalid in the acknowledged state.</summary>
    bool RequestFrameControl(ProjectFrameCommand command);
}

internal static class ProjectFrameActions
{
    internal static string? Refusal(ProjectFrameState? state,ProjectFrameCommand command)
        =>state is null||!state.Available?state?.Status??"This game hasn't enabled frame control yet."
        :state.Pending?"Wait for the simulation boundary acknowledgement."
        :command==ProjectFrameCommand.Pause&&state.Paused?"Simulation is already paused."
        :command is ProjectFrameCommand.Step or ProjectFrameCommand.Resume&&!state.Paused?"Pause the simulation first.":null;
}
