# Unified material — Mochie Uber

Part of the per-shader inventories that [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds
on, merged into the [feature catalog](UNIFIED_MATERIAL_FEATURES.md), whose names take
precedence. It also describes the parser; same format as
[the VRChat Mobile inventory](UNIFIED_MATERIAL_VRCHAT_MOBILE.md).

**Source:** Mochie Uber v1.35.2 (free version), as imported under `Assets/Mochie/Uber Shader/`,
with the inspector in `Assets/Mochie/Unity/Editor/UberEditor.cs`.
Variants: `Mochie/Uber` and `Mochie/Uber (Outline)`.

**Character:** Mochie's avatar shader: physically based or toon shading, with many stylized and
animated effects. About 590 properties.

**Parser:** `AtlasMochieUberParser`; see [*Parser*](#parser). The feature entries below don't
have *Parser* lines.

**Uber X:** the paid version adds the inspector's *Special Features* and some AudioLink targets.
Its source isn't available, so it isn't covered; see [*Uber X features*](#uber-x-features).

**Inspector sections:** Base; Textures (primary and detail maps, masks, map filters, base color
dissolve); Shading (lighting, shadows, reflections, specular highlights, matcap, subsurface,
rims, refraction, iridescence, normals, light volumes); Emission (with light reactivity and
pulse); Filtering; Flipbook; Outline; UV Distortion; Vertex Manipulation; Audio Link; Render
Settings.

---

## Base

### Shading on or off
- **Controls:** Shading: On (lit) or Off.
- **Behavior:** Off shows the base color without lighting, hides the Textures section, and
  turns off parallax.
- **Live or baked:** live.
- **Unified:** the *unlit* lighting family.

### Blending Mode and alpha
- **Controls:**
  - Opaque, Cutout, **Dithered**, Fade or Transparent.
  - Alpha source: base color alpha or an Alpha Mask (with channel and tiling).
  - Alpha strength; cutoff for Cutout; alpha-to-coverage.
- **Behavior:** Dithered is screen-door transparency: an opaque pattern that reads as
  see-through, without sorting problems.
- **Live or baked:** live mode; alpha baked.
- **Unified:** surface mode gains *dithered*. Alpha as a scalar input from any source, with a
  strength.

### Base color
- **Controls:**
  - Base color texture and color, tiling, scroll, rotation.
  - **Cube mode:** 2D texture, a cubemap (with tint, rotation, auto-rotation), or both
    blended (mask or amount, blend mode Add, Sub, Mul, Mul×2, Overlay, Screen, Lerp).
  - **Color Preservation:** keeps strong lights from washing out the base color.
  - Vertex color multiply.
- **Live or baked:** texture and color baked. The cubemap part depends on the view
  direction, so it stays live; auto-rotation is animated.
- **Unified:**
  - Base color layer.
  - A **lookup layer** with projection *view direction* on the base color, with rotation and
    spin (catalog 4.3).
  - Color preservation is the light clamp from Toon Standard's *Limit Brightness*.

### Mirror Behavior
- **Controls:** Default, Only Reflection (visible only in mirrors), Textured Reflection (a
  different texture in mirrors), No Reflection (hidden in mirrors).
- **Live or baked:** live; it's render visibility.
- **Unified:** render: mirror visibility (both, mirror only, not in mirror), and an alternate
  base color for mirrors. Avatar-specific, and Poiyomi has the same feature.

---

## Textures

### Primary maps
- **Controls:** Workflow: Metallic (separate maps), Specular (RGB with a smoothness map),
  Specular (smoothness in alpha), or Packed (channel per property). Normal map with
  scale. Occlusion with strength. Height (parallax) with strength, offset and steps.
- **Behavior:** in the metallic workflow a texture replaces its slider; the inspector hides
  the slider once a texture is set.
- **Live or baked:** baked, apart from the parallax effect.
- **Unified:** surface response scalars from any texture channel; specular color; primary
  normal; occlusion; parallax with a step count.

### Detail maps
- **Controls:** UV set (0–2); detail base color, metallic, roughness and occlusion, each with
  strength and a blend mode (Add, Alpha, Mul, Mul×2, Overlay, Screen, Lerp); detail normal
  with scale; tiling, scroll, rotation; a detail mask (under Masks).
- **Live or baked:** baked; scroll is live.
- **Unified:** the general detail layer from the
  [feature catalog](UNIFIED_MATERIAL_FEATURES.md#4-layers): target, blend, strength, mask,
  mapping.

### Masks
- **Controls:** Off, Separate, or Packed.
  - **Separate:** one mask texture per feature: reflections, specular, anisotropic blend,
    the two matcaps, shadows, basic rim, environment rim, diffuse shading, subsurface,
    detail, emission, emission pulse, filtering, refraction and outline thickness. Each has
    optional tiling and scroll.
  - **Packed:** four RGBA textures with a fixed channel per feature.
- **Live or baked:** baked; scroll is live.
- **Unified:** every feature's mask is a channel reference with its own mapping. Packed and
  separate differ only in which texture and channel the parser points at. No separate
  "mask set" is needed.

### Map filters
- **Controls:** a toggle each for metallic, roughness (or smoothness), occlusion and height:
  remap range, lightness, intensity, contrast. Occlusion also gets a **tint** color or
  texture.
- **Live or baked:** baked; it's static maths on the maps.
- **Unified:** the parser bakes the filter into the scalar's evaluation: a remap and curve on
  the input. The occlusion tint is new: colored occlusion, where creases darken toward a
  color instead of black. It needs an occlusion tint in the unified material.

### Base Color Dissolve
- **Controls:** a toggle; a second base color texture and color; a noise texture with a
  dissolve amount; rim color and width.
- **Behavior:** the second base color replaces the first wherever the noise is below the
  amount, with a glowing edge.
- **Live or baked:** baked if the amount is fixed; live when animated or driven by AudioLink.
- **Unified:** a **transition layer**: an alternate base color revealed by a noise
  threshold, with an edge color and width. Poiyomi's dissolve is similar.

---

## Shading

### Lighting
- **Controls:**
  - Static light direction (override the light's direction).
  - Direct and indirect occlusion strength.
  - Diffuse shading: Disney diffuse (softer, view-dependent falloff), light-probe strength,
    non-linear probes.
  - Real-time light: direct, indirect, vertex lights and additive lights contribution;
    clamp additive.
  - Baked light: direct and indirect contribution.
  - There's also a one-click "Standard lighting" preset button.
- **Live or baked:** live.
- **Unified:** parameters on the lighting family: per-source contribution strengths, a fixed
  light direction option, probe strength, and a diffuse model (Lambert or Disney). Poiyomi
  has many similar knobs, so expect these to merge there.

### Shadows
- **Controls:** a mode: Off, Manual Blend, or Ramp texture.
  - **Manual Blend:** shadow tint; two ramp widths, a blend between them, and an offset.
  - **Ramp:** a ramp texture.
  - Shadow conditions: always, real-time lights only, or baked lighting only.
  - A **detail shadow map**: a painted mask that multiplies the light, so shadows can be
    drawn in.
  - Strength, dithering, real-time self-shadowing, attenuation smoothing.
- **Live or baked:** live; the ramp and painted shadow map are baked.
- **Unified:**
  - The *ramp toon* lighting family. Manual Blend is a ramp described by numbers, so the
    parser can turn it into the same ramp representation as a ramp texture; one ramp
    feature covers both.
  - Shadow tint as in Toon Standard's shadow color.
  - A **painted shadow mask**: a texture that adds shadow.
  - Shadow strength; which lights cast it.

### Reflections
- **Controls:** Off, Environment (reflection probes), or a Cubemap (lit or unlit); tint and
  strength; fresnel with strength; manual roughness; lighting-based strength. Screen-space
  reflections are listed but are world-only (see the
  [feature catalog](UNIFIED_MATERIAL_FEATURES.md#7-out-of-scope)).
- **Live or baked:** live.
- **Unified:** environment reflections with a source (scene probes or a fixed cubemap), tint,
  strength, fresnel strength (the shader raises reflections by `((1 − N·V) × strength)⁵`,
  so the unified strength is its fifth power; 0 with the fresnel toggle off), and a
  roughness override.

### Specular Highlights
- **Controls:** Off, GGX, Anisotropic, or Combined.
  - Tint, strength, manual roughness, a light bias override, real-time only.
  - **Anisotropic:** stretched highlights for hair or brushed metal: two layers with
    thickness and blend, and "hair" ripples (strength, density, intensity, continuity).
  - **Stepping:** step counts for toon highlights.
- **Live or baked:** live.
- **Unified:** surface response highlight with a **shape**: round (GGX) or stretched
  (anisotropic, with layers and strand variation). The stepping is the *highlight style*
  from Toon Standard's sharpness, here as a step count.

### Matcap
- **Controls:** two matcaps, each with texture, color, blend (Add or Alpha), strength, its
  own normal map with strength and mix, manual roughness (blur), Unlit, and "No Depth in VR"
  (the same lookup for both eyes). Masks under Masks.
- **Live or baked:** live; masks baked.
- **Unified:** matcap layers, with before- or after-lighting placement (Unlit), a blur amount,
  a stereo option, and an optional per-matcap normal.

### Subsurface Scattering
- **Controls:** thickness map with power; color texture and color, base color tint; direct
  and indirect strength; power; normal strength; wrapping.
- **Unified:** the subsurface layer, plus a color texture.

### Basic Rim
- **Controls:** color texture and color; blend (Add, Sub, Mul, Mul×2, Overlay, Screen,
  Lerp); strength, width, sharpness; Unlit.
- **Unified:** rim layer with blend mode and placement (before or after lighting).

### Environment Rim
- **Controls:** blend, tint, strength, width, sharpness, manual roughness.
- **Behavior:** a rim colored by the reflection probe, so the edge picks up the
  surroundings.
- **Unified:** rim layer with **color source** environment instead of a fixed color.

### Refraction
- **Controls:** tint, IOR, opacity; chromatic aberration; blur, with an option to use
  roughness; Unlit; a dissolve mask.
- **Behavior:** bends the scene behind a transparent surface, using a screen grab.
- **Live or baked:** live.
- **Unified:** a refraction feature for transparent surfaces: strength (IOR), tint, color
  split, blur. Reproducible only by writers whose shader can grab the screen.

### Iridescence
- **Controls:** mode: hue shift or color ramp; mask; strength; width and sharpness for hue
  shift.
- **Behavior:** color changes with viewing angle, like oil or beetle shells.
- **Live or baked:** live.
- **Unified:** a view-angle color feature: angle → color from a ramp or a hue rotation.

### Normals
- **Controls:**
  - **Hard Edges:** flat-shaded look.
  - **Clear Coat:** reflections use the mesh's smooth normal instead of the normal map, so
    the surface looks varnished.
  - GSAA.
- **Unified:** surface response options: flat shading, a clear-coat reflection layer (its
  normal ignores the normal map), specular anti-aliasing.

### Light Volumes
- **Controls:** a toggle, strength, specularity with strength, bias.
- **Unified:** external lighting.

---

## Emission

- **Controls:** a section toggle; **two** emission textures, each with HDR color and intensity;
  a mask.
  - **Light Reactivity:** emission fades as the surroundings get brighter. Options: include
    shadows, a threshold, crossfade width, and smooth or linear crossover.
  - **Pulse:** a waveform (sine, square, triangle, saw, reverse saw) with strength and
    speed, with its own mask.
- **Live or baked:** textures and colors baked; light reactivity and pulse live.
- **Unified:** emission layers, plus modulations: **time** (waveform), **audio**, and a third
  source, **light level** (emission strength driven by environment brightness).

---

## Filtering

- **Controls:** a section toggle.
  - **Color Masking:** a mask whose RGBA channels recolor four regions (team colors).
  - Post filtering: apply to the lit result instead of the base color.
  - Invert; Hue Mode (HSV or Oklab); mono tint.
  - Hue, or **Auto Hue Shift** with a speed (cycling colors).
  - RGB multiplier, saturation, brightness, contrast, HDR, ACES.
  - A filter mask.
- **Live or baked:** baked when static and applied to the base color; live when post or
  auto-shifting.
- **Unified:** color adjustment with a target (base color or final color), a mask, and the
  adjustments, with time modulation on hue for auto-shift. Color masking is the recolor
  layer from Toon Standard's Color Mask.

---

## Flipbook

- **Controls:** two layers, each with a toggle.
  - Mode: a flipbook asset (texture array) or a sprite sheet (columns and rows, frame size,
    clip edges).
  - Color, blend mode (Add, Sub, Mul, Mul×2, Overlay, Screen, Lerp), brightness, Unlit, use
    alpha.
  - Position, scale, rotation, scrolling.
  - Speed in frames per second, or a fixed frame.
- **Live or baked:** animated layers are live. A fixed frame is a static decal and can be
  baked.
- **Unified:** a **decal layer** with an optional animation (frame source, frame rate or fixed
  frame). Poiyomi has decals and flipbooks too.

---

## Outline (Outline variant)

- **Controls:**
  - Apply lighting, apply emission, albedo tint; stencil mode; ignore filter mask; vertex
    color.
  - Texture and color, with texture alpha in cutout modes.
  - Thickness and multiplier; **range** (outlines only within a distance of the camera).
  - A thickness mask (under Masks).
- **Unified:** the outline feature (catalog 5.1), extended with lit or unlit,
  emission, color texture, and a maximum distance.

## UV Distortion

- **Controls:** style: a normal map or animated noise (octaves, strength, speed, scale); a
  mask; which features it distorts: main, detail, emission, rim, either matcap.
- **Live or baked:** live; animated.
- **Unified:** a distortion modulator on texture mappings: a source (texture or noise),
  strength, speed, mask, targets.

## Vertex Manipulation

- **Controls:** expansion (push vertices outward, with a mask and clamped direction); position
  rounding with a mask and precision (a retro vertex-snapping look); rotation; position.
- **Live or baked:** static values could be baked into the mesh. Audio-driven expansion is
  live.
- **Unified:** a geometry deformation feature: inflate, vertex snapping, transform. See the
  open questions.

## Audio Link

- **Controls:** a section toggle, global strength and remap, then per target: band, strength,
  remap.
  - **Free version targets:** emission, rim (width, plus a pulse with width and sharpness),
    base color dissolve, UV distortion, outline thickness.
  - **Oscilloscope:** draws the audio waveform onto the surface, with color, margins,
    scale, offset, rotation.
- **Live or baked:** live.
- **Unified:** confirms the **audio modulation** idea: one modulator type (band, strength,
  remap) that can drive any numeric parameter, rather than an audio feature per effect. The
  oscilloscope is its own feature: a waveform decal.

---

## Render Settings

- **Controls:** culling; ZWrite; ZTest; **fallback shader** (Toon, Unlit, Matcap and so on,
  for viewers who block the shader); invisible; **near clipping** with mask and range (hide
  parts close to the camera); stencil reference and operations.
- **Unified:**
  - Render: cull mode, depth settings, stencil, near-camera clipping with a mask.
  - The fallback choice isn't visible in the material itself, but output writers should
    carry it over, since it decides what people with blocked shaders see.
  - "Invisible" hides the mesh entirely; it's mostly used through animation.

---

## Uber X features

The paid Uber X enables these; in the free version they're absent from the inspector and the
shader code is a stub. **Source not available,** so they're not inventoried:
- Special Features: Distance Fade, Dissolve (texture, noise, geometry), Screenspace, Clones,
  Glitch, Shatter Culling, Wireframe.
- AudioLink targets: vertex manipulation, dissolve, triangle offset, wireframe.

A material with any of these switched on should get one *unmapped* info line, the same way
as Poiyomi Pro.

## World-only features

- **Screen-space reflections** in the Reflections section.

---

## Cross-shader notes

- **Modulators** have four sources: time (pulse, auto hue, scroll), audio, light level and
  noise (UV distortion). Uber applies audio to five different targets. That settles it:
  modulators are a general mechanism that drives any numeric parameter, not something
  built into emission.
- **Ramps can be described by numbers or textures.** The parser can turn a parameterized ramp
  into a ramp (gradient), so the lighting family needs only one ramp representation.
- **Placement before or after lighting** (Unlit toggles) appears on matcaps, rims and
  flipbooks. It's a general layer property.
- **Masks** are per feature, packed or separate. The unified material only needs a channel
  reference per feature mask.
- **Decals, flipbooks and transition layers** are new layer types; Poiyomi will refine them.
- **Avatar render options:** mirror visibility, near clipping and fallback shader belong in
  the render definition.
- **Atlasing limits:** some settings can't differ inside one merged material: blend mode,
  culling, mirror behavior, outline on or off, stencil, fallback shader. Sources that
  disagree on these can't share an output. See the open questions.

## Parser

Every section follows its keyword, as the shader does (the inspector sets them).
- **Render:** blending modes opaque, cutout (with Uber's sharpened alpha edge), **dithered**
  (screen-door), fade, transparent; cutoff, alpha-to-coverage, depth write and test, culling;
  two-sided lighting; alpha strength; the alpha mask with its channel.
- **Base color:** texture × color on the main transform (tiling, scroll, rotation); vertex
  color by its amount.
- **Rotation** (main and detail transforms): Uber turns the UV clockwise around its origin,
  after the tiling, offset and scroll. The parser converts it to the unified rotation
  (counterclockwise around the center, before them) by turning the offset and scroll and moving
  the pivot: exact when the tiling is the same on both axes, approximate otherwise.
- **Shading off** is *not* unlit: the shader lights the base color uniformly and clamps it to the
  base color. It maps to the *flat* family with limited brightness.
- **Workflows:** metallic (maps replace sliders; roughness, not smoothness), specular (color,
  smoothness from its own map, the specular map's alpha, or the scale), packed (a channel per
  property). Normal map, occlusion (direct and indirect amounts from *Direct AO* and
  *Indirect AO*; reflections always), GSAA.
- **Detail:** albedo, metallic, roughness and occlusion as surface layers, blend per mode (Lerp
  is half way, so its opacity halves; Alpha uses the detail alpha), strength × the detail mask,
  on the detail transform and UV set; the detail normal.
- **Masks:** separate (red, main transform, or their own with mask transforms) or packed.
- **Lighting:** physically based or ramp toon. *Manual Blend* becomes ramp keys (piecewise
  linear, with the shadow tint); *Ramp* the texture; shadow strength with the shadow mask; the
  painted detail shadow map as the term mask; received shadows from *Realtime Self-shadowing*.
  Disney diffuse, light-probe strength as the ambient model, contributions (the real-time ones,
  and the baked ones as the light without a main light: *ambient as main light* and the ambient
  then), color preservation as limited brightness, *Clamp Additive* as a limit of 1 on the
  additional lights (Uber clamps each channel), light volumes.
- **Reflections and highlights:** strength, tint, mask, fresnel, roughness override, probes,
  cubemap or fallback cubemap.
- **Matcaps:** overlay lookup layers, alpha blend as source alpha, unlit or scaled by light,
  blur from roughness or manual, center eye.
- **Rims:** angle layers with the *power band* profile (`(1 − width) × 10`, edge); the
  environment rim with color source *environment* (its tint applies twice in the shader).
- **Subsurface:** a subsurface layer. The shader passes the shadow and the base color to its
  subsurface function in swapped order; the unified layer follows the intended meaning.
- **Emission:** two layers; pulse as a *time* modulator (speed converted to cycles per second
  per waveform), light reactivity as a *light level* modulator, both scaling each layer.
- **Filtering:** color adjustments (hue HSV or Oklab, auto hue shift as a time modulator, RGB
  tint, saturation, contrast, brightness) on the *surface color* (after detail), or the final
  color with Post Filtering; the filter mask.

**Cube mode, mirror behavior, vertex manipulation and outline:**
- **Cube mode:** a lookup layer with projection *view direction* on the base color (replace,
  or the cube blend with its amount or mask); rotation and auto-rotation as rotation and spin.
  Uber's rotation turns each plane by the angle + 90° once any angle is set; the parser adds it.
- **Mirror behavior:** mirror visibility, and *Textured Reflection* as the mirror base color.
- **Hide** as invisible; **near clipping** with distance and mask.
- **Vertex manipulation:** deformations inflate (expansion × 0.001, absolute normal from the
  clamp), rotate (+ 90° as above), translate, snap (rounding strength and precision), masks on
  raw UV0.
- **Outline** (Outline variant, by shader name): width `thickness × 0.01 × multiplier`, the
  thickness mask (separate on raw UV0 or its own transform, packed mask 3 alpha), vertex color
  width, range as *hide within*, texture × color, albedo tint as *multiply by base color*,
  lighting and emission, the outline culling. Object units are taken as meters.
- **Hard edges** as flat shading; **clear coat** as specular from the mesh normal; the
  **static light direction**.

**Dissolve, flipbooks, refraction, iridescence and UV distortion:**
- **Base color dissolve:** a transition layer (noise threshold, amount, the second base
  color, an edge band `rim width × 0.035` glowing in the rim color × its alpha).
- **Flipbooks:** surface layers whose texture is a placed decal (center `position + 0.5`,
  scale, rotation, on wrapped UV0 with scroll) with frames (sprite sheet or texture array,
  rate or fixed frame); blended by their alpha with the flipbook blend; unlit ones placed
  *overlay*. A flipbook replacing the alpha is reported.
- **Refraction:** a refraction layer (IOR, tint, opacity as the blend amount `1 − opacity`
  with the factors zero and one, chromatic aberration, blur, the cut mask), placement *lit* or
  *overlay* (unlit).
- **Iridescence:** an angle layer on highlights and reflections only, scaled by metallic: hue
  rotation by the power band, or the ramp.
- **UV distortion:** one shared distortion (simplex noise or a normal map, strength × 0.1, the
  mask) on the main, first emission, detail and rim maps, and ×20 on matcap lookups, as chosen.
- **Matcap normals:** the lookup layer's own normal map, strength and *with surface*.
- **Height:** multi-step parallax (steps, offset), from the height map or the packed channel.
- **Map filters:** scalar remaps on metallic, roughness or smoothness, occlusion and height,
  and the occlusion tint.

**AudioLink, the oscilloscope, filters and extras:**
- **AudioLink:** audio modulators. Each target is scaled by `lerp(1, remapped band,
  multiplier × strength)`, with the band remapped from the global range to the target's; that
  is linear in the band, so it becomes a modulator's min and max. Targets: emission (each
  layer's color), the rim's strength (opacity) and width (its power band's power, replaced),
  the base color dissolve's amount, the outline's width, the vertex expansion and rounding.
  Uber reads the raw band, and 1 without AudioLink in the world.
- **Oscilloscope:** a procedural pattern layer (scale, offset, rotation, margins, color ×
  alpha × strength), added after lighting.
- **Filters:** smooth invert (unmasked, on the surface color), team colors as a channel tint,
  mono tint as a hue that's set rather than rotated, HDR as an sRGB decode, ACES tone mapping.
- **Highlights:** steps, stretched (anisotropic) and combined shapes with their layers, ripples
  and the blend mask, real-time light only, the light bias override as a base color mix,
  manual brightness as *ignore light color*.
- **Reflections:** steps, light-based strength. **Shadows:** conditions, dithering.
- **Stencil** (base and outline pass); the color alpha on the alpha mask; partial Disney
  diffuse and light-probe strength as amounts.

**Reported** (each needs something the model doesn't have): a flipbook replacing the
alpha, AudioLink's rim pulse (a history band along the view angle), AudioLink on the vertex
rotation (Uber adds 90° per plane, so it isn't a factor) and on the shared UV distortion.
**Unsupported:** Uber X.

**Coverage:** every section is mapped, apart from the four details above.

**Not represented:** the non-linear probe evaluation, shadow attenuation smoothing, the diffuse
mask, the outline texture's alpha as the outline's alpha, the filter mask ignored on outlines,
and flipbook frame clipping. Screen-space reflections are a
world feature.

**Without the shader:** Uber and Uber (Outline) declare the same properties, so the outline
isn't read, with a note.
