namespace Brutal.Authoring.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private readonly SceneClipboard objectClipboard=new();
    private bool clipboardClosed;
    private void ReleaseObjectClipboard(){clipboardClosed=true;objectClipboard.Clear();}
    private void BindSceneClipboard(EditorWindow<Scene> window)
    {
        bool Current()=>!clipboardClosed&&ReferenceEquals(SceneWindow,window);
        window.SceneCopy=()=>Current()?objectClipboard.Copy(window.Editor):(false,"Open this level before copying objects.");
        window.ScenePaste=()=>!Current()?(false,"Open this level before pasting objects.")
            :projectPlay is {HasCompleted:false}?(false,"Stop Play before pasting objects."):objectClipboard.Paste(window.Editor);
        window.ScenePasteUnavailable=()=>!objectClipboard.HasObjects?"Copy objects in this project before pasting.":window.Editor.FocusedOn is not null?"Step out of focus before pasting objects.":null;
        window.Shortcuts.Default("scene.copy","Ctrl+C").Default("scene.paste","Ctrl+V");
    }
}
