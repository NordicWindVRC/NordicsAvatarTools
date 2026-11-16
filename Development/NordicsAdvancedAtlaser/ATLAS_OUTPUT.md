# Nordic's Advanced Atlaser — Output design

**Status:** design. Nothing in this document is implemented yet, except what
[*Current state*](#current-state) lists. It describes how an atlas output turns several unified
materials into one material for an output shader, and which parts of that are shared and which
are shader specific.

Related documents:
- [UNIFIED_MATERIAL.md](UNIFIED_MATERIAL.md): the unified material and the input parsers.
- [UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md): the feature catalogue. Section
  numbers such as *4.3* in this document refer to it.
- [NORMAL_BAKER.md](NORMAL_BAKER.md): the normal correction bake that the output reuses.

---

## Goals

- **Features first, not shaders first.** Output processing is organised by unified feature.
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
| Output component | `Runtime/NordicsAtlasOutput.cs` | `AtlasOutputConfiguration`: output name, output shader ID, region padding, sources (material, UV region, normal correction, pre-baked normal map), main texture size, and a generic list of `AtlasOutputSetting` |
| Output shader interface | `Editor/AtlasShaderRegistry.cs` | `IAtlasOutputShader`: ID, display name, `FindShader`, `Fields`, `AutoConfigure`, `OnConfigurationChanged`, `Prepare(AtlasGeneratorJob)` |
| Output shaders | `Editor/Outputs/AtlasOutputShaders.cs` | Poiyomi Toon 10 and VRChat Mobile Toon Standard, both registered and generating nothing yet |
| Generator boundary | `Editor/Outputs/AtlasGeneratorJob.cs` | The unified materials, UV regions and bakes; no `Material` or `Shader` crosses it |
| Bake sizing | `Editor/Outputs/AtlasOutputLayout.cs` | Each source is baked at 1.5× its island size, at most 4096 |
| Geometry bake | `Editor/Build/AtlasSubmeshBaker.cs` | Per source: the current mesh's coverage mask and the tangent-space normal correction. The world-space normal and tangent frame maps exist only during the bake |
| Inspector | `Editor/NordicsAtlasOutputEditor.cs`, `Editor/AtlasOutputUI.cs` | Sources, UV regions, main texture size, the output shader's fields, auto setup, UV preview |
| Texture library | Texture Wrapper (`API.md`) | Float textures with usage flags, `Blit` with blend modes, masks and padding, colour operations, triangle painting, normal difference baking, dilation, PNG and in-memory export, `OutputChannels.Auto` |

The UV remapping of the meshes (moving each source's UVs into its region) is a separate,
pending part of the build. Output materials can be generated and inspected without it, but
can't be put on the built avatar until it exists.

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

1. **Surface inputs (S1):** base colour, alpha, metallic, roughness, occlusion, specular colour
   and emission, with the static surface layers and static colour adjustments that the chosen
   modes bake. Decals and details whose own maps are turned off are baked in here.
2. **Normals:** the main tangent-space normal map, in the established order: pre-baked
   normal, the build's normal correction, the primary normal, the surface layers targeting the
   normal. Layers that keep a normal map of their own are left out; layers whose map is turned
   off are baked in. The strengths are normalised to the largest among the sources.
3. **World normals:** the combined normal from step 2 in world space (avatar-root space, the
   current mesh's tangent frame), the input of every lighting-dependent bake.
4. **Lighting-dependent bake-ins:** normal-map shading, matcaps, rims, ambient occlusion
   and light, each into a temporary layer.
5. **Mix-in:** the temporary layers are mixed into the colour or emission maps by their
   targets and strengths, then the final static colour adjustments run.

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
| **Baked** | Resolved into texture content: colour, emission or surface maps |
| **Emulated** | Expressed with a different output feature, for example a rim as a matcap |
| **Off** | Not produced |

A feature shows only the modes whose capabilities the output has. A feature with no
available mode, or that no source uses, isn't shown.

### How instances merge

The merge strategies are shared building blocks; features combine them.

| Strategy | When | How |
|---|---|---|
| **Bake** | Static content placed in UV space | Evaluated per source and blitted into its region. Always available |
| **Shared value** | A setting that applies to the whole material | Equal values pass through. Different values: the value of the **reference source** (user's choice; default: the source with the largest region), or the largest or average where the feature says so |
| **Spatial value** | A setting that differs between sources and the output has a map for | Written per region into a map (for example roughness constants into a roughness map) |
| **Strength in mask** | A difference that only scales a feature's opacity or strength | Written into the feature's mask: 0 where a source lacks the feature, its strength where it has it |
| **Slots** | Countable instances: matcaps, rims, decals, outlines | Instances with the same configuration share one slot, told apart by the mask. Different instances take separate slots until the output's slots run out; then the mode decides: merge, pick, bake or drop the least visible |
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
  baked there);
- the reference source, for shared values and *single* modes.

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

When there aren't enough places, features are dropped in reverse priority (the least visible
first, as the feature order defines it), or fall back to a mode that needs no mask (*single*
or *baked*). Nothing is reported; the UI shows which features are in which mode.

**Live mask steps** (back face, mirror, camera distance, contacts) stay live only on an output
mask bus that evaluates the same step. Otherwise the mask is baked with the step's value in
the normal view: front faces, not in a mirror, the camera at the far distance, no contact.

**Vertex colour masks** are handled by the *Vertex colours* feature (see
[*Feature inventory*](#feature-inventory)): kept live, baked into a texture by painting the
mesh's vertex colours in UV space, or turned off.

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

## Textures

### Virtual maps and textures

Features work with **virtual maps**: one quantity each, such as base colour, alpha, metallic,
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
  - **main aspect** (UV-space maps: colour, normal, masks, surface maps), a size from the
    sizes that keep the main texture's aspect;
  - **square** (matcaps, generated rim matcaps);
  - **custom** (ramps and other lookups).
- Each row shows its memory for PC and for Android, and the section shows the totals for
  both (see [*Memory*](#memory)).

The main texture size and auto setup work as today.

### Optional maps and fallbacks

Every optional virtual map names the map its content goes to when its texture is turned off,
so turning a texture off simplifies the result instead of dropping it. When a compound texture
is turned off, each of its virtual maps falls back on its own.

| Optional virtual map | When its texture is turned off |
|---|---|
| Secondary and detail normal maps | Baked into the main normal map |
| Decal textures | Baked into the base colour map (and its emission into the emission map) |
| Detail colour maps | Baked into the base colour map |
| Emission map | Baked into the base colour map (the *baked into colour* mode of emission) |
| Occlusion map | Baked into the base colour map (see *Ambient occlusion baked into colour* under [*Feature inventory*](#feature-inventory)) |
| Metallic, roughness, reflectance, specular colour, clear coat and thickness maps | The reference source's constant |
| Shadow colour and ramp masks | The reference source's values |
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

Crunch and the PNG file size don't count: crunch only changes the download size.

The *Textures* section shows each texture's memory on both platforms and the material's
totals:

```
Texture          Size         PC        Android
Base colour      2048x2048    ...       ...
Normal           2048x2048    ...       ...
Matcap 0         512x512      ...       ...
Total                         ...       ...
```

The Advanced Atlaser component shows the totals over all its outputs as well.

### Export rules

- **No unused alpha.** A texture gets an alpha channel only when a virtual map uses it. Where
  it does, `OutputChannels.Auto` still drops alpha that is 1 everywhere.
- **Normal maps** are exported as normal maps.
- **Crunch compression** is one setting per output, on by default. Each texture is compressed
  both ways, and crunch is kept only when the crunched size is below 90% of the size without
  it. Otherwise the texture keeps normal compression (DXT or BC) without crunch. Crunch changes
  the download size, not the memory size.
- **Colour space** follows the virtual map: colour maps sRGB, data and masks linear.

---

## Baking toolkit

Bakes that need geometry or light share one toolkit in the Atlaser (not in the Texture
Wrapper, which stays shader-neutral).

### Bakeable subset

Baking means computing on the CPU what the source shader would draw: layers, blends, masks,
colour adjustments and texture mapping (placement, tiling, rotation), and for the bake-ins the
lighting families. That is a large part of the feature catalogue. The CPU evaluator therefore
covers an explicit **bakeable subset** that grows feature by feature, with tests against known
values. Content outside it is never guessed at: its feature uses a native mode, or the bake
falls back to the source texture as it is (without the unsupported layer or operation).

### Memory budget

A 4096 × 4096 float RGBA map takes 256 MB, so bakes keep few full-size maps alive at once:
- virtual maps are baked one at a time, blitted into the atlas, and released;
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
+Z), plus an ambient term. Each light has a role, and its own colour, softness and weights:

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
- **The ambient term** is a sky and ground gradient, not a flat colour, so occlusion darkens
  light that comes from somewhere.

There are two rigs, chosen per output. They share the lights' settings; only the directions
differ:

| Light | Classic | Symmetric |
|---|---|---|
| Key | front, above, to one side | front, centred, above |
| Fill | front, low, the other side | front, centred, low |
| Back (hair) | behind, above | behind, centred, above |
| Ambient | sky above, ground below | sky above, ground below |

The classic rig needs unique UVs. A source whose UVs overlap (mirrored halves) is baked with
the symmetric rig, even when the output chooses the classic one; the *Baking* section lists
those sources. Because the lights' settings are shared, those sources still match the rest of
the atlas. All values are starting values, to tune with real avatars. More rigs can be added
as presets.

**Light colour in baked textures.** A baked light colour stays in every world, on top of the
world's own light. So the light's colour is baked only by a **colour influence** (0: the
light's brightness alone; 1: its full colour), low by default (about 0.25). Form shading
divides by the average light, so the overall colour stays neutral and only the shift between
the warm lit side and the cool shadow side remains.

**Light terms per bake.** Every bake uses the same rig, but only the terms its purpose
needs:

| Bake | Uses |
|---|---|
| Full light, form shading | every light, diffuse and specular |
| Normal-map shading | key and fill, diffuse: detail reads evenly over the surface |
| Highlights, glitter emulation | the specular terms |
| Backlight, rim bakes | the back light |
| Occlusion into colour | the ambient term and reflections; direct light by the source's *occlusion: direct* |
| Subsurface | not baked (native or off) |

**Influence sliders.** The *Baking* section shows, folded by default:
- the rig (classic or symmetric) and an overall strength;
- the colour influence;
- one strength per bake in use: form shading or full light, normal-map shading, highlights,
  backlight and rims, occlusion;
- a *Lights* foldout with each light's intensity, temperature and softness. The directions
  stay fixed by the rig, so the symmetric rig stays symmetric.

The rig is evaluated with each source's own lighting family (3.2, 3.3) and surface response
(2.4): world normal, base colour, roughness, metallic, specular colour, occlusion.

Two bake results:
- **Full light:** the lit colour, for outputs that don't light (unlit or flat).
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
| **Colour** | 2.1 base colour<br>4.1 surface layers targeting colour (details, decals, recolour regions)<br>1.10 static adjustments | 1. Native | base colour **(Required)**<br>decal textures → base colour<br>detail colour maps → base colour | Bake. Decals and details keep maps of their own only in the output's slots for them. A layer that tiles in UV0 can't stay on the atlas UVs, where its tiling would differ per region: it keeps a map of its own on a preserved UV channel (see [*UV channels*](#uv-channels)), or is baked. Vertex colour is its own feature (below) |
| **Alpha** | 2.2 alpha, modifiers<br>6 surface mode, cutoff | 1. Native<br>2. Baked into base alpha | alpha **(Required)**, in the base colour's alpha, when the surface mode uses it | One surface mode for the material: by default the most see-through mode among the sources; opaque sources get alpha 1. Different cutoffs: alpha is remapped per source so each source's cutoff lands on one shared cutoff. Modifiers (fades) are shared values |
| **Normal maps** | 2.3 normals<br>the normal correction and the pre-baked normal<br>4.1 layers targeting the normal | 1. Native<br>2. Baked into colour (normal-map shading)<br>3. Off | main normal **(Required)**<br>secondary normal → main normal<br>detail normal → main normal | Bake, combined in the established order. A secondary or detail layer is offered a map of its own when every source using it has the same texture and a preserved UV channel is available for it; otherwise it is always baked into the main normal |
| **Colour adjustments** | 1.10 | 1. Baked (static adjustments, always)<br>2. Native (live adjustments, where the output has the operation) | — | Static adjustments are baked into the maps they target. Live ones (animated or modulated) are shared values; when they differ, the reference source's value is used |
| **Vertex colours** | 2.1 vertex colour on the base colour and alpha<br>1.4 masks reading vertex colour<br>1.5 vertex colour steps of the mask bus<br>other uses (transition space, anisotropy, outline and fur directions) | 1. Live<br>2. Baked<br>3. Off | base colour, alpha and mask maps, when baked | Shown when a source's material uses vertex colours; the user chooses. *Live*: the vertex colours stay on the mesh and the output reads them the same way; offered only when every source uses them the same way and the output supports that use. Sources that don't use them get neutral vertex colours (white) in their part of the mesh during the remap, so nothing changes for them. *Baked*: the vertex colours are painted in UV space into the maps they affect; texels shared by triangles with different colours get the average. *Off*: they are ignored |
| **Reflection and specular** | 2.4 metallic, roughness, reflectance, specular colour, highlights, reflections<br>4.3 cubemap lookups | 1. Native<br>2. Off | cubemaps **(Required)**, for cubemap lookups<br>metallic → constant<br>roughness → constant<br>reflectance → constant<br>specular colour → constant | Constants that differ become maps (spatial). Highlight and reflection groups are shared values with their strengths in a mask. A cubemap is a lookup layer and takes a slot |
| **Clear coat** | 2.4 clear coat | 1. Native<br>2. Emulated (lower roughness and higher reflectance in the coated regions)<br>3. Off | clear coat roughness → constant<br>mask (allocated) | Strength in mask |
| **Ambient occlusion** | 2.4 occlusion | 1. Native<br>2. Baked into colour<br>3. Off | occlusion **(Required)**, for the native mode | Bake. *Baked into colour* is for sources with an occlusion map when the output has none, or to save the texture. See below |

### Lighting

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Shading, lighting and shadows** | 3.1–3.4 | 1. Native (the writer maps the family and parameters to its own modes)<br>2. Baked full light (outputs that don't light)<br>3. Baked form shading (outputs that light flatly) | shadow colour → reference values<br>ramp masks → reference values | Exclusive: one lighting setup, from the reference source. The writer maps unified families to its shader's lighting modes, for example the ramp toon special cases (3.2) to Poiyomi's modes. Spatial values (shadow tint, ramp strength) become maps when the output has them |
| **Light volumes** | 3.5 | 1. Native<br>2. Off | — | Shared value: on when any source uses it, strengths from the reference source |
| **LTCGI** | 3.5 | 1. Native<br>2. Off | — | As light volumes |

### Layers

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Matcaps** | 4.3 lookup layers with a 2D texture | 1. Native (one slot each)<br>2. Merged<br>3. Single<br>4. Baked into colour (surface placement) or emission (overlay and emission placements)<br>5. Off | matcap textures, square **(Required)**, for the native modes<br>masks (allocated), one per slot | Slots. *Merged*: instances with the same texture and projection share a slot; matcaps on the same regions with the same blend are combined into one texture (added for *add*, multiplied for *multiply*); what still doesn't fit takes *single* or is dropped. Baking needs the world normals and the virtual view, so it runs after the normal maps |
| **Rim lighting** | 4.4 angle layers, edge source view angle | 1. Native (slots)<br>2. Merged<br>3. Emulated as a matcap<br>4. Baked<br>5. Off | generated matcap, square **(Required)**, when emulated<br>masks (allocated), one per slot | Slots. A view-angle rim depends only on `N·V`, so it can be drawn exactly into a generated matcap: emulation for outputs with matcap slots but no rim |
| **Environmental rim** | 4.4 colour source environment | 1. Native<br>2. Emulated as a rim with the probe's average colour<br>3. Off | mask (allocated) | As rim lighting |
| **Backlight** | 4.4 edge source backlight | 1. Native<br>2. Baked with the rig's back light (form shading)<br>3. Off | mask (allocated) | Light dependent, so no matcap emulation |
| **Emission** | 4.1 layers targeting emission<br>emission amounts of other layers | 1. Native<br>2. Baked into colour<br>3. Off | emission **(Required)**, for the native mode | Bake. Live parts (blinking, scrolling, audio) stay native only where the output has the same modulator; otherwise they are baked at their current value |
| **Glitter and sparkle** | 4.6 glitter pattern<br>2.4 glints | 1. Native<br>2. Emulated<br>3. Off | mask (allocated), for the native mode<br>emission, emulated<br>metallic and roughness, emulated<br>base colour, emulated | Native: shared values and a mask. *Emulated* generates the glitter cells as a static pattern in UV space and writes it into any combination of the three targets, each optional with its own strength: bright points in the emission map, metallic and smooth points that sparkle in reflections, lighter points in the base colour. A target whose map is turned off is left out |
| **Subsurface scattering** | 4.5 subsurface | 1. Native<br>2. Off | thickness → constant | Thickness maps are baked per region. Too dependent on the light to bake or emulate |

### Effects and render

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Proximity colour** | 1.9 a modulator with source *camera distance*, driving a colour adjustment | 1. Native<br>2. Off | mask (allocated) | Shared values; strength in mask |
| **AudioLink** | 1.3 AudioLink themes<br>1.9 audio modulators<br>4.6 audio patterns | 1. Native, per feature<br>2. Off (the feature keeps its rest value) | Per feature | Each feature that AudioLink drives keeps it only where the output supports AudioLink for that feature. AudioLink modes keyed to UV tiles read a preserved UV channel (see [*UV channels*](#uv-channels)) |
| **Surface mode and render queue** | 6 | 1. Native | — | The queue follows the chosen surface mode, with an override |
| **Fallback shader** | 6 (not typed) | 1. The writer's choice, with an override | — | The writer sets the fallback the way its shader exposes it, from the surface mode |
| **World AO blocker** | 6 render (to be typed; the Poiyomi parser ignores it today) | 1. On<br>2. Off | — | One switch for the whole material. Offered when a source uses it and the output supports it; the user chooses. See below |

**Ambient occlusion baked into colour.** Native occlusion darkens ambient light and
reflections, not direct light (2.4), so multiplying the colour by the occlusion map would
darken the lit side too. Instead, the bake evaluates each source's occlusion under the virtual
light rig, with the world normals and the virtual view. Each texel is darkened by the share of
its light that occlusion would darken: the ambient part, the reflections, and the direct part
only by the source's *occlusion: direct* amount. Texels facing the key light keep most of
their brightness; creases facing away get the full darkening. A strength blends the result
in, and a source's occlusion tint (2.4) colours the darkening.

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
| Identity | ID, display name, `FindShader` (as today) |
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
- **Output settings:** crunch, bake view, light rig, colour influence, the bake strengths,
  and each light's intensity, temperature and softness.

A mode the new shader doesn't support falls back to its default mode; the stored choice is
kept, so switching back restores it.

### Layout

```
Output name, output shader, sources and UV regions      (as today)
[Auto Setup]
▸ Textures         main size, crunch, the texture list with sizes and memory
▸ Surface          Colour · Alpha · Normal maps · Colour adjustments · Vertex colours ·
                   Reflection and specular · Clear coat · Ambient occlusion
▸ Lighting         Shading · Light volumes · LTCGI
▸ Layers           Matcaps · Rim lighting · Environmental rim · Backlight ·
                   Emission · Glitter · Subsurface
▸ Effects          Proximity colour · AudioLink
▸ Render           Surface mode and queue · Fallback
▸ Masks            where each mask went (read only)
▸ Baking           light rig, strengths, lights, view     (only when a mode bakes)
▸ UV layout preview                                      (as today)
[Bake to Assets…]
```

- Every section and feature folds. A feature's header shows its mode and how many sources use
  it (for example *Matcaps — Merged — 3 of 5 sources*), so the folded view is a summary.
- Features no source uses, and features without an available mode, aren't shown.
- The per-source foldout sits at the bottom of each feature.
- Auto setup chooses a default mode per feature: native when supported, the cheapest
  emulation when not, off when neither.

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

Each phase ends with tests passing in Unity, as for the unified material.

1. **Foundation**, as one slice through every pipeline step (collect, resolve, bake, pack,
   write, export) and the manual bake, to prove the abstractions before features are added:
   - feature interface, capabilities, virtual maps and textures, mask allocator, settings
     storage, generated UI, source analysis cache, export rules (alpha, crunch), memory
     budget;
   - input parser reports logged only with the debug setting;
   - features: colour, alpha, the main normal map, metallic and roughness (to prove a compound
     texture), surface mode and render queue, fallback;
   - writers: Poiyomi Toon 10 and VRChat Mobile Toon Standard, so the capability model is
     tested on two different shaders from the start.
2. **Surface:** normal map layers, the rest of reflection and specular, ambient occlusion
   maps, emission, colour adjustments, shading and lighting mapped natively.
3. **Layers:** masks and global masks in full, preserved UV channels (with the UV remap),
   matcaps, rim lighting, environmental rim, backlight, clear coat, light volumes, LTCGI.
4. **Baking toolkit:** geometry buffers, light rigs, virtual view, bake-ins for normal-map
   shading, matcaps, rims, light, and occlusion maps into colour; emulations, tested mostly
   with VRChat Mobile Toon Standard.
5. **Effects:** glitter and sparkle, proximity colour, subsurface scattering, AudioLink,
   world AO blocker (typed in the unified material, read by the Poiyomi parser).
6. **Later features** (above), in order of use.

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

- **Crunch fallback:** when crunch saves less than 10%, the texture keeps normal compression
  without crunch.
- **Light rigs:** two rigs, classic and symmetric. Sources with overlapping UVs are always
  baked with the symmetric rig.
- **Light roles:** each light has its own temperature, softness, and diffuse and specular
  weights. Each bake uses only the light terms its purpose needs. Baked light colour is
  limited by a colour influence, and every bake has its own strength slider.
- **First writers:** Poiyomi Toon 10 and VRChat Mobile Toon Standard, both in the first phase.
- **Compound textures:** an output shader's texture can provide several virtual maps in its
  channels. Using any of them activates the texture; it is required when any of them is
  required, and has one size for all its channels.
- **Maps are the user's choice:** that the output has a map doesn't mean it is used. Only the
  maps a mode can't work without are required (for normal maps, only the main one). An
  optional map that is turned off is baked into the map it falls back to: secondary and detail
  normals into the main normal, decals and details into the base colour.
- **Inverted masks:** a placed mask also serves features that need its inverse, when they can
  read it inverted, without another channel.
- **Ambient occlusion:** a source's occlusion map is baked into the colour map, under the light
  rig, when the output has no occlusion map. Occlusion generated from the mesh is not planned.
- **Memory:** GPU memory of the compressed textures, for PC and Android, per texture and in
  total per material.
- **World AO blocker:** typed as a render feature, one switch for the whole material, offered
  when a source uses it and the output supports it. The UV remap keeps its tile when it's on.
- **Input reports:** the input parsers' *unmapped* lines are logged only with the debug
  setting, not in normal builds.
- **Vertex colours:** when a source uses them, the user chooses to keep them live, bake them
  into textures, or turn them off.
- **Manual bake:** writes the material and its textures only; no preview copy of the avatar.
- **UV channels:** UV0 holds the atlas UVs. UV1 to UV3 carry preserved original UVs, with
  different content per source, allocated like masks: for tiled layers kept live, UV-tile
  features, and features that read other UV channels in the source.
- **Source analysis cache:** parsed sources are cached per material, keyed by the material's
  dirty count, its shader, and a version raised when textures or shaders are imported.
- **Animation:** some animations will be supported, designed separately; this design only
  keeps room to record animated properties per source.
- **Phase 1** is one slice through the whole pipeline, with the main normal map and a
  compound metallic and roughness texture added to prove the abstractions early.

## Open questions

None at the moment.
