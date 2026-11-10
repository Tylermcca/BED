# BED guides

Start with everyday editing, then try levels, Play and Export. The later guides cover automation and building an editor for your own data.

## Open and edit

1. [Edit project data](data_workflow.md) - Use tables and forms, follow references, preview bulk edits and replace values.
2. [Build a data editor](editing_data.md) - Connect schemas, tables and forms to your own document format.
3. [Your first settings change](first_task.md) - Follow a short settings-editing walkthrough and its stated release-check limits.
4. [Edit a level](levels.md) - Add models, select objects, change transforms and work with groups.
5. [Assets](assets.md) - Import models and textures, read thumbnails and part details, and resolve asset problems.
6. [Keyboard guide](keyboard.md) - Find shortcuts, navigate controls and change your local bindings.
7. [Use a screen reader](using_a_screen_reader.md) - Use the documented command-line route with a screen reader.
8. [Change many rows](bulk_edit_recipe.md) - Preview a bulk field edit, check the result and keep the changes you want.

## Run and share

9. [Play](playing.md) - Configure a program, run saved files, approve a build and stop the run.
10. [Export a game build](exporting.md) - Build and check saved files, copy a verified folder and run it outside BED.
11. [Troubleshoot Play and Export](troubleshooting.md) - Match a message from BED to the next step to take.

## Work with assets and tools

12. [Command line](command_line.md) - Open projects, draw screenshots, probe Play and Export, and use the data CLI.
13. [Use BED with an AI assistant](using_bed_with_ai.md) - Connect an assistant and use the same checked read and edit tools.
14. [Read a recorded screen](ui_snapshot.md) - Inspect a headless UI snapshot and understand its labels, state and actions.
15. [Content rights](content_rights.md) - Declare asset origins and understand the checks and their limits.
16. [Check a model before importing](importing_assets.md) - Inspect geometry, materials and diagnostics without rewriting source files.

## Build your own editor

17. [Read saved levels in a game](runtime.md) - Load saved scenes and prepared mesh/texture assets without the editor.
18. [The library at a glance](api_overview.md) - Choose the document, command, session and view pieces you need.
19. [Your first document type](your_first_document_type.md) - Build a custom format with validation, undo and replay.
20. [Write an adapter](writing_an_adapter.md) - Connect a project's data, checks, panels and default Play settings.
21. [A linked editor from the start](start_here.md) - Connect multiple JSON files, references, map movement and a save check.
22. [Add a planar map](adding_a_map.md) - Bind movable markers and lines to your document and produce a headless image.
23. [Extend the editor](extending_the_editor.md) - Add fields, tools and grouped extensions to an existing editor.
24. [Write a plug-in](writing_a_plugin.md) - Package editor features and connect them through the plug-in host.
25. [Make a small game](make_a_game.md) - Follow a data-driven board game from editable rules to a play session.

## Check contracts and compatibility

26. [Check threads and lifetime](check_threads.md) - Run checks on detached snapshots while keeping project entry points on their owning thread.
27. [Check run IDs](check_run_ids.md) - Page one completed check result within a session and handle stale or evicted IDs.
28. [API support policy](api_support_policy.md) - Read the proposed compatibility rules and distinguish API, package and document versions.
29. [Older build-guide link](building.md) - Follow the retained build-guide bookmark to the current Export guide.
