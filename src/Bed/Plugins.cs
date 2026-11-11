using System.Text.RegularExpressions;

namespace Bed;

/// <summary>A version as <c>major.minor.patch</c> (semantic versioning, without pre-release or build parts).</summary>
public readonly record struct ApiVersion(int Major, int Minor, int Patch) : IComparable<ApiVersion>
{
    /// <summary>Reads <c>1</c>, <c>1.2</c> or <c>1.2.3</c> (missing parts are 0). Throws <see cref="FormatException"/>
    /// saying why otherwise.</summary>
    public static ApiVersion Parse(string text)
    {
        var parts = text.Split('.');
        if (parts.Length is < 1 or > 3 || parts.Any(p => p.Length == 0 || p.Any(c => c is < '0' or > '9') || p.Length > 1 && p[0] == '0'))
            throw new FormatException($"'{text}' isn't a version; write it like 1.2.3");
        var n = parts.Select(int.Parse).Concat([0, 0]).ToArray();
        return new(n[0], n[1], n[2]);
    }

    /// <inheritdoc />
    public int CompareTo(ApiVersion other) => (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

    /// <summary>Whether code built against <paramref name="required"/> runs on this version: the same major version and
    /// at least the required one. Before 1.0 a minor version may break things, so the minor must match too.</summary>
    public bool Satisfies(ApiVersion required) =>
        Major == required.Major && (Major > 0 ? CompareTo(required) >= 0 : Minor == required.Minor && Patch >= required.Patch);

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>Newer than.</summary>
    public static bool operator >(ApiVersion a, ApiVersion b) => a.CompareTo(b) > 0;

    /// <summary>Older than.</summary>
    public static bool operator <(ApiVersion a, ApiVersion b) => a.CompareTo(b) < 0;

    /// <summary>Not older than.</summary>
    public static bool operator >=(ApiVersion a, ApiVersion b) => a.CompareTo(b) >= 0;

    /// <summary>Not newer than.</summary>
    public static bool operator <=(ApiVersion a, ApiVersion b) => a.CompareTo(b) <= 0;
}

/// <summary>One thing a plug-in asks to do beyond editing documents through commands, and why (BORROWED from Blender's
/// extension permissions). It's a statement for people to read before they enable it, not a sandbox: plug-ins run
/// in-process and are trusted.</summary>
/// <param name="Name">One of <see cref="PluginManifest.KnownPermissions"/>.</param>
/// <param name="Reason">A plain sentence saying why.</param>
public sealed record PluginPermission(string Name, string Reason);

/// <summary>
/// A plug-in's manifest (<c>plugin.json</c>, FUT-002): who it is, which library API it needs, its licence and what it
/// asks to do. It's read and checked before any of the plug-in's code runs. MIXED: the fields are BORROWED from
/// Blender's <c>blender_manifest.toml</c> (id, version, minimum version, SPDX licence, permissions with reasons) and
/// Godot's <c>compatibility_minimum</c>; the id prefix rule is INVENTED, so plug-ins can't collide.
/// </summary>
/// <param name="Id">Lowercase and dotted-safe (<c>lights</c>, <c>acme_terrain</c>). Everything the plug-in registers is
/// named <c>&lt;id&gt;.something</c>.</param>
/// <param name="Name">What people see.</param>
/// <param name="Version">The plug-in's own version.</param>
/// <param name="Api">The oldest library API version it works with (<see cref="AuthoringInfo.ApiVersion"/>).</param>
/// <param name="License">Optional: an SPDX licence expression, such as <c>MIT</c> or <c>Apache-2.0 OR MIT</c>, or null when
/// none is given. It's information for the people using the plug-in, not a gate: a missing one is noted, and licences
/// that can't ship together are warned about (<see cref="Licences"/>), but neither stops the plug-in.</param>
/// <param name="Permissions">What it asks to do beyond commands, each with a reason. Empty for most plug-ins.</param>
/// <param name="Description">One or two plain sentences.</param>
public sealed partial record PluginManifest(string Id, string Name, ApiVersion Version, ApiVersion Api, string? License,
    IReadOnlyList<PluginPermission> Permissions, string Description = "")
{
    /// <summary>The permissions a plug-in may ask for.</summary>
    public static readonly IReadOnlyList<string> KnownPermissions = ["files.read", "files.write", "network", "process", "clipboard"];

    [GeneratedRegex("^[a-z][a-z0-9_]{1,39}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\(*[A-Za-z0-9.+-]+\)*( +(AND|OR|WITH) +\(*[A-Za-z0-9.+-]+\)*)*$")]
    private static partial Regex SpdxPattern();

    /// <summary>Reads a manifest strictly: unknown or missing fields, a bad id, version, licence expression or
    /// permission, or a permission without a reason, is a <see cref="DocumentFormatException"/> naming its path.</summary>
    public static PluginManifest Read(string text)
    {
        var r = StrictReader.Parse(text);
        r.Only("id", "name", "version", "api", "license", "permissions", "description");
        var id = r.String("id");
        if (!IdPattern().IsMatch(id))
            throw new DocumentFormatException("$.id", "an id is 2 to 40 lowercase letters, digits or underscores, starting with a letter");
        var name = r.String("name");
        if (name.Trim().Length == 0) throw new DocumentFormatException("$.name", "give it a name people will see");
        var license = r.Has("license") ? r.String("license") : null;
        if (license is not null && (!SpdxPattern().IsMatch(license) || !Balanced(license)))
            throw new DocumentFormatException("$.license", $"'{license}' isn't an SPDX licence expression (for example MIT, or Apache-2.0 OR MIT)");
        var permissions = new List<PluginPermission>();
        if (r.Has("permissions"))
            foreach (var p in r.Array("permissions"))
            {
                p.Only("name", "reason");
                var pn = p.String("name");
                if (!KnownPermissions.Contains(pn))
                    throw new DocumentFormatException(p.Path + ".name", $"unknown permission '{pn}'; known: {string.Join(", ", KnownPermissions)}");
                if (permissions.Any(x => x.Name == pn)) throw new DocumentFormatException(p.Path + ".name", $"'{pn}' is asked for twice");
                var reason = p.String("reason");
                if (reason.Trim().Length == 0) throw new DocumentFormatException(p.Path + ".reason", $"say why it needs '{pn}'");
                permissions.Add(new(pn, reason));
            }
        return new(id, name, ReadVersion(r, "version"), ReadVersion(r, "api"), license, permissions, r.Has("description") ? r.String("description") : "");
    }

    // Brackets open before they close and all close: "(MIT OR Apache-2.0" isn't an expression.
    private static bool Balanced(string text)
    {
        var depth = 0;
        foreach (var ch in text)
            if ((depth += ch == '(' ? 1 : ch == ')' ? -1 : 0) < 0) return false;
        return depth == 0;
    }

    private static ApiVersion ReadVersion(StrictReader r, string name)
    {
        try { return ApiVersion.Parse(r.String(name)); }
        catch (FormatException e) { throw new DocumentFormatException("$." + name, e.Message); }
    }

    /// <summary>Why this plug-in can't run on a library at <paramref name="host"/>, or nothing when it can.</summary>
    public IReadOnlyList<Issue> Check(ApiVersion host) => host.Satisfies(Api) ? []
        : [Issue.Error(Api > host
            ? $"'{Name}' ({Id} {Version}) needs authoring API {Api}, newer than this editor's {host}; update the editor or use an older {Name}"
            : $"'{Name}' ({Id} {Version}) was built for authoring API {Api}, which this editor ({host}) no longer supports; update {Name}", Id)];

    /// <summary>Whether <paramref name="name"/> (a command, object type, workspace or shortcut id) belongs to this plug-in.</summary>
    public bool Owns(string name) => name.StartsWith(Id + ".", StringComparison.Ordinal) && name.Length > Id.Length + 1;
}
