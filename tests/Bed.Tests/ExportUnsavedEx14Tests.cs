using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ExportUnsavedEx14Tests
{
    private sealed class Operation:IGameBuildOperation
    {
        public Task<GameBuildReceipt> Completion {get;}=new TaskCompletionSource<GameBuildReceipt>().Task;
        public GameContentManifest? OutputManifest=>null;
        public ImmutableArray<GameBuildLog> DrainLogs()=>[];
        public void Cancel(){}
        public void Dispose(){}
    }
    private sealed class ExportHost:IGameExportHost
    {
        public readonly List<string> Inputs=[];
        public IGameBuildOperation Start(GameBuildSpec spec,string root,bool dirty,GameExportPlan plan)
        {Assert.False(dirty);Inputs.Add(Encoding.UTF8.GetString(plan.Admission.SavedInput.CopyPayload()));return new Operation();}
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken token)=>throw new InvalidOperationException("No fake verification.");
    }
    private sealed class Playing:IProjectPlaySession,IProjectPlayHost
    {
        public bool CanBuildAndPlay=>false;public bool HasCompleted {get;private set;}=true;public int? ExitCode=>null;
        public string Status=>HasCompleted?"Stopped.":"Running.";
        public IProjectPlaySession Start(PlayLaunch launch)=>this;
        public bool Start(bool dirty,bool buildAndPlay=false){Assert.False(dirty);HasCompleted=false;return true;}
        public void RequestStop()=>HasCompleted=true;public void Update(){}public void Draw(){}public void Dispose()=>HasCompleted=true;
        public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
    }
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("export-unsaved-ex14-").FullName;
        public readonly HeadlessHost Host=new(1250,950){Drawing=false};
        public readonly DataWorkspace Workspace;public readonly ExportHost Export=new();
        public readonly string FileName,Disk,Original;public readonly bool Scene;
        public Rig(bool scene=true)
        {
            Scene=scene;FileName=scene?"level.json":"item.json";
            var data=Directory.CreateDirectory(Path.Combine(Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(Root,"schemas")).FullName;
            File.WriteAllText(Path.Combine(schemas,"item.schema.json"),"{\"type\":\"object\"}");
            Disk=Path.Combine(data,FileName);Original=scene?new SceneFormat().Write(new Scene([SceneObject.At("box",null,Vector3.Zero)])):"{\"value\":0}";
            File.WriteAllText(Disk,Original);Workspace=new(DataProject.Open(data,schemas));
            if(scene)Assert.True(Workspace.Documents.BindScene(FileName,"scene",out _));
            Assert.True(Workspace.Show(FileName));
            var declaration=new GameExportDeclaration(input=>new("fixture",Encoding.UTF8.GetBytes(input.Text)),saved=>CapturedExportTests.Plan(saved,false),(root,_)=>new(Path.Combine(root,"game.exe"),root,[]));
            Workspace.BindExport(new(Path.Combine(Root,"build.exe"),Root,["{input}","{output}"]),declaration,Export);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());EditorWindow<Scene>.Configure(ImGui.GetIO());Host.Frames(Workspace.Draw,4);
        }
        public bool Dirty=>Scene?Workspace.SceneWindow!.Editor.Session.Dirty:Workspace.Project.Find(FileName)!.Dirty;
        public void Edit(double value=3)
        {
            if(Scene){if(!Workspace.SceneWindow!.Editor.Selection.Contains("box"))Assert.True(Workspace.SceneWindow.Editor.Select("box"));Assert.True(Workspace.SceneWindow.Inspector!.Set("position.x",value));}
            else Assert.True(Workspace.Project.Find(FileName)!.Session!.Execute(new SetValue("/value",JsonSerializer.SerializeToElement(value))));
        }
        public void Key(ImGuiKey key,bool alt=false){Host.Press(Workspace.Draw,key,alt:alt);Host.Frames(Workspace.Draw,2);}
        public bool Window(string label)=>Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Window&&n.Label==label);
        public void Choose(string label)
        {
            for(var i=0;i<16;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label==label&&n.Focused)){Key(ImGuiKey.Enter);return;}
                Key(ImGuiKey.Tab);
            }
            Assert.Fail("Keyboard did not focus "+label+"\n"+Ui.Snapshot().Text);
        }
        public void OpenExport()
        {
            Key(ImGuiKey.F,alt:true);
            for(var i=0;i<24;i++)
            {
                if(Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.MenuItem&&n.Label=="Export build..."&&n.Focused)){Key(ImGuiKey.Enter);return;}
                Key(ImGuiKey.DownArrow);
            }
            Assert.Fail("Keyboard did not reach Export build.");
        }
        public void StartPlay()
        {
            File.WriteAllText(Path.Combine(Root,"game.exe"),"inert self-authored declaration");Workspace.BindPlaySettings(Root,new("game.exe"),Path.Combine(Root,"local"));Workspace.BindProjectPlay(new Playing());
            Assert.True(Workspace.RequestProjectPlay());
        }
        public void Dispose(){Workspace.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory]
    [InlineData(true,"Save")][InlineData(true,"Don't Save")][InlineData(true,"Cancel")][InlineData(true,"Escape")]
    [InlineData(false,"Save")][InlineData(false,"Don't Save")][InlineData(false,"Cancel")]
    public void Keyboard_export_decision_saves_or_uses_saved_bytes_without_discarding_draft(bool scene,string choice)
    {
        using var r=new Rig(scene);r.Edit();r.OpenExport();Assert.True(r.Window("Export saved files"));Assert.Empty(r.Export.Inputs);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Don't Save exports the saved version and keeps your edits open.");
        if(choice=="Escape")r.Key(ImGuiKey.Escape);else r.Choose(choice);
        Assert.False(r.Window("Export saved files"));
        if(choice is "Cancel" or "Escape"){Assert.False(r.Window("Export build"));Assert.True(r.Dirty);Assert.Equal(r.Original,File.ReadAllText(r.Disk));Assert.Empty(r.Export.Inputs);return;}
        Assert.True(r.Window("Export build"));r.Choose("Build saved files");var input=Assert.Single(r.Export.Inputs);
        Assert.Equal(File.ReadAllText(r.Disk),input);Assert.Equal(choice=="Don't Save",r.Dirty);
        if(choice=="Don't Save")Assert.Equal(r.Original,input);else Assert.NotEqual(r.Original,input);
    }
    [Fact] public void Refused_scene_save_keeps_prompt_draft_and_disk_without_export()
    {
        using var r=new Rig();r.Edit();var revision=r.Workspace.SceneWindow!.Editor.Session.Revision;
        r.Workspace.Documents.SceneSaveCheck=_=>"Synthetic save refused.";r.OpenExport();r.Choose("Save");
        Assert.True(r.Window("Export saved files"));Assert.True(r.Dirty);Assert.Equal(revision,r.Workspace.SceneWindow.Editor.Session.Revision);
        Assert.Equal(r.Original,File.ReadAllText(r.Disk));Assert.Empty(r.Export.Inputs);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Synthetic save refused.");
    }
    [Fact] public void Changed_draft_cannot_consume_an_old_DontSave_decision()
    {
        using var r=new Rig();r.Edit();r.OpenExport();r.Edit(4);r.Choose("Don't Save");
        Assert.False(r.Window("Export saved files"));Assert.False(r.Window("Export build"));Assert.Empty(r.Export.Inputs);Assert.True(r.Dirty);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
        Assert.Contains("Documents changed",r.Workspace.Said);
    }
    [Fact] public void Editing_after_DontSave_requires_a_new_export_decision()
    {
        using var r=new Rig();r.Edit();r.OpenExport();r.Choose("Don't Save");r.Edit(4);r.Choose("Build saved files");
        Assert.Empty(r.Export.Inputs);Assert.True(r.Dirty);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("Save changes before building.",StringComparison.Ordinal));
    }
    [Fact] public void External_saved_file_change_is_never_exported_under_an_old_decision()
    {
        using var r=new Rig();r.Edit();r.OpenExport();r.Choose("Don't Save");File.AppendAllText(r.Disk," ");r.Choose("Build saved files");
        Assert.Empty(r.Export.Inputs);Assert.True(r.Dirty);Assert.Equal(r.Original+" ",File.ReadAllText(r.Disk));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("changed outside BED",StringComparison.Ordinal));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Running_Play_refuses_export_at_menu_or_if_it_starts_after_the_dialog_opens(bool alreadyOpen)
    {
        using var r=new Rig();if(alreadyOpen)r.OpenExport();r.StartPlay();
        if(alreadyOpen)r.Choose("Build saved files");else r.OpenExport();
        Assert.Empty(r.Export.Inputs);Assert.Equal(r.Original,File.ReadAllText(r.Disk));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label.Contains("Stop Play before exporting a build.",StringComparison.Ordinal));
    }
}
