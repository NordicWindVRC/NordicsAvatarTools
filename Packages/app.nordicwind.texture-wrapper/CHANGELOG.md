# Changelog — Nordic's Texture Wrapper

Newest first. Versions follow [SemVer](https://semver.org).

## 0.10.0 — unreleased

### Changed
- **Behaviour change: colors are linear inside the wrapper.** The C# API is unchanged, but
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

### Added
- **Loading any texture.** Textures without Read/Write, compressed or not, are read through
  the GPU instead of throwing.
- **Normal map decoding.** A new constructor overload takes a `TextureDataType`. For
  `NormalTangent`, the stored packing is decoded into vectors (DXT5nm style with X in alpha,
  or BC5/RGB with X in red) and Z is rebuilt. Object and world-space normals decode RGB as XYZ.
- `TextureAssetUtility` (Editor): `GetDataType` reads a texture's data type from its import
  settings, and `Load` loads it with that type.
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
- README reorganised into features, roadmap and key concepts. The dilation tie rule (lower x,
  then lower y) is documented.

### Fixed
- A dilation regression test expected the opposite tie rule. The behaviour is unchanged.

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
