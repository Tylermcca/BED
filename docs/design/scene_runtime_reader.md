# Pure-core scene reader contract

SC-S0b, based on main `1658914`. **MIXED:** existing immutable Scene/SceneFormat/SceneValidator/SceneAssets semantics behind a small read-only consumer entry point. No new scene schema or runtime package is introduced.

## Current entry point

`SceneRuntimeReader.Read(text, kind = "scene", assetExists = null)` lives in `src/Brutal.Authoring/SceneRuntimeReader.cs`. The existing core project has no editor, ImGui, rendering or package references. It accepts already decoded JSON, validates the complete scene and returns immutable scene data. It does not open files, create a DocumentSession, load meshes, resolve absolute paths, spawn objects, calculate world transforms, or run a game. The optional predicate is supplied by the caller; the reader itself performs no external operations.

Source contracts reused without modification:
- `Scene.cs:54-88`: immutable object order and strict SceneFormat kind/version policy.
- `Scene.cs:91-132`: optional local position/rotation/scale defaults, exact vector lengths and finite components.
- `Scene.cs:199-221`: ID syntax, duplicate IDs and parent/named-reference graph validation.
- `Scene.cs:224-232`: missing assets produce per-object diagnostics and retain placeholders.
- `Commands.cs:57-61` and `DocumentSession.cs:494-499`: existing format and validation exception shapes.

## Version, identity and transform policy

Only SceneFormat version 1 is supported. The caller chooses the expected kind, including custom kinds; it is not accepted merely because the input claims it. Unknown versions, kinds, fields, duplicate fields, malformed JSON and incorrectly shaped/non-finite numeric values retain SceneFormat's exact refusal path/message. No migration or best-effort parse occurs here.

IDs remain ordinal, case-sensitive and subject to SceneValidator's existing lowercase identifier grammar. Preserve source object order; parent objects need not precede children in the array. Every parent and named object reference must resolve. Parent cycles refuse; named references retain the existing cycle allowance. Do not reorder objects, synthesize IDs, repair links or discard invalid objects to produce a partial result.

Position, quaternion and scale are preserved as local values; parent links are separate. Defaults remain origin, identity quaternion and unit scale. The current parser checks component count and finiteness; it does **not** normalize quaternions, require unit length, forbid zero/negative scale, or validate a renderer's transform decomposition. The reader adds none of these restrictions silently. A stricter future runtime-instantiation policy must be separately agreed and tested. No world-space flattening or shear approximation is performed.

## Errors and assets

1. Parse first. A failure throws the same `DocumentFormatException`, including Path and Message.
2. Validate the full parsed Scene. A failure throws `InvalidDocumentException` carrying every original Issue in original order. No usable scene result is published.
3. Only then perform optional asset checks. `AssetsChecked == false` explicitly means not checked, not success. A supplied predicate receives the original asset strings through SceneAssets in object order, once per referencing object, with no new memoization.
4. Missing assets retain the Scene and produce the existing error-severity `AssetIssues`. Structural validity is not a promise that a runtime can start. A consumer may show placeholders or refuse Play/export according to its own declared gate; it must not ignore the asset diagnostics and call the scene fully ready.
5. A predicate exception propagates as a caller/service failure, never converted to “asset missing.” No partial result is returned. Issue lists in successful results are read-only snapshots.

The later shared asset catalog supplies the predicate or a separately agreed richer resolver. This reader does not create a rival registry, interpret asset strings as trusted filesystem paths, or cache across catalog generations. Object references and asset references remain different concepts.

## Extraction boundary and integration

The future small runtime package would contain Scene/SceneObject, SceneFormat, this reader and the pure validation/error dependencies they actually need. The package is not created here. Extract shared model/error definitions once, with stable type identity and a dependency test; do not copy the schema into two libraries or introduce a runtime-to-editor dependency. Existing editor code would reference the same extracted definitions.

The typed host specified by SC-S0a still owns the one authoritative editing session. This reader is for immutable input snapshots, not a second writer. Reading saved output must preserve IDs, parents, local TRS, properties, object references and original asset strings. A game-owned instantiator may build runtime objects only after it has accepted the result and asset policy. No editor panels or UI types cross that boundary.

## Proof supplied and limits

`SceneRuntimeContractTests` uses only synthetic in-memory JSON and pure core calls. It covers defaults and full local TRS, forward parent order, custom kind/canonical round-trip, wrong versions and malformed shapes, exact ordered validation failures, cyclic/missing links, non-normalization compatibility, retained missing-asset placeholders, resolver call order/failure, and no asset lookup on structural refusal.

The test project is the existing repository test harness, which has other editor references; that fact is not used to claim a standalone runtime consumer build. The production reader is compiled in the existing dependency-free core project. A future package consumer/dependency receipt belongs to extraction, not this card. No actual game, mesh, external assets, renderer, file-save lifecycle or runtime performance is validated here.
