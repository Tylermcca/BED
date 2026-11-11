# Accessibility

## Use BED with a keyboard

Press Tab or Shift+Tab to move between controls. Enter activates a focused item; Space activates buttons and checkboxes. Ctrl+Tab cycles panels: release Ctrl to choose. Some scrolling child lists need Enter to enter the list and Escape to return to its parent. The focus outline shows which control receives the next key. In the focused 3D viewport, Tab/Shift+Tab cycles objects in Outliner order and announces the selected name; Enter frames the selection. Escape then Tab leaves the viewport for its toolbar (Escape then Shift+Tab goes back).

Press Alt to reveal menu access letters. In an open project, F1 opens **Keys**, also available from **Help > Keyboard shortcuts**. Its list includes the active host and plug-in bindings. Ctrl+Shift+P opens the command palette; type a command, use Down/Up to choose a runnable result, Enter to run it, and Escape to close it. See the [keyboard guide](keyboard.md) for table, form, viewport and rebinding controls.

To try a level without a mouse:

1. On the start screen, Tab to **Try the example** and press Enter. BED makes a personal copy.
2. Press Insert to open the asset picker. Tab to its controls, then enter the asset list and choose a model with the arrow keys and Enter. Escape returns from the list; Tab to **Add selected object** and press Enter.
3. Focus **Viewport** through the panel switcher or palette. Press X, then Right Arrow to move the selected object on X.
4. Press Ctrl+S to save and wait for the result, then F5 to Play the saved files. Shift+F5 stops Play. Read any refusal before retrying.

That route has a headless test using native keyboard events, visible focus checks and the real example program. It does not establish that every custom plug-in control is keyboard accessible. The [level guide](levels.md) explains movement and selection; [Play](playing.md) explains save decisions and run settings.

## Screen readers and assistive tools

The Windows x64 desktop host supplies an AccessKit accessibility tree to Windows UI Automation. Built-in wrapped controls expose labels, roles, values, focus and enabled state. Status messages are exposed as status text. Supported actions include focus, button invocation, checkbox toggling, list selection and setting editable values. These actions use the same checked handlers as keyboard and mouse input: read-only fields, disabled commands, validation and save decisions still apply.

Stepped numeric fields expose an editable value and named **Decrease** and **Increase** buttons. Setting the value or invoking a step uses the field's ordinary edit handler, including its validation and undo. Holding Ctrl uses the configured fast step. Read-only or disabled fields refuse changes; stale targets and invalid or non-finite values are refused. Native keyboard entry and mouse stepping remain available.

Support is incomplete. A custom plug-in or directly drawn widget may lack labels or actions; the 3D canvas does not expose every piece of geometry as an editable screen-reader control. A stale or unavailable target can refuse an action. Automated tree and action tests cover the bridge, including floated panels, but a complete real Windows screen-reader walkthrough after these changes has not been completed. Compatibility with a particular screen reader or other assistive product is not certified. Accessibility support on other desktop platforms is not established here.

If your assistive tool cannot reach a control or read its result, use the [screen-reader command-line route](using_a_screen_reader.md) for supported data tasks. Its text output can be read through a terminal. Load the project's adapter when it supplies extra validation; preview an edit before saving. The [AI tools guide](using_bed_with_ai.md) describes another route through the same checked editing tools. Neither route supplies a spoken description of the entire scene canvas.

## Text size: 100% to 200%

On the start screen, open **Preferences**, then **UI scale** and choose **100%**, **150%** or **200%**. In a project, use **Edit > Preferences > UI scale** for the same choices. **Larger text**, **Smaller text** and **Normal text size** are also available there; the start screen has **Larger** and **Smaller** buttons.

BED's default text is larger than in earlier versions, and it follows the text size you set in Windows' accessibility settings and your display scaling. The UI scale you choose here applies on top. BED reads the Windows setting; it never changes it.

Scale is saved in your per-user settings and restored when BED reopens. It changes editor text and controls without editing project files. At a larger size, use the panel's scroll controls or enlarge the panel if its contents extend beyond the visible area. Built-in data and scene panels have automated checks at all three sizes; custom panels may need their own adjustments.

## High contrast

On the start screen choose **Preferences > Theme > High contrast**. In a project choose **Edit > Preferences > Theme > High contrast**. This uses dark opaque surfaces, white text and yellow focus and selection accents. The scene uses a dark background, a contrasting grid and yellow selected-object bounds. The choice is saved for the next launch and does not change Windows settings or project files. Choose Light, Dark or the Windows-setting choice to switch away.

High contrast changes editor colours; it does not recolour imported textures or guarantee contrast inside a game's own Play window. Text contrast and built-in panel layouts have automated coverage; this is not a claim of complete accessibility conformance.

## Errors and problem reports

When BED refuses an edit, the reason appears next to the field as well as in the status bar. Correcting the value clears it.

**Help > Report a problem** opens a report you can read in full before you copy or save it, or open the issue page yourself. It is labelled for assistive tools and reachable by keyboard. See [Reporting a problem](reporting_a_problem.md).
