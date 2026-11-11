# The library at a glance

Use this page to choose the library pieces for your editor. Start with a document, commands and a session, then add the views or tools you need. To build your own document type step by step, see `your_first_document_type.md`. The exact promises are in `docs/spec/first_slice.md`.

The libraries are BED's building blocks. Their namespaces still use the earlier working name, Bed.

## The idea in one paragraph

- **Your document is an immutable C# record** (a level, a board, a tile map, anything).
- **You change it only through commands.** A command is a small record whose properties are its arguments.
- **A session applies each command.** It checks the result with your validator and commits only a clean result. It keeps undo and redo, records every step in a journal, and saves atomically.
- **Everything else builds on that one path:**
  - the editor;
  - scripts;
  - replays;
  - later, plug-ins and AI helpers.

The library never knows what your game is.

## The pieces

| You want to … | Use | In |
|---|---|---|
| Describe your document's file format | `IDocumentFormat<TDoc>`, with `StrictReader` for reading and `Canonical.WriterOptions` for writing | `Commands.cs`, `Json.cs` |
| Say what makes a document valid | `IValidator<TDoc>`; combine several with `AllOf<TDoc>` | `Commands.cs`, `Links.cs` |
| Define an edit | a record implementing `ICommand<TDoc>`, returning `CommandResult<TDoc>.Done(...)` or `.Refused(...)` | `Commands.cs` |
| Register edits under stable ids | `CommandCatalog<TDoc>.Register<T>("game.move", 1, "Move")` | `CommandCatalog.cs` |
| Open, edit, undo, save | `DocumentSession<TDoc>`: `Open`, `Execute`, `ExecuteBatch`, `Undo`, `Redo`, `Save`, `CheckDisk`, `Revert` | `DocumentSession.cs` |
| Record and replay a session | `session.Journal`, `JournalFile.Write`/`Read`, `DocumentSession<TDoc>.Replay` | `Journal.cs` |
| Report a problem | `Issue.Error(message, objectId, path)` | `Issue.cs` |
| Read older files | `Migrations` with steps, wrapped in `MigratingFormat<TDoc>` | `Migrations.cs` |
| Give assets permanent ids | `AssetRecord.Create` (writes `<file>.asset.json`), `AssetIndex.Scan` | `Assets.cs` |
| Link objects to objects, assets or outside lists | `ILinkSchema<TDoc>`, `LinkValidator<TDoc>`, `LinkPlans` for deletes and duplicates | `Links.cs` |
| Offer variants (damaged, gold, LOD) | `VariantSet` | `Variants.cs` |
| Snap to a grid, keep an origin convention | `Grid`, `Origin` | `Placement.cs` |
| Build an editor: selection, workspaces, palette, focus | `EditorModel<TDoc>` with your `IDocumentOutline<TDoc>` (in `Bed.Editor`) | `EditorModel.cs` |
| Place objects in a level, room or layout without writing a document type | `Scene`, `SceneObject`, `SceneFormat`, `SceneValidator`, `SceneKit.Catalog()` (add, move, rotate, scale, parent, asset, property, reference, remove, duplicate) | `Scene.cs` |
| Edit scenes from a script, build step or AI helper | the `scene` tool (`samples/SceneTool`): `scene do room.json scene.move '{"id":"lamp","position":[1,0,2]}'`, plus `tree`, `check --assets`, `apply`, `replay`, `commands`; `--kind` for a game's own scene kind | `Program.cs` |
| Edit a scene in the editor | `SceneEditing.Outline`, `SceneEditing.ObjectType()`, `SceneEditing.CommandFor` (in `Bed.Editor`) | `SceneEditing.cs` |
| Plug your own game into the general app: its rules, checks and panels, with no code in the tools | an adapter (`IProjectAdapter`): [Writing an adapter](writing_an_adapter.md) |
| Add a plug-in: commands, object types, workspaces and shortcuts, checked all or nothing | `PluginManifest` (`plugin.json`), `IEditorPlugin<TDoc>`, `PluginHost<TDoc>`; the sample is `samples/LightsPlugin` | `Plugins.cs` (core and editor) |
| Edit properties of the selection | `Inspector<TDoc>` with your `ObjectType<TDoc, TObj>` | `Inspector.cs` |
| Draw the editor window | `EditorWindow<TDoc>` (in `Bed.Editor.ImGui`) | `EditorWindow.cs` |
| Edit a stepped integer with keyboard or assistive actions | `Ui.InputInt(ImString, ref int, int step = 1, int stepFast = 100, ImGuiInputTextFlags flags = 0)` (in `Bed.Editor.ImGui`) | `Ui.cs` |
| Import models | `GltfImporter`, `ObjImporter`, `MarkerFile` (in `Bed.Import`) | `GltfImporter.cs`, `ObjImporter.cs` |

## How they fit

```
 your game's types (records)        your commands (records)       your validator
            \                               |                          /
             +---------------- DocumentSession<TDoc> -----------------+
                     | undo/redo, journal, atomic save, outside-edit check
                     |
        EditorModel<TDoc>  (selection, workspaces, palette, focus)  ← the editor window draws this
                     |
     JournalFile / Replay  (scripts, tests, CI)   ← and later plug-ins, AI helpers: all through commands
```

## Rules the library keeps for you

- **A refused edit changes nothing.** That covers the document, undo, redo, the journal and the revision.
- **A failed save leaves the old file as it was.**
- **Save, load, save gives the same bytes.** A journal replayed twice gives the same document.
- **Reading is strict:** every problem names its JSON path, so nothing is half-read and nothing is guessed.
- **Selection, focus and workspaces are view state.** They never mark a document unsaved or enter undo.
- **An asset's id is assigned once and never re-bound by path.**
- **Vector arguments (`Vector2/3/4`, `Quaternion`) are journaled as arrays** of finite numbers, the way scene files write them.

## Current API review

See the [0.2.1 source-build API review](../api/0_2_1_api_review.md) for project admission, managed-save events, the owner tick, session file-access policy, multiline text and scene/CLI operations. Its metadata baseline also lists removals and documentation gaps; read the [support policy](api_support_policy.md) before assuming adapter binary compatibility.

Saved-game code can reference `Bed.Runtime` for `ProjectAssets`, `BedMeshReader` and `BedTextureReader` without an editor or BRUTAL dependency. The [runtime guide](runtime.md) shows the supported file-reading route.

## Beyond this page

This page maps the core library. These came later and have their own guides:

- the 3D viewport and picking (`Viewport.cs`, `Picking`), with window controls in [Keyboard](editing_data.md#keyboard);
- the planar marker/edge map: [Adding a map](adding_a_map.md);
- the real window (`EditorApp`, `ProjectLauncher`): `developer_walkthrough.md`;
- game adapters, loaded from their DLLs in `adapters/` or named with `--adapter`: `writing_an_adapter.md`; and plug-in panels: `writing_a_plugin.md`;
- the AI command line and its MCP server (`McpServer`): `using_a_screen_reader.md`.

What's still to come is listed in `docs/goals.md` and `docs/plan/readiness.md`.

## Scene version-two core contracts

These framework-only types are in `Bed`. They provide strict formats and pure resolution; the editor, the command line and the version-two runtime reader build on them. Existing `Scene`, `SceneObject`, `SceneFormat` and v1 commands retain their behavior. Definitions retain supplied fields before defaults for RFC 8785 revision hashes. All input/expansion budgets refuse excess; resolution does not load assets or update snapshots from disk.

| Type | Use |
|---|---|
| `SceneSpace` | Choose 3D or planar XY. |
| `SceneSpriteFilter` | Choose nearest or linear image sampling. |
| `SceneSprite` | Store sprite size, pivot and draw order. |
| `Scene2Object` | Pair immutable object content with optional sprite metadata. |
| `PrefabReference` | Address a definition node or exposed slot. |
| `PrefabNode` | Store one immutable definition node. |
| `PrefabSlot` | Declare a required or optional exposed reference. |
| `PrefabDefinition` | Keep a validated definition and original supplied JSON. |
| `PrefabBase` | Keep an accepted revision independent of source discovery. |
| `PrefabOverrideOperation` | Distinguish explicit set and unset. |
| `PrefabOverride` | Address an atomic local field. |
| `PrefabInstance` | Store placement, node IDs, bindings and local changes. |
| `Scene2` | Own the version-two authoring document. |
| `Scene2OriginKind` | Distinguish ordinary objects, wrappers and linked nodes. |
| `Scene2Origin` | Locate the authoring owner of effective fields. |
| `Scene2Resolution` | Read effective objects with their origin map. |
| `Scene2Format` | Read and write strict scene version two. |
| `PrefabDefinitionFormat` | Read and write a standalone definition. |
| `Scene2Validator` | Validate a version-two candidate. |
| `PrefabDefinitionValidator` | Validate a rooted definition. |
| `Scene2Resolver` | Expand accepted snapshots without file or asset reads. |
| `Scene2Kit` | Create strict v2 commands and their versioned catalog; v1 journals stay separate. |
| `ScenePhysics` | Explicit finite gravity and a fixed step of 0.0001..0.1 seconds. |
| `SceneBodyMotion` | Static, dynamic or game-driven kinematic motion. |
| `SceneShape` | Closed validated collision descriptions, with no native handles. |
| `SceneBoxShape` | Positive local half-extents and an optional zero-default center. |
| `SceneSphereShape` | Positive local radius and center. |
| `SceneCapsuleYShape` | Positive radius and cylindrical half-height, excluding hemispheres. |
| `SceneMeshBoundsShape` | Request a captured model fit at build time; core performs no asset lookup. |
| `SceneBody` | Motion, shape, friction in 0..1 and positive kilograms for dynamic only. |
| `Scene2BuildSettings` | Read/write the explicit saved scene-v2 source, private generated path and supported spaces in project settings; preserves unrelated fields. |
| `ProjectBuildStamp` | Bounded content fingerprints and output-hash freshness checks for opt-in saved-file builds; no execution or admission. |
| `Scene2RuntimeReadResult` | Retain a validated flattened v2 scene and optional asset-availability findings. |
| `Scene2Kit.Flatten(Scene2)` | Expand accepted snapshots to ordinary v2 objects with explicit effective order, without reading source files or publishing output. |
| `SceneRuntimeReader.Read2(string, Func<string, bool>?)` | Read only flattened saved v2; refuse prefab authoring content before resolution. |
| `Scene2EditPlan` | Preview an immutable candidate and commit once against its original authority, revision and saved baseline. |

### Reading flattened scene-v2 data

The framework-only core contains both runtime text readers. `SceneRuntimeReader.Read` retains its version-one contract; games that support version two opt into `Read2`. It preserves sprite size, pivot, order and sampling alongside local transforms, parent links, properties and references. Returned objects follow the explicit effective order. Runtime `SpriteGeometry` supplies the shared corners and stable painter order; reverse that order for picking.

`Scene2Kit.Flatten(snapshot)` expands only embedded accepted definitions. It neither discovers current source files nor changes the authoring scene. The caller must supply a saved, admitted snapshot and own its freshness checks. A missing external definition file does not invalidate an embedded acceptance. `Read2` refuses nonempty bases or instances with a build-required message before resolving them, and applies the accepted v2 byte/depth/count and semantic checks to flattened input.

```csharp
Scene2 flat = Scene2Kit.Flatten(savedSnapshot);
string generatedText = new Scene2Format().Write(flat);
Scene2RuntimeReadResult runtime = SceneRuntimeReader.Read2(generatedText);
```

An optional asset predicate reports availability in effective object order; it does not approve paths, decode images/models or grant distribution rights. Without the predicate, `AssetsChecked` is false. These APIs do not publish generated files, declare runtime capabilities, integrate a build or export assets; that project integration is separate. Runtime receives ordinary saved objects and needs no prefab source files or editor assemblies.

### Scene-v2 command payloads

`Scene2Kit.Command(action, args)` accepts an argument object. The factory detaches input JSON and refuses unknown members, duplicate keys and unsupported actions. Each command uses the same `DocumentSession<Scene2>`; it edits authoring records rather than saving a resolved flat copy. Catalog payload versions start at 1 and use the `scene2.` prefix. The individual command implementations remain internal.

| Action | Argument members |
|---|---|
| `set` | `id`, `field`, optional `value`; absent value is explicit unset. |
| `revert` | `instance`, `node`, `field`; removes one explicit override. |
| `instantiate` | `key`, `baseId`, complete standalone `definition`, `placement`, exact `nodeIds`, `bindings`. |
| `duplicate` | `instance`, `newKey`, exact `idMap` covering wrapper and every accepted node. |
| `update` | `instance`, fresh `baseId`, complete `definition`, `newNodeIds` for exactly the newly added node keys. |
| `break_link` | `instance`; keeps effective object IDs, transforms, references, sprites and full order. |
| `remove_instance` | `instance`; refuses outside dependencies and prunes only unused accepted bases. |
| `reorder` | `ids`, an exact permutation of every expanded scene ID. |
| `group` | Explicit new group `id` and distinct selected `ids`; preserves root world transforms and refuses internal linked-node edits, singular transforms and shear. |
| `add_sprite` | `object`, including its explicit admitted-image identity and complete `sprite` metadata. Asset admission itself belongs to the host. |
| `paste` | `fragment`: existing scene-v2 shape with the same space, explicit copied objects/bases/complete instances and copied objectOrder. IDs are already allocated. Append-only; identical accepted bases may be reused, every graph/collision/order/limit failure refuses atomically. |
| `set_physics` | Optional `physics`: complete gravity/fixedStepSeconds block; omission removes it only when no effective body remains. Explicit null refuses. |
| `edit_bodies` | `edits`: 1..20480 strict `{id,field,value?,revert?}` entries, at most 4096 IDs and five nonoverlapping body addresses per ID. Only `/body`, `/body/motion`, `/body/shape`, `/body/massKg`, `/body/friction`. Missing value unsets an allowed address; `revert:true` forbids value and removes an accepted-node local override. All edits form one candidate, validated once, and one journal step. |
| `set_sprite` | `id`, complete `sprite` object containing `size`, `pivot`, `order`, `filter`; linked nodes record four atomic field overrides. |
| `object.add`, `object.move`, `object.rotate`, `object.scale`, `object.rename`, `object.set_parent`, `object.set_asset`, `object.set_property`, `object.set_reference`, `object.remove`, `object.duplicate` | Existing scene-v1 argument shapes, translated to v2 ownership through `scene2.compatible`. Ordinary sprite metadata and root snapshots/order survive. Individual structural edits of linked nodes refuse with Break Link guidance. Generated ordinary duplicate IDs are captured in the recorded map. |

A `set` field is an atomic `/position`, `/rotation`, `/scale`, `/asset`, `/properties/<escaped-name>`, `/references/<escaped-name>` or one sprite/body field. Body fields use the approved physics vocabulary below. Reference sets use `{ "object": "target_id" }`; only asset, property, reference, whole body and body mass fields may be unset. Revert removes local ownership even when its value equalled the accepted base; it leaves placement alone. Whole-instance duplicate remaps copied internal references and keeps external references unchanged.

Source Update retains explicit local values and surviving IDs/ordinals. New IDs are explicit inputs and are inserted after the nearest preceding source node (or the placement). Deleted overridden or referenced nodes, deleted bound/required slots, incompatible property types, bad maps and invalid graphs refuse the whole candidate. Accepted source bytes and allocations are journaled, so Undo/Redo and replay do not consult a source file. Source discovery, source freshness and user review belong to the typed host integration.

```csharp
var command = Scene2Kit.Command("set", JsonSerializer.SerializeToElement(new
{
    id = "door", field = "/position", value = new[] { 3f, 2f, 0f }
}));
if (Scene2EditPlan.Capture(session, command, out var plan, out var why))
{
    // Present plan.Candidate; the owning host checks current admission/source/edit guards.
    bool committed = plan!.Commit(session, out why);
}
```

Capture does not dirty or save the document. Commit requires the same session, revision and saved-file baseline, then creates one undo step and journal entry; Undo followed by an edit still invalidates an older preview. A plan never bypasses the host's read-only, Play, modal, asset or source checks, which the host must perform before calling it. The host and CLI routes below bind and save files; the core APIs themselves perform no file discovery or saving.

### Scene-v2 typed host

These contracts are in `Bed.Editor`:

| Type | Use |
|---|---|
| `Scene2DocumentEntry` | One scene-v2 editor/session authority; use DocumentHost for source-aware edit and checked save/close. |
| `Scene2Editing` | Read-only effective-object Outliner policy for immutable scene-v2 documents. |

Bind an admitted name with `BindScene(name, "scene", out why)`, then use `OpenScene2`/`TryGetOpenScene2`. A version-one or data authority cannot coexist at that name. `CaptureScene2Edit` creates a pure candidate; `CommitScene2Edit` rechecks this host/entry, revision, saved-file baseline, read-only/Play/check state and selected source bytes before one undo/journal step. Only `SaveScene2` writes, through validation, `Scene2SaveCheck`, policy/revision/admission rechecks and disk-conflict protection. A legacy data SaveCheck requires an explicit typed policy. `CloseScene2` requires Save, Discard or Cancel and retains a refused draft.

The source registry reads admitted saved files only, by definition GUID, and rejects duplicate GUIDs or unsaved source drafts. It scans at most 10,000 admitted files / 64 MiB per source lookup, with 32 MiB reads and the definition format's 4 MiB limit. A command cannot supply an arbitrary definition path or inline source content. Instantiate/update journal accepted JSON and allocated IDs once; replay never discovers files. Missing source still permits edits, save/reopen and replay of accepted content; requesting a fresh source update requires an admitted source.

`DataProject.FileAccessRefusal(name)` exposes the current safety/admission refusal independently of read-only/save policy and generic-file diagnostics. Typed/source readers use it before access; it opens no file and retains the existing project path policy. Scene-v2 additionally refuses reparse traversal even in legacy flat projects.

### Sprite image captures

| Type | Assembly and responsibility |
| --- | --- |
| `SpriteGeometry` | Runtime: pure top-left raster to local/world XY corners and stable draw order; no scene reader or editor dependency. |
| `SpriteAssetDefaults` | Editor: strict version-one image defaults in existing settings; seeds explicit size/pivot/filter once. |
| `SceneSpritePlacement` | Editor: opaque scene authority/revision and exact image selection; DocumentHost.CaptureSpritePlacement and PlaceSprite share checked Scene2 history. |
| `SpriteAssetClosure` | Editor: bounded pinned texture leases and producing records for all effective sprites; revalidation checks admission/content/policy, never grants export rights. |

Image settings may contain `spriteDefaults` with exactly `version:1`, positive finite `pixelsPerMetre`, a two-component bottom-left `pivot` within 0..1, and `filter:"nearest"` or `"linear"`. Absence means 100 pixels/metre, centre and nearest. `WithSettings` returns detached settings preserving unrelated keys and the caller retains the original asset GUID, provenance and rights record. No new asset-record root format or importer identity exists. Placed sprites store explicit values; reimport, image resizing and defaults changes never recompute them.

Sprite placement currently accepts admitted PNG images carrying decoded RGBA8-sRGB metadata. Capture is a detached intent; PlaceSprite accepts an ID and local XY position, rechecks scene revision, saved baseline, admission, focus, editor policy and image content/policy tokens, then commits one registered add-sprite command. Scene2 command consumers supplying explicit sizes remain responsible for asset checks; no filesystem path is inferred from a GUID.

`SpriteGeometry.Corners` returns TL/TR/BR/BL in raster UV order after the supplied world transform, including reflected transforms. `DrawOrder` accepts sprite orders in effective document order and returns input indices ascending by order then ordinal (maximum 50,000); picking traverses backwards. It never sorts by depth or image identity. Parent transforms remain the caller's existing scene transform policy.

`SpriteAssetClosure.Capture` resolves prefab snapshots and captures each image once: maximum 1,024 images, 256 MiB RGBA total, and 64 MiB per complete saved BEDTEX artifact including its header. Missing, ambiguous, stale, non-image and unsupported-image entries refuse atomically. It retains producing inputs, exact source hashes, provenance and catalog tokens; `Revalidate` refuses changed/closed/foreign admissions while retained leases stay immutable. Existing export/licence policy must judge those records separately. Runtime scene-v2 reading and build mappings are subsequent work. Native Vulkan sprite presentation uses the borrowed-image seam below.

### Prefab source picker

| Type | Assembly and responsibility |
| --- | --- |
| `PrefabSourceInfo` | Editor: admitted source name, permanent GUID and space for native pickers, without paths/source bytes. |

`DocumentHost.ListPrefabSources(out sources, out why)` uses the same guarded registry as checked instantiate/update and returns a list ordered by ordinal admitted Name then Guid. Duplicate GUIDs, dirty/invalid definitions, unsafe reads and existing discovery count/byte limits refuse the complete list; no partial list is published. Reading the list never accepts an update or edits a level.

`PrefabSourceInfo.Slots` retains immutable existing `PrefabSlot` descriptors in definition order for labelled required/optional bindings. Its approved three-argument constructor remains; manually constructed descriptors have no slots. Only the host registry fills the read-only list.


### Borrowed sprite presentation

| Type | Assembly and responsibility |
| --- | --- |
| `ScenePresentation` | ImGui: UI-thread/context binding and per-frame live view collection for host-owned 3D and sprite images. |
| `SpriteImage` | ImGui: immutable exact asset selection, nearest/linear filter, borrowed texture, current-resource predicate and optional status. |

`ScenePresentation.WithSprites(produce)` adds one sprite producer to the existing binding and returns that binding. It shares BeginFrame/CompleteFrame and view retention with 3D presentation. `TrySpriteImage(viewId, catalog, selection, filter, out texture, out why)` checks the live thread/context, exact selection and filter, declared decoded image, nonzero texture, resource predicate and catalog content/policy tokens again before the caller records AddImageQuad. A producer supplies presentation only; it grants no admission or distribution rights. Dispose before host renderer shutdown. No binding means a named bounds placeholder.

The Vulkan host accepts admitted PNG/RGBA8 images within existing BEDTEX/device limits. Raster UVs follow SpriteGeometry.Corners, including reflected transforms, and the existing ImGui shader/blend handles the unchanged RGBA/alpha bytes. Nearest and linear use separate sampler variants and fence-safe cached resources. Sprite variants and scene material images share the 256 MiB image / 64 MiB in-flight staging ledger; each sprite view shares the 1,024 descriptor limit across both filters and retained replacements. The ledger measures payload bytes, excluding driver allocation overhead. Headless protocol tests do not certify actual GPU colour output.


Native prefab-update review pages through all captured changes, 32 per page. Values longer than 1,500 UTF-16 code units have Previous text / Next text pages rather than being truncated. Paging changes presentation only; accepting still commits the original checked candidate once, and source/revision guards remain unchanged.

A project can opt into v2 generation with root `sceneBuild` settings version 1:

```json
{"sceneBuild":{"version":1,"sceneVersion":2,"source":"data/level.json","generated":".bed/generated/scenes/level.json","spaces":["3d","2d-xy"]}}
```

`Scene2BuildSettings.ReadProject` returns null when absent. Its source must be separately admitted by the data host. Generated files stay outside writable authoring admission under `.bed/generated/scenes/`. Settings parsing is strict and bounded to one MiB and depth 64.

An optional `PlayBuildSettings.Stamp` persists as `play.build.stamp`; resolution fills init-only `PlayLaunch.BuildStamp`. Existing constructors remain available. Without a stamp, Play retains its program-mtime checks. With a stamp, Play requires the program plus current declared input contents and output hashes, before launch and after building. The stamp grants no execution approval.

`ProjectBuildStamp.Fingerprint(root, inputs)` hashes sorted portable names and bytes for at most 256 declared globs, 100,000 scanned file/directory entries and 256 MiB of selected input bytes. Missing literal inputs, linked paths and escapes refuse. `Write(root, stamp, inputs, capturedHash, outputs)` rechecks that hash, hashes 1–1024 explicit outputs (separate 256 MiB budget), and atomically writes a stamp of at most one MiB last. The stamp cannot include itself. `Current(root, stamp, inputs, out why)` checks without writing or executing. Stamps have version 1, `inputsHash` and an `outputs` array of `path`/`sha256` pairs.

The sample game's `--compile-scene2 PROJECT` command validates the declared saved source and published asset generation, flattens accepted snapshots, then writes the generated scene, its game-owned runtime descriptor and stamp. Default inputs are the declared source, `.bed/project.json` and `.bed/cache/current.json`; overridden inputs must include all three explicitly. An installed `game/SceneStarter.exe` is needed for this generator. V1 projects keep the existing path. The sample runtime validates its game-owned descriptor, generated scene hash and exact published generation, decodes sprite/material textures and refuses unflattened authoring. Export retains the current generated scene/descriptor and admitted cache closure with producing-record rights checks; copied runtime-only games require no authoring definitions or original images. Export requires a current build stamp and rechecks saved inputs before admitting the smoke command.

The native flat view now has world-X/Y move handles, local-X/Y scale handles, a diagonal XY scale handle and a Z rotation ring, using the same checked transform planners and one undo batch on release. Personal camera preferences use a private bounded file under the host's supplied preference folder, isolated by hashed project data path and admitted level name; pan/zoom/frame changes save after gesture end and never enter scene content/history. A retained inactive/closed flat view refuses input. The existing AssetPlacement token route also accepts planar PNG sprite placement, copying admitted defaults once and rechecking scene/source authority on delivery at the cursor. The same token route accepts admitted BEDMESH1/2 models into scene-v2 3D levels through existing CaptureScene2Edit/CommitScene2Edit plans. Root placement keeps the captured view centre; an ordinary or instance-placement parent gets local zero. Internal prefab parents, image assets and non-model opaque bytes refuse. Model drag adds no public API or persisted payload.


Scene-v2 object Copy/Paste is scoped to one workspace and coordinate profile. Copy retains an immutable accepted snapshot and includes selected linked pieces in full plus descendants. Paste allocates fresh identities before recording `scene2.paste` v1, remaps internal references, slot bindings and explicit reference overrides, and retains destination-present external targets. Missing external targets refuse the complete paste; no link is silently removed and no source file is consulted. Copied roots move one metre along world X through the existing planner, in the same undo batch. This is separate from text copy/paste and supplies no operating-system clipboard format.

Physics descriptions extend scene version 2. Existing constructors remain unchanged: optional init-only `Scene2.Physics`, `Scene2Object.Body` and `PrefabNode.Body` carry the values. Root `physics` has exactly `gravity` and `fixedStepSeconds`; any resolved body requires an explicit world. Omission creates no collider or simulation. XY gravity has zero Z. A body has `motion`, `shape`, `friction`, and `massKg` only for dynamic motion. Shapes use `box`, `sphere`, `capsule-y` or `mesh-bounds`; unknown fields refuse. Primitive centers default to zero. Mesh bounds require a model asset and no sprite; actual kind, bounds, rights and capture checks belong to the build integration.

There are at most 4096 effective bodies. A body cannot have another body as an ancestor. Its initial world pose must have finite position, unit rotation and positive uniform scale, without shear/reflection; signed local body scale refuses. Validation shares the scene-v2 transform planner's numeric tolerance. XY's existing Z-scale rule also applies, so physical world scale must satisfy all three uniform axes. Source dimensions remain local; the game applies world scale once and preserves total mass.

Body content participates in accepted definition hashes. Overrides allow atomic `/body` set/unset, `/body/motion`, `/body/shape`, `/body/massKg` (also unset), and `/body/friction`; whole-body/child overlap refuses. Child overrides require an inherited body. All fields are applied as one candidate before final motion/mass validation. Revert uses the accepted snapshot. Compatible/structural edits, clipboard paste and Flatten preserve bodies and destination world settings. Parsing/Flatten/Read2 only carry descriptions; no native world or simulation is inferred. The pure sample refuses authored physics; the separate physics sample requires the explicit capability described below.


## Saved physics runtime capability

`Scene2BuildSettings.SupportsPhysics` is an optional init-only bool, false by default; its existing three-argument constructor is unchanged. Root sceneBuild v1 accepts optional boolean `physics`; absent/false preserves the previous behavior and the writer omits false. This declares a project's supported game profile, never starts simulation or grants execution approval.

The separate [physics sample](../../samples/PhysicsStarter/README.md) supplies the game-owned Jolt integration and exact Windows runtime inventory. Its adapter default selects that executable only for physics-enabled projects. The compiler fits mesh-bounds against one published ProjectAssets snapshot and checks its generation and declared input contents before stamping output. Generated primitives retain local dimensions, saved mass/friction/world settings and scene IDs.

Only physics output uses game-owned scene2-runtime.v2 with physicsProfile `jolt-2026.10.2-v1`; the body-free v1 descriptor remains exact. The physics reader requires that profile, explicit world settings and prepared primitives. Copied games retain the profile without authoring project settings. The pure sample continues refusing physics. Framework Read2/Flatten carry descriptions and never instantiate a native world. No BRUTAL dependency or shared world API is added to core, Editor or framework Runtime.
