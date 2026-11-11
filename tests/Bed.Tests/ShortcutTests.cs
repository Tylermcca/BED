using Bed.Editor;
using Xunit;

namespace Bed.Tests;

/// <summary>Keyboard shortcuts (goal 12, keyboard; FUT-008 palette display).</summary>
public sealed class ShortcutTests
{
    [Theory]
    [InlineData("ctrl+z", "Ctrl+Z")]
    [InlineData("Shift + Ctrl + p", "Ctrl+Shift+P")]
    [InlineData("alt+f4", "Alt+F4")]
    [InlineData("escape", "Escape")]
    [InlineData("F", "F")]
    public void ChordsParseAndPrintTheSameWay(string text, string printed) => Assert.Equal(printed, Chord.Parse(text).ToString());

    [Theory]
    [InlineData("Ctrl+", "isn't a shortcut")]
    [InlineData("Hyper+Z", "isn't a modifier")]
    [InlineData("Ctrl+Ctrl+Z", "twice")]
    [InlineData("Ctrl+F13", "isn't a key")]
    public void BadChordsSayWhy(string text, string why) => Assert.Contains(why, Assert.Throws<FormatException>(() => Chord.Parse(text)).Message);

    [Fact]
    public void DefaultsWorkAndThePersonsChoiceWins()
    {
        var s = new Shortcuts().Default("toy.move", "M");
        Assert.Equal("editor.undo", s.Action(Chord.Parse("Ctrl+Z")));
        Assert.Equal("toy.move", s.Action(Chord.Parse("m")));
        s.Set("toy.move", "Ctrl+M");
        Assert.Null(s.Action(Chord.Parse("M")));
        Assert.Equal("toy.move", s.Action(Chord.Parse("Ctrl+M")));
        s.ResetToDefault("toy.move");
        Assert.Equal("M", s.For("toy.move")!.Value.ToString());
        s.Set("editor.redo", null);   // removed by the person
        Assert.Null(s.For("editor.redo"));
    }

    [Fact]
    public void AClashTriggersNothingAndIsReported()
    {
        var s = new Shortcuts().Default("toy.add", "Ctrl+S");   // clashes with save
        Assert.Null(s.Action(Chord.Parse("Ctrl+S")));
        var clash = Assert.Single(s.Clashes());
        Assert.Contains("editor.save and toy.add", clash.Message);
        s.Set("toy.add", "Ctrl+N");
        Assert.Empty(s.Clashes());
        Assert.Equal("editor.save", s.Action(Chord.Parse("Ctrl+S")));
    }

    [Fact]
    public void ThePersonsChangesRoundTripAndBadLinesAreSkipped()
    {
        var s = new Shortcuts();
        s.Set("editor.save", "Ctrl+Shift+S");
        s.Set("editor.redo", null);
        var text = s.WriteOverrides();
        var again = new Shortcuts();
        Assert.Empty(again.ReadOverrides(text));
        Assert.Equal(text, again.WriteOverrides());
        Assert.Null(again.For("editor.redo"));

        var issues = new Shortcuts().ReadOverrides("""{"kind":"editor.shortcuts","version":1,"shortcuts":{"editor.save":"Hyper+S","editor.undo":"Ctrl+U"}}""");
        Assert.Contains("editor.save", Assert.Single(issues).Message);
    }
}
