using Bed.Data;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private const int ProjectConsoleLineLimit=256;
    private sealed record ConsoleLine(bool Error,string Text,long Key,PlayDiagnostic? Diagnostic);
    private readonly Queue<ConsoleLine> projectOutput=new();
    private long projectLineKey;
    private string? projectDiagnosticRoot,projectDiagnosticSignature;
    private string projectDiagnosticNote="";
    private bool showProjectStdout=true,showProjectStderr=true,focusProjectConsole;
    private long projectLinesDropped;
    private ProjectFrameCommand? focusProjectFrameCommand;
    private void ClearProjectConsole(){projectOutput.Clear();projectLinesDropped=0;focusProjectFrameCommand=null;}
    private void CollectProjectOutput(IReadOnlyList<ProjectPlayOutput> lines)
    {
        foreach(var line in lines)
        {
            projectOutput.Enqueue(new(line.Error,line.Text.Length>4096?line.Text[..4096]:line.Text,++projectLineKey,PlayDiagnostic.Read(line.Text)));
            if(projectOutput.Count>ProjectConsoleLineLimit)
            {projectOutput.Dequeue();if(projectLinesDropped<long.MaxValue)projectLinesDropped++;}
        }
    }
    private string ProjectDropNote=>$"{projectLinesDropped} lines dropped. Showing the most recent {ProjectConsoleLineLimit} lines.";
    private void DrawProjectConsole()
    {
        if(focusProjectConsole){ImGui.SetKeyboardFocusHere();focusProjectConsole=false;}
        if(Ui.Button("Clear console"u8))ClearProjectConsole();
        ConsoleSameLineIfFits("Copy all", button:true);
        if(Ui.Button("Copy all"u8))
        {
            var all=projectOutput.Select(line=>(line.Error?"error: ":"output: ")+line.Text);
            if(projectLinesDropped>0)all=all.Prepend(ProjectDropNote);
            ImGui.SetClipboardText(string.Join(Environment.NewLine,all));
        }
        if(projectPlay is IProjectFrameControlSession control && !projectPlay.HasCompleted)
        {
            foreach(var (label,command) in new[]{("Pause simulation (F9)",ProjectFrameCommand.Pause),("Step one frame (F10)",ProjectFrameCommand.Step),("Resume simulation (F11)",ProjectFrameCommand.Resume)})
            {
                var reason=ProjectFrameActions.Refusal(control.FrameControl,command);
                if(focusProjectFrameCommand is not null && !ImGui.IsWindowFocused())focusProjectFrameCommand=null;
                if(focusProjectFrameCommand==command && reason is null)
                {ImGui.SetKeyboardFocusHere();focusProjectFrameCommand=null;}
                Ui.BeginDisabled(reason is not null,reason);
                if(Ui.Button(label) && control.RequestFrameControl(command))
                    focusProjectFrameCommand=command==ProjectFrameCommand.Resume?ProjectFrameCommand.Pause:ProjectFrameCommand.Step;
                Ui.EndDisabled();
            }
        }
        Ui.Checkbox("Show game output"u8,ref showProjectStdout);
        ConsoleSameLineIfFits("Show game errors", button:false);Ui.Checkbox("Show game errors"u8,ref showProjectStderr);
        if(projectPlay is {} current)DrawProjectPlayStatus(current);
        if(projectLinesDropped>0)Ui.TextWrapped(ProjectDropNote,semanticId:"project.play.dropped");
        foreach(var line in projectOutput)
        {
            if(line.Error?showProjectStderr:showProjectStdout)
            {
                if(line.Diagnostic is {} diagnostic)
                {
                    Ui.TextWrapped((line.Error?"error: ":"")+diagnostic.Message,semanticId:"project.play.output."+line.Key);
                    var label=("Open "+diagnostic.File+" "+diagnostic.Pointer).Replace("##","# #",StringComparison.Ordinal).Replace('\r',' ').Replace('\n',' ');
                    if(Ui.TextLinkWrapped(label,"diagnostic."+line.Key))RevealPlayDiagnostic(diagnostic);
                }
                else Ui.TextWrapped((line.Error?"error: ":"")+line.Text,semanticId:"project.play.output."+line.Key);
            }
        }
        if(projectDiagnosticNote.Length>0)Ui.StatusWrapped(projectDiagnosticNote,"project.play.diagnostic.status");
    }
    private static void ConsoleSameLineIfFits(string caption,bool button)
    {
        var style=ImGui.GetStyle();
        var nextWidth=ImGui.CalcTextSize(caption).X+(button?style.FramePadding.X*2:ImGui.GetFrameHeight()+style.ItemInnerSpacing.X);
        var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
        if(ImGui.GetItemRectMax().X+style.ItemSpacing.X+nextWidth<=right)ImGui.SameLine();
    }
    private void RevealPlayDiagnostic(PlayDiagnostic diagnostic)
    {
        try
        {
            if(projectDiagnosticSignature is null||ProjectDirty()||CheckSnapshot.SignatureOf(Project)!=projectDiagnosticSignature)
            {projectDiagnosticNote="The saved project or its draft changed since Play began. Run Play again before following this location.";return;}
            if(projectDiagnosticRoot is null){projectDiagnosticNote="This Play run has no project root for diagnostic locations.";return;}
            var absolute=Path.GetFullPath(diagnostic.File.Replace('/',Path.DirectorySeparatorChar),projectDiagnosticRoot);
            var file=Path.GetRelativePath(Project.DataFolder,absolute).Replace('\\','/');
            if(Project.Find(file) is null){projectDiagnosticNote="The diagnostic file is not available in this project: "+diagnostic.File;return;}
            if(Project.Paths.Refusal(file) is {} why){projectDiagnosticNote=why;return;}
            RevealFindingPlace(new CheckPlace(file,At:diagnostic.Pointer));projectDiagnosticNote=Said;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {projectDiagnosticNote="Cannot open this diagnostic location: "+error.Message;}
    }
}
