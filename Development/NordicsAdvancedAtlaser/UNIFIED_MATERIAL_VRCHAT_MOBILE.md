# Unified material — VRChat Mobile shaders

Part of the per-shader inventories that [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds
on, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md), whose names take
precedence. It also describes the parser. It lists what each shader lets the user change, what that does to
the look, and how the unified material represents it.

**Source:** VRChat SDK 3.10.6 (`com.vrchat.base`), folder
`Runtime/VRCSDK/Sample Assets/Shaders/Mobile/`: the shader files, `VRChat.cginc`, the
`ToonStandard/CG/` includes, and the inspectors in `GUI/`. Only what the inspector shows is
inventoried; properties the inspector doesn't show are listed under *Not visual* when they exist.

**Each feature lists:**
- **Controls:** what the user sets, grouped as in the inspector.
- **Live or baked:** *live* means the effect depends on view, light, time or audio and must
  stay a unified feature with parameters; *baked* means a static result that can go into
  textures, unless something animates it.
- **Unified:** its representation in the unified material.
- **Parser:** what the parser reads.

| Shader | Inspector | Size |
|---|---|---|
| Toon Lit | Unity's default: one texture with tiling | 1 feature |
| Standard Lite | `StandardLiteShaderGUI`: Main Maps, Secondary Maps, Forward Rendering Options | about 12 features |
| Toon Standard (and Outline) | `ToonStandardEditor`: foldout sections, most with an on/off toggle | about 15 features |

---

## Toon Lit

A flat, unlit-looking toon shader for low-end avatars. There's no shading across the surface
at all: the normal isn't used. Every pixel gets the same light: the main directional light's
colour, times its shadow, plus the ambient colour. Other lights count only through the
ambient term. It's always opaque, back-face culled, and multiplies by vertex colour.

### Base colour
Texture times vertex colour.
- **Controls:** texture, tiling and offset. No tint colour.
- **Live or baked:** baked. The vertex colour stays per-vertex (see *Vertex colour* under
  [Cross-shader notes](#cross-shader-notes)).
- **Unified:** base colour texture; vertex colour *multiply colour* on, used as stored (not
  sRGB data). Alpha source *none*: the output is always opaque.
- **Parser:** mapped as above.

### Lighting
Uniform light per object, no direction; receives shadows. When the world has no directional
light, the direct light becomes 20% of the ambient colour.
- **Controls:** none.
- **Live or baked:** live.
- **Unified:** a *flat* lighting family: no shading gradient across the surface, light =
  direct colour × shadow + ambient. This is the same look as a toon ramp that's flat
  everywhere, apart from receiving shadows, so it might merge into the ramp family as a
  preset.
- **Parser:** family *flat*, ambient model *average*, received
  shadows on, additional and vertex light contributions 0 (the shader has no add pass and
  doesn't read vertex lights).
- **Not represented:** the shader's own fallback when the world has no directional light (20%
  of the ambient colour). It's not a material setting; a flat writer has its own behaviour.

### Render
Always opaque, back faces culled, depth written, queue Geometry: the unified render defaults.
The fallback shader (*VRChat/Mobile/Diffuse*) isn't represented: mobile shaders are never
blocked.

### Not visual
Nothing; the shader has one property.

**Coverage:** the one property is mapped; nothing is reported.

---

## Standard Lite

A cut-down Standard (metallic workflow) for mobile. Always opaque: albedo alpha is ignored.
Supports one per-pixel directional light; other lights come in through light probes. It
doesn't receive real-time shadows.

**Shared tiling:** every main-section map (metallic/smoothness, normal, occlusion, emission,
detail mask) uses the **albedo's tiling and offset**. Their own tiling fields are hidden and
ignored. The detail albedo and detail normal share a second tiling, set under Secondary Maps.

### Base colour
Albedo texture × colour × vertex colour.
- **Controls:** Albedo texture and colour; tiling and offset.
- **Live or baked:** baked.
- **Unified:** base colour layer. Vertex colour multiply is **always on**: the switch for it
  exists only as a shader keyword with no inspector control.
- **Parser:** texture and colour (linear), albedo tiling; vertex colour
  *multiply colour* on unless `DISABLE_VERTEX_COLORING` is set; alpha source *none*.

### Metallic and smoothness
How metallic and how glossy the surface is.
- **Controls:** one texture (metallic in red, smoothness in alpha) with a Metallic slider, and
  a Smoothness slider. The sliders **multiply** the texture; they don't replace it.
- **Live or baked:** baked (scalar maps).
- **Unified:** surface response: metallic and roughness, each a constant times a channel.
- **Parser:** metallic = map red × the slider converted from gamma to
  linear; roughness = 1 − map alpha × the Smoothness slider; albedo tiling.

### Normal map
- **Controls:** texture and scale.
- **Live or baked:** baked; part of normal compositing.
- **Unified:** primary normal layer.
- **Parser:** always applied (no keyword), albedo tiling, scale as
  strength.

### Occlusion
Darkens ambient light in creases. Read from green.
- **Controls:** texture and strength (0 = none, 1 = full).
- **Live or baked:** baked as a map. Its effect is live: it scales ambient light only, not
  direct light.
- **Unified:** occlusion: channel, strength around one.
- **Parser:** green channel, strength around one, albedo tiling.

### Emission
Glow on top of lighting.
- **Controls:** an Emission checkbox; when on, texture and HDR colour.
- **Live or baked:** baked (texture × colour), with the HDR intensity kept as a scale.
- **Unified:** emission layer.
- **Parser:** an emission surface layer when the keyword is on.

### Detail
A second, usually tiled, albedo and normal map, faded in by a mask.
- **Controls:**
  - Detail Mask (alpha channel), in the main section on the main tiling.
  - Detail Albedo ×2 and Detail Normal with scale, with their own tiling.
  - UV Set (0 or 1) for the detail maps.
- **Blend:** albedo × (detail × 2), faded to white by the mask; normals blended with Unity's
  `BlendNormals`, faded by the mask.
- **Live or baked:** baked; it's static colour and normal maths.
- **Unified:** detail layer: albedo with *multiply ×2* blend; detail normal with whiteout
  blend; shared mask; UV set and tiling.
- **Parser:** with the `_DETAIL` keyword, Unity's detail as in Standard
  (shared code): a *multiply ×2* colour layer and a normal layer, sharing the mask.

### Specular highlights
The shader labels the toggle "Specular Lightprobe Hack". On, it adds GGX highlights from the
directional light and, when there's no directional light or reflection probe, a highlight
from the light probes' dominant direction. **Default off.**
- **Controls:** toggle.
- **Live or baked:** live.
- **Unified:** surface response: direct highlights on or off. The probe-direction highlight is
  the shader's way of producing highlights without a light, so it belongs to the lighting
  family rather than being a separate feature.
- **Parser:** on unless `_SPECULARHIGHLIGHTS_OFF` is set.
- **Not represented:** the highlight from the light probes' dominant direction when there's
  no directional light or reflection probe. It's the shader's own behaviour, not a material
  setting.

### Reflections
Reflection probe reflections. **Default off.**
- **Controls:** toggle.
- **Live or baked:** live.
- **Unified:** surface response: environment reflections on or off.
- **Parser:** on unless `_GLOSSYREFLECTIONS_OFF` is set.

### Specular anti-aliasing
Reduces sparkling highlights on small or curved detail by roughening where the surface normal
changes quickly on screen.
- **Controls:** toggle, Variance and Threshold.
- **Live or baked:** live, but minor.
- **Unified:** a surface response parameter. Toon Standard always applies its own version, so
  this fits as a shared parameter.
- **Parser:** the keyword, variance and threshold.

### Not visual for avatars
- **Lightmap Type** (Default or MonoSH) and **bicubic sampling**: they only affect lightmaps,
  which skinned avatars don't use. Ignored.
- **GPU instancing**, **double-sided global illumination**: performance and baking options.

### Lighting
Physically based, ambient along the normal (L1 light probes), one per-pixel directional light
(no add pass: other lights through vertex lights and probes), no received shadows (`noshadow`).
The BRDF is VRChat's mobile one on Quest (Lambert diffuse) and Unity's on PC (Disney diffuse).
- **Parser:** *physically based*, diffuse *Lambert* (the shader's
  mobile target), ambient *directional*, received shadows off, additional lights 0.

**Coverage:** every section is mapped; lightmap, bicubic, instancing and GI options
are ignored without a report line.

---

## Toon Standard and Toon Standard (Outline)

VRChat's toon shader for mobile avatars. Opaque only; blend modes exist in the code but are
commented out. Each foldout section has an on/off toggle that sets a shader keyword. **When a
section is off, its settings have no effect,** whatever their values.

**Tiling:** every texture has its own tiling and offset, shown in a foldout. The exceptions
are the matcap, which uses no tiling, and the detail mask, which always uses UV0.

**Since:** Toon Standard appears in SDK 3.8; colour masks and AudioLink were added after
3.10.2. Older materials simply lack those properties.

### Main
- **Base colour:** Main Texture × Main Color, optionally × vertex colour (Apply Vertex Color
  toggle, default off). Baked. **Unified:** base colour layer; vertex colour multiply as
  read.
- **Culling:** Off, Front or Back. On back faces the normal is flipped, so two-sided surfaces
  light correctly. **Unified:** render: cull mode and double-sided lighting.
- **Normal map:** texture and scale, behind a "Use Normal Map" toggle that also gates the
  detail normal. Baked. **Unified:** primary normal layer.

### Lighting
Toon shading from a ramp, with controls for how dark and how coloured the shadows get.
Ambient light is flat: the average light-probe colour, with no direction. Shadow
attenuation feeds into the ramp, so received shadows follow the ramp's shape.

- **Controls:**
  - **Shading:** a preset (Flat, Realistic, Realistic Soft, Realistic Very Soft, Toon 2, 3 or 4
    Band) or a custom ramp texture. Each preset is a ramp texture shipped with the SDK.
  - **Shadow Boost:** lifts shadows toward full light.
  - **Shadow Tint:** colours shadows with the albedo: 0 gives black shadows, 1 gives shadows
    in the albedo's colour, so the darkening is a saturated version of the surface colour.
  - **Min Brightness:** a floor of up to 0.1 × albedo, added like emission.
  - **Limit Brightness:** clamps incoming light to 1, so bright worlds don't blow out colours.
    Default on.
- **Live or baked:** live.
- **Unified:** the ramp toon lighting family, with:
  - a ramp (texture or gradient); the presets could become named ramp shapes, so a writer
    can pick its own equivalent;
  - shadow lift, shadow colour from albedo, minimum brightness, light clamp;
  - ambient model *flat* (average colour, no direction).

### Specular
GGX highlights and reflection probe reflections, with a toon option.
- **Controls:** section toggle.
  - Metallic Map with channel and Metallic Strength.
  - Gloss Map with channel and Gloss Strength.
  - **Sharpness:** blends from a soft highlight to a hard-edged toon highlight.
  - **Reflectance:** specular strength for non-metals, also multiplied by the gloss map.
- **Behaviour:** dielectric F0 = 0.16 × reflectance² × gloss map, the Filament convention;
  0.5 gives the usual 4%. Metallic darkens the diffuse colour. Geometric specular
  anti-aliasing is always on.
- **Live or baked:** maps baked; the highlight is live.
- **Unified:** surface response: metallic, roughness, reflectance (F0 for non-metals), and a
  highlight style parameter running from smooth to stylised step. Reflectance is
  a map-driven scalar here, not a constant.

### Matcap
A view-dependent texture lookup, VR-stable: projected with world up, so it doesn't roll
with the head.
- **Controls:** section toggle; matcap texture; mask with channel and strength; type Additive
  or Multiplicative.
- **Behaviour:** the result changes the albedo **before lighting**, so the matcap is lit and
  shadowed like the surface, not glowing. Additive appears in the main light pass only.
  The inspector warns that non-Flat shading with a matcap may look wrong.
- **Live or baked:** live; the mask is baked.
- **Unified:** matcap layer with projection *view, world-up stabilised*, blend add or
  multiply, strength and mask, plus **where it applies: before lighting (on albedo) or after
  (on the final colour)**. Other shaders use both, so this parameter is needed anyway.

### Emission
- **Controls:** no section toggle, so emission is always active. Emission Map, Emission Color,
  Strength (0–2); UV map 0 or 1.
- **Behaviour:** map × colour × strength. Hue shift can apply (see *Hue Shift*).
- **Live or baked:** baked.
- **Unified:** emission layer with UV set.

### Detail
- **Controls:** section toggle.
  - Mode: Alpha Blended, Additive, Multiply or Multiply ×2.
  - Detail Mask with channel, always on UV0.
  - Detail Texture; Detail Normal Map with scale, only when "Use Normal Map" is on.
  - UV map 0 or 1 for the texture and normal.
- **Behaviour:** Alpha Blended uses the detail texture's own alpha × mask. The detail normal's
  strength is scaled by the mask.
- **Live or baked:** baked.
- **Unified:** detail layer with blend mode, mask and UV set; detail normal with mask.

### Occlusion
- **Controls:** section toggle; map with channel (default green); strength.
- **Behaviour:** darkens the ramp result and the ambient light; specular isn't affected.
- **Live or baked:** baked map, live effect.
- **Unified:** occlusion.

### Rim Lighting
A glowing edge where the surface turns away from the viewer.
- **Controls:** section toggle; colour; Albedo Tint (0–1); Intensity; Range; Softness;
  Environmental.
- **Behaviour:**
  - The edge band grows with Range; Softness widens its transition.
  - **Environmental** multiplies the rim by the scene's light, so it fades in dark worlds.
  - **Albedo Tint** multiplies the rim by the surface colour.
  - The rim is added after lighting, so it glows unless Environmental is on.
  - It uses a stereo-centred view direction, so both eyes see the same rim.
- **Live or baked:** live.
- **Unified:** rim layer: colour, intensity, width, softness, albedo tint, light-scaled on or
  off.

### Hue Shift
Rotates hues, for recolouring a texture without editing it.
- **Controls:** section toggle; Albedo Hue Shift, Emission Hue Shift, and Detail Hue Shift
  (shown only when Detail is on), each 0–2π; one shared mask with channel.
- **Behaviour:** rotation around the grey axis, which keeps brightness roughly constant.
  It's applied before colour masks and detail blending.
- **Live or baked:** baked when static. If animated, it stays live.
- **Unified:** a colour adjustment with a target (base colour, detail, emission, …), an amount
  in turns, a mask, and a method (this axis rotation versus HSV and others). Separate
  targets fit better than the current fixed `HueShift`, `DetailHueShift` and
  `EmissionHueShift` fields, which wouldn't scale to other shaders.

### Color Mask
Recolours up to four regions, chosen by the red, green, blue and alpha channels of one mask,
the way paint-by-mask customisation works.
- **Controls:** section toggle; mask texture; Blend Mode Multiply or Additive; four colours,
  whose alpha acts as each region's opacity; four emission strengths.
- **Behaviour:**
  - The region becomes the albedo's greyscale × the colour (Multiply) or greyscale + the
    colour (Additive), blended in by mask × colour alpha.
  - Each region also adds colour × mask × its emission strength to emission.
- **Live or baked:** baked into base colour and emission when static.
- **Unified:** a recolour layer: mask channel, colour, opacity, blend (tint the greyscale, or
  add to it), emission amount. Other shaders have similar "recolour by mask" features, so
  this should be one general layer, not four fixed slots.

### Outline (Outline variant only)
An inverted-hull outline, PC only; on mobile it doesn't render.
- **Controls:**
  - Thickness mask with channel.
  - Thickness multiplier (0–0.5, in centimetres of world space).
  - Colour.
  - Color From Albedo, blending toward the base colour.
- **Behaviour:**
  - The outline is pushed out along the normal.
  - Its on-screen width is capped, so outlines don't get fat when the avatar is scaled down.
  - Unlit, with fog, and multiplied by vertex colour when that option is on.
- **Live or baked:** live, as geometry and a pass; the mask is baked.
- **Unified:** an outline feature: width (world units) with mask, colour, colour from base
  colour, lit or unlit, screen-space width cap.

### AudioLink
Audio-reactive emission, driven by the world's AudioLink.
- **Controls:** section toggle.
  - Tint, Intensity, Blend Mode (Replace, Additive or Multiply).
  - Effect: Pulse, Scroll, Bar, or UV Based, where the UV tile picks the effect.
  - Band: bass, mids, mid highs, highs, or UV based. Smoothing.
  - Scroll scale and centre-out; bar smoothing.
  - Mask: a texture with UV set and channel (RGB, R, G, B or A), or "mask by emission map".
  - Rim mask strength and smoothing, which fade the effect toward or away from the edges.
  - Effect mask channel and UV set.
  - Fallback beat when the world has no AudioLink, with its speed.
- **Live or baked:** live; the masks are baked.
- **Unified:** audio-reactive layer: mode, band, smoothing, tint, intensity, blend into emission,
  mask source, view-angle mask, fallback. In the catalogue: an emission surface layer with an
  audio modulator; Pulse, Scroll and Bar are its spread *none*, *history* and *fill*.
- **Atlasing conflict:** the *UV Based* effect and band, and scroll and bar along UVs, read
  the effect UV channel, and its **integer UV tile carries meaning**. Remapping or clamping
  that channel breaks the effect. See the open questions.

### Parser
`AtlasVRChatToonStandardParser`, for both variants. Every section follows its keyword; a section
that's off maps to nothing.
- **Main:** base colour (linear) × optional vertex colour; alpha *none*; cull from *Culling*;
  two-sided lighting (back faces flip the normal); the normal map only with *Use Normal Map*.
- **Lighting:** *ramp toon*, half-Lambert term, the ramp texture with its preset recognised by
  the SDK's texture names, shadow lift, shadow colour from base, minimum brightness, limit
  brightness; ambient *average*; **no received shadows** (the shader skips the shadow
  variants).
- **Specular:** metallic and gloss maps with their channels × strengths; reflectance × the gloss
  map (`F0 = 0.16 × r²`, as the unified reflectance); highlight sharpness; specular
  anti-aliasing on (the shader's own geometric roughening, mapped to the unified filtering).
  Off: no highlights and no reflections.
- **Matcap:** a lookup layer, projection *view stabilised*, placement *surface* (lit), blend
  add or multiply, strength as opacity, mask with channel; the texture's tiling is ignored, as
  in the shader.
- **Emission:** always on: an emission layer, colour × strength (linear), map on UV0 or UV1.
- **Detail:** a colour layer by mode (alpha blended = *replace* with the source alpha as
  opacity, additive, multiply, ×2) and a normal layer whose strength the mask scales, sharing
  the mask (always UV0).
- **Occlusion:** channel and strength; it darkens direct light too, not reflections.
- **Rim:** an angle layer, placement *overlay*: width = Range (the shader uses `1 − Range` as
  the edge, which the unified band formula includes), softness, intensity as opacity, albedo
  tint as base colour mix, *Environmental* as *scale by light*.
- **Hue Shift:** colour adjustments, *hue* by grey-axis rotation in turns, with the shared mask:
  on the base colour, the detail colour layer and the emission layer.
- **Color Mask:** one surface layer per channel: base colour blend *tint luminance* (Multiply)
  or *add to luminance* (Additive) with the colour's alpha as opacity, plus emission *add* by
  the region's emission strength.
- **Outline:** an outline, width = thickness × 0.01 m, mask on raw UV0, on-screen limit
  0.05 × thickness, colour with base colour mix, unlit, cull front.
- **AudioLink:** an emission layer (tint × intensity, blend replace, add or multiply, opacity
  `saturate(tint alpha × intensity)`) whose colour an audio modulator scales. Mask: the
  emission (map and colour) or the AudioLink mask, all of RGB or one channel (the channel is no
  longer off by one). Pulse, Scroll and Bar are the spreads *none*, *history* (UV u, a mask
  channel, or the view angle for centre-out) and *fill* (along v).
- **Reported, not mapped:** AudioLink's rim mask (a view-angle mask, not typed), its UV-based
  mode and band (meaning in the UV tile; atlasing pass), and an emission mask that hue shift or
  colour masks also change.
- **Not represented:** fog (not specified); the light from the probes when the world has no
  directional light (the shader's own behaviour); the additive matcap's absence from the
  add pass.

**Coverage:** every section is mapped, apart from the reported AudioLink details.

### Not visual
- The atlased variant: a `VRCHAT_ATLASING_ENABLED` keyword driven by VRChat's
  `VRCAvatarAtlasProperties` component. It's unfinished in the SDK and ignored.
- Blend modes and alpha cutoff: present in the code, but disabled, with no inspector control.

---

## Cross-shader notes

- **Section toggles decide everything.** Toon Standard's sections (all except Main, Lighting,
  Emission and Outline have one), Standard Lite's emission,
  highlights and reflections toggles: a parser must read the switch first, the values second.
  A value behind a disabled switch isn't covered, it's irrelevant.
- **Lighting families from these shaders:** *flat* (Toon Lit), *ramp toon* (Toon Standard)
  and *physically based* (Standard Lite). Each needs an ambient model (flat average or
  directional), and whether it receives shadows.
- **Surface response** covers metallic, roughness, reflectance, highlight style, specular
  anti-aliasing, and on/off for direct highlights and environment reflections.
- **Before or after lighting** is a parameter of matcaps and other overlays (the layer's
  placement).
- **Colour adjustment and recolour layers** are targeted, masked and baked when static.
- **Vertex colour:** one shader always multiplies by it, one never, one optionally. A merged
  material can only do one. Options: bake white vertex colours into meshes whose material
  ignored them, or bake vertex colour into the texture where islands don't share texels.
- **Per-material constants** (rim colour, ramp, highlight style and so on) are uniform per
  source material. When atlased materials disagree, the writer needs a per-region map or has
  to choose. That's an output concern, but the unified material must keep the per-source
  values so the writer can decide.
- **Shared tiling:** Standard Lite's main maps use the albedo's tiling. Unity Standard does
  the same; see [its inventory](UNIFIED_MATERIAL_UNITY_STANDARD.md).
