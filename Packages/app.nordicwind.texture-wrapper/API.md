# Nordic's Texture Wrapper — API reference

Everything is in namespace `NordicWind.TextureWrapper`. Types are in the
`NordicWind.TextureWrapper.Runtime` assembly, except the exporters, which are in
`NordicWind.TextureWrapper.Editor`.

Conventions used throughout:

- **Regions** (`Rect? region`, `Rect? sourceRegion`) are in **UV space**, `0..1` on both
  axes, with the origin bottom-left like Unity textures. `null` means the whole texture.
- **Pixel coordinates** `(x, y)` are integers from `0` to `Width - 1` / `Height - 1`.
- **Rotation** is in **degrees**, around the center of the selected region.
- Methods validate their input. Non-finite numbers, out-of-range pixels, negative padding
  and undefined enum values throw `ArgumentException` or `ArgumentOutOfRangeException`.
  Writing to a read-only wrapper throws `InvalidOperationException`, and using a disposed
  wrapper throws `ObjectDisposedException`.

## `NordicsTextureWrapper` (sealed class, `IDisposable`)

A texture held in memory as float RGBA plus a per-pixel *usage* flag.

### Construction and lifetime

| Member | Description |
|---|---|
| `NordicsTextureWrapper(int width, int height, TextureSettings settings = null)` | Creates an empty, writable texture. All pixels are clear and unused. `settings` is copied; `null` uses default settings. |
| `NordicsTextureWrapper(Texture2D source, bool readOnly = true)` | Loads a readable `Texture2D` (Read/Write enabled). Every pixel is marked used. Wrap mode, filter mode, anisotropic level and mipmaps are copied from the source. The data type is **not** detected: for normal maps, create the wrapper with normal settings or convert the data yourself. |
| `NordicsTextureWrapper Clone()` | Deep copy of settings, pixels and usage. The copy is writable and has no cached texture or export target. |
| `NordicsTextureWrapper CloneEmpty(int? width = null, int? height = null)` | Same settings, empty buffers, optionally a different size. |
| `void Dispose()` | Frees the buffers and any texture the wrapper created itself. Textures it didn't create (sources, exported assets) are left alone. |

### Properties

| Property | Description |
|---|---|
| `int Width`, `int Height` | Size in pixels. Fixed after construction. |
| `bool IsReadOnly` | `true` for wrappers loaded from a texture with `readOnly: true`. Use `Clone()` to edit. |
| `TextureSettings Settings` | The wrapper's settings. Changing them affects the next output or export. |
| `bool IsDirty` | `true` if pixels or settings changed since the last output or export. |

### Pixel access

| Member | Description |
|---|---|
| `Color GetPixel(int x, int y)` | Raw value, which may be outside 0–1. Normal maps are normalized first. |
| `Color GetPixel(Vector2 uv)` | Nearest-pixel value at a UV position. |
| `void SetPixel(int x, int y, Color color)` / `SetPixel(Vector2 uv, Color color)` | Writes a value and marks the pixel used. |
| `bool IsPixelUsed(int x, int y)` / `IsPixelUsed(Vector2 uv)` | Reads the usage flag. |
| `void SetPixelUsed(int x, int y, bool used)` | Sets the usage flag without changing the color. |
| `Color Sample(Vector2 uv, TextureSamplingMode sampling = Bilinear, TextureWrapMode? wrap = null)` | Filtered sample. `wrap` defaults to `Settings.WrapMode`. |

### Transforming and compositing

| Member | Description |
|---|---|
| `NordicsTextureWrapper Resize(int width, int height, Rect? sourceRegion = null, Vector2? scale = null, Vector2? offset = null, float rotation = 0, TextureSamplingMode sampling = Bilinear, TextureWrapMode? wrap = null, IReadOnlyList<ColorOperation> operations = null)` | Returns a **new** wrapper of the given size containing `sourceRegion` of this texture. `scale` tiles (2 = repeat twice), `offset` shifts in region units, and `rotation` rotates around the region center. `Area` sampling averages up to 16×16 samples when shrinking. `operations` are applied to the result. For tangent-space normals, rotation also rotates the vectors. |
| `Rect Blit(NordicsTextureWrapper source, Rect? region = null, Rect? sourceRegion = null, Vector2? scale = null, Vector2? offset = null, float rotation = 0, NordicsTextureWrapper mask = null, TextureMaskOptions maskOptions = null, TextureChannel? sourceChannel = null, TextureChannel? destinationChannel = null, TextureBlendMode blend = Replace, float strength = 1, int? padding = null, TextureSamplingMode sampling = Bilinear, TextureWrapMode? wrap = null, IReadOnlyList<ColorOperation> operations = null)` | Draws `source` (transformed like `Resize`) into `region` of this texture and returns the UV rectangle of the content, **excluding** padding. `mask` and `maskOptions` control influence per pixel, sampled in the content's own 0–1 space. `strength` (≥ 0) scales the influence. **Padding:** `null` = no padding and usage is left unchanged; `0` = copy the source's usage; `n > 0` = extend the content's edge pixels `n` pixels outward and copy usage. Blit details into the returned rectangle with `padding: null`. |
| `void ApplyColor(ColorOperation operation, Rect? region = null)` | Applies one color operation in place. |
| `void ApplyColor(IReadOnlyList<ColorOperation> operations, Rect? region = null)` | Applies operations in order. Each can have its own mask. |
| `void Fill(Color color, Rect? region = null, NordicsTextureWrapper mask = null, TextureMaskOptions maskOptions = null)` | Blends `color` in by the mask influence (full when there's no mask) and marks the pixels used. |
| `void Clear(Rect? region = null)` | Sets pixels to transparent black and marks them unused. |

**Channels** (`sourceChannel` / `destinationChannel`): with neither set, RGBA is blended. With
only a source channel, that channel is written to RGBA. With only a destination channel, the
source's RGB average is written to it. Writing to `Usage` sets it to `value > 0`. Normal blend
modes require both to be unset.

### Mesh baking and finishing

| Member | Description |
|---|---|
| `void PaintTriangle(Vector2 uv0, Vector2 uv1, Vector2 uv2, Color color0, Color color1, Color color2, TrianglePaintOptions options = null)` | Rasterizes a UV-space triangle, interpolating the corner values. Each pixel **adds** value × covered fraction (no clamping), and is marked used if any part is covered. For normal maps, paint every triangle before reading, because reading normalizes. |
| `NordicsTextureWrapper BakeNormalDifference(NordicsTextureWrapper targetNormals, TangentBasisData basis)` | `this` and `targetNormals` must be `NormalWorld` maps of the same size. Returns a new `NormalTangent` map: this texture's normals expressed in `basis`, the **target** mesh's tangent frame. Only pixels used in both maps are used in the result. |
| `void Dilate(Rect? region = null)` | Fills every unused pixel in the region with the nearest used pixel and marks it used. Normal maps get alpha 1. When two source pixels are equally near, the lower x wins, then the lower y. A region with no used pixels is left unchanged. |
| `void FillUnused(Color color, bool markUsed = false, Rect? region = null)` | Writes `color` only to unused pixels, without marking them used unless `markUsed` is set. For example, use `(0, 0, 1, 1)` for flat normals outside UV islands. |

### Output

| Member | Description |
|---|---|
| `Texture2D GetTexture2D()` | Returns a Unity texture kept in sync with the wrapper: created on first call, updated when pixels or settings change. The wrapper owns it, unless an exporter is set. |
| `TextureFormat GetOutputTextureFormat()` | `RGB24` or `RGBA32`, following `Settings.OutputChannels`. |
| `Color[] GetEncodedPixels()` | Output values: clamped to 0–1, with normals encoded as `(n + 1) / 2`. |
| `Texture2D Export(INordicsTextureExporter exporter, string name)` | Sets an export target and synchronizes it. Later `GetTexture2D()` calls keep that target up to date. |

## `TextureSettings` (serializable class)

| Field | Default | Description |
|---|---|---|
| `TextureDataType DataType` | `Color` | What the pixels mean. The `Normal*` types store signed vectors. |
| `bool SRGB` | `true` | Output as sRGB color. Ignored for normal maps. |
| `bool Mipmaps` | `true` | Generate mipmaps on output. |
| `FilterMode FilterMode` | `Bilinear` | Output filter mode. |
| `TextureWrapMode WrapMode` | `Repeat` | Default wrap for sampling and output. |
| `int AnisoLevel` | `1` | 0–16. |
| `TextureCompression Compression` | `Normal` | Importer compression, for PNG export. |
| `bool Crunch` | `false` | Crunch compression, for PNG export. |
| `bool AlphaIsTransparency` | `false` | Importer setting, for PNG export. |
| `TextureOutputChannels OutputChannels` | `Auto` | `Auto` uses RGB when every alpha would encode as 255, otherwise RGBA. `RGB` and `RGBA` force one or the other. |
| `bool PreserveAlpha` | `true` | Legacy. `OutputChannels` takes precedence. |
| `bool StreamingMipmaps` | `false` | Importer setting, for PNG export. |
| `int MipmapLimitGroup` | `0` | Reserved. |

Members: `TextureSettings Clone()`, `bool IsNormal`, and `void Validate()`, which throws on
out-of-range values.

## `TextureProcessingSettings` (static class)

| Property | Default | Description |
|---|---|---|
| `int MinParallelPixels` | `65536` | Operations on fewer pixels than this run on one thread. |
| `int MaxThreads` | Processor count | `0` = automatic, `1` = single-threaded. |

## `TextureMaskOptions` (class)

The mask value is computed in this order, following Poiyomi Toon 10's mask math:

1. **Channel:** `Channel` (default `AverageRGB`). If `UseChannelBlending` is set, R × `ChannelStrengths.x`
   is combined with G, B and A in turn using `ChannelBlendMode`, each weighted by its strength.
2. **Remap:** `(mask - Min) / (Max - Min)`, clamped to 0–1 (defaults `0` and `1`).
3. **Gamma:** `mask ^ Gamma` (default `1`, must be ≥ 0).
4. **Strength and offset:** `mask × Strength + Offset`, clamped to 0–1 (defaults `1` and `0`).
5. **Invert:** `1 - mask` when `Invert` is set.

## `ColorOperation` and `ColorOperationParameters` (classes)

`ColorOperation` has `Opcode`, an optional `Mask` and `MaskOptions`, and `Parameters`.
`ColorOperationParameters` has `Amount` (default 0), `Amount2` (default 1), `Amount3`, `Color`
(default white), `ColorSpace` (default `HSV`), `PoiyomiCompatibility`, and `Gradient` (a wrapper).

| `ColorOperationCode` | Uses | Effect (alpha is unchanged unless stated) |
|---|---|---|
| `HueShift` | `Amount`, `ColorSpace` | Rotates hue by `Amount` turns (1 = 360°), in HSV or OKLab. |
| `HueReplace` | `Amount`, `ColorSpace` | Sets hue to `Amount` turns. |
| `Saturation` | `Amount` | Standard: 1 = unchanged, 0 = grey. Poiyomi mode: 0 = unchanged, positive = more saturated. |
| `Brightness` | `Amount` | Standard: multiplies by `Amount`. Poiyomi mode: multiplies by `1 + Amount`. |
| `Contrast` | `Amount` | Scales around 0.5. 1 = unchanged. |
| `Gamma` | `Amount` | Signed power: `sign(c) × abs(c) ^ Amount`. |
| `Exposure` | `Amount` | Multiplies by `2 ^ Amount`. |
| `Tint` | `Color` | Standard: multiplies RGB by `Color`. Poiyomi mode: OKLab tint, mixed in by `Color.a`. |
| `Multiply` | `Color` | Multiplies RGBA by `Color`. |
| `Add` | `Color` | Adds `Color` to RGBA. |
| `Invert` | — | `1 - c` for RGB. |
| `Clamp` | `Amount`, `Amount2` | Clamps RGB to `[Amount, Amount2]`. |
| `Levels` | `Amount`, `Amount2` | Remaps `[Amount, Amount2]` to `[0, 1]`. |
| `Posterize` | `Amount` | Rounds RGB to `Amount` steps. |
| `GradientMap` | `Gradient` | Replaces RGB with `Gradient` sampled at the pixel's luminance (0.30, 0.59, 0.11). |
| `Chromatize` | `Amount` | Raises chroma by `1 + Amount`. Poiyomi mode works in OKLab and is limited to the maximum chroma for the hue. |

## Other types

| Type | Members |
|---|---|
| `TrianglePaintOptions` | `TriangleRasterizationMode Mode` (`Quality` default, or `Fast`); `int SamplesPerAxis` (default 4, so 4×4 sub-samples per pixel in `Quality` mode). |
| `TangentBasisData` | `Tangent`, `Bitangent`, `Normal`: three wrappers holding the target mesh's tangent frame, for example painted with `PaintTriangle`. |
| `INordicsTextureExporter` | `Texture2D Synchronize(NordicsTextureWrapper wrapper, string name, bool pixelsChanged, bool settingsChanged)`: implement this to add your own export target. |

### Enums

| Enum | Values |
|---|---|
| `TextureDataType` | `Color`, `LinearData`, `Mask`, `NormalTangent`, `NormalObject`, `NormalWorld` |
| `TextureSamplingMode` | `Nearest`, `Bilinear`, `Bicubic`, `Area` |
| `TextureChannel` | `R`, `G`, `B`, `A`, `AverageRGB`, `Usage` |
| `TextureBlendMode` | `Replace`, `Darken`, `Multiply`, `Lighten`, `Screen`, `Subtract`, `Add`, `Overlay`, `Mixed`, `Divide`, `Average`, `NormalLerp`, `NormalRNM`, `NormalWhiteout` |
| `TextureMaskBlendMode` | `Replace`, `Subtract`, `Multiply`, `Divide`, `Min`, `Max`, `Average`, `Add` |
| `TriangleRasterizationMode` | `Fast`, `Quality` |
| `TextureCompression` | `None`, `Low`, `Normal`, `High` |
| `TextureOutputChannels` | `Auto`, `RGB`, `RGBA` |
| `TextureColorSpace` | `LinearRGB`, `SRGB`, `HSV`, `OKLab` |
| `ColorOperationCode` | See the color operation table above. |

## Exporters (Editor assembly)

| Type | Description |
|---|---|
| `PngAssetTextureExporter(string assetDirectory)` | Writes `<assetDirectory>/<name>.png` and imports it with the wrapper's settings. `assetDirectory` must be `Assets` or a folder inside it. The PNG is rewritten when pixels change, and reimported when settings change. Use only from the Editor, outside build steps that don't allow synchronous asset imports. |
| `MemoryTextureExporter` (`IDisposable`) | Keeps one in-memory `Texture2D`, recreated when its size, format or mipmaps change. Use one instance per wrapper. `Dispose()` destroys the texture. |
