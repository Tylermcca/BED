using Brutal.Authoring;
using Brutal.Authoring.Runtime;
namespace RuntimeInspect;

// Console-only consumer: no editor, importer, renderer or source-asset lookup.
public static class Program
{
    public static int Main(string[] args)=>Run(args,Console.Out,Console.Error);
    public static int Run(string[] args,TextWriter output,TextWriter error)
    {
        if(args.Length!=1){error.WriteLine("Usage: RuntimeInspect <project-folder>");return 2;}
        try
        {
            var root=Path.GetFullPath(args[0]);
            var assets=ProjectAssets.Open(root);
            var level=SceneRuntimeReader.Read(File.ReadAllText(Path.Combine(root,"data","level.json")),assetExists:assets.ContainsAsset);
            if(level.AssetIssues.Count>0)throw new InvalidDataException("The saved level names an asset outside its published generation.");
            output.WriteLine($"Generation {assets.Id}: {assets.ArtifactNames.Count} artifacts");
            foreach(var item in level.Scene.Objects)
                output.WriteLine(FormattableString.Invariant($"Object {item.Id}: parent={item.Parent??"(none)"}, position=({item.Position.X}, {item.Position.Y}, {item.Position.Z}), asset={item.Asset??"(none)"}"));
            var meshCount=0;var textures=new HashSet<Guid>();
            foreach(var id in level.Scene.Objects.Select(o=>o.Asset).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                var mesh=assets.ReadMesh(id);meshCount++;
                output.WriteLine($"Mesh {id}: {mesh.Parts.Count} parts");
                for(var i=0;i<mesh.Parts.Count;i++)
                {
                    var part=mesh.Parts[i];var geometry=part.Geometry;
                    output.WriteLine($"  Part {i}: {geometry.Positions.Length} vertices, {geometry.Indices.Length/3} triangles, UV origin {geometry.Uv0Origin}");
                    if(part.Material?.Texture is {} texture&&textures.Add(texture))
                    {
                        var pixels=assets.ReadTexture(texture);
                        output.WriteLine($"  Texture {texture:N}: {pixels.Width} x {pixels.Height} RGBA8");
                    }
                }
            }
            output.WriteLine($"Loaded {level.Scene.Objects.Length} objects, {meshCount} meshes, {textures.Count} textures.");
            return 0;
        }
        catch(Exception exception)when(exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or DocumentFormatException or InvalidDocumentException)
        {error.WriteLine("RuntimeInspect refused: "+exception.Message);return 1;}
    }
}
