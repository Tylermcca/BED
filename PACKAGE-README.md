# Start with a validated JSON document

**Prepared route; execution unverified.** No approved release identity or installed-package success is supplied by this document. Obtain reviewed release inputs from the maintainer first. The source template's marked slots must be filled before distribution. A review-reference string and generated files do not prove approval, package correctness or that these steps have run.

## Choose a role

| What you need | Role to request |
|---|---|
| Your own documents, validated commands, undo and save | Core |
| JSON documents and schemas with ready-made editing commands | Data: used below |
| UI-independent selection and editor models | Editor |
| Optional graphical panels or agent tools | Separate reviewed optional roles; not exercised here |

The namespace in this example is the current public API, not a promise that its package ID has the same spelling. The reviewed Data-role ID for this prepared route is **{{DATA_PACKAGE_ID}}**, exact version **{{VERSION}}**. The consumer target is **{{FRAMEWORK}}**. The project references every package selected by the reviewed manifest; unused optional roles are not silently removed. Package notices and dependencies still follow that selection.

This is a headless document example. It does not promise a runtime-only package or a standalone graphical editor from a library package.

## Obtain the reviewed starter workspace

For the consumer steps you need the exact, .NET-10-compatible SDK **{{SDK}}**, a prepared starter workspace in a new working folder, and an already-available local feed containing reviewed packages and dependencies. No source checkout or Python is needed to consume that prepared workspace. Nothing here installs tools, contacts a remote registry or supplies credentials.

The maintainer prepares that workspace from reviewed inputs. The preparation helper requires installed Python 3.12 and does not invoke an SDK. No actual release bundle or installation result is supplied by this template.

<details>
<summary>Maintainer preparation inputs and text-only command</summary>

The flat maintainer input set is:
- this README template and the original Program.cs below;
- prepare_package_first_use.py and its unchanged package_consumer_smoke.py contract helper;
- a maintainer-reviewed format-1 manifest selecting the package IDs/version, source hashes, framework, notices and dependency closure;
- the maintainer's explicit Data-role binding and published tutorial URLs.

You need no source checkout or private developer folder. Manifest source paths are maintainer provenance labels; the preparer does not follow them. The supplied Program.cs bytes must match the manifest's single pinned consumer input. Do not substitute a synthetic test manifest.

From that flat input folder, run the text-only preparer after supplying your own local paths and reviewed links:

~~~text
python prepare_package_first_use.py --manifest REVIEWED_INPUTS.json --program Program.cs --readme PACKAGE-README.md --data-package-id {{DATA_PACKAGE_ID}} --feed LOCAL_FEED --out NEW_STARTER_FOLDER --start-tutorial-url {{START_TUTORIAL_URL}} --adapter-tutorial-url {{ADAPTER_TUTORIAL_URL}}
~~~

LOCAL_FEED and NEW_STARTER_FOLDER are local folders you choose, not source-repository paths. REVIEWED_INPUTS.json is supplied by the maintainer, not filled with guessed IDs/version. The new output must not exist and must not overlap the feed. The preparer writes source/configuration only; its receipt says prepared-text-only, never compiled or passed. It reads no package archive. On an error, keep any partial new folder for inspection; do not overwrite it or blindly retry.

</details>

## Inspect the prepared workspace

Consumer.csproj has exact bracketed PackageReferences only, no ProjectReference or checkout paths. Input0.cs is the same original program as the pinned smoke input. NuGet.Config clears remote sources and fallback folders and names your local feed. global.json requires the exact installed SDK without roll-forward. Empty build/central-version boundary files prevent accidental parent-project inheritance.

Before any separately permitted SDK command, apply the local environment values in build-environment.json using your shell. It is data, not a launcher: no environment setting was changed by preparation. Values suppress SDK certificate/telemetry/first-run side effects and shared build servers, and place caches/CLI home/TEMP below this workspace. The preparer creates these empty local cache/temp directories, without populating them. Do not change HOME, global PATH or security settings. A missing SDK/pack/package fails; no download workaround is provided.

The same future CLI commands are intended for Windows, macOS and Linux; this is source preparation, not tested OS compatibility or a clean-machine certificate. In the generated folder, only after toolchain and consumer execution are permitted:

~~~text
dotnet restore Consumer.csproj --configfile NuGet.Config --packages .packages -p:NuGetAudit=false
dotnet build Consumer.csproj -c Release --disable-build-servers
dotnet run --project Consumer.csproj -c Release -- new-document
~~~

No command above ran for this delivery. A local feed is deliberately required for the bounded prepared route; it is not a claim that a public registry/release exists. The new-document argument must name a NEW data folder. A second run with that same folder refuses instead of overwriting data.

## What the program should demonstrate

It starts with one parcel, refuses an out-of-range edit without changing the document, applies a valid edit, undoes/redoes it, then saves and reopens three parcels. Expected stdout, **not captured runtime output**:

~~~text
Initial parcels: 1
Invalid edit refused; parcels: 1
Undo/redo restored parcels: 3
Saved and reopened parcels: 3
~~~

Expected success is exit 0 and parcels.json containing a parcels value of 3 inside the new data folder. Nonzero means stop and inspect the message; a compilation pass alone does not establish this behavior.

~~~csharp
{{PROGRAM}}
~~~

## Troubleshooting and next steps

- Missing package/version: check the reviewed inputs and local feed with the maintainer; do not guess a newer version or enable a remote fallback.
- Missing namespace/API: the reviewed Data-role package must contain the documented API and be compatible with this source. Selecting an arbitrary similarly named package is not proof.
- Program hash mismatch: use the exact original reviewed program. Regenerate the review contract if an authorized change is needed.
- Existing output/data folder: choose a fresh folder; the route never deletes old work.
- Restore/build/run permission or prerequisites missing: stop before that step. Prepared text and compile-only smoke are not execution permission.

For the existing source-checkout walkthrough, see [Start here]({{START_TUTORIAL_URL}}). For optional game-side UI extensions, see [Writing an adapter]({{ADAPTER_TUTORIAL_URL}}). Those tutorials have their own source/platform prerequisites and are not required for this package-only document route. Their publication destinations must be reviewed; this preparer checks URL syntax only.

The maintainer's package smoke compiles this same pinned source with PackageReference only. It deliberately never launches the consumer. It therefore cannot prove the expected stdout, save/reopen behavior, user onboarding, actual portable execution or release/legal readiness. Those remain separate validation.

## Provenance

INVENTED: parcel data and the small first-use witness. BORROWED: public DataKit/JsonSchemas/session/SetValue APIs. MIXED: explicit reviewed package inputs and clean consumer compilation from the existing package-verification contract, applied to a portable headless route. No game records, art or new dependency.
