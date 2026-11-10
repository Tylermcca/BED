namespace Brutal.Authoring.Editor.ImGui;

public sealed partial class DataWorkspace
{
    internal Action? RecentProjectsMenu {get;set;}
    internal int TransitionDraftCount=>Project.Dirty.Count+Project.Files.Count(f=>Documents.TryGetOpenScene(f.Name,out var scene)&&scene!.Session.Dirty);
    internal bool SaveTransitionDrafts()
    {
        if(Project.Judging){Said="A save check is still running. Wait for it before changing projects.";return false;}
        if(Project.Dirty.Count>0&&!SaveAll())return false;
        foreach(var file in Project.Files)
            if(Documents.TryGetOpenScene(file.Name,out var scene)&&scene!.Session.Dirty&&!Documents.SaveScene(file.Name,out var why))
            {Said=why;return false;}
        return TransitionDraftCount==0;
    }
    private void PrepareRecentProjectsUi()
    {
        if(Window is {} data)data.RecentProjectsMenu=RecentProjectsMenu;
        if(SceneWindow is {} scene)scene.RecentProjectsMenu=RecentProjectsMenu;
    }
}
