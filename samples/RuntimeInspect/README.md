# RuntimeInspect

A console example that reads a saved level and its prepared asset generation, then prints every object, mesh part and used texture size. It references only `Bed` (the scene reader) and `Bed.Runtime` (asset readers). It opens no window and writes nothing.

From the repository root, run:

```powershell
dotnet run --project samples/RuntimeInspect/RuntimeInspect.csproj -- samples/SceneStarterProject
```

First open the level sample in BED once and let its assets finish preparing. The program needs the saved `data/level.json` and `.bed/cache/current.json` with its declared generation. A copied build with that layout works too; source art and BED do not need to be present while it reads.

`ProjectAssets.Open` checks the entire declared generation before returning owned bytes. `ReadMesh` accepts the exact 32-character lowercase asset GUID saved in the level; iterate `Parts` so multi-part models keep all their geometry and materials. Resolve a material's texture GUID with `ReadTexture` on the same snapshot. UV conventions and top-left RGBA pixels are retained by the readers.

If the generation is missing, the program prints “Open the project in BED once to prepare its assets” and exits 1. Other read/validation failures also exit 1 with a reason; missing arguments exit 2. A successful run exits 0. No import, repair or save is attempted.
