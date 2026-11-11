# NuGet packaging dry run

Seven development libraries pack, and the accepted quick-start adapter builds from a local folder feed without source references. These archives are private experiments. The first audit found internal provenance in API documentation and an embedded schema; the owner corrected it, and an unchanged replay on a fresh snapshot found none of those leaks. Repository/readme/version decisions and public-release approval remain outstanding. No game was read, referenced, edited or built for this exercise. No external feed, publishing command or window was used.

**MIXED:** role-separated packages follow the existing architecture research (Unity's assembly split, Unreal's explicit roles and Stride's authoring/runtime split) and the .NET SDK's package model. **BORROWED:** the existing Text panel's ImCode bundling and notice-resolution approach. **INVENTED:** this local feed experiment and its original generic package README. This is a packaging proposal over today's projects, without changing their interfaces or architecture.

## Which projects are packages

| Working package ID | Role and boundary |
|---|---|
| Bed | Core authoring documents, validated commands, undo, serialization and asset identities. No external package. |
| Bed.Data | JSON editing, schemas, projects and checks; depends on Core. |
| Bed.Editor | UI-independent editor model, selection and inspectors; depends on Core and Data. |
| Bed.Import | Optional glTF importer; depends on Core and SharpGLTF.Core. |
| Bed.Agent | Optional typed sessions and MCP protocol; depends on Core, Data and Editor. This is the library, not the CLI host. |
| Bed.Editor.ImGui | Optional editor panels and adapter contract; depends on Editor and BRUTAL Core/ImGui. |
| Bed.Editor.Text | Optional raw JSON text panel; depends on Editor.ImGui and BRUTAL, and bundles the pinned ImCode assemblies. |

Keep Editor.App, Launcher and Agent.Cli as internal host/application projects for this proposal. Editor.App is a library-shaped host, but its platform renderer is not a public package here. Tests and samples are development inputs, not packages. The recipe packs an explicit list of seven projects; it does not run solution-wide pack. Vendored projects have their own upstream build props, so the root whitelist does not disable their independent pack targets.

There is no runtime-only project today. None of these authoring packages is proposed for a game's runtime build. The research's small runtime package is a future architectural task, not something this packaging exercise creates. The adapter contract still lives in Editor.ImGui, so even a command-line adapter pulls that UI dependency; moving it is outside this card.

The proposal is one Directory.Build.props overlay. It preserves the current framework, nullable/implicit-usings settings, central versions and documented-public-member build rule. It makes these seven projects packable and defaults other projects using that props file to non-packable. It sets working IDs, per-role descriptions, Tyler McCauley as author (from the existing copyright), MIT for the authoring code, README.md, copyright and portable symbol settings. Dependencies retain their own licences, especially BRUTAL's custom terms.

Version scheme: synchronized SemVer across this family; begin with a user-approved 0.x prerelease series while API stability is assessed, incrementing for incompatible changes and documenting them. This experiment uses only 0.0.0-local.a194, an internal trial identifier, not a proposed public version. The user chooses the public name, stable version and release. Repository URL, branch and commit are deliberately REPLACE placeholders, with an example.invalid URL; no real commit was invented or queried. Replace them before release. No package-project URL is fabricated.

## What's in each

All seven library packages carry the existing LICENSE.txt byte-for-byte, an original generic README.md, their DLL and XML documentation under lib/net10.0, and normal NuGet metadata. The root internal README, source, samples, tests, coordination files and source maps are excluded. The generic README used here is scratch evidence, not a third proposed public file: adoption requires a reviewed PACKAGE-README.md at the repository root, and the props deliberately fails packing if it is missing.

| Package suffix | .nupkg entries | Direct dependencies as emitted in nuspec |
|---|---:|---|
| Core (Bed) | 8 | none |
| Data | 8 | Core: trial version |
| Editor | 8 | Core, Data: trial version |
| Import | 8 | Core: trial version; SharpGLTF.Core 1.0.7 |
| Agent | 8 | Core, Data, Editor: trial version |
| Editor.ImGui | 27 | Editor: trial version; Ahwoo.Brutal.Core 2026.10.1; Ahwoo.Brutal.ImGui 2026.10.2 |
| Editor.Text | 31 | Editor.ImGui: trial version; the same two BRUTAL versions |

The plain versions in those dependency declarations are NuGet minimum constraints, not exact-version brackets. The fresh restore's exact selected versions are separately recorded. This trial's consumer pins its own direct authoring reference with brackets. No broad dependency-policy change is hidden in the proposal.

Each library also has one .snupkg with five entries: its PDB, minimal nuspec and NuGet bookkeeping. The SDK omits authors, licence and readme from the symbol nuspec; these are paired symbol companions, not self-contained distributions. All seven portable PDBs were opened with System.Reflection.Metadata: all 73 document paths begin with the virtual mapped root. SourceLink is disabled and no source retrieval is configured, so symbols have local line information without hosted source navigation. The two bundled ImCode DLLs have no companion PDB in this symbol set: upstream symbol coverage remains thin.

Editor.ImGui and Editor.Text each include the exact root NOTICE.txt and 18 terms files from all eight resolved BRUTAL managed packages: their licences/notices, including the ImGui and Localization third-party notices. Every file was compared byte-for-byte with the dependency actually restored in scratch. They are namespaced under licenses by package ID. BRUTAL remains an ordinary dependency, not a relabelled MIT binary bundled by this proposal. Its existing embedded branding resources are retained unchanged.

The host still has work to do under the recorded BRUTAL terms: credits, the logo before its primary function when graphics are possible, and NOTICE plus applicable third-party terms beside the distributed program. Putting them in a library archive does not display a logo or copy these files to a consumer's output. This proposal adds no transitive deployment target and makes no licence-compatibility or publication-clearance claim. Existing host distribution targets remain a separate route.

Editor.Text's existing targets additionally put ImCode.dll and ImCode.Text.dll beside its DLL, plus THIRD-PARTY-NOTICES.txt and licenses/ImCode-LICENSE.txt. The notice and full MIT licence match the pinned vendored source. There is no dependency on a nonexistent ImCode NuGet package. Import instead references SharpGLTF.Core normally, retaining its MIT terms in that dependency; it does not bundle SharpGLTF binaries. The earlier dependency record describes an application distribution with more packages/RID assets, so it is supporting context, not a substituted inventory of these libraries.

Inspection opened every entry in all 14 created archives, recording names, sizes, SHA-256, dependencies and metadata. Nine DLLs (seven libraries and two ImCode assemblies) were inspected for type definitions and assembly references: 595 metadata type rows, including generated/module rows, with no private game type or assembly names found. No native renderer/application distribution was packed. This is bounded metadata/content inspection, not a proof against every possible secret or opaque binary payload.

## A consumer builds from them

The .NET SDK used was 10.0.402. Packing and restoring used only a scratch folder feed, seeded with the bytes of 23 already-installed dependencies at the versions selected by the existing net10 graphs. No new third-party dependency or version was introduced and no package was downloaded. NuGet.Config cleared package sources and fallback folders; every invocation also set RestoreSources to that folder and NuGetAudit=false. SDK framework packs were installed prerequisites. TEMP, TMP, CLI home, package caches and HTTP/plugin caches stayed inside this card's scratch.

Representative commands below were run inside their indicated scratch folders. The evidence receipts retain every full absolute invocation and its real output. Do not run them over the live repository: the props overlay and generic readme belong in a new scratch source copy.

```powershell
# In the source copy, repeated for the explicit seven-project list above:
dotnet pack src/Bed/Bed.csproj -c Release -o ../packages --nologo -v:minimal -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:UseSharedCompilation=false -p:PathMap="${PWD}=/_/"
# Copy the seven resulting nupkg files into the local dependency feed.
# In a separate fresh consumer with its own empty cache and no ancestor build settings:
dotnet restore QuickGardenAdapter.csproj --configfile NuGet.Config --source ../feed --packages ../consumer-cache -p:NuGetAudit=false --no-http-cache -v:normal
dotnet build QuickGardenAdapter.csproj -c Release --no-restore --nologo -p:UseSharedCompilation=false
```

The actual consumer copies GardenAdapter.cs and InventoryCheck.cs unchanged from the accepted quick start. Its project replaces the source ProjectReference with one PackageReference to Bed.Editor.ImGui, version [0.0.0-local.a194]. Empty local Directory.Build.props and Directory.Packages.props block accidental inheritance from the outer repository. The final fresh restore selected 26 packages, using only the named local feed and its newly empty private cache; its assets contain no project-reference libraries. Build succeeded with zero warnings and errors.

An independent executable consumer references all seven packages and uses another initially empty cache: 30 resolved packages, again only the local feed and no source references. Its build also has zero warnings and errors. It recognizes the original garden sample through the unchanged adapter, opens the data and runs the registered frozen-input inventory check to a complete result with no findings. It then inspects the DLL/PDB metadata described above. No window, graphical host, game, live AI client or gameplay is exercised.

This proves local package resolution, compilation, basic managed adapter execution and optional Text assembly resolution. It does not prove every public API, interactive host loading, native UI portability, online-feed behavior, package signing, reproducible byte-identical archives or an export/runtime integration.

## Problems

1. **Initial provenance leaks, fixed and replayed.** The first archives had nine private-project-name occurrences in four XML files (Core, Data, Editor, Editor.ImGui), 52 internal handoff-ID occurrences in six XML files (all except Import), and one internal ID in Data.dll's embedded reference-map schema. The owner corrected 24 source/comment/schema files during the active card. A fresh 119-file snapshot, seven new pack invocations, new empty-cache consumers and unchanged content/metadata checks now find zero private-name or internal-note matches. Documentation and the embedded schema remain present. Original archives, findings and exact before/after diffs are retained privately; do not distribute that first set. This is a bounded clean result for the final snapshot, not public-release approval. No private records or game assemblies were copied into either trial.
2. **Machine-path scan triage:** four apparent network-path matches, across two entries, were regex escape sequences in generated API regex help and the embedded schema. They are not addresses. All entries were scanned as UTF-8 and UTF-16, while PDB documents and DLL references were independently parsed. No physical source path was found. The scanner is bounded and those false positives remain recorded.
3. **Release metadata and content are incomplete.** Repository placeholders, trial version/IDs and scratch-only README are intentional. Do not adopt the props and call it publication-ready. The public-source-set proposal is an older source census, not clearance for these archives or for history/branding/vendored content. Name, Ahwoo's OK and publication remain the user's.
4. **Dependency and deployment limits.** A preview Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115 is in the existing transitive closure. The UI/native dependency and BRUTAL attribution duties remain even for an adapter with empty Configure. Full host distribution checks, licence compatibility, signing/source-hosting and update policy need their own decisions and tests. No new versions or legal conclusions were chosen here.
5. **No runtime package or CLI tool package.** The architecture research's runtime split is absent, and the current adapter-contract location brings UI packages into the adapter closure. This work packages existing contracts only. Making a dotnet tool, packaging the app/host or moving the contract would be separate work.
6. **Failures retained, then corrected.** The first proposal failed MSBuild metadata batching on project-reference items; its failed receipt and initial props remain. The first consumer inherited central package settings and failed restore; isolation files fixed that. A successful second build was followed by an audit assertion incorrectly expecting a trailing cache-path separator; the final proof used a fresh consumer/cache and normalized paths. An initial verification script wrongly assumed symbol nuspecs retain library licence/author fields; actual SDK output is documented above. No library behavior, test or schema was changed to obtain passing builds.

The exact document gate passes, all seven final pack invocations pass, both final fresh consumer builds pass, and notice/dependency/isolation checks pass. A green document/build is not public-release approval. Seed 194051 drew 20 checks, supplemented with all findings, dependency sets, vendored terms, isolation and metadata results: the final draw has 32 distinct passing judgments from 81 verified invariants. The earlier 41-of-92 review is retained with its correctly identified, since-corrected provenance holds. No finding was hidden or a source contract/test weakened.
