# Lighting data: portable values and a readable preview

Research snapshot 2026-10-31, A025 revision 1. Preserve glTF's photometric values and half-angle cones, keep ambient/environment data separate, and keep editor lighting as view state. This is a **MIXED** proposal from Khronos, Godot, Unity, Unreal and Blender, not a copy of one editor or an approved schema.

Sources are versioned observations: glTF 2.0 / ratified KHR_lights_punctual; Godot 4.4; Unity 6000.0 and HDRP 17.0; Unreal 5.6; Blender 4.5 APIs/manual, 4.2 preview/EEVEE references, and the World page whose current heading says 5.2 LTS. They are not claims about the newest release or this PC's installed defaults. HDRP's official .cn reference is 17.0.0; the .com @17.0 pages currently serve 17.0.4 guidance. Unknown creation defaults remain unknown.

## Comparison

**BORROWED observations.** In the tables, “unspecified” means the cited text does not establish that numeric default. API defaults are distinguished from startup-scene/operator presets. Common Godot fields are white colour and Energy 1; physical units are off initially, and Energy still multiplies physical intensity when enabled ([G1](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy), [G2](https://docs.godotengine.org/en/4.4/tutorials/3d/physical_light_and_camera_units.html#configure-the-light-nodes)). Blender 4.5 API common defaults include white, exposure 0, normalize true, custom distance false and stored cutoff 40 ([B1](https://docs.blender.org/api/4.5/bpy.types.Light.html#bpy.types.Light.normalize)); a stored cutoff is not necessarily active.

### Directional

| Reference | Fields / units / defaults | Falloff and range |
|---|---|---|
| glTF | type directional; colour, intensity; white/1 defaults, lux. [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) | Parallel emission, no distance attenuation. [K1](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property) |
| Godot | DirectionalLight3D orientation, colour, Energy; physical intensity in lux, numeric physical default unspecified. [G1](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy), [G3](https://docs.godotengine.org/en/4.4/classes/class_directionallight3d.html#description) | Parallel; origin ignored. |
| Unity Built-in / URP | Type, colour, Intensity, indirect multiplier; documented Intensity default 0.5. [U1](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Light.html), [U2](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/light-component.html) | Directional, no point range. Built-in units are arbitrary ([U3](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/convert-from-built-in-convert-lighting-and-shadows.html)); the URP reference lists scalar brightness, not a physical selector. |
| Unity HDRP | Physical directional intensity in lux; creation value unspecified. [U4](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Light-Component.html) | No distance attenuation. |
| Unreal | Lux; source angular size; documented Source Angle 0.5357 degrees. Intensity default unspecified. [E1](https://dev.epicgames.com/documentation/en-us/unreal-engine/directional-lights-in-unreal-engine?application_version=5.6), [E4](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-physical-lighting-units-in-unreal-engine?application_version=5.6) | Infinitely distant directional source. |
| Blender Sun | Energy API default 10 W/m²; angular diameter 0.00918043 radians. [B2](https://docs.blender.org/api/4.5/bpy.types.SunLight.html#bpy.types.SunLight.energy) | Constant directional illumination; position does not set brightness. [B5](https://docs.blender.org/UATEST/manual/en/4.5/render/lights/light_object.html) |

### Point / omni

| Reference | Fields / units / defaults | Falloff and cutoff |
|---|---|---|
| glTF | Point; intensity candela, default 1; optional positive range. [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) | Inverse-square, with finite range ending influence; absence means unbounded. [K1](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property) |
| Godot Omni | Range 5, attenuation 1; physical lumens plus Energy. [G1](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy), [G4](https://docs.godotengine.org/en/4.4/classes/class_omnilight3d.html#class-omnilight3d-property-omni-attenuation) | Configurable attenuation; documented inverse-square setting is 2. No light outside range. Enabling physical units alone does not promise that setting changes. |
| Unity Built-in / URP | Range, colour, scalar Intensity default 1; numeric Range default unspecified. [U1](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Light.html), [U2](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/light-component.html) | The Inspector references give a range control but do not specify an exact cross-pipeline attenuation equation. |
| Unity HDRP | lm/cd/lux/EV100 options; creation values unspecified. [U4](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Light-Component.html) | Inverse-square, zero contribution at Range. Physical scale assumes metres. [U5](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Physical-Light-Units.html) |
| Unreal | cd/lm/Unitless, Attenuation Radius, separate source radius/length; numeric defaults unspecified. [E2](https://dev.epicgames.com/documentation/en-us/unreal-engine/point-lights-in-unreal-engine?application_version=5.6), [E4](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-physical-lighting-units-in-unreal-engine?application_version=5.6) | Inverse-square option clamps at attenuation radius; disabling it exposes only Unitless and a falloff exponent. |
| Blender Point | Energy default 10 W radiant power, radius default 0. [B3](https://docs.blender.org/api/4.5/bpy.types.PointLight.html#bpy.types.PointLight.energy) | EEVEE inverse-square with a threshold-derived cutoff or custom distance; other renderer behavior is not inferred. [B6](https://docs.blender.org/manual/en/4.2/render/eevee/light_settings.html#custom-distance) |

### Spot

| Reference | Fields / units / defaults | Cone and distance model |
|---|---|---|
| glTF | candela; inner 0, outer π/4 radians; required spot object for a spot kind. [K3](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.spot.schema.json), [K1](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property) | Centre-to-edge half-angles, inner < outer ≤ π/2; smooth angular transition and local distance falloff. |
| Godot Spot | Physical lumens plus Energy; angle 45 degrees, angular attenuation 1, distance attenuation 1, range 5. [G1](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy), [G5](https://docs.godotengine.org/en/4.4/classes/class_spotlight3d.html#class-spotlight3d-property-spot-angle-attenuation) | Angle plus curve rather than two angles; distance 2 is inverse-square. The cited angle description does not settle full/half opening; an adapter must verify it. |
| Unity Built-in / URP | Local intensity default 1; Built-in Spot Angle, URP inner/outer controls; cone defaults unspecified. [U1](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Light.html), [U2](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/light-component.html) | Degrees; the APIs call it a cone angle, and inner angle is SRP-specific. [U7](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-spotAngle.html), [U8](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-innerSpotAngle.html) |
| Unity HDRP | Photometric local units, cone shape, inner/outer controls; creation defaults unspecified. [U4](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Light-Component.html) | Inverse-square local falloff with range cutoff. |
| Unreal | Inner/outer angles; physical page's default cone half-angle is 44 degrees; other defaults unspecified. [E3](https://dev.epicgames.com/documentation/en-us/unreal-engine/spot-lights-in-unreal-engine?application_version=5.6), [E4](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-physical-lighting-units-in-unreal-engine?application_version=5.6) | Full intensity inside inner cone, transition to outer; cd and lm respond differently to cone changes. |
| Blender Spot | Energy 10 W, size 0.785398 radians, blend 0.15, radius 0, square false: API defaults. [B4](https://docs.blender.org/api/4.5/bpy.types.SpotLight.html#bpy.types.SpotLight.spot_blend) | Size is full outer opening; Blend controls edge softness ([B5](https://docs.blender.org/UATEST/manual/en/4.5/render/lights/light_object.html)). Energy describes hypothetical unrestricted emission before the cone. EEVEE cutoff is separate. |

### Ambient / environment

| Reference | Representation and units | Defaults / distinction |
|---|---|---|
| glTF punctual extension | No ambient or environment light kind. [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) | It cannot carry the proposed ambient level. |
| Godot Environment | Ambient source/colour/energy/sky contribution; physical background luminance in nits. [G6](https://docs.godotengine.org/en/4.4/classes/class_environment.html#class-environment-property-background-intensity) | Source Background, ambient colour black, energy 1, sky contribution 1; the latter can override the ambient colour. Background intensity 30000 nits is not ambient lux. |
| Unity | Skybox/environment or ambient settings; HDRP sky Volume configuration. [U6](https://docs.unity3d.com/6000.0/Documentation/Manual/lighting-ambient-light.html), [U3](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/convert-from-built-in-convert-lighting-and-shadows.html) | No universal ambient intensity/default unit across pipelines. |
| Unreal Sky Light | Distant capture/cubemap, intensity scale; sky luminance is HDR pixel value × intensity in cd/m². [E5](https://dev.epicgames.com/documentation/en-us/unreal-engine/sky-lights-in-unreal-engine?application_version=5.6), [E4](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-physical-lighting-units-in-unreal-engine?application_version=5.6) | No universal constant ambient-lux value or numeric default established. |
| Blender World | Background colour/strength or sky/HDRI environment emission. [B8](https://docs.blender.org/manual/en/latest/render/lights/world.html#surface) | The cited World page does not give a universal numeric ambient strength or physical unit. |

## Units

**BORROWED:** candela is luminous intensity, lumen is flux, lux is illuminance; lm = cd·sr and lx = lm/m² ([N1](https://www.nist.gov/pml/special-publication-330/sp-330-section-2)). **INVENTED worked checks:** an isotropic 100 cd point has flux 4π×100 ≈ 1256.637 lm; at 2 m, normal-incidence illuminance without cutoff is 100/2² = 25 lux. For uniform intensity inside a hard cone of half-angle θ, flux = intensity × 2π(1−cos θ). At θ = 45 degrees and 100 cd, that is about 184.030 lm. This hard-cone calculation is not an exact integral for every softened spot.

**MIXED adapter rule:** a unit name is insufficient without its distribution convention. Godot's spot convention keeps apparent cone brightness unchanged while the cone varies ([G1](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy)); Blender's spot W is likewise hypothetical unrestricted power ([B4](https://docs.blender.org/api/4.5/bpy.types.SpotLight.html#bpy.types.SpotLight.spot_blend)). Unreal lm instead distributes flux into the affected solid angle; its cd is angle-independent ([E4](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-physical-lighting-units-in-unreal-engine?application_version=5.6)). Do not apply the hard-cone flux conversion to an absorbing-cone source by default. That Unreal page also documents its own 625 Unitless per cd relation; it is an engine convention, not a conversion for another game's relative units.

**BORROWED + inference:** W and W/m² are radiometric, not lm or lx. Photometric conversion weights the source spectrum by visual sensitivity ([N2](https://www.nist.gov/pml/sensor-science/optical-radiation/realization-candela)). The SI value 683 lm/W concerns monochromatic 540 THz radiation ([N1](https://www.nist.gov/pml/special-publication-330/sp-330-section-2)); it is not a general multiplier for white or coloured Blender lights. Retain native radiometric values and spectral/calibration assumptions in adapter metadata, or flag conversion as approximate.

**MIXED storage rule:** canonical physical punctual values use cd for point/spot and lx for directional. Keep finite binary64 values rather than narrowing to float32, linear RGB tint, metres, and radian half-angles. glTF's core fixes metre/radian conventions ([K6](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#coordinate-system-and-units)). Preserve original raw numeric tokens/field-presence and unknown extension payloads when exact source representation matters: binary64 alone does not preserve every decimal spelling. Semantic light round-trip and byte-identical whole-file round-trip are different promises.

**INVENTED relative mode:** a game may retain a nonnegative intensity with unit `relative` and a named project calibration/profile. A preview gain need not change it. glTF export must request an explicit cd-per-unit/lux-per-unit conversion, or report the light as unexportable. Never relabel relative 1 as physical 1. Similarly, temperature/exposure controls may bake into the exported RGB/value, but need retained authoring metadata to recover the original controls.

## Proposal

Every row is a proposal only. Claude owns the eventual contract. Defaults below apply to new canonical data; existing sample records need a versioned interpretation rather than silent relabelling.

| Choice / provenance | Neutral field / default | Meaning |
|---|---|---|
| BORROWED, glTF [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) | `kind`: directional, point or spot; explicit, no implicit kind | Selects applicable units and fields. |
| BORROWED, glTF [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) | `colorLinearRgb` = [1,1,1], channels 0..1 | White-referenced tint; no alpha. HDR native colours need an explicit conversion/preservation policy. |
| BORROWED, glTF units; MIXED with relative engines | `intensity` = 1; physical unit fixed by kind, or separately tagged relative mode | Nonnegative and finite. A UI may offer lm after applying the declared distribution conversion. |
| BORROWED, glTF [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) | `rangeMetres` = absent/null for point/spot | A supplied value is >0. Absence is unbounded; zero is not an infinity sentinel. Not applicable to directional. |
| BORROWED, glTF [K3](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.spot.schema.json) | `innerHalfAngleRadians` = 0; `outerHalfAngleRadians` = π/4 | Require 0 ≤ inner < outer ≤ π/2. Enforce the cross-field relation in validation, not only individual schema bounds. |
| MIXED, glTF nodes + current Scene | Transform belongs to the scene object; definition can be shared | Keep instance identity separate from light definition identity. |
| INVENTED | `enabled` = true | Editor convenience; disabled export needs an explicit zero-intensity/metadata policy. |
| MIXED, environment references; INVENTED simple first model | Separate scene ambient: linear RGB white, relative amount 0 | Uniform fill for the first preview. It is neither a punctual glTF light nor a complete HDRI/GI model. |
| MIXED, Godot/Blender/Unity view controls | Preview mode default Editor; exposure/gain and gizmo visibility are view state | No authored edit, dirty bit, undo step or journal entry on toggling. |
| INVENTED round-trip policy | Retain import provenance, original field presence, raw extras/extensions and shared-definition mapping | Diagnostic rather than silent loss when editing exceeds the portable subset. |

The [current Lights sample](../../samples/LightsPlugin/Lights.cs) has only `kind=light`, intensity with default 1 and a UI range fallback of 10 metres. Its “household bulb” help is not a physical unit contract, and AddLight does not initially write range. **MIXED migration implication:** keep existing values under their legacy relative profile until an explicit versioned migration defines type, units and range behavior. New glTF defaults must not silently reinterpret old sample data.

## Preview

### What the references actually offer

| Reference | Editor/view controls |
|---|---|
| Godot | Preview sun/environment fallback when scene nodes are absent; toolbar toggles and an action to create scene nodes. [G7](https://docs.godotengine.org/en/4.4/tutorials/3d/environment_and_post_processing.html#preview-environment-and-sun-low-priority) |
| Unity | Historical 2022.3 Lighting button turns Scene-view illumination on/off; 6000.0 draw/view modes affect the development view. Unlit excludes lighting; it is not itself an editor-light rig. [U9](https://docs.unity3d.com/2022.3/Documentation/Manual/ViewModes.html), [U10](https://docs.unity3d.com/6000.0/Documentation/Manual/ViewModes.html) |
| Unreal | Lit versus Unlit and separate light/GI/shadow show flags. These are inspection controls, not evidence of a saved fallback-light definition. [E6](https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-modes-in-unreal-engine?application_version=5.6), [E7](https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-show-flags-in-unreal-engine?application_version=5.6) |
| Blender | Solid: simplified Flat/Studio/MatCap. Material Preview: HDRI with independent Scene Lights/Scene World switches. Rendered: scene renderer. [B7](https://docs.blender.org/manual/en/4.2/editors/3dview/display/shading.html#material-preview) |

**MIXED first CPU preview:** retain the [current CPU viewport](../../src/Bed.Editor.ImGui/ViewportView.cs) and offer explicit Editor versus Scene mode. Editor uses a fixed readable rig; Scene uses authored punctual lights and the simple ambient record, including an honestly black unlit scene. Do not silently add fill in Scene mode. A separate Unlit diagnostic can show base colour without lighting. Shadowing, per-pixel PBR, image-based GI, source-size penumbrae and volumetrics remain later work under goal 16.

**INVENTED approximation:** shade each triangle at its centroid with its geometric world normal. Accumulate direct illumination using Lambert cosine and local inverse-square/range/spot factors. Directional lights are a cheap first case ([G8](https://docs.godotengine.org/en/4.4/tutorials/3d/lights_and_shadows.html#directional-light)). This is O(triangles × affecting lights); range culling and deterministic ordering are reasonable, but no performance budget has been measured.

**BORROWED falloff, own notation:** for distance r and finite cutoff R, use a near-edge taper such as `max(1−(r/R)^4,0)/r²`, with exactly zero at/beyond R; unbounded range uses 1/r². The Khronos recommendation permits edge smoothing ([K1](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property)). Bound the preview's r=0 singularity with a documented epsilon; never rewrite the stored position/intensity to fix it. For a spot, let c be the cosine between its outward axis and the light-to-surface direction. Interpolate c between cos(outer) and cos(inner), clamp 0..1 and square for a cheap reference-style angular term. Guard a vanishing cosine denominator numerically without changing the authored angles. This numerical shape is a preview choice, not a claim to match each engine's Blend/exponent exactly.

**INVENTED display rule:** with linear albedo ρ, ambient relative amount a and colour Ca, display a tone-mapped version of `ρ × [a Ca + (g/π) Σ E_rgb]`. E_rgb is a colour-scaled direct-illumination proxy and g is the view's fixed photometric-to-display gain. A relative-unit light uses the project's explicit preview calibration. Do not add raw unitless amounts to lux without that scale. Perform shading in linear space; apply display conversion/tone mapping at the end. Fixed exposure/ordering make headless comparisons meaningful; this is not a calibrated light meter.

**INVENTED proposed acceptance examples, not tests run by this card:** a 100 cd point at r=2 m and R=10 m gives 24.96 lux before cosine; at r=R gives zero. A 100-lux directional stays distance-independent. A spot is full inside inner, zero outside outer, and monotonic through the band. Colouring a point changes predictable pixels; Editor/Scene/Unlit switches change pixels without changing document text, dirty, history or journal. Same self-authored scene and fixed exposure should reproduce frames; do not compare against licensed/game art.

## glTF mapping

This is the proposed portable contract, not current importer support. Sources for property bounds/defaults are [K2](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json)/[K3](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.spot.schema.json); list/reference shape [K4](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/node.KHR_lights_punctual.schema.json)/[K5](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/glTF.KHR_lights_punctual.schema.json); pose/range behavior [K1](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property); coordinate units [K6](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#coordinate-system-and-units).

| glTF datum | Import / native meaning | Export / loss boundary |
|---|---|---|
| Root extension `lights`; node `light` index | Shared definition plus scene instances | Preserve valid indices/sharing and used extension declarations. Do not duplicate definitions by display name. Omit an empty root light array. |
| `type` | Explicit punctual kind | Preserve it; unsupported kinds get a diagnostic, not an invented fallback. Ambient is outside this extension. |
| `name` | Display label plus separate stable identity | Preserve optional presence; names need not be unique. |
| `color` | Native linear tint | Preserve values; no sRGB double conversion. Native HDR/negative colours are not portable as-is; never silently clamp. |
| `intensity` | Physical value in kind-specific units | Keep magnitude/unit. Relative, W-based or angle-normalised sources need the declared conversion; preserve native settings separately. |
| `range` | Optional positive metre cutoff | Export omission for unbounded range. Node scale must not multiply the value. A project unit conversion is a different operation. |
| `spot.innerConeAngle`, `spot.outerConeAngle` | Radian half-angles and two-angle fade | Preserve both and validate together. A one-angle/blend/exponent editor needs an adapter; matching an outer edge alone loses the fade. |
| Node transform | Position/orientation, hierarchy and instance mapping | Local emission is −Z. Use the node's inherited orientation and one project-basis conversion. The camera's +Z forward is not a reason to reverse the light. Keep original hierarchy/TRS if structural round-trip matters. |
| Native enabled, ambient/environment, source size, shadows, IES/projectors, temperature/exposure controls | Extra native authoring fields | No direct punctual mapping for these controls. Require declared baking/approximation or preserve namespaced metadata and report the loss of interoperability. |
| Unknown extras/extensions and field omission | Preserved import payload | Retention is not implementation. Required unsupported semantics must be rejected/reported; never claim rendering conformance merely because JSON survives. |

**MIXED transform implication:** derive each light's position and outward direction from its instance, then convert that point/direction with [ProjectFrame](../../src/Bed.Import/Frames.cs) once. Normalize direction; preserve cone/intensity/range independently of node scale. Negative/nonuniform transforms need explicit direction/hierarchy cases. Flattening instances loses structural information unless retained separately.

**Observed current gap:** [GltfImporter.cs](../../src/Bed.Import/GltfImporter.cs) returns meshes, markers, textures, material names, skins/joints and clips, with no light collection. Its supported-required-extension list is empty, so required KHR_lights_punctual is refused. Optional light-bearing nodes can appear merely as meshless markers; there is no returned intensity/cone/range data. This is source inspection, not a new import experiment. Adding support and optional-loss diagnostics belongs to a later implementation card; neither importer nor sample was changed here.

## Self-check

Citation registry below is numbered 1..41. Seed **250031**, Python `random.Random(250031).sample(range(1,42),20)`, selected: 41, 28, 22, 31, 14, 9, 12, 19, 34, 26, 25, 30, 37, 15, 16, 5, 7, 40, 24, 32. Reopened all twenty sources and checked their actual body text, not the query/metadata that echoes a searched phrase. Seventeen were direct primary-page reads; Blender B6/B3/B8 were rechecked in indexed official-page text after direct retrieval failed. This limitation also applies to other Blender pages noted in the source ledger. No screenshots were used to infer defaults.

| Draw | Citation | Exact deciding excerpt | Result |
|---|---|---|---|
| 1 | 41 / E7 | “Shows/hides the result of lighting from the Sky Light actor” | Sky illumination has a separate view flag. [source](https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-show-flags-in-unreal-engine?application_version=5.6) |
| 2 | 28 / U4 | “A Directional Light can only use Lux.” | HDRP directional intensity is lux. [source](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Light-Component.html) |
| 3 | 22 / B6 | “Specifies where light influence will be set to 0.” | EEVEE custom distance ends influence; it is renderer-specific. [source](https://docs.blender.org/manual/en/4.2/render/eevee/light_settings.html#custom-distance) |
| 4 | 31 / U7 | “The angle of the spot light's cone in degrees.” | The spot property is in degrees; this wording alone does not prove full versus half opening. [source](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-spotAngle.html) |
| 5 | 14 / G6 | “Luminance of background measured in nits (candela per square meter).” | Background luminance is nits, distinct from a scalar ambient level. [source](https://docs.godotengine.org/en/4.4/classes/class_environment.html#class-environment-property-background-intensity) |
| 6 | 9 / G1 | “The light's strength multiplier (this is not a physical unit).” | Energy is a multiplier, not a photometric value. [source](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy) |
| 7 | 12 / G4 | “Controls the distance attenuation function for omnilights.” | Distance falloff is configurable; adjacent text identifies attenuation 2 as inverse-square. [source](https://docs.godotengine.org/en/4.4/classes/class_omnilight3d.html#class-omnilight3d-property-omni-attenuation) |
| 8 | 19 / B3 | “in units of radiant power (W)” | Point energy is radiant power; default 10 is an API property default. [source](https://docs.blender.org/api/4.5/bpy.types.PointLight.html#bpy.types.PointLight.energy) |
| 9 | 34 / U10 | “have no effect on the built game” | View controls do not rewrite the built game's lighting. [source](https://docs.unity3d.com/6000.0/Documentation/Manual/ViewModes.html) |
| 10 | 26 / U2 | “The default value for a Directional light is 0.5.” | URP documents directional default 0.5 and local default 1, without a physical selector here. [source](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/light-component.html) |
| 11 | 25 / U1 | “The default value for a Directional light is 0.5.” | Built-in Inspector defaults are not HDRP defaults. [source](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Light.html) |
| 12 | 30 / U6 | “doesn’t come from any specific source object” | Ambient/environment illumination is not a punctual source. [source](https://docs.unity3d.com/6000.0/Documentation/Manual/lighting-ambient-light.html) |
| 13 | 37 / E3 | “Within the Inner Cone Angle, the light achieves full brightness.” | Full inner region and softened outer region are distinct. [source](https://dev.epicgames.com/documentation/en-us/unreal-engine/spot-lights-in-unreal-engine?application_version=5.6) |
| 14 | 15 / G7 | “This can be disabled using the buttons” | Preview sun/environment is an editor fallback that can be disabled. [source](https://docs.godotengine.org/en/4.4/tutorials/3d/environment_and_post_processing.html#preview-environment-and-sun-low-priority) |
| 15 | 16 / G8 | “It is also the cheapest light to compute” | Directional computation is cheap; this does not include shadow costs. [source](https://docs.godotengine.org/en/4.4/tutorials/3d/lights_and_shadows.html#directional-light) |
| 16 | 5 / K5 | “"minItems": 1” | A present root light list cannot be empty. [source](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/glTF.KHR_lights_punctual.schema.json) |
| 17 | 7 / N1 | “The candela, symbol cd, is the SI unit of luminous intensity” | Candela measures luminous intensity, not total flux. [source](https://www.nist.gov/pml/special-publication-330/sp-330-section-2) |
| 18 | 40 / E6 | “Unlit view mode removes all lighting from the scene” | Unlit removes lighting, rather than substituting a scene-light rig. [source](https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-modes-in-unreal-engine?application_version=5.6) |
| 19 | 24 / B8 | “The surface shader defines the light emission” | Environment emission is described through World, separate from punctual lights. [source](https://docs.blender.org/manual/en/latest/render/lights/world.html#surface) |
| 20 | 32 / U8 | “This only works with a Scriptable Render Pipeline.” | An inner cone is pipeline-dependent in Unity. [source](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-innerSpotAngle.html) |

20/20 supported the scoped claims. Additional mandatory interchange/unit checks reopened K1/K2/K3/K4/K6 and N2, so the random draw did not leave glTF's central contract unchecked:

| Citation | Exact deciding excerpt | Source |
|---|---|---|
| K1 | “rendering engines ignore the light beyond this range.” | [Range Property](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property) |
| K2 | “"default": [1, 1, 1]” | [properties.color/intensity/range/type](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) |
| K3 | “Angle in radians from centre of spotlight where falloff begins.” | [properties.innerConeAngle](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.spot.schema.json) |
| K4 | “The id of the light referenced by this node.” | [properties.light](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/node.KHR_lights_punctual.schema.json) |
| K6 | “The units for all linear distances are meters.” | [Coordinate System and Units](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#coordinate-system-and-units) |
| N2 | “the maximum spectral luminous efficacy of radiation for photopic vision.” | [Photometric/radiometric relation](https://www.nist.gov/pml/sensor-science/optical-radiation/realization-candela) |

These additional six checks also passed. Verified API default fields separately for Blender Light/Sun/Spot, and Godot's physical-unit and attenuation distinction. Unspecified physical/creation defaults and unresolved Godot spot angle conventions remain explicitly unspecified rather than guessed. Quotes are brief excerpts; several do not comprise an entire sentence. All examples/proposals and arithmetic are self-authored, with no implementation code or art copied.

### Citation registry

| 1 / K1 | [K1: Range Property](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual#range-property) |
| 2 / K2 | [K2: properties.color/intensity/range/type](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json) |
| 3 / K3 | [K3: properties.innerConeAngle](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.spot.schema.json) |
| 4 / K4 | [K4: properties.light](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/node.KHR_lights_punctual.schema.json) |
| 5 / K5 | [K5: properties.lights](https://raw.githubusercontent.com/KhronosGroup/glTF/main/extensions/2.0/Khronos/KHR_lights_punctual/schema/glTF.KHR_lights_punctual.schema.json) |
| 6 / K6 | [K6: Coordinate System and Units](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#coordinate-system-and-units) |
| 7 / N1 | [N1: The candela](https://www.nist.gov/pml/special-publication-330/sp-330-section-2) |
| 8 / N2 | [N2: Photometric/radiometric relation](https://www.nist.gov/pml/sensor-science/optical-radiation/realization-candela) |
| 9 / G1 | [G1: light_energy](https://docs.godotengine.org/en/4.4/classes/class_light3d.html#class-light3d-property-light-energy) |
| 10 / G2 | [G2: Physical units setting](https://docs.godotengine.org/en/4.4/tutorials/3d/physical_light_and_camera_units.html#configure-the-light-nodes) |
| 11 / G3 | [G3: Description](https://docs.godotengine.org/en/4.4/classes/class_directionallight3d.html#description) |
| 12 / G4 | [G4: omni_attenuation](https://docs.godotengine.org/en/4.4/classes/class_omnilight3d.html#class-omnilight3d-property-omni-attenuation) |
| 13 / G5 | [G5: spot_angle_attenuation](https://docs.godotengine.org/en/4.4/classes/class_spotlight3d.html#class-spotlight3d-property-spot-angle-attenuation) |
| 14 / G6 | [G6: background_intensity](https://docs.godotengine.org/en/4.4/classes/class_environment.html#class-environment-property-background-intensity) |
| 15 / G7 | [G7: Preview environment and sun](https://docs.godotengine.org/en/4.4/tutorials/3d/environment_and_post_processing.html#preview-environment-and-sun-low-priority) |
| 16 / G8 | [G8: Directional light](https://docs.godotengine.org/en/4.4/tutorials/3d/lights_and_shadows.html#directional-light) |
| 17 / B1 | [B1: normalize](https://docs.blender.org/api/4.5/bpy.types.Light.html#bpy.types.Light.normalize) |
| 18 / B2 | [B2: energy](https://docs.blender.org/api/4.5/bpy.types.SunLight.html#bpy.types.SunLight.energy) |
| 19 / B3 | [B3: energy](https://docs.blender.org/api/4.5/bpy.types.PointLight.html#bpy.types.PointLight.energy) |
| 20 / B4 | [B4: spot_blend](https://docs.blender.org/api/4.5/bpy.types.SpotLight.html#bpy.types.SpotLight.spot_blend) |
| 21 / B5 | [B5: Common Settings / Beam/Spot Shape / Sun Light](https://docs.blender.org/UATEST/manual/en/4.5/render/lights/light_object.html) |
| 22 / B6 | [B6: Custom Distance](https://docs.blender.org/manual/en/4.2/render/eevee/light_settings.html#custom-distance) |
| 23 / B7 | [B7: Material Preview](https://docs.blender.org/manual/en/4.2/editors/3dview/display/shading.html#material-preview) |
| 24 / B8 | [B8: Surface (heading 5.2 LTS)](https://docs.blender.org/manual/en/latest/render/lights/world.html#surface) |
| 25 / U1 | [U1: Intensity](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Light.html) |
| 26 / U2 | [U2: Emission / Intensity](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/light-component.html) |
| 27 / U3 | [U3: Conversion introduction](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/convert-from-built-in-convert-lighting-and-shadows.html) |
| 28 / U4 | [U4: Emission / Shared Properties / Intensity (17.0.0)](https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Light-Component.html) |
| 29 / U5 | [U5: Physical units scale](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Physical-Light-Units.html) |
| 30 / U6 | [U6: Ambient introduction](https://docs.unity3d.com/6000.0/Documentation/Manual/lighting-ambient-light.html) |
| 31 / U7 | [U7: Description](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-spotAngle.html) |
| 32 / U8 | [U8: Description](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-innerSpotAngle.html) |
| 33 / U9 | [U9: Lighting switch](https://docs.unity3d.com/2022.3/Documentation/Manual/ViewModes.html) |
| 34 / U10 | [U10: Draw Modes introduction](https://docs.unity3d.com/6000.0/Documentation/Manual/ViewModes.html) |
| 35 / E1 | [E1: Source Angle](https://dev.epicgames.com/documentation/en-us/unreal-engine/directional-lights-in-unreal-engine?application_version=5.6) |
| 36 / E2 | [E2: Attenuation Radius](https://dev.epicgames.com/documentation/en-us/unreal-engine/point-lights-in-unreal-engine?application_version=5.6) |
| 37 / E3 | [E3: Spot cone introduction](https://dev.epicgames.com/documentation/en-us/unreal-engine/spot-lights-in-unreal-engine?application_version=5.6) |
| 38 / E4 | [E4: Point, Spot and Rect Lights](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-physical-lighting-units-in-unreal-engine?application_version=5.6) |
| 39 / E5 | [E5: Sky light introduction](https://dev.epicgames.com/documentation/en-us/unreal-engine/sky-lights-in-unreal-engine?application_version=5.6) |
| 40 / E6 | [E6: Unlit](https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-modes-in-unreal-engine?application_version=5.6) |
| 41 / E7 | [E7: Light Types / Sky Lighting](https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-show-flags-in-unreal-engine?application_version=5.6) |
