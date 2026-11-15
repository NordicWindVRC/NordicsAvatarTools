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
