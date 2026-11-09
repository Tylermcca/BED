// Self-authored bounded build fixture. Never invokes a compiler or another process.
var mode=args[0];var output=args[1];
Console.WriteLine("Synthetic build: "+mode);
if(mode=="hang"){await Task.Delay(Timeout.InfiniteTimeSpan);return 9;}
if(mode=="nonzero")return 23;
if(mode=="long-line"){Console.WriteLine(new string('x',2048));return 0;}
if(mode=="burst"){for(var i=0;i<100;i++)Console.WriteLine("Synthetic line "+i);return 0;}
if(mode!="missing")await File.WriteAllBytesAsync(Path.Combine(output,"result.bin"),new byte[]{mode=="wrong"?(byte)2:(byte)1});
if(mode=="extra")await File.WriteAllTextAsync(Path.Combine(output,"not-allowed.txt"),"synthetic");
Console.Error.WriteLine("Synthetic diagnostic");
return 0;
