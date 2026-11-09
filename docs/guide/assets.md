# Browse assets

The Assets panel lists the records supplied by your project's asset browser. It is not an automatic importer or a permission check for distribution.

1. Open the host-provided **Assets** panel, or use the scene's **Add object** picker.
2. Type in **Search assets** to narrow the list.
3. Turn on **Problems only** to find records needing attention.
4. Select a row to read its path, ID, importer, licence, AI-use setting and reported issues.

**No imported assets** means there are no records in this browser. **No matching assets** means the current search found none. The **Not imported** section lists sources without imported records. A missing or ambiguous source is reported rather than silently replaced with another file.

## What the list proves

An indexed source is not proof that its imported content is current, that it can be placed, or that you may export it. Scene placement separately resolves a captured catalog selection and refuses missing, pending, stale or changed entries. If a refresh changes the selected entry, select it again rather than assuming an old selection still applies.

When the scene host connects placement, you can drag an asset to the scene or use the keyboard picker described in [Edit a level](levels.md). Both paths make one undoable addition. Canceling or dropping outside the target leaves the scene alone.

The current list shows metadata and diagnostics. Do not expect every entry to have a thumbnail, an asset-type classification, an import-settings editor or a Reimport button. Those depend on additional host integration; the browser's existing source index is not a substitute for it.

## Before using an asset

Keep its origin and licence information with the source. Read [content rights](content_rights.md) before distribution, and [model inspection](importing_assets.md) for the separate import-check tool. Import success does not grant redistribution rights or permission to send the asset to an AI service. Do not use private or licensed content as a troubleshooting fixture without the necessary permission.

## Source reference

- [Assets panel](../../src/Brutal.Authoring.Editor.ImGui/AssetsPanel.cs)
- [Catalog selection and leases](../../src/Brutal.Authoring/AssetCatalog.cs)
- [Scene placement checks](../../src/Brutal.Authoring.Editor/DocumentHost.cs)
