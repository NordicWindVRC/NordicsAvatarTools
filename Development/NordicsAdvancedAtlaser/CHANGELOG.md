# Changelog — Nordic's Advanced Atlaser

Newest first. Versions follow [SemVer](https://semver.org). Early versions were numbered as
"Revision N", which corresponds to version `0.N.0`. Not released yet.

## 0.40.0 — unreleased

EditMode tests pass in Unity 2022.3.22.

### Added
- **Normal maps:** each source's normals are combined into the output's normal map, in the
  established order (pre-baked map, the geometry bake's correction, the primary normal map,
  UV0 normal layers with their masks), by whiteout. The output's strength is the largest of
  the sources', and each source is baked at its share of it. A source with only a pre-baked
  map or a correction counts too, so the geometry bake's correction reaches the avatar. Both
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
  need, their sizes, and GPU memory estimates for PC and Android with totals, following
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
