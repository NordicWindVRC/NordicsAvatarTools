# Nordic's Texture Wrapper — GPU acceleration

**Status:** G1, G2 (dilation) and G3 (batch triangle painting) done; G3's hidden-surface
option is implemented. The shaders are in `Runtime/Resources/NordicWind.TextureWrapper/`.

## Principles

- **The CPU implementation stays.** It's the reference the GPU versions are tested against,
  and the fallback where there's no GPU (batch mode with `-nographics`), where the GPU is
  turned off (`TextureProcessingSettings.UseGpu`), and for small jobs
  (`TextureProcessingSettings.MinGpuPixels`), where moving the data to the GPU and back costs
  more than it saves.
- **The public API is a contract.** GPU versions are chosen inside existing operations; new
  API only adds (settings, batch overloads).
- **Results agree within a documented tolerance.** Each GPU version states how it may differ
  from the CPU, and tests run the same checks on both (`BothProcessors` in the tests).
- **Only Unity.** Shaders ship with the package and are loaded from its `Resources`; no
  dependency on other packages.
- **Moving data costs.** A 4096² float map takes on the order of 100 ms each way, so
  operations that run alone on the GPU must save more than that; chains that stay on the GPU
  save the most.

## Steps

| Step | Content | Effort |
|---|---|---|
| **G1** | Settings (`UseGpu`, `MinGpuPixels`, `GpuAvailable`); pixels and usage flags to float render textures and back, unchanged; the CPU-and-GPU test harness. In the Advanced Atlaser: per-stage timings in its diagnostics, to measure the gains | S–M |
| **G2** | `Dilate` on the GPU by jump flooding (about log₂ of the size in passes, plus one or two for accuracy) | S–M |
| **G3** | A batch `PaintTriangles` call: the CPU version paints tiles on every core instead of one call per triangle; the GPU version draws the mesh in UV space, with supersampling for the quality mode and additive blending for sums and coverage counts. Then, optionally, hidden surfaces: a position and normal per corner, so surfaces behind another with the same UVs don't paint, on both | M, plus S–M for hidden surfaces |

### G2 — dilation

Jump flooding finds, for each unused pixel, a used pixel at (nearly) the nearest distance.
How it may differ from the CPU: where two used pixels are equally near, the tie isn't
broken by "lower x, then lower y"; rarely, a pixel takes a slightly farther used pixel's
value. The passes run at step 1 first, then from about half the size down to 1, then at 2
and 1 again; the extra passes remove most of those misses. Tests check that every filled
pixel took a used pixel's value, that the CPU's is always a nearest one, and that on the GPU
under 1% of pixels took a farther one, never more than 2 pixels farther.

### G3 — triangle painting

The mesh is drawn with its UVs as positions, so the GPU rasterizes in texture space; the
values to paint are interpolated linearly, like the CPU's barycentric weights. Every sample
belongs to exactly one triangle on both, but the GPU snaps corners to a sub-pixel grid and
interpolates in its own precision: values drift slightly (well under one 8-bit step), and a
sample on or very near an edge may fall the other way. Tests allow the drift, compare totals,
and allow a few pixels to differ by a sample's share. Quality sampling renders at
n times the size and averages down; a pixel is used where any sample is covered.

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
direction shifts them all alike. Mirrored halves have opposite UV windings and are compared separately, so they
never hide each other; their overlap is handled by the caller as before. Same-winding copies
elsewhere in space (stacked copies) do compete, the outermost along the normal winning. An
earlier idea, a depth per vertex such as the distance to the skinned bone, was dropped: it
isn't comparable between vertices skinned to different bones, and a bone running to the
surface (as breast bones often do) gives the visible surface no larger depth than the hidden
one.

On the CPU, the steps run per 64-pixel tile before the tile paints, so memory stays small.
On the GPU, two draws at the texture's own size fill the average normals (ARGBHalf per
winding; the error cancels as above) and the outermost distance (RGFloat, `BlendOp Max`),
and the painting passes discard hidden samples. Pixels whose center no triangle covers have
no record, so their samples paint as without the option; this only happens along edges.
