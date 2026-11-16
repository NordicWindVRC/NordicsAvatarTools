# Unified material — Poiyomi Toon

Part of the per-shader inventories that [`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds
on, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md), whose names take
precedence. It also describes the parser; same format as
[the VRChat Mobile inventory](UNIFIED_MATERIAL_VRCHAT_MOBILE.md), but more condensed: Poiyomi has
about 60 top-level sections.

**Source:** Poiyomi Toon 10.0.29
(`Packages/com.poiyomi.toon/_PoiyomiShaders/Shaders/10.0/Toon/Poiyomi Toon.shader`). The inspector
is Thry Editor, built from the shader's property block: section markers, toggles and hidden
flags. The section tree has **3,801 properties, about 2,640 visible.**
Older versions (7.3 to 9.3) are read on this parser with version tables; see [*Older versions*](#older-versions). Their sources ship inside the Toon
packages, under `_PoiyomiShaders/Shaders/<version>`.
**Version 11 and Poiyomi Pro** (11.0.50, from the `com.poiyomi.pro` package) are
covered in [*Version 11 and Pro*](#version-11-and-pro), as differences from 10.0.29. The same
package ships Pro 9.3 and 10.0; Pro 9 is under [*Older versions*](#older-versions).

**Variants:** Poiyomi Toon, Two Pass, Early Outline, Grab Pass, Lil Fur and Lil Fur Two Pass
share this feature set. Grab Pass adds screen-grab effects; Lil Fur adds a fur pass, covered in
[*Lil Fur*](#lil-fur-variants-lil-fur-and-lil-fur-two-pass). **Poiyomi Toon World** is read as Poiyomi Toon, with one *ignored* line for its world-only features. Poiyomi Pro's variants are listed under
[*Version 11 and Pro*](#version-11-and-pro).

**Parser:** the Poiyomi parsers (`AtlasPoiyomi*.cs`) are described under
[*Parser*](#parser), part by part; the sections below are the inventory and
where each feature lives in the unified material.

**How Poiyomi is organised, and what it means for the parser:**
- **Section toggles** gate everything, as in Toon Standard.
- **Per-texture UV choice:** most textures have a UV selector: UV0–UV3, panosphere, world or
  local position, polar, distorted, matcap or screen space. Plus tiling, offset, panning and
  rotation.
- **Global masks:** 4 textures × RGBA = 16 mask channels. Features can read from them, and some
  can write to them (decals, dissolve, light maps).
- **Theme colours:** 4 shared colours that features can reference instead of their own.
- **AudioLink and time** modulate almost every feature through per-feature subsections.
- **Locking:** Thry locks materials before upload; properties tagged as animated stay live.

---

## Color & Normals

### Main
- **Controls:** Color & Alpha, Texture, Normal Map, Alpha Map, Alpha Cutoff, each with UV
  selection, tiling and panning.
- **Live or baked:** baked; panning is live.
- **Unified:** base colour layer, primary normal, alpha from any source.

### Color Adjust
- **Controls:**
  - Saturation, chromatize, brightness, gamma, clamp; a mask.
  - **Tint:** colour or texture.
  - **Hue Shift:** colour space, *select* (keep only one hue range) or *shift*, amount, speed,
    AudioLink motion.
  - **Color Grading:** a gradient map with mask and strength.
  - Global masks per adjustment.
  - Poiyomi has its own "Bake Color Adjust" button.
- **Live or baked:** baked when static, which is the common case; hue speed and AudioLink are
  live.
- **Unified:** colour adjustment, target base colour: saturation, brightness, gamma, tint, hue
  (method, colour space, select or shift), gradient map, masks, with time and audio modulation.

### Normal Map 2, Detail Normals & Texture, Bent Normal Map
- **Controls:**
  - Second normal with mask.
  - Detail texture (tint, intensity, brightness) and detail normal, sharing a detail mask.
  - Bent normal for specular occlusion and indirect diffuse.
- **Live or baked:** baked; part of normal compositing. The bent normal is a lighting input.
- **Unified:** extra normal layers with masks; the detail layer; a bent-normal input on the
  surface response.

### Normal Correct
- **Controls:** amount; origin point.
- **Behaviour:** bends normals toward a sphere around the origin, for softer face shading.
- **Live or baked:** can be baked into the normal map for a given mesh, since the origin is in
  object space.
- **Unified:** a normal modifier, "spherize toward point"; or the parser bakes it into the
  normal composite.

### Alpha Options
- **Controls:**
  - Force opaque, alpha mod.
  - Alpha-to-coverage, sharpened, with mip scaling.
  - Dithering: gradient and bias.
  - Distance fade: object or pixel position, min/max distance and alpha.
  - Fresnel alpha: view-angle fade.
  - Angular alpha: fade by camera or model angle.
  - AudioLink alpha.
  - Premultiply, forward-add boost.
- **Live or baked:** distance, fresnel, angular and audio fades are live.
- **Unified:** alpha as a scalar input, plus **alpha modifiers** (distance, view angle,
  camera or model angle, audio) and cutout edge handling, as in the Mochie drafts.

### Skin
- **Controls:** a skin tone preset; skin detail map; pigment (colour lookup texture, melanin,
  redness, hemoglobin); freckles and tan (UV, tiling, strength, redness); brightness, tint,
  blend.
- **Live or baked:** baked; it's a procedural base colour.
- **Unified:** the parser bakes it into the base colour, so no unified feature is needed unless
  it's animated.

### Back Face
- **Controls:** colour, texture, mask, emission, hue shift, detail intensity, replace alpha,
  emission limit. Requires culling off.
- **Live or baked:** live: the same texels look different from behind, so it can't go into
  one texture.
- **Unified:** a **back-face appearance** override: colour, texture, emission and hue for back
  faces.

### RGBA Color Masking
- **Controls:** a mask from a texture or vertex colours; per channel R, G, B, A:
  - colour and texture with tiling, blend mode;
  - normal map and blend;
  - emission strength;
  - metallic and smoothness maps with their own sampling.
- **Live or baked:** baked when static.
- **Unified:** the **recolour layer** from Toon Standard and Uber, generalised: a region
  defined by a mask channel, with colour, texture, normal, emission and surface overrides.

### Decals (4)
- **Controls per decal:**
  - **Placement:** position, rotation (and speed), scale, side offset, aspect ratio, mirrored-UV
    and symmetry modes, tiled, depth.
  - Colour, texture, emission; colour blend (replace, darken, multiply, lighten, screen,
    subtract, add, overlay, mixed); alpha blend.
  - Colour adjust and hue.
  - Video texture options; chromatic aberration.
  - Masks: face mask, read from and write to global masks, show on outlines.
  - AudioLink on scale, side, rotation, alpha, emission, colour.
- **Live or baked:** **static decals can be baked into the atlas textures.** That's one of the
  biggest wins for atlasing. Rotating, video, audio and parallax-depth decals stay live.
- **Unified:** a **decal layer**: placement in UV space, texture, colour, blend, emission,
  masks, and optional animation and modulation. The same layer type covers Uber's flipbook
  layers.

---

## Shading

### Lighting type
Poiyomi's *Lighting Type* chooses the whole shading model. Unified families:

| Lighting type | What it looks like | Unified family |
|---|---|---|
| Flat | Uniform light, ignores normals | *flat* |
| TextureRamp | Ramp texture over N·L | *ramp toon*, ramp from texture |
| Multilayer Math | Up to three shadow layers with colour or texture, border, blur | *ramp toon*, ramp from layers, shade colours possibly from textures |
| ShadeMap | UTS2-style 1st and 2nd shade colours or maps, steps, feathers | *ramp toon*, shade colours from textures |
| Wrapped | Wrapped diffuse with a gradient and light-side colour | *ramp toon*, generated ramp |
| Realistic | Lambert | *physically based* |
| Skin | Pre-integrated skin diffusion from a colour lookup | *physically based*, diffuse model *skin* |
| Cloth | Cloth diffuse and sheen with its own metallic, reflectance, smoothness | *physically based*, diffuse model *cloth* |
| SDF | Face shadows from a signed distance field texture and face directions | *ramp toon*, shading term from an SDF texture instead of N·L |

So the unified lighting needs:
- a **shading term source**: N·L, received shadows only, or an SDF texture;
- a **ramp** that maps the term to brightness and shade colour;
- **shade colours** that can come from a constant, the base colour, or textures;
- a **diffuse model** for the physically based family: Lambert, Disney (Uber), skin, cloth.

- **Other controls:** shadow tint, ramp offset and count; border colour and range; shadow maps
  (strength or flat); a border AO map; base colour blend; environment strength on the shadow
  colour; shadow strength; ignore indirect shadow colour. Add pass: realistic, toon, or the
  same as the base pass.
- **Live or baked:** live; ramps and maps baked.

### Light Data
- **Controls:**
  - AO maps, painted shadow maps, shadow masks.
  - Light colour, light map and light direction modes, each with Poiyomi, Standard, UTS2 and
    lilToon variants.
  - Forced direction; view-direction offsets; forced light colour; unlit intensity.
  - Limit brightness and maximum; minimum brightness.
  - Indirect uses normals; receive cast shadows with softness; grayscale lighting; vertex
    lights.
  - Add pass: enable, ignore directional, limit, cast shadows, grayscale, passthrough,
    attenuation curve.
  - Light volumes: specular, normal bias.
- **Live or baked:** live.
- **Unified:** lighting family parameters: light direction override (local, world, view),
  light colour override, brightness floor and ceiling, received shadows and softness,
  monochrome light, per-source contributions. This mirrors Uber's Lighting section. The
  colour, map and direction *modes* are other shaders' lighting conventions; the parser
  resolves them into these parameters.

### Anisotropics
- **Controls:** colour and offset map; replace or add blending; hide in shadow; fade by light
  direction; mix light and base colour; top and bottom layers (power, strength, offset, tint,
  direction, edge, blur).
- **Unified:** the *stretched* highlight shape from Uber, with two layers and a stylised edge.

### Matcaps (4)
- **Controls per matcap:**
  - Projection: UTS style, top pinch, double-sided, gradient, light direction.
  - Colour, texture, intensity, emission; base colour and light colour mix; normal strength;
    a reflection-probe fallback.
  - Masking: mask, hide in shadow, circle mask, show on outlines.
  - Blending: replace, multiply, add, mixed, screen and unlit add, **all as weights.**
  - Custom normal map; hue; blur; alpha override; TPS mask; AudioLink.
- **Unified:** matcap layer: projection, blend weights, placement before or after lighting,
  blur, per-layer normal, masks, modulation.

### CubeMap
- **Controls:** projection (skybox, reflection, world normal, local normal); colour, cubemap,
  blending, brightness, emission, smoothness; mask; hide in shadow; colour adjust.
- **Unified:** the cubemap colour layer from Uber, with a projection.

### Rim Lighting (2)
- **Controls per rim:**
  - Style: Poiyomi, UTS2 or lilToon, each with its own shape controls.
  - Colour and texture; blend; base and light colour mix; brightness; emission.
  - Width, blur, power; invert; normal strength.
  - Light-direction masking: hide or shrink in shadow, direct and indirect light widths and
    colours.
  - Hue; apply to alpha; AudioLink.
- **Unified:** rim layer, with a **light-direction response**: brighter on the lit side, an
  optional second colour on the unlit side. The three styles are parser work: each is
  resolved into width, softness and power.

### Depth Rim Lighting
- **Controls:** normal to use; sample count; width, sharpness, depth threshold; distances;
  light-direction options; mask; colour options; blending weights.
- **Behaviour:** a rim from depth differences in screen space, so it outlines silhouettes
  against what's behind them. Needs a depth texture; Poiyomi ships a prefab for avatars.
- **Unified:** rim layer with **edge source** *depth* instead of *view angle*.

### Subsurface Scattering
- **Controls:** colour; colour and thickness map; thickness; light strength, spread,
  distortion; light and base colour mix; shadow strength.
- **Unified:** subsurface layer, as in the Mochie drafts.

### Reflections & Specular, and Clear Coat
- **Controls:**
  - Metallic, smoothness, reflection and specular tint, packed maps; reflection and specular
    visibility; fresnel; exposure occlusion; fallback cubemap.
  - Anisotropy map and direction.
  - A second specular lobe.
  - GSAA; ignore cast shadows.
  - **Clear Coat** repeats this as a second reflective layer over the base, with its own
    smoothness and tints.
- **Unified:** surface response, with an optional second highlight lobe and a **clear-coat
  layer** (strength, smoothness, tints; normal-map influence). Uber's "clear coat" is the
  simple version.

### Environmental Rim, Stylized Reflections, Backlight
- **Environmental Rim:** the reflection-coloured rim from Uber.
- **Stylized Reflections:** toon highlights, in UnityChan style (map, tint, size, feather, two
  layers) or lilToon style (toon specular border and blur, reflections). These map to
  surface response with highlight style *stepped* and a size and feather.
- **Backlight:** light from behind glowing through thin edges: colour, texture, border, blur,
  directivity, view strength. A rim variant with a back-light response.

### LTCGI
- **Controls:** diffuse and specular tint and multipliers; use the surface's own settings or
  its own metallic and smoothness.
- **Unified:** external lighting.

---

## Outlines

- **Controls:**
  - Mode: basic, rim light, directional, drop shadow; local or world space.
  - Size with mask; texture; colour; rim-light blend; directional offset; drop direction;
    emission; main texture blend.
  - **Highlight:** an inner highlight line.
  - Colour adjust.
  - Distance alpha; fixed size over distance.
  - Lighting (shadow strength).
  - Vertex colour normals and mask.
  - AudioLink.
  - Rendering options: cull, depth, Z offset, clip at zero width.
- **Unified:** the outline feature, extended with:
  - shape: normal expansion, drop shadow, or a directional offset;
  - a highlight line;
  - fade over distance and fixed screen size;
  - vertex-colour normals.

---

## Special FX

### UV Tile Discard
- **Controls:** UV channel; mode; a 4×4 grid of toggles for which UV tiles to hide; face
  discard.
- **Behaviour:** hides geometry by the integer UV tile it sits in. It's commonly animated to
  toggle clothing parts without extra meshes.
- **Live or baked:** live.
- **Unified:** a visibility feature: hide by UV tile. **Atlasing conflict,** the same as
  Toon Standard's AudioLink: the integer UV tile carries meaning, so remapping or clamping
  that UV channel breaks it. See the open questions.

### Dissolve
- **Controls:**
  - Type: basic (gradient texture), point to point, spherical, centre out.
  - Alpha (the amount), softness.
  - Dissolved appearance: alpha only, or a colour, texture and emission.
  - Gradient noise; edge line (up to three colours, borders, blur, width, emission).
  - Wave mode; continuous dissolve; vertex height (geometry rising as it dissolves).
  - Masks, writing to global masks; AudioLink; UV-tile dissolve.
- **Live or baked:** live when the amount is animated, which is the point of dissolves. Fully
  dissolved or untouched states can be baked.
- **Unified:** the **transition layer** from Uber's Base Color Dissolve, generalised: a
  threshold source (texture, distance from a point, along a line, centre out), an amount, an
  edge, and the revealed appearance (transparent or another colour and texture).

### Emissions (4)
- **Controls per emission:**
  - Mask, colour, map, strength; base colour as the map; override base colour; fluorescence.
  - Colour adjust and hue.
  - **Centre out:** emission flowing from the centre.
  - **Light based:** scales with world or mesh brightness, like Uber's light reactivity.
  - **Blinking:** smooth or flashing, with min, max, speed and duty.
  - **Scrolling:** a band sweeping along a direction, with optional curve and vertex-colour
    position.
  - AudioLink.
- **Unified:** emission layers, plus modulators (catalogue 1.9): time (blink waveform,
  scroll sweep), light level, audio. Scrolling and centre-out are spatial sweeps: a
  modulator whose phase depends on position.

### Flipbooks (2)
- **Controls:** texture array, colour, mask, tiling, blending, alpha, emission; FPS and frame
  offset; placement as for decals; manual frame; start and end frame; crossfade; hue;
  AudioLink on scale, alpha, emission, frame.
- **Unified:** the decal layer with frame animation, as with Uber's flipbooks.

### Glitter / Sparkle
- **Controls:**
  - Mode: angle-based, linear emission, light reflections.
  - Shape: circle, square or texture; colour map, colour, base colour, random colours.
  - Density, size, randomness, panning; brightness range, speed, angle range; rotations;
    sprite sheet; masks; hue; AudioLink.
- **Unified:** a **procedural pattern layer**, pattern *glitter*, with view- and
  light-dependent sparkle. See *Procedural patterns* below.

### Procedural patterns: Pathing, Voronoi, Truchet, Internal Parallax, Video Effects
- **Pathing:** coloured pulses travelling along gradients in a texture or along UVs.
- **Voronoi:** animated cell patterns, 2D or 3D.
- **Truchet:** tiled pattern from a texture.
- **Internal Parallax:** layered fake depth inside the surface, like gems or eyes.
- **Video Effects:** makes a texture look like a physical screen: LCD, TN, CRT, OLED,
  Gameboy, projector.
- **Live or baked:** mostly live: animated, view-dependent, or depending on world video.
- **Unified:** one **procedural pattern layer** with a *pattern kind* and its parameters, a
  blend, an emission amount and masks. Rather than five features, these share the layer
  mechanics (blend, mask, emission, modulation) and differ only in the pattern. Writers
  without the pattern report it as unsupported.

### Mirror/Camera Visibility
- **Controls:** generic or VRChat mode; visibility in mirrors, cameras and normal view; a
  mirror texture and colour.
- **Unified:** render: mirror and camera visibility, alternate mirror appearance. The same as
  Uber's Mirror Behavior, with cameras added.

### Proximity Color, Depth FX, Depth Bulge
- **Proximity Color:** colour changes as the camera gets close. A modulator with source
  *camera distance*.
- **Depth FX:** glow where the surface touches other geometry (intersection highlights). Needs
  a depth texture. A rim layer with edge source *intersection*.
- **Depth Bulge:** pushes the surface toward the camera where it touches other objects. A
  geometry effect.

### Stats Overlay
- **Controls:** a font; text effects; FPS, position, instance time, clock and numeric
  displays, each with colour and transform.
- **Unified:** an informational overlay; out of the look. Report as unsupported.

---

## Audio Link

- **Global:** an animation toggle, delay, CC strip, smoothing, band overrides.
- **AL Spectrum:** an audio-spectrum decal: placement, spectrum shape, clipping, colours.
- **AL Volume Color:** colour along a UV direction driven by volume.
- **Per-feature AudioLink** appears inside nearly every section: decals, matcaps, rims,
  emissions, dissolve, flipbooks, outlines, glitter, vertex options and more.
- **Unified:** the general **audio modulator** (catalogue 1.9), plus two layers: the
  spectrum as a procedural pattern, and volume colour as a modulated colour layer. Global
  smoothing and delay are modulator settings.

---

## Vertex Options

- **Basics & Fun:** a mask; local and world translation, scale, rotation and spin; height-map
  displacement; vertex rounding (blocky, PS1 look); barrel and sphere distortion; tornado;
  wind and flag; AudioLink.
- **LookAt:** parts that turn to face the camera, per mask channel.
- **Glitching:** random vertex jumps.
- **Vertex Colors:** use vertex colour and its alpha; linear or sRGB.
- **View Clip Prevention:** pushes vertices toward the camera, so close-up views don't clip
  into the body.
- **Live or baked:** static translation, scale, rotation, displacement and rounding could be
  baked into the mesh; animated ones stay live.
- **Unified:** the deformation feature (catalogue 5.3). Vertex colour use
  belongs to the base colour and alpha.

---

## Global Modifiers & Data

- **Time Source:** Unity time or VRChat network time; a modulator setting.
- **BlackLight Masking:** masks that react to UV-light objects in the world; a modulator with
  source *blacklight*.
- **Global Themes:** four shared colours, each with hue, saturation and brightness
  adjustments. **Unified:** a material **palette**: features reference a palette entry
  instead of copying the colour, so recolouring stays consistent.
- **Global Mask:**
  - Four textures, 16 channels.
  - Vertex colour channels.
  - Modifiers: back-face, mirror, camera and distance masking.
  - Sliders and toggles per channel.

  **Unified:** a **mask bus** with numbered channels. Features read from and write to them;
  modifiers produce channels from view conditions. `AtlasGlobalMask*` already models part
  of this.

## UVs

- **Stochastic sampling:** Deliot-Heitz or hex tiling, to hide repeats.
- **UV Settings per channel:** tiling, offset, angle, pan, rotate, for UV0–UV3.
- **Distortion UV:** texture, Gerstner waves, simplex, voronoi, curl or FBM noise, with
  AudioLink.
- **Local/World UV, Panosphere UV, Polar UV.**
- **Parallax Heightmapping:** advanced multi-step parallax with curvature, cone stepping and
  more.
- **Unified:** texture mapping spaces and transforms. Per-channel UV settings fold into every
  texture that uses that channel. Distortion is a mapping modulator, as in Uber. Parallax is
  the parallax feature, with method parameters.

## Post Processing

- **Controls:** final colour hue, saturation, brightness, posterisation; PP animations.
- **Unified:** colour adjustment with target *final colour*, as in the Mochie drafts, plus
  posterisation.

## Rendering

- **Controls:**
  - Rendering preset: opaque, cutout, trans-clipping, fade, transparent, additive, soft
    additive, multiplicative, 2× multiplicative.
  - Cull, depth test and write, depth clip, depth offset, colour mask.
  - Reduce clip distance; ignore fog; flip back-face normals; early Z.
  - Blending for the base, add and outline passes, with advanced alpha blending.
  - Stencils for the base and outline.
  - World AO blocker.
- **Unified:** render: surface mode, which gains *additive* and *multiplicative*; culling;
  depth; stencil; fog; two-sided lighting. Raw blend factors are kept only when they don't
  match a named surface mode.

## Lil Fur (variants Lil Fur and Lil Fur Two Pass)

The Lil Fur variants have every feature above, plus a **fur pass**: a geometry shader that grows
short strands from the surface, in the style of lilToon's fur. The fur adds 68 properties: a
*LilFur* category and a *LilFur Rendering* section. Geometry shaders don't run on Quest, so fur
is PC only.

The strands are coloured by the same surface as the body: base colour, lighting, and the
features above all apply. The fur controls only change shape, transparency and shading along
the strand.

### Fur shape
- **Controls:**
  - **Fur Mask (RGBA):** a texture whose channels can serve as length and alpha masks.
  - **Normal Map (fur vector map):** strand direction per texel, as a tangent-space map.
  - **Vector|Length:** default direction, and the length in metres.
  - **Length Mask:** which mask channel scales length, or off.
  - **Use Vertex Colors:** take strand direction from vertex colours instead.
  - **Gravity:** pulls strands downward; **Randomize:** varies direction per strand.
- **Live or baked:** gravity is live because it depends on world orientation. The masks and
  direction map are baked textures.
- **Unified:** a **fur feature**:
  - length (world units), a length mask, direction (map, vertex colour, or a constant),
    gravity, randomness;
  - the direction map is a direction texture and must be composited like a normal map, not
    blended like colour.

### Fur appearance
- **Controls:**
  - **Noise:** strand pattern texture; it shapes the strands through transparency.
  - **Alpha Mask:** which mask channel thins the fur, or off.
  - **AO:** darkening toward the roots, shaped by the noise.
- **Unified:** fur strand texture, alpha mask, root occlusion strength.

### Fur layers
- **Controls:** Layer Count (1–3), Root Width, Width.
- **Unified:** fur density and strand width. These are rendering quality, but they change the
  look.

### Fur wind
- **Controls:** a primary and a detail wind, each with direction, amplitude, speed and
  frequency.
- **Live or baked:** live; animated.
- **Unified:** fur sway, with the same shape as the geometry wind in the
  [Uber](UNIFIED_MATERIAL_MOCHIE_UBER.md) and Vertex Options drafts: layers of direction,
  amplitude, speed and frequency. The deformation can share one definition and apply to
  strands or vertices.

### Fur rim light
- **Controls:** colour, fresnel power, Anti Light (suppress on the lit side).
- **Behaviour:** a rim added to the strands only, stronger toward the tips, times the direct
  light colour.
- **Unified:** a rim layer scoped to the fur, with a light-direction response.

### LilFur Rendering
- **Controls:** cull (default off), depth, offset, colour mask, alpha-to-coverage; blending for
  the fur base and add passes; a fur stencil.
- **Unified:** render settings for the fur pass, alongside the outline pass's own settings.
  This argues for **per-pass render settings** in the unified material: base, outline, fur.

### Atlasing notes
- **Fur length is a per-material constant in metres.** When atlased sources have different
  lengths, the parser can bake the ratio into the length mask and use the longest length as
  the constant. That's the same idea as normalising normal-map strengths.
- **The fur mask, noise and direction map** are textures and atlas like any other, with the
  direction map handled like a normal map.
- **Vertex-colour direction** is mesh data. It survives atlasing, but merging with sources that
  use the map instead needs the map to be baked from vertex colours, or the reverse.
- **Fur on or off** is a variant choice: fur and non-fur sources can't share one output
  material; settings that can't be merged are deferred to the atlasing pass (catalogue 10).
- **Parser:** the Lil Fur shader names go to the Poiyomi parser, which reads the shared
  features and fills the unified fur definition (`AtlasUnifiedMaterial.Fur`) from the
  controls above.

## Version 11 and Pro

**Source:** Poiyomi 11.0.50, as downloaded by the `com.poiyomi.pro` package.
It contains Toon and Pro for both 10.0.29 (as *Old Versions*) and 11.0.50.

**Detection:** Toon 11 keeps 10's shader names (`.poiyomi/Poiyomi Toon` and so on), so only the
shader's version label ("Poiyomi 11.0.50") tells them apart. Pro shaders are named
`.poiyomi/Poiyomi Pro…`; every other Poiyomi shader is Toon. A locked shader is named
`Hidden/Locked/<name>/<material id>`, and its name is read without the prefix and the id. A
version the parsers don't know (a newer one, or none found) is read by the newest parser and
reported.

**Variants** are told by a property only the variant declares (`_ModeTwoPass`,
`_EnableGrabpass`, `_FurLayerNum`, `_FurLayerCount`, `_TessellationType`, `_TriplanarMode`,
`_EnableOutlineLayer2` and the geometry variants' own). With the shader missing, such a property
may be left over from a variant the material used before: the name the material was locked with
(the optimizer's `OriginalShader` tag) decides, and without one the variant is assumed and
reported.

**Renamed properties:** a property marked *renamed when locked* is declared by the locked
shader as `<name>_<suffix>` (the material's rename tag or its name, in the optimizer's
property-name form); the reader reads it under that name.

**Size:** Toon 11 has 4,825 properties, about 3,000 visible. Compared with 10.0.29 it adds 376
properties and removes 25. Pro 11 adds another 159 on top of Toon 11, and removes none.

### What Toon 11 adds
- **PBR lighting type** (*Lighting Type* value 9): physically based shading with its own
  surface controls.
  - Metallic, smoothness and occlusion, each a constant and a channel of one packed texture
    (*White* means no texture).
  - Dielectric reflectance (0.5 is the usual 4%); multi-bounce occlusion.
  - Clear coat (strength, smoothness), anisotropy (strength, rotation, mask), **sheen**
    (strength, colour, smoothness, mask), and **glints**: sparkling flakes with density, size,
    colour and variation.

  **Unified:** the *physically based* family. Sheen and glints are new surface response
  features: sheen as a soft cloth-like edge lobe, glints as a flake layer. Glitter is a
  procedural pattern in its own section; glints belong to the specular response instead.
- **Black & White** colour adjustment: lightness, contrast, tint, highlight colour, select by
  colour, with a mask. **Unified:** another colour adjustment operation.
- **Mask levels on about 59 masks:** input and output range, midpoint, invert, normalise, plus
  optional vertex-colour channels added to the mask. **Unified:** the mask building block's
  remap (catalogue 1.4) and a vertex-colour source.
- **Decals:** sprite-sheet animation (columns, rows, frames, FPS), MSDF textures (sharp
  vector-like edges), flip in mirror, smooth border.
- **Lighting limits:** limit brightness per channel or keeping the hue, for the main and the
  additional lights.
- **Alpha:** dithering mode (soften edges, or real transparency); dithered shadows.
- **RGBA colour masking:** per-region occlusion maps, smoothness blend, *affect alpha*. The
  vertex-colour mask type and the per-region PBR toggles are gone.
- **Two backlights** instead of one; rims gain an application stage, anti-aliasing and *apply
  transparency*.
- **Outlines:** a mask channel per property (size, colour, alpha, emission, highlight and so
  on), edge fade, thin-outline smoothing, rim-style outline widths on the lit and dark sides, an
  outline emission map, Z clip and offset, camera depth.
- **Contacts:** up to 8 VRChat contacts, each producing a global mask (radius, falloff,
  position, which people). **Unified:** a mask-bus producer driven by contacts, and so also a
  modulator source.
- **Smaller additions:** dissolve colour adjust; flipbook video, depth and flip in mirror;
  emission *apply transparency*; pathing motion and easing; internal parallax refraction and
  depth fog; voronoi motion and AudioLink; a radial AL spectrum; vertex-colour hue shift; fit
  bounds for vertex effects and LookAt; screen-space UV sizing; post processing that includes
  emission.
- **Lil Fur:** fur bends away from a VRChat contact (*Contact*, *Contact Push*).
- **Removed:** Beatsaber support, the advanced parallax options (cone stepping, anisotropic
  filtering, curvature fix).

### What Pro adds (Pro 11 over Toon 11)
- **TPS deformation:** bending, squeeze, bulge, pulsing and idle shape driven by TPS lights.
  **Unified:** deformation.
- **Squish:** surfaces flatten where a contact presses, with a colour gradient. **Unified:**
  deformation driven by contacts, with a colour layer.
- **Screen-space ambient occlusion** and **contact shadows:** darkening from the depth texture
  (needs Poiyomi's depth prefab). **Unified:** screen-space lighting effects, a new lighting
  group.
- **Outline bake:** baked curvature data for better outline shapes. **Unified:** an outline
  input.
- **Constellation:** stars and connecting lines, animated, with AudioLink. **Unified:** a
  procedural pattern kind.
- **GT7 tone mapping** and **neutral surface emission** under post processing. **Unified:** a
  tone-map choice and an emission adjustment.

**Pro variant shaders:**

| Variant | What it adds | Unified home |
|---|---|---|
| Pro Fur | Shell fur: layer count, length, gravity, tip taper, colour texture, mask, height map, AO, contacts, distance LOD | Fur, method *shells* |
| Lil Fur, Lil Fur Two Pass | As Toon's Lil Fur | Fur, method *strands* |
| Two Pass, Early Outline | Pass arrangements | Render |
| Triple Outline | Three outline layers, each with its own stencil | Outline as a list |
| Grab Pass, Self Grab | Screen-grab effects: refraction, blur, pixelation, colour ramp, an SDF volume, a lava lamp | Refraction layer and screen-grab effects |
| Geom, Geometric Dissolve, Tessellated (and combinations) | A geometry stage; dissolving into pieces; tessellation | Deformation and geometry effects |
| ShatterWave, Particle | Shattering, and particles spawned from triangles | Geometry effects |
| Triplanar Projection | Triplanar texture projection | Mapping |
| World, World Fur | World variants | Read as their avatar shaders; world-only features ignored, with one line |

### Parser
`AtlasPoiyomiToon11Parser`, `AtlasPoiyomiPro10Parser` and `AtlasPoiyomiPro11Parser` share the
Poiyomi base with Toon 10's parser, since Toon 11 keeps 10's names and Pro is Toon plus extras.
11's additions read as their defaults (no effect) on 10's materials, so the shared code reads
them without a version check.

**Detection and mask options:** detection (from the version and Pro markers, or the master label), and on every
mask the shared mask helper builds (and the vertex basics and LookAt masks): **mask levels**
(`<mask>MLInputMin`, `…InputMax`, `…Midpoint`, `…OutputMin`, `…OutputMax`, with
`…Normalize` and its bounds resolved into the input range) and the **vertex colour source**
(`<mask>VertexMask`, 1–4 multiplying by R–A; per channel for the RGBA masks, x for the others).
The levels' invert (`<mask>MLInvert`) is part of the levels, or comes after the vertex colour
when there is one; masks with an invert toggle of their own invert after everything. The global
mask textures' levels are read with the contacts.

**PBR lighting:** the **PBR lighting type** as the physically based family's *calibrated* model:
the packed map (metallic, occlusion and smoothness by channel, white for none) with its
constants, reflectance, occlusion strength and multi-bounce occlusion, the three global masks,
specular anti-aliasing, and its own clear coat, anisotropy (strength × channel, rotation),
sheen (strength × channel, colour, smoothness) and glints. Reflections & Specular and Clear
Coat don't apply with it. Light Data: the limit modes (keep hue) for the main and additional
lights, the SDF's blue and alpha channels, the light volumes' specular light count. Shading:
force shadow on back faces. Anisotropics: even band width. Depth rim: its global mask.
Environmental rim: exposure occlusion. The packed maps' mask levels (BRDF, clear coat, PBR,
cloth) are reported; Light Data's AO, shadow and detail shadow maps take them.

**Colour, masking, decals and alpha:** **Black & White** as a colour operation, first in Color Adjust, with its mask
channel, texture range or picked colours, and select by colour. RGBA colour masking: *affect
alpha* as a second layer on the alpha (its alpha add reported), the regions' **AO** (strength,
own or custom sampling, mask channel and global mask) as occlusion layers taking the darker,
the regions' PBR mask channel and global mask (each *same as colour* by default) for metallic
and smoothness, and the smoothness blend (min and max swap on roughness; add, subtract and
multiply reported); smoothness on the second specular lobe is reported. Decals: MSDF textures,
smooth border, flip in mirror and sprite sheets (columns, rows, frames, FPS, start frame, area,
inset). Vertex colour hue shift. Alpha: the dither mode (transparency as screen-door) and
dithered shadows. The packed RGBA and colour adjust maps' mask levels are reported.

**Rims and related layers:** the lilToon rim's **application stage** (after lighting, on the base colour lit
with the surface, or after lighting by the direct light), anti-aliasing, *apply transparency*
(fade and transparent modes) and *use main texture UV* (the main texture's UV, tiling and
panning before the rim texture's own); the rim colour texture's mask levels are reported. The
**second backlight** (`…__1`) and *apply to outlines*. **Contacts:** up to 8 VRChat contacts
as mask bus steps (tags, people, radii, toucher scale, around the touch or the whole mesh,
invert, target and blend); a contact tinting a theme colour by who touches is reported. The
**global mask textures** take their mask levels and vertex colour sources.

**Outlines:** the mask texture's **channel per property** (size, colour, alpha,
texture blend, emission, highlight, colour adjust, fixed size, lighting, vertex colour
strength; *off* for none), the global mask (alpha, nothing where 0), edge fade, smooth thin
outlines, the light facing falloff, size follows light and side widths, the independent
emission colour and map, Z clip and offsets, writing camera depth, and AudioLink texture offset
and chrono pan. **Color Adjust** (for both 10 and 11): hue with its speed,
saturation and value (old) or saturation and brightness, gamma. The AudioLink mask channel is
reported.

**Further additions:** normal correct's vertex colour mask; the cubemap's *use material smoothness*
(with Reflections & Specular); dissolve's gamma, saturation and brightness (with its hue
shift); flipbook depth, flip in mirror and video effects; emission's *apply transparency*
(with premultiplied alpha); glitter's angle offset; screen-space UV's object relative scale and
world size; the height map channel; post processing on the emission and its global mask. The
fit bounds buttons are editor tools; the matcap normal maps' stochastic sampling is read with
the textures. The distortion noise UV and dot noise space belong to features still reported.

**Patterns:** Pathing's direction channels, motion (repeat or ping-pong), easing, separate
head and tail softness, the packed mask map with a channel per line (levels and vertex colour
sources too), overlap mode, edge anti-aliasing and *use alpha as shared mask*. Voronoi's cell
motion and speed, hue shift, and AudioLink per cell (band or random, history spread, inner
emission, dim quiet cells). Internal parallax's mip maps, smooth layers and their steps,
emission strength, add to alpha, refraction (IOR, normal influence) and depth fog (colour,
density).

**AL spectrum:** the reworked AL spectrum: flip, the radial arc (inner and outer radius, start
angle, arc degrees), display style (fast line, filled, nice line with its pixel thickness), lit
height steps, frequency mapping (minimum and maximum Hz; the band range otherwise, as 10),
bar sampling, audio response, soft compression, the gradient ramp colour mode with its emission,
mask coordinates, master opacity, emission multiplier and tint emission. The preview modes and
bounds display are noted and read as live; the colour mask's levels are reported.

**Pro screen-space effects and tone mapping:** Screen Space Ambient Occlusion and Contact Shadows as pattern layers after
the surface layers (every setting, the masks, the global mask in and out; an animation toggle
left off is off; contact shadows' debug view is noted). GT7 Tone Mapping (colour accuracy,
chroma fade) and Neutral Surface Emission (HDR retention, hue correction) as colour
operations.

**Pro TPS, squish and outline bake:** TPS Penetrator (the tool-configured measurements and baked mesh, channel,
Bézier, squeeze, bulge, pumping, idle shrink) and Squish (collider, strength, spread, max,
falloff, clip offset, mask) as deformations after the vertex options; Squish's colour gradient
(min, max, intensity, replace, multiply, unlit add) as a pattern layer sharing its settings,
with its global mask output. The outline's baked curvature data (hard edges, tips, smoothing,
crevices, UVs). The animation toggles left off are off.

**Pro constellation:** Constellation as a pattern layer after the truchet: placement (UV, centre,
density, pan, offset and layer phase, layer fade, depth), stars, lines, random colours, the
colour and emission, the blend (normal, multiply, add), the mask and the global mask in and
out. AudioLink: emission, star brightness and size, line thickness and brightness as audio
ranges; twinkle, movement and hue (random colours) as chronotensity offsets; the AL theme
colour for random colours; the colour override.

**Two Pass, Pro Fur and Lil Fur 11:** Two Pass (Toon and Pro): the second pass's preset or blend factors,
cull, depth, offsets, colour mask, stencil, alpha to coverage, force opaque, alpha cutoff,
invert and power, and the colour override (colour and alpha, or alpha only); the first and
second pass masks as mask bus steps. Pro Fur: shells (layer count, length, gravity, tip taper,
contact, colour and texture, skin tint, mask, height map and its range, AO and its range, hue
shift, vertex colour normals, distance LOD). Lil Fur 11: the fur mask's options, the contact
and push, the vertex colour modes (Poiyomi, LilToon), and Lil Fur Two Pass's cutout length.

**Grab Pass and Self Grab:** Grab Pass (Toon and Pro) and Self Grab as a refraction layer placed
*surface*, after the dissolve and before the decals as Poiyomi applies it, so decals, matcaps,
flipbooks, rims and the patterns draw on top (the grab isn't lit): refraction (index, chromatic aberration, fresnel power, ignore
foreground), blur (pattern, distance, quality, distance scaling, smoothness, centre importance,
depth ramp), pixelation (size, distance scaling, gap), colour (tint, multiplicative and additive
brightness, hue shift, vibrance, saturation), the colour ramp (presets, custom texture,
contrast, offset, posterise, levels, dither, blend), the alpha blend and blend factors. Each
effect's mask is a blend map channel (with the map's options) and its invert, then its global
mask; an inverted channel without a map turns the effect off. Self Grab blends with what its
first pass drew, in its second pass. Pro's lava lamp is reported.

**Triple Outline and Triplanar Projection:** Triple Outline: layers 2 and 3 read as further outlines, each with
every layer 1 setting under its own names (`_Outline2Width`, `_Outline2…`, `_AudioLinkOutline2…`,
`_Offset_Z2`); the mask texture, blend factors, depth clip and offsets, thin fade, camera depth
and curvature bake are shared; each layer's toggle is honoured, layer 1's too. Triplanar
Projection: the projection mode (depth or mesh), blend method and sharpness, scale with object,
the mask (channel, levels, invert before the vertex colour, strength), max size, edge and camera
fades and depth blur; the constellation is left out, as the variant does.

**Geometry variants:** the Tessellated variants' tessellation (type, uniform factor, edge
length, distance range, Phong, cull margin). Geometric Dissolve, ShatterWave and the Particle
variants are reported, one line each (ShatterWave only while on); Geom adds no settings.

**Lava lamp:** Pro Grab Pass's lava lamp as a pattern layer placed *surface* right after
the grab effects (its own toggle; the alpha and mask): the raymarching data (SDF volume, pixel
size, lower corner, size, minimum thickness, bind data mode, world rescale), refraction, the
blobs (scale, padding, steps, shape, smoothing, spacing, skip chance, sizes, speeds, flow
direction), the material (reflectiveness, roughness, translucency, matcap), the region (heights,
colours with theme colours, haze, tint, top and bottom light, colour range) and AudioLink (band,
blob pulse, flow speed, emission).

**Skin, multipliers and other settings:** the Skin section as a skin tone pattern layer after the detail
texture (LUT, melanin, redness, hemoglobin, freckles and tan with their detail map channels,
UVs and tiling, brightness, tint, blend mode and amount; the preset only sets these values).
PP Animations: the lighting multiplier and addition on the light colours, the emission and
final colour multipliers as colour operations. Force ramped light map (Flat). The add pass's
blend operation: Max, Poiyomi's default (10's too), makes additional lights combine by the
brighter, with the forward-add transparency boost; Add with factors other than the preset's,
or another operation, is reported. The time source (VRChat network time). Early Z as a depth
prepass. The Deliot-Heitz density on stochastic textures. Shading turned off reads as unlit,
with a note. Parallax curvature and the world AO blocker (a world feature) are reported.

## Parser

`AtlasPoiyomiParser` with one parser class per version (`AtlasPoiyomiToon10Parser` for Toon 10),
each reading through `AtlasPoiyomiReader`, which is split over `AtlasPoiyomi*.cs` by inspector
section and made anew for each parse. Section toggles are read from their properties, which
Thry's locking keeps.

**Textures, main colour, alpha, normals and rendering:**
- **Textures:** the UV choice (UV0–3, panosphere, world and local position, polar, matcap,
  screen), tiling and offset, panning (UV per 20 seconds, so ×
  0.05 per second), stochastic sampling, and the UV channel's *UV Settings* (tiling, offset, pan)
  folded in; a channel's rotation is reported.
- **Main:** colour (linear) × texture, pixel mode as *pixel* sampling, *ignore alpha* as the
  colour's constant alpha, vertex colouring (colour and alpha amounts, *Linear Colors* as sRGB
  data).
- **Alpha:** the alpha map (replace, multiply, add, subtract; one channel; its mix and levels
  reported), alpha mod as the offset, distance, fresnel and angular fades as modifiers, AudioLink
  alpha as an *add* modifier driven by an audio modulator, sharpened alpha-to-coverage, dithering,
  force opaque.
- **Normals:** the main map; the second map and the detail normal as normal layers (the detail
  mask's green); the bent normal; normal correction.
- **Rendering:** the preset, by its blend factors (soft additive `OneMinusDstColor One`,
  multiplicative `DstColor Zero`, ×2 `DstColor SrcColor`); changed factors become *custom*;
  TransClipping is fade with *cutoff in transparent*; culling, depth test, write, clip and
  offset, colour mask, stencil (front and back stencils reported), fog, flip back-face normals
  as two-sided lighting, reduced clip distance.

**Lighting types**, on the one ramp toon formula (catalogue 3.2):
- **TextureRamp:** the ramp texture, ramp offset, shadow strength as the ramp strength, shadow
  tint; *ignore indirect shadow colour* splits the shadow side between ramp-scaled, occluded
  direct light and the ambient light. Several ramp rows picked by UV are reported.
- **Multilayer Math:** up to three bands (colour, colour texture with its intensity, alpha,
  border, blur, normal blend, receive shadow; a band without alpha is left out), *non linear
  lightmap* as smooth edges, the border colour (times its alpha) and range, base colour blend,
  environment strength, shadow strength and its mask (*strength* type; the *flat* type, the blur
  map and the border AO map are reported).
- **Flat** as the flat family; **Realistic** as Lambert with shadow strength and tint.

**The other lighting types:**
- **SDF:** the SDF face term (threshold style) with its texture, forward and left directions and
  blur; the shadow side as Multilayer Math's.
- **Wrapped:** the wrapped term (wrap, normalization), the gradient start and end as ramp keys,
  shadow tint, lit side colour as the lit tint.
- **ShadeMap:** two bands (1st and 2nd shade colours and maps, *base as 1st* as colour from
  base, *1st as 2nd*), the steps and feathers as borders and blurs, *replace* as bands replacing
  the base. A shade map's alpha as a shadow mask is reported.
- **Skin:** physically based with pre-integrated skin diffuse (LUT, scale, thickness map with
  invert and power, absorption, scattering stability, diffuse normal softness, shadow tint,
  *ignore indirect* as the constant shadow, shadow strength).
- **Cloth:** physically based with Disney diffuse; the cloth map feeds metallic (R, squared:
  read linearly with a map, noted), reflectance (B) and smoothness (A, with invert); its green
  is the sheen mask; *clothmask lerp* as blend.
- **Light Data's SDF light map** as the SDF face term (blended style, vertical blend, LOD as
  softness).

**Light Data** (catalogue 3.4):
- **Light colour mode:** Poi Custom as *merged probes* (with *Indirect Uses Normals* as the
  directional amount), Standard as *separate*, UTS2 as *floored* (with its unlit intensity),
  OpenLit as *split probes*.
- **Light direction mode:** Poi Custom as *probe-blended*, forced local and world as *fixed*,
  UTS2 as *view fallback*, OpenLit as *split probes*, view direction with its yaw and pitch.
- **Light map mode** as the shading term: Poi Custom as *relative brightness* (half-Lambert under
  a forced direction, as the shader does), normalized and saturated N·L, casted shadows only.
  Multilayer Math keeps its own per-band terms. The SDF light map is the SDF face term (see
  above).
- **AO maps** as occlusion, **shadow maps** as the term mask, **shadow masks** as the ramp
  strength mask: the channel in use with its strength; several channels in use and
  different additive detail strengths are reported.
- Received casted shadows, softness, clean shadows, grayscale lighting, limit brightness (with
  its ceiling and luminance limit), min brightness as *minimum light*, forced light colour.
- **Additional lights:** pixel and vertex lights on or off, their lighting type and gradient,
  limit, received shadows, grayscale, passthrough, attenuation curve, ignored directional
  lights, mirror vertex lights.
- **Light volumes:** on, specular strength, normal bias.

**Surface response:**
- **Reflections & Specular** off means no highlights or reflections (Cloth keeps its own; both
  on is reported). On: metallic and smoothness from the packed map's chosen channels (or white)
  times their sliders, with their inverts; the reflection and specular masks with their
  visibilities (an inverted mask below full strength is noted); tints; fresnel strength;
  exposure occlusion; the fallback cubemap (forced as the source, lit or not); ignore casted
  shadows; 2nd specular as the second lobe; pixel normal mix (0 the mesh normal, between is
  reported); GSAA; GGX anisotropics (amount, map channel or vertex colour, reflection stretch,
  roughness anisotropy, direction map). Split mask sampling is reported; TPS
  is unsupported.
- **Clear Coat:** the coat with its packed map (mask, smoothness, reflection and specular masks),
  tints, fresnel, exposure occlusion, fallback, ignore casted shadows, pixel normal mix, GSAA.
- **LTCGI:** tints, multipliers, its own metallic and smoothness (or the surface's), exposure
  occlusion.

**Surface layers:**
- **Detail texture:** a base colour layer, multiply ×2, opacity = intensity, tint × brightness,
  the detail mask's red; without a texture its grey (neutral).
- **Back face:** a layer on back faces: colour × texture, replacing the base colour by mask ×
  alpha, or replacing the alpha; emission strength. Its detail intensity and emission
  multiplier are reported.
- **Decals (4):** placed layers: position and side offsets folded into the centre and scale,
  the aspect ratio fitted from the texture, rotation and speed (× 2 per second), tiled, symmetry,
  mirrored UV mode, depth; colour × texture by alpha, blend type, alpha × alpha intensity as the
  opacity, the shared RGBA mask's channel, face mask, emission strength replacing the emission
  under it, show on outlines. A decal without a texture, the alpha blend mode, video and chromatic
  aberration are reported.
- **Emissions (4):** emission layers: colour × map by alpha, strength, mask with channel and
  invert, *use base colors* (source times base colour), *override base color*.

**RGBA Color Masking.** Each enabled channel, masked by its chosen channel of the
mask texture or of the vertex colour:
- a colour layer: colour × texture (with the channel's scale and offset merged in), the blend
  type, by alpha plus *alpha add*, and its emission strength;
- a normal layer when it has a normal map and intensity: whiteout over the base normal, or
  replacing it when no blend source is chosen; blending with other channels' normals is
  reported;
- metallic and smoothness layers (with Reflections & Specular on, where the shared maps are
  set): the channel's value replaces the surface's, with its inverts; custom sampling is
  reported.

Global masks are reported.

**Lookup layers:**
- **Matcaps (4):** one layer per blend weight in use, in Poiyomi's order (replace, multiply,
  add, screen, unlit add after lighting, mixed), sharing the source and mask; a matcap that only
  glows keeps one layer for its emission. The UV modes as projections (UTS style as view
  corrected, top pinch as view stabilised, double sided as reflection planar, gradient, light
  direction as light half vector); border × 2 as the tiling, rotation, panning, UV blend, mirror
  flip, VR parallax (centre eye when below half; between is reported), normal strength, base and
  light colour mix, colour × intensity by alpha, reflection fallback, show on outlines, the mask
  with channel and invert, hide in shadow, circle mask, smoothness as blur, the custom normal
  map. The smoothness mask and alpha options are reported; TPS is unsupported.
- **CubeMap:** a lookup layer: skybox, reflection, world and local normal; replace, multiply or
  add by alpha × blend amount; colour × brightness; emission; smoothness as blur; normal to use;
  mask; hide in shadow; rotation and its panning (one axis exact, more than one reported for
  the order). Without a cubemap, grey at half alpha.

**Rim Lighting** 0 and 1 as angle layers with the step profile:
- **Poiyomi style:** `1 − linstep(saturate(w − blur), w, facing^power)`, `w = lerp(−0.05, 1,
  width)` (none at `w ≤ 0`); invert as the edge base, linear; colour × brightness × texture by
  alpha, base colour mix, the five blend modes with blend alpha, emission, normal strength, the
  mask with channel and invert; the shadow-map light direction mask as hide in shadow.
- **UTS2 style:** `linstep(inside, 1, edge^(2^(3 − 3 × power)))`, a hard step with feather off;
  added; light colour mix; its own mask and channel.
- **lilToon style:** `edge(edge^fresnel power, border, blur)` after lighting; replace, add,
  screen, multiply; main colour blend; lighting as scale by light; shadow mask as hide in
  shadow; backface mask as front faces only; VR parallax as per eye; *mask only* makes the
  texture a mask.
- Reported: the bias map, the UTS2 mask level, partial lighting or parallax, apply to alpha.

**Rim light direction and the other angle layers:**
- **Rims' light direction:** Poiyomi's custom falloff as a smooth light-side mask (inverted,
  strength), shrink in shadow; UTS2's direction mask as a subtracting light side, its
  antipodean rim as a second layer on the dark side (own power, feather, colour, light colour
  mix); lilToon's direction split as a light-side mask before the step and a second layer for
  the indirect rim (its colour, border, blur, range). An inverted shadow-map falloff is reported.
- **Depth Rim Lighting:** angle layers with the depth source, one per blend weight (unlit add
  after lighting, replace, add, screen, multiply): samples, width, sharpness, threshold, binary,
  fixed size threshold, distance fade, normal to use, the mask, colour × brightness, base and
  light colour mix, emission, *ignore lighting* as after lighting, the N·L light direction as a
  light-side mask, the ramped light map as hide in shadow. The screen-space light direction and
  the attenuation mix are reported.
- **Environmental Rim:** a falling smooth step on facing, the probe along the normal (blur as
  roughness, the brightness threshold) × base, × intensity, after lighting.
- **Backlight:** the backlight source (directivity, view strength, received shadow), border and
  blur as the step, colour × texture × the light's colour, main colour power, normal strength,
  the backface mask.
- **Subsurface Scattering:** the subsurface layer with the direct light: colour × the map's RGB,
  the map's chosen thickness channel with its mod as the offset, strength (× π, as the layer
  divides by π), spread as the power, distortion, light colour mix, shadow strength, base colour
  mix.

**Highlight layers** (catalogue 4.8):
- **Anisotropics:** strand highlights, one layer for *add* (after lighting) and one for
  *replace*: the colour and offset map, two lobes (power × 1000 as the exponent, strength,
  offset, map offset strength, direction, tint, stylised edge and blur), hide in shadow, fade by
  light direction, light and base colour mix, normal blend.
- **Stylized Reflections, UnityChan mode:** a toon highlight: the specular map × tint, the mask
  with channel and invert, toon or realistic, the blend mode (add after lighting), strength,
  light colour, normal strength, two layers (size, feather, strength), show on back, ignore
  shadow, ignore cast shadows. The mask level is reported.
- **lilToon mode** is reported.

**Colour adjustments** (catalogue 1.10):
- **Color Adjust** on the base colour, in the shader's order: hue (select or shift, Oklab or
  HSV, the mask's red), the gradation map (its mask channel, strength), gamma as power (alpha),
  saturation (blue), chromatize (red), tint as a hue tint (colour, texture, its alpha), brightness
  (green), clamp. Hue offset by the mask (*Hue Replace* off) is reported.
- **Post Processing** on the final colour, by its mask: hue, tint, RGB, saturation, HDR boost as
  sRGB decode, contrast, brightness, lightness, posterization. The mask applies per operation
  (noted); the hue offset by mask is reported.
- **Layers' own:** hue shifts on back face, decals, emissions, matcaps (every weight's layer),
  cubemap and rims (both layers of a dark-side rim); a decal's colour adjust (gamma, saturation,
  chromatize, brightness, clamped); an emission's saturation; the cubemap's brightness (added),
  contrast and saturation (its own luminance weights). A decal's hue by view angle is reported.

**Global Mask and Global Themes** (catalogue 1.5, 1.6):
- **Mask bus:** the four textures (one channel each, 1R … 4A), vertex colours (with their
  blends and linear colours), back face, mirror, camera and distance modifiers, and the options
  (sliders as offsets, min max sliders as remaps, force on and off). Split sampling, generic
  mirror detection (noted) and writing the shading into a mask are reported.
- **Consumers:** every global mask selector reads its channel with its blend: alpha and
  its fades, the second and detail normals, detail texture, RGBA channels and their normals,
  decals, emissions, matcaps, cubemap, rims, backlight, subsurface, anisotropics, colour adjust,
  light data's AO, detail shadow and shadow masks, cloth, reflections and specular, clear coat.
- **Producers:** decals, rims and the backlight write their coverage (*apply to global mask*).
- **Theme colours 0–3** are resolved to colours as the shader computes them (HDR split, hue
  shift, the old or new saturation, brightness as exposure); ColorChord and AudioLink themes go on
  the colour input; theme hue speeds are time modulators.

**Time and light modulators** (catalogue 1.9):
- **Hue speeds** (colour adjust, post processing, every layer's hue shift) as a saw added to the
  hue amount, at the speed ÷ 20 turns per second (reverse saw when negative).
- **Emissions:** light based as a light level modulator on the emission strength (min and max
  light as a linear range, world as the direct light, mesh with the shading); scrolling as a
  sweep (direction, width, velocity, interval, offset, vertex colour as position); blinking as
  a sine (smooth) or pulse (flashing, with its duty) at velocity ÷ 2π, with its offset. The
  scrolling curve, fluorescence and centre out are reported.
- **BlackLight Masking:** blacklight steps on the mask bus (key, range, blend).
- Theme hue speeds are reported (the theme's colour at time 0 is used).

**AudioLink** as audio modulators, each idle without AudioLink in the world and only
with the section (and its animation toggle) on; the section's per-band smoothing:
- **Emissions:** strength add, then multipliers, the centre-out add as the band's history along
  the view angle (duration × 128 samples, size as the threshold; reversed is reported), the
  colour override (blend to colour, the ramp as the curve).
- **Decals:** scale x and y, rotation, alpha (added to the opacity), emission, the chrono
  rotation (speed × 360°), the colour override. Side offsets by AudioLink and the ColorChord
  strip are reported.
- **Matcaps:** alpha add, emission add, intensity add (as × (1 + add ÷ intensity)), the colour
  override, on every weight's layer; the chrono pan is reported.
- **Rims (Poiyomi style):** width add (both step thresholds), emission add, brightness add (as ×
  (1 + add ÷ brightness)), the colour override.
- **Alpha** and the **colour adjust hue** (chronotensity × motion speed).
- **ColorChord and AudioLink theme colours** go on the colour input (base colour, decals,
  emissions, back face, RGBA channels, rims, the toon highlight); elsewhere they're reported.
- Band overrides are reported.

**Proximity colour and emission extras:**
- **Proximity Color:** a tint of the final colour (override colour) and of the emission
  (override emission) by the min colour, blended to the max colour by a camera distance
  modulator (pixel or object position, min and max distance, the transition curve, solid as a
  step). The edge colour and the forced back face colour are reported.
- **Emission fluorescence** as a fluorescence adjustment on the emission layer.
- **Centre-out emission:** the emission map in the facing gradient space (linear as its linear
  form), panning at the flow speed.

**Outlines** (catalogue 5.1): the mode (basic, rim light as light facing,
directional, drop shadow) with its direction; size ÷ 100, local or world; the size mask and
vertex colour mask; vertex colour normals; fixed size over distance; clip at zero width; colour
× texture (a theme colour, AudioLink themes), main texture blend (UTS style squared), rim light
blend as light colour mix; lit with its shadow strength; emission; the highlight; override base
alpha; distance alpha as a fade; cull, depth write and test, blend factors, stencil; AudioLink
size, emission, highlight emission and colour overrides. The colour adjust, the depth offset,
blend operations and face stencils are reported.

**UVs:**
- **Distortion UV** (texture mode) is one shared distortion for every texture choosing
  *Distorted UV*, on the distorted channel: the two flow textures (× 2 − 1, each × its
  strength, averaged), a missing one as a constant shift, the mask. The other modes (Gerstner,
  simplex, voronoi, curl, FBM) and its AudioLink are reported.
- **UV Settings rotation** (angle and rotate speed) folds into each texture's rotation and spin;
  with the channel's own tiling or offset it's noted as approximate.
- **Stochastic Sampling's mode:** None turns it off; Hextile is reported.
- **Parallax Heightmapping** as multi-step parallax (heightmap, strength, offset, max steps;
  noted); another UV channel and the mask are reported.
- Polar, panosphere and local or world UV settings other than the defaults, and shifted back
  face UVs, are reported.

**Dissolve** as a transition layer (catalogue 4.2): the type (basic gradient
texture, point to point, spherical, centre out) with its settings, the dissolve alpha as the
amount (padded), softness; the gradient noise (texture mode), wave mode, alpha only with its
ramp, the dissolved colour and texture with emission; the edge (width, colour, flip, emission);
the mask (red or a vertex colour channel, invert, global mask) scaling the amount; the dissolved
and undissolved outputs into the mask bus; hue shift; continuous dissolve as a time modulator
on the amount; AudioLink alpha, detail, edge width, edge emission and colour override. 3D dot
noise, the edge gradient and hue, UV tile dissolve, vertex height and the AudioLink chrono are
reported.

**Flipbooks** 0 and 1 as surface layers like decals: the texture array with the
decal placement (position, scale fitted to the aspect, side offsets, rotation and speed, tiling,
symmetry, mirrored UVs) and frames (FPS, frame offset, start and end frames, manual frame
wrapped by the range, crossfade); colour and alpha, colour as mask, intensity as alpha, blend,
emission, mask with global mask, hue shift; AudioLink scale, alpha, emission, frame, chrono and
colour override. The colour and alpha override is reported. Mirror/Camera Visibility: the VRChat
toggles (normal, mirror VR and desktop, camera VR and desktop, screenshot) or the generic mirror
mode into the render visibility; the mirror texture and colour as two layers shown only in
mirrors (everywhere when forced). UV Tile Discard and face discard as hidden tiles; the vertex
and pixel modes read the same.

**Glitter** / Sparkle as a glitter pattern layer (catalogue 4.6): the three modes
with their own brightness, speed and distance LOD (linear's sensitivity, contrasts, angle
limit, light direction and VR parallax), shape, UV with panning, density, layers, random
position, size and its range, overlap, normals; the shape texture (pixel mode, sprite sheet),
colour map, colour with alpha, base colour mix, random colours; rotations; the mask (channel,
invert, global mask), hide in shadow, scale with lighting; add or replace; hue shift with the
random offset; AudioLink alpha (read as the opacity), max brightness, size, the two chronos and
the colour override.

**Pathing**, Voronoi and Truchet as pattern layers (catalogue 4.6). Pathing: the
texture or UV source, merged gradients, point sampling, the colour map, remapping, override
alpha, the blend mode, and per channel the type, colour, direction, emission, softness, speed,
length, gap, manual timing, offset, segments, remap range and global mask; AudioLink time,
width and emission offsets and chronotensity. Voronoi: type, space and UV, scale, power,
gradient, speed, noise, colours and emissions, random cell colours, blend, alpha, mask and
global mask; AudioLink emissions, gradient, the chronos and both colour overrides. Truchet: UV,
pan and offset, density, thickness, edge sharpness, texture with panning, colour, alpha,
override alpha, blend, emission, mask (channel, invert, global mask); AudioLink emission, alpha,
chrono pan, colour override and hide without AudioLink. The paths' AudioLink history,
autocorrelator and ColorChord strip, and the truchet mask's intensity, are reported.

**Internal Parallax**, Depth FX and AL Volume Color as pattern layers (catalogue
4.6). Internal parallax: the mode, level blend, map with its placement, tiling and pan, height
from alpha, per-level offset, iterations with the distance falloff, depths, fades, colours,
surface blend, mask (channel, invert, global mask), hue shift and per-level hue; AudioLink
emission, depths, chrono pan, chrono hue and both colour overrides (the spatial history is
reported). Depth FX: colour and emission (blend, texture, the depth gradient UV, ranges), alpha
(ranges), mask and global mask. AL Volume Color: UV and direction, the three colours (squared,
as Poiyomi multiplies each by its theme colour), emissions, blend and alpha. The stats overlay
is out of scope: one *ignored* line.

**Video Effects** and AL Spectrum as pattern layers (catalogue 4.6). Video effects:
the screen type, brightness, emission, pixel texture with its UV and the resolution, pixelate,
saturation, contrast, the CRT refresh rate and fade, the Gameboy ramp, mask and global mask.
AL spectrum: UV and placement (position, rotation and speed, scale, side offsets, mirrored UVs,
symmetry, tiled), circle mode and dimensions, line width, bilateral, mirrored bands, band
range, volume, bass and treble boost, the steps and clips, shape clip, the colour mode,
ColorChord emission, the colour source, the three colours and emissions, the colour and mask
texture, blend, alpha, alpha override, the video effects and the global mask. The debug preview
is noted and read as live.

**Vertex Options'** Basics & Fun as deformations (catalogue 5.3), in Poiyomi's
order: scale, rotation with its speed, local translation (each × its w), barrel, sphere,
tornado, wind (with the effects mask as its noise), world translation, the height map
displacement, rounding (world or local, to the cell's middle). Each takes its mask channel:
the effects mask's RGBA or a vertex colour channel. AudioLink: local translation, rotation by
bands and chronotensity, scale, world translation and height. Rounding by AudioLink and the
spectrum motion are reported.

**LookAt**, Glitching, Depth Bulge and View Clip Prevention (Uzumore) as
deformations after the basics. LookAt: the mask's four rigs (target up, forward, up, pivot,
view-from position, distance and field-of-view falloffs, pitch, yaw and roll limits, alpha),
its own visibility, and the AudioLink alpha. Glitching: strength, interval, threshold, the
glitch map with density and pan speed, the mirror choice, the mask and the AudioLink band with
override. Depth bulge: height, touch distance and mask. Uzumore: push amount, bias, object
scale and mask; its animation toggle off is read as off.

**Lil Fur:** the variants' fur pass into the material's fur (catalogue 5.2): fur
mode, the fur mask with its four strengths, the direction map and its scale, the vector and
length, the length and alpha mask channels, vertex colour direction, gravity, randomize, noise,
AO, layer count, root width, width, the wind, the rim light, and the LilFur Rendering settings
(cull, depth test and write, depth clip, offsets, colour mask, alpha to mask, stencil). The
blend factors follow the fur mode; separate front- and back-face stencils are reported. Every
Poiyomi 10 section is mapped or reports its own unmapped details.

## Older versions

**Support is pragmatic.** Poiyomi 7.3 to 9.3 materials are read by 10's
parser with version tables for settings whose names or meanings changed. Features Poiyomi 10
rewrote are read with 10's meaning and noted once each while in use, as approximate.

**9.0–9.3:** `AtlasPoiyomiToon9Parser` (detected from the 9 markers or the master
label). Meaning changes handled: the depth rim's type (9: two or ten samples; 10: two, four or
eight), and the SSS thickness, which 9 always read from the map's alpha (its channel setting
was unused). Mislabelled enums in 9 (the SSS and internal parallax mask UVs, the push-back mask
"channel" that is a UV) read the same as 10. Noted as read with 10's meaning while in use:
decals (and their TPS masks), the AL decal (its UV position, scale, rotation), dissolve (edge
hardness, detail edge smoothing, continuous dissolve, per-tile alphas, clamps, centre-out
power), glitter (centre size), matcap, flipbook (scale and offset, panning, replace), depth rim,
the Poiyomi rim style (sharpness), clear coat, reflections and specular, stylized specular,
subsurface scattering, RGBA colour masking, voronoi, video screen, backlight, UV distortion and
the outline's AL colour range. 9.0's anisotropy noise map, reflection and clear coat fresnel
strengths and outline texture HSVG are reported when set. Lighting reads the same: 10 refined
cookies, light volumes and additional lights.

**Pro 9.0–9.3:** `AtlasPoiyomiPro9Parser` (Pro by its shader name or the Pro markers, 9 as
above), from the Pro 9.3 source in the `com.poiyomi.pro` package (`Shaders/9.3/Pro`). Pro 9 is
Toon 9 plus Pro features, read as Toon 9 is. Its Pro features compared with Pro 10:
- the same: GT7 tone mapping, TPS and tessellation;
- not in Pro 9, so read as off: contact shadows, squish, the lava lamp, Self Grab, Triplanar
  Projection, Triple Outline and the constellation's AudioLink colour override;
- the screen-space AO's colour map and mask each have a toggle (`_SSAOEnableColorTexture`,
  `_SSAOEnableMaskTexture`); without it the map isn't used. There's no camera clip (0 in Pro
  10's meaning) and no normal rejection;
- noted as read with Pro 10's meaning while in use: the screen-space AO (Pro 10 rewrote its
  sampling, and reads the colour map as a ramp at the middle of its height), the constellation
  (its lines and sparkles) and shell fur (the shells' spacing when fewer are drawn at a
  distance, the root occlusion curve, and the fur colour on the skin).

**Pro before 9** is read as Toon of its version: no Pro source before 9.3 is available, so its
Pro features aren't read, and one *ignored* line says so.

**8.0–8.1:** `AtlasPoiyomiToon8Parser`, on top of 9's handling. Meaning changes: the
Poiyomi rim style's blend lived in `_RimBlendMode` (numbered as 10's `_RimPoiBlendMode`); the
lilToon rim always added; the alpha map was `_ClippingMask` (red channel, *Inverse Clipping*);
the main colour's brightness was added (`saturate(colour + brightness × mask)`; 9 and later
multiply by `1 + brightness`); hue shifts were always in HSV, with no colour space setting.
8.0 offered more colour blends on decals and the like, some on numbers 10 reuses (colour dodge
is 7, subtract in 10): burns read as multiply, dodge as screen, the light modes as overlay,
reported. 8.1's global mask blend listed colour blends while the shader used 10's mask blends,
so screen read as average and the values past add gave 0; those are read as replace and
reported. 8.0's world position UV was XZ, as 10's default. The notes also name 8's dropped
settings (glitter panning, jitter and frequency; flipbook multiply and add; the rim's clamp;
RGBA masking's vertex colour source, multiplicative blending and normals); the cubemap's
replace, multiply and add weights are noted; iridescence (removed in 9) and 8.0's back face
alpha are reported.

**7.3:** `AtlasPoiyomiToon7Parser`, on top of 8's and 9's handling. 7.3 is a
different generation (952 properties, 316 not in 10), so more is approximate. Read through a
name table, same meanings: `_MainTextureUV`, `_ForceOpaque`, `_DitheringEnabled` and
`_DitherGradient`, `_AlphaToMask`, `_MainAlphaToCoverage`, `_MainMipScale`, `_EnableRandom`
(angular fade), `_EnableLighting` (shading), `_ParallaxMap`, `_ParallaxHeightMap`,
`_ParallaxStrength`, `_ParallaxInternalMapEnabled`, and the distance fade's `_MainDistanceFade*`,
`_MainMinAlpha`, `_MainMaxAlpha` and `_MainFadeType`. Meaning changes: UV index 4 is the
distorted UV (7 since 8); the lighting types are Toon (by `_LightingRampType`: texture ramp,
math gradient as multilayer math, shade mapping), Realistic, Wrapped, Skin and Flat; vertex
colouring has no toggle (its slider is always live); the height
map has its own toggle and reads green; the distance fade has no toggle and applies unless its
settings leave the alpha as is; the hue shift toggle switches the whole Color Adjust section
(8 gave the section its own toggle). Noted: the lighting (ramp mask, HSL shadows, shadow mix, colour
modes and limits) and parallax. Reported: the two specular sections, metallics, the BRDF
section, clear coat, subsurface scattering, the panosphere section, the AO, detail shadow and
ramp mask maps, basic emission, the fade mask and emission AudioLink.

## Out of scope

- **World-only features** of the world variants (lightmaps and the like); the variants themselves
  are read as their avatar shaders.
- **Beatsaber support:** a third-party game, not VRChat.
- **Raymarching:** an empty category in this version.
- **Debug** views, the shader UI warnings, and the optimiser's own properties.

---

## Cross-shader notes

What Poiyomi adds to, or confirms in, the other inventories:
- **Lighting families** settle at four: *unlit*, *flat*, *ramp toon*, *physically based*.
  - Ramp toon has a shading-term source (N·L, received shadows, SDF), a ramp, and shade
    colours from constants, the base colour or textures.
  - Physically based has a diffuse model (Lambert, Disney, skin, cloth).
  - Shared parameters: light direction override, colour override, brightness floor and
    ceiling, contributions per light type.
- **Layer types:** base colour, normal, detail, recolour, decal (with frames), transition
  (dissolve), matcap, cubemap, rim (edge sources: view angle, depth, intersection, light
  direction), emission, procedural pattern, back face. Each has a blend, masks, placement
  before or after lighting, and modulators.
- **Modulators:** time, audio, light level, camera distance, blacklight, spatial sweep, noise
  (UV distortion). All can drive any numeric parameter.
- **Masks:** a mask bus with numbered channels that features read from and write to, plus
  modifiers for back face, mirror, camera and distance.
- **Palette:** shared theme colours that features reference.
- **Render visibility:** mirror and camera visibility, UV-tile discard, near clipping.
- **Bake candidates:** colour adjust, skin, static decals, recolour layers, detail, normal
  correct, static vertex transforms.
