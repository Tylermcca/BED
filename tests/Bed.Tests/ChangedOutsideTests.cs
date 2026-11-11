using Bed.Editor;
using Xunit;
namespace Bed.Tests;

// A tester's AI assistant added to level.json while BED had it open; Play then refused until the project was closed and reopened.
// An open file another program changes now loads by itself when BED has no unsaved edits to it, and otherwise asks once.
public sealed class ChangedOutsideTests
{
    // The level as another program would save it: the rig's level with one edit. BED's copy is left clean and nothing is written.
    private static string Theirs(Scene2HostCommandTests.Rig r)
    {
        var session = r.Entry.Session;
        Assert.True(r.Host.CommitScene2Edit("level.json", r.Edit(), out var why), why);
        var theirs = session.TextToSave;
        Assert.True(session.Undo()); Assert.False(session.Dirty);
        return theirs;
    }
    // An unsaved edit in BED; the other program saves the level with only its spacing changed, so the two differ.
    private static string Mine(Scene2HostCommandTests.Rig r)
    {
        Assert.True(r.Host.CommitScene2Edit("level.json", r.Edit(), out var why), why); Assert.True(r.Entry.Session.Dirty);
        return r.Entry.Session.TextToSave;
    }

    [Fact]
    public void A_clean_open_level_loads_the_version_saved_outside_by_itself()
    {
        using var r = new Scene2HostCommandTests.Rig();
        Assert.Empty(r.Host.ReloadChangedOutside());   // nothing changed yet
        var theirs = Theirs(r); File.WriteAllText(r.Disk, theirs);
        Assert.Equal(["level.json"], r.Host.ReloadChangedOutside());
        var session = r.Entry.Session;
        Assert.Equal(theirs, session.TextToSave); Assert.False(session.Dirty); Assert.Empty(r.Host.WaitingOutside);
        Assert.Equal(DiskState.Unchanged, session.CheckDisk());
        Assert.Empty(r.Host.ReloadChangedOutside());   // loaded once, not again
        Assert.True(r.Host.CommitScene2Edit("level.json", r.Plan("set", """{"id":"frame_a_root","field":"/properties/locked","value":true}"""), out var why), why);   // editing works again
    }

    [Fact]
    public void Unsaved_edits_wait_for_a_choice_and_use_the_saved_version_drops_them()
    {
        using var r = new Scene2HostCommandTests.Rig();
        var clean = r.Entry.Session.TextToSave; var mine = Mine(r); File.WriteAllText(r.Disk, r.Original + Environment.NewLine);
        Assert.Empty(r.Host.ReloadChangedOutside());
        var waiting = Assert.Single(r.Host.WaitingOutside); Assert.Equal("level.json", waiting.Name); Assert.True(waiting.HasEdits); Assert.Null(waiting.Problem);
        Assert.Equal(mine, r.Entry.Session.TextToSave);   // nothing dropped without asking
        Assert.True(r.Host.LoadSaved("level.json", out var why), why);
        Assert.Equal(clean, r.Entry.Session.TextToSave); Assert.False(r.Entry.Session.Dirty); Assert.Empty(r.Host.WaitingOutside);
    }

    [Fact]
    public void Keep_my_changes_lets_the_next_save_write_over_the_other_version()
    {
        using var r = new Scene2HostCommandTests.Rig();
        var mine = Mine(r); File.WriteAllText(r.Disk, r.Original + Environment.NewLine);
        r.Host.ReloadChangedOutside();
        Assert.False(r.Host.SaveScene2("level.json", out _));   // still refused until the person chooses
        Assert.True(r.Host.KeepMine("level.json", out var why), why); Assert.Empty(r.Host.WaitingOutside);
        Assert.True(r.Entry.Session.Dirty);
        Assert.True(r.Host.SaveScene2("level.json", out why), why);
        Assert.Equal(mine, File.ReadAllText(r.Disk)); Assert.False(r.Entry.Session.Dirty);
        Assert.Empty(r.Host.ReloadChangedOutside());   // BED's own save isn't taken for an outside change
    }

    [Fact]
    public void A_half_written_file_is_reported_and_loads_once_it_is_whole()
    {
        using var r = new Scene2HostCommandTests.Rig();
        var theirs = Theirs(r); var before = r.Entry.Session.Current;
        File.WriteAllText(r.Disk, "{ \"half\": ");   // another program part-way through writing it
        Assert.Empty(r.Host.ReloadChangedOutside());
        var waiting = Assert.Single(r.Host.WaitingOutside); Assert.False(waiting.HasEdits); Assert.Contains("can't be loaded", waiting.Problem);
        Assert.Same(before, r.Entry.Session.Current);   // BED keeps showing the version it has
        File.WriteAllText(r.Disk, theirs);
        Assert.Equal(["level.json"], r.Host.ReloadChangedOutside());
        Assert.Equal(theirs, r.Entry.Session.TextToSave); Assert.Empty(r.Host.WaitingOutside);
    }
}
