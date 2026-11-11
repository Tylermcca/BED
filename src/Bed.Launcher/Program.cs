using Bed.Data;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Editor.Text;

namespace Bed.Launcher;

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
          --probe-play <folder> [--probe-args "<args>"] [--timeout 30] checks saved-files Play headlessly through normal exit; JSON on success.
          --probe-export <folder> --destination <empty folder> [--timeout 60] builds, verifies and copies saved files headlessly; JSON on success.

        BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited. See NOTICE.txt.
        """;

    /// <summary>What the app adds to every project it opens: the Text panel (a data file's JSON as text) on each file.</summary>
    public static ProjectLauncher SetUp(ProjectLauncher launcher)
    {
        launcher.ProjectPlayHost ??= new ProjectPlayBridge();
        launcher.GameExportHost ??=new ExportBuildHost(8192,256,TimeSpan.FromMinutes(5));
        launcher.AssetCatalogProvisioner ??= new AssetCatalogProvisioner();
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
        return Run(args, Console.Out, Console.Error, InitialSettings(args,()=>RecentProjects.DefaultFolder));
    }

    private static string InitialSettings(string[] args,Func<string> defaultFolder)
    {
        for(var i=0;i<args.Length;i++)if(args[i]=="--settings")return i+1<args.Length?args[i+1]:"";
        if(args.Any(a=>a is "--version" or "--help" or "-h")||args.FirstOrDefault()=="--probe-play")return "";
        return defaultFolder();
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
        if (args.Length > 0 && args[0] == "--probe-play")
        {
            var seconds = 30;string[] probeArguments=[];
            try
            {
                if(args.Length<2||string.IsNullOrWhiteSpace(args[1]))throw new ArgumentException();
                var seen=new HashSet<string>(StringComparer.Ordinal);
                for(var i=2;i<args.Length;i+=2)
                {
                    if(i+1>=args.Length||!seen.Add(args[i]))throw new ArgumentException();
                    switch(args[i])
                    {
                        case "--timeout":
                            if(!int.TryParse(args[i+1],out seconds)||seconds is <=0 or >300)throw new ArgumentException();
                            break;
                        case "--probe-args":probeArguments=ProbeArguments.Parse(args[i+1]);break;
                        default:throw new ArgumentException();
                    }
                }
            }
            catch(ArgumentException)
            { errors.WriteLine("usage: BED --probe-play <project folder> [--probe-args \"<args>\"] [--timeout <1..300 seconds>]"); return 1; }
            if (RuntimeCheckWindows.Check(errors) != 0) return 1;
            return Probe(args[1], seconds, probeArguments, output, errors);
        }

        if(args.Length>0 && args[0]=="--probe-export")
        {
            var seconds=60;string? destination=null;
            try
            {
                if(args.Length<2||string.IsNullOrWhiteSpace(args[1]))throw new ArgumentException();
                var seen=new HashSet<string>(StringComparer.Ordinal);
                for(var i=2;i<args.Length;i+=2)
                {
                    if(i+1>=args.Length||!seen.Add(args[i]))throw new ArgumentException();
                    switch(args[i])
                    {
                        case "--destination":destination=args[i+1];break;
                        case "--timeout":if(!int.TryParse(args[i+1],out seconds)||seconds is <=0 or >300)throw new ArgumentException();break;
                        default:throw new ArgumentException();
                    }
                }
                if(string.IsNullOrWhiteSpace(destination))throw new ArgumentException();
            }
            catch(ArgumentException)
            {errors.WriteLine("usage: BED --probe-export <project folder> --destination <empty folder> [--timeout <1..300 seconds>]");return 1;}
            if(RuntimeCheckWindows.Check(errors)!=0)return 1;
            return ProbeExport(args[1],destination,seconds,output,errors);
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Probe(string folder, int seconds, IReadOnlyList<string> probeArguments, TextWriter output, TextWriter errors)
        => PlayProbe.Run(folder, TimeSpan.FromSeconds(seconds), output, errors, probeArguments);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int ProbeExport(string folder,string destination,int seconds,TextWriter output,TextWriter errors)
        => ExportProbe.Run(folder,destination,TimeSpan.FromSeconds(seconds),output,errors);

    // Keep native-bearing locals and adapter calls beyond the prerequisite decision's JIT boundary.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Open(string? folder, string? data, string? schemas, string? map, string? shot,
        List<string> adapterDlls, TextWriter output, TextWriter errors, string settings)
    {
        using var reportLog=ProblemReportLog.Configure(shot is null?settings:null);
        if(shot is null)ProblemReportLog.Record(ReportEvent.Started);
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
            // A screenshot explicitly requests a view. Normal startup keeps typed scenes unopened.
            if (launcher.Workspace is { } workspace &&
                workspace.Project.Files.FirstOrDefault(file => workspace.Documents.IsScene(file.Name)) is { } scene &&
                !workspace.Show(scene.Name))
            {
                errors.WriteLine(workspace.Said);
                launcher.Dispose();
                return 1;
            }
            using var host = new HeadlessHost(1600, 900);
            EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            host.Frames(launcher.Draw, 4);
            host.Save(shot);
            output.WriteLine($"Drew the screen into {Path.GetFullPath(shot)}.");
            launcher.Dispose();
            return 0;
        }
        launcher.EnableOnboarding=true; // Native interactive host only; CLI and --shot leave it disabled.
        launcher.Splash = new BrutalSplash();   // the BRUTAL logo before the start screen (BRUTAL License v1.0, 2.2)
        EditorApp.Run(new("BED"), io => EditorWindow<DataDocument>.Configure(io), launcher.Draw, dialogs => launcher.FileDialogs = dialogs,
            close: launcher.RequestWindowClose, title: () => WindowTitle.For(launcher), drop: (drop, frame) => launcher.DispatchDrop(drop.Paths, frame, drop.TargetWindow(frame)));
        launcher.Dispose();   // a game still playing when the window closes is stopped, never left to the garbage collector
        return 0;
    }
}
