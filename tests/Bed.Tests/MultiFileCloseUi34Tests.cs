using System.Numerics;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class MultiFileCloseUi34Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("multi-close-ui34-").FullName;
        public readonly ProjectLauncher Launcher;public readonly HeadlessHost Host;
        public DataWorkspace Workspace=>Launcher.Workspace!;public string Data=>Path.Combine(Root,"data");
        public readonly DocumentSession<DataDocument> A,B;public readonly string? SceneOriginal;
        public Rig(bool scene=false,int width=1400,float scale=1,string[]? names=null)
        {
            names??=["a","b"];Directory.CreateDirectory(Data);var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            foreach(var name in names){File.WriteAllText(Path.Combine(Data,name+".json"),JsonSerializer.Serialize(new{value=1}));File.WriteAllText(Path.Combine(schemas,name+".schema.json"),"""{"type":"object","properties":{"value":{"type":"number"}}}""");}
            if(scene){SceneOriginal=new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)]));File.WriteAllText(Path.Combine(Data,"level.json"),SceneOriginal);}
            Host=new(width,1000){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetStyle().FontScaleMain=scale;
            Launcher=new(new RecentProjects(Path.Combine(Root,"recent.json")));Assert.True(Launcher.Open(new(Data,schemas)));
            Assert.True(Workspace.Show(names[0]+".json"));A=Workspace.Active!.Session!;Assert.True(A.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(2))));
            Assert.True(Workspace.Show(names[1]+".json"));B=Workspace.Active!.Session!;Assert.True(B.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(3))));
            if(scene){Assert.True(Workspace.Documents.BindScene("level.json","scene",out _));Assert.True(Workspace.Show("level.json"));Assert.True(Workspace.SceneWindow!.Editor.Session.Execute(new MoveObject("box",Vector3.UnitX)));}
            Host.Frames(Launcher.Draw,4);
        }
        public void Close(){Assert.False(Launcher.Close());Assert.True(Launcher.SaveChangesPending);Host.Frames(Launcher.Draw,4);}
        public UiNode Button(string label)
        {
            var tree=Ui.Snapshot();bool InPrompt(UiNode node){for(var at=node.Parent;at>=0;at=tree.Nodes[at].Parent)if(tree.Nodes[at].Role==UiRole.Window&&tree.Nodes[at].Label=="Save changes?")return true;return false;}
            return Assert.Single(tree.Nodes,n=>n.Role==UiRole.Button&&n.Label==label&&InPrompt(n));
        }
        public void Click(string label){Host.Frames(Launcher.Draw,2);var node=Button(label);Assert.True(node.Enabled);Assert.False(node.Bounds.IsEmpty);Host.Click(Launcher.Draw,node.Bounds.X+node.Bounds.Width/2,node.Bounds.Y+node.Bounds.Height/2);Host.Frames(Launcher.Draw,2);}
        public int Value(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(Data,name+".json"))).RootElement.GetProperty("value").GetInt32();
        public void Dispose(){Launcher.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Fact]
    public void Per_file_keyboard_choices_keep_all_drafts_open_until_mixed_data_scene_choices_finish()
    {
        using var r=new Rig(scene:true);var workspace=r.Workspace;Assert.True(workspace.Documents.TryGetOpenScene("level.json",out var scene));r.Close();
        Assert.True(r.Button("Save a.json").Focused);r.Host.Press(r.Launcher.Draw,ImGuiKey.Tab);Assert.True(r.Button("Don't save a.json").Focused);r.Host.Press(r.Launcher.Draw,ImGuiKey.Enter);r.Host.Frames(r.Launcher.Draw,2);
        Assert.Same(workspace,r.Launcher.Workspace);Assert.Same(r.A,workspace.Project.Find("a.json")!.Session);Assert.True(r.A.Dirty);Assert.Equal(1,r.Value("a"));
        r.Click("Save b.json");Assert.Same(workspace,r.Launcher.Workspace);Assert.Same(r.B,workspace.Project.Find("b.json")!.Session);Assert.False(r.B.Dirty);Assert.Equal(3,r.Value("b"));Assert.Same(scene!.Session,workspace.SceneWindow!.Editor.Session);
        r.Click("Don't save level.json");Assert.Null(r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.Equal(1,r.Value("a"));Assert.Equal(3,r.Value("b"));Assert.Equal(r.SceneOriginal,File.ReadAllText(Path.Combine(r.Data,"level.json")));
    }
    [Fact]
    public void Refused_row_save_names_the_file_and_leaves_every_authority_open_for_retry_or_discard()
    {
        using var r=new Rig();var workspace=r.Workspace;workspace.Project.SaveCheck=f=>f.Name=="a.json"?"a.json fixture refusal":null;r.Close();r.Click("Save a.json");
        Assert.True(r.Launcher.SaveChangesPending);Assert.Same(workspace,r.Launcher.Workspace);Assert.Same(r.A,workspace.Project.Find("a.json")!.Session);Assert.Same(r.B,workspace.Project.Find("b.json")!.Session);Assert.True(r.A.Dirty);Assert.True(r.B.Dirty);Assert.Contains("a.json",r.Launcher.Said);Assert.Equal(1,r.Value("a"));Assert.Equal(1,r.Value("b"));
        r.Click("Don't save a.json");Assert.NotNull(r.Launcher.Workspace);r.Click("Save b.json");Assert.Null(r.Launcher.Workspace);Assert.Equal(1,r.Value("a"));Assert.Equal(3,r.Value("b"));
    }
    [Fact]
    public void Save_all_keeps_existing_checked_group_policy_and_cancel_drops_no_remaining_draft()
    {
        using var r=new Rig();var workspace=r.Workspace;workspace.Project.SaveCheck=f=>f.Name=="b.json"?"b refused":null;r.Close();r.Click("Save all");
        Assert.Same(workspace,r.Launcher.Workspace);Assert.True(r.Launcher.SaveChangesPending);Assert.Equal(2,r.Value("a"));Assert.Equal(1,r.Value("b"));Assert.False(r.A.Dirty);Assert.True(r.B.Dirty);
        r.Click("Cancel");Assert.Same(workspace,r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.False(r.A.Dirty);Assert.True(r.B.Dirty);
        workspace.Project.SaveCheck=null;r.Close();r.Click("Save");Assert.Null(r.Launcher.Workspace);Assert.Equal(2,r.Value("a"));Assert.Equal(3,r.Value("b"));
    }
    [Fact]
    public void A_later_edit_invalidates_its_prior_dont_save_choice_and_never_closes_early()
    {
        using var r=new Rig();r.Close();r.Click("Don't save a.json");Assert.True(r.A.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(9))));
        r.Click("Don't save b.json");Assert.NotNull(r.Launcher.Workspace);Assert.True(r.Launcher.SaveChangesPending);Assert.True(r.A.Dirty);Assert.Equal(1,r.Value("a"));
        Assert.Contains("changed",r.Launcher.Said);r.Click("Don't save a.json");Assert.Null(r.Launcher.Workspace);Assert.Equal(1,r.Value("a"));
    }
    [Theory][InlineData(1400,1f)][InlineData(1024,2f)]
    public void Every_file_choice_save_all_and_cancel_is_named_and_keyboard_reachable(int width,float scale)
    {
        using var r=new Rig(width:width,scale:scale);r.Close();var labels=new[]{"Save a.json","Don't save a.json","Save b.json","Don't save b.json","Save all","Cancel"};var seen=new HashSet<string>();
        for(var i=0;i<12;i++)
        {
            foreach(var label in labels){var node=r.Button(label);Assert.True(node.Enabled);Assert.False(node.Bounds.IsEmpty);if(node.Focused)seen.Add(label);}
            if(seen.Count==labels.Length)break;r.Host.Press(r.Launcher.Draw,ImGuiKey.Tab);
        }
        Assert.Equal(labels.Order(),seen.Order());r.Host.Press(r.Launcher.Draw,ImGuiKey.Escape);r.Host.Frames(r.Launcher.Draw,2);Assert.NotNull(r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.True(r.A.Dirty);Assert.True(r.B.Dirty);
    }
    [Fact]
    public void Save_callback_creating_a_new_edit_cannot_authorize_its_silent_discard()
    {
        using var r=new Rig();var workspace=r.Workspace;r.Close();r.Click("Don't save a.json");workspace.Project.SaveCheck=f=>{if(f.Name=="b.json")Assert.True(r.A.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(8))));return null;};
        r.Click("Save b.json");Assert.NotNull(r.Launcher.Workspace);Assert.True(r.Launcher.SaveChangesPending);Assert.True(r.A.Dirty);Assert.Equal(1,r.Value("a"));Assert.Equal(3,r.Value("b"));r.Click("Don't save a.json");Assert.Null(r.Launcher.Workspace);
    }
    [Fact]
    public void Background_save_keeps_the_dialog_and_other_drafts_until_checked_write_finishes()
    {
        using var r=new Rig();using var release=new ManualResetEventSlim();var workspace=r.Workspace;workspace.BackgroundSaves=true;workspace.Project.SaveCheck=_=>{Assert.True(release.Wait(TimeSpan.FromSeconds(15)));return null;};
        try
        {
            r.Close();r.Click("Save a.json");Assert.True(workspace.SavePending("a.json"));r.Click("Don't save b.json");Assert.Same(workspace,r.Launcher.Workspace);Assert.True(r.Launcher.SaveChangesPending);Assert.True(r.A.Dirty);Assert.Equal(1,r.Value("a"));release.Set();
            var guard=System.Diagnostics.Stopwatch.StartNew();while(r.Launcher.SaveChangesPending&&guard.Elapsed<TimeSpan.FromSeconds(15)){r.Host.Frames(r.Launcher.Draw,1);Thread.Sleep(5);}
            Assert.False(r.Launcher.SaveChangesPending);Assert.Null(r.Launcher.Workspace);Assert.Equal(2,r.Value("a"));Assert.Equal(1,r.Value("b"));
        }
        finally{release.Set();}
    }
    [Fact]
    public void An_answer_from_a_stale_file_row_is_refused_before_discarding_the_new_revision()
    {
        using var r=new Rig();r.Close();var workspace=r.Workspace;Assert.True(r.B.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(8))));
        var answer=typeof(ProjectLauncher).GetMethod("AnswerFileSaveChanges",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Assert.NotNull(answer);
        Assert.False((bool)answer.Invoke(r.Launcher,["b.json",SaveChangesChoice.DontSave])!);Assert.Same(workspace,r.Launcher.Workspace);Assert.True(r.Launcher.SaveChangesPending);Assert.True(r.B.Dirty);Assert.Contains("changed",r.Launcher.Said);Assert.Equal(1,r.Value("b"));
        r.Host.Frames(r.Launcher.Draw,3);r.Click("Don't save a.json");r.Click("Don't save b.json");Assert.Null(r.Launcher.Workspace);Assert.Equal(1,r.Value("b"));
    }

    [Fact]
    public void A_delayed_global_answer_cannot_close_a_replacement_project()
    {
        using var r=new Rig();r.Close();Assert.True(r.Workspace.Project.Find("a.json")!.Save());Assert.True(r.Workspace.Project.Find("b.json")!.Save());
        var data=Directory.CreateDirectory(Path.Combine(r.Root,"replacement","data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(r.Root,"replacement","schemas")).FullName;
        File.WriteAllText(Path.Combine(data,"c.json"),JsonSerializer.Serialize(new{value=1}));File.WriteAllText(Path.Combine(schemas,"c.schema.json"),"{}");
        Assert.True(r.Launcher.Open(new(data,schemas)));var replacement=r.Workspace;
        Assert.False(r.Launcher.AnswerSaveChanges(SaveChangesChoice.DontSave));Assert.Same(replacement,r.Launcher.Workspace);Assert.False(r.Launcher.SaveChangesPending);Assert.Contains("project changed",r.Launcher.Said,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mixed_Save_all_checks_background_data_then_commits_data_and_scene_as_one_group_without_another_click()
    {
        using var r=new Rig(scene:true);using var release=new ManualResetEventSlim();var workspace=r.Workspace;var checks=0;var sceneChecks=0;
        workspace.BackgroundSaves=true;workspace.Project.SaveCheck=_=>{Assert.True(release.Wait(TimeSpan.FromSeconds(15)));Interlocked.Increment(ref checks);return null;};workspace.Documents.SceneSaveCheck=_=>{sceneChecks++;return null;};
        try
        {
            r.Close();r.Click("Save all");Assert.True(workspace.SavePending("a.json"));Assert.Same(workspace,r.Launcher.Workspace);Assert.True(r.A.Dirty);Assert.True(r.B.Dirty);Assert.Equal(1,r.Value("a"));Assert.Equal(1,r.Value("b"));Assert.Equal(r.SceneOriginal,File.ReadAllText(Path.Combine(r.Data,"level.json")));release.Set();
            var guard=System.Diagnostics.Stopwatch.StartNew();while(r.Launcher.SaveChangesPending&&guard.Elapsed<TimeSpan.FromSeconds(15)){r.Host.Frames(r.Launcher.Draw,1);Thread.Sleep(5);}
            Assert.False(r.Launcher.SaveChangesPending);Assert.Null(r.Launcher.Workspace);Assert.Equal(2,r.Value("a"));Assert.Equal(3,r.Value("b"));Assert.Equal(1,new SceneFormat().Read(File.ReadAllText(Path.Combine(r.Data,"level.json"))).Find("box")!.Position.X);Assert.Equal(2,checks);Assert.Equal(1,sceneChecks);
        }
        finally{release.Set();}
    }

    private sealed class Preview:IPlaySession
    {public int Disposals;public string Status=>"Fixture preview.";public void Draw(){}public void Dispose()=>Disposals++;}
    [Fact]
    public void Typed_preview_stays_owned_until_every_choice_succeeds_then_stops_once()
    {
        using var r=new Rig(scene:true);var workspace=r.Workspace;var editor=workspace.SceneWindow!.Editor;var preview=new Preview();editor.PlayRefusal=null;editor.Add(new PlayMode<Scene>("fixture","Preview",_=>preview));Assert.True(editor.Play("fixture"));
        r.Close();r.Click("Don't save a.json");r.Click("Don't save b.json");Assert.Same(workspace,r.Launcher.Workspace);Assert.Equal(0,preview.Disposals);Assert.NotNull(editor.Playing);
        r.Click("Don't save level.json");Assert.Null(r.Launcher.Workspace);Assert.Equal(1,preview.Disposals);Assert.Null(editor.Playing);Assert.Equal(r.SceneOriginal,File.ReadAllText(Path.Combine(r.Data,"level.json")));
    }

    [Fact]
    public void Filenames_containing_native_ID_markers_keep_distinct_save_and_discard_actions()
    {
        using var r=new Rig(names:["a###same","b###same"]);r.Close();
        var names=new[]{"Save a###same.json","Don't save a###same.json","Save b###same.json","Don't save b###same.json"};var buttons=names.Select(r.Button).ToArray();Assert.Equal(4,buttons.Select(b=>b.Id).Distinct().Count());
        r.Click("Save b###same.json");Assert.NotNull(r.Launcher.Workspace);Assert.Equal(1,r.Value("a###same"));Assert.Equal(3,r.Value("b###same"));
        r.Click("Don't save a###same.json");Assert.Null(r.Launcher.Workspace);Assert.Equal(1,r.Value("a###same"));
    }

}
