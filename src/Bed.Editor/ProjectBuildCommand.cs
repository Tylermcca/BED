using System.Text.RegularExpressions;
namespace Bed.Editor;

/// <summary>A build error's actual compiler location and suggested next action. Missing locations stay null.</summary>
public sealed record ProjectBuildError(string? File,int? Line,int? Column,string Code,string What,string Try);
/// <summary>Observed build-only process outcome, never a game test verdict.</summary>
public sealed record ProjectBuildResult(bool Success,int? ExitCode,string Summary,IReadOnlyList<ProjectBuildError> Errors,IReadOnlyList<string> Output);
/// <summary>Shared build-only owner for the editor and agent; saved files and exact user-local approvals are required.</summary>
public static class ProjectBuildCommand
{
    /// <summary>Parses compiler output without opening its reported path or treating its message as a command.</summary>
    public static ProjectBuildError? ReadError(string text)
    {
        var match=Regex.Match(text,@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)(?:,\d+,\d+)?\):\s*error\s+(?<code>[A-Z]+\d+)\s*:\s*(?<what>.*?)(?:\s+\[[^\]]+\])?$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(match.Success && int.TryParse(match.Groups["line"].Value,out var line) && int.TryParse(match.Groups["col"].Value,out var col))
            return new(match.Groups["file"].Value,line,col,match.Groups["code"].Value,match.Groups["what"].Value,Hint(match.Groups["code"].Value));
        match=Regex.Match(text,@"\berror\s+(?<code>[A-Z]+\d+)\s*:\s*(?<what>.*?)(?:\s+\[[^\]]+\])?$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        return match.Success?new(null,null,null,match.Groups["code"].Value,match.Groups["what"].Value,Hint(match.Groups["code"].Value)):null;
    }
    private static string Hint(string code)=>code switch
    {
        "CS1002"=>"Check for a missing semicolon at this line or the line before it.",
        "CS1513"=>"Check that each opening brace has a matching closing brace.",
        "CS0103"=>"Check the spelling and scope of the name against the source.",
        "CS0246"=>"Check the type name, using statements and existing project references against the source.",
        "CS0029"=>"Check the value's type and the type expected by this assignment.",
        "CS1061"=>"Check the member name and its type against the reference or source; do not invent an API.",
        "NETSDK1045"=>"Check the installed .NET SDK and the project's target framework.",
        _=>"Read the reported error and nearby source. Check names and signatures against the reference before changing them."
    };
    /// <summary>Runs the declared build once, even if outputs look current. Never saves, approves, launches Play or writes settings.</summary>
    public static async Task<ProjectBuildResult> RunAsync(PlayLaunch launch,PlayApprovals approvals,bool hasUnsavedChanges,
        CancellationToken cancellation=default,TimeSpan? timeout=null)
    {
        var lines=new List<string>();var errors=new List<ProjectBuildError>();var gate=new object();SupervisedProcess? child=null;string? fault=null;
        ProjectBuildResult Result(bool success,int? code,string summary)
        {lock(gate)return new(success,code,summary,errors.Distinct().ToArray(),lines.ToArray());}
        try
        {
            if(hasUnsavedChanges)return Result(false,null,"Save changes before building.");
            if(launch.Build is null)return Result(false,null,"Set a build command in Tools > Play settings first.");
            if(approvals.NeedsApproval(launch,true) is {} reason)return Result(false,null,reason+". Open Tools > Build game in BED to approve this exact command.");
            cancellation.ThrowIfCancellationRequested();
            var deadline=timeout??TimeSpan.FromMinutes(5);
            if(deadline<=TimeSpan.Zero||deadline>TimeSpan.FromMinutes(10))throw new ArgumentOutOfRangeException(nameof(timeout));
            void Fault(string why){lock(gate)fault??=why;child?.AbortOwned();}
            child=new(launch.Build,1,8192,(line,stderr)=>
            {
                lock(gate)
                {
                    if(lines.Count>=512){Fault("Build output exceeded 512 lines; the build was stopped. Narrow the build output and try again.");return;}
                    lines.Add(line);
                    if(ReadError(line) is {} error)errors.Add(error);
                }
            },Fault,plainOutput:true);
            child.CloseInput();using var registration=cancellation.Register(child.AbortOwned);
            lock(gate)if(fault is not null)child.AbortOwned();
            if(await Task.WhenAny(child.Completion,Task.Delay(deadline,cancellation)).ConfigureAwait(false)!=child.Completion)
            {Fault("Build exceeded its time limit.");child.AbortOwned();}
            var code=await child.Completion.ConfigureAwait(false);cancellation.ThrowIfCancellationRequested();
            lock(gate)
            {
                if(fault is not null)return Result(false,code,fault);
                if(code!=0&&errors.Count==0)errors.Add(new(null,null,null,"","The build stopped without a compiler location. Read the build output below.","Check the first reported failure and the build command in Play settings."));
                return Result(code==0&&errors.Count==0,code,code==0&&errors.Count==0?"Build finished with exit code 0. The game was not launched or tested.":"Build failed. Fix the reported errors, then build again.");
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or OperationCanceledException or PlatformNotSupportedException)
        {return Result(false,null,e is OperationCanceledException?"Build canceled.":e.Message);}
        finally{child?.Dispose();}
    }
}
