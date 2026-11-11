using Brutal.ImGuiApi;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.ImGui;

// A window-owned decision over existing commands; no force-delete command or schema bypass.
internal sealed class SceneDeleteUi(EditorModel<Scene> editor,Action<string> say,Func<bool> projectPlaying)
{
    private sealed record Pending(string Id,long Revision,string[] Selection,Link[] Links);
    private Pending? pending;private bool focus;private string message="";
    private void Report(string value){message=value;say(value);}
    internal bool HasPending=>pending is not null;
    private bool Running=>projectPlaying()||editor.Playing is not null;
    internal bool Request(RemoveObject command)
    {
        if(Running){Report("Stop Play before deleting objects.");return false;}
        var document=editor.Session.Current;
        if(document.Find(command.Id) is null){Report("The selected object no longer exists.");return false;}
        var links=new SceneLinks().Links(document).Where(l=>l.To==command.Id&&l.From!=command.Id).ToArray();
        pending=new(command.Id,editor.Session.Revision,[..editor.Selection],links);
        if(links.Length==0)return Confirm();
        focus=true;Report($"'{command.Id}' is referenced by other objects. Review them before deleting.");return true;
    }
    private bool Confirm()
    {
        if(pending is not {} request)return false;
        if(Running){Report("Stop Play before deleting objects.");return false;}
        if(editor.Session.Revision!=request.Revision||!editor.Selection.SequenceEqual(request.Selection))
        {pending=null;Report("The file or selection changed; choose Delete again.");return false;}
        var document=editor.Session.Current;
        var children=document.Objects.Where(o=>o.Parent==request.Id).Select(o=>o.Id).ToArray();
        var detach=SceneReparent.To(document,children,null,preserveWorld:true);
        if(!detach.Valid){Report(string.Join(" ",detach.Issues.Select(i=>i.Message)));return false;}
        var commands=detach.Commands.ToList();
        foreach(var source in document.Objects.Where(o=>o.Id!=request.Id))
            foreach(var reference in source.References.Where(p=>p.Value==request.Id))commands.Add(new SetReference(source.Id,reference.Key,null));
        commands.Add(new RemoveObject(request.Id));
        var touches=request.Links.Select(l=>l.From).Append(request.Id).Distinct(StringComparer.Ordinal).ToArray();
        if(!editor.ExecuteAt(request.Revision,commands,touches))
        {Report(string.Join(" ",(editor.Refused.Count>0?editor.Refused:editor.Session.LastIssues).Select(i=>i.Message)));return false;}
        pending=null;editor.Select();Report($"Deleted '{request.Id}'. Undo restores the object and its links.");return true;
    }
    internal void Cancel(){pending=null;Report("Delete canceled. The objects and their links are unchanged.");}
    internal void Draw()
    {
        if(pending is not {} request)return;
        if(focus)Im.SetNextWindowFocus();
        if(Ui.Begin("Delete referenced object?###scene.delete.decision"u8,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped($"Delete '{request.Id}'? These objects refer to it:");
            Ui.TextWrapped(message);
            foreach(var link in request.Links)Ui.TextWrapped($"{link.From}: {link.Kind.Name} '{request.Id}' ({link.Field})");
            Ui.TextWrapped("Delete anyway keeps the other objects, detaches children at their world positions and clears these references."u8);
            if(Ui.Button("Delete anyway"u8))Confirm();
            Im.SameLine();if(focus){Im.SetKeyboardFocusHere();focus=false;}
            if(Ui.Button("Cancel"u8))Cancel();
            if(Im.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&Im.IsKeyPressed(ImGuiKey.Escape,false))Cancel();
        }
        Ui.End();
    }
}
