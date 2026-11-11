using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2HostCommandTests
{
    internal sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("scene2-host-").FullName;
        internal readonly DataProject Project;internal readonly DocumentHost Host;internal readonly string Original;
        internal bool Blocked,AdmitSource=true;
        internal string Data=>Path.Combine(Root,"data");internal string Disk=>Path.Combine(Data,"level.json");internal string Source=>Path.Combine(Data,"prefab.json");
        internal string Guid=>new PrefabDefinitionFormat().Read(File.ReadAllText(Source)).Guid;
        internal Scene2DocumentEntry Entry=>Host.TryGetOpenScene2("level.json",out var entry)?entry!:throw new Exception("No entry");
        internal Rig(string fixture="scene-3d.example.json",bool open=true)
        {
            Directory.CreateDirectory(Data);var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
            Original=File.ReadAllText(Path.Combine(repo!.FullName,"tests/Bed.Tests/Fixtures/Scene2",fixture));File.WriteAllText(Disk,Original);
            var json=JsonNode.Parse(Original)!;File.WriteAllText(Source,json["bases"] is JsonArray {Count:>0} bases?bases[0]!["definition"]!.ToJsonString():File.ReadAllText(Path.Combine(repo.FullName,"tests/Bed.Tests/Fixtures/Scene2/prefab-definition.example.json")));File.WriteAllText(Path.Combine(schemas,"prefab.schema.json"),"{}");
            Project=DataProject.Open(Data,schemas,readOnlyFile:_=>Blocked?"fixture read-only":null,recursive:true,admit:name=>AdmitSource||name!="prefab.json");
            Host=DocumentHost.For(Project);Assert.True(Host.BindScene("level.json","scene",out _));
            if(open)Assert.True(Host.OpenScene2("level.json",out _,out var why),why);
        }
        internal static JsonElement J(string json)=>JsonSerializer.Deserialize<JsonElement>(json);
        internal Scene2EditPlan Plan(string action,string args)
        {Assert.True(Host.CaptureScene2Edit("level.json",action,J(args),out var plan,out var why),why);return plan!;}
        internal Scene2EditPlan Edit()=>Plan("set","""{"id":"frame_a_root","field":"/properties/locked","value":false}""");
        internal void ChangeSource()
        {var json=JsonNode.Parse(File.ReadAllText(Source))!;json["nodes"]![0]!["label"]="Revised saved source";File.WriteAllText(Source,json.ToJsonString());}
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void One_reserved_v2_authority_rejects_v1_data_shadow_and_close_cancel_preserves_exact_draft()
    {
        using var r=new Rig();var entry=r.Entry;Assert.True(r.Host.OpenScene2("level.json",out var reused,out _));Assert.Same(entry,reused);
        Assert.False(r.Host.OpenScene("level.json",out _,out _));Assert.False(r.Project.Find("level.json")!.Open());
        var initial=entry.Session.Current;var plan=r.Edit();Assert.Same(initial,entry.Session.Current);Assert.Empty(entry.Session.Journal);
        Assert.True(r.Host.CommitScene2Edit("level.json",plan,out var why),why);Assert.Single(entry.Session.Journal);
        Assert.False(r.Host.CloseScene2("level.json",SceneCloseDecision.Cancel,out _));Assert.Same(entry,r.Entry);
        Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(r.Host.OpenScene2("level.json",out var reopened,out why),why);
        Assert.NotSame(entry,reopened);Assert.Empty(reopened!.Session.Journal);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
    }
    [Fact]
    public void Version_one_keeps_its_original_typed_owner()
    {
        using var r=new Rig(open:false);File.WriteAllText(r.Disk,new SceneFormat().Write(Scene.Empty));
        Assert.False(r.Host.OpenScene2("level.json",out _,out _));Assert.True(r.Host.OpenScene("level.json",out var entry,out _));
        Assert.False(r.Host.OpenScene2("level.json",out _,out _));Assert.True(r.Host.TryGetOpenScene("level.json",out var again));Assert.Same(entry,again);
    }
    [Fact]
    public void Plan_commit_save_reopen_and_replay_use_one_authority()
    {
        using var r=new Rig();var session=r.Entry.Session;var before=session.Current;var plan=r.Edit();Assert.Same(before,session.Current);Assert.Empty(session.Journal);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
        Assert.True(r.Host.CommitScene2Edit("level.json",plan,out var why),why);Assert.Single(session.Current.Instances[0].Overrides);Assert.Equal(1,session.History.UndoCount);
        var replay=DocumentSession<Scene2>.ReplayFrom(session.JournalBase,JournalFile.Read(JournalFile.Write(session.Journal)),new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(session.TextToSave,replay.TextToSave);
        Assert.True(r.Host.SaveScene2("level.json",out why),why);var saved=File.ReadAllText(r.Disk);Assert.False(session.Dirty);Assert.True(session.Undo());Assert.True(session.Redo());Assert.Equal(saved,session.TextToSave);
        Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Save,out why),why);Assert.True(r.Host.OpenScene2("level.json",out var reopened,out why),why);Assert.Equal(saved,reopened!.Session.TextToSave);
    }
    [Theory][InlineData("revision")][InlineData("disk")][InlineData("reopen")][InlineData("readonly")]
    public void Stale_or_guarded_plans_never_add_a_history_step(string reason)
    {
        using var r=new Rig();var plan=r.Edit();
        if(reason=="revision")Assert.True(r.Entry.Session.Execute(Scene2Kit.Command("set",Rig.J("""{"id":"owner","field":"/properties/x","value":1}"""))));
        if(reason=="disk")File.AppendAllText(r.Disk," ");
        if(reason=="reopen"){Assert.True(r.Host.CloseScene2("level.json",SceneCloseDecision.Discard,out _));Assert.True(r.Host.OpenScene2("level.json",out _,out _));}
        if(reason=="readonly")r.Blocked=true;
        var before=r.Entry.Session.Current;var count=r.Entry.Session.Journal.Count;var disk=File.ReadAllText(r.Disk);
        Assert.False(r.Host.CommitScene2Edit("level.json",plan,out _));Assert.Same(before,r.Entry.Session.Current);Assert.Equal(count,r.Entry.Session.Journal.Count);Assert.Equal(disk,File.ReadAllText(r.Disk));
    }
    [Theory][InlineData("refuse")][InlineData("revision")][InlineData("readonly")][InlineData("policy")]
    public void Save_policy_is_checked_again_after_callbacks_and_refusal_keeps_draft(string reason)
    {
        using var r=new Rig();Assert.True(r.Host.CommitScene2Edit("level.json",r.Edit(),out _));var history=r.Entry.Session.History.UndoCount;
        r.Host.Scene2SaveCheck=e=>
        {
            if(reason=="revision")Assert.True(e.Session.Execute(Scene2Kit.Command("set",Rig.J("""{"id":"owner","field":"/properties/x","value":2}"""))));
            if(reason=="readonly")r.Blocked=true;if(reason=="policy")r.Host.Scene2SaveCheck=null;
            return reason=="refuse"?"fixture refusal":null;
        };
        Assert.False(r.Host.SaveScene2("level.json",out _));Assert.Equal(r.Original,File.ReadAllText(r.Disk));Assert.True(r.Entry.Session.Dirty);Assert.Equal(history+(reason=="revision"?1:0),r.Entry.Session.History.UndoCount);
        if(reason=="refuse")Assert.False(r.Host.CloseScene2("level.json",SceneCloseDecision.Save,out _));Assert.True(r.Host.TryGetOpenScene2("level.json",out _));
    }
    [Fact]
    public void Legacy_data_save_check_requires_typed_v2_policy()
    {
        using var r=new Rig();r.Project.SaveCheck=_=>null;Assert.True(r.Host.CommitScene2Edit("level.json",r.Edit(),out _));
        Assert.False(r.Host.SaveScene2("level.json",out var why));Assert.Contains("explicit",why);r.Host.Scene2SaveCheck=_=>null;Assert.True(r.Host.SaveScene2("level.json",out why),why);
    }
    [Theory][InlineData("bytes")][InlineData("duplicate")][InlineData("admission")][InlineData("dirty")]
    public void Captured_source_freshness_and_GUID_uniqueness_are_rechecked_before_commit(string reason)
    {
        using var r=new Rig();var plan=r.Plan("update","""{"instance":"22222222222222222222222222222222"}""");
        if(reason=="bytes")File.WriteAllText(r.Source,"\ufeff"+File.ReadAllText(r.Source),new System.Text.UTF8Encoding(false));
        if(reason=="duplicate")File.Copy(r.Source,Path.Combine(r.Data,"duplicate.json"));
        if(reason=="admission")r.AdmitSource=false;
        if(reason=="dirty"){var source=r.Project.Find("prefab.json")!;Assert.True(source.Open());Assert.True(source.Session!.Execute(new SetValue("/nodes/0/label",JsonSerializer.SerializeToElement("Unsaved"))));}
        var before=r.Entry.Session.Current;Assert.False(r.Host.CommitScene2Edit("level.json",plan,out _));Assert.Same(before,r.Entry.Session.Current);Assert.Empty(r.Entry.Session.Journal);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
    }
    [Fact]
    public void Saved_source_update_keeps_local_override_and_replay_never_reads_disk()
    {
        using var r=new Rig();Assert.True(r.Host.CommitScene2Edit("level.json",r.Edit(),out _));r.ChangeSource();
        var plan=r.Plan("update","""{"instance":"22222222222222222222222222222222"}""");Assert.True(r.Host.CommitScene2Edit("level.json",plan,out var why),why);File.Delete(r.Source);
        Assert.Single(r.Entry.Session.Current.Instances[0].Overrides);var s=r.Entry.Session;
        var replay=DocumentSession<Scene2>.ReplayFrom(s.JournalBase,s.Journal,new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());Assert.Equal(s.TextToSave,replay.TextToSave);
    }
    [Fact]
    public void Instantiate_discovers_only_GUID_sources_allocates_once_and_refuses_arbitrary_definition_payloads()
    {
        using var r=new Rig();var root=JsonNode.Parse(r.Original)!;var placement=root["instances"]![0]!["placement"]!.DeepClone();placement["id"]="second";
        var args=new JsonObject{{"source",r.Guid},{"placement",placement},{"bindings",root["instances"]![0]!["bindings"]!.DeepClone()}};
        Assert.True(r.Host.CaptureScene2Edit("level.json","instantiate",Rig.J(args.ToJsonString()),out var plan,out var why),why);var candidate=new Scene2Format().Write(plan!.Candidate);
        Assert.Equal(2,plan.Candidate.Instances.Length);Assert.True(r.Host.CommitScene2Edit("level.json",plan,out why),why);Assert.Equal(candidate,r.Entry.Session.TextToSave);
        Assert.False(r.Host.CaptureScene2Edit("level.json","instantiate",Rig.J("""{"source":"../outside.json","placement":{}}"""),out _,out _));
        args["definition"]=root["bases"]![0]!["definition"]!.DeepClone();Assert.False(r.Host.CaptureScene2Edit("level.json","instantiate",Rig.J(args.ToJsonString()),out _,out _));
    }
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public void Even_legacy_flat_projects_refuse_reparse_paths_before_v2_read_or_save(bool readOnly,bool saving)
    {
        using var r=new Rig(open:false);var project=DataProject.Open(r.Data,Path.Combine(r.Root,"schemas"),readOnly:readOnly);var host=DocumentHost.For(project);Assert.True(host.BindScene("level.json","scene",out _));
        if(saving)
        {
            Assert.True(host.OpenScene2("level.json",out _,out _));Assert.True(host.CaptureScene2Edit("level.json","set",Rig.J("""{"id":"owner","field":"/properties/n","value":1}"""),out var plan,out _));Assert.True(host.CommitScene2Edit("level.json",plan!,out _));
        }
        var held=Path.Combine(r.Root,"held");var prefix=Path.GetFullPath(r.Root)+Path.DirectorySeparatorChar;
        Assert.StartsWith(prefix,Path.GetFullPath(r.Data),StringComparison.OrdinalIgnoreCase);Assert.StartsWith(prefix,Path.GetFullPath(held),StringComparison.OrdinalIgnoreCase);
        Directory.Move(r.Data,held);var linked=false;
        try
        {
            if(OperatingSystem.IsWindows())
            {
                var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var arg in new[]{"/d","/c","mklink","/J",r.Data,held})start.ArgumentList.Add(arg);
                using var child=System.Diagnostics.Process.Start(start)!;var message=child.StandardOutput.ReadToEnd()+child.StandardError.ReadToEnd();Assert.True(child.WaitForExit(15000));Assert.True(child.ExitCode==0,message);
            }
            else Directory.CreateSymbolicLink(r.Data,held);
            linked=true;
            var ok=saving?host.SaveScene2("level.json",out var why):host.OpenScene2("level.json",out _,out why);
            Assert.False(ok);Assert.Contains("junction",why,StringComparison.OrdinalIgnoreCase);Assert.Equal(r.Original,File.ReadAllText(Path.Combine(held,"level.json")));
        }
        finally{if(linked){Assert.StartsWith(prefix,Path.GetFullPath(r.Data),StringComparison.OrdinalIgnoreCase);Directory.Delete(r.Data);}}
    }

    private sealed class Preview:IPlaySession
    {public string Status=>"Fixture playing.";public void Draw(){}public void Dispose(){}}
    [Fact]
    public void Active_play_blocks_capture_commit_and_save_until_stopped()
    {
        using var r=new Rig();var plan=r.Edit();var editor=r.Entry.Editor;editor.PlayRefusal=null;
        editor.Add(new PlayMode<Scene2>("fixture","Fixture",_=>new Preview()));Assert.True(editor.Play("fixture"));
        Assert.False(r.Host.CommitScene2Edit("level.json",plan,out _));Assert.False(r.Host.CaptureScene2Edit("level.json","set",Rig.J("""{"id":"owner","field":"/properties/n","value":1}"""),out _,out _));Assert.False(r.Host.SaveScene2("level.json",out _));
        Assert.Empty(editor.Session.Journal);Assert.Equal(r.Original,File.ReadAllText(r.Disk));Assert.True(editor.Stop());Assert.True(r.Host.CommitScene2Edit("level.json",plan,out var why),why);
    }
    [Theory]
    [InlineData("group","""{"ids":["owner"],"id":null}""")]
    [InlineData("update","""{"instance":"22222222222222222222222222222222","baseId":null}""")]
    [InlineData("instantiate","""{"source":null,"placement":{}}""")]
    public void Explicit_null_host_arguments_are_not_silently_replaced_by_generated_defaults(string action,string args)
    {
        using var r=new Rig();Assert.False(r.Host.CaptureScene2Edit("level.json",action,Rig.J(args),out _,out _));Assert.Empty(r.Entry.Session.Journal);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
    }

    [Fact]
    public void Shared_access_check_ignores_readonly_and_typed_diagnostics_but_rechecks_admission()
    {
        using var r=new Rig();Assert.False(r.Project.Find("level.json")!.Open());Assert.Null(r.Project.FileAccessRefusal("level.json"));
        r.Blocked=true;Assert.Null(r.Project.FileAccessRefusal("level.json"));Assert.NotNull(r.Project.ReadOnlyReason("level.json"));
        r.AdmitSource=false;Assert.NotNull(r.Project.FileAccessRefusal("prefab.json"));Assert.Equal(r.Original,File.ReadAllText(r.Disk));
        var readOnly=DataProject.Open(r.Data,Path.Combine(r.Root,"schemas"),readOnly:true,recursive:true);Assert.Null(readOnly.FileAccessRefusal("level.json"));Assert.NotNull(readOnly.ReadOnlyReason("level.json"));
    }

}
