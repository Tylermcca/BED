using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;
internal sealed class ProblemReportDialog
{
    private bool open,focus,version=true,windows=true,kind=true,logs=true,picture;
    private readonly ImInputString happened=new(4096,""u8),expected=new(4096,""u8),folder=new(2048,""u8);
    private byte[]? png;private string status="",repository=ProblemReport.DefaultRepository;private ProblemReportContext context=new();private string[] log=[];
    internal Func<ProblemReportContext> Context=()=>new();
    internal Action<string> Copy=text=>ImGui.SetClipboardText(text);
    internal Action<string> Browse=url=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    internal Func<ProblemReportContext,string[]> ReadLog=c=>ProblemReportLog.Read(c.Preferences);
    internal Func<ProblemReportContext,string> Repository=c=>ProblemReportSettings.Read(c.Preferences).Repository;
    internal Func<byte[]?>? Capture;
    internal bool HasKeyboardFocus {get;private set;}
    internal string Preview=>ProblemReport.Format(context,happened.ToString(),expected.ToString(),log,version,windows,kind,logs);
    internal void Open(){context=Context();log=ReadLog(context);png=null;picture=false;open=true;focus=true;status=ProblemReportSettings.Read(context.Preferences).Note;repository=Repository(context);}
    internal void Draw()
    {
        if(!open){HasKeyboardFocus=false;return;}
        var vp=ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos+vp.Size*.5f,ImGuiCond.FirstUseEver,new float2(.5f,.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(vp.Size.X-32,MathF.Max(760,ImGui.GetFontSize()*48)),vp.Size.Y-32),ImGuiCond.FirstUseEver);
        if(focus)ImGui.SetNextWindowFocus();
        if(Ui.Begin("Report a problem###bed.report"u8,ref open,ImGuiWindowFlags.NoDocking))
        {
            HasKeyboardFocus=ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            if(focus){ImGui.SetKeyboardFocusHere();focus=false;}
            Ui.Text("What happened"u8);Ui.NameNextItem("##report.happened"u8,"What happened");
            Ui.InputTextMultiline("##report.happened"u8,happened,new float2(-1,ImGui.GetTextLineHeight()*3));
            Ui.Text("What you expected"u8);Ui.NameNextItem("##report.expected"u8,"What you expected");
            Ui.InputTextMultiline("##report.expected"u8,expected,new float2(-1,ImGui.GetTextLineHeight()*2));
            Ui.Checkbox("BED version"u8,ref version);ImGui.SameLine();Ui.Checkbox("Windows version"u8,ref windows);
            Ui.Checkbox("Project kind only"u8,ref kind);ImGui.SameLine();Ui.Checkbox("Last 200 BED log lines"u8,ref logs);
            Ui.BeginDisabled(Capture is null,"This host cannot capture its current window.");
            if(Ui.Checkbox("Include current-window picture"u8,ref picture))
            {
                png=null;
                if(picture)Try(()=>{png=Capture!();if(png is null){picture=false;status="The window picture was unavailable; no picture is attached.";}else status="Picture captured locally. Review the saved picture before sharing it.";});
            }
            Ui.EndDisabled();
            Ui.TextWrapped("Only the text below is copied or sent to the issue page. Pictures are saved locally; attach one yourself after reviewing it. Long browser reports are shortened; copy the full text if needed."u8);
            ImGui.SeparatorText("Exact report text"u8);
            if(Ui.BeginChild("Report preview###bed.report.preview"u8,new float2(0,MathF.Max(100,ImGui.GetContentRegionAvail().Y-ImGui.GetFrameHeightWithSpacing()*6)),ImGuiChildFlags.Borders))Ui.TextWrapped(Preview);
            Ui.EndChild();
            if(Ui.Button("Copy report"u8))Try(()=>{Copy(Preview);status="Copied the report text. No picture was copied.";});ImGui.SameLine();
            if(Ui.Button("Open the issue page"u8))Try(()=>{Browse(ProblemReport.IssueUrl(repository,Preview));status="Opened the issue page. Review it and submit there; no picture was uploaded.";});
            Ui.Text("Save folder"u8);ImGui.SetNextItemWidth(-1);Ui.NameNextItem("##report.folder"u8,"Save folder");Ui.InputText("##report.folder"u8,folder);
            Ui.BeginDisabled(context.Dialogs is null,"Type a folder, or use a host with native folder selection.");
            if(Ui.Button("Choose folder"u8))Try(()=>{if(context.Dialogs!.PickFolder("Save problem report",null) is {} selected)folder.SetValue(selected.AsSpan());});Ui.EndDisabled();
            if(Ui.Button("Save report"u8))Try(()=>{Save(folder.ToString());status="Saved the report locally. Review it before sharing.";});ImGui.SameLine();if(Ui.Button("Close report"u8))open=false;
            if(status.Length>0)Ui.StatusWrapped(ProblemReport.Redact(status,context.Root),"bed.report.status");
            if(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))open=false;
        }
        Ui.End();
    }
    internal string Save(string destination)
    {
        if(string.IsNullOrWhiteSpace(destination)||!Path.IsPathFullyQualified(destination)||!Directory.Exists(destination))throw new IOException("Choose an existing absolute destination folder.");
        var stem=Path.Combine(destination,"bed-report-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        if(picture&&png is null)throw new IOException("Capture the picture again before saving it.");
        // Unique names never replace a person's existing report or image.
        if(picture)using(var file=new FileStream(stem+".png",FileMode.CreateNew))file.Write(png!);
        using(var file=new FileStream(stem+".md",FileMode.CreateNew))using(var text=new StreamWriter(file))text.Write(Preview);
        return stem+".md";
    }
    private void Try(Action action){try{action();}catch(Exception error)when(error is not OutOfMemoryException){status=error.Message;}}
}
