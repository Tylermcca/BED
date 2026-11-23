using System.Diagnostics;

namespace Bed.Editor.Gum;

public static partial class GumTool
{
    private static long nextWindowCheck;

    internal static void FitOpenedWindows()
    {
        var now = Environment.TickCount64;
        if (now < nextWindowCheck) return;
        nextWindowCheck = now + 250; // Gum normally creates its window a few frames after launch.
        ownStarts.FitOpenedWindows();
    }

    private static readonly OwnStarts ownStarts = new(info =>
    {
        var process = Process.Start(info);
        return process is null ? null : new StartedProcess(() => !process.HasExited, process.Dispose,
            project => FitStartedWindow(process, project));
    });

    // A process lease only observes/disposes the handle; it never closes or kills the person's Gum.
    internal sealed class StartedProcess(Func<bool> running, Action release, Func<string, bool>? fitWindow = null) : IDisposable
    {
        internal bool Running
        {
            get
            {
                try { return running(); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
            }
        }
        internal bool FitWindow(string project)
        {
            try { return fitWindow?.Invoke(project) ?? true; }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            { return false; }
        }

        public void Dispose() => release();
    }

    // Shared by all workspaces: closing/reopening a BED document must not forget a Gum still loading.
    // Once a matching window is found, the usual window lookup takes over. There is deliberately no timeout:
    // a slow startup must not be mistaken for an exit and start another Gum.
    internal sealed class OwnStarts(Func<ProcessStartInfo, StartedProcess?> start) : IDisposable
    {
        private readonly Dictionary<string, StartedProcess> pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new();
        private readonly HashSet<string> fitted = new(StringComparer.OrdinalIgnoreCase);

        internal void FitOpenedWindows()
        {
            lock (gate)
            {
                foreach (var (project, process) in pending)
                    if (!fitted.Contains(project) && process.FitWindow(project)) fitted.Add(project);
            }
        }
        internal (bool Started, string Message) Start(Launch launch, Func<string, bool> showOpen)
        {
            lock (gate)
            {
                foreach (var key in pending.Where(pair => !pair.Value.Running).Select(pair => pair.Key).ToArray()) Forget(key);
                var opening = false;
                var result = StartUntracked(launch, info =>
                {
                    var process = start(info);
                    if (process is null) return false;
                    if (launch.Project is { } project) pending[Path.GetFullPath(project)] = process;
                    else process.Dispose();
                    return true;
                }, project =>
                {
                    var key = Path.GetFullPath(project);
                    if (showOpen(project))
                    {
                        if (!fitted.Contains(key) && pending.TryGetValue(key, out var opened)) opened.FitWindow(project);
                        Forget(key);
                        return true;
                    }
                    if (!pending.ContainsKey(key)) return false;
                    opening = true;
                    return true;
                });
                return opening ? (true, "This project was already sent to Gum. Give its window time to appear.") : result;
            }
        }
        private void Forget(string key)
        {
            fitted.Remove(key);
            if (pending.Remove(key, out var process)) process.Dispose();
        }
        public void Dispose()
        {
            lock (gate) { foreach (var key in pending.Keys.ToArray()) Forget(key); }
        }
    }
}