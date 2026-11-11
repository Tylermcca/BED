using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Data;
using Bed.Editor.ImGui;
using Bed.Agent;
using Xunit;
using Ui = Brutal.ImGuiApi.ImGui;
using Cli = Bed.Agent.Cli.Program;

namespace Bed.Tests;

/// <summary>INVENTED black-box acceptance cases from public comments and accepted additive A050 semantics.
/// Fixtures are self-authored; no game, workbook, network, external process or OS window is used.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class RepairRefsWhereAcceptanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "a056-contract-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private const string LedgerSchema = """{"type":"object","required":["alpha","beta"],"properties":{"alpha":{"type":"integer","minimum":0},"beta":{"type":"integer","minimum":0},"caption":{"type":"string"}},"additionalProperties":false}""";
    public RepairRefsWhereAcceptanceTests() { Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas); }
    public void Dispose()
    {
        var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "a056-contract-";
        if (!Path.GetFullPath(root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture boundary.");
        Directory.Delete(root, recursive:true);
    }
    private void Put(string name,string json,string schema="{}")
    {
        File.WriteAllText(Path.Combine(Data,name+".json"),json);
        File.WriteAllText(Path.Combine(Schemas,name+".schema.json"),schema);
    }
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static SetValue Set(string at,string json) => new(at,J(json));
    private DataProject Project(bool readOnly=false,Func<string,IValidator<DataDocument>?>? rules=null) => DataProject.Open(Data,Schemas,readOnly,rules:rules);
    private DataFile Ledger(string json="""{"alpha":-4,"beta":-8,"caption":"draft"}""",bool readOnly=false,Func<string,IValidator<DataDocument>?>? rules=null)
    {
        Put("ledger",json,LedgerSchema);return Project(readOnly,rules).Find("ledger.json")!;
    }
    private DocumentSession<DataDocument> RepairLedger()
    { var f=Ledger();Assert.True(f.Repair());return f.Session!; }
    private sealed class OrderRule : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) => d.Root.TryGetProperty("alpha",out var a)&&d.Root.TryGetProperty("beta",out var b)&&a.ValueKind==JsonValueKind.Number&&b.ValueKind==JsonValueKind.Number&&a.GetDouble()>b.GetDouble()
            ? [Issue.Error("alpha must not exceed beta",path:"/alpha")] : [];
    }
    private sealed class ScoresRule : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) => d.Root.GetProperty("rows").EnumerateArray()
            .Where(x=>x.GetProperty("score").GetInt32()<0)
            .Select(x=>Issue.Error("score must be nonnegative",objectId:x.GetProperty("id").GetString(),path:"/score")).ToArray();
    }
    private static JsonObject Target(string list="/items",string key="id",string file="nodes.json") => new(){["file"]=file,["list"]=list,["key"]=key};
    private static JsonObject Clause(string at,string source) => new(){["at"]=at,["equalsSource"]=source};
    private static JsonObject Entry(string at="/rows/*/ref",JsonObject? target=null,bool keys=false)
    {
        var e=new JsonObject{["file"]="source.json",["at"]=at,["target"]=target??Target(),["evidence"]="self-authored acceptance fixture"};
        if(keys)e["keys"]=true;return e;
    }
    private static ReferenceMap Map(params JsonObject[] entries) => ReferenceMap.Read(MapText(entries));
    private static string MapText(params JsonObject[] entries) => new JsonObject
    {
        ["game"]="Acceptance fixture",["labels"]=new JsonArray(new JsonObject{["file"]="source.json",["list"]="/rows",["field"]="caption"}),
        ["references"]=new JsonArray(entries.Select(x=>(JsonNode)x.DeepClone()).ToArray())
    }.ToJsonString();
    private void Seed()
    {
        Put("nodes","""{"items":[{"id":"oak","owner":"amber","zone":1},{"id":"fir","owner":"blue","zone":2}],"modes":[{"id":"quiet","group":"amber"}],"states":["quiet","loud"]}""");
        Put("source","""{"rows":[{"caption":"Parcel signal","name":"Distractor","ref":"oak","refs":["fir","oak"],"pair":"oak:quiet","owner":"amber","zone":1}],"index":{"oak":false}}""");
    }
    private static ReferenceResult Follow(ReferenceMap map,DataProject p,string at="/rows/0/ref",ReferenceIntent? intent=null)
    {
        var f=p.Find("source.json")!;Assert.True(f.Open());var value=f.Session!.Current.At(at)??default;
        return intent is {} i?map.Resolve(p,"source.json",at,value,i):map.Resolve(p,"source.json",at,value);
    }
    private static string[] From(ReferenceMap m,DataProject p,string at="/items/0") =>
        m.ReferencesTo(p,new("nodes.json",at)).Select(x=>x.File+" "+x.Pointer).Order(StringComparer.Ordinal).ToArray();
    private static JsonObject Mcp(AgentSession a,string tool,JsonObject args,bool expectError=false)
    {
        var request=new JsonObject{["jsonrpc"]="2.0",["id"]=17,["method"]="tools/call",["params"]=new JsonObject{["name"]=tool,["arguments"]=args}};
        var reply=new McpServer(a).Handle(request.ToJsonString())!;
        Assert.Equal(expectError,reply["result"]!["isError"]!.GetValue<bool>());
        return JsonNode.Parse(reply["result"]!["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
    }
    private (int Exit,JsonObject? Json,string Errors) Command(string map,params string[] call)
    {
        var path=Path.Combine(root,"fixture-map.json");File.WriteAllText(path,map);
        using var output=new StringWriter();using var errors=new StringWriter();
        var args=new[]{"--data",Data,"--schemas",Schemas,"--map",path}.Concat(call).ToArray();
        int exit=Cli.Run(args,new StringReader(""),output,errors);
        return(exit,output.ToString().Length>0?JsonNode.Parse(output.ToString())!.AsObject():null,errors.ToString());
    }

    // Contract R01-R20: OpenForRepair accepts incremental repairs, adds no new problem, and heals/undoes normally.
    [Fact] public void Repair_ValidDocumentUsesOrdinarySessionWithoutDirtyingDisk()
    { var f=Ledger("""{"alpha":2,"beta":9}""");var bytes=File.ReadAllBytes(f.Path);Assert.True(f.Repair());Assert.False(f.Session!.Repairing);Assert.Empty(f.Session.Problems);Assert.False(f.Dirty);Assert.Equal(bytes,File.ReadAllBytes(f.Path)); }
    [Fact] public void Repair_SyntaxFailureNeverCreatesASession()
    { var f=Ledger("{ broken");Assert.False(f.Repair());Assert.Null(f.Session);Assert.NotEmpty(f.Problems); }
    [Fact] public void Repair_MissingSchemaCannotBeBypassed()
    { Put("orphan","{}");File.Delete(Path.Combine(Schemas,"orphan.schema.json"));var f=Project().Find("orphan.json")!;Assert.False(f.Repair());Assert.Null(f.Session); }
    [Fact] public void Repair_HealingOneFieldRetainsOnlyTheOtherProblem()
    { var s=RepairLedger();Assert.Equal(2,s.Problems.Count);Assert.True(s.Execute(Set("/alpha","3")));Assert.True(s.Repairing);Assert.Single(s.Problems);Assert.Contains("beta",s.Problems[0].Path!); }
    [Fact] public void Repair_NewViolationRefusesWithoutHistoryJournalOrRevisionChange()
    { var s=RepairLedger();var current=s.Current;var revision=s.Revision;Assert.False(s.Execute(Set("/caption","false")));Assert.Same(current,s.Current);Assert.Equal(revision,s.Revision);Assert.Empty(s.Journal);Assert.False(s.Undo()); }
    [Fact] public void Repair_RefusalNamesOnlyTheNewViolation()
    { var s=RepairLedger();Assert.False(s.Execute(Set("/caption","42")));Assert.Single(s.LastIssues);Assert.Contains("caption",s.LastIssues[0].Path!);Assert.Equal(2,s.Problems.Count); }
    [Fact] public void Repair_BatchHealsAllFieldsInOneUndoStep()
    { var s=RepairLedger();Assert.True(s.ExecuteBatch([Set("/alpha","3"),Set("/beta","6")]));Assert.False(s.Repairing);Assert.Single(s.Journal);Assert.True(s.Undo());Assert.Equal(-4,s.Current.At("/alpha")!.Value.GetInt32());Assert.Equal(-8,s.Current.At("/beta")!.Value.GetInt32());Assert.False(s.Undo()); }
    [Fact] public void Repair_BatchWithFinalNewViolationRollsBackEveryEarlierFix()
    { var s=RepairLedger();Assert.False(s.ExecuteBatch([Set("/alpha","3"),Set("/caption","false")]));Assert.Equal(-4,s.Current.At("/alpha")!.Value.GetInt32());Assert.Empty(s.Journal);Assert.Equal(2,s.Problems.Count); }
    [Fact] public void Repair_HarmlessEditMayLeaveAllOriginalProblemsInPlace()
    { var s=RepairLedger();Assert.True(s.Execute(Set("/caption","\"amended\"")));Assert.Equal("amended",s.Current.At("/caption")!.Value.GetString());Assert.Equal(2,s.Problems.Count);Assert.True(s.Repairing);Assert.True(s.Undo());Assert.Equal("draft",s.Current.At("/caption")!.Value.GetString()); }
    [Fact] public void Repair_UndoAndRedoAcrossHealingRestoreRepairState()
    { var s=RepairLedger();Assert.True(s.ExecuteBatch([Set("/alpha","3"),Set("/beta","6")]));Assert.True(s.Undo());Assert.True(s.Repairing);Assert.Equal(2,s.Problems.Count);Assert.True(s.Redo());Assert.False(s.Repairing);Assert.Empty(s.Problems); }
    [Fact] public void Repair_HealedSessionAgainRejectsOrdinaryInvalidEdits()
    { var s=RepairLedger();Assert.True(s.ExecuteBatch([Set("/alpha","3"),Set("/beta","6")]));Assert.False(s.Execute(Set("/alpha","-1")));Assert.False(s.Repairing);Assert.Equal(3,s.Current.At("/alpha")!.Value.GetInt32()); }
    [Fact] public void Repair_SaveMayPersistAPartiallyRepairedDocument()
    { var s=RepairLedger();Assert.True(s.Execute(Set("/alpha","4")));s.Save();Assert.False(s.Dirty);Assert.True(s.Repairing);using var d=JsonDocument.Parse(File.ReadAllText(s.FilePath!));Assert.Equal(4,d.RootElement.GetProperty("alpha").GetInt32());Assert.Equal(-8,d.RootElement.GetProperty("beta").GetInt32()); }
    [Fact] public void Repair_RevertToAValidOutsideFileHealsAndClearsHistory()
    { var s=RepairLedger();Assert.True(s.Execute(Set("/alpha","4")));File.WriteAllText(s.FilePath!,"""{"alpha":7,"beta":9}""");Assert.True(s.Revert());Assert.False(s.Repairing);Assert.False(s.Dirty);Assert.Empty(s.Journal);Assert.False(s.Undo()); }
    [Fact] public void Repair_RevertToUnreadableTextKeepsTheDraftAndProblems()
    { var s=RepairLedger();Assert.True(s.Execute(Set("/alpha","4")));var old=s.Current;File.WriteAllText(s.FilePath!,"[");Assert.False(s.Revert());Assert.Same(old,s.Current);Assert.True(s.Repairing);Assert.Single(s.Problems); }
    [Fact] public void Repair_GameRuleCannotBeIntroducedWhileSchemaIsHealing()
    { var f=Ledger("""{"alpha":-1,"beta":5}""",rules:_=>new OrderRule());Assert.True(f.Repair());Assert.False(f.Session!.Execute(Set("/alpha","6")));Assert.Single(f.Session.LastIssues);Assert.Equal("alpha must not exceed beta",f.Session.LastIssues[0].Message); }
    [Fact] public void Repair_AFileBrokenOnlyByAGameRuleCanBeHealed()
    { var f=Ledger("""{"alpha":8,"beta":3}""",rules:_=>new OrderRule());Assert.False(f.Open());Assert.True(f.Repair());Assert.True(f.Session!.Repairing);Assert.True(f.Session.Execute(Set("/beta","9")));Assert.False(f.Session.Repairing); }
    // Issue.ObjectId names the broken object; a different object is a NEW problem despite the same field/message.
    [Fact] public void Repair_HealingOneObjectMustNotPermitBreakingAnotherWithTheSameDiagnosticText()
    {
        Put("scores","""{"rows":[{"id":"first","score":-1},{"id":"second","score":2}]}""");
        var f=Project(rules:_=>new ScoresRule()).Find("scores.json")!;Assert.True(f.Repair());
        Assert.False(f.Session!.ExecuteBatch([Set("/rows/0/score","0"),Set("/rows/1/score","-1")]));
        Assert.Equal("second",Assert.Single(f.Session.LastIssues).ObjectId);
        Assert.Equal(-1,f.Session.Current.At("/rows/0/score")!.Value.GetInt32());
    }
    [Fact] public void Repair_FileStateTracksTheHealedPointAndUndo()
    { var f=Ledger();Assert.True(f.Repair());Assert.Equal(DataFileState.Repairing,f.State);Assert.True(f.Session!.ExecuteBatch([Set("/alpha","1"),Set("/beta","1")]));Assert.Equal(DataFileState.Open,f.State);Assert.True(f.Session.Undo());Assert.Equal(DataFileState.Repairing,f.State); }
    [Fact] public void Repair_ReadOnlyProjectAllowsInspectionButCannotSaveTheRepair()
    { var f=Ledger(readOnly:true);var bytes=File.ReadAllBytes(f.Path);Assert.True(f.Repair());Assert.True(f.Session!.Execute(Set("/alpha","1")));Assert.False(f.Save());Assert.True(f.Dirty);Assert.Equal(bytes,File.ReadAllBytes(f.Path)); }
    [Fact] public void Repair_WorkspaceShowsProblemsAndKeepsAnExistingOtherFileOnFailure()
    {
        Put("ledger","""{"alpha":-1,"beta":-2}""",LedgerSchema);Put("steady","{}");var w=new DataWorkspace(Project());
        Assert.True(w.Show("steady.json"));Assert.False(w.Repair("missing.json"));Assert.Equal("steady.json",w.Active!.Name);
        Assert.True(w.Repair("ledger.json"));Assert.Equal("ledger.json",w.Active!.Name);Assert.NotNull(w.Window);Assert.Equal(DataFileState.Repairing,w.Active.State);Assert.Contains("2 problem",w.Said);
    }
    [Fact] public void Repair_FilesPanelButtonOpensTheBrokenDocumentWithoutAnOSWindow()
    {
        Put("ledger","""{"alpha":-1,"beta":-2}""",LedgerSchema);var p=Project();Assert.False(p.Find("ledger.json")!.Open());var w=new DataWorkspace(p);
        using var host=new HeadlessHost(){Drawing=false};float x=0,y=0;
        void Draw(){Ui.SetNextWindowPos(new Brutal.Numerics.float2(10,10));Ui.SetNextWindowSize(new Brutal.Numerics.float2(750,300));Ui.Begin("A056 Files"u8);w.FilesPanel.Draw();var a=Ui.GetItemRectMin();var b=Ui.GetItemRectMax();x=(a.X+b.X)/2;y=(a.Y+b.Y)/2;Ui.End();}
        host.Frames(Draw);Assert.True(x>10&&x<760&&y>10&&y<310,$"Repair button center ({x},{y}) must be inside the visible 10,10 to 760,310 Files window.");host.Click(Draw,x,y);Assert.Equal(DataFileState.Repairing,p.Find("ledger.json")!.State);Assert.Equal("ledger.json",w.Active!.Name);
    }
    // AgentSession.Repair: edits that add none are accepted until none are left.
    [Fact] public void Repair_AgentCanFixOneOldProblemWhileAnotherRemains()
    { Ledger();var a=new AgentSession(Project());Assert.True(a.Repair("ledger.json")["repairing"]!.GetValue<bool>());var r=a.Set("ledger.json","/alpha","1");Assert.True(r["applied"]?.GetValue<bool>()==true,r.ToJsonString());Assert.True(a.Project.Find("ledger.json")!.Session!.Repairing); }
    [Fact] public void Repair_AgentRefusalListsOnlyTheNewProblem()
    { Ledger();var a=new AgentSession(Project());a.Repair("ledger.json");var r=a.Set("ledger.json","/caption","false");var issues=r["refused"]!.AsArray();Assert.Single(issues);Assert.Contains("caption",issues[0]!.ToJsonString()); }
    [Fact] public void Repair_McpRepairListsProblemsWithoutSaving()
    { var f=Ledger();var bytes=File.ReadAllBytes(f.Path);var r=Mcp(new AgentSession(Project()),"repair",new(){["file"]="ledger.json"});Assert.True(r["repairing"]!.GetValue<bool>());Assert.Equal(2,r["problems"]!.AsArray().Count);Assert.Equal(bytes,File.ReadAllBytes(f.Path)); }
    [Fact] public void Repair_CliRepairIsAnInspectionAndReturnsSuccess()
    { var f=Ledger();var bytes=File.ReadAllBytes(f.Path);var r=Command(MapText(),"repair","ledger.json");Assert.Equal(0,r.Exit);Assert.True(r.Json!["repairing"]!.GetValue<bool>());Assert.Equal(bytes,File.ReadAllBytes(f.Path)); }

    // ReferencesTo: every value at every mapped place that resolves to the target, including any resolved ID part.
    [Fact] public void Refs_TraversesAHealthyFileThatHasNotYetBeenOpened()
    { Seed();var p=Project();Assert.Null(p.Find("source.json")!.Session);Assert.Equal(new[]{"source.json /rows/0/ref"},From(Map(Entry()),p)); }
    [Fact] public void Refs_KeyPointerEscapesSlashAndTilde()
    { Put("nodes","""{"items":{"oak/~":{"caption":"Leaf"}}}""");Put("source","""{"index":{"oak/~":7}}""");var m=Map(Entry("/index",Target("/items","$key"),true));Assert.Equal(new[]{"source.json /index/oak~1~0"},From(m,Project(),"/items/oak~1~0")); }
    [Fact] public void Refs_ListOfIdsReturnsEveryOccurrenceWithItsOwnIndex()
    { Seed();Put("source","""{"rows":[{"refs":["oak","fir","oak"]}]}""");Assert.Equal(new[]{"source.json /rows/0/refs/0","source.json /rows/0/refs/2"},From(Map(Entry("/rows/*/refs")),Project())); }
    [Fact] public void Refs_BothPartsOfACompoundIdReferFromTheOriginalLeaf()
    { Seed();var e=Entry("/rows/*/pair",Target("/modes"));e["parts"]=new JsonObject{["separator"]=":",["targets"]=new JsonArray(Target(),Target("/modes"))};var m=Map(e);var p=Project();Assert.Equal(new[]{"source.json /rows/0/pair"},From(m,p));Assert.Equal(new[]{"source.json /rows/0/pair"},From(m,p,"/modes/0")); }
    [Fact] public void Refs_DoesNotTreatAnInvalidCompoundAsAPartialReference()
    { Seed();Put("source","""{"rows":[{"pair":"oak:unknown"}]}""");var e=Entry("/rows/*/pair",Target("/modes"));e["parts"]=new JsonObject{["separator"]=":",["targets"]=new JsonArray(Target(),Target("/modes"))};Assert.Empty(From(Map(e),Project())); }
    [Fact] public void Refs_NonIdCompoundPartDoesNotBecomeATarget()
    { Seed();Put("source","""{"rows":[{"pair":"decorative:oak"}]}""");var e=Entry("/rows/*/pair");e["parts"]=new JsonObject{["separator"]=":",["targets"]=new JsonArray(null,Target())};Assert.Equal(new[]{"source.json /rows/0/pair"},From(Map(e),Project())); }
    [Fact] public void Refs_NarrowedSourceDoesNotReferToAnIdenticallyNamedItemElsewhere()
    {
        Put("nodes","""{"groups":[{"id":"inside","items":[{"id":"oak"}]},{"id":"outside","items":[{"id":"oak"}]}]}""");Put("source","""{"rows":[{"ref":"oak","group":"inside"}]}""");
        var e=Entry(target:Target("/groups/*/items"));e["narrow"]=new JsonObject{["by"]="1/group",["file"]="nodes.json",["list"]="/groups",["key"]="id"};
        var m=Map(e);var p=Project();Assert.Equal(new[]{"source.json /rows/0/ref"},From(m,p,"/groups/0/items/0"));Assert.Empty(From(m,p,"/groups/1/items/0"));
    }
    [Fact] public void Refs_WhereConditionExcludesWrongOwnerWithSameId()
    {
        Put("nodes","""{"items":[{"id":"oak","owner":"blue"},{"id":"oak","owner":"amber"}]}""");Put("source","""{"rows":[{"ref":"oak","owner":"amber"}]}""");
        var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/owner"));var m=Map(Entry(target:t));var p=Project();
        Assert.Empty(From(m,p,"/items/0"));Assert.Equal(new[]{"source.json /rows/0/ref"},From(m,p,"/items/1"));
    }
    [Fact] public void Refs_SentinelIsNotAnIdEvenWhenItsSpellingExists()
    { Seed();var e=Entry();e["sentinels"]=new JsonArray("oak");Assert.Empty(From(Map(e),Project())); }
    [Fact] public void Refs_AmbiguousSpellingIsNotAResolvedReference()
    { Seed();Put("nodes","""{"items":[{"id":"oak"},{"id":"oak"}]}""");Assert.Empty(From(Map(Entry()),Project())); }
    [Fact] public void Refs_UnreadableSourceIsSkippedButHealthySourceStillContributes()
    {
        Seed();Put("broken","[");var a=Entry();var b=Entry();b["file"]="broken.json";var m=Map(a,b);
        Assert.Equal(new[]{"source.json /rows/0/ref"},From(m,Project()));
    }
    [Fact] public void Refs_NoMatchReturnsAnEmptyCollection()
    { Seed();Assert.Empty(From(Map(Entry()),Project(),"/items/1")); }
    [Fact] public void Refs_AgentLabelsTheReferringItemRatherThanTargetOrNameFallback()
    { Seed();var r=new AgentSession(Project(),Map(Entry())).Refs("nodes.json","/items/0");Assert.Equal(1,r["count"]!.GetValue<int>());Assert.Equal("Parcel signal",r["from"]![0]!["item"]!.GetValue<string>());Assert.Equal("/rows/0/ref",r["from"]![0]!["at"]!.GetValue<string>());Assert.NotNull(r["next"]); }
    [Fact] public void Refs_AgentNoMatchIsSuccessWithAUsefulNextAction()
    { Seed();var r=new AgentSession(Project(),Map(Entry())).Refs("nodes.json","/items/1");Assert.False(r.ContainsKey("error"));Assert.Equal(0,r["count"]!.GetValue<int>());Assert.Empty(r["from"]!.AsArray());Assert.False(string.IsNullOrWhiteSpace(r["next"]!.GetValue<string>())); }
    [Fact] public void Refs_McpMatchesDirectSessionAndDoesNotWriteFiles()
    { Seed();var a=new AgentSession(Project(),Map(Entry()));var before=File.ReadAllText(Path.Combine(Data,"source.json"));var r=Mcp(a,"refs",new(){["file"]="nodes.json",["at"]="/items/0"});Assert.True(JsonNode.DeepEquals(a.Refs("nodes.json","/items/0"),r));Assert.Equal(1,r["count"]!.GetValue<int>());Assert.Equal("source.json",r["from"]![0]!["file"]!.GetValue<string>());Assert.Equal("/rows/0/ref",r["from"]![0]!["at"]!.GetValue<string>());Assert.Equal("Parcel signal",r["from"]![0]!["item"]!.GetValue<string>());Assert.NotNull(r["next"]);Assert.Equal(before,File.ReadAllText(Path.Combine(Data,"source.json"))); }
    [Fact] public void Refs_CliMatchesTheDirectSourcePlaceAndLabel()
    { Seed();var r=Command(MapText(Entry()),"refs","nodes.json","/items/0");Assert.Equal(0,r.Exit);Assert.Equal(1,r.Json!["count"]!.GetValue<int>());Assert.Equal("source.json",r.Json["from"]![0]!["file"]!.GetValue<string>());Assert.Equal("/rows/0/ref",r.Json["from"]![0]!["at"]!.GetValue<string>());Assert.Equal("Parcel signal",r.Json["from"]![0]!["item"]!.GetValue<string>()); }
    [Fact] public void Refs_McpRequiresTheTargetPointerArgument()
    { Seed();var r=Mcp(new AgentSession(Project(),Map(Entry())),"refs",new(){["file"]="nodes.json"},expectError:true);Assert.True(r.ContainsKey("error"));Assert.NotNull(r["next"]); }
    [Fact] public void Refs_CliMissingArgumentUsesDocumentedBadArgumentsExit()
    { Seed();var r=Command(MapText(Entry()),"refs","nodes.json");Assert.Equal(2,r.Exit);Assert.Null(r.Json);Assert.Contains("arguments",r.Errors); }
    [Fact] public void Refs_KeyAndValueMeaningsBothAppearInReverseReferences()
    {
        Put("nodes","""{"items":{"token":{"caption":"Key item"}},"states":["quiet"]}""");Put("source","""{"sets":{"token":"quiet"}}""");
        var key=Entry("/sets",Target("/items","$key"),true);var value=Entry("/sets/*",Target("/states","$value"));var m=Map(key,value);var p=Project();
        Assert.Equal(new[]{"source.json /sets/token"},From(m,p,"/items/token"));
        Assert.Equal(new[]{"source.json /sets/token"},From(m,p,"/states/0"));
    }

    // A050 accepted semantics: typed scalar equality, AND on one candidate, missing source => NeedsScope.
    [Fact] public void Where_NumericEqualityUsesValueRatherThanJsonSpelling()
    { Seed();Put("source","""{"rows":[{"ref":"oak","zone":1.00}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/zone","1/zone"));Assert.Equal(ReferenceOutcome.Found,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_TextNeverEqualsANumericOperand()
    { Seed();Put("source","""{"rows":[{"ref":"oak","zone":"1"}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/zone","1/zone"));Assert.Equal(ReferenceOutcome.Missing,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_SourceAncestorKeyIsDecodedBeforeComparison()
    { Put("nodes","""{"items":[{"id":"oak","owner":"unit/~"}]}""");Put("source","""{"groups":{"unit/~":{"ref":"oak"}}}""");var t=Target();t["where"]=new JsonArray(Clause("0/owner","1#"));Assert.Equal(ReferenceOutcome.Found,Follow(Map(Entry("/groups/*/ref",t)),Project(),"/groups/unit~1~0/ref").Outcome); }
    [Fact] public void Where_BooleanOperandsAreTypedScalars()
    { Put("nodes","""{"items":[{"id":"oak","enabled":true}]}""");Put("source","""{"rows":[{"ref":"oak","enabled":true}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/enabled","1/enabled"));Assert.Equal(ReferenceOutcome.Found,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_PresentNullCanEqualPresentNull()
    { Put("nodes","""{"items":[{"id":"oak","owner":null}]}""");Put("source","""{"rows":[{"ref":"oak","owner":null}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/owner"));Assert.Equal(ReferenceOutcome.Found,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_AbsentDoesNotMeanPresentNull()
    { Put("nodes","""{"items":[{"id":"oak","side":null}]}""");Put("source","""{"rows":[{"ref":"oak"}]}""");var t=Target();t["where"]=new JsonArray(new JsonObject{["absent"]="side"});Assert.Equal(ReferenceOutcome.Missing,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_AbsentDoesNotMeanPresentFalse()
    { Put("nodes","""{"items":[{"id":"oak","side":false}]}""");Put("source","""{"rows":[{"ref":"oak"}]}""");var t=Target();t["where"]=new JsonArray(new JsonObject{["absent"]="side"});Assert.Equal(ReferenceOutcome.Missing,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_AbsentSelectsTheCandidateWhichActuallyLacksTheMember()
    { Put("nodes","""{"items":[{"id":"oak","side":null},{"id":"oak"}]}""");Put("source","""{"rows":[{"ref":"oak"}]}""");var t=Target();t["where"]=new JsonArray(new JsonObject{["absent"]="side"});Assert.Equal("/items/1",Follow(Map(Entry(target:t)),Project()).Target!.Pointer); }
    [Fact] public void Where_TwoSeparateCandidatesCannotSatisfyOneAndEach()
    { Put("nodes","""{"items":[{"id":"oak","owner":"amber","zone":2},{"id":"oak","owner":"blue","zone":1}]}""");Put("source","""{"rows":[{"ref":"oak","owner":"amber","zone":1}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/owner"),Clause("0/zone","1/zone"));Assert.Equal(ReferenceOutcome.Missing,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_SeveralClausesSelectOneCandidateJointly()
    { Put("nodes","""{"items":[{"id":"oak","owner":"blue","zone":1},{"id":"oak","owner":"amber","zone":1}]}""");Put("source","""{"rows":[{"ref":"oak","owner":"amber","zone":1}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/owner"),Clause("0/zone","1/zone"),new JsonObject{["absent"]="side"});Assert.Equal("/items/1",Follow(Map(Entry(target:t)),Project()).Target!.Pointer); }
    [Theory]
    [InlineData("""{"rows":[{"ref":"oak"}]}""")]
    [InlineData("""{"rows":[{"ref":"oak","owner":{}}]}""")]
    [InlineData("""{"rows":[{"ref":"oak","owner":[]}]}""")]
    public void Where_UnavailableOrStructuredSourceFailsClosed(string source)
    { Seed();Put("source",source);var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/owner"));Assert.Equal(ReferenceOutcome.NeedsScope,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_MissingCandidateOperandExcludesOnlyThatCandidate()
    { Put("nodes","""{"items":[{"id":"oak"},{"id":"oak","owner":"amber"}]}""");Put("source","""{"rows":[{"ref":"oak","owner":"amber"}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/owner"));Assert.Equal("/items/1",Follow(Map(Entry(target:t)),Project()).Target!.Pointer); }
    [Fact] public void Where_ConditionOnAlsoTargetIsNotIgnored()
    { Put("nodes","""{"items":[],"reserve":[{"id":"oak","owner":"blue"}]}""");Put("source","""{"rows":[{"ref":"oak","owner":"amber"}]}""");var extra=Target("/reserve");extra["where"]=new JsonArray(Clause("0/owner","1/owner"));var e=Entry();e["also"]=new JsonArray(extra);Assert.Equal(ReferenceOutcome.Missing,Follow(Map(e),Project()).Outcome); }
    [Fact] public void Where_PartsTargetsUseTheOriginalSourceContext()
    { Seed();var last=Target("/modes");last["where"]=new JsonArray(Clause("0/group","1/owner"));var e=Entry("/rows/*/pair",Target("/modes"));e["parts"]=new JsonObject{["separator"]=":",["targets"]=new JsonArray(Target(),last)};var r=Follow(Map(e),Project(),"/rows/0/pair");Assert.Equal(ReferenceOutcome.Found,r.Outcome);Assert.Equal(2,r.Parts!.Count);Assert.Equal("/modes/0",r.Target!.Pointer); }
    [Fact] public void Where_RefusesAPartWhoseConditionDoesNotMatch()
    { Seed();Put("source","""{"rows":[{"pair":"oak:quiet","owner":"blue"}]}""");var last=Target("/modes");last["where"]=new JsonArray(Clause("0/group","1/owner"));var e=Entry("/rows/*/pair",Target("/modes"));e["parts"]=new JsonObject{["separator"]=":",["targets"]=new JsonArray(Target(),last)};Assert.Equal(ReferenceOutcome.Missing,Follow(Map(e),Project(),"/rows/0/pair").Outcome); }
    [Fact] public void Where_MissingSourceInLaterTargetIsNotHiddenByAnEarlierMatch()
    { Seed();var extra=Target("/modes");extra["where"]=new JsonArray(Clause("0/group","1/absent"));var e=Entry();e["also"]=new JsonArray(extra);Assert.Equal(ReferenceOutcome.NeedsScope,Follow(Map(e),Project()).Outcome); }
    [Theory]
    [InlineData("""[]""")]
    [InlineData("""[{"at":"0/owner"}]""")]
    [InlineData("""[{"equalsSource":"1/owner"}]""")]
    [InlineData("""[{"absent":"side","at":"0/side"}]""")]
    [InlineData("""[{"at":"0/owner","equalsSource":"1/owner","extra":true}]""")]
    [InlineData("""[{"at":"00/owner","equalsSource":"1/owner"}]""")]
    [InlineData("""[{"at":"0/owner","equalsSource":"/owner"}]""")]
    [InlineData("""[{"absent":""}]""")]
    public void Where_MalformedClausesAreRefusedByThePublicMapReader(string clauses)
    { var t=Target();t["where"]=JsonNode.Parse(clauses);Assert.Throws<InvalidDocumentException>(()=>Map(Entry(target:t))); }
    [Fact] public void Where_EightClausesAreAcceptedButNineAreRefused()
    { var t=Target();t["where"]=new JsonArray(Enumerable.Range(0,8).Select(i=>(JsonNode)new JsonObject{["absent"]="field"+i}).ToArray());var e=Entry(target:t);Assert.NotNull(Map(e));t["where"]!.AsArray().Add(new JsonObject{["absent"]="ninth"});Assert.Throws<InvalidDocumentException>(()=>Map(e)); }

    // A050 decision: optional additive syntax; explicit intent without a match is NotAReference; old overload unchanged.
    [Fact] public void Intent_ExplicitKeyAndValueFollowDifferentTargetsAtOnePointer()
    { Put("nodes","""{"items":{"token":{}},"states":["quiet"]}""");Put("source","""{"sets":{"token":"quiet"}}""");var m=Map(Entry("/sets",Target("/items","$key"),true),Entry("/sets/*",Target("/states","$value")));var p=Project();Assert.Equal("/items/token",Follow(m,p,"/sets/token",ReferenceIntent.Key).Target!.Pointer);Assert.Equal("/states/0",Follow(m,p,"/sets/token",ReferenceIntent.Value).Target!.Pointer); }
    [Fact] public void Intent_LegacyOverloadKeepsItsExistingKeyMeaning()
    { Put("nodes","""{"items":{"token":{}},"states":["quiet"]}""");Put("source","""{"sets":{"token":"quiet"}}""");var m=Map(Entry("/sets",Target("/items","$key"),true),Entry("/sets/*",Target("/states","$value")));Assert.Equal("/items/token",Follow(m,Project(),"/sets/token").Target!.Pointer); }
    [Fact] public void Intent_ValueDoesNotFallBackToAKeyOnlyEntry()
    { Seed();Assert.Equal(ReferenceOutcome.NotAReference,Follow(Map(Entry("/index",Target(),true)),Project(),"/index/oak",ReferenceIntent.Value).Outcome); }
    [Fact] public void Intent_KeyDoesNotFallBackToAValueOnlyEntry()
    { Seed();Assert.Equal(ReferenceOutcome.NotAReference,Follow(Map(Entry()),Project(),intent:ReferenceIntent.Key).Outcome); }
    [Fact] public void Intent_SentinelPrecedesWhereSourceRequirements()
    { Seed();var t=Target();t["where"]=new JsonArray(Clause("0/owner","1/not_here"));var e=Entry(target:t);e["sentinels"]=new JsonArray("oak");Assert.Equal(ReferenceOutcome.Sentinel,Follow(Map(e),Project(),intent:ReferenceIntent.Value).Outcome); }

    [Fact] public void Intent_ExplicitMeaningIsIndependentOfEntryOrder()
    { Put("nodes","""{"items":{"token":{}},"states":["quiet"]}""");Put("source","""{"sets":{"token":"quiet"}}""");var m=Map(Entry("/sets/*",Target("/states","$value")),Entry("/sets",Target("/items","$key"),true));var p=Project();Assert.Equal("/items/token",Follow(m,p,"/sets/token",ReferenceIntent.Key).Target!.Pointer);Assert.Equal("/states/0",Follow(m,p,"/sets/token",ReferenceIntent.Value).Target!.Pointer); }
    [Fact] public void Intent_EqualKeyAndValueTextStillUsesTheRequestedDomain()
    { Put("nodes","""{"items":{"token":{}},"states":["token"]}""");Put("source","""{"sets":{"token":"token"}}""");var m=Map(Entry("/sets",Target("/items","$key"),true),Entry("/sets/*",Target("/states","$value")));var p=Project();Assert.Equal("/items/token",Follow(m,p,"/sets/token",ReferenceIntent.Key).Target!.Pointer);Assert.Equal("/states/0",Follow(m,p,"/sets/token",ReferenceIntent.Value).Target!.Pointer); }
    [Fact] public void Where_AbsentRejectsANullCandidateRatherThanTreatingItAsAnObject()
    { Put("nodes","""{"items":{"oak":null}}""");Put("source","""{"rows":[{"ref":"oak"}]}""");var t=Target("/items","$key");t["where"]=new JsonArray(new JsonObject{["absent"]="side"});Assert.Equal(ReferenceOutcome.Missing,Follow(Map(Entry(target:t)),Project()).Outcome); }


    // A050 accepted composition: overlapping rules within the selected intent family are Ambiguous, never first-match.
    [Fact] public void Intent_OverlappingValueRulesAreAmbiguousRatherThanChoosingFirst()
    { Seed();Put("nodes","""{"items":[{"id":"oak"}],"reserve":[{"id":"oak"}]}""");var m=Map(Entry(),Entry(target:Target("/reserve")));var r=Follow(m,Project(),intent:ReferenceIntent.Value);Assert.Equal(ReferenceOutcome.Ambiguous,r.Outcome);Assert.Null(r.Target); }
    [Fact] public void Intent_OverlappingKeyRulesAreAmbiguousRatherThanChoosingFirst()
    { Put("nodes","""{"items":{"token":{}},"reserve":{"token":{}}}""");Put("source","""{"sets":{"token":"quiet"}}""");var m=Map(Entry("/sets",Target("/items","$key"),true),Entry("/sets",Target("/reserve","$key"),true));Assert.Equal(ReferenceOutcome.Ambiguous,Follow(m,Project(),"/sets/token",ReferenceIntent.Key).Outcome); }
    // A050 accepted equality: exact base-ten value, no binary-floating rounding; N# retains numeric index type.
    [Fact] public void Where_DistinctIntegersBeyondBinaryFloatPrecisionDoNotCompareEqual()
    { Put("nodes","""{"items":[{"id":"oak","zone":9007199254740993}]}""");Put("source","""{"rows":[{"ref":"oak","zone":9007199254740992}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/zone","1/zone"));Assert.Equal(ReferenceOutcome.Missing,Follow(Map(Entry(target:t)),Project()).Outcome); }
    [Fact] public void Where_AncestorArrayIndexIsANumberRatherThanText()
    { Put("nodes","""{"items":[{"id":"oak","zone":0}]}""");Put("source","""{"rows":[{"ref":"oak"}]}""");var t=Target();t["where"]=new JsonArray(Clause("0/zone","1#"));Assert.Equal(ReferenceOutcome.Found,Follow(Map(Entry(target:t)),Project()).Outcome); }

}
