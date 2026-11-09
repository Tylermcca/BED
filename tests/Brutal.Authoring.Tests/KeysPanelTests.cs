using System.Runtime.InteropServices;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Toy;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class KeysPanelTests
{
    private static EditorModel<Board> Model() => new(BoardKit.Session(new Board([])),
        new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));

    [Fact]
    public void Every_default_is_listed_and_rows_read_rebindings_live()
    {
        var keys = new Shortcuts();
        var rows = KeysPanel.Rows(keys, []);
        Assert.Equal(Shortcuts.EditorDefaults.Count, rows.Count);
        foreach (var pair in Shortcuts.EditorDefaults)
            Assert.Contains(rows, r => r.Id == pair.Key && r.Chord == pair.Value);
        keys.Set("editor.save", "Ctrl+Alt+S");
        Assert.Contains(KeysPanel.Rows(keys, []), r => r.Id == "editor.save" && r.Chord == "Ctrl+Alt+S");
        keys.Set("editor.save", null);
        Assert.DoesNotContain(KeysPanel.Rows(keys, []), r => r.Id == "editor.save");
    }

    [Fact]
    public void Plugin_rows_use_current_palette_label_or_id_without_inventing_context()
    {
        var keys = new Shortcuts().Default("lantern.inspect", "F8");
        keys.Set("lantern.other", "F9");
        var rows = KeysPanel.Rows(keys, [("lantern.inspect", "Inspect lantern", "F8", null)]);
        Assert.Contains(rows, r => r.Id == "lantern.inspect" && r.Label == "Inspect lantern");
        Assert.Contains(rows, r => r.Id == "lantern.other" && r.Label == "lantern.other");
    }

    [Fact]
    public void Keys_is_one_palette_panel_with_overridable_F1()
    {
        var model = Model(); var keys = new Shortcuts(); var window = new EditorWindow<Board>(model, shortcuts: keys);
        Assert.Single(model.Panels, p => p.Id == KeysPanel.Id);
        Assert.False(model.IsOpen(KeysPanel.Id));
        var entry = Assert.Single(window.PaletteEntries(""), p => p.Id == KeysPanel.ActionId);
        Assert.Equal("F1", entry.Shortcut); Assert.Null(entry.Unavailable);
        Assert.True(window.HandleKey(Chord.Parse("F1"))); Assert.True(model.IsOpen(KeysPanel.Id));
        model.SetOpen(KeysPanel.Id, false);
        keys.Set(KeysPanel.ActionId, "F2");
        Assert.False(window.HandleKey(Chord.Parse("F1"))); Assert.False(model.IsOpen(KeysPanel.Id));
        Assert.True(window.HandleKey(Chord.Parse("F2"))); Assert.True(model.IsOpen(KeysPanel.Id));
        _ = new EditorWindow<Board>(model, shortcuts: keys);
        Assert.Single(model.Panels, p => p.Id == KeysPanel.Id);
    }

    [Fact]
    public void F1_clash_is_not_forced_and_palette_still_opens_keys()
    {
        var model = Model(); var keys = new Shortcuts().Default("lantern.inspect", "F1");
        var window = new EditorWindow<Board>(model, shortcuts: keys);
        Assert.False(window.HandleKey(Chord.Parse("F1"))); Assert.False(model.IsOpen(KeysPanel.Id));
        Assert.True(window.Run(KeysPanel.ActionId)); Assert.True(model.IsOpen(KeysPanel.Id));
    }

    [Fact]
    public void Native_keyboard_can_open_and_navigate_read_only_panel()
    {
        using var host = new HeadlessHost(1600, 1000) { Drawing = false };
        var model = Model(); var window = new EditorWindow<Board>(model);
        EditorWindow<Board>.Configure(ImGui.GetIO());
        host.Frames(window.Draw, 3);
        var revision = model.Session.Revision;
        host.Press(window.Draw, ImGuiKey.F1);
        Assert.True(model.IsOpen(KeysPanel.Id));
        host.Press(window.Draw, ImGuiKey.Tab);
        var context = ImGui.GetCurrentContext();
        Assert.NotEqual(0u, context.NavId.Value);
        Assert.False(context.NavWindow.IsNull());
        Assert.Contains("panel.bed.keys", Marshal.PtrToStringUTF8((IntPtr)context.NavWindow.Name)!);
        Assert.Equal(revision, model.Session.Revision);
        Assert.False(model.Session.Dirty);
    }

    [Fact]
    public void Fixed_key_documentation_has_exact_source_handlers()
    {
        Assert.Equal(4, KeysPanel.FixedKeys.Count);
        Assert.All(KeysPanel.FixedKeys, row => Assert.Contains("not rebindable", row));
        var root = SourceRoot();
        var table = File.ReadAllText(Path.Combine(root, "src", "Brutal.Authoring.Editor.ImGui", "DataTableView.cs"));
        var canvas = File.ReadAllText(Path.Combine(root, "src", "Brutal.Authoring.Editor.ImGui", "MapCanvas.cs"));
        Assert.Contains("if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C, false) && (RangeEnd is not null || !active)) { Copy(); return; }", table);
        foreach (var key in new[] { "UpArrow", "DownArrow", "LeftArrow", "RightArrow" })
            Assert.Contains("ImGui.IsKeyPressed(ImGuiKey." + key + ")", table);
        Assert.Contains("if (io.KeyShift)", table);
        Assert.Contains("if (ImGui.IsKeyPressed(ImGuiKey.Escape)) return \"Move cancelled.\";", canvas);
        Assert.Contains("if (ImGui.IsKeyPressed(ImGuiKey._0, false) || ImGui.IsKeyPressed(ImGuiKey.Keypad0, false)) (zoom, pan) = (1, default);", canvas);
    }

    private static string SourceRoot()
    {
        for (var p = new DirectoryInfo(AppContext.BaseDirectory); p is not null; p = p.Parent)
            if (File.Exists(Path.Combine(p.FullName, "Brutal.Authoring.slnx"))) return p.FullName;
        throw new InvalidOperationException("Source checkout is required for fixed-key drift checks.");
    }
}
