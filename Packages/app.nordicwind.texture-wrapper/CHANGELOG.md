# Changelog — Nordic's Texture Wrapper

Newest first. Versions follow [SemVer](https://semver.org).

## 0.10.0 — unreleased

### Changed
- **`BakeNormalDifference`'s angle limit is soft.** Corrections above half of `MaxAngle` are
  compressed smoothly toward it instead of fading to flat over the last 10°, where a larger raw
  correction gave a smaller result and left a visible step in the shading. Corrections that would
  point into the surface are compressed the same way. `Limited` now counts raw corrections
  beyond the limit.
- **Behavior change: colors are linear inside the wrapper.** The C# API is unchanged, but
  results differ for sRGB textures and sRGB output:
  - Loading an sRGB `Texture2D` decodes its colors to linear. A linear texture's values are
    kept as stored.
  - Output with `Settings.SRGB = true` is sRGB-encoded, in `GetEncodedPixels`, `GetTexture2D`
    and both exporters.
  - Blending, filtering and color operations now work on linear values, like shaders and
    GPU filtering do. An unchanged sRGB texture still round-trips byte for byte.
  - Colors passed in must be linear. Convert material colors with `Color.linear`.
- **Breaking: the wrapper is shader-neutral.** `ColorOperationParameters.PoiyomiCompatibility`
  is removed, and with it the alternative operation modes:
  - `Saturation` and `Brightness` always use their standard scales (1 = unchanged). Pass
    `1 + amount` where a shader uses 0 = unchanged.
  - `Tint` always multiplies. The perceptual tint is the new `Colorize` operation.
  - `Chromatize` always uses the OKLab version, limited to the chroma of the fully saturated
    color with the same hue. The simple RGB version is gone.
- The `Mixed` blend mode is now `base + base × blend` instead of a copy of `Average`.

### Fixed
- **Crunch-compressed textures with Read/Write on couldn't be loaded:** `GetPixels` doesn't
  decode crunch, so loading threw. They are read through the GPU, like textures without
  Read/Write.
- **Color operations ran close to single-threaded in Unity:** applying operations allocated an
  enumerator per pixel, and masks without options allocated their default options per pixel.
  Allocations in parallel loops serialize on the garbage collector, so `ApplyColor`,
  `Resize` with operations and masked `Blit`s now allocate nothing per pixel.
- **`Blit`'s `strength` for color blends:** it scales the influence, as documented; before, it
  only had an effect on normal blends.

### Added
- `NormalDifferenceOptions.Keep`: a per-texel map of how much of the correction to keep, for
  rules the caller knows; `NormalDifferenceStatistics.Kept`, `MeanAngleBefore` and
  `MeanAngleAfter`, and `NordicsTextureWrapper.SoftLimit`.
- `TextureSamplingMode.AreaFlatten`: area sampling that, for tangent-space normal maps, tilts
  each texel back toward flat by how much the normals it covers disagree.
- **`GetPixels` and `SetPixels`:** read and write every pixel at once, for whole-texture
  processing outside the wrapper without a call per pixel; `GetPixels(Color[])` fills an
  array you reuse.
- **Pixel buffer reuse:** disposed wrappers' pixel and usage arrays are kept (up to
  `TextureProcessingSettings.PooledBytes`, 1 GiB by default) for the next wrapper of the same
  size, instead of allocating hundreds of megabytes per operation on large textures.
  `ReleasePooledBuffers` frees them.
- **Faster same-size `Blit`:** a source of the region's size, copied without a transform,
  operations, padding or the usage channel, is read directly instead of through a resampled
  copy.
- **`ContentRegion`:** the rectangle `Blit` fills with content for a region and padding on a
  texture of a given size, without blitting.
- **`PaintTriangles`:** paints a batch of triangles (three corners each) with the same result as
  `PaintTriangle` per triangle in order. The CPU version paints tiles of the texture on every
  core instead of one triangle at a time, which mostly ran on one core; large batches on large
  textures run on the GPU, drawing the triangles in UV space.
- **Hidden surfaces:** a `PaintTriangles` overload with `HiddenSurfaceOptions` (a position and
  normal per corner) leaves out surfaces lying behind another surface with the same UVs and
  UV winding, measured along the average normal at each pixel. On the CPU and the GPU.
- **`Dilate` on the GPU** for whole textures from `MinGpuPixels` up, by jump flooding. Ties and,
  very rarely, the nearest pixel may differ from the CPU's.
- **GPU groundwork:** `TextureProcessingSettings.UseGpu`, `MinGpuPixels` and `GpuAvailable`,
  and internal helpers that move pixels and usage flags to float render textures and back
  without changing them. No operation uses the GPU yet; the plan is in `GPU_ACCELERATION.md`.
  The CPU stays the reference and the fallback.
- **Safety rules for normal correction.** A `BakeNormalDifference` overload takes
  `NormalDifferenceOptions` and returns `NormalDifferenceStatistics`. Where a rule applies, the
  correction fades to flat, so it never makes shading worse than no correction:
  - With a coverage map counted per UV winding (`CoverageColor`), texels covered by more than
    one surface get no correction: mirrored halves (both windings) and stacked copies (one
    winding twice). This includes texels where the overlapping frames cancel out, which used to
    be left for dilation to fill from a neighboring island.
  - Corrections beyond `MaxAngle` fade out over its last 10°, and corrections that would point
    into the surface are never stored.
  The overload without options is unchanged.
- **Loading any texture.** Textures without Read/Write, compressed or not, are read through
  the GPU instead of throwing.
- **Normal map decoding.** A new constructor overload takes a `TextureDataType`. For
  `NormalTangent`, the stored packing is decoded into vectors (DXT5nm style with X in alpha,
  or BC5/RGB with X in red) and Z is rebuilt. Object and world-space normals decode RGB as XYZ.
- `TextureAssetUtility` (Editor): `GetDataType` reads a texture's data type from its import
  settings, and `Load` loads it with that type.
- Normal bake tests: round trips for random orthonormal frames in both handednesses, for
  interpolated (not unit, not orthogonal) frames with either bitangent convention, and a flat
  frame that is skipped.
- Fill rule tests: a quad split along a diagonal through sub-samples (both windings), a fan of
  triangles around a vertex on a sub-sample, and a grid, with 1, 2 and 4 sub-samples per axis:
  never covered more than once.
- RNM normal blending tests against an independent reference (rotating the detail onto the
  base), including flat inputs and preserved angles.
- Safety rule tests: overlap by winding and by stacking, the angle fade, and default options
  never storing a correction into the surface. A whole-mesh test with two mirrored halves
  sharing UVs: neither is ever shaded worse than without correction.
- Whole-mesh normal correction tests, with normal and mirrored UVs: a curved original and a
  flat reduced mesh, painted and baked, then decoded at every texel. The corrected shading is
  within 0.5° of the original everywhere (about 0.02° on average, against 16° uncorrected).
- Loading tests: the GPU path and its orientation, sRGB decoding through the GPU, both normal
  packings readable and not, object-space normals, and a real imported normal map.
- `Colorize` color operation: a perceptual tint that keeps the pixel's lightness, takes the
  hue from the color, and scales chroma by the color's saturation.
- `ColorOperationParameters.ColorMap`: varies an operation's color per pixel, for example a
  tint texture. Used by `Tint`, `Colorize`, `Multiply` and `Add`.
- Color space tests: sRGB and linear loading, an exact round trip of all 256 values,
  encoding on output, normal maps never encoded, and filtering in linear space.
- Color operation tests: `Mixed`, `Colorize`, `Chromatize`, standard `Saturation` and
  `Brightness`, and color maps.

### Fixed
- **`PaintTriangle` counted sub-samples on shared edges twice.** A sub-sample exactly on an
  edge shared by two triangles was painted by both, and one exactly on a vertex by every
  triangle around it. That made coverage and painted values exceed 1 along such edges, which
  the normal correction then read as overlapping UVs. A fill rule now gives each sub-sample to
  exactly one triangle. A lone triangle whose edge passes exactly through sub-samples now covers
  slightly less at that edge; meshes are unaffected.
- **`BakeNormalDifference` matches how shaders decode.** It used to straighten the tangent
  frame per pixel (normalize, orthogonalize) before projecting. Shaders use the interpolated
  frame as it is, so on curved triangles of a reduced mesh the correction drifted. It now
  inverts the supplied frame exactly. Orthonormal frames give the same result as before.
- Loading a linear (non-sRGB) texture left `Settings.SRGB` on. It now follows the source's
  format.

## 0.9.1 — 2026-11-04

New naming convension on the package name in VCC.

## 0.9.0 — 2026-11-04

EditMode tests pass in Unity 2022.3.22 (2026-11-03).

### Added
- A full API reference in `API.md`.
- A test for two-dimensional dilation ties.

### Changed
- **Breaking for assembly definitions that reference this package:** the runtime assembly is
  renamed `NordicWind.TextureWrapper` → `NordicWind.TextureWrapper.Runtime`. The C# API and the
  `NordicWind.TextureWrapper` namespace are unchanged.
- Package name `app.nordicwind.texture-wrapper`. MIT license.
- README reorganized into features, roadmap and key concepts. The dilation tie rule (lower x,
  then lower y) is documented.

### Fixed
- A dilation regression test expected the opposite tie rule. The behavior is unchanged.

## 0.8.4

- `Dilate` handles each data type correctly: normal maps get alpha 1, while color and data
  maps keep the source alpha. An empty region is left unchanged.
- Dilation uses half the scratch memory, for example 192 MiB instead of 384 MiB at 4096×4096.

## 0.8.3

- `PaintTriangle` no longer clamps while accumulating. Values are clamped only when output is
  encoded.

## 0.8.2

- Coverage and accumulation fixes found while auditing the Atlaser's normal-map bakes.
- The `BakeNormalDifference` contract is clarified: the basis must be the target mesh's
  tangent frame.
- New `FillUnused`.

## 0.8.1

- `TextureSettings.OutputChannels`: choose RGB24 or RGBA32 output, or pick automatically.

## 0.8.0

- First implementation of the library.
