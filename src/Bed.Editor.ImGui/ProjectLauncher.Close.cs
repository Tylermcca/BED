using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>The explicit answer to one pending project transition.</summary>
public enum SaveChangesChoice
{
    /// <summary>Save all data drafts before continuing.</summary>
    Save,
    /// <summary>Continue without saving the current drafts.</summary>
    DontSave,
    /// <summary>Keep the current project and cancel the transition.</summary>
    Cancel
}

public sealed partial class ProjectLauncher
{
    private Func<bool>? pendingTransition;
    private bool decisionExecuting, focusSave, exitAccepted, saveAllRequested;
    private DataWorkspace? decisionOwner;
    private int draftChoiceIdentity;
    private sealed class DraftChoice(DataWorkspace.TransitionDraft draft,int identity)
    {internal readonly int Identity=identity;internal DataWorkspace.TransitionDraft Draft=draft;internal SaveChangesChoice? Choice;internal bool Saving;}
    private readonly List<DraftChoice> draftChoices=[];
    private readonly HashSet<string> changedDraftChoices=new(StringComparer.Ordinal);
    private void RefreshDraftChoices()
    {
        if(Workspace is not {} open||!ReferenceEquals(open,decisionOwner))return;
        var current=open.TransitionDrafts();
        foreach(var draft in current)
        {
            var found=draftChoices.FirstOrDefault(c=>c.Draft.Name==draft.Name);
            if(found is null){draftChoices.Add(new(draft,++draftChoiceIdentity));saveAllRequested=false;changedDraftChoices.Add(draft.Name);Said=$"{draft.Name} changed; choose Save or Don't Save.";continue;}
            if(!ReferenceEquals(found.Draft.Session,draft.Session)||found.Draft.Revision!=draft.Revision)
            {found.Draft=draft;found.Choice=null;found.Saving=false;saveAllRequested=false;changedDraftChoices.Add(draft.Name);Said=$"{draft.Name} changed; choose Save or Don't Save again.";}
            else if(found.Saving&&!open.SavePending(draft.Name)){found.Saving=false;saveAllRequested=false;Said=open.Said;}
        }
        foreach(var choice in draftChoices.Where(c=>!c.Draft.Dirty())){choice.Choice=SaveChangesChoice.Save;choice.Saving=false;changedDraftChoices.Remove(choice.Draft.Name);}
    }
    private bool FinishDraftChoices()
    {
        RefreshDraftChoices();
        if(pendingTransition is not {} next||Workspace is null||!ReferenceEquals(Workspace,decisionOwner))return false;
        if(draftChoices.Any(c=>c.Choice is null||c.Saving))return false;
        pendingTransition=null;closeAnyway=false;draftChoices.Clear();changedDraftChoices.Clear();decisionOwner=null;saveAllRequested=false;
        decisionExecuting=true;try{return next();}finally{decisionExecuting=false;}
    }
    internal bool AnswerFileSaveChanges(string name,SaveChangesChoice answer)
    {
        if(pendingTransition is null||Workspace is not {} open||!ReferenceEquals(open,decisionOwner))return Say(false,"The project changed; request Close again.");
        saveAllRequested=false;
        var displayed=draftChoices.FirstOrDefault(c=>c.Draft.Name==name)?.Draft;
        RefreshDraftChoices();var choice=draftChoices.FirstOrDefault(c=>c.Draft.Name==name);
        if(displayed is not null&&choice is not null&&(!ReferenceEquals(displayed.Session,choice.Draft.Session)||displayed.Revision!=choice.Draft.Revision))return Say(false,$"{name} changed; choose again.");
        if(choice is null||answer is not(SaveChangesChoice.Save or SaveChangesChoice.DontSave))return false;
        if(!choice.Draft.Current())return Say(false,$"{name} changed; choose again.");
        if(answer==SaveChangesChoice.Save)
        {
            if(!choice.Draft.Save())
            {choice.Saving=open.SavePending(name);focusSave=true;return Say(false,open.Said);}
            if(choice.Draft.Dirty())return Say(false,$"{name} still has unsaved edits; choose again.");
        }
        choice.Choice=answer;focusSave=true;changedDraftChoices.Remove(name);
        Said=changedDraftChoices.Count>0?$"{string.Join(", ",changedDraftChoices.Order(StringComparer.Ordinal))} changed; choose Save or Don't Save again.":answer==SaveChangesChoice.Save?$"Saved {name}.":$"Don't save {name}; it stays open until you've chosen for every file.";
        return FinishDraftChoices();
    }

    private bool ConfirmBefore(Func<bool> continuation, bool projectReplacement = false)
    {
        if (decisionExecuting) return true;
        if (projectReplacement && HasProjectChooserApproval()) return true;
        if (Workspace is not { } open || open.TransitionDraftCount == 0) return true;
        if (pendingTransition is null)
        {pendingTransition=continuation;focusSave=true;decisionOwner=open;saveAllRequested=false;draftChoices.Clear();changedDraftChoices.Clear();draftChoices.AddRange(open.TransitionDrafts().Select(d=>new DraftChoice(d,++draftChoiceIdentity)));}
        closeAnyway=true;
        return Say(false, $"{open.TransitionDraftCount} file(s) have unsaved edits. Choose Save, Don't Save or Cancel.");
    }

    /// <summary>Whether the shared save decision is waiting for an answer.</summary>
    public bool SaveChangesPending => pendingTransition is not null;

    /// <summary>Resolves the pending transition. A failed save retains the project, draft and pending decision.</summary>
    public bool AnswerSaveChanges(SaveChangesChoice choice)
    {
        if (pendingTransition is not { } next) return Say(false,"No save decision is pending.");
        if (choice==SaveChangesChoice.Cancel)
        { pendingTransition=null;closeAnyway=false;draftChoices.Clear();changedDraftChoices.Clear();decisionOwner=null;saveAllRequested=false;if(projectChooser)EndProjectChooser();return Say(false,"Cancelled; the project is still open."); }
        if (choice is not (SaveChangesChoice.Save or SaveChangesChoice.DontSave)) return Say(false,"Choose Save, Don't Save or Cancel.");
        if(Workspace is null||!ReferenceEquals(Workspace,decisionOwner))
        {pendingTransition=null;closeAnyway=false;draftChoices.Clear();changedDraftChoices.Clear();decisionOwner=null;saveAllRequested=false;return Say(false,"The project changed; request the action again.");}
        if (choice==SaveChangesChoice.Save && Workspace is { } open && !open.SaveTransitionDrafts())
        {
            foreach(var row in draftChoices)if(open.SavePending(row.Draft.Name))row.Saving=true;
            saveAllRequested=draftChoices.Any(row=>row.Saving);
            return Say(false,open.Said);
        }
        pendingTransition=null;closeAnyway=false;draftChoices.Clear();changedDraftChoices.Clear();decisionOwner=null;saveAllRequested=false;
        decisionExecuting=true;
        try { return next(); }
        finally { decisionExecuting=false; }
    }

    /// <summary>EditorApp's cleared OS-close callback. Pending or cancelled decisions never authorize exit.</summary>
    public bool RequestWindowClose(bool requested)
    {
        if (exitAccepted) return true;
        if (!requested) return false;
        bool Exit()
        {
            if (Workspace is not null && !Close()) return false;
            return exitAccepted=true;
        }
        return ConfirmBefore(Exit) && Exit();
    }

    private void DrawCloseAnyway()
    {
        RefreshDraftChoices();
        if(saveAllRequested&&!draftChoices.Any(c=>c.Saving))
        {saveAllRequested=false;AnswerSaveChanges(SaveChangesChoice.Save);if(pendingTransition is null)return;}
        if(Workspace is {} owner&&ReferenceEquals(owner,decisionOwner)&&draftChoices.Count>1&&draftChoices.All(c=>c.Choice is not null&&!c.Saving))
        {FinishDraftChoices();return;}
        var viewport=ImGui.GetMainViewport();
        ImGui.SetNextWindowSizeConstraints(new(320,0),new(MathF.Max(320,viewport.WorkSize.X-40),MathF.Max(120,viewport.WorkSize.Y-40)));
        if (Ui.Begin("Save changes?###launcher.close"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            Ui.TextWrapped($"Save changes to {(Current is { } current ? Name(current) : "this project") }?");
            if(draftChoices.Count>1)
            {
                if(Ui.BeginChild("Unsaved files"u8,new(MathF.Min(480,MathF.Max(240,viewport.WorkSize.X-80)),MathF.Min(viewport.WorkSize.Y*.55f,draftChoices.Count*(ImGui.GetFrameHeightWithSpacing()+2*ImGui.GetTextLineHeightWithSpacing()))),(ImGuiChildFlags.Borders|ImGuiChildFlags.NavFlattened)))
                    foreach(var choice in draftChoices.ToArray())
                    {
                        var name=choice.Draft.Name;Ui.TextWrapped(name);
                        if(changedDraftChoices.Contains(name))Ui.Status("This file changed; choose again."u8,"close.changed."+name);
                        if(choice.Choice is {} picked){Ui.TextWrapped(picked==SaveChangesChoice.Save?"Saved.":"Don't save selected.");continue;}
                        var disabled=choice.Saving?"Waiting for the checked save to finish.":null;
                        Ui.BeginDisabled(disabled is not null,disabled);
                        {
                            if(focusSave){ImGui.SetKeyboardFocusHere();focusSave=false;}
                            var save="Save##close.save."+choice.Identity;Ui.NameNextItem(save,"Save "+name);if(Ui.Button(save))AnswerFileSaveChanges(name,SaveChangesChoice.Save);
                            ImGui.SameLine();var discard="Don't Save##close.discard."+choice.Identity;Ui.NameNextItem(discard,"Don't save "+name);if(Ui.Button(discard))AnswerFileSaveChanges(name,SaveChangesChoice.DontSave);
                        }
                        Ui.EndDisabled();
                        if(choice.Saving)Ui.Status("Waiting for the checked save to finish."u8,"close.pending."+name);
                    }
                Ui.EndChild();
                if(Ui.Button("Save all"u8))AnswerSaveChanges(SaveChangesChoice.Save);
            }
            else
            {
                foreach(var choice in draftChoices)Ui.TextWrapped(choice.Draft.Name);
                if(focusSave){ImGui.SetKeyboardFocusHere();focusSave=false;}
                if(Ui.Button("Save"u8))AnswerSaveChanges(SaveChangesChoice.Save);
                ImGui.SameLine();if(Ui.Button("Don't Save"u8))AnswerSaveChanges(SaveChangesChoice.DontSave);
            }
            ImGui.SameLine();if(Ui.Button("Cancel"u8))AnswerSaveChanges(SaveChangesChoice.Cancel);
            Ui.StatusWrapped(Said,"launcher.close.status");
            if(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))AnswerSaveChanges(SaveChangesChoice.Cancel);
        }
        Ui.End();
    }
}
