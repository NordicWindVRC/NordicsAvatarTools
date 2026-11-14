# Unified material — feature catalogue

**Status:** first refinement pass, for review. This merges the per-shader inventories into one
proposed feature set for the unified material, the target of the refactoring. Atlasing concerns
are left out on purpose; they get their own pass later.

**Inputs:**
- [VRChat Mobile](UNIFIED_MATERIAL_VRCHAT_MOBILE.md): Toon Lit, Standard Lite, Toon Standard.
- [Unity Standard](UNIFIED_MATERIAL_UNITY_STANDARD.md).
- [Mochie Uber](UNIFIED_MATERIAL_MOCHIE_UBER.md).
- [Poiyomi Toon 10](UNIFIED_MATERIAL_POIYOMI.md), with Lil Fur.
- [Mochie Standard](UNIFIED_MATERIAL_MOCHIE_STANDARD.md) is used only as a cross-check: it's to be
  dropped in favour of Uber.

**How to read it:**
- Names are proposals in neutral wording, not code names.
- Each feature says what it is, its parameters, and which sources map into it.
- *Live* features need runtime support in the output shader; *bakeable* ones can be resolved into
  textures when nothing animates them.
- The design rules are in [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md#guidelines); this catalogue
  follows them.

**Result in numbers:** about 35 features and 10 shared building blocks cover everything
avatar-relevant in the five inventories. The current code has about 25 typed structures,
15 lighting modes and about 40 free-form semantic features.

---

## 1. Building blocks

Shared types that features are made of. Getting these right is what keeps the feature count
low: most source-specific detail lives in a building block, not in a feature.

### 1.1 Texture reference
A texture plus how it's read.
- **Texture** and **interpretation:** colour, data, tangent normal, or direction (fur).
- **Mapping:**
  - **space:** UV0–UV3, object position, world position, panosphere, polar, view (matcap),
    screen, triplanar;
  - **transform:** tiling, offset, rotation;
  - **motion:** scroll, spin;
  - **sampling:** default, stochastic, supersampled.
- **Shared mappings:** several textures can share one mapping, as Unity Standard's main maps
  do. The parser resolves inherited tilings, so writers never see source rules.
- **Per-channel settings,** such as Poiyomi's UV Settings, fold into each texture that uses
  that channel.

### 1.2 Scalar input
A number that may come from a texture: constant × channel, with:
- invert;
- **remap** (min/max range, lightness, intensity, contrast): Uber's map filters, and
  conventions like Toon Standard's 0.16·r² reflectance, are resolved into this;
- a combine mode: multiply, or strength around one (occlusion).

The parser sets the constant to 1 where a source's texture replaces its slider.

### 1.3 Colour input
A colour times an optional texture, or a reference to a **palette** entry (1.6). Linear
colour space, HDR allowed.

### 1.4 Mask
A channel reference: from any texture, a vertex colour channel, or a **mask bus** channel
(1.5). Plus invert, remap and its own mapping. Packed and separate mask textures differ only
in which texture and channel the parser points at.

### 1.5 Mask bus
Numbered channels shared by the whole material. Sources: Poiyomi's 16 global mask channels.
- **Producers:** textures, vertex colours, view conditions (back face, in mirror, in camera,
  distance), and features that write their result: decals, dissolve, light maps.
- **Consumers:** any feature's mask.

### 1.6 Palette
A few shared colours, each with optional adjustments, that features reference instead of
copying. Sources: Poiyomi's theme colours.

### 1.7 Blend
One shared list for every layer:

| Blend | Notes |
|---|---|
| replace (lerp) | covers "alpha", "lerp", detail *replace* mask mode |
| multiply | |
| multiply ×2 | detail convention |
| add | |
| subtract | |
| screen | |
| overlay | |
| lighten, darken | Poiyomi decals |
| mixed | Poiyomi's mixed mode |
| **weighted** | several blends at once by weight; Poiyomi matcaps |

Each layer also has an **opacity** and a **mask**.

### 1.8 Placement
Where a layer's result goes:
- **before lighting:** changes the surface colour, so it's lit and shadowed;
- **after lighting:** added to the lit colour, ignoring shadows;
- **emission:** glows.

The "Unlit" toggles on matcaps, rims, flipbooks and refraction choose between the first two.
Many layers also have an **emission amount** that sends part of the result to emission.

### 1.9 Modulator
A value that changes at runtime and drives any numeric parameter of any feature, addressed by
path (for example `emission[1].strength`).

| Source | Parameters | Sources |
|---|---|---|
| time | waveform (sine, square, triangle, saw, reverse saw, smooth or flashing blink), speed, phase, duty | Uber pulse, Poiyomi blink, auto hue, scroll |
| audio | band (bass, low mids, high mids, highs), smoothing, delay, fallback beat | Toon Standard, Uber, Poiyomi |
| light level | world or mesh brightness, threshold, crossfade, include shadows | Uber light reactivity, Poiyomi light-based emission |
| camera distance | near, far | Poiyomi proximity colour, distance fades |
| blacklight | key, range | Poiyomi |
| spatial sweep | direction or centre-out, width, speed, interval, optional curve | Poiyomi scrolling and centre-out emission |
| noise | texture, simplex, voronoi, curl, FBM, Gerstner; scale, speed | Uber and Poiyomi UV distortion |

- **Output:** a remap to min and max, and a **mode:** add, multiply, or replace the parameter.
- **Colour targets** take a colour ramp or palette entry instead of a number: Poiyomi's
  "Color Override Ramp".
- **Animation** uses the same parameter paths, so animated values and modulated values look
  the same to a writer.

### 1.10 Colour adjustment
A set of operations applied to a **target**:
- **Operations:** hue (method: HSV, Oklab, grey-axis rotation; *shift* or *select*),
  saturation, brightness, contrast, gamma, tint, invert, gradient map, posterise, tone map
  (ACES).
- **Targets:** base colour, detail, emission, any single layer, final colour.
- **Mask.**
- Static adjustments on anything except the final colour are bakeable.

---

## 2. Surface

### 2.1 Base colour
Colour input, optional vertex colour multiply (colour, alpha, linear or sRGB), alpha source.
**Sources:** all.

### 2.2 Alpha
- **Source:** base colour alpha, a mask, vertex alpha, or none.
- **Strength.**
- **Modifiers** (live): distance fade, view-angle (fresnel) fade, camera- or model-angle fade,
  audio.
- **Cutout handling:** threshold, sharpened coverage, mip preservation.
- **Sources:** Unity Standard, Uber, Poiyomi, Mochie Standard.

### 2.3 Normals
- **Layers:** primary, extra (Poiyomi's second), detail, each with strength, mask and
  mapping. Composited by whiteout.
- **Bent normal:** an input for specular occlusion and indirect light.
- **Modifiers:** spherize toward a point (Poiyomi normal correct), flat shading (Uber hard
  edges), back-face flip.
- **Sources:** all except Toon Lit.

### 2.4 Surface response
How the surface reflects light.
- **Workflow:** metallic, or specular colour. Kept as two inputs; see open question 1.
- **Inputs:** metallic, roughness, reflectance (F0 for non-metals), specular colour,
  occlusion (strength, **tint**, direct and indirect amount).
- **Highlights:** on, strength, tint;
  - **shape:** round, or stretched (anisotropy amount and direction map, two layers, strand
    variation);
  - **style:** smooth, or stepped (step count, size, feather);
  - optional **second lobe**; light-type limits (real-time only).
- **Reflections:** on, strength, tint, fresnel strength, roughness override, **source**
  (scene probes or a fixed cubemap, lit or unlit).
- **Clear coat:** a second reflective layer with strength, smoothness, tints, and whether it
  follows the normal map.
- **Specular anti-aliasing:** strength, variance, threshold.
- **Sources:** Standard Lite, Toon Standard, Unity Standard, Uber, Poiyomi (Reflections &
  Specular, Clear Coat, Anisotropics, Stylized Reflections).

### 2.5 Parallax
Height texture, depth, method (single step, multi-step with step counts, cone stepping), mask,
offset. Live. **Sources:** Unity Standard, Uber, Poiyomi, Mochie Standard.

---

## 3. Lighting

### 3.1 Families
| Family | Look | Sources |
|---|---|---|
| **unlit** | base colour as is | Uber shading off, Poiyomi unlit intensity |
| **flat** | uniform light, no shading across the surface | Toon Lit, Poiyomi Flat |
| **ramp toon** | a shading term mapped through a ramp to brightness and shade colour | Toon Standard, Uber shadows, Poiyomi TextureRamp, Multilayer Math, ShadeMap, Wrapped, SDF |
| **physically based** | energy-based diffuse with a diffuse model | Standard Lite, Unity Standard, Mochie, Poiyomi Realistic, Skin, Cloth |

### 3.2 Ramp toon
- **Shading term:** N·L (wrapped or half-Lambert), received shadows only, or an SDF face
  texture (with forward and left directions).
- **Ramp:** one gradient representation. Ramp textures, band layers (border, blur), step and
  feather settings and wrap parameters are all resolved into it. Optional named presets, so a
  writer can use its own equivalent (Toon Standard's ramp presets).
- **Shade colours:** per band, from a constant, the base colour (shadow tint by albedo), or a
  texture (shade maps).
- **Shadow lift**, shadow strength, painted shadow mask, border colour.

### 3.3 Physically based
**Diffuse model:** Lambert, Disney, skin (pre-integrated colour lookup, thickness), cloth
(sheen, its own reflectance).

### 3.4 Shared lighting parameters
- **Ambient model:** flat average or directional (full light probes).
- **Light direction override:** none, a fixed local or world direction, or the view direction.
- **Light colour override;** monochrome light.
- **Brightness ceiling and floor** (limit brightness, colour preservation, minimum
  brightness).
- **Received shadows** with softness.
- **Contribution per source:** main light, additional lights, vertex lights, ambient.

### 3.5 External lighting
On/off and strength per system: VRChat light volumes (with specular and bias), LTCGI (diffuse
and specular tint). **Sources:** Uber, Poiyomi, Mochie Standard.

---

## 4. Layers

All layers share: blend (1.7), opacity, mask (1.4), placement (1.8), emission amount,
colour adjustment (1.10), modulators (1.9).

| Layer | What it is | Specific parameters | Sources |
|---|---|---|---|
| **Detail** | Detail on any surface input | target (base colour, normal, metallic, roughness, occlusion), texture, strength | Unity Standard, Standard Lite, Toon Standard, Uber, Poiyomi |
| **Recolour** | Recolour regions picked by mask channels | per region: colour, texture, normal, emission, surface overrides; recolour method (tint the greyscale, add to it, replace) | Toon Standard colour mask, Uber colour masking, Poiyomi RGBA colour masking |
| **Decal** | A placed image, optionally animated | placement (position, rotation, scale, side offset, aspect, symmetry, tiled), frames (texture array or sheet, rate or fixed frame, range, crossfade), video source, chromatic aberration | Poiyomi decals and flipbooks, Uber flipbook |
| **Transition** | A second appearance revealed by a threshold | threshold source (texture, noise, point to point, sphere, centre out), amount, softness, edge (colours, widths, emission), revealed appearance (transparent, or colour and texture), wave or continuous mode | Uber base colour dissolve, Poiyomi dissolve |
| **Matcap** | View-based texture lookup | projection (view, view stabilised in VR, top pinch, double-sided, gradient, light direction), blur, own normal map, base and light colour mix, hide in shadow | Toon Standard, Uber, Poiyomi |
| **Cubemap** | Colour from a cubemap | projection (reflection, skybox, world or local normal), rotation and spin, smoothness | Uber, Poiyomi |
| **Rim** | A band along edges | **edge source** (view angle, depth, intersection), width, softness, power, **colour source** (constant, texture, environment, base colour mix), **light response** (light-scaled, lit side only, second colour on the unlit side), invert | Toon Standard, Uber basic and environment rim, Poiyomi rims, depth rim, environmental rim, Depth FX, backlight, fur rim |
| **Emission** | Glow | colour input, strength, base colour as the source, override base colour | all except Toon Lit |
| **Iridescence** | Colour by view angle | ramp or hue rotation, width, sharpness | Uber |
| **Subsurface** | Light through thin parts | thickness texture, colour, base colour tint, direct and indirect strength, falloff, distortion, wrap, shadow strength | Uber, Poiyomi, Mochie Standard |
| **Back face** | Different appearance on back faces | colour, texture, emission, hue, detail amount, alpha | Poiyomi |
| **Procedural pattern** | Generated patterns | kind (glitter, voronoi, truchet, pathing, internal parallax, video screen, audio spectrum, audio volume colour, oscilloscope) and its parameters | Poiyomi, Uber oscilloscope; see open question 2 |
| **Refraction** | Bends what's behind | IOR, tint, colour split, blur, opacity | Uber; probably Poiyomi Grab Pass (that variant wasn't inventoried) |

---

## 5. Geometry and extra passes

### 5.1 Outline
- **Shape:** normal expansion, drop shadow, directional offset.
- **Width:** in world units, with a mask; a screen-size cap; fixed size over distance; fade
  over distance.
- **Colour:** colour input, base colour blend, lit or unlit, emission, an inner highlight line.
- **Normals from vertex colours.**
- **Sources:** Toon Standard (Outline), Uber (Outline), Poiyomi.

### 5.2 Fur
- **Shape:** length (world units) and length mask; direction (map, vertex colour, constant);
  gravity; randomness.
- **Strands:** noise texture, alpha mask, root occlusion; layer count, widths.
- **Fur sway:** uses the wind deformation from 5.3.
- **Fur rim:** a rim layer scoped to the fur.
- **Sources:** Poiyomi Lil Fur.

### 5.3 Deformation
A list of vertex operations, each with a mask and modulators:
- transform (translate, rotate, scale, spin);
- inflate along the normal;
- snap to a grid;
- displace by a height map;
- barrel, sphere and tornado distortion;
- wind (layers of direction, amplitude, speed, frequency);
- look at the camera;
- glitch;
- push toward the camera (view-clip prevention).

**Sources:** Uber, Poiyomi, and Lil Fur for wind.

---

## 6. Render

- **Surface mode:** opaque, cutout, dithered, fade (everything fades), transparent (glass:
  reflections stay), additive, soft additive, multiplicative, multiplicative ×2. Named by
  look. Raw blend factors are kept only when they match none of these.
- **Per pass:** base, outline and fur each have cull, depth test and write, depth offset,
  stencil and colour mask.
- **Two-sided lighting:** flip back-face normals.
- **Fog:** on or off.
- **Visibility:** in mirrors, in cameras, in normal view; an alternate mirror appearance;
  near-camera clipping with a mask; hide by UV tile; invisible.
- **Fallback shader:** what viewers with blocked shaders see. Not part of the look, but carried
  for writers.

---

## 7. Out of scope

- **World-only features:** see the Mochie Standard draft. Rain, puddles, world vertex wind,
  screen-space reflections, lightmap-based effects, AreaLit, Bakery.
- **Shaders:** Poiyomi Toon World; Beatsaber support.
- **Unavailable source:** Poiyomi Pro, Uber X. Materials using them get one unmapped info
  line.
- **Not visual:** stats overlay (proposed), debug views, editor state, optimiser properties.

---

## 8. Source coverage

✓ the source has it; ~ partly or as a special case; blank: not present.

| Unified feature | Toon Lit | Std Lite | Toon Std | Unity Std | Uber | Poiyomi |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| 1.1 Mapping spaces beyond UV0/1 | | | | | ~ | ✓ |
| 1.5 Mask bus | | | | | | ✓ |
| 1.6 Palette | | | | | | ✓ |
| 1.9 Modulators | | | ~ | | ✓ | ✓ |
| 1.10 Colour adjustment | | | ✓ | | ✓ | ✓ |
| 2.1 Base colour | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| 2.2 Alpha | | | | ✓ | ✓ | ✓ |
| 2.3 Normals | | ✓ | ✓ | ✓ | ✓ | ✓ |
| 2.4 Surface response | | ✓ | ✓ | ✓ | ✓ | ✓ |
| 2.5 Parallax | | | | ✓ | ✓ | ✓ |
| 3 Lighting: unlit | | | | | ✓ | ~ |
| 3 Lighting: flat | ✓ | | | | | ✓ |
| 3 Lighting: ramp toon | | | ✓ | | ✓ | ✓ |
| 3 Lighting: physically based | | ✓ | | ✓ | ✓ | ✓ |
| 3.5 External lighting | | | | | ✓ | ✓ |
| 4 Detail | | ✓ | ✓ | ✓ | ✓ | ✓ |
| 4 Recolour | | | ✓ | | ✓ | ✓ |
| 4 Decal | | | | | ~ | ✓ |
| 4 Transition | | | | | ✓ | ✓ |
| 4 Matcap | | | ✓ | | ✓ | ✓ |
| 4 Cubemap | | | | | ✓ | ✓ |
| 4 Rim | | | ✓ | | ✓ | ✓ |
| 4 Emission | | ✓ | ✓ | ✓ | ✓ | ✓ |
| 4 Iridescence | | | | | ✓ | |
| 4 Subsurface | | | | | ✓ | ✓ |
| 4 Back face | | | | | | ✓ |
| 4 Procedural pattern | | | | | ~ | ✓ |
| 4 Refraction | | | | | ✓ | ~ |
| 5.1 Outline | | | ~ | | ✓ | ✓ |
| 5.2 Fur | | | | | | ✓ |
| 5.3 Deformation | | | | | ✓ | ✓ |
| 6 Visibility | | | | | ✓ | ✓ |

Toon Lit and the two Standards fit entirely inside the catalogue. Uber and Poiyomi need almost
all of it. No feature exists only for one source unless it's described generically; the
single-source ones are mask bus, palette, back face and fur (Poiyomi) and iridescence (Uber).

---

## 9. What changes in the current code

How the catalogue differs from `AtlasUnifiedMaterial` today:

| Today | Catalogue |
|---|---|
| 15 `AtlasLightingMode` values | 4 families with parameters (section 3) |
| About 40 free-form semantic features | Typed features, building blocks and modulators |
| Fixed hue fields (`HueShift`, `DetailHueShift`, `EmissionHueShift`) | Colour adjustments with targets (1.10) |
| Matcap, rim, detail, decal and emission each with their own blend fields | One shared blend list and placement (1.7, 1.8) |
| AudioLink and emission pulse as dedicated structures | Modulators on any parameter (1.9) |
| `AtlasColorMaskLayer` with four fixed slots | Recolour layer with any number of regions |
| Global masks as Poiyomi-shaped bindings | Mask bus (1.5) |
| Colours stored as sRGB | Linear (already a known gap) |
| `AtlasSurfaceMode` (Opaque, Cutout, Transparent, Premultiplied, Additive) | Surface modes named by look (6) |
| No outline, fur or deformation | Sections 5.1–5.3 |

### 9.1 Typing the free-form semantic features

The parsers create 36 distinct free-form feature IDs (`AtlasSemanticFeature`). Eight more IDs
only label missing-feature issues. Every one of the 36 has a home in this catalogue.

**What typing one involves:**
1. **A typed home:** a field in a feature or building block. Without one, typing would mean
   adding source-shaped fields, which the guidelines rule out.
2. **Units and conventions:** the free-form values are raw source values, often source enum
   integers stored as "choices". Each one is converted into the catalogue's convention. This
   is the real work.
3. **Parser and test:** the parser writes the typed field, and a test checks the converted
   value.

There are no output writers yet, so nothing downstream needs changing.

**Home exists in the catalogue; parser work only:**

| Free-form ID | Becomes |
|---|---|
| `surface.normal.secondary`, `surface.bent-normal` | 2.3 Normals: layer mask, bent-normal input |
| `surface.alpha-map`, `render.alpha` | 2.2 Alpha: source, modifiers, cutout handling |
| `surface.specular`, `surface.reflections`, `surface.clearcoat` | 2.4 Surface response: strengths, tints, fresnel, clear coat layer |
| `surface.channel-packing` | Mostly disappears: packing is the texture and channel of each scalar input (1.2) |
| `surface.detail` (Mochie's detail metallic, roughness, occlusion) | Detail layer with a target (4) |
| `lighting.rim`, `lighting.rim.options.*`, `lighting.environmental-rim`, `lighting.backlight` | Rim layer: colour source *environment*, back-light response (4) |
| `lighting.subsurface`, `lighting.skin-scattering` | Subsurface layer (4); diffuse model *skin* (3.3) |
| `lighting.external`, `lighting.light-volumes`, `lighting.ltcgi` | External lighting (3.5); the typed structure exists and needs a few more fields |
| `render.options`, `render.additional` | Render (6): fog, two-sided lighting, depth offset, queue |
| `decal.options.*` | Decal layer (4): blend, alpha blend, aspect, mirroring |
| `texture.sampling`, `mask.sampling` | Mapping (1.1): sampling, UV set, swizzle, rotation, scroll on any texture or mask |

**Needs the lighting families first:**

| Free-form ID | Becomes |
|---|---|
| `lighting.light-data`, `lighting.additional-lights`, `lighting.shadows` | Families and shared parameters (3.1–3.4). The parser resolves Poiyomi's light colour, map and direction modes into these parameters; the 15 lighting modes go away with this step |

**Needs a building block first:**

| Free-form ID | Needs |
|---|---|
| `emission.modulation`, `audio.reactive.modulation`, `audio.global` | Modulators (1.9): waveform, audio band, smoothing, fallback |
| `surface.proximity-color` | A camera-distance modulator driving a colour (1.9) |
| `color.adjustment` | Colour adjustment with targets (1.10), replacing the fixed hue fields |
| `matcap.blend` (fallback parser) | The shared blend list (1.7) |

**Needs a new feature:**

| Free-form ID | Needs |
|---|---|
| `surface.glitter` | Procedural pattern layer (4); see open question 2 |
| Outlines (`geometry.outline`, today only a missing-feature issue) | Outline (5.1) |

**Drop:** `environment.rain`, `environment.puddles` and `geometry.wind` from Mochie Standard,
and the Bakery, screen-space-reflection and lightmap values inside `lighting.external` and
`surface.reflections`. They're world-only, and Mochie Standard is being dropped.

**Order,** following the dependencies:
1. Building blocks: mapping, scalar remap, mask with mask-bus reference, blend, placement,
   colour adjustment, modulators.
2. The parser-only moves.
3. Lighting families.
4. Modulators in the parsers.
5. Outline and procedural patterns.

**Afterwards:** `AtlasSemanticFeature` stays only for reporting. A source value with no typed
home becomes an *unmapped* report line, not stored data. Coverage then becomes measurable:
every report is a gap.

---

## 10. Decisions so far

From your answers during the inventory:
- Avatar shaders and features only; world-only features are ignored, not counted as unmapped.
- Mochie Standard is dropped in favour of Mochie Uber, after the inventory.
- Coverage means the look; misunderstandings of a source are fixed in its parser, not in the
  unified material.

## 11. Open questions

Merged from the drafts, without the atlasing ones. Where the drafts asked the same thing, it's
asked once here.

1. **Specular colour workflow:** keep specular colour as a second surface response input next
   to metallic (my lean), or have parsers convert it? Unity Standard, Uber and Mochie offer it.
2. **Procedural pattern layer:** one layer with a pattern *kind*, or leave the rarer kinds
   (truchet, video screen, internal parallax, oscilloscope) reported as unsupported until a
   writer needs them?
3. **Stats overlay:** out of scope, as not part of the look?
4. **Unavailable sources:** Poiyomi Pro and Uber X stay unsupported unless you can provide the
   source?
5. **Older versions:** Poiyomi 7.3–9.3 and older Mochie material versions: keep supporting
   them, or support current versions only and report older ones?
6. **Static deformation** (transform, inflate, snap, displacement): carry it as a geometry
   feature (live), or resolve it into the mesh at build time? This touches the build, not
   just the unified material.
7. **Light volumes and LTCGI** stay in scope as external lighting?

**Deferred to the atlasing pass:** UV-tile features (AudioLink UV modes, UV tile discard),
vertex colour handling when merging, settings that can't differ within one material, and
per-material constants such as fur length.
