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

**Specified** (refactor step 7b):
- **Placement (decal):** centre (UV), scale, rotation (degrees); the UV wraps (0–1) before
  placing; *clip outside*: nothing is sampled outside the placed area.
- **Frames:** a sprite sheet (columns, rows; frames read left to right, top to bottom) or a
  texture array (one frame per slice); a rate in frames per second, or a fixed frame.
- **Distortion:** a UV offset shared by several references: simplex noise (scale, speed,
  octaves) or a normal map's xy (a texture reference), times strength and a mask. Offsets are
  in UV units. One distortion object can be shared by every reference it shifts.

### 1.2 Scalar input
`value = remap(constant × channel(texture))`, or the constant alone without a texture.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| constant | float | per use | set to 1 when a source's texture replaces its slider |
| texture, channel | texture reference, R/G/B/A/luminance | none, R | |
| invert sample | bool | off | applied to the channel before the constant |
| remap | output range, contrast, intensity, lightness | identity | `v = lerp(min, max, v)`; `v = saturate(lerp(0.5, v, contrast))`; `v += saturate(v × intensity)`; `v = saturate(v + lightness)`; applied to the sampled value (constant × channel; for strength around one the channel, before the strength), before *invert result*. Uber's map filters (specified, refactor step 7b). Input range and midpoint: not specified yet (Poiyomi 11 mask levels) |
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
- **Colour targets** use a colour ramp or a palette entry instead of min and max. A colour
  target with mode *multiply* and plain min and max scales the colour.
- A modulator whose target is a mapping (noise into UVs) is UV distortion.

**Specified so far** (refactor step 6): the modulator and the audio source.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| target | path | — | for example `layers[3].source.color`; indices are list positions in the unified material |
| mode | add, multiply, replace | multiply | |
| min, max | float | 0, 1 | |
| source | audio, time, light level | — | the other sources aren't specified yet |
| mask | 1.4, or none | none | where the modulation applies: `lerp(target, modulated, mask)` |

**Audio source:**

| Parameter | Values | Default | Notes |
|---|---|---|---|
| band | bass, low mids, high mids, highs, volume | bass | |
| smoothing | 0–1 | 0 | 0 = the raw level, 1 = AudioLink's most smoothed |
| fallback beat | bool, speed | off, 1 | without AudioLink in the world: a decaying pulse, `(1 − frac(t × speed))` on every other cycle |
| value without AudioLink | 0–1 | 0 | the value when the world has no AudioLink and there's no fallback beat |
| spread | none, history, fill | none | see below |
| coordinate | UV u, UV v (with a UV set), view angle, texture channel | UV u | for *history* and *fill*: a 0–1 position. *view angle* is `1 − abs(view · normal)`, 0 facing the viewer |
| history offset, history length | samples | 0, 128 | *history*: the sample `offset + coordinate × length` ago |
| fill softness | 0–1 | 0.1 | *fill*: the fade below the level |

**Time source** (refactor step 7): a waveform of time.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| waveform | sine, square, triangle, saw, reverse saw | sine | 0–1: sine `0.5 + 0.5 sin(2π f t)`, square its rounding, triangle up and down, saw rising, reverse saw falling |
| frequency | cycles per second | 1 | parsers convert their speed units |

**Light level source** (refactor step 7): how bright the surroundings are.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| threshold, crossfade | 0–1 | 0.5, 0.1 | the value rises from `threshold − crossfade` to `threshold + crossfade` |
| smooth | bool | off | off: the raw brightness (average of direct, ambient and vertex light, 0–1); on: smoothed through the crossfade |
| include shadows | bool | off | the brightness includes the surface's own shading |

**Audio spread:** how the audio value varies across the surface.

| Spread | Value at a point | Sources |
|---|---|---|
| none | the band's current level, everywhere | Toon Standard Pulse, most per-feature AudioLink |
| history | the band's level from `offset + coordinate × length` samples ago, so recent audio travels along the coordinate | Toon Standard Scroll, Poiyomi centre-out emission |
| fill | 1 where `coordinate ≤ level`, else 0, fading over the softness below it | Toon Standard Bar |

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

**Specified so far** (refactor step 6): one adjustment is one operation; the material keeps an
ordered list.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| target | base colour, surface colour, layer, final colour | base colour | *base colour*: the base colour input (2.1), before layers; *surface colour*: the base colour after the surface layers (end of S1); *layer*: that layer's source. Emission is a layer |
| layer | a layer | — | for target *layer* |
| mask | 1.4, or none | none | |
| operation | hue, saturation, contrast, brightness, tint, smooth invert, sRGB decode, ACES tone map, channel tint | hue | |
| amount | float | 0 | hue: turns, 1 = a full turn; saturation `lerp(luminance, c, amount)`; contrast `lerp(0.5, c, amount)`; brightness `c × amount` |
| colour | linear colour | white | tint: `c × colour` |
| hue set | bool | off | hue: every colour takes the hue `amount` instead of being rotated by it (a mono tint) |
| smooth invert, sRGB decode, ACES tone map | amount 0–1 | 1 | `lerp(c, f(c), amount)`: smooth invert `1 − smootherstep(0, 1, c)`; sRGB decode the sRGB transfer curve to linear; ACES `(x(2.51x + 0.03)) / (x(2.43x + 0.59) + 0.14)` with `x = 0.6 c` |
| channel tint | 4 linear colours, a mask texture | — | weights `m` from the mask's RGBA, divided by their sum when above 1; `t = Σ colourᵢ mᵢ + max(0, 1 − Σ mᵢ)`; `c × (2t − 1)` (Alloy team colours) |
| hue method | grey-axis rotation, HSV, Oklab | grey-axis rotation | grey-axis: rotation of the linear colour around `(1,1,1)/√3`; HSV: the hue of the HSV colour; Oklab: the Oklab hue angle |

---

## 2. Surface

### 2.1 Base colour
`base = colour input × vertex colour` (where enabled).

| Parameter | Values | Default | Notes |
|---|---|---|---|
| colour input | 1.3 | white, no texture | |
| vertex colour: multiply colour | bool | off | RGB × vertex RGB |
| vertex colour: multiply alpha | bool | off | alpha × vertex alpha |
| vertex colour: amount | 0–1 | 1 | `base × lerp(1, vertex, amount)` |
| vertex colour: sRGB data | bool | off | off: vertex colours are used as stored (as linear values); on: converted from sRGB to linear first |

**Sources:** all. *Specified with Toon Lit (refactor step 2).*

### 2.2 Alpha
| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | base colour alpha, base colour constant alpha, mask, none | base colour alpha | *base colour alpha*: colour alpha × texture alpha; *constant alpha*: the colour's alpha alone, when the texture's alpha means something else (smoothness); *none*: 1 |
| mask | 1.4 | — | for source *mask* |
| mask multiplier | float | 1 | source *mask*: the mask × this |
| strength | 0–1 | 1 | `lerp(1, alpha, strength)` |

What alpha does is the render surface mode's job (6). *Specified with Unity Standard (refactor
step 3).* Not specified yet:
- **Source** vertex alpha.
- **Modifiers** (live, S7): distance fade, view-angle fade, camera- or model-angle fade, audio.
- **Cutout handling:** threshold, sharpened coverage, mip preservation, dithering (soften edges
  or real transparency).
- **Sources:** Unity Standard, Uber, Poiyomi.

### 2.3 Normals
- **Layers:** the primary normal, then surface layers targeting the normal (4.1), composited
  by whiteout with strength and mask.
- **Flat shading** (specified, refactor step 7b): the surface normal is the triangle's face
  normal, for a hard-edged, faceted look; normal maps apply on top.
- **Bent normal:** an input for specular occlusion and indirect light.
- **Modifiers:** spherize toward a point, flat shading, back-face flip.
- **Sources:** all except Toon Lit.

### 2.4 Surface response
How the surface reflects light (S2).

| Parameter | Values | Default | Notes |
|---|---|---|---|
| metallic | scalar 0–1 | 0 | the value the shading uses: parsers convert gamma-encoded sliders |
| roughness | scalar 0–1 | 0.5 | perceptual roughness, `1 − smoothness` |
| reflectance | scalar 0–1 | 0.5 | non-metal reflectance at normal incidence; 0.5 = 4% (`F0 = 0.16 × r²`) |
| specular colour | colour input, or none | none | the specular workflow's F0 colour; *none* is the metallic workflow. Its own input: parsers don't convert it to metallic |
| occlusion | scalar, strength around one | 1, no map | darkens ambient light and reflections, not direct light |
| highlights | group | on | direct-light highlights: enabled; strength (1); tint (white); mask; roughness override (off, value); sharpness (below); **steps** (0: smooth, else `floor(h × steps) / steps`); **real-time light only**; **base colour mix** (the highlight colour toward the base colour); **ignore light colour** (not scaled by the diffuse light); **shape** (below) |
| highlight shape | round, stretched, both | round | *stretched* (hair, brushed metal) along the bitangent: `D = saturate(1 / (w × f²))`, `f = (T·H)² + (B·H)² / w² + (N·H)²` with width `w`, a second layer of width `w₂` added with a strength (or laid over by its own value), strand ripples along U (frequency, amplitude, strength, continuity), its own strength and steps; fresnel scaled by metallic. *Both*: blended by a mask (1 = round) |
| reflections | group | on | environment reflections: enabled (off: none); strength (1); tint (white); mask; fresnel strength (1, 0 = no fresnel falloff); roughness override (off, value); **steps** (0: smooth, else quantised, less on rough surfaces); **light based** (0–1: stronger where lit, `lerp(1, N·L + 0.3, amount)`); source *probes*, a fixed *cubemap* (lit by the surroundings or not), or *ambient colour* (a flat colour from the sky's ambient specular, Unity's reflections off); a fallback cubemap for scenes without probes |
| highlight sharpness | 0–1 | 0 | blends the highlight toward a hard edge: `lerp(h, smoothstep(0.5, 0.51, h), sharpness)` |
| occlusion: direct | 0–1 | 0 | how much occlusion also darkens direct light |
| occlusion: indirect | 0–1 | 1 | how much occlusion darkens ambient light |
| occlusion tint | colour input, or none | none | occlusion darkens toward this colour instead of black: `lerp(tint, 1, occlusion)` |
| occlusion: reflections | 0–1 | 1 | how much occlusion darkens reflections |
| specular from mesh normal | bool | off | highlights, reflections, matcaps and rims use the mesh normal, not the normal map; diffuse keeps it (a varnished look) |
| specular anti-aliasing | bool | off | roughens where the normal changes quickly on screen, so small or curved detail doesn't sparkle (projected-space normal filtering, Tokuyoshi 2021) |
| anti-aliasing variance | 0–1 | 0.1 | screen-space variance scale |
| anti-aliasing threshold | 0–1 | 0.2 | the most it may add to roughness |

*Specified with Unity Standard (refactor step 3); specular anti-aliasing with Standard Lite
(step 4); highlight sharpness and occlusion amounts with Toon Standard (step 6).* Not specified
yet:
- **Occlusion:** multi-bounce occlusion.
- **Highlights:** strength, tint; light-type limits;
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
- **Specular anti-aliasing** strength.
- **Sources:** Standard Lite, Toon Standard, Unity Standard, Uber, Poiyomi.

### 2.5 Parallax
Shifts the surface texture lookups (S1) by the view angle, so the surface looks deeper than it
is. Live.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | off | |
| height | scalar input | — | 0 low, 1 high |
| depth | float | 0.02 | UV shift scale |
| reference height | 0–1 | 0.5 | the height that doesn't move |
| method | single step, multi-step | single step | single step: `shift = (height − reference) × depth × view.xy / (view.z + 0.42)`, view in tangent space. Multi-step: march from height 1 down in *steps* steps of `view.xy / (view.z + 0.42) × depth / steps` until below the surface, then three halving refinement steps |
| steps | 1–50 | 25 | multi-step |
| height offset | −1–1 | 0 | multi-step: added to the height (clamped below 1) |

**Sources:** Unity Standard, Uber, Poiyomi. *Specified with Unity Standard (refactor step 3).*
Not specified yet: mask.

---

## 3. Lighting

### 3.1 Families
| Family | Look | Sources |
|---|---|---|
| **unlit** | base colour as is | Uber shading off, Poiyomi unlit intensity |
| **flat** | uniform light, no shading across the surface: `base × (main light × shadow + ambient)`, each term times its contribution (3.4) | Toon Lit, Poiyomi Flat |
| **ramp toon** | a shading term mapped through a ramp to brightness and shade colour | Toon Standard, Uber shadows, Poiyomi TextureRamp, Multilayer Math, ShadeMap, Wrapped, SDF |
| **physically based** | energy-conserving diffuse with a diffuse model, plus surface response | Standard Lite, Unity Standard, Uber, Poiyomi Realistic, Skin, Cloth, PBR |

### 3.2 Ramp toon
`brightness = min(1, ramp(term) × occlusion + shadow lift)`;
`light = lerp(base × shadow colour from base, 1, brightness) × light colour + ambient`.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| shading term | half-Lambert | half-Lambert | `N·L × 0.5 + 0.5`, times the received shadow |
| ramp | texture reference, colour keys, or none | none | sampled along u at the shading term; colour. Keys are piecewise linear (position 0–1, colour), for ramps described by numbers. None = white (fully lit everywhere) |
| ramp strength | 0–1, with a mask | 1 | `lerp(1, ramp, strength × mask)` |
| term mask | 1.4, or none | none | a painted mask multiplying N·L before the ramp, to draw shadows in |
| conditions | always, real-time light only, baked light only | always | when the ramp applies |
| dithered | bool | off | the shadow becomes a dither pattern in proportion to its darkness |
| ramp preset | none, flat, realistic, realistic soft, realistic very soft, two bands, three bands, four bands | none | names the ramp's shape, so a writer can use its own equivalent |
| shadow lift | 0–1 | 0 | |
| shadow colour from base | 0–1 | 0 | 0: shadows go black; 1: shadows take the base colour, a saturated darkening |

*Specified with Toon Standard (refactor step 6).* Not specified yet:
- **Shading term:** N·L, wrapped, received shadows only, or an SDF face texture with forward
  and left directions.
- **Ramp:** one gradient. Ramp textures, band layers (border, blur), step and feather
  settings, two-width blends and wrap parameters all resolve into it. Optional named preset.
- **Shade colours:** per band, from a constant, the base colour, or a texture.
- **Shadow lift**, strength, painted shadow mask, border colour.

### 3.3 Physically based
GGX highlights and reflection probes, with the surface response (2.4).

| Parameter | Values | Default | Notes |
|---|---|---|---|
| diffuse model | Lambert, Disney | Lambert | *Disney*: Burley's diffuse, which darkens or brightens edges with roughness, as Unity Standard on PC |
| Disney amount | 0–1 | 1 | Disney: blends from Lambert |

*Specified with Unity Standard (refactor step 3).* Not specified yet: diffuse models skin
(pre-integrated colour lookup, thickness) and cloth (with its own sheen and reflectance).

### 3.4 Shared lighting parameters
| Parameter | Values | Default | Notes |
|---|---|---|---|
| ambient model | average, directional | directional | *average*: the ambient light's average colour, the same in every direction (spherical harmonics at order 0); *directional*: sampled along the normal |
| directional amount | 0–1 | 1 | directional: blends from the average |
| received shadows | bool | on | the main light's shadow |
| contribution: main light | 0–1 | 1 | the main directional light |
| contribution: additional lights | 0–1 | 1 | other per-pixel lights |
| contribution: vertex lights | 0–1 | 1 | Unity's four per-vertex lights |
| contribution: ambient | 0–1 | 1 | ambient and light probes, including lights Unity folds into them |
| limit brightness | bool | off | clamps the light (direct + ambient) to 1 per channel, so bright worlds don't blow out colours |
| minimum brightness | 0–1 | 0 | `base × value` is added after lighting, like emission, so the surface never goes fully dark |

*Specified with Toon Lit (refactor step 2); brightness limit and minimum with Toon Standard
(step 6).* Not specified yet:
- **Light direction override:** a fixed local direction, or the view direction. *A fixed world
  direction is specified (refactor step 7b): the main light's direction is replaced by it.*
- **Light colour override;** monochrome light.
- **Brightness ceiling** other than 1, or keeping the hue.
- **Received shadow softness.**

### 3.5 External lighting
VRChat light volumes (strength, specular, bias) and LTCGI (diffuse and specular tint,
strength). **Sources:** Uber, Poiyomi.

**Light volumes, specified** (refactor step 7):

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | off | the world's light volumes replace the light probes |
| strength | 0–1 | 1 | blends from the probes to the volumes |
| specular | bool, strength | off, 1 | highlights from the volumes |
| bias | float | 0 | offsets the sample point along the normal |

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

**Parameters specified so far** (refactor step 3):

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | on | |
| mask | 1.4, or none | none | none: everywhere |
| placement | 1.8 | surface | a layer targeting only emission uses *emission* |
| emission amount | 0–1 | 0 | |
| source | colour input | white | |
| source is base colour | bool | off | |
| targets | list of: target, blend (1.7), opacity, channel | — | opacity 0–1, or any amount ≥ 0 for blend *add*; *normal* targets composite by whiteout, opacity being the strength; one-value targets read the source's *channel* |
| source alpha is opacity | bool | off | the source's alpha multiplies every target's opacity (alpha-blended details and decals) |

A source with different textures per target (a detail colour map and a detail normal map) is
two layers with the same mask. *Luminance* in the blends is `0.3 r + 0.59 g + 0.11 b`.

**Bakeable** when static and placed in UV space. **Sources:** all except Toon Lit.

### 4.2 Transition
A second appearance revealed by a threshold.
- **Threshold source:** texture, noise, point to point, sphere, centre out; amount, softness.
- **Edge:** up to three colours, widths, emission.
- **Revealed appearance:** transparent, or a colour and texture.
- **Modes:** wave, continuous.

Structurally a surface layer whose mask is a threshold with an edge band. Kept separate
(decided) because its *amount* is the thing that gets animated.

**Specified so far** (refactor step 7b), besides the shared layer parts:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| threshold | texture reference, channel | none | none: 1 everywhere |
| amount | 0–1 | 0 | where the threshold is below the amount, the revealed appearance replaces the base colour (a hard edge) |
| revealed | colour input | white | |
| edge width | 0–1 | 0 | a band just above the amount, `0 < threshold − amount ≤ width` |
| edge colour | linear colour | white | added after lighting, like emission | **Sources:** Uber base colour dissolve, Poiyomi
dissolve.

### 4.3 Lookup layer
A texture looked up by direction instead of by UV.
- **Texture:** 2D (matcap) or cube.
- **Projection:** view normal (VR-stabilised or not), top pinch, double-sided, gradient,
  light direction, reflection direction, world or local normal, skybox; rotation and spin for
  cubes.
- **Options:** blur or roughness, own normal map, base and light colour mix, hide in shadow,
  probe fallback.

**Specified so far** (refactor step 6), besides the shared layer parts:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | colour input | white | the texture is looked up by the projection, not by UV |
| projection | view stabilised, view direction | view stabilised | *view stabilised*: the normal in view space with world up kept up, so the lookup doesn't roll with the head (VR); *view direction*: a cubemap sampled along the direction from the camera to the surface, like a window into it |
| rotation, spin | 3 × degrees, 3 × degrees per second | 0, 0 | view direction: rotations of the xy, xz and yz planes, applied in that order |
| centre eye | bool | off | both eyes use the view from between them |
| tiling | 2 × float | (1, 1) | scales the lookup coordinate |
| blur | 0–1, or the surface roughness | 0 | mip blur as roughness |
| source alpha is opacity | bool | off | the source's alpha multiplies the opacity |
| light response | ignore light, scale by light | ignore light | |
| normal map | texture reference, strength, with surface | none | its own normal for the lookup; *with surface*: whiteout-blended with the surface normal |
| blend, opacity | 1.7, 0–1 | add, 1 | placement *surface* blends into the base colour (lit and shadowed); later placements into the lit colour |

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

**Specified so far** (refactor step 6), besides the shared layer parts (placement *overlay*):

| Parameter | Values | Default | Notes |
|---|---|---|---|
| edge source | view angle | view angle | `e = 1 − abs(view · normal)`, the view from between the eyes |
| profile | band, power band, hue rotation, ramp | band | band: `smoothstep((1 − width) − softness, (1 − width) + softness, e)`; power band: `smoothstep(edge, 1 − edge, e^power)`; hue rotation: the colour below rotated by `frac(power band)` turns (HSV); ramp: the colour below × a ramp texture at `e` |
| ramp | texture reference | none | ramp profile |
| specular only | bool | off | acts on highlights and reflections only, not the whole lit colour (iridescence) |
| scale by metallic | bool | off | the opacity times the metallic value |
| width, softness | 0–1 | 0.3, 0.1 | band |
| power, edge | float, 0–0.5 | 5, 0 | power band |
| colour | colour input | white | |
| colour source | colour, environment | colour | *environment*: the reflection probe along the reflection, times the colour, with an optional roughness override |
| base colour mix | 0–1 | 0 | multiplies by `lerp(1, base, mix)` |
| light response | ignore light, scale by light | ignore light | *scale by light*: times (main light colour + ambient) |
| blend, opacity | 1.7, 0–1 | add, 1 | |

**Sources:** Toon Standard rim, Uber basic and environment rim and iridescence, Poiyomi rims,
depth rim, environmental rim, Depth FX, backlights.

### 4.5 Subsurface
Light through thin parts: thickness texture, colour, base colour tint, direct and indirect
strength, falloff, distortion, wrap, shadow strength. S3. **Sources:** Uber, Poiyomi.

**Specified so far** (refactor step 7), besides the shared layer parts (placement *lit*):
`light = (pow(saturate(view · −(L + N × distortion)), power) × intensity / π + ambient) ×
pow(1 − thickness, thickness power) × (direct + ambient light) × colour`, times the received
shadow when there's a real-time light.

| Parameter | Values | Default |
|---|---|---|
| colour | colour input | white |
| base colour mix | 0–1 | 0 |
| thickness | texture reference, power | none, 1 |
| intensity, power, distortion, ambient | float | 1, 1, 1, 0 |

### 4.6 Procedural pattern
A generated pattern with a **kind** and its parameters: glitter, voronoi, truchet, pathing,
internal parallax, video screen, audio spectrum, audio volume colour, oscilloscope,
constellation. **Sources:** Poiyomi, Poiyomi Pro, Uber oscilloscope.

**Oscilloscope, specified** (refactor step 7b): AudioLink's waveform drawn as a line,
`saturate(1 − 50 × abs(wave(u′) − (2v′ − 1)))`, with `u′, v′` the UV scaled, offset and
rotated (degrees), inside margins (a u range and a v range), × colour × opacity. Placement
*overlay*, blend *add*.

One layer type (decided). Each kind gets its own typed parameters when a parser types it;
until then, a material using that kind gets an unmapped report line. Adding a kind never
changes the layer type.

### 4.7 Refraction
Bends what's behind a transparent surface: strength (IOR), tint, colour split, blur, opacity;
with screen-grab extras (pixelation, colour ramp). **Sources:** Uber, Poiyomi Pro Grab Pass and
Self Grab.

**Specified so far** (refactor step 7b), besides the shared layer parts (placement *lit* or
*overlay*): the screen behind, offset by `(IOR − 1) × the view-space normal / (depth + 1) ×
(1 − N·V)`, times the tint, then `lerp(refraction, surface, opacity)`.

| Parameter | Values | Default |
|---|---|---|
| IOR | float | 1.3 |
| tint | linear colour | white |
| opacity | 0–1 | 0 |
| chromatic aberration | off, or strength 0–1 | off |
| blur | off, or strength 0–1, optionally × roughness | off |
| cut mask | texture reference, threshold | none: refraction where the mask is at most the threshold |

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

**Specified so far** (refactor step 6):

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | on | |
| shape | normal expansion | normal expansion | an inverted hull pushed out along the normal |
| width | metres | 0.0005 | |
| width mask | 1.4, or none | none | |
| screen width limit | float, or 0 | 0 | the largest on-screen offset in normalised device coordinates; 0 = none |
| width from vertex colour | bool | off | width × vertex colour; black vertices have no outline |
| hide within | metres | 0 | no outline closer to the camera than this |
| colour | colour input | black | |
| base colour mix | 0–1 | 0 | blends toward the base colour (with vertex colour) |
| multiply by base colour | bool | off | colour × the base colour |
| lit | bool | off | unlit: the colour as is |
| emission | bool | off | the material's emission also shows on the outline |
| pass | 6 | cull front, depth write on | |

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

**Specified so far** (refactor step 7b): a list of operations, applied in order, each with a
mask (sampled per vertex on UV0) and a strength.

| Operation | Amount | Notes |
|---|---|---|
| inflate | 3 × float, object units | along the normal per axis; *absolute normal*: along its absolute value, so everything moves outward on that axis |
| rotate | 3 × degrees | the xy, xz and yz planes, in that order, around the object origin; normals turn too |
| translate | 3 × float, object units | |
| snap | precision, steps per metre | world position rounded up to the grid, `lerp(p, ceil(p × precision) / precision, strength × mask)`, for a retro look |

Static operations (no modulator) stay deformation features like the animated ones. Whether a
writer applies them to the mesh at build time, so outputs without them keep the shape, is
decided later, with the writers.

### 5.4 Geometry effects
Effects that add or remove geometry: tessellation, dissolving into pieces, shattering,
particles from triangles. **Sources:** Poiyomi Pro variants.

---

## 6. Render

| Surface mode | Look | Blend |
|---|---|---|
| opaque | alpha ignored | none |
| cutout | pixels below the cutoff are discarded | none |
| dithered | screen-door transparency: a dither pattern keeps pixels in proportion to alpha | none |
| fade | everything fades with alpha, highlights and reflections included | `src × a + dst × (1 − a)` |
| transparent | glass: diffuse fades with alpha, highlights and reflections stay | `src + dst × (1 − a)`, diffuse premultiplied by alpha |

Also: per pass, cull (off, front, back), depth write and **stencil** (reference 0–255,
compare function, pass, fail and depth-fail operations; Unity's meanings); **two-sided lighting** (back faces
flip their normal, so they light like front faces). *Specified with Unity Standard (refactor
step 3); two-sided lighting with Toon Standard (step 6).* Not specified yet:
- **Surface modes** additive, soft additive, multiplicative, multiplicative ×2. Raw
  blend factors are kept only when they match none of these.
- **Per pass:** base, each outline layer and fur have cull, depth test and write, depth
  offset, stencil and colour mask.
- **Fog**.
- **Visibility:** in mirrors, cameras, normal view; an alternate mirror appearance; near-camera
  clipping with a mask; hide by UV tile; invisible. *Specified* (refactor step 7b):
  - **mirror visibility:** both, mirror only, not in mirror;
  - **mirror base colour:** a texture replacing the base colour texture in mirrors, or none;
  - **near clip:** on, distance in metres, mask (UV0, hidden where above 0.5); not in mirrors;
  - **invisible:** bool.
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
