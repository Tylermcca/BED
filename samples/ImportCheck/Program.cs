using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Brutal.Authoring.Import;

namespace ImportCheck;

public static class Program
{
    private const string Usage = "usage: importcheck <file> [--frame gltf|y_up_left_handed|z_up_right_handed]";
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length is not (1 or 3))
            return Bad(error, "Give one model file and, optionally, --frame plus a frame name.");
        if (args.Length == 3 && args[1] != "--frame")
            return Bad(error, $"Unknown option {Quote(args[1])}; use --frame.");
        string frameName = args.Length == 3 ? args[2] : "gltf";
        ProjectFrame? frame = frameName switch
        {
            "gltf" => ProjectFrame.Gltf,
            "y_up_left_handed" => ProjectFrame.YUpLeftHanded,
            "z_up_right_handed" => ProjectFrame.ZUpRightHanded,
            _ => null
        };
        if (frame is null) return Bad(error, $"Unknown frame {Quote(frameName)}; choose one from the usage line.");
        if (string.IsNullOrWhiteSpace(args[0]) || !File.Exists(args[0]))
            return Bad(error, $"File {Quote(args[0])} was not found; give an existing model file.");

        ImportResult result;
        try
        {
            result = Path.GetExtension(args[0]).Equals(".obj", StringComparison.OrdinalIgnoreCase)
                ? ObjImporter.Import(args[0], frame)
                : GltfImporter.Import(args[0], frame);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A command-line boundary still gives a readable refusal if an importer gap is encountered.
            output.WriteLine("outcome: refused");
            output.WriteLine($"error: Could not read {Quote(Path.GetFileName(args[0]))}: {SingleLine(e.Message)}");
            return 2;
        }

        output.WriteLine("outcome: " + (result.Outcome switch
        {
            ImportOutcome.Imports => "imports",
            ImportOutcome.ImportsWithWarnings => "imports with warnings",
            _ => "refused"
        }));
        output.WriteLine("frame: " + frame.Name);
        foreach (string diagnostic in result.Diagnostics.Order(StringComparer.Ordinal))
            output.WriteLine(SingleLine(diagnostic));
        if (result.Asset is { } asset) Print(asset, output);
        return result.Outcome switch
        {
            ImportOutcome.Imports => 0,
            ImportOutcome.ImportsWithWarnings => 1,
            _ => 2
        };
    }

    private static int Bad(TextWriter error, string message)
    {
        error.WriteLine("error: " + SingleLine(message));
        error.WriteLine(Usage);
        return 3;
    }

    private static void Print(ImportedAsset asset, TextWriter output)
    {
        output.WriteLine("meshes: " + asset.Meshes.Count);
        foreach (var mesh in asset.Meshes.OrderBy(m => m.Node, StringComparer.Ordinal)
                     .ThenBy(m => m.Mesh, StringComparer.Ordinal).ThenBy(m => m.Material, StringComparer.Ordinal))
        {
            var bounds = mesh.Bounds;
            output.WriteLine($"mesh: node={Quote(mesh.Node)}; mesh={Quote(mesh.Mesh)}; triangles={mesh.Triangles.Length / 3}; " +
                $"bounds={Vector(bounds.Min)}..{Vector(bounds.Max)}; normals={Yes(mesh.Normals.Length > 0)}; " +
                $"uv0={Yes(mesh.Uv0.Length > 0)}; winding_flipped={Yes(mesh.WindingFlipped)}");
        }
        foreach (var marker in asset.Markers.OrderBy(m => m.Node, StringComparer.Ordinal)
                     .ThenBy(m => m.Position.X).ThenBy(m => m.Position.Y).ThenBy(m => m.Position.Z))
            output.WriteLine($"marker: {Quote(marker.Node)}; position={Vector(marker.Position)}");
        foreach (var texture in asset.Textures.OrderBy(t => t.Material, StringComparer.Ordinal)
                     .ThenBy(t => t.Role, StringComparer.Ordinal).ThenBy(t => t.Source, StringComparer.Ordinal))
            output.WriteLine($"texture: material={Quote(texture.Material)}; role={Quote(texture.Role)}; " +
                $"colour_space={texture.ColourSpace}; source={Quote(texture.Source)}; missing={Yes(texture.Missing)}");
        output.WriteLine("skins: " + asset.Skins);
        output.WriteLine("joints: " + asset.Joints);
        output.WriteLine("clips: " + asset.Clips.Count);
        foreach (var clip in asset.Clips.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Seconds))
            output.WriteLine($"clip: {Quote(clip.Name)}; seconds={Number(clip.Seconds)}");
    }

    private static string Number(float value) => value == 0 ? "0" : value.ToString("G", CultureInfo.InvariantCulture);
    private static string Vector(Vector3 v) => $"({Number(v.X)},{Number(v.Y)},{Number(v.Z)})";
    private static string Yes(bool value) => value ? "yes" : "no";
    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static string SingleLine(string value) => value.Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
