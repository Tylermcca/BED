using System.Globalization;
using Bed;

namespace Tiles;

// MIXED: Toy's documented CLI/exit-code conventions + original tile-map commands and validation.
public static class Program
{
    private const string Usage = "tiles new <map.json> [width height cell-size-metres]\n" +
        "tiles check <map.json>\ntiles apply <map.json> <journal.json>\n" +
        "tiles replay <base.json> <journal.json> <out.json>\ntiles commands [text]\n" +
        "Exit codes: 0 done; 1 refused; 2 bad input; 3 changed on disk.";
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);
    public static int Run(string[] args, TextWriter output, TextWriter errors, Action? afterApply = null)
    {
        try
        {
            switch (args)
            {
                case ["new", var path]:
                    return New(path, 2, 2, 1, output, errors);
                case ["new", var path, var width, var height, var size]:
                    if (!int.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out int w) ||
                        !int.TryParse(height, NumberStyles.None, CultureInfo.InvariantCulture, out int h) ||
                        !double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out double metres))
                        return Fail(errors, 2, "Use integer width/height and a finite cell size in metres.");
                    return New(path, w, h, metres, output, errors);
                case ["check", var path]:
                    var checkedMap = TilesKit.Open(path).Current;
                    output.WriteLine($"{path}: valid, {checkedMap.Width} x {checkedMap.Height} cells, {checkedMap.Kinds.Length} kind(s)");
                    return 0;
                case ["commands"]:
                    List("", output); return 0;
                case ["commands", var text]:
                    List(text, output); return 0;
                case ["apply", var path, var journal]:
                    var session = TilesKit.Open(path);
                    var entries = JournalFile.Read(File.ReadAllText(journal));
                    session.Apply(entries);
                    afterApply?.Invoke();
                    if (session.CheckDisk() != DiskState.Unchanged)
                        return Fail(errors, 3, $"{path} changed on disk while applying; nothing was written.");
                    session.Save();
                    output.WriteLine($"{path}: applied {entries.Count} step(s)");
                    return 0;
                case ["replay", var path, var journal, var target]:
                    var baseMap = TilesKit.Open(path).Current;
                    var replayed = DocumentSession<TileMap>.Replay(baseMap, JournalFile.Read(File.ReadAllText(journal)),
                        TilesKit.Format, TilesKit.Validator, TilesKit.Catalog());
                    replayed.Save(target);
                    output.WriteLine($"{target}: {replayed.Current.Width} x {replayed.Current.Height} cells");
                    return 0;
                default: return Fail(errors, 2, Usage);
            }
        }
        catch (DocumentFormatException e) { return Fail(errors, 2, e.Message); }
        catch (InvalidDocumentException e) { return Report(errors, e.Issues); }
        catch (ReplayException e)
        {
            errors.WriteLine($"journal entry {e.Entry} refused; nothing was written");
            return Report(errors, e.Issues);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return Fail(errors, 2, e.Message); }
    }
    private static int New(string path, int width, int height, double size, TextWriter output, TextWriter errors)
    {
        if (File.Exists(path)) return Fail(errors, 2, $"{path} already exists; choose another path.");
        TilesKit.Session(TileMap.Empty(width, height, size)).Save(path);
        output.WriteLine($"made {path}");
        return 0;
    }
    private static void List(string query, TextWriter output)
    {
        foreach (var c in TilesKit.Catalog().Search(query)) output.WriteLine($"{c.Id} v{c.Version}: {c.Label}");
    }
    private static int Report(TextWriter errors, IEnumerable<Issue> issues)
    {
        foreach (var issue in issues) errors.WriteLine(issue);
        return 1;
    }
    private static int Fail(TextWriter errors, int code, string message) { errors.WriteLine(message); return code; }
}
