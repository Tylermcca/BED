namespace Brutal.Authoring.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private const int ProjectConsoleLineLimit=256;
    private readonly Queue<ProjectPlayOutput> projectOutput=new();
    private bool showProjectStdout=true,showProjectStderr=true,focusProjectConsole;
    private long projectLinesDropped;
    private void ClearProjectConsole(){projectOutput.Clear();projectLinesDropped=0;}
    private void CollectProjectOutput(IReadOnlyList<ProjectPlayOutput> lines)
    {
        foreach(var line in lines)
        {
            projectOutput.Enqueue(new(line.Error,line.Text.Length>4096?line.Text[..4096]:line.Text));
            if(projectOutput.Count>ProjectConsoleLineLimit)
            {projectOutput.Dequeue();if(projectLinesDropped<long.MaxValue)projectLinesDropped++;}
        }
    }
    private string ProjectDropNote=>$"{projectLinesDropped} lines dropped. Showing the most recent {ProjectConsoleLineLimit} lines.";
    private void DrawProjectConsole()
    {
        if(focusProjectConsole){ImGui.SetKeyboardFocusHere();focusProjectConsole=false;}
        if(Ui.Button("Clear console"u8))ClearProjectConsole();
        ImGui.SameLine();
        if(Ui.Button("Copy all"u8))
        {
            var all=projectOutput.Select(line=>(line.Error?"stderr: ":"stdout: ")+line.Text);
            if(projectLinesDropped>0)all=all.Prepend(ProjectDropNote);
            ImGui.SetClipboardText(string.Join(Environment.NewLine,all));
        }
        Ui.Checkbox("Show stdout"u8,ref showProjectStdout);
        ImGui.SameLine();Ui.Checkbox("Show stderr"u8,ref showProjectStderr);
        if(projectLinesDropped>0)Ui.TextWrapped(ProjectDropNote,semanticId:"project.play.dropped");
        var row=0;
        foreach(var line in projectOutput)
        {
            if(line.Error?showProjectStderr:showProjectStdout)
                Ui.TextWrapped((line.Error?"stderr: ":"")+line.Text,semanticId:"project.play.output."+row);
            row++;
        }
    }
}
