using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Editor.ImGui;

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
        var dirty=Project.Files.Count(file=>file.Dirty||Documents.TryGetOpenScene(file.Name,out var scene)&&scene!.Session.Dirty||Documents.TryGetOpenScene2(file.Name,out var v2)&&v2!.Session.Dirty);
        var project=statusProjectRoot??Project.DataFolder;var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(project));
        string[] labels=["Project: "+(name.Length==0?project:name+" ("+project+")"),"Active file: "+(Active?.Name??"None"),
            "Unsaved changes: "+(dirty==0?"None":dirty+ (dirty==1?" file":" files")),"Play: "+WorkspacePlayStatus()];
        string[] ids=["workspace.status.project","workspace.status.file","workspace.status.unsaved","workspace.status.play"];
        var viewport=Im.GetMainViewport();var style=Im.GetStyle();
        var columns=viewport.Size.X<Im.GetFontSize()*32?1:viewport.Size.X<Im.GetFontSize()*64?2:4;var rows=4/columns;
        var width=MathF.Max(1,(viewport.Size.X-2*style.WindowPadding.X-(columns-1)*style.ItemSpacing.X)/columns);
        var visible=(string[])labels.Clone();visible[0]="Project: "+(name.Length==0?project:name);
        var rowHeights=Enumerable.Range(0,rows).Select(row=>Enumerable.Range(row*columns,columns).Max(i=>Im.CalcTextSize(visible[i],false,width).Y)).ToArray();
        var height=rowHeights.Sum()+(rows-1)*style.ItemSpacing.Y+2*style.WindowPadding.Y;
        if(Ui.BeginWorkspaceStatusBar(height))
        {
            var origin=Im.GetCursorScreenPos();var y=origin.Y;
            for(var row=0;row<rows;row++)
            {
                for(var col=0;col<columns;col++)
                {
                    var i=row*columns+col;var at=new float2(origin.X+col*(width+style.ItemSpacing.X),y);
                    Im.SetCursorScreenPos(at);Im.PushTextWrapPos(Im.GetCursorPosX()+width);
                    Ui.SummaryWrapped(visible[i],labels[i],ids[i]);Im.PopTextWrapPos();
                }
                y+=rowHeights[row]+style.ItemSpacing.Y;
            }
        }
        Ui.End();
    }
}
