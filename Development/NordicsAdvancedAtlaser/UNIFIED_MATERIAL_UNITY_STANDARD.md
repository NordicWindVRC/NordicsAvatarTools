# Unified material — Unity Standard

Part of the per-shader inventories that [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds
on, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md), whose names take
precedence. It also describes the parser; same format as
[the VRChat Mobile inventory](UNIFIED_MATERIAL_VRCHAT_MOBILE.md).

**Source:** Unity 2022.3.22f1. The behaviour comes from the shader include files in the editor
(`Editor/Data/CGIncludes/UnityStandard*.cginc`). `Standard.shader` and its inspector
(`StandardShaderGUI`) aren't part of the editor install, so the inspector layout is described
from Unity's documentation and public reference source. An inspector detail found to be wrong
here is a parser fix, not a design change.

**Covers** both `Standard` (metallic setup) and `Standard (Specular setup)`; they differ only in
the *Metallic / Specular* feature below.

**Inspector:** Rendering Mode; **Main Maps**: Albedo, Metallic or Specular, Smoothness, Normal
Map, Height Map, Occlusion, Detail Mask, Emission, Tiling and Offset; **Secondary Maps**: Detail
Albedo ×2, Normal Map, Tiling and Offset, UV Set; **Forward Rendering Options**: Specular
Highlights, Reflections; **Advanced Options**: render queue, GPU instancing, double-sided GI.

**Shared tiling:** like Standard Lite, every main map (metallic or specular, normal, height,
occlusion, detail mask, emission) uses the **albedo's tiling and offset**; the inspector shows
only one Tiling and Offset for the section. The detail albedo and detail normal share a second
one.

There's **no vertex colour** and **no culling control**: back faces are always culled.

---

## Rendering Mode
How the surface uses alpha.
- **Controls:** Opaque, Cutout (with Alpha Cutoff), Fade, Transparent.
- **Behaviour:**
  - **Cutout** discards pixels below the cutoff.
  - **Fade** fades everything, highlights and reflections included, so the surface can vanish
    completely.
  - **Transparent** fades only the diffuse colour; highlights and reflections stay, like
    glass.
- **Live or baked:** live; the alpha source is baked.
- **Unified:** render: surface mode *opaque*, *cutout* with threshold, *fade* (everything
  fades), *transparent* (glass-like: reflections stay). The last two are the same alpha with
  different treatment of specular, which other shaders also offer.
- **Parser:** surface modes *opaque*, *cutout* with the cutoff, *fade*
  and *transparent*, from the shader's keywords; depth write.

## Albedo
Base colour and transparency.
- **Controls:** Albedo texture and colour.
- **Behaviour:** colour × texture. Alpha is texture alpha × colour alpha, **except** when
  Smoothness Source is *Albedo Alpha* (see *Smoothness*): then alpha is the colour's alpha
  alone.
- **Live or baked:** baked.
- **Unified:** base colour layer; alpha source.
- **Parser:** colour (linear) and texture. With smoothness in the
  albedo's alpha, the alpha source is the colour's alpha alone (*base colour constant alpha*).

## Metallic (metallic setup)
- **Controls:** Metallic texture (metallic in red, smoothness in alpha) **or**, with no
  texture, a Metallic slider. The inspector hides the slider once a texture is set.
- **Behaviour:** the texture **replaces** the slider; it doesn't multiply it. The slider is
  stored gamma-encoded.
- **Live or baked:** baked.
- **Unified:** surface response: metallic.
- **Parser:** with a map (keyword on) the constant is 1 and the map's
  red channel is metallic; otherwise the slider, converted from gamma to linear.

## Specular (specular setup)
Specular colour instead of metallic: sets how strong and what colour reflections are, directly.
- **Controls:** Specular texture (colour in RGB, smoothness in alpha) **or** a Specular colour.
- **Behaviour:** as with metallic, the texture replaces the colour.
- **Live or baked:** baked.
- **Unified:** surface response: specular colour, its own input next to metallic (catalogue
  2.4). Converting specular colour to metallic and reflectance would be lossy for coloured
  specular on non-metals, so both workflows are kept.
- **Parser:** the specular colour (linear), or with a map, white × the
  map.

## Smoothness
- **Controls:**
  - **Smoothness** slider; with a metallic or specular texture it becomes a scale on the
    texture's alpha.
  - **Source:** *Metallic/Specular Alpha* or *Albedo Alpha*.
- **Behaviour:** with *Albedo Alpha*, smoothness comes from the albedo texture's alpha × scale,
  and that alpha no longer means transparency.
- **Live or baked:** baked.
- **Unified:** surface response: roughness, from a channel of any texture.
- **Parser:** roughness = 1 − (map alpha × scale), from the metallic or
  specular map or the albedo; otherwise 1 − the slider.

## Normal Map
- **Controls:** texture and scale.
- **Live or baked:** baked; part of normal compositing.
- **Unified:** primary normal layer.
- **Parser:** only with the normal-map keyword; albedo tiling; scale as
  strength.

## Height Map
Parallax: shifts the texture lookup by viewing angle, so surfaces look deeper than they are.
- **Controls:** texture (green channel) and scale (0.005–0.08).
- **Behaviour:** a single-step offset, applied to every texture lookup, detail included.
- **Live or baked:** live; it depends on the view. The height map itself is baked.
- **Unified:** a parallax feature: height channel, depth, method (single step here; other
  shaders use multi-step).
- **Parser:** parallax, single step, green channel, depth; only with
  the keyword and a map; albedo tiling.

## Occlusion
- **Controls:** texture (green channel) and strength.
- **Behaviour:** darkens ambient light and reflections, not direct light.
- **Live or baked:** baked map, live effect.
- **Unified:** occlusion: channel, strength around one.
- **Parser:** green channel, strength around one, albedo tiling.

## Emission
- **Controls:** an Emission checkbox; HDR colour and texture; a Global Illumination option.
- **Behaviour:** texture × colour, added after lighting.
- **Live or baked:** baked.
- **Unified:** emission layer.
- **Parser:** an emission surface layer (texture × HDR colour in
  linear, blend *add*) when the keyword is on. The Global Illumination option isn't visual on
  avatars.

## Detail
A second, usually tiled, albedo and normal map, faded in by a mask.
- **Controls:**
  - Detail Mask (alpha channel), in Main Maps, on the main tiling.
  - Detail Albedo ×2; Detail Normal Map with scale; their own Tiling and Offset.
  - UV Set: UV0 or UV1.
- **Behaviour:**
  - Albedo × (detail × 2), faded to white by the mask.
  - Normal blended with Unity's `BlendNormals`, faded by the mask.
  - The shader code also has multiply, add and lerp blends, but `Standard.shader` compiles
    only ×2, and the inspector offers nothing else.
- **Live or baked:** baked.
- **Unified:** detail layer with *multiply ×2* blend; detail normal with whiteout blend; shared
  mask.
- **Parser:** with the ×2 keyword, a colour layer (blend *multiply ×2*)
  and, with the normal-map keyword, a normal layer (strength = detail normal scale) on the
  detail tiling and UV set, sharing the detail mask (alpha, albedo tiling).

## Specular Highlights and Reflections
- **Controls:** two toggles, both **on** by default.
- **Behaviour:** Highlights turns off direct-light highlights; Reflections turns off reflection
  probes, which then become a flat colour from the sky's ambient specular.
- **Live or baked:** live.
- **Unified:** surface response: direct highlights and environment reflections on or off.
  Same as Standard Lite.
- **Parser:** from the *_OFF* keywords.

## Lighting
Unity's physically based lighting: GGX highlights, Lambert diffuse, full light probe ambient,
reflection probes, real-time shadows received and cast. Nothing to set.
- **Unified:** the *physically based* lighting family, ambient model *directional*
  (full light probes), receives shadows.
- **Parser:** *physically based*, Disney diffuse, ambient
  *directional*, receives shadows.

## Not visual for avatars
- Emission's **Global Illumination** option, **double-sided global illumination**: lightmap
  baking only.
- **GPU instancing**, **render queue**: performance and sorting. The render queue does matter
  for transparent materials drawn in a fixed order, but Rendering Mode already sets it.

---

## Cross-shader notes

- **Standard and Standard Lite are nearly the same feature set.** The differences are vertex
  colour (Lite always multiplies), alpha (Lite is always opaque), parallax (Standard only),
  the slider rules (Lite multiplies the texture by the slider, Standard replaces it), and the
  default for highlights and reflections. A unified surface response covers both. The
  differences are all parser work.
- **Map replaces constant versus map times constant:** sources differ, so parsers must set the
  constant to 1 when the source ignores it. That fits the existing scalar model; no unified
  change needed.
- **Fade versus transparent** is a real visual difference between sources, and needs to be
  explicit in the unified surface mode.
- **Workflows:** metallic and specular-colour. Mochie and Poiyomi will show whether specular
  colour is common enough to keep as its own input.
- **Parallax** first appears here; Mochie and Poiyomi have richer versions, so it should be a
  feature with a method parameter.

## Coverage

Every section is mapped; the *Not visual* items are ignored without a report line.
