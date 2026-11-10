namespace Brutal.Authoring.Editor.ImGui;

/// <summary>App factory for the single saved-files Play route. Construction starts nothing.</summary>
public interface IProjectPlayHost
{
    /// <summary>Creates an owner for an already resolved command; execution requires the session Start call.</summary>
    IProjectPlaySession Start(PlayLaunch launch);
}
/// <summary>Plain bounded stdout/stderr text, never a game protocol.</summary>
public sealed record ProjectPlayOutput(bool Error,string Text);
/// <summary>Workspace-owned process session. Update and disposal belong to the UI owner thread.</summary>
public interface IProjectPlaySession : IPlaySession
{
    /// <summary>Starts only after saved-file and command-approval decisions.</summary>
    bool Start(bool hasUnsavedChanges,bool buildAndPlay=false);
    /// <summary>Whether a missing or stale executable has a configured build.</summary>
    bool CanBuildAndPlay {get;}
    /// <summary>Actual owned-process completion, not game readiness.</summary>
    bool HasCompleted {get;}
    /// <summary>Actual owned-process exit code, absent until exit is observed.</summary>
    int? ExitCode {get;}
    /// <summary>Requests owned-process stop; no other process is affected.</summary>
    void RequestStop();
    /// <summary>Observes completion and stop deadlines on the owner thread.</summary>
    void Update();
    /// <summary>Drains bounded pending output.</summary>
    IReadOnlyList<ProjectPlayOutput> DrainLogs();
}
