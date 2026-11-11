namespace Bed.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private readonly SceneClipboard objectClipboard=new();
    private readonly Scene2Clipboard scene2Clipboard=new();
    private bool clipboardClosed;
    private void ReleaseObjectClipboard(){clipboardClosed=true;objectClipboard.Clear();scene2Clipboard.Clear();}
    private void BindScene2Clipboard(NativeScene2 view)
    {
        var window=view.Window;
        bool Current()=>!clipboardClosed&&Scene2OwnerCurrent?.Invoke(this)!=false&&ReferenceEquals(Scene2Window,window);
        window.SceneCopy=()=>Current()?scene2Clipboard.Copy(window.Editor):(false,"Open this level before copying objects.");
        window.ScenePaste=()=>!Current()?(false,"Open this level before pasting objects."):view.Guard() is {} why?(false,why):scene2Clipboard.Paste(window.Editor);
        window.ScenePasteUnavailable=()=>!scene2Clipboard.HasObjects?"Copy scene-v2 objects in this project first.":window.Editor.FocusedOn is not null?"Step out of focus before pasting objects.":view.Guard();
        window.Shortcuts.Default("scene.copy","Ctrl+C").Default("scene.paste","Ctrl+V");
    }
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
