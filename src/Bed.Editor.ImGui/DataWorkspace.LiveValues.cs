using System.Globalization;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
public sealed partial class DataWorkspace
{
    private LiveValueTarget[] liveTargets=[];
    private LiveValueTarget? pendingLiveTarget;
    private string? chosenLiveObject,lastLiveSelection,liveScene;
    private Scene2DocumentEntry? liveEntry;
    private readonly Dictionary<string,ImInputString> liveInputs=new(StringComparer.Ordinal);
    private string liveNote="";
    internal void BindLiveValues(IEnumerable<LiveValueTarget> targets)
    {
        var copied=targets.Take(65).ToArray();
        if(copied.Length>64||copied.Any(t=>t is null)||copied.Select(t=>t.ObjectId).Distinct(StringComparer.Ordinal).Count()!=copied.Length)throw new ArgumentException("Use at most 64 unique live objects.");
        liveTargets=copied;chosenLiveObject=lastLiveSelection=null;
    }
    private LiveValueTarget? SelectedLiveTarget(Scene2DocumentEntry? entry=null)
    {
        if(entry is null&&Active is {} file)Documents.TryGetOpenScene2(file.Name,out entry);
        var primary=entry?.Editor.Primary??SceneWindow?.Editor.Primary??Window?.Editor.Primary;
        if(primary!=lastLiveSelection){lastLiveSelection=primary;chosenLiveObject=null;}
        return liveTargets.FirstOrDefault(t=>t.ObjectId==chosenLiveObject)??liveTargets.FirstOrDefault(t=>t.ObjectId==primary)??(liveTargets.Length==1?liveTargets[0]:null);
    }
    internal bool RequestLivePlay()
    {
        if(projectPlayHost is not ILivePlayHost){Said="This BED installation cannot show live values.";return false;}
        if(projectPlay is {HasCompleted:false}){Said="Stop the running program first.";return false;}
        if(SelectedLiveTarget() is not {} target){Said="This game doesn't share live values for the selected object.";return false;}
        liveScene=Active?.Name;liveEntry=null;if(liveScene is {} name)Documents.TryGetOpenScene2(name,out liveEntry);
        pendingLiveTarget=target;requestingLive=true;liveInputs.Clear();liveNote="";
        try{return RequestProjectPlay();}finally{requestingLive=false;}
    }
    internal bool KeepLiveValue(string name,Scene2DocumentEntry entry,string? field=null)
    {
        if(projectPlay is not ILivePlaySession live||live.Busy||live.Values is not {} values||projectPlay.HasCompleted)
        {liveNote="Wait for the game to confirm the value.";return false;}
        var definition=live.Target.Values.FirstOrDefault(v=>v.Id==field||(field is null&&v.SaveField is not null));
        if(definition?.SaveField is not {} save){liveNote="You can try this value, but this game cannot save it.";return false;}
        if(name!=liveScene||!ReferenceEquals(entry,liveEntry)){liveNote="Return to the level that started live Play.";return false;}
        if(entry.Editor.Primary!=live.Target.ObjectId||ProjectDirty())
        {liveNote="Select the live object and save other edits before keeping this value.";return false;}
        if(!Documents.TryGetOpenScene2(name,out var current)||!ReferenceEquals(entry,current))return false;
        var index=live.Target.Values.ToList().IndexOf(definition);var number=values.Numbers[index];
        if(!definition.Accepts(number)){liveNote="The game returned a value outside the allowed range.";return false;}
        if(!Documents.CaptureScene2Edit(name,"set",Scene2InspectorType.J(new{id=live.Target.ObjectId,field=save,value=number}),out var plan,out liveNote)
            ||!Documents.CommitScene2Edit(name,plan!,out liveNote)||!Documents.SaveScene2(name,out liveNote))return false;
        liveNote="Kept "+definition.Label+" in the level. Build again before the next Play.";return true;
    }
    internal void DrawLiveValues(string name,Scene2DocumentEntry? entry)
    {
        if(liveTargets.Length==0){Ui.TextWrapped("This game doesn't share live values."u8);return;}
        var running=projectPlay is ILivePlaySession&&!projectPlay.HasCompleted;
        var selected=SelectedLiveTarget(entry);
        if(liveTargets.Length>1)
        {
            Ui.BeginDisabled(running,"Stop live Play before choosing another object.");
            if(Ui.BeginCombo("Live object"u8,selected?.Title??"Choose an object"))
            {foreach(var target in liveTargets)if(Ui.Selectable(target.Title,ReferenceEquals(target,selected))){chosenLiveObject=target.ObjectId;selected=target;}Ui.EndCombo();}
            Ui.EndDisabled();
        }
        if(projectPlay is not ILivePlaySession live||projectPlay.HasCompleted)
        {
            Ui.TextWrapped("Runs without opening a game window. Game time pauses between requests."u8);
            Ui.BeginDisabled(selected is null||projectPlay is {HasCompleted:false},selected is null?"Select an object that shares live values.":"Stop the running program first.");
            if(Ui.Button("Start live values"u8))RequestLivePlay();Ui.EndDisabled();return;
        }
        Ui.TextWrapped(live.Target.Title+". "+projectPlay.Status);
        if(selected?.ObjectId!=live.Target.ObjectId)Ui.TextWrapped("Stop live Play before inspecting another object."u8);
        if(live.Values is {} snapshot)
        {
            for(var i=0;i<live.Target.Values.Count;i++)
            {
                var definition=live.Target.Values[i];var suffix=i==live.Target.Values.Count-1?$" (frame {snapshot.Frame})":"";
                Ui.Text($"{definition.Label}: {snapshot.Numbers[i]:0.###} {definition.Unit}{suffix}");
            }
            foreach(var definition in live.Target.Values.Where(v=>v.CanTry))
            {
                var i=live.Target.Values.ToList().IndexOf(definition);var number=snapshot.Numbers[i];
                if(!liveInputs.TryGetValue(definition.Id,out var text))liveInputs[definition.Id]=text=new(64,number.ToString(CultureInfo.InvariantCulture));
                Brutal.ImGuiApi.ImGui.PushID(definition.Id);
                var label=definition.TrialLabel??$"Try {definition.Label} ({definition.Minimum} to {definition.Maximum})";
                if(Ui.InputText(label,text))
                {if(float.TryParse(text.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var wanted)&&live.TryValue(definition.Id,wanted))liveNote="Trying this value; saved files unchanged.";else liveNote="Use a number inside the displayed range.";}
                Brutal.ImGuiApi.ImGui.PopID();
            }
            Ui.BeginDisabled(live.Busy,"Waiting for the game.");
            foreach(var key in live.Target.Inputs)if(Ui.Button("Try "+key+" (quarter second)"))live.TryKey(key);
            foreach(var definition in live.Target.Values.Where(v=>v.SaveField is not null))
            {
                var index=live.Target.Values.ToList().IndexOf(definition);var text=liveInputs[definition.Id];
                var confirmed=float.TryParse(text.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var typed)&&typed==snapshot.Numbers[index];
                Brutal.ImGuiApi.ImGui.PushID(definition.Id);
                Ui.BeginDisabled(!confirmed||!definition.Accepts(snapshot.Numbers[index])||entry is null,"Wait for the game to confirm the value, and open the level where you started live Play.");
                if(Ui.Button("Keep this value"u8)&&entry is not null)KeepLiveValue(name,entry,definition.Id);
                Ui.EndDisabled();Brutal.ImGuiApi.ImGui.PopID();
            }
            Ui.EndDisabled();
        }
        Ui.TextWrapped(liveNote);
    }
}
