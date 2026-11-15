namespace Bed.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private string? codeWorkingFolder;
    private bool buildInConsole;
    private string buildConsoleStatus="";
    internal bool OpenGameCode()
    {
        if(statusProjectRoot is not {} root){Said="Open a project folder first.";return false;}
        var ok=CodeEditorTool.Open(root,root,null,out var note);Said=note;return ok;
    }
    internal bool OpenCodeDiagnostic(ProjectBuildError error)
    {
        if(statusProjectRoot is not {} root){projectDiagnosticNote="This build has no project folder.";return false;}
        return CodeEditorTool.Open(root,codeWorkingFolder??root,error,out projectDiagnosticNote);
    }
    internal void AcceptBuildOutput(ProjectBuildResult result,PlayLaunch launch)
    {
        ClearProjectConsole();buildInConsole=true;buildConsoleStatus=result.Summary;
        codeWorkingFolder=launch.Build?.WorkingDirectory??launch.ProjectRoot;
        CollectProjectOutput(result.Output.Select(s=>new ProjectPlayOutput(ProjectBuildCommand.ReadError(s) is not null,s)).ToArray());
        focusProjectConsole=true;
    }
}
