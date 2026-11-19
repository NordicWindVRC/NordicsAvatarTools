# Nordic's Advanced Atlaser — Output design

**Status:** design. Nothing in this document is implemented yet, except what
[*Current state*](#current-state) lists. It describes how an atlas output turns several unified
materials into one material for an output shader, and which parts of that are shared and which
are shader specific.

Related documents:
- [UNIFIED_MATERIAL.md](UNIFIED_MATERIAL.md): the unified material and the input parsers.
- [UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md): the feature catalog. Section
  numbers such as *4.3* in this document refer to it.
- [NORMAL_BAKER.md](NORMAL_BAKER.md): the normal correction bake that the output reuses.
- [ATLAS_OUTPUT_PLAN.md](ATLAS_OUTPUT_PLAN.md): the implementation plan, in phases and
  milestones.

---

## Goals

- **Features first, not shaders first.** Output processing is organized by unified feature.
  Each feature has shared, reusable processing: how instances from several sources merge, which
  textures it needs, which output capabilities it needs, and how it is emulated without them.
  Adding an output shader means declaring its capabilities and mapping features into its
  properties, not rewriting the merging and baking.
- **Capabilities filter choices.** An output shader declares what it can express. Only the
  feature modes it supports are offered, and only for features the sources use.
- **The user decides what to keep.** A supported feature or map is never mandatory: that the
  output shader has a map doesn't mean it is used. Every feature can be simplified or turned
  off, and every optional map can be turned off, for example to save VRAM. A map that is turned
  off has its content baked into the map it falls back to (see
  [*Optional maps and fallbacks*](#optional-maps-and-fallbacks)).
- **Best effort, without noise.** Atlasing always loses something. Features that are dropped,
  simplified or emulated are not reported as problems; the settings themselves show what each
  feature does.
- **Mostly generated UI.** The output inspector is built from the shared feature descriptions:
  one texture section, then one section per feature with a mode and that mode's settings.
- **The look is the benchmark.** As on the input side, a same-shader round trip (Poiyomi into
  Poiyomi) should look the same as the original for every feature the output keeps natively.

## Current state

What exists today, which this design builds on:

| Part | Where | What it does |
|---|---|---|
| Output component | `Runtime/NordicsAtlasOutput.cs` | `AtlasOutputConfiguration`: output name, output shader ID, region padding, sources (material, UV region, normal correction, pre-baked normal map), main texture size, feature settings (`AtlasFeatureSetting`: mode, reference source, sources turned off, values) and texture settings, by ID |
| Output shader interface | `Editor/AtlasShaderRegistry.cs` | `IAtlasOutputShader`: ID, display name, `FindShader`, `Capabilities` (the texture layout so far) |
| Output shaders | `Editor/Outputs/AtlasOutputShaders.cs` | Poiyomi Toon 10 (base color with alpha; a normal map; a packed map of metallic, smoothness, reflection mask and specular mask; a packed clear coat map; an occlusion map; a shadow and detail masks map; up to three shadow color maps; a skin thickness map; two mask textures; four matcap slots; two rim slots, an environmental rim and a backlight with its color texture; opaque, cutout, transparent, fade and fade with depth; VRChat fallback) and VRChat Mobile Toon Standard (base color; a normal map; one texture of metallic, gloss and occlusion; one mask texture; one matcap slot; one rim without a mask; opaque only) |
| Output model | `Editor/Outputs/AtlasOutputModel.cs`, `AtlasOutputFeature.cs` | Virtual maps, texture layouts, capabilities; `IAtlasOutputFeature` with its modes; the feature registry, available and effective modes |
| Features | `Editor/Outputs/AtlasColorFeature.cs`, `AtlasColorStack.cs`, `AtlasAlphaFeature.cs`, `AtlasNormalFeature.cs`, `AtlasSurfaceResponseFeatures.cs`, `AtlasEmissionFeature.cs`, `AtlasVertexColorFeature.cs`, `AtlasHiddenTilesFeature.cs`, `AtlasLightingFeature.cs`, `AtlasLightingMasksFeature.cs`, `AtlasLightingTextureFeatures.cs`, `AtlasRenderFeatures.cs`, `AtlasMatcapFeature.cs`, `AtlasRimFeature.cs`, `AtlasEdgeLightFeatures.cs`, `AtlasClearCoatFeature.cs` | *Color*: the base color map, each source's color stack baked (its color, color layers, static adjustments), the color shared when every source has it and nothing is layered on it. *Alpha*: each source's alpha in the base color's alpha, converted to the output's surface mode. *Normal Maps*: the main normal map, each source's normals combined in the established order. *Reflection and Specular*: metallic and roughness maps, and the highlight and reflection strengths in masks, relative to the largest (none where one value fits everywhere). *Matcaps*: lookup layers in the output's matcap slots, placed per matcap, combined into generated textures (`AtlasGeneratedTextures.cs`) where needed. *Rim Lighting*: view-angle rims in rim slots or drawn into free matcap slots (`AtlasRimCurve.cs`). *Environmental Rim* and *Backlight*: one setup each, from the reference source. A feature the output shader has nothing for is listed in the build log. *Clear Coat*: the coat's strength and roughness in maps, its reflection and highlight strengths in masks, the rest from the reference source. *Ambient Occlusion*: the occlusion map. *Emission*: the emission map, relative to the strongest source's emission. *Surface Mode* and *Fallback Shader*, which are always on. A feature can count a source by more than its material (`IAtlasSourceAwareFeature`): Normal Maps counts a pre-baked map, or a geometry-bake correction that bends the normal (only in the build) |
| Resolve | `Editor/Outputs/AtlasOutputPipeline.cs` | Feature choices from the settings, and steps 1 and 2 of the pipeline: the output unified material and its map requests (`AtlasOutputPlan`), with the UV channel and mask allocators' places and the generated textures |
| Live global masks | `Editor/Outputs/AtlasLiveMasks.cs` | Masks whose global mask channels change live split into an own part and a live channel with the same live steps (see [*Masks*](#masks)): alpha, emission, metallic, smoothness, reflection and highlight strengths, occlusion, clear coat, matcap and rim slots, on Poiyomi |
| Mask allocator | `Editor/Outputs/AtlasMaskAllocator.cs` | Masks over the whole atlas: constants without a texture, equal masks sharing a channel, inverses read inverted, the rest packed into the output's mask textures (see [*Masks*](#masks)) |
| Generator boundary | `Editor/Outputs/AtlasGeneratorJob.cs` | The unified materials, UV regions, bakes and feature choices; no `Material` or `Shader` crosses it |
| Bake sizing | `Editor/Outputs/AtlasOutputLayout.cs` | Each source is baked at 1.5× its island size, at most 4096 |
| Geometry bake | `Editor/Build/AtlasSubmeshBaker.cs` | Per source: the current mesh's coverage mask and the tangent-space normal correction. The world-space normal and tangent frame maps exist only during the bake |
| Auto setup | `Editor/Outputs/AtlasOutputAutoSetup.cs`, `AtlasRegionPacker.cs` | The Auto Setup button's work for the assigned sources: regions packed by base color size, the main texture size, default modes (see [*Auto setup*](#auto-setup)) |
| Output shader pick | `Editor/Outputs/AtlasOutputShaderPick.cs` | A new output's shader, once: the family most unused sources use, at its highest installed rank (see [*Auto setup*](#auto-setup)) |
| Inspector | `Editor/NordicsAtlasOutputEditor.cs`, `Editor/AtlasOutputUI.cs` | Sources, UV regions, auto setup, the *Textures* section (sizes, memory, formats), the feature sections with modes and per-source switches, UV preview |
| Source analysis cache | `Editor/AtlasSourceAnalysisCache.cs` | Each source's parse, kept until the material, its shader or an imported texture or shader changes |
| Overview | `Editor/Outputs/AtlasOutputOverview.cs`, `AtlasTextureMemory.cs` | What an output uses: features with their modes and sources, the textures they need with sizes and memory |
| Evaluator | `Editor/Outputs/AtlasMapEvaluator.cs`, `AtlasColorStack.cs` | The bakeable subset, over a source's UV 0–1: the base color texture with its tiling, offset and rotation, its color, the static color layers (tiled UV0 textures, decals placed by center, scale and rotation, or colors, every blend, masks, source alpha as opacity) and static color adjustments on the base color, the surface color and layer sources; the static emission layers; alpha; the combined normal; scalar inputs and masks on UV0, masks with their levels and vertex colors, both reading the mask bus (`AtlasMaskBusEvaluator.cs`: texture, offset, remap and force steps; live steps at their normal-view value). Live parts, decals with symmetry, mirror sides, depth or spin, final-color adjustments are listed in the diagnostics |
| Map baker | `Editor/Outputs/AtlasMapBaker.cs` | One texture, source by source: each of its virtual maps evaluated at the source's bake size and copied into its channels of the source's content; the source's coverage painted, its UV islands grown and the content placed in its region once for all of them; the rest of the atlas filled from the nearest region once per texture |
| Coverage | `Editor/Outputs/AtlasCoverage.cs` | Each source's UV0 triangles (from the output's renderers in the manual bake, the avatar's in the build), painted at bake size; a triangle in another UV tile counts by whole tiles |
| Packing | `Editor/Outputs/AtlasTexturePacker.cs` | One output texture from its virtual maps: which channels each map fills (inverted where the texture stores it so), and the neutral value of the rest |
| Writers | `Editor/Outputs/AtlasOutputShaders.cs` | Poiyomi Toon 10: the base color texture and color, the lighting (Light Data, the lighting type and its settings, the shadow and detail masks, the band, shade and skin thickness textures, an SDF face on a preserved channel, light volumes, LTCGI, the add pass), UV Tile Discard (both sides and one side, vertex mode, on the preserved channel), the normal map (`_BumpMap`, `_BumpScale`), Reflections & Specular from the packed map (`_MochieMetallicMaps` behind `MOCHIE_PBR`), the clear coat from its packed map (`_ClearCoatMaps` behind `POI_CLEARCOAT`), the AO maps (`_LightingAOMaps` behind `PROP_LIGHTINGAOMAPS`), emission 0 (`_EmissionMap`, behind `_EnableEmission` and `_EMISSION`), matcaps 1 to 4 (texture, color and intensity, lookup, mask with channel and invert, every blend weight, emission), rim lighting 0 and 1 (in the Poiyomi, UTS2 or lilToon style, with masks), the environmental rim and backlight 0, the rendering preset of the surface mode (its queue, render type, blend factors, depth write and cutoff, as its shader's presets set them) and the `VRCFallback` tag. VRChat Mobile Toon Standard: the base color texture and color and the normal map (behind `USE_NORMAL_MAPS`), metallic and gloss (behind `USE_SPECULAR`) and occlusion (behind `USE_OCCLUSION_MAP`) from one texture, emission (`_EmissionMap`), the matcap (behind `USE_MATCAP`: add or multiply, strength, mask by channel), the rim (behind `USE_RIMLIGHT`: a band, fitted for other rims), opaque in the Geometry queue |
| Export | `Editor/Outputs/AtlasTextureExport.cs` | PNG assets with the output's formats per platform, crunch where it saves 10% |
| Manual bake | `Editor/Outputs/AtlasManualBake.cs` | Bake to Assets: the pipeline in edit mode, the textures and the material written as assets |
| Build generation | `Editor/Outputs/AtlasOutputGenerator.cs` | The build's textures, packed and compressed for the platform being built (crunch where it saves 10%), and the material; normal maps generated as normal-map textures |
| Mesh remapper | `Editor/Build/AtlasMeshRemapper.cs` | A new mesh per renderer: vertices split, UV0 into the regions (whole tiles moved, straddling triangles clamped), triangles in removed UV tiles left out, preserved UV channels written per source, vertex colors rewritten per source, materials swapped, submeshes of one output merged |
| Build step | `Editor/NDMF/AtlasOutputBuild.cs` | Generates every output and remaps every mesh before changing anything, then saves the new assets with NDMF and registers the replaced meshes |
| Texture library | Texture Wrapper (`API.md`) | Float textures with usage flags, `Blit` with blend modes, masks and padding, color operations, triangle painting, normal difference baking, dilation, PNG and in-memory export, `OutputChannels.Auto` |

The build generates each output's material and textures in memory and remaps the avatar's
meshes onto them; the manual bake writes the same material and textures as assets.

---

## Pipeline

```
sources: N unified materials, their UV regions and bakes
  │
  1 Collect   per feature: the instances each source has
  2 Resolve   per feature: the chosen mode, filtered by the output's capabilities;
              instances assigned to output slots, merged, picked or marked for baking
  3 Bake      per source, at its bake size: surface buffers, then bake-ins in a fixed order;
              each result blitted into its region of the atlas maps
  4 Pack      virtual maps into the output's textures; masks into channels;
              preserved UVs into UV channels
  5 Write     the output unified material → the output shader's material properties
  6 Export    textures (channels, crunch) and the material
```

### The output is a unified material

Steps 1–4 produce **one unified material** for the output: the merge of every source,
simplified to what the output can express, with its texture references pointing at the
generated atlas maps. The output shader's writer then maps **one** unified material into its
shader, and never sees the sources.

Why:
- The writer is the mirror image of the input parser: unified material in, shader properties
  out. All merging, baking and emulation is shared code, written once.
- The capabilities are stated in unified terms (which features, modes and counts the writer
  can map), so filtering needs no shader knowledge.
- Round trips are testable: parse a material, write it to the same shader, parse the result,
  and compare the two unified materials.
- Output shaders stay small, as the goals ask: capabilities plus a property mapping.

The generated textures are Texture Wrapper maps until export. A texture reference in the
output unified material holds the generated texture (`GetTexture2D()` during the build, the
exported asset after a manual bake).

### Order of baking

Bakes depend on each other, so they run in this order per source:

1. **Surface inputs (S1):** base color, alpha, metallic, roughness, occlusion, specular color
   and emission, with the static surface layers and static color adjustments that the chosen
   modes bake. Decals and details whose own maps are turned off are baked in here.
2. **Normals:** the main tangent-space normal map, in the established order: pre-baked
   normal, the build's normal correction, the primary normal, the surface layers targeting the
   normal. Layers that keep a normal map of their own are left out; layers whose map is turned
   off are baked in. The strengths are normalized to the largest among the sources.
3. **World normals:** the combined normal from step 2 in world space (avatar-root space, the
   current mesh's tangent frame), the input of every lighting-dependent bake.
4. **Lighting-dependent bake-ins:** normal-map shading, matcaps, rims, ambient occlusion
   and light, each into a temporary layer.
5. **Mix-in:** the temporary layers are mixed into the color or emission maps by their
   targets and strengths, then the final static color adjustments run.

Step 4 only runs for features whose mode bakes them; with none, steps 3–5 are skipped.

---

## Features and modes

### What a feature declares

Each output feature is a shared class (proposed name `IAtlasOutputFeature`). It covers one or
more unified features and declares:

| Part | Meaning |
|---|---|
| ID, name, section | Stable ID (stored in settings), display name, the UI section it belongs to |
| Presence | Which sources use it, and its instances in each |
| Modes | The ways it can be produced. Each mode lists the capabilities it needs, the virtual maps it produces (required, and optional with the map each falls back to), and the masks it needs |
| Settings | Per mode: the fields shown to the user, with defaults |
| Merge | How instances from several sources combine in each mode (below) |
| Resolve | Writes the result into the output unified material and requests maps and bakes |

Modes come from a small shared vocabulary, so the UI reads the same everywhere:

| Mode | Meaning |
|---|---|
| **Native** | The output shader does it at runtime; instances map to the output's slots |
| **Merged** | Native, with instances combined to fit fewer slots |
| **Single** | Native, one instance for the whole material, chosen by the user |
| **Baked** | Resolved into texture content: color, emission or surface maps |
| **Emulated** | Expressed with a different output feature, for example a rim as a matcap |
| **Off** | Not produced |

A feature shows only the modes whose capabilities the output has. A feature with no
available mode, or that no source uses, isn't shown.

### How instances merge

The merge strategies are shared building blocks; features combine them.

| Strategy | When | How |
|---|---|---|
| **Bake** | Static content placed in UV space | Evaluated per source and blitted into its region. Always available |
| **Shared value** | A setting that applies to the whole material | Equal values pass through. Different values: the value of the **reference source** (*Automatic*: the source with the largest region; or a chosen source, in a drop-down under the feature that appears only when the sources need different values on the output), or the largest or average where the feature says so |
| **Spatial value** | A setting that differs between sources and the output has a map for | Written per region into a map (for example roughness constants into a roughness map) |
| **Strength in mask** | A difference that only scales a feature's opacity or strength | Written into the feature's mask: 0 where a source lacks the feature, its strength where it has it |
| **Slots** | Countable instances: matcaps, rims, decals, outlines | Instances with the same configuration share one slot, told apart by the mask. The user places each instance (*Automatic*, its own slot, combined with another, off), in a list under the feature shown while there is more than one; *Automatic* combines what doesn't fit into the most similar slot and the build log says so |
| **Exclusive** | Things that can't be combined, such as fur | One source's settings for the material. The user picks the source, and can turn the feature off per source (its mask is 0 there, when the output has a mask) |

**Animated values.** Avatars animate material properties, for example emission toggles,
dissolves and hue shifts. After merging, an animation that changed one source's material
would change every source's region. Some animations will be supported; how is outside this
design and is designed separately. Until then:
- a setting that an animation changes keeps the material's current value when it is baked, as
  decided for animation;
- the source analysis (see [*Source analysis cache*](#source-analysis-cache)) has room to
  record which properties are animated per source, so merging can later keep an animated
  feature live with a mask, or leave it unmerged, without changing this design.

### Per-source control

Every feature with more than one source has a foldout listing the sources that use it, with:
- a checkbox to turn the feature off for that source (its region gets mask 0, or nothing is
  baked there).

Where the sources need different values for a shared value, a drop-down under the feature
picks the reference source: *Automatic* or one of the sources (`IAtlasSharedValueFeature`).
It isn't shown when the values agree, or when the output shader has no setting for the value
(Poiyomi's Reflections & Specular has a fixed reflectance). On/off settings the output has once
work the same way, as a switch under the feature shown only while the sources disagree
(`IAtlasConflictSwitchFeature`), stored in the feature's values by switch ID. Alpha to
coverage is one: on or off for every cutout part.

---

## Masks

Masks are shared infrastructure, used by almost every feature.

**Inputs.** Each source has its own masks (1.4) and possibly a mask bus (1.5). For the output
they become atlas-space mask maps: each source's mask evaluated in its region, the static
parts baked, 0 where the source lacks the feature.

**Output.** An output shader declares:
- **Per-feature masks:** which features have their own mask texture, whether it can pick a
  channel, and whether it can invert what it reads. A mask that can pick a channel can share a
  texture with other masks.
- **Mask bus channels** (global masks): how many, which steps they evaluate live (faces,
  mirror, distance, contacts and the rest of 1.5), and which blends each feature can read a
  channel with.

**Allocation.** A mask allocator collects every mask the resolved features need and assigns
each one a place:
1. A mask that is constant over the whole atlas needs no texture: it becomes the feature's
   strength.
2. Masks that are equal share one place.
3. A mask that is the **inverse** of one already placed shares that place too, when the
   feature can read it inverted (below).
4. A feature's own mask texture, packed with other masks into RGBA where channels can be
   picked.
5. A mask bus channel, when the feature reads the bus and the output has free channels.

**Inverted masks.** Every placed mask `m` also offers `1 − m` without another channel or
texture, to every feature that can read it inverted:
- a feature mask with its own invert option reads a packed channel inverted (in unified terms,
  the mask's *invert*, 1.4);
- a feature that reads a mask bus channel with the *subtract* blend and has no mask texture of
  its own gets `saturate(1 − channel)`, since its own mask is then 1 (1.5). Poiyomi's global
  mask blending works this way.

The allocator looks for a match among the placed masks and their inverses before it uses a
new channel. Typical pairs are a region and the rest of the material, or the front and the
back of a two-sided effect.

The allocator (`AtlasMaskAllocator`) takes requests in feature order. Each request describes
its mask at every source as a key (`AtlasMaskKeys`) that is equal for masks giving the same
values, so equal and inverse masks are found by comparing keys. Its places are the virtual
maps *Mask1* to *Mask8*, which an output provides in its mask textures (Poiyomi: *Masks 1*
and *Masks 2*; Toon Standard: *Masks 1*). Masks in fixed channels (Poiyomi's reflection and
specular masks) follow rules 1 and 2 within those channels.

When there aren't enough places, the feature leaves the instance out and the build log says
so.

**Live mask steps** (back face, mirror, camera, distance, blacklight, contacts) stay live only
on an output mask bus that evaluates the same step (`AtlasLiveMasks`). A value that reads a
channel by *multiply* or *replace* is affine in the channel's value, and a channel whose last
steps are live ones that keep 0 at 0 (back face, mirror and camera; distance and blacklight
by *multiply*) is its steps before them × the live factor. So in a source:
`value = own + (prefix − own) × live`, with `own` the value with the channel at 0 and
`prefix` the value without the live steps (`AtlasBusView`). The atlas keeps `own` in the
feature's map (the other sources as they are) and `prefix − own` in an allocated mask place,
which is also a global mask channel (the mask textures double as the output's global mask
textures, *Mask1* to *Mask8* being channels 0–7); the same live steps run on it, and the
feature reads it by *add*. Outside the live sources the channel is 0, so the live steps don't
reach them. A color (emission) keeps the difference in a second map, shown through a channel
past the mask places that is 1 before the live steps. One live setup per feature stays live:
where the sources' differ, *Live from* picks it (*Automatic*: the one on the largest area).
Every other mask is baked with the steps' values in the normal view: front faces, not in a
mirror or camera, the camera far away, no blacklight or contact; the build lists the sources.

**Vertex color masks** are handled by the *Vertex colors* feature (see
[*Feature inventory*](#feature-inventory)): kept live, baked into a texture by painting the
mesh's vertex colors in UV space, or turned off.

---

## UV channels

UV0 holds the **atlas UVs**: each source's UVs moved into its region. UV1 to UV3 can carry
**preserved UVs**: a source's original UVs, untouched by the atlas, for features that need
them. Uses:
- **Tiled layers** (details, tiled normal maps, patterns) stay live on a preserved channel at
  the source's own tiling, instead of being baked into the atlas at the region's resolution.
- **UV-tile features** read their tile from a preserved channel: hidden UV tiles (6),
  AudioLink modes keyed to UV tiles, the world AO blocker.
- **Features that read UV1 to UV3 in the source** (for example a decal on UV1) keep reading
  that source channel.

**Per-source content.** Each source's triangles have vertices of their own: the remap splits
vertices shared between sources, which it has to do for UV0 anyway. So one channel can hold
different content per source, as long as the features reading it mean the same thing. A
channel holding *the original UV0* has source A's original UV0 on A's vertices, and source B's
on B's.

**Per-source transforms.** A channel used only by tiled layers can carry each source's tiling
and offset baked in (`uv × tiling + offset`). Sources that tile the same texture differently
then share one texture reference with tiling 1. A channel read for UV tiles keeps the original
values, so a transformed channel can't also serve tile features.

**Allocation.** Like masks, a UV channel allocator collects the channels the resolved
features ask for: a source channel, raw or transformed. Equal requests share a channel, and
UV1 to UV3 are assigned in feature priority. Vertices of sources that don't use a channel get
values that change nothing: tile 0 for tile features, and any value where the reading
feature's mask is 0. When the channels run out, the remaining features fall back:
- tiled layers are baked into the atlas maps;
- the world AO blocker uses UV0: its triangles are invisible, need no place in the atlas, and
  the remap leaves them in their tile;
- other tile features use their default state: the triangles are shown, and AudioLink tile
  modes are off.

**Capabilities.** The output shader declares which UV channels each texture slot and tile
feature can read. A shader that reads only UV0 gets no preserved channels; everything that
needs one is baked or uses its fallback.

**Cost.** Each written channel adds a UV per vertex to the mesh. That is small next to
textures and isn't texture memory. The remap writes only the channels the output reads, and
only on the atlased vertices; other materials on the same mesh keep their own.

---

## Mesh remapper

The remapper moves each source's UVs into its region of the atlas and puts the output
material on the meshes. It runs in the build, in the same pass as the outputs, after Avatar
Optimizer has merged and reduced the meshes. The manual bake doesn't remap.

Per renderer whose submeshes use a source material of an output:
1. **Copy the mesh.** Shared mesh assets are never changed.
2. **Split shared vertices.** A vertex used by triangles of two sources, or of a source and
   a material that isn't atlased, gets one copy per user. Every attribute is copied with it:
   position, normal, tangent, UVs, vertex color, bone weights and each blend shape frame's
   deltas.
3. **Write UV0:** each source vertex's UV placed in the source's region: `region.min + uv ×
   region.size`, with the UV first brought into 0–1:
   - a triangle that lies within one UV tile is moved back into 0–1 by whole tiles, so it
     shows the same texels it showed with a repeating texture (and the coverage counts them
     the same way);
   - a triangle that straddles a tile edge, which no whole-tile move can fix, is clamped to
     0–1, with one information line per material giving how many;
   - triangles that a kept UV-tile feature needs in their tile, such as the world AO
     blocker's, keep their UV0. Tile features read the original tiles from the preserved UV
     channels;
   - triangles in tiles *Hidden UV tiles* removes (a vertex in a tile hidden on both sides)
     are left out, with one information line per material giving how many.

   Split vertices (step 2) let the triangles of one vertex move by different tiles.
4. **Write the preserved UV channels** the allocator assigned (see
   [*UV channels*](#uv-channels)), raw or with each source's tiling and offset.
5. **Vertex colors:** with the *Vertex colors* feature live, vertices of sources that don't
   use them get white.
6. **Swap the materials.** The source materials become the output material. Submeshes of one
   renderer that now share the output material are merged into one submesh, which is where
   atlasing saves draw calls. Other submeshes keep their order.

Bones, bind poses and blend shape names are unchanged. Material swaps in animations that
target a source material are part of the animation design and aren't handled yet. Merging
submeshes removes material slots, so an animation that targets a later slot of the same
renderer by its index would point at another slot: that's part of the animation design too.

---

## Textures

### Virtual maps and textures

Features work with **virtual maps**: one quantity each, such as base color, alpha, metallic,
roughness, occlusion, emission, a matcap mask or a glitter mask. The output shader declares
its **textures**: the texture slots its shader has, and the virtual maps each one provides. A
texture provides one virtual map, or several in their own channels: a **compound texture**,
for example metallic in R, occlusion in G and smoothness in A.

- A feature that uses a virtual map activates the texture that provides it.
- A texture is **required** when any virtual map it provides is required by the chosen modes,
  and optional otherwise.
- A texture is listed only while at least one of its virtual maps is in use. Channels whose
  virtual map isn't in use get the neutral value the output shader declares for them (for
  example metallic 0, occlusion 1).
- Packing fills each texture from its virtual maps, with the conversions the layout asks for
  (roughness to smoothness, linear or sRGB).
- A virtual map that no texture provides isn't available, and modes that need it aren't
  offered.

So the user picks one size per texture, not one per channel. The shared code never knows a
shader's packing, and the writer never computes a map.

### Texture settings

The *Textures* section lists the textures in use, required ones first:
- Each row names the texture and the virtual maps it carries in this configuration, for
  example *Metallic map: metallic, roughness, occlusion*.
- **Required** textures are always on.
- **Optional** textures have a checkbox. Turning one off applies the fallback of each virtual
  map it carries (below).
- **Sizes** follow a size mode per texture:
  - **main aspect** (UV-space maps: color, normal, masks, surface maps), a size from the
    sizes that keep the main texture's aspect. A new one starts at the main size, or at half
    of it for the mask allocator's *Masks* textures, which need less detail;
  - **square** (matcaps, generated rim matcaps);
  - **custom** (ramps and other lookups).
- Each row shows its memory for PC and for Android, and the section shows the totals for
  both (see [*Memory*](#memory)).
- After the atlas textures come the textures the build **generates** at a fixed size (combined
  matcaps, rims drawn into matcaps, a generated shadow ramp), compressed with the formats for
  their kind, and the sources' textures the output material **uses as they are** (a matcap in
  its own slot, a ramp, a cubemap): their memory as imported, and on Android their Android
  override or the Android format for their kind. Both count in the totals. The overview knows
  them from a resolve of the sources, kept while the settings and sources stay the same.

The main texture size is chosen in the section, or by auto setup (see [*Auto setup*](#auto-setup)).

### Optional maps and fallbacks

Every optional virtual map names the map its content goes to when its texture is turned off,
so turning a texture off simplifies the result instead of dropping it. When a compound texture
is turned off, each of its virtual maps falls back on its own.

| Optional virtual map | When its texture is turned off |
|---|---|
| Secondary and detail normal maps | Baked into the main normal map |
| Decal textures | Baked into the base color map (and its emission into the emission map) |
| Detail color maps | Baked into the base color map |
| Emission map | Baked into the base color map (the *baked into color* mode of emission) |
| Occlusion map | Baked into the base color map (see *Ambient occlusion baked into color* under [*Feature inventory*](#feature-inventory)) |
| Metallic, roughness, reflectance, specular color, clear coat and thickness maps | The reference source's constant |
| Shadow color and ramp masks | The reference source's values |
| Mask textures | The allocator's other places, or a mode without a mask (see [*Masks*](#masks)) |

A layer that keeps a map of its own is live, so it can scroll, animate or show only in some
views. Baked in, it is fixed at its current state, like any baked animated value: a flipbook
at its current frame, a scrolling decal at its current position. A decal shown only on back
faces or only in mirrors is baked only when its condition is the normal view's; otherwise
turning its map off drops it.

### Memory

The memory shown is the **GPU memory of the compressed texture**: the figure Unity reports
for a texture, and the one VRChat's performance rank counts as texture memory. It is
computed, not measured, from:
- the size and the mip chain (a full chain adds about a third);
- the format each platform gets, which depends on the texture's kind and whether it has alpha:
  DXT/BC formats on PC, ASTC or ETC formats on Android. The format per kind and platform is a
  setting of the output, so the estimate always matches what the export writes.

Crunch and the PNG file size don't count: crunch only changes the download size. Mip
streaming doesn't lower the figure either: fewer mip levels may be loaded at a distance, but
the rank counts the full texture as the worst case.

The *Textures* section shows each texture's memory on both platforms and the material's
totals:

```
Texture          Size         PC        Android
Base color      2048x2048    ...       ...
Normal           2048x2048    ...       ...
Matcap 0         512x512      ...       ...
Total                         ...       ...
```

The Advanced Atlaser component shows the totals over all its outputs as well.

### Export rules

- **Mipmaps** are made with the Kaiser filter, which keeps them sharper than a box filter, and
  stream (NDMF warns about generated textures that don't). The build makes its textures with
  Unity's texture generator, the importer's own pipeline, so they match the manual bake's.
- **No unused alpha.** A texture gets an alpha channel only when a virtual map uses it. Where
  it does, `OutputChannels.Auto` still drops alpha that is 1 everywhere.
- **Normal maps** are exported as normal maps.
- **Crunch compression** is one setting per output, on by default. It applies where the
  platform's format has a crunched form: BC1 and BC3 on PC (BC5, BC7 and ASTC have none). Each
  such texture is compressed both ways, and crunch is kept only when the crunched size is below
  90% of the size without it. Otherwise the texture keeps normal compression without crunch.
  Crunch changes the download size, not the memory size.
- **Color space** follows the virtual map: color maps sRGB, data and masks linear.

---

## Baking toolkit

Bakes that need geometry or light share one toolkit in the Atlaser (not in the Texture
Wrapper, which stays shader-neutral).

### Bakeable subset

Baking means computing on the CPU what the source shader would draw: layers, blends, masks,
color adjustments and texture mapping (placement, tiling, rotation), and for the bake-ins the
lighting families. That is a large part of the feature catalog. The CPU evaluator therefore
covers an explicit **bakeable subset** that grows feature by feature, with tests against known
values. Content outside it is never guessed at: its feature uses a native mode, or the bake
falls back to the source texture as it is (without the unsupported layer or operation).

### Memory budget

A 4096 × 4096 float RGBA map takes 256 MB, so bakes keep few full-size maps alive at once:
- a texture is baked source by source: each of the source's virtual maps is evaluated,
  copied into the source's content and released before the next, and the content is placed
  in the atlas and released before the next source;
- the world normal exists only while a bake-in needs it;
- temporary layers are mixed in and released before the next one is made;
- sources are processed one after another, never side by side.

Half-precision buffers or GPU baking are possible later, if the budget proves too tight.

### Geometry buffers

The geometry bake keeps, per source and at its bake size:
- the coverage mask (as today);
- the **world normal** of the current mesh with the combined normal map applied (pipeline
  step 3), in avatar-root space;
- the **overlap map**: texels covered by more than one triangle (mirrored or stacked UVs), as
  the normal correction's coverage map already finds.

The tangent frame maps are needed only while building the world normal and are released
right after, so the world normal is the only full-size float map that stays alive per
source.

Without the build (the manual bake), the current mesh is the source mesh: the normal
correction is flat, and everything else works.

### Virtual light rigs

Light-dependent bakes use a fixed rig of three lights in avatar-root space (the avatar faces
+Z), plus an ambient term. Each light has a role, and its own color, softness and weights:

| Light | Temperature | Softness | Diffuse | Specular | Role |
|---|---|---|---|---|---|
| Key | slightly warm, about 5000 K | medium: an area light about 10–15° across | 1.0 | 1.0 | the main form and the main highlight |
| Fill | cool like sky light, about 7500 K | very soft: a wide area | 0.35 | 0.1 | lifts the shadow side without a second highlight |
| Back (hair) | neutral to cool, about 6500 K | fairly sharp | 0.6 | 0.6 | a crisp edge on the silhouette, highlights on hair tops |
| Ambient | sky above (cool), ground below (warmer) | a hemisphere | 0.25 above, 0.1 below | — | the floor of the shading; what occlusion darkens |

- **Softness** widens the highlight and softens the edge between the lit and the shadow side.
  It is approximated by widening the roughness each light sees by its angular size, and by a
  little diffuse wrap, rather than by integrating an area light.
- **Diffuse and specular weights** let a light shape the form without adding a highlight (the
  fill), or add a highlight without flattening the shading.
- **The ambient term** is a sky and ground gradient, not a flat color, so occlusion darkens
  light that comes from somewhere.

There are two rigs, chosen per output. They share the lights' settings; only the directions
differ:

| Light | Classic | Symmetric |
|---|---|---|
| Key | front, above, to one side | front, centered, above |
| Fill | front, low, the other side | front, centered, low |
| Back (hair) | behind, above | behind, centered, above |
| Ambient | sky above, ground below | sky above, ground below |

The classic rig needs unique UVs. A source whose UVs overlap (mirrored halves) is baked with
the symmetric rig, even when the output chooses the classic one; the *Baking* section lists
those sources. Because the lights' settings are shared, those sources still match the rest of
the atlas. All values are starting values, to tune with real avatars. More rigs can be added
as presets.

**Light color in baked textures.** A baked light color stays in every world, on top of the
world's own light. So the light's color is baked only by a **color influence** (0: the
light's brightness alone; 1: its full color), low by default (about 0.25). Form shading
divides by the average light, so the overall color stays neutral and only the shift between
the warm lit side and the cool shadow side remains.

**Light terms per bake.** Every bake uses the same rig, but only the terms its purpose
needs:

| Bake | Uses |
|---|---|
| Full light, form shading | every light, diffuse and specular |
| Normal-map shading | key and fill, diffuse: detail reads evenly over the surface |
| Highlights, glitter emulation | the specular terms |
| Backlight, rim bakes | the back light |
| Occlusion into color | the ambient term and reflections; direct light by the source's *occlusion: direct* |
| Subsurface | not baked (native or off) |

**Influence sliders.** The *Baking* section shows, folded by default:
- the rig (classic or symmetric) and an overall strength;
- the color influence;
- one strength per bake in use: form shading or full light, normal-map shading, highlights,
  backlight and rims, occlusion;
- a *Lights* foldout with each light's intensity, temperature and softness. The directions
  stay fixed by the rig, so the symmetric rig stays symmetric.

The rig is evaluated with each source's own lighting family (3.2, 3.3) and surface response
(2.4): world normal, base color, roughness, metallic, specular color, occlusion.

Two bake results:
- **Full light:** the lit color, for outputs that don't light (unlit or flat).
- **Form shading:** the light divided by its average over the source, so its average is 1.
  It adds shape to outputs that light flatly, without changing the overall brightness. A
  strength blends it in.

**Normal-map shading** for outputs without normal maps is the ratio of the shading with and
without the normal map, `shade(N mapped) / shade(N mesh)`. It adds the normal map's detail
without shading the mesh's form twice.

### Virtual view

View-dependent features (matcaps, rims, and the reflection part of baked occlusion) are baked
for a virtual view. The default is a camera in front of the avatar. Each texel uses the view
toward it from that camera, blended toward a view along its own normal where it faces away,
so sides and back aren't baked as a silhouette everywhere.

### Overlapping UVs

Texels shared by mirrored or stacked triangles can't hold two different light-dependent
results. Sources with overlapping UVs use the symmetric rig, so mirrored halves get the same
light. Texels that still differ (for example stacked copies with different shapes) get the
average of the overlapping triangles' results.

---

## Feature inventory

The initial features, with the unified features they cover, their modes, and their maps.
*Maps* are virtual maps (see [*Virtual maps and textures*](#virtual-maps-and-textures)).
Required maps come first, marked **(Required)**: the native modes can't work without them. The
others are optional, with the map they fall back to after the arrow. Masks are placed by the
allocator (see [*Masks*](#masks)). *Merge* uses the strategies above.

### Surface

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Color** | 2.1 base color<br>4.1 surface layers targeting color (details, decals, recolor regions)<br>1.10 static adjustments | 1. Native | base color **(Required)**<br>decal textures → base color<br>detail color maps → base color | Bake. Decals and details keep maps of their own only in the output's slots for them. A layer that tiles in UV0 can't stay on the atlas UVs, where its tiling would differ per region: it keeps a map of its own on a preserved UV channel (see [*UV channels*](#uv-channels)), or is baked. Vertex color is its own feature (below) |
| **Alpha** | 2.2 alpha, modifiers<br>6 surface mode, cutoff | 1. Base color alpha (native)<br>2. Off | alpha **(Required)**, in the base color's alpha, when the surface mode uses it | Only modes that give a real see-through effect are offered; there are no emulations. A transparent source either keeps its transparency (when the output can blend it, see *Surface mode*) or is drawn solid. Each source's alpha is baked (its source, mask, offset and strength) and converted to the output's surface mode: under *cutout*, a cutout source's cutoff is moved linearly to the shared cutoff 0.5; under a blended or dithered mode, a cutout source's alpha becomes 0 or 1 at its cutoff. Sources without alpha get 1. Fades, vertex alpha, dithering and masks on the mask bus, vertex colors or levels aren't baked yet; the build lists them |
| **Normal maps** | 2.3 normals<br>the normal correction and the pre-baked normal<br>4.1 layers targeting the normal | 1. Native<br>2. Baked into color (normal-map shading)<br>3. Off | main normal **(Required)**<br>secondary normal → main normal<br>detail normal → main normal | Bake, combined in the established order. A secondary or detail layer is offered a map of its own when every source using it has the same texture and a preserved UV channel is available for it; otherwise it is always baked into the main normal |
| **Color adjustments** | 1.10 | 1. Baked (static adjustments, always)<br>2. Native (live adjustments, where the output has the operation) | — | Static adjustments are baked into the maps they target. Live ones (animated or modulated) are shared values; when they differ, the reference source's value is used |
| **Vertex colors** | 2.1 vertex color on the base color and alpha<br>1.4 masks reading vertex color<br>1.5 vertex color steps of the mask bus<br>other uses (transition space, anisotropy, outline and fur directions) | 1. Live<br>2. Baked<br>3. Off | base color, alpha and mask maps, when baked | Shown when a source's material uses vertex colors; the user chooses. *Live*: the vertex colors stay on the mesh and the output multiplies its base color (and alpha, where it can) by them, with one setting (amount, sRGB decoding). The remap rewrites each source's vertex colors so that setting gives what its own did: sources that don't use them get white, and other amounts are solved exactly; a different sRGB decoding matches at the vertices and can differ slightly between them (when the sources disagree on it, the user picks the output's: *Vertex colors as sRGB*). Masks and mask bus steps reading vertex colors are baked. *Baked*: the vertex colors are painted in UV space (`AtlasVertexColors.cs`) into the maps they affect; texels shared by triangles with different colors get the average. *Off*: they are ignored (read as white) |
| **Reflection and specular** | 2.4 metallic, roughness, reflectance, specular color, highlights, reflections<br>4.3 cubemap lookups | 1. Native<br>2. Off | cubemaps **(Required)**, for cubemap lookups<br>metallic → constant<br>roughness → constant<br>reflectance → constant<br>specular color → constant | Constants that differ become maps (spatial). Reflectance is one value on the current outputs: fixed at 0.5 in Poiyomi's Reflections & Specular, and Toon Standard's gloss map × `_Reflectance`, where each source needs its reflectance over its smoothness; when those differ, it comes from the reference source. Highlight and reflection groups are shared values with their strengths in a mask. A cubemap is a lookup layer and takes a slot |
| **Clear coat** | 2.4 clear coat | 1. Native<br>2. Into the gloss map (outputs without a coat)<br>3. Off | clear coat strength and roughness **(Required)**, native<br>clear coat reflection and highlight masks, native<br>metallic and roughness **(Required)**, into the gloss map | Spatial value for strength and roughness (no coat: strength 0); strength in mask for the reflection and highlight strengths, relative to the strongest (one value everywhere: no mask; a highlight mask equal to the reflection mask reads its channel). The rest is a shared value from the reference source (*Clear Coat from*). *Into the gloss map* approximates the coat with the output's one specular layer: in coated parts the gloss is raised toward the coat's, `lerp(base, max(base, coat), coat strength × coat reflection strength)`, never lowered, so they get real, sharper highlights and probe reflections; without highlights in any source, the feature asks for the metallic and roughness maps itself. A surface under the coat becomes as glossy as the coat there, and the coat's tints and normal map amount aren't kept (listed) |
| **Ambient occlusion** | 2.4 occlusion | 1. Native<br>2. Baked into color<br>3. Off | occlusion **(Required)**, for the native mode | Bake. *Baked into color* is for sources with an occlusion map when the output has none, or to save the texture. See below |

### Lighting

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Shading, lighting and shadows** | 3.1–3.4 | 1. Native (the writer maps the family and parameters to its own modes)<br>2. Baked full light (outputs that don't light)<br>3. Baked form shading (outputs that light flatly) | shadow color → reference values<br>ramp masks → reference values | Exclusive: one lighting setup, from the reference source. The writer maps unified families to its shader's lighting modes, for example the ramp toon special cases (3.2) to Poiyomi's modes. Spatial values (shadow tint, ramp strength) become maps when the output has them |
| **Light volumes** | 3.5 | 1. Native<br>2. Off | — | Part of the lighting setup: the reference source's |
| **LTCGI** | 3.5 | 1. Native<br>2. Off | — | As light volumes |

### Layers

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Matcaps** | 4.3 lookup layers with a 2D texture | 1. Matcap slots<br>2. Baked into color (surface placement) or emission (overlay and emission placements)<br>3. Off | generated matcap textures, square, for combined slots<br>masks (allocated), one per slot | Slots. A source's matcap is its consecutive lookup layers sharing texture and mask (one per blend weight). Matcaps that look the same in several sources share a slot. The user places each one; *Automatic* combines matcaps on the same parts with the same lookup, gives the largest their own slots, and combines the rest into the most similar slot. A combined slot gets a generated texture: matcaps on the same parts stack (textures × colors × weights added for *add*, `lerp(1, texture × color, weight)` multiplied for *multiply*); stacks on different parts are averaged by area, since each part shows the one texture. An output without a matcap color also gets one for a tinted matcap. An output without base color mix takes it through the slot's mask: the mask × `lerp(1, luminance(base color), mix)`. Blends the output lacks are approximated: add, add to light, screen and mixed brighten, so they add; replace and multiply multiply; several weights take the heaviest. Approximations, combinations and left-out matcaps are listed. Baking needs the world normals and the virtual view, so it runs after the normal maps |
| **Rim lighting** | 4.4 angle layers, edge source view angle | 1. Rim slots<br>2. Baked<br>3. Off | generated matcaps, square, for rims in matcap slots<br>masks (allocated), one per slot | Slots, placed per rim like matcaps, with *As matcap* added. A rim that depends only on `N·V` (band, power band or step, no light side, no texture) is drawn exactly into a generated matcap (`AtlasRimCurve`): alone, its color with the rim × opacity in alpha where the slot reads alpha as opacity; otherwise premultiplied, rims on the same parts stacked and on different parts averaged by area. *Automatic*: a rim slot while one is free (on an output whose rim has no mask, only for a rim that is the same everywhere), then a free matcap slot, then a rim slot shown on every part (listed), then combined with the most similar slot. Combined in a rim slot, the slot's rim shows on each member's parts. A writer whose rim is a band fits other rims to it: the same midpoint and the same rise from 10% to 90% |
| **Environmental rim** | 4.4 color source environment | 1. Native<br>2. Off | mask (allocated) | Shared value: the settings from the reference source (*Environmental Rim from*), each source's strength in the mask relative to the strongest |
| **Backlight** | 4.4 edge source backlight | 1. Native<br>2. Baked with the rig's back light (form shading)<br>3. Off | backlight color, half size, where the sources' colors differ | Shared value: the settings from the reference source (*Backlight from*); each source's color × texture in the backlight color map (black without), normalized to its brightest value. Light dependent, so no matcap emulation |
| **Emission** | 4.1 layers targeting emission<br>emission amounts of other layers | 1. Native<br>2. Off | emission **(Required)**, for the native mode | Bake, relative to the strongest source's emission, which the output glows at. No *baked into color*: it wouldn't glow, so it isn't the effect (see *Decisions*). Live parts (blinking, scrolling, audio) stay native only where the output has the same modulator; otherwise they are left out |
| **Glitter and sparkle** | 4.6 glitter pattern<br>2.4 glints | 1. Native<br>2. Emulated<br>3. Off | mask (allocated), for the native mode<br>emission, emulated<br>metallic and roughness, emulated<br>base color, emulated | Native: shared values and a mask. *Emulated* generates the glitter cells as a static pattern in UV space and writes it into any combination of the three targets, each optional with its own strength: bright points in the emission map, metallic and smooth points that sparkle in reflections, lighter points in the base color. A target whose map is turned off is left out |
| **Subsurface scattering** | 4.5 subsurface | 1. Native<br>2. Off | thickness → constant | Thickness maps are baked per region. Too dependent on the light to bake or emulate |

### Effects and render

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Proximity color** | 1.9 a modulator with source *camera distance*, driving a color adjustment | 1. Native<br>2. Off | mask (allocated) | Shared values; strength in mask |
| **AudioLink** | 1.3 AudioLink themes<br>1.9 audio modulators<br>4.6 audio patterns | 1. Native, per feature<br>2. Off (the feature keeps its rest value) | Per feature | Each feature that AudioLink drives keeps it only where the output supports AudioLink for that feature. AudioLink modes keyed to UV tiles read a preserved UV channel (see [*UV channels*](#uv-channels)) |
| **Surface mode and render queue** | 6 | 1. Automatic: the most see-through mode among the sources that the output supports (opaque, cutout, dithered, transparent, fade, in that order). When transparent or fade sources mix with others, a **fade with depth writes** (Poiyomi's TransClipping): the solid parts, at alpha 1, sort like opaque ones and the transparent sources keep their alpha; a plain blended mode would make the whole avatar sort wrongly. An output that can't do that gets the most see-through of the other sources' modes, and the transparent sources are drawn solid (alpha 1), never cut or dithered. A source whose alpha is turned off counts as opaque<br>2. A chosen mode, including *Fade with Depth* | — | Always on. The queue follows the mode (the writer's queue for it); a queue override isn't there yet. Additive, multiplicative and custom blends aren't merged yet: their sources count as opaque |
| **Fallback shader** | 6 (not typed) | 1. Automatic: the writer's choice<br>2. Toon, Unlit, Standard or Hidden | — | Always on, where the writer sets one. The writer follows the surface mode and culling (Poiyomi: `VRCFallback`, such as `ToonCutout` or `ToonFadeDoubleSided`) |
| **Hidden UV tiles** | 6 tile discard | 1. Live<br>2. Removed from the mesh<br>3. Off | a preserved UV channel, live | *Live*: each source's UVs (in its tiles' channel) go into a UV channel the allocator gives, moved by whole tiles where the output would hide them differently; a tile keeps its number where the sources agree, so animated tile toggles keep their meaning there. Sources without hidden tiles get UVs outside the 4 × 4 grid. One side can be hidden live where the output has it (Poiyomi's Face Discard); a second side is listed. *Removed*: the remap leaves out the triangles with a vertex in a tile hidden on both sides, on any output; one-sided tiles are listed. *Off*: shown |
| **World AO blocker** | 6 render (to be typed; the Poiyomi parser ignores it today) | 1. On<br>2. Off | — | One switch for the whole material. Offered when a source uses it and the output supports it; the user chooses. See below |

**Ambient occlusion baked into color.** Native occlusion darkens ambient light and
reflections, not direct light (2.4), so multiplying the color by the occlusion map would
darken the lit side too. Instead, the bake evaluates each source's occlusion under the virtual
light rig, with the world normals and the virtual view. Each texel is darkened by the share of
its light that occlusion would darken: the ambient part, the reflections, and the direct part
only by the source's *occlusion: direct* amount. Texels facing the key light keep most of
their brightness; creases facing away get the full darkening. A strength blends the result
in, and a source's occlusion tint (2.4) colors the darkening.

**World AO blocker.** Triangles whose UVs lie in the tile left of the default one (U −1 to
0, V 0 to 1) render to depth only. Screen-space effects that read depth, such as ambient
occlusion, treat them as solid, while they stay invisible. When it is on, it reads the tile
from a preserved UV channel, or from UV0, where the remap leaves those triangles in their
tile: clamping or remapping them into the atlas would make them visible. When it is off, they
render like any other triangle, as they would in the source shader without the option.

### Later phases

These are covered by the same model and come after the initial features: outlines (5.1,
slots and exclusive values), fur (5.2, exclusive with a length mask), transition and dissolve
(4.2), the other procedural patterns (4.6), refraction (4.7), highlight layers (4.8, slots),
parallax (2.5, height map baked per region), deformations and geometry effects (5.3, 5.4),
second pass and visibility (6), screen-space lighting (3.6).

---

## Output shaders

An output shader keeps only what is specific to it:

| Part | Content |
|---|---|
| Identity | ID, display name, `FindShader` (as today); its family and its rank within the family, for picking the output shader (see [*Auto setup*](#auto-setup)) |
| Capabilities | In unified terms: the lighting families and modes it maps, slot counts per layer type (lookup, angle, surface, pattern kinds), per-feature masks and whether they pick a channel, mask bus channels and steps, external lighting, render modes, AudioLink per feature, modulators |
| Texture layout | Its textures, the virtual maps each provides in which channels, and each channel's neutral value |
| Writer | One unified material → shader properties, keywords, render queue, fallback, lock and animation tags |
| Mappings | Where its own modes differ from the unified ones, for example a unified lighting family mapped to the nearest of its lighting modes. Shared emulation decides *what* to produce; the writer only decides *how* its shader expresses it |

`Fields`, `AutoConfigure` and `OnConfigurationChanged` move to the shared features. An output
shader may still add a few fields of its own (for example a shader-specific option with no
unified meaning) in its own section.

### Adding an output shader

A new output shader is its capabilities, its texture layout and its writer; merging, baking,
allocation, UI and export are shared. Where the shader is also an input, the parser and its
inventory already document every property's meaning, so the writer is largely the parser in
reverse, and round-trip tests (parse, write, parse, compare) check it.

| Output | Work |
|---|---|
| Poiyomi Toon 11 | Extends the Poiyomi 10 writer, since 11 keeps 10's property names: 11's added capabilities, and the features new in 11 |
| Poiyomi Pro | Extends the Poiyomi 11 writer: more slots (three outlines, for example) and the Pro-only features. Offered only where Pro is installed. Pro is a paid shader: its writer describes it in our own words, as its parser does |
| Mochie Uber | A new writer, mostly the existing Uber parser in reverse |

What adds work is a feature or mode that no shared feature covers yet (built once, then
available to every output), and shader constraints that the capability model can't describe
yet, such as unusual mask packing or fixed UV channels. Input shaders are independent of all
this: a new input is a parser and an inventory, with no change to any output.

---

## Settings and UI

### Source analysis cache

The inspector needs each source's analysis: its parsed unified material, and which features
it uses with how many instances. Parsing every source on every redraw is too slow, so the
analysis is cached per source material and rebuilt only when its key changes:

| Key part | Catches |
|---|---|
| The material's instance ID and `EditorUtility.GetDirtyCount(material)` | Changes to the material's serialized properties: edits in an inspector, through Undo or `SerializedObject`, keywords, a texture or shader swapped |
| The shader's instance ID | Another shader assigned |
| A global version, raised by an `AssetPostprocessor` when a texture or shader is imported | Texture sizes or import settings changed, or a shader updated on disk, while the material itself is unchanged |

Reading these is cheap enough for every redraw. A domain reload clears the cache, which also
covers changed parser code. The build parses its sources itself and doesn't use the cache.

To verify: a script that changes a material with `Material.SetFloat` and similar, without
`EditorUtility.SetDirty`, may not raise the dirty count. If that matters in practice, the
fallback is a hash of the material's saved properties, checked when the inspector gains focus
rather than on every redraw.

### Stored settings

Settings are stored by feature ID, not by output shader, so changing the output shader keeps
every choice the new shader supports:
- **Feature settings:** feature ID, mode, the mode's values, per-source overrides (turned off,
  reference source), stored by source material.
- **Texture settings:** texture ID, enabled, size, size mode.
- **Output settings:** crunch, bake view, light rig, color influence, the bake strengths,
  and each light's intensity, temperature and softness.

A mode the new shader doesn't support falls back to its default mode; the stored choice is
kept, so switching back restores it.

### Layout

```
Output name, output shader, sources ("Source 1 (Body)") and UV regions
[Auto Setup]
▸ Textures            main size; a table: texture, size, PC and Android memory, totals
    ▸ GPU Compression Formats
▸ Material Features   one row per feature: name and mode
    Surface           Color · Alpha · Normal maps · Color adjustments · Vertex colors ·
                      Reflection and specular · Clear coat · Ambient occlusion
    Lighting          Shading · Light volumes · LTCGI
    Layers            Matcaps · Rim lighting · Environmental rim · Backlight ·
                      Emission · Glitter · Subsurface
    Effects           Proximity color · AudioLink
    Render            Surface mode and queue · Fallback
▸ Masks               where each mask went (read only)
▸ Baking              light rig, strengths, lights, view     (only when a mode bakes)
▸ UV layout preview
[Bake to Assets…]
```

- Each feature is one row: its name, then its mode. Where two or more sources have it, the
  row folds open to a list that turns it off for single materials; folded, the name says when
  it's off for some (*Matcaps (3 of 5 materials)*).
- The group headings (Surface, Lighting, …) only appear once features from more than one
  group are shown.
- Features no source uses, and features without an available mode, aren't shown.
- The memory figures sit in fixed, right-aligned columns, so they don't clip in a narrow
  inspector.
- Auto setup chooses a default mode per feature: native when supported, the cheapest
  emulation when not, off when neither.

### Auto setup

**Picking the output shader** happens once, when an output component is created (from the
Atlaser's *Add Atlas Output* button, the *Add Component* menu, or any other way it appears
without a shader chosen). It never changes a shader chosen before.
- Every parser and output shader belongs to a **family**: Poiyomi (Toon and Pro, every
  version) and VRChat Mobile. Input shaders without an output of their own (Unity Standard,
  Mochie Uber until it gets a writer, the fallback) belong to none.
- The materials the Atlaser found that no other output uses vote for their family (all the
  materials it found, when every one is already used). The family with the most votes and an
  installed output shader wins; a tie goes to the family whose ID comes first alphabetically
  (Poiyomi before VRChat).
- Within the family, the installed output shader of the **highest version** is chosen. At the
  same version, Pro comes before Toon: it is a superset, and it's only installed where the
  user has it.
- Without a winning family, the installed output shader of the alphabetically first family,
  at its highest version, is chosen; without any, none is, and the inspector says that no
  supported output shader is installed.
- The rank is the major version × 10, plus 1 for Pro: Poiyomi Toon 10 is 100, Pro 11 is 111.

**The Auto Setup button** sets up the output for the sources already assigned to it, in one
Undo step. It never adds or removes sources, and never runs by itself: a new output has no
sources, and running it whenever a source is added would overwrite choices made since.
1. **UV regions:** each source gets a region the size of its base color texture (keeping
   its aspect; a source without one gets the smallest texture size, 128), so texture detail
   stays even. The regions are packed largest first (MaxRects, best short side fit) into the
   smallest atlas of supported texture sizes that holds them at full size, up to 4096 per
   side. When none does, the atlas is 4096 × 4096 and every region is scaled down alike, in
   whole percent, as little as still fits. Sizes and positions are worked out in hundredths of
   a pixel, so the regions are exact decimals (0.8 and 0.2, not 0.8000488); the bake's Blit
   snaps them to whole pixels where the content is placed. Then the free space goes to the
   sources that were scaled down, the largest texture first: each grows toward its full size,
   in whole percent, as far as everything still fits, so a small source next to the free
   space gets its full detail at no cost. Last, when that still leaves free space, a dialog
   asks whether to **stretch to fill**: every region then grows into the free space beside it
   (up, right, down, left, to the nearest region or the atlas edge), the largest texture first,
   so the atlas is used whole. No region loses detail and some gain it along one axis, but the
   textures are no longer in proportion; *Keep Proportions* leaves the free space empty.
   For example: four 4096 textures become four 0.5 × 0.5 regions, and a 4096 body
   with three 1024 textures beside it gets 80% of its size. The regions tile the atlas; the
   padding isn't part of them (see *Region padding*).
2. **Main texture size:** the packed atlas's size.
3. **Feature modes:** each feature's default mode, as before: native when supported, the
   cheapest emulation when not, off when neither.
4. **Texture settings:** back to their defaults. Turning optional textures on only where a
   source uses what they hold, sized from the sources' own textures, comes with the first
   optional texture (phase 2.2).

**Region padding** keeps the sources apart without changing the regions the user sees.
With two or more sources, the map bake places each source's content inside its region, inset
by half the padding (rounded up) on every side, and fills that ring with the content's edge
(the Texture Wrapper's `Blit` with `padding`); the mesh remapper moves the meshes' UVs into
the same inset rectangle (`ContentRegion`, on the main texture's pixels), so they sample
exactly the content. A 0.5 × 0.5 region on 4096 with 4 px padding holds its content in
2044 × 2044 px. A texture of another size gets the padding scaled to it, at least one pixel.
Later, the coverage could show where a source already has room at its edges, so no padding
is added there.

When a source's region differs from both the whole texture and the layout Auto Setup would
make now, replacing it would lose an edit, so a dialog asks first: *Replace*, *Keep My
Regions* (the regions and the main texture size stay; the rest is set up) or *Cancel*.
Afterward, the console log says what it did, with each source's scale when they were
scaled down.

---

## Manual bake

A **Bake to Assets…** button on each output:

1. Asks for a folder inside `Assets` and a material name, defaulting to the output name.
2. Runs the output pipeline in edit mode on the avatar as it is: the sources are parsed, the
   current meshes are the source meshes, coverage comes from them, and the normal correction is
   flat (it needs the reduced mesh of a build).
3. Writes the textures as `<material name>_<texture>.png` (for example `Body_BaseColor.png`,
   `Body_Normal.png`, `Body_Matcap0.png`) with the Texture Wrapper's PNG exporter and its import
   settings, overwriting textures of the same name.
4. Creates `<material name>.mat` with the output shader. If it exists, asks before replacing
   it.

The build and the manual bake run the same pipeline; only the meshes and the export target
differ.

---

## Phases

The phases and milestones are in [ATLAS_OUTPUT_PLAN.md](ATLAS_OUTPUT_PLAN.md).

## Tests

- **Round trips:** for each feature a writer supports natively, parse → write → parse gives
  an equal unified material.
- **Merging:** two or more test sources with known values produce the expected slots, masks
  and maps (equal instances share a slot, different ones don't, overflow follows the mode).
- **Capabilities:** a test output shader with chosen capabilities offers exactly the modes they
  allow.
- **Bakes:** small synthetic meshes (a quad, a sphere, mirrored halves) give the expected
  values at known texels.

---

## Decisions

- **Written materials are read back:** after writing, the build parses the material with the
  output shader's own parser and compares its lighting with what the plan asked for (in a
  canonical form, settings without effect in the setup at their defaults). What differs is
  listed, so a writer's approximations are reported without rules of their own. A same-shader
  round trip lists nothing.

- **Only modes that give the effect:** a mode is offered only when its result is the feature
  as the user would recognize it. Emission isn't baked into the color, since it wouldn't
  glow; a transparent source isn't cut out or dithered when the output can't keep it
  transparent. Where the output can't, the feature is off for those sources.

- **No result left to chance:** where the output can't give every source what it had (a
  shared value the sources disagree on, a source the output can't reproduce), the user
  gets a control that decides it, shown in the feature's row only while the conflict
  exists, such as the reference source drop-down or an on/off switch
  (`IAtlasConflictSwitchFeature`: alpha to coverage). A silent rule the user can't change
  isn't enough: the avatar could look wrong with nothing the user can do about it. The
  automatic choice stays the default, and the build log says what it chose. Parts that
  aren't merged yet are listed in the diagnostics until they are.

- **Crunch fallback:** when crunch saves less than 10%, the texture keeps normal compression
  without crunch.
- **Light rigs:** two rigs, classic and symmetric. Sources with overlapping UVs are always
  baked with the symmetric rig.
- **Light roles:** each light has its own temperature, softness, and diffuse and specular
  weights. Each bake uses only the light terms its purpose needs. Baked light color is
  limited by a color influence, and every bake has its own strength slider.
- **First writers:** Poiyomi Toon 10 and VRChat Mobile Toon Standard, both from the first
  milestone.
- **Compound textures:** an output shader's texture can provide several virtual maps in its
  channels. Using any of them activates the texture; it is required when any of them is
  required, and has one size for all its channels.
- **Maps are the user's choice:** that the output has a map doesn't mean it is used. Only the
  maps a mode can't work without are required (for normal maps, only the main one). An
  optional map that is turned off is baked into the map it falls back to: secondary and detail
  normals into the main normal, decals and details into the base color.
- **Inverted masks:** a placed mask also serves features that need its inverse, when they can
  read it inverted, without another channel.
- **Ambient occlusion:** a source's occlusion map is baked into the color map, under the light
  rig, when the output has no occlusion map. Occlusion generated from the mesh is not planned.
- **Memory:** GPU memory of the compressed textures, for PC and Android, per texture and in
  total per material.
- **World AO blocker:** typed as a render feature, one switch for the whole material, offered
  when a source uses it and the output supports it. The UV remap keeps its tile when it's on.
- **Input reports:** the input parsers' *unmapped* lines are logged only with the debug
  setting, not in normal builds.
- **Vertex colors:** when a source uses them, the user chooses to keep them live, bake them
  into textures, or turn them off.
- **Manual bake:** writes the material and its textures only; no preview copy of the avatar.
- **UV channels:** UV0 holds the atlas UVs. UV1 to UV3 carry preserved original UVs, with
  different content per source, allocated like masks: for tiled layers kept live, UV-tile
  features, and features that read other UV channels in the source.
- **UVs outside 0–1:** a triangle within one UV tile is moved into 0–1 by whole tiles; only a
  triangle straddling a tile edge is clamped. This replaces clamping every UV.
- **Source analysis cache:** parsed sources are cached per material, keyed by the material's
  dirty count, its shader, and a version raised when textures or shaders are imported.
- **Animation:** some animations will be supported, designed separately; this design only
  keeps room to record animated properties per source.
- **The first milestone** is one slice through the whole pipeline, the mesh remapper and the
  manual bake included, with the base color texture only. The main normal map and a compound
  metallic and roughness texture come first in the next milestone.

## Open questions

None at the moment.
