# Edit a flat level

A flat level uses the XY plane: X goes right, Y goes up, and rotation turns around Z. You need a writable version-two flat level and PNG images your project has imported with their origin and licence records. Opening an ordinary 3D level does not convert it.

**Current preview:** the Vulkan flat view draws admitted sprite images with transparent overlap and the saved nearest/linear sampling choice, with selection bounds and markers. A missing or stale image reports a reason and shows its bounds. Hosts without sprite image presentation, including the OpenGL fallback, show bounds. A game that opts in to version-two levels can run the saved sprites, and Export build carries their images.

## Quick steps

1. Open the flat level from **Files**, and find the **Prefab and images** panel.
2. Choose **Refresh images**, pick a picture in **Image**, and choose **Add sprite**. Or drag a PNG from **Assets** onto **Flat XY**.
3. Focus **Flat XY** and press **W** to move, **R** to rotate or **S** to scale, then drag a handle.
4. Set exact values, size, pivot and draw order in the Inspector.
5. Press **Ctrl+Z** to undo, and **Ctrl+S** to save.

## Place a sprite

1. Open the level from **Files**, then find **Prefab and images**.
2. Choose **Refresh images** and select an imported picture in **Image**.
3. Choose **Add sprite**. The new sprite is selected at the level origin.
4. In the Inspector, set **Position X** to **1** and leave the field. The sprite moves one metre right in **Flat XY**.
5. Press **Ctrl+Z** to undo the move, then **Ctrl+Y** to restore it.
6. Press **Ctrl+S**, close and reopen the level, and check the saved position.

If no image is offered, read the reason in the panel and check the image import and licence records. A missing image can show only its bounds. Next, adjust the size and pivot below or follow [Export a game build](exporting.md).

For native dragging, open the level's **Assets** panel, drag an admitted PNG onto **Flat XY**, and release at the desired root position. The image defaults are captured when dragging starts. Escape, release outside the canvas, changed scene/image policy, read-only state or a closed target cancels placement. No object appears until delivery. Step out of focus before placing a root sprite. This route places flat sprites; 3D models use **Assets** and **Viewport** in a 3D level, as described in [Reusable pieces](prefabs.md).

A loose picture without an admitted asset record is not enough. Use your project's image import/reimport route first; see [Assets](assets.md) and [Content rights](content_rights.md). No asset is found by guessing a filename from its identity. A known model asset refuses as an image in a flat level.

If the selected image or its policy changed, refresh the selection after fixing the reported admission or reimport problem. Failed placement adds no object or undo step. Existing placements keep their saved dimensions when the image is reimported or its defaults change.

You can also place a flat prefab with **Prefab**, named **Bind** targets and **Place prefab**. See [Reusable pieces](prefabs.md).

## Size, pivot and order

Select a sprite in the Outliner or canvas. The Inspector provides:

| Field | Meaning |
| --- | --- |
| **Position X**, **Position Y** | Local metres relative to its parent. |
| **Rotation Z** | Degrees in the XY plane. |
| **Scale X**, **Scale Y** | Transform factors; negative values mirror. |
| **Inside** | Parent object ID. An inherited part needs Break Link before changing its parent. |
| **Sprite width**, **Sprite height** | Positive full-image size in metres, independent of transform scale. |
| **Pivot X**, **Pivot Y** | Anchor within the image, from 0 to 1 measured from the bottom-left. |
| **Draw order** | Lower numbers draw first; effective document order breaks ties. Picking goes in reverse. |
| **Filter** | Nearest or linear image sampling in the Vulkan preview. |

The pivot stays at the object's origin. Changing it moves the art's bounds around that origin; children stay anchored to the object. Width and height do not silently correct the aspect ratio. Changing one component leaves the other component and unrelated sprite fields intact.

Image defaults seed a new sprite once. Without explicit image defaults, density is 100 pixels per metre, pivot is the centre and sampling is nearest. Width and height are decoded pixel dimensions divided by density. Later density/default changes affect new placements, not existing copies or prefab overrides. Invalid sizes, pivots, transforms or filters refuse the entire edit.

## Navigate and transform

Focus the **Flat XY** canvas before using these keys. There are no depth moves or orbit controls here. With the optional **W/E/R** viewport keymap in Preferences, **E** rotates and **R** scales instead.

| Control | Action |
| --- | --- |
| **W** / **Move** | Choose movement; drag the X/Y axis handles to constrain world motion, drag a selected bound freely, or use the arrows to nudge in XY. |
| **R** / **Rotate** | Choose rotation; drag the Z ring or drag away from the selection centre. |
| **S** / **Scale** | Choose scaling; drag a local X/Y handle to change that component, the diagonal handle for both, or drag freely away from the selection centre. |
| **Snap**, **Grid (m)** | Choose movement snapping and the grid/nudge distance. Rotation snaps to 15 degrees; scale snaps in tenths. |
| **Alt** | Reverse snapping during a drag. |
| Right-drag or middle-drag | Pan. |
| Wheel | Zoom around the cursor. |
| **F** | Frame selected bounds. |
| **Home** | Frame all bounds. |
| **Escape** | Cancel a transform preview. |

Click chooses the topmost bound at that point; Ctrl-click toggles it. These are bounds picks, not transparent-pixel picks. The focus border shows when the canvas receives keyboard input. The axis and ring controls follow the selection pivot. Move uses world XY; scale uses the first selected root's local axes and preserves reflected signs. For several roots, the same component factor applies to each root; selected descendants follow once. Use the Inspector for exact component values.

Dragging previews without editing the draft; release commits one checked undo step. Selected descendants follow selected parents once rather than moving twice. A changed scene, selection, focus, camera or viewport cancels an old capture. Read-only state or Play also prevents editing. Pan, zoom and framing leave the level unchanged. When the host binds personal view preferences, the XY camera is restored separately for each project and level after reopen. Pan/zoom/frame changes are remembered after the gesture ends. Camera choices do not enter the saved level or undo history. Invalid or unwritable preferences report a reason and keep editing available.

## Save, undo and current limits

Use **Ctrl+Z**, **Ctrl+Y** and **Ctrl+S** for undo, redo and checked save. Invalid edits and failed saves keep the prior draft. The saved sprite values and prefab metadata survive save, close and reopen. See [Reusable pieces](prefabs.md) for local overrides, Revert and reviewed source updates.

Use the named picker or PNG dragging for sprite placement. Copy/paste works inside this project between flat levels; see [Reusable pieces](prefabs.md). For collision bodies and gravity, open **View > Physics**; see [Reusable pieces](prefabs.md#edit-authored-physics). Flat physics keeps bodies moving in X and Y and turning around Z.

The [Play guide](playing.md) describes the saved-file decision. Your game must opt in to version-two levels before it can run them. An exported build includes the sprites' images, so the copied game runs without the original image files. Mixed 2D/3D rendering, atlases and animation are not part of this release.
