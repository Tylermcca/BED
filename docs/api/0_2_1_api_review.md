# Public API review for 0.2.1-rc.1

## Approved API02 follow-up

Current implementation snapshot: `b53c2fd10a617bc367c4cf96915925819805178d` (API02 from Main `92b98dc`). The [compatibility ledger](compatibility_changes.md) records the lead's approved 0.x breaks, consumer migrations and absence of shims. The default plugin admission API is 0.2.0; the distribution/source assembly version remains independent. This inventory is not the previously published RC archive or a complete binary-compatibility certificate.

The regenerated delta still compares with preview source `faa4469`: 11 current src assemblies, 4,565 current public/protected entries versus 4,127 preview entries, 478 additions, 40 removals and 37 signature/raw-annotation changes (three displayed signature changes). Relative to API01's `9e68b42` source inventory, exactly 32 entries are no longer exported, all within PlayProbe, SceneIndexedGeometry/Vertex, SceneMeshResources<T> and BuildInputStaging. No new exported entry or other member signature/raw annotation changed in that comparison. These helpers have no sample/inspected adapter callers or supported public-signature leaks; App and Launcher grant friend access only to Bed.Tests. Supported build/adapter contracts remain public.

There are now 11 public positional-record parameter-documentation findings; API01's historical 12th finding belongs to the now-internal Vertex record. Internalization is not a claim that its old XML was repaired. The historical list below is retained as the original finding record. Current source builds all registered samples with 0 warnings/errors. Final scoped Release admission/Play/licence and representative production-helper tests pass 234/234; six new API02 cases cover default admission and internal exports/friend boundaries. The old 0.1 compatible registration case is retained and a 0.2 case added, rather than changing its success expectation into a refusal.

The original two-source detector/test code is unchanged. Its reviewed AuthoringInfo snapshot differs by only the approved 0.1→0.2 version expression; ApiVersion.Satisfies and the other snapshot are unchanged. Metadata extraction is SDK-only and never executes target assemblies. Full layout/ABI, arbitrary external plugin behavior and all semantic nullable-flow compatibility remain outside this result. Later commits that add public APIs require another inventory refresh.

## Historical API01 review

Review snapshot: `9e68b42`. Comparison: `faa4469`, the source commit introducing `<Version>0.2.0-preview</Version>`. No release tag exists in the local refs used for this review. This compares source-built version-introduction/current snapshots, not downloaded release binaries or later unmerged work.

The [metadata delta](0_2_1_api_delta.json) lists every added declared public/protected entry across all src projects, with assembly, signature, XML documentation status/identifier and source location. It retains removed and changed entries too. This inventory does not approve the changes or certify binary compatibility. The earlier two-source tripwire stays unchanged.

## Coverage and XML documentation

Release builds produced 10 preview assemblies and 11 current assemblies: 4,127 and 4,597 declared API entries. There are **510 additions, 40 removals and 37 changed entries**. Three changed entries have different displayed signatures; the other 34 have different raw custom attributes, mainly compiler nullability encodings. An encoding difference does not itself establish a semantic nullable change. Generated record members are retained, so these are not 510 handwritten design decisions.

| Assembly | Preview | Current | Added | Removed |
| --- | ---: | ---: | ---: | ---: |
| BED | 5 | 7 | 2 | 0 |
| Bed | 850 | 865 | 16 | 1 |
| Bed.Agent | 40 | 40 | 0 | 0 |
| Bed.Data | 400 | 404 | 4 | 0 |
| Bed.Editor | 1448 | 1575 | 127 | 0 |
| Bed.Editor.App | 256 | 357 | 138 | 37 |
| Bed.Editor.ImGui | 955 | 1072 | 119 | 2 |
| Bed.Editor.Text | 26 | 26 | 0 | 0 |
| Bed.Import | 139 | 154 | 15 | 0 |
| Bed.Runtime | 0 | 89 | 89 | 0 |
| bed-cli | 8 | 8 | 0 | 0 |

All current assemblies emit XML and build with zero warnings/errors. Of the 510 additions, 320 have compiler XML entries and 260 carry compiler-generated metadata (these sets overlap). The 190 entries without XML are 186 generated record members, two enum storage fields (`value__`) and two implicit parameterless constructors whose types are documented. No new handwritten public declaration lacking a member XML entry was found.

Complete parameter documentation is a separate question: CS1573 is suppressed. These 12 new or moved positional records lack the `<param>` entries required by CONTRIBUTING. Source edits are outside this card, so they remain findings before final.

| Record | Source | Missing positional parameter docs |
| --- | --- | --- |
| `Bed.Editor.App.ExportSmokeResult` | [VerifiedGameBuild.cs](../../src/Bed.Editor.App/VerifiedGameBuild.cs#L8) | `Build`, `Reason`, `Logs` |
| `Bed.Editor.App.MaterialTextureAdmission.Binding` | [MaterialTextureAdmission.cs](../../src/Bed.Editor.App/MaterialTextureAdmission.cs#L23) | `Ordinal`, `Material`, `Texture` |
| `Bed.Editor.App.MaterialTextureAdmission.Texture` | [MaterialTextureAdmission.cs](../../src/Bed.Editor.App/MaterialTextureAdmission.cs#L12) | `Path`, `SidecarText`, `Input` |
| `Bed.Editor.App.ProjectPlayLog` | [ProjectPlayHost.cs](../../src/Bed.Editor.App/ProjectPlayHost.cs#L25) | `Error`, `Text` |
| `Bed.Editor.App.SceneIndexedGeometry.Vertex` | [SceneIndexedGeometry.cs](../../src/Bed.Editor.App/SceneIndexedGeometry.cs#L11) | `Position`, `Uv` |
| `Bed.Editor.ImGui.GameExportSmokeResult` | [IGameExportHost.cs](../../src/Bed.Editor.ImGui/IGameExportHost.cs#L19) | `Build`, `Reason` |
| `Bed.Editor.ImGui.ProjectAssetCatalog` | [IAssetCatalogProvisioner.cs](../../src/Bed.Editor.ImGui/IAssetCatalogProvisioner.cs#L12) | `Browser`, `Catalog`, `Owner`, `Problems` |
| `Bed.Editor.ImGui.ProjectPlayOutput` | [IProjectPlayHost.cs](../../src/Bed.Editor.ImGui/IProjectPlayHost.cs#L10) | `Error`, `Text` |
| `Bed.Editor.GameBuildLog` | [GameBuildReceipt.cs](../../src/Bed.Editor/GameBuildReceipt.cs#L5) | `Error`, `Text` |
| `Bed.Editor.GameBuildReceipt` | [GameBuildReceipt.cs](../../src/Bed.Editor/GameBuildReceipt.cs#L8) | `SavedRevision`, `ManifestHash`, `OutputDirectory`, `ExitCode`, `Succeeded`, `Reason`, `Outputs` |
| `Bed.Editor.MeshPart` | [MeshPart.cs](../../src/Bed.Editor/MeshPart.cs#L5) | `Geometry`, `Material` |
| `Bed.Editor.MeshPartMaterial` | [MeshPart.cs](../../src/Bed.Editor/MeshPart.cs#L7) | `Ordinal`, `BaseColor`, `Texture` |

## Named contracts and assembly placement

| Contract | Assembly and documentation | Review |
| --- | --- | --- |
| `DataProject.Open(..., recursive, admit)` and `Recursive` | Data XML describes canonical relative paths, pure stable admission, exclusions and link refusal. | Project ownership belongs here. Original five-argument and intermediate six-argument CLR overloads remain; optional defaults now belong to the seven-argument overload. |
| `DataProject.FilesSaved` | Data XML names the synchronous caller thread, committed dirty files, one SaveAll notification and contained observer errors. | Post-save observation, not a veto; no-op/refused saves raise nothing. |
| `DataWorkspace.Ticked` | Editor.ImGui XML names owner-thread delivery after FinishSaves, including empty pumps. | Correct UI-owner boundary. A non-drawing host must pump FinishSaves; a hidden panel is not a scheduler. |
| `DocumentSession<T>.FileAccessRefusal` | Core XML specifies default-null path policy, Save/Revert/CheckDisk refusals and repeat-call behavior. | Session I/O belongs here. It adds no UI/game dependency or filesystem atomicity promise. |
| `Ui.InputTextMultiline` | Editor.ImGui XML specifies native InputText-equivalent focus/value/readonly semantics. | Native context/types belong here. The caller still owns validated draft application and saving. |
| Scene clipboard/group/rename | SceneClipboard, SceneGroupUi and SceneRenameUi remain internal UI implementation. Public SceneObjectOperations.Group/Duplicate are model-level Editor planners; RenameObject is a Core replayable command. | Keep clipboard/dialog lifetimes private. Public operations commit through the validated model/one undo step. RenameObject and its two positional inputs have XML. Existing APIs are not relabelled as new. |
| `bed-cli scene` | Executable wire surface; SceneCommandCall dispatcher is internal. | Six actions: add, move, duplicate, delete, rename, group. Preview defaults on and wins over --save; typed validation, readonly/protected policy and disk-conflict checks remain. Separate from MCP data tools; no CLI ungroup operation. See [command line](../guide/command_line.md#scene-commands). |

Runtime references framework assemblies only. Editor references Core/Data/Runtime and framework assemblies, with no BRUTAL/native UI reference. Saved-file readers and the scene model remain usable without UI. Native views/rendering stay in Editor.ImGui/Editor.Text/Editor.App; Import retains its explicit importer dependency. The pre-existing CLI dependency on Editor.ImGui for adapter discovery remains the layering debt described in [extension points](extension_points.md); no interface move is approved here.

## Compatibility decisions before final

| Observed change | Impact/migration | Lead decision |
| --- | --- | --- |
| GameBuildSpec, GameBuildReceipt and GameBuildLog moved from Editor.App namespace/assembly to Editor. | Better UI-free model placement, but old type identities/constructors are absent. GameBuildHost.Completion/DrainLogs expose new return types. Source consumers import Editor and rebuild. | Accept/document the prerelease break, or design compatibility shims. No old-binary compatibility claimed. |
| IProjectAdapter.PlaySnapshot and LinkedPlay(DataWorkspace) removed. | Adapter Play uses saved-files DefaultPlay(ProjectPaths). Old protocol adapters need migration; extra methods on an old implementation are not a host connection. | Confirm retirement and migrate consumers. Retained standalone linked-preview APIs do not reconnect those removed hooks. |
| AssetCatalog.Input.WithReimportedContent gained optional dependencies; BuildOutputInventory.Capture gained optional expectedHashes. | Old five-parameter CLR signatures are absent. Recompiled callers can omit new arguments; old compiled method references cannot. | Retain forwarding overloads or explicitly accept/version/document the binary break. |
| Original DataProject.Open five-argument entry lost optional metadata; new overload owns defaults. | Existing five-argument binaries retain their target; fewer-argument source calls bind to the new overload. Reflection/default or method-group consumers deserve review. | Preserve binary and representative optional/named-call checks; avoid claiming every possible overload expression is source-compatible. |

At the API01 snapshot, AuthoringInfo.ApiVersion remained `0.1.0` and assembly versions were `0.2.1.0`. App/package and plugin-admission versions have different jobs. An unchanged admission check does not prove old compiled consumers can call removed methods/types. The owner must select the API line and migration/support statement; this review assigns neither.

## Candidates for internal visibility

- Launcher.PlayProbe: smoke-probe implementation called by the executable/tests, rather than an adapter hook.
- SceneIndexedGeometry/Vertex and SceneMeshResources<T>: native upload layout, fence slots and resource lifetime; current product use is inside Editor.App. Prefer internal implementation unless external renderer hosts are deliberately supported. Diagnostic CopyVertices/CopyIndices alone do not justify a stable adapter API.
- BuildInputStaging: on-disk export staging implementation inside Editor.App. Prefer keeping staging internals private while GameExportPlan and host contracts carry the supported model.

These are recommendations, not visibility changes. Check external consumers/public signatures before internalizing anything already public. Keep admission/play/export interfaces used by alternate hosts; an Editor.App implementation does not make those interfaces accidental.

## Extraction and validation

Temporary tools use SDK10.0.402 MetadataLoadContext and Roslyn; no package was added and target assemblies were never executed. Scope: every src project, declared externally visible types and public/protected members; property/event accessors folded into the owner, inherited members represented at their declaring type. Generated record entries and enum literals/storage are retained. Signatures include defaults/constraints and base/interface lists; raw constructor/named custom attributes are preserved. Source syntax maps declarations and primary-record parameter names. Layout/ABI, type forwarding and semantic nullability equivalence are not certified.

The generator/build trees are temporary bin/api01 material, not a new product project or automatic compatibility gate. Reproduce by archiving the named preview sources (including embedded docs/spec schemas, samples/build/third_party dependencies), building Launcher and Agent.Cli at both roots in Release, loading all src outputs as metadata with their framework/dependency paths, comparing declared entries by assembly/member ID/signature, then matching compiler XML and source record parameters. No GUI/native target or game program is run.

Final builds at both snapshots succeeded with zero warnings/errors. An initial isolated archive lacked the embedded schema resource; adding that same old revision resource fixed staging without editing old source. Existing gates:

```powershell
python -B tools/check_api_source_baseline.py
python -B tools/test_api_source_baseline.py
```

Results: the original two snapshots are unchanged, complete compatibility UNKNOWN; six detector tests pass. This new JSON is a manual review baseline, not silently accepted input for the old detector. Every added entry and non-XML exception is represented; no downloaded-artifact or old-consumer execution proof is claimed.


## Scene-v2 command inventory after the reviewed metadata snapshot

The later scene-v2 catalog has these internal implementation entries, all payload version 1: `scene2.set`, `scene2.revert`, `scene2.break_link`, `scene2.instantiate`, `scene2.duplicate`, `scene2.update`, `scene2.group`, `scene2.reorder`, `scene2.remove_instance`, `scene2.add_sprite`, `scene2.set_sprite`, `scene2.compatible`, `scene2.paste`, `scene2.set_physics`, `scene2.edit_bodies`. The public factory remains Scene2Kit.Command; paste adds no exported implementation type or operating-system clipboard format. Its single fragment argument uses the existing scene-v2 shape with planner-baked identities, same coordinate space, complete copied instances and exact copied order. Merge appends only, reuses an accepted base only for identical content and validates the complete result through Scene2Format. See the [current command contract](../guide/api_overview.md#scene-v2-command-payloads). The historical metadata delta above remains identified by its own source snapshot.


## Approved saved physics capability addition

Scene2BuildSettings gains init-only `bool SupportsPhysics`, default false, keeping its existing constructor. ReadProject accepts optional boolean sceneBuild.physics; WriteProject omits false and preserves unrelated root/play fields. This opt-in describes a supported saved-file game profile. It does not add native dependencies, handles or simulation to the public framework surface. The game-owned physics descriptor/profile and executable are documented in the [current contract](../guide/api_overview.md#saved-physics-runtime-capability); they are not a new library runtime format.
