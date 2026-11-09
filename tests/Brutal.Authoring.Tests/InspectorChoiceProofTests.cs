using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.Authoring.TestSupport;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class InspectorChoiceProofTests
{
    private sealed class Fixture : IDisposable
    {
        private const string Schema = """
            {"type":"object","properties":{
              "tone":{"enum":["zeta","alpha","mu"]},
              "direction":{"anyOf":[{"enum":["west","east"]},{"enum":["north","south"]}]},
              "optional":{"enum":["first","second"]},
              "text":{"type":"string"}}}
            """;
        internal readonly HeadlessHost Host = new(1400, 1000) { Drawing = false };
        internal readonly EditorWindow<DataDocument> Window;
        internal readonly InspectorChoiceProof Proof;
        internal Fixture()
        {
            var schemas = new JsonSchemas().Add("fixture.schema.json", Schema);
            var document = new DataFormat("fixture").Read("""{"tone":"zeta","direction":"east","text":"plain"}""");
            var editor = new EditorModel<DataDocument>(DataKit.Session(document, schemas, "fixture.schema.json", "fixture"), DataEditing.Outline);
            Window = new EditorWindow<DataDocument>(editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "fixture.schema.json")));
            EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
            Assert.True(editor.Select(""));
            Host.Frames(Window.DrawDocked, 5);
            Proof = new InspectorChoiceProof(Host, Window, Window.DrawDocked);
        }
        internal string? At(string pointer) => Window.Editor.Session.Current.At(pointer)?.GetString();
        internal int Steps => Window.Editor.Session.History.UndoCount;
        public void Dispose() => Host.Dispose();
    }

    [Theory]
    [InlineData("tone", "zeta", "mu")]
    [InlineData("direction", "east", "south")]
    public void Declared_order_drives_forward_backward_and_undo(string field, string initial, string target)
    {
        using var f = new Fixture();
        var expected = field == "tone" ? new[] { "zeta", "alpha", "mu" } : new[] { "west", "east", "north", "south" };
        Assert.Equal(expected, f.Window.Inspector!.Rows().Single(r => r.Label == field).Choices);
        f.Proof.Choose(target, field);
        Assert.Equal(target, f.At("/" + field));
        Assert.Equal(1, f.Steps);
        Assert.True(f.Window.Run("editor.undo"));
        Assert.Equal(initial, f.At("/" + field));
        Assert.Equal(0, f.Steps);
        Assert.True(f.Window.Run("editor.redo"));
        f.Proof.Choose(initial, field);
        Assert.Equal(initial, f.At("/" + field));
        Assert.Equal(2, f.Steps);
    }

    [Theory]
    [InlineData("tone", "zeta")]
    [InlineData("direction", "east")]
    public void Same_choice_is_no_change_then_a_real_change_is_possible(string field, string current)
    {
        using var f = new Fixture();
        var before = f.Window.Editor.Session.Current;
        f.Proof.Choose(current, field);
        Assert.Same(before, f.Window.Editor.Session.Current);
        Assert.Equal(0, f.Steps);
        f.Proof.Choose(field == "tone" ? "alpha" : "north", field);
        Assert.Equal(1, f.Steps);
    }

    [Theory]
    [InlineData("unknown", "tone", "not a declared choice")]
    [InlineData("first", "optional", "no selected string choice")]
    [InlineData("plain", "text", "not one editable choice")]
    [InlineData("zeta", "absent", "Expected one inspector row")]
    public void Unsupported_requests_fail_before_input_without_editing(string value, string field, string reason)
    {
        using var f = new Fixture();
        var before = f.Window.Editor.Session.Current;
        var error = Assert.Throws<InvalidOperationException>(() => f.Proof.Choose(value, field));
        Assert.Contains(reason, error.Message);
        Assert.Same(before, f.Window.Editor.Session.Current);
        Assert.Equal(0, f.Steps);
        f.Proof.Choose("alpha", "tone"); // positive control: refusal left input ready for a real edit
        Assert.Equal("alpha", f.At("/tone"));
        Assert.Equal(1, f.Steps);
    }

    [Fact]
    public void Empty_path_fails_without_editing()
    {
        using var f = new Fixture();
        var before = f.Window.Editor.Session.Current;
        Assert.Throws<InvalidOperationException>(() => f.Proof.Choose("zeta"));
        Assert.Same(before, f.Window.Editor.Session.Current);
        Assert.Equal(0, f.Steps);
    }
    [Fact]
    public void Missing_selection_fails_without_editing()
    {
        using var f = new Fixture();
        f.Window.Editor.Select();
        var before = f.Window.Editor.Session.Current;
        var error = Assert.Throws<InvalidOperationException>(() => f.Proof.Choose("mu", "tone"));
        Assert.Contains("exactly one selected object", error.Message);
        Assert.Same(before, f.Window.Editor.Session.Current);
        Assert.Equal(0, f.Steps);
    }

}
