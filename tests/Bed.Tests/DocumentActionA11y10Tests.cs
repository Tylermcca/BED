using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DocumentActionA11y10Tests
{
    private sealed class Sink:IAccessKitUpdateSink
    {
        public readonly List<AccessKitProjectedUpdate> Updates=[];
        public bool TryApply(AccessKitProjectedUpdate update){Updates.Add(update);return true;}
        public int Count(string label)=>Updates.SelectMany(u=>u.ChangedNodes).Count(n=>n.Role==AccessKitProjectedRole.Status&&n.Label==label);
    }
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("document-actions-a11y10-").FullName;
        public readonly HeadlessHost Host=new(1400,1000){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly AccessKitProjection Projection=new();public readonly Sink Sink=new();
        public Rig(bool scene)
        {
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(data,"items.json"),"""{"items":[{"price":1}]}""");
            File.WriteAllText(Path.Combine(schemas,"items.schema.json"),"""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"price":{"type":"number"}}}}}}""");
            File.WriteAllText(Path.Combine(data,"level.json"),new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)])));
            Workspace=new(DataProject.Open(data,schemas));Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));
            Assert.True(Workspace.Show(scene?"level.json":"items.json"));EditorWindow<Scene>.Configure(ImGui.GetIO());Frame(4);
        }
        public void Frame(int count=1)
        {for(var i=0;i<count;i++){Host.Frames(Workspace.Draw,1);Projection.Submit(Ui.Snapshot(),"Document action fixture",new(0,0,1400,1000),1,1,Sink);}}
        public void Key(ImGuiKey key,bool shift=false)
        {Host.Press(Workspace.Draw,key,ctrl:true,shift:shift);Frame(2);}
        public void AnnouncedOnce(string message)
        {
            Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==message);
            Assert.Equal(1,Sink.Count(message));Frame(5);Assert.Equal(1,Sink.Count(message));
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Native_save_undo_redo_name_the_file_and_command_once_per_action(bool scene)
    {
        using var r=new Rig(scene);
        if(scene)Assert.True(r.Workspace.SceneWindow!.Editor.Session.Execute(new MoveObject("box",Vector3.UnitX)));
        else Assert.True(r.Workspace.Window!.Editor.Session.Execute(new SetValue("/items/0/price",JsonSerializer.SerializeToElement(2))));
        r.Frame(2);r.Key(ImGuiKey.S);var saved=scene?"Saved level.json.":"Saved items.json.";r.AnnouncedOnce(saved);
        r.Key(ImGuiKey.Z);r.AnnouncedOnce(scene?"Undid: Move object.":"Undid: Set a value.");
        r.Key(ImGuiKey.Y);r.AnnouncedOnce(scene?"Redid: Move object.":"Redid: Set a value.");
        // The palette/API can repeat a clean Save although the toolbar is disabled: still one new announcement.
        Assert.True(scene?r.Workspace.SceneWindow!.Run("editor.save"):r.Workspace.Window!.Run("editor.save"));
        r.Frame(2);
        var repeated=scene?"Nothing to save.":saved;
        if(scene)
        {
            r.AnnouncedOnce(repeated);Assert.Equal(1,r.Sink.Count(saved));
            Assert.True(r.Workspace.SceneWindow!.Run("editor.save"));r.Frame(2);
        }
        Assert.Equal(2,r.Sink.Count(repeated));r.Frame(5);Assert.Equal(2,r.Sink.Count(repeated));
    }
    [Fact]
    public void Batch_and_truncated_history_are_described_without_inventing_a_command_name()
    {
        using var r=new Rig(false);var w=r.Workspace.Window!;var session=w.Editor.Session;
        Assert.True(session.ExecuteBatch([new SetValue("/items/0/price",JsonSerializer.SerializeToElement(2)),new SetValue("/items/0/price",JsonSerializer.SerializeToElement(3))]));
        Assert.True(w.Run("editor.undo"));r.Frame(2);r.AnnouncedOnce("Undid: 2 changes.");
        Assert.True(w.Run("editor.redo"));r.Frame(2);r.AnnouncedOnce("Redid: 2 changes.");
        session.JournalLimit=1;
        Assert.True(session.Execute(new SetValue("/items/0/price",JsonSerializer.SerializeToElement(4))));
        Assert.True(w.Run("editor.undo"));r.Frame(2);r.AnnouncedOnce("Undid a change.");
    }
    [Fact]
    public void Empty_history_and_refused_save_never_announce_a_completed_action()
    {
        using var r=new Rig(false);var w=r.Workspace.Window!;
        Assert.False(w.Run("editor.undo"));r.Frame(2);r.AnnouncedOnce("Nothing to undo.");
        w.SaveAction=()=> (false,"Save refused by the fixture.");Assert.False(w.Run("editor.save"));r.Frame(2);r.AnnouncedOnce("Save refused by the fixture.");
        Assert.DoesNotContain(r.Sink.Updates.SelectMany(u=>u.ChangedNodes),n=>n.Role==AccessKitProjectedRole.Status&&n.Label?.StartsWith("Saved ",StringComparison.Ordinal)==true);
    }
}


