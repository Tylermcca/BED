using Bed.Editor;
using Bed.Editor.ImGui;
namespace Bed.Editor.App;

// One serial worker and the existing bounded protocol; never captures a desktop window.
internal sealed class ProjectPictureSession(PlayLaunch launch,PlayApprovals approvals,Func<byte[],(int Width,int Height,byte[] Rgba)> decode):IProjectPlaySession,IGamePictureSession
{
    private readonly PlayObservation observation=new();
    private Task<GamePictureFrame>? pending;
    private Task? ending;
    private bool started,stopped;
    public GamePictureFrame? Picture {get;private set;}
    public bool Busy=>pending is not null||ending is not null;
    public bool CanBuildAndPlay=>false;
    public bool HasCompleted=>stopped;
    public int? ExitCode=>null;
    public string Status {get;private set;}="Starting picture run.";
    public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false)
    {
        if(started||hasUnsavedChanges||buildAndPlay)return false;
        started=true;pending=Task.Run(()=>{observation.Start(launch,approvals,false);return Capture();});return true;
    }
    private GamePictureFrame Capture()
    {
        var reply=observation.Request("shot");
        var bytes=PlayObservation.ValidatePng(reply["imageData"]!.GetValue<string>());
        var image=decode(bytes);
        return new(image.Width,image.Height,image.Rgba,reply["frame"]!.GetValue<long>(),reply["description"]!.GetValue<string>());
    }
    public void Update()
    {
        if(ending is {IsCompleted:true})
        {try{ending.GetAwaiter().GetResult();Status="Picture run stopped.";}catch(Exception e){Status="Picture run could not stop: "+e.Message;}ending=null;stopped=true;return;}
        if(ending is not null||stopped||pending is not {IsCompleted:true})return;
        try{Picture=pending.GetAwaiter().GetResult();Status="Picture ready. The game pauses between requests.";}
        catch(Exception e){Status="Game picture unavailable: "+e.Message;observation.Dispose();stopped=true;}
        pending=null;
    }
    public bool TakePicture()
    {
        if(!started||stopped||Busy)return false;
        Status="Taking game picture.";pending=Task.Run(Capture);return true;
    }
    public void RequestStop()
    {
        if(stopped||ending is not null)return;
        Status="Stopping picture run.";var work=pending;
        ending=Task.Run(async()=>{if(work is not null)try{await work.ConfigureAwait(false);}catch(Exception){}observation.Dispose();});
    }
    public IReadOnlyList<ProjectPlayOutput> DrainLogs()=>[];
    public void Draw() { }
    public void Dispose(){RequestStop();ending?.WaitAsync(TimeSpan.FromSeconds(16)).GetAwaiter().GetResult();stopped=true;}
}
