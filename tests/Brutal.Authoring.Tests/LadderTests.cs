using Brutal.Authoring;
using Brutal.Authoring.Editor;
using Ladder;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class LadderTests
{
    private static (EditorModel<Notebook> Editor, Inspector<Notebook> Inspector) Describe()
    {
        var e = LadderHost.Open();
        return (e, Rung1Describe.Register(new Inspector<Notebook>(e)));
    }
    private static (EditorModel<Notebook> Editor, Inspector<Notebook> Inspector, Shortcuts Shortcuts) Extension()
    {
        var e = LadderHost.Open(); var i = new Inspector<Notebook>(e); var s = new Shortcuts();
        NotebookExtension.Register(e, i, s); return (e, i, s);
    }

    [Fact]
    public void Rung1LabelEditUsesTypedCommandAndOneUndo()
    {
        var (e, i) = Describe(); e.Select("counter");
        Assert.True(i.Set("label", "Renamed"));
        Assert.Equal("Renamed", e.Session.Current.FindCounter("counter")!.Label);
        Assert.Equal("ladder.label", Assert.Single(e.Session.Journal).Command);
        Assert.True(e.Session.Undo()); Assert.Equal("Example", e.Session.Current.FindCounter("counter")!.Label);
    }

    [Fact]
    public void Rung1MultiEditIsOneUndoStep()
    {
        var d = Notebook.Starter() with { Counters = [new("a","First",1), new("b","Second",2)] };
        var e = LadderHost.Open(d); var i = Rung1Describe.Register(new Inspector<Notebook>(e)); e.Select("a","b");
        Assert.True(i.Set("value", 7)); Assert.All(e.Session.Current.Counters, c => Assert.Equal(7,c.Value));
        Assert.Equal(1,e.Session.History.UndoCount); Assert.True(e.Session.Undo()); Assert.Equal(d,e.Session.Current);
    }

    [Fact]
    public void Rung1ValidationRefusesOutOfRangeValueWithObjectAndReason()
    {
        var (e,i) = Describe(); e.Select("counter"); var before = e.Session.Current;
        Assert.False(i.Set("value",101)); Assert.Same(before,e.Session.Current); Assert.Empty(e.Session.Journal);
        Assert.Contains(i.LastIssues,x=>x.ObjectId=="counter"&&x.Path=="$.counters[0].value"&&x.Message.Contains("100",StringComparison.Ordinal));
    }

    [Fact]
    public void Rung1IdsAreReadOnly()
    {
        var (e,i) = Describe(); e.Select("counter");
        Assert.False(i.Rows().Single(r=>r.Id=="id").Editable); Assert.False(i.Set("id","other"));
        Assert.NotEmpty(i.LastIssues); Assert.Equal("counter",Assert.Single(e.Session.Current.Counters).Id);
    }

    [Fact]
    public void Rung1ResetUsesAnOrdinaryUndoableCommand()
    {
        var (e,i) = Describe(); e.Select("counter"); Assert.True(i.Rows().Single(r=>r.Id=="value").Overridden);
        Assert.True(i.Reset("value")); Assert.Equal(0,e.Session.Current.FindCounter("counter")!.Value);
        Assert.False(i.Rows().Single(r=>r.Id=="value").Overridden); Assert.True(e.Session.Undo());
        Assert.Equal(3,e.Session.Current.FindCounter("counter")!.Value);
    }

    [Fact]
    public void Rung1DescriptionSuppliesKindsAndHelpWithoutCustomUi()
    {
        var (e,i) = Describe(); e.Select("counter"); var rows=i.Rows();
        Assert.Equal(["id","label","value"],rows.Select(r=>r.Id));
        Assert.Equal(PropertyKind.Integer,rows[2].Kind); Assert.Contains("0 to 100",rows[2].Help);
    }

    [Fact]
    public void Rung2RegistersItsOneClassForPaletteSearch()
    {
        var e=LadderHost.Open();DoubleCounter.Register(e.Session.Catalog);
        Assert.Equal(DoubleCounter.CommandId,Assert.Single(e.Search("Double counter")).Command.Id);
        Assert.Equal(DoubleCounter.CommandId,Assert.Single(e.Search("ladder.double")).Command.Id);
    }

    [Fact]
    public void Rung2ExecutesThroughTheRealSession()
    {
        var e=LadderHost.Open();DoubleCounter.Register(e.Session.Catalog);
        Assert.True(e.Execute(new DoubleCounter("counter"),"counter"));
        Assert.Equal(6,e.Session.Current.FindCounter("counter")!.Value);Assert.True(e.Session.Dirty);
        Assert.Equal(DoubleCounter.CommandId,Assert.Single(e.Session.Journal).Command);
    }

    [Fact]
    public void Rung2MissingCounterRefusesWithReasonAndNoChanges()
    {
        var e=LadderHost.Open();DoubleCounter.Register(e.Session.Catalog);var before=e.Session.Current;
        Assert.False(e.Execute(new DoubleCounter("missing"),"missing"));
        Assert.Same(before,e.Session.Current);Assert.False(e.Session.Dirty);Assert.Empty(e.Session.Journal);
        Assert.Empty(e.Refused);Assert.NotEmpty(e.Session.LastIssues);Assert.Contains(e.Session.LastIssues,x=>x.ObjectId=="missing");
    }

    [Fact]
    public void Rung2CommandItselfRefusesMissingTargetWithHelpfulReason()
    {
        var result=new DoubleCounter("missing").Apply(Notebook.Starter());
        Assert.False(result.Ok);Assert.Contains(result.Issues,x=>x.ObjectId=="missing"&&x.Message.Contains("Choose",StringComparison.Ordinal));
    }

    [Fact]
    public void Rung2SharedValidatorCanRefuseAnOtherwiseConstructibleEdit()
    {
        var e=LadderHost.Open(new Notebook([new("counter","Large",60)],[]));DoubleCounter.Register(e.Session.Catalog);
        Assert.False(e.Execute(new DoubleCounter("counter"),"counter"));
        Assert.Equal(60,e.Session.Current.FindCounter("counter")!.Value);Assert.Empty(e.Session.Journal);
        Assert.Contains(e.Session.LastIssues,x=>x.ObjectId=="counter"&&x.Path=="$.counters[0].value"&&x.Message.Contains("100",StringComparison.Ordinal));
    }

    [Fact]
    public void Rung2UndoAndRedoRestoreTheExpectedValues()
    {
        var e=LadderHost.Open();DoubleCounter.Register(e.Session.Catalog);
        e.Execute(new DoubleCounter("counter"),"counter");Assert.True(e.Session.Undo());
        Assert.Equal(3,e.Session.Current.FindCounter("counter")!.Value);Assert.True(e.Session.Redo());
        Assert.Equal(6,e.Session.Current.FindCounter("counter")!.Value);
    }

    [Fact]
    public void Rung3RegistersBothObjectTypesWithTheSameInspector()
    {
        var (e,i,_)=Extension();e.Select("counter");Assert.Equal("ladder.counter",i.SelectedType!.Id);
        Assert.True(e.Execute(new NotebookExtension.AddNote("note","A note")));
        e.Select("note");Assert.Equal("ladder.note",i.SelectedType!.Id);Assert.Equal("A note",Assert.Single(i.Rows()).Value);
    }

    [Fact]
    public void Rung3WorkspaceCommandsLeadThePalette()
    {
        var (e,_,_)=Extension();e.Use(NotebookExtension.WorkspaceId);
        string[] first=e.Search("").Take(3).Select(r=>r.Command.Id).ToArray();
        Assert.Equal(["ladder.note_add","ladder.note_label",DoubleCounter.CommandId],first);
        Assert.Contains(e.Search(""),r=>r.Command.Id=="ladder.label");
    }

    [Fact]
    public void Rung3WorkspaceSwitchIsViewStateAndSharesTheSession()
    {
        var (e,_,_)=Extension();var session=e.Session;e.Use(NotebookExtension.WorkspaceId);
        Assert.Same(session,e.Session);Assert.False(session.Dirty);Assert.Empty(session.Journal);
        Assert.False(session.History.CanUndo);e.Use(null);Assert.Null(e.Active);
    }

    [Fact]
    public void Rung3InspectorEditsItsNewTypeThroughTheSession()
    {
        var (e,i,_)=Extension();e.Execute(new NotebookExtension.AddNote("note","Before"));e.Select("note");
        Assert.True(i.Set("label","After"));Assert.Equal("After",Assert.Single(e.Session.Current.Notes).Label);
        Assert.Equal("ladder.note_label",e.Session.Journal[^1].Command);
        Assert.True(e.Session.Undo());Assert.Equal("Before",Assert.Single(e.Session.Current.Notes).Label);
    }

    [Fact]
    public void Rung3DuplicateIdIsRefusedByTheSharedValidator()
    {
        var (e,_,_)=Extension();var before=e.Session.Current;
        Assert.False(e.Execute(new NotebookExtension.AddNote("counter","Collision")));
        Assert.Same(before,e.Session.Current);Assert.Empty(e.Session.Journal);Assert.NotEmpty(e.Session.LastIssues);
    }

    [Fact]
    public void Rung3RenameOfMissingNoteRefusesWithReason()
    {
        var (e,_,_)=Extension();
        Assert.False(e.Execute(new NotebookExtension.RenameNote("missing","Title")));
        Assert.Contains(e.Session.LastIssues,x=>x.ObjectId=="missing");Assert.Empty(e.Session.Journal);
    }

    [Fact]
    public void Rung3NewInspectorTypeHonorsExistingFocusLock()
    {
        var (e,i,_)=Extension();e.Execute(new NotebookExtension.AddNote("note","Before"));
        e.FocusOn("counter");e.Select("note");long revision=e.Session.Revision;
        Assert.False(i.Set("label","After"));Assert.NotEmpty(i.LastIssues);Assert.Equal(revision,e.Session.Revision);
        Assert.Equal("Before",Assert.Single(e.Session.Current.Notes).Label);
    }

    [Fact]
    public void Rung3ShortcutDefaultIsOptionalAndUserPreferenceWins()
    {
        var e=LadderHost.Open();var i=new Inspector<Notebook>(e);var s=new Shortcuts();
        s.Set(DoubleCounter.CommandId,"Alt+K");NotebookExtension.Register(e,i,s);
        Assert.Equal(DoubleCounter.CommandId,s.Action(Chord.Parse("Alt+K")));
        s.ResetToDefault(DoubleCounter.CommandId);Assert.Equal(DoubleCounter.CommandId,s.Action(Chord.Parse("Alt+D")));
    }

    [Fact]
    public void Rung3MixedJournalReplaysToIdenticalCanonicalText()
    {
        var (e,i,_)=Extension();var basis=e.Session.Current;
        e.Execute(new DoubleCounter("counter"),"counter");e.Execute(new NotebookExtension.AddNote("note","First"));
        e.Select("note");Assert.True(i.Set("label","Final"));Assert.True(e.Session.Undo());Assert.True(e.Session.Redo());
        var replay=DocumentSession<Notebook>.Replay(basis,JournalFile.Read(JournalFile.Write(e.Session.Journal)),
            e.Session.Format,e.Session.Validator,e.Session.Catalog);
        Assert.Equal(e.Session.Format.Write(e.Session.Current),e.Session.Format.Write(replay.Current));
    }

    [Fact]
    public void SharedFormatRoundTripsBothTypesWithoutChangingCanonicalText()
    {
        var d=new Notebook([new("counter","Quote \" / newline\n",3)],[new("note","Note")]);
        var f=new NotebookFormat();string text=f.Write(d);Assert.Equal(text,f.Write(f.Read(text)));
        Assert.EndsWith("\n",text);Assert.DoesNotContain("\r",text);
    }

    [Fact]
    public void SharedFormatStrictlyRefusesUnknownFields()
    {
        var f=new NotebookFormat();string text=f.Write(Notebook.Starter()).Replace("\"version\": 1,","\"version\": 1, \"extra\": true,",StringComparison.Ordinal);
        var error=Assert.Throws<DocumentFormatException>(()=>f.Read(text));Assert.Equal("$.extra",error.Path);
    }
}
