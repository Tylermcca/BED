namespace Bed;

/// <summary>
/// Named variants of one asset (worksheet AT-114): intact, damaged and destroyed states; colour sets; levels of detail.
/// Each variant names an asset by GUID. One variant is the default. A variant whose asset can't be found is a warning,
/// not a failure: the default is shown in its place, and the warning names the variant.
/// </summary>
/// <param name="Id">The set's own id, for messages (often the object type or base asset's name).</param>
/// <param name="Default">The variant shown unless another is chosen.</param>
/// <param name="Variants">Variant name to asset GUID, in display order.</param>
public sealed record VariantSet(string Id, string Default, IReadOnlyList<(string Name, string Asset)> Variants)
{
    /// <summary>The variant names, in display order.</summary>
    public IEnumerable<string> Names => Variants.Select(v => v.Name);

    /// <summary>Problems with the set: no variants, a default that isn't one of them, a name used twice, and (given an
    /// index) variants whose asset can't be found.</summary>
    public IReadOnlyList<Issue> Validate(AssetIndex? assets = null)
    {
        var issues = new List<Issue>();
        if (Variants.Count == 0) return [Issue.Error($"'{Id}' has no variants; add at least one", Id)];
        foreach (var dup in Variants.GroupBy(v => v.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add(Issue.Error($"'{Id}' names the variant '{dup.Key}' twice", Id));
        if (!Variants.Any(v => v.Name == Default))
            issues.Add(Issue.Error($"'{Id}' defaults to '{Default}', which isn't one of its variants ({string.Join(", ", Names)})", Id));
        if (assets is not null)
            foreach (var (name, asset) in Variants)
                if (assets.Resolve(asset) is null)
                    issues.Add(Issue.Warning($"'{Id}' variant '{name}' uses asset {asset}, which can't be found; the default is shown instead", Id));
        return issues;
    }

    /// <summary>The asset to show for <paramref name="variant"/>: its own when it exists and can be found, otherwise the
    /// default's. <paramref name="fellBack"/> says when the default stood in. Choosing a variant to preview never edits
    /// the document.</summary>
    public string AssetFor(string variant, AssetIndex? assets, out bool fellBack)
    {
        var chosen = Variants.FirstOrDefault(v => v.Name == variant).Asset;
        if (chosen is not null && (assets is null || assets.Resolve(chosen) is not null)) { fellBack = false; return chosen; }
        fellBack = true;
        return Variants.FirstOrDefault(v => v.Name == Default).Asset ?? Variants[0].Asset;
    }
}
