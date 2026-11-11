using System.Reflection;
using System.Text.Json.Nodes;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class Scene2UpdatePagingTests
{
    private static EditorWindow<Scene2> Window(DataWorkspace w)=>(EditorWindow<Scene2>)typeof(DataWorkspace).GetProperty("Scene2Window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
    private static UiNode Button(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label==label);
    private static void Click(HeadlessHost host,Action draw,string label)
    {var n=Button(label);Assert.True(n.Enabled,label);Assert.False(n.Bounds.IsEmpty,label);host.Click(draw,n.Bounds.X+n.Bounds.Width/2,n.Bounds.Y+n.Bounds.Height/2);host.Frames(draw,2);}
    [Theory][InlineData(1f)][InlineData(2f)]
    public void All_changes_after_sixty_four_are_reachable_and_paging_is_not_an_edit(float scale)
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));Window(workspace).Editor.Select("frame_a_root");
        var source=JsonNode.Parse(File.ReadAllText(r.Source))!;var nodes=source["nodes"]!.AsArray();
        for(var i=1;i<=70;i++){var node=nodes[1]!.DeepClone();node["key"]=i.ToString("x32");node["label"]="Added node "+i;node["references"]=new JsonObject();nodes.Add(node);}File.WriteAllText(r.Source,source.ToJsonString());
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;ui.Frames(workspace.Draw,4);var before=r.Entry.Session.Current;
        Click(ui,workspace.Draw,"Review prefab update");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Changes page 1 of 3 (70 changes).");Assert.False(Button("Previous changes").Enabled);
        Click(ui,workspace.Draw,"Next changes");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Changes page 2 of 3 (70 changes).");
        Click(ui,workspace.Draw,"Next changes");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Changes page 3 of 3 (70 changes).");Assert.False(Button("Next changes").Enabled);
        Assert.Equal(6,Ui.Snapshot().Nodes.Count(n=>n.Role==UiRole.Text&&n.Label.StartsWith("Added ",StringComparison.Ordinal)));Assert.Same(before,r.Entry.Session.Current);Assert.Empty(r.Entry.Session.Journal);
        Click(ui,workspace.Draw,"Previous changes");Click(ui,workspace.Draw,"Cancel update");Assert.Same(before,r.Entry.Session.Current);Assert.Empty(r.Entry.Session.Journal);
        Click(ui,workspace.Draw,"Review prefab update");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Changes page 1 of 3 (70 changes).");Click(ui,workspace.Draw,"Accept update");Assert.Single(r.Entry.Session.Journal);Assert.Equal(Scene2Resolver.Resolve(before).Objects.Length+70,Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Length);
    }
    [Fact]
    public void Long_value_tail_is_browsable_without_shortening_the_captured_update_and_text_pages_reset()
    {
        using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));Window(workspace).Editor.Select("frame_a_root");
        var source=JsonNode.Parse(File.ReadAllText(r.Source))!;var value=new string('x',1600)+"TAIL_BROWSE";source["nodes"]![1]!["properties"]!["detail"]=value;File.WriteAllText(r.Source,source.ToJsonString());
        using var ui=new HeadlessHost(1400,1000){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());ui.Frames(workspace.Draw,4);var original=r.Entry.Session.Current;
        Click(ui,workspace.Draw,"Review prefab update");Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("TAIL_BROWSE"));Assert.False(Button("Previous text").Enabled);
        Click(ui,workspace.Draw,"Next text");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("TAIL_BROWSE"));Assert.False(Button("Next text").Enabled);Assert.Empty(r.Entry.Session.Journal);Assert.Same(original,r.Entry.Session.Current);
        Click(ui,workspace.Draw,"Cancel update");Click(ui,workspace.Draw,"Review prefab update");Assert.False(Button("Previous text").Enabled);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("TAIL_BROWSE"));
        Click(ui,workspace.Draw,"Accept update");Assert.Single(r.Entry.Session.Journal);Assert.Equal(value,Scene2Resolver.Resolve(r.Entry.Session.Current).Objects.Single(o=>o.Object.Id=="frame_a_marker").Object.Properties["detail"].GetString());
    }
}
