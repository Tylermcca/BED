using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;
namespace Brutal.Authoring.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private sealed record SceneCloseRequest(string Name, SceneDocumentEntry Entry, long Revision);
    private SceneCloseRequest? sceneClose;
    private bool focusSceneClose;

    private bool RequestSceneClose(string name, SceneDocumentEntry entry)
    {
        if (Active?.Name != name || !Documents.TryGetOpenScene(name,out var current) || !ReferenceEquals(current,entry))
        { Said="This scene window is no longer current; use the reopened scene."; return false; }
        if (sceneClose is not null) return false;
        sceneClose=new(name,entry,entry.Session.Revision);
        if (!entry.Session.Dirty) return AnswerSceneClose(SaveChangesChoice.DontSave);
        focusSceneClose=true;
        Said=$"Save changes to {name} before closing?";
        return true;
    }

    private bool AnswerSceneClose(SaveChangesChoice choice)
    {
        if (sceneClose is not { } request) return false;
        if (choice==SaveChangesChoice.Cancel)
        { sceneClose=null;Said="Close cancelled; the scene is still open.";return false; }
        if (Active?.Name!=request.Name || !Documents.TryGetOpenScene(request.Name,out var current)
            || !ReferenceEquals(current,request.Entry) || current.Session.Revision!=request.Revision)
        { sceneClose=null;Said="The scene changed while choosing Close; request Close again.";return false; }
        if (choice is not (SaveChangesChoice.Save or SaveChangesChoice.DontSave))return false;
        if (!Documents.CloseScene(request.Name,choice==SaveChangesChoice.Save?SceneCloseDecision.Save:SceneCloseDecision.Discard,out var why))
        { Said=why;return false; }
        sceneClose=null;scenePlacement?.Cancel();sceneWindows.Remove(request.Name);Active=null;
        Said=why;Refocus(file:request.Name);return true;
    }

    private void DrawSceneClose()
    {
        if (sceneClose is not { } request)return;
        if (focusSceneClose) ImGui.SetNextWindowFocus();
        if (Ui.Begin("Save changes?###scene.close"u8,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped($"Save changes to {request.Name} before closing?");
            Ui.TextWrapped(Said);
            if (focusSceneClose){ImGui.SetKeyboardFocusHere();focusSceneClose=false;}
            if (Ui.Button("Save"u8))AnswerSceneClose(SaveChangesChoice.Save);
            ImGui.SameLine();
            if (Ui.Button("Don't Save"u8))AnswerSceneClose(SaveChangesChoice.DontSave);
            ImGui.SameLine();
            if (Ui.Button("Cancel"u8))AnswerSceneClose(SaveChangesChoice.Cancel);
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))
                AnswerSceneClose(SaveChangesChoice.Cancel);
        }
        Ui.End();
    }
}
