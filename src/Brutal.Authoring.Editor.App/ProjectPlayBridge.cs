using Brutal.Authoring.Editor.ImGui;
namespace Brutal.Authoring.Editor.App;

/// <summary>App facade for the common saved-files process owner. No process starts at construction.</summary>
public sealed class ProjectPlayBridge : IProjectPlayHost
{
    /// <inheritdoc />
    public IProjectPlaySession Start(PlayLaunch launch)=>new Session(launch);
    private sealed class Session(PlayLaunch launch) : IProjectPlaySession
    {
        private readonly ProjectPlayHost host=new(launch,TimeProvider.System,TimeSpan.FromMinutes(5));
        public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false)=>host.Start(hasUnsavedChanges,buildAndPlay);
        public bool CanBuildAndPlay=>host.CanBuildAndPlay;
        public bool HasCompleted=>host.HasCompleted;
        public int? ExitCode=>host.ExitCode;
        public string Status=>host.Status;
        public void RequestStop()=>host.RequestStop();
        public void Update()=>host.Update();
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>host.DrainLogs().Select(l=>new ProjectPlayOutput(l.Error,l.Text)).ToArray();
        public void Draw() { } // The workspace draws the console; no game UI/protocol is hosted here.
        public void Dispose()=>host.Dispose();
    }
}
