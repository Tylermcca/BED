namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private Func<bool>? newProject, openProject;
    private bool focusFileList, scrollFileFocus;
    internal void BindProjectMenus(Func<bool> create, Func<bool> open)
    { newProject=create;openProject=open; }

    // An approval belongs to these exact drafts, never to a later session or edit.
    internal object?[] TransitionState()
    {
        var state=new List<object?> {Project};
        foreach(var file in Project.Files)
        {
            state.Add(file);state.Add(file.Session);state.Add(file.Session?.Revision);state.Add(file.Dirty);
            if(Documents.TryGetOpenScene(file.Name,out var scene))
            {state.Add(scene!.Session);state.Add(scene.Session.Revision);state.Add(scene.Session.Dirty);}
            else state.Add(null);
        }
        return state.ToArray();
    }

    private bool OpenProjectFile()
    {
        Window?.Editor.SetOpen("project.files",true);
        SceneWindow?.Editor.SetOpen("project.files",true);
        focusFileList=true;
        Said="Choose a file in this project's Files panel. Each file keeps its own draft.";
        return true;
    }

    private void PrepareFileListFocus()
    {
        if(!focusFileList)return;
        focusFileList=false;scrollFileFocus=true;
        refocus=(Active?.Name??Project.Files.FirstOrDefault()?.Name,Project.Files.Count==0?FileAction.New:null,ImGui.GetFrameCount());
    }
}
