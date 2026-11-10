# Edit a level

A level contains objects you can move, rotate, resize and group. A game's adapter connects its level files to BED's level editor. Stop Play before changing the level.

## Open and select objects

Choose **Open project** and select your project folder, then open **level.json** in **Files**. For the 3D example, choose the supplied level-example folder. The project must include its level schema and adapter connection.

The **Outliner** window contains the object list. Select a row, or click an object in the 3D view, to inspect it. Ctrl-click a row to toggle it in the selection. On scene rows, **Shift+Up/Down** extends the keyboard selection and **Ctrl+Space** toggles the focused object. Use Ctrl+Tab to choose a panel, Tab to reach its rows, and the arrows to navigate. **F** focuses the selected outline branch; **Escape** steps out of that focus. The Inspector follows the selected object.

With the Outliner focused, **Ctrl+F** enters **Search objects**. Search by object ID or asset name. Parents of matching objects remain visible, so you can follow the hierarchy. **Escape** clears the filter. Searching changes the view and leaves the saved level alone.

## Add an object

With the level open:

1. Clear the selection to place an object on its own, or select an object to add to its group.
2. Choose **Scene > Add object**, or press **Insert** outside a text field.
3. Search the asset picker and select an asset.
4. Choose **Add selected object**. Use **Cancel placement** to leave the scene unchanged.

An object added to a group starts at the selected object's position. An object placed on its own starts at the view centre. You can also drag a model from Assets into the level. **Ctrl+Z** undoes either kind of addition.

If placement refuses, read its reason and start Add object again after fixing it. **No asset catalog for this project** means the asset setup is missing; check the project's instructions.

## Move, rotate and scale

Use **Position**, **Rotation** and **Scale** in the Inspector, or focus the 3D canvas and use its toolbar and handles. A child's Inspector position is relative to its parent.

| Canvas control | Action |
| --- | --- |
| **Move (G)** / **G** | Choose translation. Arrows nudge along the ground directions nearest the camera. |
| **Rotate (R)** / **R** | Choose rotation; choose **X**, **Y** or **Z** before turning with arrows. |
| **Scale (S)** / **S** | Choose scale. Arrows change uniform scale until you choose X, Y or Z for a local axis. |
| **X**, **Y**, **Z** | Constrain the current tool to an axis. |
| **F** | Frame the selected object's bounds. |
| **Escape** | Cancel an active transform gesture. |

Each accepted keyboard nudge is one undo step. Without move snapping, an ordinary ground-direction arrow nudge is 0.5 metre. With no selection, or with Ctrl held, arrows orbit instead of moving an object. Right-drag orbits, Shift+right-drag pans, and the wheel or +/- zooms.

**Snap (B)** toggles move and rotation snapping; press **B** while the canvas has focus for the same action. **Grid step (m)** offers 0.1, 0.25, 0.5 and 1 metre. Snapped arrow moves use the selected grid step. Rotation snaps to 15 degrees while Snap is on. Hold **Alt** to reverse snapping for the current move or turn.

## Duplicate, copy and paste

These shortcuts are defaults. F1 opens Keys with the bindings currently in effect. Use them outside a text field.

| Shortcut | Edit menu command | Result |
| --- | --- | --- |
| **Ctrl+D** | **Duplicate** | Copy the selected objects to new unique IDs, move the copies 1 metre along world X and select them. |
| **Ctrl+C** | **Copy** | Remember the selected objects in this project's in-app clipboard. |
| **Ctrl+V** | **Paste** | Paste those objects with new unique IDs, move them 1 metre along world X and select them. |

Duplicate and Paste each make one undo step. Copy leaves the level unchanged. To paste into another level in the same project, copy first, open the other level in Files, then paste. Opening another project clears this clipboard.

Links between copied objects follow their copies. A link to an object outside the copied selection must resolve in the destination level; copy that target too if the refusal names it. If a copied object's parent is absent from the destination, Paste detaches that object while preserving its world pose. Include children in the copied selection when you want them copied too.

## Group and ungroup

**Ctrl+G**, or **Edit > Group**, puts the selected objects under a new empty parent at their world-position centre. It preserves their world poses and selects the new parent. The parent receives a unique ID such as group or group_2.

Select one parent and use **Ctrl+Shift+G**, or **Edit > Ungroup**, to remove it and select its children. Child world transforms stay unchanged. Group and Ungroup each make one undo step.

Step out of outline focus before grouping. Ungroup refuses when another object has a named reference to the parent. Use Delete to review and clear those references first if removing that parent is intended.

## Rename an ID

Select one object and press **F2**, or choose **Edit > Rename object**. Type the new **Object ID** in the inline field. **Enter** commits; **Escape** cancels.

An ID starts with a lowercase letter and contains only lowercase letters, digits and underscores. An empty, invalid or already-used ID refuses with a reason; correct the field and try again. Renaming updates parent links and named references to that object, keeps the object order and makes one undo step. It changes the ID, which can differ from a separately supplied display name.

## Delete and review references

Select one object and press **Delete**, or choose **Edit > Delete**. If other objects refer to it, **Delete referenced object?** lists those objects and their links.

Choose **Cancel** (or Escape) to keep everything. **Delete anyway** removes the selected object, keeps the other objects, detaches its children at their world positions and clears their links to the removed ID. Undo restores the object and its links together. A delete without referring objects happens immediately.

## Save and undo

**Ctrl+Z** undoes a document edit; **Ctrl+Y** redoes it. **Ctrl+S** saves the active level through its validation, save policy and file-conflict checks.

If another program changed or removed the saved file, BED keeps your draft and asks you to resolve the conflict. Stop Play before saving. Use Save for an existing level; Save as for levels is still being finished. Play and export use saved files and retain the undo history.

Next: [browse assets](assets.md), [run Play](playing.md), [export a build](exporting.md), or [see all keyboard controls](keyboard.md).
