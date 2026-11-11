# Read saved levels in a game

Use `Bed` for the scene JSON reader and `Bed.Runtime` for prepared meshes and textures. A game needs these two libraries and the .NET 10 runtime. The [RuntimeInspect console sample](../../samples/RuntimeInspect/README.md) shows this route without an editor, renderer or importer dependency.

## Prepare the saved files

Open the project in BED, let asset preparation finish, and save the level. Play runs the game's program against saved files. Export copies the saved level and the required prepared cache into the game folder. See [Play](playing.md) and [Export](exporting.md).

The example expects:

```text
project/
  data/level.json
  .bed/cache/current.json
  .bed/cache/generations/<allocation-id>/
    owner.txt
    manifest.json
    <asset-guid>.bin
    ...
```

Use the actual published layout; do not manufacture a pointer or pick whichever generation directory looks newest. The cache reader follows the declared generation, validates its manifest and checks every artifact's length and SHA-256. It refuses undeclared content, linked paths and names that escape the flat generation directory. Source models, textures and the editor are unnecessary at runtime.

## Reference the two libraries

For a game project in this repository, adjust these relative paths to its location. For deployment, ship the resulting libraries beside your game.

```xml
<ItemGroup>
  <ProjectReference Include="../../src/Bed/Bed.csproj" />
  <ProjectReference Include="../../src/Bed.Runtime/Bed.Runtime.csproj" />
</ItemGroup>
```

The following is a complete console `Program.cs` for a .NET 10 project with implicit usings and nullable enabled. Pass the saved project folder as its single argument. It follows the same loading order as RuntimeInspect.

<!-- DOC16 compiling runtime example -->
```csharp
using Bed;
using Bed.Runtime;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SavedGame <project-folder>");
    return 2;
}
try
{
    var root = Path.GetFullPath(args[0]);
    var assets = ProjectAssets.Open(root);
    var level = SceneRuntimeReader.Read(
        File.ReadAllText(Path.Combine(root, "data", "level.json")),
        assetExists: assets.ContainsAsset);
    if (level.AssetIssues.Count > 0)
        throw new InvalidDataException("The level names an asset missing from its saved generation.");

    foreach (var item in level.Scene.Objects)
        Console.WriteLine($"Object {item.Id}: parent={item.Parent ?? "(none)"}, asset={item.Asset ?? "(none)"}");

    foreach (var id in level.Scene.Objects.Select(item => item.Asset).OfType<string>().Distinct(StringComparer.Ordinal))
    {
        var mesh = assets.ReadMesh(id);
        Console.WriteLine($"Mesh {id}: {mesh.Parts.Count} parts");
        foreach (var part in mesh.Parts)
        {
            var geometry = part.Geometry;
            Console.WriteLine($"Part: {geometry.Positions.Length} vertices, {geometry.Indices.Length / 3} triangles, UV origin {geometry.Uv0Origin}");
            if (part.Material?.Texture is {} textureId)
            {
                var texture = assets.ReadTexture(textureId);
                Console.WriteLine($"Texture {textureId:N}: {texture.Width} x {texture.Height}, {texture.Rgba.Length} RGBA bytes");
            }
        }
    }
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
    or ArgumentException or InvalidOperationException or DocumentFormatException or InvalidDocumentException)
{
    Console.Error.WriteLine("Cannot load saved level: " + error.Message);
    return 1;
}
```

Run the maintained sample from the repository root:

```powershell
dotnet run --project samples/RuntimeInspect/RuntimeInspect.csproj -- "C:/work/project"
```

The snippet test extracts the marked code, compiles it against only the core/runtime assemblies, then runs it on self-authored saved data with a textured two-part mesh. It also checks missing-generation and unsupported-level-version refusals.

## Keep object and material information

`SceneRuntimeReader.Read` validates the scene format, IDs, parents and named references. It preserves object order, properties and local position/rotation/scale values. It does not instantiate game entities or compute their world transforms. Your game creates its own objects, resolves parents/references and applies its own behavior.

Supplying `assetExists` reports missing assets in `AssetIssues` without dropping the objects. The example chooses to refuse any missing asset; another game can display placeholders. Omitting that predicate means assets were not checked.

Use the exact 32-character lowercase GUID stored in an object's `Asset`. `ReadMesh` does not look up a source filename or basename. Iterate `mesh.Parts` to retain every material. The compatibility `Positions` and `Indices` properties accept only a single-part mesh and refuse a multipart model.

Each part has positions, triangle indices and optional vertex-aligned normals and UV0. `Uv0Origin` distinguishes top-left UVs from raw OBJ bottom-left UVs; the reader never flips them. Material base colour is opaque linear RGBA, and its optional texture GUID resolves through `ReadTexture` on the same snapshot. Texture pixels are top-left RGBA8, with sRGB RGB and linear alpha. Upload them using your renderer's matching colour-space and UV convention.

## Ownership, bounds and failures

`ProjectAssets.Open` owns a read-only snapshot of one generation. New publications do not change that snapshot. `ReadAsset` and `ReadArtifact` return independent byte copies; decoded geometry and pixels are also owned arrays. Dispose your renderer's uploads according to your renderer's lifetime rules; the runtime snapshot has no disposal operation or write API.

The snapshot allows at most 4,096 artifacts, 64 MiB per artifact and 256 MiB total. Mesh decoding bounds vertices and triangle indices before allocating geometry. Texture dimensions are at most 8,192 per axis and the complete artifact must still fit its 64 MiB limit. Both decoders require exact supported headers, dimensions/counts and byte lengths.

A missing publication reports `Open the project in BED once to prepare its assets`. Corrupt cache JSON, changed hashes, unsupported binary versions, truncated payloads and excess budgets refuse with exceptions rather than returning partial results. Invalid scene syntax/version raises `DocumentFormatException`; invalid scene links or IDs raise `InvalidDocumentException`. Keep the previously loaded game state until a complete replacement has loaded successfully. This example catches load failures, prints the reason and exits 1; argument mistakes exit 2.

## Version-two levels and physics

The example above uses the version-one reader. A game that supports prefab and sprite levels opts in to version two. It first flattens the saved level with `Scene2Kit.Flatten`, which expands each placed piece from its accepted copy and applies its local changes, then reads the result with `SceneRuntimeReader.Read2`. Flattening never looks for or updates the original prefab files. `Read2` refuses a level that still contains unexpanded pieces. Sprite size, pivot, draw order and sampling survive, along with transforms, references and object order. See [Reading flattened scene-v2 data](api_overview.md#reading-flattened-scene-v2-data) and [Export](exporting.md).

Physics is optional, and a game must opt in to it. The separate [physics sample](../../samples/PhysicsStarter/README.md) shows how: it runs the saved gravity, fixed step and bodies in its own world, and exports its native physics library and notices with the build. Reading a level never starts a simulation, and the simulation never writes back to the saved level. Flat levels keep bodies moving in X and Y and turning around Z. If the game supports [Pause and Step](pause_step.md), each step advances one whole simulation tick.
