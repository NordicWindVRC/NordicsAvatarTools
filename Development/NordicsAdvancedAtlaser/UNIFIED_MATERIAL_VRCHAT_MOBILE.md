# Unified material — VRChat Mobile shaders

**Status:** first draft, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md); the
catalogue's names take precedence. Part of the per-shader inventory that
[`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds on. It lists what each shader lets the user
change, what that does to the look, and how the unified material should represent it. Names in
the *Unified* lines are proposals in neutral wording; the cross-shader refinement pass settles
them.

**Source:** VRChat SDK 3.10.6 (`com.vrchat.base`), folder
`Runtime/VRCSDK/Sample Assets/Shaders/Mobile/`: the shader files, `VRChat.cginc`, the
`ToonStandard/CG/` includes, and the inspectors in `GUI/`. Only what the inspector shows is
inventoried; properties the user can't reach are listed under *Not visual* when they exist.

**Each feature lists:**
- **Controls:** what the user sets, grouped as in the inspector.
- **Live or baked:** *live* means the effect depends on view, light, time or audio and must
  stay a unified feature with parameters; *baked* means a static result that can go into
  textures, unless something animates it.
- **Unified:** the proposed representation.
- **Parser today:** what `AtlasVRChatMobileParsers.cs` does now, and what's wrong or missing.

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
- **Unified:** base colour layer; vertex colour multiply on.
- **Parser today:** correct.

### Lighting
Uniform light per object, no direction; receives shadows. When the world has no directional
light, the direct light becomes 20% of the ambient colour.
- **Controls:** none.
- **Live or baked:** live.
- **Unified:** a *flat* lighting family: no shading gradient across the surface, light =
  direct colour × shadow + ambient. This is the same look as a toon ramp that's flat
  everywhere, apart from receiving shadows, so it might merge into the ramp family as a
  preset.
- **Parser today:** sets `MobileVertexToon`. Fine as a value, but it's one of the
  per-shader lighting modes the guidelines want replaced by families.

### Not visual
Nothing; the shader has one property.

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
- **Parser today:** reads texture and colour, but **doesn't record the vertex colour multiply.**

### Metallic and smoothness
How metallic and how glossy the surface is.
- **Controls:** one texture (metallic in red, smoothness in alpha) with a Metallic slider, and
  a Smoothness slider. The sliders **multiply** the texture; they don't replace it.
- **Live or baked:** baked (scalar maps).
- **Unified:** surface response: metallic and roughness, each a constant times a channel.
- **Parser today:**
  - **Smoothness is wrong when a texture is set:** the shared Standard code uses
    `_GlossMapScale`, which Standard Lite doesn't have, so the constant becomes 1 instead of
    the Smoothness slider.
  - The Metallic slider is stored gamma-encoded (`[Gamma]`); the parser reads it raw.
  - Multiplying is right: the unified scalar evaluates as constant × map channel.
  - Uses each map's own tiling instead of the albedo's (see *Shared tiling*).

### Normal map
- **Controls:** texture and scale.
- **Live or baked:** baked; part of normal compositing.
- **Unified:** primary normal layer.
- **Parser today:** reads it, with the wrong tiling.

### Occlusion
Darkens ambient light in creases. Read from green.
- **Controls:** texture and strength (0 = none, 1 = full).
- **Live or baked:** baked as a map. Its effect is live: it scales ambient light only, not
  direct light.
- **Unified:** occlusion: channel, strength around one.
- **Parser today:** correct, apart from the tiling.

### Emission
Glow on top of lighting.
- **Controls:** an Emission checkbox; when on, texture and HDR colour.
- **Live or baked:** baked (texture × colour), with the HDR intensity kept as a scale.
- **Unified:** emission layer.
- **Parser today:** correct; enabled by the shader keyword.

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
- **Parser today:** correct; mask channel alpha. Its UV set and tiling are read.

### Specular highlights
The shader labels the toggle "Specular Lightprobe Hack". On, it adds GGX highlights from the
directional light and, when there's no directional light or reflection probe, a highlight
from the light probes' dominant direction. **Default off.**
- **Controls:** toggle.
- **Live or baked:** live.
- **Unified:** surface response: direct highlights on or off. The probe-direction highlight is
  the shader's way of producing highlights without a light, so it belongs to the lighting
  family rather than being a separate feature.
- **Parser today:** reads the toggle.

### Reflections
Reflection probe reflections. **Default off.**
- **Controls:** toggle.
- **Live or baked:** live.
- **Unified:** surface response: environment reflections on or off.
- **Parser today:** reads the toggle.

### Specular anti-aliasing
Reduces sparkling highlights on small or curved detail by roughening where the surface normal
changes quickly on screen.
- **Controls:** toggle, Variance and Threshold.
- **Live or baked:** live, but minor.
- **Unified:** a surface response parameter. Toon Standard always applies its own version, so
  this fits as a shared parameter.
- **Parser today:** reads all three.

### Not visual for avatars
- **Lightmap Type** (Default or MonoSH) and **bicubic sampling**: they only affect lightmaps,
  which skinned avatars don't use. The parser maps both; they can be dropped.
- **GPU instancing**, **double-sided global illumination**: performance and baking options.

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
  read. **Parser today:** correct.
- **Culling:** Off, Front or Back. On back faces the normal is flipped, so two-sided surfaces
  light correctly. **Unified:** render: cull mode and double-sided lighting. **Parser today:**
  reads the cull mode and sets double-sided when culling is off.
- **Normal map:** texture and scale, behind a "Use Normal Map" toggle that also gates the
  detail normal. Baked. **Unified:** primary normal layer. **Parser today:** reads the map
  but **ignores the toggle**, so a disabled normal map is applied anyway.

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
- **Parser today:** reads all of these. It doesn't recognise the presets, and it
  doesn't record the flat ambient model.

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
- **Parser today:**
  - **Ignores the section toggle:** a disabled section still gets metallic and gloss.
  - **Reflectance:** the default is 0.04 where the shader's is 0.5, and the value goes in
    unconverted. Either the unified field is F0, and the parser converts it, or it's this
    0–1 control, which then needs a documented convention. Its gloss-map dependency is
    lost.

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
- **Parser today:** reads texture, strength, mask with channel and blend. It ignores the
  section toggle and doesn't record the before-lighting placement. It reads the matcap's
  tiling, which the shader ignores.

### Emission
- **Controls:** no section toggle, so emission is always active. Emission Map, Emission Color,
  Strength (0–2); UV map 0 or 1.
- **Behaviour:** map × colour × strength. Hue shift can apply (see *Hue Shift*).
- **Live or baked:** baked.
- **Unified:** emission layer with UV set.
- **Parser today:** correct; enabled when a map or a non-black colour is set.

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
- **Parser today:** correct modes and mask. It ignores the section toggle, and it doesn't
  model the mask scaling the detail normal's strength.

### Occlusion
- **Controls:** section toggle; map with channel (default green); strength.
- **Behaviour:** darkens the ramp result and the ambient light; specular isn't affected.
- **Live or baked:** baked map, live effect.
- **Unified:** occlusion.
- **Parser today:** correct, but ignores the section toggle.

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
- **Parser today:** reads every value, but **adds the rim whenever the property exists, so
  every Toon Standard material gets a rim,** even with the section off. The Range value goes
  in unconverted, though the shader uses 1 − Range; a convention is needed.

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
- **Parser today:** reads all three amounts and the mask into those fixed fields. It ignores
  the section toggle and doesn't record the method.

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
- **Parser today:** reads all four regions. It ignores the section toggle: regions are
  enabled whenever a mask texture is assigned.

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
- **Parser today:** reports it as missing.

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
  mask source, view-angle mask, fallback.
- **Parser today:**
  - Reads all of it, but **enables it whenever the property exists, so every SDK 3.10.6
    material counts as audio-reactive.**
  - **The mask channel is off by one:** the shader's 0 means RGB, but the parser reads it as
    red, and so on.
- **Atlasing conflict:** the *UV Based* effect and band, and scroll and bar along UVs, read
  the effect UV channel, and its **integer UV tile carries meaning**. Remapping or clamping
  that channel breaks the effect. See the open questions.

### Not visual
- The atlased variant: a `VRCHAT_ATLASING_ENABLED` keyword driven by VRChat's
  `VRCAvatarAtlasProperties` component. It's unfinished in the SDK, so we can ignore it for
  now.
- Blend modes and alpha cutoff: present in the code, but disabled, with no inspector control.

---

## Cross-shader notes

Points for the refinement pass:

- **Section toggles decide everything.** Toon Standard's sections (all except Main, Lighting,
  Emission and Outline have one), Standard Lite's emission,
  highlights and reflections toggles: a parser must read the switch first, the values second.
  A value behind a disabled switch isn't covered, it's irrelevant.
- **Lighting families from these shaders:** *flat* (Toon Lit), *ramp toon* (Toon Standard)
  and *physically based* (Standard Lite). Each needs an ambient model (flat average or
  directional), and whether it receives shadows.
- **Surface response** covers metallic, roughness, reflectance, highlight style, specular
  anti-aliasing, and on/off for direct highlights and environment reflections.
- **Before or after lighting** is a parameter matcaps need, and probably other overlays too.
- **Colour adjustment and recolour layers** are targeted, masked and baked when static.
- **Vertex colour:** one shader always multiplies by it, one never, one optionally. A merged
  material can only do one. Options: bake white vertex colours into meshes whose material
  ignored them, or bake vertex colour into the texture where islands don't share texels.
- **Per-material constants** (rim colour, ramp, highlight style and so on) are uniform per
  source material. When atlased materials disagree, the writer needs a per-region map or has
  to choose. That's an output concern, but the unified material must keep the per-source
  values so the writer can decide.
- **Shared tiling:** Standard Lite's main maps use the albedo's tiling. Unity Standard does
  the same; see [its draft](UNIFIED_MATERIAL_UNITY_STANDARD.md).

## Parser fixes found

In order of visual impact:

1. **Toon Standard ignores the section toggles** (`USE_SPECULAR`, `USE_MATCAP`, `USE_RIMLIGHT`,
   `USE_DETAIL_MAPS`, `USE_NORMAL_MAPS`, `USE_OCCLUSION_MAP`, `USE_HUE_SHIFT`, `USE_COLOR_MASK`,
   `USE_AUDIOLINK`). Every material gets a rim and AudioLink; disabled normal maps, specular,
   detail and colour masks are applied.
2. **Standard Lite tiling:** main maps should use the albedo's tiling.
3. **Standard Lite smoothness** with a texture uses a property it doesn't have; it should
   use Smoothness × map alpha.
4. **Toon Standard AudioLink mask channel** is off by one.
5. **Standard Lite vertex colour** multiply isn't recorded.
6. **Toon Standard reflectance:** wrong default, and no defined convention.
7. **Standard Lite metallic** is gamma-encoded.
8. Smaller items: ramp presets, flat ambient, matcap placement and tiling, rim range
   convention, hue method, the detail normal's mask scaling, and dropping lightmap and
   bicubic.

These belong in the refactoring. Nothing has been changed yet.

## Open questions

1. **AudioLink UV-based effects versus atlasing.** The effect UV channel's integer tile
   carries meaning. If that channel is UV0, any remapping breaks the effect. Proposal: never
   remap or clamp a UV channel that a live effect reads by tile. If that channel is the one
   being atlased, report the effect as unsupported for that material.
2. **Vertex colour when merging** materials that differ: bake white into meshes, bake into
   texture, or keep it per material and report it?
3. **Format:** is this the level of detail you want for the remaining shaders? Poiyomi will be
   several times longer at this depth.
