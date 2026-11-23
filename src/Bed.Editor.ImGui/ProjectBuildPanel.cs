namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

internal sealed class ProjectBuildPanel(PlaySettingsForm settings,PlayApprovals approvals,Func<bool> dirty,Func<bool> playRunning):IDisposable
{
    internal const string Id="project.build";
    private CancellationTokenSource? cancellation;
    private Task<ProjectBuildResult>? running;
    private PlayLaunch? pending,executing;
    internal Action<ProjectBuildResult,PlayLaunch>? Completed;
    internal Func<ProjectBuildError,bool>? OpenSource;
    private bool focusApproval,focusBuild,showApprovalDetails;
    private ProjectBuildResult? result;
    private string status="Build the saved files without starting the game.";
    internal bool Busy=>running is {IsCompleted:false};
    internal Panel AsPanel()=>new(Id,"Build game",Draw,OpenAtStart:false,Width:760,Height:560);
    private bool Current(out PlayLaunch? launch)
    {
        if(dirty()){status="Save changes before building.";launch=null;return false;}
        if(playRunning()){status="Stop the running game before building.";launch=null;return false;}
        if(!settings.TryLaunch(out launch,out var why)){status=why??"Set the build command in Play settings.";return false;}
        return true;
    }
    private void Start(PlayLaunch launch)
    {
        executing=launch;pending=null;result=null;cancellation?.Dispose();cancellation=new();
        running=ProjectBuildCommand.RunAsync(launch,approvals,false,cancellation.Token);status="Building saved files...";
    }
    internal void Update()
    {
        if(running is {IsCompleted:true} completed){result=completed.GetAwaiter().GetResult();status=result.Summary;running=null;focusBuild=true;if(executing is {} built)Completed?.Invoke(result,built);executing=null;}
    }
    internal void Draw()
    {
        Update();Ui.TextWrapped(status);
        if(running is not null)
        {if(Ui.Button("Cancel build"u8))cancellation?.Cancel();}
        else if(pending is {} requested)
        {
            Ui.TextWrapped("This runs the project's build on your computer. Allow it if you trust the project."u8);
            PlaySettingsForm.DrawApprovalDetails(requested.Build!, ref showApprovalDetails);
            if(focusApproval){ImGui.SetKeyboardFocusHere();focusApproval=false;}
            if(Ui.Button("Allow this exact command and build"u8))
            {
                if(Current(out var now)&&System.Text.Json.JsonSerializer.Serialize(now)==System.Text.Json.JsonSerializer.Serialize(requested))
                {
                    try{settings.Approve(requested,true);Start(requested);}
                    catch(Exception e)when(e is IOException or UnauthorizedAccessException){pending=null;focusBuild=true;status="The command could not be approved: "+e.Message;}
                }
                else {pending=null;status="The project or command changed. Choose Build saved files again.";}
            }
            ImGui.SameLine();if(Ui.Button("Cancel"u8)){pending=null;focusBuild=true;status="Build canceled.";}
        }
        else
        {
            if(focusBuild){ImGui.SetKeyboardFocusHere();focusBuild=false;}
            if(Ui.Button("Build saved files"u8)&&Current(out var launch))
            {
                if(launch!.Build is null)status="Set a build command in Tools > Play settings first.";
                else if(settings.ApprovalNeeded(launch,true) is not null){pending=launch;status="Build your game";focusApproval=true;showApprovalDetails=false;}
                else Start(launch);
            }
        }
        if(Ui.BeginChild("Build result"u8))
        {
            foreach(var error in result?.Errors??[])
            {
                Ui.TextWrapped(error.File is null?"No source location reported.":$"{error.File}:{error.Line}:{error.Column}");
                if(error.File is not null&&error.Line is >0&&OpenSource is not null&&Ui.TextLinkWrapped("Open source at line "+error.Line,"build.source."+error.File+"."+error.Line))OpenSource(error);
                Ui.TextWrapped(error.What);Ui.TextWrapped("Try: "+error.Try);ImGui.Separator();
            }
            foreach(var line in result?.Output??[])Ui.TextWrapped(line);
        }
        Ui.EndChild();
    }
    public void Dispose(){cancellation?.Cancel();cancellation?.Dispose();cancellation=null;}
}
