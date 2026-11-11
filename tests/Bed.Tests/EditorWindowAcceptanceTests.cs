using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Toy;
using Xunit;
using Gui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED neutral board fixtures; MIXED headless context lifecycle from the accepted A007 recipe.
// Uses the existing process-global ImGui collection; no second collection, OS window or renderer.
[Collection(ImGuiCollection.Name)]
public sealed class EditorWindowAcceptanceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public EditorWindowAcceptanceTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;
    private sealed class Outline : IDocumentOutline<Board>
    {
        public IEnumerable<string> ObjectIds(Board d) => d.Items.Select(i=>i.Id);
        public string? Label(Board d,string id) => d.Find(id)?.Label;
        public string? Parent(Board d,string id) => id=="a" ? "room" : null;
    }
    private sealed record Hidden() : ICommand<Board>
    {
        public CommandResult<Board> Apply(Board d)=>CommandResult<Board>.Done(d);
    }
    private static EditorWindow<Board> Window(bool commands=true,bool inspector=true,Shortcuts? keys=null)
    {
        var catalog=BoardKit.Catalog().Register<Hidden>("accept.hidden",1,"Hidden action",isInternal:true);
        var board=new Board([new("room","Room",0,0),new("a","Same name",1,2),new("b","Same name",3,4)]);
        var e=new EditorModel<Board>(new DocumentSession<Board>(BoardKit.Format,BoardKit.Validator,catalog,board),new Outline());
        var i=new Inspector<Board>(e).Add(new ObjectType<Board,Item>("accept.item","Item",(d,id)=>d.Find(id),
            [new("label","Label",PropertyKind.Text,x=>x.Label,(x,v)=>new RenameItem(x.Id,(string)v!)),
             new("x","X",PropertyKind.Number,x=>x.X,(x,v)=>new MoveItem(x.Id,Convert.ToDouble(v),x.Y))]));
        return new EditorWindow<Board>(e,inspector?i:null,keys,
            commands ? (info,model)=>info.Id switch
            {
                "toy.add"=>new AddItem("added","Added",0,0),
                "toy.rename" when model.Primary is { } id=>new RenameItem(id,"Renamed"),
                "toy.remove" when model.Primary is { } id=>new RemoveItem(id),
                _=>null
            } : null);
    }
    private static string State(EditorWindow<Board> w)=>BoardKit.Format.Write(w.Editor.Session.Current);
    private static void Words(string text,params string[] words)
    {
        Assert.False(string.IsNullOrWhiteSpace(text));
        foreach(string word in words)Assert.Contains(word,text,StringComparison.OrdinalIgnoreCase);
    }
    private sealed class Scratch : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"A013-"+Guid.NewGuid().ToString("N"));
        public Scratch()=>Directory.CreateDirectory(Root);
        public string File(string name)=>Path.Combine(Root,name);
        public void Dispose()
        {
            string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(Root).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe scratch.");
            Directory.Delete(Root,true);
        }
    }

    [Fact]
    public void CleanTitleHasNoDirtyMarkerAndAnEditAddsOne()
    {
        var w=Window();Assert.DoesNotContain("*",w.Title);Assert.True(w.Run("toy.add"));
        Assert.Contains("*",w.Title);Assert.True(w.Editor.Session.Dirty);
    }

    [Fact]
    public void UndoToTheInitialSnapshotClearsDirtyMarker()
    {
        var w=Window();w.Run("toy.add");Assert.True(w.Run("editor.undo"));
        Assert.DoesNotContain("*",w.Title);Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void SelectionFocusAndWorkspaceDoNotAddDirtyMarker()
    {
        var w=Window();w.Editor.Add(new Workspace("accept.layout","Layout",["toy.add"]));
        w.Editor.Select("a");w.Editor.FocusOn("room");w.Editor.Use("accept.layout");
        Assert.DoesNotContain("*",w.Title);Assert.Empty(w.Editor.Session.Journal);Assert.False(w.Editor.Session.History.CanUndo);
    }

    [Fact]
    public void RunDocumentCommandUsesSessionValidationHistoryAndJournal()
    {
        var w=Window();Assert.True(w.Run("toy.add"));Assert.NotNull(w.Editor.Session.Current.Find("added"));
        Assert.Equal("toy.add",Assert.Single(w.Editor.Session.Journal).Command);Assert.True(w.Editor.Session.History.CanUndo);
        Assert.False(string.IsNullOrWhiteSpace(w.Status));
    }

    [Fact]
    public void RunUnknownCommandRefusesWithItsIdAndNoMutation()
    {
        var w=Window();string before=State(w);Assert.False(w.Run("accept.unknown"));
        Words(w.Status,"accept.unknown");Assert.Equal(before,State(w));Assert.Empty(w.Editor.Session.Journal);
    }

    [Fact]
    public void CommandNeedingArgumentsStaysUnavailableWithReason()
    {
        var w=Window(commands:false);var entry=Assert.Single(w.PaletteEntries("toy.move"));
        Assert.Equal("toy.move",entry.Id);Assert.False(string.IsNullOrWhiteSpace(entry.Unavailable));
        string before=State(w);Assert.False(w.Run("toy.move"));Words(w.Status,"detail");
        Assert.Equal(before,State(w));Assert.Empty(w.Editor.Session.Journal);
    }

    [Fact]
    public void SaveWithNoFileRefusesAndExplainsTheMissingFileChoice()
    {
        var w=Window();Assert.False(w.Run("editor.save"));Words(w.Status,"file");
        Assert.False(w.Editor.Session.Dirty);Assert.Empty(w.Editor.Session.Journal);
    }

    [Fact]
    public void SaveExistingPathClearsDirtyMarkerWithoutAddingAnEdit()
    {
        using var s=new Scratch();var w=Window();w.Editor.Session.Save(s.File("board.json"));w.Run("toy.add");
        string journal=JournalFile.Write(w.Editor.Session.Journal);Assert.True(w.Run("editor.save"));
        Assert.DoesNotContain("*",w.Title);Assert.False(w.Editor.Session.Dirty);
        Assert.Equal(journal,JournalFile.Write(w.Editor.Session.Journal));Words(w.Status,"sav");
        Assert.NotNull(BoardKit.Open(s.File("board.json")).Current.Find("added"));
    }

    [Fact]
    public void FailedSaveIsReportedAndPreservesDirtyDocumentAndHistory()
    {
        using var s=new Scratch();var w=Window();string path=s.File("board.json");w.Editor.Session.Save(path);
        w.Run("toy.add");string before=State(w),journal=JournalFile.Write(w.Editor.Session.Journal);
        File.Delete(path);Directory.CreateDirectory(path);
        Assert.False(w.Run("editor.save"));Words(w.Status,"sav");
        Assert.Equal(before,State(w));Assert.Equal(journal,JournalFile.Write(w.Editor.Session.Journal));
        Assert.True(w.Editor.Session.Dirty);Assert.True(w.Editor.Session.History.CanUndo);Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void UndoWithNoHistoryRefusesAndSaysWhatCannotBeUndone()
    {
        var w=Window();string before=State(w);Assert.False(w.Run("editor.undo"));Words(w.Status,"undo");
        Assert.Equal(before,State(w));Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void RedoWithNoBranchRefusesWithoutInventingAnEdit()
    {
        var w=Window();Assert.False(w.Run("editor.redo"));Words(w.Status,"redo");
        Assert.Empty(w.Editor.Session.Journal);Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void UndoAndRedoActionsUseTheSharedSession()
    {
        var w=Window();w.Run("toy.add");Assert.True(w.Run("editor.undo"));Assert.Null(w.Editor.Session.Current.Find("added"));
        Assert.True(w.Run("editor.redo"));Assert.NotNull(w.Editor.Session.Current.Find("added"));
    }

    [Fact]
    public void FocusInUsesPrimarySelectionAndFocusOutLeavesIt()
    {
        var w=Window();w.Editor.Select("a");Assert.True(w.Run("editor.focus_in"));Assert.Equal("a",w.Editor.FocusedOn);
        Assert.True(w.Run("editor.focus_out"));Assert.Equal("room",w.Editor.FocusedOn);Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void FocusInWithEmptySelectionRefusesWithAnExplanation()
    {
        var w=Window();Assert.False(w.Run("editor.focus_in"));Words(w.Status,"select");Assert.Empty(w.Editor.Focus);
    }

    [Fact]
    public void PaletteActionOpensWithoutEditingTheDocument()
    {
        var w=Window();Assert.False(w.PaletteOpen);Assert.True(w.Run("editor.palette"));Assert.True(w.PaletteOpen);
        Assert.Empty(w.Editor.Session.Journal);Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void HandleKeyRunsEffectiveBindingAndUnboundKeyReturnsFalse()
    {
        var keys=new Shortcuts().Default("toy.add","Alt+A");var w=Window(keys:keys);
        Assert.True(w.HandleKey(Chord.Parse("Alt+A")));Assert.NotNull(w.Editor.Session.Current.Find("added"));
        string before=State(w);Assert.False(w.HandleKey(Chord.Parse("Alt+F12")));Assert.Equal(before,State(w));
    }

    [Fact]
    public void UserShortcutOverrideWinsOverTheOldEditorBinding()
    {
        var keys=new Shortcuts();keys.Set("editor.palette","Alt+P");var w=Window(keys:keys);
        Assert.False(w.HandleKey(Chord.Parse("Ctrl+Shift+P")));Assert.False(w.PaletteOpen);
        Assert.True(w.HandleKey(Chord.Parse("Alt+P")));Assert.True(w.PaletteOpen);
    }

    [Fact]
    public void ClashingShortcutFiresNothingAndExplainsTheClash()
    {
        var keys=new Shortcuts().Default("toy.add","Ctrl+S");var w=Window(keys:keys);string before=State(w);
        Assert.False(w.HandleKey(Chord.Parse("Ctrl+S")));Assert.Equal(before,State(w));
        Assert.Empty(w.Editor.Session.Journal);Assert.False(w.Editor.Session.Dirty);
        Assert.True(w.Status.Contains("clash",StringComparison.OrdinalIgnoreCase)||
                    w.Status.Contains("conflict",StringComparison.OrdinalIgnoreCase)||
                    w.Status.Contains("twice",StringComparison.OrdinalIgnoreCase)||
                    w.Status.Contains("more than one",StringComparison.OrdinalIgnoreCase),w.Status);
    }

    [Fact]
    public void PaletteListsEditorActionsBeforeDocumentCommands()
    {
        var entries=Window().PaletteEntries("");int firstCommand=entries.ToList().FindIndex(x=>!x.Id.StartsWith("editor.",StringComparison.Ordinal));
        Assert.True(firstCommand>0);Assert.All(entries.Take(firstCommand),x=>Assert.StartsWith("editor.",x.Id));
        Assert.DoesNotContain(entries.Skip(firstCommand),x=>x.Id.StartsWith("editor.",StringComparison.Ordinal));
    }

    [Fact]
    public void PaletteHidesInternalCommandsEvenWhenSearchedByTheirId()
    {
        var w=Window();Assert.DoesNotContain(w.PaletteEntries(""),x=>x.Id=="accept.hidden");
        Assert.Empty(w.PaletteEntries("accept.hidden"));
    }

    [Fact]
    public void PaletteShowsEffectiveShortcutAndUnavailableReason()
    {
        var keys=new Shortcuts();keys.Set("editor.save","Alt+S");var w=Window(keys:keys);
        var save=Assert.Single(w.PaletteEntries("editor.save"));Assert.Equal("Alt+S",save.Shortcut);
        Assert.False(string.IsNullOrWhiteSpace(save.Unavailable));
        var move=Assert.Single(w.PaletteEntries("toy.move"));Assert.False(string.IsNullOrWhiteSpace(move.Unavailable));
    }

    [Fact]
    public void GoToCurrentObjectSelectsStableIdWithoutEditing()
    {
        var w=Window();Assert.True(w.GoTo(Issue.Error("Check it.","b","$.items[2]")));
        Assert.Equal(["b"],w.Editor.Selection);Assert.False(w.Editor.Session.Dirty);Assert.Empty(w.Editor.Session.Journal);
    }

    [Fact]
    public void GoToIssueWithoutObjectRefusesWithAnHonestExplanation()
    {
        var w=Window();w.Editor.Select("a");Assert.False(w.GoTo(Issue.Error("File problem.")));
        Assert.Equal(["a"],w.Editor.Selection);Words(w.Status,"object");
    }

    [Fact]
    public void GoToDeletedObjectDoesNotRebindByDuplicateDisplayLabel()
    {
        var w=Window();w.Editor.Session.Execute(new RemoveItem("a"));w.Editor.Select("room");
        string before=State(w);Assert.False(w.GoTo(Issue.Error("Old issue.","a")));
        Assert.Equal(["room"],w.Editor.Selection);Assert.Equal(before,State(w));Words(w.Status,"a");
        Assert.NotNull(w.Editor.Session.Current.Find("b"));
    }

    [Fact]
    public void GoToOutsideFocusStepsOutThenSelectsWithoutUnlockingAnEdit()
    {
        var w=Window();w.Editor.FocusOn("a");string before=State(w);
        Assert.True(w.GoTo(Issue.Error("Check outside.","b")));Assert.Empty(w.Editor.Focus);Assert.Equal(["b"],w.Editor.Selection);
        Assert.Equal(before,State(w));Assert.Empty(w.Editor.Session.Journal);Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void GoToInsideFocusKeepsTheExistingBreadcrumb()
    {
        var w=Window();w.Editor.FocusOn("room");Assert.True(w.GoTo(Issue.Error("Check inside.","a")));
        Assert.Equal(["room"],w.Editor.Focus);Assert.Equal(["a"],w.Editor.Selection);
    }

    private void Context(Action<ImGuiIOPtr> body)
    {
        var context=Gui.CreateContext();
        output.WriteLine($"ImGui {Gui.GetVersion()} | .NET {Environment.Version} | OS {Environment.OSVersion} | 1280x720 | default font/theme | CPU headless; no OS backend/window/GPU");
        try
        {
            var io=Gui.GetIO();io.IniFilename=default;io.LogFilename=default;
            io.DisplaySize=new float2(1280,720);io.DeltaTime=1f/60;
            io.BackendFlags|=ImGuiBackendFlags.RendererHasTextures;io.Fonts.AddFontDefault();
            body(io);
        }
        finally {Gui.DestroyContext(context);}
    }

    [Fact]
    public void ConfigureEnablesKeyboardNavigationWithoutEditingTheDocument()
    {
        Context(io=>{EditorWindow<Board>.Configure(io);Assert.True((io.ConfigFlags&ImGuiConfigFlags.NavEnableKeyboard)!=0);});
    }

    [Fact]
    public void ConfigureKeepsNavigationCursorVisibleAsProfileRequires()
    {
        Context(io=>{io.ConfigNavCursorVisibleAlways=false;EditorWindow<Board>.Configure(io);Assert.True(io.ConfigNavCursorVisibleAlways);});
    }

    [Fact]
    public void ConfigureDoesNotLetEscapeClearWidgetFocusBehindTheHost()
    {
        Context(io=>{io.ConfigNavEscapeClearFocusItem=true;EditorWindow<Board>.Configure(io);Assert.False(io.ConfigNavEscapeClearFocusItem);});
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("one")]
    [InlineData("mixed")]
    [InlineData("palette")]
    [InlineData("focus")]
    public void DrawProducesRealImGuiCommandListsWithoutDocumentEdits(string state)
    {
        Context(io=>
        {
            EditorWindow<Board>.Configure(io);var w=Window();
            if(state=="one")w.Editor.Select("a");
            if(state=="mixed")w.Editor.Select("a","b");
            if(state=="palette")w.Run("editor.palette");
            if(state=="focus")w.Editor.FocusOn("room");
            string before=State(w);
            for(int frame=0;frame<4;frame++){Gui.NewFrame();w.Draw();Gui.Render();}
            Assert.True(Gui.GetDrawData().CmdListsCount>0);
            Assert.Equal(before,State(w));Assert.Empty(w.Editor.Session.Journal);Assert.False(w.Editor.Session.Dirty);
        });
    }

    [Fact]
    public void DrawWithoutInspectorRemainsHeadlessAndDoesNotEdit()
    {
        Context(io=>
        {
            EditorWindow<Board>.Configure(io);var w=Window(inspector:false);w.Editor.Select("a");
            for(int frame=0;frame<3;frame++){Gui.NewFrame();w.Draw();Gui.Render();}
            Assert.True(Gui.GetDrawData().CmdListsCount>0);Assert.False(w.Editor.Session.Dirty);
        });
    }

    [Fact]
    public void StatusSurvivesSeveralDrawFramesForPersistentFeedback()
    {
        Context(io=>
        {
            EditorWindow<Board>.Configure(io);var w=Window();w.Run("accept.unknown");string status=w.Status;
            for(int frame=0;frame<3;frame++){Gui.NewFrame();w.Draw();Gui.Render();}
            Assert.False(string.IsNullOrWhiteSpace(status));Assert.Equal(status,w.Status);
        });
    }

    [Fact]
    public void EscapeCancelsPaletteBeforeSteppingOutOfDocumentFocus()
    {
        var w=Window();w.Editor.FocusOn("a");w.Run("editor.palette");
        Assert.True(w.HandleKey(Chord.Parse("Escape")));Assert.False(w.PaletteOpen);
        Assert.Equal("a",w.Editor.FocusedOn);Assert.False(w.Editor.Session.Dirty);
    }

    [Fact]
    public void ValidatorFailureStatusNamesTheObjectAndGivesARemedy()
    {
        var board=new Board([new("victim_42","DiagnosticTarget",0,0)]);
        var e=new EditorModel<Board>(BoardKit.Session(board),new FlatOutline<Board>(d=>d.Items.Select(i=>(i.Id,i.Label))));
        var w=new EditorWindow<Board>(e,commandFor:(_,_)=>new RenameItem("victim_42",""));
        Assert.False(w.Run("toy.rename"));Words(w.Status,"label","name");
        Assert.True(w.Status.Contains("victim_42",StringComparison.Ordinal)||w.Status.Contains("DiagnosticTarget",StringComparison.Ordinal),w.Status);
        Assert.Equal("DiagnosticTarget",e.Session.Current.Find("victim_42")!.Label);Assert.Empty(e.Session.Journal);
    }
}
