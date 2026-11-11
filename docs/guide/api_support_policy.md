# API support and compatibility review

Status: maintenance/review policy with the approved 0.2 plugin API line; not a release announcement, product-name decision, package-version assignment or binary-compatibility certificate. Names here are current source project names. No support period or platform certification is promised by this document.

## Three versions with different jobs

- `AuthoringInfo.ApiVersion` currently equals `0.2.0`. The default host rejects manifests requiring the old 0.1 line; rebuild/review the plugin for the selected host, then declare `"api":"0.2"`. Changing the manifest alone cannot repair a binary incompatibility. A plugin manifest's `Api` is the minimum host API it requests. This is a host admission check, not a comparison of assemblies or proof that all plugin operations work.
- A distribution version identifies a build. The reviewed source build uses `0.2.1-rc.1` (assembly version `0.2.1.0`); that does not change the plugin admission API version or select independently versioned NuGet package IDs/support. Do not use the historical `0.0.0-local.a194` experiment as a selected version. Distribution and plugin API numbers need not match; release notes must distinguish them.
- Document/schema versions and command/journal revisions govern persisted data. An API or package increment does not migrate documents. `AuthoringInfo.PlanRevision` is an existing public constant, not a package version; removing or changing it requires API review. Do not quietly remove it as internal-document cleanup.

## Existing plugin admission behavior

`ApiVersion.Satisfies(required)` in `src/Bed/Plugins.cs` requires equal major versions. When the host major is zero, the minor must also match and the host patch must be at least the requested patch. For a positive major, the host's major/minor/patch tuple must be at least the requested tuple. Thus a `0.1.2` host accepts `0.1.0`, but a `0.2.0` host does not accept `0.1.0`. A `1.2.0` host accepts `1.1.9`, not `2.0.0`.

`ApiVersion.Parse` accepts one to three numeric components with missing components treated as zero; it does not support prerelease/build suffixes. Do not feed a NuGet prerelease label to it. Keep this admission behavior unchanged unless a separately reviewed API change explicitly replaces it. The rules are unchanged. ApiLineApi02Tests exercises default-host admission for 0.1, 0.2, a newer 0.2 patch, 0.3 and 1.0; PluginAcceptanceTests retains explicit compatible registration on both 0.1 and 0.2 hosts.

## Proposed change and deprecation rules

Within one pre-1.0 API minor line, avoid source/binary breaks and preserve documented behavior. A deliberate break requires review of the affected declarations, consumers, persisted formats and migration route; use a new API minor line before release rather than claiming patch compatibility. After1.0, breaking API changes require a major-line decision. Owner approval still selects actual versions; this policy does not assign them.

Before removal, prefer an additive replacement and an obsolete/deprecation message with a concrete migration example. Record the first warning release and intended removal boundary. Do not promise a support duration not selected by maintainers. A new overload, enum member, default parameter, generic constraint, nullability annotation or changed constant can affect callers even when old names remain; review such changes rather than treating all additions as safe. Review errors, ordering, threading, cancellation, save/refusal and outside-change behavior separately from declaration changes.

Each intended break needs a baseline diff, changelog entry, rationale, migration instructions and explicit reviewer approval together. A successful regeneration is not that approval. Never update expected output solely to silence a failing compatibility check. Do not change signatures, safety contracts or schema semantics to fit an implementation.

## Framework and platform boundaries

The current shared build targets `net10.0`, with nullable analysis and warnings as errors. That source setting is not certification of every OS, architecture or native deployment. Core/Data/Editor model consumers and optional UI/native consumers have different dependencies; inspect the selected project's actual references. `Editor.ImGui` uses BRUTAL/native ImGui and a host-supplied context; `Editor.Text` adds its documented ImCode integration. GUI/native use needs the appropriate runtime, platform and upstream notices. A model-level build or no-window test does not certify a renderer or application on another platform.

Any release support matrix must name tested framework, OS/architecture, native dependency versions, scenarios and known exclusions. No broader platform support or long-term servicing promise is adopted here. Upstream terms remain applicable; API policy is not a licence clearance.

## Baseline and verification boundaries

A source-derived baseline is a review tripwire, not a compiled public API or ABI inventory. It can omit generated declarations, conditional-compilation outcomes, inherited members, compiler-emitted signatures, metadata and type forwarding. A whole-source hash detects edits but overflags private implementation/documentation and is not a semantic diff. Every baseline must state its exact allowlist, exclusions, extraction method and source revision/bytes.

A future authoritative baseline must be generated from the approved package/assembly set using an authorized compiler/metadata workflow, then reviewed. Include public/protected signatures, overloads, generic constraints, defaults, enum underlying types/numeric values, constants, attributes relevant to callers and assembly identity/forwarding. Add focused checks for intentional additions/removals and enum changes, plus representative consumer compilation and behavior tests. No analyzer/dependency is presumed approved. Do not scan excluded source or load assemblies as a workaround for a tool restriction.

Until that workflow runs, label compatibility UNKNOWN rather than green. Current narrow documentation or source checks do not satisfy complete package API coverage. Track pending evidence and intentional changes in `../api/compatibility_changes.md`.

Provenance: MIXED. Existing semantics come from AuthoringInfo.cs/Plugins.cs and framework/dependency project files. Review/deprecation workflow is INVENTED for these boundaries; it does not replace owner release decisions.

## 0.2.1 review inventory

The [current source-build review](../api/0_2_1_api_review.md) adds a [metadata delta](../api/0_2_1_api_delta.json) for every src assembly between the preview version-introduction snapshot and the reviewed main commit. All 510 new entries are listed, including generated members; removed and changed signatures/annotations are retained. It also records XML parameter gaps, assembly placement, CLI wire behavior and candidates for internal implementation status.

This is a manual review baseline, not an automatic compatibility gate or approval of an API change. It does not replace the two-source detector below. Known old method/type removals require a migration/API-line decision before claiming binary compatibility; an unchanged AuthoringInfo.ApiVersion does not resolve them. Rebuild matching adapters against the selected host and review its compatibility ledger. No support duration or general prerelease binary guarantee is introduced.

## Current provisional tripwire

`docs/api/source_compatibility_baseline.json` captures exactly two snapshots: all of AuthoringInfo.cs, and Plugins.cs's preamble plus its leading ApiVersion declaration. No other public types or enum values are inventoried by that detector. The detector uses the current column-zero closing-brace layout, not a C# grammar; layout drift is a review signal/error, not something it silently normalizes. It stops before later declarations. Captured method bodies/comments/whitespace deliberately overflag non-API changes.

Run `python -B tools/check_api_source_baseline.py` from a source checkout. Exit0 means only those two snapshots are unchanged; exit1 means source drift; exit2 means invalid/missing inputs or extraction failure. None means complete API compatibility. `python -B tools/test_api_source_baseline.py` runs the bounded stdlib detector tests. It detects a synthetic signature/constant change and fails closed on missing scope; no C# is compiled or loaded. Enum/public-package compatibility remains outside this partial gate.

The checker has no baseline-generation/accept switch. An intentional snapshot change needs human review, a corresponding ledger entry and separately edited expected data; approval of this initial baseline is not preapproval of future changes. Source revision is provenance, not a package version or proof of a clean tree.
