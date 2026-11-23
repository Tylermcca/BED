using Bed.Editor;
using Bed.Editor.ImGui;
namespace Bed.Editor.App;

// Uses the AI observation route, one owned child and a serial worker. No desktop keys/window.
internal sealed class ProjectLiveSession(PlayLaunch launch,PlayApprovals approvals,LiveValueTarget target):IProjectPlaySession,ILivePlaySession
{
    private readonly PlayObservation observation=new();
    private Task<LiveValuesSnapshot>? pending;
    private Task? ending;
    private bool stopped,started;
    private readonly Dictionary<string,float> desired=new(StringComparer.Ordinal);
    public LiveValueTarget Target=>target;
    public LiveValuesSnapshot? Values {get;private set;}
    public bool Busy=>pending is not null||ending is not null||desired.Count>0;
    public bool CanBuildAndPlay=>false;
    public bool HasCompleted=>stopped;
    public int? ExitCode=>null;
    public string Status {get;private set;}="Starting live values.";
    public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false)
    {
        if(started||hasUnsavedChanges||buildAndPlay)return false;
        started=true;pending=Task.Run(()=>{observation.Start(launch,approvals,false,target.ObjectId);return observation.ReadValues(target);});return true;
    }
    public void Update()
    {
        if(ending is {IsCompleted:true})
        {try{ending.GetAwaiter().GetResult();Status="Stopped live Play.";}catch(Exception e){Status="Could not stop live Play: "+e.Message;}ending=null;stopped=true;return;}
        if(ending is not null||stopped)return;
        if(pending is {IsCompleted:true})
        {
            try{Values=pending.GetAwaiter().GetResult();Status="Live values are ready. The game pauses between updates.";}
            catch(Exception e){Status="Live Play unavailable: "+e.Message;observation.Dispose();stopped=true;}
            pending=null;
        }
        if(started&&!stopped&&pending is null&&desired.Count>0){var edit=desired.First();desired.Remove(edit.Key);pending=Task.Run(()=>observation.ReadValues(target,field:edit.Key,value:edit.Value));}
    }
    public bool TryValue(string field,float value)
    {
        if(ending is not null||!started||stopped||target.Values.FirstOrDefault(v=>v.Id==field)?.Accepts(value)!=true)return false;
        desired[field]=value;return true;
    }
    public bool TryKey(string key)
    {
        if(Busy||!started||stopped||!target.Inputs.Contains(key,StringComparer.Ordinal))return false;
        pending=Task.Run(()=>observation.ReadValues(target,key,.25));return true;
    }
    public void RequestStop()
    {
        if(stopped||ending is not null)return;
        Status="Stopping live Play.";var work=pending;
        ending=Task.Run(async()=>{if(work is not null)try{await work.ConfigureAwait(false);}catch(Exception){}observation.Dispose();});
    }
    public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
    public void Draw() { }
    public void Dispose()
    {
        RequestStop();
        // Workspace loss must observe exit before releasing the project folder.
        ending?.WaitAsync(TimeSpan.FromSeconds(16)).GetAwaiter().GetResult();stopped=true;
    }
}
