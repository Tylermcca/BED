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
            string? root=null;double seconds=1;int? frames=null;int drops=0;string? shot=null;var headless=false;var seen=new HashSet<string>(StringComparer.Ordinal);
            for(var i=0;i<args.Length;i+=2)
            {
                if(args[i]=="--headless"){if(!seen.Add(args[i]))throw new ArgumentException("Use each physics sample option once.");headless=true;i--;continue;}
                if(i+1>=args.Length||!seen.Add(args[i]))throw new ArgumentException("Use each physics sample option once with its value.");
                if(args[i]=="--project")root=args[i+1];
                else if(args[i]=="--seconds"&&double.TryParse(args[i+1],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>=0&&value<=60)seconds=value;
                else if(args[i]=="--frames"&&int.TryParse(args[i+1],NumberStyles.None,CultureInfo.InvariantCulture,out var count)&&count is >=0 and <=100000)frames=count;
                else if(args[i]=="--drops"&&int.TryParse(args[i+1],NumberStyles.None,CultureInfo.InvariantCulture,out var many)&&many is >=0 and <=CrateToy.MaxDropped)drops=many;
                else if(args[i]=="--shot"&&args[i+1].EndsWith(".png",StringComparison.OrdinalIgnoreCase))shot=args[i+1];
                else throw new ArgumentException("Use --project, --seconds (0..60), --frames (0..100000), --drops (0..40), --shot <file.png> or --headless.");
            }
            if(frames is not null&&seen.Contains("--seconds"))throw new ArgumentException("Choose --seconds or --frames, not both.");
            if(shot is not null&&seen.Contains("--seconds"))throw new ArgumentException("A --shot renders after --frames fixed steps; --seconds does not apply.");
            if(root is null)throw new ArgumentException("Choose a saved physics project with --project.");var saved=Scene2RuntimeInput.Open(root,true);
            if(saved.Level.Scene.Physics is null){if(frames is null&&shot is null)Thread.Sleep(TimeSpan.FromSeconds(seconds));output.WriteLine("PhysicsStarter loaded a body-free saved level.");return 0;}
            // A window opens only for an ordinary run: --seconds, --frames, --shot or --headless keep it a console program.
            var windowed=!headless&&shot is null&&!seen.Contains("--seconds")&&frames is null;
            using var toy=new CrateToy(saved.Level.Scene);var assets=new Scene2RuntimeAssets(saved.Meshes,saved.Textures);
            var played=windowed&&ToyWindow.Play(toy,assets,error);
            if(!played&&(frames is not null||shot is not null))
            {
                // A bounded, wall-clock-free run: exactly this many fixed steps, as fast as the machine allows.
                // --drops drops that many crates, one every half simulated second from the first step.
                var spacing=(int)Math.Round(.5/toy.World.StepSeconds);var dropped=0;
                for(var tick=0;tick<(frames??0);tick++){if(dropped<drops&&tick==dropped*spacing)toy.Drop(ScriptedDrop(toy,dropped++));toy.Step();}
                while(dropped<drops)toy.Drop(ScriptedDrop(toy,dropped++));
            }
            else if(!played)
            {
                var watch=Stopwatch.StartNew();var last=watch.Elapsed.TotalSeconds;
                while(watch.Elapsed.TotalSeconds<seconds){var now=watch.Elapsed.TotalSeconds;toy.Advance(now-last);last=now;Thread.Sleep(1);}
            }
            if(shot is not null){ToyWindow.Shot(toy,assets,shot,1280,720,new OrbitCamera());output.WriteLine($"PhysicsStarter rendered {shot}.");}
            var world=toy.World;
            output.WriteLine($"PhysicsStarter loaded saved physics: {toy.SavedBodies} bodies, {toy.Ticks} fixed ticks, {toy.DroppedSeconds:g} dropped seconds.");
            output.WriteLine($"Simulated {(toy.Ticks*(double)world.StepSeconds).ToString("0.00",CultureInfo.InvariantCulture)} seconds."+(played?" Played in the window.":" Ran without a window; run with only --project . to play."));
            if(toy.Dropped.Count>0)output.WriteLine($"{toy.Dropped.Count} dropped crate(s) are on the island.");
            // The highest dynamic body at the start is the top of a stack.
            if(world.Named(SceneBodyMotion.Dynamic).Where(n=>!toy.Dropped.Contains(n)).OrderByDescending(n=>world.Saved(n).Position.Y).FirstOrDefault() is {} top)
            {var start=world.Saved(top).Position;var end=world.Pose(top).Position;output.WriteLine($"Top dynamic body {top} started at {Text(start)} and came to rest at {Text(end)}.");}
            foreach(var name in world.Named(SceneBodyMotion.Kinematic))
                output.WriteLine($"Kinematic body {name} slid along X and travelled {toy.Motion.Travelled(name).ToString("0.00",CultureInfo.InvariantCulture)} m.");
            return 0;
        }
        catch(Exception e)when(e is not OutOfMemoryException){error.WriteLine(e.Message);return 1;}
    }
    /// <summary>Repeatable drop points for --drops: a small spiral around the island centre.</summary>
    internal static Vector2 ScriptedDrop(CrateToy toy,int index)
    {
        var centre=(toy.Island.Min+toy.Island.Max)/2;var angle=index*2.4f;var radius=.9f+.35f*(index%4);
        return centre+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*radius;
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
