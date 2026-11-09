using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Sdk;
using Im = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class UiTreeOracleA448Tests
{
    private static UiNode Node(string label, UiBounds bounds) =>
        new(1, UiRole.Button, label, bounds, true, null, false, false, false, null, null, null, -1, UiActions.Invoke);
    [Fact]
    public void Exact_find_rejects_absence_duplicate_and_partial_labels()
    {
        var tree = new UiTree(); tree.Begin(); tree.Record(Node("Save all", new(0,0,20,20))); tree.Complete();
        var query = new UiTreeAssertionsA448(tree.Snapshot());
        Assert.Equal("Save all", query.Find(UiRole.Button, "Save all").Label);
        Assert.ThrowsAny<XunitException>(() => query.Find(UiRole.Button, "Save"));
        Assert.ThrowsAny<XunitException>(() => query.Find(UiRole.Link, "Save all"));
        tree.Begin(); tree.Record(Node("Save all", new(0,0,20,20))); tree.Record(Node("Save all", new(0,30,20,20))); tree.Complete();
        Assert.ThrowsAny<XunitException>(() => new UiTreeAssertionsA448(tree.Snapshot()).Find(UiRole.Button, "Save all"));
    }
    [Fact]
    public void Geometry_and_state_helpers_reject_empty_clipped_disabled_or_unfocused_nodes()
    {
        var node = Node("Save", new(10,10,20,20)); var clip = new UiBounds(0,0,40,40);
        UiTreeAssertionsA448.InsideClip(node, clip); UiTreeAssertionsA448.Enabled(node);
        UiTreeAssertionsA448.Visible(node, new(25,25,20,20));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.Visible(node,new(30,30,20,20)));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.InsideClip(node with { Bounds = default }, clip));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.InsideClip(node, new(0,0,29,40)));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.InsideClip(node, default));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.Enabled(node with { Enabled = false, DisabledReason = "nothing changed" }));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.Focused(node));
        UiTreeAssertionsA448.Focused(node with { Focused = true });
    }
    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Native_item_clip_and_recorded_state_are_available_without_claiming_text_coverage(float scale)
    {
        using var host = new HeadlessHost(900, 700) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        UiBounds clip = default; UiBounds narrow = default;
        void Draw()
        {
            Im.SetNextWindowPos(new float2(0,0)); Im.SetNextWindowSize(new float2(800,600));
            Ui.Begin("Geometry proof");
            var c = Im.Internal.GetCurrentWindow().InnerClipRect;
            clip = new(c.Min.X,c.Min.Y,c.Max.X-c.Min.X,c.Max.Y-c.Min.Y);
            Ui.Button("Save");
            Ui.BeginDisabled("nothing changed"); Ui.Button("Disabled"); Ui.EndDisabled();
            var pos = Im.GetCursorScreenPos(); narrow = new(pos.X,pos.Y,10,30);
            Im.PushClipRect(pos, pos + new float2(10,30), true);
            Ui.Button("A deliberately wider control", new float2(250,30));
            Im.PopClipRect(); Ui.End();
        }
        host.Frames(Draw,3);
        var query = new UiTreeAssertionsA448(Ui.Snapshot());
        var save = query.Find(UiRole.Button,"Save");
        UiTreeAssertionsA448.Enabled(save); UiTreeAssertionsA448.InsideClip(save,clip);
        var disabled = query.Find(UiRole.Button,"Disabled");
        Assert.Equal("nothing changed", disabled.DisabledReason);
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.Enabled(disabled));
        var clipped = query.Find(UiRole.Button,"A deliberately wider control");
        // Its submitted label is still complete even though the native clip is too narrow.
        Assert.Equal("A deliberately wider control", clipped.Label);
        UiTreeAssertionsA448.InsideClip(clipped,clip);
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.InsideClip(clipped,narrow));
    }
    [Theory]
    [InlineData(1f)] [InlineData(1.5f)] [InlineData(2f)]
    public void Full_text_evidence_rejects_clips_small_buttons_and_missing_content(float scale)
    {
        using var host = new HeadlessHost(900, 700) { Drawing = false };
        Im.GetStyle().FontScaleMain = scale;
        const string full = "Complete text with punctuation: alpha, beta; gamma!";
        void Draw()
        {
            Im.SetNextWindowPos(new float2(0,0)); Im.SetNextWindowSize(new float2(800,600));
            Ui.Begin("Text proof");
            Ui.TextWrapped(full);
            Ui.Text("first line\nsecond line");
            Ui.Button("Full button");
            Ui.Button("Short"); Ui.NameItem("Short", "Semantic name is not drawn text");
            Ui.Button("Too wide for its button", new float2(30, 40));
            var p = Im.GetCursorScreenPos();
            Im.PushClipRect(p, p + new float2(12, 40), true);
            Ui.Text("Horizontally clipped text"); Im.PopClipRect();
            p = Im.GetCursorScreenPos();
            Im.PushClipRect(p, p + new float2(700, 3), true);
            Ui.TextWrapped("Vertically clipped text"); Im.PopClipRect();
            Ui.Text("Missing suffix...");
            Ui.End();
        }
        host.Frames(Draw,3);
        var snapshot = Ui.Snapshot(); var query = new UiTreeAssertionsA448(snapshot);
        UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Text, full), full);
        UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Text, "first line\nsecond line"), "first line\nsecond line");
        UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Button, "Full button"), "Full button");
        Assert.True(query.Find(UiRole.Text, full).TextEvidence!.Value.WrapWidth > 0);
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Button,"Semantic name is not drawn text"),"Semantic name is not drawn text"));
        foreach (var label in new[] { "Horizontally clipped text", "Vertically clipped text" })
            Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Text,label),label));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Button,"Too wide for its button"),"Too wide for its button"));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.FullyShown(query.Find(UiRole.Text,"Missing suffix..."),"Missing suffix is required"));
        Assert.ThrowsAny<XunitException>(() => UiTreeAssertionsA448.FullyShown(Node("Unmeasured",new(0,0,20,20)),"Unmeasured"));
        Assert.Contains("textEvidence", snapshot.Json); Assert.Contains("truncated", snapshot.Json);
    }

}
