using System.Collections.Concurrent;
using System.Diagnostics;
using Bed.Runtime;
using RoutePuzzleSample;

// INVENTED standalone saved-files sample. Route decisions are applied only on permitted ticks.
string? Option(string key){var index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null;}
var root=Option("--project")??Environment.GetEnvironmentVariable("BED_PROJECT")??Directory.GetCurrentDirectory();
var seconds=double.TryParse(Option("--seconds"),System.Globalization.CultureInfo.InvariantCulture,out var finite)?Math.Clamp(finite,0,600):600;
var log=Option("--tick-log"); // Optional test receipt, never a source file or stdout control channel.
var route=new Queue<string>((Option("--route")??"").Split(',',StringSplitOptions.RemoveEmptyEntries));
var choices=new ConcurrentQueue<string>();
var game=new RouteGame(RouteCatalog.Read(File.ReadAllText(Path.Combine(root,"data","routes.json"))));
using var control=FrameControl.ConnectFromEnvironment();
Console.WriteLine(game.Status);Console.WriteLine("Type a destination ID; simulation commands arrive only on the optional owned pipe.");
if(!Console.IsInputRedirected)_=Task.Run(()=>{string? line;while((line=Console.ReadLine()) is not null)choices.Enqueue(line);});
var watch=Stopwatch.StartNew();long ticks=0;var previous=game.Status;
using var receipt=log is null?null:new StreamWriter(new FileStream(log,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite)){AutoFlush=true};
while(watch.Elapsed.TotalSeconds<seconds)
{
    // Always pump the gate, even while paused. Each permit advances one 20ms tick; no catch-up accumulator.
    if(control.PollFrame())
    {
        ticks++;
        if(choices.TryDequeue(out var chosen))game.Choose(chosen);
        else if(route.Count>0&&ticks%10==0)game.Choose(route.Dequeue());
        receipt?.WriteLine($"{ticks}|{game.Current}|{game.Energy}");
    }
    if(game.Status!=previous){previous=game.Status;Console.WriteLine(previous);}
    Thread.Sleep(20);
}
Console.WriteLine($"Completed {ticks} simulation ticks.");
