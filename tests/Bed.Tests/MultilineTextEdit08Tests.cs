using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class MultilineTextEdit08Tests
{
    [Fact]
    public void Native_multiline_typing_and_value_actions_use_the_same_buffer_and_tree()
    {
        using var host = new HeadlessHost(700, 500) { Drawing = false };
        var text = new ImInputString(1024, "first line"); var changes = 0;
        void Draw() { Ui.Begin("Own multiline"u8); if (Ui.InputTextMultiline("Paragraphs"u8, text, new float2(480, 180))) changes++; Ui.End(); }
        host.Frames(Draw, 3); var node = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField);
        Assert.Equal("Paragraphs", node.Label); Assert.Equal("first line", node.Value); Assert.True(node.Bounds.Height >= 180);
        host.Click(Draw, node.Bounds.X + 15, node.Bounds.Y + 12); host.Press(Draw, ImGuiKey.A, ctrl: true);
        host.Type(Draw, "typed first\ntyped second"); host.Frames(Draw, 2);
        Assert.Equal("typed first\ntyped second", text.ToString()); Assert.True(changes > 0);
        node = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField);
        var ticket = Ui.RequestAction(node.Id, UiActions.SetValue, "action first\naction second");
        var before = changes; host.Frames(Draw, 2);
        Assert.Equal(UiActionOutcome.Applied, ticket.Outcome); Assert.Equal(before + 1, changes);
        Assert.Equal("action first\naction second", text.ToString()); Assert.Equal(text.ToString(), Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField).Value);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Readonly_and_disabled_fields_refuse_value_requests(bool readOnly, bool disabled)
    {
        using var host = new HeadlessHost(700, 500) { Drawing = false }; var text = new ImInputString(128, "unchanged"); var changes = 0;
        void Draw() { Ui.Begin("Own refusal"u8); Ui.BeginDisabled(disabled, "own disabled reason"); if (Ui.InputTextMultiline("Paragraphs"u8, text, new float2(480, 180), readOnly ? ImGuiInputTextFlags.ReadOnly : 0)) changes++; Ui.EndDisabled(); Ui.End(); }
        host.Frames(Draw, 3); var node = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField);
        Assert.False(node.Actions.HasFlag(UiActions.SetValue));
        var ticket = Ui.RequestAction(node.Id, UiActions.SetValue, "refused\nvalue"); host.Frames(Draw, 3);
        Assert.Equal(UiActionOutcome.NotAvailable, ticket.Outcome); Assert.Equal("unchanged", text.ToString()); Assert.Equal(0, changes);
    }

    [Fact]
    public void Stale_value_and_oversize_requests_do_not_mutate_the_buffer()
    {
        using var host = new HeadlessHost(700, 500) { Drawing = false }; var text = new ImInputString(32, "old");
        void Draw() { Ui.Begin("Own stale"u8); Ui.InputTextMultiline("Paragraphs"u8, text, new float2(480, 180)); Ui.End(); }
        host.Frames(Draw, 3); var node = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField);
        var stale = Ui.RequestAction(node.Id, UiActions.SetValue, "request"); text.SetValue("changed outside".AsSpan()); host.Frames(Draw, 3);
        Assert.Equal(UiActionOutcome.NotAvailable, stale.Outcome); Assert.Equal("changed outside", text.ToString());
        node = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField);
        var huge = Ui.RequestAction(node.Id, UiActions.SetValue, new string('x', 64)); host.Frames(Draw, 3);
        Assert.Equal(UiActionOutcome.NotAvailable, huge.Outcome); Assert.Equal("changed outside", text.ToString());
    }
}
