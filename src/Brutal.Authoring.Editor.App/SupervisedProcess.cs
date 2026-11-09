using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
namespace Brutal.Authoring.Editor.App;

// Shared owned child; protocols stay in consumers. Native start is the unchanged PL05 suspended Job path.
internal sealed class SupervisedProcess : IDisposable
{
    private readonly object gate=new();
    private readonly SemaphoreSlim writes=new(1);
    private SafeFileHandle? job;
    private StreamWriter? input;
    private StreamReader? output,errors;
    private bool aborted,exited;
    private readonly int maxLine,maxInput;
    private readonly Action<string,bool> line;
    private readonly Action<string> fault;
    internal Task<int> Completion {get;private set;}=null!;
    internal SupervisedProcess(GameLaunchSpec spec,int maxInput,int maxLine,Action<string,bool> line,Action<string> fault)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Supervised processes need Windows for now");
        if(maxInput<=0||maxLine<=0)throw new ArgumentOutOfRangeException(nameof(maxInput));
        this.maxInput=maxInput;this.maxLine=maxLine;this.line=line;this.fault=fault;
        var owned=Native.Start(spec);job=owned.Job;
        try
        {
            input=new StreamWriter(owned.Input,new UTF8Encoding(false,true)){AutoFlush=true};
            output=new StreamReader(owned.Output,new UTF8Encoding(false,true));errors=new StreamReader(owned.Error,new UTF8Encoding(false,true));
            Completion=Watch(owned.Process,Read(output,false),Read(errors,true));
        }
        catch {AbortOwned();owned.Input.Dispose();owned.Output.Dispose();owned.Error.Dispose();owned.Process.Dispose();throw;}
    }
    internal async Task Send(string value)
    {
        if(Encoding.UTF8.GetByteCount(value)>maxInput)throw new ArgumentException("Process input exceeds its budget.");
        await writes.WaitAsync().ConfigureAwait(false);
        try {StreamWriter? writer;lock(gate){if(aborted||exited)return;writer=input;}if(writer is not null)await writer.WriteLineAsync(value).ConfigureAwait(false);}
        catch(Exception e)when(e is IOException or ObjectDisposedException or InvalidOperationException){fault("Could not write process input.");}
        finally{writes.Release();}
    }
    internal void CloseInput(){lock(gate){input?.Dispose();input=null;}}
    private async Task Read(StreamReader reader,bool stderr)
    {
        var text=new StringBuilder();var chunk=new char[256];
        try
        {
            while(true)
            {
                var count=await reader.ReadAsync(chunk.AsMemory()).ConfigureAwait(false);if(count==0)break;
                for(var i=0;i<count;i++)
                {
                    if(chunk[i]=='\n'){if(text.Length>0&&text[^1]=='\r')text.Length--;if(text.Length>0)line(text.ToString(),stderr);text.Clear();}
                    else{if(text.Length>=maxLine){fault("Process output line exceeded its budget.");AbortOwned();return;}text.Append(chunk[i]);}
                }
            }
            if(text.Length>0)line(text.ToString(),stderr);
        }
        catch(Exception e)when(e is IOException or ObjectDisposedException or DecoderFallbackException){fault("Could not read process output.");}
    }
    private async Task<int> Watch(Process owned,Task stdout,Task stderr)
    {
        try{await owned.WaitForExitAsync().ConfigureAwait(false);var code=owned.ExitCode;lock(gate)exited=true;CloseJob();await Task.WhenAll(stdout,stderr).ConfigureAwait(false);return code;}
        finally{owned.Dispose();}
    }
    private void CloseJob(){SafeFileHandle? owned;lock(gate){owned=job;job=null;}owned?.Dispose();}
    internal void AbortOwned(){lock(gate){if(aborted)return;aborted=true;}CloseJob();foreach(var stream in new IDisposable?[]{input,output,errors})try{stream?.Dispose();}catch(Exception e)when(e is IOException or ObjectDisposedException or InvalidOperationException){}}
    public void Dispose()=>AbortOwned();
    private static class Native
    {
        private const uint Suspended=4,NoWindow=0x08000000,Extended=0x00080000,UseStdHandles=0x100,KillOnClose=0x2000;
        public sealed record Started(Process Process,SafeFileHandle Job,FileStream Input,FileStream Output,FileStream Error);
        public static Started Start(GameLaunchSpec spec)
        {
            SafeFileHandle? job=null,stdinRead=null,stdinWrite=null,stdoutRead=null,stdoutWrite=null,stderrRead=null,stderrWrite=null;
            Process? managed=null;IntPtr attributes=IntPtr.Zero,handles=IntPtr.Zero;ProcessInfo created=default;var resumed=false;var attributesReady=false;
            try
            {
                job=new(CreateJobObjectW(IntPtr.Zero,null),true);
                if(job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                var limit=new ExtendedLimit {Basic=new BasicLimit {Flags=KillOnClose}};
                if(!SetInformationJobObject(job,9,ref limit,(uint)Marshal.SizeOf<ExtendedLimit>())) throw new Win32Exception(Marshal.GetLastWin32Error());
                var security=new Security {Length=Marshal.SizeOf<Security>(),Inherit=1};
                if(!CreatePipe(out stdinRead,out stdinWrite,ref security,0) || !CreatePipe(out stdoutRead,out stdoutWrite,ref security,0)
                    || !CreatePipe(out stderrRead,out stderrWrite,ref security,0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if(!SetHandleInformation(stdinWrite,1,0) || !SetHandleInformation(stdoutRead,1,0) || !SetHandleInformation(stderrRead,1,0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                nuint bytes=0;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref bytes);
                attributes=Marshal.AllocHGlobal(checked((nint)bytes));
                if(!InitializeProcThreadAttributeList(attributes,1,0,ref bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
                attributesReady=true;
                handles=Marshal.AllocHGlobal(IntPtr.Size*3);
                Marshal.Copy(new[]{stdinRead.DangerousGetHandle(),stdoutWrite.DangerousGetHandle(),stderrWrite.DangerousGetHandle()},0,handles,3);
                if(!UpdateProcThreadAttribute(attributes,0,(nuint)0x20002,handles,(nuint)(IntPtr.Size*3),IntPtr.Zero,IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var startup=new StartupEx {Startup=new Startup {Size=Marshal.SizeOf<StartupEx>(),Flags=UseStdHandles,
                    Input=stdinRead.DangerousGetHandle(),Output=stdoutWrite.DangerousGetHandle(),Error=stderrWrite.DangerousGetHandle()},Attributes=attributes};
                if(!CreateProcessW(spec.Executable,new StringBuilder(spec.CommandLine()),IntPtr.Zero,IntPtr.Zero,true,
                    Suspended|NoWindow|Extended,IntPtr.Zero,spec.WorkingDirectory,ref startup,out created))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if(!AssignProcessToJobObject(job,created.Process)) throw new Win32Exception(Marshal.GetLastWin32Error());
                // The process is still suspended here, so acquiring its managed wait handle cannot race early exit.
                managed=Process.GetProcessById(checked((int)created.ProcessId));
                var input=new FileStream(stdinWrite,FileAccess.Write,4096,false);
                var output=new FileStream(stdoutRead,FileAccess.Read,4096,false);
                var error=new FileStream(stderrRead,FileAccess.Read,4096,false);
                if(ResumeThread(created.Thread)==uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                resumed=true;
                var result=new Started(managed,job,input,output,error);
                managed=null;job=null;stdinWrite=null;stdoutRead=null;stderrRead=null;return result;
            }
            finally
            {
                if(created.Process!=IntPtr.Zero && !resumed) TerminateProcess(created.Process,1);
                if(created.Thread!=IntPtr.Zero) CloseHandle(created.Thread);
                if(created.Process!=IntPtr.Zero) CloseHandle(created.Process);
                if(attributes!=IntPtr.Zero) {if(attributesReady) DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);}
                if(handles!=IntPtr.Zero) Marshal.FreeHGlobal(handles);
                stdinRead?.Dispose();stdinWrite?.Dispose();stdoutRead?.Dispose();stdoutWrite?.Dispose();stderrRead?.Dispose();stderrWrite?.Dispose();
                job?.Dispose();managed?.Dispose();
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Security {public int Length;public IntPtr Descriptor;public int Inherit;}
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
        {public long PerProcess,PerJob;public uint Flags;public nuint MinWorking,MaxWorking;public uint Active;public nuint Affinity;public uint Priority,Scheduling;}
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit
        {public BasicLimit Basic;public IoCounters Io;public nuint ProcessMemory,JobMemory,PeakProcess,PeakJob;}
        [StructLayout(LayoutKind.Sequential)] private struct Startup
        {public int Size;public IntPtr Reserved,Desktop,Title;public uint X,Y,Width,Height,XChars,YChars,Fill,Flags;
         public ushort Show,ReservedSize;public IntPtr ReservedBytes,Input,Output,Error;}
        [StructLayout(LayoutKind.Sequential)] private struct StartupEx {public Startup Startup;public IntPtr Attributes;}
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo {public IntPtr Process,Thread;public uint ProcessId,ThreadId;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern IntPtr CreateJobObjectW(IntPtr security,string? name);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetInformationJobObject(SafeFileHandle job,int information,ref ExtendedLimit data,uint size);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool CreatePipe(out SafeFileHandle read,out SafeFileHandle write,ref Security security,uint size);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetHandleInformation(SafeFileHandle handle,uint mask,uint flags);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref nuint size);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,nuint attribute,IntPtr value,nuint size,IntPtr previous,IntPtr returned);
        [DllImport("kernel32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool CreateProcessW(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,
            uint flags,IntPtr environment,string directory,ref StartupEx startup,out ProcessInfo process);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool CloseHandle(IntPtr handle);
    }
}
