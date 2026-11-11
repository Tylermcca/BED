using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.ImGui;

// Window-local inline editing. The supplied callback retains revision, focus, validation and journal authority.
internal sealed class SceneRenameUi(EditorModel<Scene> editor,Action<string> say,Func<bool> projectPlaying,
    Func<string,string,long,string[],(bool Ok,string Message)> apply)
{
    private sealed record Pending(string Id,long Revision,string[] Selection);
    private Pending? pending;private string? focusRow;private bool focus;
    private readonly ImInputString text=new(4096,""u8);
    internal string? PendingId=>pending?.Id;
    internal bool HasPending=>pending is not null;
    internal bool FocusWindow=>pending is not null&&focus;
    internal bool Request()
    {
        if(editor.Playing is not null||projectPlaying()){say("Stop Play before renaming an object.");return false;}
        if(editor.Selection.Count!=1||editor.Primary is not {} id||editor.Session.Current.Find(id) is null)
        {say("Select one object to rename.");return false;}
        if(System.Text.Encoding.UTF8.GetByteCount(id)>=text.Capacity){say("This ID is too long for the rename field.");return false;}
        pending=new(id,editor.Session.Revision,[..editor.Selection]);text.SetValue(id.AsSpan());focus=true;
        say("Type the object ID, then Enter to rename or Escape to cancel.");return true;
    }
    internal void Cancel()
    {
        focusRow=pending?.Id;pending=null;Im.Internal.ClearActiveID();say("Rename canceled. IDs and references are unchanged.");
    }
    private void Commit()
    {
        if(pending is not {} request)return;
        if(editor.Session.Revision!=request.Revision||!editor.Selection.SequenceEqual(request.Selection))
        {pending=null;say("The document or selection changed; choose Rename again.");Im.Internal.ClearActiveID();return;}
        if(editor.Playing is not null||projectPlaying()){say("Stop Play before renaming an object.");focus=true;return;}
        text.EvaluateLength();var next=text.ToString();
        if(next==request.Id){pending=null;focusRow=request.Id;Im.Internal.ClearActiveID();say("The object ID is unchanged.");return;}
        var result=apply(request.Id,next,request.Revision,request.Selection);say(result.Message);
        if(result.Ok){pending=null;focusRow=next;Im.Internal.ClearActiveID();}else focus=true;
    }
    internal bool DrawRow(string id)
    {
        if(pending is not {} request||request.Id!=id)return false;
        if(editor.Session.Revision!=request.Revision||!editor.Selection.SequenceEqual(request.Selection))
        {pending=null;say("The document or selection changed; choose Rename again.");Im.Internal.ClearActiveID();return false;}
        Im.PushID(id);var pad=Im.GetStyle().FramePadding;Im.PushStyleVar(ImGuiStyleVar.FramePadding,new float2(pad.X,0));
        Im.SetNextItemWidth(-1);
        if(focus){Im.SetKeyboardFocusHere();focus=false;}
        Ui.NameNextItem("##rename.id"u8,"Object ID");var entered=Ui.InputText("##rename.id"u8,text,ImGuiInputTextFlags.EnterReturnsTrue);
        Im.PopStyleVar();Im.PopID();
        if(entered)Commit();return true;
    }
    internal bool TakeFocusRow(string id)
    {if(focusRow!=id)return false;focusRow=null;return true;}
}
