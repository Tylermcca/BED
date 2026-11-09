using System.Text.Json;
using Brutal.Authoring;

namespace SceneTool;

/// <summary>
/// The <c>scene</c> command-line tool: the general scene document edited headless (goal 2). Every change is one of
/// <see cref="SceneKit.Catalog"/>'s commands, so a script, a build step or an AI helper edits exactly as the editor does:
/// validated, refused whole, replayable. Exit codes: 0 done; 1 refused (an edit, the scene, or a missing asset; nothing
/// written); 2 bad input (unreadable file, bad journal or arguments); 3 the file changed on disk underneath.
/// </summary>
public static class Program
{
    /// <summary>Exit code: done.</summary>
    public const int Done = 0;
    /// <summary>Exit code: refused; nothing was written.</summary>
    public const int Refused = 1;
    /// <summary>Exit code: bad input.</summary>
    public const int BadInput = 2;
    /// <summary>Exit code: the file changed on disk underneath.</summary>
    public const int Conflict = 3;

    private const string Usage = """
        scene: edit scene files from the command line.

          scene new <scene.json>                          make an empty scene (won't overwrite)
          scene tree <scene.json>                         show its objects, nested by parent
          scene check <scene.json> [--assets <folder>]    read and validate it; with --assets, also report
                                                          assets missing from that folder
          scene do <scene.json> <command> <args-json>     run one command and save, e.g.
                                                          scene do room.json scene.move '{"id":"lamp","position":[1,0,2]}'
          scene apply <scene.json> <journal.json>         apply a journal's steps and save
          scene replay <base.json> <journal.json> <out.json>
                                                          replay a journal onto a base scene, write the result
          scene commands [text]                           list commands, or search them by name

        Add --kind <kind> to read and write a game's own scene kind (the file's "kind"; default "scene").
        Exit codes: 0 done, 1 refused (nothing written), 2 bad input, 3 the file changed on disk.
        """;

    /// <summary>The entry point.</summary>
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs one command, writing to <paramref name="output"/> and <paramref name="errors"/>. Tests call this directly.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors) => Run(args, output, errors, afterApply: null);

    /// <summary>Like <see cref="Run(string[], TextWriter, TextWriter)"/>, with a hook for tests: <paramref name="afterApply"/>
    /// runs during <c>apply</c> and <c>do</c> after the edit and before the disk check, so a test can change the file
    /// there and see exit code 3 deterministically.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors, Action? afterApply)
    {
        var kind = "scene";
        var rest = new List<string>(args);
        var k = rest.IndexOf("--kind");
        if (k >= 0)
        {
            if (k + 1 >= rest.Count) return Fail(errors, BadInput, "--kind needs a kind, e.g. --kind level");
            kind = rest[k + 1];
            rest.RemoveRange(k, 2);
        }
        var format = new SceneFormat(kind);
        try
        {
            return rest.ToArray() switch
            {
                ["new", var path] => New(format, path, output, errors),
                ["tree", var path] => Tree(format, path, output),
                ["check", var path] => Check(format, path, null, output, errors),
                ["check", var path, "--assets", var root] => Check(format, path, root, output, errors),
                ["do", var path, var command, var json] => Do(format, path, command, json, output, errors, afterApply),
                ["apply", var path, var journal] => Apply(format, path, journal, output, errors, afterApply),
                ["replay", var basePath, var journal, var outPath] => Replay(format, basePath, journal, outPath, output),
                ["commands"] => Commands("", output),
                ["commands", var text] => Commands(text, output),
                _ => Fail(errors, BadInput, Usage),
            };
        }
        catch (DocumentFormatException e) { return Fail(errors, BadInput, $"can't read it: {e.Message} (at {e.Path})"); }
        catch (InvalidDocumentException e) { return Report(errors, Refused, e.Issues); }
        catch (ReplayException e) { return Report(errors, Refused, e.Issues, $"journal entry {e.Entry} was refused; nothing was written"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail(errors, BadInput, e.Message); }
    }

    private static DocumentSession<Scene> Open(SceneFormat format, string path) =>
        DocumentSession<Scene>.Open(path, format, new SceneValidator(), SceneKit.Catalog());

    private static int New(SceneFormat format, string path, TextWriter output, TextWriter errors)
    {
        if (File.Exists(path)) return Fail(errors, BadInput, $"{path} already exists; pick another name or remove it first");
        new DocumentSession<Scene>(format, new SceneValidator(), SceneKit.Catalog(), Scene.Empty).Save(path);
        output.WriteLine($"made {path}");
        return Done;
    }

    private static int Tree(SceneFormat format, string path, TextWriter output)
    {
        var scene = Open(format, path).Current;
        void Show(string? parent, int depth)
        {
            foreach (var o in scene.Objects.Where(o => o.Parent == parent))
            {
                var p = o.Position;
                output.WriteLine($"{new string(' ', depth * 2)}{o.Id}  ({p.X:0.###}, {p.Y:0.###}, {p.Z:0.###}){(o.Asset is { } a ? "  " + a : "")}");
                Show(o.Id, depth + 1);
            }
        }
        Show(null, 0);
        output.WriteLine($"{scene.Objects.Length} object(s)");
        return Done;
    }

    private static int Check(SceneFormat format, string path, string? root, TextWriter output, TextWriter errors)
    {
        var scene = Open(format, path).Current;
        if (root is not null)
        {
            var full = Path.GetFullPath(root);
            var missing = SceneAssets.Check(scene, a => File.Exists(Path.Combine(full, a)));
            if (missing.Count > 0) return Report(errors, Refused, missing, $"{path}: {missing.Count} asset(s) missing from {root}");
        }
        output.WriteLine($"{path}: valid, {scene.Objects.Length} object(s)");
        return Done;
    }

    private static int Do(SceneFormat format, string path, string command, string json, TextWriter output, TextWriter errors, Action? afterApply)
    {
        var catalog = SceneKit.Catalog();
        if (catalog.Commands.FirstOrDefault(c => c.Id == command) is not { } info)
            return Fail(errors, BadInput, $"unknown command '{command}'; 'scene commands' lists them");
        JsonElement args;
        try { args = JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException e) { return Fail(errors, BadInput, $"the arguments aren't JSON: {e.Message}"); }
        var step = catalog.FromEntry(JournalEntry.Do(info.Id, info.Version, args));
        var session = Open(format, path);
        if (!session.Execute(step)) return Report(errors, Refused, session.LastIssues, $"{info.Label} was refused; nothing was written");
        afterApply?.Invoke();
        if (session.CheckDisk() == DiskState.ChangedOnDisk)
            return Fail(errors, Conflict, $"{path} changed on disk while it was being edited; nothing was written");
        session.Save();
        output.WriteLine($"{path}: {info.Label.ToLowerInvariant()} done, {session.Current.Objects.Length} object(s)");
        return Done;
    }

    private static int Apply(SceneFormat format, string path, string journalPath, TextWriter output, TextWriter errors, Action? afterApply)
    {
        var session = Open(format, path);
        var steps = JournalFile.Read(File.ReadAllText(journalPath));
        session.Apply(steps);
        afterApply?.Invoke();
        if (session.CheckDisk() == DiskState.ChangedOnDisk)
            return Fail(errors, Conflict, $"{path} changed on disk while the journal was applied; nothing was written");
        session.Save();
        output.WriteLine($"{path}: applied {steps.Count} step(s), {session.Current.Objects.Length} object(s)");
        return Done;
    }

    private static int Replay(SceneFormat format, string basePath, string journalPath, string outPath, TextWriter output)
    {
        var start = Open(format, basePath).Current;
        var session = DocumentSession<Scene>.Replay(start, JournalFile.Read(File.ReadAllText(journalPath)), format, new SceneValidator(), SceneKit.Catalog());
        session.Save(outPath);
        output.WriteLine($"{outPath}: {session.Current.Objects.Length} object(s)");
        return Done;
    }

    private static int Commands(string text, TextWriter output)
    {
        foreach (var c in SceneKit.Catalog().Search(text)) output.WriteLine($"{c.Id,-20} v{c.Version}  {c.Label}");
        return Done;
    }

    private static int Report(TextWriter errors, int code, IEnumerable<Issue> issues, string? lead = null)
    {
        if (lead is not null) errors.WriteLine(lead);
        foreach (var i in issues) errors.WriteLine(i);
        return code;
    }

    private static int Fail(TextWriter errors, int code, string message)
    {
        errors.WriteLine(message);
        return code;
    }
}
