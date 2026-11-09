using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brutal.Authoring;

// Content rights, part 2 (2026-11-07): every import declares where its asset came from, and a declaration that a file's own
// rights tags contradict is refused. This is a seatbelt, not a detector: no tool can tell whether a file is someone else's
// work. It makes origins declared and traceable, and it refuses what a file plainly says against them. MIXED: the asset
// record's provenance (research section 5, BORROWED from Unity's .meta and Godot's .import sidecars); INVENTED: the three
// declarations and the refusal rules.

/// <summary>Text as name matching sees it: compatibility forms folded (FormKD, so "ﬁ" is "fi"), accents dropped, lower
/// case, and every run of anything but letters and digits one space, trimmed. Used for protected names and for a project's
/// own rights holders, so both match whole words the same way.</summary>
public static class NameText
{
    /// <summary>The normalised form of <paramref name="text"/>.</summary>
    public static string Normalise(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormKD);
        var b = new System.Text.StringBuilder(decomposed.Length);
        var space = false;
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { if (space && b.Length > 0) b.Append(' '); b.Append(char.ToLowerInvariant(c)); space = false; }
            else space = true;
        }
        return b.ToString();
    }

    /// <summary>Whether <paramref name="text"/> contains <paramref name="name"/> as whole words, both normalised.</summary>
    public static bool ContainsWords(string text, string name)
    {
        var n = Normalise(name);
        return n.Length > 0 && $" {Normalise(text)} ".Contains($" {n} ", StringComparison.Ordinal);
    }
}

/// <summary>Where an asset came from, as its importer declares it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetOrigin>))]
public enum AssetOrigin
{
    /// <summary>Not declared: a record from before declarations. Never accepted for a new import; flagged by the content
    /// rights check.</summary>
    Undeclared,
    /// <summary>Made by the project itself.</summary>
    Ours,
    /// <summary>Someone else's work, used under a licence: the licence's name and where it came from are both required.</summary>
    Licensed,
    /// <summary>Public domain (or dedicated to it, such as CC0): where it came from is required.</summary>
    PublicDomain,
}

/// <summary>The names <see cref="AssetOrigin"/> values are written and read as: <c>ours</c>, <c>licensed</c>,
/// <c>publicDomain</c>.</summary>
public static class AssetOrigins
{
    /// <summary>The written name of <paramref name="origin"/>.</summary>
    public static string Name(AssetOrigin origin) => origin switch
    {
        AssetOrigin.Ours => "ours",
        AssetOrigin.Licensed => "licensed",
        AssetOrigin.PublicDomain => "publicDomain",
        _ => "undeclared",
    };

    /// <summary>Reads a written name (case-insensitive; <c>public-domain</c> and <c>public_domain</c> are accepted too).
    /// <c>undeclared</c> is not a declaration, so it isn't accepted.</summary>
    public static bool TryParse(string text, out AssetOrigin origin)
    {
        origin = text.Replace("-", "").Replace("_", "").ToLowerInvariant() switch
        {
            "ours" => AssetOrigin.Ours,
            "licensed" => AssetOrigin.Licensed,
            "publicdomain" => AssetOrigin.PublicDomain,
            _ => AssetOrigin.Undeclared,
        };
        return origin != AssetOrigin.Undeclared;
    }

    /// <summary>What's missing from a declaration, as errors: none declared; a licensed asset without its licence or
    /// source; a public-domain asset without its source.</summary>
    public static IReadOnlyList<Issue> Missing(Provenance provenance, string? objectId = null)
    {
        var issues = new List<Issue>();
        switch (provenance.Declared)
        {
            case AssetOrigin.Undeclared:
                issues.Add(Issue.Error("no origin is declared: say whether it's ours (made by this project), licensed (with the licence's name and where it came from) or public domain (with where it came from)", objectId, "$.provenance.declared"));
                break;
            case AssetOrigin.Licensed:
                if (string.IsNullOrWhiteSpace(provenance.Licence)) issues.Add(Issue.Error("a licensed asset needs its licence's name (an SPDX id such as CC-BY-4.0, or the licence's title)", objectId, "$.provenance.licence"));
                if (string.IsNullOrWhiteSpace(provenance.Origin)) issues.Add(Issue.Error("a licensed asset needs its source: where it came from (a URL, a shop, a person)", objectId, "$.provenance.origin"));
                break;
            case AssetOrigin.PublicDomain:
                if (string.IsNullOrWhiteSpace(provenance.Origin)) issues.Add(Issue.Error("a public-domain asset needs its source: where it came from, so it can be checked", objectId, "$.provenance.origin"));
                break;
            case AssetOrigin.Ours:
                break;
            default:   // a number that isn't one of the origins is refused exactly as DeclareAssetOrigin refuses it (found by A237)
                issues.Add(Issue.Error($"{(int)provenance.Declared} isn't an origin: declare ours, licensed or public domain", objectId, "$.provenance.declared"));
                break;
        }
        return issues;
    }

    /// <summary>
    /// Judges a declaration against the rights tags a file carries: declared <see cref="AssetOrigin.Ours"/> or
    /// <see cref="AssetOrigin.PublicDomain"/> while a tag claims rights (<see cref="RightsTags.IsClaim"/>) is an error that
    /// quotes exactly what was found. A <see cref="AssetOrigin.Licensed"/> declaration is never refused for its tags: they're
    /// kept beside it and shown. <paramref name="ourNames"/> are the project's own rights holders, whose names in a tag
    /// don't count against an asset declared ours. <paramref name="unread"/> says why the file's tags couldn't all be read
    /// (<see cref="RightsScan.Problem"/>): an incomplete scan can't show that nothing contradicts ours or public domain, so it
    /// refuses them too, and is only noted beside a licensed asset (found by A237).
    /// </summary>
    public static IReadOnlyList<Issue> Judge(Provenance provenance, IReadOnlyList<RightsTag> tags, IReadOnlyCollection<string>? ourNames = null, string? objectId = null,
        string? unread = null)
    {
        if (provenance.Declared is not (AssetOrigin.Ours or AssetOrigin.PublicDomain)) return [];
        var said = provenance.Declared == AssetOrigin.Ours ? "ours (made by this project)" : "public domain";
        var issues = new List<Issue>();
        if (unread is not null)
            issues.Add(Issue.Error($"declared {said}, but this scan couldn't read all of this file's rights tags: {unread}. "
                + "An unread tag could say otherwise, so it can't be accepted as " + (provenance.Declared == AssetOrigin.Ours ? "ours" : "public domain")
                + ". Use a copy that reads cleanly, or declare it licensed with the licence's name and where it came from.", objectId, "$.provenance.declared"));
        var claims = tags.Where(t => RightsTags.IsClaim(t, provenance.Declared == AssetOrigin.Ours ? ourNames : null, provenance.Declared == AssetOrigin.PublicDomain)).ToList();
        if (claims.Count == 0) return issues;
        issues.Add(Issue.Error($"declared {said}, but the file's own rights tags say otherwise: {string.Join("; ", claims)}. "
            + "If it's someone else's work, declare it licensed with the licence's name and where it came from; if it really is "
            + (provenance.Declared == AssetOrigin.Ours ? "the project's own, remove the tags, or add the holder to the project's own names (an adapter's OwnNames, or --own-name)" : "public domain, use a copy without those tags")
            + ".", objectId, "$.provenance.declared"));
        return issues;
    }
}

/// <summary>What an import did: the new record, or the issues that refused it, and what the file's rights tags said.</summary>
/// <param name="Record">The asset's new record, or null when the import was refused.</param>
/// <param name="Scan">The rights tags read from the file.</param>
/// <param name="Issues">Why it was refused (errors), or what's worth knowing (a file whose tags couldn't all be read).</param>
public sealed record AssetImportResult(AssetRecord? Record, RightsScan Scan, IReadOnlyList<Issue> Issues)
{
    /// <summary>True when the asset was imported.</summary>
    public bool Ok => Record is not null;
}

/// <summary>
/// The one way an asset enters a project with a declared origin: the editor, the command line and MCP all call
/// <see cref="Import"/>, so they refuse alike. No declaration, no import: <see cref="Provenance.Declared"/> must be ours,
/// licensed (licence and source given) or public domain (source given). The file's rights tags are read
/// (<see cref="RightsTags"/>); tags that claim rights refuse an asset declared ours or public domain, with exactly what
/// they say, and are kept on a licensed asset's record. Nothing is written when the import is refused.
/// </summary>
public static class AssetImport
{
    /// <summary>Imports <paramref name="assetPath"/>: judges the declaration and the file's tags, then writes the sidecar
    /// once, with a new GUID and the tags found. Refuses an asset that already has a sidecar (change its declaration with
    /// <see cref="DeclareAssetOrigin"/> instead).</summary>
    /// <param name="ourNames">The project's own rights holders (a studio's or person's name), whose name in a tag doesn't
    /// count against an asset declared ours (see <see cref="RightsTags.IsClaim"/>). Empty by default.</param>
    /// <param name="beforeWrite">A host's last word on the record about to be written: why it mustn't be, or null. A host
    /// passes its file policy here (a read-only project, a game's file check); a refusal writes nothing.</param>
    public static AssetImportResult Import(string assetPath, string importer, int importerVersion, JsonElement settings, Provenance provenance, AiUse aiUse,
        IReadOnlyCollection<string>? ourNames = null, Func<AssetRecord, string?>? beforeWrite = null)
    {
        var name = Path.GetFileName(assetPath);
        if (!File.Exists(assetPath)) return new(null, new(null, [], null), [Issue.Error($"there's no file at {assetPath}", path: name)]);
        if (File.Exists(AssetRecord.SidecarFor(assetPath)))
            return new(null, new(null, [], null), [Issue.Error($"{name} is already imported; change its declaration with the declare command instead", path: name)]);
        var scan = RightsTags.ReadFile(assetPath);
        var issues = new List<Issue>(AssetOrigins.Missing(provenance, name));
        issues.AddRange(AssetOrigins.Judge(provenance, scan.Tags, ourNames, name, scan.Problem));
        if (scan.Problem is { } problem && !issues.HasErrors()) issues.Add(Issue.Warning($"{name}'s rights tags couldn't all be read: {problem}. That's recorded with it.", name));
        if (issues.HasErrors()) return new(null, scan, issues);
        try
        {
            var record = AssetRecord.New(assetPath, importer, importerVersion, settings, provenance, aiUse, scan.Tags) with { RightsUnread = scan.Problem };
            if (beforeWrite?.Invoke(record) is { } no) return new(null, scan, [.. issues, Issue.Error(no, path: name)]);
            record.WriteNew(assetPath);
            return new(record, scan, issues);
        }
        catch (InvalidOperationException e) { return new(null, scan, [.. issues, Issue.Error(e.Message, path: name)]); }
    }

    /// <summary>A session over an asset's sidecar, so its declaration is changed only by <see cref="DeclareAssetOrigin"/>,
    /// with undo and an atomic save. A record whose declaration its own found tags contradict opens for repair.</summary>
    /// <param name="ourNames">The project's own rights holders (see <see cref="Import"/>).</param>
    public static DocumentSession<AssetRecord> Open(string assetPath, IReadOnlyCollection<string>? ourNames = null) =>
        DocumentSession<AssetRecord>.OpenForRepair(AssetRecord.SidecarFor(assetPath), new AssetRecordFormat(), new AssetRecordValidator(ourNames), Catalog());

    /// <summary>The commands an asset record supports.</summary>
    public static CommandCatalog<AssetRecord> Catalog() => new CommandCatalog<AssetRecord>()
        .Register<DeclareAssetOrigin>("asset.declare_origin", 1, "Declare asset origin");
}

/// <summary>Reads and writes <see cref="AssetRecord"/> sidecars as a document (kind <c>asset</c>), so a session can edit one.</summary>
public sealed class AssetRecordFormat : IDocumentFormat<AssetRecord>
{
    /// <inheritdoc />
    public string Kind => "asset";

    /// <inheritdoc />
    public int Version => AssetRecord.Version;

    /// <inheritdoc />
    public AssetRecord Read(string text) => AssetRecord.Read(text);

    /// <inheritdoc />
    public string Write(AssetRecord document) => document.Write();
}

/// <summary>Checks an asset record's declaration: a declared origin must have what it needs
/// (<see cref="AssetOrigins.Missing"/>), and the tags found at import mustn't contradict it (<see cref="AssetOrigins.Judge"/>).
/// An undeclared record (from before declarations) is a warning, so it still opens.</summary>
/// <param name="ourNames">The project's own rights holders.</param>
public sealed class AssetRecordValidator(IReadOnlyCollection<string>? ourNames = null) : IValidator<AssetRecord>
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(AssetRecord document)
    {
        if (document.Provenance.Declared == AssetOrigin.Undeclared)
            return [Issue.Warning($"{document.Source} has no declared origin; declare it ours, licensed or public domain", document.Guid, "$.provenance.declared")];
        return [.. AssetOrigins.Missing(document.Provenance, document.Guid), .. AssetOrigins.Judge(document.Provenance, document.RightsFound ?? [], ourNames, document.Guid, document.RightsUnread)];
    }
}

/// <summary>Declares (or changes) where an asset came from. Ours needs nothing more; licensed needs
/// <see cref="Licence"/> and <see cref="Source"/>; public domain needs <see cref="Source"/>. Blank fields keep the record's
/// current licence or source. Undeclared is refused. The record's validator then judges it against the tags found at
/// import, so a contradicted declaration is refused like any invalid edit.</summary>
/// <param name="Origin">The declared origin.</param>
/// <param name="Licence">The licence's name, or empty to keep the current one.</param>
/// <param name="Source">Where it came from, or empty to keep the current one.</param>
public sealed record DeclareAssetOrigin(AssetOrigin Origin, string Licence = "", string Source = "") : ICommand<AssetRecord>
{
    /// <inheritdoc />
    public CommandResult<AssetRecord> Apply(AssetRecord document)
    {
        if (Origin == AssetOrigin.Undeclared || !Enum.IsDefined(Origin))
            return CommandResult<AssetRecord>.Refused(Issue.Error("an origin can only be declared as ours, licensed or public domain, never taken back to undeclared", document.Guid, "$.provenance.declared"));
        var p = document.Provenance with
        {
            Declared = Origin,
            Licence = string.IsNullOrWhiteSpace(Licence) ? document.Provenance.Licence : Licence.Trim(),
            Origin = string.IsNullOrWhiteSpace(Source) ? document.Provenance.Origin : Source.Trim(),
        };
        var missing = AssetOrigins.Missing(p, document.Guid);
        return missing.Count > 0 ? new(null, missing) : CommandResult<AssetRecord>.Done(document with { Provenance = p });
    }
}
