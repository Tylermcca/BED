using System.Numerics;
namespace Bed.Editor;

/// <summary>Shared non-drawing scene operations for editor actions and command-line consumers.
/// Plans use existing scene commands and commit through the model as one validated, journaled undo step.</summary>
public static class SceneObjectOperations
{
    /// <summary>Duplicates selected objects beside their originals, one metre along world X, and selects the copies.</summary>
    /// <param name="editor">The authoritative editor; its focus, Play and edit-admission policies apply.</param>
    /// <param name="duplicate">The source IDs and optional duplicate-ID policy, recorded through the existing command.</param>
    /// <param name="why">A plain completion or refusal message.</param>
    /// <returns>True only when the complete duplicate and move batch commits.</returns>
    public static bool Duplicate(EditorModel<Scene> editor,DuplicateGroup duplicate,out string why)
    {
        why="Select objects to duplicate first.";
        if(duplicate.Ids.Count==0)return false;
        var revision=editor.Session.Revision;var preview=duplicate.Apply(editor.Session.Current);
        if(!preview.Ok||preview.Resolved is not DuplicateGroup {Map:{} map} resolved)
        {why=string.Join(" ",preview.Issues.Select(i=>i.Message));return false;}
        var copies=duplicate.Ids.Select(id=>map[id]).ToArray();
        var move=SceneTranslation.World(preview.Document!,copies,Vector3.UnitX);
        if(!move.Valid){why=string.Join(" ",move.Issues.Select(i=>i.Message));return false;}
        var names=editor.Session.Current.Objects.Select(CopyNames.NameOf).ToList();
        var labels=new List<ICommand<Scene>>();
        foreach(var id in duplicate.Ids)
        {
            var label=CopyNames.Next(CopyNames.NameOf(editor.Session.Current.Find(id)!),names);names.Add(label);
            labels.Add(new SetSceneProperty(map[id],"name",System.Text.Json.JsonSerializer.SerializeToElement(label)));
        }
        if(!editor.ExecuteAt(revision,[resolved,..labels,..move.Commands],[..duplicate.Ids]))
        {why=string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message));return false;}
        editor.Select(copies);why=$"Duplicated {copies.Length} object(s); copies moved 1 metre along world X.";return true;
    }

    /// <summary>Groups the selection under a uniquely named empty parent at the mean selected world position.
    /// Selected roots retain their world poses; descendants are not reparented twice. A common parent is retained.</summary>
    /// <param name="editor">The editor supplying the current scene and selection, without a focused subtree.</param>
    /// <returns>Whether the whole add/reparent batch committed, with a plain message.</returns>
    public static (bool Ok,string Message) Group(EditorModel<Scene> editor)
    {
        var scene=editor.Session.Current;var revision=editor.Session.Revision;var ids=editor.Selection.ToArray();
        if(ids.Length==0)return(false,"Select objects to group first.");
        if(editor.FocusedOn is not null)return(false,"Step out of focus before grouping objects.");
        if(ids.Any(id=>scene.Find(id) is null))return(false,"The selection changed; select the objects again.");
        var selected=ids.ToHashSet(StringComparer.Ordinal);
        bool AncestorSelected(SceneObject item)
        {for(var parent=item.Parent;parent is not null;parent=scene.Find(parent)!.Parent)if(selected.Contains(parent))return true;return false;}
        if(new SceneValidator().Validate(scene).HasErrors())return(false,"Resolve the scene's issues before grouping objects.");
        var roots=scene.Objects.Where(o=>selected.Contains(o.Id)&&!AncestorSelected(o)).ToArray();
        double x=0,y=0,z=0;
        foreach(var id in ids){var position=SceneViewport.World(scene,id).Translation;x+=position.X;y+=position.Y;z+=position.Z;}
        var center=new Vector3((float)(x/ids.Length),(float)(y/ids.Length),(float)(z/ids.Length));
        var common=roots.Select(o=>o.Parent).Distinct(StringComparer.Ordinal).ToArray();var parent=common.Length==1?common[0]:null;
        if(parent is not null)
        {
            if(!Matrix4x4.Invert(SceneViewport.World(scene,parent),out var inverse))return(false,"The parent transform cannot hold a group here.");
            center=Vector3.Transform(center,inverse);
        }
        var used=scene.Objects.Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);var groupId="group";var suffix=2;
        while(used.Contains(groupId))groupId="group_"+suffix++;
        var group=SceneObject.At(groupId,null,center) with{Parent=parent};var candidate=new Scene(scene.Objects.Add(group));
        var reparent=SceneReparent.To(candidate,roots.Select(o=>o.Id),groupId,preserveWorld:true);
        if(!reparent.Valid)return(false,string.Join(" ",reparent.Issues.Select(i=>i.Message)));
        if(!editor.ExecuteAt(revision,[new AddObject(group),..reparent.Commands],roots.Select(o=>o.Id).ToArray()))return Failure(editor);
        editor.Select(groupId);return(true,$"Grouped {ids.Length} selected object(s) under '{groupId}' at their world-position centre.");
    }
    private static (bool Ok,string Message) Failure(EditorModel<Scene> editor)=>
        (false,string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message)));
}
