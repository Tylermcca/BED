namespace Bed;

/// <summary>
/// Spots licences that can't be shipped together, so people find out before they distribute, not after. It knows the
/// common open-source licences and the well-known clashes between them, as the Free Software Foundation's licence list
/// describes them (BORROWED): GPL-2.0-only with Apache-2.0 or GPL-3.0, and the GPL family with closed-source code. It's
/// a warning, never a block, and not legal advice: every message ends by saying to check before distributing.
/// INVENTED: the families and the plain-words messages.
/// </summary>
public static class Licences
{
    /// <summary>How a licence behaves when code under it is combined with other code.</summary>
    public enum Family
    {
        /// <summary>Not a licence this checker knows; it can't say anything about it.</summary>
        Unknown,
        /// <summary>Permissive (MIT, BSD, Apache-2.0, Zlib, ISC, Unlicense, CC0, BSL-1.0): combines with almost anything.</summary>
        Permissive,
        /// <summary>Weak copyleft (MPL-2.0, LGPL): its own files stay open; combines with most code.</summary>
        WeakCopyleft,
        /// <summary>Strong copyleft (GPL, AGPL): the whole combined work must be under it when distributed.</summary>
        StrongCopyleft,
        /// <summary>Closed source: a <c>LicenseRef-</c> name, or <c>proprietary</c>.</summary>
        Proprietary,
    }

    private static readonly Dictionary<string, Family> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MIT"] = Family.Permissive, ["MIT-0"] = Family.Permissive, ["BSD-2-Clause"] = Family.Permissive,
        ["BSD-3-Clause"] = Family.Permissive, ["Apache-2.0"] = Family.Permissive, ["Zlib"] = Family.Permissive,
        ["ISC"] = Family.Permissive, ["Unlicense"] = Family.Permissive, ["CC0-1.0"] = Family.Permissive,
        ["BSL-1.0"] = Family.Permissive,
        ["MPL-2.0"] = Family.WeakCopyleft, ["LGPL-2.1-only"] = Family.WeakCopyleft, ["LGPL-2.1-or-later"] = Family.WeakCopyleft,
        ["LGPL-3.0-only"] = Family.WeakCopyleft, ["LGPL-3.0-or-later"] = Family.WeakCopyleft,
        ["GPL-2.0-only"] = Family.StrongCopyleft, ["GPL-2.0-or-later"] = Family.StrongCopyleft,
        ["GPL-3.0-only"] = Family.StrongCopyleft, ["GPL-3.0-or-later"] = Family.StrongCopyleft,
        ["AGPL-3.0-only"] = Family.StrongCopyleft, ["AGPL-3.0-or-later"] = Family.StrongCopyleft,
        ["proprietary"] = Family.Proprietary,
    };

    /// <summary>The family of one licence id (not an expression).</summary>
    public static Family Of(string id) => id.StartsWith("LicenseRef-", StringComparison.OrdinalIgnoreCase) ? Family.Proprietary
        : Known.TryGetValue(id, out var f) ? f : Family.Unknown;

    /// <summary>The choices an SPDX expression offers: each is a set of licence ids that all apply. <c>MIT OR Apache-2.0</c>
    /// is two choices; <c>MIT AND Zlib</c> is one choice of two licences. <c>WITH</c> exceptions are kept on their id
    /// and ignored for checking.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Choices(string expression) =>
        expression.Replace("(", " ").Replace(")", " ")
            .Split(" OR ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(choice => (IReadOnlyList<string>)choice.Split(" AND ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => id.Split(" WITH ")[0].Trim()).ToList())
            .ToList();

    /// <summary>Why licence ids <paramref name="a"/> and <paramref name="b"/> can't be shipped together, or null when they
    /// can, or when this checker doesn't know.</summary>
    public static string? Clash(string a, string b)
    {
        static bool Is(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        foreach (var (x, y) in new[] { (a, b), (b, a) })
        {
            if (Is(x, "GPL-2.0-only") && (Is(y, "Apache-2.0") || y.StartsWith("GPL-3.0", StringComparison.OrdinalIgnoreCase)
                || y.StartsWith("LGPL-3.0", StringComparison.OrdinalIgnoreCase) || y.StartsWith("AGPL-3.0", StringComparison.OrdinalIgnoreCase)))
                return $"{x} can't be combined with {y} (GPL version 2 only doesn't accept their terms)";
            if (Of(x) == Family.StrongCopyleft && Of(y) == Family.Proprietary)
                return $"{x} code requires the whole combination to be open under it, so it can't ship with closed-source {y} code";
        }
        return null;
    }

    /// <summary>Why two licence expressions can't be shipped together: null when at least one choice from each combines
    /// cleanly (or the checker can't tell), otherwise the first clash found.</summary>
    public static string? Clash(IReadOnlyList<IReadOnlyList<string>> a, IReadOnlyList<IReadOnlyList<string>> b)
    {
        string? first = null;
        foreach (var x in a)
            foreach (var y in b)
            {
                var why = x.SelectMany(i => y.Select(j => Clash(i, j))).FirstOrDefault(r => r is not null);
                if (why is null) return null;   // this pair of choices works
                first ??= why;
            }
        return first;
    }

    /// <summary>Licence ids in the expression this checker doesn't know.</summary>
    public static IEnumerable<string> Unknown(string expression) =>
        Choices(expression).SelectMany(c => c).Where(id => Of(id) == Family.Unknown).Distinct(StringComparer.OrdinalIgnoreCase);
}
