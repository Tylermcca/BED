using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// INVENTED fixtures, independent value-stack model, seeds and long-run schedules.
// BORROWED: the documented bounded snapshot history and replay/session contracts.
public sealed class LongSessionProofTests(ITestOutputHelper output)
{
    private const string SchemaName = "state.schema.json";
    private const string Schema = """{"type":"object","required":["n","m","blob"],"additionalProperties":false,"properties":{"n":{"type":"integer","minimum":0},"m":{"type":"integer","minimum":0},"blob":{"type":"string"}}}""";
    private const string Seed = "{\"n\":0,\"m\":0,\"blob\":\"seed\"}\n";
    private static JsonElement J<T>(T value) => JsonSerializer.SerializeToElement(value);
    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "long-session-" + Guid.NewGuid().ToString("N"));
        internal readonly DataProject Project;
        internal readonly DataFile File;
        internal DocumentSession<DataDocument> Session => File.Session!;
        internal string Path => File.Path;
        internal Fixture(string newline = "\n")
        {
            var data = System.IO.Path.Combine(Root,"data");var schemas=System.IO.Path.Combine(Root,"schemas");Directory.CreateDirectory(data);Directory.CreateDirectory(schemas);
            System.IO.File.WriteAllText(System.IO.Path.Combine(data,"state.json"),Seed.Replace("\n",newline),new UTF8Encoding(false));
            System.IO.File.WriteAllText(System.IO.Path.Combine(schemas,SchemaName),Schema);
            Project=DataProject.Open(data,schemas);File=Project.Find("state.json")!;Assert.True(File.Open());
        }
        public void Dispose()
        {
            var boundary=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
            Assert.StartsWith(boundary,System.IO.Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,recursive:true);
        }
    }
    private void Evidence(string name, object value)
    {
        var json=JsonSerializer.Serialize(value);output.WriteLine(json);
        if(Environment.GetEnvironmentVariable("A204_OBSERVATIONS") is {Length:>0} dir)
        {Directory.CreateDirectory(dir);System.IO.File.WriteAllText(System.IO.Path.Combine(dir,name+".json"),json);}
    }
    private static DocumentSession<DataDocument> Replay(DataDocument original,DocumentSession<DataDocument> session,bool roundTrip=true)
    {
        var entries=roundTrip?JournalFile.Read(JournalFile.Write(session.Journal)):session.Journal;
        return DocumentSession<DataDocument>.Replay(original,entries,session.Format,session.Validator,session.Catalog,session.History.Capacity);
    }
    private readonly record struct Values(int N,int M);
    private static Values Current(DocumentSession<DataDocument> s)=>new(s.Current.At("/n")!.Value.GetInt32(),s.Current.At("/m")!.Value.GetInt32());
    private static void ValueCheck(DocumentSession<DataDocument> s,Values v)
    {Assert.Equal(v,Current(s));Assert.Equal("seed",s.Current.At("/blob")!.Value.GetString());}
    public static IEnumerable<object[]> Seeds=>Enumerable.Range(204001,18).Select(n=>new object[]{n});

    [Theory,MemberData(nameof(Seeds))]
    public void Seeded_mix_matches_an_independent_model_and_undo_redo_and_journal_are_exact(int seed)
    {
        using var f=new Fixture();var s=f.Session;var original=s.Current;var opened=s.TextToSave;var disk=System.IO.File.ReadAllBytes(f.Path);
        var random=new Random(seed);var undo=new Stack<Values>();var redo=new Stack<Values>();var model=new Values(0,0);var ops=new List<string>();var refused=0;var changed=0;
        for(var i=0;i<200;i++)
        {
            var pick=random.Next(10);var before=s.Current;var rev=s.Revision;var count=s.Journal.Count;
            if(pick==0)
            {
                var ok=s.Undo();Assert.Equal(undo.Count>0,ok);
                if(ok){redo.Push(model);model=undo.Pop();ops.Add("undo");changed++;}
            }
            else if(pick==1)
            {
                var ok=s.Redo();Assert.Equal(redo.Count>0,ok);
                if(ok){undo.Push(model);model=redo.Pop();ops.Add("redo");changed++;}
            }
            else if(pick==2)
            {
                var history=s.History.UndoCount;Assert.False(s.Execute(new SetValue("/n",J(-1))));Assert.Same(before,s.Current);
                Assert.Equal(rev,s.Revision);Assert.Equal(count,s.Journal.Count);Assert.Equal(history,s.History.UndoCount);refused++;
            }
            else
            {
                var n=random.Next(10000);var m=random.Next(10000);undo.Push(model);redo.Clear();
                if(pick is 3 or 4)
                {Assert.True(s.ExecuteBatch([new SetValue("/n",J(n)),new SetValue("/m",J(m))]));model=new(n,m);ops.Add("batch");}
                else if(pick%2==0){Assert.True(s.Execute(new SetValue("/n",J(n))));model=model with{N=n};ops.Add("do");}
                else{Assert.True(s.Execute(new SetValue("/m",J(m))));model=model with{M=m};ops.Add("do");}
                changed++;
            }
            ValueCheck(s,model);Assert.Equal(undo.Count,s.History.UndoCount);Assert.Equal(redo.Count>0,s.History.CanRedo);
            Assert.Equal(ops,s.Journal.Select(e=>e.Op));Assert.Equal(changed,s.Revision);
            Assert.Equal(count+(s.Revision==rev?0:1),s.Journal.Count);
        }
        var middle=s.TextToSave;Assert.Equal(middle,Replay(original,s).TextToSave);
        // Finish the current redo branch before naming its tip: pending redo is not the current document.
        var drained=0;while(s.Redo()){undo.Push(model);model=redo.Pop();drained++;}ValueCheck(s,model);var tip=s.TextToSave;
        var undid=0;while(s.Undo())undid++;Assert.Equal(opened,s.TextToSave);Assert.False(s.Dirty);Assert.Equal(0,s.History.UndoCount);
        var redid=0;while(s.Redo())redid++;Assert.Equal(undid,redid);Assert.Equal(tip,s.TextToSave);Assert.Equal(tip,Replay(original,s).TextToSave);
        Assert.Equal(disk,System.IO.File.ReadAllBytes(f.Path));
        Evidence("mix_"+seed,new{name="mix_"+seed,seed,steps=200,refused,changed,drained,undid,redid,historyCapacity=s.History.Capacity,journal=s.Journal.Count,openedHash=Hash(opened),tipHash=Hash(tip),exact=true,diskUnchanged=true});
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(256)]
    public void History_capacity_forgets_only_the_oldest_steps_and_replay_keeps_the_tip(int capacity)
    {
        var schemas=new JsonSchemas();schemas.Add(SchemaName,Schema);var original=new DataFormat().Read(Seed);
        var s=new DocumentSession<DataDocument>(new DataFormat(),new DataValidator(schemas,SchemaName),DataKit.Catalog(),original,capacity);
        for(var i=1;i<=capacity+7;i++){Assert.True(s.Execute(new SetValue("/n",J(i))));Assert.Equal(Math.Min(i,capacity),s.History.UndoCount);}
        var journal=s.Journal.Count;var count=0;while(s.Undo())count++;Assert.Equal(capacity,count);Assert.Equal(7,Current(s).N);
        Assert.Equal(journal+capacity,s.Journal.Count);var atEnd=s.Journal.Count;Assert.False(s.Undo());Assert.Equal(atEnd,s.Journal.Count);
        var redos=0;while(s.Redo())redos++;Assert.Equal(capacity,redos);Assert.Equal(capacity+7,Current(s).N);Assert.Equal(s.TextToSave,Replay(original,s).TextToSave);
        Evidence("capacity_"+capacity,new{name="capacity_"+capacity,capacity,edits=capacity+7,undo=count,oldestRetainedValue=7,redos,journal=s.Journal.Count,exact=true});
    }
    [Fact]
    public void New_edit_after_undo_drops_the_redo_branch_and_the_journal_replays_that_choice()
    {
        using var f=new Fixture();var s=f.Session;var original=s.Current;
        Assert.True(s.Execute(new SetValue("/n",J(1))));Assert.True(s.Execute(new SetValue("/n",J(2))));Assert.True(s.Undo());
        Assert.True(s.Execute(new SetValue("/n",J(9))));var count=s.Journal.Count;var rev=s.Revision;Assert.False(s.Redo());Assert.Equal(count,s.Journal.Count);Assert.Equal(rev,s.Revision);
        Assert.Equal(new[]{"do","do","undo","do"},s.Journal.Select(e=>e.Op));Assert.Equal(9,Current(s).N);Assert.Equal(s.TextToSave,Replay(original,s).TextToSave);
        Evidence("branch",new{name="branch",ops=s.Journal.Select(e=>e.Op),value=9,redo=false,exact=true});
    }
    [Theory]
    [InlineData("lf")] [InlineData("crlf")]
    public void Undo_past_save_redo_back_and_save_again_keep_dirty_and_bytes_exact(string ending)
    {
        using var f=new Fixture(ending=="lf"?"\n":"\r\n");var s=f.Session;var original=s.Current;var opened=System.IO.File.ReadAllBytes(f.Path);
        Assert.True(s.Execute(new SetValue("/n",J(1))));Assert.True(s.Dirty);var journal=s.Journal.Count;Assert.True(f.File.Save());Assert.False(s.Dirty);Assert.Equal(journal,s.Journal.Count);
        var saved=System.IO.File.ReadAllBytes(f.Path);Assert.Equal(Encoding.UTF8.GetBytes(s.TextToSave),saved);
        Assert.True(s.Undo());Assert.True(s.Dirty);Assert.Equal(opened,Encoding.UTF8.GetBytes(s.TextToSave));Assert.Equal(saved,System.IO.File.ReadAllBytes(f.Path));
        Assert.True(s.Redo());Assert.False(s.Dirty);Assert.Equal(saved,Encoding.UTF8.GetBytes(s.TextToSave));Assert.True(f.File.Save());Assert.Equal(saved,System.IO.File.ReadAllBytes(f.Path));
        Assert.True(s.Execute(new SetValue("/m",J(2))));var before=s.Current;var rev=s.Revision;var count=s.Journal.Count;var history=s.History.UndoCount;
        Assert.False(s.ExecuteBatch([new SetValue("/n",J(3)),new SetValue("/m",J(-1))]));Assert.Same(before,s.Current);Assert.Equal(rev,s.Revision);Assert.Equal(count,s.Journal.Count);Assert.Equal(history,s.History.UndoCount);
        Assert.True(s.Undo());Assert.False(s.Dirty);Assert.Equal(saved,System.IO.File.ReadAllBytes(f.Path));Assert.Equal(s.TextToSave,Replay(original,s).TextToSave);
        Evidence("save_"+ending,new{name="save_"+ending,ending,openedHash=Hash(Encoding.UTF8.GetString(opened)),savedHash=Hash(Encoding.UTF8.GetString(saved)),dirtyAfterUndo=true,dirtyAtSavedVersion=false,refusedBatchUnchanged=true,journal=s.Journal.Count,exact=true});
    }
    [Fact]
    public void Reload_with_history_sets_a_new_replay_base_and_clears_history_and_journal()
    {
        using var f=new Fixture();var s=f.Session;Assert.True(s.Execute(new SetValue("/n",J(1))));Assert.True(s.Execute(new SetValue("/n",J(2))));var rev=s.Revision;
        const string external="{\"n\":37,\"m\":5,\"blob\":\"seed\"}\n";System.IO.File.WriteAllText(f.Path,external);Assert.Equal(DiskState.ChangedOnDisk,s.CheckDisk());Assert.True(s.Revert());
        Assert.Equal(rev+1,s.Revision);Assert.Equal(0,s.History.UndoCount);Assert.Empty(s.Journal);Assert.False(s.Dirty);Assert.Equal(external,s.TextToSave);Assert.Equal(external,System.IO.File.ReadAllText(f.Path));
        var newBase=s.Current;Assert.True(s.Execute(new SetValue("/n",J(38))));Assert.Equal(s.TextToSave,Replay(newBase,s).TextToSave);
        Evidence("reload",new{name="reload",newBaseN=37,newN=38,journal=s.Journal.Count,revision=s.Revision,exact=true});
    }
    [Fact]
    public void Refused_reload_preserves_the_live_document_history_journal_and_revision()
    {
        using var f=new Fixture();var s=f.Session;Assert.True(s.Execute(new SetValue("/n",J(1))));var before=s.Current;var count=s.Journal.Count;var rev=s.Revision;var history=s.History.UndoCount;
        System.IO.File.WriteAllText(f.Path,"{");Assert.False(s.Revert());Assert.Same(before,s.Current);Assert.Equal(count,s.Journal.Count);Assert.Equal(rev,s.Revision);Assert.Equal(history,s.History.UndoCount);Assert.Equal("{",System.IO.File.ReadAllText(f.Path));
        Evidence("refused_reload",new{name="refused_reload",issues=s.LastIssues.Select(i=>i.Message),count,rev,history,exact=true});
    }
    [Fact]
    public void Closing_and_reopening_the_data_file_loads_disk_with_no_old_history()
    {
        using var f=new Fixture();var old=f.Session;Assert.True(old.Execute(new SetValue("/n",J(1))));Assert.True(f.File.Save());Assert.True(old.Execute(new SetValue("/n",J(2))));
        f.File.Close();Assert.Null(f.File.Session);Assert.True(f.File.Open());Assert.NotSame(old,f.Session);Assert.Equal(1,Current(f.Session).N);Assert.Empty(f.Session.Journal);Assert.Equal(0,f.Session.History.UndoCount);Assert.False(f.Session.Dirty);
        Evidence("reopen",new{name="reopen",diskN=1,discardedUnsavedN=2,journal=0,history=0,exact=true});
    }
    public sealed class LongTheoryAttribute:TheoryAttribute
    {public LongTheoryAttribute(){if(Environment.GetEnvironmentVariable("A204_LONG")!="1")Skip="Set A204_LONG=1 to run the 100,000-small / 1,000-one-MB edit memory and cost proof; no long-run claim otherwise.";}}
    private static SetValue NextCommand(string kind,int id,int bytes)=>kind=="small"?new("/n",J(id)):new("/blob",J(id.ToString("D10",CultureInfo.InvariantCulture)+new string('x',bytes-10)));
    private static void Warm(string kind,int bytes)
    {
        var schemas=new JsonSchemas();schemas.Add(SchemaName,Schema);var s=DataKit.Session(new DataFormat().Read(Seed),schemas,SchemaName);
        for(var i=1;i<=20;i++){Assert.True(s.Execute(NextCommand(kind,i,bytes)));Assert.True(s.Undo());Assert.True(s.Redo());}
    }
    private sealed record Cost(double Ms,long Bytes);
    private static Cost Time(Func<bool> action)
    {var bytes=GC.GetAllocatedBytesForCurrentThread();var t=Stopwatch.GetTimestamp();var ok=action();var elapsed=Stopwatch.GetElapsedTime(t).TotalMilliseconds;var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;Assert.True(ok);return new(elapsed,allocated);}
    [LongTheory]
    [InlineData("small",100000,0)] [InlineData("large",1000,1000000)]
    public void Measured_long_run_retains_exact_history_and_replays_every_recorded_step(string kind,int primaryEdits,int payloadBytes)
    {
        Warm(kind,payloadBytes);using var f=new Fixture();var s=f.Session;var original=s.Current;var disk=System.IO.File.ReadAllBytes(f.Path);
        var points=new List<object>();var id=0;var probes=0;var checkpoints=kind=="small"?new[]{0,1,16,256,1000,10000,100000}:new[]{0,1,16,128,256,512,1000};
        var runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;var available=GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        void Point(int primary)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();var managed=GC.GetTotalMemory(true);using var process=Process.GetCurrentProcess();process.Refresh();
            var memory=new{managedBytes=managed,privateBytes=process.PrivateMemorySize64,workingSetBytes=process.WorkingSet64,totalAvailableBytes=available};
            var edits=new List<Cost>();var undos=new List<Cost>();var redos=new List<Cost>();var count=s.Journal.Count;
            for(var sample=0;sample<5;sample++)
            {
                var previous=s.Current;var command=NextCommand(kind,++id,payloadBytes);edits.Add(Time(()=>s.Execute(command)));var current=s.Current;
                undos.Add(Time(s.Undo));Assert.Same(previous,s.Current);redos.Add(Time(s.Redo));Assert.Same(current,s.Current);probes++;
            }
            Assert.Equal(count+15,s.Journal.Count);Assert.True(s.History.UndoCount<=s.History.Capacity);
            points.Add(new{primary,committedBeforeProbe=primary+probes-5,journalBeforeProbe=count,historyBeforeProbe=Math.Min(primary+probes-5,s.History.Capacity),memory,samples=5,edit=edits,undo=undos,redo=redos});
            Evidence("long_"+kind,new{name="long_"+kind,phase="measuring",kind,primaryEdits,payloadBytes,primaryCompleted=primary,processId=Environment.ProcessId,runtime,clock=Stopwatch.Frequency,processors=Environment.ProcessorCount,checkpoints=points});
        }
        Point(0);
        for(var i=1;i<=primaryEdits;i++)
        {
            Assert.True(s.Execute(NextCommand(kind,++id,payloadBytes)));Assert.True(s.History.UndoCount<=s.History.Capacity);
            if(Array.IndexOf(checkpoints,i)>=0)Point(i);
        }
        var tip=s.TextToSave;Assert.Equal(primaryEdits+probes,id);Assert.Equal(primaryEdits+3*probes,s.Journal.Count);
        var undid=0;while(s.Undo())undid++;Assert.Equal(s.History.Capacity,undid);
        var oldest=kind=="small"?Current(s).N:int.Parse(s.Current.At("/blob")!.Value.GetString()![..10],CultureInfo.InvariantCulture);
        Assert.Equal(id-s.History.Capacity,oldest);var redid=0;while(s.Redo())redid++;Assert.Equal(undid,redid);Assert.Equal(tip,s.TextToSave);
        // Replay the live entries, avoiding a needless single >1 GB journal string in the large proof.
        var replay=Replay(original,s,roundTrip:false);Assert.Equal(tip,replay.TextToSave);Assert.Equal(s.Journal.Count,replay.Journal.Count);
        Assert.Equal(disk,System.IO.File.ReadAllBytes(f.Path));using var finalProcess=Process.GetCurrentProcess();finalProcess.Refresh();
        Evidence("long_"+kind,new{name="long_"+kind,phase="complete",kind,primaryEdits,payloadBytes,primaryCompleted=primaryEdits,committed=id,probeEdits=probes,undid,redid,oldestRetainedValue=oldest,historyCapacity=s.History.Capacity,journal=s.Journal.Count,replayJournal=replay.Journal.Count,exact=true,diskUnchanged=true,tipHash=Hash(tip),processId=Environment.ProcessId,runtime,clock=Stopwatch.Frequency,processors=Environment.ProcessorCount,finalManagedBytes=GC.GetTotalMemory(false),peakWorkingSetBytes=finalProcess.PeakWorkingSet64,checkpoints=points});
    }
}
