# Nordic's Texture Wrapper — GPU acceleration

Some operations have a GPU version that runs on large textures. They are chosen inside the
existing operations: the API is the same, and the CPU version runs where the GPU can't or
shouldn't. The shaders are in `Runtime/Resources/NordicWind.TextureWrapper/`.

## When the GPU is used

| Setting (`TextureProcessingSettings`) | Default | Meaning |
|---|---|---|
| `UseGpu` | on | Lets operations that have a GPU version use it. Off: everything runs on the CPU |
| `MinGpuPixels` | 512 × 512 | Textures with fewer pixels run on the CPU, where moving the data to the GPU and back costs more than it saves |
| `GpuAvailable` | read only | A GPU that renders to float textures is present; false in batch mode without graphics (`-nographics`) |

- **The CPU implementation stays.** It's the reference the GPU versions are tested against,
  and the fallback where there's no GPU, where the GPU is turned off, and for small jobs. It
  runs on every core, with the same result for any thread count.
- **Results agree within a documented tolerance.** Each GPU version states how it may differ
  from the CPU below, and the tests run the same checks on both (`BothProcessors`).
- **Only Unity.** The shaders ship with the package and load from its `Resources`; no other
  package is needed.
- **Moving data costs.** A 4096² float texture takes on the order of 100 ms each way, so an
  operation that runs on the GPU on its own has to save more than that.

## GPU operations

| Operation | GPU version | When |
|---|---|---|
| `Dilate()` on the whole texture | Jump flooding | `UseGpu`, `GpuAvailable`, at least `MinGpuPixels` |
| `PaintTriangles` (both overloads, hidden surfaces included) | The mesh drawn in UV space | Also batches of at least 1,024 triangles |
| Loading a `Texture2D` without Read/Write | Rendered into a float render texture and read back | Always: it's the only way to read such a texture |

### Dilation

Jump flooding finds, for each unused pixel, a used pixel at (nearly) the nearest distance.
The passes run at step 1 first, then from about half the size down to 1, then at 2 and 1
again; the extra passes remove most of the misses plain jump flooding has.

How it may differ from the CPU: where two used pixels are equally near, the tie isn't
broken by "lower x, then lower y"; rarely, a pixel takes a slightly farther used pixel's
value. The tests check that every filled pixel took a used pixel's value, that the CPU's is
always a nearest one, and that on the GPU under 1% of pixels took a farther one, never more
than 2 pixels farther.

### Triangle painting

The mesh is drawn with its UVs as positions, so the GPU rasterizes in texture space; the
values to paint are interpolated linearly, like the CPU's barycentric weights. Quality
sampling renders at n times the size and averages down (`ResolvePaint`); a pixel is used
where any sample is covered. Painting adds, so sums and coverage counts work as on the CPU.

How it may differ from the CPU: every sample belongs to exactly one triangle on both, but
the GPU snaps corners to a sub-pixel grid and interpolates in its own precision. Values drift
slightly (well under one 8-bit step), and a sample on or very near an edge may fall the other
way. The tests allow the drift, compare totals, and allow a few pixels to differ by a
sample's share.

The vertex shader writes clip space directly from the UV, without matrices. Graphics APIs
disagree on which way clip space's y runs through a render texture, so a small test draw
finds it once per session; if that draw doesn't land in the texture, painting stays on the
CPU.

**Hidden surfaces (`HiddenSurfaceOptions`).** Each corner carries a position and a normal.
Where triangles share a pixel, the ones lying behind another surface are left out, like a
depth test. "Behind" needs a direction that is the same for every surface at the pixel, so
three steps run per pixel and UV winding:

1. Every triangle covering the pixel's center adds its interpolated normal there.
2. Along that average direction, the outermost covering surface's distance is recorded.
3. While painting, a sample whose distance along the same direction is more than `Margin`
   plus its triangle's footprint below that record is hidden. The footprint is how far the
   triangle's surface moves over one pixel: a sample lies up to that far from the surface at
   the pixel's center, so on coarse or tilted triangles anything finer would let a surface
   hide parts of itself.

Because all surfaces at a pixel are measured along the same direction, an error in that
direction shifts them all alike. Mirrored halves have opposite UV windings and are compared
separately, so they never hide each other; their overlap is left to the caller. Same-winding
copies elsewhere in space (stacked copies) do compete, the outermost along the normal
winning. A depth per vertex, such as the distance to the skinned bone, isn't used: it isn't
comparable between vertices skinned to different bones, and a bone running to the surface
(as breast bones often do) gives the visible surface no larger depth than the hidden one.

On the CPU, the steps run per 64-pixel tile before the tile paints, so memory stays small.
On the GPU, two draws at the texture's own size fill the average normals (ARGBHalf per
winding; the error cancels as above) and the outermost distance (RGFloat, `BlendOp Max`),
and the painting passes discard hidden samples. Pixels whose center no triangle covers have
no record, so their samples paint as without the option; this only happens along edges.

## Future candidates

Only the wrapper's own, general operations are candidates. The wrapper stays shader-neutral
and holds no caller logic: what a caller computes per pixel itself (through `GetPixels` and
`SetPixels`) stays on the CPU in the caller. Each candidate would follow the rules above: the
CPU version stays as the reference and fallback, and the differences are documented and tested.

| Operation | Why it fits | What to watch |
|---|---|---|
| `Resize` | Every output pixel is independent: bilinear, bicubic and nearest sampling, area supersampling, the transform, and its color operations | Area sampling's sample count per pixel; tangent-space normals renormalized after sampling |
| `Blit` | Per pixel: the resampled source, the mask with its options, the blend mode, the strength, channel routing and padding | The usage flags (used, padding, the usage channel) have to come back exactly; self-blits read a copy |
| `ApplyColor` | Per pixel: each operation's formula, with its mask and color map | The OKLab and HSV operations and `Chromatize` must match the CPU's math within the tolerance |
| `BakeNormalDifference` | Per pixel: the frame inversion and the safety rules | The statistics (baked, overlapping, limited, largest angle) need a reduction pass |
| `Dilate(region)` | Only whole textures run on the GPU today | Small regions rarely pay for the transfer |

**Keeping textures on the GPU between operations.** Alone, each of these pays for moving the
data both ways, which on large textures costs about as much as the operation saves on a
many-core CPU. The bigger gain is a chain that stays on the GPU: a wrapper keeps its pixels
in a render texture after a GPU operation and reads them back only when the CPU needs them
(a CPU operation, `GetPixel`, `GetPixels`, export). The API wouldn't change. Open questions:
the memory a render texture per wrapper takes, float precision on every graphics API, and
keeping results the same for any mix of CPU and GPU steps.
