# Nordic's Texture Wrapper — GPU acceleration

**Status:** G1 done: the settings and the GPU groundwork; no operation uses the GPU yet. G2 is
next.

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
| **G3** | A batch `PaintTriangles` call: the CPU version paints tiles on every core instead of one call per triangle; the GPU version draws the mesh in UV space, with supersampling for the quality mode and additive blending for sums and coverage counts | M |

### G2 — dilation

Jump flooding finds, for each unused pixel, a used pixel at (nearly) the nearest distance.
How it may differ from the CPU: where two used pixels are equally near, the tie isn't
broken by "lower x, then lower y"; rarely, a pixel takes the second-nearest used pixel's
value. Tests check that every filled pixel took a value from a nearest used pixel,
allowing ties, instead of comparing pixel for pixel.

### G3 — triangle painting

The mesh is drawn with its UVs as positions, so the GPU rasterises in texture space; the
values to paint are interpolated linearly, like the CPU's barycentric weights. Every sample
belongs to exactly one triangle on both, but a sample exactly on a shared edge may go to the
other triangle: tests compare totals and allow edge differences. Quality sampling renders at
n times the size and averages down; a pixel is used where any sample is covered.
