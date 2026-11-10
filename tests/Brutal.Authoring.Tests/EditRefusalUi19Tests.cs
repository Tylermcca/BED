using Brutal.Authoring.Editor;
using Toy;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class EditRefusalUi19Tests
{
    [Fact] public void Adapter_style_policy_checks_single_batch_and_revision_guarded_commands_before_commit_but_history_stays_usable()
    {
        var session=BoardKit.Session(new Board([new Item("a","A",0,0),new Item("b","B",1,0)]));
        var editor=new EditorModel<Board>(session,new FlatOutline<Board>(b=>b.Items.Select(i=>(i.Id,i.Label))));
        Assert.Null(editor.EditRefusal);var calls=0;Board? seen=null;string[] touched=[];IReadOnlyList<ICommand<Board>> candidates=[];
        editor.EditRefusal=(document,commands,touches)=>
        {
            calls++;seen=document;touched=touches.ToArray();candidates=commands;
            return commands.OfType<MoveItem>().Any(move=>move.X>10)?"The adapter limits X to 10.":null;
        };
        var initial=session.Current;var first=new MoveItem("a",2,0);
        Assert.True(editor.Execute(first,"a"));Assert.Same(initial,seen);Assert.Same(first,Assert.Single(candidates));Assert.Equal(new[]{"a"},touched);
        var accepted=session.Current;Assert.False(editor.Execute(new MoveItem("a",99,0),"a"));Assert.Same(accepted,session.Current);Assert.Equal(1,session.History.UndoCount);
        Assert.Contains(editor.Refused,i=>i.Message=="The adapter limits X to 10.");
        ICommand<Board>[] batch=[new MoveItem("a",3,0),new MoveItem("b",4,0)];
        Assert.True(editor.ExecuteBatch(batch,"a","b"));Assert.Same(batch,candidates);Assert.Equal(new[]{"a","b"},touched);
        var revision=session.Revision;Assert.True(editor.ExecuteAt(revision,[new MoveItem("b",5,0)],"b"));
        var count=calls;Assert.False(editor.ExecuteAt(revision,[new MoveItem("b",6,0)],"b"));Assert.Equal(count,calls);
        Assert.True(session.Undo());Assert.True(session.Redo());Assert.Equal(count,calls);
        editor.EditRefusal=null;Assert.True(editor.Execute(new MoveItem("a",99,0),"a"));Assert.Equal(count,calls);
    }
}
