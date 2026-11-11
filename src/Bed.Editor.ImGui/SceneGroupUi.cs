using System.Numerics;
namespace Bed.Editor.ImGui;

// Grouping composes existing replayable commands; all pose conversion stays in SceneReparent.
internal static class SceneGroupUi
{
    internal static (bool Ok,string Message) Group(EditorModel<Scene> editor)=>SceneObjectOperations.Group(editor);
    internal static (bool Ok,string Message) Ungroup(EditorModel<Scene> editor)
    {
        var scene=editor.Session.Current;var revision=editor.Session.Revision;
        if(editor.Selection.Count!=1||scene.Find(editor.Primary!) is not {} group)return(false,"Select one parent to ungroup.");
        var children=scene.Objects.Where(o=>o.Parent==group.Id).Select(o=>o.Id).ToArray();
        if(children.Length==0)return(false,"The selected object has no children to ungroup.");
        var references=scene.Objects.Where(o=>o.Id!=group.Id&&o.References.Values.Contains(group.Id,StringComparer.Ordinal)).Select(o=>o.Id).ToArray();
        if(references.Length>0)return(false,$"'{group.Id}' is referenced by {string.Join(", ",references)}. Use Delete to review and clear those references first.");
        var reparent=SceneReparent.To(scene,children,group.Parent,preserveWorld:true);
        if(!reparent.Valid)return(false,string.Join(" ",reparent.Issues.Select(i=>i.Message)));
        if(!editor.ExecuteAt(revision,[..reparent.Commands,new RemoveObject(group.Id)],[group.Id,..children]))return Failure(editor);
        editor.Select(children);return(true,$"Ungrouped '{group.Id}'; child world transforms are unchanged.");
    }
    private static (bool Ok,string Message) Failure(EditorModel<Scene> editor)=>
        (false,string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message)));
}
