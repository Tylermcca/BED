# BED guides

New to BED? Begin with **[Start here](start_here.md)**: ten minutes, no programming. Then try everyday editing, levels, Play and Export. If something goes wrong, see [Reporting a problem](reporting_a_problem.md). The later guides cover automation and building an editor for your own data.

## Open and edit

1. [Start here](start_here.md) - Open BED, change data, move an object in a level, play it and save, in about ten minutes.
2. [Edit project data](data_workflow.md) - Use tables and forms, follow references, preview bulk edits and replace values.
3. [Build a data editor](editing_data.md) - Connect schemas, tables and forms to your own document format.
4. [Your first settings change](first_task.md) - Change a setting, see a mistake caught, run the game's check, then save and undo.
5. [Edit a level](levels.md) - Add models, select objects, change transforms and work with groups.
6. [Prefabs](prefabs.md) - Place a reusable piece, change one copy, review an update, and add physics bodies.
7. [Flat levels](flat_levels.md) - Place, size, move and order sprites in a flat XY level.
8. [Assets](assets.md) - Import models and textures, read thumbnails and part details, and resolve asset problems.
9. [Keyboard guide](keyboard.md) - Find shortcuts, navigate controls and change your local bindings.
10. [Accessibility](accessibility.md) - Use keyboard controls, assistive tools, larger text and high contrast.
11. [Use a screen reader](using_a_screen_reader.md) - Use the documented command-line route with a screen reader.
12. [Change many rows](bulk_edit_recipe.md) - Preview a bulk field edit, check the result and keep the changes you want.

## Run and share

13. [Play](playing.md) - Configure a program, run saved files, approve a build and stop the run.
14. [Follow a Play problem](play_diagnostics.md) - Open a reported saved field and emit a diagnostic from a game.
15. [Export a game build](exporting.md) - Build and check saved files, copy a verified folder and run it outside BED.
16. [Troubleshoot Play and Export](troubleshooting.md) - Match a message from BED to the next step to take.

## Work with assets and tools

17. [Command line](command_line.md) - Open projects, draw screenshots, probe Play and Export, and use the data CLI.
18. [Use BED with an AI assistant](using_bed_with_ai.md) - Connect an assistant and use the same checked read and edit tools.
19. [Read a recorded screen](ui_snapshot.md) - Inspect a headless UI snapshot and understand its labels, state and actions.
20. [Content rights](content_rights.md) - Declare asset origins and understand the checks and their limits.
21. [Check a model before importing](importing_assets.md) - Inspect geometry, materials and diagnostics without rewriting source files.
22. [Preview sound](sound_preview.md) - Inspect admitted WAV and Ogg audio and control playback.

## Build your own editor

23. [Read saved levels in a game](runtime.md) - Load saved scenes and prepared mesh/texture assets without the editor.
24. [Add Pause and Step](pause_step.md) - Gate individual game ticks while continuing to draw and discarding paused time.
25. [The library at a glance](api_overview.md) - Choose the document, command, session and view pieces you need.
26. [Your first document type](your_first_document_type.md) - Build a custom format with validation, undo and replay.
27. [Write an adapter](writing_an_adapter.md) - Connect a project's data, checks, panels and default Play settings.
28. [Developer walkthrough: a linked editor](developer_walkthrough.md) - Connect multiple JSON files, references, map movement and a save check.
29. [Add a planar map](adding_a_map.md) - Bind movable markers and lines to your document and produce a headless image.
30. [Extend the editor](extending_the_editor.md) - Add fields, tools and grouped extensions to an existing editor.
31. [Write a plug-in](writing_a_plugin.md) - Package editor features and connect them through the plug-in host.
32. [Make a small game](make_a_game.md) - Follow a data-driven board game from editable rules to a play session.

## Check contracts and compatibility

33. [Check threads and lifetime](check_threads.md) - Run checks on detached snapshots while keeping project entry points on their owning thread.
34. [Check run IDs](check_run_ids.md) - Page one completed check result within a session and handle stale or evicted IDs.
35. [API support policy](api_support_policy.md) - Read the proposed compatibility rules and distinguish API, package and document versions.
36. [Older build-guide link](building.md) - Follow the retained build-guide bookmark to the current Export guide.
37. [Reporting a problem](reporting_a_problem.md) - See exactly what a report contains, then copy it, save it or open an issue page yourself.
