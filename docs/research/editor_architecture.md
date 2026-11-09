# How Unity, Unreal, Godot, Blender and Stride build their editors, and what we take from each

Research of 2026-10-31. **Reviewed and corrected by Codex in A003** (`docs/plan/review_A003.md`): of 12 claims checked, 8 were confirmed, 3 were wrong (now fixed) and 1 is unverified (now qualified). The user asked: "is that the right way? what does unity godot and unreal do differently in this respect. i think you should go through each toolset and make sure we're doing the compilation correct, future thought", "maybe some research", "a list of goals". They also added ease of use ("it shouldnt be complicated but should allow for complex solutions when required") and expansion ("the ability to expand with tools and plugins").

- **Method.** Four web research passes, one per toolset (Blender and Stride shared one; Flax was noted briefly). Each claim was checked against official documentation where possible. Claims the researchers could not confirm are marked UNVERIFIED. This page keeps one or two sources per point; the full reports are in the session record.
- **Labels.** BORROWED (from which), MIXED or INVENTED, per the house rule.
- **Goals.** The list these findings feed is `docs/goals.md`.

## 1. Compilation: keeping editor code out of the game

| Toolset | How editor code is kept out of shipped games |
|---|---|
| Unity | Editor and runtime code compile into separate assemblies (`.asmdef`; a package has `Editor/` and `Runtime/` folders, each with its own asmdef), plus `#if UNITY_EDITOR`. Folder-name rules are implicit, and newcomers trip on them. [layout](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/cus-pkg-development/cus-layout) |
| Unreal | Every module declares a type (Runtime, Editor, UncookedOnly, Program and others) in its `.uplugin`. Editor modules never load in cooked games. "Developer" was deprecated "due to ambiguities". `WITH_EDITORONLY_DATA` strips data, but an unguarded reference still drags assets into the build. [EHostType](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Projects/EHostType__Type) |
| Godot | In development the editor and the game are one binary. Exported games use export templates built without `TOOLS_ENABLED`. C# relies on `#if TOOLS`, which breaks IntelliSense and exports when it's forgotten. [build system](https://docs.godotengine.org/en/stable/engine_details/development/compiling/introduction_to_the_buildsystem.html) |
| Stride (C#) | **Design-time asset types and runtime types are separate classes.** A registered asset compiler, with declared inputs and an incremental cache, turns one into the other. Games load only compiled runtime data. [custom assets](https://doc.stride3d.net/4.3/en/manual/scripts/custom-assets.html) |
| Flax (C#) | Editor plugins sit inside `#if FLAX_EDITOR` or in a separate editor-only scripts module. An editor plugin links to a game plugin only **when both are used** (corrected, A003). [plugins](https://docs.flaxengine.com/manual/scripting/plugins/index.html) |

**What we do (MIXED: Unity's assembly split, Unreal's explicit module types, Stride's design/runtime split):**

1. **Separate projects, no `#if`.** Editor code lives in its own projects, and a game never references them. This is the C# version of Unity's asmdef split, and it avoids Godot's and Flax's `#if` pitfalls.
2. **Every project is named for its role,** so nothing is ambiguous (Unreal's "Developer" lesson):
   - `*.Runtime`: the only thing a game ships;
   - `*.Authoring` (core, no UI);
   - `*.Authoring.Editor` (ImGui);
   - `*.Authoring.Viewport` (GPU);
   - `*.Authoring.Remote` (optional);
   - game adapters, outside the library.
3. **A small runtime package (new, from Stride).** It holds the data formats and loaders a game needs to read what the editor saved. The game references only this. The editor writes authoring files, and the game reads runtime files (or the same files through the runtime loader).
4. **A check, not a promise.** A test reads each runtime build's dependency list and fails on any `Authoring` assembly (worksheet AT-034).

## 2. Commands and undo

| Toolset | Model | Known problem |
|---|---|---|
| Blender | **Every action is an operator**, with a string id, typed properties, `poll` / `execute` / `invoke`, and flags (REGISTER = logged and redo-able, UNDO, UNDO_GROUPED, INTERNAL = hidden from search). Ctrl-C on any button copies its Python call. [Operator](https://docs.blender.org/api/current/bpy.types.Operator.html), [flags](https://docs.blender.org/api/current/bpy_types_enum_items/operator_type_flag_items.html) | Whole-file "memfile" undo is heavy |
| Unity | `Undo.RecordObject` snapshots serialized state. `SerializedProperty` is one generic path-addressed edit that brings undo, multi-object editing and prefab-override styling for free. There is no public command API. [Undo](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/undo), [SerializedObject](https://docs.unity.com/en-us/engine/current/script-reference/unityeditor/serializedobject) | Undo is opt-in: forget `RecordObject` and the edit can't be undone. The AI assistant fell back to coarse checkpoints that also revert the user's own work |
| Unreal | `FScopedTransaction` plus `Modify()` snapshot object state. UI commands are a registry (`TCommands`, `FUICommandList.MapAction`) with execute / can-execute / is-checked. [FScopedTransaction](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Editor/UnrealEd/FScopedTransaction) | Remote and Python edits are undoable only if they ask for a transaction. The undo buffer can't be persisted |
| Godot | `EditorUndoRedoManager`: an action is built from do and undo steps. There is one history per scene plus a global one, and the history is guessed from the first object touched. [EditorUndoRedoManager](https://docs.godotengine.org/en/stable/classes/class_editorundoredomanager.html) | Tool scripts bypass undo silently. Wrong guesses forced `custom_context` |
| Stride | A nested, disposable transaction stack with bounded capacity, `IMergeableOperation`, and dirty tracking kept separate. [ITransactionStack](https://github.com/stride3d/stride/blob/master/sources/core/Stride.Core.Design/Transactions/ITransactionStack.cs) | Undo for derived assets relies on a later "reconcile" step, which its own docs call "not an ideal design" |

**Was the command layer the right way? Yes, and the research sharpens it.** A recurring failure is an edit that goes around undo. It is documented in Godot (tool-script changes have no undo) and in Unity and Unreal (undo needs an explicit opt-in). A003 struck an earlier "every engine's worst bug" claim as unsupported. So the rule is that **no code can change a document except through a command**. Documents are immutable, and the only mutator is the command runner. One of our own games' galaxy editors already works this way (it records through one validated `Apply`).

What the command layer adds, and where each piece comes from:

| Piece | Source |
|---|---|
| A string id, typed arguments, a can-run check (`poll`), and run | BORROWED from Blender operators and Unreal's TCommands |
| Flags: *logged* (in the edit log, can be copied as a script), *undoable*, *merge repeats* (one drag is one step), *hidden from search* | BORROWED from Blender's bl_options, Stride's IMergeableOperation and Unity's gesture grouping |
| **One generic "set property at path" command**, so most edits need no custom command at all; overrides and multi-edit hang off the same paths | BORROWED from Unity's SerializedProperty |
| One history per document plus a global history, each command naming its document explicitly (no guessing) | BORROWED from Godot, avoiding its guess |
| "Doesn't dirty the document" for selection-only commands | BORROWED from Godot's `mark_unsaved=false` |
| Undo by immutable snapshots with shared structure, as one of our games' galaxy editor does, and command-level inverse operations only where snapshots are too costly | MIXED: our own game editor plus Blender's stateful/differential steps |

## 3. Plug-ins and expansion

| Toolset | Manifest and distribution | Lesson |
|---|---|---|
| Unity | `package.json` (exact SemVer only), registries, attribute discovery through `TypeCache` (an index, not a reflection scan) | Two inspector UI systems (IMGUI and UI Toolkit) doubled the learning curve and broke compatibility |
| Unreal | `.uplugin` (modules, loading phase), Fab | Binary plug-ins can need rebuilding across engine versions. That's a compatibility risk; "every minor version" is UNVERIFIED as a universal rule (A003). Many extension paths are Beta |
| Godot | `plugin.cfg` in `addons/`, EditorPlugin, inspector plug-ins (`_can_handle` → `_parse_property`), GDExtension (`compatibility_minimum`) | Fake custom types were later replaced by real classes. Forward compatibility is a goal, not a guarantee |
| Blender | `blender_manifest.toml`: id, semver, `blender_version_min`, **SPDX licence**, **permissions with reasons** (e.g. network) | The store's GPL requirement doesn't suit a .NET library. Bundled binaries and wheels are allowed under conditions; corrected from "bans bundled binaries" (A003) |
| Stride | `StrideAssetsPlugin` and **documented custom asset editors** (a ViewModel plus View registered against an asset type, [custom editor guide](https://doc.stride3d.net/en/contributors/engine/architecture/editor/custom-editor.html)). Corrected from "no public plug-in API" (A003) | Extension points exist, but a stable third-party packaging story is less clear (its [extensibility RFC](https://github.com/stride3d/stride/issues/1120)). Document and version the extension interface from day one |

**What we do (MIXED):**
1. **Manifest.** Each plug-in has an id, a semver version, the minimum library API version, a licence (SPDX), and permissions with reasons (BORROWED from Blender extensions). Plug-ins ship as NuGet packages with version ranges, not exact pins (avoiding Unity's pin-only rule). Plug-ins are built against a stable API, not a library build, which avoids Unreal's per-version rebuilds.
2. **Three rungs of expansion,** each a small step up from the last (BORROWED from Unity's inspector ladder, Godot's EditorScript to EditorPlugin, and Unreal's Editor Utility Widgets):
   - (a) **No code:** mark up a game's data type, and it gets an inspector, validation and undo for free;
   - (b) **A small tool:** one class that adds a command or a panel;
   - (c) **A full plug-in:** a manifest, importers, object types, overlays and more.
3. **Discovery by explicit registration,** or a source generator. No runtime reflection scans (BORROWED from Unity's TypeCache lesson; it also suits trimmed builds).
4. **One UI stack: ImGui, as BRUTAL uses.** Never two (Unity's lesson).

## 4. Remote control, AI and LLM links

A common shape, mostly in community projects, is a bridge inside the editor listening on localhost, with a separate MCP server process talking to the AI client. (Official HTTP APIs such as Unreal's Remote Control are not MCP themselves; A003 corrected an earlier "all four converged".)
- Unity: its own MCP relay (now deprecated) and the new Unity CLI, `unity mcp`. [Unity MCP](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.19/manual/integration/unity-mcp-overview.html), [Unity CLI](https://discussions.unity.com/t/announcing-the-unity-cli-a-new-way-to-connect-your-tools-and-agents/1731104)
- Unreal: the Remote Control API (HTTP on port 30010) and Python remote execution. [Remote Control API](https://dev.epicgames.com/documentation/en-us/unreal-engine/remote-control-api-http-reference-for-unreal-engine)
- Godot: community WebSocket bridges.
- Blender: a socket add-on, plus Blender Lab's official MCP server. [Blender Lab MCP](https://www.blender.org/lab/mcp-server/), [blender-mcp](https://github.com/ahujasid/blender-mcp)

The recurring problems:
- **Arbitrary code execution as the main tool.** Blender's official server "will execute LLM generated code … without any guards", and Unity's CLI has `eval`.
- **No authentication.** godot-ai's bridge is "loopback-only since it's unauthenticated", and blender-mcp's socket "has no authentication or encryption".
- **Edits that skip undo.** Unreal's are undoable only when the server asks for a transaction, and Unity's AI used checkpoints.
- **Hiding a tool treated as access control.**

**What we do (MIXED; it corrects FUT-003 and FUT-004):**
1. **Typed tools only.** The server exposes the **command catalogue** plus read-only queries. There is no "run any code" tool. If one is ever added, it is a separate, off-by-default, clearly warned option.
2. **Off by default, localhost only, a token, and the user approves each client once.** BORROWED from Unity's MCP approval and Unreal's passphrase for clients off the machine.
3. **Every remote or AI edit is an ordinary command.** It is validated, undoable one step at a time, and shown in the edit log. Coarse checkpoints are never used.
4. **The plug-in permission model covers AI pieces too.** For example, an LLM link declares `network`, with a reason.
5. **LLM links stay provider-neutral and proposal-only.** An assistant proposes commands, and the user applies them. Assets whose AI-use flag forbids it are never sent anywhere (FUT-005).

## 5. Assets: identity and import

| Toolset | Identity | Lesson |
|---|---|---|
| Unity | A GUID in a `.meta` sidecar beside each asset, holding the import settings too. Imported results go to `Library/`. Scripted importers are versioned and must be deterministic | Move a file without its `.meta` and references break |
| Godot | `uid://` ids exist in Godot 4; 4.4 added `.uid` sidecars for files that can't hold one; `.import` settings files; a rebuildable `.godot/imported` cache | The 4.4 sidecar change advised one re-save, so the id churn lands in a single diff (A003 corrected "every project needed a mass re-save"). Lesson: settle asset identity before projects depend on paths |
| Unreal | **Package path**, not GUID. A move leaves a redirector that a fix-up pass later removes | Path identity is fragile and needs a whole clean-up machine |
| Stride | A GUID in each YAML asset, plus ids on collection items so reordering doesn't break inheritance | |

**What we do (BORROWED from Unity and Godot, avoiding Unreal):**
- **Every asset gets a GUID from day one,** in a small sidecar beside it. The sidecar also holds its import settings, provenance and AI-use flag (AT-024, FUT-005). The path is kept as a fallback.
- **The import cache is rebuildable and git-ignored,** keyed by a content hash.
- **Importers are versioned and deterministic,** and they declare their inputs.

This changes worksheet AT-012. Readable ids stay for objects inside a scene, where people read them (ease of use), but assets are identified by GUID.

## 6. Scene files

- **Unity:** YAML with file-local ids. Prefab overrides are stored as `{target, propertyPath, value}`, and merging needs a special tool (UnityYAMLMerge). [prefab serialization](https://docs.unity3d.com/Manual/yaml-prefab-serialization.html)
- **Godot:** text `.tscn` with a format version in the header, values equal to the default omitted, and instances storing only what they change. Godot calls it "mostly readable and mergeable". [tscn](https://docs.godotengine.org/en/stable/engine_details/file_formats/tscn.html)
- **Unreal:** binary, so merges hurt. It answered with One File Per Actor rather than text. [OFPA](https://dev.epicgames.com/documentation/en-us/unreal-engine/one-file-per-actor-in-unreal-engine)
- **Stride:** YAML, an override marker (`*`) per overridden key, and explicit delete markers. [asset introspection](https://doc.stride3d.net/latest/en/manual/engine/asset-introspection.html)
- **Blender:** binary, versioned, and patched forward on load (`do_versions`).

**What we do (MIXED):** this confirms worksheet AT-013 and AT-014, with additions.
- Text JSON with a `kind` and `version` header, defaults omitted, a deterministic order and canonical numbers. Plain git merges should work without a special tool.
- Overrides stored as `{object id, property path, value}`, using the same paths as the generic property command (Unity), with explicit delete markers (Stride).
- Migration steps on load, in the spirit of `do_versions`.
- One file per entity is left for later, if scenes grow (Unreal's OFPA).

## 7. Ease of use: simple by default, power when asked

- **Unity:** a plain field shows in the inspector with no code. More control comes by climbing a ladder of attributes, then drawers, then custom editors, then windows. The two UI stacks and boilerplate-heavy inspectors are the pain.
- **Godot:** one mental model (scenes are trees of nodes), extract-and-run, and one line (`@tool`) to run code in the editor. The pain is undo that tools must opt into, and C# as a second-class citizen. [design philosophy](https://docs.godotengine.org/en/stable/getting_started/introduction/godot_design_philosophy.html)
- **Unreal:** the Details panel works with no setup, and Blueprints sit over C++ as layered power. But it has the "steepest" learning curve, and plug-in setup takes a lot of ceremony.
- **Blender 2.80:** left-click select, toolbars and workspaces for newcomers. **F3 search reaches every operator** for experts. All of it sits on the same operators, so nothing was replaced. [2.80](https://www.blender.org/download/releases/2-80/)
- **Dwarf Fortress on Steam (2022):** a new interface, menus, mouse support, tiles and a tutorial, with the simulation untouched and the classic version kept free. [devlog](https://kitfoxgames.itch.io/dwarf-fortress/devlog/460421/dwarf-fortress-is-out-now)

**What we do (MIXED):**
- **The surface is simple, and the depth lives in commands.**
- **A command palette** (an F3-style search over the command catalogue), so every action is findable without menus to memorise (BORROWED from Blender).
- **Sensible defaults with no configuration,** one way to do each common thing, and advanced panels and options out of sight until asked for.
- **The extension ladder in section 3,** so going further is always one small step.

## 8. What changed in our plan

| Before the research | After |
|---|---|
| Command layer (FUT-001) as a seam | Confirmed and made **mandatory**: the only way to edit, with Blender-style ids, `poll`, flags, a generic property-path command, merged drags, one history per document, and no guessing |
| Plug-ins (FUT-002) with a manifest | Plus SPDX licence and permissions with reasons, NuGet version ranges, a three-rung expansion ladder, and registration with no reflection scan |
| Remote/AI server (FUT-003) | Typed commands only, no `eval`; off by default, localhost, token, per-client approval; never checkpoints |
| Readable ids for everything (AT-012) | GUIDs for assets in a sidecar from day one; readable ids only for scene objects |
| Library = editor pieces | **Plus a small runtime package,** the only thing games reference (from Stride's design/runtime split) |
| No discoverability plan | A command palette over the catalogue; defaults with no configuration |

The new and changed rows are in `docs/plan/worksheet_r2_future.md`. The goals are in `docs/goals.md`.
