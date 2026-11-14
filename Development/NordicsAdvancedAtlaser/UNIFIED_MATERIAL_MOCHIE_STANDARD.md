# Unified material — Mochie Standard

**Status:** first draft, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md); the
catalogue's names take precedence. Part of the per-shader inventory that
[`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds on; same format as
[the VRChat Mobile draft](UNIFIED_MATERIAL_VRCHAT_MOBILE.md).

**Source:** Mochie Shaders v2.14.2, in the dev project under `Assets/Mochie/Standard Shader/`,
with the inspector in `Assets/Mochie/Unity/Editor/StandardEditor.cs`. Nordics Nardo has an
older version (about v2.10); differences between versions aren't covered here.

**Variants:**

| Shader | What it drops |
|---|---|
| `Mochie/Standard` | Nothing; the full feature set |
| `Mochie/Standard Lite` | Subsurface scattering, detail metallic/roughness/occlusion and detail packed map, detail sampling modes |
| `Mochie/Standard Mobile` | As Lite, plus parallax, stochastic/supersampled/triplanar sampling, emission pulse and mask, alpha source |

**Character:** a general physically based shader, mostly aimed at worlds. Avatars use it too,
typically for props and hard-surface parts. Mochie's dedicated avatar shader is **Uber**, which
the Atlaser doesn't support yet (see the open questions).

**Scope:** avatar use only. Features meant for world surfaces are listed once under
[*World-only features*](#world-only-features) and not inventoried. What's left, compared with
Unity Standard: four sampling methods, detail layers for every surface map, colour filtering,
emission pulse and AudioLink, subsurface scattering, and VRChat light volumes and LTCGI.

**Inspector sections covered:** Shader Variant, Primary Textures (with Emission), Detail
Textures, Specularity, Subsurface Scattering, Filtering, Extra UVs, Light Volumes, LTCGI,
Render Settings.

---

## Shader Variant

### Specular model
- **Controls:** Unity Standard or Google Filament.
- **Behaviour:** two specular formulas. Filament is more accurate at grazing angles and
  rough surfaces. The choice changes which Specularity controls are shown.
- **Live or baked:** live.
- **Unified:** a parameter of the *physically based* lighting family, if output shaders offer
  it; otherwise the difference is small enough to drop. The parser already records it.
- **Parser today:** reads it.

### Blending Mode and alpha
- **Controls:**
  - Opaque, Cutout, Fade or Transparent; the same meaning as Unity Standard.
  - **Alpha Source:** base colour alpha, an Alpha Mask (texture, channel, opacity, its own UV
    set), or vertex colour alpha.
  - **Cutout:** Alpha Cutoff, and **Mip Map Rescaling** with a scale. Cutout uses
    alpha-to-coverage with sharpened edges, and rescaling keeps thin cutouts like foliage or
    hair from vanishing in the distance.
- **Live or baked:** live mode; the alpha itself can be baked into the base colour's alpha,
  whatever its source.
- **Unified:** surface mode and cutoff; alpha as a scalar input with any source (texture
  channel, separate mask, vertex alpha); cutout edge handling (sharpened coverage, distance
  preservation) as cutout parameters.
- **Parser today:** reads mode, cutoff, alpha-to-coverage and the alpha mask. **The alpha
  source choice is only recorded as a raw choice**, so with *Alpha Mask* or *Vertex Color
  Alpha* the base colour alpha is still treated as the alpha. Mip rescaling is recorded raw.

### Smoothness or roughness
- **Controls:** "Use Smoothness": whether the roughness inputs mean roughness (default) or
  smoothness.
- **Unified:** none; the parser converts everything to roughness.
- **Parser today:** reads it for the separate roughness map. **The specular-alpha source
  always inverts**, whatever this toggle says (see *Specular workflow*).

---

## Primary Textures

### Workflow
- **Controls:** Metallic (separate maps), Packed Metallic (one channel-packed map), or
  Specular.
- **Behaviour:**
  - **Separate maps:** metallic, roughness and occlusion each read the **green** channel of
    their own texture, times a strength.
  - **Packed:** one texture, with a selectable channel and strength for metallic, roughness,
    occlusion and optionally height.
  - **Specular:** specular colour map × colour; roughness from its own map or from the
    specular map's alpha.
- **Live or baked:** baked.
- **Unified:** surface response scalars, each a channel of any texture × constant. Packing
  is a property of the texture reference, not a separate workflow. Specular colour as in
  [Unity Standard](UNIFIED_MATERIAL_UNITY_STANDARD.md).
- **Parser today:**
  - Packed: correct.
  - **Separate maps use the packed map's channel settings** (defaults blue for metallic,
    green for roughness), where the shader always reads green. Metallic from a separate map
    is therefore read from the wrong channel by default.
  - Specular with roughness from the specular alpha: always treated as smoothness, though
    the shader follows the smoothness toggle.

### Sampling mode
- **Controls:** Default, Stochastic, Supersampling, or Triplanar (local or world space).
  Separate settings for primary and detail textures.
- **Behaviour:**
  - **Stochastic** hides visible tiling by blending randomly offset copies.
  - **Supersampling** sharpens distant texture detail.
  - **Triplanar** ignores UVs and projects from three axes.
  - All of them apply to every texture in the group.
  - Stochastic and triplanar are mostly for large world surfaces; on avatars they're rare.
- **Live or baked:** stochastic and supersampling are filtering choices; dropping them changes
  little at atlas sizes. Triplanar depends on position, so it can only be baked if the mesh
  has unique UVs; otherwise it stays live.
- **Unified:** a sampling method on the texture mapping, with triplanar as a coordinate space.
  The existing mapping fields already do this.
- **Parser today:** applies the mode to the base colour only, not to the other maps in the
  group, and records the rest as raw values.

### Base colour
- **Controls:** texture and colour.
- **Behaviour:** texture × colour, then **filtered** (see *Filtering*), then detail is
  blended in, then × vertex colour if Render Settings → Vertex Base Color is on.
- **Live or baked:** baked.
- **Unified:** base colour layer.
- **Parser today:** correct; vertex colour read.

### Normal map
- **Controls:** texture and strength.
- **Unified:** primary normal layer.
- **Parser today:** correct.

### Height map (parallax)
- **Controls:** texture and strength, a height mask with channel, a step count, and an offset
  in the packed workflow.
- **Behaviour:** multi-step parallax, offsetting all primary texture lookups.
- **Live or baked:** live.
- **Unified:** the parallax feature from the Unity Standard draft, with method *multi-step* and
  a step count, plus a mask.
- **Parser today:** reads map, strength, channel, offset and steps. The mask is a separate
  named mask, not linked to parallax.

### UV settings
- **Controls:** UV set (UV0–UV4, or world or local position with axis swizzle), tiling and
  offset, scroll speed, rotation. Separate settings for primary, detail, and each mask under
  *Extra UVs*.
- **Live or baked:** baked, except scrolling and world/local projection.
- **Unified:** texture mapping: UV set or projection space, tiling, offset, rotation, scroll.
- **Parser today:** reads all of it for the base colour; the masks' UV settings only as raw
  values. Doesn't apply the main UV settings to the other primary maps, which share them.

### Emission
- **Controls:** an Emission checkbox; texture, HDR colour and strength; a mask with channel;
  **Pulse** (off, or sine, square, triangle, saw, reverse saw, with speed and strength);
  **AudioLink band** (off, or bass, low mids, upper mids, highs, with strength and min/max
  remap).
- **Behaviour:**
  - Texture × colour × strength, filtered, × pulse × mask × AudioLink.
  - **The colour is raised to the power 2.2 first.** That looks like a gamma conversion on
    top of Unity's own; the parser should convert the same way so the result matches.
- **Live or baked:** texture × colour × mask baked; pulse and AudioLink live.
- **Unified:** emission layer with mask, plus two modulations: **time** (waveform, speed,
  depth) and **audio** (band, strength, range). The audio modulation should share the
  audio-reactive vocabulary from Toon Standard's AudioLink.
- **Parser today:**
  - **Enabled whenever strength isn't 0,** not by the Emission checkbox.
  - The 2.2 power isn't applied.
  - Pulse and AudioLink are raw values; the mask is a separate named mask, not linked to
    emission.

---

## Detail Textures

A second set of surface maps faded in by a mask. Mochie's version is the most general so far:
every surface property can be detailed.
- **Controls:**
  - Workflow: separate or packed detail maps. Sampling mode.
  - Detail Tint; detail base colour with strength and a blend mode: Add, Alpha, Multiply,
    Multiply ×2, Overlay, Screen, Lerp.
  - Detail normal with strength.
  - Detail metallic, roughness and occlusion, each with strength and the same blend modes.
  - Detail Mask with channel and **mode: Blend or Replace.** Replace swaps the base values
    for the detail values instead of blending.
  - UV set, tiling, scroll, rotation.
- **Behaviour:**
  - Each blend is faded by strength × mask.
  - The detail base colour has its own filtering.
  - The detail normal's strength is scaled by the mask.
- **Live or baked:** baked.
- **Unified:** detail layer with a target (base colour, normal, metallic, roughness,
  occlusion), blend mode, strength, mask and mapping. That means one detail layer type that
  can target any surface input, rather than separate albedo and normal fields. Replace mode is
  blend *lerp* at full mask.
- **Parser today:** base colour and normal detail are typed, with the blend mode. **Detail
  metallic, roughness and occlusion** are raw values, and so is the mask mode.

---

## Specularity

- **Controls (avatar-relevant):**
  - Specular Highlights toggle with strength; Reflections toggle with strength.
  - Fresnel toggle with strength (Unity model only).
  - **Contact Hardening:** sharper reflections near the reflecting surface.
  - GSAA (specular anti-aliasing) with strength.
- **Live or baked:** live.
- **Unified:** surface response: highlight strength and reflection strength (not only on and
  off), fresnel strength, specular anti-aliasing strength. Contact hardening is a reflection
  quality option a writer can only reproduce if its shader has it; record it, but it doesn't
  change the base look much.
- **Parser today:** toggles typed; strengths and the rest raw.
- Screen-space reflections and specular occlusion are world-only; see
  [*World-only features*](#world-only-features).

---

## Subsurface Scattering (Standard only)

Light passing through thin parts, like ears or leaves.
- **Controls:** a section toggle; thickness map with power; colour; tint by base colour;
  intensity, ambient intensity, power, distance; wrapping factor.
- **Live or baked:** live; the thickness map baked.
- **Unified:** subsurface layer: thickness map, colour, base-colour tint, intensity, ambient
  contribution, falloff (power and distance), wrap.
- **Parser today:** typed layer for map, colour, intensity and thickness; the rest raw.

---

## Filtering

Colour adjustments without editing textures.
- **Controls:** a section toggle; Hue Mode (HSV or Oklab); Mono Tint (makes the hue a flat
  colour); then hue, saturation, contrast and brightness **separately for base colour,
  detail, emission, and the final lit colour (Post)**; ACES tone mapping strength for Post.
- **Behaviour:** applied in that order; Post applies to the lit diffuse colour.
- **Live or baked:** base, detail and emission can be baked when not animated. Post affects
  lighting, so it stays live.
- **Unified:** colour adjustment with a target, the same structure the VRChat Mobile draft
  proposed for hue shift. The target list grows to include *final colour*; adjustments are
  hue (with method), saturation, contrast, brightness, and an optional tone map for the
  final colour.
- **Parser today:** base colour hue, saturation, contrast and brightness are typed. The rest is
  raw, and **the section toggle isn't checked,** so a disabled section's values still
  apply.
- **Not visual:** the Color Grading LUT has properties, but the shader code that applies it is
  commented out.

---

## Light Volumes and LTCGI

World-provided lighting that also lights avatars: **VRChat light volumes** replace or add to
light-probe lighting; **LTCGI** adds light from video screens and area lights. Avatar shaders
such as Poiyomi support both, so they're in scope.
- **Controls:**
  - Light Volumes: toggles and strengths for normal and additive volumes, specularity, a
    bias.
  - LTCGI: strength, roughness, diffuse and specular colours, specular occlusion.
- **Live or baked:** live; they only do something in worlds that provide them.
- **Unified:** the existing external-lighting definition: on/off and strength per system. The
  remaining detail is tuning a writer can pass through if its shader supports the system.
- **Parser today:** typed on/off and main strengths; the rest raw.

## Render Settings

- **Controls:** Cull; Queue Offset; Unity Fog; Vertex Base Color; Flip Backface Normals;
  Primary and Detail wrap/filter settings (sampler textures).
- **Unified:** render: cull mode, fog, two-sided lighting (flip back-face normals); queue
  offset; base colour × vertex colour; texture wrap and filter on the mapping.
- **Parser today:** cull, vertex colour typed; fog, flip back-face normals and queue offset raw;
  wrap/filter not read.

## World-only features

Meant for world surfaces and props; out of scope. The parser should ignore them, without
reporting each value. At most one info line per material when one is switched on.
- **Rain** (droplets, ripples) and **Puddles:** weather on world surfaces.
- **Vertex Manipulation** and **Wind:** static and animated rotation, offset, and noise sway,
  for props and foliage.
- **Screen-space reflections:** need a depth texture that avatars can't count on.
- **Specular occlusion:** reads lightmaps, and avatars aren't lightmapped.
- **AreaLit:** needs per-material light textures from the world's setup.
- **Lightmap Settings:** Bakery modes, lightmap specular, non-linear SH, bicubic lightmap
  sampling, ignore realtime GI, RNM textures. The parser currently reads bicubic as a base
  colour filter, which is wrong: it's lightmap sampling.

**Not visual:** Debug views, the hidden material version and reset properties.

---

## Cross-shader notes

- **Detail as a general layer:** Mochie can detail every surface input, each with its own
  blend. With Unity Standard and Toon Standard as subsets, one detail layer type that
  targets any input covers all three.
- **Colour adjustment targets** now include *final colour*, which is live. That also covers
  Toon Standard's hue shift. So far, the targets are base colour, detail, emission and final
  colour.
- **Modulations on emission:** time waveform (Mochie) and audio (Mochie, Toon Standard).
  Both should be generic modulators that could later drive other features (Poiyomi will
  show whether that's needed).
- **Strengths, not just toggles,** for highlights and reflections.
- **Alpha from any source:** base colour alpha, a separate mask, or vertex alpha.
- **Sampling methods** belong on the texture mapping and apply to every texture that shares
  the mapping.
- **World-provided lighting** (light volumes, LTCGI) is a lighting input the avatar receives,
  not a surface feature: on/off and strength per system.
- **World-only features** are ignored, not unmapped: they don't count against the coverage
  goal.

## Parser fixes found

1. **The shader names Lite and Mobile aren't accepted;** those materials go to the fallback
   parser. One parser can handle all three, since the variants are subsets.
2. **Separate metallic, roughness and occlusion maps use the packed channel settings;** the
   shader reads green.
3. **Emission is enabled by strength, not the Emission checkbox;** the colour's power 2.2
   isn't applied.
4. **Alpha source** (alpha mask, vertex alpha) isn't applied to the base colour alpha.
5. **Filtering's section toggle** isn't checked.
6. **Sampling mode and main UV settings** reach the base colour only, not the other primary
   maps.
7. **Specular-alpha roughness** always inverts, ignoring the smoothness toggle.
8. **Bicubic sampling** is lightmap-only, not a texture filter.
9. **World-only features** (rain, puddles, wind and vertex manipulation, screen-space
   reflections, specular occlusion, AreaLit, Bakery) are read as raw values today. They
   should be dropped from the parser.
10. Typing the raw values: detail scalar layers, colour adjustment targets, emission
    modulations, the masks' links to the features that use them.

Nothing has been changed yet.

## Open questions

1. **Mochie's emission colour power 2.2:** I read it as a quirk to reproduce: the parser
   converts, so emission matches. It could also be intended as authoring in gamma. Either
   way the parser absorbs it; the question is only whether you know Mochie's intent.
2. **Older Mochie versions** (Nordics Nardo has about v2.10): Mochie stamps a material
   version and has an upgrader. Should the parser support older material versions, or
   expect users to upgrade first?
3. **Decided:** Mochie Standard will be dropped in favour of
   [Mochie Uber](UNIFIED_MATERIAL_MOCHIE_UBER.md), Mochie's avatar shader, once the inventory
   is finished. Until then this draft stays for reference in the refinement pass.
4. **Light volumes and LTCGI:** I've kept them in scope because they light avatars in worlds
   that provide them, and Poiyomi supports both. Correct me if you'd rather treat them as
   world features.
