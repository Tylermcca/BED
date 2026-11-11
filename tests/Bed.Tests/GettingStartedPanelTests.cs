using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Brutal.ImGuiApi.Internal;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Toy;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// BORROWED existing KeysPanel registration and AI panel owned-clipboard/keyboard test seams.
// Prepared source only; C#/native/UI execution is UNRUN by this worker.
[Collection(ImGuiCollection.Name)]
public sealed class GettingStartedPanelTests
{
    private static EditorModel<Board> Model() => new(BoardKit.Session(new Board([])),
        new FlatOutline<Board>(board => board.Items.Select(item => (item.Id, item.Label))));

    [Fact]
    public void Guide_has_the_three_exact_card_steps_in_one_read_only_source()
    {
        string[] expected =
        ["A project is a folder. BED shows its files on the left; click one to open it.",
         "Edit a row: pick a row in the table, change its values in Properties, and Ctrl+Z undoes.",
         "Save and check: Ctrl+S saves, and Tools > Run all checks finds problems before your game does."];
        Assert.Equal(expected, GettingStartedPanel.Steps);
        Assert.Equal("Start editing", GettingStartedPanel.CloseLabel);
        var list = Assert.IsAssignableFrom<IList<string>>(GettingStartedPanel.Steps);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = "Changed");
    }

    [Fact]
    public void Registers_once_and_show_is_idempotent_and_editor_local()
    {
        var first = Model(); var other = Model();
        GettingStartedPanel.Attach(first);
        GettingStartedPanel.Attach(first);
        var panel = Assert.Single(first.Panels, p => p.Id == GettingStartedPanel.Id);
        Assert.Equal("bed.getting-started", panel.Id);
        Assert.Equal(GettingStartedPanel.Title, panel.Title);
        Assert.False(first.IsOpen(panel.Id));
        GettingStartedPanel.Show(first);
        GettingStartedPanel.Show(first);
        Assert.True(first.IsOpen(panel.Id));
        Assert.Same(panel, Assert.Single(first.Panels, p => p.Id == panel.Id));
        Assert.DoesNotContain(other.Panels, p => p.Id == panel.Id);
        GettingStartedPanel.Show(other);
        first.SetOpen(panel.Id, false);
        Assert.True(other.IsOpen(panel.Id));
        Assert.Empty(first.Session.Journal);
        Assert.False(first.Session.Dirty);
        var window = new EditorWindow<Board>(first);
        var action = Assert.Single(window.PaletteEntries(""), p => p.Id == GettingStartedPanel.ActionId);
        Assert.Null(action.Unavailable);
        Assert.True(window.Run(action.Id));
        Assert.True(first.IsOpen(panel.Id));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Draws_each_step_and_keyboard_button_closes_without_editing(float scale)
    {
        using var host = new HeadlessHost(1280, 720);
        EditorWindow<Board>.Configure(ImGui.GetIO());
        ImGui.GetStyle().FontScaleMain = scale;
        var model = Model();
        var before = model.Session.Current;
        var revision = model.Session.Revision;
        GettingStartedPanel.Show(model);
        var panel = Assert.Single(model.Panels, p => p.Id == GettingStartedPanel.Id);
        const string title = "Getting started###getting-started-proof";
        float2 buttonMin = default, buttonMax = default;
        void Draw()
        {
            if (!model.IsOpen(panel.Id)) return;
            ImGui.SetNextWindowPos(new float2(20, 20), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(panel.Width, panel.Height), ImGuiCond.Always);
            if (ImGui.Begin(title, ImGuiWindowFlags.NoSavedSettings))
            {
                panel.Draw();
                buttonMin = ImGui.GetItemRectMin(); buttonMax = ImGui.GetItemRectMax();
            }
            ImGui.End();
        }
        host.Frames(Draw, 4);
        string Text()
        {
            ImGui.SetClipboardText(""u8); // Owned HeadlessHost clipboard: an empty draw must not reuse old log text.
            host.Frame(() => { ImGui.LogToClipboard(); Draw(); ImGui.LogFinish(); });
            return Regex.Replace(ImGui.GetClipboardText().ToString() ?? "", @"\s+", " ");
        }
        var text = Text();
        Assert.Contains(GettingStartedPanel.Heading, text);
        var last = -1;
        foreach (var step in GettingStartedPanel.Steps)
        {
            Assert.Single(Regex.Matches(text, Regex.Escape(step)));
            var at = text.IndexOf(step, StringComparison.Ordinal);
            Assert.True(at > last); last = at;
        }
        Assert.Contains(GettingStartedPanel.CloseLabel, text);
        Assert.True(host.Covered() > 0);
        var frame = ImGui.Internal.FindWindowByName(title);
        Assert.False(frame.IsNull());
        Assert.True(buttonMin.X >= frame.InnerClipRect.Min.X && buttonMax.X <= frame.InnerClipRect.Max.X);
        Assert.True(buttonMin.Y >= frame.InnerClipRect.Min.Y && buttonMax.Y <= frame.InnerClipRect.Max.Y);
        var wanted = frame.GetID(GettingStartedPanel.CloseLabel).Value;
        var reached = false;
        for (var i = 0; i < 40; i++)
        {
            var context = ImGui.GetCurrentContext();
            if (!context.NavWindow.IsNull() && Marshal.PtrToStringUTF8((IntPtr)context.NavWindow.Name) == title && context.NavId.Value == wanted) { reached = true; break; }
            host.Press(Draw, ImGuiKey.Tab);
        }
        Assert.True(reached, "Tab must reach the actual Start editing button.");
        host.Press(Draw, ImGuiKey.Enter);
        Assert.False(model.IsOpen(panel.Id));
        Assert.DoesNotContain(GettingStartedPanel.Heading, Text());
        Assert.Same(before, model.Session.Current);
        Assert.Equal(revision, model.Session.Revision);
        Assert.Empty(model.Session.Journal);
        Assert.False(model.Session.Dirty);
        GettingStartedPanel.Show(model);
        host.Frames(Draw, 2);
        Assert.True(model.IsOpen(panel.Id));
        Assert.Contains(GettingStartedPanel.Heading, Text());
        Assert.Single(model.Panels, p => p.Id == panel.Id);
    }
}
