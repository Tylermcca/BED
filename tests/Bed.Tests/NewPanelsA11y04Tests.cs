using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class NewPanelsA11y04Tests
{
    private sealed class Sink : IAccessKitUpdateSink
    {
        public List<AccessKitProjectedUpdate> Updates { get; } = [];
        public bool TryApply(AccessKitProjectedUpdate update) { Updates.Add(update); return true; }
    }
    private static void Begin()
    {
        ImGui.SetNextWindowPos(new float2(15, 15)); ImGui.SetNextWindowSize(new float2(650, 500));
        Ui.Begin("Own source controls"u8);
    }
    [Fact]
    public void Named_combo_can_be_focused_through_projection_then_changed_with_native_keys()
    {
        using var host = new HeadlessHost(750, 580) { Drawing = false };
        EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
        var selected = 0;
        void Draw()
        {
            Begin();
            if (Ui.BeginCombo("Source choice"u8, selected == 0 ? "First" : "Second"))
            {
                if (Ui.Selectable("First"u8, selected == 0)) selected = 0;
                if (Ui.Selectable("Second"u8, selected == 1)) selected = 1;
                Ui.EndCombo();
            }
            Ui.End();
        }
        host.Frames(Draw, 3);
        var combo = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo);
        Assert.Equal("Source choice", combo.Label); Assert.Equal("First", combo.Value); Assert.False(combo.Bounds.IsEmpty);
        var projection = new AccessKitProjection(); var sink = new Sink();
        Assert.True(projection.Submit(Ui.Snapshot(), "Own controls", new(0, 0, 750, 580), 1, 1, sink));
        var projected = Assert.Single(sink.Updates.Single().ChangedNodes, n => n.Role == AccessKitProjectedRole.ComboBox);
        Assert.Equal(UiActions.Focus, projected.Actions);
        var ticket = Ui.RequestAction(combo.Id, UiActions.Focus); host.Frames(Draw, 2);
        Assert.Equal(UiActionOutcome.Applied, ticket.Outcome);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo).Focused);
        host.Press(Draw, ImGuiKey.Space); host.Frames(Draw, 2);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == "Second");
        host.Press(Draw, ImGuiKey.DownArrow); host.Press(Draw, ImGuiKey.Enter); host.Frames(Draw, 2);
        Assert.Equal(1, selected);
        Assert.Equal("Second", Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo).Value);
    }
    [Fact]
    public void Disabled_combo_refuses_queued_focus_and_keeps_its_full_reason_in_projection()
    {
        using var host = new HeadlessHost(750, 580) { Drawing = false }; var disabled = false; EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO());
        const string reason = "Save the source draft before choosing a generated result.";
        void Draw() { Begin(); Ui.BeginDisabled(disabled, reason); if (Ui.BeginCombo("Generated choice"u8, "Own result"u8)) Ui.EndCombo(); Ui.EndDisabled(); Ui.End(); }
        host.Frames(Draw, 3); var combo = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo);
        var ticket = Ui.RequestAction(combo.Id, UiActions.Focus); disabled = true; host.Frames(Draw, 2);
        Assert.Equal(UiActionOutcome.NotAvailable, ticket.Outcome);
        combo = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Combo); Assert.False(combo.Enabled); Assert.Equal(reason, combo.DisabledReason);
        var sink = new Sink(); new AccessKitProjection().Submit(Ui.Snapshot(), "Own controls", new(0, 0, 750, 580), 1, 1, sink);
        var projected = Assert.Single(sink.Updates.Single().ChangedNodes, n => n.Role == AccessKitProjectedRole.ComboBox);
        Assert.Equal(UiActions.None, projected.Actions); Assert.Contains(reason, projected.Description);
    }
    [Fact]
    public void Multiline_field_is_reached_and_left_by_Tab_without_changing_the_buffer()
    {
        using var host = new HeadlessHost(750, 580) { Drawing = false }; EditorWindow<Bed.Data.DataDocument>.Configure(ImGui.GetIO()); var text = new ImInputString(256, "Own first line\nOwn second line");
        void Draw() { Begin(); Ui.Button("Before prose"u8); Ui.InputTextMultiline("Authored prose"u8, text, new float2(500, 180)); Ui.Button("After prose"u8); Ui.End(); }
        host.Frames(Draw, 3); var before = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button && n.Label == "Before prose");
        var focus = Ui.RequestAction(before.Id, UiActions.Focus); host.Frames(Draw, 2); Assert.Equal(UiActionOutcome.Applied, focus.Outcome);
        host.Press(Draw, ImGuiKey.Tab); host.Frames(Draw, 2);
        var prose = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField);
        Assert.Equal("Authored prose", prose.Label); Assert.Equal(text.ToString(), prose.Value); Assert.True(prose.Focused, "Focused: "+string.Join(", ", Ui.Snapshot().Nodes.Where(n=>n.Focused).Select(n=>n.Label))+"; nav="+ImGui.GetCurrentContext().NavId.Value+"; field="+(uint)prose.Id);
        host.Press(Draw, ImGuiKey.Tab); host.Frames(Draw, 2);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button && n.Label == "After prose").Focused);
        Assert.Equal("Own first line\nOwn second line", text.ToString());
    }
}
