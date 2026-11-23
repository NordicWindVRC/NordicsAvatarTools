# Nordic's Advanced Atlaser — Implementation plan

What's next and later, for the outputs and for the inputs. It uses the section names of the
output design, [ATLAS_OUTPUT.md](ATLAS_OUTPUT.md), and of the input design,
[UNIFIED_MATERIAL.md](UNIFIED_MATERIAL.md), with its feature catalog
([UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md), cited by section number). How
finished parts work is in the designs; what each version added is in
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
| **M6 — Features read but not written** | What the parsers read and no output keeps yet: culling and blends, outlines, the rest of highlights and reflections, live parts, layers and maps outside the bakeable subset, parallax, the remaining effects | Planned |
| **M7 — More input shaders** | The remaining VRChat Mobile shaders, lilToon, UnlitWF, Unity-Chan Toon Shader and the other shaders common on avatars | Planned |
| Later | Mochie Uber as an output, animation, the rest of matching by look, bake speed | — |

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

## M6 — Features read but not written

The parsers read every feature of their shaders on purpose, ahead of the outputs (the input
design's *Coverage and fidelity*). This milestone closes the gap for the features common on
avatars: today the build lists them as left out, approximated or "not merged yet". Each
phase adds or extends a shared feature for every writer that can express it, with a
fallback mode or a listed omission on the others.

The order follows how common a feature is on avatars and how much its absence shows. It is
a suggestion: confirm it before starting a phase.

### 6.1 Culling and pass settings

- **Culling** (catalog 6): no writer sets it today, so a double-sided source (hair cards,
  skirts) comes out single-sided. One setting per atlas: *Automatic* is double-sided where
  any source is; a control shows while the sources disagree. Writers: Poiyomi and Toon
  Standard set it; Unity Standard and Diffuse always cull back faces (listed).
- Two-sided lighting, the depth test, depth offset, color mask and stencil: shared values
  from the reference source, listed where the sources differ.
- **Workable:** a double-sided source stays double-sided on the writers that can, with
  round trips.

### 6.2 Blends, queue and alpha effects

- Additive, multiplicative and custom blends as surface modes (today their sources count as
  opaque), with the rule for mixing them with other sources.
- A render queue override; a cutoff in a blended mode.
- Alpha dithering and alpha fades (distance, view angle, audio): native where the output
  has them, otherwise left out and listed.
- **Workable:** particle-style and glow materials keep their blend on Poiyomi.

### 6.3 Outlines

- Catalog 5.1: one outline setup from the reference source, with each source's width in an
  allocated mask (0 without an outline) and its color in a map where the colors differ.
- Writers: Poiyomi natively; VRChat Mobile Toon Standard (Outline) as an output of its own;
  the others leave it out (listed). Poiyomi Pro's three outlines and its outline bake.
- **Workable:** an outlined avatar keeps its outline on Poiyomi and Toon Standard (Outline).

### 6.4 The rest of highlights and reflections

- What the build lists as "not merged yet" beyond strengths and masks: tints, fresnel,
  stepped (toon) highlights, anisotropy, a second lobe, cubemap reflections (a lookup slot).
- Specular anti-aliasing for outputs without it: the normals' variance baked into the gloss
  map, so small glossy details don't show as sharp spots.
- Cloth lighting: Poiyomi's cloth type, with every source's metallic, cloth mask,
  reflectance and smoothness in its packed map.
- Poiyomi 11's PBR lighting type as the output's lighting. It replaces Reflections &
  Specular and the clear coat with its own controls, so what the output can express changes
  with the lighting chosen; it needs the output's look as the kept material
  ([ATLAS_OUTPUT_LOOK.md](ATLAS_OUTPUT_LOOK.md)).
- The clear coat on an output of the specular workflow (into the gloss map).
- **Workable:** a stylized highlight keeps its tint and steps on Poiyomi; the others list
  what they approximate.

### 6.5 Live parts

- Modulators (catalog 1.9) on emission, rims, matcaps, glitter, backlight, subsurface and
  the environmental rim: blinking, scrolling and audio-driven parts stay live where the
  output has the same modulator, and are baked at rest elsewhere (listed). Today they are
  left out.
- Vertex color hue shifts; final color adjustments.
- **Workable:** a blinking or scrolling emission keeps moving on Poiyomi.

### 6.6 Layers and maps outside the bakeable subset

- Maps on another UV channel or remapped (metallic, roughness, occlusion, masks, band and
  shade textures): baked through the mesh's UVs.
- Layers that target alpha, metallic, roughness or occlusion; color layers that are lit,
  one-sided or in mirrors only; normal layers that are placed or lerped.
- Decals with symmetry, spin, parallax depth, a rotated texture or MSDF text.
- Rim color textures (a rim's texture on UV0 baked into a color map); the glitter's color
  map; the screen-space ambient occlusion's color map.
- Masks written by layers or by the second pass (the mask bus, catalog 1.5).
- **Workable:** the build log no longer lists these as "not baked yet" for the cases
  covered.

### 6.7 Rims and highlights without a slot

- Depth rims: Poiyomi's depth rim lighting natively, one setup from the reference source.
- Rims and highlights drawn into the color texture of an output without them (VRChat
  Mobile Diffuse, Unity Standard), as modes used only when chosen.
- **Workable:** both modes offered where they apply, off by default.

### 6.8 Parallax

- Catalog 2.5: each source's height map baked into one map per atlas, with the depth
  relative to the deepest source's. Native on Poiyomi and Unity Standard; left out
  elsewhere (listed).
- **Workable:** a parallax source keeps its depth on those writers.

### 6.9 The remaining effects

In this order, each a phase of its own:

- transition and dissolve (catalog 4.2);
- the other procedural patterns (4.6), with Poiyomi Pro's constellation pattern;
- highlight layers (4.8, slots);
- refraction (4.7);
- fur (5.2, exclusive, with a length mask);
- deformations and geometry effects (5.3, 5.4), with Poiyomi Pro's TPS and squish;
- the second pass and visibility (6).

Each is native where the output has the effect and left out elsewhere: none of them has a
baked stand-in that is the real effect.

---

## M7 — More input shaders

The shaders and their order are in the input design's
[*Future input shaders*](UNIFIED_MATERIAL.md#future-input-shaders). Their materials go to
the fallback parser until they have a parser of their own.

**Each parser is built the same way:**

1. **Sources first.** The shader's source and its inspector, for every version to support.
   A parser isn't started without them: nothing is mapped from property names alone.
2. **Inventory.** A `UNIFIED_MATERIAL_<SHADER>.md` document, section by section of the
   inspector: controls, behavior as the source computes it, and the unified feature each
   maps to. What the catalog lacks is added to the catalog first.
3. **Parser.** Detection (name, version, variants), reading with and without the shader
   installed, and a report of what couldn't be mapped.
4. **Tests.** An untouched material gives the shader's defaults; each section; saved
   values give the same result as the installed shader.
5. **Docs.** The coverage table in the feature catalog, the README's input table, the
   changelog.

Outputs don't change: a new input is a parser and an inventory.

### 7.1 The remaining VRChat Mobile shaders

- Matcap Lit (a matcap on a base texture), Bumped Mapped Specular (Unity's legacy mobile
  shader), Particles/Additive and Particles/Multiply. Their sources ship with the VRChat
  SDK.
- The particle shaders' blends are written by the outputs once 6.2 is built; until then
  they are read and listed.
- **Workable:** every `VRChat/Mobile/` shader has a parser.

### 7.2 lilToon

- The most common shader after Poiyomi. Its variants (Lite, Multi, Fur, Gem, Refraction,
  Outline, Tessellation) and its versions are part of the inventory; which versions to
  support is decided from what is in use.
- In phases: the main shader's surface, lighting and layers; then outline and the other
  variants. The catalog already has lilToon-style rims and stylized reflections, from
  Poiyomi.
- **Workable:** a lilToon avatar atlases onto every output, with what it couldn't map
  listed.

### 7.3 UnlitWF and Unity-Chan Toon Shader

- UnlitWF (whiteflare): stepped shading and metallics. UTS2 and UTS3: the catalog already
  has UTS-style rims and shade maps, from Poiyomi.
- **Workable:** both families have parsers.

### 7.4 Other current shaders

- Silent's Cel Shading, Sunao Shader, MToon (avatars converted from VRM), Orels Toon and
  Standard. One phase each, in the order avatars at hand need them.

### 7.5 Legacy shaders, and Mochie Uber X

- Xiexe XSToon, Arktoon and CubedParadox Flat Lit Toon: lowest priority.
- Mochie Uber X: when its source is available; until then its materials get an
  *unsupported* report.

---

## Later

Not planned in detail yet, in rough order:
- Mochie Uber as an output (its parser in reverse);
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
- **a matcap's ways compared by their cost** (from 4.5): one side, two slots and a shifted
  base as candidates, with the blocked view's difference. Built with the first output that
  has two matcap slots of one blend each; until then *Base color shift* decides between
  one side and a shifted base;
- **full light and form shading** (from 2.8): the lit color baked for outputs that don't light
  and the form shading for outputs that light flatly, with each source's lighting family and
  surface response under the rig. Today's outputs light with their own ramps, where either
  would light twice; built with the first unlit or flat output;
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
