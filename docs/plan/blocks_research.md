# Extending rule blocks across BED

Research proposal · 2026-11-17 · no implementation or new dependency

## Recommended direction

**MIXED:** use BED's existing block panel, document commands, validation and history as the common authoring surface. Providers supply block vocabulary, types, meaning, checks and runtime bindings. Prove this with a data-validation provider and one scene interaction before moving a large amount of code into a common package.

A shared authoring/checking framework can support several domain runtimes. A block editor library supplies editing; it does not supply every game's rules. This follows BED's [goals](../goals.md) and [editor/runtime separation](../research/editor_architecture.md).

Current development code already offers rows, sentences, slots, palette groups, editing, Check and Play through a provider. The native panel has keyboard controls, drag/drop and document undo. Reuse these. Current row IDs are opaque locations valid for the current file state; future tools must pair them with a revision. The existing Check result tracks text freshness, but does not carry a persisted share stamp through the provider result. Cancellation must also reach a runner, not merely its initial entry point. Providers should declare Check/simulate/Play/export capabilities; a validation document should not be forced into game-specific captions or a Play flow.

## Useful applications

**MIXED design; examples are INVENTED.**

| Area | Example | What stays with the provider |
|---|---|---|
| Turns, scoring and victory | Add the captured tokens after a turn; finish at the target. | Legal actions, scoring, randomness and terminal state. |
| Scene/3D events | An entering actor opens this linked gate once. | Physics, object identity, scheduling and units. |
| AI behaviour | Patrol waypoints, then enter Alert on a sighting. | Sensing, navigation, agent state and action cancellation. |
| Dialogue/quests | Offer a line after an objective; deliver a reward only once. | Narrative runtime, persistent flags, saves, localisation and typed game commands. |
| UI flows | Continue opens the selected saved-game screen. | Event/state/focus bindings; Gum keeps layout and animation. |
| Validation | Every spawn references a scene and has a nonnegative count. | Pure predicates over existing schemas/data/reference maps. |
| Tests by blocks | Start fresh, press a switch, advance under a limit, expect the gate open. | A harness drives real actions and observes actual state. |
| AI authoring | Preview adding a timer and run its scenario. | Typed, revision-checked edits, rights, checks and explicit save. |

Validation and one scene event are recommended first. They test substantially different meanings using a small surface. A provider whose checks cannot simulate must say so; a readable block is not evidence that its game is correct or balanced.

## What to take from other tools

**BORROWED observations; MIXED application to BED.**

| Source | Lesson |
|---|---|
| [Blockly connection checks](https://docs.blockly.com/guides/create-custom-blocks/inputs/connection-checks/) and [serialization](https://docs.blockly.com/guides/configure/serialization/) | Reuse editing/serialization, but independently validate types and meaning; compatibility labels are not runtime proofs. |
| [GDevelop events](https://wiki.gdevelop.io/gdevelop5/events/) and [experimental gameplay tests](https://wiki.gdevelop.io/gdevelop5/interface/gameplay-tests/) | Understandable event sentences and bounded scenarios; object scope and per-frame repetition must be clear. |
| [Construct events](https://www.construct.net/en/make-games/manuals/construct-3/project-primitives/events/how-events-work) | Selection and ordering affect meaning. Prefer explicit event actors/targets in the first scene dialect. |
| [Unreal Blueprints](https://dev.epicgames.com/documentation/en-us/unreal-engine/blueprint-best-practices-in-unreal-engine) | Useful high-level events and reusable functions; keep heavy per-tick work in the runtime. |
| [Godot's removal account](https://godotengine.org/article/godot-4-will-discontinue-visual-scripting/) | High-level components, real users and maintainable examples matter more than a graph of low-level API calls. |
| [Unity graph types](https://docs.unity.cn/Packages/com.unity.visualscripting%401.9/manual/vs-graph-types.html) | State/transition views and action/value views serve different tasks. |
| [Ludii](https://ludii.games/index.php) / [GGP](https://ijcai.org/proceedings/2011/189) | Explicit state, legal choices, transitions and outcomes help reasoning; they do not establish a universal real-time engine. |
| [PuzzleScript execution](https://www.puzzlescript.net/Documentation/executionorder.html) | Small domain languages can be powerful; expose evaluation order and bounded loops. |
| [Machinations](https://machinations.io/docs/framework-basics) | Resource forecasts need units and clear separation from actual gameplay simulation. |
| [Twine format](https://github.com/iftechfoundation/twine-specs/blob/master/twine-2-htmloutput-spec.md) / [Yarn variables](https://www.yarnspinner.dev/docs/yarn/02-fundamentals/05-logic-and-variables/) | Keep narrative dialect identity and typed state. Use an existing dialogue compiler rather than a new prose language. |

## Shared model, provider meaning

**MIXED proposal, not an approved schema.** The common description needs namespaced dialect/operation IDs, format and semantic versions, ordered statements/child regions, typed values/references, defaults, diagnostics and runtime capabilities. State transitions may use explicit references; arbitrary cyclic wiring is a later need.

Providers define object/asset/state references, event scope, units, expressions, scheduling and effect handlers. A point count is not a duration; “this object” must be inspectable. Pure queries and gameplay changes are distinct from privileged operations. Shared data cannot introduce new executable handlers.

~~~mermaid
flowchart LR
 U[Panel / CLI / optional canvas] --> C[Checked commands and history]
 C --> D[Block document]
 P[Provider descriptions and meaning] --> C
 D --> K[Snapshot checks and scenarios]
 P --> K
 D --> E[Provider export]
 E --> R[Game data and runtime]
 G[Gum layout and animation] --> R
~~~

Reuse the current native panel and common commands for mouse, keyboard and tools. Add task search/contextual insertion as catalogues grow. Preserve readable nesting, field names, refusal reasons, diagnostic navigation and focus after edits. Colour alone conveys no meaning. Forms, tables, blocks and text views share authoritative documents, rather than competing undo systems.

A later Blockly canvas must use the real library and the same checked command bridge. Its [current keyboard/reader navigation](https://docs.blockly.com/guides/configure/keyboard-nav/) needs a coherent pinned release; an obsolete keyboard plugin is not a substitute. [Custom fields](https://docs.blockly.com/guides/create-custom-blocks/fields/customizing-fields/creating/) still need accessible values/editors. Native/browser focus, input, accessibility and offline packaging require a trial. A browser editor does not become a dependency of every shipped game.

## Check, sharing and AI

**MIXED:** distinguish structural/type/reference checks, provider compilation/link checks and optional bounded simulation. Report pass, fail, error, cancelled, timeout or unsupported, with profile/coverage and a useful trace. Do not convert a cancelled or timed-out run to a passing stamp. Thread scheduling alone does not isolate untrusted executable providers.

Use document revision for edits and a versioned semantic fingerprint for check/export freshness. Define canonical ordering, editor-only fields, library dependencies and provider/runtime identity before generalising stamps. A Check stamp records evidence; it is not an author signature or a complete proof.

Local packs should declare typed inputs/outputs and pinned dependencies. Preview library updates, preserve linked/own-copy distinctions, invalidate affected results and refuse unsupported versions non-destructively. Import validates paths, sizes, rights and capabilities and never installs DLLs/setup scripts. An online gallery and signature service are separate choices.

**INVENTED tool-operation sketch:** describe, preview edit, apply atomic batch, check/simulate, then explicit save/export. Reuse BED's [existing CLI/MCP workflow](../guide/using_bed_with_ai.md). A stale revision refuses; UI and tools use the same model validation. Comments and labels remain data, not instructions granting extra authority. Filter private observations/traces as well as writes. Test blocks drive real actions before asserting outcomes; setup must not fabricate the state being tested.

## Reuse and licence findings

No packages were installed. Root licence metadata is not an audit of a resolved dependency closure.

| Candidate | Verified finding and decision |
|---|---|
| Blockly 13.4.0 | Apache-2.0; no direct dependencies, but jsdom is a peer disabled in the browser map. Browser-only and Node paths differ. Candidate for an optional canvas spike. [published metadata](https://registry.npmjs.org/blockly/13.4.0), [jsdom closure](https://registry.npmjs.org/jsdom/30.1.3) |
| Scratch Blocks / VM | Blocks is Apache-2.0 and depends on Blockly/plugins; VM is AGPL-3.0. Do not transfer one component's licence to another. [Blocks](https://raw.githubusercontent.com/scratchfoundation/scratch-blocks/develop/package.json), [VM](https://raw.githubusercontent.com/scratchfoundation/scratch-vm/develop/LICENSE) |
| Yarn Spinner | MIT dialogue candidate. Runtime depends on Protobuf; compiler also brings ANTLR, CsvHelper, JSON, file globbing and, in 3.3.0, Shared. Check dependency compatibility and package provenance before adoption. [runtime](https://api.nuget.org/v3-flatcontainer/yarnspinner/3.3.0/yarnspinner.nuspec), [compiler](https://api.nuget.org/v3-flatcontainer/yarnspinner.compiler/3.3.0/yarnspinner.compiler.nuspec) |
| Yarn dependency caveat | Shared 3.3.0.5 satisfies the declared minimum. Its metadata omits a licence/URL, but source at its declared upstream commit is MIT. Preserve notices and verify selected artifacts/versions in a trial; Gum's existing CsvHelper version also needs compatibility checking ([current dependency notes](../../THIRD-PARTY-NOTICES.md)). [Shared metadata](https://api.nuget.org/v3-flatcontainer/yarnspinner.shared/3.3.0.5/yarnspinner.shared.nuspec), [upstream licence at commit](https://raw.githubusercontent.com/YarnSpinnerTool/YarnSpinner/526caa5f2c9d558acbc3b847395816ed2712a0d3/LICENSE.md) |
| Stateless 5.20.1 | Apache-2.0, no dependencies in the published net10.0 group; optional state-machine provider candidate. [manifest](https://api.nuget.org/v3-flatcontainer/stateless/5.20.1/stateless.nuspec) |
| GDevelop / PuzzleScript | MIT upstream code; a whole-runtime adapter could be evaluated if desired. No complete build closure or embedding is selected here. [GDevelop](https://raw.githubusercontent.com/4ian/GDevelop/master/LICENSE.md), [PuzzleScript](https://raw.githubusercontent.com/increpare/PuzzleScript/master/LICENSE) |
| Ludii | Engine licence is CC BY-NC-ND 4.0, distinct from tutorials. Research reference; unsuitable for the intended modifiable/commercially usable shared runtime under that licence. [engine licence](https://raw.githubusercontent.com/Ludeme/Ludii/master/LICENSE) |
| Other tools | Twine editor is GPL-3.0; story formats differ (for example, [Chapbook is MIT](https://raw.githubusercontent.com/klembot/chapbook/develop/LICENSE)). Commercial engine/package/service terms need separate review for any integration. No such code/content is selected. [Twine](https://raw.githubusercontent.com/klembot/twinejs/develop/LICENSE), [Epic](https://www.unrealengine.com/eula/unreal), [Unity package](https://docs.unity3d.com/ja/Packages/com.unity.visualscripting%401.8/license/LICENSE.html), [Machinations](https://machinations.io/terms) |

Reuse existing BED primitives first. If an appropriate engine exists, adapt it rather than re-code it. Shared authoring utilities move into a generic home only after two providers demonstrate the need; domain operations remain outside the generic core. Games retain runtime-only dependencies.

## Proposed phases and owner decisions

**MIXED plan. Each item below is a separate 30–45 minute card**, not a promise to finish an entire phase in that time.

| Phase | Separate card-sized steps |
|---|---|
| 0: boundary | 30m contract/dependency decision; 30m baseline contract fixture. |
| 1: current gaps | 30m cancellation forwarding; 45m explicit stamp-result path with stale-result and undo checks. |
| 2: validation pilot | 45m pure validation provider using current interface; 45m typed preview/apply tool operation; 30–45m keyboard/reader task. |
| 3: scene pilot | 45m one real event/effect; 45m bounded headless scenario or narrow missing runner seam; 30m extraction review. |
| 4: demonstrated sharing | Four 45m cards: common descriptions, one leaf edit, tree edits, then per-document provider/catalogue routing, retaining current transaction validation. |
| 5: local packs | 45m library identities/versioning; 45m pack/stamp import and refusal cases. |
| 6: optional canvas | 45m actual Blockly spike; 30m native-versus-browser host decision. |
| 7: optional backends | 45m state-machine trial; 45m dialogue dependency/export trial; 45m typed dialogue binding. |
| 8: tests/export | 45m one block-authored real scenario; 30–45m runtime/package boundary check. |

The first gate is two non-card uses working without domain switches in BED. Then extract proven duplication. Packs, canvas, dialogue and test authoring are independent later priorities.

Owner choices: first author tasks; framework versus mandatory universal interpreter; native/canvas hosting; adapter formats versus a common envelope; local versus online sharing; optional runtime dependencies; compatibility/migration policy. Recommended defaults are two small pilots, native editing, provider-owned execution and formats, local pinned packs, and explicit non-destructive migration.

Risks to manage are domain leakage, building a graph before useful blocks, stale IDs/stamps, execution authority, browser accessibility, dependency conflicts, private observations and overstated simulation results. This proposal is based on source/document review; no integration, performance result or new schema is claimed.

## Round 2 check (2026-11-19)

Spot-checked against the live package registries and upstream licence files on 2026-11-19:

- Blockly 13.4.0 is the current release (published 2026-10-13), Apache-2.0: confirmed.
- The separate keyboard navigation plugin (`@blockly/keyboard-navigation` 3.0.5, and the 4.0.0 beta) still declares `blockly ^12.3.0`, so it does not pair with Blockly 13. This confirms round 1's caution: a canvas spike must use the keyboard/reader navigation built into a pinned Blockly release, or wait for a plugin that matches it.
- Stateless 5.20.1 is the newest stable release: confirmed.
- Yarn Spinner: 3.3.1 is now out (round 1 read 3.3.0). Its repository licence is MIT. Recheck the dependency list against 3.3.1 in any trial.
- Scratch VM is AGPL-3.0 and the Ludii engine is CC BY-NC-ND 4.0: confirmed. Both stay reference-only.

Two sources round 1 didn't use, both MIT:

- [Microsoft MakeCode](https://github.com/microsoft/pxt): blocks and text are two views of one program and convert both ways. This supports round 1's "one authoritative document, several views".
- [GB Studio](https://github.com/chrismaltby/gb-studio): scene and dialogue games built entirely from event lists attached to scenes, actors and triggers. This is the closest working example of the scene-event and dialogue uses. It shows that a small fixed set of events with clear targets can carry whole games, without a general graph.

Nothing in round 2 changes the recommendation: two small pilots (validation, then one scene event) on BED's existing block panel, with each game's provider owning meaning and running.
