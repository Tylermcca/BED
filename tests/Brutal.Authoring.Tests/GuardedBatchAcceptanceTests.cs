using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

/// <summary>INVENTED neutral ledger fixtures; independent acceptance oracles from the guarded-batch and schema contracts.</summary>
public sealed class GuardedBatchAcceptanceTests(ITestOutputHelper output)
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
    private static SetValue Amount(int index,int value) => new($"/entries/{index}/amount",JsonSerializer.SerializeToElement(value));
    private const string Document = """{"entries":[{"id":"north","amount":1},{"id":"south","amount":2}]}""";
    private const string Shape = """{"type":"object","required":["entries"],"properties":{"$schema":{"type":"string"},"entries":{"type":"array","items":{"type":"object","required":["id","amount"],"properties":{"id":{"type":"string"},"amount":{"type":"integer","minimum":0,"maximum":100,"default":2}},"additionalProperties":false},"default":[]}},"additionalProperties":false}""";
    private sealed class Balanced : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) => d.At("/entries")!.Value.EnumerateArray().Sum(x=>x.GetProperty("amount").GetInt32())==3
            ? [] : [Issue.Error("The two ledger amounts must total three.","/entries","/entries")];
    }
    private sealed class QuietPlay : IPlaySession
    {
        public string Status=>"Neutral preview";
        public void Draw() { }
        public void Dispose() { }
    }
    private sealed class Batch
    {
        internal DocumentSession<DataDocument> Session {get;}
        internal EditorModel<DataDocument> Editor {get;}
        internal Batch(bool balanced=false)
        {
            var schemas=new JsonSchemas().Add("ledger.schema.json",Shape);
            IValidator<DataDocument> validator=new DataValidator(schemas,"ledger.schema.json");
            if(balanced)validator=new AllOf<DataDocument>(validator,new Balanced());
            Session=new(new DataFormat(),validator,DataKit.Catalog(),new DataDocument(J(Document)));
            Editor=new(Session,new FlatOutline<DataDocument>(d=>d.At("/entries")!.Value.EnumerateArray()
                .Select((e,i)=>(Id:$"/entries/{i}",Label:e.GetProperty("id").GetString()!)).ToArray()));
        }
        internal int Value(int index)=>Session.Current.At($"/entries/{index}/amount")!.Value.GetInt32();
        internal (DataDocument Doc,int Steps,int Journal,long Revision) Before() => (Session.Current,Session.History.UndoCount,Session.Journal.Count,Session.Revision);
        internal void Kept((DataDocument Doc,int Steps,int Journal,long Revision) before)
        {
            Assert.Same(before.Doc,Session.Current);Assert.Equal(before.Steps,Session.History.UndoCount);
            Assert.Equal(before.Journal,Session.Journal.Count);Assert.Equal(before.Revision,Session.Revision);
        }
        internal void StartPlay()
        {
            Editor.Add(new PlayMode<DataDocument>("ledger.preview","Ledger preview",_=>new QuietPlay()));
            Assert.True(Editor.Play("ledger.preview"));
        }
    }
    private sealed class Files : IDisposable
    {
        internal string Root {get;}=Path.Combine(Path.GetTempPath(),"guarded-schema-"+Guid.NewGuid().ToString("N"));
        internal string Data=>Path.Combine(Root,"data");
        internal string Schemas=>Path.Combine(Root,"schemas");
        internal DataProject Project {get;}
        internal Files()
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Schemas,"loose.schema.json"),Shape);
            File.WriteAllText(Path.Combine(Schemas,"strict.schema.json"),Shape.Replace("\"maximum\":100","\"maximum\":5"));
            File.WriteAllText(Path.Combine(Schemas,"closed.schema.json"),"""{"type":"object","required":["amount"],"properties":{"amount":{"type":"integer","minimum":0,"default":2}},"additionalProperties":false}""");
            File.WriteAllText(Path.Combine(Schemas,"fallback.schema.json"),Shape);
            File.WriteAllText(Path.Combine(Data,"alpha.json"),Document);
            File.WriteAllText(Path.Combine(Data,"beta.json"),Document);
            Project=DataProject.Open(Data,Schemas);
        }
        internal string Disk(string name="alpha.json")=>File.ReadAllText(Path.Combine(Data,name));
        internal void Write(string name,string text)=>File.WriteAllText(Path.Combine(Data,name),text);
        internal DataFile Open(string name="alpha.json")
        {
            var f=Project.Find(name)!;Assert.True(f.Open(),string.Join(" ",f.Problems.Select(i=>i.Message)));return f;
        }
        public void Dispose()
        {
            var full=Path.GetFullPath(Root);var parent=Path.GetFullPath(Path.GetTempPath());
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("guarded-schema-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup path.");
            Directory.Delete(full,true);
        }
    }

    // Source: applies several commands as one undo step, all or none, under the same refusals as Execute.
    [Fact] public void Playing_refuses_the_entire_batch_and_explains_it_in_editor_Refused()
    {
        var b=new Batch();b.StartPlay();var before=b.Before();
        Assert.False(b.Editor.ExecuteBatch([Amount(0,3),Amount(1,4)],"/entries/0","/entries/1"));
        b.Kept(before);Assert.Contains(b.Editor.Refused,x=>x.Message.Contains("playing",StringComparison.Ordinal));
        Assert.Empty(b.Session.LastIssues);Assert.True(b.Editor.Stop());
    }
    [Fact] public void Focus_checks_every_declared_object_not_only_the_selected_one()
    {
        var b=new Batch();Assert.True(b.Editor.Select("/entries/0"));Assert.True(b.Editor.FocusOn("/entries/0"));var before=b.Before();
        Assert.False(b.Editor.ExecuteBatch([Amount(0,4),Amount(1,5)],"/entries/0","/entries/1"));
        b.Kept(before);Assert.Contains(b.Editor.Refused,x=>x.ObjectId=="/entries/1"&&x.Message.Contains("focus",StringComparison.Ordinal));
        Assert.Empty(b.Session.LastIssues);
    }
    [Fact] public void A_success_after_a_focus_refusal_clears_the_editors_refusal()
    {
        var b=new Batch();Assert.True(b.Editor.FocusOn("/entries/0"));
        Assert.False(b.Editor.ExecuteBatch([Amount(1,5)],"/entries/1"));Assert.NotEmpty(b.Editor.Refused);
        Assert.True(b.Editor.ExecuteBatch([Amount(0,4)],"/entries/0"));Assert.Empty(b.Editor.Refused);
        Assert.Empty(b.Session.LastIssues);Assert.Equal(4,b.Value(0));
    }
    [Fact] public void An_empty_allowed_batch_changes_no_document_history_or_revision()
    {
        var b=new Batch();var before=b.Before();
        Assert.True(b.Editor.ExecuteBatch([]));b.Kept(before);Assert.Empty(b.Editor.Refused);Assert.Empty(b.Session.LastIssues);
    }
    [Fact] public void An_empty_batch_still_cannot_use_the_playing_editor_entry()
    {
        var b=new Batch();b.StartPlay();var before=b.Before();Assert.False(b.Editor.ExecuteBatch([]));
        b.Kept(before);Assert.NotEmpty(b.Editor.Refused);Assert.True(b.Editor.Stop());
    }
    [Fact] public void A_missing_command_target_refuses_without_partial_history()
    {
        var b=new Batch();var before=b.Before();
        Assert.False(b.Editor.ExecuteBatch([Amount(0,4),Amount(77,5)],"/entries/0","/entries/77"));
        b.Kept(before);Assert.Empty(b.Editor.Refused);Assert.NotEmpty(b.Session.LastIssues);
    }
    [Fact] public void A_refused_middle_command_prevents_the_first_and_last_from_applying()
    {
        var b=new Batch();var before=b.Before();
        Assert.False(b.Editor.ExecuteBatch([Amount(0,4),new RemoveItem("/absent/0"),Amount(1,5)],"/entries/0","/entries/1"));
        b.Kept(before);Assert.Equal(1,b.Value(0));Assert.Equal(2,b.Value(1));Assert.NotEmpty(b.Session.LastIssues);
    }
    [Fact] public void A_whole_candidate_validator_refuses_all_of_the_batch()
    {
        var b=new Batch(balanced:true);var before=b.Before();
        Assert.False(b.Editor.ExecuteBatch([Amount(0,2),Amount(1,2)],"/entries/0","/entries/1"));
        b.Kept(before);Assert.Empty(b.Editor.Refused);Assert.Contains(b.Session.LastIssues,x=>x.Message.Contains("total three",StringComparison.Ordinal));
    }
    [Fact] public void Validation_judges_the_final_candidate_not_each_temporary_substep()
    {
        var b=new Batch(balanced:true);Assert.True(b.Editor.ExecuteBatch([Amount(0,2),Amount(1,1)],"/entries/0","/entries/1"));
        Assert.Equal(2,b.Value(0));Assert.Equal(1,b.Value(1));Assert.Equal(1,b.Session.History.UndoCount);Assert.Single(b.Session.Journal);
    }
    [Fact] public void Undo_and_redo_move_the_whole_batch_as_one_step()
    {
        var b=new Batch();var before=b.Session.Current.Root.GetRawText();
        Assert.True(b.Editor.ExecuteBatch([Amount(0,4),Amount(1,5)],"/entries/0","/entries/1"));
        Assert.Equal(1,b.Session.History.UndoCount);Assert.Single(b.Session.Journal);
        Assert.True(b.Session.Undo());Assert.Equal(before,b.Session.Current.Root.GetRawText());
        Assert.True(b.Session.Redo());Assert.Equal(4,b.Value(0));Assert.Equal(5,b.Value(1));Assert.Equal(1,b.Session.History.UndoCount);
    }
    [Fact] public void Session_refusals_clear_on_success_without_becoming_editor_authority_refusals()
    {
        var b=new Batch();Assert.False(b.Editor.ExecuteBatch([Amount(0,-1)],"/entries/0"));
        Assert.Empty(b.Editor.Refused);Assert.NotEmpty(b.Session.LastIssues);
        Assert.True(b.Editor.ExecuteBatch([Amount(0,3)],"/entries/0"));Assert.Empty(b.Editor.Refused);Assert.Empty(b.Session.LastIssues);
    }
    [Fact] public void Editor_refusal_does_not_relabel_an_earlier_session_diagnostic()
    {
        var b=new Batch();Assert.False(b.Editor.ExecuteBatch([Amount(0,-1)],"/entries/0"));
        var old=b.Session.LastIssues.ToArray();b.StartPlay();var before=b.Before();
        Assert.False(b.Editor.ExecuteBatch([Amount(0,3)],"/entries/0"));b.Kept(before);
        Assert.NotEmpty(b.Editor.Refused);Assert.Equal(old,b.Session.LastIssues);
        Assert.True(b.Editor.Stop());
    }
    [Fact] public void Unknown_declared_ids_do_not_prove_the_command_targets_were_resolved()
    {
        var b=new Batch();Assert.True(b.Editor.FocusOn("/entries/0"));var before=b.Before();
        var accepted=b.Editor.ExecuteBatch([Amount(1,9)],"/unknown/object");
        output.WriteLine($"unknown declared touch: accepted={accepted}; outside amount={b.Value(1)}");
        // touches is a caller precondition, not a capability inferred from an opaque command body.
        if(accepted){Assert.Equal(9,b.Value(1));Assert.Equal(before.Steps+1,b.Session.History.UndoCount);}
        else {b.Kept(before);Assert.NotEmpty(b.Editor.Refused.Concat(b.Session.LastIssues));}
    }
    [Fact] public void An_undeclared_changed_object_demonstrates_the_trusted_caller_boundary()
    {
        var b=new Batch();Assert.True(b.Editor.FocusOn("/entries/0"));var before=b.Before();
        var accepted=b.Editor.ExecuteBatch([Amount(0,4),Amount(1,7)],"/entries/0");
        output.WriteLine($"incomplete touches: accepted={accepted}; outside amount={b.Value(1)}");
        if(accepted){Assert.Equal(4,b.Value(0));Assert.Equal(7,b.Value(1));Assert.Single(b.Session.Journal);}
        else {b.Kept(before);Assert.NotEmpty(b.Editor.Refused.Concat(b.Session.LastIssues));}
    }
    [Fact] public void An_old_revision_held_only_by_the_caller_is_not_an_input_to_this_overload()
    {
        var b=new Batch();var revision=b.Session.Revision;var plan=Amount(1,9);
        Assert.True(b.Editor.Execute(new InsertItem("/entries",0,J("""{"id":"inserted","amount":0}"""))));
        Assert.NotEqual(revision,b.Session.Revision);
        var before=b.Before();var accepted=b.Editor.ExecuteBatch([plan],"/entries/1");
        output.WriteLine($"old-revision plan: captured={revision}; before={before.Revision}; accepted={accepted}; pointer id={b.Session.Current.At("/entries/1/id")!.Value.GetString()}");
        if(accepted){Assert.Equal("north",b.Session.Current.At("/entries/1/id")!.Value.GetString());Assert.Equal(9,b.Value(1));}
        else {b.Kept(before);Assert.NotEmpty(b.Editor.Refused.Concat(b.Session.LastIssues));}
    }

    // Source: SchemaFor returns which schema a file uses; null falls back to the usual way; set before opening.
    [Fact] public void A_named_schema_selector_validates_many_files_and_reports_it_to_agents()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";var agent=new AgentSession(f.Project);
        var description=agent.Describe();var rows=description["files"]!.AsArray();Assert.Equal(2,rows.Count);
        Assert.All(rows,x=>Assert.Equal("strict.schema.json",x!["schema"]!.GetValue<string>()));
        var list=agent.Find("alpha.json","/entries");Assert.Equal(2,list["matches"]!.GetValue<int>());
        var refused=agent.Set("alpha.json","/entries/0/amount","6");Assert.NotEmpty(refused["refused"]!.AsArray());Assert.Null(refused["applied"]);
        Assert.Equal(1,f.Open().Session!.Current.At("/entries/0/amount")!.Value.GetInt32());
    }
    [Fact] public void A_null_selector_answer_falls_back_to_the_files_own_schema()
    {
        using var f=new Files();f.Write("alpha.json",Document.Insert(1,"\"$schema\":\"strict.schema.json\","));f.Project.SchemaFor=_=>null;
        Assert.Equal("strict.schema.json",f.Open().Schema);
    }
    [Fact] public void An_unknown_file_uses_the_filename_rule_after_a_null_answer()
    {
        using var f=new Files();f.Write("fallback.json",Document);f.Project.Refresh();
        f.Project.SchemaFor=name=>name=="alpha.json"?"strict.schema.json":null;
        Assert.True(f.Project.Find("fallback.json")!.Open());Assert.Equal("fallback.schema.json",f.Project.Find("fallback.json")!.Schema);
        Assert.False(f.Project.Find("beta.json")!.Open());Assert.Contains(f.Project.Find("beta.json")!.Problems,x=>x.Message.Contains("beta.schema.json",StringComparison.Ordinal));
    }
    [Fact] public void A_missing_selected_schema_refuses_open_without_a_session_or_disk_change()
    {
        using var f=new Files();var before=f.Disk();f.Project.SchemaFor=_=>"absent.schema.json";var a=f.Project.Find("alpha.json")!;
        Assert.False(a.Open());Assert.Null(a.Session);Assert.Equal(before,f.Disk());Assert.Contains(a.Problems,x=>x.Message.Contains("absent.schema.json",StringComparison.Ordinal));
    }
    [Fact] public void A_selector_path_cannot_load_an_unregistered_schema_outside_the_schema_folder()
    {
        using var f=new Files();Directory.CreateDirectory(Path.Combine(f.Root,"outside"));
        File.WriteAllText(Path.Combine(f.Root,"outside","escape.schema.json"),Shape);
        f.Project.SchemaFor=_=>Path.Combine("..","outside","escape.schema.json");var a=f.Project.Find("alpha.json")!;var before=f.Disk();
        Assert.False(a.Open());Assert.Null(a.Session);Assert.Equal(before,f.Disk());Assert.NotEmpty(a.Problems);
    }
    [Fact] public void An_empty_selected_name_is_a_clear_refusal_not_a_schema_bypass()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"";var a=f.Project.Find("alpha.json")!;
        var error=Record.Exception(()=>Assert.False(a.Open()));Assert.Null(error);Assert.Null(a.Session);Assert.NotEmpty(a.Problems);
    }
    [Fact] public void A_host_schema_choice_wins_over_a_disagreeing_data_schema_field()
    {
        using var f=new Files();f.Write("alpha.json",Document.Insert(1,"\"$schema\":\"loose.schema.json\",").Replace("\"amount\":1","\"amount\":8"));
        f.Project.SchemaFor=_=>"strict.schema.json";var a=f.Project.Find("alpha.json")!;
        Assert.False(a.Open());Assert.Equal("strict.schema.json",a.Schema);Assert.NotEmpty(a.Problems);Assert.Null(a.Session);
    }
    [Fact] public void The_open_session_keeps_its_schema_even_if_the_selector_answer_changes()
    {
        using var f=new Files();var selected="strict.schema.json";var calls=0;
        f.Project.SchemaFor=_=>{calls++;return selected;};var a=f.Open();selected="loose.schema.json";
        Assert.False(a.Session!.Execute(Amount(0,8)));Assert.True(a.Session.Execute(Amount(0,4)));Assert.True(a.Save());
        Assert.Equal(1,calls);Assert.Equal("strict.schema.json",a.Schema);
        a.Close();Assert.True(a.Open());Assert.Equal("loose.schema.json",a.Schema);Assert.Equal(2,calls);
    }
    [Fact] public void Changing_to_a_tighter_selector_does_not_secretly_rebind_an_open_draft()
    {
        using var f=new Files();var selected="loose.schema.json";f.Project.SchemaFor=_=>selected;var a=f.Open();
        selected="strict.schema.json";Assert.True(a.Session!.Execute(Amount(0,8)));Assert.True(a.Save());
        output.WriteLine($"open schema pinned={a.Schema}; selector now={selected}; close/reopen checks the new choice");
        a.Close();Assert.False(a.Open());Assert.Equal("strict.schema.json",a.Schema);Assert.NotEmpty(a.Problems);
    }
    [Fact] public void A_throwing_selector_is_a_file_open_refusal_not_a_frame_exception()
    {
        using var f=new Files();var a=f.Project.Find("alpha.json")!;var before=f.Disk();
        f.Project.SchemaFor=_=>throw new InvalidOperationException("neutral selector failed");
        bool? opened=null;var error=Record.Exception(()=>opened=a.Open());
        Assert.Null(error);Assert.False(opened);Assert.Null(a.Session);Assert.Equal(before,f.Disk());
        Assert.Contains(a.Problems,x=>x.Message.Contains("neutral selector failed",StringComparison.Ordinal));
    }
    [Fact] public void A_throwing_selector_does_not_take_down_a_workspace_or_drop_another_draft()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";var workspace=new DataWorkspace(f.Project);
        Assert.True(workspace.Show("alpha.json"));var a=f.Open();Assert.True(a.Session!.Execute(Amount(0,4)));var before=f.Disk();
        f.Project.SchemaFor=name=>name=="beta.json"?throw new InvalidOperationException("neutral selector failed"):"strict.schema.json";
        bool? shown=null;var error=Record.Exception(()=>shown=workspace.Show("beta.json"));
        Assert.Null(error);Assert.False(shown);Assert.Equal("alpha.json",workspace.Active!.Name);Assert.True(a.Dirty);
        Assert.Equal(4,a.Session.Current.At("/entries/0/amount")!.Value.GetInt32());Assert.Equal(before,f.Disk());
    }
    [Fact] public void An_agents_description_contains_selector_failures_instead_of_throwing()
    {
        using var f=new Files();f.Project.SchemaFor=_=>throw new InvalidOperationException("neutral selector failed");
        JsonObject? response=null;var error=Record.Exception(()=>response=new AgentSession(f.Project).Describe());
        Assert.Null(error);Assert.NotNull(response);Assert.All(response["files"]!.AsArray(),x=>Assert.Contains("neutral selector failed",x!["cantOpen"]!.GetValue<string>()));
    }
    [Fact] public void An_agents_list_explains_a_selector_failure_instead_of_throwing()
    {
        using var f=new Files();f.Project.SchemaFor=_=>throw new InvalidOperationException("neutral selector failed");
        JsonObject? response=null;var error=Record.Exception(()=>response=new AgentSession(f.Project).Find("alpha.json","/entries"));
        Assert.Null(error);Assert.NotNull(response);Assert.Contains("neutral selector failed",response.ToJsonString());
    }
    [Fact] public void Creating_a_closed_schema_file_uses_the_mapping_without_injecting_schema()
    {
        using var f=new Files();f.Project.SchemaFor=name=>name=="fresh.json"?"closed.schema.json":"strict.schema.json";
        Assert.True(f.Project.Create("fresh.json","closed.schema.json",out var why),why);
        var fresh=f.Open("fresh.json");Assert.Equal("closed.schema.json",fresh.Schema);
        Assert.False(JsonDocument.Parse(f.Disk("fresh.json")).RootElement.TryGetProperty("$schema",out _));
        Assert.Equal(2,fresh.Session!.Current.At("/amount")!.Value.GetInt32());
    }
    [Fact] public void Create_preview_is_read_only_under_a_valid_schema_mapping()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"closed.schema.json";
        Assert.True(f.Project.Create("preview.json","closed.schema.json",out _,preview:true));Assert.False(File.Exists(Path.Combine(f.Data,"preview.json")));
        Assert.Null(f.Project.Find("preview.json"));
    }
    [Fact] public void A_throwing_schema_selector_makes_create_a_refusal_before_writing()
    {
        using var f=new Files();f.Project.SchemaFor=_=>throw new InvalidOperationException("neutral selector failed");
        bool? made=null;var error=Record.Exception(()=>made=f.Project.Create("fresh.json","closed.schema.json",out _));
        Assert.Null(error);Assert.False(made);Assert.False(File.Exists(Path.Combine(f.Data,"fresh.json")));Assert.Null(f.Project.Find("fresh.json"));
    }
    [Fact] public void Create_refuses_when_the_hosts_mapping_disagrees_with_the_requested_schema()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"loose.schema.json";
        var made=f.Project.Create("fresh.json","strict.schema.json",out var why);
        output.WriteLine($"conflicting create: made={made}; opened schema={f.Project.Find("fresh.json")?.Schema}; why={why}");
        Assert.False(made);Assert.False(File.Exists(Path.Combine(f.Data,"fresh.json")));Assert.Null(f.Project.Find("fresh.json"));
    }
    [Fact] public void Create_cannot_report_success_for_a_file_the_selected_schema_cannot_open()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"absent.schema.json";
        var made=f.Project.Create("fresh.json","strict.schema.json",out var why);
        output.WriteLine($"unopenable create: made={made}; state={f.Project.Find("fresh.json")?.State}; why={why}");
        Assert.False(made);Assert.False(File.Exists(Path.Combine(f.Data,"fresh.json")));Assert.Null(f.Project.Find("fresh.json"));
    }
    [Fact] public void Rename_with_a_stable_shared_mapping_preserves_bytes_and_reopens()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";f.Open();var before=f.Disk();
        Assert.True(f.Project.Rename("alpha.json","renamed.json",out var why),why);Assert.False(File.Exists(Path.Combine(f.Data,"alpha.json")));
        var after=f.Open("renamed.json");Assert.Equal(before,f.Disk("renamed.json"));Assert.Equal("strict.schema.json",after.Schema);
    }
    [Fact] public void Rename_refuses_when_the_destination_mapping_changes_the_schema()
    {
        using var f=new Files();f.Project.SchemaFor=name=>name=="alpha.json"?"strict.schema.json":"loose.schema.json";f.Open();var before=f.Disk();
        Assert.False(f.Project.Rename("alpha.json","renamed.json",out var why));Assert.Contains("checked against",why);
        Assert.Equal(before,f.Disk());Assert.False(File.Exists(Path.Combine(f.Data,"renamed.json")));
    }
    [Fact] public void Rename_preview_leaves_a_mapped_open_file_and_its_session_intact()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";var a=f.Open();var session=a.Session;var before=f.Disk();
        Assert.True(f.Project.Rename("alpha.json","renamed.json",out _,preview:true));Assert.Same(session,a.Session);
        Assert.Equal(before,f.Disk());Assert.Null(f.Project.Find("renamed.json"));Assert.False(File.Exists(Path.Combine(f.Data,"renamed.json")));
    }
    [Fact] public void A_throwing_selector_refuses_rename_without_moving_the_source()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";var a=f.Open();var before=f.Disk();
        f.Project.SchemaFor=_=>throw new InvalidOperationException("neutral selector failed");
        bool? renamed=null;var error=Record.Exception(()=>renamed=f.Project.Rename("alpha.json","renamed.json",out _));
        Assert.Null(error);Assert.False(renamed);Assert.Same(a,f.Project.Find("alpha.json"));Assert.Equal(before,f.Disk());Assert.False(File.Exists(Path.Combine(f.Data,"renamed.json")));
    }
    [Fact] public void A_schema_choice_changing_during_rename_cannot_silently_rebind_the_open_file()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";f.Open();var before=f.Disk();var calls=0;
        f.Project.SchemaFor=_=>++calls<=2?"strict.schema.json":"absent.schema.json";
        var renamed=f.Project.Rename("alpha.json","renamed.json",out var why);
        output.WriteLine($"stateful rename: renamed={renamed}; calls={calls}; reopened state={f.Project.Find("renamed.json")?.State}; why={why}");
        if(renamed)
        {
            var reopened=f.Project.Find("renamed.json")!;Assert.NotNull(reopened.Session);Assert.Equal("strict.schema.json",reopened.Schema);
            Assert.Equal(before,f.Disk("renamed.json"));Assert.False(File.Exists(Path.Combine(f.Data,"alpha.json")));
        }
        else {Assert.Equal(before,f.Disk());Assert.False(File.Exists(Path.Combine(f.Data,"renamed.json")));}
    }
    [Fact] public void A_selector_fault_after_rename_preflight_is_contained_with_a_coherent_file()
    {
        using var f=new Files();f.Project.SchemaFor=_=>"strict.schema.json";f.Open();var before=f.Disk();var calls=0;
        f.Project.SchemaFor=_=>++calls<=2?"strict.schema.json":throw new InvalidOperationException("late selector failed");
        bool? renamed=null;var error=Record.Exception(()=>renamed=f.Project.Rename("alpha.json","renamed.json",out _));
        output.WriteLine($"late rename fault: exception={error?.GetType().Name}; renamed={renamed}; source exists={File.Exists(Path.Combine(f.Data,"alpha.json"))}; destination exists={File.Exists(Path.Combine(f.Data,"renamed.json"))}");
        Assert.Null(error);
        if(renamed==true)
        {
            var reopened=f.Project.Find("renamed.json")!;Assert.NotNull(reopened.Session);Assert.Equal("strict.schema.json",reopened.Schema);
            Assert.Equal(before,f.Disk("renamed.json"));Assert.False(File.Exists(Path.Combine(f.Data,"alpha.json")));
        }
        else {Assert.Equal(before,f.Disk());Assert.False(File.Exists(Path.Combine(f.Data,"renamed.json")));}
    }

    // The same preflight schema coherence promised by Create/Rename applies when a selector changes during creation.
    private void AssertCreateCoherence(Files f, bool? made)
    {
        if(made==true)
        {
            var created=f.Project.Find("fresh.json");Assert.NotNull(created);Assert.NotNull(created.Session);
            Assert.Equal("strict.schema.json",created.Schema);
            Assert.True(File.Exists(Path.Combine(f.Data,"fresh.json")));
        }
        else
        {
            Assert.False(File.Exists(Path.Combine(f.Data,"fresh.json")));Assert.Null(f.Project.Find("fresh.json"));
        }
    }
    [Fact] public void Create_keeps_the_preflight_schema_when_the_selector_changes_its_answer()
    {
        using var f=new Files();var calls=0;f.Project.SchemaFor=_=>++calls==1?"strict.schema.json":"loose.schema.json";
        bool? made=null;string why="";var error=Record.Exception(()=>made=f.Project.Create("fresh.json","strict.schema.json",out why));
        output.WriteLine($"stateful create: made={made}; calls={calls}; schema={f.Project.Find("fresh.json")?.Schema}; why={why}");
        Assert.Null(error);AssertCreateCoherence(f,made);
    }
    [Fact] public void Create_cannot_report_success_without_a_session_after_a_late_missing_schema()
    {
        using var f=new Files();var calls=0;f.Project.SchemaFor=_=>++calls==1?"strict.schema.json":"absent.schema.json";
        bool? made=null;string why="";var error=Record.Exception(()=>made=f.Project.Create("fresh.json","strict.schema.json",out why));
        output.WriteLine($"late missing create: made={made}; calls={calls}; state={f.Project.Find("fresh.json")?.State}; why={why}");
        Assert.Null(error);AssertCreateCoherence(f,made);
    }
    [Fact] public void Create_contains_a_late_selector_exception_and_keeps_its_preflight_choice_or_refuses()
    {
        using var f=new Files();var calls=0;f.Project.SchemaFor=_=>++calls==1?"strict.schema.json":throw new InvalidOperationException("late create selector failed");
        bool? made=null;string why="";var error=Record.Exception(()=>made=f.Project.Create("fresh.json","strict.schema.json",out why));
        output.WriteLine($"late throwing create: exception={error?.GetType().Name}; made={made}; calls={calls}; state={f.Project.Find("fresh.json")?.State}; why={why}");
        Assert.Null(error);AssertCreateCoherence(f,made);
    }

}
