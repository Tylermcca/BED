using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Brutal.Authoring.Data;

// Content rights, part 3 (2026-11-07): names a game must never use, refused at the field, and a check listing every asset's
// declared origin. INVENTED: the normalised whole-word match and the check's findings; MIXED: the check rides the existing
// registered-check seam (piece J), and the names ride the project's rules seam, so the editor, the command line and MCP
// refuse alike.

/// <summary>
/// A project's protected names: names or phrases its game must never use (a trademark it doesn't own, a real person's
/// name). A game supplies them (<see cref="DataProject.ProtectedNames"/>; an editor adapter gives them through its
/// <c>ProtectedNames</c>). As a validator it refuses any text value in a data file that contains one, case-insensitively
/// and as whole words, after simple normalisation: accents are dropped, and punctuation and runs of spaces count as one
/// space, so "Spider-Man", "spider  man" and "SPIDER MAN!" all match the name "Spider Man", while "Spidermanly" doesn't.
/// It's a seatbelt for names the game already knows to avoid, not a search for every protected name in the world.
/// </summary>
public sealed class ProtectedNames : IValidator<DataDocument>
{
    private readonly List<(string Name, string Normal)> names;

    /// <summary>A list of <paramref name="names"/>; blank ones are ignored.</summary>
    public ProtectedNames(IEnumerable<string> names) =>
        this.names = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => (n.Trim(), Normalise(n))).Where(n => n.Item2.Length > 0)
            .DistinctBy(n => n.Item2).ToList();

    /// <summary>The names, as given.</summary>
    public IReadOnlyList<string> Names => names.Select(n => n.Name).ToList();

    /// <summary>True when there are none.</summary>
    public bool IsEmpty => names.Count == 0;

    /// <summary>The protected name <paramref name="text"/> uses, or null when it uses none.</summary>
    public string? Match(string text)
    {
        if (names.Count == 0 || string.IsNullOrEmpty(text)) return null;
        var normal = " " + Normalise(text) + " ";
        foreach (var (name, n) in names)
            if (normal.Contains(" " + n + " ", StringComparison.Ordinal)) return name;
        return null;
    }

    /// <summary>Every text value or object key in <paramref name="document"/> that uses a protected name, as an error at
    /// its field, naming the item it's in.</summary>
    public IReadOnlyList<Issue> Validate(DataDocument document)
    {
        var issues = new List<Issue>();
        if (names.Count == 0) return issues;
        Walk(document.Root, [], issues);
        return issues;
    }

    private void Walk(JsonElement value, List<string> at, List<Issue> issues)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                if (Match(value.GetString()!) is { } name) issues.Add(Refusal(name, at, null));
                break;
            case JsonValueKind.Object:
                foreach (var p in value.EnumerateObject())
                {
                    at.Add(p.Name);
                    // Data keyed by name ({"Zorbo": {...}}) is checked by its keys too, the issue at the keyed member.
                    if (Match(p.Name) is { } keyed) issues.Add(Refusal(keyed, at, "as a key"));
                    Walk(p.Value, at, issues);
                    at.RemoveAt(at.Count - 1);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var e in value.EnumerateArray()) { at.Add(i++.ToString(CultureInfo.InvariantCulture)); Walk(e, at, issues); at.RemoveAt(at.Count - 1); }
                break;
        }
    }

    private static Issue Refusal(string name, List<string> at, string? how)
    {
        var item = at.Count switch { 0 => null, 1 => Pointer.Join(at.Take(1)), _ => int.TryParse(at[1], out _) ? Pointer.Join(at.Take(2)) : Pointer.Join(at.Take(1)) };
        return Issue.Error($"uses \"{name}\"{(how is null ? "" : " " + how)}, which is on this project's protected names list (names the game must never use); choose another name",
            item, Pointer.ToPath(Pointer.Join(at)));
    }

    /// <summary>Text as matching sees it (<see cref="NameText.Normalise"/>): compatibility forms folded, accents dropped,
    /// lower case, every run of non-letters and non-digits one space, trimmed.</summary>
    public static string Normalise(string text) => NameText.Normalise(text);
}

/// <summary>
/// The content rights check (<c>rights.assets</c>): every asset under a folder with its declared origin, licence, source
/// and the rights tags its file carries now, listed in the report's summary (a licensed asset's tags are shown there, not
/// flagged). It flags an asset with no declared origin (imported before declarations
/// existed), a declaration missing what it needs, and a declaration the file's tags contradict (declared ours or public
/// domain while a tag claims rights), as well as a file that looks like an asset but was never imported. A licensed asset's
/// tags are listed, not flagged. It reads the folder (declared in <see cref="CheckDefinition.Reads"/>, so a change there
/// makes its result stale) and never writes. It can't tell whether a file is someone else's work: only what's declared,
/// and what the file says about itself.
/// </summary>
/// <param name="assetFolder">The folder holding the assets and their sidecars.</param>
/// <param name="assetExtensions">Extensions of files that count as assets even without a sidecar, such as <c>.png</c>.</param>
/// <param name="ourNames">The project's own rights holders, whose names in a tag don't count against an asset declared ours.</param>
public sealed class ContentRightsCheck(string assetFolder, IReadOnlyList<string>? assetExtensions = null, IReadOnlyCollection<string>? ourNames = null) : ICheck
{
    /// <summary>The extensions treated as assets when none are given: images, models, sounds and fonts.</summary>
    public static readonly IReadOnlyList<string> CommonExtensions = [".png", ".jpg", ".jpeg", ".glb", ".gltf", ".obj", ".wav", ".ogg", ".mp3", ".flac", ".ttf", ".otf", ".ttc"];

    /// <summary>The most assets the summary lists one by one.</summary>
    public const int MostListed = 200;

    private readonly string folder = Path.GetFullPath(assetFolder);
    private readonly IReadOnlyList<string> extensions = assetExtensions ?? CommonExtensions;

    /// <inheritdoc />
    public CheckDefinition Definition => new("rights.assets", "Check content rights",
        "Lists every asset's declared origin, licence, source and the rights tags in its file; flags undeclared assets, incomplete declarations and tags that contradict a declaration. It can't detect copying: it checks what's declared against what the files say.",
        TimeSpan.FromMinutes(2), Reads: [folder]);

    /// <inheritdoc />
    public Task<CheckReport> RunAsync(CheckSnapshot snapshot, IProgress<CheckProgress> progress, CancellationToken cancel) => Task.Run(() => Run(progress, cancel), cancel);

    private CheckReport Run(IProgress<CheckProgress> progress, CancellationToken cancel)
    {
        if (!Directory.Exists(folder)) return new(false, [], $"There's no asset folder at {folder}, so no assets were checked.");
        var findings = new List<CheckFinding>();
        var index = AssetIndex.Scan(folder, [.. extensions]);
        foreach (var issue in index.Issues.Where(i => i.ObjectId is null && i.Path is not null && i.Message.Contains("has no " + AssetRecord.Suffix)))
            findings.Add(new(Issue.Warning($"{issue.Path} isn't imported, so it has no declared origin", path: issue.Path), new CheckPlace(issue.Path!), "Import it with its origin declared: ours, licensed (licence and source) or public domain (source)."));
        var sidecars = Directory.EnumerateFiles(folder, "*" + AssetRecord.Suffix, SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var listed = 0;
        var lines = new List<string>();
        foreach (var sidecar in sidecars)
        {
            cancel.ThrowIfCancellationRequested();
            progress.Report(new CheckProgress(sidecars.Count == 0 ? 1 : (double)listed / sidecars.Count, $"Reading {Path.GetFileName(sidecar)}..."));
            var asset = sidecar[..^AssetRecord.Suffix.Length];
            var rel = Path.GetRelativePath(folder, asset);
            AssetRecord record;
            try { record = AssetRecord.Read(File.ReadAllText(sidecar)); }
            catch (Exception e) when (e is DocumentFormatException or IOException or JsonException)
            {
                findings.Add(new(Issue.Error($"{rel}'s record can't be read: {e.Message}", path: rel), new CheckPlace(rel), "Fix or restore the sidecar."));
                continue;
            }
            listed++;
            var place = new CheckPlace(rel, record.Guid);
            var scan = File.Exists(asset) ? RightsTags.ReadFile(asset) : new RightsScan(null, record.RightsFound ?? [], "the file is missing");
            var tags = scan.Tags.Count > 0 ? scan.Tags : record.RightsFound ?? [];
            var p = record.Provenance;
            var line = $"{rel}: {AssetOrigins.Name(p.Declared)}" + (p.Licence.Length > 0 ? $", licence \"{p.Licence}\"" : "") + (p.Origin.Length > 0 ? $", source \"{p.Origin}\"" : "")
                + (tags.Count > 0 ? $"; tags: {string.Join("; ", tags)}" : "; no rights tags") + (scan.Problem is { } unread ? $"; not all read: {unread}" : "");
            if (lines.Count < MostListed) lines.Add(line);
            var summary = line;
            if (p.Declared == AssetOrigin.Undeclared)
            {
                findings.Add(new(Issue.Warning($"{summary}. It has no declared origin (imported before declarations).", record.Guid, rel), place, "Declare it ours, licensed (licence and source) or public domain (source)."));
                continue;
            }
            var problems = AssetOrigins.Missing(p, record.Guid).Concat(AssetOrigins.Judge(p, tags, ourNames, record.Guid, scan.Problem)).ToList();
            foreach (var problem in problems)
                findings.Add(new(problem with { Message = $"{summary}. {problem.Message}" }, place, "Correct its declaration, or replace the file."));
        }
        progress.Report(new CheckProgress(1, $"Read {listed} asset record(s)."));
        var more = listed > lines.Count ? $"\n...and {listed - lines.Count} more" : "";
        return new(true, findings, $"{listed} asset(s); {findings.Count} finding(s)." + (lines.Count > 0 ? "\n" + string.Join("\n", lines) + more : ""));
    }
}
