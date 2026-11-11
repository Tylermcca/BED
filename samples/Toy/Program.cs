using Bed;

namespace Toy;

/// <summary>
/// The sample's command-line tool: everything a person could do to a board, done headless (goal 2). Exit codes:
/// 0 done; 1 refused (an edit or the document isn't valid; nothing written); 2 bad input (unreadable file, bad journal,
/// wrong arguments); 3 the file changed on disk underneath.
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
        toy: edit sample boards from the command line.

          toy new <board.json>                       make an empty board (won't overwrite)
          toy check <board.json>                     read and validate a board
          toy apply <board.json> <journal.json>      apply a journal's steps and save the board
          toy replay <base.json> <journal.json> <out.json>
                                                     replay a journal onto a base board, write the result
          toy commands [text]                        list commands, or search them by name

        Exit codes: 0 done, 1 refused (nothing written), 2 bad input, 3 the file changed on disk.
        """;

    /// <summary>The entry point.</summary>
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs one command, writing to <paramref name="output"/> and <paramref name="errors"/>. Tests call this directly.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors) => Run(args, output, errors, afterApply: null);

    /// <summary>Like <see cref="Run(string[], TextWriter, TextWriter)"/>, with a hook for tests: <paramref name="afterApply"/>
    /// runs during <c>apply</c> after the journal is applied and before the disk check, so a test can change the file
    /// there and see exit code 3 deterministically.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors, Action? afterApply)
    {
        try
        {
            return args switch
            {
                ["new", var path] => New(path, output, errors),
                ["check", var path] => Check(path, output, errors),
                ["apply", var path, var journal] => Apply(path, journal, output, errors, afterApply),
                ["replay", var basePath, var journal, var outPath] => Replay(basePath, journal, outPath, output, errors),
                ["commands"] => Commands("", output),
                ["commands", var text] => Commands(text, output),
                _ => Fail(errors, BadInput, Usage),
            };
        }
        catch (DocumentFormatException e) { return Fail(errors, BadInput, $"can't read it: {e.Message}"); }
        catch (InvalidDocumentException e) { return Report(errors, Refused, e.Issues); }
        catch (ReplayException e) { return Report(errors, Refused, e.Issues, $"journal entry {e.Entry} was refused; nothing was written"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail(errors, BadInput, e.Message); }
    }

    private static int New(string path, TextWriter output, TextWriter errors)
    {
        if (File.Exists(path)) return Fail(errors, BadInput, $"{path} already exists; pick another name or remove it first");
        BoardKit.Session(Board.Empty).Save(path);
        output.WriteLine($"made {path}");
        return Done;
    }

    private static int Check(string path, TextWriter output, TextWriter errors)
    {
        var session = BoardKit.Open(path);
        output.WriteLine($"{path}: valid, {session.Current.Items.Length} item(s)");
        return Done;
    }

    private static int Apply(string path, string journalPath, TextWriter output, TextWriter errors, Action? afterApply)
    {
        var session = BoardKit.Open(path);
        var steps = JournalFile.Read(File.ReadAllText(journalPath));
        session.Apply(steps);
        afterApply?.Invoke();
        if (session.CheckDisk() == DiskState.ChangedOnDisk)
            return Fail(errors, Conflict, $"{path} changed on disk while the journal was applied; nothing was written");
        session.Save();
        output.WriteLine($"{path}: applied {steps.Count} step(s), {session.Current.Items.Length} item(s)");
        return Done;
    }

    private static int Replay(string basePath, string journalPath, string outPath, TextWriter output, TextWriter errors)
    {
        var start = BoardKit.Open(basePath).Current;
        var session = DocumentSession<Board>.Replay(start, JournalFile.Read(File.ReadAllText(journalPath)), BoardKit.Format, BoardKit.Validator, BoardKit.Catalog());
        session.Save(outPath);
        output.WriteLine($"{outPath}: {session.Current.Items.Length} item(s)");
        return Done;
    }

    private static int Commands(string text, TextWriter output)
    {
        foreach (var c in BoardKit.Catalog().Search(text)) output.WriteLine($"{c.Id,-12} v{c.Version}  {c.Label}");
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
