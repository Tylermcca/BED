# Edit a level

A level is a scene document: objects have stable IDs, a parent, a local position, rotation and scale, and optional asset references. Your project host must connect the scene format before BED can open that file as a scene. Opening arbitrary JSON does not establish that connection.

## Add an object

With an admitted scene open and an asset catalog connected:

1. Select the parent object, or clear the selection to place a root object.
2. Choose **Scene > Add object.**, or press **Insert** outside a text field.
3. Search the asset picker and select an asset.
4. Choose **Add selected object**. Use **Cancel placement** to leave the scene unchanged.

A child starts at local position zero under the selected parent. A root starts at the captured view centre. Adding an object is one undoable edit. The keyboard and asset-drag routes use the same placement checks. Dropping outside the scene does not place an object.

If the scene, parent or asset changed while the picker was open, placement can be refused. Read the reason, then start Add object again. A listed source file alone is not permission to place it: the host must supply its admitted catalog. **No asset catalog for this project** means that connection is missing, not that you should bypass the check.

## Inspect and save

Select an object in Objects to inspect it. Position, rotation and scale are local to its parent. Use the available scene commands and Undo to revise the draft; asset placement does not save it automatically.

Save writes the current scene only after validation, project policy and the on-disk conflict check succeed. If another program changed or removed the saved file, BED keeps your draft and refuses the write. Resolve the conflict before trying again. Stop Play before saving. Scene **Save as** is not yet connected to the project save/conflict policy.

Scene support depends on the project host. This page does not promise that every folder or starter adapter supplies a scene format, asset producer or runtime.

Next: [browse assets](assets.md), [run Play](playing.md), or [review a build](building.md).

## Source reference

- [Scene binding and save rules](../../src/Brutal.Authoring.Editor/DocumentHost.cs)
- [Asset picker and Insert route](../../src/Brutal.Authoring.Editor.ImGui/DataWorkspace.SceneAssets.cs)
- [Placement lifecycle](../../src/Brutal.Authoring.Editor.ImGui/AssetPlacement.cs)
