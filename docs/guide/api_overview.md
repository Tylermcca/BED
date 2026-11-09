# The library at a glance

Use this page to choose the library pieces for your editor. Start with a document, commands and a session, then add the views or tools you need. To build your own document type step by step, see `your_first_document_type.md`. The exact promises are in `docs/spec/first_slice.md`.

The libraries are BED's building blocks. Their namespaces still use the earlier working name, Brutal.Authoring.

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
| Build an editor: selection, workspaces, palette, focus | `EditorModel<TDoc>` with your `IDocumentOutline<TDoc>` (in `Brutal.Authoring.Editor`) | `EditorModel.cs` |
| Place objects in a level, room or layout without writing a document type | `Scene`, `SceneObject`, `SceneFormat`, `SceneValidator`, `SceneKit.Catalog()` (add, move, rotate, scale, parent, asset, property, reference, remove, duplicate) | `Scene.cs` |
| Edit scenes from a script, build step or AI helper | the `scene` tool (`samples/SceneTool`): `scene do room.json scene.move '{"id":"lamp","position":[1,0,2]}'`, plus `tree`, `check --assets`, `apply`, `replay`, `commands`; `--kind` for a game's own scene kind | `Program.cs` |
| Edit a scene in the editor | `SceneEditing.Outline`, `SceneEditing.ObjectType()`, `SceneEditing.CommandFor` (in `Brutal.Authoring.Editor`) | `SceneEditing.cs` |
| Plug your own game into the general app: its rules, checks and panels, with no code in the tools | an adapter (`IProjectAdapter`): [Writing an adapter](writing_an_adapter.md) |
| Add a plug-in: commands, object types, workspaces and shortcuts, checked all or nothing | `PluginManifest` (`plugin.json`), `IEditorPlugin<TDoc>`, `PluginHost<TDoc>`; the sample is `samples/LightsPlugin` | `Plugins.cs` (core and editor) |
| Edit properties of the selection | `Inspector<TDoc>` with your `ObjectType<TDoc, TObj>` | `Inspector.cs` |
| Draw the editor window | `EditorWindow<TDoc>` (in `Brutal.Authoring.Editor.ImGui`) | `EditorWindow.cs` |
| Import models | `GltfImporter`, `ObjImporter`, `MarkerFile` (in `Brutal.Authoring.Import`) | `GltfImporter.cs`, `ObjImporter.cs` |

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

## Beyond this page

This page maps the core library. These came later and have their own guides:

- the 3D viewport and picking (`Viewport.cs`, `Picking`), with window controls in [Keyboard](editing_data.md#keyboard);
- the planar marker/edge map: [Adding a map](adding_a_map.md);
- the real window (`EditorApp`, `ProjectLauncher`): `start_here.md`;
- game adapters, loaded from their DLLs in `adapters/` or named with `--adapter`: `writing_an_adapter.md`; and plug-in panels: `writing_a_plugin.md`;
- the AI command line and its MCP server (`McpServer`): `using_a_screen_reader.md`.

What's still to come is listed in `docs/goals.md` and `docs/plan/readiness.md`.
