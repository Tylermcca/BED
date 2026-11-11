using System.Numerics;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

/// <summary>Real key events reach the shortcuts: keys go in as ImGui backend events (as a window's GLFW backend sends
/// them), never by calling HandleKey, and a text field keeps its own keys.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class KeyBridgeTests
{
    private static (EditorModel<Scene> Editor, EditorWindow<Scene> Window) Setup()
    {
        var editor = new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(), new SceneValidator(), SceneKit.Catalog(),
            new Scene([SceneObject.At("room", null, Vector3.Zero), SceneObject.At("crate", null, Vector3.One) with { Parent = "room" }])), SceneEditing.Outline);
        return (editor, new EditorWindow<Scene>(editor, new Inspector<Scene>(editor).Add(SceneEditing.ObjectType()), new Shortcuts(), SceneEditing.CommandFor));
    }

    [Fact]
    public void CtrlShiftPOpensThePaletteAndEscapeClosesIt()
    {
        var (_, window) = Setup();
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked);
        host.Press(window.DrawDocked, ImGuiKey.P, ctrl: true, shift: true);
        Assert.True(window.PaletteOpen);
        host.Press(window.DrawDocked, ImGuiKey.Escape);
        host.Frames(window.DrawDocked, 2);
        Assert.False(window.PaletteOpen);
    }

    [Fact]
    public void CtrlZUndoesAndCtrlYRedoesFromTheKeyboard()
    {
        var (editor, window) = Setup();
        Assert.True(editor.Execute(new MoveObject("crate", new Vector3(5, 0, 0))));
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked);
        host.Press(window.DrawDocked, ImGuiKey.Z, ctrl: true);
        Assert.Equal(Vector3.One, editor.Session.Current.Find("crate")!.Position);
        host.Press(window.DrawDocked, ImGuiKey.Y, ctrl: true);
        Assert.Equal(new Vector3(5, 0, 0), editor.Session.Current.Find("crate")!.Position);
    }

    [Fact]
    public void FFocusesOnTheSelectionAndEscapeStepsOut()
    {
        var (editor, window) = Setup();
        editor.Select("room");
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked);
        host.Press(window.DrawDocked, ImGuiKey.F);
        Assert.Equal("room", editor.FocusedOn);
        host.Press(window.DrawDocked, ImGuiKey.Escape);
        Assert.Null(editor.FocusedOn);
    }

    [Fact]
    public void KeysTypedIntoATextFieldStayInTheField()
    {
        var (editor, window) = Setup();
        editor.Select("room");
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked);
        Assert.True(window.Run("editor.file_save_as"));   // its path field takes the keyboard
        host.Frames(window.DrawDocked, 2);
        host.Type(window.DrawDocked, "f");
        host.Press(window.DrawDocked, ImGuiKey.F);
        Assert.Null(editor.FocusedOn);   // "F" was typing, not the focus shortcut
    }
}
