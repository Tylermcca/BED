using System.Text.Json;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;
public sealed partial class DataWorkspace
{
    private IProjectPlayHost? projectPlayHost;
    private IProjectPlaySession? projectPlay;
    private PlayLaunch? pendingLaunch;
    private bool pendingBuild,playSavePrompt,playApprovalPrompt,playFocus;
    private Func<bool>? playCurrent;
    /// <summary>Project Play configuration, available for plain projects as well as adapters.</summary>
    public PlaySettingsForm? PlaySettings {get;private set;}
    /// <summary>The current owned process session, if one has been created.</summary>
    public IProjectPlaySession? ProjectPlay=>projectPlay;
    /// <summary>Whether the saved-file decision is pending.</summary>
    public bool ProjectPlaySavePending=>playSavePrompt;
    /// <summary>Whether exact local command approval is pending.</summary>
    public bool ProjectPlayApprovalPending=>playApprovalPrompt;
    /// <summary>Binds project settings without starting a process or changing any document.</summary>
    public void BindPlaySettings(string root,global::Bed.Editor.PlaySettings? defaults,string localPreferences,Func<IFileDialogs?>? dialogs=null)
    { statusProjectRoot=root;reportDialogs=dialogs;reportPreferences=localPreferences;PlaySettings=new(root,defaults,localPreferences,dialogs);BindSceneViewPreferences(localPreferences); }
    /// <summary>Binds the common factory; no settings are approved and no process starts.</summary>
    public void BindProjectPlay(IProjectPlayHost host){ArgumentNullException.ThrowIfNull(host);projectPlayHost=host;}

    private bool ProjectDirty()=>TransitionDraftCount>0;
    private Func<bool> CapturePlayDocuments()
    {
        var names=Project.Files.Select(f=>f.Name).ToArray();
        var data=Project.Files.Where(f=>!Documents.IsScene(f.Name)).Select(f=>(File:f,Session:f.Session,Revision:f.Session?.Revision,Saved:f.Session?.ExpectedOnDisk)).ToArray();
        var scenes=Project.Files.Select(f=>Documents.TryGetOpenScene(f.Name,out var s)?(Name:f.Name,Entry:s):default).Where(p=>p.Entry is not null)
            .Select(p=>(p.Name,Entry:p.Entry!,Revision:p.Entry!.Session.Revision,Saved:p.Entry.Session.ExpectedOnDisk)).ToArray();
        var v2=Project.Files.Select(f=>Documents.TryGetOpenScene2(f.Name,out var s)?(Name:f.Name,Entry:s):default).Where(p=>p.Entry is not null)
            .Select(p=>(p.Name,Entry:p.Entry!,Revision:p.Entry!.Session.Revision,Saved:p.Entry.Session.ExpectedOnDisk)).ToArray();
        return ()=>names.SequenceEqual(Project.Files.Select(f=>f.Name))
            &&data.All(p=>ReferenceEquals(p.File.Session,p.Session)&&p.File.Session?.Revision==p.Revision&&p.File.Session?.ExpectedOnDisk==p.Saved)
            &&scenes.All(p=>Documents.TryGetOpenScene(p.Name,out var s)&&ReferenceEquals(s,p.Entry)&&s!.Session.Revision==p.Revision&&s.Session.ExpectedOnDisk==p.Saved)
            &&v2.Length==Project.Files.Count(f=>Documents.TryGetOpenScene2(f.Name,out _))
            &&v2.All(p=>Documents.TryGetOpenScene2(p.Name,out var s)&&ReferenceEquals(s,p.Entry)&&s!.Session.Revision==p.Revision&&s.Session.ExpectedOnDisk==p.Saved);
    }
    private bool SavedPlayInputs()
    {
        if(Project.Judging){Said="A save check is still running; try Play after it finishes.";return false;}
        PickUpOutsideChanges();   // a level an AI assistant just changed, with no unsaved edits here, is loaded and played as saved
        foreach(var file in Project.Files)
        {
            if(Documents.TryGetOpenScene(file.Name,out var scene))
            {if(scene!.Session.ExpectedOnDisk is null||scene.Session.CheckDisk()!=DiskState.Unchanged){Said=$"{file.Name} was changed outside BED and you have unsaved changes to it; choose which version to keep in the Changed outside BED box, then Play.";return false;}}
            else if(Documents.TryGetOpenScene2(file.Name,out var v2))
            {if(v2!.Session.ExpectedOnDisk is null||v2.Session.CheckDisk()!=DiskState.Unchanged){Said=$"{file.Name} was changed outside BED and you have unsaved changes to it; choose which version to keep in the Changed outside BED box, then Play.";return false;}}
            else if(file.Session is {} session&&(session.ExpectedOnDisk is null||session.CheckDisk()!=DiskState.Unchanged))
            {Said=$"{file.Name} was changed outside BED and you have unsaved changes to it; choose which version to keep in the Changed outside BED box, then Play.";return false;}
        }
        return true;
    }
    /// <summary>Requests the same saved-files launch from toolbar, palette or host. Dirty drafts require a decision.</summary>
    public bool RequestProjectPlay(bool buildAndPlay=false)
    {
        if(projectPlayHost is null||PlaySettings is null){Said="No project Play host is available.";return false;}
        if(projectPlay is {HasCompleted:false}){Said="Stop the running program first.";return false;}
        if(!PlaySettings.TryLaunch(out var launch,out var why))
        {PlaySettings.Open=true;Said=why=="Choose a program in Play settings."?"Set the program in Tools > Play settings":why!;return false;}
        pendingLaunch=launch;pendingBuild=buildAndPlay;playCurrent=CapturePlayDocuments();playApprovalPrompt=false;
        if(ProjectDirty()){playSavePrompt=true;playFocus=true;Said="Play runs saved files. Save changes before Play?";return true;}
        playSavePrompt=false;return ContinueProjectPlay();
    }
    /// <summary>Resolves the captured save decision. Failed saves retain drafts and never start a process.</summary>
    public bool AnswerProjectPlay(SaveChangesChoice choice)
    {
        if(!playSavePrompt)return false;
        if(choice==SaveChangesChoice.Cancel){CancelProjectPlay();return false;}
        if(playCurrent?.Invoke()!=true){CancelProjectPlay();Said="Documents changed; choose Play again.";return false;}
        if(choice==SaveChangesChoice.Save)
        {
            if(Project.Judging){Said="A save check is still running; try again when it finishes.";return false;}
            foreach(var file in Project.Files)
            {
                if(Documents.TryGetOpenScene(file.Name,out var scene))
                {if(scene!.Session.Dirty&&!Documents.SaveScene(file.Name,out var why)){Said=why;return false;}}
                else if(Documents.TryGetOpenScene2(file.Name,out var v2))
                {if(v2!.Session.Dirty&&!Documents.SaveScene2(file.Name,out var why)){Said=why;return false;}}
                else if(file.Dirty)
                {
                    // Reuse the workspace's exact save/check/background-check route.
                    if(!windows.TryGetValue(file.Name,out var window)||!window.Run("editor.save"))
                    {Said=window?.Status??$"Open {file.Name} before saving it.";return false;}
                }
            }
            if(ProjectDirty()){Said="Changes are still waiting to be saved.";return false;}
        }
        else if(choice!=SaveChangesChoice.DontSave)return false;
        playSavePrompt=false;playCurrent=CapturePlayDocuments();return ContinueProjectPlay();
    }
    private bool ContinueProjectPlay()
    {
        if(pendingLaunch is not {} launch||PlaySettings is not {} settings||projectPlayHost is null)return false;
        if(playCurrent?.Invoke()!=true||!settings.TryLaunch(out var now,out _)||JsonSerializer.Serialize(now)!=JsonSerializer.Serialize(launch))
        {CancelProjectPlay();Said="The documents or Play command changed; choose Play again.";return false;}
        if(!SavedPlayInputs())return false;
        var reason=settings.ApprovalNeeded(launch,false)??(pendingBuild?settings.ApprovalNeeded(launch,true):null);
        if(reason is not null){playApprovalPrompt=true;playFocus=true;Said=reason;return false;}
        projectPlay?.Dispose();projectPlay=null;
        try
        {
            projectPlay=projectPlayHost.Start(launch);ClearProjectConsole();focusProjectConsole=true;
            projectDiagnosticRoot=launch.ProjectRoot;projectDiagnosticSignature=ProjectDirty()?null:Bed.Data.CheckSnapshot.SignatureOf(Project);projectDiagnosticNote="";
            projectPlayWasBuilding=pendingBuild;projectPlayStopRequested=false;
            var ok=projectPlay.Start(false,pendingBuild);Said=projectPlay.Status;
            pendingLaunch=null;playApprovalPrompt=false;return ok;
        }
        catch(Exception e)when(e is not OutOfMemoryException){projectPlay?.Dispose();projectPlay=null;Said=e.Message;return false;}
    }
    /// <summary>Accepts only the exact displayed command while the captured request is unchanged.</summary>
    public bool ApproveProjectPlay()
    {
        if(!playApprovalPrompt||pendingLaunch is not {} launch||PlaySettings is not {} settings)return false;
        if(playCurrent?.Invoke()!=true||!settings.TryLaunch(out var now,out _)||JsonSerializer.Serialize(now)!=JsonSerializer.Serialize(launch))
        {CancelProjectPlay();Said="The documents or Play command changed; choose Play again.";return false;}
        // Each click grants only the exact action currently displayed. Build approval follows external Run approval.
        settings.Approve(launch,settings.ApprovalNeeded(launch,false) is null&&pendingBuild);
        playApprovalPrompt=false;return ContinueProjectPlay();
    }
    /// <summary>Cancels pending UI decisions without saving, approving or starting.</summary>
    public void CancelProjectPlay(){pendingLaunch=null;playSavePrompt=playApprovalPrompt=false;playCurrent=null;Said="Play cancelled.";}
    /// <summary>Requests the current owner's stop and leaves all documents untouched.</summary>
    public bool StopProjectPlay(){if(projectPlay is null||projectPlay.HasCompleted)return false;projectPlay.RequestStop();TourStopRequests++;projectPlayStopRequested=true;Said=projectPlay.Status;return true;}
    private void DisposeProjectPlay(){CancelProjectPlay();var owned=projectPlay;projectPlay=null;owned?.Dispose();}
    private void BindProjectControls<T>(EditorWindow<T> window) where T:class
    {
        window.PlaySettingsAction=()=>PlaySettings!.Open=true;
        if(projectPlayHost is null)return;
        window.ProjectFrames=()=>projectPlay is {HasCompleted:false} ? projectPlay as IProjectFrameControlSession : null;
        window.Shortcuts.Default("editor.pause_simulation","F9").Default("editor.step_simulation","F10").Default("editor.resume_simulation","F11");
        window.ProjectPlayAction=()=>RequestProjectPlay();window.ProjectStopAction=StopProjectPlay;
        window.ProjectPlaying=()=>projectPlay is {HasCompleted:false};
        window.ProjectBuildAndPlayAction=()=>RequestProjectPlay(true);
        window.ProjectPlayUnavailable=build=>
        {
            if(!PlaySettings!.TryLaunch(out var launch,out var why))
                return why=="Choose a program in Play settings."?"Set the program in Tools > Play settings":why;
            return build&&launch!.Build is null?"Set a build command in Tools > Play settings":null;
        };
    }
    private void PreparePlaySettingsUi()
    {
        if(PlaySettings is not {} form)return;
        if(SceneWindow is {} scene)BindProjectControls(scene);
        if(Scene2Window is {} scene2)BindProjectControls(scene2);
        if(Window is {} data)BindProjectControls(data);
        projectPlay?.Update();
        if(projectPlay is {} session)
        {
            CollectProjectOutput(session.DrainLogs());
            if(focusProjectConsole)ImGui.SetNextWindowFocus();
            var files=ImGui.Internal.FindWindowByName(Scene2Window is not null?"Prefab and images###panel.scene2.prefabs"u8:"Files###panel.project.files"u8);
            if(!files.IsNull()&&files.DockId.Value!=0)ImGui.SetNextWindowDockID(files.DockId,ImGuiCond.FirstUseEver);
            if(Ui.Begin("Play console###project.play.console"u8,ImGuiWindowFlags.HorizontalScrollbar|ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                if(!session.HasCompleted&&Ui.Button("Stop program"u8))StopProjectPlay();
                if(session.CanBuildAndPlay&&Ui.Button("Build and Play"u8))RequestProjectPlay(true);
                DrawProjectConsole();
            }
            Ui.End();
        }
        if(playSavePrompt||playApprovalPrompt)
        {
            if(playFocus)ImGui.SetNextWindowFocus();
            if(Ui.Begin("Play saved files###project.play.decision"u8,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings))
            {
                Ui.TextWrapped(Said);
                if(playFocus){ImGui.SetKeyboardFocusHere();playFocus=false;}
                if(playSavePrompt)
                {if(Ui.Button("Save"u8))AnswerProjectPlay(SaveChangesChoice.Save);ImGui.SameLine();if(Ui.Button("Don't Save"u8))AnswerProjectPlay(SaveChangesChoice.DontSave);}
                else if(Ui.Button("Allow this exact command and continue"u8))ApproveProjectPlay();
                ImGui.SameLine();if(Ui.Button("Cancel"u8))CancelProjectPlay();
                if(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))CancelProjectPlay();
            }
            Ui.End();
        }
        form.Draw();
    }
}
