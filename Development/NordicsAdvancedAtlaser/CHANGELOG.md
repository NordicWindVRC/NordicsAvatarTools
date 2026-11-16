# Changelog — Nordic's Advanced Atlaser

Newest first. Versions follow [SemVer](https://semver.org). Early versions were numbered as
"Revision N", which corresponds to version `0.N.0`. Not released yet.

## 0.40.0 — unreleased

EditMode tests pass in Unity 2022.3.22 (2026-11-03).

**Unified material rebuild in progress** (`UNIFIED_MATERIAL_REFACTOR.md`). Until each parser is
rebuilt, its materials are read by the fallback parser. The parser entries
under *Added* describe the parsers before the rebuild.

### Unified material rebuild
- **The unified material is rebuilt** from the feature catalogue, in `Editor/Unified/`. So far
  it has the building blocks (texture reference, scalar, colour, mask, blend, placement), the
  surface (base colour, alpha, primary normal, surface response), the lighting family, the
  surface layer, fur and render settings. Colours are linear. More features are added as the
  parsers that need them are rebuilt.
- Report lines have a kind (unmapped, unsupported, ignored, note), at most one per kind and
  feature per material. Unmapped and unsupported lines are build warnings.
- The free-form semantic features (`AtlasSemanticFeature`) and the parsers' model-writing
  helpers are removed. Parsers read through `AtlasMaterialReader` and the small helpers in
  `AtlasSourceMapping`; `AtlasMaterialReader.ColorLinear` reads colours in linear.
- Every input parser except the fallback is removed, to be rebuilt one at a time. The
  fallback reads only the base colour for now.
- Auto setup sizes the atlas by the base colour texture; the unused `SetTextureForSemantic`
  is removed.
- Tests: parser tests are removed with the parsers; new tests cover the building blocks,
  report lines, auto setup sizing and the fallback's linear base colour.
- **VRChat Mobile Toon Lit rebuilt:** base colour texture times vertex colour, the *flat*
  lighting family with the ambient average, opaque. The unified material gains vertex colour
  (multiply colour or alpha, sRGB data) and the shared lighting parameters (ambient model,
  received shadows, light contributions). Tests use a shared saved-value helper,
  `AtlasTestMaterials`, so they don't need the shader installed.
- **Unity Standard rebuilt** (metallic and specular setup), following the shader's keywords as
  the shader does:
  - rendering modes named by look: opaque, cutout, fade (everything fades), transparent
    (glass: reflections stay);
  - every main map uses the albedo's tiling and offset (was each map's own);
  - a metallic or specular map replaces the slider or colour (a leftover slider used to scale
    the map); the metallic slider is converted from gamma to linear;
  - with smoothness in the albedo's alpha, transparency comes from the colour's alpha alone;
  - normal map, parallax, emission and detail apply only with their keyword, as in the
    shader; the detail normal also needs the normal-map keyword;
  - emission and detail are surface layers (detail: *multiply ×2* colour and a normal layer
    sharing the mask);
  - physically based lighting with Disney diffuse.

  The unified material gains parallax, the *base colour constant alpha* source, the diffuse
  model, and surface modes fade and transparent.
- **VRChat Mobile Standard Lite rebuilt:** every main map uses the albedo's tiling (was each
  map's own); smoothness is the map's alpha × the Smoothness slider (was a property the shader
  doesn't have); the metallic slider is converted from gamma to linear; vertex colour multiply
  is recorded (off with `DISABLE_VERTEX_COLORING`); emission, detail, specular anti-aliasing,
  highlights and reflections follow their keywords; physically based lighting with Lambert
  diffuse, no received shadows. Lightmap type and bicubic sampling are no longer read: they
  only affect lightmaps. The unified material gains specular anti-aliasing.
- **Fallback parser rebuilt:** common property names with Unity's conventions (base colour,
  alpha mask, normal, metallic or specular colour, roughness or smoothness, occlusion,
  emission, culling, depth write, surface mode from the blend factors). A smoothness map is
  now inverted into roughness (it used to be read as roughness). One warning per material says
  the result is a guess; a matcap texture is reported, not mapped. First fallback tests.
- **VRChat Mobile Toon Standard rebuilt** (and its Outline variant). Every section follows its
  keyword, so a section that's off has no effect (before, every material got a rim and
  AudioLink, and disabled normal maps, specular, detail and colour masks were applied):
  - ramp toon lighting with the ramp preset recognised, shadow lift and colour, minimum and
    limited brightness, flat ambient, no received shadows;
  - specular with reflectance as the unified 0–1 control (0.5 = 4%), scaled by the gloss map;
  - matcap as a lit lookup layer; rim as an angle layer; detail by mode; colour mask regions
    as recolour layers; hue shifts as colour adjustments in turns; the outline;
  - AudioLink as an emission layer scaled by an audio modulator, with the mask channel fixed
    (it was off by one). Its rim mask and UV-based mode are reported.

  The unified material gains ramp toon, the lookup and angle layers, colour adjustments,
  modulators with the audio source, outlines, highlight sharpness, occlusion amounts,
  two-sided lighting and limit and minimum brightness.
- **Mochie Uber rebuilt, part 1** (refactor step 7a), following the shader's keywords:
  render and alpha (with dithered as its own surface mode), base colour, the three workflows,
  detail layers, separate or packed masks, physically based or ramp toon lighting (Manual Blend
  shadows as ramp keys), reflections and highlights, matcaps, rims, subsurface, two emission
  layers with pulse and light reactivity as modulators, filtering as colour adjustments, light
  volumes. **Shading off** maps to flat lighting clamped to the base colour (it was read as
  unlit). The remaining sections are reported until part 2; Uber X is unsupported.

  The unified material gains: highlights and reflections as groups (strength, tint, mask,
  roughness override, fresnel, cubemaps), the reflection source *ambient colour* (Unity
  Standard and Standard Lite now use it for reflections off, instead of turning reflections
  off), ramp keys, ramp strength and term mask, light volumes, the subsurface layer, time and
  light-level modulators, more colour operations and the *surface colour* target, and more
  lookup and angle layer options.
- **Mochie Uber, part 2a** (refactor step 7b-1): cube mode, mirror behaviour, hiding, near
  clipping, vertex manipulation, the Outline variant's outline, hard edges, clear coat and the
  static light direction are mapped. The unified material gains deformations, mirror
  visibility and a mirror base colour, near clip, invisible, the lookup projection *view
  direction* with rotation and spin, flat shading, specular from the mesh normal, a fixed
  light direction, and more outline options.
- **Mochie Uber, part 2b** (refactor step 7b-2): base colour dissolve, flipbooks, refraction,
  iridescence, UV distortion, matcap normal maps, multi-step parallax and map filters are
  mapped. The unified material gains the transition and refraction layers, decal placement,
  frames and shared UV distortion on texture references, scalar remaps, the occlusion tint,
  multi-step parallax, lookup normal maps and the angle profiles hue rotation and ramp.
- **Mochie Uber, part 2c** (refactor step 7b-3, completing Uber): AudioLink as audio
  modulators on emission, rim strength and width, dissolve, outline width and vertex
  manipulation; the oscilloscope as a procedural pattern layer; invert, team colours, mono
  tint, HDR and ACES as colour adjustments; stepped, anisotropic and combined highlights and
  their options; stepped and light-based reflections; shadow conditions and dithering;
  stencil; the colour alpha on the alpha mask; partial Disney diffuse and light-probe strength.
  Four details stay reported; Uber X is unsupported.
- **Poiyomi Toon 10 rebuild started** (refactor step 8a-1): a new `AtlasPoiyomiParser`, split by
  inspector section, reads textures with Poiyomi's UV choice, panning, stochastic sampling and UV
  channel settings; the main colour and vertex colouring; the alpha options; the normal
  sections; and rendering. The rest reports one line until its part. The unified material
  gains alpha mask blends, offset and fade modifiers, sharpened coverage and dithering, the bent
  normal and normal correction, pixel sampling, the surface modes additive, soft additive,
  multiplicative and ×2, depth clip, stencil masks, fog and reduced near clipping.
- **Poiyomi lighting types** (refactor step 8a-2): TextureRamp, Multilayer Math, Flat and
  Realistic. Ramp toon is now one formula with each source as a special case: the unified
  material gains the ramp offset, shadow tint, the shadow side's direct and ambient shares, the
  lit side's ambient, bands (colour, alpha, border, blur, normal strength, received shadow) with
  smooth edges, a border colour and environment strength, the physically based shading
  strength, and a brightness ceiling. Toon Standard uses the new parameters.
- **Poiyomi Light Data** (refactor step 8a-3): the light colour and direction modes, the light
  map as the shading term, AO, shadow and shadow mask maps, received shadows, brightness limits,
  forced light colour, additional lights and light volumes. The unified material gains light
  colour and direction models (the fixed direction now one of them, in world or object space),
  the shading terms Lambert, shadows only and relative brightness, received shadows as a
  strength with softness and cleaning, monochrome light, a luminance limit, minimum light, and
  additional light settings.
- **Poiyomi's other lighting types** (refactor step 8a-4): SDF, Wrapped, ShadeMap, Skin and
  Cloth, and the SDF light map; Realistic gains its shadow strength and tint. The unified
  material gains the wrapped and SDF face shading terms, a lit tint, bands from base and
  replacing the base, the pre-integrated skin diffuse, cloth sheen, and the physically based
  shadow strength and tint (replacing 8a-2's shading strength).
- **Poiyomi reflections and specular** (refactor step 8b-1): Reflections & Specular (off now
  means no highlights or reflections), Clear Coat and LTCGI. The unified material gains GGX
  anisotropy, a clear coat with its own highlights and reflections, a second highlight lobe,
  highlights ignoring shadows, reflection exposure occlusion, and LTCGI settings.
- **Poiyomi surface layers** (refactor step 8b-2): detail texture, back face, four decals and
  four emissions. Layers gain a faces choice (both, front, back) and showing on outlines;
  surface layers gain replacing the emission below and an emission overriding the base colour,
  and "source is base colour" became "source times base colour". Decal placement gains spin,
  symmetry, mirroring and depth.
- **Poiyomi RGBA colour masking** (refactor step 8b-3): per channel a colour layer, a normal
  layer, and metallic and smoothness layers. Masks can read a vertex colour channel; surface
  layers gain a source alpha offset; targets gain an inverted channel and a replacing normal.
- **Poiyomi matcaps and cubemap** (refactor step 8b-4): four matcaps (a layer per blend weight)
  and the cubemap as lookup layers. Lookup layers gain the view corrected, reflection planar,
  gradient, light half vector, reflection, world normal and object normal projections, a 2D
  rotation, UV blend, mirror flip, surface normal amount, base and light colour mix, a
  reflection-probe fallback and a circle mask; every layer gains hide in shadow.
- **Poiyomi rims** (refactor step 8b-5): both rims in the Poiyomi, UTS2 and lilToon styles, as
  angle layers. Angle layers gain the step profile (facing or edge, linear angle, power, low and
  high, falling), light colour mix, source alpha as opacity, surface normal amount and per eye.
- **Poiyomi depth rim, environmental rim, backlight, subsurface, rim light direction**
  (refactor step 8b-6). Angle layers gain the depth and backlight edge sources, a light-side
  response (mask, scale or subtract by half-Lambert, before or after the step, shrink in
  shadow), smooth steps and the environment along the normal; a rim's dark-side colour is a
  second layer. Subsurface layers gain a thickness channel and offset, and a direct-light mode
  with light colour mix and shadow strength.
- **Highlight layers** (refactor step 8b-7): a new layer type for stand-alone highlights, kinds
  strand (Kajiya-Kay hair) and toon (UnityChan style). Poiyomi's Anisotropics and Stylized
  Reflections map to it; lilToon-mode stylized reflections are reported.
- **Poiyomi colour adjustment** (refactor step 8b-8): Color Adjust, Post Processing and every
  layer's hue shift and colour adjust. Colour adjustments gain chromatize, power, clamp,
  gradient map, hue tint, posterize and lightness, a texture input and saturation weights.
- **Mask bus** (refactor step 8c-1): the unified material gains a 16-channel mask bus with
  ordered producer steps (texture, vertex colour, faces, mirror, camera, distance, offset,
  remap, force, layer coverage); masks and scalar inputs can read a channel with a blend; alpha
  modifiers gain a mask. Poiyomi's global masks map to it, and its theme colours are resolved
  to colours.
- **Poiyomi time and light modulators** (refactor step 8c-2): hue speeds, emission blinking,
  scrolling and light-based glow, and blacklight masking. Modulators gain the sweep source, a
  pulse waveform with duty, a time offset, and a linear and direct-only light level; the mask
  bus gains blacklight steps.
- **Poiyomi AudioLink** (refactor step 8c-3): emissions, decals, matcaps, rims, alpha and the
  colour adjust hue, with the section's smoothing; ColorChord and AudioLink theme colours.
  Modulators gain *blend to colour* with a colour and a curve; the audio source gains idle
  without AudioLink, a threshold and the chrono spread; colour inputs gain an AudioLink theme.
- **Poiyomi proximity colour, fluorescence, centre-out emission** (refactor step 8c-4).
  Modulators gain the camera distance source; colour adjustments gain the emission target and
  fluorescence; texture references gain the facing gradient spaces.
- **Poiyomi outlines** (refactor step 8d-1). Outlines gain the light facing, view directional
  and drop shadow shapes, object-space width, vertex colour normals and width mask, fixed size
  over distance, a base colour multiply, light colour mix, shadow strength, emission, an inner
  highlight, alpha override and modifiers, and blend factors.
- **Poiyomi UV options** (refactor step 8d-2): distorted UVs, UV channel rotation, the
  stochastic modes and parallax heightmapping. UV distortion gains a second map and a constant
  offset; a texture's rotation is specified as turning before its tiling.
- **Poiyomi dissolve** (refactor step 8d-3) as a transition layer. Transitions gain point to
  point, sphere and centre-out sources, softness and padding, detail noise, wave mode, alpha
  only with a curve, revealed emission, and the edge's flip, colour replacement and emission;
  the mask bus's layer coverage can be inverted.
- **Poiyomi flipbooks, mirror/camera visibility and UV tile discard** (refactor step 8d-4).
  Frames gain an offset, a range and crossfade; surface layers gain mirror visibility,
  intensity as alpha and the map as a colour mask; the render's mirror visibility becomes
  per-view visibility (normal, mirrors and cameras in VR and desktop, screenshots), and hidden
  UV tiles are added.
- **Poiyomi glitter** (refactor step 8d-5) as a pattern layer of the new glitter kind, with its
  three sparkle modes, shapes, shape texture and sprite sheet, colours, rotations, masking and
  AudioLink.
- **Poiyomi pathing, voronoi and truchet** (refactor step 8d-6) as pattern layers of three new
  kinds: four-channel paths (fill, path, loop, dashed), voronoi cells (2D, 3D, 3D difference)
  and truchet arcs, with their masks and AudioLink.
- **Poiyomi internal parallax, depth FX and AL volume colour** (refactor step 8d-7) as pattern
  layers of three new kinds; the stats overlay is reported.
- **Poiyomi video effects and AL spectrum** (refactor step 8d-8) as pattern layers of two new
  kinds: a video screen (six screen types) and an audio spectrum that can draw through it.
- **Poiyomi vertex options** (refactor step 8d-9): Basics & Fun as deformations. Deformations
  gain scale, axis rotation with spin, world translation, displacement, barrel, sphere, tornado
  and wind, and local, centred snapping; fur's wind becomes the shared wind with turbulence.
- **Poiyomi 11's remaining settings** (refactor step 9g-4f): the Skin section as a skin tone
  pattern kind; light, emission and final colour multipliers; additional lights combining by
  max (Poiyomi's default, Toon 10 included); the network clock; Early Z; the stochastic
  density; the flat light map. The catch-all "not mapped yet" reports for 11 and Pro are gone.
- **Poiyomi Pro's lava lamp** (refactor step 9g-4e): a pattern kind with its container,
  blobs, shading, reservoirs, haze and AudioLink.
- **Poiyomi Pro's geometry variants** (refactor step 9g-4d): tessellation as a geometry
  effect; Geometric Dissolve, ShatterWave and the particles reported.
- **Poiyomi Pro's Triple Outline and Triplanar Projection** (refactor step 9g-4c): outline
  layers 2 and 3 as further outlines; triplanar projection as a material-level mapping setting.
- **Poiyomi's Grab Pass and Self Grab** (refraction layer rewritten, refactor step 9g-4b): the
  refraction layer gains the screen-grab effects (blur patterns, pixelation, colour, a colour
  ramp, per-effect masks, blend factors and self grab); Uber's opacity becomes its blend amount.
- **Poiyomi's Two Pass and fur variants** (refactor step 9g-4a): a second pass in the render
  settings (with mask bus steps that tell the passes apart); Pro Fur's shells; Lil Fur 11's
  mask options, contact, vertex colour modes and cutout length.
- **Poiyomi Pro's constellation** (refactor step 9g-3): a pattern kind with its stars, lines,
  layers, random colours and AudioLink.
- **Poiyomi Pro's TPS, squish and outline bake** (refactor step 9g-2): TPS and squish
  deformations (squish's colour as a pattern layer sharing its settings) and baked outline
  curvature data.
- **Poiyomi Pro's screen-space effects and tone mapping** (refactor step 9g-1): ambient
  occlusion and contact shadows as pattern kinds; GT7 tone mapping and neutral emission as
  colour operations (with per-operation settings).
- **Poiyomi 11's AL spectrum** (refactor step 9f-3): the audio spectrum gains flips, an arc,
  three display styles, lit steps, frequency mapping, bar sampling, response rows,
  compression, a gradient colour mode, mask coordinates, master opacity and emission controls.
- **Poiyomi 11's pathing, voronoi and internal parallax** (refactor step 9f-2): path lines gain
  direction channels, ping-pong motion, easing, head and tail softness, packed masks, overlap
  modes and anti-aliasing; voronoi gains cell motion and AudioLink cells; internal parallax
  gains mip maps, smooth layers, alpha, refraction and depth fog.
- **Poiyomi 11's small effect additions** (refactor step 9f-1): a material-wide screen space
  layout; surface layers can go through a video screen and fade their emission by alpha;
  normal correction gets a mask; final colour operations can include the emission; glitter's
  angle offset; dissolve's revealed colour adjust; flipbook depth and mirror flip.
- **Poiyomi 11's outlines** (refactor step 9e): outlines gain per-property masks, colour
  adjustments (also mapped for Poiyomi 10 now), a bus mask, edge fade, smooth thin outlines,
  light facing side widths, an emission colour and camera depth writing; a new HSV offset
  colour operation.
- **Poiyomi 11's rims, backlights and contacts** (refactor step 9d): angle layers gain
  anti-aliasing, *times alpha* and a direct-light response; the lilToon rim's application stage
  and main texture UV; a second backlight; VRChat contacts as a mask bus step; the mask bus's
  texture step takes a full mask with levels.
- **Poiyomi 11's colour, alpha and decals** (refactor step 9c): a black and white colour
  operation; MSDF textures; decal smooth borders, mirror flips and sprite sheets with area and
  inset; vertex colour hue; dither cutout and dithered shadows; RGBA colour masking's AO, alpha
  and per-region PBR masks.
- **Poiyomi 11's lighting** (refactor step 9b): the PBR lighting type as the physically based
  family's new *calibrated* model; the surface response gains sheen, glints, multi-bounce
  occlusion and a rotated, multiplied anisotropy; limits that keep the hue, SDF mask channels,
  back face shadow, a light volume specular light count, strand even width and environmental
  rim exposure occlusion.
- **Poiyomi Toon 11, Pro 10 and Pro 11 parsers** (refactor step 9a) on Toon 10's parser. Masks
  gain levels (input and output range, midpoint, invert) and a vertex colour multiplier, read
  from Poiyomi 11's mask options.
- **Poiyomi Lil Fur** (refactor step 8d-11) into the material's fur, with a fur mode and alpha to
  coverage; the Poiyomi 10 parser is complete and no longer reports unmapped sections.
- **Poiyomi look-at, glitching, depth bulge and view clip prevention** (refactor step 8d-10) as
  deformations: a look-at chain of rigs with its own visibility, glitch, depth bulge and camera
  push.

### Changed
- Package and NDMF name `app.nordicwind.advanced-atlaser`. Requires Texture Wrapper 0.10.0 or
  later, for the renamed runtime assembly and the normal correction safety rules.
- The NDMF plugin class is renamed `NordicsAdvancedAtlaserPlugin`. The AAO integration define
  is renamed `NORDIC_ADVANCED_ATLASER_HAS_AAO`. Tests use namespace
  `NordicWind.AdvancedAtlaser.Tests`. MIT license.
- Build errors start with `[Nordic's Advanced Atlaser]`.

### Changed
- **Geometry baking resolution is automatic.** Each material is baked at 1.5× its area in its
  atlas, up to 4096 per side; a larger area is baked 1:1. The *Geometry Baking Resolution*
  setting is removed.
- The main output texture is always generated: no on/off switch, default 4096 × 4096.
- Region padding defaults to 4 px and is only used with two or more source materials.
- The Geometry Baking section explains what it does. *Excluded Blend Shapes* moved into it,
  with a description, and only shows when baking is on. Its separate on/off switch is removed:
  an empty list excludes nothing.
- The output shaders' placeholder settings are removed. Each output shader gets its own
  settings as its writer is built.
- **Serialized fields are camelCase**, as the design guidelines require. Existing Atlaser
  configurations are not migrated.
- **The bake releases its maps as soon as it's done.** Only the maps the bake needs exist at
  the same time (6 instead of 8: about 1.7 GB instead of 2.3 GB at 4096²), and each material
  keeps only its usage mask and correction afterwards (2 maps instead of 4). The frame-normal
  map serves as the bake's coverage area, and the usage mask is painted after the bake. Results
  are unchanged. `AtlasSubmeshBake` no longer has `OriginalWorldNormals` or
  `CurrentWorldNormals`; debug exports still write them.

### Added
- **Source shaders don't need to be installed.** When a material's shader is missing, the
  parsers read the values the material saved, through `AtlasMaterialReader` (new
  `AtlasSavedProperties`). Parsers no longer call the material or shader API directly.
  Poiyomi (family, Pro, version 7–11) and Mochie Uber are recognised from saved property
  names. Option names that only the shader declares are reported, not guessed. Such materials
  get an info note.
- **Output shaders must be installed.** `IAtlasOutputShader.FindShader()` finds the Unity
  shader an output uses. The output picker lists only installed ones, the validator warns
  about an output whose shader isn't installed, and the build skips it with one diagnostic.
  Poiyomi Toon 10 is found under its usual name or under *Old Versions*.
- Tests for both: saved-value parsing matches shader parsing, Poiyomi Pro and Mochie Uber
  detection from saved values alone, version and Pro markers, and the installed-output list.
- **Poiyomi 11 and Poiyomi Pro.** The version detector recognises 11, which keeps 10's shader
  names and is told apart by its version label, and the Pro variant. New parsers:
  `poiyomi-toon-11`, `poiyomi-pro-11`, `poiyomi-pro-10` and a generic `poiyomi-pro`, all on the
  shared Poiyomi base.
  - 11's **PBR lighting type** maps to physically based lighting, with metallic, smoothness,
    occlusion and reflectance from its packed texture. Clear coat, anisotropy, sheen and glints
    are kept as raw values.
  - **Pro Fur** (shell fur) fills the fur definition, which gains a method (strands or shells),
    fur colour, tip taper, contact response and distance level of detail. Lil Fur's 11.x
    contact push is read too.
  - Pro-only effects (SSAO, contact shadows, TPS deformation, squish, constellation, GT7 tone
    mapping, neutral emission, outline bake) and Pro variant shaders (geometric dissolve,
    ShatterWave, particles, tessellation, triplanar projection, triple outline, grab pass) are
    reported once each.
  - 11's mask levels and vertex-colour mask channels are reported once when used.
  - Tests: detection from shader metadata, and parser tests that run when Pro is installed. The
    Poiyomi tint test now runs on 10 or 11.
- **Mochie Uber input parser** (`Mochie/Uber` and `Mochie/Uber (Outline)`), Mochie's avatar
  shader. It reads:
  - shading on or off; blending mode, cutoff and alpha mask; culling; vertex colour;
  - the four surface workflows (metallic, specular with a smoothness map, specular with
    smoothness in alpha, packed), occlusion and parallax;
  - toon shadows (a ramp texture, or a ramp described by numbers) and lighting contributions;
  - detail maps, both emission layers, both matcaps, basic and environment rims, subsurface
    scattering, filtering and team colours, light volumes;
  - per-feature masks in separate or packed mode.

  Features without a typed home yet (outline, refraction, iridescence, flipbooks, UV
  distortion, vertex manipulation, AudioLink, map filters, mirror behaviour, cubemap base
  colour) are reported once each when switched on. Uber X special features are reported as
  unsupported. Tests run when Uber is installed.
- **Fur:** `AtlasUnifiedMaterial.Fur`, a typed fur definition: length and length mask,
  direction (constant, map or vertex colours), gravity, randomness, strand pattern, alpha
  mask, root occlusion, layers, width, wind, a fur rim, and the fur pass's own render
  settings. The Poiyomi parser fills it for the *Lil Fur* variants. Tests run when Poiyomi is
  installed.
- **Normal Baker** (**Tools → NordicWind → Advanced Atlaser → Normal Baker**): a dockable window
  that bakes the shading of an original mesh onto a current, for example lower-detail, mesh and
  saves it as a PNG normal map, for a source's *Pre-baked Normal Map*. Slots accept mesh assets
  and scene objects, with a submesh dropdown each; sizes up to 4096. It refuses to save when the
  two UV layouts evidently don't match, and warns when the meshes face different ways. It uses
  the build's bake (`AtlasSubmeshBaker.BakeCorrection`, now shared). Tests cover the slots,
  identical meshes, a lower-detail mesh, mismatched UVs and transforms.
- An end-to-end test through `AtlasSubmeshBaker.Bake`: a curved original and a flat reduced
  mesh, checked texel by texel against the original shading.
- **Pre-baked Normal Map** per source material: an optional, author-supplied normal map for the
  mesh, applied at strength 1 beneath the build's normal correction. The inspector warns when
  the texture isn't imported as a normal map. Output writers get it with the documented
  combining order.
- `AtlasOutputLayout`: the bake size and effective padding rules, with tests.
- **Normal Correction per source material**, on by default. Turned off, the bake keeps only
  the fast UV usage mask and a flat correction.
- **Skips the normal-correction bake when nothing changed.** When every surviving triangle is
  an original triangle with unchanged corners (removed submeshes or triangles only), the
  correction would be flat, so only the fast usage mask is made. The check compares triangles,
  not just vertices, and runs in time proportional to the mesh size.
- `AtlasSubmeshBake.CorrectionIsFlat`, so output writers can skip a flat correction.
- Tests for the unchanged-geometry check: removed submeshes and triangles across two merged
  renderers, rounding noise, new triangles from existing vertices, moved vertices, recalculated
  normals, and missing data.
- **Normal correction never makes shading worse.** The bake paints a coverage map per UV
  winding and uses the Texture Wrapper's safety rules: overlapping UVs (mirrored halves, stacked
  copies) get no correction, and corrections above 60° fade out. One summary line per material
  says how much was left uncorrected. One warning per material only when at least 1% of the
  correction hit the 60° limit, which points to a mismatch between the meshes.
- `AtlasColorProcessingDefinition.TintMode`: *Multiply* or *Perceptual* (keeps lightness,
  takes the tint's hue, scales chroma by the tint's saturation, mixes in by its alpha).
  Poiyomi 10's tint is perceptual.

### Removed
- **The Mochie Standard input parser.** Mochie Standard is mainly a world shader; Mochie's
  avatar shader, Uber, replaces it. Mochie Standard materials now go to the fallback parser.

### Fixed
- With *Export Intermediate Maps* on, the tangent-frame maps were exported (which normalizes
  them) before the normal correction bake, so debug builds baked slightly different
  corrections. They're now exported after the bake.
- Poiyomi saturation was copied unconverted, so an untouched material (saturation 0) read as
  fully grey. It is now `1 + value`, the unified scale.
- Poiyomi brightness is mapped per version: additive for 7.x and 8.x, a multiplier of
  `1 + value` for 9.x and later. 7.x to 9.x always clamp after brightness, so `ClampOutput` is
  set for them.
- Poiyomi saturation was read twice, by two parsers. Only the version-aware one is left.
- Global mask textures were reported as ordinary masks and missing from the global masks.
- `AtlasGlobalMaskResource.MappingFor()` returned null for a resource without a texture.

## Pre-release history

| Version | Summary |
|---|---|
| 0.39.0 | Blend-shape geometry exclusion with a threshold and *Any* / *All Vertices* mode per entry. Normal corrections use full dilation. |
| 0.38.0 | Nested AAO Merge Skinned Mesh sources are discovered. |
| 0.37.0 | Fixed a hang at 4096² caused by a per-pixel loop after dilation. |
| 0.35.0 | Configurable normal-correction padding (later replaced by full dilation). |
| 0.34.0 | Optional exclusion of geometry moved by selected blend shapes. |
| 0.33.0 | Pose-aware geometry capture, without requiring AAO. |
| 0.32.0 | Geometry correction in avatar-root space. Requires Texture Wrapper 0.8.3. |
| 0.30.0 | Debug menu: verbose logging, intermediate map export and build summary. |
| 0.29.0 | Material-centric geometry baking across all original renderers. |
| 0.28.0 | Original geometry captured early in the build. Capture diagnostics. |
| 0.24.0–0.26.0 | Unified material model: input parsers for Standard, VRChat Mobile, Mochie and Poiyomi 7–10, global masks, and Poiyomi inspector mappings. |
