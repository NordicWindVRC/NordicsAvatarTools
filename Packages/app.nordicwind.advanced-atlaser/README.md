# Nordic's Advanced Atlaser

> **Beta.** Settings may still change between beta versions; the changelog says when a
> configuration must be redone.

A non-destructive material processor for VRChat avatars. It combines an avatar's materials into
atlased materials for the shader you pick, when the avatar is built. It reads each material,
whatever its shader, into one common description, then makes every feature (color, normal maps,
matcaps, rim lighting, glitter, …) the best way the output shader allows: as it is,
approximated, baked into the textures, or left out. Where your materials differ, the
differences are kept per part of the atlas, or you choose which material's settings to use.
The build log lists what couldn't be kept, or was only approximated.

| | |
|---|---|
| Package | `app.nordicwind.advanced-atlaser` |
| Components | **Nordic's Advanced Atlaser** and **Nordic's Atlas Output** |
| Requirements | Unity 2022.3, NDMF 1.7.0 or later, Nordic's Texture Wrapper 0.10.0 or later |
| Works with | Avatar Optimizer, Modular Avatar, VRCFury; none of them required |

## Install

- **VCC:** add *Nordic's Advanced Atlaser* from the NordicWind listing. VCC installs the
  Texture Wrapper with it.
- **Manual:** put this package and Nordic's Texture Wrapper in `Packages/`.

## Getting started

1. Add **Nordic's Advanced Atlaser** to the object with the **Skinned Mesh Renderer** whose
   materials you want to combine, or to an Avatar Optimizer **Merge Skinned Mesh** object (its
   merged renderers' materials are used). The inspector lists the materials it found.
2. Add one **Nordic's Atlas Output** per atlased material, on the same object, and assign its
   source materials. Each material can belong to one output only.
3. Press **Auto Setup**: it lays out the sources' regions, sets the texture size and every
   feature's default mode.
4. Look through the output's sections: **Textures** (sizes and memory), **Material Features**
   (how each feature is made) and **Baking** (when something is baked with light). Every
   setting explains itself in its tooltip.
5. Build the avatar: the atlas is made in the build copy only, so your scene, prefabs and
   materials are never changed. **Bake to Assets…** writes the same material and textures to a
   folder, to look at them without building.

**Normal Baker** (**Tools → NordicWind → Advanced Atlaser**) bakes a normal map for a
lower-detail mesh from its detailed original, for a source's *Pre-baked Normal Map*.
**Verbose Logging** in the same menu writes the build's diagnostics to Unity's console and to
`Diagnostics.txt` in the debug output folder (**Open Debug Output Folder** in the same menu).
The NDMF Console lists only what needs attention.

## Supported shaders

**Input:** the shaders your materials can use. A material whose shader isn't installed is
still read from the values it saved. What can't be read exactly is listed with *Verbose
Logging*.

| Shader | Notes |
|---|---|
| Poiyomi Toon 10.x, 11.x, and its avatar variants (Two Pass, Early Outline, Grab Pass, Lil Fur) | Every section |
| Poiyomi Pro 9.x, 10.x, 11.x, and its variants | Everything Toon reads, plus the Pro features |
| Poiyomi Toon 7.3, 8.x, 9.x, and unversioned | Read with Poiyomi 10's meaning; differences are listed |
| VRChat Mobile Toon Standard (and Outline) | Every section |
| VRChat Mobile Toon Lit, Standard Lite, Diffuse, Bumped Diffuse | Every setting |
| Mochie Uber (and Outline) | Every section of the free version; Uber X is unsupported |
| Unity Standard, Standard (Specular setup) | Every setting |
| Any other shader | Common property names: base color, alpha, normal, metallic or specular, smoothness, occlusion, emission, culling and blending |

**Output:** the shaders an atlas can be made for. The one you pick must be installed.

| Shader | Notes |
|---|---|
| Poiyomi Toon 10 | Keeps nearly everything your materials do. The closest match for Poiyomi materials |
| Poiyomi Toon 11 | Keeps nearly everything your materials do, for projects on Poiyomi 11 |
| Poiyomi Pro 10 | For Poiyomi Pro 10. Keeps nearly everything, including Pro's tone mapping and depth-based shadows |
| Poiyomi Pro 11 | For Poiyomi Pro 11. Keeps nearly everything, including Pro's tone mapping and depth-based shadows |
| VRChat Mobile Toon Standard | For Quest and mobile avatars. Toon shading with one matcap, one rim light and emission. Solid materials only: transparency is not kept |
| VRChat Mobile Diffuse | The lightest choice for Quest: one color texture with simple lighting. No shine, glow or transparency |
| Unity Standard | Realistic lighting for PC. Keeps color, transparency, normal maps, shine and emission. Toon effects such as matcaps and rim lights are averaged into the color or left out |
| Unity Standard (Specular setup) | Realistic lighting for PC, with shine set per material: materials without shine stay matte. The better of the two Standard choices for materials that come from a toon shader |

## Limitations

- Animations that swap materials by slot on a merged renderer aren't adjusted.
- Baked shading (normal-map shading, occlusion, matcaps) is fixed in the textures: it doesn't
  follow the world's lights or the viewer.
- Bakes use the mesh in its current pose and UV0.

## Documentation

- [CHANGELOG.md](CHANGELOG.md): what each version changed, and what it was tested with.
- [ATLAS_OUTPUT.md](ATLAS_OUTPUT.md): how an atlas is made, feature by feature.
- [UNIFIED_MATERIAL.md](UNIFIED_MATERIAL.md): how each source shader is read.
- [NORMAL_BAKER.md](NORMAL_BAKER.md): the Normal Baker window.

## AI transparency

This tool was designed, specified and tested by NordicWind. Its code and documentation were
written together with AI. See [AI use and transparency](../app.nordicwind.avatar-tools/README.md#ai-use-and-transparency)
for the full statement.
