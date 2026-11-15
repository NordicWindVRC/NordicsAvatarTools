# Unified material — Mochie Uber

**Status:** first draft, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md); the
catalogue's names take precedence. Part of the per-shader inventory that
[`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds on; same format as
[the VRChat Mobile draft](UNIFIED_MATERIAL_VRCHAT_MOBILE.md).

**Source:** Mochie Uber v1.35.2 (free version), in the dev project under
`Assets/Mochie/Uber Shader/`, with the inspector in `Assets/Mochie/Unity/Editor/UberEditor.cs`.
Variants: `Mochie/Uber` and `Mochie/Uber (Outline)`.

**Character:** Mochie's avatar shader: physically based or toon shading, with many stylised and
animated effects. About 590 properties.

**Parser:** `AtlasMochieUberParser` follows this inventory against today's unified material. It
maps shading on/off, blending and alpha, the four surface workflows, parallax, toon shadows,
lighting contributions, detail, both emission layers, both matcaps, both rims, subsurface,
filtering and team colours, light volumes and the feature masks. Features without a typed home
yet are reported once each when switched on; see [*Parser work*](#parser-work). The feature
entries below don't have *Parser today* lines.

**Uber X:** the paid version adds the inspector's *Special Features* and some AudioLink targets.
Its code isn't installed here; see [*Uber X features*](#uber-x-features).

**Inspector sections:** Base; Textures (primary and detail maps, masks, map filters, base colour
dissolve); Shading (lighting, shadows, reflections, specular highlights, matcap, subsurface,
rims, refraction, iridescence, normals, light volumes); Emission (with light reactivity and
pulse); Filtering; Flipbook; Outline; UV Distortion; Vertex Manipulation; Audio Link; Render
Settings.

---

## Base

### Shading on or off
- **Controls:** Shading: On (lit) or Off.
- **Behaviour:** Off shows the base colour without lighting, hides the Textures section, and
  turns off parallax.
- **Live or baked:** live.
- **Unified:** the *unlit* lighting family.

### Blending Mode and alpha
- **Controls:**
  - Opaque, Cutout, **Dithered**, Fade or Transparent.
  - Alpha source: base colour alpha or an Alpha Mask (with channel and tiling).
  - Alpha strength; cutoff for Cutout; alpha-to-coverage.
- **Behaviour:** Dithered is screen-door transparency: an opaque pattern that reads as
  see-through, without sorting problems.
- **Live or baked:** live mode; alpha baked.
- **Unified:** surface mode gains *dithered*. Alpha as a scalar input from any source, with a
  strength.

### Base colour
- **Controls:**
  - Base colour texture and colour, tiling, scroll, rotation.
  - **Cube mode:** 2D texture, a cubemap (with tint, rotation, auto-rotation), or both
    blended (mask or amount, blend mode Add, Sub, Mul, Mul×2, Overlay, Screen, Lerp).
  - **Color Preservation:** keeps strong lights from washing out the base colour.
  - Vertex colour multiply.
- **Live or baked:** texture and colour baked. The cubemap part depends on the view
  direction, so it stays live; auto-rotation is animated.
- **Unified:**
  - Base colour layer.
  - A **cubemap colour layer** blended onto the base colour, with rotation and spin.
    Poiyomi has similar panosphere projections; the refinement pass will tell whether this
    is a texture projection or a layer of its own.
  - Colour preservation is the light clamp from Toon Standard's *Limit Brightness*.

### Mirror Behavior
- **Controls:** Default, Only Reflection (visible only in mirrors), Textured Reflection (a
  different texture in mirrors), No Reflection (hidden in mirrors).
- **Live or baked:** live; it's render visibility.
- **Unified:** render: mirror visibility (both, mirror only, not in mirror), and an alternate
  base colour for mirrors. Avatar-specific, and Poiyomi has the same feature.

---

## Textures

### Primary maps
- **Controls:** Workflow: Metallic (separate maps), Specular (RGB with a smoothness map),
  Specular (smoothness in alpha), or Packed (channel per property). Normal map with
  scale. Occlusion with strength. Height (parallax) with strength, offset and steps.
- **Behaviour:** in the metallic workflow a texture replaces its slider; the inspector hides
  the slider once a texture is set.
- **Live or baked:** baked, apart from the parallax effect.
- **Unified:** surface response scalars from any texture channel; specular colour; primary
  normal; occlusion; parallax with a step count. All shapes seen in the earlier drafts.

### Detail maps
- **Controls:** UV set (0–2); detail base colour, metallic, roughness and occlusion, each with
  strength and a blend mode (Add, Alpha, Mul, Mul×2, Overlay, Screen, Lerp); detail normal
  with scale; tiling, scroll, rotation; a detail mask (under Masks).
- **Live or baked:** baked; scroll is live.
- **Unified:** the general detail layer from the
  [feature catalogue](UNIFIED_MATERIAL_FEATURES.md#4-layers): target, blend, strength, mask,
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
  remap range, lightness, intensity, contrast. Occlusion also gets a **tint** colour or
  texture.
- **Live or baked:** baked; it's static maths on the maps.
- **Unified:** the parser bakes the filter into the scalar's evaluation: a remap and curve on
  the input. The occlusion tint is new: coloured occlusion, where creases darken toward a
  colour instead of black. It needs an occlusion tint in the unified material.

### Base Color Dissolve
- **Controls:** a toggle; a second base colour texture and colour; a noise texture with a
  dissolve amount; rim colour and width.
- **Behaviour:** the second base colour replaces the first wherever the noise is below the
  amount, with a glowing edge.
- **Live or baked:** baked if the amount is fixed; live when animated or driven by AudioLink.
- **Unified:** a **transition layer**: an alternate base colour revealed by a noise
  threshold, with an edge colour and width. Poiyomi's dissolve is similar.

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
  - Shadow tint as in Toon Standard's shadow colour.
  - A **painted shadow mask**: a texture that adds shadow.
  - Shadow strength; which lights cast it.

### Reflections
- **Controls:** Off, Environment (reflection probes), or a Cubemap (lit or unlit); tint and
  strength; fresnel with strength; manual roughness; lighting-based strength. Screen-space
  reflections are listed but are world-only (see the
  [feature catalogue](UNIFIED_MATERIAL_FEATURES.md#7-out-of-scope)).
- **Live or baked:** live.
- **Unified:** environment reflections with a source (scene probes or a fixed cubemap), tint,
  strength, fresnel strength, and a roughness override.

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
- **Controls:** two matcaps, each with texture, colour, blend (Add or Alpha), strength, its
  own normal map with strength and mix, manual roughness (blur), Unlit, and "No Depth in VR"
  (the same lookup for both eyes). Masks under Masks.
- **Live or baked:** live; masks baked.
- **Unified:** matcap layers, with before- or after-lighting placement (Unlit), a blur amount,
  a stereo option, and an optional per-matcap normal.

### Subsurface Scattering
- **Controls:** thickness map with power; colour texture and colour, base colour tint; direct
  and indirect strength; power; normal strength; wrapping.
- **Unified:** the subsurface layer, plus a colour texture.

### Basic Rim
- **Controls:** colour texture and colour; blend (Add, Sub, Mul, Mul×2, Overlay, Screen,
  Lerp); strength, width, sharpness; Unlit.
- **Unified:** rim layer with blend mode and placement (before or after lighting).

### Environment Rim
- **Controls:** blend, tint, strength, width, sharpness, manual roughness.
- **Behaviour:** a rim coloured by the reflection probe, so the edge picks up the
  surroundings.
- **Unified:** rim layer with **colour source** environment instead of a fixed colour.

### Refraction
- **Controls:** tint, IOR, opacity; chromatic aberration; blur, with an option to use
  roughness; Unlit; a dissolve mask.
- **Behaviour:** bends the scene behind a transparent surface, using a screen grab.
- **Live or baked:** live.
- **Unified:** a refraction feature for transparent surfaces: strength (IOR), tint, colour
  split, blur. Reproducible only by writers whose shader can grab the screen.

### Iridescence
- **Controls:** mode: hue shift or colour ramp; mask; strength; width and sharpness for hue
  shift.
- **Behaviour:** colour changes with viewing angle, like oil or beetle shells.
- **Live or baked:** live.
- **Unified:** a view-angle colour feature: angle → colour from a ramp or a hue rotation.

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

- **Controls:** a section toggle; **two** emission textures, each with HDR colour and intensity;
  a mask.
  - **Light Reactivity:** emission fades as the surroundings get brighter. Options: include
    shadows, a threshold, crossfade width, and smooth or linear crossover.
  - **Pulse:** a waveform (sine, square, triangle, saw, reverse saw) with strength and
    speed, with its own mask.
- **Live or baked:** textures and colours baked; light reactivity and pulse live.
- **Unified:** emission layers, plus modulations: **time** (waveform), **audio**, and a third
  source, **light level** (emission strength driven by environment brightness).

---

## Filtering

- **Controls:** a section toggle.
  - **Color Masking:** a mask whose RGBA channels recolour four regions (team colours).
  - Post filtering: apply to the lit result instead of the base colour.
  - Invert; Hue Mode (HSV or Oklab); mono tint.
  - Hue, or **Auto Hue Shift** with a speed (cycling colours).
  - RGB multiplier, saturation, brightness, contrast, HDR, ACES.
  - A filter mask.
- **Live or baked:** baked when static and applied to the base colour; live when post or
  auto-shifting.
- **Unified:** colour adjustment with a target (base colour or final colour), a mask, and the
  adjustments, with time modulation on hue for auto-shift. Colour masking is the recolour
  layer from Toon Standard's Color Mask.

---

## Flipbook

- **Controls:** two layers, each with a toggle.
  - Mode: a flipbook asset (texture array) or a sprite sheet (columns and rows, frame size,
    clip edges).
  - Colour, blend mode (Add, Sub, Mul, Mul×2, Overlay, Screen, Lerp), brightness, Unlit, use
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
    colour.
  - Texture and colour, with texture alpha in cutout modes.
  - Thickness and multiplier; **range** (outlines only within a distance of the camera).
  - A thickness mask (under Masks).
- **Unified:** the outline feature from the VRChat Mobile draft, extended with lit or unlit,
  emission, colour texture, and a maximum distance.

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
    base colour dissolve, UV distortion, outline thickness.
  - **Oscilloscope:** draws the audio waveform onto the surface, with colour, margins,
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

- **Modulators** now have four sources: time (pulse, auto hue, scroll), audio, light level and
  noise (UV distortion). Uber applies audio to five different targets. That settles it:
  modulators are a general mechanism that drives any numeric parameter, not something
  built into emission.
- **Ramps can be described by numbers or textures.** The parser can turn a parameterised ramp
  into a ramp (gradient), so the lighting family needs only one ramp representation.
- **Placement before or after lighting** (Unlit toggles) appears on matcaps, rims and
  flipbooks. It's a general layer property.
- **Masks** are per feature, packed or separate. The unified material only needs a channel
  reference per feature mask.
- **Decals, flipbooks and transition layers** are new layer types; Poiyomi will refine them.
- **Avatar render options:** mirror visibility, near clipping and fallback shader belong in
  the render definition.
- **Atlasing limits:** some settings can't differ inside one merged material: blend mode,
  culling, mirror behaviour, outline on or off, stencil, fallback shader. Sources that
  disagree on these can't share an output. See the open questions.

## Parser work

`AtlasMochieUberParser` handles these points:
- **Section toggles** gate everything; values behind a disabled section don't count.
- **Masks:** resolve separate or packed masks to the feature that uses them.
- **Metallic workflow:** a texture replaces its slider.
- **Map filters** are reported, not applied yet. They become remaps on the scalar inputs once
  the scalar input has a remap.
- **Manual Blend shadows** become a ramp.
- **Uber X** materials: enabled X features are reported once.

**Reported, not mapped yet:** outline, refraction, iridescence, cubemap base colour, mirror
behaviour, base colour dissolve, flipbooks, UV distortion, vertex manipulation, AudioLink,
hard edges, map filters, dithered blending (approximated as cutout), and post filtering
(approximated as a base colour adjustment).

## Open questions

1. **Uber X:** treat like Poiyomi Pro: unsupported unless you can provide the source?
2. **Static vertex manipulation** (expansion, rounding, rotation, position): bake into the mesh
   at build time, so it works with any output shader, or carry it as a geometry feature?
   On avatars these are deliberate looks, not world props.
3. **Settings that can't be merged** (blend mode, culling, mirror behaviour, outline, stencil,
   fallback shader): should the Atlaser report sources that disagree within one output, or
   should the user's output configuration simply define them, so sources are rendered with
   the output's settings?
4. **Mochie versions:** Nordics Nardo has Uber v1.34.2, one minor version behind. Same
   question as for Poiyomi: support older material versions, or expect an upgrade first?
