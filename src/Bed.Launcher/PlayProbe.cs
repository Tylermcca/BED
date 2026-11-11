using System.Diagnostics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;

namespace Bed.Launcher;

/// <summary>Bounded release probe of the real startup admission and saved-files Play route, without a window.</summary>
internal static class PlayProbe
{
    /// <summary>Opens the supplied project, observes successful owned-process completion, and writes one JSON result.</summary>
    public static int Run(string folder, TimeSpan timeout, TextWriter output, TextWriter errors, IReadOnlyList<string>? probeArguments=null)
    {
        if(timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        { errors.WriteLine("Play probe timeout must be greater than zero and at most 300 seconds."); return 1; }
        try
        {
            using var host = new HeadlessHost(1024, 768);
            EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            // Probe settings are isolated; the user's recent-project list is never read or changed.
            var settings = Directory.CreateTempSubdirectory("bed-play-probe-").FullName;
            try
            {
                using var launcher = Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(settings,"recent.json")),
                    ProjectPaths.In(Program.SampleFolder())));
                // The workspace checks the configured command's approval first. Only the probe-owned
                // factory appends explicit CLI arguments, after that decision, without changing settings.
                if(probeArguments is {Count:>0})launcher.ProjectPlayHost=new ProbeHost(launcher.ProjectPlayHost!,probeArguments.ToArray());
                var (loaded, problems) = Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory,"adapters")));
                if(problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
                launcher.Loaded = loaded;
                if(!launcher.OpenFolder(folder)) throw new InvalidOperationException(launcher.Said);
                var workspace = launcher.Workspace ?? throw new InvalidOperationException("The project did not open.");
                var scenes = workspace.Project.Files.Where(f => workspace.Documents.IsScene(f.Name)).ToArray();
                string name;
                if(scenes.Length > 0)
                {
                    if(scenes.Length != 1) throw new InvalidOperationException("The Play probe requires exactly one admitted scene document.");
                    name = scenes[0].Name;
                    if(!workspace.Show(name)) throw new InvalidOperationException(workspace.Said);
                    if(workspace.SceneWindow is null && workspace.Scene2Window is null) throw new InvalidOperationException("The scene did not open.");
                }
                else
                {
                    // A data project uses the same saved-files Play route as a scene project.
                    // Open through normal admission; never repair or synthesize a document.
                    var opened = workspace.Project.Files.OrderBy(f => f.Name, StringComparer.Ordinal)
                        .FirstOrDefault(f => workspace.Show(f.Name));
                    if(opened is null) throw new InvalidOperationException("No admitted data document could be opened. " + workspace.Said);
                    name = opened.Name;
                }
                if(!workspace.RequestProjectPlay()) throw new InvalidOperationException(workspace.Said);
                var session = workspace.ProjectPlay ?? throw new InvalidOperationException("Saved-files Play did not start.");
                var lines = new List<ProjectPlayOutput>();
                var watch = Stopwatch.StartNew();
                do
                {
                    session.Update();
                    lines.AddRange(session.DrainLogs());
                    if(watch.Elapsed >= timeout) throw new TimeoutException("Play probe timed out: " + session.Status);
                    if(!session.HasCompleted) Thread.Sleep(5);
                } while(!session.HasCompleted);
                session.Update();
                lines.AddRange(session.DrainLogs());
                if(session.ExitCode != 0) throw new InvalidOperationException(session.Status);
                output.WriteLine(JsonSerializer.Serialize(new {status="passed", completed=true, exitCode=session.ExitCode, processStatus=session.Status,
                    document=name, output=lines.Select(line => new {error=line.Error, text=line.Text})}));
                return 0;
            }
            finally { Directory.Delete(settings, true); }
        }
        catch(Exception ex) { errors.WriteLine("Play probe failed: " + ex.Message); return 1; }
    }
    private sealed class ProbeHost(IProjectPlayHost inner,IReadOnlyList<string> arguments):IProjectPlayHost
    {
        public IProjectPlaySession Start(PlayLaunch launch)=>inner.Start(launch with
        {Run=new GameLaunchSpec(launch.Run.Executable,launch.Run.WorkingDirectory,launch.Run.Arguments.Concat(arguments))});
    }
}

