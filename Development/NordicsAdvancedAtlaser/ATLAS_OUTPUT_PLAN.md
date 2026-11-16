# Nordic's Advanced Atlaser — Output implementation plan

**Status:** phase 1.1 done; 1.2 is next. It implements the design in
[ATLAS_OUTPUT.md](ATLAS_OUTPUT.md), whose section names are used here.

## How the plan works

- **Phases** are small. Each ends in a **workable state**: the EditMode tests pass in Unity,
  and the inspector, the manual bake and the build all work for what exists so far. Nothing
  is left half-built between phases.
- **Milestones** group phases. Each milestone is a usable release point.
- Features are added one at a time to **both first writers** (Poiyomi Toon 10 and VRChat Mobile
  Toon Standard). A feature a writer can't express natively uses its fallback mode, or isn't
  offered.
- **Every phase adds tests**:
  - unit tests for allocators, packing and evaluators;
  - round trips (parse, write, parse, compare) for what a writer maps natively;
  - small synthetic meshes for bakes and the remapper.
- **Settings change freely until the first release.** The Atlaser isn't released yet, so
  changes to the serialized output settings need no migration.

| Milestone | Result |
|---|---|
| **M1 — End to end** | An avatar builds with the sources' base colour textures atlased into one, on both writers, and the manual bake writes the material and its texture |
| **M2 — Initial features** | Every feature in the design's *Feature inventory*, on both writers |
| **M3 — More Poiyomi writers** | Poiyomi Toon 11, Pro 10 and Pro 11 as outputs |
| Later | Mochie Uber as an output, the later features, animation, GPU baking |

---

## M1 — End to end

Only the **base colour texture**, but through every step: collect, resolve, bake, pack,
write, export, and the mesh remapper. Each source's base colour texture is placed in its
region as it is; the colour it is multiplied by, vertex colours, layers and adjustments come
in M2 (2.3).

### 1.1 Output model

- `IAtlasOutputFeature`, feature modes, capabilities in unified terms, virtual maps and the
  output shader's texture layout.
- Building the **output unified material**: the merge of the sources, with texture references
  that can hold generated maps.
- Settings stored by feature ID: feature settings (mode, values, per-source overrides) and
  texture settings. They replace the generic `AtlasOutputSetting` list. The output settings
  (crunch, formats, baking) are added with the phases that use them.
- `IAtlasOutputShader` gains capabilities and a texture layout; its writer comes in 1.4. Its
  `Fields`, `AutoConfigure` and `OnConfigurationChanged` are replaced by the shared features.
- Input parsers' *unmapped* lines are logged only with the debug setting.
- **Features:** *Colour*, the base colour texture only.
- **Tests:** capability filtering with a test output shader; settings surviving a change of
  output shader.

**Workable:** the inspector shows the Colour feature and its mode from the shared
descriptions; builds behave as before.

### 1.2 Inspector

- The **source analysis cache** (keyed by the material's dirty count, its shader, and an import
  version).
- The generated UI: the *Textures* section (required textures, sizes, size modes), feature
  sections with foldouts and the source count, the per-source foldout, auto setup choosing
  default modes.
- **Memory** per texture for PC and Android, and the totals, from the formats in the output
  settings.
- **Tests:** cache invalidation; memory figures for known sizes and formats.

**Workable:** the full inspector layout for colour, fast with many sources.

### 1.3 Colour bake

- The CPU evaluator's first **bakeable subset**: the base colour texture, with its tiling,
  offset and rotation on UV0 resolved into the region. A source without a texture fills its
  region with white. Everything else in the colour stack is left out until its phase.
- Per-source bake at the bake size, blit into the source's region with padding, dilation.
- The **memory budget** rules: one virtual map at a time, sources one after another.
- **Tests:** a two-source atlas from small textures gives the expected texels in each region
  and in the padding.

**Workable:** the atlas's base colour map can be produced in a test, without a writer.

### 1.4 Pack, write, export and manual bake

- Packing virtual maps into the layout's textures (the base colour texture only so far).
- Writers for Poiyomi Toon 10 and VRChat Mobile Toon Standard: the base colour texture (with a
  white colour), an opaque
  surface mode, the render queue, and what Poiyomi needs to lock the material (Thry's
  animated-property tags, where values are animated).
- Export rules: no unused alpha, colour space per virtual map, crunch with the 90% rule.
- **Bake to Assets…**: folder and name (defaulting to the output name), textures named
  `<material>_<texture>.png`, overwriting textures, and asking before replacing the material.
- **Tests:** a base-colour round trip for both writers; the crunch rule's choice; file names.

**Workable:** the manual bake writes a working material and its texture for both shaders.

### 1.5 Mesh remapper

- The remapper's first version, as the design's *Mesh remapper* describes: copy the mesh,
  split shared vertices (with every attribute and blend shape frame), write UV0 into the
  region (clamped), swap the materials, merge the submeshes that share the output material.
- The build creates the output materials and textures in memory and puts them on the
  remapped meshes.
- **Tests:** synthetic meshes:
  - a vertex shared by two sources is split, with its blend shape deltas;
  - UVs land in their regions, and clamped UVs are counted in one line;
  - submeshes merge, and other submeshes keep their order.

**Workable — M1 reached:** an avatar builds with the atlased base colour texture on both
writers.

---

## M2 — Initial features

Each phase adds features to both writers, with their modes, maps and fallbacks.

### 2.1 Alpha and render

- *Alpha* (native, baked into base alpha; cutoffs remapped to one shared cutoff), the surface
  mode and render queue, the fallback shader.
- **Workable:** cutout and transparent sources atlas correctly.

### 2.2 Normals and surface maps

- *Normal maps*: the main normal in the established order (pre-baked normal, the build's
  correction, the primary normal, normal layers), strengths normalised to the largest.
- **Compound textures**: metallic and roughness through the virtual maps, with neutral values
  for unused channels.
- *Reflection and specular* (shared groups, spatial constants), *Ambient occlusion* (native
  map).
- **Workable:** lit materials keep their normals and surface response.

### 2.3 Colour stack and emission

- The bakeable subset grows: the base colour's own colour (multiplied into the texture, or a
  shared colour when every source has the same one), surface layers targeting colour
  (details, decals, recolour regions), static colour adjustments, vertex colour baked into
  colour.
- *Emission* (native map, baked into colour); *Colour adjustments* (baked static, native
  live).
- **Optional maps and fallbacks**: decal and detail colour maps baked into the base colour,
  and secondary and detail normals into the main normal, when their maps are off.
- **Workable:** typical toon materials with decals and emission atlas without losing them.

### 2.4 Masks

- The **mask allocator**: constants, shared masks, inverted masks, packed channels, mask bus
  channels, and dropping or falling back when places run out.
- Global masks on Poiyomi, with live steps baked at their normal-view value where the output
  lacks them.
- Per-source control: turning a feature off per source, the reference source.
- **Workable:** features limited to parts of the atlas stay where they were.

### 2.5 Remapper: UV channels and vertex colours

- The **UV channel allocator** and preserved UVs in UV1 to UV3, with per-source tiling for
  tiled layers. Tiled detail and normal layers can then keep maps of their own.
- *Vertex colours*: live (white for sources that don't use them), baked, or off.
- UV-tile features keep their tiles.
- **Workable:** tiled details stay sharp, and vertex colours behave as the user chose.

### 2.6 Lighting

- *Shading, lighting and shadows*: one lighting setup from the reference source, mapped by
  each writer to its own modes, with shadow colour and ramp masks where supported.
- *Light volumes*, *LTCGI*.
- **Workable:** the atlas is shaded like its main source in both shaders.

### 2.7 Layers

- *Matcaps* (native, merged, single), *Rim lighting*, *Environmental rim*, *Backlight*,
  *Clear coat* (native and emulated).
- **Workable:** matcaps and rims survive atlasing, as far as the output has slots for them.

### 2.8 Baking toolkit

- Geometry buffers: the world normal and the overlap map, within the memory budget.
- The light rigs with roles, colour influence and the strength sliders; the virtual view;
  overlapping UVs (symmetric rig, averaging).
- Bake-ins: full light and form shading, normal-map shading, occlusion maps into colour,
  matcaps and rims into colour or emission, rims emulated as matcaps.
- **Workable:** VRChat Mobile Toon Standard keeps much more of the look through baking.

### 2.9 Effects

- *Glitter and sparkle* (native, emulated into emission, metallic and roughness, and colour),
  *Proximity colour*, *Subsurface scattering* (native), *AudioLink* (native per feature).
- *World AO blocker*: typed in the unified material, read by the Poiyomi parser, written as
  one switch.
- **Workable — M2 reached:** every initial feature, on both writers.

---

## M3 — More Poiyomi writers

### 3.1 One Poiyomi writer, by version and variant

- The Poiyomi Toon 10 writer becomes one writer for version and variant, registered as Toon
  10 and Toon 11. Toon 11 adds its capabilities and maps the features new in 11.
- **Workable:** Poiyomi Toon 11 as an output, with round trips.

### 3.2 Poiyomi Pro 10 and Pro 11

- The Pro variant: Pro's added capabilities (more slots), and its features natively for Pro
  sources (screen-space AO, contact shadows, constellation, GT7 tone mapping, neutral
  emission and the others the unified material types). Offered only where Pro is installed.
  The writer describes Pro in its own words.
- **Workable — M3 reached:** Poiyomi Pro 10 and Pro 11 as outputs, with round trips.

---

## Later

Not planned in detail yet, in rough order:
- Mochie Uber as an output (its parser in reverse);
- the design's later features: outlines, fur, transition and dissolve, the other procedural
  patterns, refraction, highlight layers, parallax, deformations and geometry effects,
  second pass and visibility, screen-space lighting;
- animation support;
- half-precision buffers or GPU baking, if the memory budget is too tight.
