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
    private Stream? output,errors;
    private bool aborted,exited;
    private readonly int maxLine,maxInput;
    private readonly Action<string,bool> line;
    private readonly Action<string> fault;
    internal Task<int> Completion {get;private set;}=null!;
    internal SupervisedProcess(GameLaunchSpec spec,int maxInput,int maxLine,Action<string,bool> line,Action<string> fault,IReadOnlyDictionary<string,string>? environment=null,bool plainOutput=false)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Supervised processes need Windows for now");
        if(maxInput<=0||maxLine<=0)throw new ArgumentOutOfRangeException(nameof(maxInput));
        this.maxInput=maxInput;this.maxLine=maxLine;this.line=line;this.fault=fault;
        var owned=Native.Start(spec,environment);job=owned.Job;
        try
        {
            input=new StreamWriter(owned.Input,new UTF8Encoding(false,true)){AutoFlush=true};
            output=owned.Output;errors=owned.Error;
            Completion=Watch(owned.Process,
                plainOutput?ReadPlain(output,false):Read(new StreamReader(output,new UTF8Encoding(false,true)),false),
                plainOutput?ReadPlain(errors,true):Read(new StreamReader(errors,new UTF8Encoding(false,true)),true));
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
    private static readonly Encoding StrictUtf8=new UTF8Encoding(false,true);
    private static readonly Lazy<Encoding?> Oem=new(()=>
    {
        try{return CodePagesEncodingProvider.Instance.GetEncoding((int)Native.GetOEMCP(),EncoderFallback.ReplacementFallback,DecoderFallback.ExceptionFallback);}
        catch(Exception e)when(e is ArgumentException or NotSupportedException){return null;}
    });
    private static string PlainLine(ReadOnlySpan<byte> bytes)
    {
        try{return StrictUtf8.GetString(bytes);}
        catch(DecoderFallbackException)
        {
            try{if(Oem.Value is {} encoding)return encoding.GetString(bytes);}
            catch(DecoderFallbackException) { }
            return Encoding.UTF8.GetString(bytes); // Ordinary output must survive undecodable bytes.
        }
    }
    private async Task ReadPlain(Stream reader,bool stderr)
    {
        using var bytes=new MemoryStream();var chunk=new byte[256];var first=true;
        bool Emit()
        {
            var span=bytes.GetBuffer().AsSpan(0,(int)bytes.Length);
            if(first&&span.StartsWith("\uFEFF"u8))span=span[3..];
            first=false;
            if(!span.IsEmpty&&span[^1]=='\r')span=span[..^1];
            var text=PlainLine(span);
            if(text.Length>maxLine){fault("Process output line exceeded its budget.");AbortOwned();return false;}
            if(text.Length>0)line(text,stderr);
            bytes.SetLength(0);bytes.Position=0;return true;
        }
        try
        {
            while(true)
            {
                var count=await reader.ReadAsync(chunk.AsMemory()).ConfigureAwait(false);if(count==0)break;
                for(var i=0;i<count;i++)
                {
                    if(chunk[i]=='\n'){if(!Emit())return;}
                    else
                    {
                        // Bound acquisition before decoding, allowing split multi-byte UTF-8 characters.
                        if(bytes.Length>=(long)maxLine*4+3){fault("Process output line exceeded its budget.");AbortOwned();return;}
                        bytes.WriteByte(chunk[i]);
                    }
                }
            }
            if(bytes.Length>0)Emit();
        }
        catch(Exception e)when(e is IOException or ObjectDisposedException){fault("Could not read process output.");}
    }
    private async Task<int> Watch(Process owned,Task stdout,Task stderr)
    {
        try{await owned.WaitForExitAsync().ConfigureAwait(false);var code=owned.ExitCode;lock(gate)exited=true;CloseJob();await Task.WhenAll(stdout,stderr).ConfigureAwait(false);return code;}
        finally{owned.Dispose();}
    }
    // Enumerate top-level HWNDs, but post only while their process is still in this exact owned Job.
    internal bool RequestOwnedWindowClose()
    {
        SafeFileHandle? owned;var retained=false;
        lock(gate){if(aborted||exited||job is null)return false;owned=job;owned.DangerousAddRef(ref retained);}
        try{return Native.CloseJobWindows(owned);}
        finally{if(retained)owned.DangerousRelease();}
    }
    private void CloseJob(){SafeFileHandle? owned;lock(gate){owned=job;job=null;}owned?.Dispose();}
    internal void AbortOwned(){lock(gate){if(aborted)return;aborted=true;}CloseJob();foreach(var stream in new IDisposable?[]{input,output,errors})try{stream?.Dispose();}catch(Exception e)when(e is IOException or ObjectDisposedException or InvalidOperationException){}}
    public void Dispose()=>AbortOwned();
    private static class Native
    {
        private const uint Suspended=4,NoWindow=0x08000000,Extended=0x00080000,UseStdHandles=0x100,KillOnClose=0x2000;
        public sealed record Started(Process Process,SafeFileHandle Job,FileStream Input,FileStream Output,FileStream Error);
        public static Started Start(GameLaunchSpec spec,IReadOnlyDictionary<string,string>? environment)
        {
            SafeFileHandle? job=null,stdinRead=null,stdinWrite=null,stdoutRead=null,stdoutWrite=null,stderrRead=null,stderrWrite=null;
            Process? managed=null;IntPtr attributes=IntPtr.Zero,handles=IntPtr.Zero,environmentBlock=IntPtr.Zero;ProcessInfo created=default;var resumed=false;var attributesReady=false;
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
                if(environment is not null)
                {
                    var entries=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables())
                        entries[(string)item.Key]=(string?)item.Value??"";
                    foreach(var item in environment)
                    {
                        if(string.IsNullOrEmpty(item.Key)||item.Key.Contains('=')||item.Key.Contains('\0')||item.Value.Contains('\0'))
                            throw new ArgumentException("Process environment needs literal names and values without null characters.");
                        entries[item.Key]=item.Value;
                    }
                    environmentBlock=Marshal.StringToHGlobalUni(string.Join("\0",entries.Select(item=>item.Key+"="+item.Value))+"\0\0");
                }
                if(!CreateProcessW(spec.Executable,new StringBuilder(spec.CommandLine()),IntPtr.Zero,IntPtr.Zero,true,
                    Suspended|NoWindow|Extended|(environment is null?0u:0x400u),environmentBlock,spec.WorkingDirectory,ref startup,out created))
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
                if(environmentBlock!=IntPtr.Zero)Marshal.FreeHGlobal(environmentBlock);
                stdinRead?.Dispose();stdinWrite?.Dispose();stdoutRead?.Dispose();stdoutWrite?.Dispose();stderrRead?.Dispose();stderrWrite?.Dispose();
                job?.Dispose();managed?.Dispose();
            }
        }
        internal static bool CloseJobWindows(SafeFileHandle ownedJob)
        {
            var ids=new HashSet<uint>();var read=false;
            for(var capacity=16;capacity<=4096;capacity*=2)
            {
                var size=checked(8+capacity*IntPtr.Size);var buffer=Marshal.AllocHGlobal(size);
                try
                {
                    if(!QueryInformationJobObject(ownedJob,3,buffer,(uint)size,out _))
                    {var error=Marshal.GetLastWin32Error();if(error==234)continue;throw new Win32Exception(error);}
                    var count=Marshal.ReadInt32(buffer,4);
                    if(count<0||count>capacity)throw new InvalidOperationException("Owned Job returned an invalid process list.");
                    for(var i=0;i<count;i++)ids.Add(checked((uint)Marshal.ReadIntPtr(buffer,8+i*IntPtr.Size).ToInt64()));
                    read=true;break;
                }
                finally{Marshal.FreeHGlobal(buffer);}
            }
            if(!read)throw new InvalidOperationException("Owned process tree exceeded its close-request budget.");
            var sent=false;var failure=0;
            EnumWindow callback=(window,_)=>
            {
                GetWindowThreadProcessId(window,out var pid);if(!ids.Contains(pid))return true;
                using var process=OpenProcess(0x1000,false,pid);
                if(process.IsInvalid||!IsProcessInJob(process,ownedJob,out var belongs)||!belongs)return true;
                GetWindowThreadProcessId(window,out var again);if(again!=pid)return true;
                if(PostMessageW(window,0x0010,IntPtr.Zero,IntPtr.Zero))sent=true;
                else failure=Marshal.GetLastWin32Error() is var error&&error!=0?error:1;
                return true;
            };
            if(!EnumWindows(callback,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(failure!=0)throw new Win32Exception(failure);
            return sent;
        }
        private delegate bool EnumWindow(IntPtr window,IntPtr state);
        [DllImport("kernel32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint GetOEMCP();
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool QueryInformationJobObject(SafeFileHandle job,int kind,IntPtr information,uint length,out uint returned);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
        [DllImport("kernel32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool IsProcessInJob(SafeProcessHandle process,SafeFileHandle job,out bool belongs);
        [DllImport("user32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool EnumWindows(EnumWindow callback,IntPtr state);
        [DllImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll",SetLastError=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool PostMessageW(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
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
