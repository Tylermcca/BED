using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private sealed record ReplaceFile(DataFile File,DocumentSession<DataDocument> Session,DataDocument Document,long Revision,string? Saved);
    private sealed class ReplaceMatch(ReplaceFile file,string pointer,JsonElement before,JsonElement after,string? refusal)
    {
        public readonly ReplaceFile Source=file;
        public readonly string Pointer=pointer;
        public readonly JsonElement Before=before,After=after;
        public readonly string? Refusal=refusal;
        public bool Chosen=refusal is null;
    }
    private readonly ImInputString replaceFind=new(4096,""u8),replaceWith=new(4096,""u8);
    private readonly List<ReplaceMatch> replaceMatches=[];
    private readonly List<string> replaceNotes=[];
    private string replaceStatus="Enter values, then preview the matches.";
    private bool replacePreview;
    private int replaceDrawFrame=-10;
    private const int ReplaceLimit=5000;
    private Panel FindReplacePanel=>new("project.find_replace","Find and replace",DrawFindReplace,OpenAtStart:false,Width:650,Height:500);

    private static bool ReplaceValue(string text,out JsonElement value,out string? why)
    {
        value=default;why=null;
        try
        {
            using var parsed=JsonDocument.Parse(text,new(){MaxDepth=8});
            if(parsed.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {why="Use quoted text, a number, true, false or null.";return false;}
            value=parsed.RootElement.Clone();return true;
        }
        catch(JsonException){why="Use quoted text, a number, true, false or null.";return false;}
    }
    private static IEnumerable<(string Pointer,JsonElement Value)> ReplaceValues(JsonElement value,string pointer="")
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in value.EnumerateObject())
            {
                if(pointer.Length==0&&property.Name=="$schema")continue;
                foreach(var row in ReplaceValues(property.Value,pointer+Pointer.Join([property.Name])))yield return row;
            }
        }
        else if(value.ValueKind==JsonValueKind.Array)
        {
            var index=0;
            foreach(var item in value.EnumerateArray())
            {foreach(var row in ReplaceValues(item,pointer+Pointer.Join([(index++).ToString(System.Globalization.CultureInfo.InvariantCulture)])))yield return row;}
        }
        else if(pointer.Length>0)yield return(pointer,value.Clone());
    }
    private void PreviewReplace()
    {
        replaceMatches.Clear();replaceNotes.Clear();replacePreview=false;
        if(!ReplaceValue(replaceFind.ToString(),out var find,out var why)||!ReplaceValue(replaceWith.ToString(),out var replacement,out why))
        {replaceStatus=why!;return;}
        if(JsonSchemas.Same(find,replacement)){replaceStatus="The replacement is the same value. Choose a different value.";return;}
        foreach(var file in Project.Files.OrderBy(f=>f.Name,StringComparer.Ordinal))
        {
            if(Documents.IsScene(file.Name)){replaceNotes.Add(file.Name+": scene documents use scene editing.");continue;}
            if(!file.Open()){replaceNotes.Add(file.Name+": "+string.Join("; ",file.Problems.Select(i=>i.Message)));continue;}
            var session=file.Session!;var captured=new ReplaceFile(file,session,session.Current,session.Revision,session.ExpectedOnDisk);
            foreach(var (pointer,value) in ReplaceValues(session.Current.Root))
            {
                if(!JsonSchemas.Same(value,find))continue;
                if(replaceMatches.Count==ReplaceLimit)
                {
                    replaceMatches.Clear();replaceStatus=$"More than {ReplaceLimit} matches. Search for a more specific value.";return;
                }
                replaceMatches.Add(new(captured,pointer,value,replacement,file.ReadOnlyReason));
            }
        }
        replacePreview=true;
        replaceStatus=$"{replaceMatches.Count} matches in {replaceMatches.Select(m=>m.Source.File.Name).Distinct().Count()} files. Choose matches, then Replace chosen.";
    }
    private string? ReplaceStillCurrent(ReplaceFile source)
    {
        if(!ReferenceEquals(Project.Find(source.File.Name),source.File)||!ReferenceEquals(source.File.Session,source.Session)
            ||!ReferenceEquals(source.Session.Current,source.Document)||source.Session.Revision!=source.Revision||source.Session.ExpectedOnDisk!=source.Saved)
            return source.File.Name+": the document changed. Preview the matches again.";
        if(source.File.ReadOnlyReason is {} reason)return source.File.Name+": "+reason;
        if(source.Session.CheckDisk()!=DiskState.Unchanged)return source.File.Name+": the saved file changed outside BED. Resolve it, then preview again.";
        return null;
    }
    private void ApplyReplace()
    {
        if(projectPlay is {HasCompleted:false}||Playing is not null){replaceStatus="Stop Play before replacing values.";return;}
        var chosen=replaceMatches.Where(m=>m.Chosen&&m.Refusal is null).GroupBy(m=>m.Source.File.Name).ToArray();
        if(!replacePreview||chosen.Length==0){replaceStatus="Choose at least one match from a current preview.";return;}
        var plans=new List<(ReplaceFile Source,EditorModel<DataDocument> Editor,SetValues Command,string[] Touches)>();
        foreach(var group in chosen)
        {
            var captured=group.First().Source;
            if(ReplaceStillCurrent(captured) is {} stale){replaceStatus=stale;return;}
            if(!windows.TryGetValue(group.Key,out var window)||!ReferenceEquals(window.Editor.Session,captured.Session))
                windows[group.Key]=window=Make(captured.File);
            var editor=window.Editor;
            var command=new SetValues(group.Select(m=>new SetValue(m.Pointer,m.After)).ToArray());
            var candidate=command.Apply(captured.Document);
            var issues=candidate.Ok?captured.Session.Validator.Validate(candidate.Document!):candidate.Issues;
            if(!candidate.Ok||issues.Any(i=>i.Severity==Severity.Error))
            {replaceStatus=group.Key+": "+string.Join("; ",issues.Where(i=>i.Severity==Severity.Error).Select(i=>i.Message));return;}
            var known=editor.Outline.ObjectIds(captured.Document).ToHashSet(StringComparer.Ordinal);
            var touches=group.Select(m=>
            {
                var parts=Pointer.Split(m.Pointer);
                return Enumerable.Range(0,parts.Count+1).Reverse().Select(n=>Pointer.Join(parts.Take(n))).FirstOrDefault(known.Contains)??m.Pointer;
            }).Distinct(StringComparer.Ordinal).ToArray();
            plans.Add((captured,editor,command,touches));
        }
        var done=0;
        foreach(var plan in plans)
        {
            if(ReplaceStillCurrent(plan.Source) is {} stale){replaceStatus=$"Changed {done} files. "+stale;replacePreview=false;return;}
            if(!plan.Editor.ExecuteAt(plan.Source.Revision,[plan.Command],plan.Touches))
            {
                replaceStatus=$"Changed {done} files. "+plan.Source.File.Name+": "+string.Join("; ",plan.Editor.Refused.Concat(plan.Source.Session.LastIssues).Select(i=>i.Message));
                replacePreview=false;return;
            }
            done++;
        }
        replacePreview=false;replaceStatus=$"Replaced {chosen.Sum(g=>g.Count())} values in {done} files. One undo step per file; save to keep the changes.";
    }
    private void DrawFindReplace()
    {
        var frame=ImGui.GetFrameCount();
        if(frame>replaceDrawFrame+1){ImGui.SetWindowFocus();ImGui.SetKeyboardFocusHere();}
        replaceDrawFrame=frame;
        Ui.TextWrapped("Preview replacements before applying."u8);
        var findChanged=Ui.InputText("Find value"u8,replaceFind);
        var replacementChanged=Ui.InputText("Replace with"u8,replaceWith);
        if(findChanged||replacementChanged){replacePreview=false;replaceMatches.Clear();replaceStatus="Values changed. Preview the matches again.";}
        if(Ui.Button("Preview matches"u8))PreviewReplace();
        var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
        void Inline(string label){if(ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+ImGui.CalcTextSize(label).X+ImGui.GetStyle().FramePadding.X*2<=right)ImGui.SameLine();}
        Inline("Replace chosen");
        var unavailable=projectPlay is {HasCompleted:false}||Playing is not null?"Stop Play before replacing values.":
            !replacePreview||!replaceMatches.Any(m=>m.Chosen&&m.Refusal is null)?"Choose matches from a current preview.":null;
        Ui.BeginDisabled(unavailable is not null,unavailable);
        if(Ui.Button("Replace chosen"u8))ApplyReplace();
        Ui.EndDisabled();Inline("Close find and replace");
        if(Ui.Button("Close find and replace"u8)||ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false))
        {Window?.Editor.SetOpen("project.find_replace",false);return;}
        Ui.TextWrapped(replaceStatus);
        foreach(var note in replaceNotes.Take(10))Ui.TextWrapped(note);
        if(replaceNotes.Count>10)Ui.TextWrapped($"{replaceNotes.Count-10} more files could not be searched.");
        if(Ui.BeginChild("Replacement preview"u8,new Brutal.Numerics.float2(0,0),ImGuiChildFlags.Borders))
        {
            var clipper=new ImGuiListClipper();clipper.Begin(replaceMatches.Count,-1);
            while(clipper.Step())
                for(var i=clipper.DisplayStart;i<clipper.DisplayEnd;i++)
                {
                    var match=replaceMatches[i];
                    Ui.BeginDisabled(match.Refusal is not null,match.Refusal);
                    Ui.Checkbox($"{match.Source.File.Name} {match.Pointer}: {match.Before.GetRawText()} -> {match.After.GetRawText()}##replace.{match.Source.File.Name}.{match.Pointer}",ref match.Chosen);
                    Ui.EndDisabled();
                }
            clipper.End();
        }
        Ui.EndChild();
    }
}
