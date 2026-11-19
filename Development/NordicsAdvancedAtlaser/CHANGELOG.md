# Changelog — Nordic's Advanced Atlaser

Newest first. Versions follow [SemVer](https://semver.org). Early versions were numbered as
"Revision N", which corresponds to version `0.N.0`. Not released yet.

## 0.40.0 — unreleased

EditMode tests pass in Unity 2022.3.22.

### Added
- **Rim lighting:** a *Rim Lighting* feature puts every source's view-angle rims into the
  output's rim slots: Poiyomi's two (each written in the style that holds it: Poiyomi, UTS2
  with its antipodean rim, or lilToon with its indirect rim; other rims fitted to the Poiyomi
  style) and Toon Standard's one band rim (`USE_RIMLIGHT`; other rims fitted to the band by
  `AtlasRimCurve.FitBand`). Rims are placed like matcaps, with *As matcap*: a rim that
  depends only on the view angle drawn into a generated matcap in a free matcap slot. On Toon
  Standard, whose rim has no mask, *Automatic* draws a partial rim into the free matcap slot,
  otherwise shows it everywhere and lists it.
- **Environmental rim and backlight** (Poiyomi): one setup each from the reference source
  (*Environmental Rim from*, *Backlight from*); the environmental rim's strength per part in
  an allocated mask, the backlight's color per part in a *Backlight color* texture.
- Features the output shader doesn't offer are listed in the build log with their sources.
- **Matcaps:** a *Matcaps* feature puts every source's matcaps (lookup layers with a 2D
  texture) into the output's matcap slots: Poiyomi's four (texture, color and intensity,
  lookup, mask, every blend weight, emission) and Toon Standard's one (`USE_MATCAP`, add or
  multiply, strength, mask). Matcaps that look the same in several sources share a slot, each
  source's mask saying where it shows. With more than one matcap, the inspector lists them,
  each placed *Automatic*, *Own slot*, *Off* or *Combine with* another (stored in the
  feature's values; `IAtlasInstanceFeature`). A combined slot gets a generated 512×512
  texture (`AtlasGeneratedTextures`): matcaps on the same parts added or multiplied by
  weight, those on different parts averaged by area. Blends the output lacks are
  approximated with add or multiply; on an output without base color mix (Toon Standard),
  the base color's luminance goes into the slot's mask. Combined, approximated and left-out
  matcaps are listed.
- **Mask allocator:** `AtlasMaskAllocator` gives each mask a feature needs over the atlas a
  place: none for a constant, a shared channel for equal masks, the same channel read
  inverted for an inverse, otherwise the next channel of the output's *Masks 1* and *Masks 2*
  textures (Poiyomi; Toon Standard has *Masks 1*), which start at half the main texture's
  size (`AtlasOutputTexture.MainDivisor`).
- `AtlasSettingValue.text`, for stored values that name something.
- **Lighting floors:** each writer sets its one floor from the unified floors, so an atlas
  stays visible in dark worlds whatever the source shader: Toon Standard's Min Brightness from
  the largest of the unified minimum brightness, minimum light and the floored light color's
  ambient floor (capped at its 0.1), Poiyomi's Min Brightness from the larger of the minimum
  light and minimum brightness (`AtlasLightingFeature.Floor`).
- **Lighting drawn on the texture:** *Shadow Colors* bakes every source's band and shade
  textures into up to three shadow color textures, and *Skin Thickness* every source's skin
  thickness into its own, which the Poiyomi writer sets (`_ShadowColorTex` and its 2nd and 3rd,
  `_1st_ShadeMap`, `_2nd_ShadeMap`, `_SkinThicknessMap`). An SDF face (the SDF type, or the
  SDF light map) keeps the reference source's texture, read through every source's own UVs in
  a free UV channel, since it's sampled mirrored. Baked parts no longer count as lighting
  differently. Cloth maps are still listed.
- **Shadow masks:** a *Shadow Masks* feature bakes every source's shadow strength mask and
  detail shadow (`AtlasVirtualMap.ShadowStrengthMask`, `DetailShadow`) into a *Shadow and
  detail masks* texture, which the Poiyomi writer sets as Light Data's shadow masks (R) and
  detail shadows (G) by their channel strengths. For ramp toon lighting; outputs without them
  list them.
- **Lighting:** a *Lighting* feature: one lighting setup from the reference source, with
  *Lighting from* when the materials are lit differently. The Poiyomi writer writes its
  parser's reading back (Light Data, the texture ramp, multilayer math, wrapped, skin, shade
  map, flat and realistic types, light volumes, LTCGI and the add pass); the Toon Standard
  writer its ramp (the source's, one generated from banded shading such as Poiyomi's
  multilayer math and shade map (`AtlasRampTexture`), or the SDK ramp nearest the family),
  shadow boost, shadow tint (the first band's color for banded shading), minimum brightness
  and brightness limit. Lighting drawn on the texture is left out and
  listed. The written material is read back by its parser and the lighting settings it
  doesn't hold the same way are listed (`AtlasLightingFeature.Unkept`, `AtlasFieldwise`).
- **Hidden UV tiles:** parts hidden by Poiyomi's UV tile discard stay hidden. *Kept live*: the
  remap writes each source's UVs into a free UV channel (the plan's UV channel allocator,
  `AtlasOutputPlan.AllocateUVChannel`), moved by whole tiles only where the output would hide
  them differently, and the Poiyomi writer sets UV Tile Discard (vertex mode) and Face
  Discard on it. *Removed from the mesh*: the remap leaves out triangles with a vertex in a
  tile hidden on both sides (`AtlasRemapTarget.RemovedTiles`), on any output, and logs how
  many. Before, these parts showed after atlasing.
- **Vertex colors:** a *Vertex Colors* feature with three modes.
  - *Kept on the mesh*: the output multiplies its base color and alpha by the vertex colors
    with one setting, and the remap rewrites each source's vertex colors (`AtlasRemapTarget.VertexColor`)
    so it gives what the source's own did; white for sources that don't use them. When the
    sources disagree on sRGB decoding, *Vertex colors as sRGB* picks the output's.
  - *Baked into the textures*: painted in UV space (`AtlasVertexColors`, overlaps averaged)
    into the base color and alpha.
  - *Off*.
  - Masks and mask bus steps that read vertex colors are baked in the first two modes. Both
    writers set their vertex color options (Poiyomi's amount, alpha and linear colors; Toon
    Standard's `_VertexColor`). `AtlasUVTriangles` carries the meshes' vertex colors.
- **Mask baking:** masks are baked with their levels, and the mask bus (Poiyomi's global
  masks) is baked per source by `AtlasMaskBusEvaluator`: texture, offset, remap and force
  steps exactly; back face, mirror, camera, distance, blacklight and contact steps as the
  avatar is normally seen (front faces, outside mirrors and cameras, far away, nothing
  touching), which the build lists. Masks and scalar inputs (metallic, smoothness, occlusion)
  read their channel by Poiyomi's mask blend, the scalars before the smoothness is turned
  into roughness. Color layers, adjustments, alpha masks, normal layers, emission and the
  highlight and reflection masks gain it. Channels with vertex color, layer coverage or
  second-pass steps are still listed as not baked.
- **Conflicts decided by the user or reported:** where the output can't give every material
  what it had, the user decides or the build log says so.
  - *Alpha to coverage* (On or Off) under *Surface Mode*, shown only when the cutout
    materials disagree (`IAtlasConflictSwitchFeature`, stored in the feature's values).
    Before, one cutout material with it turned it on for all of them.
  - Base colors above 1 are kept when the materials' colors differ: the baked base color map
    is divided by its brightest value, which becomes the material's color. Before, they were
    cut off at 1.
  - The build log lists materials whose weaker highlights or reflections an output without
    strengths or masks (Toon Standard) can't keep, and that lighting isn't merged yet.
  - `AtlasMapRequest.Finish` runs on the whole baked map before packing.
- **Reflectance from:** a drop-down under *Reflection and Specular*, shown only when the
  materials need different reflectance on the output: *Automatic* (the material with the
  largest area) or a chosen material. It replaces the clipped *Reference* checkboxes in the
  material list. Outputs say how they set reflectance (`AtlasReflectanceSupport`): Poiyomi's
  is fixed, so it is never shown there. Toon Standard's reflectance is its gloss map ×
  `_Reflectance`, so each source's value is its reflectance over its smoothness, which keeps
  Toon Standard sources' reflectance unchanged; before, it was halved at the default settings.
- **Decals and emission:** decals placed by center, scale and rotation (clipped or not) are
  baked into the base color as Poiyomi places them. *Emission* bakes each source's static
  emission layers into one emission map, relative to the strongest source's emission, which
  both writers set (Poiyomi emission 0 with its keyword; Toon Standard's emission), strengths
  above the shaders' sliders going into the color. No *baked into color* mode: it wouldn't
  glow. Decals with symmetry, mirror sides, depth, spin or MSDF text, and live emission, are
  listed in the diagnostics.
- **Base color stack:** each source's color, its static color layers (tiled UV0 textures or
  colors, every blend including tint and add to luminance, masks, source alpha as opacity)
  and its static color adjustments (hue in HSV or Oklab, saturation, contrast, brightness,
  tint, power, clamp, lightness, chromatize; on the base color, the surface color and layer
  sources) are baked into the base color. The color stays shared when every source has it and
  nothing is layered on it, so colors above 1 keep working. Live layers, decals, final-color
  adjustments, the other operations and vertex colors are listed in the diagnostics.
- **Surface maps:** *Reflection and Specular* bakes each source's metallic and roughness, and
  its highlight and reflection strengths into masks relative to the strongest; *Ambient
  Occlusion* bakes each source's occlusion. Sources without them are matte and unoccluded.
  Poiyomi Toon 10 gets its packed map (metallic, smoothness, reflection and specular masks)
  and AO map, with their keywords; Toon Standard one texture of metallic, gloss and
  occlusion. Layout channels can be stored inverted (smoothness). Remapped or bus-masked
  inputs and the highlight and reflection settings beyond strength (tints, fresnel, steps,
  anisotropy) are listed in the diagnostics.
- **Normal maps:** each source's normals are combined into the output's normal map, in the
  established order (pre-baked map, the geometry bake's correction, the primary normal map,
  UV0 normal layers with their masks), by whiteout. The output's strength is the largest of
  the sources', and each source is baked at its share of it. A source with only a pre-baked
  map or a correction that bends the normal (its mesh was reduced) counts too, so the geometry
  bake's correction reaches the avatar; a flat correction adds nothing. Both
  writers set the normal map; the build generates it as a normal-map texture. Layers on
  other UV channels, placed, lerped or masked by the mask bus or vertex colors are listed in
  the diagnostics.
- **Alpha, surface mode and fallback:** each source's alpha is baked into the base color
  texture's alpha (its source, mask, offset and strength), converted to the output's surface
  mode: a cutout source's cutoff is moved to the shared cutoff 0.5, or becomes a hard edge
  under a blended mode; opaque sources stay solid. *Surface Mode* picks the most see-through
  mode among the sources that the output supports, or the one chosen; transparent sources
  among solid ones get a fade that writes depth (Poiyomi's TransClipping), so they stay
  see-through and the solid parts sort like opaque ones; where the output can't, they're
  drawn solid (no cutout or dither stand-ins); the Poiyomi Toon 10
  writer sets that mode's rendering preset (queue, render type, blending, depth write, cutoff)
  and the `VRCFallback` tag, which *Fallback Shader* can override. What isn't merged yet
  (fades, vertex alpha, dithering, some masks, additive and multiplicative blends) is listed
  in the build's diagnostics and the manual bake's log line.
- **Auto Setup** lays out the assigned sources' UV regions by their base color textures'
  sizes: in the smallest atlas that holds them at full size, or 4096 × 4096 with every region
  scaled down alike, in whole percent, as little as fits; then the free space goes to the
  scaled-down sources, each growing toward its full size as far as everything still fits.
  When free space is still left, it asks whether to stretch the regions into it, so the whole
  atlas is used, or keep the textures' proportions. The
  regions tile the atlas and are exact decimals, such as 0.8 and 0.2. It sets the main texture
  size to match and every feature to its default mode, asks before replacing edited regions,
  logs what it did, and never adds or removes sources. One Undo step.
- **Region padding is applied:** the map bake places each source's content inside its region,
  half the padding from each edge, with the content's edge filling the ring, and the meshes
  are remapped into the same rectangle. Before, the padding setting had no effect.
- **Output shader pick:** a new output component picks its output shader once, when it's
  created: the shader family most of the Atlaser's unused source materials use, at the
  highest installed version (Pro before Toon at the same version). A build skips an output
  without a shader, with an error.
- **Unified material.** Each source material is parsed into a shader-independent, typed
  description in `Editor/Unified/`, specified in `UNIFIED_MATERIAL_FEATURES.md`: building
  blocks (texture references, scalar and color inputs, masks and the mask bus, blends,
  placement, modulators, color adjustments), the surface, four lighting families, an ordered
  list of layers (surface, transition, lookup, angle, subsurface, procedural pattern,
  refraction, highlight), outlines, fur, deformations and geometry effects, and render
  settings including visibility and a second pass. Colors are linear. Values without a typed
  home become report lines (unmapped, unsupported, ignored or note), at most one per kind and
  feature per material, listed in the build only with *Verbose Logging*.
- **Output model** (`ATLAS_OUTPUT.md`): shared output features with modes, filtered by what
  the output shader can express (its capabilities and texture layout); the output is resolved
  into one merged unified material and the virtual maps it needs. The first feature is
  *Color*, the base color map. Output settings are stored by feature ID, so changing the
  output shader keeps every choice the new one supports. Nothing is baked or written yet.
- **Output inspector:** a *Textures* section with a table of the textures the chosen modes
  need, their sizes (a drop-down each for width and height, locked where the size is fixed), and GPU memory estimates for PC and Android with totals, following
  per-platform *GPU Compression Formats*; a *Material Features* section with one row per
  feature (its name and mode) that folds open to per-material switches. Sources are listed
  with their material's name. Sources are read through a cache, rebuilt only when a material, its
  shader, or an imported texture or shader changes.
- **Base color bake:** each source's base color texture, with its tiling, offset and rotation,
  placed in its region of the atlas map. Its UV islands, from the meshes that use it, grow over
  the parts of the texture they don't use, and the pixels between the regions take the nearest
  region's color. A source without a texture is white. Maps are baked one at a time, and
  sources one after another, to bound memory.
- **The build generates the outputs:** each output's textures, compressed for the platform
  being built (PC or Android formats, crunch where it saves 10%), and its material, saved with
  NDMF. A **mesh remapper** then gives each renderer a new mesh: vertices shared with another
  region or material are split with all their data (UVs, colors, bone weights, blend shape
  deltas), UV0 is placed in the source's region (a triangle in another UV tile is moved back by
  whole tiles; only one straddling a tile edge is clamped), the source materials become the
  output material, and submeshes of one output are merged. Everything is made before anything
  changes, and the replaced meshes are registered with NDMF.
- **Bake to Assets** on each output: its textures (`<material>_<texture>.png`) and material,
  written where you choose; the save dialog asks before replacing a material already there,
  and textures of the same names are overwritten. Writers for Poiyomi Toon 10 and VRChat Mobile
  Toon Standard set the base color texture. Mipmaps are made with the Kaiser filter and
  stream, in the build and in the manual bake. Textures get the output's formats for PC and
  Android, no alpha channel unless it's used, and crunch (on by default) only where it makes a
  BC1 or BC3 texture at least 10% smaller.
- **Input parsers** on the unified material (details in the `UNIFIED_MATERIAL_*.md`
  inventories):
  - VRChat Mobile Toon Lit, Standard Lite and Toon Standard (and Outline);
  - Unity Standard (metallic and specular setup);
  - Mochie Uber (and Outline);
  - Poiyomi Toon 10 and 11 with their avatar variants (Two Pass, Early Outline, Grab Pass,
    Lil Fur), and Poiyomi Pro 10 and 11 with theirs (Pro Fur, Triple Outline, Self Grab,
    Triplanar Projection, Tessellated and the geometry variants);
  - Poiyomi Toon 7.3, 8.x and 9.x, and Poiyomi Pro 9.x, read on the Poiyomi 10 parser with
    version tables. Poiyomi Pro before 9 is read as Toon of its version, without its Pro
    features;
  - a fallback for any other shader, from common property names.
- **Source shaders don't need to be installed.** When a material's shader is missing, the
  parsers read the values the material saved, through `AtlasMaterialReader` (new
  `AtlasSavedProperties`); parsing reads materials only through it. Poiyomi (family, Pro,
  version 7–11) and Mochie Uber are recognized from saved property names. Option names that
  only the shader declares are reported, not guessed. Such materials get an info note.
- **Output shaders must be installed.** `IAtlasOutputShader.FindShader()` finds the Unity
  shader an output uses. The output picker lists only installed ones, the validator warns
  about an output whose shader isn't installed, and the build skips it with one diagnostic.
  Poiyomi Toon 10 is found under its usual name or under *Old Versions*.
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

### Changed
- **Reflection and specular masks:** a reflection or highlight strength that is the same
  everywhere goes into the strength instead of a mask, and a highlight mask equal to the
  reflection mask reads the reflection mask's channel (Poiyomi's channel 4, white, or 2).
- `AtlasFieldwise.Signature` describes assets by GUID and file ID, so keys stay the same
  between sessions.
- **Faster source evaluation:** per-pixel work (mask levels and blends, global mask steps,
  vertex colors, decals, the base color's brightest value) runs on every core through the
  Texture Wrapper's new `GetPixels`/`SetPixels`, and within one source's evaluation each
  texture is loaded and sampled once, and each global mask channel and vertex color map made
  once (`AtlasEvaluationCache`), instead of once per layer or mask. A channel copied into RGB
  is one pass instead of three blits, and the Texture Wrapper reuses pixel buffers and skips
  the resampled copy in same-size blits. Evaluation steps of 100 ms or more get a timing line.
- **One coverage, island growth and placement per source and texture:** a texture's virtual
  maps (base color and alpha, or metallic, smoothness and the masks) are baked source by
  source into one content, so each source's coverage is painted, its islands grown and its
  region placed once per texture instead of once per map, and the space between regions is
  filled once. A map's region a source doesn't fill keeps the channel's neutral value.
- **Progress bar with Cancel** while an output bakes (build and *Bake to Assets*): the
  texture, map and source being baked. Cancel stops the build and leaves the avatar as it was.
- **Atlaser inspector:** *Geometry Baking* is a folding section right above the output
  components; the "configuration-only" note is gone. The output inspector drops its build
  note and the "all detected materials are assigned" line.
- Package and NDMF name `app.nordicwind.advanced-atlaser`. Requires Texture Wrapper 0.10.0 or
  later, for the renamed runtime assembly and the normal correction safety rules.
- The NDMF plugin class is renamed `NordicsAdvancedAtlaserPlugin`. The AAO integration define
  is renamed `NORDIC_ADVANCED_ATLASER_HAS_AAO`. Tests use namespace
  `NordicWind.AdvancedAtlaser.Tests`. MIT license.
- Build errors start with `[Nordic's Advanced Atlaser]`.
- **Geometry baking resolution is automatic.** Each material is baked at 1.5× its area in its
  atlas, up to 4096 per side; a larger area is baked 1:1. The *Geometry Baking Resolution*
  setting is removed.
- The main output texture is always generated: no on/off switch, default 4096 × 4096.
- Region padding defaults to 4 px and is only used with two or more source materials.
- The Geometry Baking section explains what it does. *Excluded Blend Shapes* moved into it,
  with a description, and only shows when baking is on. Its separate on/off switch is removed:
  an empty list excludes nothing.
- The output shaders' placeholder settings are removed. Output shaders declare their
  capabilities and texture layout; the settings belong to the shared output features.
- **Build results no longer look like a failure.** NDMF writes every report to Unity's console
  as an "[NDMF] Error Reported" warning, whatever its severity. The build summary and the
  diagnostics are added with that line filtered out, so the NDMF Console still lists them and
  Unity's console stays clean. Warnings are reported as they are.
- **Exclude Hidden Geometry (Depth Test)**, a geometry baking option, on by default: where
  surfaces share UVs, the ones lying more than 2 mm plus one texel's size behind another
  (along the average normal there) are left out of the bake, so geometry hidden inside the avatar no longer blocks or
  skews the correction without setting up *Excluded Blend Shapes*. Mirrored halves aren't
  affected. All original meshes of a material are now painted in one batch, so a part of one
  can be hidden by another.
- **Faster painting from meshes:** the geometry bake and the coverage paint each map's triangles
  in one batch (`PaintTriangles`), on every core or the GPU, instead of one triangle at a time.
  Dilation runs on the GPU for large maps.
- **Stage timings:** with *Verbose Logging*, the build lists how long each stage took
  (parsing, the geometry bake's steps, each map bake, packing, compression, the remap), and
  *Bake to Assets* logs its stages in one line, to measure where builds spend their time.
- **Input report lines are debug information.** How each source was read is listed in the
  build only with *Verbose Logging*, since atlasing is best effort.
- **Serialized fields are camelCase**, as the design guidelines require. Existing Atlaser
  configurations are not migrated.
- **The bake releases its maps as soon as it's done.** Only the maps the bake needs exist at
  the same time (6 instead of 8: about 1.7 GB instead of 2.3 GB at 4096²), and each material
  keeps only its usage mask and correction afterward (2 maps instead of 4). The frame-normal
  map serves as the bake's coverage area, and the usage mask is painted after the bake. Results
  are unchanged. `AtlasSubmeshBake` no longer has `OriginalWorldNormals` or
  `CurrentWorldNormals`; debug exports still write them.
- The UV preview reads its thumbnails through the material reader, so materials whose shader
  is missing show their texture too.

### Removed
- **The earlier material model** and its free-form semantic features (`AtlasSemanticFeature`),
  replaced by the unified material.
- **The Mochie Standard input parser.** Mochie Standard is mainly a world shader; Mochie's
  avatar shader, Uber, replaces it. Mochie Standard materials go to the fallback parser.

### Fixed
- **Mochie Uber's texture rotation** (main and detail) was taken over as it is, but Uber turns
  the UV clockwise around its origin after the tiling. It's now converted to the unified
  rotation, which is defined as counterclockwise around the center, before the tiling.- With *Export Intermediate Maps* on, the tangent-frame maps were exported (which normalizes
  them) before the normal correction bake, so debug builds baked slightly different
  corrections. They're now exported after the bake.

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
