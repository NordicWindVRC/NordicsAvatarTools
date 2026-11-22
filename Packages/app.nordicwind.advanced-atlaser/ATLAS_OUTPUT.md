# Nordic's Advanced Atlaser — Output design

How an atlas output turns several unified materials into one material for an output shader,
and which parts of that are shared and which are shader specific. Everything here is built
except what is marked *later* (the [*Feature inventory*](#feature-inventory)'s later phases and
the plan's *Later* list); [*Code map*](#code-map) says where each part lives.

Related documents:
- [UNIFIED_MATERIAL.md](UNIFIED_MATERIAL.md): the unified material and the input parsers.
- [UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md): the feature catalog. Section
  numbers such as *4.3* in this document refer to it.
- [NORMAL_BAKER.md](NORMAL_BAKER.md): the normal correction bake that the output reuses.
- [ATLAS_OUTPUT_PLAN.md](ATLAS_OUTPUT_PLAN.md): the implementation plan: what's done, what's
  next and what's later.
- [ATLAS_OUTPUT_LOOK.md](ATLAS_OUTPUT_LOOK.md): the planned design for matching by look, not
  built yet.

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
  Where the output can't keep a feature as it is, it should still look as close as its controls
  allow (see [*Matching the look*](#matching-the-look)).

## Code map

Where each part of this design lives:

| Part | Where | What it does |
|---|---|---|
| Output component | `Runtime/NordicsAtlasOutput.cs` | `AtlasOutputConfiguration`: the output's name and shader, region padding, sources, main texture size, feature, texture and baking settings (see [*Stored settings*](#stored-settings)) |
| Output shader interface | `Editor/AtlasShaderRegistry.cs` | `IAtlasOutputShader`: ID, display name, family and rank, `FindShader`, `Capabilities`, `Write` |
| Output shaders | `Editor/Outputs/AtlasOutputShaders.cs` | Poiyomi Toon 10 and VRChat Mobile Toon Standard: their texture layouts and capabilities (see [*Output shaders*](#output-shaders)) |
| Output model | `Editor/Outputs/AtlasOutputModel.cs`, `AtlasOutputFeature.cs` | Virtual maps, texture layouts, capabilities; `IAtlasOutputFeature` with its modes; the feature registry, available and effective modes |
| Features | `Editor/Outputs/Atlas*Feature*.cs` | One class per feature of the [*Feature inventory*](#feature-inventory), found through `IAtlasOutputFeature`; `IAtlasSourceAwareFeature` counts a source by more than its material (a pre-baked map, the build's correction) |
| Resolve | `Editor/Outputs/AtlasOutputPipeline.cs` | Feature choices from the settings, and steps 1 and 2 of the pipeline: the output unified material and its map requests (`AtlasOutputPlan`), with the UV channel and mask allocators' places and the generated textures |
| Live global masks | `Editor/Outputs/AtlasLiveMasks.cs` | Masks whose global mask channels change live, split into an own part and a live channel (see [*Masks*](#masks)) |
| Mask allocator | `Editor/Outputs/AtlasMaskAllocator.cs` | Masks over the whole atlas: constants without a texture, equal masks sharing a channel, inverses read inverted, the rest packed into the output's mask textures (see [*Masks*](#masks)) |
| Generator boundary | `Editor/Outputs/AtlasGeneratorJob.cs` | The unified materials, UV regions, bakes and feature choices; no `Material` or `Shader` crosses it |
| Bake sizing | `Editor/Outputs/AtlasOutputLayout.cs` | Each source is baked at 1.5× its island size, at most 4096 |
| Geometry bake | `Editor/Build/AtlasSubmeshBaker.cs` | Per source: the current mesh's coverage mask and the tangent-space normal correction. The world-space normal and tangent frame maps exist only during the bake |
| Auto setup | `Editor/Outputs/AtlasOutputAutoSetup.cs`, `AtlasRegionPacker.cs` | The Auto Setup button's work for the assigned sources: regions packed by base color size, the main texture size, default modes (see [*Auto setup*](#auto-setup)) |
| Output shader pick | `Editor/Outputs/AtlasOutputShaderPick.cs` | A new output's shader, once: the family most unused sources use, at its highest installed rank (see [*Auto setup*](#auto-setup)) |
| Inspector | `Editor/NordicsAtlasOutputEditor.cs`, `Editor/AtlasOutputUI.cs` | Sources and regions, auto setup, the *Textures*, *Material Features* and *Baking* sections, the UV preview; the costly parts kept until something changes (see [*Settings and UI*](#settings-and-ui)) |
| Baking toolkit | `Editor/Outputs/AtlasLightRig.cs`, `AtlasSurfaceShading.cs`, `AtlasMatcapBake.cs` | The light rig and the virtual view, each source's surface for lit bakes, normal-map shading, occlusion and matcaps baked into the textures (see [*Baking toolkit*](#baking-toolkit)) |
| Source analysis cache | `Editor/AtlasSourceAnalysisCache.cs` | Each source's parse, kept until the material, its shader or an imported texture or shader changes |
| Overview | `Editor/Outputs/AtlasOutputOverview.cs`, `AtlasTextureMemory.cs` | What an output uses: features with their modes and sources, the textures they need with sizes and memory |
| Evaluator | `Editor/Outputs/AtlasMapEvaluator.cs`, `AtlasColorStack.cs` | The bakeable subset over a source's UV 0–1: textures with their placement, color layers, decals, adjustments, masks and the mask bus |
| Map requests | `Editor/Outputs/AtlasMapRequest.cs` | A virtual map's content, views and contributions by stage, its shared color and its range step (see [*Map contributions*](#map-contributions)) |
| Look model | `Editor/Outputs/AtlasLook.cs`, `AtlasReferenceLook.cs`, `AtlasKeptMaterial.cs`, `AtlasLightingTuning.cs`, `AtlasLevelCompensation.cs`, `AtlasMatcapLook.cs` | The standard scenes, a lighting's level and contrast, a layer's cost, a view layer's look in the unified evaluation stages, the written material read back and compared with the sources' own looks, the lighting tuned where the output shader shows it differently, and the base color scaled for sources the atlas's lighting shows brighter or darker than their own (see [*The look model*](#the-look-model)), and a matcap's effect measured and named by its role (see [*What a matcap is for*](#what-a-matcap-is-for)) |
| Map baker | `Editor/Outputs/AtlasMapBaker.cs` | One texture, source by source: each virtual map evaluated at the source's bake size, its UV islands grown over the unused texels, placed in its region |
| Coverage | `Editor/Outputs/AtlasCoverage.cs` | Each source's UV0 triangles (from the output's renderers in the manual bake, the avatar's in the build), painted at bake size; a triangle in another UV tile counts by whole tiles |
| Packing | `Editor/Outputs/AtlasTexturePacker.cs` | One output texture from its virtual maps: which channels each map fills (inverted where the texture stores it so), and the neutral value of the rest |
| Writers | `Editor/Outputs/AtlasOutputShaders.cs` | Each output shader's `Write`: the output unified material into the shader's properties and keywords (see [*Output shaders*](#output-shaders)) |
| Export | `Editor/Outputs/AtlasTextureExport.cs` | PNG assets with the output's formats per platform, crunch where it saves 10% |
| Manual bake | `Editor/Outputs/AtlasManualBake.cs` | Bake to Assets: the pipeline in edit mode, the textures and the material written as assets |
| Build generation | `Editor/Outputs/AtlasOutputGenerator.cs` | The build's textures, packed and compressed for the platform being built (crunch where it saves 10%), and the material; normal maps generated as normal-map textures |
| Mesh remapper | `Editor/Build/AtlasMeshRemapper.cs` | A new mesh per renderer: UV0 into the regions, preserved UV channels, vertex colors, removed tiles and blockers (see [*Mesh remapper*](#mesh-remapper)) |
| Build step | `Editor/NDMF/AtlasOutputBuild.cs` | Generates every output and remaps every mesh before changing anything, then saves the new assets with NDMF and registers the replaced meshes |
| Texture library | Texture Wrapper (`API.md`) | Float textures with usage flags, `Blit` with blend modes, masks and padding, color operations, triangle painting, normal difference baking, dilation, PNG and in-memory export, `OutputChannels.Auto` |

The build generates each output's material and textures in memory and remaps the avatar's
meshes onto them; the manual bake writes the same material and textures as assets.

## Build steps

The NDMF passes (`Editor/NDMF/`):
1. **Resolving:** captures the original geometry of each configured material, in avatar-root
   space with the current pose applied, before AAO or other tools change it, and which renderer
   slots hold it.
2. **Transforming, mobile builds only:** puts a mobile placeholder in the slots of each source
   of an output with a mobile shader, because VRCFury, which runs next, removes (and warns
   about) materials without one.
3. **Optimizing, before AAO:** puts the source materials back; where another tool has since
   swapped a source material for a copy (VRCFury does for textures without mipmap streaming),
   the settings follow the material now in its slots.
4. **Optimizing, after AAO:** finds the surviving renderers, parses each assigned source
   material once, bakes each source's [*Normal correction*](#normal-correction), and builds
   each output: its material, its textures (compressed for the platform being built) and the
   meshes remapped onto the atlas. Every output is made before any mesh changes.
5. Removes both components from the build copy.

After merging, each configured source material must appear on at most one surviving submesh;
otherwise the build stops, because the bake would be ambiguous.

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

### Map contributions

Several features can change one map: the base color takes baked matcaps, the shifts of fitted
matcap slots, glitter points, normal-map shading, occlusion and level compensation. A map request
(`AtlasMapRequest`) builds each source's part of the map from three things:

- **Content:** the map's own value, set once by the feature that makes the map (the color
  stack for the base color).
- **Views:** scopes the content is evaluated under, such as the global masks with a source's
  live channels at 0 (see [*Masks*](#masks)). Contributions run outside them.
- **Contributions:** the other features' changes, each made in place on the source's content.
  A contribution names its feature, the sources it changes, whether it is a bake (the map
  still looks right on its own) or a shift (right only together with a live layer), and
  whether it can raise values.

Contributions run by stage, then in the order they were added, whatever order the features
resolved in:

| Stage | Holds |
|---|---|
| Layers | Baked matcaps, the clear coat's gloss, the shifts of matcap slot fits |
| Overlays | Glitter points, and the matcap glow baked into the emission |
| Shading | Normal-map shading, baked occlusion, the occlusion of reflections: the light on everything the stages before put there |
| Level | Level compensation: the whole of a source's part scaled to the level its own lighting gave it |

The map owns what each feature would otherwise have to know:
- **What the output multiplies it by.** The base color map leaves out a color that every
  source shares; it stays in the output's color. The emission map is relative to the
  emission's strength. Contributions get the content × that color or strength, the
  surface's real value, and the map divides it out again when they are done, so a feature
  adds its part in full and needs to know nothing of the scale: a baked matcap's glow and
  glitter points go into the emission as they are. A source that no contribution changes
  keeps its content exactly.
- **The range.** Once the whole map is baked, a map whose content or contributions can go
  above 1 is divided by its brightest value, which the map's carrier takes: the base color's
  shared color, a layer's color or strength. A map marked as stretched is divided whatever its
  brightest value is (see [*What reads a value*](#what-reads-a-value)).

A feature that needs the map as it was before a change takes a snapshot of it: the highlight
mask copied before the occlusion goes into the reflection mask, the live part of a split map.

A reader that needs the map partway asks the map for its value before a stage, or as one
contribution finds it, at the size it needs (`Before`), with what the output multiplies the
map by in it: the base color for a baked matcap glow's base color mix and for a combined
slot's glow fit, both before the layers stage, and for a fitted matcap slot's strength as
the slot's shift finds it, so the mask and the shifted base color start from the same
color. Within a source's evaluation the value is made once per size.

No contribution is kept as a layer of its own: each changes the source's content and releases
its temporaries before the next one runs (see [*Memory budget*](#memory-budget)). A source's
mesh surface is made when the first bake asks for it and shared by the bakes after it (the
matcaps in the color and in the emission, the normal-map shading). Normal maps are
evaluated by each bake at its own size, since keeping one between stages would hold a
full-size map.

---

## Features and modes

### What a feature declares

Each output feature is a shared class (proposed name `IAtlasOutputFeature`). It covers one or
more unified features and declares:

| Part | Meaning |
|---|---|
| ID, name, section | Stable ID (stored in settings), display name, the UI section it belongs to |
| Order | Its place within the section, which is also its priority when places run out. Features are listed and resolved by section, then order, then ID, so a feature can rely on the ones before it: baked matcaps add their glow to the map the emission feature made |
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
| **Shared value** | A setting that applies to the whole material | Equal values pass through. Different values: the value of the **reference source** (*Automatic*: the value that changes the sources' look least where the feature can draw it, as lighting and reflectance do (see [*Matching the look*](#matching-the-look)), otherwise the source with the largest region; or a chosen source, in a drop-down under the feature that appears only when the sources need different values on the output), or the largest or average where the feature says so |
| **Spatial value** | A setting that differs between sources and the output has a map for | Written per region into a map (for example roughness constants into a roughness map) |
| **Strength in mask** | A difference that only scales a feature's opacity or strength | Written into the feature's mask: 0 where a source lacks the feature, its strength where it has it |
| **Slots** | Countable instances: matcaps, rims, decals, outlines | Instances with the same configuration share one slot, told apart by the mask. The user places each instance (*Automatic*, its own slot, combined with another, off), in a list under the feature shown while there is more than one or *Automatic* leaves one out. Each row says in a few words how the instance ends up shown (*In matcap slot 1*, *Shares rim slot 1; keeps most of its look*, *Left out: no slot is free*). *Automatic* combines what doesn't fit with another slot where that costs less than leaving it out (see [*Comparing layers*](#comparing-layers)), and the build log says so |
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

## Matching the look

A feature is finished when the atlased avatar looks like the original, not when its values are
copied. This section collects how features get there and what comparing builds with their
originals has shown; new features and fixes follow it.

### Principles

- **Approximate the intent, don't drop it.** Where the output can't do a source's math, the
  feature is expressed with the output's own controls: blends it lacks become the nearest it
  has, a glow it can't show becomes added color, a clear coat becomes gloss, strengths without
  a setting go into one it has. An approximation that still gives the effect is closer to the
  original than nothing. Modes that don't give the effect at all aren't offered (see
  [*Decisions*](#decisions)), and what is approximated is listed in the build log.
- **General rules only.** A fix found on one avatar must hold for every material and avatar.
  Each rule comes from a shader's source or the unified model. A heuristic (scenes, weights,
  thresholds) has a reason that holds for any material and isn't tuned to one avatar's numbers;
  where it could choose differently elsewhere, its comment says so. Tests use synthetic
  materials that cover several cases (dark and bright, masked, blended, mixed), not copies of
  one avatar's values.
- **Sources, not memory.** What a shader does is read in its source, for each version it
  applies to. A missing source is named, not guessed.
- **Exact where the output can be exact.** A source the output can reproduce gets its exact
  values, so same-shader round trips stay unchanged. Approximation starts only where the output
  lacks something.
- **No silent choices.** Where sources need different values, the automatic choice is a
  default the user can change (see [*Decisions*](#decisions)), and the log says what it chose
  and which sources would need something else.

### Fitting what the output lacks

A shared value, or a setting the output lacks, is chosen by how the result looks:

1. Each source is drawn twice: **as its own shader shows it**, with its own settings, and
   **as the output shader shows it**, with the candidate value and only what the output
   keeps. Where the two shaders compute the same way, only the missing part differs, and a
   source the output can reproduce costs nothing, so its exact value wins. Where they
   compute differently (another fresnel), the value also makes up for that as far as it
   can.
2. The results are compared as **displayed (sRGB) colors** in the standard scenes (see
   [*The look model*](#the-look-model)), over the light and view angles, on the source's own
   base colors (`AtlasSourceSamples`, 4 × 4 samples). Displayed colors count what the eye sees:
   linear ones would overweight bright parts, and dark sources show differences less.
3. The cost is the sum over the sources of **area × the mean absolute difference**. Absolute
   differences make the result follow the source whose look changes most with the value,
   rather than averaging two looks into one that matches neither.
4. What doesn't depend on the candidate is computed once per source, so the fit stays fast
   enough for the inspector.

| Fit | What it chooses | Where |
|---|---|---|
| Automatic lighting | One lighting setup among the sources' | `AtlasLightingResponse` |
| Reflectance | Toon Standard's `_Reflectance`, carrying the highlight and reflection strengths it has no settings for, and making up for a source shader's other fresnel | `AtlasSpecularResponse` |
| Matcap glow | A combined slot's one glow strength, by least squares over the sources' texels when the material is written | `AtlasMatcapFeature` |
| Matcap blends | A slot's strength and base color for blends it lacks, from Poiyomi's chain with black and white textures | `AtlasMatcapFeature` |
| Band rims | Another rim's midpoint and rise on a writer whose rim is a band | `AtlasRimCurve` |
| Added rims | Strength, base color mix and lighting for a rim slot that only adds after lighting, from a rim that blends, sits on the surface, glows or takes its color from the environment | `AtlasRimResponse` |

### The look model

What the fits and the build's checks share (`AtlasLook`):

- **Scenes.** One set of standard scenes:

  | Scene | Main light | Probes | Why |
  |---|---|---|---|
  | Bright | 1 | 0.3, even | A lit world |
  | Dim | 0.4 | 0.15, even | The same at low light |
  | Probes only | None | 0.5 on average, 0.25 more along their dominant direction | No main light: each shader turns the probes into light its own way, so shaders differ most here. About as much light as the bright scene gives a surface on average |
  | Dark | None | 0.02, even | The floors (minimum brightness, minimum light) decide the look |

  Without a main light, the angle to the light is the angle to the probes' dominant
  direction, and the lighting's light color model says what becomes direct light (catalog
  3.4): the probes merged, floored or split, or a share of the ambient light standing in
  for the main light. The lighting's look, *Automatic* lighting and the rim fit use all four
  scenes. The rim fit lights the surface by the atlas's lighting, which also says what a
  scene without a main light gives a rim that follows the light. The highlight fit uses all
  four as well: its diffuse light and the light of its highlights are what each source's
  own lighting makes of the scene.
- **Base colors.** A source's look is judged on its base colors. Where layers or
  adjustments change the base color (a decal, a hue shift), they are the base color stack
  as it is baked, evaluated at sample size from small copies of its textures, kept by
  texture and content. Otherwise they are its color × its base color
  texture where its meshes use it (the UV0 triangles' texels, as up to 16 samples from the
  darkest to the brightest), `AtlasSourceSamples`. A texture's unused parts, often black and
  often most of it, would otherwise make a dark material count for almost nothing. The
  inspector has no meshes for this and samples the whole texture. The highlight fit reads
  the base color together with the smoothness and the masks at the same place, so it takes
  the whole texture's samples by position.
- **A lighting's look.** A lighting setup on a set of base colors has a **level**, the mean
  displayed brightness over the angles to the light, and a **contrast**, the lit side's mean
  minus the shadow side's, in each scene.
- **The reference look.** A view layer (a matcap, a rim) is judged by its effect: the
  displayed look with it minus the look without it, at a sample point
  (`AtlasReferenceLook`). A sample point is one of a source's base colors under a light,
  and its look is computed in the unified evaluation stages:

  | Stage | What happens |
  |---|---|
  | S1 | The layers on the surface blend into the base color, in order |
  | S2 | The light shades the result |
  | S3–S4 | Overlays blend with the lit color, by the light they follow; lit adds take the direct and ambient light |
  | S5 | Unlit adds: emission placements and glows |

  A layer says what it shows at the point and at which stages it acts. The matcap
  measures, the rim fit and the rims' costs describe their layers so, and take the stages
  and the light from there. A rim is taken over the angles to the light, under the atlas's
  lighting. A matcap's place on the surface doesn't follow the light, so it takes each
  scene's light on average over those angles, under its source's own lighting.

  The lighting stage also holds highlights and reflections, which the reflectance fit
  uses: GGX highlights from the direct light and reflections of an environment as bright
  as the scene's ambient light, each × its fresnel, `F0 + (1 − F0) × its rise`, and both ×
  the reflectance fade. The rise follows the surface response's fresnel mode, and for
  reflections its fresnel strength. A source is drawn with its own shader's and the output
  with the output shader's (below), each under the source's lighting.
- **The kept material.** The written material, read back by the output shader's own parser
  (`AtlasKeptMaterial`): what the shader will show, in unified terms. An output shader is
  described once, by its parser. Before an atlas's material exists, the kept lighting is
  read the same way from a scratch material that holds the lighting alone
  (`AtlasKeptLighting`).
- **What a shader has no settings for.** How an output shader computes something it has no
  settings for is a unified mode its parser sets on every material of the shader: the
  fresnel of its highlights and reflections, and the reflectance below which they fade.
  A fit reads it from the parser's reading of a material of the shader with nothing set
  (`AtlasKeptMaterial.Described`); where the shader isn't known, the unified defaults
  apply. What a slot shows of a layer is read the same way, with the layer written alone
  to a material of the shader (`AtlasKeptMaterial.Shown`): a rim slot that shows every
  rim as a band, or as a color added after lighting. The capabilities name their shader
  and hold counts and what is available, no formulas.
- **What a source loses.** After writing, each source's own lighting is compared with the
  kept lighting on the source's own base colors. Where the level or the contrast differs by
  more than about 8 of the 256 displayed levels in a scene, the build lists the source and the
  scene, in one line per output. *Lighting from* picks another source's lighting.

#### Tuning the lighting

The chosen lighting is mapped to the output shader by its values. Where the shader keeps it
as mapped, nothing else happens, so a source the output can reproduce keeps its exact values.
Where the kept lighting looks different, the build tunes it (`AtlasLightingTuning`):

1. **The difference** is the mean difference in displayed color between the chosen lighting
   and the kept one, on each source's darkest, average and brightest base color, over the
   four scenes and the angles to the light, weighted by the sources' areas. Below a third of
   the visible tolerance the mapped lighting stays.
2. **The values searched** are those that set how bright the surface is and how much darker
   its shadow side: the light color model, the light multiplier and addition, the brightness
   ceiling, the minimum light and brightness, the directional ambient amount, the shadow
   lift, the ramp and shadow strengths, and the shadow color's brightness (its hue stays).
   The family, the shading term and the hues are the artist's and aren't searched.
3. **The search** takes one value at a time: points across its range, then steps that halve
   around the best, with the kept lighting read back for each candidate. A value the shader
   doesn't have changes nothing in the kept lighting and stays as mapped, so the search
   knows no shader.
4. **The ramp's shape** is fitted for an output that shades only through a generated ramp
   texture read by half-Lambert (Toon Standard). Such a shader has no family, shading term or
   shadow strength of its own: the ramp is where the chosen lighting's shading over the angle
   to the light goes. For each texel of the ramp and each color channel, the fit takes the
   value that makes the kept lighting show the sources' colors closest to the chosen
   lighting at that angle, over the four scenes; where the ramp makes no difference it is
   white. It works at the ramp texture's resolution, with a source's ramp texture read twice
   as finely, so a sharp edge stays sharp. The ramp is fitted first, then the values, then
   the ramp again with the values found. The fitted ramp replaces the source's ramp texture
   or the SDK's preset, and the build generates it.
5. **The result** replaces the mapped lighting only when it is closer by the same margin, so
   it doesn't flip on a small change: the fitted ramp against the mapped one, and the values
   against what the ramp left. The build then names what was tuned and the difference before
   and after, in one line.

The target is the chosen lighting, not each source's own: which lighting the atlas gets is
the user's choice (*Lighting from*), and tuning only makes the output shader show that
choice. The inspector resolves as the build does, with the search's results kept by the
output shader, the chosen lighting and the sources' colors, so a changed setting elsewhere
doesn't search again.

#### Level compensation

The atlas has one lighting. A source that the kept lighting shows brighter or darker than
its own lighting did gets its part of the base color map scaled (`AtlasLevelCompensation`),
after the lighting is tuned:

- **The scale** is the one that makes the source's darkest, average and brightest base color
  under the kept lighting look closest to its own look, over the four scenes and the angles
  to the light, between a half and double. A source whose look is kept, or that no scale
  brings closer by the tuning margin, isn't scaled.
- **It applies to the whole of the source's part,** as the map's last contribution (the
  *Level* stage), since the lighting lights everything in the map.
- **A raise stays within the map's range.** It stops where the source's brightest texel in
  the texture reaches 1, so it takes no levels from the other sources (see [*What reads a
  value*](#what-reads-a-value)). A texture with a white texel can't be raised; lowering has
  no such bound.
- **Side effects.** Every feature that reads the base color reads the scaled one, and
  viewers who block the avatar's shaders see it where the fallback shows the base color.
- **The control.** *Keep each material's brightness* under the lighting, on by default,
  shown while a source would be scaled. Off leaves the base colors as they are.
- **Reporting.** The build names the scaled sources with their scales in one line. The
  comparison after writing takes each source as the atlas shows it, with the scale the bake
  applied.

#### Comparing layers

A layer (a matcap, a rim) is compared by its effect: the look with it minus the look without
it, per sample point (`AtlasLayerCost`).

- **Kept:** what the output's effect and the source's have in common: the same direction, up
  to the source's size.
- **Missing:** what the source shows and the output doesn't.
- **Added:** what the output shows and the source doesn't.

The cost is missing + 2 × added. Something the artist didn't put there reads as a fault,
while a weaker effect reads as a subtler material, so added counts double. Leaving a layer out
costs its impact, kept + missing, so a way of showing a layer is worth using when it keeps
more than twice what it adds.

A matcap that stands in for the light is compared by its **shading** instead: its
**level**, the size of its average effect, and its **contrast**, how far its effect lies
from that average on average, seen from around the avatar. A way of showing it loses the
share of level + contrast by which its own level and contrast differ, wherever the shading
sits. Point by point, a lit sphere frozen by a bake or replaced by another lit sphere costs
more than leaving it out, though it still reads as shading. By its shading:

- **left out** loses all of it;
- **its average in the color** keeps its level and loses its contrast;
- **a bake** loses what the frozen level and contrast differ by (a matcap's *bake cost* is
  this for lighting, and the point-by-point loss for a rim, a highlight and lighting with a
  highlight, whose spot would stay in place);
- **a slot shared across parts** keeps each source's level through its fit, and its contrast
  becomes the shared texture's: the texture's variation over the lookup disc, between its
  black and white ends, against that of the source's own matcaps. An even matcap has no
  contrast to compare, so what the shared texture's variation shows as on it is taken from
  its range, its effect from a black texture to a white one.

*Automatic* places a lighting matcap without a slot of its own by this (see the matcaps'
row of the [*Feature inventory*](#feature-inventory)).

A rim or a highlight is where it is, so a way that shows it elsewhere or as another is
compared point by point:

- **a matcap that is a rim or a highlight, in a slot shared across parts:** its effect at
  each point of the lookup disc, its level + its range × its texture's distance from its
  mean there, against the same with the shared texture. The shared texture shows a share
  of it on its own parts and the rest on the others', so sharing usually costs more than
  it keeps;
- **a rim in a rim slot it shares:** the slot's rim shows in its place;
- **a rim in a rim slot without a mask:** it is added on every part that didn't have it;
- **rims drawn into one matcap slot on different parts:** each part shows every part's
  rims at its share of the area.

A rim's effect is taken on its sources' darkest, average and brightest base color under
the atlas's lighting, in the four scenes, at five angles to the light and twelve facing
angles that each stand for an equal share of the surface a viewer sees of a round form
(`AtlasRimResponse.Effects`), and weighed by the area its mask covers. A layer's light
side is taken after its curve; shaping before the step and shrinking in shadow aren't part
of it.

*Automatic* uses such a way only where it costs less than leaving the layer out, and among
the slots a layer can share it takes the one that costs the least. What the user places
goes where the user put it.

### Reading intent from content

A setting doesn't always say what it's for. Where the purpose changes the result, the content
is measured as the output will use it:
- **What a texture shows where it's used.** Matcaps are sampled through the lookup disc with
  their texture's scale and offset, as the slot reads them. One that changes nothing (black
  where it adds, white where it multiplies) is left out, which frees a slot.
- **Effect or environment.** A matcap whose role is highlight (see below) is an effect and
  keeps a slot of its own. The others can be combined. When slots run out, the weakest
  effects go first.
- **Guarded.** Lighting with a highlight takes its own slot before the other environment
  matcaps do, and they share its slot only where no other will do. Out of slots it shares,
  since most of what it does is its body.
- **What shows, not counts.** Slots go to the matcaps with the most measured impact, by the
  area their masks cover. Shares of a combined texture weigh what each matcap adds to it:
  area × the blend's effect × the mask's average × the base color mix's darkening.

#### What a matcap is for

Each matcap is also measured by its effect (`AtlasMatcapLook`): the look with it minus the
look without it, as displayed, on its largest source's darkest, average and brightest base
color, under that source's own lighting in the four scenes. The effect is taken on a sphere
of normals, looked up as the layer's projection does, seen from the front and from around
the avatar in 45° steps:

| Measure | Meaning |
|---|---|
| Impact | The mean size of its effect where it shows |
| Direction | The share of its effect that brightens; the rest darkens |
| Level, contrast | Its shading seen from around the avatar: the size of its average effect, and how far its effect lies from that average on average |
| Average loss | The share of its shading (level + contrast) lost as its average alone: its contrast |
| Bake shading loss | The share of its shading lost frozen at the virtual view: how far the frozen level and contrast are from its own |
| Range | How far its effect goes from a black texture to a white one |
| Bake loss | What freezing it at the virtual view costs seen from around the avatar, over its impact (see [*Comparing layers*](#comparing-layers)). From 1, leaving it out costs less |
| Spread | The share of its effect in its strongest tenth, seen from the front |
| Evenness | 1: the same effect all over. 0: as much variation as effect |
| Edge weight | The share of its effect on the outer ring, beyond three quarters of the disc's radius. An even matcap has 0.44 there |
| Around | How much of the way around the outer ring shows the effect: the share of directions with a quarter of the ring's average or more |
| Detail | The share of its variation that is fine: what is left after each point is averaged with its neighbors |
| Standout | How far its sharpest features stand out: what its neighbors' average leaves of the sharpest fiftieth of the disc, over how much the matcap varies (at least a quarter of its effect) |
| Tint | How colored the matcap is where it shows: its saturation, weighted by its effect |

The **role** is the first that holds:

| Role | What it does | Measured as |
|---|---|---|
| Rim | Lights or darkens the edges: a rim light, soft shading toward the silhouette | An edge weight of 0.6 or more, around 0.65 of the ring or more |
| Highlight | Gloss, shine, glints, and reflections, which usually come with them | A spread of 0.5 or more, or a detail of 0.5 or more on a matcap that varies (an evenness below 0.75) |
| Lighting | Stands in for the world's light: a lit sphere, sky and ground, light from a side, a soft glow, one color or shade all over | The rest |

Two modifiers say what a matcap does besides its role:
- **With a highlight**, on lighting: its sharpest features stand out by 0.95 or more. A pearl
  and a lit sphere with a hot spot are both a soft body with a highlight on it, and no
  measure at this size tells them apart: an artist called the pearls highlights and the lit
  spheres lighting, and found the line hard to draw.
- **Tints**, on any role: a tint of 0.25 or more colors the surface (a red glint, a blue silk
  sheen). A warm white (0.2) doesn't.

A matcap whose impact is below half a displayed level has no role: it shows as having no
visible effect (a texture scale of 0, which reads one texel, makes one).

Three things don't decide the role:
- **The lookup.** Poiyomi's *Double Sided* mode looks the texture up by the reflection, and
  is used for every kind of matcap.
- **The bake loss.** An ordinary lit-sphere matcap changes enough with the view to cost more
  frozen than left out, so the loss says how a bake would look, not what the matcap is for.

- **The evenness.** How even a matcap is depends on the base colors it is on: a multiply
  mixed with a dark base color darkens almost evenly. An even matcap is lighting; that a
  bake keeps it shows in its bake loss.

There is no reflection role: highlights and reflections usually come together and are hard
to tell apart, by eye as by measure.

The limits are each on a measure of the matcap's own effect, checked against the matcaps in
use on one avatar and two collections of 184: an artist's, sorted into gems, glass, metal,
pearl, rubber, shine and silk, and Poiyomi's samples. Gems and facets measure a detail of
0.53 and up and soft lit spheres 0.35 and below; pearls and silks come out as lighting, gems,
glass and shine as highlights, and thin rings as rims. Metals split: chrome with a hot spot
is a highlight, a gold sphere lighting that tints. The inspector
shows each matcap with its texture (tinted by its color; a click shows it in the Project
window), and under its name the role with its modifiers, whether it brightens or darkens,
how it ends up shown with the settings as they are, and the materials it is on. The build
lists the roles in one line. *Automatic* placement follows
the role: highlights are effects, and lighting with a highlight is guarded.

#### Base color shifts

A slot with one blend at a time shows one side of a matcap: its multiply can only darken, its
add can only brighten. A matcap that does both is shown through a **base color shift**: the
base color is raised and the slot multiplies it down, or lowered and the slot adds to it
(the slot fit, `AtlasMatcapFeature`). The shift is right only together with the slot.

- **The blocked view.** VRChat draws an avatar whose shaders a viewer has blocked with a
  fallback shader, chosen by the material's `VRCFallback` tag, and copies the standard
  properties to it: the main texture and color, the emission, the normal map and others. A
  matcap slot isn't among them.

  | The output's fallback | The blocked view shows |
  |---|---|
  | The output shader itself (Toon Standard: its tag names it, and every property is copied) | The same look |
  | A fallback the writer sets (a Poiyomi output: Toon, Unlit or Standard) | The base color map × the color, without the matcaps |

- **How far a shift goes.** Where the blocked view shows the same look, the shift goes as far
  as the matcap needs. Where it shows the base color alone, the base color is right for that
  view anywhere between the surface's own color and its average look with the matcap, and
  the user chooses (*Base color shift*, shown while a slot is fitted):

  | Choice | The base color | With the shader | Blocked |
  |---|---|---|---|
  | *Automatic* | Shifted no further than the average look | The side the slot's blend can't show is weaker: a multiplying slot reaches the average at its brightest, an adding one at its darkest | As bright as the material is on average |
  | *Keep the base color* | The surface's own | One side of the matcap only | The surface's own color |
  | *Full* | Shifted as far as the matcap needs | Exact where the matcap is black or full | Too bright with a raised base, too dark with a lowered one |

  *Automatic* stops at the average because every step past it costs the blocked view in
  full, while it gives the look with the shader only the matcap's average share of it.
- **The slot's strength** follows the base color: a multiplying slot still reaches the
  matcaps' darkest, an adding one their brightest, as far as the base color lets it.
- **The average look** is taken from the matcap's mean color over the lookup disc, between
  the look with black matcaps and with full ones.
- **A shared slot.** A slot shared by matcaps on different parts shows their averaged
  texture on every part, which is brighter than one part's own matcap and darker than
  another's. Every source whose matcaps blend there is fitted, also one whose matcaps the
  slot could show as they are (not where the slot's differing base color mixes go into its
  masks), and keeps its own average look (**average in color**): its fit is moved by
  the difference between its own matcaps' average and the shared texture's as the slot
  shows it, as far as its darkest stays a color, so the base color takes the difference and
  the shared texture varies the look around the right level (listed). The bounds above
  apply as to any shift.
- **Without a slot.** A matcap can be kept as its average effect alone (*Average in
  color*), a bake: each texel's color goes through the matcap's blends with its texture at
  its mean over the lookup disc, where its mask is, and what changes with the view is lost.
  The user places a matcap so, which takes no slot. A lighting matcap that ends up without
  a slot (no mask place is left for it) is kept so (listed); a rim's or a highlight's
  average would light the whole surface, so those are left out. A glow the output's
  matcaps would show isn't part of it (listed).
- **Range.** A shift above 1 goes into the shared color, by the map's range step (see [*Map
  contributions*](#map-contributions)).

### Versions

A shader's settings can change meaning between versions under the same names: Poiyomi 10.0.26
to 10.0.29 multiply the direct highlights by π, the versions before and Poiyomi 11 don't, and
Poiyomi's upgrader doesn't convert old materials. So:
- each unified value has one convention, stated where it's declared (`AtlasHighlights.Strength`
  is without π);
- parsers and writers convert by the full version in the shader's master label, which locking
  keeps (`AtlasPoiyomiVersionDetector`). A locked material keeps the version it was locked
  with, which can differ from the installed one the writer targets;
- version handling is added only where the sources show which version changed. Versions outside
  the checked range, and unknown versions, are noted rather than guessed.

### What reads a value

- **A value can feed several features.** Poiyomi's Multilayer Math builds its own half-Lambert
  terms, but the light map it bypasses still drives the specular attenuation and the light
  masks; replacing the light map for the bands made those stronger in shadow. A feature that
  needs a value of its own gets a field (`AtlasRampToon.BandsOwnTerm`) instead of overwriting one
  that others read.
- **Transforms belong to the texture.** A texture's scale, offset and pan are applied in the
  shader's order (Poiyomi's matcap adds the pan before the scale and offset). An output slot
  without them gets a generated texture with them applied.
- **A shared slot shows on all its members' parts.** A combined texture is seen wherever the
  slot's mask reaches, so only what tolerates that is combined (environment matcaps, shared by
  visibility); effects whose highlights would move aren't.
- **A bake can't skip what the shader does before it.** Poiyomi applies its matcaps and rims to
  the base color before an emission that takes the base color reads it; baking that emission
  from the base color texture alone left out what the matcaps add. A bake of something read
  after view-dependent layers takes them at their average look (`AtlasViewLayers`).
- **Small values need range.** A texture holding only dim values uses a few of its 256 levels,
  and compression loses much of those; maps whose level a setting can carry are stretched to
  their brightest value (the AudioLink color map).
- **A limit must never step.** The normal correction used to fade to flat over the last 10° before
  its angle limit, so a larger raw correction gave a smaller result, and where the raw value
  crossed the limit the shading jumped: creases a toon ramp shows clearly. Limits on anything
  continuous compress smoothly instead (`NordicsTextureWrapper.SoftLimit`).
- **A texture can't be trusted at its UV seams.** A normal correction that was exact at the
  bake's size still showed hard edges in the atlas, along the seams between UV islands: the two
  sides read different parts of the texture, and filtering, compression and mip levels never let
  them match. Smoothing inside the islands didn't help; it made the sides differ more. What a
  texture adds on top of the mesh's own seamless values fades out at the seams.
- **A correction needs a valid correspondence.** Pairing two meshes by UV alone applied the
  original's normal where a reducer had moved or collapsed the surface. A rule that compares
  values from two sources first checks that they describe the same place.
- **Inputs aren't always readable as stored.** Crunched or unreadable textures are read through
  the GPU (Texture Wrapper).

### Checking a build against the original

1. Build each output and compare it with the original avatar in the same scene and light, from
   several angles: highlights, fresnel and matcaps differ only at some.
2. Isolate the difference: turn features off in the generated material (normal maps,
   specular, one matcap slot at a time) until it goes. Turning the atlas off tells the mesh
   from the material.
3. Compare the generated material's values with the sources', and work out from both shaders'
   sources what each does with them. A missing factor or a capped value shows in the numbers
   before it shows in a screenshot.
4. Fix the rule, not the value: state the fix for all materials, cover it with synthetic
   materials in the tests, and record it here or in the feature's row.

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
values, so a transformed channel can't also serve tile features. Tiled layers with the same
tilings per source share a channel.

**Tiled layers kept live** (`AtlasDetailFeatures.cs`): *Detail Texture* and *Detail Normal*
each keep one slot live: the sources whose last static tiled layer of the kind looks like the
reference source's (*Automatic*: the look on the largest area). Each source's strength
(relative to the strongest, where the slot has a strength) × its mask goes into the detail
mask, 0 where a source has none. The base color and normal bakes skip layers kept live
(`AtlasLiveLayers`); *Off* leaves them out. A layer that is rotated, followed by a layer of
the same kind, adjusted (its own or the surface color's adjustments), in a blend the output
lacks, or tinted or stronger than an output without a tint and strength can show, is baked,
and listed.

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

The *Textures* section lists the textures in use, required ones first. With every source
readable, these are the textures the build makes (from a resolve of the sources); otherwise the
ones the active feature modes can use:
- Each row names the texture and the virtual maps it carries in this configuration, for
  example *Metallic map: metallic, roughness, occlusion*.
- **Required** textures are always on.
- **Optional** textures have a checkbox. Turning one off applies the fallback of each virtual
  map it carries (below).
- **Sizes** follow a size mode per texture:
  - **main aspect** (UV-space maps: color, normal, masks, surface maps), a size from the
    sizes that keep the main texture's aspect. A new one starts at the main size, or at half
    of it for textures that need less detail (the mask allocator's *Masks* textures, the
    emission textures, and other masks and effect colors);
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

The geometry bake keeps, per source and at its bake size, the coverage mask and the normal
correction. Bakes that light a source read its current surface instead, when they evaluate it:
the sections of every submesh using its material, in avatar-root space, skinned meshes in their
current pose (`AtlasSurfaceGeometry`). From it they paint, at the size the source is evaluated
at:
- the **tangent frames** (tangent, bitangent with its handedness, normal), from which each
  texel's world normal with the combined normal map applied follows;
- **per UV winding:** the triangles whose UVs run counterclockwise in one pass and the others
  in a second, so mirrored halves (whose UVs run the other way) never mix their frames.

The frame maps exist only while one source's bake-in is evaluated and are released before the
next, so no full-size float map stays alive per source. Surfaces hidden close behind another
surface with the same UVs are left out, as in the normal correction.

A source's UVs **overlap** when more than 1% of the texels its UV triangles cover, at
256 × 256, are covered more than once (mirrored halves, stacked copies): a few stray
triangles don't change how a source looks. Triangles in other UV tiles count where the texture
repeats them.

Without the build (the manual bake), the current mesh is the source mesh, read from the
output's renderers in the space of the avatar root (the topmost ancestor with an Animator):
the normal correction is flat, and everything else works.

### Normal correction

Per source, the build bakes a tangent-space normal map that restores the original mesh's
shading on the surviving (for example reduced) mesh, by UV correspondence
(`AtlasSubmeshBaker.BakeCorrection`, shared with the [Normal Baker](NORMAL_BAKER.md)): the
original's normals and the current mesh's tangent frame are painted into UV space, and the
original normal is expressed in that frame (the Texture Wrapper's `BakeNormalDifference`). Each
material is baked at 1.5× its area in the atlas, up to 4096 per side. Rules, so that it never
makes the shading worse:
- **Overlapping UVs** get no correction.
- **Surface alignment:** both meshes' positions are painted into UV space too, with the current
  mesh's size around each point (the mean edge length around each vertex, blended over each
  triangle, so it has no steps). Where the two surfaces at a UV are further apart than a
  quarter of that length (after removing a constant offset, the median difference), the
  correction fades out, reaching none at three quarters: there the original's normal belongs
  to a surface the reduction moved or collapsed.
- **Soft angle limit:** corrections above half of the 60° limit are compressed smoothly toward
  it, never cut, so a larger raw correction never gives a smaller result and the map has no
  steps.
- **Hidden surfaces** (*Exclude Hidden Geometry*, on by default): where surfaces share UVs,
  those more than 2 mm plus a texel behind another, along the normal there, are left out, so
  geometry hidden inside the avatar doesn't affect the bake. Mirrored halves (opposite UV
  winding) never hide each other.
- **Excluded Blend Shapes:** triangles whose vertices a listed blend shape moves (by more than
  its threshold; any or all of a triangle's vertices) are left out, so parts removed on purpose
  aren't baked in.
- **Skipped** (a flat correction) when the source's *Normal Correction* is off, or when every
  surviving triangle is an unchanged original (a reducer that removes vertices makes new
  triangles, so its result is always baked).

The result is dilated. In the atlas it's shrunk by area and flattened where the normals a texel
covers disagree (`AreaFlatten`), then the corrections (this one and a pre-baked map, not normal
maps an artist made) fade to none at the borders of the source's UV islands, over one
compression block of the normal map's format on the platform being built, in final pixels (at
least two; `AtlasNormalSeams`). At a UV seam each side reads its own part of the texture in its
own tangent directions, and they only match if both decode to exactly the same direction, which
pixel size, filtering, compression and mip levels never allow; faded at the island borders,
both sides show only the vertex normals, which are seamless. The build log reports, per
material, how far the shading is from the original without and with the correction.

A source's normals combine in this order, each on top of the previous one: the pre-baked
normal map (applied at strength 1), this correction, the material's normal map, then its
normal layers.

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

The starting directions, as azimuth (from the front toward +X) and elevation:

| Light | Classic | Symmetric |
|---|---|---|
| Key | 40°, 35° up | 0°, 35° up |
| Fill | −55°, 5° up | 0°, 10° down |
| Back (hair) | −150°, 45° up | 180°, 45° up |

Softness runs from 0 (sharp) to 1 (very wide): it sets a diffuse wrap of up to 0.5 (wrapped
Lambert, `max(0, (N·L + wrap) / (1 + wrap))`) and an angular size of up to 30° for
highlights. The starting softness is 0.4 for the key (12°), 0.9 for the fill and 0.15 for the
back light. The ambient sky is 9000 K, the ground 4000 K. A light's color is Unity's color
temperature conversion scaled to luminance 1; its intensity multiplies its role's weights.

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
| Occlusion into color | the ambient and direct diffuse light by the source's occlusion amounts, averaged over every direction a surface can face; reflections in the reflection mask |
| Subsurface | not baked (native or off) |

**Influence sliders.** The *Baking* section is shown while a feature bakes with the rig
(`IAtlasLitBakeFeature`) and shows, folded by default:
- the rig (*Light Rig*: classic or symmetric) and an overall *Strength*; with the classic rig,
  the sources whose UVs overlap, which use the symmetric one;
- the color influence (*Light Color*);
- one strength per bake in use: form shading or full light, normal-map shading
  (*Normal-Map Shading*), highlights, backlight and rims, occlusion (*Occlusion*);
- a *Lights* foldout with each light's intensity, temperature and softness. The directions
  stay fixed by the rig, so the symmetric rig stays symmetric.

The rig is evaluated with each source's own lighting family (3.2, 3.3) and surface response
(2.4): world normal, base color, roughness, metallic, specular color, occlusion.

Two bake results:
- **Full light:** the lit color, for outputs that don't light (unlit or flat).
- **Form shading:** the light divided by its average over the source, so its average is 1.
  It adds shape to outputs that light flatly, without changing the overall brightness. A
  strength blends it in.

**Normal-map shading** for outputs without normal maps, or with the normal map turned off to
save its memory (*Normal Maps*: *Baked into color*), is the ratio of the shading with and
without the normal map, `shade(N mapped) / shade(N mesh)`, per color channel, under the key
and fill lights' diffuse terms. It adds the normal map's detail without shading the mesh's
form twice. N mapped is the combined normal map at the sources' own strengths, with the
corrections faded at the UV seams as in the normal map.
- The rig turns with each texel's virtual view (tilted, then turned around up, so its lights
  stay above), and is then tilted from the view onto the surface by the shortest rotation:
  every surface gets the lights at the angles a surface facing the view does, and the view
  only decides which way along the surface is up. So the detail reads the same over the
  front, the sides, the back and surfaces facing up or down, instead of vanishing or turning
  harsh where the lights only graze the mesh (under a jaw, the key from above barely reaches
  a surface facing down and forward, and the ratio of two small numbers exaggerates every
  bump). The view always faces the surface's own side, so the rotation is defined everywhere
  and changes smoothly.
- Both sides of the ratio get a floor of 5% of the lights' full diffuse light, so texels the
  lights barely reach keep their color rather than a noisy ratio, and the ratio is capped at
  2: beyond it, a texel is nearly at a grazing angle to every light.
- The base color map is multiplied by `lerp(1, ratio, strength)` (the overall strength ×
  *Normal-Map Shading*). Where that takes it above 1, it is divided by its brightest value,
  which goes into the shared color.

### Virtual view

View-dependent features (matcaps, rims, and the reflection part of baked occlusion) are baked
for a virtual view (`AtlasVirtualView`). The default is a camera far in front of the avatar,
looking along −Z, so every texel facing it is seen from the front. A texel facing away is seen
along its own normal instead: the view blends (smoothly) from the front at N·Z = 0.5 to the
normal at N·Z = −0.5, so sides and back aren't baked as a silhouette everywhere. Baked
matcaps keep the view frontal over the whole front half (down to N·Z = 0) and turn it only on
the back: a shading matcap puts its darkening at the edge of its disc, which only the sides as
seen from the front reach; a view that turns earlier keeps the sides from reaching it, and the
bake shows little more than the matcap's bright center. Their camera is set in the *Baking*
section (*Matcap View Side* and *Matcap View Height*, starting at 20° to the viewer's left and
15° up, so the matcap's light falls off-center as from a real light); the front half is then
the half facing that camera. Sources with overlapping UVs are seen from the middle at the same
height, so mirrored halves match, as with the symmetric rig.

### Overlapping UVs

Texels shared by mirrored or stacked triangles can't hold two different light-dependent
results. Sources with overlapping UVs use the symmetric rig, so mirrored halves get the same
light. Texels that still differ get the average of the overlapping surfaces' results: each UV
winding is shaded on its own and the results are averaged by coverage, so mirrored halves are
shaded apart. Stacked copies with the same winding share the averaged frame, which is exact
when their shapes match.

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
| **Normal maps** | 2.3 normals<br>the normal correction and the pre-baked normal<br>4.1 layers targeting the normal | 1. Native<br>2. Baked into color (normal-map shading)<br>3. Off | main normal **(Required)**<br>secondary normal → main normal<br>detail normal → main normal | Bake, combined in the order of [*Normal correction*](#normal-correction). A secondary or detail layer is offered a map of its own when every source using it has the same texture and a preserved UV channel is available for it; otherwise it is baked into the main normal. Normal maps shrunk into a source's bake size or region are averaged by area and flattened where the normals a texel covers disagree (`AreaFlatten`), so detail finer than the texel turns flat instead of into noise, which a toon ramp shows as speckles. *Baked into color* makes no normal map: each source's combined normals are baked into its base color as normal-map shading under the virtual light rig (see [*Virtual light rigs*](#virtual-light-rigs)), the default where the output has no normal map; sources whose meshes aren't known are listed |
| **Color adjustments** | 1.10 | 1. Baked (static adjustments, always)<br>2. Native (live adjustments, where the output has the operation) | hue cycling mask, for the base color's hue cycling | Static adjustments are baked into the maps they target. A cycling hue (a hue shift with a speed) is baked at its start, and *Hue Cycling* keeps the turning live on top: one rate from the reference source (*Hue Cycling from*), the base color's in the hue cycling mask, the final color's in an allocated mask, 0 where a source doesn't cycle. Slots with a hue shift of their own (matcaps, rims, glitter) copy their layer's; an emission whose hue cycles takes an emission slot of its own. Other live adjustments (AudioLink) come with AudioLink |
| **Vertex colors** | 2.1 vertex color on the base color and alpha<br>1.4 masks reading vertex color<br>1.5 vertex color steps of the mask bus<br>other uses (transition space, anisotropy, outline and fur directions) | 1. Live<br>2. Baked<br>3. Off | base color, alpha and mask maps, when baked | Shown when a source's material uses vertex colors; the user chooses. *Live*: the vertex colors stay on the mesh and the output multiplies its base color (and alpha, where it can) by them, with one setting (amount, sRGB decoding). The remap rewrites each source's vertex colors so that setting gives what its own did: sources that don't use them get white, and other amounts are solved exactly; a different sRGB decoding matches at the vertices and can differ slightly between them (when the sources disagree on it, the user picks the output's: *Vertex colors as sRGB*). Masks and mask bus steps reading vertex colors are baked. *Baked*: the vertex colors are painted in UV space (`AtlasVertexColors.cs`) into the maps they affect; texels shared by triangles with different colors get the average. *Off*: they are ignored (read as white) |
| **Reflection and specular** | 2.4 metallic, roughness, reflectance, specular color, highlights, reflections<br>4.3 cubemap lookups | 1. Native<br>2. Off | cubemaps **(Required)**, for cubemap lookups<br>metallic → constant<br>roughness → constant<br>reflectance → constant<br>specular color → constant | Constants that differ become maps (spatial). Reflectance is one value on the current outputs: fixed at 0.5 in Poiyomi's Reflections & Specular, and Toon Standard's gloss map × `_Reflectance`, where each source needs its reflectance over its smoothness. The value is fitted (`AtlasSpecularResponse`): each source is drawn as its own shader shows it (GGX highlights and environment reflections with its shader's fresnel, the fresnel strength of its reflections and its reflectance fade, its own reflectance and strengths) and as the output shader shows it (the output's value, the strengths the output keeps, 1 without a mask, and the output shader's fresnel: Toon Standard's one term and its fade below reflectance 0.25), both under the source's own lighting, and the displayed results are compared over the four scenes and the light and view angles; Automatic takes the value with the least difference over all sources by area, a chosen source is fitted alone, and sources the output can give exactly keep their exact value: those whose shader computes highlights and reflections as the output's does. A source of another shader is always fitted, since the same values look different there. Without highlight and reflection strengths (Toon Standard), a lower reflectance (F0 = 0.16 × r²) is what dims them. Highlight and reflection groups are shared values with their strengths in a mask. The unified highlight strength is on the scale of GGX × N·L without π, as Poiyomi lights its highlights before 10.0.26 and in 11; Poiyomi 10.0.26 to 10.0.29 multiply them by π, so their parser reads × π and their writer writes ÷ π, by the full version in the shader's master label (`AtlasPoiyomiVersionDetector.HighlightsTimesPi`; checked in 8.1.167 to 11.0.50). A cubemap is a lookup layer and takes a slot |
| **Clear coat** | 2.4 clear coat | 1. Native<br>2. Into the gloss map (outputs without a coat)<br>3. Off | clear coat strength and roughness **(Required)**, native<br>clear coat reflection and highlight masks, native<br>metallic and roughness **(Required)**, into the gloss map | Spatial value for strength and roughness (no coat: strength 0); strength in mask for the reflection and highlight strengths, relative to the strongest (one value everywhere: no mask; a highlight mask equal to the reflection mask reads its channel). The rest is a shared value from the reference source (*Clear Coat from*). *Into the gloss map* approximates the coat with the output's one specular layer: in coated parts the gloss is raised toward the coat's, `lerp(base, max(base, coat), coat strength × coat reflection strength)`, never lowered, so they get real, sharper highlights and probe reflections; without highlights in any source, the feature asks for the metallic and roughness maps itself. A surface under the coat becomes as glossy as the coat there, and the coat's tints and normal map amount aren't kept (listed) |
| **Ambient occlusion** | 2.4 occlusion | 1. Native<br>2. Baked into color<br>3. Off | occlusion **(Required)**, for the native mode | Bake. *Baked into color* is for sources with an occlusion map when the output has none, or to save the texture. See below |

### Lighting

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Shading, lighting and shadows** | 3.1–3.4 | 1. Native (the writer maps the family and parameters to its own modes)<br>2. Baked full light (outputs that don't light)<br>3. Baked form shading (outputs that light flatly) | shadow color → reference values<br>ramp masks → reference values | Exclusive: one lighting setup, from the reference source. *Automatic* takes the setup that changes the sources' look least (`AtlasLightingResponse`): each source's base colors (its color × a 4 × 4 copy of its base color texture) are lit by white light in the standard scenes (*The look model*) over the angle to the light, by its own lighting and by each candidate's, following the unified formulas for the main and ambient light; the cost is the sum of area × the mean difference of the displayed (sRGB) colors, so dark sources count for little. Among equal costs, the largest region. The writer maps unified families to its shader's lighting modes, for example the ramp toon special cases (3.2) to Poiyomi's modes. Spatial values (shadow tint, ramp strength) become maps when the output has them |
| **Light volumes** | 3.5 | 1. Native<br>2. Off | — | Part of the lighting setup: the reference source's |
| **LTCGI** | 3.5 | 1. Native<br>2. Off | — | As light volumes |

### Layers

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Matcaps** | 4.3 lookup layers with a 2D texture | 1. Matcap slots<br>2. Baked into color (surface placement) or emission (overlay and emission placements)<br>3. Off | generated matcap textures, square, for combined slots<br>masks (allocated), one per slot | Slots. A source's matcap is its consecutive lookup layers sharing texture and mask (one per blend weight). Matcaps that look the same in several sources share a slot. What a matcap shows is measured as its slot looks it up (its texture over the lookup disc, through its scale and offset): one that reads black where it adds, screens, mixes or glows, or white where it multiplies without base color mix, changes nothing and is left out (listed). An **effect** is a matcap whose role is highlight (shine, glints, facets; see *What a matcap is for*); the others are **environment** matcaps (lighting, rims). Lighting with a highlight is guarded: it takes its own slot before the other environment matcaps, which share its slot only where no other will do. A shared slot shows its one texture on all its members' parts, which an effect can't share without moving its highlights, so effects get their own slots, those with the most impact first, keeping one slot for the environment matcaps where none has one yet; where the slots run out the weakest effects are left out (listed; placing one in its own slot keeps it). The user places each one (in its own slot, combined with another, baked, kept as its average in the base color, see *Base color shifts*, or off); *Automatic* combines matcaps on the same parts with the same lookup, gives the environment matcaps with the most impact their own slots (a matcap's measured impact × the area its masks cover, see *What a matcap is for*), and combines the rest into the most similar slot: one whose matcaps do the same (blend, glow or both) first, since a slot has one blend and one glow strength, then the same lookup, the same main blend and the nearest color. A lighting matcap that blends is placed by its shading instead (see *Comparing layers*): among the slots that do what it does, it joins the one that loses the least of its members' level and contrast, each weighed by how visible it is (as for a combined texture, below), and where joining would lose more than its average does by over a quarter of its shading, it is kept as its average in the base color and the slot stays as it was (listed; a slot that follows the view is preferred up to that quarter, since its shading moves with the view, which level and contrast don't count). The log says what a slot shared across parts keeps of each lighting matcap's level and contrast. A rim or a highlight without a slot of its own is where it is, so it is judged point by point instead: it joins the slot that costs the least, and is left out where sharing would cost more than it keeps (listed; placing it combined with another shares the slot). *Automatic* doesn't bake in this mode: a bake stays in place from every side and is made with the virtual light rig's settings, so it is the user's choice; where one would keep more of a lighting matcap's shading than the way chosen, by over that quarter, the log names it. A combined slot gets a generated texture: matcaps on the same parts stack (textures × colors × weights added for *add*, `lerp(1, texture × color, weight)` multiplied for *multiply*; in a slot that only glows, the weights are the glows relative to the strongest); stacks on different parts are averaged by how visible they are (area × each weight's effect, the shown brightness where it adds, screens or glows and its darkness where it multiplies, × the mask's average × the base color mix's darkening `lerp(1, luminance(base), mix)` over the source's base colors, `AtlasSourceSamples`), since each part shows the one texture. Combined matcaps that mix in the base color differently get none in the slot: each one's mix goes into its mask, as without a base color mix. The glow (Poiyomi's matcap emission: matcap × strength × mask × `lerp(1, base color, mix)`) is kept as it is where the slot shows one matcap with its own mask and mix, scaled by a constant mask. In a combined slot, or one whose base color mix went into the mask or the fit, the one strength is fitted when the material is written: least squares over the sources' texels of strength × texture part × written mask × the slot's mix against each matcap's own glow, and listed where they differ. Matcaps that only glow but share a slot with a blend show the blend too, and are listed. An output whose matcaps don't glow (Toon Standard) gets the glow added to the color: one more *add* of the matcap at the glow's strength with its base color mix, lit rather than glowing (listed). A slot that takes one blend at a time gets a matcap with several weights merged (the same blend summed) or fitted, not only its first weight. An output without a matcap color also gets one for a tinted matcap, and an output whose slot has no texture transform for a matcap whose texture has a scale or offset. An output whose slot looks up by the view-space normal over the whole texture (Toon Standard) gets one for a matcap looked up otherwise (Poiyomi's *Double Sided* mode, which reads by the reflection; a border; a rotation): for each point of the slot's lookup, the texture is read where the matcap's own lookup reads it for that normal, seen from straight in front (listed). A lookup that follows the view is exact that way. One fixed to the world (the planar reflection, the light's half vector) looks different from each side, which a slot that follows the view can't show, so the user chooses (*World-fixed matcaps*, shown while one is in such a slot, listed): *Follow the view*, the default, looks the texture up by the view-space normal with its border and rotation, as any matcap: its middle where the surface faces the viewer and its edge at the outline, which keeps a shading matcap's form on the surface. Its sideways texture offset is left out: that shifts the texture along a world axis, which points the other way from behind, and kept by the view it would pull one side of the texture (a matcap's dark edge and its corners) into view as a band on the same side of the surface from everywhere; the upward offset is the same from every side and stays. *As seen from the front* redraws the texture for its own lookup from straight in front, exact there: the planar reflection reads at twice the normal's angle, so the texture's edge lies halfway out and the outline reads its middle again, from every side. The texture keeps its own scale and offset (Poiyomi samples at `(projection × border + 0.5) × scale + offset`); generated textures are sampled with them. Blends the output lacks are approximated: add, add to light, screen and mixed brighten, so they add; replace and multiply multiply; several weights take the heaviest. Where a slot can't show a source's matcaps as they are (blends it lacks, base color mix it doesn't have), it is fitted to them per texel: Poiyomi's chain (each matcap's replace, multiply, add, screen and mixed in turn, its color × matcap × `lerp(1, the base color so far, mix)` by weight × mask) is matched with the slot's texture black and white (T0, T1). From the rise k = (white − black) / (T1 − T0), the strength goes into the slot's mask and the rest into the base color map: for a multiplying slot `t = black − k T0 + k`, `s = k / t`; for an adding one `s = k`, `t = black − k T0`. The base color can come out brighter than the source's (a screen over a multiply). Exact for a single layer on a gray base; the strength is one value, so it takes the channels' luminance, and each channel of t matches halfway between T0 and T1. Without a base color map, base color mix goes into the mask: the mask × `lerp(1, luminance(base color), mix)`. Approximations, combinations and left-out matcaps are listed. *Baked* (`AtlasMatcapBake`): a matcap placed *Baked into color*, or every matcap in the *Baked into color* mode (the default without matcap slots), is drawn into the textures as the virtual view sees it instead of taking a slot: each texel's lookup by the layer's projection as Poiyomi computes it (view-stabilized, where the corrected projection is the same for a view along the texel's own direction; planar reflection; gradient; the light half vector with the rig's key light), from the mesh normal or the normal map by its surface normal amount, through its tiling, rotation and the texture's scale and offset, with its circle mask, × its color, base color mix and mask. Surface matcaps blend into the base color by their blend, in layer order, before the lit bakes, with a light mask (Poiyomi's mask × `lerp(1, lit, light mask)`) at its average under the rig, `lerp(1, (1 + wrap) / 2, light mask)`: a toon ramp lights the directions with N·L above −wrap, and the output's own shadow already darkens the rest; overlay matcaps that add and matcap glows are split by how they follow the light: Poiyomi multiplies them by `lerp(1, light color, light color mix)` and `lerp(1, lit, light mask)`, so the share `(1 − light color mix) × (1 − light mask)` shows without light and goes into the emission (made where the emission feature has none, normalized to its brightest value), and the rest is added to the base color, which the world's light shades (all of it where the output has no emission turned on); overlay matcaps that multiply go into the color. Surfaces are evaluated per UV winding, so mirrored halves are looked up apart and averaged. *Automatic* in the baked mode leaves out effects that brighten (their heaviest blend adds, or they only glow; listed): a baked highlight stays in place from every view; placing one *Baked* keeps it. An effect that mostly multiplies is shading, and is baked. Lookups blended toward a mesh UV, turned view directions, blur, a matcap's own normal map and panning are left as they are (listed). A *Matcaps* strength in the *Baking* section |
| **Rim lighting** | 4.4 angle layers, edge source view angle | 1. Rim slots<br>2. Baked<br>3. Off | generated matcaps, square, for rims in matcap slots<br>masks (allocated), one per slot | Slots, placed per rim like matcaps, with *As matcap* added. A rim that depends only on `N·V` (band, power band or step, no light side, no texture) is drawn exactly into a generated matcap (`AtlasRimCurve`): alone, its color with the rim × opacity in alpha where the slot reads alpha as opacity; otherwise premultiplied, rims on the same parts stacked and on different parts averaged by area. *Automatic*: a rim slot while one is free (on an output whose rim has no mask, only for a rim that is the same everywhere), then a free matcap slot, then a rim slot that shows it on every part (listed), then combined with another slot. The last two show the rim otherwise than it is, so *Automatic* uses them only where that costs less than leaving the rim out (see *Comparing layers*): a rim on most of the atlas shows on every part, one on a small part is left out (listed; its own slot by the user's choice shows it on every part), and among the slots a rim can share it joins the one that costs the least, or is left out (listed; placing it combined with another shares the slot). Combined in a rim slot, the slot's rim shows on each member's parts. The inspector shows each rim with its color, and under its name how it ends up shown. What a rim slot shows of a rim is read from the rim written to a material of the output shader and read back by its parser (see *The look model*). A writer whose rim is a band fits other rims to it: the same midpoint and the same rise from 10% to 90% (listed where the rim read back is a band and the source's isn't). A rim slot that shows a rim as a color added after lighting (Toon Standard: unlit, or × light + ambient when environmental, × `lerp(1, base, mix)`) can't blend, sit on the surface or glow, so a rim that does is fitted by its look (`AtlasRimResponse`): the source's rim at its peak, by its placement (on the surface it blends into the base color before lighting and is lit with it; lit and overlay rims blend with the lit color by their light response), its blend and its emission amount, against the added rim's strength, base color mix and lighting, on the slot's sources' base colors (listed). A rim already added after lighting is kept as it is |
| **Environmental rim** | 4.4 color source environment | 1. Native<br>2. In the rim (outputs whose rim adds after lighting and can be lit by the scene: Toon Standard)<br>3. Off | mask (allocated) | Shared value: the settings from the reference source (*Environmental Rim from*), each source's strength in the mask relative to the strongest. *In the rim*: where rim lighting leaves the output's rim free, the reference source's rim draws it, × the scene's light and ambient (Toon Standard's *Environmental*) instead of the reflection probe; its curve is fitted to the band by the writer. Its strength and base color mix are the ones that look most like the probe's rim on the sources that have it (`AtlasRimResponse`, listed): the probe along the normal is taken as the scene's ambient light, less the rim's threshold, where the output's rim is × the light and the ambient together. Without a mask, it shows on every part (listed); otherwise the note says what takes the rim |
| **Backlight** | 4.4 edge source backlight | 1. Native<br>2. Baked with the rig's back light (form shading)<br>3. Off | backlight color, half size, where the sources' colors differ | Shared value: the settings from the reference source (*Backlight from*); each source's color × texture in the backlight color map (black without), normalized to its brightest value. Light dependent, so no matcap emulation. *In the rim*: where the output's rim is free (after rim lighting and the environmental rim), a band fitted (`AtlasRimCurve.FitBand`) to the backlight's strength at each facing under the rig's back light, seen from the front, where it shows (the brightest direction around the view, × the directivity), × the scene's light, in the reference source's color. It shows on every lit edge, not only against a light behind (listed) |
| **Emission** | 4.1 layers targeting emission<br>emission amounts of other layers | 1. Native<br>2. Off | emission **(Required)**, for the native mode | Bake, relative to the strongest source's emission, which the output glows at. An emission that takes the base color (*source times base color*) takes it at the end of S1, as Poiyomi does: after the surface layers, with the matcaps and rims on the surface at their average look over the view (`AtlasViewLayers`: a matcap's mean over its lookup disc × its color; a rim's strength averaged over the visible surface, ∫ rim(f) 2f df). An emission that also overrides the base color is baked into both: the emission map, and the base color map as its last step, `lerp(b, saturate(e), max(e))`. No *baked into color*: it wouldn't glow, so it isn't the effect (see *Decisions*). Live parts (blinking, scrolling, audio) stay native only where the output has the same modulator; otherwise they are left out |
| **Glitter and sparkle** | 4.6 glitter pattern<br>2.4 glints | 1. Native<br>2. Points in the maps (outputs without glitter; only when chosen, so the default there is Off)<br>3. Off | mask (allocated), native<br>a preserved UV channel, native<br>emission, metallic and roughness, base color, points | Native: a shared value from the reference source (*Glitter from*); the cells read a preserved UV channel holding each source's UV × (its density ÷ the output's), so each source keeps its particle size; opacity × mask in the mask. *Points in the maps* draws the cells as still points in UV space (`AtlasGlitterPattern`) into any of three targets, each with its own strength (*Glow*, *Shine*, *Color*): glowing points in the emission map (added in full; an emission made for them glows at the glitter's brightness, and what goes above the map's range goes into the strength), metallic and smooth points that catch the light and reflections, the glitter's color in the base color. A target whose map the output lacks is left out; the points don't twinkle, and the shape texture, color map and rotation aren't drawn (listed) |
| **Subsurface scattering** | 4.5 subsurface | 1. Native<br>2. Off | subsurface color and thickness **(Required)** | Shared value from the reference source (*Subsurface from*); spatial values for each source's color × strength (relative to the strongest; black where a source has none, which turns it off there) and thickness (relative to the smallest offset). Too dependent on the light to bake or emulate |

### Effects and render

| Feature | Unified | Modes | Maps | Merge and notes |
|---|---|---|---|---|
| **Proximity color** | 1.9 a modulator with source *camera distance*, driving a color adjustment | 1. Native<br>2. Off | — | Shared value from the reference source (*Proximity Color from*). Poiyomi's has no mask, so it covers the whole atlas; when some sources don't have it, that's listed and *Off* is the choice |
| **AudioLink** | 1.3 AudioLink themes<br>1.9 audio modulators<br>4.6 audio patterns | 1. Per feature (outputs whose sections have AudioLink)<br>2. AudioLink emission (outputs with one)<br>3. Off (the feature keeps its rest value) | AudioLink color, for an AudioLink emission | A modulator that leaves its target as it is without music is baked at rest (`AtlasAudio`). Per feature: slots copy their layer's modulators (matcaps, rims, glitter), an audio-driven emission takes an emission slot of its own (slots whose maps come out the same, such as emissions that differ only in their band, share one map); parts without a slot stay at rest, listed. AudioLink emission: the reference source's (*AudioLink from*), each kept source's emission × its audio gain in the AudioLink color map, stretched to its brightest value (a dim emission keeps its precision through 8 bits and compression), the level in the layer's color (Toon Standard: above 1 in the intensity, below 1 in the tint, since an intensity below 1 also lowers the tint's alpha); with smoothing 0, Poiyomi reads the raw level, Toon Standard only the filtered ones (its least filtered lags about 0.1 s); scroll and bar run along the atlas's UVs (listed). AudioLink modes keyed to UV tiles read a preserved UV channel (see [*UV channels*](#uv-channels)) |
| **Surface mode and render queue** | 6 | 1. Automatic: the most see-through mode among the sources that the output supports (opaque, cutout, dithered, transparent, fade, in that order). When transparent or fade sources mix with others, a **fade with depth writes** (Poiyomi's TransClipping): the solid parts, at alpha 1, sort like opaque ones and the transparent sources keep their alpha; a plain blended mode would make the whole avatar sort wrongly. An output that can't do that gets the most see-through of the other sources' modes, and the transparent sources are drawn solid (alpha 1), never cut or dithered. A source whose alpha is turned off counts as opaque<br>2. A chosen mode, including *Fade with Depth* | — | Always on. The queue follows the mode (the writer's queue for it); a queue override isn't there yet. Additive, multiplicative and custom blends aren't merged yet: their sources count as opaque |
| **Fallback shader** | 6 (not typed) | 1. Automatic: the writer's choice<br>2. Toon, Unlit, Standard or Hidden | — | Always on, where the writer sets one. The writer follows the surface mode and culling (Poiyomi: `VRCFallback`, such as `ToonCutout` or `ToonFadeDoubleSided`) |
| **Hidden UV tiles** | 6 tile discard | 1. Live<br>2. Removed from the mesh<br>3. Off | a preserved UV channel, live | *Live*: each source's UVs (in its tiles' channel) go into a UV channel the allocator gives, moved by whole tiles where the output would hide them differently; a tile keeps its number where the sources agree, so animated tile toggles keep their meaning there. Sources without hidden tiles get UVs outside the 4 × 4 grid. One side can be hidden live where the output has it (Poiyomi's Face Discard); a second side is listed. *Removed*: the remap leaves out the triangles with a vertex in a tile hidden on both sides, on any output; one-sided tiles are listed. *Off*: shown |
| **World AO blocker** | 6 render, world AO blocker | 1. Kept live (where the output has one)<br>2. Removed from the mesh<br>3. Off | a preserved UV channel, when a source's blocker reads UV1 to UV3 | One switch for the whole material (`AtlasAOBlockerFeature`). See below |

**Ambient occlusion baked into color** (`AtlasOcclusionShading`). Occlusion darkens what its
shader says (2.4): Toon Standard multiplies the ambient light and the direct light's shadow
ramp, not highlights; Poiyomi multiplies the ambient on the shadow side and the reflections,
not the lit side. Multiplying the color by the occlusion map would darken a Poiyomi source's
lit side too; instead, the bake darkens by the share of the light the source's occlusion
darkens:
- **Diffuse:** the base color × `1 − (1 − tint) × (shares.direct × (1 − o) + shares.ambient ×
  (1 − o ambient))`, lerped from 1 by the overall strength × *Occlusion*. The shares are the
  rig's direct light × the source's *occlusion: direct* and its ambient × *occlusion:
  ambient*, each over their sum, averaged over every direction a surface can face (1024 evenly
  spread): shadow sides, lit by the ambient alone, count fully. With the starting rig, that is
  1 for Toon Standard (direct and ambient) and about 0.31 for Poiyomi (ambient only; the back
  light counts as direct). The darkening is the same over the whole surface, so no light
  direction is baked.
- **Multi-bounce occlusion** (Poiyomi's PBR lighting) raises the ambient's occlusion on light
  colors: o ambient = o × max(1, (a o + b) o + c), Jimenez et al.'s fit (2016) over the base
  color × (1 − metallic).
- **Tint:** a source's occlusion tint (2.4) is what full occlusion darkens toward; its color is
  used, a tint texture is listed.
- **Reflections:** × `lerp(1, o, occlusion: reflections)`, in the output's reflection mask: a
  mask map of its own is multiplied; a highlight mask that read the same map gets its own copy,
  since occlusion doesn't darken highlights; one strength everywhere gets a mask holding the
  occlusion alone. An output without a reflection mask keeps reflections unoccluded, listed.

**World AO blocker.** Triangles with a vertex whose UV lies inside the tile left of the
default one (U −1 to 0, V 0 to 1, edges excluded) render to depth only (Poiyomi from 9.2, on
UV0 to UV3). Screen-space effects that read depth, such as ambient occlusion, treat them as
solid, while they stay invisible.
- *Kept live*, where the output has a blocker: when every source's blocker reads UV0, the
  output's does too, and the remap leaves those triangles' UV0 in their tile; moving them into
  the atlas would make them visible. Otherwise each source's blocker channel goes into a free UV
  channel the output reads, with (0, 0) for the other sources. Without a free channel, blockers
  on UV0 stay live and the others are removed from the mesh, listed. *Flip normal* only changes
  depth-normal passes, which VRChat's built-in pipeline doesn't draw; it's kept where any source
  sets it.
- *Removed from the mesh*, on any output and the default where the output has no blocker: the
  triangles stay invisible but no longer block.
- *Off*: they render like any other triangle, as they would in the source shader without the
  option.

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
reverse, and round-trip tests (parse, write, parse, compare) check it. How the shader
computes what it has no settings for comes from its parser too (see [*The look
model*](#the-look-model)), so an output shader needs one.

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

### What the inspectors keep

Unity redraws an inspector twice a frame while it scrolls, so the rest of the costly work is
kept too:
- **Discovery and warnings** (`AtlasMaterialDiscovery.Cached`, `Warnings`), shared by the
  Atlaser's and the outputs' inspectors, until the scene, the project or an undo changes
  something (`SceneVersion`: hierarchy, project and object change events, and undo).
- **The overview** (features, modes, items, textures and memory), per output inspector, until
  its settings (as JSON), its output shader, a source's analysis or the scene changes.
- **Overlapping UVs** in the *Baking* section, per material, until the scene changes.

### Stored settings

Settings are stored by feature ID, not by output shader, so changing the output shader keeps
every choice the new shader supports:
- **Feature settings:** feature ID, mode, the mode's values, per-source overrides (turned off,
  reference source), stored by source material.
- **Texture settings:** texture ID, enabled, size, size mode.
- **Output settings:** crunch, the formats per platform, and the baking settings: light rig,
  overall strength, color influence, the bake strengths, the matcap view, and each light's
  intensity, temperature and softness.

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
- **Map contributions:** contributions run by stage whatever order they were added in, a
  map's content is set once, a contribution works on the color with the shared color in
  it, and a reader gets the map's value before a stage or as a contribution finds it;
  glitter points are added in full to an emission of any strength.
- **Base colors with layers:** a layer on the base color is part of the base colors, by
  position and where the mesh uses the texture, read from small copies.
- **Bakes:** small synthetic meshes (a quad, a sphere, mirrored halves) give the expected
  values at known texels.
- **The look model:** a lighting's level and contrast in a scene, the sources listed where
  the kept lighting looks different from their own, and a layer's cost for a weaker, a stronger
  and an opposite effect.
- **The reference look:** a layer on the surface is lit with it, an overlay blends with the
  lit color with or without the light, lit and unlit adds, the base color mix, and the
  effect as displayed.
- **Layers by cost:** a rim's effect lies toward the edge, and showing it where there was
  none costs twice what leaving it out does; where the rim slot has no mask, a rim on most
  of the atlas shows on every part and one on a small part is left out unless the user
  gives it the slot; a rim matcap is left out rather than shared with a matcap on other
  parts, unless the user combines them; and the plan says how each one ends up shown.
- **Base color shifts:** where the blocked view shows the base color, a shift stops at the
  average look, the kept base color shows the darkening side alone, and *Full* goes as far as
  the matcap needs, as an output whose blocked view is the same look always does; the
  inspector offers the choice while a slot is fitted.
- **Matcap lookups:** a matcap with a border or a planar reflection lookup is redrawn for a
  slot that looks up by the view-space normal; fixed to the world, it is looked up by the
  view (without its sideways texture offset) or redrawn as seen from the front by the
  user's choice, which the inspector offers while one is in such a slot.
- **Environmental rim in the rim:** weaker than its own strength where the output's rim is
  lit by the scene, and weaker still with a threshold.
- **Shared slots:** two shading matcaps on different materials in one slot each keep the
  average look of their own, also where the slot could show each as it is.
- **Matcaps by their shading:** an even matcap's average and bake lose nothing of its level
  and contrast, a sky's average loses its contrast, and a highlight's bake is judged point
  by point; an even matcap is kept as its average instead of sharing a shaded matcap's
  slot; two shaded matcaps share one, and the log says how much each keeps; a bake is named
  only where it would keep clearly more of a lighting matcap without a highlight.
- **Slot order:** a faint add on a white base comes after a similar one on a dark base
  that covers less of the atlas, since it changes the look less.
- **Average in color:** a matcap placed so takes no slot and its average effect is in the
  base color; a lighting matcap without a slot keeps its average where its mask is.
- **Matcap roles:** a small bright spot is a highlight that a bake would freeze, as is fine
  detail all over; an effect toward the edges all around is a rim; the rest is lighting (an
  even matcap, which a bake keeps; sky above ground; light from a side), with a highlight
  where a soft body has a crisp spot. A colored matcap tints whatever its role, the lookup
  doesn't decide the role, and a matcap that changes nothing says so. Placement follows the
  role: facets are left out of a shared slot, and lighting with a highlight takes its own
  slot while plain lighting shares another.
- **Base colors:** sampled where the mesh uses the texture, not in its unused parts.
- **Tuning:** the kept lighting read from a scratch material equals the written material's;
  an output of the source's own shader keeps the mapped lighting; a value that looks closer
  replaces the mapped one; a generated ramp's edge moves to where the chosen lighting puts
  it; and tuning never leaves the lighting farther from the chosen one.
- **Level compensation:** a source the atlas lights darker is scaled by what its own lighting
  had over it, up to where its brightest texel reaches 1; turned off, the base colors stay;
  a scaled source is compared as the atlas shows it; and the inspector shows the control
  while a source would be scaled.
- **The output shader described by its parser:** Toon Standard's fresnel and reflectance
  fade are read from a material of the shader with nothing set, once, and capabilities of
  no registered shader have none; a rim that blends into the surface and glows is read
  back from Toon Standard as a band added after lighting.
- **A source's own fresnel:** a Toon Standard source costs nothing on Toon Standard at its
  own values, a source with Schlick's fresnel looks different there and is fitted, and
  one whose reflections rise less is closer; Poiyomi's fresnel strength slider converts to
  the unified strength and back.
- **Fits:** a source the output can reproduce gets its exact value back; a fit with nothing
  missing finds that same value; a weaker setting gives the expected direction (weaker
  highlights, a lower reflectance); shared values follow the source whose look changes most.
- **Versions:** where a convention depends on the version, the rule is tested at its edges
  (the last version before a change, the first with it, and the next major version).

---

## Decisions

- **Written materials are read back:** after writing, the build parses the material with the
  output shader's own parser and compares its lighting with what the plan asked for (in a
  canonical form, settings without effect in the setup at their defaults). What differs is
  listed, so a writer's approximations are reported without rules of their own. A same-shader
  round trip lists nothing. The same read-back gives the lighting each source ends up with,
  which is compared with its own (see [*The look model*](#the-look-model)).

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

- **A layer right, or not there:** *Automatic* shows a matcap or a rim otherwise than it is
  (on parts that didn't have it, or as another's) only where that costs less than leaving
  it out, with what it adds counting double. The user's placement overrides it, and the
  inspector shows the placement while *Automatic* leaves a layer out.

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
- **Ambient occlusion:** a source's occlusion map can be baked into the color map, by the share
  of the light its shader lets occlusion darken (see [*Virtual light rigs*](#virtual-light-rigs)).
  Occlusion generated from the mesh is not planned.
- **Memory:** GPU memory of the compressed textures, for PC and Android, per texture and in
  total per material.
- **World AO blocker:** typed as a render feature, one switch for the whole material, kept live
  where the output supports it (the UV remap keeps its tile), and otherwise removed from the
  mesh by default, since showing the triangles would make invisible geometry visible.
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

## Open questions

None at the moment.
