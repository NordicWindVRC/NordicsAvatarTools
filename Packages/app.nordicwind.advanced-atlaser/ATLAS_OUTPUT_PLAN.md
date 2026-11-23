# Nordic's Advanced Atlaser — Output implementation plan

What's next and later for the design in [ATLAS_OUTPUT.md](ATLAS_OUTPUT.md), whose section names
it uses. How finished parts work is in the design; what each version added is in
[CHANGELOG.md](CHANGELOG.md). What matching by look still plans beyond M4 is in
[ATLAS_OUTPUT_LOOK.md](ATLAS_OUTPUT_LOOK.md).

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
- **Fixes need no phase.** What testing more avatars and features turns up in a finished
  milestone is fixed when it is found, by the same rule.
- **Settings may change until 1.0.** Beta versions need no migration of the serialized output
  settings; the changelog says when a configuration must be redone.

| Milestone | Result | Status |
|---|---|---|
| **M1 — End to end** | An avatar builds with the sources' base color textures atlased into one, on both writers, and the manual bake writes the material and its texture | Done |
| **M2 — Initial features** | Every feature in the design's *Feature inventory*, on both writers | Done |
| **M4 — Matching by look** | Looks measured and tuned in every scene; matcaps placed by role and cost | Done |
| **M3 — More Poiyomi writers** | Poiyomi Toon 11, Pro 10 and Pro 11 as outputs | Done |
| **M5 — More output families** | VRChat Mobile Diffuse, Unity Standard (both setups), VRChat Mobile Toon Lit and Standard Lite as outputs | In progress |
| Later | Mochie Uber as an output, the later features, animation, GPU baking | — |

---

## M5 — More output families

Each output starts from its shader's source and uses its parser for the kept material.

Built so far (the design's *Output shaders*):
- VRChat Mobile Diffuse as an input (with Bumped Diffuse) and as an output with one color
  texture, where every feature it keeps is a contribution to that texture, emission included
  when the user chooses so;
- Unity Standard as an output in both setups, with its own maps, and the Unity family for
  auto setup. The specular setup keeps each source's reflectance, metallic and highlight and
  reflection strengths in its specular color;
- on outputs without matcap slots, lighting matcaps kept as their average in the color
  (*Average in color*);
- the minimum light an output shader can't keep itself, added through the emission and
  shaded by the lit bakes.

### 5.4 VRChat Mobile Toon Lit and Standard Lite

- Toon Lit: one texture, as Diffuse, lit without shading across the surface, so its form
  comes from baked shading (the design's *form shading*): lighting matcaps are baked there
  by default, where an output that shades the form itself keeps their average.
- Standard Lite: Standard's maps on the albedo's tiling, opaque only.
- **Workable — M5 reached:** both shaders as outputs, with round trips.

---

## Later

Not planned in detail yet, in rough order:
- Mochie Uber as an output (its parser in reverse);
- the design's later features: outlines, fur, transition and dissolve, the other procedural
  patterns, refraction, highlight layers, parallax, deformations and geometry effects,
  second pass and visibility. Poiyomi Pro's remaining features come with them: the
  constellation pattern, TPS and squish, three outlines and the outline bake (from 3.2);
- rims and highlights drawn into the color texture of an output without them (VRChat
  Mobile Diffuse), as modes used when chosen (from 5.2);
- animation support;
- half-precision buffers or GPU baking, if the memory budget is too tight;
- a typed-in value for shared values, next to *Automatic* and the sources in the reference
  source drop-down;
- **the rest of matching by look** ([ATLAS_OUTPUT_LOOK.md](ATLAS_OUTPUT_LOOK.md)): the
  reference look computed from a source's unified material as a whole; the output's look as
  the reference look of the kept material, for every feature; the matcap slot fit judged in
  the scenes; other shared values and values in maps tuned by look; every feature that
  reads the base color reading a shifted one; declared inputs, a smooth limit for maps
  without a carrier, and the range each contribution raised;
- **Poiyomi 11's PBR lighting type as the output's lighting** (from 3.1). It replaces
  Reflections & Specular and the clear coat with its own controls (one packed map, a
  reflectance, no highlight or reflection strengths, sheen and glints), so what the output
  can express changes with the lighting chosen: built with the output's look as the kept
  material, above. Until then such a lighting is written as the nearest other type;
- **a matcap's ways compared by their cost** (from 4.5): one side, two slots and a shifted
  base as candidates, with the blocked view's difference. Built with the first output that
  has two matcap slots of one blend each; until then *Base color shift* decides between
  one side and a shifted base;
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
