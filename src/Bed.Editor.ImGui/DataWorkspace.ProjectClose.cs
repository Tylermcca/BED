using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private IReadOnlyList<TransitionDraft>? projectCloseDrafts;
    private bool focusProjectClose,projectCloseSaving;
    private bool CloseSceneView(string name)
    {
        if(Documents.TryGetOpenScene2(name,out var two))return RequestScene2Close(name,two!);
        if(Documents.TryGetOpenScene(name,out var one))return RequestSceneClose(name,one!);
        Said="Open this scene before closing its view.";return false;
    }
    private bool AnswerProjectClose(SaveChangesChoice choice)
    {
        if(scene2WorkspaceClosed||Scene2OwnerCurrent?.Invoke(this)==false){projectCloseDrafts=null;Said="This workspace is no longer current.";return false;}
        if(projectCloseDrafts is not {} drafts)return false;
        if(choice==SaveChangesChoice.Cancel){projectCloseSaving=false;projectCloseDrafts=null;Said="Close cancelled; project retained.";return false;}
        if(choice is not(SaveChangesChoice.Save or SaveChangesChoice.DontSave))return false;
        var now=TransitionDrafts();
        if(drafts.Any(d=>!d.Current())||now.Count!=drafts.Count||now.Any(d=>!drafts.Any(old=>old.Name==d.Name&&ReferenceEquals(old.Session,d.Session)&&old.Revision==d.Revision)))
        {projectCloseDrafts=now;Said="The drafts changed; review the close choice again.";return false;}
        if(choice==SaveChangesChoice.Save&&!SaveTransitionDrafts()){projectCloseSaving=pendingMixedSave is not null;return false;}
        projectCloseDrafts=null;return closeProject?.Invoke()??false;
    }
    private void DrawCloseChoices()
    {
        DrawSceneClose();DrawScene2Close();DrawOutsideChoices();
        if(projectCloseSaving&&pendingMixedSave is null)
        {
            projectCloseSaving=false;
            if(projectCloseDrafts is {} pending&&MixedSaveOwnerRefusal() is null&&TransitionDraftCount==0&&pending.All(d=>d.Current()&&!d.Dirty()))
            {projectCloseDrafts=null;closeProject?.Invoke();return;}
        }
        if(projectCloseDrafts is not {} drafts)return;
        if(focusProjectClose)ImGui.SetNextWindowFocus();
        if(Ui.Begin("Close project?###workspace.close"u8,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped("Save changes before closing this project?"u8);
            foreach(var draft in drafts)Ui.TextWrapped(draft.Name);
            Ui.TextWrapped(Said);if(focusProjectClose){ImGui.SetKeyboardFocusHere();focusProjectClose=false;}
            if(Ui.Button("Save all and close"u8))AnswerProjectClose(SaveChangesChoice.Save);
            if(Ui.Button("Don't Save"u8))AnswerProjectClose(SaveChangesChoice.DontSave);
            if(Ui.Button("Cancel"u8))AnswerProjectClose(SaveChangesChoice.Cancel);
            if(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))AnswerProjectClose(SaveChangesChoice.Cancel);
        }
        Ui.End();
    }
}
