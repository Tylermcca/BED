using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Toy;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class MenuNavigationTests
{
    private static EditorWindow<Board> Window()
    {
        var session = BoardKit.Session(new Board([new Item("lamp", "Lamp", 1, 2)]));
        var editor = new EditorModel<Board>(session,
            new FlatOutline<Board>(b => b.Items.Select(i => (i.Id, i.Label))));
        return new EditorWindow<Board>(editor);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void File_menu_opens_and_Escape_closes_without_editing(float scale)
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        EditorWindow<Board>.Configure(ImGui.GetIO());
        ImGui.GetStyle().FontScaleMain = scale;
        var window = Window();
        host.Frames(window.DrawDocked, 4);
        var file = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.Menu, "File");
        Assert.True(file.Enabled); Assert.False(file.Bounds.IsEmpty);
        host.Click(window.DrawDocked, file.Bounds.X + file.Bounds.Width / 2, file.Bounds.Y + file.Bounds.Height / 2);
        Assert.True(ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel));
        host.Press(window.DrawDocked, ImGuiKey.Escape);
        host.Frames(window.DrawDocked, 2);
        Assert.False(ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel));
        Assert.False(window.Editor.Session.Dirty);
        Assert.Empty(window.Editor.Session.Journal);
        Assert.Equal(1, window.Editor.Session.Current.Find("lamp")!.X);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Edit_menu_Undo_uses_existing_history(float scale)
    {
        using var host = new HeadlessHost(1280, 800) { Drawing = false };
        EditorWindow<Board>.Configure(ImGui.GetIO());
        ImGui.GetStyle().FontScaleMain = scale;
        var window = Window();
        Assert.True(window.Editor.Execute(new MoveItem("lamp", 9, 2)));
        host.Frames(window.DrawDocked, 4);
        var edit = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.Menu, "Edit");
        Assert.True(edit.Enabled); Assert.False(edit.Bounds.IsEmpty);
        host.Click(window.DrawDocked, edit.Bounds.X + edit.Bounds.Width / 2, edit.Bounds.Y + edit.Bounds.Height / 2);
        Assert.True(ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel));
        var undo = new UiTreeAssertionsA448(Ui.Snapshot()).Find(UiRole.MenuItem, "Undo");
        Assert.True(undo.Enabled); Assert.False(undo.Bounds.IsEmpty);
        var editX = undo.Bounds.X + undo.Bounds.Width / 2;
        var row = undo.Bounds.Y + undo.Bounds.Height / 2;
        host.Click(window.DrawDocked, editX, row);
        Assert.True(window.Editor.Session.Current.Find("lamp")!.X == 1, $"Undo did not run: scale={scale}, x={editX}, y={row}, font={ImGui.GetFontSize()}, status={window.Status}");
        Assert.False(window.Editor.Session.History.CanUndo);
        Assert.True(window.Editor.Session.History.CanRedo);
        Assert.Equal("Undid: Move item.", window.Status);
    }
}
