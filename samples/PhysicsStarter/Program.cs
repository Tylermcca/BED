using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Bed;
using SceneStarter;
namespace PhysicsStarter;
public static class Program
{
    public static int Main(string[] args)=>Run(args,Console.Out,Console.Error);
    private static string Text(Vector3 v)=>string.Create(CultureInfo.InvariantCulture,$"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})");
    public static int Run(string[] args,TextWriter output,TextWriter error)
    {
        try
        {
            if(args is ["--compile-scene2",var compile]){Scene2Compiler.Compile(compile,true);output.WriteLine("PhysicsStarter generated saved physics.");return 0;}
            if(args is ["--build",var input,var destination]){PhysicsExportBuild.Create(input,destination,AppContext.BaseDirectory);output.WriteLine("PhysicsStarter copied the checked physics build.");return 0;}
            string? root=null;double seconds=1;int? frames=null;var seen=new HashSet<string>(StringComparer.Ordinal);
            for(var i=0;i<args.Length;i+=2)
            {
                if(i+1>=args.Length||!seen.Add(args[i]))throw new ArgumentException("Use each physics sample option once with its value.");
                if(args[i]=="--project")root=args[i+1];
                else if(args[i]=="--seconds"&&double.TryParse(args[i+1],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>=0&&value<=60)seconds=value;
                else if(args[i]=="--frames"&&int.TryParse(args[i+1],NumberStyles.None,CultureInfo.InvariantCulture,out var count)&&count is >=0 and <=100000)frames=count;
                else throw new ArgumentException("Use --project, --seconds (0..60) or --frames (0..100000).");
            }
            if(frames is not null&&seen.Contains("--seconds"))throw new ArgumentException("Choose --seconds or --frames, not both.");
            if(root is null)throw new ArgumentException("Choose a saved physics project with --project.");var saved=Scene2RuntimeInput.Open(root,true);
            if(saved.Level.Scene.Physics is null){if(frames is null)Thread.Sleep(TimeSpan.FromSeconds(seconds));output.WriteLine("PhysicsStarter loaded a body-free saved level.");return 0;}
            using var world=new PhysicsWorld(saved.Level.Scene);var motion=new KinematicMotion(world);var ticks=0;double dropped=0;
            if(frames is {} fixedTicks)
                // A bounded, wall-clock-free run: exactly this many fixed steps, as fast as the machine allows.
                for(;ticks<fixedTicks;ticks++){motion.Step(ticks);world.Tick();}
            else
            {
                var watch=Stopwatch.StartNew();var last=watch.Elapsed.TotalSeconds;double accumulator=0;
                while(watch.Elapsed.TotalSeconds<seconds)
                {
                    var now=watch.Elapsed.TotalSeconds;accumulator+=now-last;last=now;var steps=0;
                    while(accumulator>=world.StepSeconds&&steps++<8){motion.Step(ticks);world.Tick();ticks++;accumulator-=world.StepSeconds;}
                    if(accumulator>=world.StepSeconds){var skip=Math.Floor(accumulator/world.StepSeconds)*world.StepSeconds;dropped+=skip;accumulator-=skip;}
                    Thread.Sleep(1);
                }
            }
            output.WriteLine($"PhysicsStarter loaded saved physics: {world.Count} bodies, {ticks} fixed ticks, {dropped:g} dropped seconds.");
            output.WriteLine($"Simulated {(ticks*(double)world.StepSeconds).ToString("0.00",CultureInfo.InvariantCulture)} seconds. This game has no window yet; it reports what happened.");
            // The highest dynamic body at the start is the top of a stack.
            if(world.Named(SceneBodyMotion.Dynamic).OrderByDescending(n=>world.Saved(n).Position.Y).FirstOrDefault() is {} top)
            {var start=world.Saved(top).Position;var end=world.Pose(top).Position;output.WriteLine($"Top dynamic body {top} started at {Text(start)} and came to rest at {Text(end)}.");}
            foreach(var name in world.Named(SceneBodyMotion.Kinematic))
                output.WriteLine($"Kinematic body {name} slid along X and travelled {motion.Travelled(name).ToString("0.00",CultureInfo.InvariantCulture)} m.");
            return 0;
        }
        catch(Exception e)when(e is not OutOfMemoryException){error.WriteLine(e.Message);return 1;}
    }
}

/// <summary>The fixed rule for kinematic bodies: each slides back and forth along X around its saved position,
/// X(t) = saved X + Amplitude * sin(2 * pi * t / Period), driven through MoveKinematic once per fixed step.</summary>
internal sealed class KinematicMotion(PhysicsWorld world)
{
    internal const float Amplitude=1.5f,Period=4f;
    private readonly IReadOnlyList<string> names=world.Named(SceneBodyMotion.Kinematic);
    private readonly Dictionary<string,double> travelled=new(StringComparer.Ordinal);
    private static float Offset(double seconds)=>Amplitude*MathF.Sin((float)(2*Math.PI*seconds/Period));
    /// <summary>Sets each kinematic target for the step that ends at (tick + 1) fixed steps.</summary>
    internal void Step(int tick)
    {
        var before=tick*(double)world.StepSeconds;var after=(tick+1)*(double)world.StepSeconds;
        foreach(var name in names)
        {
            var (position,rotation)=world.Saved(name);
            world.MoveKinematic(name,position+new Vector3(Offset(after),0,0),rotation);
            travelled[name]=travelled.GetValueOrDefault(name)+Math.Abs(Offset(after)-Offset(before));
        }
    }
    internal double Travelled(string name)=>travelled.GetValueOrDefault(name);
}
