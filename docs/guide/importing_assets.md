# Check a model before importing it

Use importcheck to read a model and report what the importer finds. It accepts glTF 2.0 files (.glb or .gltf) and Wavefront OBJ geometry (.obj). It prints the outcome, every diagnostic, mesh bounds and triangle counts, normals/UV presence, winding changes, markers, textures, skins, joints and clips. It does not write or repair your source file on disk.

This guide uses the repository's self-authored import fixtures. They contain no game records or purchased art. The tool's report format is INVENTED; geometry conversion uses the existing public import API.

## Build the tool

You need this checkout and the .NET 10 SDK. Start at the repository root in Windows PowerShell 5.1. Every PowerShell block below was run in Windows PowerShell 5.1.26100.9711 against the isolated source copy.

```powershell
dotnet build samples/ImportCheck/ImportCheck.csproj --nologo -v:q
$tool = "samples/ImportCheck/bin/Debug/net10.0/importcheck.dll"
```

The build prints:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Elapsed time varies and is omitted here. Assigning $tool prints nothing. The build may restore the repository's existing packages.

The invocation is importcheck followed by one file, with an optional --frame and frame name. The examples run the built DLL through dotnet, so no global tool installation is needed. Quote a file path if it contains spaces.

| Exit code | Meaning |
|---:|---|
| 0 | Imports with no diagnostic. |
| 1 | Imports with warnings. Read each warning before using the result. |
| 2 | Refused. No usable import is reported. |
| 3 | Bad arguments or a missing file. The error stream includes usage. |

PowerShell keeps the native exit code in $LASTEXITCODE. Read it immediately after dotnet. A warning exit of 1 is an expected report, not a successful clean import. An existing unsupported file, such as .fbx, is refused with exit 2; a path that does not exist is an argument error with exit 3.

## First example: a one-metre cube

```powershell
dotnet $tool fixtures/import/im01_cube.glb
$LASTEXITCODE
```

Output:

```text
outcome: imports
frame: gltf
meshes: 1
mesh: node="Cube"; mesh="CubeMesh"; triangles=12; bounds=(-0.5,-0.5,-0.5)..(0.5,0.5,0.5); normals=yes; uv0=yes; winding_flipped=no
skins: 0
joints: 0
clips: 0
0
```

The bounds run from -0.5 to +0.5 on each axis, so the cube is one metre wide. Each mesh row names the scene node and mesh, then reports triangles, bounds, attribute presence and winding. Markers use node names. Texture rows include role, colour space, source and a missing flag. Diagnostics use severity: message, one per line. Rows are ordered consistently; numbers use decimal dots.

This is a CPU inspection. It does not show a preview, play animation or prove that a texture looks right in a renderer.

## Choose which way is up

The frame option applies one basis conversion at the import boundary.

| Frame | Mapping of source (x,y,z) | What it means |
|---|---|---|
| gltf | (x,y,z) | Leave the coordinates as they are. glTF uses a right-handed, Y-up frame. This is also the identity option for an OBJ already in your project's frame. |
| y_up_left_handed | (-x,y,z) | Keep Y up and mirror X. Triangle winding flips once to preserve outward faces. |
| z_up_right_handed | (-x,z,y) | Put source Y into Z and source Z into Y, while reversing X. The combined basis has positive determinant, so it does not add a winding flip. |

Use the mapping your project expects. A second conversion on a model already in that frame gives the wrong placement. Names alone cannot tell the tool how an arbitrary OBJ was authored.

The asymmetric arrow fixture has a marker at (-0.35,0.2,0.7) in its original frame. Try Z-up:

```powershell
dotnet $tool fixtures/import/im05_arrow.glb --frame z_up_right_handed
$LASTEXITCODE
```

Output:

```text
outcome: imports
frame: z_up_right_handed
meshes: 1
mesh: node="Arrow"; mesh="ArrowMesh"; triangles=24; bounds=(-0.3,0,-0.05)..(0.3,2,0.05); normals=yes; uv0=no; winding_flipped=no
marker: "RightMarker"; position=(0.35,0.7,0.2)
skins: 0
joints: 0
clips: 0
0
```

The marker becomes (0.35,0.7,0.2), as fixtures/import/expected.json specifies. The tests check all three frames against that file. Node mirroring and frame mirroring combine; the importer corrects winding once for their combined sign.

## A warning that keeps useful geometry

```powershell
dotnet $tool fixtures/import/im06_degenerate.glb
$LASTEXITCODE
```

Output:

```text
outcome: imports with warnings
frame: gltf
warning: im06_degenerate.glb: removed 1 degenerate triangle
meshes: 1
mesh: node="RepairTriangle"; mesh="RepairTriangleMesh"; triangles=1; bounds=(0,0,0)..(1,1,0); normals=yes; uv0=no; winding_flipped=no
skins: 0
joints: 0
clips: 0
1
```

The input has two triangles. One has zero area. The imported result keeps the other triangle and reports the repair count. The source file is unchanged.

| Warning | What happened | What to do |
|---|---|---|
| Removed degenerate triangles | A zero-area triangle was dropped from the imported result. | Fix collapsed or repeated vertices in the source mesh and export again if those faces should exist. |
| Negative world determinant; winding corrected | A node transform mirrors the mesh. | Check that the mirror is intended. Inspect the reported winding and resulting bounds before changing the model. |
| Optional texture not found | Geometry remains; the material uses a fallback for that texture role. | Put the named image beside the asset at the recorded relative path, or embed it in a new export. |
| Skinning not displayed (deferred) | The skin and clips are reported, with mesh data in the bind pose. | Use the static result knowingly; this tool does not play the clip or validate a future animation renderer. |
| UV0 is absent (OBJ) | The OBJ has no first texture-coordinate set. | Add/export UVs if the material needs them. A deliberately untextured model may not need them. |

A mesh row saying uv0=no is a measurement. It is not by itself a refusal. Check the actual outcome and diagnostics. A texture's sRGB label means colour data; linear means data such as normal vectors. The tool reports the importer's roles rather than changing source image encoding.

## A refusal with a next step

```powershell
dotnet $tool fixtures/import/im06_draco.glb
$LASTEXITCODE
```

Output:

```text
outcome: refused
frame: gltf
error: im06_draco.glb: required compression KHR_draco_mesh_compression is unsupported
2
```

This importer has no decoder for the required Draco extension. Re-export without compression. A required meshopt or unknown extension is also refused. Singular zero-scale transforms, bad indices, malformed or truncated files and resource paths that escape the model's folder are refused with named diagnostics. Fix the stated source problem and run the check again.

FBX is not read by this tool because its import API implements glTF and OBJ, with no FBX reader. In Blender use **File > Export > glTF 2.0 (.glb/.gltf)**, then choose glTF Binary (.glb) for a single-file export. Turn compression off for this importer. The menu is documented in the [official Blender glTF manual](https://docs.blender.org/manual/en/5.3/addons/scene_gltf2.html). This guide does not launch Blender.

Keep external images and buffers inside the model's folder. A .glb can still name external images, as the relative-texture fixture demonstrates; its extension alone is not proof that all resources are embedded.

## Use it on your own file

Replace the fixture path in a worked command with your model path. Use --frame only when your project needs that conversion. Exit 0 means this importer accepted the data it reports; it does not establish renderer quality or every possible format feature.

For the exact self-authored fixture facts and expected outcomes, read fixtures/import/README.md and expected.json. The guide's tests live in tests/Bed.Tests/ImportCheckTests.cs.
