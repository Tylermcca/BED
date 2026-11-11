using Bed.Editor;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;
public sealed partial class DataWorkspace
{
    private sealed record Scene2CloseRequest(string Name,Scene2DocumentEntry Entry,long Revision);
    private Scene2CloseRequest? scene2Close;
    private bool focusScene2Close;
    private bool RequestScene2Close(string name,Scene2DocumentEntry entry)
    {
        if(scene2WorkspaceClosed||Scene2OwnerCurrent?.Invoke(this)==false){Said="This workspace is closed.";return false;}
        if(Active?.Name!=name||!Documents.TryGetOpenScene2(name,out var current)||!ReferenceEquals(current,entry)){Said="The scene owner changed; use the current window.";return false;}
        if(scene2Windows.TryGetValue(name,out var view))view.CancelInput();scenePlacement?.Cancel();
        scene2Close=new(name,entry,entry.Session.Revision);
        if(!entry.Session.Dirty)return AnswerScene2Close(SaveChangesChoice.DontSave);
        focusScene2Close=true;Said=$"Save changes to {name} before closing?";return true;
    }
    private bool AnswerScene2Close(SaveChangesChoice choice)
    {
        if(scene2Close is not {} request)return false;
        if(choice==SaveChangesChoice.Cancel){scene2Close=null;Said="Close cancelled; scene retained.";return false;}
        if(Active?.Name!=request.Name||!Documents.TryGetOpenScene2(request.Name,out var current)||!ReferenceEquals(current,request.Entry)||current!.Session.Revision!=request.Revision)
        {scene2Close=null;Said="The scene changed; request Close again.";return false;}
        if(choice is not(SaveChangesChoice.Save or SaveChangesChoice.DontSave))return false;
        if(!Documents.CloseScene2(request.Name,choice==SaveChangesChoice.Save?SceneCloseDecision.Save:SceneCloseDecision.Discard,out var why)){Said=why;return false;}
        scene2Close=null;scene2Windows.Remove(request.Name);Active=null;Said=why;Refocus(file:request.Name);return true;
    }
    private void DrawScene2Close()
    {
        if(scene2Close is not {} request)return;
        if(focusScene2Close)ImGui.SetNextWindowFocus();
        if(Ui.Begin("Save changes?###scene2.close"u8,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped($"Save changes to {request.Name} before closing?");Ui.TextWrapped(Said);
            if(focusScene2Close){ImGui.SetKeyboardFocusHere();focusScene2Close=false;}
            if(Ui.Button("Save"u8))AnswerScene2Close(SaveChangesChoice.Save);ImGui.SameLine();
            if(Ui.Button("Don't Save"u8))AnswerScene2Close(SaveChangesChoice.DontSave);ImGui.SameLine();
            if(Ui.Button("Cancel"u8))AnswerScene2Close(SaveChangesChoice.Cancel);
            if(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))AnswerScene2Close(SaveChangesChoice.Cancel);
        }
        Ui.End();
    }
}
