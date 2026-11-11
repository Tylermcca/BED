using Bed.Editor;
using Xunit;

namespace Bed.Tests;

// BORROWED: InEffect is the existing public snapshot contract; Action must select its one unique match.
public sealed class ShortcutActionLookupTests
{
    private static string? FromSnapshot(Shortcuts shortcuts, Chord chord)
    {
        var ids = shortcuts.InEffect().Where(pair => pair.Value == chord).Select(pair => pair.Key).ToArray();
        return ids.Length == 1 ? ids[0] : null;
    }

    [Fact]
    public void Lookup_tracks_defaults_overrides_removal_and_reset_without_stale_state()
    {
        var shortcuts = new Shortcuts().Default("sample", "M");
        var chords = new[] { Chord.Parse("M"), Chord.Parse("N"), Chord.Parse("Ctrl+S"), default(Chord) };
        void Same() { foreach (var chord in chords) Assert.Equal(FromSnapshot(shortcuts, chord), shortcuts.Action(chord)); }
        Same();
        shortcuts.Set("sample", "N"); Same();
        shortcuts.Default("sample", "Ctrl+S"); Same(); // override still wins
        shortcuts.Set("sample", null); Same();
        shortcuts.ResetToDefault("sample"); Same(); // clash with save
        Assert.Null(shortcuts.Action(Chord.Parse("Ctrl+S")));
        shortcuts.ResetToDefault("absent"); Same();
    }

    [Fact]
    public void Override_only_ids_and_default_ids_are_counted_once_and_clashes_still_refuse()
    {
        var shortcuts = new Shortcuts().Default("first", "M");
        shortcuts.Set("first", "M"); // the same id must not be counted twice
        Assert.Equal("first", shortcuts.Action(Chord.Parse("M")));
        shortcuts.Set("second", "M");
        Assert.Null(shortcuts.Action(Chord.Parse("M")));
        Assert.Contains("first and second", Assert.Single(shortcuts.Clashes()).Message);
        shortcuts.Set("first", null);
        Assert.Equal("second", shortcuts.Action(Chord.Parse("M")));
        shortcuts.ResetToDefault("second");
        Assert.Null(shortcuts.Action(Chord.Parse("M")));
        shortcuts.Set("", "M"); // empty id is still a non-null unique action
        Assert.Equal("", shortcuts.Action(Chord.Parse("M")));
    }

    [Fact]
    public void Reading_overrides_keeps_valid_entries_and_does_not_replace_an_invalid_entry()
    {
        var shortcuts = new Shortcuts().Default("sample", "M");
        var issues = shortcuts.ReadOverrides("""{"kind":"editor.shortcuts","version":1,"shortcuts":{"sample":"Hyper+M","new":"N","editor.save":null}}""");
        Assert.Single(issues);
        Assert.Equal("sample", shortcuts.Action(Chord.Parse("M")));
        Assert.Equal("new", shortcuts.Action(Chord.Parse("N")));
        Assert.Null(shortcuts.Action(Chord.Parse("Ctrl+S")));
        Assert.Equal(FromSnapshot(shortcuts, Chord.Parse("N")), shortcuts.Action(Chord.Parse("N")));
    }

    [Fact]
    public void Lookup_keeps_exact_chord_equality_and_does_not_change_snapshot_or_serialization()
    {
        var shortcuts = new Shortcuts().Default("sample", "M");
        var snapshot = shortcuts.InEffect().ToArray();
        var written = shortcuts.WriteOverrides();
        Assert.Null(shortcuts.Action(new Chord(Mods.None, "m"))); // Action does not parse/canonicalize its argument
        Assert.Equal("sample", shortcuts.Action(Chord.Parse("m")));
        Assert.Equal(snapshot, shortcuts.InEffect().ToArray());
        Assert.Equal(written, shortcuts.WriteOverrides());
    }

    [Fact]
    public void Warm_unique_lookup_needs_no_effective_dictionary_or_match_list_allocation()
    {
        var shortcuts = new Shortcuts();
        shortcuts.Set("new", "M");
        var chord = Chord.Parse("M");
        for (var i = 0; i < 16; i++) _ = shortcuts.Action(chord);
        var found = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 32; i++) if (shortcuts.Action(chord) == "new") found++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(32, found);
        Assert.Equal(0L, allocated);
    }
}
