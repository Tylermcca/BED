namespace Bed;

/// <summary>What a link points at.</summary>
public enum LinkTarget
{
    /// <summary>Another object in the same document.</summary>
    Object,
    /// <summary>An asset, by its GUID (<see cref="AssetRecord"/>).</summary>
    Asset,
    /// <summary>An id in a named outside list (a rule registry, a table of tile kinds).</summary>
    External,
}

/// <summary>What happens to links when the object they point at is deleted.</summary>
public enum OnDelete
{
    /// <summary>The delete is refused while the link exists.</summary>
    Refuse,
    /// <summary>The linking object is deleted too.</summary>
    Cascade,
    /// <summary>The link is cleared.</summary>
    Clear,
}

/// <summary>One kind of link a document type has (for example "supports", "uses mesh", "follows rule").</summary>
/// <param name="Name">The kind's name, used in messages.</param>
/// <param name="Target">What it points at.</param>
/// <param name="Registry">For <see cref="LinkTarget.External"/>: the outside list's name.</param>
/// <param name="AllowCycles">Whether objects may link round in a circle through this kind.</param>
/// <param name="OnDelete">What deleting the target does.</param>
public sealed record LinkKind(string Name, LinkTarget Target, string? Registry = null, bool AllowCycles = true, OnDelete OnDelete = OnDelete.Refuse);

/// <summary>One link in a document: from an object's field to a target id.</summary>
/// <param name="Kind">Its kind.</param>
/// <param name="From">The object holding the link.</param>
/// <param name="Field">The field or JSON path holding it, for messages.</param>
/// <param name="To">The target's id (an object id, an asset GUID or an outside id).</param>
public sealed record Link(LinkKind Kind, string From, string Field, string To);

/// <summary>
/// A document type's links (worksheet AT-113): the general form of a support graph, a gate pair, a station's field, a
/// mesh reference. A game or plug-in describes where its links are; the core checks them, finds what depends on an
/// object, and plans id remapping for duplicates. The core never knows what the links mean.
/// </summary>
public interface ILinkSchema<TDoc> where TDoc : class
{
    /// <summary>Every object id in the document.</summary>
    IEnumerable<string> ObjectIds(TDoc document);

    /// <summary>Every link in the document.</summary>
    IEnumerable<Link> Links(TDoc document);
}

/// <summary>Checks a document's links: every target exists, and kinds that forbid cycles have none.</summary>
/// <param name="schema">Where the links are.</param>
/// <param name="assets">Resolves asset GUIDs, when asset links are checked.</param>
/// <param name="registries">The outside lists, by name.</param>
public sealed class LinkValidator<TDoc>(ILinkSchema<TDoc> schema, AssetIndex? assets = null, IReadOnlyDictionary<string, IReadOnlySet<string>>? registries = null)
    : IValidator<TDoc> where TDoc : class
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(TDoc document)
    {
        var issues = new List<Issue>();
        var objects = schema.ObjectIds(document).ToHashSet(StringComparer.Ordinal);
        var links = schema.Links(document).ToList();
        foreach (var l in links)
        {
            switch (l.Kind.Target)
            {
                case LinkTarget.Object when !objects.Contains(l.To):
                    issues.Add(Issue.Error($"'{l.From}' {l.Kind.Name} '{l.To}', which isn't in the document; pick an existing object or remove the link", l.From, l.Field));
                    break;
                case LinkTarget.Asset when assets is not null && assets.Resolve(l.To) is null:
                    issues.Add(Issue.Error($"'{l.From}' {l.Kind.Name} asset {l.To}, which can't be found; restore it or pick another", l.From, l.Field));
                    break;
                case LinkTarget.External when l.Kind.Registry is { } name:
                    if (registries is null || !registries.TryGetValue(name, out var ids))
                        issues.Add(Issue.Error($"'{l.From}' {l.Kind.Name} '{l.To}' in '{name}', but that list isn't loaded", l.From, l.Field));
                    else if (!ids.Contains(l.To))
                        issues.Add(Issue.Error($"'{l.From}' {l.Kind.Name} '{l.To}', which '{name}' doesn't have", l.From, l.Field));
                    break;
            }
        }
        foreach (var kind in links.Select(l => l.Kind).Where(k => k.Target == LinkTarget.Object && !k.AllowCycles).Distinct())
            if (Cycle(links.Where(l => l.Kind == kind && objects.Contains(l.To)).ToList()) is { } cycle)
                issues.Add(Issue.Error($"{kind.Name} goes round in a circle: {string.Join(" → ", cycle)}; break one of those links", cycle[0]));
        return issues;
    }

    // The first cycle found (its ids, the first repeated at the end), or null.
    private static List<string>? Cycle(List<Link> edges)
    {
        var next = edges.GroupBy(e => e.From, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList(), StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 visiting, 2 done
        var stack = new List<string>();
        List<string>? Visit(string n)
        {
            state[n] = 1;
            stack.Add(n);
            foreach (var m in next.GetValueOrDefault(n) ?? [])
            {
                if (state.GetValueOrDefault(m) == 1) return [.. stack[stack.IndexOf(m)..], m];
                if (state.GetValueOrDefault(m) == 0 && Visit(m) is { } c) return c;
            }
            stack.RemoveAt(stack.Count - 1);
            state[n] = 2;
            return null;
        }
        foreach (var n in next.Keys.Order(StringComparer.Ordinal))
            if (state.GetValueOrDefault(n) == 0 && Visit(n) is { } c) return c;
        return null;
    }
}

/// <summary>Helpers for commands that delete or duplicate objects with links.</summary>
public static class LinkPlans
{
    /// <summary>Links from other objects pointing at <paramref name="objectId"/>, grouped by what deleting it would do.</summary>
    public static ILookup<OnDelete, Link> Dependents<TDoc>(ILinkSchema<TDoc> schema, TDoc document, string objectId) where TDoc : class =>
        schema.Links(document).Where(l => l.Kind.Target == LinkTarget.Object && l.To == objectId && l.From != objectId).ToLookup(l => l.Kind.OnDelete);

    /// <summary>
    /// What deleting <paramref name="objectId"/> means: refused (with the links that block it), or the full set of objects
    /// to delete once cascades are followed, plus the links to clear.
    /// </summary>
    public static (IReadOnlyList<Link> Blocking, IReadOnlySet<string> Delete, IReadOnlyList<Link> Clear) PlanDelete<TDoc>(ILinkSchema<TDoc> schema, TDoc document, string objectId) where TDoc : class
    {
        var delete = new HashSet<string>(StringComparer.Ordinal) { objectId };
        var queue = new Queue<string>([objectId]);
        var links = schema.Links(document).Where(l => l.Kind.Target == LinkTarget.Object).ToList();
        while (queue.TryDequeue(out var id))
            foreach (var l in links.Where(l => l.To == id && l.Kind.OnDelete == OnDelete.Cascade && delete.Add(l.From)))
                queue.Enqueue(l.From);
        var outside = links.Where(l => delete.Contains(l.To) && !delete.Contains(l.From)).ToList();
        return (outside.Where(l => l.Kind.OnDelete == OnDelete.Refuse).ToList(), delete, outside.Where(l => l.Kind.OnDelete == OnDelete.Clear).ToList());
    }

    /// <summary>
    /// The id map for duplicating <paramref name="group"/> (AT-101): each duplicated object gets a new id that's unused
    /// (<paramref name="propose"/> suggests one per attempt), links inside the group are remapped to the copies, and links
    /// leaving the group keep their targets.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PlanDuplicate<TDoc>(ILinkSchema<TDoc> schema, TDoc document, IEnumerable<string> group,
        Func<string, int, string> propose) where TDoc : class
    {
        var used = schema.ObjectIds(document).ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in group.Distinct(StringComparer.Ordinal))
        {
            if (!used.Contains(id)) throw new ArgumentException($"'{id}' isn't in the document", nameof(group));
            var attempt = 1;
            string fresh;
            while (!used.Add(fresh = propose(id, attempt))) attempt++;
            map[id] = fresh;
        }
        return map;
    }

    /// <summary>A link's target in a duplicate made with <paramref name="map"/>: remapped when it points inside the group.</summary>
    public static string TargetInCopy(Link link, IReadOnlyDictionary<string, string> map) =>
        link.Kind.Target == LinkTarget.Object && map.TryGetValue(link.To, out var copy) ? copy : link.To;
}

/// <summary>Combines validators: a document type's own rules plus its link checks, and any a plug-in adds.</summary>
public sealed class AllOf<TDoc>(params IValidator<TDoc>[] validators) : IValidator<TDoc> where TDoc : class
{
    /// <inheritdoc />
    public IReadOnlyList<Issue> Validate(TDoc document) => validators.SelectMany(v => v.Validate(document)).ToList();
}
