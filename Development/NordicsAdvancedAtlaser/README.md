# Nordic's Advanced Atlaser

> **Work in progress, not released yet.** Builds generate atlased materials for Poiyomi Toon
> 10 and VRChat Mobile Toon Standard; more features are still being added.

A non-destructive, multi-purpose material processor. Its main job is building atlased
materials. It is also meant to:
- optimize materials
- bake normal maps
- derive tuned variants from a set of advanced base materials, such as lower resolution,
  fewer features, or Quest versions of PC materials
- apply per-variant changes, such as a decal on one prefab variant, without maintaining
  another material copy

## What it does

**TL;DR:** each of your materials is read into one common description of how it looks, no
matter which shader it uses. The Atlaser then combines them into one material for the shader
you pick, feature by feature (color, normal maps, matcaps, rim lighting, glitter, …). Each
feature is made the best way that shader allows: as it is, approximated, baked into the
textures, or left out. Where your materials differ, the differences are kept per part of the
atlas, or you choose which material's settings to use. Whatever couldn't be kept, or was only
approximated, is listed in the build log.

In a bit more detail:

1. **Reading your materials.** Each material is read into a description that doesn't depend
   on its shader. The supported shaders are listed below; anything that isn't understood yet
   is reported.
2. **Combining them, one feature at a time.** Each feature gathers the materials that use it
   and merges them. A feature can be made in different **modes**:
   - **Native:** the new material's shader does it itself, as close to the original as it can.
   - **Approximated:** the shader doesn't have it, so the look is recreated with what it does
     have. For example, a clear coat becomes extra gloss, and glitter becomes still, shiny
     points.
   - **Baked:** drawn into the atlas textures.
   - **Off.**

   The inspector only offers the modes the chosen shader supports, starts with the best one,
   and lets you pick another.
3. **Keeping your materials' differences.**
   - Values that differ per material, such as colors, smoothness, strengths and masks, are
     kept per part of the atlas, in its textures.
   - Settings the new material can only have once, such as the lighting setup, come from one
     material. When they differ, a **"… from"** choice picks which one (*Automatic*: the
     material covering the most of the atlas).
   - Things the shader has a limited number of, such as matcaps and rims, are placed one by
     one: automatically, or the way you choose.
   - Where there's no right answer, the inspector shows a choice instead of guessing.
4. **Writing the new material.** The combined description is turned into the new material
   and its textures, and the meshes are moved onto the atlas. Your scene and original
   materials are never changed.

Mapping is per material, not per submesh, because every submesh that uses a material shares
its UV space. For the details, see `UNIFIED_MATERIAL.md` (the description of a material) and
`ATLAS_OUTPUT.md` (how the new material is made).

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
2. **Geometry Baking**, a folding section above the output components, is on by default
   (**Enable Geometry Baking**). It reduces visual artifacts on decimated
   meshes by baking a normal map that compensates for the removed detail. Each material is
   baked at 1.5× its area in the atlas, up to 4096 per side; a larger area is baked 1:1.
   - **Exclude Hidden Geometry (Depth Test)** is on by default. Where several surfaces share
     the same UVs, the ones lying behind another (for example a body part hidden inside the
     mesh) are left out of the bake, so it follows what the avatar shows. Mirrored halves
     aren't affected. Turn it off if a bake looks wrong where copies of a part share UVs.
   - **Excluded Blend Shapes** (optional): mesh parts moved by these blend shapes are left out
     of the bake, so details from parts you remove on purpose aren't baked into the normal map.
     **Threshold** is the minimum vertex movement that counts, in mesh units (default 0.001).
     **Selection mode** is *Any Vertex* or *All Vertices* of a triangle. The avatar mesh and its
     animations are never changed.
3. Add one **Nordic's Atlas Output** per output material on the same GameObject. A new
   output picks its output shader from the source materials: the shader family most of them
   use (Poiyomi or VRChat), at the highest installed version. You can change it; the pick
   only happens when the output is created. Choose the main texture size (default 4096 × 4096), and assign source materials
   with their UV rectangles; each source is listed with its material's name, such as
   *Source 1 (Body)*. **Region Padding** (default 4 px) keeps the sources apart: each
   source's texture is placed inside its region, half the padding from each edge, and its
   meshes are remapped to match, so the regions themselves can touch. It's only used with two
   or more source materials. **Auto Setup** works on the sources
   you assigned (it never adds or removes one): it lays out their UV regions by the size of
   their base color textures, in the smallest atlas that holds them at full size (or 4096 ×
   4096 with every region scaled down alike as little as fits, then grown back where there is
   room), sets the main texture size to match, and sets every feature
   to its default mode. If you edited a region, it asks before replacing it. When the layout
   leaves part of the atlas empty, it asks whether to stretch the regions to fill it: they keep
   all their detail and gain some in one direction, but are no longer in proportion. Undo reverts it
   in one step. Each source material may be assigned to
   only one output.
   - **Textures** is a table of the textures the chosen feature modes need: each one's size and
     an estimate of its GPU memory on PC and Android (compressed, with mipmaps, as VRChat's
     performance rank counts it, which mip streaming doesn't lower), and the totals. *GPU
     Compression Formats* sets the compression per platform and kind of texture, which the
     estimates follow.
   - **Material Features** has one row per feature a source uses and the output shader
     supports, with its mode. Where two or more sources have the feature, the row folds open
     to turn it off for single materials, and its name then says how many still use it. Once
     there are features from several groups (Surface, Lighting, Layers, Effects, Render), each
     group gets a small heading. So far:
     - **Color**: each source's base color: its texture and color, its color layers (details,
       decals, recolor regions, plain colors, with their masks) and its static color
       adjustments, baked together. When every source has the same color and nothing is
       layered on it, the color stays a shared setting. Otherwise colors brighter than 1 are
       kept: the map is scaled down and the material's color makes up for it. Live (animated)
       layers and vertex colors follow later; the build lists them.
     - **Emission**: each source's emission in one emission map, relative to the strongest
       source's, which the output glows at. Sources without emission stay dark. Blinking,
       scrolling, audio or light-based emission follows later.
     - **Alpha**: each source's alpha, in the base color texture's alpha. A cutout source keeps
       its cutoff (moved to the shared one); under a transparent mode its edges stay hard.
       Opaque sources stay solid. A transparent source stays transparent only where the
       output shader can show it so (Poiyomi); otherwise, or when you turn it off for that
       material, it's drawn solid.
     - **Normal Maps**: each source's normals in one normal map: its pre-baked normal map,
       the geometry bake's correction, its own normal map and its normal layers (details on
       UV0), combined in that order. The inspector lists the sources with a normal map or a
       pre-baked one; the build also includes a source whose mesh was reduced, for its
       correction. The output's normal strength is the largest of the
       sources'; the weaker ones are baked at their share of it, so each looks as before.
     - **Reflection and Specular**: each source's metallic and roughness in maps, and how
       strong its highlights and reflections are, in masks (Poiyomi; relative to the
       strongest; no mask where they're the same everywhere). Sources without them stay
       matte. Poiyomi's reflectance is fixed; on Toon
       Standard, where the materials need different reflectance, *Reflectance from* picks
       the material it comes from (*Automatic*: the one with the largest area).
     - **Clear Coat**: on Poiyomi, each material's coat strength and smoothness in a *Clear
       coat maps* texture, and how strong its reflections and highlights are, relative to the
       strongest; materials without a coat have none. Where the coats' other settings (tints,
       fresnel, cubemaps) differ, *Clear Coat from* picks the material they come from. Mobile
       Toon Standard has no coat; *Into the gloss map* makes the coated parts as glossy as
       their coat instead, so they keep its sharp highlights and reflections.
     - **Ambient Occlusion**: each source's occlusion map, at its strength; sources without
       one stay unoccluded.
     - **Detail Texture** and **Detail Normal**: a detail texture or detail normal tiled over
       a material stays sharp: the atlas material samples it through a spare UV channel the
       build writes with each material's own tiling, instead of baking it into the atlas at
       the material's size. Materials using a different detail are baked as before
       (*Detail Texture from* / *Detail Normal from* picks the one kept live); *Baked* bakes
       them all, *Off* leaves them out.
     - **Lighting**: one lighting setup for the atlas, taken from one material. Where the
       materials are lit differently, *Lighting from* picks it (*Automatic*: the one with the
       largest area). Poiyomi keeps the lighting type and Light Data settings; Toon Standard
       its ramp, shadow and brightness settings. Lighting drawn on the texture (shadow masks,
       band textures, SDF faces) follows later; the build lists it, and what the output
       shader can't set the same way.
     - **Shadow Masks** (Poiyomi): each material's shadow strength mask and detail shadows,
       baked into one *Shadow and detail masks* texture.
     - **Shadow Colors** and **Skin Thickness** (Poiyomi): each material's band and shade
       textures, and its skin thickness, baked into their own textures. An SDF face keeps its
       texture, read through the face's own UVs on a spare UV channel.
     - **Matcaps**: each material's matcaps in the output's matcap slots (Poiyomi 4, Mobile
       Toon Standard 1). A matcap that looks the same in several materials uses one slot,
       limited to where each material had it. With more than one matcap, a list under the
       feature places each: *Automatic*, *Own slot*, *Off*, or *Combine with* another matcap,
       which bakes both into one generated matcap texture. *Automatic* combines matcaps on the
       same parts, then gives the largest their own slots and combines the rest into the most
       similar slot. Blends the output shader lacks (Mobile Toon Standard only adds or
       multiplies) are approximated with the nearest, and a matcap mixed with the base color
       is limited by the base color's brightness. The build log lists what was combined,
       approximated or left out.
     - **Rim Lighting**: each material's rims in the output's rim slots (Poiyomi 2, Mobile
       Toon Standard 1), placed like matcaps, with *As matcap* added: the rim drawn into a
       generated matcap in a free matcap slot, which keeps it to its own parts. Mobile Toon
       Standard's rim has no mask, so *Automatic* draws a rim only some materials have into
       the matcap slot when it's free, and otherwise shows it on every part (the build log
       says so). Its rim is a simple band; other rim shapes are fitted to it.
     - **Environmental Rim** and **Backlight** (Poiyomi): one setup for the atlas, from one
       material (*Environmental Rim from*, *Backlight from*: *Automatic* takes the one with
       the largest area); each material's strength, or backlight color, is kept per part.
     - Features the output shader has nothing for are listed in the build log.
     - **Glitter**: on Poiyomi, one glitter setup for the atlas (*Glitter from*), each
       material keeping its own particle size and strength. Mobile Toon Standard has no
       glitter: its particles are drawn as still points that glow (*Glow*), shine in the
       light and reflections (*Shine*) and take the glitter's color (*Color*), each with its
       own strength; they don't twinkle.
     - **Subsurface Scattering** and **Proximity Color** (Poiyomi): one setup for the atlas
       each (*… from*). Subsurface keeps each material's color, strength and thickness in a
       texture; proximity color has no mask, so it covers the whole atlas (the build log says
       so when some materials didn't have it).
     - **Hidden UV Tiles** (Poiyomi's UV tile discard): *Kept live* (the atlas material hides
       the same tiles, read from a spare UV channel the build writes), *Removed from the mesh*
       (any output shader; only tiles hidden on both sides), or *Off*.
     - **Vertex Colors**: *Kept on the mesh* (the atlas material multiplies by them as the
       sources did; the build rewrites each material's vertex colors to match, white where a
       material didn't use them), *Baked into the textures*, or *Off*. Masks that read vertex
       colors are baked either way.
     - Masks, including Poiyomi's global masks, are baked into the features they limit.
       Global mask options that change live (back faces, mirrors, cameras, distance,
       blacklights) stay live on a Poiyomi atlas, for alpha, emission, metallic, smoothness,
       reflections and highlights, occlusion, clear coat, matcaps and rims: the build bakes
       the rest of the mask and keeps the live part in a global mask with the same options.
       When materials of one feature use different live options, *Live from* picks the ones
       kept. The rest (and everything on Mobile Toon Standard, and contacts) is baked as the
       avatar is normally seen, and the build lists it.
     - **Surface Mode**: *Automatic* takes the most see-through mode among the sources that
       the output shader supports (Poiyomi: opaque, cutout, transparent, fade; Mobile Toon
       Standard: opaque). When transparent sources share the atlas with solid ones, it uses
       *Fade with Depth* (Poiyomi's TransClipping): the transparent parts keep their alpha,
       and the solid parts still sort like opaque ones. You can also choose a mode. The render
       queue follows it. When some cutout materials use alpha to coverage and others don't,
       *Alpha to coverage* (On or Off) sets it for all of them.
     - **Fallback Shader** (Poiyomi): what viewers with the shader blocked see. *Automatic*
       follows the surface mode, such as *Toon Cutout*; or choose Toon, Unlit, Standard or
       Hidden.
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
- **Verbose Logging**: the build's diagnostics, including each stage's time (`[Time] …`):
  parsing, the geometry bake's painting, normal difference and dilation, and each output's map
  bakes, packing and compression. They're shown in the NDMF Console, written as one
  information line in Unity's console, and saved to
  `<project>/NordicWindDebug/AdvancedAtlaser/Diagnostics.txt` (replaced on every build) so they
  can be copied. *Bake to Assets…* logs its stages' times in one line.
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
   - Hands each output's job to the output pipeline, which builds the atlas material and its
     textures, then remaps the meshes onto them.
3. Removes both components from the build copy. Scene components and source assets are never changed.

After merging, each configured source material must appear on **at most one** surviving
submesh. If it appears on more, the build stops, because the bake would be ambiguous.

## Supported source shaders

The Atlaser reads these shaders. Each source material is converted into one shader-independent
description before an output shader uses it.


| Shader | Recognized by | What is read | Reported or not covered |
|---|---|---|---|
| Unity **Standard** | Shader name | Rendering modes, base color, metallic/smoothness, normal, parallax, occlusion, emission, detail maps, highlights and reflections | — |
| Unity **Standard (Specular setup)** | Shader name | Same as Standard, with the specular color | — |
| **VRChat/Mobile/Standard Lite** | Shader name | Base color and vertex color, metallic workflow, normal, occlusion, emission, detail, highlights and reflections, specular anti-aliasing | — |
| **VRChat/Mobile/Toon Lit** | Shader name | Base texture, vertex color, flat lighting | — |
| **VRChat/Mobile/Toon Standard** (and Outline) | Shader name | Every section by its toggle: toon ramp, specular, matcap, rim, emission, detail, occlusion, color masks, hue shift, outline, AudioLink | AudioLink's rim mask and UV-based mode (reported) |
| **Mochie/Uber** (and Outline) | Shader name, or its saved properties | Every section of the free version: render, base color and cube mode, workflows and map filters, detail, masks, lighting and shadows, reflections, highlights, matcaps, rims, subsurface, refraction, iridescence, emission, filtering, dissolve, flipbooks, UV distortion, vertex manipulation, outline, AudioLink, light volumes, mirror and near clip | Flipbook alpha, AudioLink rim pulse, AudioLink on vertex rotation and UV distortion (reported). Uber X is unsupported |
| **Poiyomi Toon 10.x, 11.x** (and its avatar variants: Two Pass, Early Outline, Grab Pass, Lil Fur) | Version label in the shader, then name/path, or its saved properties | Every section: UV options and global masks, main color and alpha, normals, all lighting types, reflections, clear coat, decals, emissions, matcaps and cubemaps, rims and depth rims, dissolve, flipbooks, glitter, voronoi, truchet, pathing and the other patterns, outlines, vertex options, fur, AudioLink, Two Pass's second pass and Grab Pass's screen effects | A few details, each reported (for example hextile sampling and the newer UV distortion modes); the stats overlay is out of scope |
| **Poiyomi Pro 9.x, 10.x, 11.x** (and its variants) | Shader name, then the version label, or its saved properties | Everything Poiyomi Toon reads, plus SSAO, contact shadows, TPS, squish, constellation, the lava lamp, GT7 tone mapping and neutral emission, the outline bake, shell fur, Triple Outline, Self Grab, Triplanar Projection and tessellation | Geometric Dissolve, ShatterWave and the particle variants (reported) |
| **Poiyomi Toon 7.3, 8.x, 9.x** | As above | Read on the Poiyomi 10 parser with version tables for renamed and re-meaning settings | Features Poiyomi 10 rewrote are read with 10's meaning and noted; 7.3's own specular, metallics, BRDF, clear coat, SSS and panosphere sections are reported. Pro 9's screen-space AO, constellation and shell fur are read with Pro 10's meaning and noted. Pro before 9 is read as Toon of its version, without its Pro features |
| Poiyomi Toon (unversioned) | Poiyomi Toon without a version | As above, best effort | As above |
| **Any other shader** | Fallback | Common property names with Unity's conventions: base color, alpha mask, normal, metallic or specular, roughness or smoothness, occlusion, emission, culling and blending | Anything shader-specific; matcaps (reported) |

Features that can't be read exactly are recorded per material, never silently assumed; the
build lists them with *Verbose Logging* (Tools > NordicWind > Advanced Atlaser). See `UNIFIED_MATERIAL.md` for the details of each parser.

**Source shaders don't need to be installed.** A material whose shader is missing, such as a
Poiyomi material copied into a Quest project, is still read from the values it saved. Poiyomi
and Mochie Uber are recognized from their properties, including the Poiyomi version and Pro.
A few option names that only the shader declares are reported instead of read.

## Output shaders

| Output shader | ID | Features | Status |
|---|---|---|---|
| **Poiyomi Toon 10** | `poiyomi10` | Color (the base color texture) | Generated |
| **VRChat Mobile Toon Standard** | `mobiletoon` | Color (the base color texture) | Generated |

**Output shaders must be installed,** since the new materials are made with them. The picker
only lists installed ones. An output whose shader isn't installed shows a warning, and is
skipped at build time with one diagnostic. Poiyomi Toon 10 is found under its usual name, or
under *Old Versions* when Poiyomi Pro or 11 is installed.

The settings belong to shared output features, offered as far as each output shader supports
them (see `ATLAS_OUTPUT.md`); changing the output shader keeps every choice the new one
supports.

## Limitations

- Outputs carry only the base color texture so far: the color it's multiplied by, the
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
