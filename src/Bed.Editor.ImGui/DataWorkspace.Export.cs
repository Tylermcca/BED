using Brutal.ImGuiApi;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.ImGui;
/// <summary>Typed export verification bridge.</summary>
public sealed partial class DataWorkspace
{
    private ExportBuildDialog? exportDialog;
    private bool exportSavePrompt,exportFocus;
    private Func<bool>? exportCurrent,exportSavedCurrent;
    private bool ExportPlayRunning()=>projectPlay is {HasCompleted:false}||Playing is not null||sceneWindows.Values.Any(w=>w.Editor.Playing is not null)||scene2Windows.Values.Any(w=>w.Entry.Editor.Playing is not null);
    private bool ExportOwnerCurrent()=>!scene2WorkspaceClosed&&Scene2OwnerCurrent?.Invoke(this)!=false;
    private bool ExportInputsCurrent(Func<bool>? current)=>current?.Invoke()==true;
    private Func<bool> CaptureExportDocuments()
    {
        var current=CapturePlayDocuments();var name=Active?.Name;
        return ()=>ExportOwnerCurrent()&&Active?.Name==name&&current();
    }
    private void RequestExport()
    {
        if(exportDialog is null)return;
        if(!ExportOwnerCurrent()){Said="This workspace is closed or no longer current; use the current project.";return;}
        if(ExportPlayRunning()){Said="Stop Play before exporting a build.";return;}
        if(exportDialog.Busy){exportDialog.Open=true;return;}
        exportSavedCurrent=null;exportCurrent=CaptureExportDocuments();
        if(ProjectDirty())
        {exportSavePrompt=true;exportFocus=true;exportDialog.Open=false;Said="Export uses saved files. Save changes before exporting?";return;}
        exportSavePrompt=false;exportDialog.Open=true;
    }
    private void AnswerExport(SaveChangesChoice choice)
    {
        if(!exportSavePrompt||exportDialog is null)return;
        if(choice==SaveChangesChoice.Cancel){exportSavePrompt=false;exportCurrent=null;Said="Export canceled. Your edits are unchanged.";return;}
        if(ExportPlayRunning()){exportSavePrompt=false;Said="Stop Play before exporting a build.";return;}
        if(!ExportInputsCurrent(exportCurrent)){exportSavePrompt=false;Said="Documents changed; choose Export build again.";return;}
        if(choice==SaveChangesChoice.Save)
        {
            if(Project.Judging){Said="A save check is still running; try again when it finishes.";return;}
            foreach(var file in Project.Files)
            {
                if(Documents.TryGetOpenScene(file.Name,out var scene))
                {if(scene!.Session.Dirty&&!Documents.SaveScene(file.Name,out var why)){Said=why;return;}}
                else if(Documents.TryGetOpenScene2(file.Name,out var scene2))
                {if(scene2!.Session.Dirty&&!Documents.SaveScene2(file.Name,out var why)){Said=why;return;}}
                else if(file.Dirty)
                {
                    if(!windows.TryGetValue(file.Name,out var window)||!window.Run("editor.save"))
                    {Said=window?.Status??$"Open {file.Name} before saving it.";return;}
                }
            }
            if(ProjectDirty()){Said="Changes are still waiting to be saved.";return;}
        }
        else if(choice!=SaveChangesChoice.DontSave)return;
        exportSavePrompt=false;exportCurrent=null;exportSavedCurrent=CaptureExportDocuments();exportDialog.Open=true;
    }
    private void DrawExportDecision()
    {
        if(!exportSavePrompt)return;
        if(exportFocus)Im.SetNextWindowFocus();
        if(Ui.Begin("Export saved files###export.save.decision"u8,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped(Said);
            Ui.TextWrapped("Don't Save exports the saved version and keeps your edits open."u8);
            if(exportFocus){Im.SetKeyboardFocusHere();exportFocus=false;}
            if(Ui.Button("Save"u8))AnswerExport(SaveChangesChoice.Save);
            Im.SameLine();if(Ui.Button("Don't Save"u8))AnswerExport(SaveChangesChoice.DontSave);
            Im.SameLine();if(Ui.Button("Cancel"u8))AnswerExport(SaveChangesChoice.Cancel);
            if(Im.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&Im.IsKeyPressed(ImGuiKey.Escape,false))AnswerExport(SaveChangesChoice.Cancel);
        }
        Ui.End();
    }
    /// <summary>Binds game-owned export declarations without launching or saving.</summary>
    public void BindExport(GameBuildSpec spec,GameExportDeclaration declaration,IGameExportHost host)
    {
        exportDialog?.Dispose();exportSavePrompt=false;exportCurrent=null;exportSavedCurrent=null;
        PlaySnapshot Capture()
        {
            if(!ExportOwnerCurrent())throw new InvalidOperationException("This workspace is closed or no longer current; use the current project.");
            if(ExportPlayRunning())throw new InvalidOperationException("Stop Play before exporting a build.");
            var file=Active??throw new InvalidOperationException("Open a file before exporting.");
            PlaySnapshotResult result;
            if(Documents.TryGetOpenScene(file.Name,out var scene))
            {if(scene!.Session.CheckDisk()!=DiskState.Unchanged)throw new InvalidOperationException("The saved file changed outside BED; resolve it before exporting.");result=PlaySnapshot.FromSession(scene.Session,file.Name,PlayInputChoice.Saved,declaration.ConvertSaved);}
            else if(Documents.TryGetOpenScene2(file.Name,out var scene2))
            {if(scene2!.Session.CheckDisk()!=DiskState.Unchanged)throw new InvalidOperationException("The saved file changed outside BED; resolve it before exporting.");result=PlaySnapshot.FromSession(scene2.Session,file.Name,PlayInputChoice.Saved,declaration.ConvertSaved);}
            else if(file.Session is { } session)
            {if(session.CheckDisk()!=DiskState.Unchanged)throw new InvalidOperationException("The saved file changed outside BED; resolve it before exporting.");result=PlaySnapshot.FromSession(session,file.Name,PlayInputChoice.Saved,declaration.ConvertSaved);}
            else throw new InvalidOperationException("Open the file before exporting.");
            return result.Snapshot??throw new InvalidOperationException(result.Error?.Message??"Saved input could not be captured.");
        }
        exportDialog=new(host,spec,declaration,Project.DataFolder,Capture,()=>ProjectDirty()&&!ExportInputsCurrent(exportSavedCurrent));
    }
    // The launcher supplies its own local settings folder; project content never chooses this location.
    internal void BindExportPreferences(string preferencesFolder)
        => exportDialog?.BindDestinationPreference(Project.DataFolder, preferencesFolder);
    private void PrepareExportUi()
    {
        if(SceneWindow is {} scene)scene.ExportBuildAction=exportDialog is null?null:RequestExport;
        if(Window is {} data)data.ExportBuildAction=exportDialog is null?null:RequestExport;
        if(Scene2Window is {} scene2)
        {
            var name=Active?.Name;
            scene2.ExportBuildAction=exportDialog is null?null:()=>
            {
                if(!ReferenceEquals(Scene2Window,scene2)||Active?.Name!=name)
                {Said="This scene window is no longer current; use the reopened scene.";return;}
                RequestExport();
            };
        }
        exportDialog?.Draw();
        DrawExportDecision();
    }
}
