using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;
using Cli = Bed.Agent.Cli.Program;

namespace Bed.Tests;

// INVENTED: self-authored beacon/route fixtures and controlled checkers. Oracles are A066,
// Checks.cs XML comments, and the implemented subset of A060 AC01-44, not Claude's test bodies.
// Future paging/options/process-runner APIs are deliberately absent.
[Collection(ImGuiCollection.Name)]
public sealed class ChecksAcceptanceTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private const string Schema = """{"type":"object","required":["nodes"],"properties":{"nodes":{"type":"array","items":{"type":"object","required":["id","name"],"properties":{"id":{"type":"string"},"name":{"type":"string"}}}}}}""";
    private const string Nodes = """{"nodes":[{"id":"beacon","name":"Amber beacon"},{"id":"dock","name":"Birch dock"}]}""";
    private const string Routes = """{"routes":[{"target":"beacon"},{"target":"dock"}]}""";
    private const string RouteSchema = """{"type":"object","required":["routes"],"properties":{"routes":{"type":"array","items":{"type":"object"}}}}""";
    private static ReferenceMap Map(string extra = "", string target = """{"file":"nodes.json","list":"/nodes","key":"id"}""")
        => ReferenceMap.Read("""{"game":"Invented A066 beacons","labels":[{"file":"nodes.json","list":"/nodes","field":"name"}],"references":[{"file":"routes.json","at":"/routes/*/target","target":"""
            + target + ""","evidence":"INVENTED: a route names a beacon or dock" """ + extra + "}]}");
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "a066-" + Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root, "data");
        public string Schemas => Path.Combine(Root, "schemas");
        public Fixture()
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Schemas);
            Put("nodes.json", Nodes); Put("routes.json", Routes);
            File.WriteAllText(Path.Combine(Schemas, "nodes.schema.json"), Schema);
            File.WriteAllText(Path.Combine(Schemas, "routes.schema.json"), RouteSchema);
        }
        public void Put(string name, string text) => File.WriteAllText(Path.Combine(Data, name), text);
        public DataProject Open(bool readOnly = false, Func<string,IValidator<DataDocument>?>? rules = null)
            => DataProject.Open(Data, Schemas, readOnly, rules: rules);
        public string Disk()
            => string.Join("|", Directory.EnumerateFiles(Data).Concat(Directory.EnumerateFiles(Schemas)).Order(StringComparer.Ordinal)
                .Select(p => Path.GetFileName(p) + ":" + Convert.ToHexString(File.ReadAllBytes(p)) + ":" + File.GetLastWriteTimeUtc(p).Ticks));
        public void Dispose()
        {
            // Delete only this instance's freshly created, absolute temp directory.
            var path = Path.GetFullPath(Root);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), path, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("a066-", Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
    private sealed class Checker : ICheck
    {
        public CheckDefinition Definition { get; }
        private readonly Func<CheckSnapshot,IProgress<CheckProgress>,CancellationToken,Task<CheckReport>> body;
        public int Calls;
        public Task<CheckReport>? Work;
        public Checker(string id = "sample.audit", string label = "Audit beacons", TimeSpan? timeout = null,
            Func<CheckSnapshot,IProgress<CheckProgress>,CancellationToken,Task<CheckReport>>? body = null)
        {
            Definition = new(id, label, "Inspect invented beacons, not game assets.", timeout ?? TimeSpan.FromSeconds(5));
            this.body = body ?? ((_,_,_) => Task.FromResult(CleanReport()));
        }
        public Task<CheckReport> RunAsync(CheckSnapshot s,IProgress<CheckProgress> p,CancellationToken c)
        { Interlocked.Increment(ref Calls); return Work = body(s,p,c); }
    }
    private sealed class Held : IDisposable
    {
        public readonly TaskCompletionSource<CheckSnapshot> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<CheckReport> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource TokenSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<CheckProgress>? Progress;
        public Checker Check { get; }
        public Held(string id = "held.audit", TimeSpan? timeout = null)
        {
            Check = new Checker(id, timeout: timeout, body: async (s,p,c) =>
            {
                Progress = p; using var registered = c.Register(() => TokenSeen.TrySetResult());
                Entered.TrySetResult(s);
                return await Release.Task;
            });
        }
        public void Dispose() => Release.TrySetResult(CleanReport());
    }
    private static CheckReport CleanReport() => new(true, [], "All invented inputs inspected.");
    private static CheckFinding Finding(Severity severity = Severity.Error, string text = "Invented beacon has no destination.")
        => new(new Issue(severity,text,"beacon","/nodes/0/name"),new("nodes.json","/nodes/0","/nodes/0/name"),"Choose an existing destination.");
    private static CheckRun Start(Checks checks, DataProject p, string id = "sample.audit", ReferenceMap? map = null)
    { var run = checks.Start(id,p,out var why,map); Assert.True(run is not null,why); return run!; }
    private static async Task End(CheckRun run) => await run.Ended.WaitAsync(Guard);
    private static DocumentSession<DataDocument> Session(DataProject p, string name = "nodes.json")
    { var f = p.Find(name)!; Assert.True(f.Open()); return f.Session!; }
    private static void Edit(DocumentSession<DataDocument> s, string text = "Changed draft")
        => Assert.True(s.Execute(new SetValue("/nodes/0/name",JsonSerializer.SerializeToElement(text))));

    [Fact] public void Registration_TwoDuplicateNewIdsPublishNothing()
    {
        var c=new Checks(); Assert.Throws<ArgumentException>(()=>c.Add(new Checker("same.id"),new Checker("same.id")));
        Assert.Empty(c.Registered);
    }
    [Fact] public void Registration_ConflictWithExistingPreservesOnlyExisting()
    {
        var c=new Checks().Add(new Checker("kept.id"));
        Assert.Throws<ArgumentException>(()=>c.Add(new Checker("fresh.id"),new Checker("kept.id")));
        Assert.Equal(new[]{"kept.id"},c.Registered.Select(d=>d.Id));
    }
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("\t")]
    public void Registration_EmptyIdRejectsWholeBatch(string id)
    {
        var c=new Checks(); Assert.Throws<ArgumentException>(()=>c.Add(new Checker(),new Checker(id))); Assert.Empty(c.Registered);
    }
    [Theory] [InlineData("")] [InlineData(" ")]
    public void Registration_EmptyLabelRejectsWholeBatch(string label)
    {
        var c=new Checks(); Assert.Throws<ArgumentException>(()=>c.Add(new Checker(),new Checker("bad.label",label))); Assert.Empty(c.Registered);
    }
    [Theory] [InlineData(0)] [InlineData(-1)]
    public void Registration_NonpositiveLimitRejectsWholeBatch(int ms)
    {
        var c=new Checks(); Assert.Throws<ArgumentException>(()=>c.Add(new Checker(),new Checker("bad.limit",timeout:TimeSpan.FromMilliseconds(ms)))); Assert.Empty(c.Registered);
    }
    [Fact] public void Registration_ExposesEveryDefinitionAndUnknownLastIsAbsent()
    {
        var a=new Checker("a.audit","Check amber"); var b=new Checker("b.audit","Check birch"); var c=new Checks().Add(a,b);
        Assert.Equal(new[]{a.Definition,b.Definition},c.Registered); Assert.Null(c.Last("never.ran"));
    }
    [Fact] public void Start_UnknownIdReturnsReasonAndInvokesNothing()
    {
        using var f=new Fixture(); var check=new Checker(); var c=new Checks().Add(check);
        Assert.Null(c.Start("not.registered",f.Open(),out var why)); Assert.False(string.IsNullOrWhiteSpace(why)); Assert.Equal(0,check.Calls);
    }
    [Fact] public void Snapshot_CapturesUnopenedFilesWithoutOpeningOrInvokingRules()
    {
        using var f=new Fixture(); int calls=0; var p=f.Open(rules:_=>{calls++;return null;}); var disk=f.Disk();
        var snap=CheckSnapshot.Capture(p);
        Assert.Equal(Nodes,snap.Text("data/nodes.json")); Assert.Equal(Routes,snap.Text("data/routes.json"));
        Assert.All(p.Files,x=>Assert.Null(x.Session)); Assert.Equal(0,calls); Assert.Equal(disk,f.Disk());
    }
    [Fact] public void Snapshot_DraftWinsAndNeverSavesOrRevisionsTheLiveSession()
    {
        using var f=new Fixture(); var p=f.Open(); var s=Session(p); Edit(s); var rev=s.Revision; var journal=s.Journal.ToArray(); var disk=f.Disk();
        var snap=CheckSnapshot.Capture(p);
        Assert.Contains("Changed draft",snap.Text("data/nodes.json")); Assert.DoesNotContain("Changed draft",File.ReadAllText(Path.Combine(f.Data,"nodes.json")));
        Assert.Equal(rev,s.Revision); Assert.Equal(journal,s.Journal); Assert.True(s.Dirty); Assert.Equal(disk,f.Disk());
    }
    [Fact] public void Snapshot_RepairDraftUsesItsCurrentValueWithoutFinishingRepair()
    {
        using var f=new Fixture(); f.Put("nodes.json","""{"nodes":[{"id":"beacon","name":17}]}""");
        var p=f.Open(); var file=p.Find("nodes.json")!; Assert.True(file.Repair()); var s=file.Session!;
        Assert.True(s.Repairing); Assert.True(s.Execute(new SetValue("/nodes/0/id",JsonSerializer.SerializeToElement("repair-draft"))));
        var snap=CheckSnapshot.Capture(p);
        Assert.Contains("repair-draft",snap.Text("data/nodes.json")); Assert.True(s.Repairing); Assert.True(s.Dirty);
        Assert.Contains("\"name\":17",File.ReadAllText(file.Path));
    }
    [Fact] public void Snapshot_MalformedReadableJsonIsIncludedByteForByte()
    {
        using var f=new Fixture(); f.Put("routes.json","{broken invented JSON"); var p=f.Open(); var snap=CheckSnapshot.Capture(p);
        Assert.Equal("{broken invented JSON",snap.Text("data/routes.json")); Assert.Null(p.Find("routes.json")!.Session);
    }
    [Fact] public void Snapshot_NamesIncludeEverySchemaAndMapInOrdinalOrder()
    {
        using var f=new Fixture(); File.WriteAllText(Path.Combine(f.Schemas,"unused.schema.json"),"{}");
        var m=Map(); var snap=CheckSnapshot.Capture(f.Open(),m);
        Assert.Equal(new[]{"data/nodes.json","data/routes.json","map","schemas/nodes.schema.json","schemas/routes.schema.json","schemas/unused.schema.json"},snap.Names);
        Assert.Equal(m.Text,snap.Text("map")); Assert.Equal(Schema,snap.Text("schemas/nodes.schema.json"));
    }
    [Fact] public void Snapshot_ReturnedByteBuffersCannotMutateStoredInputs()
    {
        using var f=new Fixture(); var snap=CheckSnapshot.Capture(f.Open()); var hash=snap.InputHash;
        var bytes=snap.Bytes("data/nodes.json")!; Array.Fill(bytes,(byte)'x');
        Assert.Equal(Nodes,snap.Text("data/nodes.json")); Assert.Equal(hash,snap.InputHash); Assert.NotSame(bytes,snap.Bytes("data/nodes.json"));
    }
    [Fact] public void Snapshot_MissingInputReturnsNullForTextAndBytes()
    {
        using var f=new Fixture(); var snap=CheckSnapshot.Capture(f.Open());
        Assert.Null(snap.Bytes("data/missing.json")); Assert.Null(snap.Text("map"));
    }
    [Fact] public void Snapshot_IdenticalInputsAcrossDifferentFoldersHaveSameHash()
    {
        using var a=new Fixture(); using var b=new Fixture();
        Assert.Equal(CheckSnapshot.Capture(a.Open(),Map()).InputHash,CheckSnapshot.Capture(b.Open(),Map()).InputHash);
    }
    [Theory] [InlineData("data")] [InlineData("schema")] [InlineData("map")]
    public void Snapshot_ChangingAnyCapturedInputChangesItsHash(string kind)
    {
        using var f=new Fixture(); var p=f.Open(); var m=Map(); var before=CheckSnapshot.Capture(p,m).InputHash;
        if(kind=="data")f.Put("nodes.json",Nodes+" ");
        if(kind=="schema")File.AppendAllText(Path.Combine(f.Schemas,"nodes.schema.json")," ");
        var after=CheckSnapshot.Capture(p,kind=="map"?Map(",\"sentinels\":[\"void\"]"):m).InputHash;
        Assert.NotEqual(before,after);
    }
    [Fact] public void Snapshot_OpenCopyIsReadOnlyContainsFrozenDraftAndIndependentFiles()
    {
        using var f=new Fixture(); var p=f.Open(); var s=Session(p); Edit(s,"Captured draft"); var snap=CheckSnapshot.Capture(p,Map()); Edit(s,"Later draft");
        var disk=f.Disk(); var copy=snap.OpenCopy(Path.Combine(f.Root,"private-copy"),out var m);
        Assert.True(copy.ReadOnly); Assert.NotNull(m); Assert.Equal("Captured draft",Session(copy).Current.At("/nodes/0/name")!.Value.GetString());
        Assert.NotEqual(p.DataFolder,copy.DataFolder); Assert.Equal(disk,f.Disk());
        Assert.All(snap.Names.Where(n=>n!="map"),n=>Assert.Equal(snap.Bytes(n),File.ReadAllBytes(Path.Combine(f.Root,"private-copy",n))));
    }
    [Fact] public void Snapshot_ExternalNewDataFileIsIncludedWithoutAnEditorRefresh()
    {
        using var f=new Fixture(); var p=f.Open(); f.Put("new.json","""{"selfAuthored":true}""");
        var snap=CheckSnapshot.Capture(p);
        Assert.Contains("data/new.json",snap.Names); Assert.Equal("""{"selfAuthored":true}""",snap.Text("data/new.json"));
    }
    [Fact] public async Task Freshness_ExternalNewFileImmediatelyInvalidatesCompletedCleanRun()
    {
        using var f=new Fixture(); var p=f.Open(); var r=Start(new Checks().Add(new Checker()),p); await End(r); Assert.True(r.Clean(p));
        f.Put("new.json","{}"); Assert.True(r.IsStale(p)); Assert.False(r.Clean(p)); Assert.Contains("stale",r.Verdict(p));
    }
    [Theory] [InlineData("delete")] [InlineData("rename")] [InlineData("replace")] [InlineData("schema-add")] [InlineData("schema-delete")]
    public void Freshness_DiskInputSetAndStampsInvalidateSnapshots(string change)
    {
        using var f=new Fixture(); var p=f.Open(); var snap=CheckSnapshot.Capture(p); var path=Path.Combine(f.Data,"nodes.json");
        switch(change)
        {
            case "delete":File.Delete(path);break;
            case "rename":File.Move(path,Path.Combine(f.Data,"renamed.json"));break;
            case "replace":f.Put("nodes.json",Nodes+"  ");break;
            case "schema-add":File.WriteAllText(Path.Combine(f.Schemas,"extra.schema.json"),"{}");break;
            case "schema-delete":File.Delete(Path.Combine(f.Schemas,"nodes.schema.json"));break;
        }
        Assert.NotEqual(snap.Signature,CheckSnapshot.SignatureOf(p));
    }
    [Fact] public async Task Freshness_EditThenUndoToSavedMakesTheOldResultCurrentAgain()
    {
        using var f=new Fixture(); var p=f.Open(); var s=Session(p); var r=Start(new Checks().Add(new Checker()),p); await End(r);
        Edit(s); Assert.True(r.IsStale(p)); Assert.False(r.Clean(p)); Assert.True(s.Undo()); Assert.False(s.Dirty);
        Assert.False(r.IsStale(p)); Assert.True(r.Clean(p));
    }
    // Found by A165: a check that reads files outside the project (a game's own data) must go stale when they change.
    private sealed class ReadsOutside(string outside) : ICheck
    {
        public CheckDefinition Definition { get; } = new("outside.audit", "Audit with outside data", "Reads an invented outside file.", TimeSpan.FromSeconds(5), Reads: [outside]);
        public Task<CheckReport> RunAsync(CheckSnapshot s, IProgress<CheckProgress> p, CancellationToken c) => Task.FromResult(CleanReport());
    }
    // A179: the panel asks every frame, so it may reuse an answer for a short while; the exact answer is always IsStale(project).
    [Fact] public async Task Stale_APanelsAnswerIsReusedForItsAgeAndTheExactOneNeverIs()
    {
        using var f=new Fixture(); var p=f.Open();
        var folder=Directory.CreateTempSubdirectory("outside-age-").FullName;
        try
        {
            var file=Path.Combine(folder,"axes.json"); File.WriteAllText(file,"{}");
            var r=Start(new Checks().Add(new ReadsOutside(folder)),p,"outside.audit"); await End(r);
            Assert.False(r.IsStale(p,TimeSpan.FromHours(1)));
            File.WriteAllText(file,"{\"changed\":true}");
            Assert.True(r.IsStale(p));                              // exact: at once
            Assert.False(r.IsStale(p,TimeSpan.FromHours(1)));       // the panel's: the answer it had, within its age
            Assert.True(r.IsStale(p,TimeSpan.Zero));                 // no age: asks again
            Assert.Contains("stale",r.Verdict(p));
        }
        finally { Directory.Delete(folder,recursive:true); }
    }
    [Fact] public async Task Stale_WhenAnOutsideFileTheCheckReadsChanges()
    {
        using var f=new Fixture(); var p=f.Open();
        var folder=Directory.CreateTempSubdirectory("outside-reads-").FullName;
        try
        {
            var file=Path.Combine(folder,"axes.json"); File.WriteAllText(file,"{\"a\":1}");
            var r=Start(new Checks().Add(new ReadsOutside(folder)),p,"outside.audit"); await End(r);
            Assert.True(r.Clean(p)); Assert.False(r.IsStale(p));
            File.WriteAllText(file,"{\"a\":22}");   // a different size and a new write: the old result can't speak for it
            Assert.True(r.IsStale(p)); Assert.False(r.Clean(p)); Assert.Contains("stale",r.Verdict(p));
            var again=Start(new Checks().Add(new ReadsOutside(folder)),p,"outside.audit"); await End(again);
            File.WriteAllText(Path.Combine(folder,"new.json"),"{}");   // a file added to a folder it reads
            Assert.True(again.IsStale(p));
        }
        finally { Directory.Delete(folder,recursive:true); }
    }
    [Fact] public async Task Worker_ReceivesFrozenBytesEvenWhileLiveDraftChanges()
    {
        using var f=new Fixture(); using var held=new Held(); var p=f.Open(); var s=Session(p);
        var r=Start(new Checks().Add(held.Check),p,held.Check.Definition.Id); var captured=await held.Entered.Task.WaitAsync(Guard);
        Edit(s,"Edit during work"); Assert.Equal(Nodes,captured.Text("data/nodes.json")); Assert.False(r.Clean(p)); Assert.True(r.IsStale(p));
        held.Release.SetResult(CleanReport()); await End(r); Assert.Equal(CheckState.Completed,r.State); Assert.Contains("stale",r.Verdict(p)); Assert.False(r.Clean(p));
    }
    [Fact] public async Task Worker_SynchronousPrefixDoesNotBlockStartAndEditing()
    {
        using var f=new Fixture(); using var release=new ManualResetEventSlim(); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check=new Checker(body:(_,_,_)=>{entered.TrySetResult();Assert.True(release.Wait(Guard));return Task.FromResult(CleanReport());});
        var p=f.Open(); var s=Session(p); CheckRun? r=null;
        try { r=Start(new Checks().Add(check),p); await entered.Task.WaitAsync(Guard); Assert.False(r.Ended.IsCompleted); Edit(s); }
        finally { release.Set(); }
        await End(r!); Assert.Equal(CheckState.Completed,r!.State);
    }
    [Fact] public async Task Admission_SecondRunOfSameCheckIsRefusedUntilTerminal()
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var c=new Checks().Add(h.Check); var r=Start(c,p,h.Check.Definition.Id);
        await h.Entered.Task.WaitAsync(Guard); Assert.Null(c.Start(h.Check.Definition.Id,p,out var why)); Assert.False(string.IsNullOrWhiteSpace(why));
        Assert.Same(r,c.Last(h.Check.Definition.Id)); Assert.Equal(1,h.Check.Calls); h.Release.SetResult(CleanReport()); await End(r);
        var second=Start(c,p,h.Check.Definition.Id); await End(second); Assert.NotSame(r,second); Assert.Same(second,c.Last(h.Check.Definition.Id));
    }
    [Theory] [InlineData(true,0,"clean")] [InlineData(false,0,"incomplete")] [InlineData(true,1,"errors")] [InlineData(true,2,"warnings")]
    public async Task Result_VerdictAndCleanSeparateCompletenessFromSeverity(bool complete,int severity,string verdict)
    {
        using var f=new Fixture(); var p=f.Open(); var findings=severity==0?Array.Empty<CheckFinding>():new[]{Finding(severity==1?Severity.Error:Severity.Warning)};
        var check=new Checker(body:(_,_,_)=>Task.FromResult(new CheckReport(complete,findings,"Invented report.")));
        var r=Start(new Checks().Add(check),p); await End(r);
        Assert.Equal(CheckState.Completed,r.State); Assert.Equal(verdict,r.Verdict(p)); Assert.Equal(complete&&severity==0,r.Clean(p));
        Assert.Equal(findings,r.Report!.Findings); Assert.Null(r.Problem);
    }
    [Fact] public async Task Result_MixedWarningsAndErrorsKeepEveryFindingAndPreferErrorVerdict()
    {
        using var f=new Fixture(); var p=f.Open(); var findings=new[]{Finding(Severity.Warning,"Warning A"),Finding(Severity.Error,"Error B"),Finding(Severity.Warning,"Warning A")};
        var r=Start(new Checks().Add(new Checker(body:(_,_,_)=>Task.FromResult(new CheckReport(true,findings,"Three.")))),p); await End(r);
        Assert.Equal("errors",r.Verdict(p)); Assert.Equal(findings,r.Report!.Findings); Assert.Equal(3,r.Report.Findings.Count); Assert.False(r.Clean(p));
    }
    [Fact] public async Task Result_ThrowIsFailedExecutionWithExplanationAndNoCleanReport()
    {
        using var f=new Fixture(); var p=f.Open(); var r=Start(new Checks().Add(new Checker(body:(_,_,_)=>throw new InvalidOperationException("Invented checker crash"))),p);
        await End(r); Assert.Equal(CheckState.Failed,r.State); Assert.Contains("Invented checker crash",r.Problem); Assert.Null(r.Report); Assert.False(r.Clean(p)); Assert.Equal("failed",r.Verdict(p));
    }
    [Theory] [InlineData("null-report")] [InlineData("null-findings")]
    public async Task Result_MalformedOutputFailsInsteadOfPassing(string kind)
    {
        using var f=new Fixture(); var p=f.Open();
        var check=new Checker(body:(_,_,_)=>Task.FromResult(kind=="null-report"?null!:new CheckReport(true,null!,"Malformed.")));
        var r=Start(new Checks().Add(check),p); await End(r); Assert.Equal(CheckState.Failed,r.State); Assert.False(r.Clean(p)); Assert.NotNull(r.Problem); Assert.Null(r.Report);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(0.375)]
    public async Task Progress_ValidFractionAndTextAreVisibleDuringWork(double fraction)
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard);
        h.Progress!.Report(new(fraction,"Counting amber beacons")); Assert.Equal(fraction,r.Progress!.Fraction); Assert.Equal("Counting amber beacons",r.Progress.Message);
        Assert.False(r.IsOver); Assert.False(r.Clean(p)); h.Release.SetResult(CleanReport()); await End(r);
    }
    [Fact] public async Task Progress_IndeterminateIsRepresentedByNullAndNotPretendCompletion()
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard);
        h.Progress!.Report(new(null,"Discovering inputs")); Assert.Null(r.Progress!.Fraction); Assert.Equal("Discovering inputs",r.Progress.Message); Assert.False(r.Clean(p));
        h.Release.SetResult(CleanReport()); await End(r);
    }
    [Theory] [InlineData(-0.1)] [InlineData(1.1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public async Task Progress_InvalidValuesCannotReplaceLastHonestProgress(double fraction)
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard);
        var previous=new CheckProgress(0.25,"First quarter"); h.Progress!.Report(previous); h.Progress.Report(new(fraction,"Invalid"));
        Assert.Equal(previous,r.Progress); h.Release.SetResult(CleanReport()); await End(r);
    }
    [Fact] public async Task Progress_LateMessageCannotAlterTerminalProgressOrReport()
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard);
        h.Progress!.Report(new(0.5,"Half")); h.Release.SetResult(CleanReport()); await End(r); var report=r.Report; var progress=r.Progress;
        h.Progress.Report(new(1,"Late")); Assert.Same(report,r.Report); Assert.Same(progress,r.Progress); Assert.Equal(CheckState.Completed,r.State);
    }
    [Fact] public async Task Cancellation_RequestReachesWorkerAndLateSuccessCannotTurnItClean()
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard);
        Assert.True(r.Cancel()); r.Cancel(); r.Cancel(); await h.TokenSeen.Task.WaitAsync(Guard); h.Release.SetResult(CleanReport()); await h.Check.Work!.WaitAsync(Guard); await End(r);
        Assert.Equal(CheckState.Cancelled,r.State); Assert.False(r.Clean(p)); Assert.Null(r.Report); Assert.NotNull(r.Problem);
    }
    [Fact] public async Task Cancellation_TerminalCompletionCannotBeCancelledOrRewritten()
    {
        using var f=new Fixture(); var p=f.Open(); var r=Start(new Checks().Add(new Checker()),p); await End(r); var report=r.Report;
        Assert.False(r.Cancel()); Assert.False(r.Cancel()); Assert.Equal(CheckState.Completed,r.State); Assert.Same(report,r.Report); Assert.True(r.Clean(p));
    }
    [Fact] public async Task Timeout_TokenIgnoringWorkerStillEndsLogicallyAndLateReportIsIgnored()
    {
        using var f=new Fixture(); using var h=new Held(timeout:TimeSpan.FromMilliseconds(400)); var p=f.Open(); var s=Session(p);
        var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard); await End(r);
        Assert.Equal(CheckState.TimedOut,r.State); Assert.Equal("timed out",r.Verdict(p)); Assert.False(r.Clean(p)); Assert.Null(r.Report); Assert.NotNull(r.Problem);
        var problem=r.Problem; h.Progress!.Report(new(1,"Late success")); h.Release.SetResult(CleanReport()); await h.Check.Work!.WaitAsync(Guard); Edit(s);
        Assert.Equal(CheckState.TimedOut,r.State); Assert.Equal(problem,r.Problem); Assert.Null(r.Report); Assert.Null(r.Progress); Assert.False(r.Cancel());
    }
    [Fact] public async Task Timeout_CooperativeCancellationIsATimeoutNotACheckerCrash()
    {
        using var f=new Fixture(); var p=f.Open(); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check=new Checker(timeout:TimeSpan.FromMilliseconds(400),body:async(_,_,c)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,c);return CleanReport();});
        var r=Start(new Checks().Add(check),p); await entered.Task.WaitAsync(Guard); await End(r); Assert.Equal(CheckState.TimedOut,r.State); Assert.False(r.Clean(p)); Assert.Null(r.Report);
    }
    [Fact] public async Task LinkCheck_NoMapCompletesWithoutClaimingLinksWereInspected()
    {
        using var f=new Fixture(); var p=f.Open(); var r=Start(new Checks().Add(new LinkCheck()),p,"links.all"); await End(r);
        Assert.True(r.Clean(p)); Assert.Empty(r.Report!.Findings); Assert.Contains("map",r.Report.Summary,StringComparison.OrdinalIgnoreCase); Assert.All(p.Files,x=>Assert.Null(x.Session));
    }
    [Theory] [InlineData("found",0)] [InlineData("sentinel",0)] [InlineData("missing",1)] [InlineData("ambiguous",1)] [InlineData("scope",1)] [InlineData("notloaded",1)]
    public async Task LinkCheck_ClassifiesEveryDocumentedLinkOutcome(string kind,int count)
    {
        using var f=new Fixture(); var target=kind=="sentinel"?"void":kind=="missing"?"lost":"beacon"; f.Put("routes.json","""{"routes":[{"target":""" + JsonSerializer.Serialize(target) + "}]}");
        if(kind=="ambiguous")f.Put("nodes.json","""{"nodes":[{"id":"beacon","name":"One"},{"id":"beacon","name":"Two"}]}""");
        var map=kind=="notloaded"?Map(target:"""{"file":"absent.json","list":"/nodes","key":"id"}""")
            :Map(kind=="sentinel"?",\"sentinels\":[\"void\"]":kind=="scope"?",\"scope\":\"same invented district\"":"");
        var p=f.Open(); var disk=f.Disk(); var links=map.Links(p).ToList();
        Assert.Single(links);
        Assert.Equal(kind switch{"found"=>ReferenceOutcome.Found,"sentinel"=>ReferenceOutcome.Sentinel,"missing"=>ReferenceOutcome.Missing,"ambiguous"=>ReferenceOutcome.Ambiguous,"scope"=>ReferenceOutcome.NeedsScope,_=>ReferenceOutcome.NotLoaded},links[0].Result.Outcome);
        var r=Start(new Checks().Add(new LinkCheck()),p,"links.all",map); await End(r);
        Assert.Equal(CheckState.Completed,r.State); Assert.Equal(count,r.Report!.Findings.Count); Assert.Equal(count==0,r.Clean(p));
        if(count>0){var finding=Assert.Single(r.Report.Findings);Assert.Equal(kind=="scope"?Severity.Warning:Severity.Error,finding.Problem.Severity);Assert.Equal(new CheckPlace("routes.json","/routes/0","/routes/0/target"),finding.Place);Assert.False(string.IsNullOrWhiteSpace(finding.Next));}
        Assert.Equal(disk,f.Disk());
    }
    [Fact] public async Task LinkCheck_MalformedSourceIsAnIncompleteFindingNotASilentSkip()
    {
        using var f=new Fixture(); f.Put("routes.json","{invalid"); var p=f.Open(); var r=Start(new Checks().Add(new LinkCheck()),p,"links.all",Map()); await End(r);
        Assert.Equal(CheckState.Completed,r.State); Assert.False(r.Report!.Complete); Assert.Contains(r.Report.Findings,x=>x.Place?.File=="routes.json"&&x.Problem.Severity==Severity.Error);
        Assert.False(r.Clean(p)); Assert.All(p.Files,x=>Assert.Null(x.Session));
    }
    [Fact] public async Task LinkCheck_DraftLinkWinsOverSavedValidTarget()
    {
        using var f=new Fixture(); var p=f.Open(); var s=Session(p,"routes.json"); Assert.True(s.Execute(new SetValue("/routes/0/target",JsonSerializer.SerializeToElement("unsaved-missing")))); var disk=f.Disk();
        var r=Start(new Checks().Add(new LinkCheck()),p,"links.all",Map()); await End(r);
        Assert.Contains(r.Report!.Findings,x=>x.Place?.At=="/routes/0/target"); Assert.False(r.Clean(p)); Assert.Equal(disk,f.Disk()); Assert.True(s.Dirty);
    }
    [Fact] public async Task ReadOnly_CheckingPreservesRevisionJournalDirtyUndoAndDisk()
    {
        using var f=new Fixture(); var p=f.Open(readOnly:true); var s=Session(p); var disk=f.Disk(); var rev=s.Revision; var journal=s.Journal.ToArray();
        var r=Start(new Checks().Add(new LinkCheck()),p,"links.all",Map()); await End(r);
        Assert.True(r.Clean(p)); Assert.Equal(rev,s.Revision); Assert.Equal(journal,s.Journal); Assert.False(s.Dirty); Assert.False(s.Undo()); Assert.Equal(disk,f.Disk());
    }
    private sealed class RejectName : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) => d.At("/nodes/0/name")?.GetString()=="forbidden" ? [Issue.Error("Invented rule refuses that name.")] : [];
    }
    [Fact] public async Task Validation_SynchronousRulesRemainEnforcedWhileCheckIsHeld()
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(rules:n=>n=="nodes.json"?new RejectName():null); var s=Session(p); var before=s.Current;
        var r=Start(new Checks().Add(h.Check),p,h.Check.Definition.Id); await h.Entered.Task.WaitAsync(Guard);
        Assert.False(s.Execute(new SetValue("/nodes/0/name",JsonSerializer.SerializeToElement("forbidden")))); Assert.Equal(before,s.Current); Assert.False(s.Dirty);
        h.Release.SetResult(CleanReport()); await End(r); Assert.True(r.Clean(p));
    }

    [Fact] public void Panel_StartsClosedAndExposesOneRunCommandPerRegisteredCheck()
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open()); w.Checks.Add(new Checker());
        var panel=w.ChecksPanel; Assert.Equal("project.checks",panel.Id); Assert.False(panel.OpenAtStart);
        Assert.Equal(new[]{"run.links.all","run.sample.audit"},panel.Commands!.Select(x=>x.Id));
        Assert.Contains(panel.Commands!,x=>x.Label.Contains("Audit beacons",StringComparison.Ordinal));
    }
    [Fact] public async Task Panel_RunCommandUsesSameChecksServiceAndBusyReason()
    {
        using var f=new Fixture(); using var h=new Held(); var w=new DataWorkspace(f.Open()); w.Checks.Add(h.Check);
        var command=Assert.Single(w.ChecksPanel.Commands!,x=>x.Id=="run.held.audit"); Assert.Null(command.Unavailable!());
        Assert.True(command.Run()); await h.Entered.Task.WaitAsync(Guard); Assert.NotNull(command.Unavailable());
        Assert.False(command.Run()); var r=w.Checks.Last("held.audit")!; Assert.False(string.IsNullOrWhiteSpace(w.Said));
        h.Release.SetResult(CleanReport()); await End(r); Assert.Null(command.Unavailable()); Assert.True(r.Clean(w.Project));
    }
    [Fact] public void Panel_UnknownRunSaysWhyInsteadOfStartingAnything()
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open()); Assert.False(w.RunCheck("unknown.audit")); Assert.False(string.IsNullOrWhiteSpace(w.Said)); Assert.Null(w.Checks.Last("unknown.audit"));
    }
    [Theory] [InlineData("exact")] [InlineData("item")] [InlineData("file")] [InlineData("nested")] [InlineData("named")]
    public void Reveal_SelectsKnownOwnerOrFileWithoutEditing(string where)
    {
        using var f=new Fixture(); var p=f.Open(); var w=new DataWorkspace(p,map:Map()); Assert.True(w.Show("routes.json")); w.Window!.Editor.Select("/routes/1"); var disk=f.Disk();
        var place=where switch{"exact"=>new CheckPlace("nodes.json","/nodes/0","/nodes/0/name"),"item"=>new CheckPlace("nodes.json","/nodes/0"),"nested"=>new CheckPlace("nodes.json","/nodes/0","/nodes/0/details/missing"),"named"=>new CheckPlace("nodes.json","a_template_id"),_=>new CheckPlace("nodes.json")};   // named: an item that isn't a pointer shows the file, not a crash (A165)
        var finding=Finding() with {Place=place}; Assert.True(w.Reveal(finding)); Assert.Equal("nodes.json",w.Active!.Name);
        if(where is not ("file" or "named"))Assert.Equal("/nodes/0",w.Window!.Editor.Primary);
        if(where=="nested")Assert.Contains("/details/missing",w.Said);
        Assert.All(p.Files.Where(x=>x.Session!=null),x=>{Assert.False(x.Session!.Dirty);Assert.Empty(x.Session.Journal);Assert.Equal(0,x.Session.Revision);});
        Assert.Equal(disk,f.Disk()); if(where is not ("file" or "named")){Assert.True(w.Back());Assert.Equal("routes.json",w.Active!.Name);Assert.Equal("/routes/1",w.Window!.Editor.Primary);}
    }
    [Theory] [InlineData("no-place")] [InlineData("missing-file")]
    public void Reveal_RefusesUnknownPlaceAndKeepsActiveFile(string where)
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open()); Assert.True(w.Show("routes.json"));
        Assert.False(w.Reveal(Finding() with{Place=where=="no-place"?null:new CheckPlace("gone.json")})); Assert.Equal("routes.json",w.Active!.Name);
    }
    [Fact] public async Task Reveal_StaleIndexFindingCannotSilentlySelectADifferentItem()
    {
        using var f=new Fixture(); var p=f.Open(); var w=new DataWorkspace(p,map:Map()); Assert.True(w.Show("nodes.json"));
        var original=Finding(); var c=new Checker(body:(_,_,_)=>Task.FromResult(new CheckReport(true,[original],"One."))); w.Checks.Add(c);
        Assert.True(w.RunCheck("sample.audit")); var run=w.Checks.Last("sample.audit")!; await End(run);
        // Replace the list so /nodes/0 is now Birch dock, while the receipt names Amber beacon.
        Assert.True(w.Active!.Session!.Execute(new SetValue("/nodes",JsonSerializer.SerializeToElement(new[]{new{id="dock",name="Birch dock"}}))));
        Assert.True(run.IsStale(p)); Assert.False(w.Reveal(run.Report!.Findings[0])); Assert.Equal(new CheckPlace("nodes.json","/nodes/0","/nodes/0/name"),original.Place);
    }
    [Fact] public async Task Freshness_SelectionAndTableViewChangesDoNotInvalidateACurrentRun()
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open()); Assert.True(w.Show("nodes.json")); w.Checks.Add(new Checker()); Assert.True(w.RunCheck("sample.audit"));
        var r=w.Checks.Last("sample.audit")!; await End(r); w.Window!.Editor.Select("/nodes/1"); w.TableOf("nodes.json")!.Filter="Birch";
        Assert.True(w.Show("routes.json")); Assert.False(r.IsStale(w.Project)); Assert.True(r.Clean(w.Project));
    }
    private static JsonObject Rpc(McpServer server,string tool,JsonObject? arguments=null)
    {
        var request=new JsonObject{["jsonrpc"]="2.0",["id"]=66,["method"]="tools/call",["params"]=new JsonObject{["name"]=tool,["arguments"]=arguments??new JsonObject()}};
        return server.Handle(request.ToJsonString())!;
    }
    private static JsonObject Receipt(JsonObject rpc)
        => JsonNode.Parse(rpc["result"]!["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
    private static (int Exit,JsonObject? Receipt,string Error) Command(Fixture f,params string[] tail)
    {
        using var o=new StringWriter(); using var e=new StringWriter(); var args=new[]{"--data",f.Data,"--schemas",f.Schemas}.Concat(tail).ToArray();
        var exit=Cli.Run(args,new StringReader(""),o,e); var text=o.ToString(); return(exit,text.Length==0?null:JsonNode.Parse(text)!.AsObject(),e.ToString());
    }
    [Fact] public void Agent_ListHasDefinitionsNextHintAndLastVerdictAfterRunning()
    {
        using var f=new Fixture(); var agent=new AgentSession(f.Open(),Map()); var before=agent.ListChecks();
        var entry=Assert.Single(before["checks"]!.AsArray())!.AsObject(); Assert.Equal("links.all",entry["id"]!.GetValue<string>()); Assert.False(string.IsNullOrWhiteSpace(entry["description"]!.GetValue<string>())); Assert.Equal("never run",entry["last"]!.GetValue<string>());
        Assert.NotNull(before["next"]); Assert.True(agent.RunCheck("links.all")["clean"]!.GetValue<bool>());
        Assert.Equal("clean",agent.ListChecks()["checks"]![0]!["last"]!.GetValue<string>());
    }
    [Fact] public void Agent_ReceiptIncludesEveryFindingSeverityLocationNextAndHash()
    {
        using var f=new Fixture(); var p=f.Open(); var a=new AgentSession(p); var findings=Enumerable.Range(0,75).Select(i=>Finding(i%2==0?Severity.Warning:Severity.Error,"Finding "+i)).ToArray();
        a.Checks.Add(new Checker(body:(_,_,_)=>Task.FromResult(new CheckReport(true,findings,"Seventy-five, including repeated places."))));
        var receipt=a.RunCheck("sample.audit"); Assert.False(receipt["clean"]!.GetValue<bool>()); Assert.Equal("Completed",receipt["state"]!.GetValue<string>());
        Assert.Equal(CheckSnapshot.Capture(p).InputHash,receipt["inputHash"]!.GetValue<string>()); var returned=receipt["findings"]!.AsArray(); Assert.Equal(75,returned.Count);
        for(int i=0;i<findings.Length;i++)
        {
            var x=returned[i]!; var expected=findings[i]; Assert.Equal(expected.Problem.Message,x["message"]!.GetValue<string>()); Assert.Equal(expected.Problem.Severity.ToString().ToLowerInvariant(),x["severity"]!.GetValue<string>());
            Assert.Equal(expected.Place!.File,x["file"]!.GetValue<string>()); Assert.Equal(expected.Place.Item,x["item"]!.GetValue<string>()); Assert.Equal(expected.Place.At,x["at"]!.GetValue<string>()); Assert.Equal(expected.Next,x["next"]!.GetValue<string>());
        }
    }
    [Theory] [InlineData("failed")] [InlineData("incomplete")] [InlineData("warning")]
    public void Agent_NonCleanResultsStayNonCleanAndExplainNextStep(string kind)
    {
        using var f=new Fixture(); var a=new AgentSession(f.Open()); a.Checks.Add(new Checker(body:(_,_,_)=>kind=="failed"?throw new InvalidOperationException("Invented failure"):Task.FromResult(new CheckReport(kind!="incomplete",kind=="warning"?[Finding(Severity.Warning)]:[],"Partial or warning."))));
        var result=a.RunCheck("sample.audit"); Assert.False(result["clean"]!.GetValue<bool>()); Assert.Equal(kind=="failed"?"Failed":"Completed",result["state"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(result["next"]!.GetValue<string>())); if(kind=="failed")Assert.Contains("Invented failure",result["problem"]!.GetValue<string>());
    }
    [Fact] public void Agent_UnregisteredIdIsAnExplainedRefusal()
    {
        using var f=new Fixture(); var a=new AgentSession(f.Open()); var result=a.RunCheck("not.registered"); Assert.NotNull(result["error"]); Assert.Contains("checks",result["next"]!.GetValue<string>()); Assert.Null(a.Checks.Last("not.registered"));
    }
    [Fact] public async Task Agent_BusyCallIsRefusedWithoutStartingAnotherWorker()
    {
        using var f=new Fixture(); using var h=new Held(); var p=f.Open(); var a=new AgentSession(p); a.Checks.Add(h.Check);
        var r=Start(a.Checks,p,"held.audit"); await h.Entered.Task.WaitAsync(Guard); var result=a.RunCheck("held.audit");
        Assert.NotNull(result["error"]); Assert.Equal(1,h.Check.Calls); h.Release.SetResult(CleanReport()); await End(r);
    }
    [Fact] public void Agent_UnsavedDraftIsCheckedWithoutSaving()
    {
        using var f=new Fixture(); var p=f.Open(); var a=new AgentSession(p,Map()); var disk=f.Disk();
        var edit=a.Set("routes.json","/routes/0/target","\"agent-missing\""); Assert.True(edit["applied"]!.GetValue<bool>());
        var receipt=a.RunCheck("links.all"); Assert.False(receipt["clean"]!.GetValue<bool>()); Assert.Single(receipt["findings"]!.AsArray()); Assert.Equal(disk,f.Disk()); Assert.True(p.Find("routes.json")!.Dirty);
    }
    [Fact] public async Task AgentAndEditor_SeparateServicesOnSameProjectAgreeOnHashAndEveryFinding()
    {
        using var f=new Fixture(); f.Put("routes.json","""{"routes":[{"target":"lost"},{"target":"beacon"}]}"""); var p=f.Open(); var m=Map();
        var w=new DataWorkspace(p,map:m); var a=new AgentSession(p,m); Assert.True(w.RunCheck("links.all")); var r=w.Checks.Last("links.all")!; await End(r);
        var receipt=a.RunCheck("links.all"); Assert.Equal(r.Snapshot.InputHash,receipt["inputHash"]!.GetValue<string>()); Assert.Equal(r.Verdict(p),receipt["verdict"]!.GetValue<string>()); Assert.Equal(r.Clean(p),receipt["clean"]!.GetValue<bool>());
        Assert.Equal(r.Report!.Findings.Count,receipt["findings"]!.AsArray().Count);
        for(int i=0;i<r.Report.Findings.Count;i++){var expected=r.Report.Findings[i];var x=receipt["findings"]![i]!;Assert.Equal(expected.Problem.Message,x["message"]!.GetValue<string>());Assert.Equal(expected.Next,x["next"]!.GetValue<string>());Assert.Equal(expected.Place!.At,x["at"]!.GetValue<string>());}
    }
    [Fact] public void Mcp_CheckToolsAreDiscoverableWithStringIdRequired()
    {
        var tools=McpServer.Tools(); Assert.Single(tools,x=>x!["name"]!.GetValue<string>()=="checks"); var check=Assert.Single(tools,x=>x!["name"]!.GetValue<string>()=="check")!;
        Assert.Contains(check["inputSchema"]!["required"]!.AsArray(),x=>x!.GetValue<string>()=="id"); Assert.Equal("string",check["inputSchema"]!["properties"]!["id"]!["type"]!.GetValue<string>());
    }
    [Theory] [InlineData("{}")] [InlineData("{\"id\":null}")] [InlineData("{\"id\":42}")] [InlineData("{\"id\":[]}")]
    public void Mcp_BadCheckArgumentsAreRefusedWithoutRunning(string json)
    {
        using var f=new Fixture(); var a=new AgentSession(f.Open(),Map()); var result=Rpc(new McpServer(a),"check",JsonNode.Parse(json)!.AsObject());
        Assert.True(result["result"]!["isError"]!.GetValue<bool>()); Assert.NotNull(Receipt(result)["error"]); Assert.Null(a.Checks.Last("links.all"));
    }
    [Fact] public void Mcp_CheckAndListMatchAgentReceiptsAndPreserveAllFields()
    {
        using var f=new Fixture(); f.Put("routes.json","""{"routes":[{"target":"lost"}]}"""); var a=new AgentSession(f.Open(),Map()); var server=new McpServer(a);
        Assert.True(JsonNode.DeepEquals(a.ListChecks(),Receipt(Rpc(server,"checks"))));
        var expected=a.RunCheck("links.all"); var result=Rpc(server,"check",new JsonObject{["id"]="links.all"});
        var got=Receipt(result); Assert.NotNull(expected["run"]); Assert.NotEqual(expected["run"]!.GetValue<string>(),got["run"]!.GetValue<string>()); expected.Remove("run"); got.Remove("run"); /* two separate runs, so their run ids differ (A376) */ Assert.True(JsonNode.DeepEquals(expected,got)); Assert.Equal("errors",Receipt(Rpc(server,"checks"))["checks"]![0]!["last"]!.GetValue<string>());
    }
    [Theory] [InlineData("checks",0)] [InlineData("clean",0)] [InlineData("missing",1)] [InlineData("unknown",1)] [InlineData("missing-id",2)] [InlineData("warning",1)]
    public void Cli_OneJsonReceiptAndDocumentedExitClasses(string kind,int exit)
    {
        using var f=new Fixture(); var map=Map();
        if(kind=="missing")f.Put("routes.json","""{"routes":[{"target":"not-there"}]}""");
        if(kind=="warning")map=Map(",\"scope\":\"same invented district\"");
        var path=Path.Combine(f.Root,"map.json"); File.WriteAllText(path,map.Text); var disk=f.Disk();
        var tail=new List<string>{"--map",path,"--read-only"};
        tail.Add(kind=="checks"?"checks":"check"); if(kind!="checks"&&kind!="missing-id")tail.Add(kind=="unknown"?"not.registered":"links.all");
        var result=Command(f,tail.ToArray()); Assert.Equal(exit,result.Exit); Assert.Equal(disk,f.Disk());
        if(exit==2){Assert.Null(result.Receipt);Assert.Contains("arguments",result.Error);return;}
        Assert.NotNull(result.Receipt); Assert.Equal("",result.Error);
        if(kind=="checks")Assert.Single(result.Receipt!["checks"]!.AsArray());
        else if(kind=="unknown")Assert.NotNull(result.Receipt!["error"]);
        else {Assert.Equal(exit==0,result.Receipt!["clean"]!.GetValue<bool>());Assert.Equal("Completed",result.Receipt["state"]!.GetValue<string>());}
    }
    [Fact] public void Cli_SeparateProcessAuthorityUsesDiskRatherThanAnotherEditorsDraft()
    {
        using var f=new Fixture(); var p=f.Open(); var a=new AgentSession(p,Map()); Assert.True(a.Set("routes.json","/routes/0/target","\"draft-lost\"")["applied"]!.GetValue<bool>());
        Assert.False(a.RunCheck("links.all")["clean"]!.GetValue<bool>()); var path=Path.Combine(f.Root,"map.json"); File.WriteAllText(path,Map().Text);
        var result=Command(f,"--map",path,"check","links.all"); Assert.Equal(0,result.Exit); Assert.True(result.Receipt!["clean"]!.GetValue<bool>()); Assert.True(p.Find("routes.json")!.Dirty);
    }

    private sealed record Caption(int Width,int Height,(int X,int Y)[] Ink,(int X,int Y)[] Paper);
    private static readonly Dictionary<(string Text,float Scale,float Wrap),Caption> captions=new();
    private static Caption CaptionFor(string text,float scale=1,float wrap=-1,HeadlessHost? existing=null)
    {
        if(captions.TryGetValue((text,scale,wrap),out var cached))return cached;
        using var owned=existing is null?new HeadlessHost((int)(700*scale),(int)(300*scale)):null;
        var h=existing??owned!;
        EditorWindow<DataDocument>.SetTextScale(scale);
        float2 start=default,end=default;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(700*scale,300*scale),ImGuiCond.Always);
            ImGui.Begin("A066 caption reference"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings);
            if(wrap>0) ImGui.PushTextWrapPos(ImGui.GetCursorPos().X+wrap);
            ImGui.Text(text);start=ImGui.GetItemRectMin();end=ImGui.GetItemRectMax();
            if(wrap>0) ImGui.PopTextWrapPos();
            ImGui.End();
        }
        h.Frames(Draw,4);
        var counts=new Dictionary<uint,int>();
        int x0=(int)start.X,y0=(int)start.Y,x1=(int)Math.Ceiling(end.X),y1=(int)Math.Ceiling(end.Y);
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
        {var c=h.Pixels[y*h.Width+x]&0xffffff;counts[c]=counts.GetValueOrDefault(c)+1;}
        var paper=counts.MaxBy(c=>c.Value).Key;
        static int Distance(uint a,uint b)=>Math.Abs((int)(a&255)-(int)(b&255))+Math.Abs((int)(a>>8&255)-(int)(b>>8&255))+Math.Abs((int)(a>>16&255)-(int)(b>>16&255));
        var ink=counts.Where(c=>c.Value>=2).MaxBy(c=>Distance(c.Key,paper)).Key;
        var painted=new List<(int X,int Y)>();
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)if((h.Pixels[y*h.Width+x]&0xffffff)!=paper)painted.Add((x,y));
        Assert.NotEmpty(painted);int left=painted.Min(p=>p.X),top=painted.Min(p=>p.Y),right=painted.Max(p=>p.X),bottom=painted.Max(p=>p.Y);
        var yes=new List<(int,int)>();var no=new List<(int,int)>();
        for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
        {
            var c=h.Pixels[y*h.Width+x]&0xffffff;
            if(c==ink)yes.Add((x-left,y-top));
            else if(c==paper&&x>left&&x<right&&y>top&&y<bottom)no.Add((x-left,y-top));
        }
        Assert.True(yes.Count>=5&&no.Count>=5);
        var answer=new Caption(right-left+1,bottom-top+1,yes.ToArray(),no.Where((_,i)=>i%Math.Max(1,no.Count/24)==0).ToArray());
        captions[(text,scale,wrap)]=answer;return answer;
    }
    private sealed class Screen : IDisposable
    {
        public HeadlessHost Host {get;}
        public Action Draw {get;}
        private readonly Dictionary<string,Caption> patterns;
        public Screen(DataWorkspace w,params string[] captions):this(w,1,captions) { }
        public Screen(DataWorkspace w,float scale,params string[] captions)
        {
            patterns=captions.Distinct().ToDictionary(x=>x,x=>CaptionFor(x,scale),StringComparer.Ordinal);
            Host=new HeadlessHost(1600,900){Drawing=false}; EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            EditorWindow<DataDocument>.SetTextScale(scale);
            Draw=w.Draw; Assert.True(w.Show("nodes.json")); Host.MoveMouse(Draw,-100,-100); Host.Frames(Draw,5);
        }
        public void Palette(string id)
        {
            Host.Press(Draw,ImGuiKey.P,ctrl:true,shift:true); Host.Press(Draw,ImGuiKey.A,ctrl:true);
            Host.Type(Draw,id); Host.Press(Draw,ImGuiKey.Enter); Host.Frames(Draw,5);
        }
        public (float X,float Y)? Find(string caption)
        {
            Host.Drawing=true; Host.Frame(Draw);
            var p=patterns[caption];
            // Compare every glyph of the same complete text reflowed at the actual native wrap width.
            // The reference is rendered separately in this same host, never cropped to the target panel.
            var evidence=Ui.Snapshot().Nodes.FirstOrDefault(n=>n.Label==caption).TextEvidence;
            if(evidence is { WrapWidth: > 0 } layout)
            {
                p=CaptionFor(caption,EditorWindow<DataDocument>.TextScale,layout.WrapWidth,Host);
                Host.Frame(Draw);
            }
            var window=ImGui.Internal.FindWindowByName("Checks###panel.project.checks"); var rect=window.InnerClipRect;
            int x0=Math.Max(0,(int)Math.Ceiling(rect.Min.X)),y0=Math.Max(0,(int)Math.Ceiling(rect.Min.Y));
            int x1=Math.Min(Host.Width,(int)Math.Floor(rect.Max.X))-p.Width,y1=Math.Min(Host.Height,(int)Math.Floor(rect.Max.Y))-p.Height;
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                var first=p.Ink[0]; var ink=Host.Pixels[(y+first.Y)*Host.Width+x+first.X]&0xffffff;
                int bad=0; foreach(var q in p.Ink)if((Host.Pixels[(y+q.Y)*Host.Width+x+q.X]&0xffffff)!=ink)bad++;
                if(bad>p.Ink.Length*.02)continue;
                bad=0; foreach(var q in p.Paper)
                {
                    var c=Host.Pixels[(y+q.Y)*Host.Width+x+q.X]&0xffffff;
                    int distance=Math.Abs((int)(c&255)-(int)(ink&255))+Math.Abs((int)(c>>8&255)-(int)(ink>>8&255))+Math.Abs((int)(c>>16&255)-(int)(ink>>16&255));
                    if(distance<120)bad++;
                }
                if(bad>p.Paper.Length*.02)continue; Host.Drawing=false; return(x+p.Width/2f,y+p.Height/2f);
            }
            Host.Drawing=false; return null;
        }
        public void Click(string caption)
        { var point=Find(caption); Assert.True(point is not null,"Actual Checks caption not visible: "+caption); Host.Click(Draw,point.Value.X,point.Value.Y); Host.Frames(Draw,4); }
        public void Dispose()=>Host.Dispose();
    }
    [Fact] public async Task Screen_PaletteOpensChecksAndRenderedRunButtonStartsAnotherRun()
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open(),map:Map()); using var ui=new Screen(w,"Run","Check every link: clean");
        ui.Palette("editor.panel_do.project.checks/run.links.all"); var first=w.Checks.Last("links.all"); Assert.NotNull(first); await End(first!);
        Assert.NotNull(ui.Find("Check every link: clean")); ui.Click("Run"); var second=w.Checks.Last("links.all"); Assert.NotSame(first,second); await End(second!); Assert.True(second!.Clean(w.Project));
    }
    [Fact] public async Task Screen_HeldProgressAndRenderedCancelLeadToCancelledVerdict()
    {
        using var f=new Fixture(); using var h=new Held(); var w=new DataWorkspace(f.Open()); w.Checks.Add(h.Check);
        using var ui=new Screen(w,"Cancel","Counting beacons","Audit beacons: cancelled");
        ui.Palette("editor.panel_do.project.checks/run.held.audit"); var run=w.Checks.Last("held.audit"); Assert.NotNull(run); await h.Entered.Task.WaitAsync(Guard);
        h.Progress!.Report(new(null,"Counting beacons")); Assert.NotNull(ui.Find("Counting beacons")); ui.Click("Cancel");
        await h.TokenSeen.Task.WaitAsync(Guard); h.Release.SetResult(CleanReport()); await End(run!); Assert.NotNull(ui.Find("Audit beacons: cancelled")); Assert.False(run!.Clean(w.Project));
    }
    [Theory] [InlineData(1f)] [InlineData(2f)] public async Task Screen_StaleVerdictExplainsNeedForANewRun(float scale)
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open(),map:Map());
        using var ui=new Screen(w,scale,"Check every link: clean\nResults are out of date","Results are out of date because the project changed.\nRun again.");
        ui.Palette("editor.panel_do.project.checks/run.links.all"); var run=w.Checks.Last("links.all")!; await End(run); Edit(w.Active!.Session!);
        // Ended publishes the verdict, not expiry of the panel's 500 ms freshness cache. Palette frames may
        // already have cached clean. Reconcile this edit through the existing exact API before testing its pixels.
        Assert.True(run.IsStale(w.Project,TimeSpan.Zero));
        Assert.True(ui.Find("Check every link: clean\nResults are out of date").HasValue, "The complete stale-result heading must be visible.");
        Assert.True(ui.Find("Results are out of date because the project changed.\nRun again.").HasValue, "The stale project-change explanation must be visible.");
        Assert.False(run.Clean(w.Project));
    }
    [Fact] public async Task Screen_ClickingRenderedFindingNavigatesToItsOwnerWithoutEditing()
    {
        using var f=new Fixture(); var w=new DataWorkspace(f.Open(),map:Map());
        w.Checks.Add(new Checker(body:(_,_,_)=>Task.FromResult(new CheckReport(true,[Finding()],"One invented problem."))));
        using var ui=new Screen(w,"Error: Invented beacon has no destination.");
        ui.Palette("editor.panel_do.project.checks/run.sample.audit"); var run=w.Checks.Last("sample.audit")!; await End(run); var disk=f.Disk();
        ui.Click("Error: Invented beacon has no destination."); Assert.Equal("nodes.json",w.Active!.Name); Assert.Equal("/nodes/0",w.Window!.Editor.Primary);
        Assert.False(w.Active.Dirty); Assert.Empty(w.Active.Session!.Journal); Assert.Equal(disk,f.Disk());
    }

    private sealed class PlainAdapter : IProjectAdapter
    {
        public string Name => "Invented plain adapter";
        public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data,p.Schemas);
        public void Configure(EditorModel<DataDocument> e,DataTableView t) { }
    }
    private sealed class CheckAdapter(params ICheck[] checks) : IProjectAdapter
    {
        public string Name => "Invented check adapter";
        public IEnumerable<ICheck> Checks => checks;
        public DataProject Open(ProjectPaths p) => DataProject.Open(p.Data,p.Schemas);
        public void Configure(EditorModel<DataDocument> e,DataTableView t) { }
    }
    [Fact] public void Adapter_DefaultContributesNoChecks()
    { IProjectAdapter adapter=new PlainAdapter(); Assert.Empty(adapter.Checks); }
    [Fact] public async Task Adapter_CheckAppearsBesideLinkCheckAndCanRunThroughWorkspace()
    {
        using var f=new Fixture(); var l=new ProjectLauncher(new RecentProjects(Path.Combine(f.Root,"recent.json"))){Adapter=new CheckAdapter(new Checker())};
        Assert.True(l.Open(new ProjectPaths(f.Data,f.Schemas))); Assert.Equal(new[]{"links.all","sample.audit"},l.Workspace!.Checks.Registered.Select(d=>d.Id));
        Assert.Contains(l.Workspace.ChecksPanel.Commands!,c=>c.Id=="run.sample.audit"); Assert.True(l.Workspace.RunCheck("sample.audit")); var r=l.Workspace.Checks.Last("sample.audit")!; await End(r); Assert.True(r.Clean(l.Workspace.Project));
    }
    [Fact] public void Adapter_InvalidBatchCannotPublishItsEarlierValidCheck()
    {
        using var f=new Fixture(); var l=new ProjectLauncher(new RecentProjects(Path.Combine(f.Root,"recent.json"))){Adapter=new CheckAdapter(new Checker(),new Checker("links.all"))};
        Assert.True(l.Open(new ProjectPaths(f.Data,f.Schemas))); Assert.Equal(new[]{"links.all"},l.Workspace!.Checks.Registered.Select(d=>d.Id));
        Assert.DoesNotContain(l.Workspace.ChecksPanel.Commands!,c=>c.Id=="run.sample.audit");
    }
}

