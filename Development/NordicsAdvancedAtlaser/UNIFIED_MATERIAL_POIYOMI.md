# Unified material — Poiyomi Toon

**Status:** first draft, merged into the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md); the
catalogue's names take precedence. Part of the per-shader inventory that
[`UNIFIED_MATERIAL.md`](UNIFIED_MATERIAL.md) builds on; same format as
[the VRChat Mobile draft](UNIFIED_MATERIAL_VRCHAT_MOBILE.md), but more condensed: Poiyomi has
about 60 top-level sections.

**Source:** Poiyomi Toon 10.0.29, in Nordics Nardo
(`Packages/com.poiyomi.toon/_PoiyomiShaders/Shaders/10.0/Toon/Poiyomi Toon.shader`). The inspector
is Thry Editor, built from the shader's property block: section markers, toggles and hidden
flags. I extracted the section tree with a script: **3,801 properties, about 2,640 visible.**
Older versions (7.3, 8.1, 9.0, 9.3) aren't covered; their differences are parser work.

**Variants:** Poiyomi Toon, Two Pass, Early Outline, Grab Pass, Lil Fur and Lil Fur Two Pass
share this feature set. Grab Pass adds screen-grab effects; Lil Fur adds a fur pass, covered in
[*Lil Fur*](#lil-fur-variants-lil-fur-and-lil-fur-two-pass). **Poiyomi Toon World** is world-only and out of scope. **Poiyomi Pro** isn't
installed, so its features aren't covered.

**Parser today:** the Poiyomi parsers (`AtlasPoiyomi*.cs`) read some controls in most sections,
many as raw values. Each feature has a short *Parser today* line based on a script count of how
many visible controls the parser mentions; that's a rough guide, not a check of correctness.

**How Poiyomi is organised, and what it means for us:**
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
- **Parser today:** reads all five.

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
- **Parser today:** reads most values.

### Normal Map 2, Detail Normals & Texture, Bent Normal Map
- **Controls:**
  - Second normal with mask.
  - Detail texture (tint, intensity, brightness) and detail normal, sharing a detail mask.
  - Bent normal for specular occlusion and indirect diffuse.
- **Live or baked:** baked; part of normal compositing. The bent normal is a lighting input.
- **Unified:** extra normal layers with masks; the detail layer; a bent-normal input on the
  surface response.
- **Parser today:** reads all three.

### Normal Correct
- **Controls:** amount; origin point.
- **Behaviour:** bends normals toward a sphere around the origin, for softer face shading.
- **Live or baked:** can be baked into the normal map for a given mesh, since the origin is in
  object space.
- **Unified:** a normal modifier, "spherize toward point"; or the parser bakes it into the
  normal composite.
- **Parser today:** none.

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
- **Parser today:** reads a few.

### Skin
- **Controls:** a skin tone preset; skin detail map; pigment (colour lookup texture, melanin,
  redness, hemoglobin); freckles and tan (UV, tiling, strength, redness); brightness, tint,
  blend.
- **Live or baked:** baked; it's a procedural base colour.
- **Unified:** the parser bakes it into the base colour, so no unified feature is needed unless
  it's animated.
- **Parser today:** none.

### Back Face
- **Controls:** colour, texture, mask, emission, hue shift, detail intensity, replace alpha,
  emission limit. Requires culling off.
- **Live or baked:** live: the same texels look different from behind, so it can't go into
  one texture.
- **Unified:** a **back-face appearance** override: colour, texture, emission and hue for back
  faces.
- **Parser today:** none.

### RGBA Color Masking
- **Controls:** a mask from a texture or vertex colours; per channel R, G, B, A:
  - colour and texture with tiling, blend mode;
  - normal map and blend;
  - emission strength;
  - metallic and smoothness maps with their own sampling.
- **Live or baked:** baked when static.
- **Unified:** the **recolour layer** from Toon Standard and Uber, generalised: a region
  defined by a mask channel, with colour, texture, normal, emission and surface overrides.
- **Parser today:** none.

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
- **Parser today:** reads most values.

---

## Shading

### Lighting type
Poiyomi's *Lighting Type* chooses the whole shading model. Proposed families:

| Lighting type | What it looks like | Proposed unified family |
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
- **Parser today:** reads lighting type and some shadow layers. This is where the
  [*Known gaps*](UNIFIED_MATERIAL.md#known-gaps) note about 15 lighting modes comes from.

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
- **Parser today:** reads about half.

### Anisotropics
- **Controls:** colour and offset map; replace or add blending; hide in shadow; fade by light
  direction; mix light and base colour; top and bottom layers (power, strength, offset, tint,
  direction, edge, blur).
- **Unified:** the *stretched* highlight shape from Uber, with two layers and a stylised edge.
- **Parser today:** none.

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
- **Parser today:** reads nearly all, with typed weighted blends.

### CubeMap
- **Controls:** projection (skybox, reflection, world normal, local normal); colour, cubemap,
  blending, brightness, emission, smoothness; mask; hide in shadow; colour adjust.
- **Unified:** the cubemap colour layer from Uber, with a projection.
- **Parser today:** reads little.

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
- **Parser today:** reads a few values.

### Depth Rim Lighting
- **Controls:** normal to use; sample count; width, sharpness, depth threshold; distances;
  light-direction options; mask; colour options; blending weights.
- **Behaviour:** a rim from depth differences in screen space, so it outlines silhouettes
  against what's behind them. Needs a depth texture; Poiyomi ships a prefab for avatars.
- **Unified:** rim layer with **edge source** *depth* instead of *view angle*.
- **Parser today:** none.

### Subsurface Scattering
- **Controls:** colour; colour and thickness map; thickness; light strength, spread,
  distortion; light and base colour mix; shadow strength.
- **Unified:** subsurface layer, as in the Mochie drafts.
- **Parser today:** reads a few.

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
- **Parser today:** reads a few; clear coat partly.

### Environmental Rim, Stylized Reflections, Backlight
- **Environmental Rim:** the reflection-coloured rim from Uber. *Parser today:* reads all.
- **Stylized Reflections:** toon highlights, in UnityChan style (map, tint, size, feather, two
  layers) or lilToon style (toon specular border and blur, reflections). These map to
  surface response with highlight style *stepped* and a size and feather. *Parser today:*
  almost none.
- **Backlight:** light from behind glowing through thin edges: colour, texture, border, blur,
  directivity, view strength. A rim variant with a back-light response. *Parser today:* reads
  most.

### LTCGI
- **Controls:** diffuse and specular tint and multipliers; use the surface's own settings or
  its own metallic and smoothness.
- **Unified:** external lighting. *Parser today:* reads most.

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
- **Parser today:** none.

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
- **Parser today:** none.

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
- **Parser today:** none.

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
- **Unified:** emission layers, plus the modulators from the Uber draft: time (blink waveform,
  scroll sweep), light level, audio. Scrolling and centre-out are spatial sweeps: a
  modulator whose phase depends on position.
- **Parser today:** reads about half.

### Flipbooks (2)
- **Controls:** texture array, colour, mask, tiling, blending, alpha, emission; FPS and frame
  offset; placement as for decals; manual frame; start and end frame; crossfade; hue;
  AudioLink on scale, alpha, emission, frame.
- **Unified:** the decal layer with frame animation, as proposed in the Uber draft.
- **Parser today:** none.

### Glitter / Sparkle
- **Controls:**
  - Mode: angle-based, linear emission, light reflections.
  - Shape: circle, square or texture; colour map, colour, base colour, random colours.
  - Density, size, randomness, panning; brightness range, speed, angle range; rotations;
    sprite sheet; masks; hue; AudioLink.
- **Unified:** a **procedural pattern layer**, pattern *glitter*, with view- and
  light-dependent sparkle. See *Procedural patterns* below.
- **Parser today:** reads about a third.

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
- **Parser today:** none.

### Mirror/Camera Visibility
- **Controls:** generic or VRChat mode; visibility in mirrors, cameras and normal view; a
  mirror texture and colour.
- **Unified:** render: mirror and camera visibility, alternate mirror appearance. The same as
  Uber's Mirror Behavior, with cameras added.
- **Parser today:** none.

### Proximity Color, Depth FX, Depth Bulge
- **Proximity Color:** colour changes as the camera gets close. A modulator with source
  *camera distance*. *Parser today:* reads most.
- **Depth FX:** glow where the surface touches other geometry (intersection highlights). Needs
  a depth texture. A rim layer with edge source *intersection*. *Parser today:* none.
- **Depth Bulge:** pushes the surface toward the camera where it touches other objects. A
  geometry effect. *Parser today:* none.

### Stats Overlay
- **Controls:** a font; text effects; FPS, position, instance time, clock and numeric
  displays, each with colour and transform.
- **Unified:** an informational overlay; out of the look. Report as unsupported. *Parser
  today:* none.

---

## Audio Link

- **Global:** an animation toggle, delay, CC strip, smoothing, band overrides.
- **AL Spectrum:** an audio-spectrum decal: placement, spectrum shape, clipping, colours.
- **AL Volume Color:** colour along a UV direction driven by volume.
- **Per-feature AudioLink** appears inside nearly every section: decals, matcaps, rims,
  emissions, dissolve, flipbooks, outlines, glitter, vertex options and more.
- **Unified:** the general **audio modulator** from the Uber draft, plus two layers: the
  spectrum as a procedural pattern, and volume colour as a modulated colour layer. Global
  smoothing and delay are modulator settings.
- **Parser today:** reads the global controls only.

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
- **Unified:** the geometry deformation feature from the Uber draft. Vertex colour use
  belongs to the base colour and alpha.
- **Parser today:** none, including vertex colours.

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
- **Parser today:** reads the textures and some bindings; the rest is to do.

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
- **Parser today:** none, apart from the per-texture UV selection.

## Post Processing

- **Controls:** final colour hue, saturation, brightness, posterisation; PP animations.
- **Unified:** colour adjustment with target *final colour*, as in the Mochie drafts, plus
  posterisation.
- **Parser today:** none.

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
- **Parser today:** reads cull and a few options.

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
  material. See the open question about settings that can't be merged in the
  [Uber draft](UNIFIED_MATERIAL_MOCHIE_UBER.md#open-questions).
- **Parser today:** the Lil Fur shader names go to the Poiyomi parser, which reads the shared
  features. Nothing fur-specific is read.

## Out of scope

- **Poiyomi Toon World:** a world variant.
- **Beatsaber support:** a third-party game, not VRChat.
- **Raymarching:** an empty category in this version.
- **Debug** views, the shader UI warnings, and the optimiser's own properties.

---

## Cross-shader notes

What Poiyomi adds to, or confirms in, the earlier drafts:
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

## Parser fixes and work

1. **Section toggles:** verify every section is gated by its toggle; the coverage script found
   several sections where the toggle isn't read.
2. **Lighting type:** move from per-type modes to the four families plus parameters.
3. **Unread sections:** RGBA colour masking, back face, skin, outlines, dissolve, flipbooks,
   UV tile discard, vertex options, mirror visibility, alpha modifiers, anisotropics,
   stylised reflections, depth rim, UV settings and distortion.
4. **Raw values to type:** much of the advanced parser's output, as the guidelines ask.
5. **Locked materials:** Thry-locked (optimised) materials are compiled shaders with their
   values baked in. The parser must detect them and read the original properties. The
   versions installed here keep the properties, but this needs checking.

## Open questions

1. **UV tile discard versus atlasing:** the same issue as Toon Standard's UV-based AudioLink.
   UV-tile discard is common on avatars for clothing toggles. Proposal: never remap a UV
   channel that a live feature reads by tile. If that channel is UV0, the one being atlased,
   either report the feature as unsupported, or keep each tile's region at its integer
   offset in the atlas. The latter is much harder. Which way?
2. **Procedural patterns:** one unified *procedural pattern* layer with a pattern kind (glitter,
   voronoi, truchet, pathing, internal parallax, video screen, audio spectrum), or leave the
   rarer ones reported as unsupported until a writer needs them?
3. **Stats Overlay:** treat as out of scope, like world-only features?
4. **Poiyomi Pro:** confirm it stays unsupported unless you can provide the source.
5. **Older Poiyomi versions** (7.3–9.3): the parser has version detection. Keep supporting
   them, or limit support to 10.x and report older versions?
6. **Fur length when atlasing:** bake the length ratio into the length mask and use the longest
   length as the constant, as proposed under *Lil Fur*?
