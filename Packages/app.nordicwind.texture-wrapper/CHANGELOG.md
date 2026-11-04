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

### Fixed
- Loading a linear (non-sRGB) texture left `Settings.SRGB` on. It now follows the source's
  format.

### Added
- Color space tests: sRGB and linear loading, an exact round trip of all 256 values,
  encoding on output, normal maps never encoded, and filtering in linear space.

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
