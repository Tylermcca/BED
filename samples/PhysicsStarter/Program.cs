using System.Diagnostics;
using System.Globalization;
using SceneStarter;
namespace PhysicsStarter;
public static class Program
{
    public static int Main(string[] args)=>Run(args,Console.Out,Console.Error);
    public static int Run(string[] args,TextWriter output,TextWriter error)
    {
        try
        {
            if(args is ["--compile-scene2",var compile]){Scene2Compiler.Compile(compile,true);output.WriteLine("PhysicsStarter generated saved physics.");return 0;}
            if(args is ["--build",var input,var destination]){PhysicsExportBuild.Create(input,destination,AppContext.BaseDirectory);output.WriteLine("PhysicsStarter copied the checked physics build.");return 0;}
            string? root=null;double seconds=1;var seen=new HashSet<string>(StringComparer.Ordinal);
            for(var i=0;i<args.Length;i+=2)
            {
                if(i+1>=args.Length||!seen.Add(args[i]))throw new ArgumentException("Use each physics sample option once with its value.");
                if(args[i]=="--project")root=args[i+1];
                else if(args[i]=="--seconds"&&double.TryParse(args[i+1],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>=0&&value<=60)seconds=value;
                else throw new ArgumentException("Use --project and --seconds (0..60).");
            }
            if(root is null)throw new ArgumentException("Choose a saved physics project with --project.");var saved=Scene2RuntimeInput.Open(root,true);
            if(saved.Level.Scene.Physics is null){Thread.Sleep(TimeSpan.FromSeconds(seconds));output.WriteLine("PhysicsStarter loaded a body-free saved level.");return 0;}
            using var world=new PhysicsWorld(saved.Level.Scene);var watch=Stopwatch.StartNew();var last=watch.Elapsed.TotalSeconds;double accumulator=0,dropped=0;var ticks=0;
            while(watch.Elapsed.TotalSeconds<seconds)
            {
                var now=watch.Elapsed.TotalSeconds;accumulator+=now-last;last=now;var steps=0;
                while(accumulator>=world.StepSeconds&&steps++<8){world.Tick();ticks++;accumulator-=world.StepSeconds;}
                if(accumulator>=world.StepSeconds){var skip=Math.Floor(accumulator/world.StepSeconds)*world.StepSeconds;dropped+=skip;accumulator-=skip;}
                Thread.Sleep(1);
            }
            output.WriteLine($"PhysicsStarter loaded saved physics: {world.Count} bodies, {ticks} fixed ticks, {dropped:g} dropped seconds.");return 0;
        }
        catch(Exception e)when(e is not OutOfMemoryException){error.WriteLine(e.Message);return 1;}
    }
}
