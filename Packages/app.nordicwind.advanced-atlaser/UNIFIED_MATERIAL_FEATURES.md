# Unified material — feature catalog

**Status:** the specification of the unified material; the model in `Editor/Unified/` matches
it. It merges the per-shader inventories into one feature set, with parameters, units, defaults
and formulas for every feature a parser maps. Atlasing concerns are left out on purpose; they
get their own pass later.

**Inputs:**
- [VRChat Mobile](UNIFIED_MATERIAL_VRCHAT_MOBILE.md): Toon Lit, Diffuse, Bumped Diffuse, Standard Lite, Toon Standard.
- [Unity Standard](UNIFIED_MATERIAL_UNITY_STANDARD.md).
- [Mochie Uber](UNIFIED_MATERIAL_MOCHIE_UBER.md).
- [Poiyomi Toon 10 and 11, and Poiyomi Pro](UNIFIED_MATERIAL_POIYOMI.md), with both kinds of fur.

Mochie Standard was inventoried as a cross-check and then dropped in favor of Uber. Its
features are covered by the shaders above, apart from the world-only ones in section 7.

**How to read it:**
- Names are neutral descriptions, not code names.
- *Live* features need runtime support in the output shader. *Bakeable* ones can be resolved
  into textures when nothing animates them.
- The design rules are in [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md#guidelines).

---

## How a unified material is evaluated

Every feature acts at one stage. Writers implement the stages in this order; parsers decide which
stage a source's feature belongs to.

| Stage | What happens | Features |
|---|---|---|
| **S1 Surface inputs** | Base color, alpha, normals and surface response inputs are built up | Base color (2.1), surface layers targeting color, alpha, normal or surface inputs (4.1), transition (4.2), parallax (2.5) |
| **S2 Lighting** | The lighting family shades the surface: diffuse, specular, reflections, sheen, glints, clear coat | Lighting (3), surface response (2.4), external and screen-space lighting (3.5, 3.6) |
| **S3 Light-dependent layers** | Layers that need the light: transmission through thin parts, rims that follow the light | Subsurface (4.5); lit angle and lookup layers |
| **S4 Overlays** | Layers added to the lit result, ignoring shadows | Angle, lookup and pattern layers with placement *overlay*; refraction (4.7) |
| **S5 Emission** | Everything that glows, added last so it's never shadowed | Surface layers targeting emission; emission amounts of other layers |
| **S6 Final color** | Adjustments and tone mapping of the finished color | Color adjustment with target *final color* (1.10) |
| **S7 Output** | Alpha modifiers, cutout, blending, visibility | Alpha (2.2), render (6) |

Geometry features (5) act before all of this, on the mesh and the extra passes.

**Placement (1.8)** is how a layer chooses its stage: *surface* = S1, *lit* = S3, *overlay* = S4,
*emission* = S5. Poiyomi's rim *application stage*, the "Unlit" toggles on matcaps, rims and
flipbooks, and Toon Standard's matcap (which changes the albedo, so it's lit) are all
placements.

---

## 1. Building blocks

Shared types that features are made of. Most source-specific detail lives here, which keeps the
feature count low. Each has its parameters below; see 1.0 for units.

### 1.0 Conventions
| Quantity | Convention |
|---|---|
| Color | Linear, HDR allowed (values above 1). Parsers convert sRGB-authored colors |
| Hue | Turns: 1 = a full revolution, whatever the source's units (radians, degrees, 0–1) |
| Angle | Degrees |
| Distance | Meters in world space, unless the parameter says *screen* |
| Strength, opacity, mask value | 0–1, linear |
| Surface roughness | Roughness (0 smooth, 1 rough), never smoothness |
| Saturation, contrast, brightness multipliers | 1 = unchanged |
| Choices | Neutral names, never a source's enum numbers |
| Time | Seconds; speeds per second |

### 1.1 Texture reference
| Parameter | Values | Default | Notes |
|---|---|---|---|
| texture | 2D, cube or array texture | — | |
| interpretation | color, data, tangent normal, direction, MSDF | color | *direction* is a tangent-space direction map (fur), composited like a normal map; *MSDF* a multi-channel signed distance field: white, alpha `saturate((median(r, g, b) − 0.5) / its change across a pixel)`, sharp at any size |
| space | UV0–UV3, object position, world position, panosphere, polar, view (matcap), screen, facing gradient (and its linear form) | UV0 | triplanar projection is a setting of the whole material, below |
| tiling, offset | 2 × float | (1,1), (0,0) | |
| rotation | degrees | 0 | the UV turned counterclockwise around (0.5, 0.5) before the tiling and offset: `uv′ = R(rotation)(uv − 0.5) + 0.5`, then `uv′ × tiling + offset` |
| scroll | UV units per second | (0,0) | live |
| spin | degrees per second | 0 | live |
| sampling | default, stochastic, pixel | default | stochastic hides tiling (with a density of its tile grid, 1); *pixel*: sharp texels with anti-aliased edges, for pixel art |
| placement | none, or placed: position, scale, rotation, side offsets, aspect, symmetry, tiled | none | decal placement; outside the placed area nothing is sampled |
| frames | none, or a sheet (columns, rows, count) or array; rate (frames per second) or fixed frame; range; crossfade | none | flipbooks and sprite-sheet decals; live unless fixed |

Shared mappings and per-UV-channel settings (Poiyomi's UV Settings) are folded into each texture
reference by the parser.

**Screen space layout**: the screen space is square, centered on each
eye's optical center so it fuses in stereo; with *object relative* it is sized to a world size
(1) at the object's distance and anchored to its origin, so it keeps its size on the object.
One setting for the material.

**Triplanar projection** (Poiyomi Pro's Triplanar Projection): one setting
for the material that replaces UV0 everywhere with three planar projections of a position `p`
divided by `lerp(1, object scale, scale with object)`: `(p.z × sign n.x, p.y)`,
`(p.x × sign n.y, p.z)` and `(−p.x × sign n.z, p.y)`, each + 0.5. The source is the *mesh*
(the object-space position and normal) or the *depth* texture (the scene behind, relative to
the object's origin, its normal from depth samples a blur's worth of pixels apart), which lands
the material on the surroundings within a *max size* of the origin (faded over its last edge
fade) while the camera is nearer than a fade range, and draws nothing without a depth texture.
The surface is evaluated per projection and blended by weights from the normal (*normal*:
`|n| − sharpness`, normalized; *sharp*: `|n|⁶`, normalized; *asymmetric*: the sides past 0.67
and a top past 0.675): color, alpha, normals, emission, additive light and the mask bus. The
base color becomes `lerp(the first projection's, the blend, mask)` and the alpha
`blend alpha × mask × fades`, emission and lighting × that alpha; the mask (1.4, sampled per
projection and blended) is × a strength. Constellation patterns (4.6) don't draw with it.

**Facing gradient** spaces: `u = v = 0.5 + 0.5 × abs(N·V)` (the view
from between the eyes), rings from the center of the view outward; *linear* uses the angle,
`0.5 + 0.5 × (1 − angle / 90°)`. Tiling scales both; the offset is unused.

**Specified**:
- **Placement (decal):** center (UV), scale, rotation (degrees) and spin (degrees per second);
  the UV wraps (0–1) before placing; the texture's tiling and offset apply to the placed UV;
  *clip outside*: nothing is sampled outside the placed area. *Symmetry* (before placing):
  *copy* `u = abs(u − 0.5) + 0.5`, *flip* `u + 0.5` where `u < 0.5`. *Mirror* (the mesh's
  mirrored side): *flip* `u = 1 − u` there, *left only*, *right only*, *flip right only*.
  *Depth*: parallax, the decal appearing that far behind the surface. *Smooth border*:
  the clipped edge is anti-aliased over a pixel. *Flip in mirror*: `u = 1 − u` in
  mirrors, so text reads the right way round.
- **Frames:** a sprite sheet (columns, rows, the area of the texture the grid covers, an inset
  pulling samples toward each frame's center; frames read left to right, top to bottom) or a
  texture array (one frame per slice); a rate in frames per second, or a fixed frame. With
  Poiyomi: frame `f = first + (t × rate + offset) mod count` (count 0: to the last
  frame); a fixed frame is a frame index; *crossfade* blends frame `floor(f)` toward the next
  one of the range by `smoothstep(range.x, range.y, frac(f))`.
- **Distortion:** a UV offset shared by several references: simplex noise (scale, speed,
  octaves) or a normal map's xy (a texture reference), times strength, plus a second map's
  xy × its own strength and a constant offset, times a mask. Offsets are
  in UV units. One distortion object can be shared by every reference it shifts.

### 1.2 Scalar input
`value = remap(constant × channel(texture))`, or the constant alone without a texture.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| constant | float | per use | set to 1 when a source's texture replaces its slider |
| texture, channel | texture reference, R/G/B/A/luminance | none, R | |
| invert sample | bool | off | applied to the channel before the constant |
| remap | output range, contrast, intensity, lightness | identity | `v = lerp(min, max, v)`; `v = saturate(lerp(0.5, v, contrast))`; `v += saturate(v × intensity)`; `v = saturate(v + lightness)`; applied to the sampled value (constant × channel; for strength around one the channel, before the strength), before *invert result*. Uber's map filters. Mask levels (1.4) on a map that feeds a scalar aren't typed: parsers report them |
| combine | multiply, strength around one | multiply | around one: `1 − (1 − channel) × constant` (occlusion) |
| invert result | bool | off | only for parser convenience (smoothness → roughness) |

Reflectance conventions resolve into the scalar: unified reflectance is the 0–1 control where
0.5 = 4% F0 (`F0 = 0.16 × r²`). Sources that store F0 directly are converted.

### 1.3 Color input
`color × texture`. Linear, HDR. Palette colors (1.6) are resolved into the color by the parser.

*Specified*: an **AudioLink theme** (ColorChord colors 0–3, or the
world's AudioLink theme colors 4–7) replaces the color where the world has AudioLink.

### 1.4 Mask
`value = lerp(1, invert(levels(channel) × vertex channel), strength)`, then blended with a
mask bus channel.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | texture channel, vertex color channel, constant | constant 1 | a texture channel, or a vertex color channel instead |
| levels | input min/max, midpoint, output min/max, invert | identity | on the texture channel: `t = saturate((v − in min) / (in max − in min))` (`step(in max, v)` for an empty range), `t = pow(t, log 0.5 / log midpoint)` (midpoint clamped to 0.01–0.99; none at 0.5), `1 − t` when inverted, then `lerp(out min, out max, t)` |
| times vertex color | a vertex color channel, or none | none | multiplies after the levels (Poiyomi 11's vertex color mask source) |
| invert | bool | off | after the vertex color |
| strength | 0–1 | 1 | `lerp(1, value, strength)` |
| bus | a mask bus channel, blend | none | 1.5 |

Poiyomi 11's *normalize* (an input range within measured bounds) is resolved into the input
range by the parser.

### 1.5 Mask bus
Numbered channels shared by the whole material (Poiyomi: 16).
- **Producers:** textures, vertex colors, view conditions (back face, in mirror, in camera,
  camera distance), VRChat contacts (radius, falloff, position, which people), and features
  that write their result (decals, dissolve, light maps).
- **Combining:** a producer writes with a blend: replace, add, multiply, min, max, average,
  subtract, divide.
- **Consumers:** any mask (1.4).

**Specified**: 16 channels, each starting at 1 and running its **steps**
in order:

| Step | Parameters | Effect |
|---|---|---|
| texture | a mask (1.4) | `value = the mask's value`, a map's channel with its levels and vertex color |
| vertex color | channel, sRGB decode, blend | `value = saturate(blend(value, vertex color channel))` |
| faces | front or back | `value × (1 on that side, 0 on the other)` |
| mirror, camera | inside or outside | `value × (1 when in a mirror / VRChat camera matches, else 0)` |
| distance | pixel or object position, min, max, min value, max value, blend (replace, multiply) | `value = saturate(blend(value, lerp(min value, max value, smoothstep(min, max, distance to the camera))))` |
| offset | amount | `value = saturate(value + amount)` |
| remap | min, max | `value = lerp(min, max, value)` |
| force on, force off | — | `value = 1`, `value = 0` |
| layer coverage | a layer, blend, invert | `value = saturate(blend(value, the layer's coverage))`, or of `1 − it` when inverted (Poiyomi's *apply to global mask*, *output undissolved*) |
| blacklight | key, near, far, blend | `value = saturate(blend(value, smoothstep(far, near, distance to a blacklight)))`; a VRChat blacklight is a black vertex light whose alpha is the key |
| contact | contact tags (64), people (everyone, others, me), inner and outer radius, scale with the toucher, around the touch or the whole mesh, invert, blend | `value = saturate(blend(value, touch))`: VRChat's contacts sent to shaders, each matching contact touching with `1 − smoothstep(inner, outer, distance)` from the pixel (or the object's origin), radii in meters at the uploaded size (× the toucher's size when scaled), added up and faded as contacts come and go; `1 − touch` inverted (Poiyomi 11) |
| second pass | inside or outside, blend | `value = blend(value, 1 in the second pass (6) when inside, in the first when outside; else 0)` (Poiyomi's Two Pass masks) |

**Blends** (Poiyomi's order): replace, subtract, multiply, divide, min, max, average, add.

**Consumers:** a mask (1.4) or a scalar input (1.2) names a channel and a blend; the channel is
combined with the mask's own value (after its invert and strength; 1 without a map) or with the
scalar's value (before its result inversion). They read the finished bus.

### 1.6 Palette
Up to a few shared colors, each with hue, saturation and brightness adjustments, referenced
by color inputs. Recoloring a palette entry recolors every feature that uses it.

*Not typed:* Poiyomi's theme colors are resolved by the parser to the colors they produce
(the look); a shared palette for editing comes later.

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
| tint luminance | `lerp(b, luminance(b) × l, o)` | recolor regions (Toon Standard color mask) |
| add to luminance | `lerp(b, min(luminance(b) + l, 1), o)` | |

Several blends at once (Poiyomi's matcap weights) are several layers, one per blend (4.3).

### 1.8 Placement
*surface* (S1), *lit* (S3), *overlay* (S4), *emission* (S5). Every layer has a placement and an
**emission amount**, which copies part of its result to S5 as well.

### 1.9 Modulator
A runtime value driving any numeric or color parameter, addressed by path:
`target = mode(target, lerp(min, max, value))`.

**Path syntax:** field names from the material down, with list positions in brackets, for
example `layers[3].opacity`, `outlines[0].width` or `fur.length`. The names are the model's
field names, matched without regard to case, and `AtlasModulatorPath.Resolve` finds what a path
points at. Animation clips use the same paths, so to a writer an animated value and a modulated
value look the same. Paths in use:
- **any layer:** `layers[i].emissionAmount`;
- **surface and lookup layers:** `layers[i].source.color` (and `.a`),
  `layers[i].targets[t].opacity`, `layers[i].source.map.decal.scale.x` (`.y`),
  `layers[i].source.map.decal.rotation`, `layers[i].source.map.frames.offset`;
- **angle layers:** `layers[i].color.color`, `layers[i].low`, `layers[i].high`,
  `layers[i].power`, `layers[i].opacity`;
- **transition layers:** `layers[i].amount`, `layers[i].detailStrength`, `layers[i].edgeWidth`,
  `layers[i].edgeEmission`, `layers[i].edgeColor`;
- **pattern layers:** `layers[i].opacity`, `layers[i].color`, and the kind's own:
  `layers[i].glitter.maxBrightness` (`.size`, `.sparkleOffset`, `.rotationOffset`),
  `layers[i].voronoi.innerEmission` (`.outerEmission`, `.gradient.x`, `.gradient.y`,
  `.offset.x`/`.y`/`.z`, `.innerColor`, `.outerColor`), `layers[i].truchet.textureOffset.x`
  (`.y`), `layers[i].constellation.starSize` (`.starBrightness`, `.lineThickness`,
  `.lineBrightness`, `.twinkleOffset`, `.movementOffset`, `.hueOffset`),
  `layers[i].path.channels[c].offset` (`.length`, `.emission`),
  `layers[i].internalParallax.minDepth` (`.maxDepth`, `.panOffset.x`/`.y`, `.minColor`,
  `.maxColor`);
- **outlines:** `outlines[k].width`, `outlines[k].emissionAmount`, `outlines[k].color.color`,
  `outlines[k].color.map.offset.x` (`.y`), `outlines[k].highlight.emission`,
  `outlines[k].highlight.color`, `outlines[k].colorAdjustments[n].amount`;
- **the rest:** `colorAdjustments[k].amount` (`.color`), `fur.colorAdjustments[n].amount`,
  `deformations[k].amount.x` (`.y`, `.z`), `deformations[k].strength`,
  `surface.alpha.modifiers[k].amount`.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| target | path | — | indices are list positions in the unified material |
| mode | add, multiply, replace, blend to color | multiply | *blend to color*: a color target becomes `lerp(target, color, value)`. A color target with *multiply* is scaled |
| curve | 4 floats | (0, 1, 0, 1) | shapes the value first: `lerp(x, y, smoothstep(z, w, value))` |
| color | linear color | white | for *blend to color* |
| min, max | float | 0, 1 | |
| source | audio, time, light level, sweep, camera distance | audio | below |
| mask | 1.4, or none | none | where the modulation applies: `lerp(target, modulated, mask)` |

**Time source:** a waveform of `t′ = t + offset`. A hue that turns at a speed is a saw added to
the hue's amount.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| waveform | sine, square, triangle, saw, reverse saw, pulse | sine | 0–1: sine `0.5 + 0.5 sin(2π f t′)`, square its rounding, triangle up and down, saw rising, reverse saw falling, pulse 1 while `frac(f t′) ≤ duty` |
| frequency | cycles per second | 1 | parsers convert their speed units |
| offset | seconds | 0 | |
| duty | 0–1 | 0.5 | pulse |

**Light level source:** how bright the surroundings are, 0–1.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| level | average, direct only, include shadows | average | the average of direct, ambient and vertex light; the direct light's luminance alone; or including the surface's own shading |
| smooth | bool | off | a smoothstep from `threshold − crossfade` to `threshold + crossfade` |
| threshold, crossfade | 0–1 | 0.5, 0.1 | smooth |
| linear | bool, low, high | off, 0, 1 | `saturate((level − low) / (high − low))`; neither smooth nor linear: the raw level |

**Sweep source:** a band moving along a direction: `(p^width + (1 − p)^(4 width)) / 2`, `p =
frac((position · direction − (t + offset) × speed) / interval)`, the position in object space or
the vertex color's RGB.

**Camera distance source:** the distance to the camera from the pixel or the object's origin,
`saturate((d − near) / (far − near))` through a four-key curve (values at 0, ⅓, ⅔, 1, linear
between), stepped at 0.5 when *step*.

**Audio source:** a value from the world's AudioLink.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| band | bass, low mids, high mids, highs, volume | bass | |
| smoothing | 0–1 | 0 | 0 = the raw level, 1 = AudioLink's most smoothed |
| fallback beat | bool, speed | off, 1 | without AudioLink in the world: a decaying pulse, `(1 − frac(t × speed))` on every other cycle |
| value without AudioLink | 0–1 | 0 | the value when the world has no AudioLink and there's no fallback beat |
| idle without AudioLink | bool | off | the target keeps its own value when the world has no AudioLink |
| threshold | 0–1 | 0 | `saturate((level − threshold) / (1 − threshold))` |
| spread | none, history, fill, chrono | none | below; *chrono*: AudioLink's chronotensity, time that runs with the band (one of its 8 motions), in seconds, unbounded |
| coordinate | UV u, UV v (with a UV set), view angle, texture channel | UV u | for *history* and *fill*: a 0–1 position. *view angle* is `1 − abs(view · normal)`, 0 facing the viewer |
| history offset, history length | samples | 0, 128 | *history*: the sample `offset + coordinate × length` ago |
| fill softness | 0–1 | 0.1 | *fill*: the fade below the level |

**Audio spread:** how the audio value varies across the surface. It keeps one modulator type
for whole-surface and traveling audio effects alike.

| Spread | Value at a point | Sources |
|---|---|---|
| none | the band's current level, everywhere | Toon Standard Pulse, most per-feature AudioLink |
| history | the band's level from `offset + coordinate × length` samples ago, so recent audio travels along the coordinate | Toon Standard Scroll, Poiyomi center-out emission |
| fill | 1 where `coordinate ≤ level`, else 0, fading over the softness below it | Toon Standard Bar |

**Not typed:** contact and blacklight sources (Poiyomi feeds both through the mask bus, 1.5);
a noise source (noise into UVs is the UV distortion of 1.1); an AudioLink delay; a color ramp
or a palette entry as a color target's output.

### 1.10 Color adjustment
One adjustment is one operation on a **target**, with a mask; the material keeps an ordered
list. Static adjustments on anything but the final color are bakeable.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| target | base color, surface color, layer, final color, emission | base color | *base color*: the base color input (2.1), before layers; *surface color*: the base color after the surface layers (end of S1); *layer*: that layer's source. Emission is a layer |
| layer | a layer | — | for target *layer* |
| mask | 1.4, or none | none | |
| operation | hue, saturation, contrast, brightness, tint, smooth invert, sRGB decode, ACES tone map, channel tint, chromatize, power, clamp, gradient map, hue tint, posterize, lightness | hue | |
| amount | float | 0 | hue: turns, 1 = a full turn; saturation `lerp(luminance, c, amount)`; contrast `lerp(0.5, c, amount)`; brightness `c × amount` |
| color | linear color | white | tint: `c × color` |
| hue set | bool | off | hue: every color takes the hue `amount` instead of being rotated by it (a mono tint) |
| smooth invert, sRGB decode, ACES tone map | amount 0–1 | 1 | `lerp(c, f(c), amount)`: smooth invert `1 − smootherstep(0, 1, c)`; sRGB decode the sRGB transfer curve to linear; ACES `(x(2.51x + 0.03)) / (x(2.43x + 0.59) + 0.14)` with `x = 0.6 c` |
| chromatize | amount | — | Oklab chroma × `(1 + amount)`, at most the chroma of the most saturated color of that hue; saturated |
| power | exponent | — | `sign(c) × abs(c)^amount` (gamma) |
| clamp | — | — | `saturate(c)` |
| gradient map | map, amount | — | `lerp(c, ramp(c), amount)`, the ramp sampled per channel at the sRGB value (red of red, green of green, blue of blue), back to linear |
| hue tint | color, map, amount | — | Oklab: keeps the lightness, takes the color's hue, chroma × the color's saturation `1 − min/max`; saturated; by `amount ×` the map's alpha, the color × the map's RGB |
| posterize | steps | — | HSV each quantized to `floor(x × steps) / steps` |
| lightness | amount | — | `c + amount` |
| fluorescence | amount | — | `c × lerp(1, saturate((1 − l) × sqrt(l)), amount)`, `l` the direct light color per channel: brightest in dim light |
| *include emission* | bool | off | final color operations: the emission goes through them too, on its own |
| GT7 tone map | color accuracy, chroma fade start and end | — | Polyphony Digital's GT7 tone mapping (their 2025 reference), on the final color before emission |
| neutral emission | HDR retention, hue correction | — | bright emission eased toward white as it grows, colors in the normal range unchanged (Poiyomi Pro) |
| HSV offset | hue, saturation, value offsets | — | HSV: hue + h (wrapped), saturation + s and value + v (saturated) |
| black and white | amount; lightness, contrast, tint, the texture's typical tone and highlight (or picked colors and a highlight strength), select by color (color, range) | — | Poiyomi 11's photographic black and white: luminance through a tone curve anchored at the typical tone, its highlight stretched toward about 2.1× (or the picked highlight strength), the base level set by the lightness, contrast in log space, × tint; *select* keeps it to colors near the selected one (Oklab chroma over lightness and lightness × 0.3, within the range, softened over half of it). The exact curve is in `AtlasBlackWhite` |
| luminance | 3 weights | 0.3, 0.59, 0.11 | saturation's luminance weights |
| channel tint | 4 linear colors, a mask texture | — | weights `m` from the mask's RGBA, divided by their sum when above 1; `t = Σ colorᵢ mᵢ + max(0, 1 − Σ mᵢ)`; `c × (2t − 1)` (Alloy team colors) |
| hue method | gray-axis rotation, HSV, Oklab | gray-axis rotation | gray-axis: rotation of the linear color around `(1,1,1)/√3`; HSV: the hue of the HSV color; Oklab: the Oklab hue angle |

---

## 2. Surface

### 2.1 Base color
`base = color input × vertex color` (where enabled).

| Parameter | Values | Default | Notes |
|---|---|---|---|
| color input | 1.3 | white, no texture | |
| vertex color: multiply color | bool | off | RGB × vertex RGB |
| vertex color: multiply alpha | bool | off | alpha × vertex alpha |
| vertex color: amount | 0–1 | 1 | `base × lerp(1, vertex, amount)` |
| vertex color: alpha amount | 0–1 | 1 | for *multiply alpha*: `alpha × lerp(1, vertex alpha, amount)` |
| vertex color: sRGB data | bool | off | off: vertex colors are used as stored (as linear values); on: converted from sRGB to linear first |
| vertex color: hue | off, or shift, speed (turns per second), method, set | off | the vertex color's hue shifted (or set) before it multiplies |

**Sources:** all. *Specified with Toon Lit.*

### 2.2 Alpha
| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | base color alpha, base color constant alpha, mask, none | base color alpha | *base color alpha*: color alpha × texture alpha; *constant alpha*: the color's alpha alone, when the texture's alpha means something else (smoothness); *none*: 1 |
| mask | 1.4 | — | for source *mask* |
| mask multiplier | float | 1 | source *mask*: the mask × this |
| mask blend | replace, multiply, add, subtract | replace | source *base color alpha* with a mask: how the mask combines with it (saturated) |
| offset | −1–1 | 0 | `saturate(alpha + offset)`, before the modifiers |
| global mask | 1.5 channel and blend | none | the channel combined with the alpha after the offset (saturated), before the modifiers |
| modifiers | list | none | in order, each multiplies alpha: *distance fade* (object or pixel position; distances, alphas: `lerp(min alpha, max alpha, smoothstep(min, max, distance))`); *view-angle fade* (intensity, sharpness, width, invert: `saturate(1 − smoothstep(min(sharpness, width), width, N·V))`); *angle fade* (camera faces model, model faces camera, or both; camera and model angle ranges in degrees, the model's forward direction, minimum alpha); *add* (an amount added, saturated; for audio modulators) |
| sharpened coverage | bool, mip scale | off, 0.25 | alpha-to-coverage edges sharpened and kept across mip levels |
| dithering | off, or gradient and bias | off | an 8×8 ordered dither lowers alpha where it's partial |
| dither cutout | bool | off | screen-door transparency instead: a pixel is kept where alpha exceeds the dither (1/64 to 1, less half a step), then alpha is 1 |
| strength | 0–1 | 1 | `lerp(1, alpha, strength)` |

What alpha does is the render surface mode's job (6), which also holds the cutout threshold. The
modifiers are live (S7), and each takes a mask (1.4). Vertex alpha is part of the base color
(2.1). **Sources:** Unity Standard, Uber, Poiyomi.

### 2.3 Normals
- **Layers:** the primary normal, then surface layers targeting the normal (4.1), composited
  by whiteout with strength and mask.
- **Flat shading** (specified): the surface normal is the triangle's face
  normal, for a hard-edged, faceted look; normal maps apply on top.
- **Bent normal** (specified): a tangent-space map of the unoccluded
  direction, for specular occlusion (strength 0–1) and, optionally, indirect diffuse.
- **Normal correct** (specified): the mesh normal bent toward the horizontal
  direction from a vertical axis through an object-space origin, by an amount (0–1), before
  normal maps; softer face shading.
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
| specular color | color input, or none | none | the specular workflow's F0 color; *none* is the metallic workflow. Its own input: parsers don't convert it to metallic |
| occlusion | scalar, strength around one | 1, no map | darkens ambient light and reflections, not direct light |
| highlights | group | on | direct-light highlights: enabled; strength (1); tint (white); mask; roughness override (off, value); sharpness (below); **steps** (0: smooth, else `floor(h × steps) / steps`); **real-time light only**; **base color mix** (the highlight color toward the base color); **ignore light color** (not scaled by the diffuse light); **ignore shadows** (not darkened by the received shadow); a **second lobe** (strength, 0 = none, and its roughness input's constant, the map and inversion shared; the brighter lobe shows); **shape** (below) |
| highlight shape | round, stretched, both | round | *stretched* (hair, brushed metal) along the bitangent: `D = saturate(1 / (w × f²))`, `f = (T·H)² + (B·H)² / w² + (N·H)²` with width `w`, a second layer of width `w₂` added with a strength (or laid over by its own value), strand ripples along U (frequency, amplitude, strength, continuity), its own strength and steps; fresnel scaled by metallic. *Both*: blended by a mask (1 = round) |
| reflections | group | on | environment reflections: enabled (off: none); strength (1); tint (white); mask; fresnel strength (1; how far reflections rise from F0 toward grazing angles, `F0 + (1 − F0) × strength × (1 − N·V)⁵`; 0 = no rise; parsers convert their shader's setting); roughness override (off, value); **steps** (0: smooth, else quantized, less on rough surfaces); **light based** (0–1: stronger where lit, `lerp(1, N·L + 0.3, amount)`); source *probes*, a fixed *cubemap* (lit by the surroundings or not), or *ambient color* (a flat color from the sky's ambient specular, Unity's reflections off); a fallback cubemap for scenes without probes; **exposure occlusion** (0–1: fades where the direct light is dim) |
| highlight sharpness | 0–1 | 0 | blends the highlight toward a hard edge: `lerp(h, smoothstep(0.5, 0.51, h), sharpness)` |
| fresnel | Schlick, view-half × view-normal | Schlick | how highlights and reflections grow toward grazing angles, from F0. *Schlick*: `F0 + (1 − F0)(1 − V·H)⁵` for highlights, `(1 − N·V)⁵` for reflections. *View-half × view-normal*: one term for both, `F0 + (1 − F0)((1 − V·H)(1 − N·V))⁵` (Toon Standard) |
| reflectance fade | 0–1 | 0 | highlights and reflections × `saturate((reflectance + metallic) / fade)`, so they fade out below it; 0: no fade (Toon Standard: 0.25) |
| occlusion: direct | 0–1 | 0 | how much occlusion also darkens direct light |
| occlusion: indirect | 0–1 | 1 | how much occlusion darkens ambient light |
| occlusion tint | color input, or none | none | occlusion darkens toward this color instead of black: `lerp(tint, 1, occlusion)` |
| occlusion: reflections | 0–1 | 1 | how much occlusion darkens reflections |
| specular from mesh normal | bool | off | highlights, reflections, matcaps and rims use the mesh normal, not the normal map; diffuse keeps it (a varnished look) |
| specular anti-aliasing | bool | off | roughens where the normal changes quickly on screen, so small or curved detail doesn't sparkle (projected-space normal filtering, Tokuyoshi 2021) |
| anti-aliasing variance | 0–1 | 0.1 | screen-space variance scale |
| anti-aliasing threshold | 0–1 | 0.2 | the most it may add to roughness |
| anisotropy | group | off | GGX anisotropy: `a = clamp(amount + (sample × 2 − 1), −1, 1)`, or `clamp(amount × sample, −1, 1)` with *map multiplies*, the sample from a map's channel or a vertex color channel; highlights stretch along the tangent (`a > 0`) or bitangent, the tangent turned by a rotation (degrees); a reflection stretch (1–5); a roughness anisotropy (0–1, 1: how much roughness stays along the stretch); an optional tangent-space direction map (RG) |
| clear coat | group, or none | none | a dielectric coat (F0 4%) over the surface: strength (scalar, 1), roughness (scalar, 0), its own highlights and reflections groups, normal-map amount (0: the mesh normal), its own specular anti-aliasing |
| multi-bounce occlusion | bool | off | occlusion brightened by interreflection on light surfaces (Jimenez's fit by albedo) |
| sheen | group, or none | none | Charlie sheen: `D = (2 + 1/α) sin(θh)^(1/α) / 2π`, `α = roughness²` (roughness at least 0.07), visibility `1 / (4(N·L + N·V − N·L N·V))`, × color × strength (scalar); the base layers give up the sheen's energy |
| glints | group, or none | none | sparkling flakes: a multi-scale grid of flakes on a UV (density per UV unit), each with a hashed position and normal, sparkling where its normal meets the half vector (flake roughness, size), filtered by the pixel's footprint so distant flakes average into a smooth lobe (roughness); flake color `lerp(color, base F0, base reflectance mix)`, varied toward rainbow hues; a mask keeps that fraction of the flakes; VR eye blend, ambient sparkle, vertex-light sparkles |

Stand-alone strand and toon highlights are highlight layers (4.8). Glints are sparkling flakes
in the specular response, as opposed to the glitter pattern layer (4.6). Not typed: highlights
limited to one kind of light, and a strength for specular anti-aliasing. **Sources:** Standard
Lite, Toon Standard, Unity Standard, Uber, Poiyomi.

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

**Sources:** Unity Standard, Uber, Poiyomi. *Specified with Unity Standard.*
Not specified: mask.

---

## 3. Lighting

### 3.1 Families
| Family | Look | Sources |
|---|---|---|
| **unlit** | base color as is | Uber shading off, Poiyomi unlit intensity |
| **flat** | uniform light, no shading across the surface: `base × (main light × shadow + ambient)`, each term times its contribution (3.4) | Toon Lit, Poiyomi Flat |
| **ramp toon** | a shading term mapped through a ramp to brightness and shade color | Toon Standard, Uber shadows, Poiyomi TextureRamp, Multilayer Math, ShadeMap, Wrapped, SDF |
| **physically based** | energy-conserving diffuse with a diffuse model, plus surface response | Standard Lite, Unity Standard, Uber, Poiyomi Realistic, Skin, Cloth, PBR |

### 3.2 Ramp toon
One formula for every source. The lit color is `base × L`:

```
b = lerp(1, min(1, ramp(term) × occlusion(direct) + shadow lift), strength × strength mask)
A = ambient light, darkened by occlusion (indirect)
D = direct light + lit ambient × A                               (the lit side)
S = C ⊙ (shadow direct × direct light × (ramp-scaled ? b : 1) × (direct occluded ? occlusion : 1)
         + shadow ambient × A) + untinted ambient × A            (the shadow side)
C = shadow tint ⊙ lerp(1, base, shadow color from base)        (bands can vary it, below)
L = lerp(S, D, b)
```

Each source is a special case: Toon Standard (tint = its shadow tint as gray, color from base
1, shadow direct 1, shadow ambient 0, untinted ambient 1, lit ambient 1, lift and occlusion in
`b`); Uber (the defaults: shadow ambient 1, lit ambient 1, the ramp keys carry its tint);
Poiyomi TextureRamp (tint, shadow direct = *ignore indirect shadow color*, ramp-scaled, direct
occluded, shadow ambient = 1 − that, lit ambient 0); Poiyomi Multilayer Math (bands, color
from base = *base color blend*, shadow direct = *ignore indirect shadow color*, shadow ambient
= 1 − that, lit ambient 0); Poiyomi SDF (the SDF term, the shadow as Multilayer's); Poiyomi
Wrapped (the wrapped term, keys from black to white for its gradient, tint, lit tint, shadow
ambient 1, lit ambient 1); Poiyomi ShadeMap (two bands replacing the base, shadow direct 1,
direct occluded, no ambient).

| Parameter | Values | Default | Notes |
|---|---|---|---|
| shading term | half-Lambert, Lambert, shadows only, relative brightness, wrapped, SDF face | half-Lambert | *half-Lambert* `N·L × 0.5 + 0.5`; *Lambert* `saturate(N·L)`; *shadows only* 1; *relative brightness*: where the point's light (half-Lambert × direct + ambient along the normal) sits between the darkest and brightest the light allows, by luminance, smoothstepped; *wrapped* (below); *SDF face* (below). Each times the received shadow |
| ramp | texture reference, color keys, bands, or none | none | sampled along u at the shading term; color. Keys are piecewise linear (position 0–1, color), for ramps described by numbers. None = white (fully lit everywhere) |
| ramp offset | −1–1 | 0 | added to the term before the ramp texture |
| ramp strength | 0–1, with a mask | 1 | `lerp(1, ramp, strength × mask)` |
| term mask | 1.4, or none | none | a painted mask multiplying N·L before the ramp, to draw shadows in |
| conditions | always, real-time light only, baked light only | always | when the ramp applies |
| dithered | bool | off | the shadow becomes a dither pattern in proportion to its darkness |
| ramp preset | none, flat, realistic, realistic soft, realistic very soft, two bands, three bands, four bands | none | names the ramp's shape, so a writer can use its own equivalent |
| shadow lift | 0–1 | 0 | |
| shadow tint | color input | white | `C` |
| shadow color from base | 0–1 | 0 | `C` toward the base color: shadows take a saturated version of it |
| shadow direct | 0–1 | 0 | how much direct light the shadow side keeps |
| ramp-scaled shadow direct | bool | off | that direct light × `b` |
| direct occluded | bool | off | that direct light × occlusion |
| shadow ambient | 0–1 | 1 | tinted ambient on the shadow side |
| untinted ambient | 0–1 | 0 | ambient added to the shadow side without the tint |
| lit ambient | 0–1 | 1 | ambient on the lit side |
| lit tint | color | white | tints the direct light on the lit side |

**Wrapped term:** the relative brightness of the wrapped light, `direct × saturate(wrap(N·L))`
plus the ambient wrapped alike, with `wrap(x) = (x + w) / ((1 + w)(1 + w × n))`, `w` the wrap
(0–2; above 1 it blends toward `1 − 0.85n`) and `n` the normalization (0–1).

**SDF face term:** a face shaded by a signed-distance texture, with forward and left directions
(object space). *Threshold* style: `b = smoothstep(a − blur × 0.1, a + blur × 0.1, sdf)`,
`a = 1 − (horizontal L·forward × 0.5 + 0.5)`, the texture mirrored when the light comes from the
left. *Blended* style: `saturate(L·forward × 0.5 + sdf × 0.5 + 0.25)`, with L and forward's
height scaled by a vertical blend, sdf from the red channel or, with the light on the other
side, the green; a softness (0–1) samples a blurrier mip. With *mask channels* the
map's blue blends the term toward half-Lambert and its alpha scales the shadow strength.
**Back face shadow** (0–1): back faces' term × `1 − it`.

**Bands** (Poiyomi Multilayer Math): `b` comes from the first band; the second and third change
`C` inside the shadow: `C = lerp(lerp(C₁, C₂, (1 − e₂) × a₂), C₃, (1 − e₃) × a₃)`. Each band:
color input (with a texture, faded toward white by its alpha, and a brightness addition), alpha `a`, border, blur, normal
strength (how much of the normal map its term uses), received shadow (0–1). Its edge
`e = (t − lo) / (hi − lo)` with `lo = border − blur/2`, `hi = border + blur/2` (each clamped to
0–1), anti-aliased;
*smooth edges* uses smoothstep instead. A **border color** band (the first band's edge
widened by a range) brightens `C` toward white × color. An **environment strength** (0–1)
lifts `C` toward the lit side by the ambient light. A band's color can be *from base* (times
the base color), and *bands replace base* makes the shadow color `C` instead of `base × C`.
With bands, each band's term uses its own normal strength and received shadow.

*Specified with Toon Standard; the one formula, offset and bands with Poiyomi
.* Not specified:
- **Bands:** a blur map, a border AO map, shadow strength mask types, a shade map's alpha as a
  shadow mask.

### 3.3 Physically based
GGX highlights and reflection probes, with the surface response (2.4).

| Parameter | Values | Default | Notes |
|---|---|---|---|
| diffuse model | Lambert, Disney, pre-integrated skin | Lambert | *Disney*: Burley's diffuse, which darkens or brightens edges with roughness, as Unity Standard on PC; *pre-integrated skin* (below) |
| Disney amount | 0–1 | 1 | Disney: blends from Lambert |
| shadow strength | 0–1 | 0 | the light × `lerp(1, term, s)`, the term as 3.2's |
| shadow tint | color | white | the light × `lerp(1, tint, (1 − term) × s)` |
| cloth | mask (1.4) or none, blend | none | sheen highlights (Charlie distribution, Neubelt visibility) where the mask is set, GGX elsewhere; *blend* mixes the two by the mask instead of switching at 0.5 |
| physical model | standard, calibrated | standard | *standard*: Unity Standard's; *calibrated* (Poiyomi 11's PBR type): Filament-style GGX with height-correlated Smith visibility, multi-scattering energy compensation from a DFG lookup, diffuse × `(1 − metallic)(1 − reflected energy)`, specular occlusion from N·V and occlusion, anisotropy as `α(1 ± a)`, and Charlie sheen |

**Pre-integrated skin:** a color lookup texture sampled at (half-Lambert, scattering × lerp(
luminance(light), 1, stability)) replaces N·L. Scattering = `saturate((1 − thickness)^power ×
scale × 2)`, thickness from a mask's red channel (power 1, scale 1). Absorption (a vector,
(−8, −40, −64)) tints by depth: `exp((1 − scattering) × absorption)`. A normal softness (0–1)
blends the diffuse normal toward the mesh normal. Its shadow side `S = lerp(A × tint, tint,
constant shadow) × occlusion` and the light `lerp(D, min(S + lookup × D × shadow, D), strength)`.

*Specified with Unity Standard; shadow strength, tint, skin and cloth with
Poiyomi.*

### 3.4 Shared lighting parameters
| Parameter | Values | Default | Notes |
|---|---|---|---|
| light color model | separate, merged probes, floored, split probes | separate | where the direct and ambient colors come from. *Separate*: direct from the main light, ambient from the probes. *Merged probes*: the probes' dominant light joins the direct light, keeping the total's luminance. *Floored* (with an intensity, 1): ambient = the brighter of the probes' average and their downward color, at least 0.05, times the intensity; direct = at least the ambient. *Split probes*: the probes split into a direct and an ambient part along their dominant direction, the direct part added to the main light |
| ambient model | average, directional | directional | *average*: the ambient light's average color, the same in every direction (spherical harmonics at order 0); *directional*: sampled along the normal |
| directional amount | 0–1 | 1 | directional: blends from the average |
| light direction model | light, probe-blended, split probes, view fallback, view, fixed | light | *light*: the main light's. *Probe-blended*: the main light's weighted by its luminance, plus the probes' dominant direction. *Split probes*: as that color model splits them. *View fallback*: the main light's, or between the view's forward and up without one. *View*: toward the camera, turned by a yaw and pitch (degrees). *Fixed*: a direction in world or object space |
| fixed light color | bool, color | off, white | replaces the direct light's color |
| received shadows | 0–1 | 1 | how much the main light's shadow darkens, `lerp(1, shadow, value)` |
| shadow softness | 0–1 | 0 | blurs the received shadow |
| clean shadows | bool | off | smooths the received shadow's stair-stepped edge |
| monochrome | 0–1 | 0 | the light toward its luminance (gray) |
| contribution: main light | 0–1 | 1 | the main directional light |
| contribution: additional lights | 0–1 | 1 | other per-pixel lights |
| contribution: vertex lights | 0–1 | 1 | Unity's four per-vertex lights |
| contribution: ambient | 0–1 | 1 | ambient and light probes, including lights Unity folds into them |
| without a main light: ambient as main light | ≥ 0 | 0 | in a world lit by probes alone, in the *separate* light color model: the ambient light × this stands in for the main light (Toon Lit: 0.2; Poiyomi's Standard light color: 0.6; Toon Standard: 1; Uber's baked direct contribution). The other light color models make their direct light of the probes themselves |
| without a main light: stand-in | ambient, brightest | ambient | which ambient light stands in. *Ambient*: as the surface gets it (the ambient model's). *Brightest*: the probes along their dominant direction (Toon Standard) |
| without a main light: ambient | 0–1 | 1 | the ambient contribution then, instead of *contribution: ambient* (Poiyomi's Standard light color: 0.5; Uber's baked indirect contribution) |
| limit brightness | bool, ceiling | off, 1 | clamps the direct and the ambient light, each per channel, to the ceiling, so bright worlds don't blow out colors |
| limit applies to | each light, the shaded light | each light | *the shaded light*: the limit clamps the direct light through the shading + the ambient light instead, so the lit side never passes the ceiling (Toon Standard) |
| limit by luminance | 0–1 | 0 | the direct light's limit blends from per channel to scaling by luminance, keeping the hue |
| limit keeps hue | bool | off | the limit scales the color so its brightest channel is at the ceiling |
| minimum light | 0–1 | 0 | raises the luminance of the direct and of the ambient light to at least this, keeping the hue, before the limit |
| minimum brightness | 0–1 | 0 | `base × value` is added after lighting, like emission, so the surface never goes fully dark |
| light multiplier, light addition | floats | 1, 0 | every light color (direct, ambient, additional) × multiplier + addition, at least 0, after the minimum light and the limit on each light |
| flat light map | bool | off | *flat* family: features reading the shading's light map see `smoothstep(0.4, 0.6, half-Lambert)` (the vertex normal's for additional lights) instead of 1 |

**Additional lights** (point, spot, and directional lights beyond the main one):

| Parameter | Values | Default | Notes |
|---|---|---|---|
| shading | same as main, Lambert, toon | same as main | *toon*: `smoothstep(end, start, 1 − half-Lambert)` between the light and its passthrough, with a gradient start (0) and end (0.5) |
| limit brightness | bool, ceiling, keeps hue | off, 1, off | clamps each light's luminance, or its brightest channel when keeping the hue |
| received shadows | 0–1 | 1 | |
| monochrome | 0–1 | 0 | |
| passthrough | 0–1 | 0 | the light's share that lights the surface regardless of direction, like ambient |
| falloff | four-key curve | linear | remaps the distance falloff (values at 0, ⅓, ⅔, 1) |
| directional | bool | on | directional lights beyond the main one |
| vertex lights in mirrors | bool | on | |
| combine | add, max | add | how each light joins the surface: added, or per channel the brighter of the two (Poiyomi's default); with *max* the alpha in that pass × a boost (10), saturated |

*Specified with Toon Lit; brightness limit and minimum with Toon Standard
; a fixed world direction with Uber; the color and direction models, received
shadow strength, softness, monochrome, the limits and additional lights with Poiyomi.*
Nothing left unspecified here.

### 3.5 External lighting
VRChat light volumes (strength, specular, bias) and LTCGI (diffuse and specular tint,
strength). **Sources:** Uber, Poiyomi.

**Light volumes, specified**:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | off | the world's light volumes replace the light probes |
| strength | 0–1 | 1 | blends from the probes to the volumes |
| specular | bool, strength | off, 1 | highlights from the volumes |
| bias | float | 0 | offsets the sample point along the normal |
| specular lights | count | 0 | only the first this many volume lights add highlights; 0: all |

**LTCGI, specified**:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | off | area lights from the world's video screens |
| diffuse, specular tint | color | white | |
| diffuse, specular strength | 0–2 | 1 | |
| use surface response | bool | on | the surface's metallic and roughness; otherwise its own metallic (0) and roughness (1) |
| exposure occlusion | bool | off | the reflections' exposure occlusion applies |

### 3.6 Screen-space lighting effects
Darkening from the depth texture, which avatars only have with a depth-providing prefab:
ambient occlusion (radius, intensity, color, mask) and contact shadows (light source,
distance, thickness, color, mask). **Sources:** Poiyomi Pro.

**Specified** as two pattern kinds (4.6), since each blends a color into
the base color with a mask and can write the mask bus:
- **Ambient occlusion:** darkening where the depth texture shows nearby geometry around the
  pixel: samples within a radius (a quality's worth, jittered, biased by depth and cone,
  rejected by the normal), × intensity, faded with camera distance (a quality falloff and a
  far-sample falloff), hidden near the camera, by alpha and in light. It blends the layer's
  color (× a color map, or the map read as a ramp by the occlusion; HSVG offsets; × the base
  color by a mix) by the layer's blend.
- **Contact shadow:** a short ray marched through the depth texture toward the light (the main
  light, or a direction in world, object or view space) up to a distance in quality steps;
  geometry in front within a thickness shadows it, × intensity, dithered, faded by camera
  distance and in light. It blends the layer's color into the base color and/or multiplies
  the shading's light map.

---

## 4. Layers

Eight layer types. All share: source, mask (1.4), blend (1.7) with opacity, placement (1.8)
with emission amount, color adjustment (1.10) and modulators (1.9).

### 4.1 Surface layer
A color or texture applied to one or more surface inputs. It covers what the sources call
detail, decal, flipbook, recolor or color mask, back face, and emission.

| Part | Values |
|---|---|
| source | color input (with placement and frames from 1.1), the base color itself, or a palette entry |
| targets | one or more of: base color, alpha, normal, metallic, roughness, occlusion, emission; each with its own blend and opacity |
| mask | any mask, including view conditions: back face, in mirror |

**How sources map:**
- **Detail:** a tiled source targeting color (multiply ×2), normal, metallic, roughness,
  occlusion.
- **Decal or flipbook:** a placed source, optionally with frames.
- **Recolor region** (Toon Standard color mask, Uber team colors, Poiyomi RGBA color
  masking): a layer per mask channel, color blend *tint luminance* or *add to luminance*,
  plus emission, normal and surface targets.
- **Back face:** a layer masked by the back-face condition.
- **Emission:** a layer targeting emission. "Use base color" is *source times base color*;
  "override base color" is *overrides base color*;
  blinking, scrolling, center-out and light-based glow are modulators.

**Parameters**:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | on | |
| mask | 1.4, or none | none | none: everywhere |
| placement | 1.8 | surface | a layer targeting only emission uses *emission* |
| faces | both, front, back | both | which side of the triangles it shows on (back face) |
| mirror | both, mirror only, not in mirror | both | where it shows (Poiyomi's mirror texture) |
| on outlines | bool | off | it also shows on the outline |
| emission amount | ≥ 0 | 0 | |
| hide in shadow | 0–1 | 0 | the mask × `lerp(1, shading brightness, amount)`, the brightness being the ramp × light map |
| source | color input | white | |
| source times base color | bool | off | "use base color": the surface color at the end of S1, after the surface layers, matcaps and rims included (Poiyomi applies them before its emissions read it) |
| replaces emission | bool | off | the emission below is replaced where the layer covers: `e × (1 − coverage) + its own` (decals) |
| overrides base color | bool | off | an emission layer's emission `e` also replaces the base color: `lerp(b, saturate(e), max(e))` |
| targets | list of: target, blend (1.7), opacity, channel, invert channel, replace normal | — | opacity 0–1, or any amount ≥ 0 for blend *add*; *normal* targets composite by whiteout, opacity being the strength, or with *replace normal* lerp toward the layer's normal by the mask; one-value targets read the source's *channel* (`1 − channel` when inverted) |
| source alpha is opacity | bool | off | the source's alpha multiplies every target's opacity (alpha-blended details and decals), clamped to 0–1 |
| source alpha offset | −1–1 | 0 | added to the source alpha first, saturated |
| screen | a video screen (4.6), or none | none | the source goes through it first |
| emission times alpha | bool | off | the emission it adds × the surface's alpha (premultiplied transparency) |
| source intensity is alpha | bool | off | the map's alpha becomes `max(r, g, b)` |
| source map is mask | bool | off | only the map's alpha is used: the color shows where it covers |

A source with different textures per target (a detail color map and a detail normal map) is
two layers with the same mask. *Luminance* in the blends is `0.3 r + 0.59 g + 0.11 b`.

**Bakeable** when static and placed in UV space. **Sources:** all except Toon Lit.

### 4.2 Transition
A second appearance revealed by a threshold.
- **Threshold source:** texture, noise, point to point, sphere, center out; amount, softness.
- **Edge:** up to three colors, widths, emission.
- **Revealed appearance:** transparent, or a color and texture.
- **Modes:** wave, continuous.

Structurally a surface layer whose mask is a threshold with an edge band. Kept separate
(decided) because its *amount* is the thing that gets animated.

**Specified**, besides the shared layer parts:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | texture, point to point, sphere, center out | texture | the threshold `g` |
| threshold | texture reference, channel, invert | none | none: 1 everywhere |
| point to point | space (object, world, vertex color, UV), start, end | object, (0, −1, 0), (0, 1, 0) | `saturate(dot(p − start, direction) / length)` |
| sphere | center, radius | 0, 1.5 | `saturate(distance(object position, center) / radius)` |
| center out | mode (view, direction, light), direction, pixel normal amount, linear | view | `abs(N·V)`, `N·dir × 0.5 + 0.5` or `N·L × 0.5 + 0.5`; as an angle when linear; `1 − it` unless inverted |
| amount | 0–1 | 0 | where the threshold is below the amount, the revealed appearance replaces the base color |
| softness | 0–1 | 0 | dissolved `d = 1 − smoothstep(a′ − s, a′ + s, g)`, `s = softness / 2`; 0 is a hard edge |
| padded | bool | off | `a′ = lerp(−pad, 1 + pad, amount)`, `pad = 2s + edge width + detail strength / 2`, so 0 and 1 are fully in and out |
| detail noise | texture reference, invert, strength, edge only | none | shifts the boundary by `(noise − 0.5) × strength`, near the edge only by *edge only* |
| wave | bool, width, bounds (edge, center, loop) | off | a band of the width around the amount instead of everything below it |
| revealed | color input, emission | white, 0 | replaces the surface color and alpha by `d`, plus emission |
| alpha only | bool, curve | off, (1, 0, 0, 1) | the alpha × `lerp(x, y, smoothstep(z, w, d))` instead |
| edge width | 0–1 | 0 | a band just above the amount, `0 < threshold − amount ≤ width`; on the dissolved side when *edge flip* |
| edge color | linear color | white | added after lighting, like emission; with *edge replaces color* the surface color becomes it, plus *edge emission* × it |

*Specified with Uber; the sources, softness, padding, detail, wave, alpha only
and the edge options with Poiyomi.* The mask scales the amount. **Sources:** Uber
base color dissolve, Poiyomi dissolve.

### 4.3 Lookup layer
A texture looked up by direction instead of by UV.
- **Texture:** 2D (matcap) or cube.
- **Projection:** view normal (VR-stabilized or not), top pinch, double-sided, gradient,
  light direction, reflection direction, world or local normal, skybox; rotation and spin for
  cubes.
- **Options:** blur or roughness, own normal map, base and light color mix, hide in shadow,
  probe fallback.

**Specified**, besides the shared layer parts:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| source | color input | white | the texture is looked up by the projection, not by UV |
| projection | view stabilized, view corrected, reflection planar, gradient, light half vector; for cubes view direction, reflection, world normal, object normal | view stabilized | *view stabilized*: the normal in view space with world up kept up, so the lookup doesn't roll with the head (VR); *view corrected*: the view-space normal corrected for the view direction, so it doesn't skew at the edges; *reflection planar*: the view reflected about the normal, its world x and y (the same on both sides); *gradient*: `u = v = 1 − abs(N·V)`; *light half vector*: the half vector of light and view in a tangent frame around the normal; *view direction*: a cubemap sampled along the direction from the camera to the surface, like a window into it; *reflection*, *world normal*, *object normal*: a cubemap sampled along those |
| rotation, spin | 3 × degrees, 3 × degrees per second | 0, 0 | view direction: rotations of the xy, xz and yz planes, applied in that order |
| center eye | bool | off | both eyes use the view from between them |
| tiling | 2 × float | (1, 1) | scales the lookup coordinate: `uv = projection × 0.5 × tiling + 0.5` |
| lookup rotation | degrees | 0 | turns the 2D lookup around its center |
| UV blend | 2 × 0–1, a UV channel | 0, UV1 | blends the lookup coordinate toward a mesh UV, per axis |
| mirror flip | bool | off | `u` flips in mirrors |
| surface normal amount | 0–1 | 1 | how much of the surface's normal map the lookup follows (0: the mesh normal) |
| base color mix, light color mix | 0–1 | 0 | the color × `lerp(1, base, mix)` and × `lerp(1, direct light color, mix)` |
| hide with reflection probes | bool | off | hidden where the scene has reflection probes (a fallback for them) |
| circle mask | bool, radius, blur | off, 0.45, 0.1 | fades outside a circle of the lookup: `1 − edge(distance to the center, radius, blur)` |
| blur | 0–1, or the surface roughness | 0 | mip blur as roughness |
| source alpha is opacity | bool | off | the source's alpha multiplies the opacity |
| light response | ignore light, scale by light | ignore light | |
| normal map | texture reference, strength, with surface | none | its own normal for the lookup; *with surface*: whiteout-blended with the surface normal |
| blend, opacity | 1.7, 0–1 | add, 1 | placement *surface* blends into the base color (lit and shadowed); later placements into the lit color |

Several blends at once (Poiyomi's weights) are several layers with the same source and mask,
in the source's order.

*Specified with Toon Standard; the other projections and options with Poiyomi
.* **Sources:** Toon Standard, Uber, Poiyomi (matcaps, cubemap).

### 4.4 Angle layer
Color driven by how the surface faces the viewer or the light.
- **Edge source:** view angle, depth (screen-space silhouette), intersection with other
  geometry.
- **Profile:** a band (width, softness, power, invert) for rims, or a full gradient over the
  angle (a ramp or hue rotation) for iridescence.
- **Color source:** color input, the environment (reflection probe), base color mix.
- **Light response:** ignore light, scale by light, lit side only, a second color on the dark
  side, back light (shines where the light is behind).

**Specified**, besides the shared layer parts (placement *overlay*):

| Parameter | Values | Default | Notes |
|---|---|---|---|
| edge source | view angle, depth, backlight | view angle | *view angle*: `e = 1 − abs(view · normal)`, the view from between the eyes; *depth* and *backlight* below. For those two the step profile acts on their `e` directly (its base and power unused) |
| depth | samples (2, 4, 8), width, sharpness, threshold, binary, fixed size distance, fade near and far | 8, 0.08, 0.5, 0.25, on, 0.5, 0, 0 | a silhouette from the screen's depth: samples around the pixel (2 along the normal, or 4 or 8 around it) at `width × 0.1` world units, scaled up within the fixed size distance; the rim where a sample lies `threshold` farther (a smoothstep from `threshold × sharpness`, or a step when binary, which also steps the mask and fade at 0.5), times how sideways the normal faces unless binary; faded between fade far and fade near (far 0: no fade). Needs a depth texture |
| backlight | directivity, view strength, received shadows | 5, 1, on | `e = (normalize(L − V × view strength) · N) × 0.5 + 0.5`, times the received shadow; the result × `saturate(0.5 − (V·L) × 0.5)^directivity` |
| light side | mode (none, mask, scale, subtract), low, high, smooth, invert, strength, level, before step, shrink in shadow | none | `h` = half-Lambert (`1 − h` when inverted), `w = linstep(low, high, h)` (smoothstep when smooth); *mask*: `× lerp(1, w, strength)`; *scale*: `× w × strength`; *subtract*: `saturate(value − (1 − h) − level)`; on the angle term before the step, or on the result. *Shrink in shadow* adds `(1 − h) × 3 × amount` to the term before the step. A rim's dark-side color (UTS2 antipodean, lilToon indirect) is a second layer with an inverted light side |
| profile | band, power band, hue rotation, ramp, step | band | band: `smoothstep((1 − width) − softness, (1 − width) + softness, e)`; power band: `smoothstep(edge, 1 − edge, e^power)`; hue rotation: the color below rotated by `frac(power band)` turns (HSV); ramp: the color below × a ramp texture at `e`; step (below) |
| step | base facing or edge, linear angle, power, low, high, falling, smooth | edge, off, 5, 0, 1, off, off | `x = base^power`, the base *facing* `abs(N·V)` or *edge* `1 − abs(N·V)` (as angles, `1 − angle/90°` and `angle/90°`, with *linear angle*); a linear step from low to high (hard when equal; smoothstep when *smooth*), its complement when *falling*. Poiyomi's three rim styles are each a step |
| ramp | texture reference | none | ramp profile |
| specular only | bool | off | acts on highlights and reflections only, not the whole lit color (iridescence) |
| scale by metallic | bool | off | the opacity times the metallic value |
| width, softness | 0–1 | 0.3, 0.1 | band |
| power, edge | float, 0–0.5 | 5, 0 | power band |
| color | color input | white | |
| color source | color, environment, environment along normal | color | *environment*: the reflection probe along the reflection, times the color, with an optional roughness override; *environment along normal*: the probe along the normal, less a threshold (not below 0), times the color |
| base color mix | 0–1 | 0 | multiplies by `lerp(1, base, mix)` |
| light color mix | 0–1 | 0 | multiplies by `lerp(1, direct light color, mix)` |
| light response | ignore light, scale by light, scale by direct light | ignore light | *scale by light*: times (main light color + ambient); *scale by direct light*: times the main light's color |
| source alpha is opacity | bool | off | the color's alpha multiplies the opacity |
| anti-aliasing | 0–1 | 1 | the step's range widens by this × the value's change across a pixel |
| times alpha | bool | off | × the surface's alpha (fade and transparent surfaces) |
| surface normal amount | 0–1 | 1 | how much of the surface's normal map the angle follows (0: the mesh normal) |
| per eye | bool | off | each eye uses its own view instead of the view from between them |
| blend, opacity | 1.7, 0–1 | add, 1 | |

*Specified with Toon Standard; the step profile and the options after band with
Poiyomi.* Not specified: a bias map; a light-direction factor from
screen-space directions.

**Sources:** Toon Standard rim, Uber basic and environment rim and iridescence, Poiyomi rims,
depth rim, environmental rim, Depth FX, backlights.

### 4.5 Subsurface
Light through thin parts: thickness texture, color, base color tint, direct and indirect
strength, falloff, distortion, wrap, shadow strength. S3. **Sources:** Uber, Poiyomi.

**Specified**, besides the shared layer parts (placement *lit*):
`light = (pow(saturate(view · −(L + N × distortion)), power) × intensity / π + ambient) ×
pow(saturate(1 − thickness − offset), thickness power) × the light × color`. The light is
*direct and ambient*: `(direct + ambient light)`, times the received shadow when there's a
real-time light; or *direct*: `lerp(1, direct light color, light color mix) × lerp(1, light
map, shadow strength)` (Poiyomi).

| Parameter | Values | Default |
|---|---|---|
| color | color input | white |
| base color mix | 0–1 | 0 |
| thickness | texture reference, channel, offset, power | none, red, 0, 1 |
| intensity, power, distortion, ambient | float | 1, 1, 1, 0 |
| light | direct and ambient, direct; light color mix, shadow strength | direct and ambient; 0, 1 |

### 4.6 Procedural pattern
A generated pattern with a **kind** and its parameters: glitter, voronoi, truchet, pathing,
internal parallax, video screen, audio spectrum, audio volume color, oscilloscope,
constellation, lava lamp. **Sources:** Poiyomi, Poiyomi Pro, Uber oscilloscope.

**Oscilloscope, specified**: AudioLink's waveform drawn as a line,
`saturate(1 − 50 × abs(wave(u′) − (2v′ − 1)))`, with `u′, v′` the UV scaled, offset and
rotated (degrees), inside margins (a u range and a v range), × color × opacity. Placement
*overlay*, blend *add*.

**Glitter, specified** (Poiyomi's Glitter / Sparkle): the UV (+ scroll × t)
× density makes cells; each holds a particle at a random point (× *random position*) with a
random seed; only the nearest particle draws, or with *overlap* every one that reaches; *layers*
(1–4) repeats the grid with other seeds. A particle's coverage `a` is its shape (circle or
square) at its size (cell units; random in a range when *random size*), anti-aliased; the
linear mode's circle compares the squared distance with the size. Its sparkle `s` (time
`τ = t / 20 + sparkle offset`):

| Mode | `s` |
|---|---|
| angle | `(min × a + pow(saturate(dot(lerp(n′, V, bias), V)), contrast)) × a`, `n′` the normal (mesh lerped to pixel by *normal amount*) turned per axis by `sin((τ + r π) × (1 + speed)) × angle range` degrees, `r` random per particle (fixed random angles when the speed is 0) |
| light | as angle against the light's half vector, `× N·L` with `a`, the color × the direct light color |
| linear | a random normal swept as `abs(frac(r × 14.274 + τ × speed) × 2 − 1) × 2 − 1`, its dot with the camera direction (forward lerped to the view by *VR parallax*) through `saturate(1 − ((abs(frac(d k + k) − 0.5) × 4 − 1) c + c))` (sensitivity `k`, contrast `c`), to the power *post contrast*, × `saturate(N·H × angle limit + 1 − angle limit)` (`H` from the camera and the light × *light direction*); `max(min, that) × a` |

The color is the layer's color × `lerp(1, base, base color mix)` × the color map × the shape
texture (sampled over the particle's square with its own tiling; pixel sampling; frames as a
sprite sheet, random per particle or by time with a random start) × an HSV color of random hue
(saturation and value in ranges) with *random colors*; a hue adjustment on the layer adds the
particle's random hue with *hue random offset*. The texture turns by `τ′ × speed` turns
(`τ′ = t / 20 + rotation offset`, the speed a constant plus a random one in a range, a random
start turn and direction optional). The mask is the layer's mask × opacity (the color's alpha)
× the hide in shadow term × `lerp(1, direct light luminance, light scale)`. With brightness `b`
(the max brightness): *add* (placement emission) adds `color × b × s × texture alpha × mask` as
emission; *replace* (placement surface) sets the base color to `lerp(base, s × color × b,
s × texture alpha × mask)` and adds `s × color × max(0, (b − 1) × texture alpha) × mask` as
emission. *Distance LOD* merges distant cells; *jaggy fix* widens a square's anti-aliasing by
distance² × amount.

**Voronoi, specified**: `p = (position + offset) × scale + scroll × t`
(cell units; the position in object space, world space or a UV), `v = pow(distance, power)`
with the distance by type: *2D cells* the distance to the cell's edge on a 2D grid × 1.55,
*3D difference* `sqrt(F2) − sqrt(F1)` of 3D cellular noise at `p × 0.7`, *3D cells* the
distance to the edge on a 3D grid × 1.8. `ramp = smoothstep(gradient.x + n, gradient.y + n, v)`,
`n` a noise texture's channel × intensity. `c = lerp(outer, inner, ramp)` (colors with alpha;
the inner color is a random HSV color per cell, saturation and value in ranges, with *random
cell color*). Unless *emission only*: base color `lerp(base, c.rgb, mask × c.a)`; with *affects
alpha*, `alpha = lerp(alpha, c.a, mask)`. Emission adds `c.rgb × lerp(outer emission, inner
emission, ramp) × mask × c.a`. With Poiyomi 11: *cell motion* moves each cell's point, `o = lerp(o, 0.5 + 0.5 sin(speed × t × (0.6 +
0.8 o.yx) + 2π o), amount)`; *AudioLink cells*: each cell listens to a band (or a random one)
at a random moment of its recent history, the inner color × `lerp(1 − dim, 1, level)` and
the inner emission + emission × level; a hue adjustment on the layer shifts the resolved
color.

**Truchet, specified**: `q = UV × density + scroll × t + offset`; each
cell, flipped in u at random, holds two quarter-circle arcs of radius 0.5 around opposite
corners. With `e = thickness − abs(distance to the corner − 0.5)`, the coverage is
`saturate(e / fwidth) × smoothstep(0, 1 − edge sharpness, e)` × the mask. A texture (null:
white) is sampled along the path: u the angle along the arc (alternating per checker cell), v
across the line, + a texture offset. With `k = min(coverage, texture alpha) × opacity`: base
`lerp(base, blend(base, texture × color), k)`; emission adds `texture × color × k × emission
amount`; *override alpha* multiplies the alpha by `k`. *Hide without AudioLink* draws nothing
while AudioLink is missing.

**Constellation, specified** (Poiyomi Pro): a star field on a grid,
`q = (UV − center) × density + scroll / density × t + offset`, drawn four times at depths
`z = frac(i / 4 + phase)` (back 0, front 1), each scaled by `lerp(10, 0.5, z)` and weighted by
`lerp(layer fade min, max, z)`; a *depth* above 0 sinks the copies below the surface along the
tangent-space view, the back ones deepest. Each cell holds one star at a random point that
drifts (*star movement*, at time × *star speed* + a movement offset), sized by *star size*
(random magnitudes with *star variation*), twinkling at time × *twinkle speed* + a twinkle
offset, × *star brightness*. Lines join neighboring stars (a random share of the links set by
*line density*), *line thickness* thick and thinner toward the ends by *thickness variation*,
× *line brightness*. The coverage is the copies' weighted sum, at most 1, × the mask. The
color is the layer's color, or with *random colors* one per star (a random hue + a hue
offset, saturation and value in ranges; or one of AudioLink's theme colors, jittered slightly
per star), blended along the lines. With `c = color × coverage`, the layer's blend (replace,
multiply or add) puts `c` on the base color with weight coverage, and emission adds
`c × coverage × emission amount`.

**Skin tone, specified** (Poiyomi 11's Skin section): a skin color from
pigments: melanin, its redness and hemoglobin pick a color from a three-panel lookup strip
(redness chooses between the panels, blended; melanin runs along a panel, kept off its edges;
v is `1 − hemoglobin`), white without one. *Freckles* add `strength × mask^sharpness` to the
melanin and that × a redness shift to its redness; a *tan* adds an amount × its mask to the
melanin and a redness shift × the mask to the redness (each mask a channel of a detail map on
its own UV and tiling). The layer's color (tint × brightness) multiplies it; the blend is
*replace* or *multiply*, by an amount, and the mask (the detail map's red channel) says where it
doesn't apply.

**Lava lamp, specified** (Poiyomi Pro Grab Pass): blobs rising and sinking
in a liquid inside the mesh, drawn unlit where the surface is see-through (opacity × mask ×
`1 − alpha`, placement *surface*, right after the refraction layer), its glow added as emission.
- **Container:** the view ray, bent where it enters front faces (a refractive index, around a
  normal between the vertex and pixel normal), runs through the mesh for its thickness, marched
  through a baked signed distance volume (corner, size and voxel size in object space; without
  one, a sphere-like guess), at least a minimum thickness. With *bind data* (on, or automatic
  when the mesh carries baked bind positions) the ray runs in the bind pose, distances divided
  by a world rescale; otherwise in object space around the origin.
- **Blobs:** cells along a flow direction, a scale wide and a vertical spacing tall, in four
  offset layers. A cell holds a blob or not (a skip chance), of a random size between a minimum
  and a maximum share of the room left by a padding (skewed by a size distribution), scrolling
  up or down at a random speed in a range and drifting sideways on an ellipse at a random drift
  speed in a range. Shapes: spheres, or slowly turning crosses and rings, stars or hearts. The
  blobs and the reservoirs at a bottom and a top height melt together by a smoothing; the march
  takes up to a step count.
- **Shading:** the blob color (optionally blended with a matcap: border, intensity, alpha,
  blend) × the reservoir tint (the bottom to the top color by height), lit by the main light
  with wrap lighting (translucency) and a GGX highlight (roughness, reflectiveness). The
  reservoirs glow (each color × its emission, fading over a color range of the span) onto the
  blobs, through them (absorbed by the blob color's complement, × translucency) and as
  reflections. The liquid hazes (haze color × the tint, a density) and tints (a tint color and
  strength) with distance, in front of the blobs and the surface behind them.
- **AudioLink:** a band's level scales the blobs and the glow between two values each (the first
  without AudioLink), and its chronotensity speeds the flow between two speeds.

**Path, specified**: four lines (R, G, B, A), each along a gradient `g`
(the map's channel, or the average of all four when *merged*; or a UV axis per line, the map's
channels then masking the lines; `g = 0` is never drawn). Per line, time
`τ = frac((manual ? time : t × speed) + offset)`, stepped to `(ceil(τ n) − 0.5) / n` with `n`
segments; with *remapping*, `g` outside its range isn't drawn and is rescaled to 0–1. With
`w = length / 2` and `s = softness`:

| Type | Line |
|---|---|
| fill | `smoothstep(τ(1 + s), τ(1 + s) − s, g)`; `step(g, τ)` when `s` is 0 |
| path | `smoothstep(0, s, saturate(1 − abs(lerp(−w, 1 + w, τ) − g) / w))` |
| loop | `smoothstep(0, s, saturate(1 − min(abs(τ − g), 1 − abs(τ − g)) / w))` |
| dashed | dashes of the length with gaps between, `frac(g / (length + gap) − τ)`, their edges softened by `s × 0.5` of the dash |
| off | nothing |

Each line's alpha blends with a mask bus channel. The lines' colors are composited over each
other in order R, G, B, A (each alpha the line × its color's alpha), times the color map
(and its alpha); the layer's blend puts the result on the base color: *replace*
`base (1 − a) + C`, *add* `base + C`, *multiply* `base × (C + 1 − a)` (`C` premultiplied).
Emission adds each line × its color × its emission × the color map × its alpha. *Override
alpha* sets the alpha to `a`.

With Poiyomi 11: a line can read another channel of the map as its gradient; its
time can *ping-pong* (`τ` runs 0 → 1 → 0, the travel direction flipping at the turn) and be
eased (`τ²`, `1 − (1 − τ)²`, smoothstep); path, loop and dashed lines can soften their leading
and trailing edges apart (head and tail softness); each line can be masked by a channel of a
packed mask map; *overlap* combines the lines over each other (layered), adding their colors
(additive) or keeping only the brightest; *anti-aliasing* softens edges by the gradient's
change across a pixel; the color map's alpha masking every line can be turned off.

**Internal parallax, specified**: levels of a placed texture seen below
the surface. For levels `j = n − 1` down to 0, `r = j / (n − 1)`: the placed UV is offset by
`(depth × o + scroll × t + r × per-level offset) / the map's tiling`, `depth = lerp(min depth,
max depth, r)`, `o = −V.xy × 1.3 / (V.z ± 0.3)` from the tangent-space view; clamped to 0–1
unless *tiled*. The level's tint `lerp(min color, max color, r)` (× the map's color with
*height from alpha*), hue shifted by `frac(r × hue per level)`, × `pow(lerp(min fade, max fade,
r), 2.2)`; its height is the map's alpha (*height from alpha*) or red. *Basic*: each level adds
tint × height (or the brightest wins, *level max*); *heightmap*: a level shows where its height
≥ `1 − r`, nearer levels in front. `n = round(lerp(iterations, min iterations, saturate((object
distance − near) / (far − near))))`. The result goes on the base color by the layer's blend ×
mask, and adds result × emission amount × mask as emission. With Poiyomi 11: mip
maps; *smooth layers* (about one sample per pixel the layers shift, up to a step count);
*add to alpha* (`alpha + saturate(max(r, g, b)) × amount × mask`); *refraction* (the view bent
by Snell's law, IOR and normal-map influence, before it offsets the levels); *depth fog* (each
level toward the fog color by `1 − exp(−density × sqrt(1 + |o|²) × depth / max depth)`).

**Depth, specified**: `d` is the distance from the surface to the scene
point behind it, from the camera's depth texture. With color on, `k = lerp(values.x, values.y,
saturate((d − range.x) / (range.y − range.x))) × mask` and `c` = the layer's color × a texture
(sampled at the depth remap with *along depth*): *replace* `lerp(base, c, k)`, *multiply*
`base × lerp(1, c, k)`, *add* `saturate(base + c × k)`; emission adds `c × k × emission amount`.
With alpha on, `alpha ×= lerp(alpha, a, mask)`, `a` the same remap over the alpha range and
values.

**Audio volume color, specified**: `v` = AudioLink's filtered spectrum
at a UV's u (or v) across its bins; `c = lerp(lerp(low, mid, saturate(2v)), high,
saturate(2v − 1))`, the emission the same with each color × its emission; `c` goes on the
base color by the layer's blend and opacity. Nothing without AudioLink.

**Video screen, specified**: the base color `c` shown as a screen. Grade:
`c = lerp(c, luminance(c), −saturation)`, then `saturate(lerp(0.5, c, contrast + 1))` in gamma.
`p` is a pixel texture repeated once per screen pixel (the resolution). By type: *LCD*
`max(c, backlight / 100)`, graded, × `p` × backlight; *TN* as LCD, the view angle shifting the
contrast (along the bitangent) and the hue (toward `gbr`, along the tangent) before the grade;
*CRT* graded × `p` × backlight × the afterglow of a beam sweeping the screen's pixels refresh
rate times a second (`saturate(fade time − the time since it passed, as a fraction of a
sweep)`); *OLED* graded × `p` × backlight; *Gameboy* the graded brightness (gamma luminance)
through a ramp (when its larger side exceeds 16 pixels) or from dark to light green;
*Projector* graded, each channel at least itself × backlight. The base color becomes
`lerp(base, screen, mask)`; with *emission*, emission adds `screen × mask`. *Pixelate* samples
the base color texture once per screen pixel.

**Audio spectrum, specified**: AudioLink's spectrum drawn in a placed
area (1.1 placement, without a texture). In the area, u is the band and v the volume, or with
*circle* the angle and the radius, remapped by the circle dimensions. The volume and the band
are stepped (0: smooth) and clipped to ranges; *mirror bands* folds the bands
(`1 − abs(2b − 1)` or `abs(2b − 1)`); *shape clip* leaves gaps between steps; *bilateral*
measures the volume from the middle. The level is `tanh(spectrum × volume × lerp(bass boost,
treble boost, band))`, the spectrum (filtered) read over the first *band range* of
AudioLink's four bands. A pixel is covered where the level is above its volume, or within the
line width of it, × the color mask's alpha. The color is ColorChord's at the band, or
`lerp(lerp(low, mid, saturate(2s)), high, saturate(2s − 1))` with `s` the band, the volume or
the level, × the color mask; the emission the same with each color × its emission. Through a
video screen when one is set, whose emission then replaces the emission by the coverage. The
color goes on the base color by the layer's blend × opacity × coverage; `alpha =
lerp(alpha, alpha × coverage, override alpha)`. Nothing without AudioLink.

With Poiyomi 11: the level is `lerp(saturate(x), tanh(x), compression)` (1: as
before); the spectrum row is the filtered one, the raw one (responsive) or the world's EQ; the
bars span the DFT's first band range or, with *frequency mapping*, logarithmically from a
minimum to a maximum frequency (Hz), sampled at a point, averaged or peaked over four; the area
can flip in u and v; the circle can be an arc (inner and outer radius, start angle, degrees);
the display is a line, filled, or a smooth anti-aliased line of a thickness in pixels; the lit
part can be stepped; the color mode adds a gradient ramp (with its emission) sampled by the
color source; the color mask can follow the spectrum's area; a master opacity, an emission
multiplier and *tint emission* (× the color mask) apply at the end.

One layer type (decided). Each kind gets its own typed parameters when a parser types it;
until then, a material using that kind gets an unmapped report line. Adding a kind never
changes the layer type.

### 4.7 Refraction
Bends what's behind a transparent surface: strength (IOR), tint, color split, blur, opacity;
with screen-grab extras (pixelation, color ramp). **Sources:** Uber, Poiyomi Pro Grab Pass and
Self Grab.

**Specified** with Uber and Poiyomi's Grab Pass,
besides the shared layer parts. Placement *lit* or *overlay* blends the lit surface with the
screen. Placement *surface* (Poiyomi) blends the screen into the base color at the layer's
place in the list, so the layers after it draw on top, and the screen's part stays exempt from
lighting. The steps, in order:

1. **Refraction** (when on): the screen read offset by `(IOR − 1) × the view-space normal ×
   (1 − N·V)`, divided by `depth + 1`, or with *field-of-view scale* times the projection's
   field-of-view scale and divided by `max(camera distance, 1)`; times `(1 − N·V)^fresnel power`
   when that isn't 0; times the refraction mask. *Ignore foreground* drops the offset where it
   would land on something in front of the surface. *Chromatic aberration* spreads the reads
   along the offset × its strength, weighted red to blue over a spectrum.
2. **Blur:** the screen averaged around the point, its radius `strength × 0.1` meters seen at
   `lerp(1, camera distance, camera falloff)` meters, × `lerp(1, roughness, by roughness)`, × the
   mask; up to *quality*² samples in a pattern: *sunflower* (a golden-angle spiral, the radius
   bent by `r^(power + 1)`, weights `1 − r × power`), *gaussian* (weights
   `exp(−r²(3 + 5 power))`) or *hexagon* (an even hexagonal bokeh). A *depth fade* scales the
   radius by `lerp(a, b, smoothstep(c, d, distance from the surface to the scene behind))`.
3. **Pixelation:** the screen read in blocks of *size* screen pixels, anchored at the object's
   origin on screen (× `(object scale / camera distance)^amount` with *distance scaling*); the
   mask halves the block size up to eight times as it falls; a *gap* (0–1) cuts see-through
   lines between the blocks.
4. **Color:** `lerp(screen, (screen × brightness + add brightness) × tint` then the layer's hue
   adjustments, vibrance (saturation raised more for dull colors) and saturation`, color
   mask)`.
5. **Ramp:** the screen's luminance `t` (in gamma), with contrast and an offset, optionally
   posterized to *levels* steps after an 8 × 8 Bayer dither; the color from a ramp texture or a
   four-color palette (*Game Boy*, *Virtual Boy*, *CGA*, *sepia*, *neon*), blended in by
   *blend* × the mask.
6. **Blend:** `k = amount × (1 − alpha, with by alpha) × alpha mask`, then
   `lerp(source factor × surface + destination factor × screen, surface, 1 − k)` with Unity's
   blend factors, their source alpha being `1 − k`. Uber's `lerp(refraction, surface, opacity)`
   is the factors *zero* and *one* with `amount = 1 − opacity`. *Self grab* draws the layer in
   the second pass (6), the surface being the screen at the pixel unrefracted: the object as its
   first pass drew it.

| Parameter | Values | Default |
|---|---|---|
| refracts, IOR, field-of-view scale | bool, float, bool | on, 1.3, off |
| fresnel power, ignore foreground | float (0: none), bool | 0, off |
| refraction mask | 1.4 | none |
| chromatic aberration | off, or strength 0–1 | off |
| blur | off, or strength, by roughness, pattern, quality, camera falloff, power, depth fade, mask | off |
| pixelation | off, or size, distance scaling and amount, gap, mask | off |
| tint, brightness, add brightness, vibrance, saturation, color mask | linear color, floats, 1.4 | white, 1, 0, 0, 1, none |
| ramp | off, or palette or texture, blend, contrast, offset, posterize, levels, dither, mask | off |
| amount, by alpha, alpha mask | 0–1, bool, 1.4 | 1, off, none |
| source and destination factors | Unity's blend factors | zero, one |
| self grab | bool | off |
| cut mask | texture reference, threshold | none: refraction where the mask is at most the threshold |

### 4.8 Highlight layer
A highlight drawn on its own, besides the surface response's (2.4), with a formula of its kind
(a design decision, 9). Usually placement *overlay* (added after lighting); a highlight
that replaces or multiplies the base color is placement *surface*. **Sources:** Poiyomi
Anisotropics and Stylized Reflections.

| Parameter | Values | Default | Notes |
|---|---|---|---|
| kind | strand, toon | strand | below |
| color | color input | white | strand: the map's alpha offsets the strands |
| lobes | list | — | per lobe: strength (1); strand: exponent, offset, offset map strength, tangent or bitangent, stylized with edge and blur, tint; toon: size, feather |
| realistic | bool | off | toon: power lobes instead of edged ones |
| strength | float | 1 | toon: the whole highlight |
| light color mix | 0–1 | 1 | strand: from the light's luminance to its color; toon: from white to the light's color |
| base color mix | 0–1 | 0 | multiplies by `lerp(1, base, mix)` |
| surface normal amount | 0–1 | 1 | how much of the surface's normal map it follows |
| fade by light direction | 0–1 | 0 | strand: `× lerp(1, N·L, amount)` |
| ignore shadows, ignore received shadows, show on back | bool | off | toon |
| blend, opacity | 1.7, 0–1 | add, 1 | strand *replace* blends by `saturate(max(color))` too |

- **Strand** (Kajiya-Kay hair): per lobe `smoothstep(−1, 0, T′·H) × sin(T′, H)^exponent ×
  strength`, `T′ = normalize(T + (offset + offset map strength × map alpha) × N)`, `T` the
  tangent or bitangent; *stylized*: `linstep(edge − blur/2, edge + blur/2)` of that. Lobes add;
  × the received-shadow mask (hide in shadow) and the color map's RGB × tint.
- **Toon** (UnityChan style) on `a = N·H × 0.5 + 0.5`: per lobe *realistic*
  `a^(2^(11 − 10 × size))`, or smooth-edged at `1 − size^5` with the feather; × lobe strength,
  summed and saturated. Then `min(that × mask × lerp(ramp, 1, ignore shadows), occlusion,
  lerp(saturate(N·L), 1, show on back), lerp(light map, 1, ignore shadows)) × strength`.

Not specified: lilToon-style stylized reflections (their own smoothness, metallic,
reflectance, toon or realistic specular and environment reflections), possibly a reflection
kind.

---

## 5. Geometry and extra passes

### 5.1 Outline
A **list** of outline layers (Poiyomi Pro has three), each with:
- **Shape:** normal expansion, drop shadow, directional offset; normals from vertex colors or
  baked curvature data.
- **Width:** world units with a mask; screen-size cap; fixed size over distance; fade over
  distance; edge fade; thicker on the lit or the dark side.
- **Color:** color input, base color blend, lit or unlit, emission, an inner highlight line.
- **Masks per property** (size, color, alpha, emission and so on).
- Its own render settings (6).

**Specified**:

| Parameter | Values | Default | Notes |
|---|---|---|---|
| enabled | bool | on | |
| shape | normal expansion, light facing, view directional, drop shadow | normal expansion | *normal expansion*: an inverted hull pushed out along the normal; *light facing*: `width × lerp(1, lit, follows) + light width × lit + dark width × dark`, `lit = saturate((L·N − f.x) / (f.y − f.x))`, `dark` the same of `−L·N`, with a falloff `f` (0, 1), size follows light (1) and side widths (0, meters), which by default is `width × saturate(L · N)`, L the main light plus the probes' dominant direction; *view directional*: × `saturate(view-space normal xy · direction xy)`; *drop shadow*: the hull offset by the direction in view space |
| direction | vector | (1, 0, 0) | view directional and drop shadow |
| width | meters, object space or world | 0.0005, world | *object space*: scaled with the object |
| normals from vertex color | bool | off | the expansion follows a tangent-space direction in the vertex color (`RGB × 2 − 1`) |
| width vertex mask | a vertex color channel, strength, strength mask | none | width × `lerp(1, channel, strength × mask)` |
| fixed size | 0–1, max distance, mask | 0, 1, none | width × `lerp(1, clamp(distance to the camera, 0, max), amount × mask)`: keeps its on-screen size up to that distance |
| smooth thin | bool | off | outlines thinner than a pixel are widened to one, their width kept as coverage |
| clip at zero width | bool | on | width 0 draws nothing |
| width mask | 1.4, or none | none | |
| screen width limit | float, or 0 | 0 | the largest on-screen offset in normalized device coordinates; 0 = none |
| width from vertex color | bool | off | width × vertex color; black vertices have no outline |
| hide within | meters | 0 | no outline closer to the camera than this |
| color | color input | black | |
| base color mix | 0–1 | 0 | blends toward the base color (with vertex color) |
| multiply by base color | bool | off | color × the base color |
| base color multiply | 0–1, squared | 0, off | color × `lerp(1, base, amount)`; the base squared when *squared* (UTS style) |
| light color mix | 0–1 | 0 | toward the direct light's color |
| shadow strength | 0–1 | 1 | lit: how much the shading's shadow shows |
| emission amount | float, mask | 0, none | the outline's color (or the emission color) × amount × mask, added as emission |
| emission color | color input, or none | none | instead of the outline's color |
| per-property masks | color, alpha, texture blend, color adjust, lighting, highlight | none | color applies by `lerp(1, color, mask)`; alpha × mask; the base color multiply × mask; the color adjustments by the mask; the lighting `lerp(1, lighting, mask)`; the highlight × mask |
| color adjustments | 1.10 operations | none | on the outline's color and its highlight's, in order |
| mask | 1.4 (a bus mask) | none | alpha × it; nothing where it is 0 |
| edge fade | 0–1 | 0 | alpha × `smoothstep(0, edge fade, abs(N·V))` |
| writes camera depth | bool | on | the outline also draws into the camera's depth texture |
| curvature | baked data, or none | none | Poiyomi Pro's outline bake: a texture indexed by vertex with expansion directions (hard edges kept above an angle limit, sharp tips mitred or tapered past an angle, optional smoothing and crevice shrinking) and outline texture UVs |
| highlight | group | off | an inner line toward the light: `edge(view-space N · L × 0.5 + 0.5, border, blur)` × the color's alpha; color, emission, tint from texture, border (0.85), blur (0.1), received shadows |
| override alpha | bool | off | the outline's alpha is its color's alpha instead of the base alpha × it |
| alpha modifiers | 2.2's modifiers | none | distance fade and the others |
| blend | source and destination factors (6) | one, zero | |
| lit | bool | off | unlit: the color as is |
| emission | bool | off | the material's emission also shows on the outline |
| pass | 6 | cull front, depth write on | |

Poiyomi Pro's Triple Outline gives the list three entries, each with its own
settings, pass and stencil; they share the mask texture, blend factors, depth clip and offsets,
smooth thin, camera depth and the curvature bake.

*Specified with Toon Standard; the shapes and options after emission with
Poiyomi; the masks per property, color adjustments and the light facing widths
with Poiyomi 11.* **Sources:** Toon Standard (Outline), Uber (Outline), Poiyomi, Poiyomi
Pro.

### 5.2 Fur
- **Method:** strands or shells.
- **Shape:** length (meters) and length mask; direction (constant, direction map, vertex
  colors); gravity; randomness; tip taper.
- **Strands:** pattern texture, alpha mask, root occlusion; layer count; widths; distance level
  of detail.
- **Color:** color input, optionally also tinting the skin.
- **Contacts:** bends away from a VRChat contact, with a push strength.
- **Sway:** the wind deformation (5.3). **Rim:** an angle layer scoped to the fur.
- Its own render settings (6).

**Sources:** Poiyomi Lil Fur (strands), Poiyomi Pro Fur (shells).

**Specified with Poiyomi Lil Fur**: method (strands, shells); *transparent*
(alpha blended without depth writes; otherwise cutout) and alpha to coverage; length (meters)
× a length mask (a mask channel, `lerp(1, channel, strength)`); direction (a constant in tangent
space, a direction map × its strength, or vertex colors); gravity; randomness; the strand
noise; an alpha mask; root occlusion (the color × `1 − amount × alpha mask`); layer count
(1–3), root width, width; the wind (the shared wind, 5.3); a rim on the strands (linear color,
fresnel power, lit-side suppression); and its own pass settings (cull, depth test and write,
depth clip, offsets, color mask, stencil).

With Poiyomi 11: the mask channels take 11's mask options; vertex colors as the
direction are signed (`RGB × 2 − 1`) or unsigned (as they are), their xy added to the
direction's and their z multiplying it; the fur bends away from a mask bus contact by a push;
*cutout length* (Lil Fur Two Pass) draws the strands twice, first cut out at that share of the
length, then in the fur's own mode.

**Shells, specified with Poiyomi Pro Fur**: the mesh is drawn again as *layer
count* shells (1–31) in the surface's own passes. Shell `i` sits at height `h = i / count`,
pushed out by `h × length` (× the object scale) along the normal (or the signed vertex color
as a tangent-space direction) and down by `h × length × gravity`, faded by how much the normal
faces up (negative gravity flips it); the contact push bends it. A shell keeps a pixel where
both the strand height (the height map's first channel, `saturate(remap(x, 0 → min, max → 1))`)
and the mask reach `h + taper × h²`, anti-aliased with alpha to coverage. Shells show the fur
color (color input × hue adjustment × occlusion `lerp(1, inverse lerp(AO min, AO max, height)
clamped, AO × mask)`); the skin under them is tinted toward it by the mask when *color applies
to surface*. Far away fewer shells draw: the count × `1 − smoothstep(LOD start, LOD end,
distance to the object)` (× the object scale), the outermost fading by the fraction.

### 5.3 Deformation
A list of vertex operations, each with a mask and modulators:
- transform (translate, rotate, scale, spin); inflate along the normal; snap to a grid;
  displace by a height map;
- barrel, sphere and tornado distortion; wind (layers of direction, amplitude, speed,
  frequency); glitch;
- look at the camera; push toward the camera (view-clip prevention);
- squash where a contact presses; TPS shaping (bend, squeeze, bulge, pulse).

**Sources:** Uber, Poiyomi, Poiyomi Pro, and Lil Fur for wind.

**Specified** (with Poiyomi's operations): a list of
operations, applied in order, each with a mask (sampled per vertex: a map on its UV, or a
vertex color channel) and a strength.

| Operation | Amount | Notes |
|---|---|---|
| inflate | 3 × float, object units | along the normal per axis; *absolute normal*: along its absolute value, so everything moves outward on that axis |
| rotate | 3 × degrees | the xy, xz and yz planes, in that order, around the object origin; normals turn too |
| translate | 3 × float, object units | |
| snap | precision, steps per meter; local; centered | the position (world, or object with *local*) rounded up to the grid, `lerp(p, ceil(p × precision) / precision, strength × mask)`, for a retro look; *centered* moves to the middle of the cell, `− 0.5 / precision` |
| scale | 3 × float | around the object origin |
| rotate axes | 3 × degrees, spin (degrees per second) | about the x, y and z axes (+ spin × t) around the object origin, by Poiyomi's quaternion product of the three; normals and tangents turn too |
| translate world | 3 × float, world units | |
| displace | height (x); map, channel, bias | along the normal by `(the map's channel − bias) × height` (the map 1 when there is none) |
| barrel | radius, height | `xz = lerp(xz, normalize(xz) × radius + xz × height, strength × mask)` |
| sphere | radius, height, center | `p = lerp(p, normalize(p + center) × radius + p × height, strength × mask)` |
| tornado | radius, speed, twist, funnel, pull, height range | `f = pow(smoothstep(range.x, range.y, y), funnel) × mask`; `xz += (cos a, sin a) × radius × f`, `a = t × speed + y × twist`; `y += pull × f`; blended by `f` |
| wind | the wind | two sine waves along their directions in world space, `sin(t × speed + dot(p, direction) × frequency) × turbulence × direction × amplitude`, turbulence `lerp(1, noise × 2 − 1, noise strength)` from a noise texture at `p.xz × scale × 0.1 + t × noise speed` |
| look at | rigs (below); visibility | parts turned toward the camera by a chain of up to four rigs, each covering its mask; the strength scales them all; views the visibility excludes show the parts unturned |
| glitch | strength (x); frequency, threshold; map, density, speed; AudioLink band, override; mirror | horizontal jitter across the view: `a × d × strength / 100 × gate × mask`, `d` horizontal across the view of the object's origin, `a` from the glitch map (red and green at `y × density + t / 20 × speed`, minus red and blue at `y × density − t / 20 × speed`, each × 2 − 1) or random per world height and time; `gate = step(threshold, the mean of sin(f), sin(2.2f + 5.52), sin(2.9f + 0.93), sin(4.6f + 8.94))`, `f = t × frequency`; with a band, the gate is the band (*override*) or the gate + the band; mirror visibility limits where it runs |
| depth bulge | height (x); fade length | where the scene behind (the depth texture) is within the fade length (meters), along the normal by `height × (1 − smoothstep(0, fade length, separation))`; not in mirrors |
| camera push | amount (x, meters); bias; object scale | view clip prevention: parts closer than the near plane (+ bias) and facing the camera (`N·forward < −0.2`) move along the view direction, at most amount × the object's scale × object scale |

| TPS | the TPS settings | Thry's Penetration System (Poiyomi Pro): the mesh, measured by the setup tool (length, scale, axes, a baked mesh or vertex colors), bends along a Bézier curve toward a matching orifice light on its channel, squeezed and bulged around the orifice (hole and ring multipliers), with pumping and an idle shrink |
| squish | collider (a contact 1–8, or an orifice plane of a size), strength, spread, max, falloff, clip offset; a color gradient | flattened against the collider (Poiyomi Pro); the squish also colors the surface through a gradient (between a min and max, × intensity, by replace, multiply and unlit add weights) as a pattern layer of kind *squish* sharing the settings, which can write the mask bus |

**Look-at rig:** a mask, a strength, the target's up (the world up kept square to the view,
the world up, the camera's up, the object's up), forward and up directions, a pivot and an
origin (object space), distance and field-of-view falloffs (`smoothstep(far, near, …)` of the
camera's distance from the origin and of the angle between forward and the camera, in degrees;
`(0, 0)` none) and pitch, yaw and roll ranges. The parts turn around the pivot so forward (with
up) points from the origin to the camera, slerped by strength × mask × falloffs, the Euler
angles clamped to the ranges. Each rig's vectors are turned by the rigs before it.

Static operations (no modulator) stay deformation features like the animated ones. Whether a
writer applies them to the mesh at build time, so outputs without them keep the shape, is
decided later, with the writers.

### 5.4 Geometry effects
Effects that add or remove geometry: tessellation, dissolving into pieces, shattering,
particles from triangles. **Sources:** Poiyomi Pro variants.

**Tessellation, specified**: triangles are subdivided before the
deformations (5.3). *Uniform* splits each edge a factor of times; *edge length* into pieces
about that many screen pixels long; *distance* uses the factor near the camera, falling to 1
between two distances (meters). *Phong* (0–1) bends the new vertices toward the curved surface
the vertex normals describe; triangles outside the view (with a margin) aren't subdivided.

*Not typed* (reported, one line each): dissolving into pieces (Poiyomi Pro's Geometric
Dissolve), ShatterWave, particles spawned from triangles.

---

## 6. Render

| Surface mode | Look | Blend |
|---|---|---|
| opaque | alpha ignored | none |
| cutout | pixels below the cutoff are discarded | none |
| dithered | screen-door transparency: a dither pattern keeps pixels in proportion to alpha | none |
| additive | adds the color; alpha ignored | `src + dst` |
| soft additive | adds, softened near white | `src × (1 − dst) + dst` |
| multiplicative | darkens by the color | `src × dst` |
| multiplicative ×2 | darkens and brightens around mid gray | `src × dst + dst × src` |
| fade | everything fades with alpha, highlights and reflections included | `src × a + dst × (1 − a)` |
| transparent | glass: diffuse fades with alpha, highlights and reflections stay | `src + dst × (1 − a)`, diffuse premultiplied by alpha |

Also: per pass, cull (off, front, back), depth write, depth clip, depth offset, color mask and
**stencil** (reference 0–255, read and write masks, compare function, pass, fail and depth-fail
operations; Unity's meanings); **cutoff in transparent modes** (fade and transparent also
discard below the cutoff); **fog** (on by default); **reduced near clipping** (geometry very
close to the camera isn't cut by the near plane); **two-sided lighting** (back faces
flip their normal, so they light like front faces). Also:
- Raw blend factors are kept only when they match none of these (surface mode *custom*).
- **Per pass:** base, each outline layer and fur have cull, depth test and write, depth
  offset, stencil and color mask.
- **Fog**.
- **Visibility:** in mirrors, cameras, normal view; an alternate mirror appearance; near-camera
  clipping with a mask; hide by UV tile; invisible. *Specified*:
  - **visibility** (replacing mirror visibility): drawn or not in the normal view,
    mirrors in VR and on desktop, VRChat cameras in VR and on desktop, and screenshots; *mirror
    only* hides the normal view and the cameras, *not in mirror* hides both mirrors;
  - **hidden UV tiles**: a list of a UV channel, tiles of the 4 × 4 grid from
    (0, 0) to (4, 4), and a side (both, front, back); triangles there aren't drawn;
  - **mirror base color:** a texture replacing the base color texture in mirrors, or none;
  - **near clip:** on, distance in meters, mask (UV0, hidden where above 0.5); not in mirrors;
  - **invisible:** bool;
  - **dithered shadows**: the shadow is as see-through as the surface, by a dither of
    its alpha.
- **Depth prepass**: a depth-only pass first, so a see-through surface
  shows only its nearest layer.
- **World AO blocker**: triangles with a vertex whose UV (on a chosen channel, UV0 to UV3)
  lies inside the tile left of the default one (U −1 to 0, V 0 to 1, edges excluded) render
  only to depth, so screen-space effects such as ambient occlusion treat them as solid while
  they stay invisible; *flip normal* flips their normals in depth-normal passes (scriptable
  render pipelines only). Poiyomi from 9.2.
- **Clock**: time for animation and modulators is the local clock or
  VRChat's network time, the same for every viewer (Poiyomi 11's default).
- **Second pass** (Poiyomi's Two Pass): the whole surface drawn again
  after the first, with its own surface mode (or blend factors), cull, depth, offsets, color
  mask and stencil, and alpha to coverage. Its color is the base color's, its own color and
  alpha, or its own alpha; its alpha is `pow(alpha, power)` (1 when opaque or forced opaque),
  and pixels where it (or `1 − it`, inverted) is below its cutoff are discarded in every mode.
  Mask bus steps of kind *second pass* (1.5) let features differ between the passes.
- **Fallback shader** (what viewers with blocked shaders see) isn't read from source
  materials. An output sets `Render.Fallback` (automatic, toon, unlit, standard, hidden), and
  its writer turns it into its shader's form; automatic is the writer's own choice.

---

## 7. Out of scope

- **World-only features:** parsers ignore them, with at most one info line per material when
  one is switched on: rain, puddles, world vertex wind, screen-space reflections,
  lightmap-based effects, AreaLit, Bakery.
- **World variants:** Poiyomi Toon World, Poiyomi Pro World and World Fur are read as their
  avatar shaders, with one *ignored* line; their world-only features aren't mapped. Beatsaber
  support (gone in 11) is out of scope.
- **Unavailable source:** Uber X. Materials using it get one *unsupported* line.
- **Not visual:** stats overlay (live text such as FPS, position and a clock; one *ignored*
  line when switched on), debug views, editor state, optimizer properties.

---

## 8. Source coverage

✓ the source has it; ~ partly or as a special case; blank: not present.

| Unified feature | Toon Lit | Diffuse | Std Lite | Toon Std | Unity Std | Uber | Poiyomi | Poiyomi Pro adds |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| 1.1 Mapping beyond UV0/1, placement, frames | | | | | | ~ | ✓ | |
| 1.5 Mask bus | | | | | | | ✓ | |
| 1.6 Palette | | | | | | | ~ | |
| 1.9 Modulators | | | | ~ | | ✓ | ✓ | |
| 1.10 Color adjustment | | | | ✓ | | ✓ | ✓ | GT7, neutral emission |
| 2.1 Base color | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 2.2 Alpha | | | | | ✓ | ✓ | ✓ | |
| 2.3 Normals | | ~ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 2.4 Surface response | | | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 2.5 Parallax | | | | | ✓ | ✓ | ✓ | |
| 3 Lighting: unlit | | | | | | ✓ | ✓ | |
| 3 Lighting: flat | ✓ | | | | | | ✓ | |
| 3 Lighting: ramp toon | | | | ✓ | | ✓ | ✓ | |
| 3 Lighting: physically based | | ✓ | ✓ | | ✓ | ✓ | ✓ | |
| 3.5 External lighting | | | | | | ✓ | ✓ | |
| 3.6 Screen-space lighting | | | | | | | | ✓ |
| 4.1 Surface layer | | | ✓ | ✓ | ✓ | ✓ | ✓ | |
| 4.2 Transition | | | | | | ✓ | ✓ | |
| 4.3 Lookup layer | | | | ✓ | | ✓ | ✓ | |
| 4.4 Angle layer | | | | ✓ | | ✓ | ✓ | |
| 4.5 Subsurface | | | | | | ✓ | ✓ | |
| 4.6 Procedural pattern | | | | | | ~ | ✓ | constellation, lava lamp |
| 4.7 Refraction | | | | | | ✓ | ✓ | self grab, lava lamp |
| 5.1 Outline | | | | ~ | | ✓ | ✓ | layers, curvature |
| 5.2 Fur | | | | | | | ✓ | shells |
| 5.3 Deformation | | | | | | ✓ | ✓ | squish, TPS |
| 5.4 Geometry effects | | | | | | | | tessellation (dissolve, shatter, particles reported) |
| 6 Visibility | | | | | | ✓ | ✓ | |
| 6 Second pass, depth prepass | | | | | | | ✓ | |

Single-source features, described generically so other shaders can map into them later: mask
bus, palette and the back-face condition (Poiyomi); screen-space lighting and geometry effects
(Poiyomi Pro). The Diffuse column is VRChat Mobile Diffuse and Bumped Diffuse, whose normal
map is the ~. The Poiyomi column is Poiyomi 10 and 11 (Grab Pass and Two Pass
included); 7.3 to 9.3 are read on the same parser with version tables. *Palette* is ~ because
theme colors are resolved to the colors they produce (1.6).

---

## 9. Design decisions

- Avatar shaders and features only; world-only features are ignored, not counted as unmapped.
- Mochie Standard is not supported; Mochie Uber is its avatar counterpart.
- Poiyomi 11 and Poiyomi Pro are supported.
- Parsing never requires the source shader to be installed; output shaders must be installed
  (see [*Shader availability*](UNIFIED_MATERIAL.md#shader-availability)).
- The layer merges (one surface layer type, one lookup layer, one angle layer) stand until
  output writers show a need to split them.
- Coverage means the look; misunderstandings of a source are fixed in its parser, not in the
  unified material.
- **Specular color** is its own surface response input next to metallic (2.4); parsers don't
  convert it.
- **Uber X** is unsupported (7).
- **Static deformation** stays a deformation feature (5.3); applying it to the mesh is a later
  writer decision.
- **Light volumes and LTCGI** stay in scope as external lighting (3.5): they're common on
  avatars.
- **Transition** stays its own layer type (4.2).
- **Procedural patterns** are one layer type with a pattern kind (4.6).
- **Stats overlay** is out of scope (7).
- **Ramp toon is one formula** with each source as a special case (3.2).
- **Stand-alone highlights are a layer type** (4.8): strand and toon
  highlights keep their own formulas instead of folding into the surface response's highlights,
  so a same-shader round trip is lossless.
- **Older Poiyomi versions are supported pragmatically:** 7.3 to 9.3 are read on the Poiyomi 10
  parser with version tables for renamed and re-meaning settings; features Poiyomi 10 rewrote
  are read with 10's meaning and noted once each.
- **Mochie Uber is read as of its current release;** older Uber versions aren't distinguished.

## 10. Open questions

None outside the atlasing pass.

**Deferred to the atlasing pass:** UV-tile features (AudioLink UV modes, UV tile discard),
vertex color handling when merging, settings that can't differ within one material, and
per-material constants such as fur length.
