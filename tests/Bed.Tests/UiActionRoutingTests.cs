using Bed.Editor;
using Bed.Editor.ImGui;
using Toy;
using Xunit;

namespace Bed.Tests;

// Source-prepared only. Existing serial collection; no window, subprocess or remote command.
[Collection(ImGuiCollection.Name)]
public sealed class UiActionRoutingTests
{
    private static EditorModel<Board> Model() => new(BoardKit.Session(new Board([])),
        new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));
    private static UiAction Action(EditorWindow<Board> w, string id) => Assert.Single(w.UiActions(), a => a.Id == id);

    [Fact]
    public void Descriptor_keeps_context_and_live_shortcuts_without_invoking()
    {
        var keys = new Shortcuts(); var calls = 0;
        var action = new UiAction("editor.save", c => new(c, c == UiActionContext.Toolbar ? "Toolbar save" : "Save", true,
            c == UiActionContext.Palette, null), () => keys.For("editor.save"), () => { calls++; return true; });
        Assert.Equal("editor.save", action.Id);
        Assert.False(action.Present(UiActionContext.Toolbar).Enabled);
        Assert.Null(action.Present(UiActionContext.Toolbar).Reason);
        Assert.Equal("Save", action.Present(UiActionContext.Palette).Label);
        Assert.Equal(UiActionContext.Palette, action.Present(UiActionContext.Palette).Context);
        Assert.Equal("Ctrl+S", action.Shortcut?.ToString());
        keys.Set("editor.save", "Ctrl+Alt+S"); Assert.Equal("Ctrl+Alt+S", action.Shortcut?.ToString());
        keys.Set("editor.save", null); Assert.Null(action.Shortcut);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Disabled_presentation_does_not_replace_the_existing_invocation_refusal()
    {
        var calls = 0; var status = "";
        var action = new UiAction("fixture.refuse", c => new(c, "Fixture", true, false, "display reason"), () => null,
            () => { calls++; status = "handler reason"; return false; });
        Assert.False(action.Present(UiActionContext.Palette).Enabled);
        Assert.False(action.Invoke()); Assert.Equal(1, calls); Assert.Equal("handler reason", status);
    }

    [Fact]
    public void Required_ids_exist_but_palette_action_keeps_its_hidden_palette_and_Keys_fallback()
    {
        var w = new EditorWindow<Board>(Model());
        foreach (var id in new[] { "editor.undo", "editor.redo", "editor.save", "editor.file_save_as", "editor.palette",
            "editor.text_larger", "editor.text_smaller", "editor.text_reset", "editor.stop" }) _ = Action(w, id);
        var palette = Action(w, "editor.palette");
        Assert.Equal("Find a command...", palette.Present(UiActionContext.Toolbar).Label);
        Assert.False(palette.Present(UiActionContext.Palette).Visible);
        Assert.DoesNotContain(w.PaletteEntries(""), x => x.Id == "editor.palette");
        Assert.Contains(KeysPanel.Rows(w.Shortcuts, w.PaletteEntries("")), x => x.Id == "editor.palette" && x.Label == "editor.palette");
        Assert.Equal(new[] { "editor.focus_in", "editor.focus_out" }, w.PaletteEntries("focus").Select(x => x.Id));
    }

    [Fact]
    public void Descriptor_and_key_routes_share_one_history_handler_and_keep_clashes()
    {
        var model = Model(); var keys = new Shortcuts(); var w = new EditorWindow<Board>(model, shortcuts: keys);
        Assert.True(model.Execute(new AddItem("lamp", "Lamp", 0, 0)));
        var undo = Action(w, "editor.undo"); Assert.True(undo.Present(UiActionContext.Toolbar).Enabled);
        Assert.True(undo.Invoke()); Assert.Empty(model.Session.Current.Items); Assert.Equal("Undid: Add item.", w.Status);
        Assert.True(w.HandleKey(Chord.Parse("Ctrl+Y"))); Assert.Single(model.Session.Current.Items); Assert.Equal("Redid: Add item.", w.Status);
        keys.Set("editor.undo", "Ctrl+Y");
        Assert.False(w.HandleKey(Chord.Parse("Ctrl+Y"))); Assert.Single(model.Session.Current.Items); Assert.Contains("a clash", w.Status);
    }

    [Fact]
    public void Clean_saved_document_keeps_toolbar_disabled_palette_enabled_and_handler_refusal()
    {
        var folder = Directory.CreateTempSubdirectory("ui-action-a432-").FullName;
        try
        {
            var model = Model(); var path = Path.Combine(folder, "board.json"); model.Session.Save(path);
            var w = new EditorWindow<Board>(model); var save = Action(w, "editor.save"); var before = File.ReadAllBytes(path);
            Assert.False(model.Session.Dirty);
            Assert.False(save.Present(UiActionContext.Toolbar).Enabled); Assert.Null(save.Present(UiActionContext.Toolbar).Reason);
            Assert.True(save.Present(UiActionContext.Palette).Enabled); Assert.Null(save.Present(UiActionContext.Palette).Reason);
            Assert.True(save.Invoke()); Assert.Equal("Saved board.json.", w.Status); Assert.Equal(before, File.ReadAllBytes(path));
            w.SaveRefusal = () => "fixture read only";
            Assert.False(save.Invoke()); Assert.Equal("fixture read only", w.Status); Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            var full = Path.GetFullPath(folder);
            var boundary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("ui-action-a432-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected owned fixture boundary.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }

    private sealed class Game : IPlaySession
    {
        public int Disposals { get; private set; }
        public string Status => "fixture running";
        public void Draw() { }
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void Playing_preserves_route_availability_Stop_labels_and_one_stop()
    {
        var model = Model(); var game = new Game(); var starts = 0;
        model.Add(new PlayMode<Board>("fixture", "Fixture", _ => { starts++; return game; }));
        Assert.True(model.Execute(new AddItem("lamp", "Lamp", 0, 0)));
        var w = new EditorWindow<Board>(model);
        Assert.True(Action(w, "editor.play.fixture").Invoke()); Assert.Equal(1, starts);
        var undo = Action(w, "editor.undo");
        Assert.False(undo.Present(UiActionContext.Toolbar).Enabled); Assert.True(undo.Present(UiActionContext.Palette).Enabled);
        Assert.False(undo.Invoke()); Assert.Equal("'Fixture' is playing; stop it first.", w.Status); Assert.Single(model.Session.Current.Items);
        var stop = Action(w, "editor.stop");
        Assert.Equal("Stop Fixture", stop.Present(UiActionContext.Toolbar).Label);
        Assert.Equal("Stop playing Fixture", stop.Present(UiActionContext.Palette).Label);
        Assert.True(stop.Invoke()); Assert.Equal(1, game.Disposals); Assert.Null(model.Playing);
        Assert.False(Action(w, "editor.stop").Present(UiActionContext.Palette).Visible);
    }

    [Fact]
    public void Panel_state_and_command_refusal_remain_live_without_double_preflight()
    {
        var model = Model(); var blocked = true; var calls = 0; var checks = 0;
        model.Add(new Panel("fixture.panel", "Fixture", () => { }, OpenAtStart: false,
            Commands: [new PanelCommand("go", "Go...", () => { calls++; return true; }, () => { checks++; return blocked ? "fixture blocked" : null; })]));
        var w = new EditorWindow<Board>(model); var panel = Action(w, "editor.panel.fixture.panel");
        Assert.Equal("Show Fixture", panel.Present(UiActionContext.Palette).Label);
        Assert.True(panel.Invoke()); Assert.Equal("Hide Fixture", panel.Present(UiActionContext.Palette).Label);
        var command = Action(w, "editor.panel_do.fixture.panel/go");
        Assert.Equal("fixture blocked", command.Present(UiActionContext.Palette).Reason); Assert.Equal(1, checks);
        Assert.False(command.Invoke()); Assert.Equal(2, checks); Assert.Equal(0, calls); Assert.Equal("Go...: fixture blocked.", w.Status);
        blocked = false; Assert.True(command.Invoke()); Assert.Equal(3, checks); Assert.Equal(1, calls); Assert.Equal("Go", w.Status);
    }

    [Fact]
    public void Typed_document_key_fallback_uses_the_existing_maker_once_and_keeps_unknown_id_status()
    {
        var model = Model(); var made = 0; var keys = new Shortcuts().Default("toy.add", "F8");
        var w = new EditorWindow<Board>(model, shortcuts: keys, commandFor: (info, _) =>
        {
            made++; return info.Id == "toy.add" ? new AddItem("fixture", "Fixture", 0, 0) : null;
        });
        Assert.True(w.HandleKey(Chord.Parse("F8"))); Assert.Equal(1, made); Assert.Single(model.Session.Current.Items);
        Assert.False(w.Run("toy.missing")); Assert.Equal("There's no command 'toy.missing'.", w.Status); Assert.Equal(1, made);
    }
}
