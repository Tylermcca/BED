using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bed;

/// <summary>Whether an asset may be sent to, or used as reference by, an AI system (FUT-005). Unknown counts as forbidden.</summary>
public enum AiUse
{
    /// <summary>Nobody has said. Treated as forbidden.</summary>
    Unknown,
    /// <summary>The asset's terms allow AI use.</summary>
    Allowed,
    /// <summary>The asset's terms forbid AI use.</summary>
    Forbidden,
}

/// <summary>Where an asset came from and on what terms (AT-024, OSS-005).</summary>
/// <param name="Author">Who made it.</param>
/// <param name="Licence">An SPDX id, <c>self-authored</c>, or a short name for other terms.</param>
/// <param name="Origin">Where it came from: a URL, a script, a purchase.</param>
/// <param name="Notes">Anything else worth keeping, such as conversion history.</param>
/// <param name="Declared">What the importer declared about where it came from (<see cref="AssetOrigin"/>): made by the
/// project, licensed, or public domain. <see cref="AssetOrigin.Undeclared"/> for records from before declarations, which
/// the content rights check flags. For <see cref="AssetOrigin.Licensed"/>, <paramref name="Licence"/> and
/// <paramref name="Origin"/> (the source) are both required; for <see cref="AssetOrigin.PublicDomain"/>, the source is.</param>
public sealed record Provenance(string Author, string Licence, string Origin, string Notes = "", AssetOrigin Declared = AssetOrigin.Undeclared);

/// <summary>
/// The sidecar record that gives an asset its permanent identity (FUT-009): written beside the asset as
/// <c>&lt;file&gt;.asset.json</c> at first import, it carries a GUID that never changes, how the asset was imported, where it
/// came from, and whether AI may use it. The path is only a fallback; references use the GUID, so a move or rename (with
/// the sidecar beside the file) keeps every reference.
/// </summary>
/// <param name="Guid">The permanent id, 32 lowercase hex digits.</param>
/// <param name="Source">The asset's file name, beside the sidecar.</param>
/// <param name="SourceHash">SHA-256 of the source file when it was last imported, lowercase hex.</param>
/// <param name="Importer">The importer's id (for example <c>gltf</c>).</param>
/// <param name="ImporterVersion">The importer's version; a newer importer re-imports.</param>
/// <param name="Settings">Import settings, as a JSON object.</param>
/// <param name="Provenance">Where it came from and on what terms.</param>
/// <param name="AiUse">Whether AI may use it.</param>
/// <param name="RightsFound">The rights tags the file carried when it was imported (<see cref="RightsTags"/>), kept so
/// they're shown beside a licensed asset's declaration. Null or empty when none were found.</param>
/// <param name="RightsUnread">Why the file's rights tags couldn't all be read at import (<see cref="RightsScan.Problem"/>),
/// or null when they were. Only a licensed asset can be imported with an incomplete scan; it's kept and shown.</param>
public sealed record AssetRecord(string Guid, string Source, string SourceHash, string Importer, int ImporterVersion,
    JsonElement Settings, Provenance Provenance, AiUse AiUse, IReadOnlyList<RightsTag>? RightsFound = null, string? RightsUnread = null)
{
    /// <summary>The sidecar file name suffix.</summary>
    public const string Suffix = ".asset.json";

    /// <summary>The record's format version.</summary>
    public const int Version = 1;

    /// <summary>The sidecar path for an asset file.</summary>
    public static string SidecarFor(string assetPath) => assetPath + Suffix;

    /// <summary>True when the asset may be sent to or used by an AI system: only when explicitly allowed.</summary>
    public bool MayUseWithAi => AiUse == AiUse.Allowed;

    /// <summary>
    /// Writes a first record for <paramref name="assetPath"/> with a new GUID, without reading the file's rights tags or
    /// requiring a declared origin. Refuses when a sidecar already exists, because a GUID is assigned once and never
    /// replaced (use <see cref="Reimport"/>). Kept for existing callers; an import should go through
    /// <see cref="AssetImport.Import"/>, which requires a declaration and judges the file's tags.
    /// </summary>
    [Obsolete("Writes a record with no declared origin and no rights-tag check. Use AssetImport.Import, which requires a declaration.")]
    public static AssetRecord Create(string assetPath, string importer, int importerVersion, JsonElement settings, Provenance provenance, AiUse aiUse)
    {
        var record = New(assetPath, importer, importerVersion, settings, provenance, aiUse);
        record.WriteNew(assetPath);
        return record;
    }

    // A first record, not yet written.
    internal static AssetRecord New(string assetPath, string importer, int importerVersion, JsonElement settings, Provenance provenance, AiUse aiUse,
        IReadOnlyList<RightsTag>? found = null)
    {
        if (File.Exists(SidecarFor(assetPath))) throw new InvalidOperationException($"{SidecarFor(assetPath)} already exists; an asset's id is assigned once. Re-import it instead.");
        return new AssetRecord(System.Guid.NewGuid().ToString("N"), Path.GetFileName(assetPath), HashOf(assetPath), importer, importerVersion,
            settings.Clone(), provenance, aiUse, found is { Count: > 0 } ? found : null);
    }

    // Writes a first record, refusing if a sidecar appeared meanwhile.
    internal void WriteNew(string assetPath)
    {
        var sidecar = SidecarFor(assetPath);
        if (File.Exists(sidecar)) throw new InvalidOperationException($"{sidecar} already exists; an asset's id is assigned once. Re-import it instead.");
        AtomicFile.Write(sidecar, Write());
    }

    /// <summary>Records a fresh import of the same asset: the GUID stays, the source hash and importer version update, and
    /// the file's rights tags are read again (<see cref="RightsFound"/>). Throws <see cref="InvalidOperationException"/>,
    /// writing nothing, when the new tags contradict the declared origin (<see cref="AssetOrigins.Judge"/>): a file
    /// declared ours that now says it's someone else's isn't silently re-imported.</summary>
    /// <param name="ourNames">The project's own rights holders (see <see cref="AssetImport.Import"/>).</param>
    public AssetRecord Reimport(string assetPath,int importerVersion,IReadOnlyCollection<string>? ourNames=null)
    {
        var updated=PrepareReimport(assetPath,importerVersion,ourNames);updated.SaveReimport(assetPath);return updated;
    }
    /// <summary>Prepares the same existing reimport checks and updated hash without writing the sidecar.</summary>
    /// <param name="assetPath">The existing admitted source path.</param>
    /// <param name="importerVersion">The importer version to record.</param>
    /// <param name="ourNames">The project's declared rights holders, as for Reimport.</param>
    public AssetRecord PrepareReimport(string assetPath, int importerVersion, IReadOnlyCollection<string>? ourNames = null)
    {
        var scan = RightsTags.ReadFile(assetPath);
        if (AssetOrigins.Judge(Provenance, scan.Tags, ourNames, Guid, scan.Problem) is { Count: > 0 } refused)
            throw new InvalidOperationException($"{Source} wasn't re-imported: {refused[0].Message}");
        var updated = this with { SourceHash = HashOf(assetPath), ImporterVersion = importerVersion, RightsFound = scan.Tags.Count > 0 ? scan.Tags : null, RightsUnread = scan.Problem };
        return updated;
    }

    /// <summary>Saves a prepared same-GUID record. Optional exact predecessor text prevents a stale settings draft overwriting another save.</summary>
    public void SaveReimport(string assetPath,string? expectedSidecar=null)
    {
        var sidecar=SidecarFor(assetPath);
        if(expectedSidecar is not null)
        {
            var before=File.ReadAllText(sidecar);
            if(before!=expectedSidecar)throw new InvalidOperationException("Asset sidecar changed; reopen its settings.");
            if(Read(before).Guid!=Guid)throw new InvalidOperationException("Asset identity changed; reimport refused.");
        }
        AtomicFile.Write(sidecar,Write());
    }

    /// <summary>SHA-256 of a file, lowercase hex.</summary>
    public static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Canonical text for the record.</summary>
    public string Write()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind", "asset");
            w.WriteNumber("version", Version);
            w.WriteString("guid", Guid);
            w.WriteString("source", Source);
            w.WriteString("sourceHash", SourceHash);
            w.WriteStartObject("importer");
            w.WriteString("id", Importer);
            w.WriteNumber("version", ImporterVersion);
            w.WriteEndObject();
            w.WritePropertyName("settings");
            Settings.WriteTo(w);
            w.WriteStartObject("provenance");
            w.WriteString("author", Provenance.Author);
            w.WriteString("licence", Provenance.Licence);
            w.WriteString("origin", Provenance.Origin);
            if (Provenance.Notes.Length > 0) w.WriteString("notes", Provenance.Notes);
            if (Provenance.Declared != AssetOrigin.Undeclared) w.WriteString("declared", AssetOrigins.Name(Provenance.Declared));
            w.WriteEndObject();
            w.WriteString("aiUse", AiUse.ToString().ToLowerInvariant());
            if (RightsFound is { Count: > 0 } found)
            {
                w.WriteStartArray("rightsFound");
                foreach (var tag in found)
                {
                    w.WriteStartObject();
                    w.WriteString("format", tag.Format);
                    w.WriteString("field", tag.Field);
                    w.WriteString("kind", tag.Kind.ToString().ToLowerInvariant());
                    w.WriteString("text", tag.Text);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            if (RightsUnread is { } unread) w.WriteString("rightsUnread", unread);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Reads a sidecar's text. Throws <see cref="DocumentFormatException"/> with the path of the first problem.</summary>
    public static AssetRecord Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "guid", "source", "sourceHash", "importer", "settings", "provenance", "aiUse", "rightsFound", "rightsUnread");
        if (r.String("kind") != "asset") throw new DocumentFormatException("$.kind", "expected \"asset\"");
        var version = r.Int("version");
        if (version != Version) throw new DocumentFormatException("$.version", $"asset record version {version} is not supported (this tool reads version {Version})");
        var guid = r.String("guid");
        if (guid.Length != 32 || !guid.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new DocumentFormatException("$.guid", "expected 32 lowercase hex digits");
        var importer = new StrictReader(r.Element("importer"), "$.importer");
        importer.Only("id", "version");
        var settings = r.Element("settings");
        if (settings.ValueKind != JsonValueKind.Object) throw new DocumentFormatException("$.settings", "expected an object");
        var p = new StrictReader(r.Element("provenance"), "$.provenance");
        p.Only("author", "licence", "origin", "notes", "declared");
        var declared = AssetOrigin.Undeclared;
        if (p.Has("declared") && !AssetOrigins.TryParse(p.String("declared"), out declared))
            throw new DocumentFormatException("$.provenance.declared", $"'{p.String("declared")}' isn't ours, licensed or publicDomain");
        var ai = r.String("aiUse") switch
        {
            "allowed" => AiUse.Allowed,
            "forbidden" => AiUse.Forbidden,
            "unknown" => AiUse.Unknown,
            var other => throw new DocumentFormatException("$.aiUse", $"'{other}' isn't allowed, forbidden or unknown"),
        };
        List<RightsTag>? found = null;
        if (r.Has("rightsFound"))
        {
            var list = r.Element("rightsFound");
            if (list.ValueKind != JsonValueKind.Array) throw new DocumentFormatException("$.rightsFound", "expected an array");
            found = [];
            var i = 0;
            foreach (var item in list.EnumerateArray())
            {
                var t = new StrictReader(item, $"$.rightsFound[{i}]");
                t.Only("format", "field", "kind", "text");
                if (!Enum.TryParse<RightsKind>(t.String("kind"), ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
                    throw new DocumentFormatException($"$.rightsFound[{i}].kind", $"'{t.String("kind")}' isn't copyright, licence or author");
                found.Add(new RightsTag(t.String("format"), t.String("field"), kind, t.String("text")));
                i++;
            }
        }
        return new AssetRecord(guid, r.String("source"), r.String("sourceHash"), importer.String("id"), importer.Int("version"), settings,
            new Provenance(p.String("author"), p.String("licence"), p.String("origin"), p.Has("notes") ? p.String("notes") : "", declared), ai, found,
            r.Has("rightsUnread") ? r.String("rightsUnread") : null);
    }
}

/// <summary>
/// Every asset under a folder, found by its sidecar and indexed by GUID. Building the index also reports what's wrong:
/// a GUID used twice (both places named), a sidecar whose source is missing, a source changed since its import, a file
/// with no sidecar yet, and an asset with no licence recorded. It never guesses: a GUID that can't be found stays missing,
/// and is never re-bound to another file by its path.
/// </summary>
public sealed class AssetIndex
{
    private readonly Dictionary<string, (AssetRecord Record, string Path)> byGuid = new(StringComparer.Ordinal);

    private AssetIndex(IReadOnlyList<Issue> issues) => Issues = issues;

    /// <summary>What the scan found wrong.</summary>
    public IReadOnlyList<Issue> Issues { get; }

    /// <summary>How many assets were indexed.</summary>
    public int Count => byGuid.Count;

    /// <summary>The asset file for <paramref name="guid"/>, or null when no asset has it.</summary>
    public string? Resolve(string guid) => byGuid.TryGetValue(guid, out var a) ? a.Path : null;

    /// <summary>The record for <paramref name="guid"/>, or null.</summary>
    public AssetRecord? Record(string guid) => byGuid.TryGetValue(guid, out var a) ? a.Record : null;

    /// <summary>Scans <paramref name="root"/>. Files whose extension is in <paramref name="assetExtensions"/> and have no
    /// sidecar are reported as not yet imported.</summary>
    public static AssetIndex Scan(string root, params string[] assetExtensions)
    {
        var issues = new List<Issue>();
        var found = new List<(AssetRecord Record, string Path, string Sidecar)>();
        foreach (var sidecar in Directory.EnumerateFiles(root, "*" + AssetRecord.Suffix, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, sidecar);
            AssetRecord record;
            try { record = AssetRecord.Read(File.ReadAllText(sidecar)); }
            catch (DocumentFormatException e) { issues.Add(Issue.Error($"can't read {rel}: {e.Message}", path: rel)); continue; }
            var asset = sidecar[..^AssetRecord.Suffix.Length];
            if (Path.GetFileName(asset) != record.Source)
                issues.Add(Issue.Warning($"{rel} names its source '{record.Source}' but sits beside '{Path.GetFileName(asset)}'; the sidecar follows its file", record.Guid, rel));
            if (!File.Exists(asset))
            {
                issues.Add(Issue.Error($"{rel} has no asset beside it; restore the file or delete the sidecar", record.Guid, rel));
                continue;
            }
            if (AssetRecord.HashOf(asset) != record.SourceHash)
                issues.Add(Issue.Warning($"{Path.GetRelativePath(root, asset)} changed since it was imported; re-import it", record.Guid, rel));
            if (string.IsNullOrWhiteSpace(record.Provenance.Licence) && record.Provenance.Declared != AssetOrigin.Ours)
                issues.Add(Issue.Warning($"{Path.GetRelativePath(root, asset)} has no licence recorded", record.Guid, rel));
            found.Add((record, asset, rel));
        }
        var index = new AssetIndex(issues);
        foreach (var group in found.GroupBy(f => f.Record.Guid, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                issues.Add(Issue.Error($"id {group.Key} is used by {string.Join(" and ", group.Select(g => g.Sidecar))}; give one of them a new id", group.Key));
                continue;   // ambiguous: resolve neither rather than pick one
            }
            var only = group.Single();
            index.byGuid[group.Key] = (only.Record, only.Path);
        }
        var known = found.Select(f => Path.GetFullPath(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in assetExtensions)
            foreach (var file in Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                if (!known.Contains(Path.GetFullPath(file)))
                    issues.Add(Issue.Warning($"{Path.GetRelativePath(root, file)} has no {AssetRecord.Suffix} yet; import it to give it an id", path: Path.GetRelativePath(root, file)));
        return index;
    }
}
