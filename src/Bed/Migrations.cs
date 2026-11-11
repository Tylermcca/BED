using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bed;

/// <summary>
/// Upgrades older files of a document type at load (worksheet AT-014). Each step turns version <c>n</c> into
/// <c>n + 1</c> as JSON, so a format only ever reads its current version. An older file is upgraded in memory; it is
/// written in the new version only when the person saves (a plain open never rewrites a file). A newer file than this
/// tool knows is refused with a clear message, never guessed at.
/// </summary>
public sealed class Migrations
{
    private readonly SortedDictionary<int, (string Why, Func<JsonObject, JsonObject> Step)> steps = [];

    /// <summary>The document kind these steps apply to.</summary>
    public string Kind { get; }

    /// <summary>The version the steps lead to.</summary>
    public int Current { get; }

    /// <summary>Steps for <paramref name="kind"/>, leading to version <paramref name="current"/>.</summary>
    public Migrations(string kind, int current)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(current, 1);
        Kind = kind;
        Current = current;
    }

    /// <summary>Adds the step from <paramref name="from"/> to <paramref name="from"/> + 1, with a sentence saying why.</summary>
    public Migrations Step(int from, string why, Func<JsonObject, JsonObject> step)
    {
        if (from < 1 || from >= Current) throw new ArgumentOutOfRangeException(nameof(from), $"steps run from 1 up to {Current - 1}");
        if (!steps.TryAdd(from, (why, step))) throw new ArgumentException($"there is already a step from version {from}", nameof(from));
        return this;
    }

    /// <summary>
    /// The text upgraded to the current version, and the steps applied (empty when it was already current). Throws
    /// <see cref="DocumentFormatException"/> for another kind, a version newer than this tool, or a missing step.
    /// </summary>
    public (string Text, IReadOnlyList<string> Applied) Upgrade(string text)
    {
        JsonObject root;
        try { root = JsonNode.Parse(text) as JsonObject ?? throw new DocumentFormatException("$", "expected an object"); }
        catch (JsonException e) { throw new DocumentFormatException("$", "not valid JSON: " + e.Message); }
        if (root["kind"]?.GetValueKind() != JsonValueKind.String || root["kind"]!.GetValue<string>() != Kind)
            throw new DocumentFormatException("$.kind", $"expected \"{Kind}\"");
        if (root["version"] is not JsonValue v || !v.TryGetValue<int>(out var version))
            throw new DocumentFormatException("$.version", "expected a whole number");
        if (version > Current)
            throw new DocumentFormatException("$.version", $"this file is version {version}, made by a newer tool; this one reads up to version {Current}. Update the tool to open it");
        var applied = new List<string>();
        for (var n = version; n < Current; n++)
        {
            if (!steps.TryGetValue(n, out var s))
                throw new DocumentFormatException("$.version", $"no upgrade from version {n} to {n + 1}; this file can't be read by this tool");
            root = s.Step(root);
            root["version"] = n + 1;
            applied.Add($"{n} → {n + 1}: {s.Why}");
        }
        return (applied.Count == 0 ? text : root.ToJsonString(), applied);
    }
}

/// <summary>A format that may upgrade what it reads. A session opened from an upgraded file starts dirty, because saving
/// would write the new version.</summary>
public interface IUpgradingFormat
{
    /// <summary>The upgrade steps the last read applied (empty when the file was current).</summary>
    IReadOnlyList<string> LastApplied { get; }
}

/// <summary>
/// A format that upgrades older files on read: <see cref="IDocumentFormat{TDoc}.Read"/> runs the migrations, then the
/// current format. <see cref="LastApplied"/> tells the host what was upgraded, so it can say so (and the session is then
/// dirty, because saving would write the new version).
/// </summary>
public sealed class MigratingFormat<TDoc>(IDocumentFormat<TDoc> current, Migrations migrations) : IDocumentFormat<TDoc>, IUpgradingFormat where TDoc : class
{
    /// <inheritdoc />
    public string Kind => current.Kind;

    /// <inheritdoc />
    public int Version => current.Version;

    /// <summary>The upgrade steps the last read applied (empty when the file was current).</summary>
    public IReadOnlyList<string> LastApplied { get; private set; } = [];

    /// <inheritdoc />
    public TDoc Read(string text)
    {
        var (upgraded, applied) = migrations.Upgrade(text);
        LastApplied = applied;
        return current.Read(upgraded);
    }

    /// <inheritdoc />
    public string Write(TDoc document) => current.Write(document);
}
