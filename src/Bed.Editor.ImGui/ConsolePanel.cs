using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Read-only linked Play console over a bounded buffer. Hosts register AsPanel explicitly; this class does
/// not alter existing Play, menus or editor lifetime. Source actions are an exact captured host-provided map,
/// never a wildcard path/URL opener. Each action must revalidate current project admission/lifetime when invoked.</summary>
public sealed class ConsolePanel
{
    private readonly PlayLogBuffer logs;
    private readonly Dictionary<string,Func<PlaySourceLink,bool>> sources;
    /// <summary>Creates a presentation without opening files or registering a panel. Source actions return false if
    /// their captured project/file is no longer admitted. Without an exact action, navigation is disabled.</summary>
    public ConsolePanel(PlayLogBuffer logs,IReadOnlyDictionary<string,Func<PlaySourceLink,bool>>? sources=null)
    {
        ArgumentNullException.ThrowIfNull(logs); this.logs=logs;
        this.sources=new(StringComparer.Ordinal);
        foreach(var pair in sources ?? new Dictionary<string,Func<PlaySourceLink,bool>>())
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            this.sources.Add(pair.Key,pair.Value);
        }
    }
    /// <summary>Latest source-navigation outcome, separate from the game-owned log text.</summary>
    public string Said { get; private set; }="";
    /// <summary>A standalone initially closed Console panel; host attachment belongs to the later Play wiring.</summary>
    public Panel AsPanel(string id="editor.linked-play.console")=>new(id,"Console",Draw,OpenAtStart:false,Width:720,Height:440);

    /// <summary>Draws severity, input/session identity, bounded records and visible loss counters through the shared
    /// UI tree. Text is plain data; no message or source identity becomes a command or URI.</summary>
    public void Draw()
    {
        Ui.TextWrapped($"Play session {logs.Correlation.SessionId:N}, input revision {logs.Correlation.Revision}",semanticId:"play.console.session");
        Ui.TextWrapped($"{logs.Count} messages; {logs.DroppedCount} dropped; {logs.TruncatedCount} shortened; {logs.OmittedSourceCount} source links omitted.",
            semanticId:"play.console.loss");
        if(Said.Length>0) Ui.TextWrapped(Said,semanticId:"play.console.notice");
        if(Ui.BeginChild("Play messages"u8))
        {
            foreach(var record in logs.Snapshot())
            {
                ImGui.PushID(record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Ui.FieldCaption($"{record.Severity}: {record.Message}",semanticId:"play.console.record."+record.Sequence);
                if(record.Source is { } source)
                {
                    Ui.FieldCaption(source.DocumentId+(source.Line is { } line ? $":{line}" : "")
                        +(source.ObjectId is { } target ? $" ({target})" : ""));
                    var available=sources.TryGetValue(source.DocumentId,out var open);
                    Ui.BeginDisabled(!available,"There's no way to jump to the source from this file.");
                    if(Ui.Button("Open source"u8) && open is not null)
                    {
                        try { Said=open(source) ? "Opened source." : "Source is no longer available in this project."; }
                        catch(Exception error) when(error is not OutOfMemoryException)
                        { Said="Could not open source."; }
                    }
                    Ui.EndDisabled();
                }
                else if(record.SourceOmitted) Ui.TextWrapped("Source link omitted."u8);
                ImGui.PopID();
            }
        }
        Ui.EndChild();
    }
}
