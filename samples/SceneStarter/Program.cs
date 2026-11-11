using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Bed;

namespace SceneStarter;

public static class Program
{
    public static int Main(string[] args) => args is ["--linked-play"] ? LinkedScene.Run(Console.In,Console.Out,Console.Error) : Run(args, Console.Out, Console.Error);

    // Same entry logic for the owner-run executable and focused headless tests; no editor session or repair.
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if(args.Length==0||args[0] is "--project" or "--seconds" or "--exit-code")
            {
                var project=Environment.GetEnvironmentVariable("BED_PROJECT");double? seconds=null;int exitCode=0;var exitChosen=false;
                var seen=new HashSet<string>(StringComparer.Ordinal);
                for(var i=0;i<args.Length;i+=2)
                {
                    if(i+1>=args.Length||!seen.Add(args[i]))throw new ArgumentException("Use each sample option once with its value.");
                    switch(args[i])
                    {
                        case "--project":project=args[i+1];break;
                        case "--seconds":
                            if(!double.TryParse(args[i+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<0||value>60)
                                throw new ArgumentException("Sample seconds must be between 0 and 60.");
                            seconds=value;break;
                        case "--exit-code":
                            if(!int.TryParse(args[i+1],out exitCode)||exitCode<0||exitCode>255)throw new ArgumentException("Sample exit code must be between 0 and 255.");
                            exitChosen=true;break;
                        default:throw new ArgumentException("Use --project, --seconds or --exit-code for saved-project Play.");
                    }
                }
                if(string.IsNullOrWhiteSpace(project))throw new ArgumentException("Choose a saved project with --project or BED_PROJECT.");
                var saved=SavedScene.Open(project);
                if(saved.Level2 is {} v2)output.WriteLine($"SceneStarter loaded saved level-v2: {v2.Scene.Objects.Length} objects, {saved.Meshes.Count} meshes, {saved.Textures.Count} textures.");
                else output.WriteLine($"SceneStarter loaded saved level: {saved.Level.Scene.Objects.Count()} objects, {saved.Meshes.Count} meshes.");output.Flush();
                if(seconds is {} duration)Thread.Sleep(TimeSpan.FromSeconds(duration));else if(!exitChosen)Thread.Sleep(Timeout.Infinite);
                return exitCode;
            }
            if(args is ["--compile-scene2",var sceneProject])
            { Scene2Compiler.Compile(sceneProject);output.WriteLine("SceneStarter generated the saved scene-v2 runtime files.");return 0; }
            if(args is ["--build",var inputDirectory,var outputDirectory])
            {ExportBuild.Create(inputDirectory,outputDirectory,AppContext.BaseDirectory);output.WriteLine("SceneStarter build copied checked saved project files.");return 0;}
            if(args is ["--smoke-payload",var payloadPath])
            {
                var loaded=ScenePayload.Read(File.ReadAllBytes(payloadPath));
                output.WriteLine($"SceneStarter payload smoke passed: {loaded.Scene.Objects.Count()} objects.");return 0;
            }
            if(args.Length!=2 || args[0] is not ("--smoke" or "--review"))
                throw new ArgumentException("Use --smoke SCENE_FILE or --review UNPACKED_FOLDER.");
            if(args[0]=="--smoke") Smoke(File.ReadAllText(args[1]));
            else
            {
                var files=ExportInventory.Read(args[1]);
                Smoke(System.Text.Encoding.UTF8.GetString(files["scene.json"]));
                output.WriteLine(JsonSerializer.Serialize(files.OrderBy(f=>f.Key,StringComparer.Ordinal)
                    .Select(f=>new { path=f.Key, length=f.Value.Length, sha256=Convert.ToHexStringLower(SHA256.HashData(f.Value)) })));
            }
            output.WriteLine("SceneStarter smoke passed: root, child, local transforms and reference preserved.");
            return 0;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or InvalidDataException or DocumentFormatException or InvalidDocumentException or JsonException or KeyNotFoundException or FormatException or OverflowException or System.Text.DecoderFallbackException)
        { error.WriteLine("SceneStarter refused: "+e.Message); return 3; }
    }

    public static void Smoke(string text)
    {
        var read=SceneRuntimeReader.Read(text,assetExists:_=>false);
        if(read.AssetIssues.Count!=0 || read.Scene.Objects.Any(o=>o.Asset is not null))
            throw new InvalidOperationException("This sample requires no external assets.");
        var objects=read.Scene.Objects.ToDictionary(o=>o.Id,StringComparer.Ordinal);
        if(objects.Count!=2 || !objects.TryGetValue("root",out var root) || !objects.TryGetValue("child",out var child)
            || root.Parent is not null || child.Parent!="root" || root.Position!=new Vector3(2,3,4)
            || child.Position!=new Vector3(1,0,0) || root.Scale!=Vector3.One || child.Scale!=new Vector3(2)
            || root.Rotation!=Quaternion.Identity || child.Rotation!=Quaternion.Identity
            || child.References.Count!=1 || !child.References.TryGetValue("anchor",out var target) || target!=root.Id)
            throw new InvalidOperationException("The sample identities, parent, reference or local transforms differ.");
        // Resolve actual runtime parent/reference identities, not merely a successful JSON parse.
        var runtime=objects.Values.ToDictionary(o=>o.Id,o=>new RuntimeObject(o));
        foreach(var item in runtime.Values)
        { item.Parent=item.Source.Parent is { } parent?runtime[parent]:null; item.Anchor=item.Source.References.TryGetValue("anchor",out var id)?runtime[id]:null; }
        if(!ReferenceEquals(runtime["child"].Parent,runtime["root"]) || !ReferenceEquals(runtime["child"].Anchor,runtime["root"]))
            throw new InvalidOperationException("Runtime identity links differ.");
    }
    private sealed class RuntimeObject(SceneObject source)
    { public SceneObject Source {get;}=source; public RuntimeObject? Parent {get;set;} public RuntimeObject? Anchor {get;set;} }
}
