# Compatibility change ledger

## Approved API02 decisions — 2026-11-09

Claude approved the API01 decisions on 2026-11-09 (codex/inbox/messages.md, 20:22), for the next source/final build. The default plugin host now selects `AuthoringInfo.ApiVersion = 0.2.0`. This deliberately moves to a new pre-1.0 minor API line; it is not a patch/binary-compatibility promise or a new published distribution version. Distribution remains independently versioned. No support period or forwarding/type-forwarding/legacy-overload shim was approved.

| Accepted change | Consumer migration | Compatibility boundary |
| --- | --- | --- |
| `GameBuildSpec`, `GameBuildReceipt` and `GameBuildLog` moved from `Bed.Editor.App` to `Bed.Editor` | Replace fully qualified App type names with Editor type names; use `using Bed.Editor;` and reference that assembly/project, then rebuild consumers. `GameBuildHost` is still the App implementation; its receipt/log return types now use Editor contracts. | Old binaries reference different declaring type identities; a namespace using change in source is insufficient for an already compiled caller. No type forwarders. |
| Adapter `PlaySnapshot` / `LinkedPlay` hooks retired | Remove reliance on those hooks. Implement current `IProjectAdapter.DefaultPlay(ProjectPaths)` returning `PlaySettings` for the game's ordinary executable and saved-project arguments. Supply current `Build(DataWorkspace)` / `Export(DataWorkspace)` contracts when the project supports them. Run the game's saved-files/dirty-decision Play journey against the matching host. | An old adapter's extra methods may still exist on its class, but the current host does not call retired hooks. Standalone linked-preview types are not an adapter reconnection mechanism. |
| Longer optional-parameter methods replaced the old five-parameter CLR signatures of `AssetCatalog.Input.WithReimportedContent` and `BuildOutputInventory.Capture` | Recompile existing source calls against the current declaration; use the new optional dependency/expected-hash parameters when appropriate. Reflection/delegate binders must select the current parameter list explicitly. | Optional arguments provide source convenience after recompilation, not an old CLR entry point. Old compiled five-argument method references are not preserved by shims. |
| `DataProject.Open` optional metadata moved to the newest recursive/admit overload | Recompile omitted/named-argument calls; specify recursive/admit only when needed. Review reflection and method-group selection. Original explicit five-argument and intermediate six-argument CLR overloads remain callable. | Do not falsely classify all old Open binaries as broken: retained five/six-argument targets remain. Fewer-argument source calls now bind through the newest optional overload, and reflection/default metadata differs. |
| Renderer/probe/staging helpers internal | Stop referencing Launcher.PlayProbe; use public Program probe entry points/CLI. Host renderers through supported scene/presentation APIs, and pass GameExportPlan/host contracts instead of BuildInputStaging. Do not couple a consumer to SceneIndexedGeometry/Vertex or SceneMeshResources<T>. Rebuild against the supported surface. | These helpers were implementation details newly exposed during preview development. No external sample/inspected adapter/public signature depends on them. Only Bed.Tests receives friend access. |
| Plugin admission line 0.1 → 0.2 | Rebuild and review the plugin with the matching source API; after that, change its manifest requirement to `"api":"0.2"`. Both shipped sample plugin manifests and current guide snippets use the new line. | Merely editing an old DLL's manifest is not a binary migration. Same-minor and minimum-patch admission semantics are unchanged; 0.1 manifests now refuse before Register. |

Saved document/schema and command versions are unchanged by API02; no game-data migration is implied. Existing validation, save/conflict guards and public adapter/build/export contracts remain. The partial-source baseline changes exactly AuthoringInfo's selected version expression; its ApiVersion declaration snapshot and detector/tests are unchanged. Current compiled inventory is regenerated separately from that narrow tripwire. API01's positional-record param-doc findings remain tracked; the regenerated inventory reports those still public after internalization. This card does not silently waive or fix their documentation.

### Validation and review limits

Current source consumers were searched in src/samples and the inspected connected adapters before visibility changes. Source builds and scoped admission tests validate this tree; they are not an ABI/layout/type-forwarding or every-external-plugin certificate. The historical API01 snapshot below remains a record of what was observed before these approved decisions. The current delta's source revision/counts identify the API02 implementation snapshot; it is not the already published RC archive.

## Historical API01 source-build review (before API02)

The [review](0_2_1_api_review.md) and [compiled metadata delta](0_2_1_api_delta.json) compare the version-introduction preview source `faa4469` with `9e68b42`: 510 additions, 40 removals and 37 signature/raw-annotation changes across every src assembly. Generated members are included. The original two-source detector remains unchanged and does not certify this larger surface.

New admission/save/tick/session-access/multiline contracts have XML and their assembly boundaries are recorded. Twelve new or moved positional records still need the missing parameter docs listed in the review. Candidate renderer/staging/probe implementation types should be reviewed for internal visibility before final, without silently breaking existing consumers.

At that snapshot, decisions were still required for moved build type identities and affected return types; removed adapter PlaySnapshot/LinkedPlay hooks; old CLR overloads replaced by longer optional-argument methods; and DataProject.Open optional metadata changes. The review describes source migration and binary risks. Recording these deltas approves no API break, new API version, package identity or support period. AuthoringInfo.ApiVersion remains 0.1.0.

## Earlier preparation

- Added a proposed API support/review policy. No package identity/version or platform-support commitment selected.
- Existing plugin API remains0.1.0. `ApiVersion.Satisfies` and public `PlanRevision` remain unchanged.
- Complete reviewed assembly/public-package baseline and binary/consumer proof are pending. Compilation and metadata loading were not performed for this preparation.
- Accepted preparatory scope: AuthoringInfo.cs plus the leading ApiVersion declaration/preamble only. Added a conservative source-snapshot detector and six stdlib tests; full API/enum/binary coverage remains pending. No held security work is brought into scope.

## Required entry for an intentional API change

Record affected package/project and exact declarations; old/new behavior and signatures; source/binary/persisted-data impact; baseline additions/removals; approved API-line and package-version decisions (when made); deprecation/replacement/removal plan; consumer migration example; focused test results and unrun gates; reviewer and source reference. Do not record a planned version as released or remove a failure by silently accepting new expected output.

This is a compatibility-review ledger, not a claim of an existing release history or an automatically approved breaking change.
