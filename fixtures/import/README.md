# Original import fixtures

INVENTED: every mesh, pixel, name and fixture document here is self-authored for the import matrix. Nothing comes from a game, purchased pack or downloaded model. The geometry is deliberately small. No importer is implemented by this directory.

The reference convention is [Khronos glTF 2.0](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html): metres, right-handed, +Y up, +Z forward and asset-right -X. These facts inform the fixture; the proposed import/repair policies below are this project's explicit acceptance choices. Check combined determinants and inverse-transpose normals when baking transforms.

## Regenerate

Use Blender **5.2** in the background. The tested build is 5.2.2 LTS, build hash d13f752e3b9c. From the repository root, with Blender available as `blender`:

```text
blender -b --factory-startup --python-exit-code 1 -P fixtures/import/make_fixtures.py -- fixtures/import
```

The extra Python exit-code option propagates generation errors. The card's shorter invocation without that option also works. Never launch the UI for this process. The generator checks background mode and Blender's major/minor version.

Exporter choices are explicit: GLB, Y-up, UVs/normals/extras, no tangent/morph/light/camera/GPU-instancing export, no optimizer, unchanged node transforms, PNG source images, and animation only for the skin. AUTO image output preserves those PNG inputs; Blender 5.2 has no PNG override enum. The compression fixture alone enables Blender's bundled Draco encoder. Do not install an add-on or substitute fake compressed bytes.

Run the command twice with the same generator and Blender build. Compare SHA256 of every generated GLB, PNG and expected.json; the generator prints those hashes. Filenames, model names, key ordering and pixels contain no time/random values. Byte identity is checked for this pinned environment; another Blender/exporter/codec version needs its own comparison.

Each file is under 200,000 bytes. The two PNGs are original 2 by 2 constant RGBA images. Their encoded bytes are base colour [128,64,32,255] and normal [128,128,255,255]. Base colour is sRGB-encoded; the normal data is linear. Do not apply sRGB conversion to the normal map. Material factors in expected.json use linear values.

## Cases

| Id | Files / generator | Purpose and expected policy |
|---|---|---|
| IM-01 | im01_cube.glb | 1 m cube, 12 triangles, normals and UV0, bounds [-0.5,-0.5,-0.5] to [0.5,0.5,0.5], tolerance 1e-5. Vertex count is deliberately unpinned because seams split vertices. |
| IM-02 | im02_embedded.glb; im02_relative.glb; base.png; normal.png | Two materials, embedded and allowed relative image resources, explicit factors, MASK alpha cutoff 0.4, and base/normal role encodings. Resolve relative paths inside the fixture resource root. |
| IM-03 | im03_skin.glb | One skin, three joints, one 1 s clip. Import the declared bind/rest pose at t=0 and report skin/clip data with the display-deferred warning; do not claim the animation plays. |
| IM-04 | im04_transforms.glb; im04_zero.glb | Bake complete node-world transforms. Parent/child scales combine to [1,6,1]. The slanted normal is [0,-1/sqrt(37),6/sqrt(37)]. Mirror's determinant is -1, DoubleReflection's is +1. Flip once for the combined determinant, not once per ancestor. Refuse the singular Zero transform with no replacement output. |
| IM-05 | im05_arrow.glb | Closed 24-triangle arrow along +Z, length 2 m; right marker [-0.35,0.2,0.7]. Marker vectors are authored glTF vectors in extras. The two generic frame mappings and winding parity are explicit below. |
| IM-06 | generated_truncated; generated_wrong_extension; im06_required_extension.glb; im06_draco.glb; im06_bad_index.glb; im06_degenerate.glb | Separate truncated container, unsupported name, unknown required feature, real unsupported compression, invalid index and repairable geometry. Refusal preserves previous good output. The repair removes exactly one zero-area triangle and retains the other. |
| IM-07 | missing_references; im07_missing_texture.glb | Preserve a missing-asset placeholder and all three object identities; distinguish that local asset error from the optional missing-normal warning. Keep original unresolved paths on save. A resource-level error does not imply dropping the entire scene. |
| IM-08 | roundtrip_documents | A neutral fixture-scene v1 document, canonical round-trip target, plus duplicate-id, unknown-property, duplicate-property and future-version refusals. Includes explicit later lifecycle acceptance targets. This is not the Toy board schema or a new production API. |
| IM-09 | module_with_markers | Original OBJ Hull plus JSON markers in a native right-handed Z-up frame. No UVs by design. Identity placement, no glTF conversion, two explicit up/outward vectors and collision reference module.obj#Hull. |
| IM-10 | im10_instances.glb; im10_other_asset.glb; clone_identity | Left/Right share one mesh and have distinct instance ids. A different asset reuses the display name SharedDisplay without reusing asset identity. The clone target remaps root/child references and retains the external outside reference. |
| IM-11 | im11_origin.glb | Local minimum corner [0,0,0], maximum [1,2,3], off-grid anchor [0.125,0.375,0.625], pivot [0.2,0,0.4], +90° about Y. Default free placement must not snap. Optional unit-grid results are separate expected values. |

Diagnostics in expected.json name severity, affected node/object/file and reason. Their wording pins the semantic report to test; an importer should preserve those facts if its presentation changes. `imports-with-warnings` includes incomplete resource scenes kept as placeholders; the scene-level retention policy is explicit.

For malformed GLB derivatives, the generator modifies its own freshly exported bytes deliberately. The invalid-index file contains index 65535 for a three-position primitive. The truncated file is eleven bytes shorter than its declared container length. The wrong-extension fixture is genuine self-authored GLB bytes with an .fbx name; it checks extension refusal, not FBX parsing.

## Generic frames and origins

- y_up_left_handed maps (x,y,z) to (-x,y,z), determinant -1. The right marker becomes [0.35,0.2,0.7]; flip triangle winding once.
- z_up_right_handed maps (x,y,z) to (-x,z,y), determinant +1. The right marker becomes [0.35,0.7,0.2]; no winding flip.
- Arrow bounds refer to mesh geometry, not the empty marker. Its signed volume before frame conversion is 0.047 cubic metres.
- IM-11 applies T(anchor) T(pivot) R_y(90°) T(-pivot). The resulting node translation is [-0.075,0.375,1.225]. World bounds are [-0.075,0.375,0.225] to [2.925,2.375,1.225], tolerance 1e-5.
- Optional unit-grid snapping changes the anchor only to [0,0,1], not the stored local geometry/pivot. Bounds then become [-0.2,0,0.6] to [2.8,2,1.6]. Free placement and round trips retain the off-grid values.

## Runtime-only bundles

A005 permits .glb/.bin/.png hand-ins plus this README, generator and expected.json; its generated_by mechanism covers other test-time names. The following named Python functions return `{relative_filename: bytes}` in memory. A consuming test writes them into its own disposable test directory. They do not write OBJ, scene JSON or an FBX name into this hand-in.

| Function | Returned members |
|---|---|
| generated_truncated(root) | truncated.glb, from root/im01_cube.glb |
| generated_wrong_extension(root) | unsupported.fbx, from root/im01_cube.glb |
| missing_references() | missing.scene.json |
| roundtrip_documents() | roundtrip.scene.json; duplicate_ids.scene.json; unknown_property.scene.json; duplicate_property.scene.json; future_version.scene.json |
| module_with_markers() | module.obj; markers.json |
| clone_identity() | source.group.json; expected_clone.group.json |

The `member` check identifies the intended malformed round-trip member. The successful round-trip case explicitly selects roundtrip.scene.json. These functions use standard Python; importing the module does not run Blender generation. Set PYTHONDONTWRITEBYTECODE when checking it in a read-only source tree.

The fixture-scene shape is `kind/version/objects`; objects carry stable id, asset path, position, optional rotation/scale, properties and references. It is test data for the forthcoming neutral scene/import consumer, not an interface change. Unknown properties are deliberate negative inputs. The clone pair is a before/expected-after fixture, not a clone implementation.

The lifecycle entries (dirty/revert/watch/save failure/conflict/undo/cancel) and future importer outcomes are **acceptance targets**. Numeric file facts, malformed conditions, reproducibility and canonical bundle data were checked for A005. A005 does not claim a future importer, UI draft-close workflow, clone operation or scene-session implementation has already passed those targets.

## Validate the hand-in

From a source checkout, run the existing gate with the root that holds fixtures/import:

```text
python tools/check_fixtures.py .
```

It prints OK. This gate checks matrix coverage and expected-results shape; it is not a glTF validator or a proof of importer behavior. The handoff report separately records the independent seeded file measurements and two-run hashes.
