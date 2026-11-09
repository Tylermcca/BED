using System.Text;
using System.Text.Json;

namespace Brutal.Authoring.Editor;

/// <summary>Modifier keys, combinable.</summary>
[Flags]
public enum Mods
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary>Control.</summary>
    Ctrl = 1,
    /// <summary>Shift.</summary>
    Shift = 2,
    /// <summary>Alt.</summary>
    Alt = 4,
}

/// <summary>A key with its modifiers, written like <c>Ctrl+Shift+Z</c>.</summary>
/// <param name="Mods">The modifiers.</param>
/// <param name="Key">The key's name (a letter, a digit, F1-F12, or a named key such as Space, Escape, Delete, Enter, Tab).</param>
public readonly record struct Chord(Mods Mods, string Key)
{
    private static readonly string[] Named = ["Space", "Escape", "Delete", "Enter", "Tab", "Backspace", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "Insert"];

    /// <summary>Reads <c>Ctrl+Shift+Z</c> (modifiers in any order, case-insensitive). Throws <see cref="FormatException"/>
    /// with a plain message when it can't.</summary>
    public static Chord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => p.Length == 0)) throw new FormatException($"'{text}' isn't a shortcut; write it like Ctrl+Z");
        var mods = Mods.None;
        foreach (var m in parts[..^1])
        {
            var mod = m.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Mods.Ctrl,
                "shift" => Mods.Shift,
                "alt" => Mods.Alt,
                _ => throw new FormatException($"'{m}' in '{text}' isn't a modifier; use Ctrl, Shift or Alt"),
            };
            if (mods.HasFlag(mod)) throw new FormatException($"'{text}' names {mod} twice");
            mods |= mod;
        }
        return new Chord(mods, KeyName(parts[^1], text));
    }

    private static string KeyName(string k, string text)
    {
        if (k.Length == 1 && char.IsLetterOrDigit(k[0])) return k.ToUpperInvariant();
        if (k.Length is 2 or 3 && (k[0] is 'F' or 'f') && int.TryParse(k[1..], out var f) && f is >= 1 and <= 12) return "F" + f;
        var named = Named.FirstOrDefault(n => n.Equals(k, StringComparison.OrdinalIgnoreCase));
        return named ?? throw new FormatException($"'{k}' in '{text}' isn't a key this editor knows (letters, digits, F1-F12, or {string.Join(", ", Named)})");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Mods.HasFlag(Mods.Ctrl)) sb.Append("Ctrl+");
        if (Mods.HasFlag(Mods.Shift)) sb.Append("Shift+");
        if (Mods.HasFlag(Mods.Alt)) sb.Append("Alt+");
        return sb.Append(Key).ToString();
    }
}

/// <summary>
/// Keyboard shortcuts for editor actions and document commands, by id. Defaults come from the editor and from games or
/// plug-ins; a person's own changes are kept in a per-user settings file, never in a document. Every action stays
/// reachable through the palette whether or not it has a shortcut (goal 12, keyboard).
/// </summary>
public sealed class Shortcuts
{
    /// <summary>The editor's own actions, with their default shortcuts.</summary>
    public static readonly IReadOnlyDictionary<string, string> EditorDefaults = new Dictionary<string, string>
    {
        ["editor.undo"] = "Ctrl+Z",
        ["editor.redo"] = "Ctrl+Y",
        ["editor.save"] = "Ctrl+S",
        ["editor.palette"] = "Ctrl+Shift+P",
        ["editor.focus_out"] = "Escape",
        ["editor.focus_in"] = "F",
        ["editor.panel.bed.keys"] = "F1",
        ["editor.panel_do.editor.compare/next"] = "Alt+F5",
        ["editor.panel_do.editor.compare/previous"] = "Shift+Alt+F5",
    };

    private readonly Dictionary<string, Chord> defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Chord?> overrides = new(StringComparer.Ordinal);   // null: the person removed it

    /// <summary>Shortcuts starting from the editor's defaults.</summary>
    public Shortcuts()
    {
        foreach (var (id, chord) in EditorDefaults) defaults[id] = Chord.Parse(chord);
    }

    /// <summary>Adds a default shortcut (from a game, plug-in or workspace). The person's own choice still wins.</summary>
    public Shortcuts Default(string id, string chord)
    {
        defaults[id] = Chord.Parse(chord);
        return this;
    }

    /// <summary>The person's own shortcut for <paramref name="id"/>; null removes it.</summary>
    public void Set(string id, string? chord) => overrides[id] = chord is null ? null : Chord.Parse(chord);

    /// <summary>Forgets the person's choice for <paramref name="id"/>, back to the default.</summary>
    public void ResetToDefault(string id) => overrides.Remove(id);

    /// <summary>The shortcut in effect for <paramref name="id"/>, or null.</summary>
    public Chord? For(string id) => overrides.TryGetValue(id, out var o) ? o : defaults.TryGetValue(id, out var d) ? d : null;

    /// <summary>Every shortcut in effect, by id.</summary>
    public IReadOnlyDictionary<string, Chord> InEffect() => defaults.Keys.Concat(overrides.Keys).Distinct(StringComparer.Ordinal)
        .Select(id => (id, chord: For(id))).Where(x => x.chord is not null).ToDictionary(x => x.id, x => x.chord!.Value, StringComparer.Ordinal);

    /// <summary>The action a key press triggers, or null. When a chord is bound twice, it triggers nothing until the clash
    /// is fixed (see <see cref="Clashes"/>).</summary>
    public string? Action(Chord pressed)
    {
        // A key press needs only a unique match, not a dictionary snapshot of every binding.
        string? match = null;
        foreach (var id in defaults.Keys)
        {
            if (For(id) != pressed) continue;
            if (match is not null) return null;
            match = id;
        }
        foreach (var (id, chord) in overrides)
        {
            if (defaults.ContainsKey(id) || chord != pressed) continue;
            if (match is not null) return null;
            match = id;
        }
        return match;
    }

    /// <summary>Chords bound to more than one action, as issues naming them all.</summary>
    public IReadOnlyList<Issue> Clashes() => InEffect().GroupBy(kv => kv.Value).Where(g => g.Count() > 1)
        .Select(g => Issue.Warning($"{g.Key} is bound to {string.Join(" and ", g.Select(kv => kv.Key).Order(StringComparer.Ordinal))}; it does nothing until one is changed"))
        .ToList();

    /// <summary>The person's own changes, as canonical text for their settings file.</summary>
    public string WriteOverrides()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Canonical.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("kind"u8, "editor.shortcuts"u8);
            w.WriteNumber("version"u8, 1);
            w.WriteStartObject("shortcuts"u8);
            foreach (var (id, chord) in overrides.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (chord is { } c) w.WriteString(id, c.ToString()); else w.WriteNull(id);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Loads the person's own changes. A shortcut that can't be read is skipped and reported, never fatal: a bad
    /// settings line shouldn't stop the editor opening.</summary>
    public IReadOnlyList<Issue> ReadOverrides(string text)
    {
        var issues = new List<Issue>();
        var r = StrictReader.Parse(text);
        r.Only("kind", "version", "shortcuts");
        if (r.String("kind") != "editor.shortcuts") throw new DocumentFormatException("$.kind", "expected \"editor.shortcuts\"");
        if (r.Int("version") != 1) throw new DocumentFormatException("$.version", "expected version 1");
        foreach (var p in r.Element("shortcuts").EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Null) { overrides[p.Name] = null; continue; }
            try { overrides[p.Name] = Chord.Parse(p.Value.GetString() ?? ""); }
            catch (Exception e) when (e is FormatException or InvalidOperationException)
            {
                issues.Add(Issue.Warning($"shortcut for '{p.Name}' skipped: {e.Message}", path: "$.shortcuts." + p.Name));
            }
        }
        return issues;
    }
}

