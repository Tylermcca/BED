using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im=Brutal.ImGuiApi.ImGui;
namespace Brutal.Authoring.Editor.ImGui;

public sealed partial class DataWorkspace
{
    private string? statusProjectRoot;
    private string WorkspacePlayStatus()
    {
        if(playSavePrompt)return "Waiting for save choice.";
        if(playApprovalPrompt)return "Waiting for command approval.";
        if(projectPlay is {HasCompleted:false} running)return running.Status;
        var embedded=windows.Values.Select(window=>window.Editor.Game).FirstOrDefault(game=>game is not null)
            ??sceneWindows.Values.Select(window=>window.Editor.Game).FirstOrDefault(game=>game is not null);
        return embedded?.Status??projectPlay?.Status??"Not running.";
    }
    private void DrawWorkspaceStatusBar()
    {
        var dirty=Project.Files.Count(file=>file.Dirty||Documents.TryGetOpenScene(file.Name,out var scene)&&scene!.Session.Dirty);
        var project=statusProjectRoot??Project.DataFolder;var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(project));
        string[] labels=["Project: "+(name.Length==0?project:name+" ("+project+")"),"Active file: "+(Active?.Name??"None"),
            "Unsaved changes: "+(dirty==0?"None":dirty+ (dirty==1?" file":" files")),"Play: "+WorkspacePlayStatus()];
        string[] ids=["workspace.status.project","workspace.status.file","workspace.status.unsaved","workspace.status.play"];
        var viewport=Im.GetMainViewport();var style=Im.GetStyle();
        var columns=viewport.Size.X<500?1:viewport.Size.X<1000?2:4;var rows=4/columns;
        var line=Im.GetTextLineHeight();var height=rows*line+(rows-1)*style.ItemSpacing.Y+2*style.WindowPadding.Y;
        if(Ui.BeginWorkspaceStatusBar(height))
        {
            var origin=Im.GetCursorScreenPos();var width=MathF.Max(1,(Im.GetContentRegionAvail().X-(columns-1)*style.ItemSpacing.X)/columns);
            var draw=Im.GetWindowDrawList();
            for(var i=0;i<labels.Length;i++)
            {
                var at=origin+new float2((i%columns)*(width+style.ItemSpacing.X),(i/columns)*(line+style.ItemSpacing.Y));
                Im.SetCursorScreenPos(at);draw.PushClipRect(at,at+new float2(width,line),true);
                // Full accessible labels survive clipping; Ui records text evidence and supplies the full-text tooltip.
                Ui.Text(labels[i],ids[i]);draw.PopClipRect();
            }
        }
        Ui.End();
    }
}
