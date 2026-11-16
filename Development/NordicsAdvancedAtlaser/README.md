# Nordic's Advanced Atlaser

> **Work in progress.** Input parsing, the inspector UI, geometry baking and debug output
> work. **Output materials are not generated yet**, so builds are unchanged apart from
> removing the components.

A non-destructive, multi-purpose material processor. Its main job is building atlased
materials. It is also meant to:
- optimize materials
- bake normal maps
- derive tuned variants from a set of advanced base materials, such as lower resolution,
  fewer features, or Quest versions of PC materials
- apply per-variant changes, such as a decal on one prefab variant, without maintaining
  another material copy

Mapping is **per material**, not per submesh, because every submesh that uses a material
shares that material's UV space. Each source material is parsed into one
**shader-agnostic unified material**. Independent **output generators** read only that
unified model and never see the source shader. See `UNIFIED_MATERIAL.md` for the model
and `ATLAS_OUTPUT.md` for the output design.

| | |
|---|---|
| Package | `app.nordicwind.advanced-atlaser` |
| Components | **Nordic's Advanced Atlaser** (`NordicsAdvancedAtlaser`) and **Nordic's Atlas Output** (`NordicsAtlasOutput`) |
| Requirements | Unity 2022.3, NDMF 1.7.0 or later, Nordic's Texture Wrapper 0.10.0 or later |
| Works with | Avatar Optimizer (Merge Skinned Mesh sources are discovered, including nested merges, and the components register with AAO when it is installed), Modular Avatar, VRCFury. None of them are required. |

## Install

- **VCC:** add `app.nordicwind.advanced-atlaser` from the NordicWind listing, once it is
  published. VCC installs the Texture Wrapper automatically.
- **Manual:** copy this folder and `NordicsTextureWrapper` to `Assets/NordicWind/` or to
  `Packages/`. Install only one copy of each.

## Usage

1. Add **Nordic's Advanced Atlaser** to the object whose materials you want to combine:
   - **A normal mesh object:** add it to the object with the **Skinned Mesh Renderer**. Its
     materials become the available source materials.
   - **An Avatar Optimizer *Merge Skinned Mesh* object:** add it to the object with the
     **Merge Skinned Mesh** component. The source materials are then those of every renderer
     being merged into it, including renderers that are themselves merges.

   The inspector lists the source materials it found, so you can check before going on.
2. **Enable Geometry Baking** is on by default. It reduces visual artifacts on decimated
   meshes by baking a normal map that compensates for the removed detail. Each material is
   baked at 1.5× its area in the atlas, up to 4096 per side; a larger area is baked 1:1.
   - **Excluded Blend Shapes** (optional): mesh parts moved by these blend shapes are left out
     of the bake, so details from parts you remove on purpose aren't baked into the normal map.
     **Threshold** is the minimum vertex movement that counts, in mesh units (default 0.001).
     **Selection mode** is *Any Vertex* or *All Vertices* of a triangle. The avatar mesh and its
     animations are never changed.
3. Add one **Nordic's Atlas Output** per output material on the same GameObject. Choose the
   output shader and the main texture size (default 4096 × 4096), and assign source materials
   with their UV rectangles. **Region Padding** (default 4 px) separates the sources' regions,
   so it's only used with two or more source materials. **Auto Setup** picks the largest base
   texture and sets every feature to its default mode. Each source material may be assigned to
   only one output.
   - **Textures** lists the textures the chosen feature modes need, with their sizes and an
     estimate of their GPU memory on PC and Android (compressed, with mipmaps, as VRChat's
     performance rank counts it, which mip streaming doesn't lower), and the totals. *Formats* sets the compression per platform
     and kind of texture, which the estimates follow.
   - **Features** are grouped into sections (Surface, Lighting, Layers, Effects, Render). Each
     feature a source uses and the output shader supports folds to one line with its mode and
     how many sources use it; open, it has the mode and, with two or more sources, a list to
     turn it off per source.
   Turn off **Normal Correction** on a source to skip its normal-correction bake, for example
   when that material's mesh isn't reduced.

   **Pre-baked Normal Map** (optional) is a normal map made for this mesh, for example for a
   lower-detail Quest version of a PC mesh. It's applied at strength 1. It doesn't replace the
   build's normal correction, which only sees changes made during the build. A source's normal
   maps are combined in this order, each on top of the previous one:
   1. the pre-baked normal map
   2. the build's normal correction
   3. the material's primary normal map
   4. its secondary normal map
   5. its detail normal map

   When sources with different normal strengths share an atlas, all of them are rescaled to the
   largest strength.

### Normal Baker

**Tools → NordicWind → Advanced Atlaser → Normal Baker** bakes a normal map for a lower-detail
mesh, for example a Quest version of a PC mesh, from the detailed original. Drag both meshes
(assets or scene objects) into the window, pick a submesh for each, and click **Generate…** to
choose where to save the PNG. Use it as a source's **Pre-baked Normal Map**. Both meshes must use
the same UV layout. See `NORMAL_BAKER.md`.

### Debug options

These are per-editor settings under **Tools → NordicWind → Advanced Atlaser**:
- **Verbose Logging**
- **Export Intermediate Maps**: EXR and PNG bakes written to `<project>/NordicWindDebug/AdvancedAtlaser/`
- **Build Summary**: on by default
- **Open Debug Output Folder**

Exporting 4096² float maps is slow, so leave it off for everyday builds.

## How it works

1. **NDMF Resolving:** captures the original geometry of each configured material, in
   avatar-root space with the current pose applied, before AAO or other tools change it.
2. **NDMF Optimizing, after AAO:**
   - Finds the surviving renderers and parses each assigned source material once into the
     unified model. Unsupported or approximate features are reported.
   - Bakes, in **source UV space** using the Texture Wrapper:
     - a tangent-space normal correction that reconstructs the original shading on the
       surviving (for example reduced) mesh, from the original and surviving normals and the
       surviving mesh's tangent frame. It never makes shading worse: overlapping UVs and
       implausibly large corrections (above 60°) get no correction. The maps used to make it
       are released as soon as it's done.
     - a usage mask: which texels the surviving mesh covers.
   - Skips the normal-correction bake, keeping only the fast usage mask and a flat correction,
     when it's turned off for the material, or when every surviving triangle is an unchanged
     original triangle (removed submeshes or triangles only). A reducer that removes vertices
     creates new triangles, so its result is always baked.
   - Hands output jobs to the output generators, which are placeholders for now.
3. Removes both components from the build copy. Scene components and source assets are never changed.

After merging, each configured source material must appear on **at most one** surviving
submesh. If it appears on more, the build stops, because the bake would be ambiguous.

## Supported source shaders

The Atlaser reads these shaders. Each source material is converted into one shader-independent
description before an output shader uses it.


| Shader | Recognised by | What is read | Reported or not covered |
|---|---|---|---|
| Unity **Standard** | Shader name | Rendering modes, base color, metallic/smoothness, normal, parallax, occlusion, emission, detail maps, highlights and reflections | — |
| Unity **Standard (Specular setup)** | Shader name | Same as Standard, with the specular colour | — |
| **VRChat/Mobile/Standard Lite** | Shader name | Base color and vertex color, metallic workflow, normal, occlusion, emission, detail, highlights and reflections, specular anti-aliasing | — |
| **VRChat/Mobile/Toon Lit** | Shader name | Base texture, vertex color, flat lighting | — |
| **VRChat/Mobile/Toon Standard** (and Outline) | Shader name | Every section by its toggle: toon ramp, specular, matcap, rim, emission, detail, occlusion, color masks, hue shift, outline, AudioLink | AudioLink's rim mask and UV-based mode (reported) |
| **Mochie/Uber** (and Outline) | Shader name, or its saved properties | Every section of the free version: render, base colour and cube mode, workflows and map filters, detail, masks, lighting and shadows, reflections, highlights, matcaps, rims, subsurface, refraction, iridescence, emission, filtering, dissolve, flipbooks, UV distortion, vertex manipulation, outline, AudioLink, light volumes, mirror and near clip | Flipbook alpha, AudioLink rim pulse, AudioLink on vertex rotation and UV distortion (reported). Uber X is unsupported |
| **Poiyomi Toon 10.x, 11.x** (and its avatar variants: Two Pass, Early Outline, Grab Pass, Lil Fur) | Version label in the shader, then name/path, or its saved properties | Every section: UV options and global masks, main colour and alpha, normals, all lighting types, reflections, clear coat, decals, emissions, matcaps and cubemaps, rims and depth rims, dissolve, flipbooks, glitter, voronoi, truchet, pathing and the other patterns, outlines, vertex options, fur, AudioLink, Two Pass's second pass and Grab Pass's screen effects | A few details, each reported (for example hextile sampling and the newer UV distortion modes); the stats overlay is out of scope |
| **Poiyomi Pro 9.x, 10.x, 11.x** (and its variants) | Shader name, then the version label, or its saved properties | Everything Poiyomi Toon reads, plus SSAO, contact shadows, TPS, squish, constellation, the lava lamp, GT7 tone mapping and neutral emission, the outline bake, shell fur, Triple Outline, Self Grab, Triplanar Projection and tessellation | Geometric Dissolve, ShatterWave and the particle variants (reported) |
| **Poiyomi Toon 7.3, 8.x, 9.x** | As above | Read on the Poiyomi 10 parser with version tables for renamed and re-meaning settings | Features Poiyomi 10 rewrote are read with 10's meaning and noted; 7.3's own specular, metallics, BRDF, clear coat, SSS and panosphere sections are reported. Pro 9's screen-space AO, constellation and shell fur are read with Pro 10's meaning and noted. Pro before 9 is read as Toon of its version, without its Pro features |
| Poiyomi Toon (unversioned) | Poiyomi Toon without a version | As above, best effort | As above |
| **Any other shader** | Fallback | Common property names with Unity's conventions: base color, alpha mask, normal, metallic or specular, roughness or smoothness, occlusion, emission, culling and blending | Anything shader-specific; matcaps (reported) |

Features that can't be read exactly are recorded per material, never silently assumed; the
build lists them with *Verbose Logging* (Tools > NordicWind > Advanced Atlaser). See `UNIFIED_MATERIAL.md` for the details of each parser.

**Source shaders don't need to be installed.** A material whose shader is missing, such as a
Poiyomi material copied into a Quest project, is still read from the values it saved. Poiyomi
and Mochie Uber are recognised from their properties, including the Poiyomi version and Pro.
A few option names that only the shader declares are reported instead of read.

## Output shaders

| Output shader | ID | Features | Status |
|---|---|---|---|
| **Poiyomi Toon 10** | `poiyomi10` | Colour (the base colour texture) | Generated |
| **VRChat Mobile Toon Standard** | `mobiletoon` | Colour (the base colour texture) | Generated |

**Output shaders must be installed,** since the new materials are made with them. The picker
only lists installed ones. An output whose shader isn't installed shows a warning, and is
skipped at build time with one diagnostic. Poiyomi Toon 10 is found under its usual name, or
under *Old Versions* when Poiyomi Pro or 11 is installed.

The settings belong to shared output features, offered as far as each output shader supports
them (see `ATLAS_OUTPUT.md`); changing the output shader keeps every choice the new one
supports.

## Limitations

- Outputs carry only the base colour texture so far: the colour it's multiplied by, the
  other maps and every other feature come in later versions. The build compresses the
  textures for the platform being built; **Bake to Assets…** writes the same material and
  textures to a folder without building.
- Merging an output's submeshes removes material slots, so animations that swap materials by
  slot on the same renderer aren't adjusted yet.
- Normals come from the mesh in its current pose, not from skinned animation. UV0 is used for bakes.
- Matching original geometry to the surviving geometry is best-effort when AAO remaps it.
- Development builds have no migration code. Configuration may need to be recreated after
  structural changes.

## Key source files

| Path | Responsibility |
|---|---|
| `Editor/Inputs/` | Source shader parsers, the material reader and version detection |
| `Editor/Unified/` | The unified material model |
| `Editor/Outputs/` | Output generator interface and shader schemas |
| `Editor/Build/` | Geometry capture, pose evaluation, blend-shape exclusion and baking |
| `Editor/NDMF/` | Plugin registration, build coordination, reporting and cleanup |
| `Editor/Debug/` | Debug settings and intermediate map export |

## Verification status

Version 0.40.0 builds against Unity 2022.3.22, NDMF 1.14.8 and AAO 1.9.19. Its EditMode tests
(`NordicWind.AdvancedAtlaser.Tests`) pass in Unity 2022.3.22; tests that need a source shader
run when that shader is imported. Version history is in `CHANGELOG.md`.

## AI transparency

This tool was designed, specified and tested by NordicWind. Its code and documentation were
written together with AI. See [AI use and transparency](../../Packages/app.nordicwind.avatar-tools/README.md#ai-use-and-transparency)
for the full statement.
