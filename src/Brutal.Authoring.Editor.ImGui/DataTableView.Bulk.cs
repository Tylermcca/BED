using Brutal.Authoring.Data;
using Brutal.ImGuiApi;
using Im=Brutal.ImGuiApi.ImGui;
namespace Brutal.Authoring.Editor.ImGui;

public sealed partial class DataTableView
{
    private sealed record SelectedFill(BatchPlan Plan,long Revision,DataDocument Document,string? List,string[] Selection);
    private SelectedFill? selectedFill;
    private string? selectedRowAnchor;
    private bool focusSelectedFill;
    private int refusalPage;
    private IReadOnlyList<string> selectedApplyProblems=[];
    private long selectedProblemRevision=-1;
    private string[] selectedProblemRows=[];
    private void SelectedApplyRefused()
    {
        var issues=Editor.Refused.Count>0?Editor.Refused:Editor.Session.LastIssues;
        selectedApplyProblems=issues.Count==0||Said!=Explain(issues)?[Said]:issues.Select(issue=>Explain([issue])).ToArray();
        selectedProblemRevision=Editor.Session.Revision;selectedProblemRows=[..Editor.Selection];
        refusalPage=0;Said="Bulk edit refused; no rows changed. "+selectedApplyProblems[0];
    }
    private void SelectedRefusals(IReadOnlyList<string> problems)
    {
        if(problems.Count==0)return;
        const int pageSize=20;refusalPage=Math.Min(refusalPage,(problems.Count-1)/pageSize);
        foreach(var problem in problems.Skip(refusalPage*pageSize).Take(pageSize))Ui.TextWrapped(problem);
        if(problems.Count<=pageSize)return;
        Ui.Text($"Refused rows {refusalPage*pageSize+1}–{Math.Min(problems.Count,(refusalPage+1)*pageSize)} of {problems.Count}.");
        Ui.BeginDisabled(refusalPage==0);if(Ui.SmallButton("Earlier refused rows"u8))refusalPage--;Ui.EndDisabled();Im.SameLine();
        Ui.BeginDisabled((refusalPage+1)*pageSize>=problems.Count);if(Ui.SmallButton("More refused rows"u8))refusalPage++;Ui.EndDisabled();
    }
    private void SelectTableRow(IReadOnlyList<string> visible,int index,bool picked)
    {
        var item=visible[index];var io=Im.GetIO();
        if(Im.IsItemFocused()&&!io.WantTextInput&&io.KeyCtrl&&!io.KeyAlt&&!io.KeyShift&&Im.IsKeyPressed(ImGuiKey.Space,false))
        {Editor.Toggle(item);selectedRowAnchor=item;return;}
        if(!picked)return;
        if(io.KeyCtrl)Editor.Toggle(item);
        else if(io.KeyShift)
        {
            var start=IndexOf(visible,selectedRowAnchor??Editor.Primary??item);if(start<0)start=index;
            Editor.Select(visible.Skip(Math.Min(start,index)).Take(Math.Abs(start-index)+1).ToArray());return;
        }
        else Editor.Select(item);
        selectedRowAnchor=item;
    }
    private void SelectedFillControls()
    {
        var selection=Editor.Selection;
        if(selectedProblemRevision!=Editor.Session.Revision||!selectedProblemRows.SequenceEqual(selection))selectedApplyProblems=[];
        // Existing one-row/fill/paste keyboard routes keep their established controls and order.
        if(selection.Count<2)
        {if(focusSelectedFill){focusSelectedFill=false;refocusAfterPlan=true;}return;}
        var selected=selection.ToHashSet(StringComparer.Ordinal);var visible=Visible().ToHashSet(StringComparer.Ordinal);
        var targets=Items().Where(selected.Contains).ToArray();
        var hidden=targets.Count(id=>!visible.Contains(id));
        Ui.TextWrapped($"{targets.Length} rows selected{(hidden>0?$" ({hidden} hidden by the filter)":"")}. Ctrl+click or Ctrl+Space toggles a row; Shift+click selects a range.");
        if(focusSelectedFill)
        {
            if(targets.Length==selection.Count)Im.SetKeyboardFocusHere();else refocusAfterPlan=true;
            focusSelectedFill=false;
        }
        Ui.BeginDisabled(targets.Length!=selection.Count,"Select rows from this table only.");
        if(Ui.Button("Fill selected rows"u8))
        {
            fillText.EvaluateLength();var plan=model.PlanRows(targets,fillField,fillText.ToString()) with{Operation="selected fill"};
            pending=plan;selectedFill=new(plan,Editor.Session.Revision,Editor.Session.Current,Shown,[..selection]);refusalPage=0;selectedApplyProblems=[];
        }
        Ui.EndDisabled();
        if(selectedFill is {} captured&&ReferenceEquals(pending,captured.Plan))
        {
            Ui.TextWrapped($"Preview targets {captured.Selection.Length} selected rows; other rows keep their values.");
            SelectedRefusals(captured.Plan.Problems);
        }
        SelectedRefusals(selectedApplyProblems);
    }
}
