using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;

namespace Bed.Tests;

[CollectionDefinition("Save rollback acceptance", DisableParallelization = true)]
public sealed class SaveRollbackAcceptanceCollection { }

// INVENTED: neutral documents, dependency and timing scenarios. The basic self-authored fixture is
// reused from A101, extended for exact-byte snapshots and observed outside-file actors. No game data.
[Collection("Save rollback acceptance")]
public sealed class SaveRollbackAcceptanceTests(ITestOutputHelper output)
{
    private sealed class Box : IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"rollback-acceptance-"+Guid.NewGuid().ToString("N"));
        internal string Data => Path.Combine(Root,"data");
        internal readonly DataProject Project;
        internal readonly string[] Names;
        internal Action<string>? PolicyVisit;
        internal bool Injected;
        private FileStream? late;
        internal Box(params string[] names)
        {
            Names=names;Directory.CreateDirectory(Data);Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Path.Combine(Root,"schemas","neutral.schema.json"),"""
                {"type":"object","required":["settings"],"properties":{"settings":{"type":"object","required":["value","depends","pad"],"properties":{"value":{"type":"integer","minimum":0},"depends":{"type":"string"},"pad":{"type":"string"}},"additionalProperties":false}},"additionalProperties":false}
                """);
            foreach(var n in names)File.WriteAllText(Path.Combine(Data,n),Text(0));
            Project=DataProject.Open(Data,Path.Combine(Root,"schemas"),readOnlyFile:n=>{PolicyVisit?.Invoke(n);return null;});
            Project.SchemaFor=_=>"neutral.schema.json";
            foreach(var n in names)Assert.True(F(n).Open());
        }
        internal DataFile F(string n) => Project.Find(n) ?? throw new InvalidOperationException("Missing neutral fixture file "+n);
        internal DocumentSession<DataDocument> S(string n) => F(n).Session ?? throw new InvalidOperationException("Closed neutral fixture "+n);
        internal static string Text(int value,string depends="",string pad="") =>
            JsonSerializer.Serialize(new{settings=new{value,depends,pad}})+"\n";
        internal int Current(string n) => S(n).Current.At("/settings/value")!.Value.GetInt32();
        internal byte[] Bytes(string n) => File.ReadAllBytes(F(n).Path);
        internal int Disk(string n)
        {
            using var j=JsonDocument.Parse(File.ReadAllText(F(n).Path));
            return j.RootElement.GetProperty("settings").GetProperty("value").GetInt32();
        }
        internal void Edit(string n,int v) => Assert.True(S(n).Execute(new SetValue("/settings/value",JsonSerializer.SerializeToElement(v))));
        internal void Pad(string n,string s) => Assert.True(S(n).Execute(new SetValue("/settings/pad",JsonSerializer.SerializeToElement(s))));
        internal void Replace(string n,byte[] bytes)
        {
            F(n).Close();File.WriteAllBytes(F(n).Path,bytes);Assert.True(F(n).Open());
        }
        internal void Depend(string n,string provider) => Replace(n,Encoding.UTF8.GetBytes(Text(0,provider)));
        internal string? Relationship(DataFile file)
        {
            var values=new Dictionary<string,(int Value,string Depends)>(StringComparer.Ordinal);
            foreach(var n in Names)
            {
                using var j=JsonDocument.Parse(File.ReadAllText(F(n).Path));var s=j.RootElement.GetProperty("settings");
                values[n]=(s.GetProperty("value").GetInt32(),s.GetProperty("depends").GetString()!);
            }
            foreach(var f in Project.BeingSaved(file))
            {
                var s=S(f.Name).Current.Root.GetProperty("settings");
                values[f.Name]=(s.GetProperty("value").GetInt32(),s.GetProperty("depends").GetString()!);
            }
            foreach(var pair in values)
                if(pair.Value.Depends.Length>0 && (!values.TryGetValue(pair.Value.Depends,out var p)||p.Value!=pair.Value.Value))
                    return pair.Key+" needs its declared provider at the same value.";
            return null;
        }
        internal void Arm(Action eventAfterFinalEarlierChecks,Func<DataFile,string?>? check=null)
        {
            var judged=false;var visits=0;var last=Names.Order(StringComparer.Ordinal).Last();
            Project.SaveCheck=f=>
            {
                var result=(check ?? Relationship)(f);
                if(f.Name==last)judged=true;
                return result;
            };
            PolicyVisit=n=>
            {
                if(n==last&&judged&&++visits==2){Injected=true;eventAfterFinalEarlierChecks();}
            };
        }
        internal void LateFailure(string provider="b.json",Func<DataFile,string?>? check=null) =>
            Arm(()=>late=new FileStream(F(provider).Path,FileMode.Open,FileAccess.Read,FileShare.Read),check);
        internal void Release()
        {
            late?.Dispose();late=null;PolicyVisit=null;
        }
        internal void Prepare(bool large=false)
        {
            Depend("a.json","b.json");
            foreach(var n in Names)Edit(n,1);
            if(large)Pad("b.json",new string('q',8*1024*1024));
            LateFailure();
        }
        public void Dispose()
        {
            Release();
            var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var absolute=Path.GetFullPath(Root);
            if(!absolute.StartsWith(temp,StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(absolute).StartsWith("rollback-acceptance-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe fixture cleanup target.");
            if(Directory.Exists(absolute))Directory.Delete(absolute,recursive:true);
        }
    }

    private enum OutsideMode { LockWritten, EditWritten, EditAndLockWritten }
    private sealed class OutsideActor : IDisposable
    {
        private readonly Box box;
        private readonly OutsideMode mode;
        private readonly object sync=new();
        private readonly FileSystemWatcher watcher;
        private FileStream? held;
        private bool stopped;
        internal long AppliedAt {get;private set;}
        internal string? Fault {get;private set;}
        internal byte[] External => Encoding.UTF8.GetBytes(Box.Text(0,"b.json","outside"));
        internal OutsideActor(Box b,OutsideMode m)
        {
            box=b;mode=m;
            watcher=new FileSystemWatcher(b.Data,"a.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
            watcher.Created+=(_,_)=>Apply();watcher.Changed+=(_,_)=>Apply();watcher.Renamed+=(_,_)=>Apply();
            watcher.EnableRaisingEvents=true;
        }
        private void Apply()
        {
            lock(sync)
            {
                if(stopped||AppliedAt!=0)return;
                try
                {
                    if(box.Disk("a.json")!=1)return;
                    if(mode!=OutsideMode.LockWritten)File.WriteAllBytes(box.F("a.json").Path,External);
                    if(mode!=OutsideMode.EditWritten)held=new FileStream(box.F("a.json").Path,FileMode.Open,FileAccess.Read,FileShare.Read);
                    AppliedAt=Stopwatch.GetTimestamp();
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
                { Fault=e.GetType().Name; }
            }
        }
        internal void ReleaseReader() { lock(sync){held?.Dispose();held=null;} }
        internal (long At,string? Error) Snapshot()
        {
            lock(sync)return(AppliedAt,Fault);
        }
        public void Dispose()
        {
            lock(sync){stopped=true;watcher.EnableRaisingEvents=false;held?.Dispose();held=null;}
            watcher.Dispose();
        }
    }
    private void Observed(OutsideMode mode,Action<Box,OutsideActor,IReadOnlyList<DataFile>,JsonObject?> assertions,bool agent=false)
    {
        // Bounded reachability check, never a skip or a pass without injection. The large neutral second draft
        // gives the OS observer a scheduling window; timestamps prove the actor finished before SaveAll returned.
        for(var attempt=1;attempt<=25;attempt++)   // widened from 4: under a full run this race can miss, and missing still fails
        {
            using var b=new Box("a.json","b.json","z.json");b.Prepare(large:true);
            using var actor=new OutsideActor(b,mode);
            JsonObject? answer=null;IReadOnlyList<DataFile> failed;
            if(agent)
            {
                answer=new AgentSession(b.Project).Save();
                failed=answer["refused"]!.AsArray().Select(n=>b.F(n!["file"]!.GetValue<string>())).ToArray();
            }
            else failed=b.Project.SaveAll();
            var returned=Stopwatch.GetTimestamp();var snapshot=actor.Snapshot();
            output.WriteLine(JsonSerializer.Serialize(new{mode=mode.ToString(),attempt,actorApplied=snapshot.At>0,
                actorBeforeReturn=snapshot.At>0&&snapshot.At<=returned,error=snapshot.Error,
                diskA=b.Disk("a.json"),dirtyA=b.F("a.json").Dirty,checkDiskA=b.S("a.json").CheckDisk().ToString(),
                refused=failed.Select(f=>f.Name).ToArray()}));
            if(snapshot.At==0 || snapshot.At>returned)continue;
            Assert.True(b.Injected,"The late provider fault must also have been injected.");
            assertions(b,actor,failed,answer);
            return;
        }
        Assert.Fail("The bounded OS actor did not reach the between-write-and-put-back window; no safety result is claimed without injection.");
    }
    private static string Journal(Box b,string n) => JournalFile.Write(b.S(n).Journal);
    private static void NoTemps(Box b) => Assert.Empty(Directory.EnumerateFiles(b.Root,".*.tmp",SearchOption.AllDirectories));
    private static string[] FailedNames(IReadOnlyList<DataFile> failed) => failed.Select(f=>f.Name).Order(StringComparer.Ordinal).ToArray();

    // R01. Source: "Dirty, undo, journal and CheckDisk after a put-back." Successful-group control.
    [Fact]
    public void R01_A_successful_group_records_the_actual_written_baselines()
    {
        using var b=new Box("a.json","b.json");b.Edit("a.json",1);b.Edit("b.json",1);
        Assert.Empty(b.Project.SaveAll());
        foreach(var n in b.Names){Assert.Equal(1,b.Disk(n));Assert.False(b.F(n).Dirty);Assert.Equal(DiskState.Unchanged,b.S(n).CheckDisk());}
        NoTemps(b);
    }

    // R02. Source: "The order of files." A first failed provider cannot publish its dependent.
    [Fact]
    public void R02_A_first_failed_provider_leaves_its_later_consumer_as_a_draft()
    {
        using var b=new Box("a.json","b.json","z.json");b.Depend("b.json","a.json");
        foreach(var n in b.Names)b.Edit(n,1);b.LateFailure("a.json");
        var failed=b.Project.SaveAll();Assert.True(b.Injected);
        Assert.Contains(b.F("a.json"),failed);Assert.Contains(b.F("b.json"),failed);
        Assert.Equal(0,b.Disk("a.json"));Assert.Equal(0,b.Disk("b.json"));Assert.True(b.F("b.json").Dirty);
    }

    // R03. Source: "Bytes kept exactly on put-back." A later failed provider restores an earlier consumer.
    [Fact]
    public void R03_A_later_failed_provider_restores_the_earlier_consumers_exact_bytes()
    {
        using var b=new Box("a.json","b.json","z.json");b.Depend("a.json","b.json");var before=b.Bytes("a.json");
        foreach(var n in b.Names)b.Edit(n,1);b.LateFailure();
        var failed=b.Project.SaveAll();Assert.True(b.Injected);Assert.Contains(b.F("a.json"),failed);
        Assert.Equal(before,b.Bytes("a.json"));Assert.True(b.F("a.json").Dirty);Assert.Equal(1,b.Current("a.json"));
    }

    // R04. Source: "a byte-order mark, line endings, a file with text the format would rewrite".
    [Theory]
    [InlineData("bom")][InlineData("crlf")][InlineData("indent")][InlineData("trailing")]
    public void R04_Put_back_keeps_noncanonical_original_bytes(string variant)
    {
        using var b=new Box("a.json","b.json","z.json");
        var text=Box.Text(0,"b.json");
        byte[] bytes=variant switch
        {
            "bom"=>new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(text)).ToArray(),
            "crlf"=>Encoding.UTF8.GetBytes(text.Replace("\n","\r\n",StringComparison.Ordinal)),
            "indent"=>Encoding.UTF8.GetBytes("{\r\n  \"settings\": { \"pad\": \"\", \"depends\": \"b.json\", \"value\": 0 }\r\n}\r\n"),
            _=>Encoding.UTF8.GetBytes(text+"\t \r\n")
        };
        b.Replace("a.json",bytes);foreach(var n in b.Names)b.Edit(n,1);b.LateFailure();
        b.Project.SaveAll();Assert.True(b.Injected);Assert.Equal(bytes,b.Bytes("a.json"));Assert.True(b.F("a.json").Dirty);
    }

    // R05. Source: "Dirty ... and CheckDisk after a put-back."
    [Fact]
    public void R05_A_restored_draft_is_dirty_but_its_disk_baseline_is_unchanged()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();b.Project.SaveAll();
        Assert.True(b.Injected);Assert.True(b.F("a.json").Dirty);Assert.Equal(1,b.Current("a.json"));
        Assert.Equal(0,b.Disk("a.json"));Assert.Equal(DiskState.Unchanged,b.S("a.json").CheckDisk());
    }

    // R06. Source: "undo, journal ... after a put-back."
    [Fact]
    public void R06_Rollback_does_not_add_edit_history_or_journal_entries()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();var journal=Journal(b,"a.json");var undo=b.S("a.json").History.UndoCount;
        b.Project.SaveAll();Assert.True(b.Injected);Assert.Equal(journal,Journal(b,"a.json"));Assert.Equal(undo,b.S("a.json").History.UndoCount);
    }

    // R07. Source: "Dirty, undo, journal ... after a put-back."
    [Fact]
    public void R07_Undo_and_redo_after_restoration_track_the_original_baseline()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();b.Project.SaveAll();
        Assert.True(b.S("a.json").Undo());Assert.False(b.F("a.json").Dirty);Assert.Equal(0,b.Current("a.json"));
        Assert.True(b.S("a.json").Redo());Assert.True(b.F("a.json").Dirty);Assert.Equal(1,b.Current("a.json"));Assert.Equal(0,b.Disk("a.json"));
    }

    // R08. Source: "the rest are judged again without it" (SaveAll public summary).
    [Fact]
    public void R08_An_independent_remaining_draft_can_save_after_safe_restoration()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();
        var failed=b.Project.SaveAll();Assert.Equal(new[]{"a.json","b.json"},FailedNames(failed));
        Assert.Equal(1,b.Disk("z.json"));Assert.False(b.F("z.json").Dirty);Assert.Equal(2,b.Project.Dirty.Count);
    }

    // R09. Source: "The workspace's ... words."
    [Fact]
    public void R09_Workspace_reports_a_partial_refusal_and_keeps_restored_work()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();var workspace=new DataWorkspace(b.Project);
        Assert.False(workspace.SaveAll());Assert.Contains("b.json",workspace.Said);Assert.Contains("a.json",workspace.Said);
        Assert.True(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);Assert.False(b.F("z.json").Dirty);
    }

    // R10. Source: "an agent's words."
    [Fact]
    public void R10_Agent_saved_and_refused_lists_match_the_post_rollback_disks()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();var answer=new AgentSession(b.Project).Save();
        Assert.Equal(new[]{"z.json"},answer["saved"]!.AsArray().Select(n=>n!.GetValue<string>()).ToArray());
        Assert.Equal(new[]{"a.json","b.json"},answer["refused"]!.AsArray().Select(n=>n!["file"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(0,b.Disk("a.json"));Assert.Equal(1,b.Disk("z.json"));Assert.True(b.F("a.json").Dirty);
    }

    // R11. Source: "Temporary files left behind." Named-file error control.
    [Fact]
    public void R11_A_named_refused_write_keeps_its_original_file_and_no_temporary_files()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var before=b.Bytes("a.json");
        using(var held=new FileStream(b.F("a.json").Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            Assert.False(b.F("a.json").Save());
        Assert.Equal(before,b.Bytes("a.json"));Assert.True(b.F("a.json").Dirty);NoTemps(b);
    }

    // R12. Source: "Can the person go on?"
    [Fact]
    public void R12_Closing_the_faulting_handle_allows_the_retained_group_to_save()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();Assert.NotEmpty(b.Project.SaveAll());b.Release();
        Assert.Empty(b.Project.SaveAll());
        foreach(var n in b.Names){Assert.Equal(1,b.Disk(n));Assert.False(b.F(n).Dirty);Assert.Equal(DiskState.Unchanged,b.S(n).CheckDisk());}
    }

    // R13. Source: "A file changed outside ... is anyone's work overwritten?" After-rollback control.
    [Fact]
    public void R13_An_outside_edit_after_put_back_is_not_overwritten_by_retry()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();b.Project.SaveAll();b.Release();
        var outside=Box.Text(0,"b.json","outside");File.WriteAllText(b.F("a.json").Path,outside);
        Assert.Equal(DiskState.ChangedOnDisk,b.S("a.json").CheckDisk());Assert.False(b.F("a.json").Save());
        Assert.Equal(outside,File.ReadAllText(b.F("a.json").Path));Assert.True(b.F("a.json").Dirty);
    }

    // R14. Source: "Temporary files left behind."
    [Fact]
    public void R14_A_failed_group_and_successful_put_back_leave_no_atomic_temps()
    {
        using var b=new Box("a.json","b.json","z.json");b.Prepare();b.Project.SaveAll();Assert.True(b.Injected);NoTemps(b);
    }

    // R15. Source: "The order of files."
    [Theory]
    [InlineData("a.json","b.json")][InlineData("b.json","a.json")]
    public void R15_Refused_dependency_is_coherent_in_either_provider_order(string provider,string consumer)
    {
        using var b=new Box("a.json","b.json","z.json");b.Depend(consumer,provider);
        foreach(var n in b.Names)b.Edit(n,1);b.LateFailure(provider);
        var failed=b.Project.SaveAll();Assert.True(b.Injected);Assert.Contains(b.F(provider),failed);Assert.Contains(b.F(consumer),failed);
        Assert.Equal(0,b.Disk(provider));Assert.Equal(0,b.Disk(consumer));Assert.True(b.F(consumer).Dirty);
    }

    // R16. Source: "A second failure on the judging again."
    [Fact]
    public void R16_A_second_judgment_failure_is_reported_without_losing_the_restored_draft()
    {
        using var b=new Box("a.json","b.json","z.json");b.Depend("a.json","b.json");foreach(var n in b.Names)b.Edit(n,1);
        var visits=0;b.LateFailure(check:f=>f.Name=="a.json"&&++visits>1?throw new InvalidOperationException("Second judgment failed."):b.Relationship(f));
        var failed=b.Project.SaveAll();Assert.Contains(b.F("a.json"),failed);Assert.Contains("Second judgment failed",b.F("a.json").Problems.Single().Message);
        Assert.True(b.F("a.json").Dirty);Assert.Equal(0,b.Disk("a.json"));
    }

    // R17. Source: "Dirty ... after a put-back." Clean/closed siblings are not implicit writes.
    [Fact]
    public void R17_A_clean_closed_sibling_is_untouched_by_the_failed_group()
    {
        using var b=new Box("a.json","b.json","c.json","z.json");b.Depend("a.json","b.json");
        var before=b.Bytes("c.json");b.F("c.json").Close();
        foreach(var n in new[]{"a.json","b.json","z.json"})b.Edit(n,1);
        b.LateFailure();b.Project.SaveAll();Assert.True(b.Injected);
        Assert.Equal(before,b.Bytes("c.json"));Assert.Null(b.F("c.json").Session);
    }

    // R18. Source: TextToSave "The text Save would write now."
    [Fact]
    public void R18_Reading_TextToSave_does_not_save_or_add_history()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var before=b.Bytes("a.json");var journal=Journal(b,"a.json");
        var text=b.S("a.json").TextToSave;Assert.Equal(text,b.S("a.json").TextToSave);
        using var json=JsonDocument.Parse(text);Assert.Equal(1,json.RootElement.GetProperty("settings").GetProperty("value").GetInt32());
        Assert.Equal(before,b.Bytes("a.json"));Assert.Equal(journal,Journal(b,"a.json"));Assert.True(b.F("a.json").Dirty);
    }

    // R19. Source: MarkSaved "Records that the session's file now holds text, written by the caller."
    [Fact]
    public void R19_MarkSaved_records_an_actual_caller_write_without_erasing_history()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var s=b.S("a.json");var journal=Journal(b,"a.json");
        var text=s.TextToSave;AtomicFile.Write(b.F("a.json").Path,text);s.MarkSaved(text);
        Assert.False(s.Dirty);Assert.Equal(DiskState.Unchanged,s.CheckDisk());Assert.Equal(1,s.History.UndoCount);
        Assert.Equal(journal,Journal(b,"a.json"));Assert.Equal(text,File.ReadAllText(b.F("a.json").Path));
    }

    // R20. Source: MarkSaved supplied text, and "Dirty ... after a put-back." A newer draft stays dirty.
    [Fact]
    public void R20_Recording_an_older_written_snapshot_keeps_a_newer_draft_dirty()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var written=b.S("a.json").TextToSave;b.Edit("a.json",2);
        AtomicFile.Write(b.F("a.json").Path,written);b.S("a.json").MarkSaved(written);
        Assert.True(b.F("a.json").Dirty);Assert.Equal(2,b.Current("a.json"));Assert.Equal(1,b.Disk("a.json"));
        Assert.Equal(DiskState.Unchanged,b.S("a.json").CheckDisk());Assert.Equal(2,b.S("a.json").History.UndoCount);
    }

    // R21. Source: "undo, journal ... after a put-back." Caller-written success keeps undo/redo meaningful.
    [Fact]
    public void R21_Undo_after_a_caller_recorded_success_does_not_rewrite_disk()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var s=b.S("a.json");var text=s.TextToSave;
        AtomicFile.Write(b.F("a.json").Path,text);s.MarkSaved(text);
        Assert.True(s.Undo());Assert.True(s.Dirty);Assert.Equal(1,b.Disk("a.json"));
        Assert.True(s.Redo());Assert.False(s.Dirty);Assert.Equal(3,s.Journal.Count);Assert.Equal(1,b.Disk("a.json"));
    }

    // R22. Source: "between the last look and the write ... is anyone's work overwritten?"
    [Fact]
    public void R22_An_outside_edit_after_the_earlier_final_check_must_not_be_overwritten()
    {
        using var b=new Box("a.json","z.json");foreach(var n in b.Names)b.Edit(n,1);
        var external=Box.Text(7);b.Arm(()=>File.WriteAllText(b.F("a.json").Path,external));
        var failed=b.Project.SaveAll();
        output.WriteLine(JsonSerializer.Serialize(new{b.Injected,actual=b.Disk("a.json"),dirty=b.F("a.json").Dirty,failed=FailedNames(failed)}));
        Assert.True(b.Injected);Assert.Equal(external,File.ReadAllText(b.F("a.json").Path));
        Assert.True(b.F("a.json").Dirty,"The editor draft was not the outside version that is now on disk.");
    }

    // R23. Source: "A put-back that itself fails: is what's reported true, and does the session then agree with the disk?"
    [Fact]
    public void R23_A_failed_put_back_reports_the_written_member_and_its_actual_state()
    {
        Observed(OutsideMode.LockWritten,(b,_,failed,_)=>
        {
            Assert.Contains(b.F("b.json"),failed);Assert.Equal(1,b.Disk("a.json"));Assert.False(b.F("a.json").Dirty);
            Assert.Equal(DiskState.Unchanged,b.S("a.json").CheckDisk());Assert.True(b.F("b.json").Dirty);
            var why=b.F("b.json").Problems.Single().Message;
            Assert.Contains("a.json",why);Assert.Contains("couldn't be put back",why);
        });
    }

    // R24. Source: "Can the person go on?"
    [Fact]
    public void R24_Retry_after_both_handles_close_can_complete_a_reported_partial_put_back()
    {
        Observed(OutsideMode.LockWritten,(b,actor,_,_)=>
        {
            actor.ReleaseReader();b.Release();Assert.Empty(b.Project.SaveAll());
            foreach(var n in b.Names){Assert.Equal(1,b.Disk(n));Assert.False(b.F(n).Dirty);Assert.Equal(DiskState.Unchanged,b.S(n).CheckDisk());}
        });
    }

    // R25. Source: "between the write and the put-back ... is anyone's work overwritten?"
    [Fact]
    public void R25_Put_back_must_preserve_an_outside_version_written_after_our_write()
    {
        Observed(OutsideMode.EditWritten,(b,actor,_,_)=>
        {
            Assert.Equal(actor.External,b.Bytes("a.json"));
            Assert.True(b.F("a.json").Dirty);Assert.Equal(DiskState.ChangedOnDisk,b.S("a.json").CheckDisk());
        });
    }

    // R26. Source: "does the session then agree with the disk?" when a put-back itself fails.
    [Fact]
    public void R26_A_failed_restore_cannot_mark_the_attempted_text_saved_over_an_outside_version()
    {
        Observed(OutsideMode.EditAndLockWritten,(b,actor,_,_)=>
        {
            Assert.Equal(actor.External,b.Bytes("a.json"));Assert.Equal(DiskState.ChangedOnDisk,b.S("a.json").CheckDisk());
            Assert.True(b.F("a.json").Dirty,"The attempted editor text is not the version on disk; it must remain unsaved work.");
        });
    }

    // R27. Source: "The workspace's and an agent's words."
    [Fact]
    public void R27_An_agent_must_not_list_an_attempted_version_as_saved_when_disk_holds_an_outside_version()
    {
        Observed(OutsideMode.EditAndLockWritten,(b,actor,_,answer)=>
        {
            Assert.Equal(actor.External,b.Bytes("a.json"));
            Assert.NotNull(answer);
            Assert.DoesNotContain("a.json",answer["saved"]!.AsArray().Select(n=>n!.GetValue<string>()));
            Assert.True(b.F("a.json").Dirty);
        },agent:true);
    }

    // R28. Source: "Temporary files left behind." Both initial write and put-back fail.
    [Fact]
    public void R28_A_failed_write_and_failed_put_back_leave_no_atomic_temporary_files()
    {
        Observed(OutsideMode.LockWritten,(b,_,failed,_)=>{Assert.NotEmpty(failed);NoTemps(b);});
    }

    private sealed record Counter(int Value);
    private sealed class CounterFormat : IDocumentFormat<Counter>
    {
        public string Kind => "acceptance.counter";
        public int Version => 2;
        public Counter Read(string text)
        {
            using var json=JsonDocument.Parse(text);var r=json.RootElement;
            if(r.GetProperty("kind").GetString()!=Kind||r.GetProperty("version").GetInt32()!=Version)
                throw new DocumentFormatException("$","Expected the neutral current counter format.");
            return new Counter(r.GetProperty("value").GetInt32());
        }
        public string Write(Counter doc) => JsonSerializer.Serialize(new{kind=Kind,version=Version,value=doc.Value})+"\n";
    }
    private sealed class CounterValidator : IValidator<Counter>
    {
        public IReadOnlyList<Issue> Validate(Counter doc) => [];
    }
    private static MigratingFormat<Counter> CounterMigration() =>
        new(new CounterFormat(),new Migrations("acceptance.counter",2).Step(1,"Rename the neutral value field.",o=>
        {o["value"]=o["oldValue"]!.DeepClone();o.Remove("oldValue");return o;}));
    private static byte[] LegacyCounter() => new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(
        "{ \"kind\": \"acceptance.counter\", \"version\": 1, \"oldValue\": 3 }\r\n")).ToArray();

    // R29. Source: "an upgraded file." The caller controls when MarkSaved records a group success.
    [Fact]
    public void R29_An_upgraded_session_put_back_by_its_caller_keeps_legacy_bytes_and_unsaved_upgrade()
    {
        using var b=new Box("a.json");var path=Path.Combine(b.Root,"counter.json");var legacy=LegacyCounter();
        File.WriteAllBytes(path,legacy);var s=DocumentSession<Counter>.Open(path,CounterMigration(),new CounterValidator(),new CommandCatalog<Counter>());
        Assert.True(s.Dirty);Assert.Equal(3,s.Current.Value);
        b.Edit("a.json",1);
        var files=new[]{new GroupFile(path,s.ExpectedOnDisk,s.TextToSave),new GroupFile(b.F("a.json").Path,b.S("a.json").ExpectedOnDisk,b.S("a.json").TextToSave)};
        var result=FileGroup.Write(files,(target,bytes)=>
        {if(target==b.F("a.json").Path)throw new IOException("Second member refused.");AtomicFile.Write(target,bytes);});
        Assert.Equal(1,result.Failed);Assert.Equal(GroupFileState.AsItWas,result.States[0]); // no MarkSaved after rollback
        Assert.Equal(legacy,File.ReadAllBytes(path));Assert.True(s.Dirty);Assert.Equal(DiskState.Unchanged,s.CheckDisk());
        Assert.Empty(s.Journal);Assert.Equal(0,s.History.UndoCount);NoTemps(b);
    }

    // R30. Source: "an upgraded file" plus MarkSaved/TextToSave listed interfaces.
    [Fact]
    public void R30_A_caller_recorded_upgraded_write_reopens_at_the_new_version_without_new_commands()
    {
        using var b=new Box("a.json");var path=Path.Combine(b.Root,"counter.json");File.WriteAllBytes(path,LegacyCounter());
        var s=DocumentSession<Counter>.Open(path,CounterMigration(),new CounterValidator(),new CommandCatalog<Counter>());
        var text=s.TextToSave;b.Edit("a.json",1);var other=b.S("a.json").TextToSave;
        var result=FileGroup.Write([new GroupFile(path,s.ExpectedOnDisk,text),new GroupFile(b.F("a.json").Path,b.S("a.json").ExpectedOnDisk,other)]);
        Assert.Null(result.Failed);Assert.All(result.States,state=>Assert.Equal(GroupFileState.Written,state));
        s.MarkSaved(text);b.S("a.json").MarkSaved(other);
        Assert.False(s.Dirty);Assert.Equal(DiskState.Unchanged,s.CheckDisk());Assert.Empty(s.Journal);
        var again=DocumentSession<Counter>.Open(path,CounterMigration(),new CounterValidator(),new CommandCatalog<Counter>());
        Assert.Equal(3,again.Current.Value);Assert.False(again.Dirty);
        using var json=JsonDocument.Parse(File.ReadAllText(path));Assert.Equal(2,json.RootElement.GetProperty("version").GetInt32());NoTemps(b);
    }
    private static GroupFile[] Group(Box b,params string[] names) =>
        names.Select(n=>new GroupFile(b.F(n).Path,File.ReadAllText(b.F(n).Path),Box.Text(1))).ToArray();

    // R31. Source: "between the last look and the write ... is anyone's work overwritten?" Expected text is a guard.
    [Fact]
    public void R31_FileGroup_refuses_a_stale_first_expected_version_without_calling_the_writer()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var outside=Box.Text(7);
        File.WriteAllText(b.F("a.json").Path,outside);var calls=0;
        var result=FileGroup.Write(files,(_,_)=>calls++);
        Assert.Equal(0,result.Failed);Assert.Equal(0,calls);Assert.Contains("a.json",result.Why);Assert.Contains("outside",result.Why);
        Assert.Equal(outside,File.ReadAllText(b.F("a.json").Path));Assert.Equal(0,b.Disk("b.json"));
    }

    // R32. Source: "between the last look and the write" and exact put-back bytes.
    [Fact]
    public void R32_A_later_stale_version_restores_earlier_writes_without_touching_the_outside_file()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var old=b.Bytes("a.json");var outside=Box.Text(7);
        File.WriteAllText(b.F("b.json").Path,outside);
        var result=FileGroup.Write(files);
        Assert.Equal(1,result.Failed);Assert.Equal(old,b.Bytes("a.json"));Assert.Equal(outside,File.ReadAllText(b.F("b.json").Path));
        Assert.Equal(GroupFileState.AsItWas,result.States[0]);NoTemps(b);
    }

    // R33. Source: "Bytes kept exactly on put-back: a byte-order mark, line endings".
    [Theory]
    [InlineData("utf8bom")][InlineData("utf16")][InlineData("crlf")]
    public void R33_The_injected_writer_seam_restores_the_original_encoding_bytes(string kind)
    {
        using var b=new Box("a.json","b.json");var text=Box.Text(0);
        var raw=kind=="utf16" ? Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()
            :kind=="utf8bom" ? new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(text)).ToArray()
            :Encoding.UTF8.GetBytes(text.Replace("\n","\r\n",StringComparison.Ordinal));
        b.Replace("a.json",raw);var files=Group(b,b.Names);
        var result=FileGroup.Write(files,(path,bytes)=>
        {if(path==b.F("b.json").Path)throw new IOException("Injected second write failure.");AtomicFile.Write(path,bytes);});
        Assert.Equal(1,result.Failed);Assert.Equal(raw,b.Bytes("a.json"));Assert.Equal(GroupFileState.AsItWas,result.States[0]);NoTemps(b);
    }

    // R34. Source: "A put-back that itself fails: is what's reported true".
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void R34_A_refused_restore_reports_Written_only_when_the_new_bytes_really_remain(bool access)
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var aCalls=0;
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            if(path==b.F("b.json").Path)throw new IOException("Second file refused.");
            if(++aCalls==2)
            {
                if(access)throw new UnauthorizedAccessException("Restore denied.");
                throw new IOException("Restore refused.");
            }
            AtomicFile.Write(path,bytes);
        });
        Assert.Equal(1,result.Failed);Assert.Equal(GroupFileState.Written,result.States[0]);Assert.Equal(1,b.Disk("a.json"));NoTemps(b);
    }

    // R35. Source: "does the session then agree with the disk?" Restore failure coincides with an outside edit.
    [Fact]
    public void R35_A_restore_error_after_an_outside_change_must_not_report_our_attempted_bytes_as_written()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var aCalls=0;var outside=Box.Text(7);
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            if(path==b.F("b.json").Path)throw new IOException("Second file refused.");
            if(++aCalls==2){File.WriteAllText(path,outside);throw new IOException("Outside edit during restore refusal.");}
            AtomicFile.Write(path,bytes);
        });
        Assert.Equal(outside,File.ReadAllText(b.F("a.json").Path));
        Assert.Equal(GroupFileState.Unknown,result.States[0]);
    }

    // R36. Source: "between the write and the put-back ... is anyone's work overwritten?" Outside deletion is intentional too.
    [Fact]
    public void R36_An_outside_deletion_before_restore_is_left_missing_and_reported_unknown()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            if(path==b.F("b.json").Path){File.Delete(b.F("a.json").Path);throw new IOException("Second file refused.");}
            AtomicFile.Write(path,bytes);
        });
        Assert.Equal(1,result.Failed);Assert.False(File.Exists(b.F("a.json").Path));
        Assert.Equal(GroupFileState.Unknown,result.States[0]);NoTemps(b);
    }

    // R37. Source: "a put-back that itself fails ... reported true". Unreadable is not assumed saved.
    [Fact]
    public void R37_An_unreadable_written_file_is_reported_unknown_and_left_alone()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);FileStream? held=null;GroupResult result;
        try
        {
            result=FileGroup.Write(files,(path,bytes)=>
            {
                if(path==b.F("b.json").Path)
                {held=new FileStream(b.F("a.json").Path,FileMode.Open,FileAccess.Read,FileShare.None);throw new IOException("Second file refused.");}
                AtomicFile.Write(path,bytes);
            });
            Assert.Equal(GroupFileState.Unknown,result.States[0]);Assert.Equal(1,result.Failed);
        }
        finally {held?.Dispose();}
        Assert.Equal(1,b.Disk("a.json"));NoTemps(b);
    }

    // R38. Source: "is what's reported true" when a writer can fail. A failure may follow the replacement.
    [Fact]
    public void R38_A_writer_that_throws_after_replacement_cannot_be_reported_as_unchanged()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var before=b.Bytes("b.json");
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            AtomicFile.Write(path,bytes);
            if(path==b.F("b.json").Path)throw new IOException("Failure after replacement.");
        });
        Assert.Equal(1,result.Failed);
        if(result.States[1]==GroupFileState.AsItWas)Assert.Equal(before,b.Bytes("b.json"));
        if(result.States[1]==GroupFileState.Written)Assert.Equal(Encoding.UTF8.GetBytes(files[1].Text),b.Bytes("b.json"));
        // Unknown is a conservative result: it must not claim the old baseline or the new version without evidence.
        NoTemps(b);
    }

    // R39. Source: "A file changed outside ... is anyone's work overwritten?" Group states must describe what remains.
    [Fact]
    public void R39_An_outside_version_before_group_return_cannot_be_reported_as_our_written_text()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var outside=Box.Text(7);
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            AtomicFile.Write(path,bytes);
            if(path==b.F("b.json").Path)File.WriteAllText(b.F("a.json").Path,outside);
        });
        Assert.Equal(outside,File.ReadAllText(b.F("a.json").Path));
        Assert.NotEqual(GroupFileState.Written,result.States[0]);
    }

    // R40. Source: "A put-back that itself fails" and truthful error reporting; initial failure control.
    [Fact]
    public void R40_An_initial_access_refusal_names_the_file_and_never_calls_the_later_writer()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var calls=0;
        var result=FileGroup.Write(files,(_,_)=>{calls++;throw new UnauthorizedAccessException("Access refused.");});
        Assert.Equal(0,result.Failed);Assert.Equal(1,calls);Assert.Contains("a.json",result.Why);Assert.Contains("Access refused",result.Why);
        Assert.Equal(0,b.Disk("a.json"));Assert.Equal(0,b.Disk("b.json"));NoTemps(b);
    }

    // R41. Source: "A put-back that itself fails" and "The order of files."
    [Fact]
    public void R41_More_than_one_restore_refusal_is_classified_per_file_without_stopping_the_other_restore_attempt()
    {
        using var b=new Box("a.json","b.json","z.json");var files=Group(b,b.Names);var calls=new Dictionary<string,int>();
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            calls[path]=calls.GetValueOrDefault(path)+1;
            if(path==b.F("z.json").Path)throw new IOException("Last file refused.");
            if(calls[path]==2)throw new IOException("Restore refused.");
            AtomicFile.Write(path,bytes);
        });
        Assert.Equal(2,result.Failed);Assert.Equal(2,calls[b.F("a.json").Path]);Assert.Equal(2,calls[b.F("b.json").Path]);
        Assert.Equal(GroupFileState.Written,result.States[0]);Assert.Equal(GroupFileState.Written,result.States[1]);
        Assert.Equal(1,b.Disk("a.json"));Assert.Equal(1,b.Disk("b.json"));Assert.Equal(0,b.Disk("z.json"));
    }

    // R42. Source: GroupFile's nullable Expected. No baseline is an explicit caller choice, not implicit detection.
    [Fact]
    public void R42_An_explicit_null_expected_version_can_write_an_existing_file()
    {
        using var b=new Box("a.json");File.WriteAllText(b.F("a.json").Path,Box.Text(7));
        var result=FileGroup.Write([new GroupFile(b.F("a.json").Path,null,Box.Text(1))]);
        Assert.Null(result.Failed);Assert.Equal(GroupFileState.Written,Assert.Single(result.States));Assert.Equal(1,b.Disk("a.json"));
    }

    // R43. Source: FileGroup writes "each file in turn." An empty group is a truthful no-op.
    [Fact]
    public void R43_An_empty_group_does_not_call_the_writer()
    {
        var calls=0;var result=FileGroup.Write([],(_,_)=>calls++);
        Assert.Empty(result.States);Assert.Null(result.Failed);Assert.Null(result.Why);Assert.Equal(0,calls);
    }

    // R44. Source: "Bytes kept exactly on put-back" and "The order of files."
    [Fact]
    public void R44_A_middle_failure_restores_every_earlier_encoding_and_does_not_write_later_files()
    {
        using var b=new Box("a.json","b.json","c.json","z.json");
        var a=new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(Box.Text(0).Replace("\n","\r\n",StringComparison.Ordinal))).ToArray();
        var bb=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Box.Text(0))).ToArray();
        b.Replace("a.json",a);b.Replace("b.json",bb);var files=Group(b,b.Names);var calls=new List<string>();
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            calls.Add(Path.GetFileName(path));if(path==b.F("c.json").Path)throw new IOException("Middle file refused.");
            AtomicFile.Write(path,bytes);
        });
        Assert.Equal(2,result.Failed);Assert.Equal(new[]{"a.json","b.json","c.json"},calls.Take(3).ToArray());
        Assert.DoesNotContain("z.json",calls);Assert.Equal(a,b.Bytes("a.json"));Assert.Equal(bb,b.Bytes("b.json"));
        Assert.All(result.States,state=>Assert.Equal(GroupFileState.AsItWas,state));Assert.Equal(0,b.Disk("z.json"));NoTemps(b);
    }

    // R45. Source: "A file changed outside ... between the last look and the write".
    [Fact]
    public void R45_An_outside_change_to_the_next_file_is_preserved_and_the_earlier_write_is_restored()
    {
        using var b=new Box("a.json","b.json");var files=Group(b,b.Names);var a=b.Bytes("a.json");var outside=Box.Text(7);
        var result=FileGroup.Write(files,(path,bytes)=>
        {
            AtomicFile.Write(path,bytes);
            if(path==b.F("a.json").Path && bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(files[0].Text)))
                File.WriteAllText(b.F("b.json").Path,outside);
        });
        Assert.Equal(1,result.Failed);Assert.Equal(a,b.Bytes("a.json"));Assert.Equal(outside,File.ReadAllText(b.F("b.json").Path));
        Assert.Contains("outside",result.Why);NoTemps(b);
    }
}

