using System.Text.Json;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class OutlinerFilterUi32Tests
{
    [Theory]
    [InlineData("LANDING PAD","Landing pad")]
    [InlineData("PuRpOsE","group_root,ship_alpha,engine_child")]
    [InlineData("RESCUE CAFÉ","group_root,ship_alpha,engine_child")]
    [InlineData("42.5","group_root,ship_alpha,engine_child")]
    [InlineData("true","group_root,ship_alpha,engine_child")]
    [InlineData("FALSE","group_root,ship_alpha,engine_child")]
    public void Keyboard_filter_matches_names_and_scalar_property_keys_and_values_without_editing(string query,string expected)
    {
        using var r=new OutlinerSearchUi17Tests.Rig(properties:true);r.FocusOutliner();
        var selected=r.Window.Editor.Selection.ToArray();r.Query(query);
        Assert.Equal(expected.Split(','),r.Rows());Assert.Equal(selected,r.Window.Editor.Selection);r.Unchanged();
        r.Key(ImGuiKey.Escape);Assert.Equal("",r.Search().Value);Assert.Equal(5,r.Rows().Length);
        Assert.Equal(selected,r.Window.Editor.Selection);r.Unchanged();
    }
    [Fact]
    public void Property_edit_undo_and_redo_refresh_the_active_filter_without_changing_selection()
    {
        using var r=new OutlinerSearchUi17Tests.Rig(properties:true);r.FocusOutliner();r.Query("medical");Assert.Empty(r.Rows());
        var selected=r.Window.Editor.Selection.ToArray();var session=r.Window.Editor.Session;
        Assert.True(session.Execute(new SetSceneProperty("engine_child","purpose",JsonSerializer.SerializeToElement("Medical bay"))));
        r.Host.Frames(r.Workspace.Draw,3);Assert.Equal(new[]{"group_root","ship_alpha","engine_child"},r.Rows());
        Assert.True(session.Undo());r.Host.Frames(r.Workspace.Draw,3);Assert.Empty(r.Rows());
        Assert.True(session.Redo());r.Host.Frames(r.Workspace.Draw,3);Assert.Equal(new[]{"group_root","ship_alpha","engine_child"},r.Rows());
        Assert.Equal(selected,r.Window.Editor.Selection);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
    }
}
