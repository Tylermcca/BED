// Self-authored hidden fixture only. No game/editor dependency, window, external command or arbitrary process control.
Console.WriteLine("plain stdout");Console.Error.WriteLine("plain stderr");
Console.WriteLine("BED_PROJECT="+Environment.GetEnvironmentVariable("BED_PROJECT"));
Console.WriteLine("BED_PLAY="+Environment.GetEnvironmentVariable("BED_PLAY"));
var mode=args.FirstOrDefault()??"ok";
if(mode=="diagnostic")
{
    Console.WriteLine(args[1]);Console.Error.WriteLine(args[2]);return 0;
}

if(mode=="stdin")
{
    var text=await Console.In.ReadToEndAsync();Console.WriteLine("stdin-bytes="+System.Text.Encoding.UTF8.GetByteCount(text));
    return text.Length==0?0:31;
}
if(args is ["journey","--seconds","0",var marker])
{
    File.AppendAllText(marker,"game ran once\n");
    Console.WriteLine("journey game ran with --seconds 0");return 0;
}
if(mode is "encoded" or "encoded-fail" or "build-encoded")
{
    using var stdout=Console.OpenStandardOutput();using var stderr=Console.OpenStandardError();
    var utf8=System.Text.Encoding.UTF8.GetBytes("utf8: "+new string('界',120)+" \U0001F680\n");
    stdout.Write(utf8);
    // Literal OEM bytes, deliberately invalid UTF-8; the host must fall back for each whole line.
    stdout.Write(new byte[]{(byte)'o',(byte)'e',(byte)'m',(byte)':',(byte)' ',0xf6,0x82,0x9c,13,10});
    stderr.Write(new byte[]{(byte)'e',(byte)'r',(byte)'r',(byte)':',(byte)' ',0xf6,0x82,0x9c,10});
    stdout.Write(System.Text.Encoding.UTF8.GetBytes("utf8-after: caf\u00e9\n"));
    stdout.Flush();stderr.Flush();
    if(mode!="build-encoded")
    {
        stdout.Write(new byte[]{(byte)'t',(byte)'a',(byte)'i',(byte)'l',(byte)':',(byte)' ',0xf6});
        stdout.Flush();return mode=="encoded-fail"?7:0;
    }
}
if(mode is "missing-runtime" or "missing-runtime-quiet" or "runtime-text-only")
{
    if(mode!="missing-runtime-quiet")Console.Error.WriteLine("Framework: 'Microsoft.NETCore.App', version '10.0.0' (x64)");
    return mode=="runtime-text-only"?7:unchecked((int)0x80008096);
}
if(mode=="exit-on-file")
{
    var deadline=System.Diagnostics.Stopwatch.StartNew();
    while(!File.Exists(args[1])&&deadline.Elapsed<TimeSpan.FromSeconds(30))await Task.Delay(2);
    return File.Exists(args[1])?0:32;
}
if(mode=="crash")return 7;
if(mode=="build-fail")return 9;
if(mode is "build" or "build-encoded")
{
    var destination=args[1];Directory.CreateDirectory(destination);
    foreach(var name in new[]{"ProjectPlayTestGame.dll","ProjectPlayTestGame.deps.json","ProjectPlayTestGame.runtimeconfig.json"})
        File.Copy(Path.Combine(AppContext.BaseDirectory,name),Path.Combine(destination,name),true);
    File.Copy(Environment.ProcessPath!,Path.Combine(destination,"ProjectPlayTestGame.exe"),true);
    Console.WriteLine("self-authored build fixture complete");return 0;
}
if(mode=="many-lines")for(var i=0;i<100;i++)Console.WriteLine("fixture-line-"+i);
if(mode=="long-line")Console.WriteLine(new string('x',8192));
if(mode is "wait" or "ignore")
{
    await Task.Delay(Timeout.InfiniteTimeSpan);return 0;
}
await Task.Delay(150);return 0;
