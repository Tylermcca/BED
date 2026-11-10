// Self-authored bounded build fixture. Never invokes a compiler or another process.
var mode=args[0];var output=args[1];
Console.WriteLine("Synthetic build: "+mode);
if(mode=="hang"){await Task.Delay(Timeout.InfiniteTimeSpan);return 9;}
if(mode=="nonzero")return 23;
if(mode=="long-line"){Console.WriteLine(new string('x',2048));return 0;}
if(mode=="burst"){for(var i=0;i<100;i++)Console.WriteLine("Synthetic line "+i);return 0;}
if(mode is "captured" or "alter-captured" or "tamper-captured")
{
    using var manifest=System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(args[2],"manifest.json")));
    foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
    {
        var relative=entry.GetProperty("path").GetString()!;var input=Path.Combine(args[2],relative);
        if(mode=="tamper-captured")await File.WriteAllBytesAsync(input,new byte[]{99});
        var destination=Path.Combine(output,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination,mode=="alter-captured"?new byte[]{99}:await File.ReadAllBytesAsync(input));
    }
}
else if(mode!="missing")await File.WriteAllBytesAsync(Path.Combine(output,"result.bin"),new byte[]{mode=="wrong"?(byte)2:(byte)1});
if(mode=="extra")await File.WriteAllTextAsync(Path.Combine(output,"not-allowed.txt"),"synthetic");
Console.Error.WriteLine("Synthetic diagnostic");
return 0;
