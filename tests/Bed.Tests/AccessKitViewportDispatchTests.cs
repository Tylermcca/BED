using Bed.Editor.ImGui;
using Brutal.Numerics;
using Xunit;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AccessKitViewportDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_request_stays_bound_to_its_recorded_viewport_even_when_widget_identity_is_unchanged(bool anotherViewport)
    {
        using var host = new HeadlessHost { Drawing = false };
        var calls = 0;
        void Draw()
        {
            Im.SetNextWindowPos(new float2(20, 20)); Im.SetNextWindowSize(new float2(400, 250));
            Ui.Begin("Viewport dispatch"u8);
            if (Ui.Button("Apply"u8)) calls++;
            Ui.End();
        }
        host.Frames(Draw, 3);
        var original = Ui.Snapshot();
        var before = Assert.Single(original.Nodes, n => n.Role == UiRole.Button);
        Assert.Equal(0u, before.ViewportId);
        // Feed the existing public recorder a completed earlier-HWND receipt. No OS window is created or moved.
        Ui.Tree.Begin();
        foreach (var node in original.Nodes) Ui.Tree.Record(node with { ViewportId = anotherViewport ? 71u : 0u });
        Ui.Tree.Complete();
        var expected = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button);
        Assert.Equal(before with { ViewportId = anotherViewport ? 71u : 0u }, expected);
        var ticket = Ui.RequestAction(expected.Id, UiActions.Invoke);
        Assert.Equal(UiActionOutcome.Pending, ticket.Outcome); Assert.Equal(0, calls);
        host.Frame(Draw);
        var after = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Button);
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.Label, after.Label);
        Assert.Equal(before.Parent, after.Parent); Assert.Equal(before.Value, after.Value); Assert.Equal(0u, after.ViewportId);
        Assert.Equal(anotherViewport ? UiActionOutcome.NotAvailable : UiActionOutcome.Applied, ticket.Outcome);
        Assert.Equal(anotherViewport ? "not available" : "applied", ticket.Message);
        Assert.Equal(anotherViewport ? 0 : 1, calls);
        host.Frame(Draw); Assert.Equal(anotherViewport ? 0 : 1, calls); // no late or duplicate invocation
    }
}
