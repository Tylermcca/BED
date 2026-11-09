# SceneStarter export review

A tiny self-authored console runtime sample. It uses only `Brutal.Authoring` core and the .NET 10 runtime, not BED editor assemblies, BRUTAL native libraries or a window. This is a framework-dependent Windows sample: the destination needs the .NET 10 runtime. It is not a standalone game or a release approval.

The supplied `scene.json` contains two objects, `root` and `child`, with a parent link, local transforms and a named reference. `--smoke` reads through `SceneRuntimeReader`, reconstructs runtime identities, checks the exact sample facts and refuses missing, malformed, unsupported-version or changed input. It never saves or repairs the scene. The standalone --smoke fixture refuses external assets. The separate linked Play v2 input accepts only GUIDs and copied derived bytes from the host-admitted pinned catalog; it does not discover asset files or change this export inventory.

## Owner acceptance

These commands are for the owner to run from a trusted build checkout. Replace `FRESH_OUTPUT` with a new empty output directory. Do not reuse a previous export directory or disable inventory checks to accommodate unexpected files.

```
dotnet publish samples/SceneStarter/SceneStarter.csproj -c Release --no-restore --self-contained false -p:UseAppHost=true -p:PublishSingleFile=false -o FRESH_OUTPUT
```

Then, from that output directory, run `SceneStarter.exe --review .` to check the files, and `SceneStarter.exe --smoke scene.json` to load the scene.

`--review` permits exactly `SceneStarter.exe`, `SceneStarter.dll`, `SceneStarter.deps.json`, `SceneStarter.runtimeconfig.json`, `Brutal.Authoring.dll`, `scene.json`, `LICENSE.txt` and `NOTICE.txt`. It rejects directories, links, empty or oversized files, missing notices, unexpected assemblies/source files and dependencies outside the two declared assemblies. After the inventory and scene smoke both succeed, it prints path/length/SHA-256 records and the smoke result. Exit 3 means refusal. It does not publish, replace a prior export, grant rights or prove binary authenticity against a hostile producer. Review only the owner's freshly built trusted sample.

Record the source commit, exact build and smoke commands, exit codes, stdout/stderr and the complete inventory. On separate copies, verify missing scene, malformed scene and unsupported version fail with exit 3 and no success text. Keep the valid output unchanged. Missing or failed owner smoke means export acceptance remains incomplete even if headless tests pass.

## Integration boundary

The approved future flow is: successful owned build, strict output walk/hash, EX04 staging from that captured inventory, then owner runtime smoke. The current `GameBuildHost` still requires pre-pinned outputs; this sample does not change that host or loosen `ExportStaging.Record`. Headless tests construct the post-build inventory from actual compiled sample bytes and demonstrate unchanged staging hash checks. They call the sample entry logic in-process; they are not evidence that a published/unpacked executable started successfully. No complete source-to-export integration is claimed until the host producer and owner smoke are supplied.

The included LICENSE and NOTICE are preserved repository texts. No art, private data, editor source, test helper or external asset is part of this sample. Packaging rights review remains separate from byte inventory validation.

The post-build host now accepts `BuildOutputDeclaration`, bound to admitted source check and origin receipts. It captures the exact declared output set after a zero exit, pins an output manifest with `SourceManifestHash`, and applies unchanged staging byte checks. Required notice coverage is explicit. The older pre-pinned staging overload remains compatible; it is not the new post-build route. No startup producer, owner publish, or exported-process smoke receipt is supplied by these source changes.
