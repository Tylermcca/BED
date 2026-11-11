using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>A field whose schema says nothing of its type keeps the kind its value has. Found by A123 on a game's data: an open-shaped record's true/false showed as text, and typing "true" saved a word.</summary>
public sealed class UntypedFieldsTests
{
    private static (DocumentSession<DataDocument> Session, EditorModel<DataDocument> Editor, Inspector<DataDocument> Inspector) Rig()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","properties":{"rules":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"conditions":{"type":"object"}}}}}}""");
        var session = DataKit.Session(new DataFormat().Read("""{"rules":[{"id":"r","conditions":{"falling":true,"height":2.5,"note":"x"}}]}"""), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (session, editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc")));
    }

    private static InspectorRow Row(Inspector<DataDocument> inspector, string field) =>
        inspector.Nested(inspector.Rows().Single(r => r.Id == "conditions")).Single(r => r.Label == field);

    [Fact]
    public void An_untyped_true_or_false_is_a_toggle_and_text_cant_replace_it()
    {
        var (session, editor, inspector) = Rig();
        Assert.True(editor.Select("/rules/0"));
        var row = Row(inspector, "falling");
        Assert.Equal(PropertyKind.Toggle, row.Kind);
        Assert.False(inspector.Set(row.Id, "true"));
        Assert.Equal(System.Text.Json.JsonValueKind.True, session.Current.At("/rules/0/conditions/falling")!.Value.ValueKind);
        Assert.True(inspector.Set(row.Id, false), string.Join(" ", inspector.LastIssues));
        Assert.Equal(System.Text.Json.JsonValueKind.False, session.Current.At("/rules/0/conditions/falling")!.Value.ValueKind);
    }

    [Fact]
    public void An_untyped_number_stays_a_number_and_text_stays_text()
    {
        var (session, editor, inspector) = Rig();
        Assert.True(editor.Select("/rules/0"));
        var height = Row(inspector, "height");
        Assert.Equal(PropertyKind.Number, height.Kind);
        Assert.True(inspector.Set(height.Id, 3.5), string.Join(" ", inspector.LastIssues));
        Assert.Equal(3.5, session.Current.At("/rules/0/conditions/height")!.Value.GetDouble());
        Assert.Equal(PropertyKind.Text, Row(inspector, "note").Kind);
    }

    // A123 found spell text over 1024 bytes couldn't be drawn at all.
    [Fact]
    public void Text_longer_than_the_old_box_draws_and_can_be_typed_into()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","properties":{"spells":{"type":"array","items":{"type":"object","properties":{"text":{"type":"string"}}}}}}""");
        var long_ = string.Concat(Enumerable.Repeat("Flamme brûlée — ", 300));
        var session = DataKit.Session(new DataFormat().Read(System.Text.Json.JsonSerializer.Serialize(new { spells = new[] { new { text = long_ } } })), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc"));
        var window = new EditorWindow<DataDocument>(editor, inspector);
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        Assert.True(editor.Select("/spells/0"));
        host.Frames(window.DrawDocked, 4);
        Assert.True(inspector.Set("text", long_ + "!"), string.Join(" ", inspector.LastIssues));
        host.Frames(window.DrawDocked, 2);
        Assert.Equal(long_ + "!", session.Current.At("/spells/0/text")!.Value.GetString());
    }

    private static (DocumentSession<DataDocument> Session, EditorModel<DataDocument> Editor, Inspector<DataDocument> Inspector) Socket()
    {
        var schemas = new JsonSchemas().Add("doc", """{"type":"object","properties":{"note":{"type":"string"},"turn":{"enum":[0,90]},"rolls":{"type":"array","items":{"type":"integer","enum":[0,90,180,270]}}}}""");
        var session = DataKit.Session(new DataFormat().Read("""{"note":"hi","turn":0,"rolls":[0,180]}"""), schemas, "doc");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        return (session, editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "doc")));
    }

    // A126 found a number chosen from a list, such as a roll of 90, was saved as the text "90" and refused.
    [Fact]
    public void A_number_chosen_from_a_list_is_saved_as_a_number()
    {
        var (session, editor, inspector) = Socket();
        Assert.True(editor.Select("/rolls"));
        var first = inspector.Rows()[0];
        Assert.Equal(PropertyKind.Choice, first.Kind);
        Assert.True(inspector.Set(first.Id, "90"), string.Join(" ", inspector.LastIssues));
        Assert.Equal(90, session.Current.At("/rolls/0")!.Value.GetInt32());
        Assert.False(inspector.Set(first.Id, "45"));
        Assert.Equal(90, session.Current.At("/rolls/0")!.Value.GetInt32());
    }

    // A126 found plain fields at the top of a file, such as a game's story notes, couldn't be selected at all.
    [Fact]
    public void A_files_own_plain_fields_can_be_selected_and_edited()
    {
        var (session, editor, inspector) = Socket();
        Assert.Contains("", DataEditing.Outline.ObjectIds(session.Current));
        Assert.Equal("(top level)", DataEditing.Outline.Label(session.Current, ""));
        Assert.True(editor.Select(""));
        var rows = inspector.Rows();
        Assert.Contains(rows, r => r.Id == "note");
        Assert.True(inspector.Set("note", "there"), string.Join(" ", inspector.LastIssues));
        Assert.Equal("there", session.Current.At("/note")!.Value.GetString());
        Assert.True(inspector.Set("turn", "90"), string.Join(" ", inspector.LastIssues));
        Assert.Equal(90, session.Current.At("/turn")!.Value.GetInt32());
        Assert.True(session.Undo());
        Assert.Equal(0, session.Current.At("/turn")!.Value.GetInt32());
    }

    // A126 found Reveal and search still couldn't reach a field of the file itself.
    [Fact]
    public void Reveal_reaches_a_files_own_plain_field()
    {
        var (session, editor, inspector) = Socket();
        var window = new EditorWindow<DataDocument>(editor, inspector);
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(window.DrawDocked, 2);
        Assert.True(window.Reveal("/note"), window.Status);
        Assert.Equal("", editor.Primary);
        host.Frames(window.DrawDocked, 2);
        Assert.True(window.Reveal("/rolls/1"), window.Status);
        Assert.Equal("/rolls", editor.Primary);
    }
}
