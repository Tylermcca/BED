namespace Bed.Editor;

/// <summary>One asset as the browser lists it.</summary>
/// <param name="Guid">Its permanent id.</param>
/// <param name="Name">Its file name.</param>
/// <param name="RelativePath">Where it is, relative to the project folder.</param>
/// <param name="Importer">Which importer reads it.</param>
/// <param name="Licence">Its recorded licence.</param>
/// <param name="AiUse">Whether AI may use it.</param>
/// <param name="Issues">What's wrong with it, if anything (shown as a badge).</param>
public sealed record AssetEntry(string Guid, string Name, string RelativePath, string Importer, string Licence, AiUse AiUse, IReadOnlyList<Issue> Issues)
{
    /// <summary>True when something about it needs attention.</summary>
    public bool HasProblems => Issues.Count > 0;
}

/// <summary>
/// The asset browser's model (worksheet AT-025): every asset under a project folder, from its sidecar, with search and a
/// "problems only" filter. Assets with problems carry badges: a missing source, a change since import, no licence, an id
/// used twice. Files not yet imported are listed separately. The ImGui window draws this; it owns no UI.
/// </summary>
public sealed class AssetBrowser
{
    private readonly string root;
    private readonly string[] extensions;

    /// <summary>A browser over <paramref name="root"/>, treating files with <paramref name="assetExtensions"/> as assets.</summary>
    public AssetBrowser(string root, params string[] assetExtensions)
    {
        this.root = Path.GetFullPath(root);
        extensions = assetExtensions;
        Refresh();
    }

    /// <summary>Every indexed asset, ordered by path.</summary>
    public IReadOnlyList<AssetEntry> Entries { get; private set; } = [];

    /// <summary>Files that look like assets but have no sidecar yet, relative to the project folder.</summary>
    public IReadOnlyList<string> NotImported { get; private set; } = [];

    /// <summary>Problems that belong to no single listed asset: an unreadable sidecar, or an id used by two or more assets
    /// (which also shows as a badge on each of them).</summary>
    public IReadOnlyList<Issue> General { get; private set; } = [];

    /// <summary>The index behind the list.</summary>
    public AssetIndex Index { get; private set; } = null!;

    /// <summary>Rescans the folder.</summary>
    public void Refresh()
    {
        Index = AssetIndex.Scan(root, extensions);
        var byGuid = Index.Issues.Where(i => i.ObjectId is not null).ToLookup(i => i.ObjectId!);
        var entries = new List<AssetEntry>();
        foreach (var sidecar in Directory.EnumerateFiles(root, "*" + AssetRecord.Suffix, SearchOption.AllDirectories))
        {
            AssetRecord record;
            try { record = AssetRecord.Read(File.ReadAllText(sidecar)); }
            catch (DocumentFormatException) { continue; }   // reported in General by the index
            var asset = sidecar[..^AssetRecord.Suffix.Length];
            entries.Add(new AssetEntry(record.Guid, Path.GetFileName(asset), Path.GetRelativePath(root, asset), record.Importer,
                record.Provenance.Licence, record.AiUse, byGuid[record.Guid].ToList()));
        }
        Entries = entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        NotImported = Index.Issues.Where(i => i.ObjectId is null && i.Message.Contains("has no " + AssetRecord.Suffix)).Select(i => i.Path!).Order(StringComparer.OrdinalIgnoreCase).ToList();
        // An id used by two or more listed assets belongs to none of them alone: it shows in General as well as on each badge.
        var shared = Entries.GroupBy(e => e.Guid).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        General = Index.Issues.Where(i => i.ObjectId is null ? !i.Message.Contains("has no " + AssetRecord.Suffix) : shared.Contains(i.ObjectId)).ToList();
    }

    /// <summary>Assets matching <paramref name="text"/> in their name, path, id prefix, importer or licence
    /// (case-insensitive; empty matches all), optionally only those with problems.</summary>
    public IReadOnlyList<AssetEntry> Search(string text, bool problemsOnly = false)
    {
        var t = text.Trim();
        return Entries.Where(e => (!problemsOnly || e.HasProblems) && (t.Length == 0
            || e.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
            || e.RelativePath.Contains(t, StringComparison.OrdinalIgnoreCase)
            || e.Guid.StartsWith(t, StringComparison.OrdinalIgnoreCase)
            || e.Importer.Equals(t, StringComparison.OrdinalIgnoreCase)
            || e.Licence.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
