# Browse assets

**Assets** shows the imported models and textures in your project's `assets/` folder. It recognises OBJ (`.obj`) and glTF (`.gltf` or `.glb`) models. Select a row to see its file type, source, licence and any problems needing attention.

The bundled level example includes a cube, a pillar and a textured crate with an 8 by 8 pixel image. Keep a model's material files, images and import records together when copying it to another project.

1. Open **Assets**, or choose **Scene > Add object** in a level. **Insert** also starts Add object.
2. Type in **Search assets** to narrow the list.
3. Turn on **Problems only** to find records needing attention.
4. Select a row to read its details.

**No imported assets.** means no assets have been imported here. **No matching assets.** means the search found none. **Not imported** lists source files that still need import records; follow your project's asset import setup. This panel browses the records and previews prepared content.

## Textures and thumbnails

Select a prepared texture to see **Texture thumbnail:** with its filename, and **Size:** with the original width and height in pixels. The thumbnail fits within a bounded preview area; the image's recorded pixel size remains visible as text and is available to a screen reader.

BED prepares texture pixels in its **BEDTEX1** format. That prepared copy supplies the thumbnail and model textures; the panel does not overwrite the source image. Supported source images include PNG, JPEG and TGA. You do not need to rename an image to BEDTEX1.

If the prepared pixels cannot be used, **Texture preview unavailable:** explains why. A missing source, duplicate asset ID, changed import or invalid image needs attention before using it. Open the project again after correcting its saved assets so BED can prepare them.

## Models, parts and glTF

The type line distinguishes **Model (OBJ)**, **Model (glTF)** and **Model (GLB)**. The **Model:** details line gives the filename and part count. A part is a section of a model with its own material; selecting or moving the placed model still acts on the whole object.

Models with several parts keep each part's base colour and available base-colour texture in the 3D view. The texture thumbnail belongs to the selected texture asset; the model details show text and a part count. If the CPU fallback is active, the view says **Material textures are not displayed.** and draws the part colours.

Keep a glTF model's external buffers and images with it, even when its main file is `.glb`: a GLB can still refer to external images. The current view displays static geometry. Imported skinning and animation information does not mean the view plays those animations. Unsupported compression or invalid resource paths can refuse an import; read the reported problem and see [checking a model](importing_assets.md).

## Add a model to a level

Select a model in **Add object**, then choose **Add selected object**. You can also drag it from Assets into the level. Either action can be undone with **Ctrl+Z**. Canceling or dropping outside the level leaves it unchanged. Textures are used by model materials; choosing a texture does not add a model.

If the model has changed or needs importing, BED refuses to place it and shows a reason. Fix the problem, then select it again. See [Edit a level](levels.md) for the full walkthrough and [Export a game build](exporting.md) for keeping the saved level and prepared cache together.

## Before using an asset

Keep the model's origin and licence with its source. **Source file found. Placement and export need separate checks.** confirms that the source exists; placement and export perform their own checks. Before sharing a build, check that you may include each asset; before sending one to an AI service, check its AI-use terms. See [content rights](content_rights.md).

## Source reference

- [Assets panel](../../src/Brutal.Authoring.Editor.ImGui/AssetsPanel.cs)
- [Catalog selection and leases](../../src/Brutal.Authoring/AssetCatalog.cs)
- [Scene placement checks](../../src/Brutal.Authoring.Editor/DocumentHost.cs)
