# Place and change reusable pieces

A prefab is an original piece you can place more than once. Each prefab copy in a level keeps its own record of the prefab as it was when you last accepted it. You can change one copy and undo that change; saving a new version of the original never silently replaces a placed copy.

You need a writable version-two level and a saved prefab supplied by your project. Opening an older level does not convert it. A prefab must be available for the same kind of level: flat or 3D. For ordinary 3D level controls, see [Edit a level](levels.md); for sprites, see [Flat levels](flat_levels.md).

**Try it:** the download's **samples/prefab-physics** project places one crate prefab three times, with one copy changed, and gives the pieces physics bodies. The flat **samples/sprite-2d** project places a flag-and-block prefab twice. [Start here](start_here.md#5-try-the-2d-and-physics-samples) walks through both.

## Quick steps

1. Open the level from **Files**, and find the **Prefab and images** panel.
2. Choose **Refresh prefabs**, then pick a saved piece in **Prefab**.
3. Fill any required **Bind** choices, then choose **Place prefab**.
4. Select one of its parts and change a field in the Inspector. The field gains an asterisk and a **Revert** button.
5. When the original changes, select the piece and choose **Review update**, then **Accept update** or **Cancel update**.
6. Press **Ctrl+S** to save.

## Place a piece

1. Open the level from **Files** and open **Prefab and images** if its panel is hidden.
2. Choose **Refresh prefabs**, then choose a saved source in **Prefab**. Assistive tools name the refresh button **Refresh prefab sources**. The list shows the prefabs that fit this kind of level; a 3D source does not appear in a flat level's picker.
3. For each **Bind** control, choose the existing object that the named reference slot should target. Required slots say **(required)** and keep **Place prefab** disabled until filled. **Choose target** leaves an optional slot unbound.
4. Choose **Place prefab**. The new copy is selected at the centre of the level (0, 0, 0). Move it in the Inspector or viewport to arrange the whole piece.

A missing, dirty, invalid or duplicate original can refuse discovery or placement. Save the original, fix the named problem and refresh the list. Refreshing the list never changes copies already in the level. Step out of Outliner focus before placing or structurally changing pieces.

After placement, the Outliner shows the new copy with a small box icon before its name. Press **Ctrl+S**, then reopen the level to check it stayed in place. If nothing appears in the picker, read the panel's reason, save the original and refresh again. Next, try changing one copy below.

## Change one copy

The Outliner draws a prefab copy's name in the prefab colour with a small box icon before it. Its parts that still match the prefab use a slightly dimmer prefab colour and no other mark. Hover a row to read what it is, such as **Copy of crate** or **Part of crate copy, unchanged**. Assistive tools hear the same words in the row's name, such as **crate_1, copy of crate**. The high contrast theme uses its own bright prefab colour. Selecting the copy itself lets you move, rotate or scale the whole piece. Selecting one of its parts lets you change that part's fields in the Inspector.

A field you change on a part applies only to that copy. Its Inspector label gains an asterisk, the part gets a small filled dot after its name, and **Revert** appears beside the field. Assistive tools receive a name such as **Revert Position X**. Hovering a changed part lists what changed, such as **Changed in this copy: position**, and assistive tools hear a name such as **Crate, changed in this copy**. Revert puts back the prefab's value for that field. Your other changes to the copy stay.

BED checks every change to position, scale, asset, properties and references before making it. Flat sprites also expose width, height, pivot, drawing order and sampling. A refused edit keeps the draft and history and says why. Each accepted edit or multi-object batch is one undo step. Use **Ctrl+Z** and **Ctrl+Y** for undo and redo.

Changing a part's parent or ID requires **Break Link** first. To follow a named reference, choose its **Go to** button in the piece panel. **Back** restores the previous selection. Step out of focus if the target lies outside the focused branch.

## Review a changed original

Save the original through your project's supported source-editing route, then select a copy or one of its parts.

1. Choose **Review update**. Assistive tools call this **Review prefab update**.
2. In **Review prefab update**, scroll through the reported additions, removals, labels and changed values. Changes you made to this copy stay.
3. Choose **Accept update** to take that version of the prefab in one undo step. **Cancel update** or **Escape** leaves the level unchanged.

Use **Next changes** and **Previous changes** to browse every reported change, 32 per page. Long values have **Next text** and **Previous text** pages of about 1,500 characters. No value is shortened. Paging leaves the level unchanged; the review shows the same changes until you accept or cancel.

If the original or the level changes while the review is open, Accept refuses. Cancel and review again. If the update removes a part or slot that one of your changes or references still needs, the whole update refuses; change or remove what needs it, or keep the version you have. Nothing is dropped to make an update fit. Undo puts back the previous version and your changes.

If the original can't be found, the copies in the level keep what they have. You can't accept a new version until the original is available again.

## Duplicate, unlink or remove

- **Duplicate** on a linked piece is announced as **Duplicate prefab copy**. It copies the whole piece with new IDs, keeps its prefab version and your changes, and selects the new copy.
- **Break Link** turns the copy into ordinary objects. It keeps their IDs, positions, references, sprite fields and order. The objects stop getting prefab updates. Undo restores the link.
- **Delete prefab copy** removes the whole piece. References or children outside the copy can refuse removal with a reason.

Ordinary objects also have **Add empty object**, **Group selection**, **Duplicate objects**, **New object ID**, **Rename object** and **Delete object** controls in this panel. The parts of a prefab copy can't be deleted one by one like ordinary objects.

## Drag a model into a 3D level

Open the level's **Assets** panel and drag a model that has completed the project's import onto **Viewport**. With no object selected, it appears at the view centre captured when dragging started, even if the camera moves afterward. With an ordinary object or a prefab copy selected, the new model goes inside it, at local position zero. An internal part refuses as a parent; select the prefab copy instead.

Delivery creates one selected object and one undo step. Escape or release outside the viewport cancels. A changed level, saved file or asset, focus, a closed project, Play or a read-only file can refuse the drop; no partial object is added. PNG images belong in a flat level, and unrecognized model content must be fixed through import first. Dragging changes the level draft; it never saves it or changes the model's asset record.

## Copy between levels

Use **Ctrl+C** and **Ctrl+V**, or **Edit > Copy** and **Edit > Paste**, to copy objects inside this open project. Selecting a part of a prefab copy copies its whole linked piece. Objects inside it are included. Copy keeps the prefab version and your changes as they are at that moment, even if the original level changes afterward.

Paste selects the copies with new IDs, moves them one metre along X and creates one undo step. References between the copied objects follow the copies. An external target must already exist in the destination under its current ID; otherwise the entire paste refuses. Copy that target too when it should travel with the piece. Parent links are never silently detached.

The destination must be the same 3D or flat kind of level. Step out of focus before pasting. A read-only level, Play, a closed project or a level that changed refuses, and nothing is half-pasted. The object clipboard belongs to this workspace and is cleared on project close; it does not share objects through the system text clipboard. While typing in a field, text copy/paste keeps its usual meaning.

## Save and continue

**Ctrl+S** checks the level and saves it. The project close prompt lists an unsaved level beside unsaved data files. A refused save keeps your unsaved changes. Reopening keeps each copy's prefab version and your changes.

**Save all** and **Close project** include the level alongside data files in one decision. Closing the level's view by its tab, its own window, **Ctrl+W** or **Ctrl+F4** asks **Save**, **Don't Save** or **Cancel** first. **Save as** is unavailable for levels and says why; use **Save**.

Play uses the common saved-files decision and the project's own program. The game must opt in to reading version-two levels. When it does, BED prepares the saved level for it with each placed piece expanded and its local changes applied. Export build carries that prepared level and its checked assets, so the copied game needs neither BED nor the original prefab files. See the [Play guide](playing.md) and [Export guide](exporting.md). Flat levels also support PNG dragging; see [Flat levels](flat_levels.md).

## Edit authored physics

Open **View > Physics** for a version-two level. **Add physics** explicitly starts with zero gravity and a 1/60 second step. Enter world acceleration and the fixed step in seconds; a flat level exposes X and Y only. **Remove physics** refuses while any object still has a body. Opening the panel changes nothing in the level or its undo history.

Select ordinary objects or parts of a prefab copy, then choose **Add body**. The starting body is a static box (it never moves), 1 metre on each side, centred on the object, with friction 0.5. A prefab copy as a whole can't have a body; give one to its parts. In the Inspector, edit **Motion** (static never moves, dynamic is moved by physics, kinematic is moved by the game and pushes others), **Collision shape**, its size and center, **Mass (kg)** and **Friction**. Choosing dynamic starts at 1 kg unless a mass already exists; motion and mass enter together in one undo step. Choosing a primitive shape starts its dimensions at 0.5 metres. Mass remains constant when dimensions change. A mass, size, position or parent that can't be used, or motion along an axis a flat level doesn't have, refuses the whole edit.

On a part of a prefab copy, body fields come from the prefab until you change them. Revert beside Motion also puts back the mass that changed with it. If you changed the whole body at once, Revert on any of its fields puts back the prefab's whole body. **Revert body changes** puts back every body change on the selected parts together, including a removed body. A change that would clash with another of your changes refuses rather than throwing one away. Save, prefab review, undo, the command line and reopening all work on the same level.

In **Physics**, **Bounds** shows collision bounds in the 3D or XY view. This personal setting lasts for the open view, never enters the level and starts no simulation. Spheres and capsules show enclosing boxes. A selected body shows its local center and half extents. **mesh-bounds** previews only a model that has been imported and is up to date, and names its version; a missing, out-of-date, image or empty model shows a reason. It does not import, approve or publish anything. The game build checks the model again. **Use preview as explicit box** copies that exact current local center and half extents in one checked edit. If the assets, the model or the level changed since the preview, the conversion refuses. Choosing box from the Inspector shape list instead starts with the displayed primitive defaults.

To see a finished example, open the download's **samples/prefab-physics** project ([its README](../../samples/PrefabPhysicsProject/README.md)). It has a static floor, a kinematic paddle and three dynamic crates, and its Play prints what the simulation did as text; it has no game window yet.

The separate [saved physics sample](../../samples/PhysicsStarter/README.md) runs these bodies in a game, through Play and in an exported build. It opts in to physics in its project settings and uses a Windows-only native physics library. A game that hasn't opted in refuses a level with physics rather than ignoring the bodies. Reading a level never starts a simulation, and the simulation never changes the saved level.

## Names for copies

Choose **Names for copies** in **Options** to set the style for duplicated or pasted pieces. Both the wrapper and its inherited parts get new display names; IDs, bindings and references keep their existing rules. A wrapper stores its optional display name as `placement.label` in a scene-v2 file. Old files need no label and keep it absent when saved.
