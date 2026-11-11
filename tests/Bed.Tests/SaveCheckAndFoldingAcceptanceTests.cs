using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED neutral shelf fixtures and independent safety oracles.
// BORROWED contracts: DataProject.SaveCheck/DataFile.Save XML, EditorWindow.SaveRefusal/InspectorPlace XML,
// and A082's explicit input/folding boundary questions. No private game inputs, windows or new packages.
[Collection(ImGuiCollection.Name)]
public sealed class SaveCheckAndFoldingAcceptanceTests(ITestOutputHelper output)
{
    private const string SaveSentence = "A game's last word before a file with unsaved edits is written; the edits stay, unsaved, to be corrected.";
    private const string PlaceSentence = "Where the inspector last drew a part of a row, in screen coordinates, or null when it didn't.";
    private const string FoldSentence = "Kept per selected object; a long list starts folded.";
    private sealed class Files : IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("a082-shelves-").FullName;
        internal string Data=>Path.Combine(Root,"data");
        internal string Schemas=>Path.Combine(Root,"schemas");
        internal readonly DataProject Project;
        internal readonly DataWorkspace Workspace;
        internal Files(bool invalid=false)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Schemas,"shelf.schema.json"),"""
                {"type":"object","required":["shelves"],"properties":{"shelves":{"type":"array","items":{"type":"object","required":["id","count"],"properties":{"id":{"type":"string"},"count":{"type":"integer","minimum":0}}}}}}
                """);
            foreach(var name in new[]{"a.json","b.json"})
                File.WriteAllText(Path.Combine(Data,name),"{\"$schema\":\"shelf.schema.json\",\"shelves\":[{\"id\":\"neutral\",\"count\":"+(invalid&&name=="a.json"?"-1":"2")+"}],\"unknown\":{\"retain\":true}}\n");
            Project=DataProject.Open(Data,Schemas);
            Workspace=new DataWorkspace(Project);
        }
        internal string Disk(string name="a.json")=>File.ReadAllText(Path.Combine(Data,name));
        internal DataFile Open(string name="a.json") {var f=Project.Find(name)!;Assert.True(f.Open());return f;}
        internal DataFile Show(string name="a.json") {Assert.True(Workspace.Show(name),Workspace.Said);return Project.Find(name)!;}
        internal void Edit(DataFile f,int n=7)=>Assert.True(f.Session!.Execute(new SetValue("/shelves/0/count",JsonSerializer.SerializeToElement(n))));
        internal int Value(DataFile f)=>f.Session!.Current.At("/shelves/0/count")!.Value.GetInt32();
        internal void Kept(DataFile f,string disk,int n=7)
        {
            Assert.Equal(disk,Disk(f.Name));Assert.True(f.Dirty);Assert.Equal(n,Value(f));Assert.True(f.Session!.History.UndoCount>0);
        }
        public void Dispose()
        {
            var target=Path.GetFullPath(Root);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),target,StringComparison.OrdinalIgnoreCase);
            Directory.Delete(target,true);
        }
    }
    private sealed class View : IDisposable
    {
        internal readonly HeadlessHost Host;
        internal readonly DocumentSession<DataDocument> Session;
        internal readonly EditorModel<DataDocument> Editor;
        internal readonly Inspector<DataDocument> Inspector;
        internal readonly EditorWindow<DataDocument> Window;
        internal View(string first,string? second=null,int height=850)
        {
            var schema=new JsonSchemas().Add("shelf","{}");
            Session=DataKit.Session(new DataFormat().Read("{\"shelves\":["+first+(second is null?"":","+second)+"]}"),schema,"shelf");
            Editor=new(Session,DataEditing.Outline);Inspector=new Inspector<DataDocument>(Editor).Add(DataEditing.ObjectType(schema,"shelf"));
            Editor.Select("/shelves/0");Window=new(Editor,Inspector);
            Host=new(1280,height){Drawing=false};
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());EditorWindow<DataDocument>.SetTextScale(1);
            Host.Frames(Draw,5);
        }
        internal void Draw()=>Window.DrawDocked();
        internal void Frames(int n=3)=>Host.Frames(Draw,n);
        internal InspectorRow Row(params string[] path)
        {
            IReadOnlyList<InspectorRow> rows=Inspector.Rows();InspectorRow? row=null;
            foreach(var label in path){row=Assert.Single(rows,r=>r.Label==label||r.Label.StartsWith(label+" ",StringComparison.Ordinal));rows=Inspector.Nested(row);}
            return row!;
        }
        internal float2 Point(InspectorRow row,InspectorPart part=InspectorPart.Field)
        {var p=Window.InspectorPlace(row.Id,part);Assert.NotNull(p);return p!.Value;}
        internal void Click(InspectorRow row,InspectorPart part=InspectorPart.Field)
        {Frames();var p=Point(row,part);Host.Click(Draw,p.X+8,p.Y+6);Frames();}
        internal uint Id(IReadOnlyList<InspectorRow> stack,string caption)
        {
            var pane=ImGui.Internal.FindWindowByName("Inspector###inspector"u8);Assert.False(pane.IsNull());
            var id=pane.GetID(Encoding.UTF8.GetBytes(stack[0].Id));
            foreach(var row in stack.Skip(1))id=ImGui.Internal.GetIDWithSeed(Encoding.UTF8.GetBytes(row.Id),id);
            return ImGui.Internal.GetIDWithSeed(Encoding.UTF8.GetBytes(caption),id).Value;
        }
        internal (float X,float Y) FocusArrow(params InspectorRow[] stack)
        {
            var wanted=Id(stack,"##inside");
            for(var windows=0;windows<6;windows++)
            {
                var ctx=ImGui.GetCurrentContext();
                var name=ctx.NavWindow.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)ctx.NavWindow.Name)??"";
                if(name.Contains("###inspector",StringComparison.Ordinal))
                    for(var i=0;i<180;i++)
                    {
                        ctx=ImGui.GetCurrentContext();
                        if(ctx.NavId.Value==wanted)
                        {
                            var w=ctx.NavWindow;var r=w.NavRectRel[(int)ctx.NavLayer];
                            return(w.DC.CursorStartPos.X+(r.Min.X+r.Max.X)/2,w.DC.CursorStartPos.Y+(r.Min.Y+r.Max.Y)/2);
                        }
                        Host.Press(Draw,ImGuiKey.Tab);
                    }
                Host.Press(Draw,ImGuiKey.Tab,ctrl:true);
            }
            Assert.Fail("Real keyboard navigation could not reach the named fold arrow.");return default;
        }
        internal void Arrow(bool mouse,params InspectorRow[] stack)
        {
            var p=FocusArrow(stack);
            if(mouse)Host.Click(Draw,p.X,p.Y);else Host.Press(Draw,ImGuiKey.Space);
            Frames();
        }
        internal void Draft(InspectorRow row,string text,bool commit=false)
        {
            Click(row);Assert.True(ImGui.GetIO().WantTextInput,"Named field click did not take text input");
            Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,text);
            if(commit)Host.Press(Draw,ImGuiKey.Enter);
            Frames();
        }
        public void Dispose()=>Host.Dispose();
    }
    private static string Unit(int n=2,string list="list")=>JsonSerializer.Serialize(new {id="shelf",note="original",box=new{note="boxed"},list=Enumerable.Range(0,n).Select(i=>new{id="item"+i,box=new{note="note"+i}}).ToArray()}).Replace("\"list\"",JsonSerializer.Serialize(list),StringComparison.Ordinal);

    // Contract: SaveCheck is the last word for the actual file with unsaved edits.
    [Fact] public void Check_observes_the_current_file_draft_and_all_other_project_drafts()
    {
        using var f=new Files();var a=f.Open();var b=f.Open("b.json");f.Edit(a,7);f.Edit(b,11);
        DataFile? seen=null;f.Project.SaveCheck=file=>{seen=file;Assert.Equal(7,f.Value(file));Assert.Equal(11,f.Value(b));return null;};
        Assert.True(a.Save());Assert.Same(a,seen);Assert.True(b.Dirty);Assert.Contains("\"count\":11",new DataFormat().Write(b.Session!.Current).Replace(" ",""));Assert.Contains("retain",f.Disk());
    }
    // Contract: refused edits stay unsaved to be corrected; each later save gets a fresh verdict.
    [Fact] public void Refusal_then_fix_preserves_history_and_rechecks_the_fixed_value()
    {
        using var f=new Files();var a=f.Open();var disk=f.Disk();var seen=new List<int>();
        f.Project.SaveCheck=file=>{seen.Add(f.Value(file));return f.Value(file)>5?"over shelf budget":null;};
        f.Edit(a);Assert.False(a.Save());f.Kept(a,disk);Assert.Contains("over shelf budget",a.Problems[0].Message);
        f.Edit(a,4);Assert.True(a.Save());Assert.Equal(new[]{7,4},seen);Assert.False(a.Dirty);
        Assert.True(a.Session!.Undo());Assert.Equal(7,f.Value(a));Assert.Equal(4,JsonDocument.Parse(f.Disk()).RootElement.GetProperty("shelves")[0].GetProperty("count").GetInt32());
    }
    // Contract: Save and Save as check SaveRefusal before anything is written.
    [Fact] public void Save_as_refuses_without_creating_target_or_losing_source_history()
    {
        using var f=new Files();var a=f.Show();var disk=f.Disk();f.Edit(a);
        f.Project.SaveCheck=_=>"shelf capacity";var target=Path.Combine(f.Data,"copy.json");
        Assert.False(f.Workspace.Window!.SaveAs(target));Assert.False(File.Exists(target));f.Kept(a,disk);
        Assert.Contains("shelf capacity",f.Workspace.Window.Status);
    }
    // Contract: Save as becomes the session file and retains edits on failure.
    [Fact] public void Accepted_save_as_checks_draft_and_keeps_original_bytes()
    {
        using var f=new Files();var a=f.Show();var original=f.Disk();f.Edit(a);
        var calls=0;f.Project.SaveCheck=file=>{calls++;Assert.Same(a,file);return null;};
        var target=Path.Combine(f.Data,"copy.json");Assert.True(f.Workspace.Window!.SaveAs(target));
        Assert.Equal(1,calls);Assert.Equal(original,f.Disk());Assert.Equal(target,a.Session!.FilePath);
        Assert.False(a.Dirty);Assert.Equal(7,JsonDocument.Parse(File.ReadAllText(target)).RootElement.GetProperty("shelves")[0].GetProperty("count").GetInt32());
    }
    // Contract: adapter faults must not escape a person's Save action or lose edits (A082 throw case).
    [Fact] public void Throwing_check_is_a_visible_window_refusal_not_an_unhandled_error()
    {
        using var f=new Files();var a=f.Show();var disk=f.Disk();f.Edit(a);
        f.Project.SaveCheck=_=>throw new InvalidOperationException("neutral checker unavailable");
        var error=Record.Exception(()=>f.Workspace.Window!.Run("editor.save"));f.Kept(a,disk);
        Assert.Null(error);Assert.Contains("neutral checker unavailable",f.Workspace.Window!.Status);
    }
    // Contract: a SaveCheck exception must fail closed, preserving the pending draft and explaining failure.
    [Fact] public void Throwing_check_is_a_datafile_refusal_with_the_original_draft_intact()
    {
        using var f=new Files();var a=f.Open();var disk=f.Disk();f.Edit(a);
        f.Project.SaveCheck=_=>throw new IOException("neutral verifier offline");
        bool? saved=null;var error=Record.Exception(()=>saved=a.Save());f.Kept(a,disk);
        Assert.Null(error);Assert.False(saved);Assert.Contains("neutral verifier offline",string.Join(" ",a.Problems.Select(x=>x.Message)));
    }
    // Contract: people and agents both meet the check; agents return an actionable refusal.
    [Fact] public void Throwing_check_is_a_structured_agent_refusal()
    {
        using var f=new Files();var a=f.Open();var disk=f.Disk();f.Edit(a);f.Project.SaveCheck=_=>throw new InvalidOperationException("retry checker");
        JsonObject? answer=null;var error=Record.Exception(()=>answer=new AgentSession(f.Project).Save("a.json"));f.Kept(a,disk);
        Assert.Null(error);Assert.Contains("retry checker",answer!.ToJsonString());Assert.NotEmpty(answer["refused"]!.AsArray());
    }
    // Contract: save all returns every unsaved file and its reason, even when a checker faults.
    [Fact] public void Save_all_reports_a_checker_fault_and_continues_the_independent_allowed_file()
    {
        using var f=new Files();var a=f.Open();var b=f.Open("b.json");var disk=f.Disk();f.Edit(a);f.Edit(b,3);
        f.Project.SaveCheck=file=>file.Name=="a.json"?throw new InvalidOperationException("a checker fault"):null;
        bool? ok=null;var error=Record.Exception(()=>ok=f.Workspace.SaveAll());f.Kept(a,disk);
        Assert.Null(error);Assert.False(ok);Assert.False(b.Dirty);Assert.Contains("a checker fault",f.Workspace.Said);
    }
    // Contract: a slow check is still the last word; no write precedes its answer.
    [Fact] public async Task A_waiting_check_writes_nothing_until_released()
    {
        using var f=new Files();var a=f.Open();var disk=f.Disk();f.Edit(a);
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        f.Project.SaveCheck=_=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))return "bounded wait expired";return null;};
        var task=Task.Run(a.Save);
        try {Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));Assert.False(task.IsCompleted);f.Kept(a,disk);}
        finally{release.Set();}
        Assert.True(await task);Assert.False(a.Dirty);
    }
    // Contract: outside edits must never be overwritten, including edits made while the check runs.
    [Fact] public void A_file_change_during_the_datafile_check_is_preserved()
    {
        using var f=new Files();var a=f.Open();f.Edit(a);var outside=f.Disk().Replace("\"count\":2","\"count\":19");
        f.Project.SaveCheck=_=>{File.WriteAllText(a.Path,outside);return null;};
        var saved=a.Save();output.WriteLine("saved="+saved+"; outside expected count=19; disk count="+JsonDocument.Parse(f.Disk()).RootElement.GetProperty("shelves")[0].GetProperty("count").GetInt32());Assert.Equal(outside,f.Disk());Assert.False(saved);Assert.True(a.Dirty);
    }
    // Contract: outside edits must never be overwritten; the window checks again after its callback.
    [Fact] public void A_file_change_during_the_window_check_is_preserved()
    {
        using var f=new Files();var a=f.Show();f.Edit(a);var outside=f.Disk().Replace("\"count\":2","\"count\":19");
        f.Project.SaveCheck=_=>{File.WriteAllText(a.Path,outside);return null;};
        Assert.False(f.Workspace.Window!.Run("editor.save"));Assert.Equal(outside,f.Disk());Assert.True(a.Dirty);
        Assert.Contains("outside",f.Workspace.Window.Status);
    }
    // Contract: a file deleted outside is not silently put back, even during a slow check.
    [Fact] public void A_deletion_during_the_datafile_check_is_not_resurrected()
    {
        using var f=new Files();var a=f.Open();f.Edit(a);
        f.Project.SaveCheck=_=>{File.Delete(a.Path);return null;};
        var saved=a.Save();output.WriteLine("saved="+saved+"; deleted file exists="+File.Exists(a.Path));Assert.False(File.Exists(a.Path));Assert.False(saved);Assert.True(a.Dirty);
    }
    // Contract: no unchecked draft revision is written after the last-word checker changes it.
    [Fact] public void A_draft_mutated_by_its_check_requires_a_new_verdict()
    {
        using var f=new Files();var a=f.Open();var disk=f.Disk();f.Edit(a);
        f.Project.SaveCheck=file=>{var validated=f.Value(file);f.Edit(file,9);return validated==7?null:"nine refused";};
        var saved=a.Save();output.WriteLine("saved="+saved+"; draft count="+f.Value(a)+"; disk count="+JsonDocument.Parse(f.Disk()).RootElement.GetProperty("shelves")[0].GetProperty("count").GetInt32());Assert.Equal(disk,f.Disk());Assert.False(saved);Assert.True(a.Dirty);Assert.Equal(9,f.Value(a));
    }
    // Contract: edits created during a check stay pending; observe completion reporting without prescribing a snapshot policy.
    [Fact] public void A_check_that_edits_another_clean_file_keeps_that_new_draft_pending()
    {
        using var f=new Files();var a=f.Open();var b=f.Open("b.json");var oldB=f.Disk("b.json");f.Edit(a);
        f.Project.SaveCheck=_=>{f.Edit(b,12);return null;};
        var refused=f.Project.SaveAll();Assert.True(b.Dirty);Assert.Equal(12,f.Value(b));Assert.Equal(oldB,f.Disk("b.json"));
        output.WriteLine("new draft remains dirty; SaveAll returned refused="+refused.Count+"; dynamic completion policy needs to be explicit.");
    }
    // Contract: read-only files are refused; a check changing that policy must not authorize a late write.
    [Fact] public void Read_only_policy_enabled_during_check_prevents_the_write()
    {
        using var f=new Files();var locked=false;
        var project=DataProject.Open(f.Data,f.Schemas,readOnlyFile:_=>locked?"now locked":null);
        var a=project.Find("a.json")!;Assert.True(a.Open());f.Edit(a);var disk=f.Disk();
        project.SaveCheck=_=>{locked=true;return null;};
        var saved=a.Save();output.WriteLine("saved="+saved+"; draft count="+f.Value(a)+"; disk count="+JsonDocument.Parse(f.Disk()).RootElement.GetProperty("shelves")[0].GetProperty("count").GetInt32());Assert.Equal(disk,f.Disk());Assert.False(saved);Assert.True(a.Dirty);
    }
    // Contract: saving a file with no edits does nothing and returns true.
    [Fact] public void Undo_to_clean_does_not_call_check_or_touch_last_write_time()
    {
        using var f=new Files();var a=f.Open();f.Edit(a);Assert.True(a.Session!.Undo());Assert.False(a.Dirty);
        File.SetLastWriteTimeUtc(a.Path,new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc));var time=File.GetLastWriteTimeUtc(a.Path);
        f.Project.SaveCheck=_=>throw new InvalidOperationException("must not inspect clean draft");
        Assert.True(a.Save());Assert.Equal(time,File.GetLastWriteTimeUtc(a.Path));
    }
    // Contract: save all returns failed files with why; allowed files may be saved independently.
    [Fact] public void Mixed_workspace_save_all_preserves_refused_bytes_and_names_the_problem()
    {
        using var f=new Files();var a=f.Open();var b=f.Open("b.json");var oldA=f.Disk();f.Edit(a,8);f.Edit(b,4);
        f.Project.SaveCheck=file=>file.Name=="a.json"?"budget exceeded":null;
        Assert.False(f.Workspace.SaveAll());f.Kept(a,oldA,8);Assert.False(b.Dirty);
        Assert.Contains("a.json",f.Workspace.Said);Assert.Contains("budget exceeded",f.Workspace.Said);
        Assert.Equal(4,JsonDocument.Parse(f.Disk("b.json")).RootElement.GetProperty("shelves")[0].GetProperty("count").GetInt32());
    }
    // Contract: agents identify both saved and refused files; no transaction is promised by SaveAll.
    [Fact] public void Mixed_agent_save_all_explains_both_sides_of_partial_success()
    {
        using var f=new Files();var a=f.Open();var b=f.Open("b.json");f.Edit(a);f.Edit(b,4);
        f.Project.SaveCheck=file=>file.Name=="a.json"?"blocked shelf":null;
        var reply=new AgentSession(f.Project).Save();
        Assert.Equal("b.json",Assert.Single(reply["saved"]!.AsArray())!.GetValue<string>());
        Assert.Equal("a.json",Assert.Single(reply["refused"]!.AsArray())!["file"]!.GetValue<string>());
        Assert.Contains("blocked shelf",reply.ToJsonString());Assert.True(a.Dirty);Assert.False(b.Dirty);
    }
    // Contract: repair is editable, but a valid repaired draft still meets the final save check.
    [Fact] public void A_repaired_draft_still_requires_a_positive_final_verdict()
    {
        using var f=new Files(invalid:true);var a=f.Project.Find("a.json")!;Assert.True(a.Repair());var disk=f.Disk();f.Edit(a,3);
        var allow=false;f.Project.SaveCheck=_=>allow?null:"review repaired shelf";
        Assert.False(a.Save());f.Kept(a,disk,3);allow=true;Assert.True(a.Save());Assert.False(a.Dirty);
        Assert.DoesNotContain(a.Session!.Validator.Validate(a.Session.Current),x=>x.Severity==Severity.Error);
    }
    // Contract: Create has its own schema/rule/preview contract; record its separate SaveCheck boundary.
    [Fact] public void Creation_uses_schema_defaults_without_overwriting_and_reports_check_coverage()
    {
        using var f=new Files();var calls=0;f.Project.SaveCheck=_=>{calls++;return null;};
        Assert.True(f.Project.Create("new.json","shelf.schema.json",out var why),why);
        var created=File.ReadAllBytes(Path.Combine(f.Data,"new.json"));
        Assert.False(f.Project.Create("new.json","shelf.schema.json",out _));Assert.Equal(created,File.ReadAllBytes(Path.Combine(f.Data,"new.json")));
        output.WriteLine("create SaveCheck calls="+calls+"; new-document lifecycle is separate from dirty-save contract.");
    }
    // Contract: Rename keeps contents, rejects unsaved work, and names its independent lifecycle boundary.
    [Fact] public void Rename_keeps_bytes_rejects_dirty_input_and_records_check_coverage()
    {
        using var f=new Files();var a=f.Open();f.Edit(a);Assert.False(f.Project.Rename("a.json","renamed.json",out _));
        Assert.True(a.Session!.Undo());var bytes=File.ReadAllBytes(a.Path);var calls=0;f.Project.SaveCheck=_=>{calls++;return null;};
        Assert.True(f.Project.Rename("a.json","renamed.json",out var why),why);
        Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(f.Data,"renamed.json")));Assert.False(File.Exists(a.Path));
        output.WriteLine("rename SaveCheck calls="+calls+"; cross-file name policy needs its own lifecycle preflight.");
    }
    // Contract: Delete rejects dirty files and preserves original bytes for Restore.
    [Fact] public void Delete_cannot_discard_dirty_work_and_records_its_lifecycle_boundary()
    {
        using var f=new Files();var a=f.Open();f.Edit(a);Assert.False(f.Project.Delete("a.json",out _));Assert.True(a.Dirty);
        Assert.True(a.Session!.Undo());var calls=0;f.Project.SaveCheck=_=>{calls++;return null;};
        Assert.True(f.Project.Delete("a.json",out var why),why);Assert.False(File.Exists(a.Path));Assert.Contains("a.json",f.Project.Deleted);
        output.WriteLine("delete SaveCheck calls="+calls+"; dirty-save veto is not reference-safe deletion approval.");
    }
    // Contract: Restore is byte-for-byte and never overwrites a name created in the meantime.
    [Fact] public void Restore_never_overwrites_and_records_the_separate_check_boundary()
    {
        using var f=new Files();var bytes=File.ReadAllBytes(Path.Combine(f.Data,"a.json"));Assert.True(f.Project.Delete("a.json",out _));
        var calls=0;f.Project.SaveCheck=_=>{calls++;return null;};
        File.WriteAllText(Path.Combine(f.Data,"a.json"),"outside");Assert.False(f.Project.Restore("a.json",out _));Assert.Equal("outside",f.Disk());
        File.Delete(Path.Combine(f.Data,"a.json"));Assert.True(f.Project.Restore("a.json",out var why),why);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(f.Data,"a.json")));
        output.WriteLine("restore SaveCheck calls="+calls+"; restored bytes can need cross-file lifecycle validation.");
    }
    // Contract: generic CLI --save writes accepted schema edits; it constructs its own unconfigured project.
    [Fact] public void Generic_cli_save_does_not_inherit_an_in_process_hosts_delegate()
    {
        using var f=new Files();var calls=0;f.Project.SaveCheck=_=>{calls++;return "host-only veto";};
        using var stdout=new StringWriter();using var stderr=new StringWriter();
        var code=Bed.Agent.Cli.Program.Run(["--data",f.Data,"--schemas",f.Schemas,"set","a.json","/shelves/0/count","6","--save"],TextReader.Null,stdout,stderr);
        Assert.Equal(0,code);Assert.Empty(stderr.ToString());Assert.Contains("\"saved\"",stdout.ToString());Assert.Contains("6",f.Disk());
        output.WriteLine("generic CLI inherited delegate calls="+calls+"; this is not a game-configured CLI.");
    }
    // Contract: --save cannot force a schema-refused edit to disk.
    [Fact] public void Cli_save_flag_does_not_persist_a_schema_refusal()
    {
        using var f=new Files();var disk=f.Disk();using var stdout=new StringWriter();using var stderr=new StringWriter();
        var code=Bed.Agent.Cli.Program.Run(["--data",f.Data,"--schemas",f.Schemas,"set","a.json","/shelves/0/count","-4","--save"],TextReader.Null,stdout,stderr);
        Assert.Equal(1,code);Assert.Equal(disk,f.Disk());Assert.Contains("refused",stdout.ToString());
    }

    // Contract: InspectorPlace returns null for a part which was not drawn.
    [Fact] public void Missing_row_and_unavailable_parts_have_no_locator()
    {
        using var v=new View(Unit());var note=v.Row("note");
        Assert.Null(v.Window.InspectorPlace("never submitted"));Assert.Null(v.Window.InspectorPlace(note.Id,InspectorPart.Add));Assert.Null(v.Window.InspectorPlace(note.Id,InspectorPart.Remove));
    }
    // Contract: Field identifies the actual editable control; Remove is checked independently.
    [Fact] public void Named_field_locator_takes_real_keyboard_input()
    {
        using var v=new View(Unit());v.Draft(v.Row("note"),"typed by locator",commit:true);
        Assert.Equal("typed by locator",v.Session.Current.At("/shelves/0/note")!.Value.GetString());Assert.Equal(1,v.Session.History.UndoCount);
    }
    // Contract: Add locates the list's actual Add button.
    [Fact] public void Named_add_locator_inserts_once_and_undo_removes_that_item()
    {
        using var v=new View(Unit());v.Click(v.Row("list"),InspectorPart.Add);
        Assert.Equal(3,v.Session.Current.At("/shelves/0/list")!.Value.GetArrayLength());Assert.True(v.Session.Undo());Assert.Equal(2,v.Session.Current.At("/shelves/0/list")!.Value.GetArrayLength());
    }
    // Contract: Remove locates the item's button, not the adjacent read-only summary.
    [Fact] public void Named_remove_locator_removes_the_identified_item()
    {
        using var v=new View(Unit());var item=v.Row("list","1.");v.Click(item,InspectorPart.Remove);
        Assert.Equal("item1",v.Session.Current.At("/shelves/0/list/0/id")!.Value.GetString());Assert.True(v.Session.Undo());Assert.Equal(2,v.Session.Current.At("/shelves/0/list")!.Value.GetArrayLength());
    }
    // Contract: no part was drawn after selection becomes empty, so an old locator must not survive.
    [Fact] public void Clearing_selection_invalidates_all_previously_drawn_locators()
    {
        using var v=new View(Unit());var row=v.Row("note");Assert.NotNull(v.Window.InspectorPlace(row.Id));Assert.True(v.Editor.Select());v.Frames();
        Assert.Null(v.Window.InspectorPlace(row.Id));
    }
    // Contract: selection changes cannot leave a locator for a field missing from the new selection.
    [Fact] public void Changing_selection_discards_locations_unique_to_the_previous_object()
    {
        using var v=new View(Unit(),"""{"id":"second","other":"different"}""");var row=v.Row("note");Assert.NotNull(v.Window.InspectorPlace(row.Id));
        Assert.True(v.Editor.Select("/shelves/1"));v.Frames();Assert.Null(v.Window.InspectorPlace(row.Id));Assert.NotNull(v.Window.InspectorPlace(v.Row("other").Id));
    }
    // Contract: a removed last list item has no remaining Field or Remove control.
    [Fact] public void List_removal_invalidates_both_locators_for_the_disappeared_index()
    {
        using var v=new View(Unit());var last=v.Row("list","2.");Assert.NotNull(v.Window.InspectorPlace(last.Id,InspectorPart.Remove));
        v.Click(last,InspectorPart.Remove);Assert.Null(v.Window.InspectorPlace(last.Id));Assert.Null(v.Window.InspectorPlace(last.Id,InspectorPart.Remove));
    }
    // Contract: the fold arrow itself is mouse-operable; hidden children have no locator.
    [Fact] public void Mouse_arrow_closes_and_reopens_a_nested_object()
    {
        using var v=new View(Unit());var box=v.Row("box");var note=v.Row("box","note");var revision=v.Session.Revision;
        Assert.NotNull(v.Window.InspectorPlace(note.Id));v.Arrow(true,box);Assert.Null(v.Window.InspectorPlace(note.Id));
        v.Arrow(true,box);Assert.NotNull(v.Window.InspectorPlace(note.Id));Assert.Equal(revision,v.Session.Revision);Assert.False(v.Session.Dirty);
    }
    // Contract: the arrow can be reached and toggled by keyboard; no NavId is assigned by the test.
    [Fact] public void Keyboard_arrow_closes_and_reopens_without_editing_data()
    {
        using var v=new View(Unit());var box=v.Row("box");var note=v.Row("box","note");
        v.Arrow(false,box);Assert.Null(v.Window.InspectorPlace(note.Id));v.Arrow(false,box);Assert.NotNull(v.Window.InspectorPlace(note.Id));Assert.Equal(0,v.Session.History.UndoCount);
    }
    // Contract: fold state is kept per selected object, not merely by the same property name.
    [Fact] public void One_objects_closed_box_does_not_close_the_next_objects_box()
    {
        using var v=new View(Unit(),Unit());v.Arrow(false,v.Row("box"));Assert.Null(v.Window.InspectorPlace(v.Row("box","note").Id));
        Assert.True(v.Editor.Select("/shelves/1"));v.Frames();Assert.NotNull(v.Window.InspectorPlace(v.Row("box","note").Id));
        Assert.True(v.Editor.Select("/shelves/0"));v.Frames();Assert.Null(v.Window.InspectorPlace(v.Row("box","note").Id));
    }
    // Contract: an unrelated undo is data history, not a request to forget the person's fold choice.
    [Fact] public void Unrelated_undo_retains_the_closed_box_state()
    {
        using var v=new View(Unit());v.Draft(v.Row("note"),"changed",commit:true);v.Arrow(false,v.Row("box"));
        Assert.True(v.Session.Undo());v.Frames();Assert.Null(v.Window.InspectorPlace(v.Row("box","note").Id));Assert.Equal("original",v.Session.Current.At("/shelves/0/note")!.Value.GetString());
    }
    // Contract: fold state across a removed item above must not be borrowed from that removed item's path.
    [Fact] public void Removing_an_item_above_does_not_apply_its_fold_state_to_the_survivor()
    {
        using var v=new View(Unit());var list=v.Row("list");var second=v.Row("list","2.");var box=v.Row("list","2.","box");
        v.Arrow(false,list,second,box);Assert.Null(v.Window.InspectorPlace(v.Row("list","2.","box","note").Id));
        v.Click(v.Row("list","1."),InspectorPart.Remove);
        Assert.Equal("item1",v.Session.Current.At("/shelves/0/list/0/id")!.Value.GetString());
        Assert.Null(v.Window.InspectorPlace(v.Row("list","1.","box","note").Id));
    }
    // Contract: a long list starts folded; it can then be opened through its real arrow.
    [Fact] public void A_51_item_list_starts_closed_and_opens_with_the_mouse()
    {
        using var v=new View(Unit(51));var list=v.Row("list");var first=v.Row("list","1.");
        Assert.Null(v.Window.InspectorPlace(first.Id));v.Arrow(true,list);Assert.NotNull(v.Window.InspectorPlace(first.Id));
        Assert.Equal(51,v.Session.Current.At("/shelves/0/list")!.Value.GetArrayLength());
    }
    // Contract: the long-list threshold is greater than fifty, not fifty or more.
    [Fact] public void A_50_item_list_starts_open()
    {
        using var v=new View(Unit(50));Assert.NotNull(v.Window.InspectorPlace(v.Row("list","1.").Id));Assert.False(v.Session.Dirty);
    }
    // Contract: long-list default applies on first display; an existing user's open choice survives growth.
    [Fact] public void An_open_list_that_grows_from_50_to_51_keeps_its_open_choice()
    {
        using var v=new View(Unit(50));var list=v.Row("list");Assert.True(v.Inspector.AddItem(list));v.Frames();
        Assert.Equal(51,v.Session.Current.At("/shelves/0/list")!.Value.GetArrayLength());Assert.NotNull(v.Window.InspectorPlace(v.Row("list","1.").Id));
    }
    // Contract: a long list explicitly kept closed stays closed after it shrinks below the default threshold.
    [Fact] public void A_closed_list_that_shrinks_to_50_keeps_its_fold_choice()
    {
        using var v=new View(Unit(51));Assert.True(v.Inspector.RemoveItem(v.Row("list","51.")));v.Frames();
        Assert.Equal(50,v.Session.Current.At("/shelves/0/list")!.Value.GetArrayLength());Assert.Null(v.Window.InspectorPlace(v.Row("list","1.").Id));
    }
    // Contract: a long list opened by keyboard can be folded again without losing its contents.
    [Fact] public void A_long_list_keyboard_open_and_close_is_view_state_only()
    {
        using var v=new View(Unit(60));var list=v.Row("list");var before=v.Session.Current.Root.GetRawText();
        v.Arrow(false,list);Assert.NotNull(v.Window.InspectorPlace(v.Row("list","1.").Id));v.Arrow(false,list);
        Assert.Null(v.Window.InspectorPlace(v.Row("list","1.").Id));Assert.Equal(before,v.Session.Current.Root.GetRawText());Assert.Equal(0,v.Session.History.UndoCount);
    }

    // Contract: TextField commits on Enter or on leaving the box; a fold must not silently lose pending valid text.
    [Fact] public void Clicking_the_fold_arrow_commits_valid_text_when_it_leaves_the_box()
    {
        using var v=new View(Unit());var box=v.Row("box");var note=v.Row("box","note");
        // Locate first. Do not Tab away from pending text: that would test Tab's commit instead of folding.
        var arrow=v.FocusArrow(box);
        v.Draft(note,"pending shelf note");Assert.Equal("boxed",v.Session.Current.At("/shelves/0/box/note")!.Value.GetString());
        v.Host.Click(v.Draw,arrow.X,arrow.Y);v.Frames();
        Assert.Null(v.Window.InspectorPlace(note.Id));
        var afterFold=v.Session.Current.At("/shelves/0/box/note")!.Value.GetString();var stepsAfterFold=v.Session.History.UndoCount;
        v.Host.Click(v.Draw,arrow.X,arrow.Y);v.Frames();Assert.NotNull(v.Window.InspectorPlace(note.Id));
        v.Click(note);v.Host.Press(v.Draw,ImGuiKey.Enter);v.Frames();
        output.WriteLine("after direct fold: count="+stepsAfterFold+"; value="+afterFold+"; after reopen/Enter="+v.Session.Current.At("/shelves/0/box/note")!.Value.GetString());
        Assert.Equal("pending shelf note",afterFold);Assert.Equal(1,stepsAfterFold);
        Assert.Equal("pending shelf note",v.Session.Current.At("/shelves/0/box/note")!.Value.GetString());
    }

    // Contract: locators remain screen coordinates when a row is scrolled; scripts may scroll to an offscreen item.
    [Fact] public void Scrolling_to_an_offscreen_named_field_reaches_and_edits_it()
    {
        using var v=new View(Unit(18),height:500);var row=v.Row("list","18.","box","note");var before=v.Point(row);
        var pane=ImGui.Internal.FindWindowByName("Inspector###inspector"u8);Assert.True(before.Y>pane.InnerClipRect.Max.Y);
        for(var i=0;i<60;i++)
        {
            var at=v.Point(row);pane=ImGui.Internal.FindWindowByName("Inspector###inspector"u8);
            if(at.Y>=pane.InnerClipRect.Min.Y&&at.Y+18<pane.InnerClipRect.Max.Y)break;
            v.Host.MoveMouse(v.Draw,pane.InnerClipRect.Min.X+25,(pane.InnerClipRect.Min.Y+pane.InnerClipRect.Max.Y)/2);
            ImGui.GetIO().AddMouseWheelEvent(0,-4);v.Frames();
        }
        var final=v.Point(row);pane=ImGui.Internal.FindWindowByName("Inspector###inspector"u8);
        Assert.InRange(final.Y,pane.InnerClipRect.Min.Y,pane.InnerClipRect.Max.Y-18);Assert.True(final.Y<before.Y);
        v.Draft(row,"scrolled edit",commit:true);Assert.Equal("scrolled edit",v.Session.Current.At("/shelves/0/list/17/box/note")!.Value.GetString());
    }
    // Contract: folded child controls cannot retain a stale Add or Remove part.
    [Fact] public void Folding_a_list_invalidates_its_child_remove_control_but_keeps_its_own_add()
    {
        using var v=new View(Unit());var list=v.Row("list");var child=v.Row("list","1.");
        Assert.NotNull(v.Window.InspectorPlace(child.Id,InspectorPart.Remove));v.Arrow(false,list);
        Assert.Null(v.Window.InspectorPlace(child.Id,InspectorPart.Remove));Assert.NotNull(v.Window.InspectorPlace(list.Id,InspectorPart.Add));
    }
}
