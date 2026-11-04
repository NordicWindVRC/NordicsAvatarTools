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
  `app.nordicwind.texture-wrapper` from the NordicWind listing once it is published.
- **Manual:** copy this folder, including its `.meta` files, to
  `Assets/NordicWind/NordicsTextureWrapper`.
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

## Features

- **Lossless working buffer:** RGBA in 32-bit float per channel, with no 8-bit rounding
  until export. Values outside 0–1 are kept until export.
- **Linear color:** colors are processed as the linear values a shader reads. sRGB textures
  are decoded when loaded and encoded again when saved, so an unchanged texture round-trips
  exactly.
- **Coverage tracking:** a separate per-pixel *usage* mask records which pixels contain real
  data, independent of alpha.
- **Transforms:** resize, crop, tile, offset and rotate, with nearest, bilinear, bicubic and
  area sampling and per-operation wrap modes.
- **Composition:** `Blit` with 14 blend modes, including three for normal maps, plus
  channel routing, masks, strength and padding.
- **Masks:** channel blending, remap, gamma, strength, offset and invert.
- **Color operations:** hue shift and replace (HSV or OKLab), saturation, brightness, contrast,
  gamma, exposure, tint, colorize (perceptual tint), multiply, add, invert, clamp, levels,
  posterize, gradient map and chromatize, each optionally masked. Operations that take a
  color can vary it per pixel with a color map, such as a tint texture.
- **Normal maps:** signed vector storage, lazy normalization, and RNM, Whiteout and lerp
  blending.
- **Loading:** any `Texture2D`, with or without Read/Write and compressed or not. Normal maps
  are decoded from their stored packing (DXT5nm, BC5 or RGB). In the Editor,
  `TextureAssetUtility.Load` reads the data type from the import settings.
- **Mesh baking:** rasterize triangles with interpolated vertex values, with sub-pixel coverage.
- **Normal correction:** express one normal field in another mesh's tangent frame
  (`BakeNormalDifference`), for correcting simplified meshes.
- **Finishing:** dilate edges outward (nearest-pixel padding), and fill uncovered pixels.
- **Output:** in-memory `Texture2D` or PNG assets with importer settings applied. RGB or
  RGBA output can be chosen automatically.
- **Parallel processing** for large textures, with deterministic results for any thread count.

## Roadmap

Known gaps, in no particular order:

- **RNM normal blending** needs a numerical comparison against a reference implementation.
- **Normal correction:** `BakeNormalDifference` needs validation against a reference
  implementation on real meshes.
- **Accuracy:** area downsampling and triangle coverage use bounded supersampling, not exact
  area integration. Padding extends border pixels and doesn't sample outside the UV island.
- **Loading with mipmap streaming:** textures without Read/Write are read from the GPU. When
  mipmap streaming is active (it's off in edit mode by default), the full-resolution level
  may not be loaded yet, and a lower level is read instead.
- **GPU acceleration** for large textures.
- **Performance benchmarks.**

## Key concepts

- **Colors are linear.** The buffer holds what a shader reads. Every color you pass in must be
  linear too: convert material and editor colors with `Color.linear`. `Settings.SRGB` only
  decides how the output is stored. Alpha is never converted.
- **Pixels are data, alpha is data.** Blending strength comes from masks and the `strength`
  argument, never implicitly from the source alpha.
- **Usage is coverage.** Usage is 0 or 1 per pixel. Painting, `Fill`, and padded `Blit`s
  mark pixels used. `FillUnused` and `Dilate` work only on unused pixels. Capture coverage
  before dilating, because dilation marks every pixel used.
- **Normal maps are vectors.** For the `Normal*` data types, RGB holds a signed XYZ vector.
  It's normalized lazily before reads and export, and encoded as `(n + 1) / 2` only on export.
- **Compose with `Resize`.** To tile, offset or rotate a mask independently of its source,
  `Resize` it first and pass the result to `Blit`. Don't look for extra UV options.
- **Threads:** large operations are split across rows, controlled by
  `TextureProcessingSettings`. Unity API calls stay on the main thread. Don't modify one
  wrapper from several threads at once.

## Behaviour contracts

These are part of the API and covered by tests:

- **Output channels:** `TextureSettings.OutputChannels` picks RGB24 or RGBA32. `Auto` uses RGB24
  when every output alpha would be 255. `PreserveAlpha` is legacy, and `OutputChannels` takes precedence.
- **No clamping while painting:** `PaintTriangle` accumulates raw values, including values above
  1. Clamping happens only when encoding output (`GetEncodedPixels`, `GetTexture2D`, export).
  `GetPixel` returns raw values.
- **`BakeNormalDifference(targetNormals, basis)`:** `this` is the original world-normal map,
  and `targetNormals` is the target (for example simplified) world-normal map whose coverage
  limits the result. `basis` must be the **target** mesh's tangent frame. The result is the
  original normal expressed in that frame.
- **`FillUnused(color, markUsed = false, region = null)`:** writes only where usage is 0, and by
  default leaves usage unchanged.
- **`Dilate(region = null)`:** copies the nearest originally used pixel into every unused pixel
  and marks them used. Normal maps get alpha 1. Other data types keep the source alpha. An
  empty region is left unchanged. When two source pixels are equally near, the lower x wins,
  then the lower y. Results don't depend on the number of threads.

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

## Version and status

See `CHANGELOG.md`. The EditMode tests in `NordicWind.TextureWrapper.Tests` pass in
Unity 2022.3.22.

## AI transparency

This library was designed, specified and tested by NordicWind. Its code and documentation
were written together with AI coding assistants, directed and reviewed by NordicWind. The
library itself doesn't use AI and never sends data anywhere. The full statement is in the
*AI use and transparency* section of the NordicWind - Avatar Tools user guide.
