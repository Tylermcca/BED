using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.Editor.Text;

namespace Brutal.Authoring.Launcher;

/// <summary>
/// BED: opens the start screen, or a project straight away.
/// <code>
/// BED                                    the start screen: recent projects, the sample, Open
/// BED &lt;folder&gt;                           a folder holding data/ and schemas/ (and reference_map.json)
/// BED --data &lt;d&gt; --schemas &lt;s&gt; [--map &lt;m&gt;]
/// ... --shot &lt;file.png&gt;                               draw it headless into a PNG, with no window
/// </code>
/// </summary>
public static class Program
{
    private const string Usage = """
        --version                               print the BED build version without opening a project or window
        usage: BED [<project folder> | --data <folder> --schemas <folder> [--map <file>]] [--adapter <dll>] [--settings <folder>] [--shot <file.png>]
          with nothing, it opens the start screen: recent projects, the sample project, and Open.
          a project folder holds data/ and schemas/ (and reference_map.json, if it has links).
          --adapter loads a game's adapter (its rules and panels); adapters in the adapters folder beside the program
          load anyway. Both are optional: plain data with schemas opens without one.
          --settings keeps the recent-projects list in that folder instead of your own settings (for tests and trials).
          --shot draws the screen into a PNG instead of opening a window.

        BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited. See NOTICE.txt.
        """;

    /// <summary>What the app adds to every project it opens: the Text panel (a data file's JSON as text) on each file.</summary>
    public static ProjectLauncher SetUp(ProjectLauncher launcher)
    {
        launcher.LinkedPlayHost ??= new LinkedPlayHost();
        launcher.SetUpWorkspace = workspace =>
        {
            workspace.AddTextPanel();
            workspace.FilePanels.Add((file, editor) => new CompareFilePanel(file, editor, workspace.Project.Schemas,
                pointer => workspace.GoTo(new ReferenceTarget(file.Name, pointer))).Panel());
            workspace.BackgroundSaves = true;   // a game's slow check before saving runs off the frame (A184, rank 1)
        };
        return launcher;
    }

    /// <summary>The sample a newcomer is offered: samples/recipes beside the program, in a release and a build alike.</summary>
    public static string SampleFolder() => Path.Combine(AppContext.BaseDirectory, "samples", "recipes");

    /// <summary>The entry point. Exit codes: 0 done; 1 the project couldn't open (with why); 2 bad arguments; 3 a required VC++ runtime library could not load.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        ParentConsole.Connect();
        return Run(args, Console.Out, Console.Error, RecentProjects.DefaultFolder);
    }

    // --settings wins over the folder the caller passes: Windows' own settings folder ignores APPDATA, so a test that set
    // it still read and changed the person's recent list (found by A065).

    /// <summary>Runs the app with the recent list kept in <paramref name="settings"/>.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors, string settings)
    {
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            if (args.Length != 1) { errors.WriteLine("--version must be used alone."); return 2; }
            output.WriteLine("BED " + BuildVersion.Current);
            return 0;
        }
        string? folder = null, data = null, schemas = null, map = null, shot = null;
        var adapterDlls = new List<string>();
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--data" when i + 1 < args.Length: data = args[++i]; break;
                case "--schemas" when i + 1 < args.Length: schemas = args[++i]; break;
                case "--map" when i + 1 < args.Length: map = args[++i]; break;
                case "--shot" when i + 1 < args.Length: shot = args[++i]; break;
                case "--adapter" when i + 1 < args.Length: adapterDlls.Add(args[++i]); break;
                case "--settings" when i + 1 < args.Length: settings = args[++i]; break;
                case "-h" or "--help" or "/?": output.Write(Usage); return 0;
                case var a when !a.StartsWith('-') && folder is null: folder = a; break;
                default: errors.Write(Usage); return 2;
            }
        if ((data is null) != (schemas is null) || folder is not null && data is not null) { errors.Write(Usage); return 2; }

        var runtime = RuntimeCheckWindows.Check(errors);
        if (runtime != 0) return runtime;
        return Open(folder, data, schemas, map, shot, adapterDlls, output, errors, settings);
    }

    // Keep native-bearing locals and adapter calls beyond the prerequisite decision's JIT boundary.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Open(string? folder, string? data, string? schemas, string? map, string? shot,
        List<string> adapterDlls, TextWriter output, TextWriter errors, string settings)
    {
        var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(settings, "recent.json")),
            ProjectPaths.In(SampleFolder()));
        var (loaded, problems) = Adapters.Load(Adapters.In(Path.Combine(AppContext.BaseDirectory, "adapters")).Concat(adapterDlls));
        SetUp(launcher);
        launcher.Loaded = loaded;
        launcher.AdapterProblems = problems;
        foreach (var problem in problems) errors.WriteLine(problem);
        // An explicit map must also win for the positional-folder route.
        var folderOpened = folder is null || launcher.OpenFolder(folder, map);
        if (!folderOpened || data is not null && !launcher.Open(new(data, schemas!, map)))
        {
            errors.WriteLine(launcher.Said);
            if (shot is not null) return 1;   // headless: say why and stop; in a window, the start screen shows it
        }
        else if (launcher.Said.Length > 0) output.WriteLine(launcher.Said);

        if (shot is not null)
        {
            using var host = new HeadlessHost(1600, 900);
            EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            host.Frames(launcher.Draw, 4);
            host.Save(shot);
            output.WriteLine($"Drew the screen into {Path.GetFullPath(shot)}.");
            return 0;
        }
        launcher.Splash = new BrutalSplash();   // the BRUTAL logo before the start screen (BRUTAL License v1.0, 2.2)
        EditorApp.Run(new("BED"), io => EditorWindow<DataDocument>.Configure(io), launcher.Draw, dialogs => launcher.FileDialogs = dialogs,
            close: launcher.RequestWindowClose, title: () => WindowTitle.For(launcher), drop: (drop, frame) => launcher.DispatchDrop(drop.Paths, frame, drop.TargetWindow(frame)));
        launcher.Dispose();   // a game still playing when the window closes is stopped, never left to the garbage collector
        return 0;
    }
}
