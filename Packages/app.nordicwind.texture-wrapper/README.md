# Nordic's Texture Wrapper

A texture-processing library for Unity tool developers. It loads, creates, combines and edits
textures in full floating-point precision, tracks which pixels are actually covered, and bakes
data from meshes into textures. For example, it can render a mesh's normals into a texture,
compare an original and a simplified mesh, and produce a normal map that hides the shading
artifacts of the lower polygon count.

It runs on the CPU and uses all cores for large textures. The core library works anywhere
in Unity, while the exporters that write texture assets need the Unity Editor. It has no
VRChat, NDMF or shader dependencies.

| | |
|---|---|
| Package | `app.nordicwind.texture-wrapper` |
| Assemblies | `NordicWind.TextureWrapper.Runtime`, plus `NordicWind.TextureWrapper.Editor` for the file and memory exporters |
| Namespace | `NordicWind.TextureWrapper` |
| Main class | `NordicsTextureWrapper` |
| Requirements | Unity 2022.3 or later |
| API reference | [API.md](API.md) |

**The public API is a contract.** New features add overloads or optional parameters, and
anything removed is marked `[Obsolete]` for at least one minor version first.

## Install

- **VCC:** tools that need the Texture Wrapper install it automatically. You can also add
  *Nordic's Texture Wrapper* from the NordicWind listing.
- **Manual:** put this folder, including its `.meta` files, in `Packages/`. An older copy in
  `Assets/NordicWind/NordicsTextureWrapper` is replaced by the package (`legacyFolders`).
- In your own assembly definition, reference `NordicWind.TextureWrapper.Runtime`, plus
  `NordicWind.TextureWrapper.Editor` if you export textures.

## Quick example

```csharp
using NordicWind.TextureWrapper;
using UnityEngine;

// Load two textures (Read/Write isn't needed), and tile the detail 8×8.
using var baseColor = new NordicsTextureWrapper(baseTexture);
using var detailSource = new NordicsTextureWrapper(detailTexture);
using var detail = detailSource.Resize(512, 512, scale: new Vector2(8, 8));

// Place the base texture in the lower-left quarter of a new 2048×2048 atlas, with 8 px padding,
// then multiply the tiled detail over the same area.
using var atlas = new NordicsTextureWrapper(2048, 2048);
Rect area = atlas.Blit(baseColor, region: new Rect(0, 0, .5f, .5f), padding: 8);
atlas.Blit(detail, region: area, blend: TextureBlendMode.Multiply);

Texture2D preview = atlas.GetTexture2D();                                       // in memory
// atlas.Export(new PngAssetTextureExporter("Assets/Generated"), "Atlas");    // as a PNG asset
```

## What it does

- Loads any `Texture2D` (with or without Read/Write, compressed or not) or creates a texture,
  held as linear float RGBA with a per-pixel *usage* (coverage) flag.
- Resizes, crops, tiles and rotates; composites with blend modes, masks and padding; applies
  color operations.
- Rasterizes mesh triangles into UV space, and bakes normal corrections between an original and
  a simplified mesh.
- Pads islands (dilation) and writes `Texture2D`s or PNG assets with their import settings.
- Runs on every core for large textures, and on the GPU for dilation and batch triangle painting
  ([GPU_ACCELERATION.md](GPU_ACCELERATION.md)).

Every member, its conventions and its guaranteed behavior are in [API.md](API.md).

## Roadmap

Known gaps, in no particular order:

- **Normal correction on overlapping UVs:** texels where surfaces overlap (mirrored halves,
  stacked copies) get no correction, which is safe but leaves them unimproved. Mirrored halves
  that were reduced symmetrically need the same correction, so it could be kept where they agree.
- **Accuracy:** area downsampling and triangle coverage use bounded supersampling, not exact
  area integration. Padding extends border pixels and doesn't sample outside the UV island.
- **Loading with mipmap streaming:** textures without Read/Write are read from the GPU. When
  mipmap streaming is active (it's off in edit mode by default), the full-resolution level
  may not be loaded yet, and a lower level is read instead.
- **More GPU acceleration:** dilation and triangle painting run on the GPU for large
  textures; `Resize`, `Blit` and `ApplyColor` are candidates (see *Future candidates* in
  [GPU_ACCELERATION.md](GPU_ACCELERATION.md)).
- **Performance benchmarks.**

## Tests

In Unity, open **Window → General → Test Runner → EditMode** and run
`NordicWind.TextureWrapper.Tests`.

## Design decisions

- The wrapper is one step in a pipeline, never an all-in-one material converter. Callers
  decide the order of operations, atlas layout and asset lifetime.
- **Shader-neutral.** The wrapper knows no shaders or shader versions. Operations are named
  by what they do, and callers map shader features onto them. Ideas from a particular shader
  are welcome when they're useful in general, but results aren't meant to match any shader
  exactly.
- The float buffer is authoritative. 8-bit rounding happens only at output.
- Changing data or normalization invalidates cached output. Changed disk output is saved
  again, and importer settings are re-applied.
- Cloning copies settings and optionally the pixel and usage buffers, never cached textures
  or export targets.

## Attribution

The *Mixed* blend mode, `Colorize`, `Chromatize` and the mask channel blending are based on
formulas in the Poiyomi Toon 10 shader source (MIT license). No Poiyomi source files are
included. Poiyomi is a separate project, and its shader source and license remain with its
authors.

## AI transparency

This library was designed, specified and tested by NordicWind. Its code and documentation
were written together with AI coding assistants, directed and reviewed by NordicWind. The
library itself doesn't use AI and never sends data anywhere. The full statement is in the
*AI use and transparency* section of the NordicWind - Avatar Tools user guide.
