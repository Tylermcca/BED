# Place and change reusable pieces

A prefab is an original piece you can place more than once. Each placed instance keeps an accepted copy of the original. You can change one instance and undo that change; saving a new original never silently replaces a placed copy.

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
2. Choose **Refresh prefabs**, then choose a saved source in **Prefab**. Assistive tools name the refresh button **Refresh prefab sources**. The list shows admitted names for this level's space; a 3D source does not appear in a flat level's picker.
3. For each **Bind** control, choose the existing object that the named reference slot should target. Required slots say **(required)** and keep **Place prefab** disabled until filled. **Choose target** leaves an optional slot unbound.
4. Choose **Place prefab**. The new instance is selected at the level origin. Move its placement in the Inspector or viewport to arrange the whole piece.

A missing, dirty, invalid or duplicate original can refuse discovery or placement. Save the original, fix the named problem and refresh the list. Refreshing the list never changes instances already in the level. Step out of Outliner focus before placing or structurally changing pieces.

After placement, the Outliner shows the new **[instance]**. Press **Ctrl+S**, then reopen the level to check it stayed in place. If nothing appears in the picker, read the panel's reason, save the original and refresh again. Next, try changing one copy below.

## Change one copy

The Outliner marks the instance placement **[instance]** and its parts **[inherited]**. Selecting the placement lets you move, rotate or scale the whole piece. Selecting an inherited part lets you edit that part's supported fields in the Inspector.

An edited inherited field becomes a local override. Its Inspector label gains an asterisk, the part is marked **[local changes]**, and **Revert** appears beside the field. Assistive tools receive a name such as **Revert Position X**. Revert removes that field's override and restores the value from this instance's accepted copy. Other local changes remain.

Position, scale, asset, existing properties and references use checked edits. Flat sprites also expose width, height, pivot, drawing order and sampling. A refused edit keeps the draft and history and says why. Each accepted edit or multi-object batch is one undo step. Use **Ctrl+Z** and **Ctrl+Y** for undo and redo.

Changing a part's parent or ID requires **Break Link** first. To follow a named reference, choose its **Go to** button in the piece panel. **Back** restores the previous selection. Step out of focus if the target lies outside the focused branch.

## Review a changed original

Save the original through your project's supported source-editing route, then select an instance or one of its inherited parts.

1. Choose **Review update**. Assistive tools call this **Review source update**.
2. In **Review prefab update**, scroll through the reported additions, removals, labels and changed values. Local overrides remain in the candidate.
3. Choose **Accept update** to accept that captured source revision in one undo step. **Cancel update** or **Escape** leaves the level unchanged.

Use **Next changes** and **Previous changes** to browse every reported change, 32 per page. Long values have **Next text** and **Previous text** pages of about 1,500 characters. No value is shortened. Paging leaves the level unchanged; the review keeps the same captured candidate until you accept or cancel.

If the original or level changes while the preview is open, acceptance refuses. Cancel and review again. If a removed part or slot is still needed by a local change or reference, the whole update refuses; resolve the conflict or keep the accepted copy. Nothing is dropped to make an update fit. Undo restores the previous accepted copy and local overrides.

An unavailable original does not erase an instance's accepted content. It prevents accepting a new source revision until the source is admitted again.

## Duplicate, unlink or remove

- **Duplicate** on a linked piece is announced as **Duplicate instance**. It copies the whole instance with new identities, keeps its accepted content and local changes, and selects the new placement.
- **Break Link** makes the accepted piece ordinary objects. It preserves IDs, transforms, references, sprite fields and effective object order. The objects stop receiving source-update choices. Undo restores the link.
- **Delete instance** removes the whole piece. References or children outside the instance can refuse removal with a reason.

Ordinary objects also have **Add empty** (announced as **Add empty object**), **Group selection**, **Duplicate objects**, **New object ID**, **Rename object** and **Delete object** controls in this panel. An instance's internal parts cannot be individually deleted as ordinary objects.

## Drag a model into a 3D level

Open the level's **Assets** panel and drag a model that has completed the project's import onto **Viewport**. With no object selected, it appears at the view centre captured when dragging started, even if the camera moves afterward. With an ordinary object or instance placement selected, the new ordinary model is parented there at local position zero. An inherited internal part refuses as a parent; select the instance placement instead.

Delivery creates one selected object and one undo step. Escape or release outside the viewport cancels. A changed scene, saved file, asset content or policy, focus, project ownership, Play or edit policy can refuse delivery; no partial object is added. PNG images belong in a flat level, and unrecognized model content must be fixed through import first. Dragging changes the level draft; it never saves it or changes the model's asset record.

## Copy between levels

Use **Ctrl+C** and **Ctrl+V**, or **Edit > Copy** and **Edit > Paste**, to copy objects inside this open project. Selecting an inherited part copies its whole linked piece. Descendants are included. Copy keeps the accepted content and local changes at that moment, even if the original level changes afterward.

Paste selects the copies with fresh identities, moves copied roots one metre along world X and creates one undo step. Internal references, bindings and reference overrides follow the copies. An external target must already exist in the destination under its current ID; otherwise the entire paste refuses. Copy that target too when it should travel with the piece. Parent links are never silently detached.

The destination must use the same 3D or flat coordinate profile. Step out of focus before pasting. Read-only state, Play, a closed project or a stale target refuses without partial objects. The object clipboard belongs to this workspace and is cleared on project close; it does not share objects through the system text clipboard. While typing in a field, text copy/paste keeps its usual meaning.

## Save and continue

**Ctrl+S** saves through the level's own validation and conflict checks. Close decisions include the actual level draft; the project close prompt can list it beside unsaved data files. A refused save retains the draft. Reopening keeps the accepted content and local overrides.

**Save all** and **Close project** include the level alongside data files in one decision. Closing the level's view by its tab, its own window, **Ctrl+W** or **Ctrl+F4** asks **Save**, **Don't Save** or **Cancel** first. **Save as** is unavailable for levels and says why; use **Save**.

Play uses the common saved-files decision and the project's own program. The game must opt in to reading version-two levels. When it does, BED prepares the saved level for it with each placed piece expanded and its local changes applied. Export build carries that prepared level and its checked assets, so the copied game needs neither BED nor the original prefab files. See the [Play guide](playing.md) and [Export guide](exporting.md). Flat levels also support PNG dragging; see [Flat levels](flat_levels.md).

## Edit authored physics

Open **View > Physics** for a version-two level. **Add physics** explicitly starts with zero gravity and a 1/60 second step. Enter world acceleration and the fixed step in seconds; a flat level exposes X and Y only. **Remove physics** refuses while any resolved body remains. Opening the panel changes no level or history.

Select ordinary objects or inherited prefab nodes, then choose **Add body**. The explicit starting body is a static box with 0.5 metre half extents, zero center and friction 0.5. A placement wrapper cannot own a body. In the Inspector, edit **Motion**, **Collision shape**, local dimensions/center, **Mass (kg)** and **Friction**. Choosing dynamic starts at 1 kg unless a mass already exists; motion and mass enter together in one undo step. Choosing a primitive shape starts its dimensions at 0.5 metres. Mass remains constant when dimensions change. Invalid mass, dimensions, transforms, ancestry or hidden-axis motion refuse the complete edit.

Child fields inherit from this instance's accepted snapshot. Revert beside Motion also returns its coupled local mass change. Editing an existing whole-body local value keeps that whole-body ownership; its field Revert restores the complete accepted body. **Revert body changes** clears all selected body overrides together, including a removed body. Incompatible whole/child override requests refuse rather than discarding another local value. Save, source review, undo, checked CLI edits and reopening use the same scene owner.

In **Physics**, **Bounds** shows collision bounds in the 3D or XY view. This personal setting lasts for the open view, never enters the level and starts no simulation. Spheres and capsules show enclosing boxes. A selected body shows its local center and half extents. **mesh-bounds** previews only a current admitted model and names its content revision; missing, stale, image or degenerate content shows a reason. It does not import, approve or publish anything. Runtime build must capture and verify the model again. **Use preview as explicit box** copies that exact current local center and half extents in one checked edit. A changed catalog, model revision or edit policy refuses the conversion. Choosing box from the Inspector shape list instead starts with the displayed primitive defaults.

To see a finished example, open the download's **samples/prefab-physics** project ([its README](../../samples/PrefabPhysicsProject/README.md)). It has a static floor, a kinematic paddle and three dynamic crates, and its Play prints what the simulation did as text; it has no game window yet.

The separate [saved physics sample](../../samples/PhysicsStarter/README.md) runs these bodies in a game, through Play and in an exported build. It opts in to physics in its project settings and uses a Windows-only native physics library. A game that hasn't opted in refuses a level with physics rather than ignoring the bodies. Reading a level never starts a simulation, and the simulation never changes the saved level.
