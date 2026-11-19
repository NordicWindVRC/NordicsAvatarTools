# Nordic's Advanced Atlaser — Output implementation plan

**Status:** milestone 1 reached (phases 1.1 to 1.5); auto setup (A1, A2) and 2.1 are done; 2.2 is done; 2.3 is done; 2.4 is done (its output masks moved to 2.7); 2.5 is done (tiled layers kept live moved to 2.7); 2.6 is done (cloth lighting moved to Later); 2.7 is done; 2.9 is next (2.9a done, 2.9b next), before 2.8 (both outputs light and have the maps 2.8's bakes stand in for, so 2.8 matters first where the user turns maps off). It implements the design in
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
| **M1 — End to end** | An avatar builds with the sources' base color textures atlased into one, on both writers, and the manual bake writes the material and its texture |
| **M2 — Initial features** | Every feature in the design's *Feature inventory*, on both writers |
| **M3 — More Poiyomi writers** | Poiyomi Toon 11, Pro 10 and Pro 11 as outputs |
| Later | Mochie Uber as an output, the later features, animation, GPU baking |

---

## M1 — End to end

Only the **base color texture**, but through every step: collect, resolve, bake, pack,
write, export, and the mesh remapper. Each source's base color texture is placed in its
region as it is; the color it is multiplied by, vertex colors, layers and adjustments come
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
- **Features:** *Color*, the base color texture only.
- **Tests:** capability filtering with a test output shader; settings surviving a change of
  output shader.

**Workable:** the inspector shows the Color feature and its mode from the shared
descriptions; builds behave as before.

### 1.2 Inspector

- The **source analysis cache** (keyed by the material's dirty count, its shader, and an import
  version).
- The generated UI: the *Textures* section (required textures, sizes, size modes), feature
  sections with foldouts and the source count, the per-source foldout, auto setup choosing
  default modes.
- **Memory** per texture for PC and Android, and the totals, from the formats in the output
  settings. The formats are the first output settings: color, color with alpha, normal map,
  and data, per platform.
- **Tests:** cache invalidation; memory figures for known sizes and formats.

**Workable:** the full inspector layout for color, fast with many sources.

### 1.3 Color bake

- The CPU evaluator's first **bakeable subset**: the base color texture, with its tiling,
  offset and rotation on UV0 resolved into the region. A source without a texture fills its
  region with white. Everything else in the color stack is left out until its phase.
- Per-source bake at the bake size, blit into the source's region, and dilation: the pixels
  between the regions, the region padding included, take the nearest region's color.
- The **memory budget** rules: one virtual map at a time, sources one after another.
- **Tests:** a two-source atlas from small textures gives the expected texels in each region
  and in the padding.

**Workable:** the atlas's base color map can be produced in a test, without a writer.

### 1.4 Pack, write, export and manual bake

- Packing virtual maps into the layout's textures (the base color texture only so far).
- Writers for Poiyomi Toon 10 and VRChat Mobile Toon Standard: the base color texture (with a
  white color), an opaque
  surface mode, the render queue, and what Poiyomi needs to lock the material (Thry's
  animated-property tags, where values are animated).
- Export rules: no unused alpha, color space per virtual map, crunch with the 90% rule
  (where the PC format has a crunched form: BC1, BC3).
- **Bake to Assets…**: folder and name (defaulting to the output name), textures named
  `<material>_<texture>.png`, overwriting textures, and asking before replacing the material.
- **Tests:** a base-color round trip for both writers; the crunch rule's choice; file names.

**Workable:** the manual bake writes a working material and its texture for both shaders.

### 1.5 Mesh remapper

- The remapper's first version, as the design's *Mesh remapper* describes: copy the mesh,
  split shared vertices (with every attribute and blend shape frame), write UV0 into the
  region (triangles within one UV tile moved into 0–1 by whole tiles, only straddling ones
  clamped), swap the materials, merge the submeshes that share the output material.
- The build creates the output materials and textures in memory and puts them on the
  remapped meshes.
- **Tests:** synthetic meshes:
  - a vertex shared by two sources is split, with its blend shape deltas;
  - UVs land in their regions; a triangle one tile over lands on the same texels; a
    straddling triangle is clamped and counted in one line;
  - submeshes merge, and other submeshes keep their order.

**Workable — M1 reached:** an avatar builds with the atlased base color texture on both
writers.

---

## M2 — Initial features

Each phase adds features to both writers, with their modes, maps and fallbacks.

### 2.1 Alpha and render

- *Alpha* (native, baked into base alpha; cutoffs remapped to one shared cutoff), the surface
  mode and render queue, the fallback shader.
- The bakeable subset of alpha: its four sources, a mask that is a UV0 texture channel (with
  invert and strength), the mask blend, offset and strength. Fades, vertex alpha, dithering and
  other masks are listed in the build's diagnostics.
- Later: a queue override; merging additive and multiplicative sources.
- **Workable:** cutout and transparent sources atlas correctly.

### 2.2 Normals and surface maps

In two steps, since each output shader stores these maps its own way.

**2.2a Normal maps**
- *Normal maps*: the main normal in the established order (pre-baked normal, the build's
  correction, the primary normal, normal layers), strengths normalized to the largest.
- The feature counts a source that has only a pre-baked map or a geometry-bake correction,
  so a decimated avatar without normal maps still gets its correction.
- Normal maps are generated and exported as normal maps.
- **Workable:** lit materials keep their normals, and the build's normal correction reaches
  the avatar.

**2.2b Surface maps**
- **Compound textures**: metallic and roughness through the virtual maps, with neutral values
  for unused channels; a channel can be stored inverted (smoothness for roughness). Poiyomi:
  R metallic, G smoothness, B reflection mask, A specular mask, and its AO map; Toon Standard:
  one texture of metallic, gloss and occlusion.
- The bakeable subset of a scalar input: a constant, or a UV0 map's channel with its
  constant, invert and combine; remaps and the mask bus are listed in the diagnostics.
- Highlight and reflection settings other than their strengths and masks (tints, fresnel,
  steps, anisotropy, cubemaps) follow later, as shared values from the reference source.
- *Reflection and specular* (shared groups, spatial constants), *Ambient occlusion* (native
  map).
- **Workable:** lit materials keep their surface response.

### 2.3 Color stack and emission

In two steps; vertex colors move to 2.5, with the remapper's vertex color handling.

**2.3a Base color stack**
- The bakeable subset grows: the base color's own color (multiplied into the texture, or a
  shared color when every source has the same one and nothing is layered on it), surface
  layers targeting color (tiled details, plain colors, recolor regions with tint or add to
  luminance), static color adjustments on the base color, the surface color and layer
  sources.
- Static: no modulator drives it and its texture doesn't scroll, spin or animate. Live parts,
  decals, final-color adjustments and the operations without a wrapper equivalent are listed
  in the diagnostics.
- **Workable:** toon materials with details and recolor regions atlas without losing them.

**2.3b Decals and emission**
- Decals (placed sources: center, scale and rotation, clipped or not) baked into the base
  color, as Poiyomi places them; symmetry, mirror sides, depth, spin and MSDF text are listed.
- *Emission* (native map only: baked into color wouldn't glow), relative to the strongest
  source's emission; live emission is listed.
- **Optional maps and fallbacks**: no output has a separate decal, detail or secondary normal
  texture yet, so they are always baked into the base color and the main normal.
- Live color adjustments (native) move to 2.9, with the other modulated effects.
- **Workable:** typical toon materials with decals and emission atlas without losing them.

### 2.4 Masks

**2.4a Mask baking**
- Masks with their levels, and the mask bus (global masks) baked per source: texture,
  offset, remap and force steps exactly; the live steps (faces, mirror, camera, distance,
  blacklight, contact) at their normal-view value, listed in the diagnostics. Masks and
  scalar inputs read their channel by Poiyomi's mask blend. Vertex color, layer coverage and
  second-pass steps wait for 2.5, 2.7 and the second pass.
- Per-source control (turning a feature off per source, the reference source) is in place
  from 2.2 and the conflict controls.

**2.4b Output masks** moved to 2.7: the output's only masks so far are Poiyomi's reflection
and specular masks, in fixed channels, so the allocator gets its first real users with the
layers. Until then, live mask steps stay baked at their normal-view value and are listed.
- **Workable:** features limited to parts of the atlas stay where they were (baked).

### 2.5 Remapper: UV channels and vertex colors

**2.5a Vertex colors**
- *Vertex colors*: live (each source's vertex colors rewritten by the remap for the output's
  one setting; white for sources that don't use them), baked (painted in UV space into the
  base color, alpha, masks and mask bus steps), or off. Hue shifts on vertex colors are listed.

**2.5b Hidden UV tiles**
- *Hidden UV Tiles* (Poiyomi's UV tile discard): kept live (each source's UVs in a UV channel
  the allocator gives, tiles mapped to the output's hidden tiles, both sides and one side),
  removed from the mesh by the remap (tiles hidden on both sides, any output), or off.
- The **UV channel allocator**'s groundwork: the plan hands out UV1 to UV3, and the remap
  writes preserved UVs per source.
- Tiled layers kept live on a preserved channel, with per-source tiling, moved to 2.7 with the
  other layer writers; until then they are baked into the atlas.
- **Workable:** parts hidden by UV tiles stay hidden, and vertex colors behave as the user chose.

### 2.6 Lighting

**2.6a Lighting setup**
- *Lighting*: one lighting setup from the reference source (*Lighting from*: automatic, the
  largest region, or a chosen material), mapped by each writer to its own modes. Poiyomi
  writes its parser's reading back: Light Data, every lighting type but cloth and SDF,
  light volumes and LTCGI. Toon Standard takes its ramp (the source's, one generated from
  banded or keyed shading, or the SDK ramp nearest the family), shadow boost, shadow tint and
  brightness settings.
- The written material is read back by its parser, and the lighting settings it doesn't hold
  the same way are listed.

**2.6b Shadow masks**
- *Shadow Masks*: every source's shadow strength mask and detail shadow baked into the
  output's lighting masks texture (1 where a source has none), for ramp toon lighting.
  Poiyomi reads them from Light Data's shadow masks (R) and detail shadows (G); Toon Standard
  has neither, and lists them.

**2.6c Lighting drawn on the texture**
- *Shadow Colors*: every source's band and shade textures (multilayer math, shade map) baked
  into up to three shadow color textures, white where a source has none.
- *Skin Thickness*: every source's skin thickness baked into a skin thickness texture, 0
  where a source has none.
- SDF faces (the SDF type and the SDF light map) stay live: the reference source's texture,
  read through every source's own UVs in a channel the allocator gives, since an SDF is
  sampled mirrored by the light's side, which an atlas can't keep.
- Baked parts don't count as lighting differently.
- Cloth maps with the cloth type moved to *Later*: a packed metallic, mask, reflectance and
  smoothness map and its own type, rarely used.
- **Workable:** the atlas is shaded like its main source in both shaders.

### 2.7 Layers

**2.7a Mask allocator and matcaps**
- The **mask allocator** (from 2.4b): a mask that is one value everywhere needs no texture;
  equal masks share a place; a mask that is the inverse of a placed one reads it inverted
  where the reader can; the rest are packed into the output's *Masks 1* and *Masks 2* RGBA
  textures. The reflection and specular masks follow the same rules in their own channels:
  a constant becomes the strength, and a highlight mask equal to the reflection mask reads
  its channel.
- *Matcaps*: one mode, *Matcap slots* (Poiyomi 4, Toon Standard 1). Matcaps that look the
  same in several sources share a slot, each source's mask saying where it shows. Each
  matcap is placed by the user (*Automatic*, its own slot, combined with another, off);
  automatic combines matcaps on the same parts with the same lookup, gives the largest their
  own slots and combines the rest into the most similar slot. A combined slot gets a
  generated texture (matcaps on the same parts added for *add*, multiplied for *multiply*;
  those on different parts averaged by area). Blends the output lacks are approximated
  with add or multiply, and base color mix through the mask; both are listed.
- **Workable:** matcaps survive atlasing, combined where the output has too few slots.

**2.7b Rim lighting**
- *Rim Lighting*: one mode, *Rim slots* (Poiyomi 2 with masks, Toon Standard 1 without).
  Rims that look the same share a slot. Each rim is placed by the user (*Automatic*, its own
  slot, as a matcap, combined with another, off); automatic gives it a rim slot while one is
  free (without a rim mask, only when the rim is the same everywhere), then a free matcap
  slot (the rim drawn into a generated matcap, exactly when it depends only on the view
  angle), then a rim slot anyway, shown on every part and listed. Poiyomi writes each slot in
  the style that holds it (Poiyomi, UTS2 with its unlit-side rim, lilToon); Toon Standard's
  band takes other rims fitted (same midpoint and rise).
- *Environmental Rim*: Poiyomi only; one setup from the reference source, the strength per
  part in an allocated mask.
- *Backlight*: Poiyomi only; one setup from the reference source, the color per part in a
  backlight color texture.
- What the output shader has nothing for is listed, for every feature.
- Rim color textures are listed; depth rims moved to *Later*.

**2.7c Clear coat:** native on Poiyomi; on Toon Standard *Into the gloss map*: in coated parts
the gloss is raised toward the coat's, `lerp(base, max(base, coat), coat strength × coat
reflection strength)`, so they get real, sharper highlights and reflections on its one layer
(the surface under the coat becomes as glossy as the coat there; tints and the normal map
amount aren't kept, listed). On Poiyomi: each source's coat strength and roughness in the
*Clear coat maps* texture (no coat: strength 0), its reflection and highlight strengths in
its masks relative to the strongest, under the allocator's rules; tints, fresnel, cubemaps,
the normal map amount and anti-aliasing from the reference source (*Clear Coat from*).

**2.7d Global masks on Poiyomi** (from 2.4b), keeping live steps live: a feature splits its
value into its own part (the live channel at 0) and the rest, a global mask channel holding
the value without the live steps minus the own part, with the same live steps after it, read
by *add* (`AtlasLiveMasks`). Exact for values reading a channel by multiply or replace whose
live steps keep 0 at 0 (back face, mirror, camera; distance and blacklight by multiply).
Wired for alpha, emission (a second emission through a channel past the mask places),
metallic, smoothness, reflection and highlight strengths, occlusion, clear coat, matcap and
rim slots. Where a feature's sources need different live setups, *Live from* picks the one
kept; the others are baked as normally seen and listed. Contacts stay baked (Poiyomi 11).

**2.7e Tiled layers kept live** (from 2.5b): *Detail Texture* and *Detail Normal* (kept live,
baked, off). A source's last static layer of the kind tiled over UV0 stays live on a preserved
UV channel holding its UV0 × tiling + offset, so each source keeps its own scale; its strength
× mask goes into the detail mask (Poiyomi's *Detail mask* R and G, Toon Standard's allocated
mask by channel, one for both), 0 where a source has none. Sources whose layer looks the
same as the reference source's share the slot (*Detail Texture from*, *Detail Normal from*);
the others, and layers that are rotated, followed by layers of the same kind, adjusted, or
in a blend or tint the output lacks, are baked as before and listed. The base color and
normal bakes skip what's kept live (`AtlasLiveLayers`); *off* leaves it out of them
(`IAtlasLeftOutFeature`).
- **Workable:** matcaps and rims survive atlasing, as far as the output has slots for them.

### 2.8 Baking toolkit

- Geometry buffers: the world normal and the overlap map, within the memory budget.
- The light rigs with roles, color influence and the strength sliders; the virtual view;
  overlapping UVs (symmetric rig, averaging).
- Bake-ins: full light and form shading, normal-map shading, occlusion maps into color,
  matcaps and rims into color or emission, rims emulated as matcaps.
- Where it applies on Poiyomi and Toon Standard: a map the user turns off to save memory
  (normal maps → normal-map shading, the occlusion map → occlusion into color), and outputs
  added later that are unlit or have no normal maps. Bake-ins that freeze a view- or
  light-dependent effect into the color (rims, matcaps, backlight) stay out unless they keep
  the artist's intent better than leaving the effect out.
- **Workable:** VRChat Mobile Toon Standard keeps much more of the look through baking.

### 2.9 Effects

**2.9a Glitter:** native on Poiyomi, one setup from the reference source (*Glitter from*), on
a preserved UV channel holding each source's UV × (its density ÷ the output's) so every
source keeps its particle size, opacity × mask in an allocated mask. Toon Standard: *Points in
the maps*, the cells drawn as still points into the emission (*Glow*), the metallic and gloss
(*Shine*) and the base color (*Color*), each with its own strength (feature options,
`IAtlasOptionFeature`); they don't twinkle, which the build log says.

**2.9b** *Subsurface scattering* (native), *Proximity color*.

**2.9c** *Color adjustments* (native live), with the output's modulators; *AudioLink* (native
per feature).

**2.9d** *World AO blocker*: typed in the unified material, read by the Poiyomi parser,
written as one switch.
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

## Texture Wrapper GPU work (before 2.1)

The Texture Wrapper's GPU acceleration (its `GPU_ACCELERATION.md`) came first: dilation and
batch triangle painting run on the GPU for large textures, and the Atlaser's painting uses
the batch. The geometry bake has the toggle **Exclude hidden geometry (depth test)**, on by
default: where surfaces share UVs, the ones more than 2 mm plus one texel's size behind
another along the average normal there are left out, so geometry hidden inside the avatar doesn't affect the bake
without setting up *Excluded Blend Shapes*. Mirrored halves (opposite UV winding) never hide
each other. The wrapper's `GPU_ACCELERATION.md` explains why it compares along the normal
instead of using a bone distance.

## Auto setup (before 2.1)

The design's *Auto setup*, in two phases.

### A1 Output shader pick

- Families on the parsers and output shaders (Poiyomi; VRChat Mobile), and a rank within
  the family on the output shaders: the version, with Pro before Toon at the same version.
- A new output component starts without a shader. When one appears without a shader, the
  editor picks one by the sources' families, once: from *Add Atlas Output*, the *Add
  Component* menu, or the inspector's first look at it.
- A build skips an output without a shader, with an error naming it; the inspector and the
  manual bake say that no output shader is chosen.
- **Tests:** the vote (most common family, ties, materials of other outputs, no family), the
  highest installed version, Pro before Toon, nothing installed, and an existing choice left
  alone.

**Workable:** a new output gets the shader that fits its avatar's materials.

### A2 Auto Setup

- The button sets up the assigned sources only (it never adds or removes one): it lays out
  their UV regions and sets the main texture size, the feature modes and the optional
  textures, as the design describes, in one Undo step.
- The regions: base color texture sizes, packed largest first into the smallest atlas up
  to 4096 that holds them at full size, or 4096 × 4096 with every region scaled down alike as
  little as fits. The regions tile; the padding goes inside them, in the bake's Blit and the
  mesh remap (Texture Wrapper `ContentRegion`).
- Later: use the coverage to skip padding where a source already has room at its edges.
- Optional textures are only reset to their defaults here; turning them on by what the
  sources use comes with the first optional texture, in 2.2.
- The dialog before replacing edited regions, and a log line saying what it did.
- The free space goes to the scaled-down sources, largest first, each up to its full size;
  then, if the user agrees in a dialog, every region stretches into the free space beside it,
  so the atlas is used whole.
- **Tests:** the packer (no overlaps, padding kept, proportions kept when halved, the
  smallest atlas), the sources left as they were, edited regions kept on *Keep*, and a full
  setup on synthetic materials.

**Workable:** one click sets up an output that builds.

---

## Later

Not planned in detail yet, in rough order:
- Mochie Uber as an output (its parser in reverse);
- the design's later features: outlines, fur, transition and dissolve, the other procedural
  patterns, refraction, highlight layers, parallax, deformations and geometry effects,
  second pass and visibility, screen-space lighting;
- animation support;
- half-precision buffers or GPU baking, if the memory budget is too tight;
- a typed-in value for shared values, next to *Automatic* and the sources in the reference
  source drop-down;
- **depth rims** (from 2.7b): Poiyomi's depth rim lighting natively, one setup from the
  reference source;
- **rim color textures** (from 2.7b): a rim's texture on UV0 baked into a color map;
- **cloth lighting** (from 2.6c): Poiyomi's cloth type, with every source's metallic, cloth
  mask, reflectance and smoothness baked into its packed map;
- **bake speed**, measured on a 5-material avatar (11 s per output, 2.7 s of it Unity's
  compression), CPU only, the Texture Wrapper kept generic (no GPU color stack: it would move
  growing feature logic into the wrapper):
  - decals worked on within their own rectangle instead of full-size temporaries (about 2 s
    on a 4096 body);
  - same-size `Resize` without a transform as a copy, and the same-size `Blit` shortcut for
    copying a channel into usage (island growing);
  - redundant passes such as the neutral fill where every channel is overwritten;
  - a per-source bake cache in `Library`, keyed by the material's values, its textures'
    content hashes, the coverage, the bake size and the layout: repeat builds evaluate only
    changed sources; optionally the compressed textures too.
