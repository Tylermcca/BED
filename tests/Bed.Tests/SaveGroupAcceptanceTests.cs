using System.Text.Json;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

// INVENTED: neutral linked documents and adversarial scenarios, from the public save contract.
// No window, game data, adapter binary, workbook, new dependency or global state.
public sealed class SaveGroupAcceptanceTests
{
    private sealed class Box : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "save-group-acceptance-" + Guid.NewGuid().ToString("N"));
        internal readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal);
        internal readonly DataProject Project;
        internal readonly string[] Names;
        internal Box(params string[] names)
        {
            Names=names;
            Directory.CreateDirectory(Path.Combine(Root,"data"));
            Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Path.Combine(Root,"schemas/doc.schema.json"), """
                {"type":"object","required":["settings"],"properties":{"settings":{"type":"object","default":{"value":0,"depends":""},"required":["value","depends"],"properties":{"value":{"type":"integer","minimum":0},"depends":{"type":"string","default":""}},"additionalProperties":false}},"additionalProperties":false}
                """);
            foreach(var name in names) File.WriteAllText(Path.Combine(Root,"data",name), Text(0));
            Project=DataProject.Open(Path.Combine(Root,"data"),Path.Combine(Root,"schemas"),
                readOnlyFile:name=>ReadOnly.Contains(name)?"This document is protected.":null);
            Project.SchemaFor=_=>"doc.schema.json";
            foreach(var name in names) Assert.True(F(name).Open());
        }
        internal DataFile F(string name) => Project.Find(name)!;
        internal static string Text(int value,string depends="") =>
            JsonSerializer.Serialize(new {settings=new {value,depends}})+"\n";
        internal void Depend(string name,string on)
        {
            F(name).Close();File.WriteAllText(F(name).Path,Text(0,on));Assert.True(F(name).Open());
        }
        internal void Edit(string name,int value) =>
            Assert.True(F(name).Session!.Execute(new SetValue("/settings/value",JsonSerializer.SerializeToElement(value))));
        internal int Current(string name) => F(name).Session!.Current.At("/settings/value")!.Value.GetInt32();
        internal string Disk(string name) => File.ReadAllText(F(name).Path);
        internal int DiskValue(string name)
        {
            using var json=JsonDocument.Parse(Disk(name));
            return json.RootElement.GetProperty("settings").GetProperty("value").GetInt32();
        }
        internal string[] Group(DataFile file) => Project.BeingSaved(file).Select(f=>f.Name).Order(StringComparer.Ordinal).ToArray();
        private Dictionary<string,JsonElement> Saved()
        {
            var result=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
            foreach(var name in Names)
                if(File.Exists(F(name).Path))
                {
                    using var json=JsonDocument.Parse(Disk(name));result[name]=json.RootElement.Clone();
                }
            return result;
        }
        private static string? Check(Dictionary<string,JsonElement> values)
        {
            foreach(var pair in values)
            {
                var settings=pair.Value.GetProperty("settings");
                var dependency=settings.GetProperty("depends").GetString()!;
                if(dependency.Length==0)continue;
                if(!values.TryGetValue(dependency,out var other) ||
                    settings.GetProperty("value").GetInt32()!=other.GetProperty("settings").GetProperty("value").GetInt32())
                    return pair.Key+" needs its declared dependency "+dependency+" at the same value.";
            }
            return null;
        }
        internal string? Relationship(DataFile file)
        {
            var proposed=Saved();
            foreach(var saving in Project.BeingSaved(file))
                if(saving.Session is {} draft) proposed[saving.Name]=draft.Current.Root;
            return Check(proposed);
        }
        internal bool DiskCoherent()
        {
            var saved=Saved();
            return saved.Count==Names.Length && Check(saved) is null;
        }
        internal void LinkPolicy() => Project.SaveCheck=Relationship;
        internal void Both(int value=1)
        {
            foreach(var name in Names)Edit(name,value);
        }
        public void Dispose()
        {
            // The generated fixture must stay one leaf below the configured temporary root before recursive removal.
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var absolute=Path.GetFullPath(Root);
            if(!absolute.StartsWith(temporary,StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(absolute).StartsWith("save-group-acceptance-",StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the fixture temporary root.");
            if(Directory.Exists(absolute))Directory.Delete(absolute,recursive:true);
        }
    }

    // G01. Source: "BeingSaved ... just file, or all ... during SaveAll." A named write sees only itself.
    [Fact]
    public void G01_A_named_save_sees_only_its_own_draft()
    {
        using var b=new Box("a.json","b.json");b.Both();
        string[]? seen=null;b.Project.SaveCheck=f=>{seen=b.Group(f);return null;};
        Assert.True(b.F("a.json").Save());Assert.Equal(["a.json"],seen ?? throw new InvalidOperationException("The expected callback was not observed."));
        Assert.False(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
        Assert.Equal(1,b.DiskValue("a.json"));Assert.Equal(0,b.DiskValue("b.json"));
    }

    // G02. Source: "A chain of three depending on each other; a cycle." All intended drafts can pass together.
    [Fact]
    public void G02_All_dirty_files_are_in_every_ordinary_group_judgment()
    {
        using var b=new Box("a.json","b.json");b.Both();var seen=new List<string[]>();
        b.Project.SaveCheck=f=>{seen.Add(b.Group(f));return null;};
        Assert.Empty(b.Project.SaveAll());Assert.Equal(2,seen.Count);
        Assert.All(seen,ids=>Assert.Equal(["a.json","b.json"],ids));
        Assert.Empty(b.Project.Dirty);Assert.True(b.DiskCoherent());
    }

    // G03. Source: "A chain of three depending on each other." Judge the whole proposed chain, not partial disk states.
    [Fact]
    public void G03_A_three_document_chain_saves_and_meets_its_persisted_contract()
    {
        using var b=new Box("a.json","b.json","c.json");b.Depend("a.json","b.json");b.Depend("b.json","c.json");
        b.Both();b.LinkPolicy();Assert.Empty(b.Project.SaveAll());
        Assert.True(b.DiskCoherent());Assert.Empty(b.Project.Dirty);
    }

    // G04. Source: "a cycle". A declared cycle can be saved as a valid complete proposal.
    [Fact]
    public void G04_A_two_document_cycle_can_be_saved_together()
    {
        using var b=new Box("a.json","b.json");b.Depend("a.json","b.json");b.Depend("b.json","a.json");b.Both();b.LinkPolicy();
        Assert.False(b.F("a.json").Save());Assert.All(b.Project.Files,f=>Assert.True(f.Dirty));
        Assert.Empty(b.Project.SaveAll());Assert.True(b.DiskCoherent());Assert.Empty(b.Project.Dirty);
    }

    // G05. Source: "a refusal ... chain". A changed provider must leave the dependent and its draft intact.
    [Fact]
    public void G05_Known_outside_change_refuses_a_provider_and_its_dependant_before_writing()
    {
        using var b=new Box("a.json","b.json");b.Depend("b.json","a.json");b.Both();b.LinkPolicy();
        File.AppendAllText(b.F("a.json").Path,"\n");var a=b.Disk("a.json");var before=b.Disk("b.json");
        Assert.Equal(2,b.Project.SaveAll().Count);Assert.Equal(a,b.Disk("a.json"));Assert.Equal(before,b.Disk("b.json"));
        Assert.True(b.DiskCoherent());Assert.Equal(2,b.Project.Dirty.Count);
    }

    // G06. Source: "A file going read-only". A policy refusal removes it from the candidate dependency set.
    [Fact]
    public void G06_Known_read_only_provider_keeps_both_linked_drafts()
    {
        using var b=new Box("a.json","b.json");b.Depend("b.json","a.json");b.Both();b.LinkPolicy();b.ReadOnly.Add("a.json");
        Assert.Equal(2,b.Project.SaveAll().Count);Assert.True(b.DiskCoherent());
        Assert.Equal(2,b.Project.Dirty.Count);Assert.Contains("protected",b.F("a.json").Problems.Single().Message);
    }

    // G07. Source: "when some go and some don't". An independent refusal need not block unrelated work.
    [Fact]
    public void G07_An_independent_refusal_saves_only_the_allowed_file()
    {
        using var b=new Box("a.json","b.json");b.Both();b.Project.SaveCheck=f=>f.Name=="b.json"?"Keep this draft.":null;
        Assert.Equal([b.F("b.json")],b.Project.SaveAll());Assert.False(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
        Assert.Equal(1,b.DiskValue("a.json"));Assert.Equal(0,b.DiskValue("b.json"));
    }

    // G08. Source: "a refusal that frees another". The smaller set must be judged again rather than frozen as refused.
    [Fact]
    public void G08_Removing_a_refused_file_can_free_the_remaining_file()
    {
        using var b=new Box("a.json","b.json");b.Both();
        b.Project.SaveCheck=f=>f.Name=="a.json"?"No A.":b.Group(f).Contains("a.json")?"B cannot go with A.":null;
        Assert.Equal([b.F("a.json")],b.Project.SaveAll());
        Assert.True(b.F("a.json").Dirty);Assert.False(b.F("b.json").Dirty);
    }

    // G09. Source: "a refusal ... chain". A previously allowed consumer must be reconsidered after a late refusal.
    [Fact]
    public void G09_A_refused_last_provider_causes_an_earlier_consumer_to_be_rejudged()
    {
        using var b=new Box("a.json","z.json");b.Depend("a.json","z.json");b.Both();var calls=0;
        b.Project.SaveCheck=f=>f.Name=="z.json"?"Provider refused.":(++calls>0?b.Relationship(f):null);
        Assert.Equal(2,b.Project.SaveAll().Count);Assert.True(calls>=2);Assert.True(b.DiskCoherent());
        Assert.Equal(2,b.Project.Dirty.Count);
    }

    // G10. Source: "A check that changes its answer between passes". The final refusal must govern writes.
    [Fact]
    public void G10_A_changed_answer_on_the_second_pass_is_not_ignored()
    {
        using var b=new Box("a.json","z.json");b.Both();var visits=0;
        b.Project.SaveCheck=f=>f.Name=="z.json"?"Reject Z.":++visits==1?null:"A changed its answer.";
        Assert.Equal(2,b.Project.SaveAll().Count);Assert.Equal(2,visits);
        Assert.Equal(2,b.Project.Dirty.Count);Assert.Equal(0,b.DiskValue("a.json"));
    }

    // G11. Source: "throws on the second". Exceptions are refusals and preserve the draft/disk.
    [Fact]
    public void G11_A_throw_on_a_later_pass_is_a_reported_refusal()
    {
        using var b=new Box("a.json","z.json");b.Both();var visits=0;
        b.Project.SaveCheck=f=>f.Name=="z.json"?"Reject Z.":++visits==1?null:throw new InvalidOperationException("Second-pass fault.");
        Assert.Equal(2,b.Project.SaveAll().Count);Assert.Equal(2,b.Project.Dirty.Count);
        Assert.Contains("Second-pass fault",b.F("a.json").Problems.Single().Message);
        Assert.Equal(0,b.DiskValue("a.json"));
    }

    // G12. Source: "edits a draft while it runs". The target revision that was not judged must stay unsaved.
    [Fact]
    public void G12_A_check_mutating_its_own_draft_cannot_save_it()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var history=b.F("a.json").Session!.History.UndoCount;
        b.Project.SaveCheck=_=>{b.Edit("a.json",2);return null;};
        Assert.Equal([b.F("a.json")],b.Project.SaveAll());Assert.Equal(2,b.Current("a.json"));
        Assert.True(b.F("a.json").Dirty);Assert.Equal(history+1,b.F("a.json").Session!.History.UndoCount);
        Assert.Equal(0,b.DiskValue("a.json"));
    }

    // G13. Source: "edits a draft while it runs" and "Lost work ... always a gap". All judged revisions must stay current.
    [Fact]
    public void G13_A_later_check_must_not_publish_an_unjudged_revision_of_an_earlier_draft()
    {
        using var b=new Box("a.json","b.json");b.Depend("b.json","a.json");b.Both();var changed=false;
        b.Project.SaveCheck=f=>
        {
            var answer=b.Relationship(f);
            if(f.Name=="b.json"&&!changed){changed=true;b.Edit("a.json",9);}
            return answer;
        };
        b.Project.SaveAll();
        Assert.True(b.DiskCoherent(),"A check changed an already-judged provider; persisting the invalid pair is a gap.");
        Assert.True(b.F("a.json").Dirty || b.Current("a.json")==1,"An unjudged changed draft must not be marked saved.");
    }

    // G14. Source: "closed files". A closed clean file is not an implicit save candidate.
    [Fact]
    public void G14_Closed_clean_files_are_not_opened_or_saved_by_SaveAll()
    {
        using var b=new Box("a.json","b.json");b.F("b.json").Close();b.Edit("a.json",1);string[]? seen=null;
        b.Project.SaveCheck=f=>{seen=b.Group(f);return null;};Assert.Empty(b.Project.SaveAll());
        Assert.Equal(["a.json"],seen ?? throw new InvalidOperationException("The expected callback was not observed."));Assert.Null(b.F("b.json").Session);Assert.Equal(0,b.DiskValue("b.json"));
    }

    // G15. Source: "files with no edits in the group". A clean session is not counted as an intended write.
    [Fact]
    public void G15_Clean_open_files_are_excluded_from_the_group()
    {
        using var b=new Box("a.json","b.json");b.Edit("a.json",1);string[]? seen=null;
        b.Project.SaveCheck=f=>{seen=b.Group(f);return null;};Assert.Empty(b.Project.SaveAll());
        Assert.Equal(["a.json"],seen ?? throw new InvalidOperationException("The expected callback was not observed."));Assert.False(b.F("b.json").Dirty);Assert.Equal(0,b.DiskValue("b.json"));
    }

    // G16. Source: "files with no edits". Saving an empty group invokes no permission callbacks.
    [Fact]
    public void G16_An_empty_group_is_a_no_op_with_no_validator_calls()
    {
        using var b=new Box("a.json");var calls=0;b.Project.SaveCheck=_=>{calls++;return "Should not run.";};
        Assert.Empty(b.Project.SaveAll());Assert.Equal(0,calls);Assert.Empty(b.Project.Dirty);
    }

    // G17. Source: "is slow" and "changed outside ... between judging and writing". Recheck disk after a slow own check.
    [Fact]
    public void G17_A_slow_own_check_does_not_waive_an_outside_edit()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var external=Box.Text(7);
        b.Project.SaveCheck=_=>{Thread.Sleep(20);File.WriteAllText(b.F("a.json").Path,external);return null;};
        Assert.Equal([b.F("a.json")],b.Project.SaveAll());Assert.Equal(external,b.Disk("a.json"));Assert.True(b.F("a.json").Dirty);
    }

    // G18. Source: "calls SaveAll ... itself". A rejected outer permission must not write by reentering the same route.
    [Fact]
    public void G18_A_recursive_SaveAll_cannot_publish_work_before_the_outer_refusal()
    {
        using var b=new Box("a.json","b.json");b.Both();var inside=false;
        b.Project.SaveCheck=_=>
        {
            if(inside)return null;
            inside=true;try{b.Project.SaveAll();}finally{inside=false;}
            return "Outer permission refused.";
        };
        b.Project.SaveAll();
        Assert.Equal(0,b.DiskValue("a.json"));
        Assert.Equal(0,b.DiskValue("b.json"));Assert.Equal(2,b.Project.Dirty.Count);
    }

    // G19. Source: "calls ... Save itself". SaveCheck must not bypass its unfinished verdict through a sibling Save.
    [Fact]
    public void G19_A_recursive_named_Save_cannot_publish_a_sibling_from_inside_permission()
    {
        using var b=new Box("a.json","b.json");b.Both();var inside=false;
        b.Project.SaveCheck=f=>
        {
            if(inside)return null;
            inside=true;try{b.F("b.json").Save();}finally{inside=false;}
            return "Outer save refused.";
        };
        Assert.False(b.F("a.json").Save());Assert.Equal(0,b.DiskValue("b.json"));Assert.True(b.F("b.json").Dirty);
    }

    // G20. Source: "BeingSaved during that". A nested attempt must not erase the outer candidate context.
    [Fact]
    public void G20_A_nested_refused_group_preserves_the_outer_BeingSaved_context()
    {
        using var b=new Box("a.json","b.json");b.Both();var inside=false;var attempted=false;string[]? after=null;
        b.Project.SaveCheck=f=>
        {
            if(inside)return "Nested attempt refused.";
            if(!attempted){attempted=true;inside=true;try{b.Project.SaveAll();}finally{inside=false;}after=b.Group(f);}
            return null;
        };
        b.Project.SaveAll();Assert.Equal(["a.json","b.json"],after ?? throw new InvalidOperationException("The nested context was not observed."));
        Assert.Equal(["a.json"],b.Group(b.F("a.json")));
    }

    // G21. Source: "Dirty, undo and journal after each". Schema refusals do not add commands or become save work.
    [Fact]
    public void G21_A_schema_refusal_keeps_history_journal_and_disk_unchanged()
    {
        using var b=new Box("a.json");var s=b.F("a.json").Session!;
        Assert.False(s.Execute(new SetValue("/settings/value",JsonSerializer.SerializeToElement(-1))));
        Assert.False(s.Dirty);Assert.Equal(0,s.History.UndoCount);Assert.Empty(s.Journal);
        Assert.Empty(b.Project.SaveAll());Assert.Equal(0,b.DiskValue("a.json"));
    }

    // G22. Source: "when some go and some don't". A thrown check reports the failed file without losing its work.
    [Fact]
    public void G22_An_independent_throw_reports_only_the_failed_document()
    {
        using var b=new Box("a.json","b.json");b.Both();
        b.Project.SaveCheck=f=>f.Name=="b.json"?throw new InvalidOperationException("Neutral fault."):null;
        Assert.Equal([b.F("b.json")],b.Project.SaveAll());Assert.Contains("Neutral fault",b.F("b.json").Problems.Single().Message);
        Assert.False(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
    }

    // G23. Source: "The write failing part way" and "a save that can't be reopened ... always a gap".
    [Fact]
    public void G23_A_failed_first_write_does_not_publish_its_dependent_file()
    {
        using var b=new Box("a.json","b.json");b.Depend("b.json","a.json");b.Both();b.LinkPolicy();
        IReadOnlyList<DataFile> refused;
        using(var held=new FileStream(b.F("a.json").Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            refused=b.Project.SaveAll();
        Assert.Contains(b.F("a.json"),refused);
        Assert.True(b.DiskCoherent(),"An actual failed provider write must not be followed by an invalid dependent write.");
        Assert.True(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
    }

    // G24. Source: "The order ... what's saved?" A later I/O failure cannot be hidden behind earlier saved dependants.
    [Fact]
    public void G24_A_failed_last_provider_leaves_a_coherent_persisted_project_or_recovers_it()
    {
        using var b=new Box("a.json","z.json");b.Depend("a.json","z.json");b.Both();b.LinkPolicy();
        IReadOnlyList<DataFile> refused;
        using(var held=new FileStream(b.F("z.json").Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            refused=b.Project.SaveAll();
        Assert.Contains(b.F("z.json"),refused);
        Assert.True(b.DiskCoherent(),"A partial group cannot publish a consumer whose provider failed later.");
        Assert.True(b.F("z.json").Dirty);
    }

    // G25. Source: "is what's reported true, is every draft kept?" Independent successful files can remain saved.
    [Fact]
    public void G25_An_independent_write_error_reports_the_actual_saved_and_unsaved_files()
    {
        using var b=new Box("a.json","b.json");b.Both();
        IReadOnlyList<DataFile> refused;
        using(var held=new FileStream(b.F("b.json").Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            refused=b.Project.SaveAll();
        Assert.Equal([b.F("b.json")],refused);
        Assert.False(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
        Assert.Equal(1,b.DiskValue("a.json"));Assert.Equal(0,b.DiskValue("b.json"));
        Assert.Contains("b.json",b.F("b.json").Problems.Single().Message);
    }

    // G26. Source: "The workspace's ... reports when some go and some don't". Name the refused file truthfully.
    [Fact]
    public void G26_The_workspace_reports_the_refused_file_without_losing_its_draft()
    {
        using var b=new Box("a.json","b.json");b.Both();b.Project.SaveCheck=f=>f.Name=="b.json"?"Neutral restriction.":null;
        var workspace=new DataWorkspace(b.Project);Assert.True(workspace.Show("a.json"));
        Assert.False(workspace.SaveAll());Assert.Contains("b.json",workspace.Said);Assert.Contains("Neutral restriction",workspace.Said);
        Assert.False(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
        Assert.DoesNotContain("Saved 2",workspace.Said);
    }

    // G27. Source: "an agent's reports when some go and some don't". JSON reports match disk and session state.
    [Fact]
    public void G27_The_agent_lists_saved_and_refused_files_accurately()
    {
        using var b=new Box("a.json","b.json");b.Both();b.Project.SaveCheck=f=>f.Name=="b.json"?"Neutral restriction.":null;
        var agent=new AgentSession(b.Project);var answer=agent.Save();
        Assert.Equal(["a.json"],answer["saved"]!.AsArray().Select(x=>x!.GetValue<string>()));
        Assert.Equal("b.json",Assert.Single(answer["refused"]!.AsArray())!["file"]!.GetValue<string>());
        Assert.Contains("Neutral restriction",answer["refused"]![0]!["why"]!.GetValue<string>());
        Assert.False(b.F("a.json").Dirty);Assert.True(b.F("b.json").Dirty);
    }

    // G28. Source: "a cycle" and "an agent's reports". Naming one file does not secretly save another draft.
    [Fact]
    public void G28_The_agent_named_save_refuses_a_cycle_but_explicit_all_can_save_it()
    {
        using var b=new Box("a.json","b.json");b.Depend("a.json","b.json");b.Depend("b.json","a.json");b.Both();b.LinkPolicy();
        var agent=new AgentSession(b.Project);var named=agent.Save("a.json");
        Assert.Empty(named["saved"]!.AsArray());Assert.Single(named["refused"]!.AsArray());
        Assert.Equal(2,b.Project.Dirty.Count);Assert.True(b.DiskCoherent());
        var all=agent.Save();Assert.Empty(all["refused"]!.AsArray());Assert.Equal(2,all["saved"]!.AsArray().Count);
        Assert.Empty(b.Project.Dirty);Assert.True(b.DiskCoherent());
    }

    // G29. Source: "FileCheck" (listed public interface). Lifecycle writes meet their separate policy rather than SaveCheck.
    [Fact]
    public void G29_Whole_file_lifecycle_calls_FileCheck_and_not_the_dirty_save_callback()
    {
        using var b=new Box("a.json");var kinds=new List<FileChangeKind>();var saves=0;
        b.Project.FileCheck=c=>{kinds.Add(c.Kind);return null;};
        b.Project.SaveCheck=_=>{saves++;return "Not a dirty-save route.";};
        Assert.True(b.Project.Create("spare.json","doc.schema.json",out var why),why);
        Assert.True(b.Project.Rename("spare.json","other.json",out why),why);
        Assert.True(b.Project.Delete("other.json",out why),why);
        Assert.True(b.Project.Restore("other.json",out why),why);
        Assert.Equal([FileChangeKind.Create,FileChangeKind.Rename,FileChangeKind.Delete,FileChangeKind.Restore],kinds);
        Assert.Equal(0,saves);Assert.True(b.Project.Find("other.json")!.Open());
    }

    // G30. Source: "Dirty, undo and journal after each". Refusing a save is not an edit or history reset.
    [Fact]
    public void G30_A_refused_save_preserves_undo_redo_and_the_journal()
    {
        using var b=new Box("a.json");b.Edit("a.json",1);var s=b.F("a.json").Session!;
        var before=JournalFile.Write(s.Journal);b.Project.SaveCheck=_=>"Keep the edit.";
        Assert.Single(b.Project.SaveAll());Assert.Equal(before,JournalFile.Write(s.Journal));Assert.Equal(1,s.History.UndoCount);
        Assert.True(s.Undo());Assert.False(s.Dirty);Assert.Equal(0,b.Current("a.json"));
        Assert.True(s.Redo());Assert.True(s.Dirty);Assert.Equal(1,b.Current("a.json"));Assert.Equal(0,b.DiskValue("a.json"));
    }

    // G31. Source: "Dirty, undo and journal after each". Save moves the baseline but does not erase edit history.
    [Fact]
    public void G31_Undo_after_successful_group_save_changes_only_the_draft_and_redo_returns_clean()
    {
        using var b=new Box("a.json","b.json");b.Both();Assert.Empty(b.Project.SaveAll());
        foreach(var f in b.Project.Files)
        {
            var s=f.Session!;Assert.False(s.Dirty);Assert.Equal(1,s.History.UndoCount);Assert.Single(s.Journal);
            Assert.True(s.Undo());Assert.True(s.Dirty);Assert.Equal(1,b.DiskValue(f.Name));
            Assert.True(s.Redo());Assert.False(s.Dirty);Assert.Equal(3,s.Journal.Count);
        }
    }

    // G32. Source: "does a different order of files change what's saved?" Known dependency refusal is order-independent.
    [Theory]
    [InlineData("a.json","z.json")]
    [InlineData("z.json","a.json")]
    public void G32_Known_provider_refusal_keeps_the_declared_pair_in_either_filename_order(string provider,string consumer)
    {
        using var b=new Box(provider,consumer);b.Depend(consumer,provider);b.Both();b.LinkPolicy();b.ReadOnly.Add(provider);
        Assert.Equal(2,b.Project.SaveAll().Count);Assert.True(b.DiskCoherent());Assert.Equal(2,b.Project.Dirty.Count);
        Assert.All(b.Project.Files,f=>Assert.Equal(0,b.DiskValue(f.Name)));
    }

    // G33. Source: "A file going read-only ... between the judging and the writing". An earlier judgment is not permission now.
    [Fact]
    public void G33_A_later_check_cannot_make_an_earlier_protected_file_writable()
    {
        using var b=new Box("a.json","b.json");b.Both();
        b.Project.SaveCheck=f=>{if(f.Name=="b.json")b.ReadOnly.Add("a.json");return null;};
        b.Project.SaveAll();
        Assert.Equal(0,b.DiskValue("a.json"));
        Assert.True(b.F("a.json").Dirty,"A newly protected draft must remain unsaved.");
    }

    // G34. Source: "changed outside ... between the judging and the writing" and "Lost work ... always a gap".
    [Fact]
    public void G34_A_later_check_cannot_cause_an_earlier_outside_edit_to_be_overwritten()
    {
        using var b=new Box("a.json","b.json");b.Both();var external=Box.Text(7);var changed=false;
        b.Project.SaveCheck=f=>
        {
            if(f.Name=="b.json"&&!changed){changed=true;File.WriteAllText(b.F("a.json").Path,external);}
            return null;
        };
        b.Project.SaveAll();
        Assert.Equal(external,b.Disk("a.json"));Assert.True(b.F("a.json").Dirty);
        Assert.Equal(1,b.Current("a.json"));
    }

    // G35. Source: "BeingSaved during that". Refused/throwing saves do not leak an ambient group into later calls.
    [Fact]
    public void G35_A_refused_throwing_group_always_clears_its_ambient_context()
    {
        using var b=new Box("a.json","b.json");b.Both();b.Project.SaveCheck=_=>throw new InvalidOperationException("Neutral refusal.");
        Assert.Equal(2,b.Project.SaveAll().Count);
        Assert.Equal(["a.json"],b.Group(b.F("a.json")));Assert.Equal(["b.json"],b.Group(b.F("b.json")));
        Assert.Equal(2,b.Project.Dirty.Count);
    }
}
