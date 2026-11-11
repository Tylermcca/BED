namespace Bed.Editor.ImGui;

public sealed partial class DataWorkspace
{
    internal Action? RecentProjectsMenu {get;set;}
    internal int TransitionDraftCount=>Project.Dirty.Count+Project.Files.Count(f=>Documents.TryGetOpenScene(f.Name,out var scene)&&scene!.Session.Dirty||Documents.TryGetOpenScene2(f.Name,out var v2)&&v2!.Session.Dirty);
    internal bool SaveTransitionDrafts()
    {
        if(Project.Judging){Said="A save check is still running. Wait for it before changing projects.";return false;}
        if(!SaveAll())return false;
        return TransitionDraftCount==0;
    }
    internal sealed record TransitionDraft(string Name,object Session,long Revision,Func<bool> Current,Func<bool> Dirty,Func<bool> Save);
    internal IReadOnlyList<TransitionDraft> TransitionDrafts()
    {
        var drafts=new List<TransitionDraft>();
        foreach(var file in Project.Files)
        {
            if(file.Session is {} session&&file.Dirty)
            {
                var revision=session.Revision;
                bool Current()=>ReferenceEquals(Project.Find(file.Name),file)&&ReferenceEquals(file.Session,session)&&session.Revision==revision;
                bool Save()
                {
                    if(!Current()){Said=$"{file.Name} changed; choose again.";return false;}
                    if(!windows.TryGetValue(file.Name,out var window)||!ReferenceEquals(window.Editor.Session,session))windows[file.Name]=window=Make(file);
                    if(!Current()){Said=$"{file.Name} changed while preparing Save; choose again.";return false;}
                    var ok=window.Run("editor.save");Said=window.Status;return ok;
                }
                drafts.Add(new(file.Name,session,revision,Current,()=>ReferenceEquals(file.Session,session)&&session.Dirty,Save));
            }
            if(Documents.TryGetOpenScene(file.Name,out var scene)&&scene!.Session.Dirty)
            {
                var entry=scene;var revision=entry.Session.Revision;
                bool Current()=>Documents.TryGetOpenScene(file.Name,out var current)&&ReferenceEquals(current,entry)&&entry.Session.Revision==revision;
                bool Save(){if(!Current()){Said=$"{file.Name} changed; choose again.";return false;}var ok=Documents.SaveScene(file.Name,out var why);Said=why;return ok;}
                drafts.Add(new(file.Name,entry.Session,revision,Current,()=>Current()&&entry.Session.Dirty,Save));
            }
            if(Documents.TryGetOpenScene2(file.Name,out var v2)&&v2!.Session.Dirty)
            {
                var entry=v2;var revision=entry.Session.Revision;
                bool Current()=>Documents.TryGetOpenScene2(file.Name,out var current)&&ReferenceEquals(current,entry)&&entry.Session.Revision==revision;
                bool Save(){if(!Current()){Said=$"{file.Name} changed; choose again.";return false;}var ok=Documents.SaveScene2(file.Name,out var why);Said=why;return ok;}
                drafts.Add(new(file.Name,entry.Session,revision,Current,()=>Current()&&entry.Session.Dirty,Save));
            }
        }
        return drafts.OrderBy(d=>d.Name,StringComparer.Ordinal).ToArray();
    }
    private void PrepareRecentProjectsUi()
    {
        if(Window is {} data)data.RecentProjectsMenu=RecentProjectsMenu;
        if(SceneWindow is {} scene)scene.RecentProjectsMenu=RecentProjectsMenu;
    }
}
