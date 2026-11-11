using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class OutlinerSelectionA11y05Tests
{
    private sealed class Sink:IAccessKitUpdateSink
    {
        public readonly List<AccessKitProjectedNode> Statuses=[];
        public bool TryApply(AccessKitProjectedUpdate update)
        {Statuses.AddRange(update.ChangedNodes.Where(n=>n.Role==AccessKitProjectedRole.Status));return true;}
    }
    [Fact]
    public void Count_changes_are_one_stable_status_and_unchanged_counts_are_not_announced_again()
    {
        using var host=new HeadlessHost(1300,950){Drawing=false};
        EditorWindow<Scene>.Configure(ImGui.GetIO());
        var scene=new Scene([SceneObject.At("a",null,default),SceneObject.At("b",null,default),SceneObject.At("c",null,default)]);
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);
        var editor=new EditorModel<Scene>(session,SceneEditing.Outline);
        var window=new EditorWindow<Scene>(editor,new Inspector<Scene>(editor),new Shortcuts());
        var projection=new AccessKitProjection();var sink=new Sink();
        void Frame(int count=1)
        {
            for(var i=0;i<count;i++)
            {host.Frames(window.Draw,1);projection.Submit(Ui.Snapshot(),"Selection fixture",new(0,0,1300,950),1,1,sink);}
        }
        host.Frames(window.Draw,4);Frame();
        var status=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status);
        Assert.Equal("0 objects selected",status.Label);Assert.Equal(UiIdKind.Semantic,status.IdKind);
        var native=Assert.Single(sink.Statuses);Assert.True(native.StableIdentity);
        void Check(string text,int changes)
        {
            var current=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status);
            Assert.Equal(text,current.Label);Assert.Equal(status.Id,current.Id);
            Assert.Equal(changes,sink.Statuses.Count);Assert.All(sink.Statuses,n=>Assert.Equal(native.Id,n.Id));
        }
        Frame(5);Check("0 objects selected",1);
        editor.Select("a");Frame(5);Check("1 object selected",2);
        editor.Select("a","b","c");Frame(5);Check("3 objects selected",3);
        editor.Select("a","b");Frame(5);Check("2 objects selected",4);
        editor.Select("b","c");Frame(5);Check("2 objects selected",4);
        editor.Select("c","b");Frame(5);Check("2 objects selected",4);
        editor.Select();Frame(5);Check("0 objects selected",5);
        Assert.False(session.Dirty);Assert.Empty(session.Journal);Assert.False(session.History.CanUndo);
    }
}
