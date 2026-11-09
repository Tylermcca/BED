using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AccessKeyTests
{
    [Fact]
    public void Top_level_keys_and_each_menu_are_independent_and_unique()
    {
        var top = AccessKeys.Assign(new[] { ("file", "File"), ("edit", "Edit"), ("view", "View"), ("tools", "Tools"), ("help", "Help") });
        Assert.Equal("FEVTH", new string(top.Select(k => k.Letter).ToArray()));
        var file = AccessKeys.Assign(new[] { ("save", "Save"), ("save-as", "Save as..."), ("save-all", "Save all"), ("close", "Close##hidden") });
        Assert.Equal(file.Count, file.Select(k => k.Letter).Distinct().Count());
        Assert.Equal('S', file[0].Letter); Assert.Equal('S', AccessKeys.Assign(new[] { ("other", "Save") })[0].Letter);
        Assert.Equal("Close", file[3].Label);
    }

    [Fact]
    public void Text_entry_plain_keys_AltGr_and_ambiguous_keys_never_dispatch()
    {
        var menu = AccessKeys.Assign(new[] { ("file", "File") });
        Assert.Equal("file", AccessKeys.Resolve(menu, 'f', true, false)!.Id);
        Assert.Null(AccessKeys.Resolve(menu, 'f', false, false));
        Assert.Null(AccessKeys.Resolve(menu, 'f', true, true));
        Assert.Null(AccessKeys.Resolve(menu, 'f', true, false, ctrl: true));
        Assert.Null(AccessKeys.Resolve(menu, 'f', true, false, super: true));
        Assert.Null(AccessKeys.Resolve(menu.Concat(menu), 'f', true, false));
    }

    [Fact]
    public void Descriptor_invocation_preserves_enabled_and_visible_boundaries()
    {
        var calls = 0; var enabled = true; var visible = true;
        var action = new UiAction("save", c => new(c, "Save", visible, enabled, enabled ? null : "No edits"), () => null, () => { calls++; return true; });
        var menu = AccessKeys.Assign(new[] { (action.Id, "Save") });
        Assert.True(AccessKeys.Invoke(menu, new[] { action }, 's', true, false));
        enabled = false; Assert.False(AccessKeys.Invoke(menu, new[] { action }, 's', true, false));
        enabled = true; visible = false; Assert.False(AccessKeys.Invoke(menu, new[] { action }, 's', true, false));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Underline_uses_measured_prefix_and_is_emitted_only_during_Alt(float scale)
    {
        using var host = new HeadlessHost { Drawing = false };
        ImGui.GetStyle().FontScaleMain = scale;
        var key = new AccessKey("save-as", "Save as...", 'A', 1);
        var emitted = false; var vertices = 0;
        void Draw()
        {
            ImGui.Begin("Access proof"u8);
            var origin = ImGui.GetCursorScreenPos();
            ImGui.Text("Save as..."u8);
            var line = AccessKeys.Underline(key, origin);
            Assert.Equal(origin.X + ImGui.CalcTextSize("S"u8).X, line.X);
            Assert.Equal(ImGui.CalcTextSize("a"u8).X, line.Width);
            var before = ImGui.GetWindowDrawList().VtxBuffer.Count;
            emitted = AccessKeys.DrawUnderline(key, origin);
            vertices = ImGui.GetWindowDrawList().VtxBuffer.Count - before;
            ImGui.End();
        }
        host.Frames(Draw); Assert.False(emitted); Assert.Equal(0, vertices);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Alt, true); host.Frames(Draw, 2);
        Assert.True(emitted); Assert.True(vertices > 0);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Alt, false); host.Frames(Draw, 2);
        Assert.False(emitted); Assert.Equal(0, vertices);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Real_menu_Alt_F_opens_File_and_metadata_does_not_require_Alt(float scale)
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        EditorWindow<Toy.Board>.Configure(ImGui.GetIO());
        ImGui.GetStyle().FontScaleMain = scale;
        var session = Toy.BoardKit.Session(new Toy.Board([new Toy.Item("lamp", "Lamp", 1, 2)]));
        var model = new Brutal.Authoring.Editor.EditorModel<Toy.Board>(session,
            new Brutal.Authoring.Editor.FlatOutline<Toy.Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        var window = new EditorWindow<Toy.Board>(model);
        host.Frames(window.DrawDocked, 4);
        Assert.Equal("F", Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Menu && n.Label == "File").AccessKey);
        host.Press(window.DrawDocked, ImGuiKey.F, alt: true);
        host.Frames(window.DrawDocked, 2);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.Menu && n.Label == "File" && n.Expanded == true);
        var entries = Ui.Snapshot().Nodes.Where(n => n.Role == UiRole.MenuItem).ToArray();
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Length, entries.Select(n => n.AccessKey).Distinct().Count());
        Assert.All(entries, n => Assert.NotNull(n.AccessKey));
        Assert.False(session.Dirty);
        host.Press(window.DrawDocked, ImGuiKey.Escape);
        Assert.False(ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel));
    }

    [Fact]
    public void Access_metadata_preserves_native_text_evidence_and_rejects_invalid_letters()
    {
        using var host = new HeadlessHost { Drawing = false };
        void Draw()
        {
            Ui.Begin("Annotation"u8); Ui.Button("Save"u8); Ui.AccessKeyLast("S"); Ui.End();
        }
        host.Frames(Draw);
        var node = Assert.Single(Ui.Snapshot().Nodes, n => n.Label == "Save");
        Assert.Equal("S", node.AccessKey); Assert.NotNull(node.TextEvidence);
        Assert.Throws<ArgumentException>(() => Ui.AccessKeyLast("ss"));
    }

    [Fact]
    public void Focused_text_field_keeps_F_and_AltGr_away_from_menus()
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        EditorWindow<Toy.Board>.Configure(ImGui.GetIO());
        var session = Toy.BoardKit.Session(new Toy.Board([new Toy.Item("lamp", "Lamp", 1, 2)]));
        var model = new Brutal.Authoring.Editor.EditorModel<Toy.Board>(session,
            new Brutal.Authoring.Editor.FlatOutline<Toy.Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        var window = new EditorWindow<Toy.Board>(model); var input = new ImInputString(100); var focus = true;
        void Draw()
        {
            window.DrawDocked();
            ImGui.SetNextWindowPos(new float2(300, 300)); ImGui.SetNextWindowSize(new float2(350, 150));
            Ui.Begin("Text entry proof"u8);
            if (focus) { ImGui.SetKeyboardFocusHere(); focus = false; }
            Ui.InputText("Name"u8, input); Ui.End();
        }
        host.Frames(Draw, 4);
        Assert.True(ImGui.GetIO().WantTextInput);
        host.Type(Draw, "F");
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField && n.Value == "F");
        host.Press(Draw, ImGuiKey.F, alt: true);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Role == UiRole.Menu && n.Expanded == true);
        host.Press(Draw, ImGuiKey.F, ctrl: true, alt: true);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Role == UiRole.Menu && n.Expanded == true);
        Assert.False(session.Dirty);
    }

    [Fact]
    public void Real_Edit_Undo_access_key_runs_existing_action_once()
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        EditorWindow<Toy.Board>.Configure(ImGui.GetIO());
        var session = Toy.BoardKit.Session(new Toy.Board([new Toy.Item("lamp", "Lamp", 1, 2)]));
        var model = new Brutal.Authoring.Editor.EditorModel<Toy.Board>(session,
            new Brutal.Authoring.Editor.FlatOutline<Toy.Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        var window = new EditorWindow<Toy.Board>(model);
        Assert.True(model.Execute(new Toy.MoveItem("lamp", 9, 2)));
        host.Frames(window.DrawDocked, 4);
        host.Press(window.DrawDocked, ImGuiKey.E, alt: true);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.MenuItem && n.Label == "Undo" && n.AccessKey == "U");
        host.Press(window.DrawDocked, ImGuiKey.U, alt: true);
        Assert.Equal(1, session.Current.Find("lamp")!.X);
        Assert.False(session.History.CanUndo); Assert.True(session.History.CanRedo);
        Assert.False(session.Dirty);
    }

    [Fact]
    public void Defined_dialog_button_key_is_recorded_and_disabled_button_never_activates()
    {
        using var host = new HeadlessHost { Drawing = false };
        var calls = 0; var disabled = true;
        void Draw()
        {
            Ui.Begin("Dialog buttons"u8);
            Ui.BeginDisabled(disabled);
            if (AccessKeys.Button("Save", 'S')) calls++;
            Ui.EndDisabled(); Ui.End();
        }
        host.Frames(Draw, 4);
        Assert.Contains("[accessKey: S]", Ui.Snapshot().Text);
        host.Press(Draw, ImGuiKey.S, alt: true); Assert.Equal(0, calls);
        disabled = false; host.Frames(Draw, 2);
        host.Press(Draw, ImGuiKey.S, alt: true); Assert.Equal(1, calls);
    }
}
