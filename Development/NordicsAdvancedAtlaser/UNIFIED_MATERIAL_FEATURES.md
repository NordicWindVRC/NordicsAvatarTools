# Unified material — feature catalogue

**Status:** specification of the unified material, after two refinement passes; the code is
being rebuilt to match it. It merges the per-shader inventories into one feature set. Each
feature's full parameter specification is added when the first parser types it. Atlasing
concerns are left out on purpose; they get their own pass later.

**What changed since the first pass:**
- **Poiyomi 11 and Pro are included:** PBR lighting with sheen and glints, mask levels,
  contacts, screen-space lighting effects, shell fur, Pro deformations and geometry effects.
- **An evaluation order:** where every feature acts, from surface inputs to the final pixel.
  It replaces the loose "before or after lighting" note, and settles ordering questions such
  as Poiyomi's rim *application stage* and Toon Standard's lit matcap.
- **Layers merged from 13 to 7:**
  - detail, decal, recolour, back face and emission become one **surface layer** with targets;
  - matcap and cubemap become one **lookup layer**;
  - rim and iridescence become one **angle layer**.
- **Building blocks are specified:** parameters, units, defaults and formulas, as the first
  part of the parameter-level specification.
- **About 25 features** instead of about 35, plus 10 building blocks.

**Refactor plan:** [UNIFIED_MATERIAL_REFACTOR.md](UNIFIED_MATERIAL_REFACTOR.md).

**Inputs:**
- [VRChat Mobile](UNIFIED_MATERIAL_VRCHAT_MOBILE.md): Toon Lit, Standard Lite, Toon Standard.
- [Unity Standard](UNIFIED_MATERIAL_UNITY_STANDARD.md).
- [Mochie Uber](UNIFIED_MATERIAL_MOCHIE_UBER.md).
- [Poiyomi Toon 10 and 11, and Poiyomi Pro](UNIFIED_MATERIAL_POIYOMI.md), with both kinds of fur.

Mochie Standard was inventoried as a cross-check and then dropped in favour of Uber. Its
features are covered by the shaders above, apart from the world-only ones in section 7.

**How to read it:**
- Names are proposals in neutral wording, not code names.
- *Live* features need runtime support in the output shader. *Bakeable* ones can be resolved
  into textures when nothing animates them.
- The design rules are in [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md#guidelines).

---

## How a unified material is evaluated

Every feature acts at one stage. Writers implement the stages in this order; parsers decide which
stage a source's feature belongs to.

| Stage | What happens | Features |
|---|---|---|
| **S1 Surface inputs** | Base colour, alpha, normals and surface response inputs are built up | Base colour (2.1), surface layers targeting colour, alpha, normal or surface inputs (4.1), transition (4.2), parallax (2.5) |
| **S2 Lighting** | The lighting family shades the surface: diffuse, specular, reflections, sheen, glints, clear coat | Lighting (3), surface response (2.4), external and screen-space lighting (3.5, 3.6) |
| **S3 Light-dependent layers** | Layers that need the light: transmission through thin parts, rims that follow the light | Subsurface (4.5); lit angle and lookup layers |
| **S4 Overlays** | Layers added to the lit result, ignoring shadows | Angle, lookup and pattern layers with placement *overlay*; refraction (4.7) |
| **S5 Emission** | Everything that glows, added last so it's never shadowed | Surface layers targeting emission; emission amounts of other layers |
| **S6 Final colour** | Adjustments and tone mapping of the finished colour | Colour adjustment with target *final colour* (1.10) |
| **S7 Output** | Alpha modifiers, cutout, blending, visibility | Alpha (2.2), render (6) |

Geometry features (5) act before all of this, on the mesh and the extra passes.

**Placement (1.8)** is how a layer chooses its stage: *surface* = S1, *lit* = S3, *overlay* = S4,
*emission* = S5. Poiyomi's rim *application stage*, the "Unlit" toggles on matcaps, rims and
flipbooks, and Toon Standard's matcap (which changes the albedo, so it's lit) are all
placements.

---

## 1. Building blocks

Shared types that features are made of. Most source-specific detail lives here, which keeps the
feature count low. This pass gives each one its parameters; see 1.0 for units.

### 1.0 Conventions
| Quantity | Convention |
|---|---|
| Colour | Linear, HDR allowed (values above 1). Parsers convert sRGB-authored colours |
| Hue | Turns: 1 = a full revolution, whatever the source's units (radians, degrees, 0–1) |
| Angle | Degrees |
| Distance | Metres in world space, unless the parameter says *screen* |
| Strength, opacity, mask value | 0–1, linear |
| Surface roughness | Roughness (0 smooth, 1 rough), never smoothness |
| Saturation, contrast, brightness multipliers | 1 = unchanged |
| Choices | Neutral names, never a source's enum numbers |
| Time | Seconds; speeds per second |

### 1.1 Texture reference
| Parameter | Values | Default | Notes |
|---|---|---|---|
| texture | 2D, cube or array texture | — | |
| interpretation | colour, data, tangent normal, direction | colour | *direction* is a tangent-space direction map (fur), composited like a normal map |
| space | UV0–UV3, object position, world position, panosphere, polar, view (matcap), screen, triplanar | UV0 | triplanar has a base space: object or world |
| tiling, offset | 2 × float | (1,1), (0,0) | |
| rotation | degrees | 0 | around the tile centre |
| scroll | UV units per second | (0,0) | live |
| spin | degrees per second | 0 | live |
| sampling | default, stochastic, supersampled | default | stochastic hides tiling |
| placement | none, or placed: position, scale, rotation, side offsets, aspect, symmetry, tiled | none | decal placement; outside the placed area nothing is sampled |
| frames | none, or a sheet (columns, rows, count) or array; rate (frames per second) or fixed frame; range; crossfade | none | flipbooks and sprite-sheet decals; live unless fixed |

Shared mappings and per-UV-channel settings (Poiyomi's UV Settings) are folded into each texture
reference by the parser.

### 1.2 Scalar input
`value = remap(constant × channel(texture))`, or the constant alone without a texture.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| constant | float | per use | set to 1 when a source's texture replaces its slider |
| texture, channel | texture reference, R/G/B/A/luminance | none, R | |
| invert sample | bool | off | applied to the channel before the constant |
| remap | input range, output range, midpoint, contrast | identity | Uber's map filters, Poiyomi 11 mask levels |
| combine | multiply, strength around one | multiply | around one: `1 − (1 − channel) × constant` (occlusion) |
| invert result | bool | off | only for parser convenience (smoothness → roughness) |

Reflectance conventions resolve into the scalar: unified reflectance is the 0–1 control where
0.5 = 4% F0 (`F0 = 0.16 × r²`). Sources that store F0 directly are converted.

### 1.3 Colour input
`colour × texture`, or a **palette** reference (1.6) times the texture. Linear, HDR.

### 1.4 Mask
`value = levels(max(channel(texture), extra sources))`, then optionally inverted.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | texture channel, vertex colour channel, mask bus channel, constant | constant 1 | |
| extra sources | vertex colour channels | none | Poiyomi 11; combined by max (to verify against the source) |
| levels | input min/max, midpoint, output min/max, normalise | identity | midpoint 0.5 = linear |
| invert | bool | off | |
| strength | 0–1 | 1 | `lerp(1, value, strength)` |

### 1.5 Mask bus
Numbered channels shared by the whole material (Poiyomi: 16).
- **Producers:** textures, vertex colours, view conditions (back face, in mirror, in camera,
  camera distance), VRChat contacts (radius, falloff, position, which people), and features
  that write their result (decals, dissolve, light maps).
- **Combining:** a producer writes with a blend: replace, add, multiply, min, max, average,
  subtract, divide.
- **Consumers:** any mask (1.4).

### 1.6 Palette
Up to a few shared colours, each with hue, saturation and brightness adjustments, referenced
by colour inputs. Recolouring a palette entry recolours every feature that uses it.

### 1.7 Blend
`b` = what's below, `l` = the layer's value, `o` = opacity × mask.

| Blend | Result | Notes |
|---|---|---|
| replace | `lerp(b, l, o)` | covers *alpha*, *lerp*, *normal* and detail *replace* |
| multiply | `b × lerp(1, l, o)` | |
| multiply ×2 | `b × lerp(1, 2l, o)` in gamma terms | in linear space the factor is 4.595, matching Unity's detail maps |
| add | `b + l × o` | Poiyomi's *linear dodge* |
| subtract | `max(b − l × o, 0)` | |
| screen | `lerp(b, 1 − (1 − b)(1 − l), o)` | |
| overlay | `lerp(b, overlay(b, l), o)` | standard overlay |
| lighten, darken | `lerp(b, max(b, l), o)`, `lerp(b, min(b, l), o)` | |
| mixed | `lerp(b, b + b × l, o)` | Poiyomi |
| tint luminance | `lerp(b, luminance(b) × l, o)` | recolour regions (Toon Standard colour mask) |
| add to luminance | `lerp(b, min(luminance(b) + l, 1), o)` | |
| weighted | sum of several blends by weight | Poiyomi matcaps |

### 1.8 Placement
*surface* (S1), *lit* (S3), *overlay* (S4), *emission* (S5). Every layer has a placement and an
**emission amount**, which copies part of its result to S5 as well.

### 1.9 Modulator
A runtime value driving any numeric or colour parameter, addressed by path.

**Path syntax:** `feature[index].parameter`, for example `layers[3].opacity`,
`lighting.shadowStrength` or `fur.length`. Animation clips use the same paths, so to a writer
an animated value and a modulated value look the same.

| Source | Output 0–1 from | Parameters |
|---|---|---|
| time | a waveform | waveform (sine, square, triangle, saw, reverse saw, smooth blink, flashing blink), speed, phase, duty |
| audio | an AudioLink band | band (bass, low mids, high mids, highs, volume), smoothing, delay, fallback beat speed, spread |
| light level | world or mesh brightness | threshold, crossfade, include shadows |
| camera distance | distance to the camera | near, far |
| contact | a VRChat contact | contact index, radius, falloff |
| blacklight | a UV light in the world | key, range |
| spatial sweep | position along a direction or from the centre, over time | direction or centre-out, width, speed, interval, curve |
| noise | a noise field over UV or space | kind (texture, simplex, voronoi, curl, FBM, Gerstner), scale, speed |

- **Output:** `target = mode(target, lerp(min, max, value))`, mode *add*, *multiply* or
  *replace*.
- **Colour targets** use a colour ramp or a palette entry instead of min and max.
- A modulator whose target is a mapping (noise into UVs) is UV distortion.

**Audio spread:** how the audio value varies across the surface. The **coordinate** is a
0–1 position: along a UV direction, or out from a UV centre.

| Spread | Value at a point | Sources |
|---|---|---|
| none | the band's current level, everywhere | Toon Standard Pulse, most per-feature AudioLink |
| history | the band's level from `coordinate × length` ago, so recent audio travels along the coordinate; length in history samples | Toon Standard Scroll, Poiyomi centre-out emission |
| fill | 1 where `coordinate ≤ level`, else 0, with a softness | Toon Standard Bar |

Spread keeps one modulator type for whole-surface and travelling audio effects alike.

### 1.10 Colour adjustment
An ordered list of operations on a **target**, with a mask.

| Operation | Parameters | Notes |
|---|---|---|
| hue | amount (turns), method (HSV, Oklab, grey-axis rotation), shift or select (keep one hue range) | |
| saturation, brightness, contrast | multipliers, 1 = unchanged | brightness can also be an offset |
| gamma | exponent | |
| tint | colour input, mode multiply or perceptual (keeps lightness, takes hue) | |
| black and white | lightness, contrast, tint, highlight colour, select by colour | Poiyomi 11 |
| invert | bool | |
| gradient map | colour ramp, strength | |
| posterise | steps | |
| tone map | ACES or GT7, strength | final colour only |

- **Targets:** base colour, any single layer, emission, final colour (S6).
- Static adjustments on anything but the final colour are bakeable.

---

## 2. Surface

### 2.1 Base colour
Colour input; vertex colour multiply (colour, alpha; linear or sRGB vertex data). **Sources:**
all.

### 2.2 Alpha
- **Source:** base colour alpha, a mask, vertex alpha, or none; strength.
- **Modifiers** (live, S7): distance fade, view-angle fade, camera- or model-angle fade, audio.
- **Cutout handling:** threshold, sharpened coverage, mip preservation, dithering (soften edges
  or real transparency).
- **Sources:** Unity Standard, Uber, Poiyomi.

### 2.3 Normals
- **Layers:** the primary normal, then surface layers targeting the normal (4.1), composited
  by whiteout with strength and mask.
- **Bent normal:** an input for specular occlusion and indirect light.
- **Modifiers:** spherize toward a point, flat shading, back-face flip.
- **Sources:** all except Toon Lit.

### 2.4 Surface response
How the surface reflects light (S2).
- **Inputs:** metallic, roughness, reflectance, specular colour (its own input; parsers don't
  convert it to metallic); occlusion
  with strength, tint, direct and indirect amount; multi-bounce occlusion.
- **Highlights:** on, strength, tint; light-type limits;
  - **shape:** round, or stretched (anisotropy amount, rotation, direction map, two layers,
    strand variation);
  - **style:** smooth, or stepped (step count, size, feather);
  - an optional **second lobe**.
- **Reflections:** on, strength, tint, fresnel strength, roughness override; source scene
  probes or a fixed cubemap, lit or unlit.
- **Clear coat:** strength, roughness, tints, whether it follows the normal map.
- **Sheen:** strength, colour, roughness, mask; a soft edge lobe for cloth.
- **Glints:** density, size, colour and variation, roughness, mask; sparkling flakes in the
  specular response, as opposed to the glitter pattern layer.
- **Specular anti-aliasing:** strength, variance, threshold.
- **Sources:** Standard Lite, Toon Standard, Unity Standard, Uber, Poiyomi.

### 2.5 Parallax
Height scalar, depth, method (single step, multi-step with step counts), mask, offset. Shifts
every S1 texture lookup that uses the same mapping. Live. **Sources:** Unity Standard, Uber,
Poiyomi.

---

## 3. Lighting

### 3.1 Families
| Family | Look | Sources |
|---|---|---|
| **unlit** | base colour as is | Uber shading off, Poiyomi unlit intensity |
| **flat** | uniform light, no shading across the surface | Toon Lit, Poiyomi Flat |
| **ramp toon** | a shading term mapped through a ramp to brightness and shade colour | Toon Standard, Uber shadows, Poiyomi TextureRamp, Multilayer Math, ShadeMap, Wrapped, SDF |
| **physically based** | energy-conserving diffuse with a diffuse model, plus surface response | Standard Lite, Unity Standard, Uber, Poiyomi Realistic, Skin, Cloth, PBR |

### 3.2 Ramp toon
- **Shading term:** N·L (wrapped or half-Lambert), received shadows only, or an SDF face
  texture with forward and left directions.
- **Ramp:** one gradient. Ramp textures, band layers (border, blur), step and feather
  settings, two-width blends and wrap parameters all resolve into it. Optional named preset.
- **Shade colours:** per band, from a constant, the base colour, or a texture.
- **Shadow lift**, strength, painted shadow mask, border colour.

### 3.3 Physically based
**Diffuse model:** Lambert, Disney, skin (pre-integrated colour lookup, thickness), cloth (with
its own sheen and reflectance).

### 3.4 Shared lighting parameters
- **Ambient model:** flat average or directional.
- **Light direction override:** none, a fixed local or world direction, or the view direction.
- **Light colour override;** monochrome light.
- **Brightness ceiling** (per channel, or keeping the hue) and **floor**.
- **Received shadows** with softness.
- **Contribution per source:** main light, additional lights, vertex lights, ambient.

### 3.5 External lighting
VRChat light volumes (strength, specular, bias) and LTCGI (diffuse and specular tint,
strength). **Sources:** Uber, Poiyomi.

### 3.6 Screen-space lighting effects
Darkening from the depth texture, which avatars only have with a depth-providing prefab:
ambient occlusion (radius, intensity, colour, mask) and contact shadows (light source,
distance, thickness, colour, mask). **Sources:** Poiyomi Pro.

---

## 4. Layers

Seven layer types. All share: source, mask (1.4), blend (1.7) with opacity, placement (1.8)
with emission amount, colour adjustment (1.10) and modulators (1.9).

### 4.1 Surface layer
A colour or texture applied to one or more surface inputs. It covers what the sources call
detail, decal, flipbook, recolour or colour mask, back face, and emission.

| Part | Values |
|---|---|
| source | colour input (with placement and frames from 1.1), the base colour itself, or a palette entry |
| targets | one or more of: base colour, alpha, normal, metallic, roughness, occlusion, emission; each with its own blend and opacity |
| mask | any mask, including view conditions: back face, in mirror |

**How sources map:**
- **Detail:** a tiled source targeting colour (multiply ×2), normal, metallic, roughness,
  occlusion.
- **Decal or flipbook:** a placed source, optionally with frames.
- **Recolour region** (Toon Standard colour mask, Uber team colours, Poiyomi RGBA colour
  masking): a layer per mask channel, colour blend *tint luminance* or *add to luminance*,
  plus emission, normal and surface targets.
- **Back face:** a layer masked by the back-face condition.
- **Emission:** a layer targeting emission. "Use base colour" is the base colour as the
  source; "override base colour" adds a base colour target with blend *replace* to black;
  blinking, scrolling, centre-out and light-based glow are modulators.

**Bakeable** when static and placed in UV space. **Sources:** all except Toon Lit.

### 4.2 Transition
A second appearance revealed by a threshold.
- **Threshold source:** texture, noise, point to point, sphere, centre out; amount, softness.
- **Edge:** up to three colours, widths, emission.
- **Revealed appearance:** transparent, or a colour and texture.
- **Modes:** wave, continuous.

Structurally a surface layer whose mask is a threshold with an edge band. Kept separate
(decided) because its *amount* is the thing that gets animated. **Sources:** Uber base colour dissolve, Poiyomi
dissolve.

### 4.3 Lookup layer
A texture looked up by direction instead of by UV.
- **Texture:** 2D (matcap) or cube.
- **Projection:** view normal (VR-stabilised or not), top pinch, double-sided, gradient,
  light direction, reflection direction, world or local normal, skybox; rotation and spin for
  cubes.
- **Options:** blur or roughness, own normal map, base and light colour mix, hide in shadow,
  probe fallback.

**Sources:** Toon Standard, Uber, Poiyomi (matcaps, cubemap).

### 4.4 Angle layer
Colour driven by how the surface faces the viewer or the light.
- **Edge source:** view angle, depth (screen-space silhouette), intersection with other
  geometry.
- **Profile:** a band (width, softness, power, invert) for rims, or a full gradient over the
  angle (a ramp or hue rotation) for iridescence.
- **Colour source:** colour input, the environment (reflection probe), base colour mix.
- **Light response:** ignore light, scale by light, lit side only, a second colour on the dark
  side, back light (shines where the light is behind).

**Sources:** Toon Standard rim, Uber basic and environment rim and iridescence, Poiyomi rims,
depth rim, environmental rim, Depth FX, backlights.

### 4.5 Subsurface
Light through thin parts: thickness texture, colour, base colour tint, direct and indirect
strength, falloff, distortion, wrap, shadow strength. S3. **Sources:** Uber, Poiyomi.

### 4.6 Procedural pattern
A generated pattern with a **kind** and its parameters: glitter, voronoi, truchet, pathing,
internal parallax, video screen, audio spectrum, audio volume colour, oscilloscope,
constellation. **Sources:** Poiyomi, Poiyomi Pro, Uber oscilloscope.

One layer type (decided). Each kind gets its own typed parameters when a parser types it;
until then, a material using that kind gets an unmapped report line. Adding a kind never
changes the layer type.

### 4.7 Refraction
Bends what's behind a transparent surface: strength (IOR), tint, colour split, blur, opacity;
with screen-grab extras (pixelation, colour ramp). **Sources:** Uber, Poiyomi Pro Grab Pass and
Self Grab.

---

## 5. Geometry and extra passes

### 5.1 Outline
A **list** of outline layers (Poiyomi Pro has three), each with:
- **Shape:** normal expansion, drop shadow, directional offset; normals from vertex colours or
  baked curvature data.
- **Width:** world units with a mask; screen-size cap; fixed size over distance; fade over
  distance; edge fade; thicker on the lit or the dark side.
- **Colour:** colour input, base colour blend, lit or unlit, emission, an inner highlight line.
- **Masks per property** (size, colour, alpha, emission and so on).
- Its own render settings (6).

**Sources:** Toon Standard (Outline), Uber (Outline), Poiyomi, Poiyomi Pro.

### 5.2 Fur
- **Method:** strands or shells.
- **Shape:** length (metres) and length mask; direction (constant, direction map, vertex
  colours); gravity; randomness; tip taper.
- **Strands:** pattern texture, alpha mask, root occlusion; layer count; widths; distance level
  of detail.
- **Colour:** colour input, optionally also tinting the skin.
- **Contacts:** bends away from a VRChat contact, with a push strength.
- **Sway:** the wind deformation (5.3). **Rim:** an angle layer scoped to the fur.
- Its own render settings (6).

**Sources:** Poiyomi Lil Fur (strands), Poiyomi Pro Fur (shells). Typed in code already.

### 5.3 Deformation
A list of vertex operations, each with a mask and modulators:
- transform (translate, rotate, scale, spin); inflate along the normal; snap to a grid;
  displace by a height map;
- barrel, sphere and tornado distortion; wind (layers of direction, amplitude, speed,
  frequency); glitch;
- look at the camera; push toward the camera (view-clip prevention);
- squash where a contact presses; TPS shaping (bend, squeeze, bulge, pulse).

**Sources:** Uber, Poiyomi, Poiyomi Pro, and Lil Fur for wind.

Static operations (no modulator) stay deformation features like the animated ones. Whether a
writer applies them to the mesh at build time, so outputs without them keep the shape, is
decided later, with the writers.

### 5.4 Geometry effects
Effects that add or remove geometry: tessellation, dissolving into pieces, shattering,
particles from triangles. **Sources:** Poiyomi Pro variants.

---

## 6. Render

- **Surface mode:** opaque, cutout, dithered, fade (everything fades), transparent (glass:
  reflections stay), additive, soft additive, multiplicative, multiplicative ×2. Raw blend
  factors are kept only when they match none of these.
- **Per pass:** base, each outline layer and fur have cull, depth test and write, depth
  offset, stencil and colour mask.
- **Two-sided lighting**, **fog**.
- **Visibility:** in mirrors, cameras, normal view; an alternate mirror appearance; near-camera
  clipping with a mask; hide by UV tile; invisible.
- **Fallback shader:** what viewers with blocked shaders see; carried for writers.

---

## 7. Out of scope

- **World-only features:** parsers ignore them, with at most one info line per material when
  one is switched on: rain, puddles, world vertex wind, screen-space reflections,
  lightmap-based effects, AreaLit, Bakery.
- **Shaders:** Poiyomi Toon World, Poiyomi Pro World and World Fur; Beatsaber support (gone
  in 11).
- **Unavailable source:** Uber X. Materials using it get one unmapped info line.
- **Not visual:** stats overlay (live text such as FPS, position and a clock; one info line
  when switched on), debug views, editor state, optimiser properties.

---

## 8. Source coverage

✓ the source has it; ~ partly or as a special case; blank: not present.

| Unified feature | Toon Lit | Std Lite | Toon Std | Unity Std | Uber | Poiyomi | Poiyomi Pro adds |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| 1.1 Mapping beyond UV0/1, placement, frames | | | | | ~ | ✓ | |
| 1.5 Mask bus | | | | | | ✓ | |
| 1.6 Palette | | | | | | ✓ | |
| 1.9 Modulators | | | ~ | | ✓ | ✓ | |
| 1.10 Colour adjustment | | | ✓ | | ✓ | ✓ | GT7 |
| 2.1 Base colour | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 2.2 Alpha | | | | ✓ | ✓ | ✓ | |
| 2.3 Normals | | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 2.4 Surface response | | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 2.5 Parallax | | | | ✓ | ✓ | ✓ | |
| 3 Lighting: unlit | | | | | ✓ | ~ | |
| 3 Lighting: flat | ✓ | | | | | ✓ | |
| 3 Lighting: ramp toon | | | ✓ | | ✓ | ✓ | |
| 3 Lighting: physically based | | ✓ | | ✓ | ✓ | ✓ | |
| 3.5 External lighting | | | | | ✓ | ✓ | |
| 3.6 Screen-space lighting | | | | | | | ✓ |
| 4.1 Surface layer | | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 4.2 Transition | | | | | ✓ | ✓ | |
| 4.3 Lookup layer | | | ✓ | | ✓ | ✓ | |
| 4.4 Angle layer | | | ✓ | | ✓ | ✓ | |
| 4.5 Subsurface | | | | | ✓ | ✓ | |
| 4.6 Procedural pattern | | | | | ~ | ✓ | constellation |
| 4.7 Refraction | | | | | ✓ | | ✓ |
| 5.1 Outline | | | ~ | | ✓ | ✓ | layers, curvature |
| 5.2 Fur | | | | | | ✓ | shells |
| 5.3 Deformation | | | | | ✓ | ✓ | squish, TPS |
| 5.4 Geometry effects | | | | | | | ✓ |
| 6 Visibility | | | | | ✓ | ✓ | |

Single-source features, described generically so other shaders can map into them later: mask
bus, palette and the back-face condition (Poiyomi); screen-space lighting and geometry effects
(Poiyomi Pro).

---

## 9. What changes in the current code

| Today | Catalogue |
|---|---|
| 15 `AtlasLightingMode` values | 4 families with parameters (3) |
| 27 free-form semantic features | Typed features, building blocks and modulators |
| Fixed hue fields (`HueShift`, `DetailHueShift`, `EmissionHueShift`) | Colour adjustments with targets (1.10) |
| Separate detail, decal, colour mask and emission structures, each with its own blend fields | One surface layer type with targets (4.1) and one blend list (1.7) |
| Separate matcap and rim structures | Lookup layer (4.3) and angle layer (4.4) |
| AudioLink and emission pulse as dedicated structures | Modulators on any parameter (1.9) |
| `AtlasColorMaskLayer` with four fixed slots | Recolour regions as surface layers |
| Global masks as Poiyomi-shaped bindings | Mask bus (1.5) |
| Colours stored as sRGB | Linear (1.0) |
| `AtlasSurfaceMode` (Opaque, Cutout, Transparent, Premultiplied, Additive) | Surface modes named by look (6) |
| No outline, deformation or geometry effects; fur typed | 5.1, 5.3, 5.4; fur matches 5.2 |
| No defined order between features | Evaluation stages S1–S7 |

### 9.1 Typing the free-form semantic features

The parsers create 27 distinct free-form feature IDs (`AtlasSemanticFeature`); other IDs only
label missing-feature issues. Every one has a home here.

**What typing one involves:**
1. **A typed home:** a field in a feature or building block.
2. **Units and conventions** (1.0): the free-form values are raw source values, often source
   enum numbers. Converting them is the real work.
3. **Parser and test:** the parser writes the typed field, and a test checks the converted
   value.

| Free-form ID | Becomes |
|---|---|
| `surface.normal.secondary`, `surface.bent-normal` | Normals (2.3) |
| `surface.alpha-map`, `render.alpha` | Alpha (2.2) |
| `surface.specular`, `surface.reflections`, `surface.clearcoat` | Surface response (2.4), including sheen and glints |
| `surface.detail` | Surface layer targets (4.1) |
| `decal.options.*` | Surface layer with placement (1.1, 4.1) |
| `lighting.rim`, `lighting.rim.options.*`, `lighting.environmental-rim`, `lighting.backlight` | Angle layer (4.4) |
| `lighting.subsurface`, `lighting.skin-scattering` | Subsurface (4.5); skin diffuse model (3.3) |
| `lighting.external`, `lighting.light-volumes`, `lighting.ltcgi` | External lighting (3.5) |
| `render.options` | Render (6) |
| `lighting.light-data`, `lighting.additional-lights`, `lighting.shadows` | Lighting families and shared parameters (3); needs the families first |
| `emission.modulation`, `audio.reactive.modulation`, `audio.global`, `surface.proximity-color` | Modulators (1.9) |
| `color.adjustment` | Colour adjustment (1.10) |
| `surface.glitter` | Procedural pattern (4.6) |
| `geometry.fur` (Pro Fur extras) | Fur (5.2): strand height range, occlusion range, hue |

**Order,** following the dependencies:
1. Building blocks (1.1–1.10).
2. Surface layer, lookup layer and angle layer, replacing today's per-kind structures.
3. The parser-only moves.
4. Lighting families.
5. Modulators in the parsers.
6. Outline, deformation and procedural patterns.

**Afterwards:** `AtlasSemanticFeature` stays only for reporting. A source value with no typed
home becomes an *unmapped* report line, not stored data, so every report is a coverage gap.

---

## 10. Decisions so far

- Avatar shaders and features only; world-only features are ignored, not counted as unmapped.
- Mochie Standard is dropped in favour of Mochie Uber (done).
- Poiyomi 11 and Poiyomi Pro are supported (done).
- Parsing never requires the source shader to be installed; output shaders must be installed
  (done; see [*Shader availability*](UNIFIED_MATERIAL.md#shader-availability)).
- The layer merges of the second pass are accepted for now, to be refined once output writers
  exist and are tested.
- Coverage means the look; misunderstandings of a source are fixed in its parser, not in the
  unified material.
- **Specular colour** is its own surface response input next to metallic (2.4); parsers don't
  convert it.
- **Uber X** stays unsupported for now (7).
- **Static deformation** stays a deformation feature (5.3); applying it to the mesh is a later
  writer decision.
- **Light volumes and LTCGI** stay in scope as external lighting (3.5): they're common on
  avatars.
- **Transition** stays its own layer type (4.2).
- **Procedural patterns** are one layer type with a pattern kind (4.6).
- **Stats overlay** is out of scope (7).
- **Older versions are supported where their source is available.** That is every Poiyomi
  version from 7.3 to 11: the Toon packages ship the older versions under
  `_PoiyomiShaders/Shaders/<version>`. 8.1 to 9.3 share 96–99% of their property names with
  10.0; 7.3 shares about two thirds and needs the most parser work. Only the current Mochie
  Uber is on hand; older Uber sources can be obtained if a difference needs checking.

## 11. Open questions

None outside the atlasing pass.

**Deferred to the atlasing pass:** UV-tile features (AudioLink UV modes, UV tile discard),
vertex colour handling when merging, settings that can't differ within one material, and
per-material constants such as fur length.
