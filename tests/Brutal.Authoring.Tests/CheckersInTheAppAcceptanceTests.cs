using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Checkers;
using Xunit;
using GameApp = CheckersPlay.Program;

namespace Brutal.Authoring.Tests;

// A071: INVENTED positions/oracles; MIXED existing headless events and public adapter/session seams.
// BORROWED requirements: goal 14, the named XML comments and the existing sample's documented rules.
// No CheckersAdapter compile-time reference. Every adapter is loaded with Adapters.Load from its actual built DLL.
// Launch is replaced on THAT loaded type before any action; no real process/window can start.
[Collection(ImGuiCollection.Name)]
public sealed class CheckersInTheAppAcceptanceTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source="")
    {
        var d=new DirectoryInfo(Path.GetDirectoryName(source)!);
        while(d is not null&&!File.Exists(Path.Combine(d.FullName,"Brutal.Authoring.slnx")))d=d.Parent;
        return d?.FullName??throw new InvalidOperationException("No test repository");
    }

    private static string AdapterDll()
    {
        var repo=Repo();var parent=Path.GetDirectoryName(repo)!;
        var at=Path.GetFileName(parent)==".gate"?parent:repo;
        var files=Directory.GetFiles(at,"CheckersAdapter.dll",SearchOption.AllDirectories)
            .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(x=>x is "obj" or "ref" or "refint"))
            .Where(p=>p.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar+"CheckersAdapter"+Path.DirectorySeparatorChar)
                ||p.StartsWith(Path.Combine(repo,"samples","CheckersAdapter","bin")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        Assert.NotEmpty(files);return files[0];
    }

    private sealed class Trial : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("a071-").FullName;
        public string Project=>Path.Combine(Root,"authored project");
        public string File=>Path.Combine(Project,"data","custom.json");
        public string AdapterFolder=>Path.Combine(Root,"adapter");
        public string AppFolder=>Directory.CreateDirectory(Path.Combine(Root,"isolated app")).FullName;
        public readonly IProjectAdapter Adapter;
        public readonly Type LoadedType;
        public readonly ProjectLauncher Launcher;
        public readonly List<(ProcessStartInfo Info,string Text)> Starts=[];
        private readonly object? originalLaunch;
        public bool LaunchResult=true;

        public Trial()
        {
            foreach(var p in Directory.GetFiles(Path.Combine(Repo(),"samples","CheckersProject"),"*.json",SearchOption.AllDirectories))
            {
                var rel=Path.GetRelativePath(Path.Combine(Repo(),"samples","CheckersProject"),p);
                var to=Path.Combine(Project,rel);Directory.CreateDirectory(Path.GetDirectoryName(to)!);System.IO.File.Copy(p,to);
            }
            Directory.CreateDirectory(AdapterFolder);
            var dll=AdapterDll();
            System.IO.File.Copy(dll,Path.Combine(AdapterFolder,"CheckersAdapter.dll"));
            var deps=Path.ChangeExtension(dll,"deps.json");
            if(System.IO.File.Exists(deps))System.IO.File.Copy(deps,Path.Combine(AdapterFolder,"CheckersAdapter.deps.json"));
            var (loaded,problems)=Adapters.Load([Path.Combine(AdapterFolder,"CheckersAdapter.dll")]);
            Assert.Empty(problems);Adapter=Assert.Single(loaded);LoadedType=Adapter.GetType();
            originalLaunch=LoadedType.GetProperty("Launch")!.GetValue(null);
            LoadedType.GetProperty("Launch")!.SetValue(null,new Func<ProcessStartInfo,bool>(info=>
            {
                Starts.Add((info,System.IO.File.ReadAllText(info.ArgumentList[^1])));
                return LaunchResult;
            }));
            // A never-executed self-authored marker: GameFor needs a file, while Launch is ALWAYS intercepted.
            System.IO.File.WriteAllText(Path.Combine(AdapterFolder,"play-checkers.exe"),"A071 inert resolver marker; never executed.");
            Launcher=new ProjectLauncher(new RecentProjects(Path.Combine(Root,"settings","recent.json"))){Loaded=[Adapter]};
        }

        public ProjectPaths Paths=>ProjectPaths.In(Project)!;
        public EditorModel<DataDocument> Editor=>Launcher.Workspace!.Window!.Editor;
        public DocumentSession<DataDocument> Session=>Editor.Session;
        public object Board=>Assert.Single(Editor.Panels,p=>p.Id=="checkers.board").Draw.Target!;
        public Game Current=>Position.Read(Session.Current.Root.GetRawText());
        public string Said=>(string)Board.GetType().GetProperty("Said")!.GetValue(Board)!;
        public void Open(string? text=null)
        {
            System.IO.File.WriteAllText(File,text??PositionText());
            Assert.True(Launcher.Open(Paths),Launcher.Said);
            Assert.True(Launcher.Workspace!.Show("custom.json"),Launcher.Workspace.Said);
        }
        public bool Act(string method,params object[] args)
        {
            try{return (bool)Board.GetType().GetMethod(method)!.Invoke(Board,args)!;}
            catch(TargetInvocationException e) when(e.InnerException is not null)
            {ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        public (float2 Origin,float Cell) Geometry=>(ValueTuple<float2,float>)Board.GetType().GetProperty("LastBoard")!.GetValue(Board)!;
        public ProcessStartInfo? GameFor(string position)=>(ProcessStartInfo?)LoadedType.GetMethod("GameFor")!.Invoke(null,[position]);
        public T InApp<T>(Func<T> action)
        {
            var old=AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
            try{AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",AppFolder);return action();}
            finally{AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",old);}
        }
        public void Dispose()
        {
            LoadedType.GetProperty("Launch")!.SetValue(null,originalLaunch);
            // Adapter assembly files can remain loaded on Windows; all are in this card's temporary folder.
            var full=Path.GetFullPath(Root);
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(!full.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                ||!Path.GetFileName(full).StartsWith("a071-",StringComparison.Ordinal))
                throw new InvalidOperationException("Cleanup target is not this trial's temporary folder.");
            try{Directory.Delete(full,true);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
        }
    }

    private static string PositionText(int size=6,string turn="dark",params (string Square,char Piece)[] pieces)
    {
        var rows=Enumerable.Range(0,size).Select(r=>Enumerable.Range(0,size).Select(c=>(size-1-r+c)%2==0?'.':'-').ToArray()).ToArray();
        foreach(var (square,piece) in pieces){int c=square[0]-'a',rank=int.Parse(square[1..]);rows[size-rank][c]=piece;}
        return JsonSerializer.Serialize(new Dictionary<string,object>{{"$schema","board.schema.json"},{"kind","checkers.game"},{"version",1},{"to_move",turn},{"board",rows.Select(x=>new string(x)).ToArray()}});
    }

    private static char Piece(Game g,string square){var (r,c)=g.Parse(square)!.Value;return g.At(r,c);}
    private static string State(Game g)=>new GameFormat().Write(g);
    private static HeadlessHost Host(){var h=new HeadlessHost(1500,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());return h;}
    private static void Button(HeadlessHost h,Action draw,string window,string caption)
    {
        h.Frames(draw,3);
        var w=ImGui.Internal.FindWindowByName(window);Assert.False(w.IsNull());
        var id=w.GetID(caption).Value;
        for(int i=0;i<100;i++)
        {
            if(ImGui.GetCurrentContext().NavId.Value==id){h.Press(draw,ImGuiKey.Space);return;}
            h.Press(draw,ImGuiKey.Tab);
        }
        Assert.Fail("Keyboard did not reach "+window+"/"+caption);
    }

    private sealed record Frozen(DataDocument Current,string Json,int Undo,int Journal,string JournalJson,long Revision,bool Dirty,byte[] Disk)
    {
        public static Frozen Of(Trial t)=>new(t.Session.Current,t.Session.Current.Root.GetRawText(),t.Session.History.UndoCount,t.Session.Journal.Count,JsonSerializer.Serialize(t.Session.Journal),t.Session.Revision,t.Session.Dirty,System.IO.File.ReadAllBytes(t.File));
        public void Unchanged(Trial t)
        {
            Assert.Same(Current,t.Session.Current);Assert.Equal(Json,t.Session.Current.Root.GetRawText());
            Assert.Equal(Undo,t.Session.History.UndoCount);Assert.Equal(Journal,t.Session.Journal.Count);
            Assert.Equal(JournalJson,JsonSerializer.Serialize(t.Session.Journal));
            Assert.Equal(Revision,t.Session.Revision);Assert.Equal(Dirty,t.Session.Dirty);Assert.Equal(Disk,System.IO.File.ReadAllBytes(t.File));
        }
    }

    // Source: Adapters.Load "Loads game adapters from their DLLs"; A071 requires the loaded type.
    [Fact] public void Loading_UsesTheDllAndTheSharedAdapterContract()
    {
        using var t=new Trial();Assert.Equal("Checkers",t.Adapter.Name);
        Assert.Equal(Path.GetFullPath(Path.Combine(t.AdapterFolder,"CheckersAdapter.dll")),Path.GetFullPath(t.LoadedType.Assembly.Location),StringComparer.OrdinalIgnoreCase);
        Assert.True(typeof(IProjectAdapter).IsAssignableFrom(t.LoadedType));Assert.Empty(t.Starts);
    }

    private static ProjectPaths Probe(Trial t,params string[] texts)
    {
        var d=Directory.CreateDirectory(Path.Combine(t.Root,"probe","data")).FullName;
        var s=Directory.CreateDirectory(Path.Combine(t.Root,"probe","schemas")).FullName;
        for(int i=0;i<texts.Length;i++)System.IO.File.WriteAllText(Path.Combine(d,$"probe{i}.json"),texts[i]);
        return new(d,s);
    }

    // Source: adapter "a position is a data file (kind: checkers.game)".
    [Theory][InlineData("opening.json")][InlineData("kings_endgame.json")]
    public void Recognition_AcceptsEachShippedPosition(string file)
    {using var t=new Trial();Assert.True(t.Adapter.Recognises(Probe(t,System.IO.File.ReadAllText(Path.Combine(t.Project,"data",file)))));}

    // Source: Recognises "Whether this is one of its game's projects".
    [Theory][InlineData("{}")][InlineData("[]")][InlineData("null")][InlineData("""{"kind":3}""")]
    [InlineData("""{"kind":"checkers.board"}""")][InlineData("""{"kind":"CHECKERS.GAME"}""")][InlineData("""{"items":[1,2]}""")]
    public void Recognition_DoesNotTakeOtherData(string text)
    {using var t=new Trial();Assert.False(t.Adapter.Recognises(Probe(t,text)));}

    // Source: adapter IsPosition catches syntax errors; recognition is a shape test, not board validation.
    [Theory][InlineData(true)][InlineData(false)]
    public void Recognition_SkipsBrokenJsonAndContinuesToARealPosition(bool validFirst)
    {using var t=new Trial();var good=PositionText();Assert.True(t.Adapter.Recognises(Probe(t,validFirst?good:"{broken",validFirst?"{broken":good)));}

    // Source: Recognises returns false when the data directory is absent.
    [Fact] public void Recognition_MissingDataDirectoryIsNotAProject()
    {using var t=new Trial();Assert.False(t.Adapter.Recognises(new(Path.Combine(t.Root,"absent"),t.Paths.Schemas)));}

    // Source: adapter Configure "Each position gets a Board panel ... a Match play mode".
    [Theory][InlineData("opening.json")][InlineData("kings_endgame.json")]
    public void Configuration_EachSampleGetsExactlyOneBoardAndMatch(string file)
    {
        using var t=new Trial();Assert.True(t.Launcher.OpenFolder(t.Project));Assert.Same(t.Adapter,t.Launcher.Using);
        Assert.True(t.Launcher.Workspace!.Show(file));
        Assert.Equal("Board",Assert.Single(t.Editor.Panels,p=>p.Id=="checkers.board").Title);
        Assert.Equal("Match",Assert.Single(t.Editor.PlayModes,p=>p.Id=="checkers.match").Title);
    }

    // Source: adapter Configure "if not IsPosition ... return"; unrelated files remain plain schema data.
    [Fact] public void Configuration_NonPositionFileHasNoCheckersPanelOrPlayMode()
    {
        using var t=new Trial();
        System.IO.File.WriteAllText(Path.Combine(t.Project,"schemas","plain.schema.json"),"""{"type":"object"}""");
        System.IO.File.WriteAllText(Path.Combine(t.Project,"data","plain.json"),"""{"$schema":"plain.schema.json","note":"invented"}""");
        Assert.True(t.Launcher.Open(t.Paths));Assert.True(t.Launcher.Workspace!.Show("plain.json"));
        Assert.DoesNotContain(t.Editor.Panels,p=>p.Id=="checkers.board");Assert.DoesNotContain(t.Editor.PlayModes,p=>p.Id=="checkers.match");
    }

    // Source: IProjectAdapter.Configure "Adds the game's panels or play modes to each file's editor."
    [Fact] public void Configuration_SwitchingBetweenFilesKeepsIndependentEditors()
    {
        using var t=new Trial();t.Open();var first=t.Editor;
        Assert.True(t.Act("Cycle","c3"));Assert.True(t.Launcher.Workspace!.Show("opening.json"));
        Assert.NotSame(first,t.Editor);Assert.False(t.Session.Dirty);
        Assert.True(t.Launcher.Workspace.Show("custom.json"));Assert.Same(first,t.Editor);Assert.Equal('d',Piece(t.Current,"c3"));
        Assert.Single(t.Editor.Panels,p=>p.Id=="checkers.board");
    }

    private static (string Text,string Message) Invalid(string kind)
    {
        var node=JsonNode.Parse(PositionText())!;
        var rows=node["board"]!.AsArray();
        switch(kind)
        {
            case "odd":return(PositionText(5),"the board has 5 rows; use an even number from 4 to 16");
            case "short":rows[2]="-.-.-";return(node.ToJsonString(),"row 3 has 5 squares; every row needs 6");
            case "light":rows[0]="d.-.-.";return(node.ToJsonString(),"a piece can't stand on a light square (a6)");
            case "dark-crown":rows[0]="-d-.-.";return(node.ToJsonString(),"the man at b6 is on its crowning row; make it a king");
            case "light-crown":rows[5]="l-.-.-";return(node.ToJsonString(),"the man at a1 is on its crowning row; make it a king");
            case "piece":rows[3]=".-x-.-";return(node.ToJsonString(),"'x' at c3 isn't a piece; use . d l D L");
            case "side":node["to_move"]="neutral";return(node.ToJsonString(),"'neutral' isn't a side; use dark or light");
            default:node["version"]=2;return(node.ToJsonString(),"game version 2 is not supported");
        }
    }

    // Source: GameRules "with the game's message"; "Files that aren't positions are left to their schemas."
    [Theory][InlineData("odd")][InlineData("short")][InlineData("light")][InlineData("dark-crown")]
    [InlineData("light-crown")][InlineData("piece")][InlineData("side")][InlineData("version")]
    public void Rules_UsesTheGamesSpecificMessageForEveryInvalidShape(string kind)
    {
        using var t=new Trial();var (text,message)=Invalid(kind);
        var project=t.Adapter.Open(t.Paths);var rule=project.RulesFor("custom.json");Assert.NotNull(rule);
        var issues=rule.Validate(new DataFormat().Read(text));
        Assert.Contains(issues,i=>i.Severity==Severity.Error&&i.Message.Contains(message,StringComparison.Ordinal));
        var error=Record.Exception(()=>Position.Read(text));Assert.NotNull(error);
        var gameMessages=error is InvalidDocumentException invalid?string.Join(" ",invalid.Issues.Select(i=>i.Message)):error.Message;
        Assert.Contains(message,gameMessages);
    }

    // Source: adapter "a board the game would refuse can't be saved".
    [Theory][InlineData("odd")][InlineData("short")][InlineData("light")][InlineData("dark-crown")][InlineData("light-crown")]
    public void Rules_OpeningARefusedBoardKeepsItClosedAndUnwritten(string kind)
    {
        using var t=new Trial();var (text,message)=Invalid(kind);System.IO.File.WriteAllText(t.File,text);
        var file=t.Adapter.Open(t.Paths).Find("custom.json")!;Assert.False(file.Open());
        Assert.Contains(file.Problems,i=>i.Message.Contains(message,StringComparison.Ordinal));
        Assert.Null(file.Session);Assert.Equal(text,System.IO.File.ReadAllText(t.File));
    }

    // Source: goal 4 "A rejected edit leaves the document and its history unchanged."
    [Theory][InlineData("light")][InlineData("dark-crown")][InlineData("light-crown")]
    public void Rules_RefusedEditsPreserveDraftUndoJournalAndFile(string kind)
    {
        using var t=new Trial();t.Open();Assert.True(t.Act("SwitchTurn"));var before=Frozen.Of(t);
        var (text,message)=Invalid(kind);using var json=JsonDocument.Parse(text);
        Assert.False(t.Session.Execute(new SetValue("/board",json.RootElement.GetProperty("board"))));
        Assert.Contains(t.Session.LastIssues,i=>i.Message.Contains(message,StringComparison.Ordinal));before.Unchanged(t);
    }

    // Source: BoardView "empty, dark man, dark king, light man, light king, and round again".
    [Theory][InlineData('.', 'd')][InlineData('d','D')][InlineData('D','l')][InlineData('l','L')][InlineData('L','.')]
    public void Cycle_EveryStepChangesOnlyItsNamedSquareAndHasOneUndo(char from,char to)
    {
        using var t=new Trial();t.Open(PositionText(6,"dark",("c3",from),("e5",'L')));
        var before=Frozen.Of(t);var original=t.Current;
        Assert.True(t.Act("Cycle","c3"));Assert.Equal(to,Piece(t.Current,"c3"));
        Assert.Equal(original.ToMove,t.Current.ToMove);Assert.Equal('L',Piece(t.Current,"e5"));
        foreach(var square in original.Squares().Where(x=>x!="c3"))Assert.Equal(Piece(original,square),Piece(t.Current,square));
        Assert.Equal(before.Undo+1,t.Session.History.UndoCount);Assert.Equal(before.Journal+1,t.Session.Journal.Count);
        Assert.True(t.Session.Undo());Assert.Equal(before.Json,t.Session.Current.Root.GetRawText());Assert.False(t.Session.History.CanUndo);
        Assert.True(t.Session.Redo());Assert.Equal(to,Piece(t.Current,"c3"));Assert.Equal(before.Disk,System.IO.File.ReadAllBytes(t.File));
    }

    // Source: Cycle "step on to the next piece, so a click never leaves the square stuck".
    [Theory][InlineData("b6",'.','D')][InlineData("a1",'D','L')]
    public void Cycle_CrowningRowSkipsTheForbiddenManAsASingleUndo(string square,char from,char expected)
    {
        using var t=new Trial();t.Open(PositionText(6,"dark",(square,from)));var before=Frozen.Of(t);
        Assert.True(t.Act("Cycle",square));Assert.Equal(expected,Piece(t.Current,square));
        Assert.Equal(before.Undo+1,t.Session.History.UndoCount);Assert.Equal(before.Journal+1,t.Session.Journal.Count);
        Assert.Contains(Game.Describe(expected),t.Said);Assert.True(t.Session.Undo());
        Assert.Equal(before.Json,t.Session.Current.Root.GetRawText());Assert.False(t.Session.History.CanUndo);
    }

    // Source: BoardView "and round again"; each step is an undoable edit.
    [Fact] public void Cycle_AWholeRoundTripsThroughFiveUndoAndRedoSteps()
    {
        using var t=new Trial();t.Open();var initial=t.Session.Current.Root.GetRawText();
        foreach(var expected in "dDlL."){Assert.True(t.Act("Cycle","c3"));Assert.Equal(expected,Piece(t.Current,"c3"));}
        Assert.Equal(initial,t.Session.Current.Root.GetRawText());Assert.Equal(5,t.Session.History.UndoCount);
        for(int i=0;i<5;i++)Assert.True(t.Session.Undo());
        Assert.False(t.Session.History.CanUndo);Assert.Equal(initial,t.Session.Current.Root.GetRawText());
        for(int i=0;i<5;i++)Assert.True(t.Session.Redo());
        Assert.Equal(initial,t.Session.Current.Root.GetRawText());
    }

    // Source: Cycle "Returns false, saying why" for a square the game cannot use.
    [Theory][InlineData("a6")][InlineData("g1")][InlineData("a0")][InlineData("C3")][InlineData("")][InlineData("bad")]
    public void Cycle_InvalidSquaresRefuseWithoutAnyStateChange(string square)
    {
        using var t=new Trial();t.Open();var before=Frozen.Of(t);
        Assert.False(t.Act("Cycle",square));Assert.Contains("isn't a dark square",t.Said);before.Unchanged(t);
    }

    // Source: SwitchTurn "Gives the turn to the other side, as one undoable edit."
    [Theory][InlineData("dark",Side.Light)][InlineData("light",Side.Dark)]
    public void Turn_SwitchesBothWaysAndUndoesWithoutTouchingSquares(string side,Side expected)
    {
        using var t=new Trial();t.Open(PositionText(6,side,("c3",'D')));var before=Frozen.Of(t);var rows=t.Current.Rows;
        Assert.True(t.Act("SwitchTurn"));Assert.Equal(expected,t.Current.ToMove);Assert.Equal(rows.ToArray(),t.Current.Rows.ToArray());
        Assert.Equal(1,t.Session.History.UndoCount);Assert.True(t.Session.Undo());Assert.Equal(before.Json,t.Session.Current.Root.GetRawText());
        Assert.True(t.Session.Redo());Assert.Equal(expected,t.Current.ToMove);Assert.Equal(before.Disk,System.IO.File.ReadAllBytes(t.File));
    }

    // Source: BoardView.Current "or null when the file doesn't hold a valid one".
    [Fact] public void Board_InvalidRepairPositionHasNoCurrentGameAndCannotLaunch()
    {
        using var t=new Trial();var (text,_)=Invalid("dark-crown");System.IO.File.WriteAllText(t.File,text);
        var project=t.Adapter.Open(t.Paths);var file=project.Find("custom.json")!;Assert.True(file.Repair());
        var editor=new EditorModel<DataDocument>(file.Session!,new FlatOutline<DataDocument>(_=>[]));
        var board=Activator.CreateInstance(t.LoadedType.Assembly.GetType("CheckersAdapter.BoardView")!,editor)!;
        Assert.Null(board.GetType().GetProperty("Current")!.GetValue(board));
        Assert.False((bool)board.GetType().GetMethod("PlayInOwnWindow")!.Invoke(board,null)!);Assert.Empty(t.Starts);
        Assert.Equal(text,System.IO.File.ReadAllText(t.File));
    }

    // Source: BoardView "a click on a dark square changes what stands there".
    [Fact] public void Board_ActualHeadlessClickEditsTheSquareUnderThePointer()
    {
        using var t=new Trial();t.Open();using var h=Host();h.Frames(t.Launcher.Draw,4);
        var centre=Board.Centre(t.Current,t.Geometry,"c3");h.Click(t.Launcher.Draw,centre.X,centre.Y);
        Assert.Equal('d',Piece(t.Current,"c3"));Assert.Equal(1,t.Session.History.UndoCount);
        Assert.True(t.Session.Undo());Assert.Equal('.',Piece(t.Current,"c3"));
    }

    // Source: BoardView.Draw "whose turn it is" switch button.
    [Fact] public void Board_ActualKeyboardTurnButtonIsAnUndoableEdit()
    {
        using var t=new Trial();t.Open();using var h=Host();
        Button(h,t.Launcher.Draw,"Board###panel.checkers.board","Dark to move: switch");
        Assert.Equal(Side.Light,t.Current.ToMove);Assert.Equal(1,t.Session.History.UndoCount);
        Assert.True(t.Session.Undo());Assert.Equal(Side.Dark,t.Current.ToMove);
    }

    // Source: goal 14 "Play hands it a copy of the current document" including unsaved authoring.
    [Fact] public void Inside_MatchStartsFromTheEditedDraftRatherThanTheSavedFile()
    {
        using var t=new Trial();t.Open(PositionText(6,"dark",("c3",'d'),("d4",'l')));
        Assert.True(t.Act("Cycle","a1"));var before=Frozen.Of(t);
        Assert.True(t.Editor.Play("checkers.match"));var match=Assert.IsType<Match>(t.Editor.Game);
        Assert.Equal(State(t.Current),State(match.Current));Assert.Equal('d',Piece(match.Current,"a1"));
        Assert.Equal('.',Piece(Position.Read(System.IO.File.ReadAllText(t.File)),"a1"));Assert.True(t.Editor.Stop());before.Unchanged(t);
    }

    // Source: goal 14 "press Play, play to a win by clicks, then press Stop ... exactly as before Play".
    [Fact] public void Inside_ClickingAForcedCaptureWinsAndStopPreservesTheWholeAuthoredState()
    {
        using var t=new Trial();t.Open(PositionText(6,"dark",("c3",'d'),("d4",'l')));Assert.True(t.Act("SwitchTurn"));Assert.True(t.Act("SwitchTurn"));
        var before=Frozen.Of(t);using var h=Host();Assert.True(t.Launcher.Workspace!.Window!.Run(EditorWindow<DataDocument>.PlayAction+"checkers.match"));
        var match=Assert.IsType<Match>(t.Editor.Game);h.Frames(t.Launcher.Draw,4);
        foreach(var square in new[]{"c3","e5"}){var at=Board.Centre(match.Current,match.LastBoard,square);h.Click(t.Launcher.Draw,at.X,at.Y);}
        Assert.Equal(Side.Dark,Rules.Winner(match.Current));Assert.Equal('.',Piece(match.Current,"d4"));
        Assert.True(t.Launcher.Workspace.Window.Run("editor.stop"));before.Unchanged(t);
        Assert.True(t.Session.Undo());Assert.Equal(Side.Light,t.Current.ToMove);
    }

    // Source: goal 14 "While playing, edits to the document are locked".
    [Fact] public void Inside_BoardEditsAndTurnSwitchCannotChangeThePlayingDocument()
    {
        using var t=new Trial();t.Open();var before=Frozen.Of(t);Assert.True(t.Editor.Play("checkers.match"));
        Assert.False(t.Act("Cycle","c3"));Assert.False(t.Act("SwitchTurn"));before.Unchanged(t);
        Assert.True(t.Editor.Stop());Assert.True(t.Act("Cycle","c3"));
    }

    // Source: goal 14 "Stop throws that state away and returns to editing".
    [Fact] public void Inside_RestartGetsAFreshMatchAtTheAuthoredPosition()
    {
        using var t=new Trial();t.Open(PositionText(6,"dark",("c3",'d'),("d4",'l')));
        Assert.True(t.Editor.Play("checkers.match"));var first=Assert.IsType<Match>(t.Editor.Game);
        Assert.Equal("Played c3-e5.",first.Play(new MovePiece(["c3","e5"])));Assert.True(t.Editor.Stop());
        Assert.Null(t.Editor.Game);Assert.True(t.Editor.Play("checkers.match"));var second=Assert.IsType<Match>(t.Editor.Game);
        Assert.NotSame(first,second);Assert.Equal(State(t.Current),State(second.Current));Assert.Null(Rules.Winner(second.Current));
        Assert.True(t.Editor.Stop());
    }

    // Source: goal 14 "Undo, the journal and the saved file never see play."
    [Fact] public void Inside_PlayAndStopKeepAnExistingRedoBranchIntact()
    {
        using var t=new Trial();t.Open();Assert.True(t.Act("Cycle","c3"));Assert.True(t.Session.Undo());var before=Frozen.Of(t);
        Assert.True(t.Session.History.CanRedo);Assert.True(t.Editor.Play("checkers.match"));Assert.True(t.Editor.Stop());
        before.Unchanged(t);Assert.True(t.Session.Redo());Assert.Equal('d',Piece(t.Current,"c3"));
    }

    // Source: PlayInOwnWindow "unsaved edits included (from a copy, so the game never writes the file)".
    [Fact] public void Outside_InterceptedLaunchReceivesTheDraftCopyAndNeverTheAuthoredFile()
    {
        using var t=new Trial();t.Open();Assert.True(t.Act("Cycle","c3"));Assert.True(t.Act("SwitchTurn"));var before=Frozen.Of(t);
        Assert.True(t.Act("PlayInOwnWindow"));var call=Assert.Single(t.Starts);
        Assert.NotEqual(Path.GetFullPath(t.File),Path.GetFullPath(call.Info.ArgumentList[^1]),StringComparer.OrdinalIgnoreCase);
        Assert.Equal(State(t.Current),State(Position.Read(call.Text)));Assert.False(call.Info.UseShellExecute);before.Unchanged(t);
        Assert.Equal("Started the game in its own window.",t.Said);
    }

    // Source: PlayInOwnWindow "Starts the game ... on the position being edited".
    [Fact] public void Outside_DeclinedLaunchIsReportedAndLeavesTheDocumentUntouched()
    {
        using var t=new Trial();t.Open();t.LaunchResult=false;var before=Frozen.Of(t);
        Assert.False(t.Act("PlayInOwnWindow"));Assert.Single(t.Starts);Assert.Equal("The game didn't start.",t.Said);before.Unchanged(t);
    }

    // Source: goal 14 "starts play-checkers on the position being edited ... from a copy".
    [Fact] public void Outside_EachLaunchKeepsItsSnapshotStableForItsOwnProcess()
    {
        using var t=new Trial();t.Open();Assert.True(t.Act("PlayInOwnWindow"));
        var first=t.Starts[0];var path=first.Info.ArgumentList[^1];var bytes=System.IO.File.ReadAllBytes(path);
        Assert.True(t.Act("Cycle","c3"));Assert.True(t.Act("PlayInOwnWindow"));Assert.Equal(2,t.Starts.Count);
        Assert.Equal(State(Position.Read(first.Text)),State(Position.Read(System.IO.File.ReadAllText(path))));
        Assert.Equal(bytes,System.IO.File.ReadAllBytes(path));
        Assert.NotEqual(first.Info.ArgumentList[^1],t.Starts[1].Info.ArgumentList[^1]);
    }

    // Source: GameFor "beside this adapter, or beside the app".
    [Fact] public void Resolver_PrefersTheExecutableBesideItsLoadedAdapter()
    {
        using var t=new Trial();var input=Path.Combine(t.Root,"position with spaces.json");var info=t.InApp(()=>t.GameFor(input));
        Assert.NotNull(info);Assert.Equal(Path.Combine(t.AdapterFolder,"play-checkers.exe"),info.FileName);
        Assert.Equal([input],info.ArgumentList);Assert.False(info.UseShellExecute);
    }

    // Source: GameFor checks play-checkers.dll after the adjacent executable.
    [Fact] public void Resolver_CanUseAnAdjacentDllThroughDotnetWithSeparateArguments()
    {
        using var t=new Trial();System.IO.File.Delete(Path.Combine(t.AdapterFolder,"play-checkers.exe"));
        System.IO.File.WriteAllText(Path.Combine(t.AdapterFolder,"play-checkers.dll"),"A071 inert marker");
        var input=Path.Combine(t.Root,"é space","position.json");var info=t.InApp(()=>t.GameFor(input));
        Assert.NotNull(info);Assert.Equal("dotnet",info.FileName);Assert.Equal([Path.Combine(t.AdapterFolder,"play-checkers.dll"),input],info.ArgumentList);
        Assert.False(info.UseShellExecute);
    }

    // Source: GameFor "or beside the app".
    [Theory][InlineData("exe")][InlineData("dll")]
    public void Resolver_FallsBackToTheGameBesideTheApp(string extension)
    {
        using var t=new Trial();System.IO.File.Delete(Path.Combine(t.AdapterFolder,"play-checkers.exe"));
        var candidate=Path.Combine(t.AppFolder,"play-checkers."+extension);System.IO.File.WriteAllText(candidate,"A071 inert marker");
        var info=t.InApp(()=>t.GameFor("named position.json"));Assert.NotNull(info);
        Assert.Equal(extension=="exe"?candidate:"dotnet",info.FileName);
        Assert.Equal(extension=="exe"?new[]{"named position.json"}:new[]{candidate,"named position.json"},info.ArgumentList);
    }

    // Source: GameFor "Null when it isn't there."
    [Fact] public void Resolver_MissingGameIsNullAndTheBoardRefusesWithoutInvokingLaunch()
    {
        using var t=new Trial();System.IO.File.Delete(Path.Combine(t.AdapterFolder,"play-checkers.exe"));t.Open();
        Assert.Null(t.InApp(()=>t.GameFor("position.json")));
        Assert.False(t.InApp(()=>t.Act("PlayInOwnWindow")));Assert.Empty(t.Starts);
        Assert.Contains("isn't beside the adapter or the app",t.Said);Assert.False(t.Session.Dirty);
    }

    // Source: Position.Read "with a data project's $schema field allowed and ignored".
    [Theory][InlineData("board.schema.json")][InlineData("urn:invented:schema")][InlineData("../any.schema.json")]
    public void Position_SchemaIdentifierIsIgnoredWithoutChangingTheAuthoredGame(string schema)
    {
        var json=JsonNode.Parse(PositionText(6,"light",("c3",'L')))!;json["$schema"]=schema;
        var g=Position.Read(json.ToJsonString());Assert.Equal(Side.Light,g.ToMove);Assert.Equal('L',Piece(g,"c3"));Assert.Equal(6,g.Size);
        Assert.Equal(schema,json["$schema"]!.GetValue<string>());
    }

    // Source: Position.Read checked by the game's rules; unrelated fields remain strict.
    [Fact] public void Position_UnknownFieldsDoNotBecomeSilentGameSettings()
    {
        var json=JsonNode.Parse(PositionText())!;json["invented_cheat"]=true;
        var e=Assert.Throws<DocumentFormatException>(()=>Position.Read(json.ToJsonString()));Assert.Contains("invented_cheat",e.Message);
    }

    // Source: Program "It starts from a position file ... or the standard opening."
    [Fact] public void Program_HeadlessSavedPositionWithSchemaStartsAndDoesNotWriteIt()
    {
        using var t=new Trial();var text=PositionText(6,"light",("c3",'D'),("e5",'L'));System.IO.File.WriteAllText(t.File,text);
        var shot=Path.Combine(t.Root,"saved.png");var output=new StringWriter();var errors=new StringWriter();
        Assert.Equal(0,GameApp.Run([t.File,"--shot",shot],output,errors));Assert.Equal("",errors.ToString());
        Assert.True(new FileInfo(shot).Length>1000);Assert.Equal(text,System.IO.File.ReadAllText(t.File));Assert.Contains(Path.GetFullPath(shot),output.ToString());
        // An independent expected render of the supplied position distinguishes loading it from silently using Opening().
        var expected=new CheckersPlay.Play(Position.Read(text));
        using var oracle=new HeadlessHost(720,800);oracle.Frames(expected.Draw,3);
        Assert.Equal(Png.Encode(720,800,oracle.Pixels),System.IO.File.ReadAllBytes(shot));
    }

    // Source: Program exit codes "1 the position can't be read (with why)".
    [Theory][InlineData("odd")][InlineData("short")][InlineData("light")][InlineData("dark-crown")][InlineData("side")]
    public void Program_InvalidBoardsReturnOneWithTheGamesMessageAndNoImage(string kind)
    {
        using var t=new Trial();var(text,message)=Invalid(kind);System.IO.File.WriteAllText(t.File,text);
        var shot=Path.Combine(t.Root,"no.png");var errors=new StringWriter();
        Assert.Equal(1,GameApp.Run([t.File,"--shot",shot],TextWriter.Null,errors));Assert.Contains(message,errors.ToString());
        Assert.False(System.IO.File.Exists(shot));Assert.Equal(text,System.IO.File.ReadAllText(t.File));
    }

    // Source: Program "1 the position can't be read (with why)".
    [Theory][InlineData("{broken")][InlineData("")][InlineData("[]")][InlineData("null")]
    public void Program_MalformedPositionIsARefusalRatherThanAnUnhandledException(string text)
    {
        using var t=new Trial();System.IO.File.WriteAllText(t.File,text);var errors=new StringWriter();var shot=Path.Combine(t.Root,"no.png");
        Assert.Equal(1,GameApp.Run([t.File,"--shot",shot],TextWriter.Null,errors));Assert.Contains("isn't a checkers position",errors.ToString());Assert.False(System.IO.File.Exists(shot));
    }

    // Source: Program "1 the position can't be read (with why)".
    [Fact] public void Program_MissingPositionReturnsOneAndNamesTheFile()
    {
        using var t=new Trial();var missing=Path.Combine(t.Root,"absent.json");var shot=Path.Combine(t.Root,"no.png");var errors=new StringWriter();
        Assert.Equal(1,GameApp.Run([missing,"--shot",shot],TextWriter.Null,errors));Assert.Contains(missing,errors.ToString());Assert.False(System.IO.File.Exists(shot));
    }

    // Source: Program "--shot draws the game into a PNG instead of opening a window".
    [Fact] public void Program_DefaultOpeningRunsOnlyThroughItsHeadlessDoor()
    {
        using var t=new Trial();var shot=Path.Combine(t.Root,"opening.png");var errors=new StringWriter();
        Assert.Equal(0,GameApp.Run(["--shot",shot],TextWriter.Null,errors));Assert.Equal("",errors.ToString());
        Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},System.IO.File.ReadAllBytes(shot)[..8]);
    }

    // Source: Program "2 bad arguments".
    [Theory][InlineData("--shot")][InlineData("--unknown")]
    public void Program_BadArgumentsReturnTwoWithoutAnyLaunch(string arg)
    {
        var errors=new StringWriter();Assert.Equal(2,GameApp.Run([arg],TextWriter.Null,errors));Assert.Contains("usage:",errors.ToString());
    }

    // Source: Play "New game ... starts over from the same position."
    [Fact] public void Standalone_NewGameRestartsTheSuppliedPositionNotTheStandardOpening()
    {
        var start=Position.Read(PositionText(6,"dark",("c3",'d'),("d4",'l')));var play=new CheckersPlay.Play(start);
        var first=play.Match;Assert.Equal("Played c3-e5.",first.Play(new MovePiece(["c3","e5"])));
        using var h=Host();Button(h,play.Draw,"Checkers###game","New game");
        Assert.NotSame(first,play.Match);Assert.Equal(State(start),State(play.Match.Current));Assert.Null(Rules.Winner(play.Match.Current));
    }

    // Source: Match.Draw "Take back" removes the last move, independent of document undo.
    [Fact] public void Standalone_TakeBackRestoresTheLastPositionAndTurnThroughKeyboard()
    {
        var start=Position.Read(PositionText(6,"dark",("c3",'d'),("d4",'l')));var play=new CheckersPlay.Play(start);
        Assert.Equal("Played c3-e5.",play.Match.Play(new MovePiece(["c3","e5"])));
        using var h=Host();Button(h,play.Draw,"Checkers###game","Take back");
        Assert.Equal(State(start),State(play.Match.Current));Assert.Equal("Took back the last move.",play.Match.Said);
    }

    // Source: Match.Take back is disabled at the initial history entry.
    [Fact] public void Standalone_InitialTakeBackCannotRemoveTheStartingPosition()
    {
        var start=Position.Read(PositionText(6,"dark",("c3",'d'),("d4",'l')));var play=new CheckersPlay.Play(start);
        using var h=Host();h.Frames(play.Draw,4);var snapshot=State(play.Match.Current);
        // The disabled Take back target is absent from keyboard navigation; live neighbours must be reached.
        var w=ImGui.Internal.FindWindowByName("Checkers###game");var back=w.GetID("Take back").Value;
        var next=w.GetID("New game").Value;var computer=w.GetID("Computer move").Value;var seen=new HashSet<uint>();
        for(int i=0;i<70;i++){h.Press(play.Draw,ImGuiKey.Tab);seen.Add(ImGui.GetCurrentContext().NavId.Value);}
        Assert.DoesNotContain(back,seen);Assert.Contains(next,seen);Assert.Contains(computer,seen);
        Assert.Equal(snapshot,State(play.Match.Current));Assert.Equal(State(start),State(play.Match.Current));
        Assert.Equal("",play.Match.Said);
    }
}
