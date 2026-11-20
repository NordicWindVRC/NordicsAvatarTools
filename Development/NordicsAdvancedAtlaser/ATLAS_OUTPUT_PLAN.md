# Nordic's Advanced Atlaser — Output implementation plan

What's next and later for the design in [ATLAS_OUTPUT.md](ATLAS_OUTPUT.md), whose section names
it uses. How finished parts work is in the design; what each version added is in
[CHANGELOG.md](CHANGELOG.md).

## How the plan works

- **Phases** are small. Each ends in a **workable state**: the EditMode tests pass in Unity,
  and the inspector, the manual bake and the build all work for what exists so far. Nothing
  is left half-built between phases.
- **Milestones** group phases. Each milestone is a usable release point.
- Features are added to **every writer** at once. A feature a writer can't express natively
  uses its fallback mode, or isn't offered.
- **Every phase adds tests**:
  - unit tests for allocators, packing and evaluators;
  - round trips (parse, write, parse, compare) for what a writer maps natively;
  - small synthetic meshes for bakes and the remapper.
- **A feature is done when it looks right**: a build on every writer is compared with the
  original avatar, as the design's *Matching the look* describes, and what that turns up is
  fixed as a general rule with tests, not tuned to the test avatar.
- **Settings may change until 1.0.** Beta versions need no migration of the serialized output
  settings; the changelog says when a configuration must be redone.

| Milestone | Result | Status |
|---|---|---|
| **M1 — End to end** | An avatar builds with the sources' base color textures atlased into one, on both writers, and the manual bake writes the material and its texture | Done |
| **M2 — Initial features** | Every feature in the design's *Feature inventory*, on both writers | Done |
| **M3 — More Poiyomi writers** | Poiyomi Toon 11, Pro 10 and Pro 11 as outputs | Next |
| Later | Mochie Uber as an output, the later features, animation, GPU baking | — |

---

## M3 — More Poiyomi writers

### 3.1 One Poiyomi writer, by version and variant

- The Poiyomi Toon 10 writer becomes one writer for version and variant, registered as Toon
  10 and Toon 11. Toon 11 adds its capabilities and maps the features new in 11.
- **Workable:** Poiyomi Toon 11 as an output, with round trips.

### 3.2 Poiyomi Pro 10 and Pro 11

- The Pro variant: Pro's added capabilities (more slots), and its features natively for Pro
  sources (screen-space AO, contact shadows, constellation, GT7 tone mapping, neutral
  emission and the others the unified material types). Offered only where Pro is installed.
  The writer describes Pro in its own words.
- **Workable — M3 reached:** Poiyomi Pro 10 and Pro 11 as outputs, with round trips.

---

## Later

Not planned in detail yet, in rough order:
- Mochie Uber as an output (its parser in reverse);
- the design's later features: outlines, fur, transition and dissolve, the other procedural
  patterns, refraction, highlight layers, parallax, deformations and geometry effects,
  second pass and visibility, screen-space lighting;
- animation support;
- half-precision buffers or GPU baking, if the memory budget is too tight;
- a typed-in value for shared values, next to *Automatic* and the sources in the reference
  source drop-down;
- **depth rims** (from 2.7b): Poiyomi's depth rim lighting natively, one setup from the
  reference source;
- **rim color textures** (from 2.7b): a rim's texture on UV0 baked into a color map;
- **specular anti-aliasing for outputs without it** (Toon Standard): Poiyomi's widens highlights
  where the normals change quickly, so small glossy features (an artist's glossy notch in a
  smoothness map) stay subtle; without it they show as sharp shiny spots. Baking the normals'
  variance into the gloss map (lower gloss where the normal map and the mesh curvature vary
  within a texel) would approximate it;
- **full light and form shading** (from 2.8): the lit color baked for outputs that don't light
  and the form shading for outputs that light flatly, with each source's lighting family and
  surface response under the rig. Today's outputs light with their own ramps, where either
  would light twice; built with the first unlit or flat output;
- **cloth lighting** (from 2.6c): Poiyomi's cloth type, with every source's metallic, cloth
  mask, reflectance and smoothness baked into its packed map;
- **bake speed**, measured on a 5-material avatar (11 s per output, 2.7 s of it Unity's
  compression), CPU only, the Texture Wrapper kept generic (no GPU color stack: it would move
  growing feature logic into the wrapper):
  - decals worked on within their own rectangle instead of full-size temporaries (about 2 s
    on a 4096 body);
  - same-size `Resize` without a transform as a copy, and the same-size `Blit` shortcut for
    copying a channel into usage (island growing);
  - redundant passes such as the neutral fill where every channel is overwritten;
  - a per-source bake cache in `Library`, keyed by the material's values, its textures'
    content hashes, the coverage, the bake size and the layout: repeat builds evaluate only
    changed sources; optionally the compressed textures too.
  - a second measurement, a 5-material avatar in two Poiyomi outputs (Body 4096², Misc
    4096 × 2048) with many maps: entering play mode takes about 2 minutes. The Atlaser's part
    is about 57 s: the outputs 26.8 s and 22.6 s (each texture 2–4.5 s to evaluate, place and
    pack, 0.3–2.5 s to compress; the body's decal layer alone 2.1 s) and the geometry bakes
    7.6 s. The other minute, outside the Atlaser (other NDMF plugins, Unity), isn't broken down
    yet.
