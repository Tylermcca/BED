# Keyboard guide

Press **F1** in an open file to show **Keys**, or choose **Help > Keyboard shortcuts**. Keys shows the bindings currently in effect, including changes made by a host and shortcuts added by a connection or plug-in. The command palette also shows each command's current shortcut and why a command is unavailable.

These are BED's built-in defaults. A shortcut uses the same save, validation and approval decisions as its menu command. While typing, ordinary editing keys belong to the text field; the Play function keys remain available. A command can be unavailable for the current selection, file or Play state.

## Commands in an open file

The action IDs in the first column identify the commands for a host that changes bindings.

| Action ID | Default | Action and scope |
| --- | --- | --- |
| `editor.undo` | `Ctrl+Z` | Undo the last document edit. |
| `editor.redo` | `Ctrl+Y` | Redo an undone document edit. |
| `editor.save` | `Ctrl+S` | Save the active document through its normal checks. |
| `editor.palette` | `Ctrl+Shift+P` | Find and run a command by name. |
| `editor.play.project` | `F5` | Play the project's saved files; resolve the save prompt first if there are edits. |
| `editor.stop` | `Shift+F5` | Stop the program started by this project. |
| `editor.build_play` | `Ctrl+F5` | Build and Play using the configured command and its normal approval prompt. |
| `editor.focus_out` | `Escape` | Step out of the focused outline branch; open dialogs and the palette use Escape first. |
| `editor.focus_in` | `F` | Focus the selected outline item. The focused 3D canvas also uses F to frame its selection. |
| `editor.panel.project.find_replace` | `Ctrl+H` | Show or hide Find and replace in a data workspace; preview and choose scalar value matches across files. |
| `editor.panel.bed.keys` | `F1` | Show or hide the Keys panel. |
| `editor.panel_do.editor.compare/next` | `Alt+F5` | Go to the next difference when a comparison panel is available. |
| `editor.panel_do.editor.compare/previous` | `Shift+Alt+F5` | Go to the previous difference when a comparison panel is available. |
| `scene.copy` | `Ctrl+C` | Copy selected scene objects to this project's in-app clipboard; text fields keep normal text copy. |
| `scene.paste` | `Ctrl+V` | Paste copied objects beside their copied poses, with new IDs and one undo step. Works between levels in the same project. |
| `scene.group` | `Ctrl+G` | Put selected objects under a new empty parent at their world-position centre; keep world poses and one undo step. |
| `scene.ungroup` | `Ctrl+Shift+G` | Remove the selected parent, preserve child world poses, and select its children; one undo step. |
| `editor.hide_objects` | `H` | Hide/show selected objects in this editor, including their children; local user preferences only. |
| `editor.lock_objects` | `L` | Lock/unlock selected objects and their children against transforms; local user preferences only. |
| `editor.rename_object` | `F2` | Rename the selected object ID in the Outliner; parent and named references follow. Enter applies; Escape cancels. Also available in Edit > Rename object. |
| `scene.remove` | `Delete` | Delete one selected object; review referring objects before choosing Delete anyway, or Cancel. Also available in Edit > Delete. |
| `scene.duplicate` | `Ctrl+D` | Duplicate the selected objects beside their originals, select the copies, and keep the edit in one undo step. Also available in Edit > Duplicate. |
| `editor.close` | `Ctrl+W` | Close the active scene document through its save decision; installed by the scene workspace. |
| `editor.panel_do.scene.assets/add` | `Insert` | Open the scene's Add object picker; placement requires an admitted asset. |

See [Play](playing.md) for save decisions, Build and Play, approval and Stop. The last two bindings are installed when a scene opens. Connections and plug-ins can add other commands: use F1 to see their current bindings.

## Search the Outliner

With a scene’s Outliner focused, press **Ctrl+F** to focus **Search objects**. Type an object ID, asset path or imported asset source name. Matching objects keep their parent rows for context. **Escape** clears the filter without changing selection or the document. This shortcut belongs to the Outliner; text fields in other panels keep their own shortcuts.

## Hide and lock scene objects

**H** toggles hidden objects and **L** toggles locked objects. Their Outliner rows show `[hidden]` and `[locked]`. A parent's choices also apply to its children; select that parent to show or unlock them. Hidden objects leave the viewport and its picking. Locked objects keep their transforms, including when an ancestor moves. These choices belong to your local preferences for each project and level; they do not edit the saved level or add an undo step. Undo and redo still use the document's existing history.

## Start screen

| Key | Action |
| --- | --- |
| Ctrl+O | Open the project-folder chooser on the start screen. |
| Ctrl+Shift+N | Open New project on the start screen. |
| F1 | Show or hide Getting started on the start screen. |
| Escape | Close the start-screen guide/preferences/about view, return from New/Open to Home, or clear the recent-project search. |

## Menus, panels and dialogs

Hold **Alt** to reveal menu access letters. Press Alt with the underlined letter, then use that menu's underlined choices. Letters are assigned from the visible labels within each menu; added commands can change a submenu's letters. Tab and the arrow keys remain available when a letter is absent. Menu access letters are separate from the rebindable command table.

Hold **Ctrl** and cycle **Tab** to choose a panel, then release Ctrl. **Tab** and **Shift+Tab** move between controls. **Enter** activates the focused button or opens a selected row; **Space** operates buttons and checkboxes. A scrolling child list may need Enter to enter it and Escape to return to its parent before Tab can continue. Follow the visible focus cue rather than counting Tab presses.

In the command palette, **Down** or **Tab** moves to the next runnable result; **Up** or **Shift+Tab** moves back. **Enter** runs it and **Escape** closes the palette. Typing changes the search.

In inspector fields, Enter or leaving the field commits valid text. Read a refusal and correct the field if the document rejects it. In Play settings, Tab reaches Save Play settings; Enter saves the declaration and Escape closes the form. In Export build, Escape cancels a running export or closes the idle dialog. After entering **Destination folder**, press **Tab** to reach **Copy build to folder...**, then Enter. Copy becomes available only after the build and unpacked check succeed.

## Tables and canvases

These controls are handled by the focused table or canvas and are fixed rather than command bindings:

- Table: Ctrl+C copies selected cells (not rebindable).
- Table: Up/Down/Left/Right moves the cell selection; Shift extends it (not rebindable).
- Canvas: Escape cancels a move (not rebindable).
- Canvas: 0 or keypad 0 resets zoom and pan (not rebindable).

The last item applies to the **2D map canvas**. The 3D viewport uses F to frame a selection instead of a 0-to-fit command. In a focused table cell, **Shift+F10** or the **Menu key** opens the cell menu. Text fields keep their ordinary text-editing controls while active.

In either canvas, **Enter** selects the next object/marker and **Shift+Enter** selects the previous one, wrapping at the ends. Keypad Enter works too. **= / +**, **-**, and keypad **+ / -** zoom. Tab leaves the canvas.

On the map, arrows move a selected movable marker by the active grid step. With no selection, or with Ctrl held, arrows pan. On the 3D viewport, arrows move a selected object in the camera-facing ground directions; Ctrl+arrows, or arrows with no selection, orbit. Alt reverses snapping for the current viewport move. Each accepted move is an undo step.

| 3D viewport key | Action while the canvas has focus |
| --- | --- |
| `B` | Toggle move snapping and 15-degree rotation snapping. |
| `F` | Frame the selected object's bounds. |
| `G` | Choose the Move tool. |
| `R` | Choose the Rotate tool when supported. |
| `S` | Choose the Scale tool when supported. |
| `X` | Constrain the current tool's arrow nudges to X. |
| `Y` | Constrain the current tool's arrow nudges to Y. |
| `Z` | Constrain the current tool's arrow nudges to Z. |

The viewport toolbar has **Snap (B)** and **Grid step (m)**. Choose 0.1, 0.25, 0.5 or 1 metre; snapped arrow moves use that step. Toggle Snap from the toolbar or press B while the canvas has focus. Rotation uses 15-degree steps while Snap is on; Alt still reverses snapping for the current move or turn.

Choose an axis before rotating with arrows. Scale uses the chosen local axis, or all axes when unconstrained. Escape cancels an active move, rotation or scale gesture. Transform controls are unavailable while Play owns the view. See [editing data](editing_data.md#keyboard) for table and canvas navigation details.

## Select objects in the Outliner

In a scene, use Tab to focus an Outliner row and Enter to select it. Hold **Shift** and press **Up/Down** to extend or shrink a range in the displayed tree order; the Inspector and 3D view use the same selection. **Ctrl+Space** toggles the focused row without clearing the other selected objects. These controls apply while a row has keyboard focus.

## Change a shortcut

Press **F1** to open **Keys** (or use the command palette's Keyboard shortcuts action if F1 has changed). Use Tab to reach the action list; Enter enters its rows, and Tab or the arrows move between them. Press Enter on the action to select it. Escape returns from the list to the panel controls.

Tab to **Rebind selected shortcut**, press Enter, then press the new chord. Hold Ctrl, Shift or Alt as needed. Escape cancels capture and preserves the previous shortcut. The captured chord changes only the binding; it does not run the action. **Reset to default** restores the selected action's built-in binding, or leaves it unbound when it has no default.

A conflict message names actions sharing a chord. That chord runs neither action until one is changed. Choose another chord or reset one action. Commands remain available in the palette. Fixed table, canvas, menu and native navigation controls above cannot be rebound here.

BED loads `shortcuts.json` beside its user-local `recent.json` and `settings.json` at startup (`%APPDATA%\brutal-authoring` on Windows). Changes and resets save there immediately, using `WriteOverrides`; startup restores them with `ReadOverrides`. They never change a project's files. Invalid entries are skipped with a plain message in Keys; an unreadable file leaves defaults available. If saving fails, Keys says so and the change works for this session. Opening a project does not overwrite shortcut preferences.

## For custom hosts

A custom host can still supply the live `Shortcuts` shared by its workspace. The Keys panel and command palette reflect those choices:

```csharp
var keys = new Shortcuts();
keys.Set("editor.play.project", "F6");
keys.Set("editor.stop", null);          // remove this binding
keys.ResetToDefault("editor.stop");   // restore Shift+F5
var workspace = new DataWorkspace(project, shortcuts: keys);
```

A chord uses Ctrl, Shift and Alt plus one supported key, such as F6 or Enter. Two actions on the same chord create a clash: that chord runs neither action until one binding changes. Commands remain discoverable through the palette.

For persistence in a custom host, write `keys.WriteOverrides()` to user-local preferences and restore it with `keys.ReadOverrides(text)` before opening a workspace. Hosts without a preferences connection keep Keys panel edits for the current session.

The checked source inventory is `Shortcuts.EditorDefaults`, the scene workspace's Close/Add registrations, `KeysPanel.FixedKeys`, `CanvasKeys`, `ViewportView.Keyboard`, the start-screen handlers and the menu/dialog handlers. The guide test compares every registered built-in action/chord and the fixed-key inventory with those live definitions.

In a data workspace, **Ctrl+H** opens **Find and replace**. Enter whole scalar values (quoted text, numbers, true, false or null), choose **Preview matches**, select the matches and choose **Replace chosen**. Changes stay in drafts with one undo step per file. Preview again after an edit or outside file change.
