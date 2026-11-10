using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Brutal.Authoring.Editor;
using Xunit;
using Xunit.Abstractions;
namespace Brutal.Authoring.Tests;

public sealed class MoveCommitQa15Tests(ITestOutputHelper output)
{
    [Fact] public void Profile_five_thousand_move_commit_and_preserve_exact_journal_replay_undo_redo()
    {
        var scene=new Scene(Enumerable.Range(0,5000).Select(i=>SceneObject.At("object_"+i,null,new(i%100,i/100,0))).ToImmutableArray());
        var catalog=SceneKit.Catalog();var commands=scene.Objects.Select(o=>(ICommand<Scene>)new MoveObject(o.Id,o.Position+Vector3.UnitX)).ToArray();
        var phases=new Dictionary<string,double>();var watch=Stopwatch.StartNew();var candidate=scene;
        foreach(var command in commands)candidate=command.Apply(candidate).Document!;
        phases["sequential reference application"]=watch.Elapsed.TotalMilliseconds;watch.Restart();
        Assert.Empty(new SceneValidator().Validate(candidate));phases["validation"]=watch.Elapsed.TotalMilliseconds;watch.Restart();
        var expected=commands.Select(catalog.ToEntry).ToArray();phases["journal entries"]=watch.Elapsed.TotalMilliseconds;
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),catalog,scene);
        watch.Restart();Assert.True(session.ExecuteBatch(commands));phases["complete commit"]=watch.Elapsed.TotalMilliseconds;
        Assert.Equal(1,session.History.UndoCount);var batch=Assert.Single(session.Journal);Assert.Equal("batch",batch.Op);Assert.Equal(5000,batch.Batch!.Count);
        Assert.Equal(JournalFile.Write([JournalEntry.Of(expected)]),JournalFile.Write(session.Journal));
        for(var i=0;i<5000;i++)Assert.Equal(scene.Objects[i].Position+Vector3.UnitX,session.Current.Objects[i].Position);
        var replay=DocumentSession<Scene>.Replay(scene,session.Journal,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());
        Assert.Equal(new SceneFormat().Write(session.Current),new SceneFormat().Write(replay.Current));Assert.Equal(1,replay.History.UndoCount);
        var final=session.Current;Assert.True(session.Undo());Assert.Same(scene,session.Current);Assert.True(session.Redo());Assert.Same(final,session.Current);
        foreach(var (name,ms) in phases)output.WriteLine($"QA15 {name}: {ms:F3} ms");
    }
    [Theory][InlineData("repeat")][InlineData("missing")][InlineData("nonfinite")][InlineData("intermediate-nonfinite")][InlineData("mixed")]
    public void Batch_matches_sequential_commands_including_repeats_refusals_and_mixed_fallback(string mode)
    {
        var scene=new Scene(Enumerable.Range(0,32).Select(i=>SceneObject.At("object_"+i,null,new(i,0,0))).ToImmutableArray());
        var random=new Random(15092026);var commands=new List<ICommand<Scene>>();
        for(var i=0;i<80;i++)commands.Add(new MoveObject("object_"+random.Next(32),new(random.Next(-20,20),i,0)));
        if(mode=="missing")commands.Insert(17,new MoveObject("gone",Vector3.One));
        if(mode is "nonfinite" or "intermediate-nonfinite")commands.Add(new MoveObject("object_0",new(float.NaN,0,0)));
        if(mode=="intermediate-nonfinite")commands.Add(new MoveObject("object_0",Vector3.One));
        if(mode=="mixed")commands.Insert(23,new SetAsset("object_0","self-authored"));
        // A no-op non-move command forces the unchanged general batch path, including validation and journal admission.
        var reference=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);
        var accepted=reference.ExecuteBatch([new SetAsset("object_0",null),..commands]);var expected=reference.Current;var issues=reference.LastIssues;
        var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);
        Assert.Equal(accepted,session.ExecuteBatch(commands));Assert.Equal(issues.Select(i=>i.Message),session.LastIssues.Select(i=>i.Message));
        if(!accepted){Assert.Same(scene,session.Current);Assert.Empty(session.Journal);Assert.Equal(0,session.History.UndoCount);return;}
        Assert.Equal(new SceneFormat().Write(expected),new SceneFormat().Write(session.Current));Assert.Equal(1,session.History.UndoCount);
        Assert.Equal(JournalFile.Write([JournalEntry.Of(commands.Select(session.Catalog.ToEntry).ToArray())]),JournalFile.Write(session.Journal));
    }
    private sealed class HiddenLabelOutline:IDocumentOutline<Scene>
    {
        public IEnumerable<string> ObjectIds(Scene scene)=>scene.Objects.Select(o=>o.Id);
        public string? Label(Scene scene,string id)=>id=="object_0"?null:scene.Find(id)?.Id;
        public string? Parent(Scene scene,string id)=>null;
    }
    [Fact] public void Builtin_missing_identity_and_custom_null_label_still_refuse_guarded_commits()
    {
        var scene=new Scene([SceneObject.At("object_0",null,Vector3.Zero)]);
        foreach(var outline in new IDocumentOutline<Scene>[]{SceneEditing.Outline,new HiddenLabelOutline()})
        {
            var session=new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene);var editor=new EditorModel<Scene>(session,outline);
            var touch=ReferenceEquals(outline,SceneEditing.Outline)?"gone":"object_0";
            Assert.False(editor.ExecuteAt(0,[new MoveObject("object_0",Vector3.One)],[touch]));Assert.Same(scene,session.Current);Assert.Empty(session.Journal);Assert.Equal(0,session.History.UndoCount);
        }
    }
    [Fact] public void Duplicate_id_repair_scene_keeps_original_replace_all_semantics()
    {
        var folder=Directory.CreateTempSubdirectory("move-batch-repair-qa15-").FullName;
        try
        {
            var scene=new Scene([SceneObject.At("same","first",Vector3.Zero),SceneObject.At("same","second",new(10,0,0))]);var path=Path.Combine(folder,"level.json");File.WriteAllText(path,new SceneFormat().Write(scene));
            var session=DocumentSession<Scene>.OpenForRepair(path,new SceneFormat(),new SceneValidator(),SceneKit.Catalog());Assert.True(session.Repairing);
            Assert.True(session.ExecuteBatch([new MoveObject("same",Vector3.UnitX),new MoveObject("same",Vector3.UnitY)]));
            Assert.All(session.Current.Objects,o=>{Assert.Equal(Vector3.UnitY,o.Position);Assert.Equal("first",o.Asset);});Assert.Equal(1,session.History.UndoCount);Assert.True(session.Undo());Assert.Equal(new SceneFormat().Write(scene),new SceneFormat().Write(session.Current));
        }
        finally{Directory.Delete(folder,true);}
    }

}
