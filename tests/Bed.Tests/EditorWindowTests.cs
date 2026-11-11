using System.Collections.Immutable;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Toy;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

/// <summary>ImGui's current context is process-global: tests that make one never run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ImGuiCollection
{
    public const string Name = "ImGui";
}

/// <summary>The editor window, drawn headless with a real ImGui context and no window (A007's recipe, from a BRUTAL game's panel tests).</summary>
[Collection(ImGuiCollection.Name)]
public sealed class EditorWindowTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("bed-window-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static readonly ObjectType<Board, Item> ItemType = new("toy.item", "Item", (b, id) => b.Find(id), [
        new("label", "Label", PropertyKind.Text, i => i.Label, (i, v) => new RenameItem(i.Id, (string)v!), Default: (_, _) => "Item"),
        new("x", "X", PropertyKind.Number, i => i.X, (i, v) => new MoveItem(i.Id, Convert.ToDouble(v), i.Y)),
        new("y", "Y", PropertyKind.Number, i => i.Y, (i, v) => new MoveItem(i.Id, i.X, Convert.ToDouble(v))),
    ]);

    // "Add item" can run from the palette: it adds a new item at the origin. The other commands need details.
    private static ICommand<Board>? CommandFor(CommandInfo c, EditorModel<Board> _) => c.Id == "toy.add" ? new AddItem(null, "New item", 0, 0) : null;

    private EditorWindow<Board> Window(bool withFile = true)
    {
        var session = BoardKit.Session(new Board([new Item("lamp", "Lamp", 1, 2), new Item("chair", "Chair", 3, 4)]));
        if (withFile) session.Save(Path.Combine(dir, "board.json"));
        var editor = new EditorModel<Board>(session, new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        return new EditorWindow<Board>(editor, new Inspector<Board>(editor).Add(ItemType), new Shortcuts(), CommandFor);
    }

    internal static void Frames(Action draw, int count = 4)
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = default;
            io.LogFilename = default;
            io.DisplaySize = new float2(1280, 900);
            io.DeltaTime = 1f / 60;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.AddFontDefault();
            for (var f = 0; f < count; f++)
            {
                ImGui.NewFrame();
                draw();
                ImGui.Render();
            }
            Assert.True(ImGui.GetDrawData().CmdListsCount > 0);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void DrawsHeadlessWithNothingSelectedASelectionAndThePalette()
    {
        var w = Window();
        Frames(w.Draw);
        w.Editor.Select("lamp");
        Frames(w.Draw);
        w.Editor.Select("lamp", "chair");   // mixed values
        Frames(w.Draw);
        w.Run("editor.palette");
        Assert.True(w.PaletteOpen);
        Frames(w.Draw);
        Assert.True(w.PaletteOpen);    // still open after the frames drew it
        Assert.True(w.HandleKey(Chord.Parse("Escape")));
        Assert.False(w.PaletteOpen);   // Escape closes the palette first
    }

    [Fact]
    public void KeysUndoRedoAndSave()
    {
        var w = Window();
        w.Editor.Select("lamp");
        Assert.True(w.Inspector!.Set("x", 9.0));
        Assert.EndsWith(" *", w.Title);
        Assert.True(w.HandleKey(Chord.Parse("Ctrl+Z")));
        Assert.Equal(1.0, w.Editor.Session.Current.Find("lamp")!.X);
        Assert.True(w.HandleKey(Chord.Parse("Ctrl+Y")));
        Assert.True(w.HandleKey(Chord.Parse("Ctrl+S")));
        Assert.Equal("board.json", w.Title);
        Assert.Equal("Saved board.json.", w.Status);
        Assert.False(w.HandleKey(Chord.Parse("Ctrl+Q")));   // bound to nothing
    }

    [Fact]
    public void KeysFocusInAndOut()
    {
        var w = Window();
        Assert.False(w.HandleKey(Chord.Parse("F")));
        Assert.Equal("Select something to focus on.", w.Status);
        w.Editor.Select("lamp");
        Assert.True(w.HandleKey(Chord.Parse("F")));
        Assert.Equal(["lamp"], w.Editor.Focus);
        Assert.True(w.HandleKey(Chord.Parse("Escape")));
        Assert.Empty(w.Editor.Focus);
    }

    [Fact]
    public void ThePaletteListsActionsAndCommandsAndSaysWhyOneCantRun()
    {
        var w = Window();
        var all = w.PaletteEntries("");
        Assert.Contains(all, e => e.Id == "editor.undo" && e.Unavailable == "nothing to undo" && e.Shortcut == "Ctrl+Z");
        Assert.Contains(all, e => e.Id == "toy.add" && e.Unavailable is null);
        Assert.Contains(all, e => e.Id == "toy.move" && e.Unavailable == "needs details: use the inspector");
        Assert.Equal(["editor.focus_in", "editor.focus_out"], w.PaletteEntries("focus").Select(e => e.Id));
    }

    [Fact]
    public void RunningACommandFromThePaletteEditsThroughTheSession()
    {
        var w = Window();
        Assert.True(w.Run("toy.add"));
        Assert.Equal(3, w.Editor.Session.Current.Items.Length);
        Assert.Equal("Add item: done.", w.Status);
        Assert.True(w.Editor.Session.History.CanUndo);
        Assert.False(w.Run("toy.move"));
        Assert.Equal("Move item needs details: use the inspector.", w.Status);
        Assert.False(w.Run("toy.teleport"));
        Assert.Equal("There's no command 'toy.teleport'.", w.Status);
    }

    [Fact]
    public void SavingWithNoFileSaysSoAndARefusedEditExplainsWhy()
    {
        var w = Window(withFile: false);
        Assert.False(w.Run("editor.save"));
        Assert.Contains("saved to a file yet", w.Status);
        Assert.Equal("Untitled", w.Title);

        w.Editor.Select("lamp");
        w.Editor.FocusOn("chair");   // the lamp is now outside the focus
        Assert.False(w.Inspector!.Set("x", 5.0));
        Assert.Contains("outside the place you're focused on", w.Inspector.LastIssues[0].Message);
    }

    [Fact]
    public void AnIssueLeadsBackToItsObjectEvenFromOutsideTheFocus()
    {
        var w = Window();
        w.Editor.FocusOn("chair");
        Assert.True(w.GoTo(Issue.Error("the label is empty", "lamp", "$.items[0].label")));
        Assert.Empty(w.Editor.Focus);            // stepped out so the lamp can be seen
        Assert.Equal(["lamp"], w.Editor.Selection);
        Assert.Equal("Showing Lamp ($.items[0].label).", w.Status);
        Assert.False(w.GoTo(Issue.Error("gone", "ghost")));
        Assert.Equal("'ghost' isn't in the file any more.", w.Status);
        Assert.False(w.GoTo(Issue.Error("whole file")));
    }

    [Fact]
    public void ConfigureTurnsOnKeyboardNavigation()
    {
        var context = ImGui.CreateContext();
        try
        {
            EditorWindow<Board>.Configure(ImGui.GetIO());
            Assert.True(ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.NavEnableKeyboard));
        }
        finally { ImGui.DestroyContext(context); }
    }
}
