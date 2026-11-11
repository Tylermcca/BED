namespace Bed.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private Func<IFileDialogs?>? reportDialogs;private string? reportPreferences;
    private void BindProblemReport<T>(EditorWindow<T> window) where T:class
    {
        window.ProblemReport.Context=()=>new(typeof(T)==typeof(Scene2)?(window.Editor.Session.Current is Scene2 {Space:SceneSpace.TwoDXY}?"2D level":"3D level"):typeof(T)==typeof(Scene)?"3D level":"Data",statusProjectRoot??Project.DataFolder,reportPreferences,reportDialogs?.Invoke());
        window.ProblemReport.Capture=()=>ProblemReportHost.Capture?.Invoke(window.ProblemReport.Context());
    }
}
