using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

// The Outliner marks prefab copies with colour, an icon and a dot, never bracketed words; assistive tools hear plain words.
[Collection(ImGuiCollection.Name)]
public sealed class OutlinerPrefabMarksTests
{
    private static EditorWindow<Scene2> W(DataWorkspace w)=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
    private static UiNode Row(string startsWith)
    {
        var rows=Ui.Snapshot().Nodes.Where(n=>n.Role==UiRole.ListItem).ToArray();
        return rows.SingleOrDefault(n=>n.Label.TrimStart('>',' ').StartsWith(startsWith+",",StringComparison.Ordinal)) is {Label.Length:>0} row?row:throw new Xunit.Sdk.XunitException($"No row {startsWith}; rows: "+string.Join("; ",rows.Select(n=>n.Label)));
    }
    [Fact]
    public void Copy_unchanged_part_and_changed_part_speak_plain_words_and_draw_no_bracketed_text()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"),workspace.Said);
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,5);
        var copy=Row("frame_a");var unchanged=Row("marker");var changed=Row("frame");
        Assert.EndsWith(", copy of frame",copy.Label);Assert.Equal("Copy of frame",copy.Tooltip);
        Assert.EndsWith(", part of frame copy",unchanged.Label);Assert.Equal("Part of frame copy, unchanged",unchanged.Tooltip);
        Assert.EndsWith(", changed in this copy",changed.Label);Assert.Equal("Changed in this copy: locked",changed.Tooltip);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.Contains("[instance]")||n.Label.Contains("[inherited]")||n.Label.Contains("[local changes]"));
        // The drawn caption is the name alone: the two rows' measured widths keep the names' ratio, not the spoken labels'.
        Assert.NotNull(unchanged.TextEvidence);Assert.NotNull(changed.TextEvidence);
        Assert.Equal(ImGui.CalcTextSize("marker").X/ImGui.CalcTextSize("> frame").X,unchanged.TextEvidence!.Value.FullBounds.Width/changed.TextEvidence!.Value.FullBounds.Width,2);
        Assert.True(W(workspace).Inspector is not null&&W(workspace).Editor.Select("frame_a_marker"));ui.Frames(workspace.Draw,2);
        Assert.True(W(workspace).Inspector!.Set("position.0",4d));ui.Frames(workspace.Draw,2);
        Assert.Equal("Changed in this copy: position",Row("marker").Tooltip);Assert.EndsWith(", changed in this copy",Row("marker").Label);
    }
    [Fact]
    public void Field_names_read_as_plain_words()
    {
        Assert.Equal("position X",OutlineMark.FieldName("/position/0"));Assert.Equal("scale Z",OutlineMark.FieldName("/scale/2"));
        Assert.Equal("locked",OutlineMark.FieldName("/properties/locked"));Assert.Equal("a/b",OutlineMark.FieldName("/properties/a~1b"));
        Assert.Equal("target reference",OutlineMark.FieldName("/references/target"));
    }
    [Fact]
    public void Flat_level_uses_the_same_outliner_marks()
    {
        using var r=new Scene2HostCommandTests.Rig("scene-2d.example.json");using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"),workspace.Said);
        Assert.Equal(SceneSpace.TwoDXY,r.Entry.Session.Current.Space);Assert.NotNull(W(workspace).OutlineMarks);Assert.Null(W(workspace).OutlineBadges);
    }
}
