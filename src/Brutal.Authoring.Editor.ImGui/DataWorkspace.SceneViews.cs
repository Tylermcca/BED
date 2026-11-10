namespace Brutal.Authoring.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private readonly Dictionary<string,SceneEditorFlags> sceneFlags=new(StringComparer.Ordinal);
    private string? sceneViewPreferences;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorModel<Scene>,object> viewGuardedEditors=new();
    private SceneEditorFlags Flags(string name)
    {
        if(!sceneFlags.TryGetValue(name,out var flags))
        {
            flags=new();sceneFlags.Add(name,flags);
            if(sceneViewPreferences is {} folder)flags.Bind(Project.DataFolder,name,folder);
        }
        return flags;
    }
    private void BindSceneViewPreferences(string folder)
    {
        sceneViewPreferences=folder;
        foreach(var (name,flags) in sceneFlags)
        {
            flags.Bind(Project.DataFolder,name,folder);
            if(sceneWindows.TryGetValue(name,out var window)&&Documents.TryGetOpenScene(name,out var entry)&&sceneViews.TryGetValue(entry!,out var view))
            {view.CancelInput();view.SourceGeneration++;view.InvalidateRenderRequest();}
            if(flags.Note.Length>0)Said=flags.Note;
        }
    }
    private void BindSceneViewChoices(string name,EditorWindow<Scene> window,SceneEditorFlags flags)
    {
        (bool Ok,string Message) Toggle(bool hide)
        {
            if(clipboardClosed||!ReferenceEquals(SceneWindow,window))return(false,"Open this level before changing its view choices.");
            if(projectPlay is {HasCompleted:false}||window.Editor.Playing is not null||window.ProjectPlaying?.Invoke()==true)return(false,"Stop Play before changing view choices.");
            var result=flags.Toggle(window.Editor.Session.Current,window.Editor.Selection,hide);
            if(Documents.TryGetOpenScene(name,out var entry)&&sceneViews.TryGetValue(entry!,out var view))
            {view.CancelInput();view.SourceGeneration++;view.InvalidateRenderRequest();}
            return result;
        }
        if(!viewGuardedEditors.TryGetValue(window.Editor,out _))
        {
            var previous=window.Editor.EditRefusal;
            window.Editor.EditRefusal=(document,commands,touches)=>previous?.Invoke(document,commands,touches)??flags.Refusal(document,commands);
            viewGuardedEditors.Add(window.Editor,new());
        }
        if(Documents.TryGetOpenScene(name,out var current)&&sceneViews.TryGetValue(current!,out var viewport))
            viewport.TransformRefusal=()=>flags.TransformRefusal(window.Editor.Session.Current,window.Editor.Selection);
        window.SceneHide=()=>Toggle(true);window.SceneLock=()=>Toggle(false);
        window.OutlineBadges=id=>flags.Badge(window.Editor.Session.Current,id);
        window.Shortcuts.Default("editor.hide_objects","H").Default("editor.lock_objects","L");
    }
}
