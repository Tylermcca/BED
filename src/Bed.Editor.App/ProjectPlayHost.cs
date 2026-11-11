using System.Text.RegularExpressions;
using Bed.Editor;
using Bed.Editor.ImGui;

namespace Bed.Editor.App;

/// <summary>Plain saved-files process state. Running means OS process creation, never game readiness.</summary>
public enum ProjectPlayState
{
    /// <summary>No process has been attempted.</summary>
    Created,
    /// <summary>Program is missing or older than declared inputs.</summary>
    NeedsBuild,
    /// <summary>The declared build command is running.</summary>
    Building,
    /// <summary>The OS program started; no game-readiness assertion.</summary>
    Running,
    /// <summary>Polite close requested; owned-tree grace is active.</summary>
    Stopping,
    /// <summary>Actual owned completion was observed.</summary>
    Stopped,
    /// <summary>Admission, process, output or deadline failed.</summary>
    Failed
}
/// <summary>Bounded plain output from an owned build/program; never parsed as protocol.</summary>
public sealed record ProjectPlayLog(bool Error,string Text);

/// <summary>Single saved-files launch owner. No editor/session/save callback, snapshot or game protocol is retained.
/// Call Update on the owner thread and Dispose on workspace loss. The caller admits the resolved command and enforces user-local approval before construction.</summary>
public sealed class ProjectPlayHost : IDisposable
{
    private readonly GameLaunchSpec run;
    private readonly GameLaunchSpec? build;
    private readonly string[] inputs;
    private readonly string root;
    private readonly string? buildStamp;
    private readonly TimeProvider clock;
    private readonly TimeSpan buildTimeout;
    private readonly int maxLine,maxPending;
    private readonly object gate=new();
    private readonly Queue<ProjectPlayLog> pending=[];
    private SupervisedProcess? child;
    // Owner-thread test observation only: completion still advances lifecycle through Update.
    internal Task OwnedCompletion => child?.Completion??Task.CompletedTask;
    private ProjectFrameServer? frameControl;
    internal ProjectFrameState FrameControl=>frameControl?.Snapshot??new(false,false,false,0,"This game hasn't enabled frame control yet.");
    internal bool RequestFrameControl(ProjectFrameCommand command)=>State==ProjectPlayState.Running&&frameControl?.Request(command)==true;
    private string? fault,requiredFramework;
    private long stateAt;
    private bool disposed,forced;
    /// <summary>Declared cooperative-stop grace; after it only this owner's job tree is terminated.</summary>
    public static readonly TimeSpan StopGrace=TimeSpan.FromSeconds(5);
    /// <summary>Constructs without starting a process or writing project data.</summary>
    public ProjectPlayHost(PlayLaunch launch,TimeProvider clock,TimeSpan buildTimeout,
        int maxLineCharacters=4096,int maxPendingLines=256)
    {
        ArgumentNullException.ThrowIfNull(launch);ArgumentNullException.ThrowIfNull(launch.Run);ArgumentNullException.ThrowIfNull(launch.BuildInputs);ArgumentNullException.ThrowIfNull(clock);
        var projectRoot=launch.ProjectRoot;
        if(!Path.IsPathFullyQualified(projectRoot))throw new ArgumentException("Choose an absolute project root.",nameof(projectRoot));
        if(buildTimeout<=TimeSpan.Zero||maxLineCharacters<=0||maxPendingLines<=0)throw new ArgumentOutOfRangeException(nameof(buildTimeout));
        run=launch.Run;build=launch.Build;inputs=launch.BuildInputs.ToArray();buildStamp=launch.BuildStamp;
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));this.clock=clock;
        this.buildTimeout=buildTimeout;maxLine=maxLineCharacters;maxPending=maxPendingLines;
    }
    /// <summary>Current owner-thread lifecycle.</summary>
    public ProjectPlayState State {get;private set;}
    /// <summary>Plain reason/status, including failures and forced stop outcome.</summary>
    public string Status {get;private set;}="Choose Play to run the saved project files.";
    /// <summary>Number of oldest pending log lines evicted at the declared bound.</summary>
    public long DiscardedLogLines {get;private set;}
    /// <summary>Actual last owned-process exit code, absent until exit.</summary>
    public int? ExitCode {get;private set;}
    /// <summary>Whether this owner has observed actual process completion.</summary>
    public bool HasCompleted=>child is null || child.Completion.IsCompleted;
    /// <summary>Whether a stale/missing program can be built from a declared command.</summary>
    public bool CanBuildAndPlay=>State==ProjectPlayState.NeedsBuild && build is not null;
    /// <summary>Starts only after the caller's existing save-first decision. Never saves or accepts draft input.</summary>
    public bool Start(bool hasUnsavedChanges,bool buildAndPlay=false)
    {
        if(disposed||State is not (ProjectPlayState.Created or ProjectPlayState.NeedsBuild))return false;
        if(hasUnsavedChanges){Status="Save changes before Play.";return false;}
        try
        {
            var program=run.Executable;var working=run.WorkingDirectory;
            if(!Directory.Exists(working))throw new InvalidOperationException("The Play working folder does not exist.");
            var newest=NewestInput(); // Validate every declared input before any build/program can start.
            var stale=!File.Exists(program)||(buildStamp is null?newest>File.GetLastWriteTimeUtc(program):!StampCurrent());
            if(stale&&!buildAndPlay)
            {
                State=ProjectPlayState.NeedsBuild;Status=build is not null
                    ? (buildStamp is null?"The program is missing or older than its build inputs. Choose Build and Play.":"Saved content is missing or changed. Choose Build and Play.")
                    : "The program is missing. Set its program or build command in Play settings.";
                return false;
            }
            if(stale)
            {
                if(build is null)throw new InvalidOperationException("Set a build command in Play settings first.");
                Launch(build,ProjectPlayState.Building,"Building saved project files.");
            }
            else Launch(run,ProjectPlayState.Running,"Running.");
            return true;
        }
        catch(Exception error)when(error is not OutOfMemoryException){Fail(error.Message);return false;}
    }
    private void Launch(GameLaunchSpec spec,ProjectPlayState state,string status)
    {
        child?.Dispose();fault=null;requiredFramework=null;forced=false;ExitCode=null;
        frameControl?.Dispose();frameControl=null;
        var environment=new Dictionary<string,string>{{"BED_PROJECT",root},{"BED_PLAY","1"},{"BED_FRAME_PIPE",""},{"BED_FRAME_TOKEN",""},{"BED_FRAME_VERSION",""}};
        if(state==ProjectPlayState.Running){frameControl=new(clock);frameControl.AddEnvironment(environment);}
        child=new(spec,1,maxLine,Record,why=>{lock(gate)fault??=why;},environment,plainOutput:true);
        frameControl?.Start(child.ProcessId);
        child.CloseInput(); // Plain Play supplies no stdin protocol or captured payload.
        State=state;Status=status;stateAt=clock.GetTimestamp();Log(false,status);
    }
    /// <summary>Requests polite close exactly once. This never closes an unrelated process/window.</summary>
    public bool RequestStop()
    {
        if(disposed||State is not (ProjectPlayState.Running or ProjectPlayState.Building))return false;
        frameControl?.Dispose();
        State=ProjectPlayState.Stopping;stateAt=clock.GetTimestamp();
        try
        {
            if(child!.RequestOwnedWindowClose())Status="Requested polite close; waiting up to 5 seconds.";
            else{forced=true;child.AbortOwned();Status="The program has no window to close, so BED ended it.";}
            Log(false,Status);return true;
        }
        catch(Exception error)when(error is not OutOfMemoryException){Fail("Could not request owned process close: "+error.Message);return true;}
    }
    /// <summary>Pumps deadlines and actual exits. This is the only thread that changes lifecycle/status.</summary>
    public void Update()
    {
        if(disposed)return;
        frameControl?.Update();
        string? problem;lock(gate){problem=fault;fault=null;}
        if(problem is not null&&!forced&&State is ProjectPlayState.Building or ProjectPlayState.Running or ProjectPlayState.Stopping){Fail(problem);return;}
        if(child is null)return;
        if(child.Completion.IsCompleted)
        {
            frameControl?.Dispose();
            if(State is ProjectPlayState.Stopped or ProjectPlayState.Failed)return;
            try
            {
                ExitCode=child.Completion.GetAwaiter().GetResult();
                if(forced){State=ProjectPlayState.Stopped;return;}
                if(ExitCode!=0){string? framework;lock(gate)framework=requiredFramework;Fail(DotNetLaunchFailure.Describe(ExitCode.Value,framework)??$"The process exited with code {ExitCode}.");return;}
                if(State==ProjectPlayState.Building)
                {
                    var program=run.Executable;
                    if(!File.Exists(program)||(buildStamp is null?NewestInput()>File.GetLastWriteTimeUtc(program):!StampCurrent()))throw new InvalidOperationException(buildStamp is null?"Build succeeded but the program is still missing or stale.":"Build succeeded but the program or declared content stamp is still missing or stale.");
                    Log(false,"Build succeeded.");Launch(run,ProjectPlayState.Running,"Running.");
                }
                else{State=ProjectPlayState.Stopped;Status="The process exited normally (code 0).";Log(false,Status);}
            }
            catch(Exception error)when(error is not OutOfMemoryException){Fail(error.Message);}
        }
        else if(State==ProjectPlayState.Stopping&&!forced&&clock.GetElapsedTime(stateAt)>=StopGrace)
        {
            forced=true;child.AbortOwned();Status="Stopped the owned process tree after the 5-second grace period.";Log(false,Status);
            // Keep pumping until actual completion before releasing the editing lifetime.
        }
        else if(State==ProjectPlayState.Building&&clock.GetElapsedTime(stateAt)>=buildTimeout)Fail("The build exceeded its declared deadline.");
    }
    /// <summary>Drains bounded pending stdout/stderr/status lines. No links or commands are inferred.</summary>
    public IReadOnlyList<ProjectPlayLog> DrainLogs(){lock(gate){var result=pending.ToArray();pending.Clear();return result;}}
    private void Record(string text,bool error)
    {
        if(error&&DotNetLaunchFailure.Framework(text) is {} framework)lock(gate)requiredFramework??=framework;
        Log(error,text);
    }
    private void Log(bool error,string text)
    {
        lock(gate)
        {
            if(disposed)return;
            if(pending.Count>=maxPending){pending.Dequeue();if(DiscardedLogLines<long.MaxValue)DiscardedLogLines++;}
            pending.Enqueue(new(error,text.Length>maxLine?text[..maxLine]:text));
        }
    }
    private void Fail(string why){frameControl?.Dispose();child?.AbortOwned();State=ProjectPlayState.Failed;Status="Play failed: "+why;Log(true,Status);}
    private string InputPathInside(string relative)
    {
        if(!PlaySettings.TryProjectPath(root,relative,out var full,out var why))throw new InvalidOperationException(why);
        var at=new DirectoryInfo(Path.GetDirectoryName(full!)!);
        while(at is not null && at.FullName.StartsWith(root,StringComparison.OrdinalIgnoreCase))
        {if(at.Exists&&(at.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Play paths cannot follow a link without explicit approval.");at=at.Parent;}
        if((File.Exists(full)||Directory.Exists(full))&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Play paths cannot follow a link without explicit approval.");
        return full!;
    }
    private bool StampCurrent()
    {
        var path=buildStamp!;
        if(!PlaySettings.TryProjectPath(root,Path.GetRelativePath(root,path),out var full,out _)||!string.Equals(full,path,StringComparison.OrdinalIgnoreCase))return false;
        return ProjectBuildStamp.Current(root,Path.GetRelativePath(root,path).Replace('\\','/'),inputs,out _);
    }
    private DateTime NewestInput()
    {
        var newest=DateTime.MinValue;
        foreach(var raw in inputs)
        {
            if(raw is null)throw new InvalidOperationException("Build input globs cannot be null.");
            var pattern=raw.Replace('\\','/');
            if(pattern.Length==0||Path.IsPathRooted(pattern)||pattern.Contains(':')||pattern.Split('/').Contains(".."))throw new InvalidOperationException("Build input globs must stay inside the project.");
            var first=pattern.IndexOfAny(['*','?']);
            var slash=pattern.LastIndexOf('/',first<0?pattern.Length-1:first);
            var prefix=slash<0?".":pattern[..slash];var directory=InputPathInside(prefix);
            var expression="^"+Regex.Escape(pattern).Replace(@"\*\*/","(?:.*/)?").Replace(@"\*\*",".*").Replace(@"\*","[^/]*").Replace(@"\?","[^/]")+"$";
            if(!Directory.Exists(directory))continue;
            var recurse=pattern.Contains('/');var stack=new Stack<string>();stack.Push(directory);var count=0;
            while(stack.Count>0)
            {
                var dir=stack.Pop();
                foreach(var file in Directory.EnumerateFiles(dir))
                {
                    if(++count>100000)throw new InvalidOperationException("Build input scan exceeded its declared file budget.");
                    if(!Regex.IsMatch(Path.GetRelativePath(root,file).Replace('\\','/'),expression,RegexOptions.CultureInvariant|RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)))continue;
                    InputPathInside(Path.GetRelativePath(root,file));var time=File.GetLastWriteTimeUtc(file);if(time>newest)newest=time;
                }
                if(recurse)foreach(var childDir in Directory.EnumerateDirectories(dir)){InputPathInside(Path.GetRelativePath(root,childDir));stack.Push(childDir);}
            }
        }
        return newest;
    }
    /// <summary>Workspace loss aborts only this owner's job immediately; never saves, deletes or closes the editor.</summary>
    public void Dispose(){if(disposed)return;frameControl?.Dispose();child?.Dispose();disposed=true;}
}
