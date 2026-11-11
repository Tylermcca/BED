using System.Collections.Immutable;
using System.Numerics;
namespace Bed.Editor.ImGui;

// Owned by one DataWorkspace. Immutable source objects never enter the OS clipboard or another project.
internal sealed class SceneClipboard
{
    private Scene? snapshot;private string[] selected=[];
    internal void Clear(){snapshot=null;selected=[];}
    internal bool HasObjects=>snapshot is not null&&selected.Length>0;
    internal (bool Ok,string Message) Copy(EditorModel<Scene> editor)
    {
        var ids=editor.Selection.ToArray();var scene=editor.Session.Current;
        if(ids.Length==0)return(false,"Select objects to copy first.");
        if(ids.Any(id=>scene.Find(id) is null))return(false,"The selection changed; select the objects again.");
        snapshot=scene;selected=ids;return(true,$"Copied {ids.Length} object(s) inside this project.");
    }
    internal (bool Ok,string Message) Paste(EditorModel<Scene> editor)
    {
        if(snapshot is not {} source||selected.Length==0)return(false,"Copy objects in this project before pasting.");
        if(editor.Playing is not null)return(false,"Stop Play before pasting objects.");
        if(editor.FocusedOn is not null)return(false,"Step out of focus before pasting objects.");
        var destination=editor.Session.Current;var revision=editor.Session.Revision;
        var included=selected.ToHashSet(StringComparer.Ordinal);var existing=destination.Objects.Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
        // An external parent absent from the other level cannot remain a dangling parent link.
        var detach=selected.Where(id=>source.Find(id)!.Parent is {} parent&&!included.Contains(parent)&&!existing.Contains(parent)).ToArray();
        var reparent=SceneReparent.To(source,detach,null,preserveWorld:true);
        if(!reparent.Valid)return(false,string.Join(" ",reparent.Issues.Select(i=>i.Message)));
        foreach(var command in reparent.Commands)source=command.Apply(source).Document!;
        foreach(var id in selected)
            foreach(var reference in source.Find(id)!.References)
                if(!included.Contains(reference.Value)&&!existing.Contains(reference.Value))
                    return(false,$"Cannot paste '{id}': reference '{reference.Key}' needs '{reference.Value}' in this level. Copy that object too or add its target first.");
        // Feed UI10's exact planner a union of identities; it only allocates IDs, never reads this temporary scene's links.
        var basis=new Scene(destination.Objects.AddRange(selected.Where(id=>!existing.Contains(id)).Select(id=>source.Find(id)!)));
        var map=LinkPlans.PlanDuplicate(new SceneLinks(),basis,selected,(id,n)=>n==1?"copy_"+id:$"copy_{id}_{n}");
        string In(string id)=>map.TryGetValue(id,out var copy)?copy:id;
        var copies=selected.Select(id=>source.Find(id)!).Select(o=>o with{Id=map[o.Id],Parent=o.Parent is {} parent?In(parent):null,
            References=o.References.ToImmutableSortedDictionary(p=>p.Key,p=>In(p.Value),StringComparer.Ordinal)}).ToArray();
        var candidate=new Scene(destination.Objects.AddRange(copies));var ids=copies.Select(o=>o.Id).ToArray();
        var move=SceneTranslation.World(candidate,ids,Vector3.UnitX);
        if(!move.Valid)return(false,string.Join(" ",move.Issues.Select(i=>i.Message)));
        if(!editor.ExecuteAt(revision,[..copies.Select(o=>(ICommand<Scene>)new AddObject(o)),..move.Commands]))
            return(false,string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message)));
        editor.Select(ids);return(true,$"Pasted {ids.Length} object(s), moved 1 metre along world X.");
    }
}
