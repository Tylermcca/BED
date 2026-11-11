namespace Bed.Editor.ImGui;

public sealed partial class ProjectLauncher
{
    private bool projectChooser;
    private DataWorkspace? chooserOwner;
    private object?[] chooserState=[];
    private bool HasProjectChooserApproval()=>projectChooser&&Workspace is {} open&&ReferenceEquals(open,chooserOwner)
        &&chooserState.SequenceEqual(open.TransitionState());

    private bool ChooseProject(StartPage page)
    {
        if(!ConfirmBefore(()=>ChooseProject(page))) {Workspace?.Note(Said);return false;}
        if(Workspace is not {} open)return Say(false,"No project is open.");
        chooserOwner=open;chooserState=open.TransitionState();projectChooser=true;
        startPage=page;
        if(page==StartPage.New)focusNewName=true;
        else BeginOpenProject();
        return true;
    }

    private void EndProjectChooser()
    {projectChooser=false;chooserOwner=null;chooserState=[];startPage=StartPage.Home;}
}
