namespace Bed.Editor.ImGui;
public sealed partial class DataWorkspace
{
    internal long TourStopRequests {get;private set;}
    internal void BindTour(Action? start)
    {
        foreach(var window in windows.Values)window.TakeTourAction=start;
        foreach(var window in sceneWindows.Values)window.TakeTourAction=start;
        foreach(var binding in scene2Windows.Values)binding.Window.TakeTourAction=start;
    }
}
