# Changelog — Nordic's Advanced Atlaser

Newest first. Versions follow [SemVer](https://semver.org). Early versions were numbered as
"Revision N", which corresponds to version `0.N.0`. Not released yet.

## 0.40.0 — unreleased

EditMode tests pass in Unity 2022.3.22.

### Added
- **Unified material.** Each source material is parsed into a shader-independent, typed
  description in `Editor/Unified/`, specified in `UNIFIED_MATERIAL_FEATURES.md`: building
  blocks (texture references, scalar and colour inputs, masks and the mask bus, blends,
  placement, modulators, colour adjustments), the surface, four lighting families, an ordered
  list of layers (surface, transition, lookup, angle, subsurface, procedural pattern,
  refraction, highlight), outlines, fur, deformations and geometry effects, and render
  settings including visibility and a second pass. Colours are linear. Values without a typed
  home become report lines (unmapped, unsupported, ignored or note), at most one per kind and
  feature per material; unmapped and unsupported lines are build warnings.
- **Input parsers** on the unified material (details in the `UNIFIED_MATERIAL_*.md`
  inventories):
  - VRChat Mobile Toon Lit, Standard Lite and Toon Standard (and Outline);
  - Unity Standard (metallic and specular setup);
  - Mochie Uber (and Outline);
  - Poiyomi Toon 10 and 11 with their avatar variants (Two Pass, Early Outline, Grab Pass,
    Lil Fur), and Poiyomi Pro 10 and 11 with theirs (Pro Fur, Triple Outline, Self Grab,
    Triplanar Projection, Tessellated and the geometry variants);
  - Poiyomi Toon 7.3, 8.x and 9.x, read on the Poiyomi 10 parser with version tables;
  - a fallback for any other shader, from common property names.
- **Source shaders don't need to be installed.** When a material's shader is missing, the
  parsers read the values the material saved, through `AtlasMaterialReader` (new
  `AtlasSavedProperties`); parsing reads materials only through it. Poiyomi (family, Pro,
  version 7–11) and Mochie Uber are recognised from saved property names. Option names that
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
- The UV preview reads its thumbnails through the material reader, so materials whose shader
  is missing show their texture too.

### Removed
- **The earlier material model** and its free-form semantic features (`AtlasSemanticFeature`),
  replaced by the unified material.
- **The Mochie Standard input parser.** Mochie Standard is mainly a world shader; Mochie's
  avatar shader, Uber, replaces it. Mochie Standard materials go to the fallback parser.

### Fixed
- With *Export Intermediate Maps* on, the tangent-frame maps were exported (which normalizes
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
