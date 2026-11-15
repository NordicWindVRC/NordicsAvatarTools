# Unified material — refactor plan

**Status:** steps 1 and 2 done (2026-11-15); step 2 waiting for the test run. Phase 4 of the first release: rebuilding the unified material from
the [feature catalogue](UNIFIED_MATERIAL_FEATURES.md), then rebuilding every input parser on it.

**Goal:** the code matches the catalogue. Every supported input shader maps into typed
features, and anything it can't map yet is reported, never stored as free-form data.

---

## Approach

**Replace, don't run side by side.** Input parsers may be broken while the model is rebuilt,
so there is no old and new model living together and no adapter between them.
1. The new model replaces the old one in one step.
2. Every parser is then rebuilt on it, **one at a time, least complex first**.
3. Until its turn, a shader's materials go to the fallback parser.

The rest of the Atlaser keeps building and its tests stay green throughout: discovery, the
geometry bake, normal correction, the Normal Baker and output layout. Only three places read
the unified material today: auto setup (the largest base colour texture, and which textures
exist), the build coordinator (issues), and the generator job's normal-map order comment.

**Rewrite rather than update** wherever the old code is shaped by the old model, which is
nearly all of it. The parsers write the old structures on almost every line, so updating
them would touch everything anyway, and a rewrite can follow the shader's inspector, as the
inventories do.

**Kept as is:**
- the reading half of `AtlasMaterialReader`;
- `AtlasSavedProperties` and the shader-availability rules;
- `AtlasPoiyomiVersionDetector`;
- `AtlasShaderRegistry`. Each shader's detection (`CanParse`) returns with its rebuilt parser;
- the fur definition, which already matches 5.2 and moves into the new model.

**The model grows with the parsers.**
- The first step builds the building blocks and the features every shader has.
- Each later parser adds the features it is the first to need.
- When a feature is typed, its parameter spec (units, defaults, formula) is written into the
  catalogue at the same time. That's when the conventions get decided anyway.
- Features no supported shader uses are not written ahead of time.

### Less complex code
- **Plain data classes:** public fields with defaults, one class per catalogue feature. No
  inheritance beyond what a list of mixed items needs: layers are one list, so they share one
  base class.
- **Neutral enums** with the catalogue's names. No string-keyed values, no dictionaries of
  parameters.
- **One parser per shader family,** reading top to bottom in the order of the shader's
  inspector. Poiyomi is split into one file per inspector section.
- **Option tables** (source enum value → unified choice) are small static arrays next to
  where they're used. Typed features never ask the shader.
- **Shared helpers only for real repetition:** a texture reference from a texture property,
  its tiling and its UV choice; a scalar from a slider and a map. Nothing speculative.
- **Units are converted once, in the parser:** colours to linear, hue to turns, smoothness to
  roughness.

### Reporting
- **Unmapped:** a source value that affects the look but has no typed home yet. One report
  line per feature per material, with the feature's name.
- **Ignored:** world-only features. At most one info line per material.
- **Not visual** (editor state, debug views, the stats overlay): nothing.
- **Unsupported:** Uber X. One line.

`AtlasSemanticFeature` and `AtlasSemanticReader` are removed in step 1. Reporting doesn't need
them.

### Tests
- **Shaders don't need to be installed.** Parser tests build their materials from saved
  values: a Standard material carrying the source shader's saved properties, read with
  `ForceSavedProperties`, as `AtlasShaderAvailabilityTests` already does. The helper moves to a
  shared test utility.
- **Per parser:**
  - an untouched material maps to the feature defaults with no unmapped lines;
  - each converted value: units, option tables, combined slider and map;
  - each known bug from the inventory gets a test that fails on the old behaviour;
  - one test per shader, when that shader is installed, checks that reading through the shader
    gives the same result as reading saved values.
- **Old tests** that check the old model's fields are deleted in step 1. Each parser step adds
  its own.

### Each step ends with
1. An offline compile.
2. You run the EditMode tests in Unity.
3. Docs updated:
   - the catalogue gets the specs of the newly typed features;
   - the parser's inventory draft has accurate *Parser today* lines;
   - the parser status table in `UNIFIED_MATERIAL.md`;
   - `CHANGELOG.md`.
4. A coverage pass against that shader's inventory: every inspector section is mapped,
   reported or ignored with a reason.

---

## Steps

### 1. Foundation (done)
- **New model** in `Editor/Unified/`, one file per catalogue part:
  - `AtlasUnifiedMaterial` (root and report lines);
  - building blocks: texture reference, scalar input, colour input, mask, blend, placement;
  - surface: base colour, alpha, primary normal, the basic surface response inputs;
  - lighting: the family;
  - layers: the shared layer parts and the surface layer with targets;
  - render: surface mode, cutoff, blend factors, pass settings;
  - fur (moved).

  Parallax, the lighting families' parameters and everything else come with the parser that
  first needs them.
- **Removed:** the old model types, `AtlasSemanticFeature`, `AtlasSemanticReader`, the model
  helpers in `AtlasMaterialReader`, and all parsers but the fallback. Commit `e3b647d` is the
  reference for the old mappings and detection.
- **Reader:** adds `ColorLinear`. `AtlasSourceMapping` holds the shared helpers (texture
  reference, colour input).
- **Fallback parser,** minimal: base colour under the common names, and one report line saying
  the parsers are being rebuilt.
- **Consumers:** auto setup sizes by the base colour texture; the build coordinator shows
  unmapped and unsupported lines; the generator job's normal order points at the primary
  normal and at surface layers targeting the normal.
- **Tests:** old-model and parser tests removed; new tests for the building blocks, report
  lines, auto setup sizing and the fallback; the saved-value test now uses the fallback.
- **Docs:** the catalogue is the specification; `UNIFIED_MATERIAL.md` describes the new code and
  lists every parser as being rebuilt; README and changelog note the rebuild.

### 2. VRChat Mobile Toon Lit (done)
- **Typed:** vertex colour on the base colour (2.1); the *flat* family; the shared lighting
  parameters ambient model, received shadows and light contributions (3.4). Specs written into
  the catalogue.
- **Parser:** `AtlasVRChatToonLitParser`, detected by shader name.
- **Tests:** `AtlasTestMaterials` is the shared saved-value helper; `AtlasVRChatToonLitTests`
  covers the untouched material, texture tiling, and the installed shader against saved values.
- **Coverage:** the shader's one property is mapped; nothing is reported.

### 3. Unity Standard (metallic and specular)
- **Typed:**
  - surface response: metallic, specular colour, roughness, occlusion, highlights and
    reflections toggles;
  - parallax;
  - the *physically based* family;
  - render surface modes (opaque, cutout, fade, transparent);
  - emission and the detail map as surface layers, with blend *multiply ×2*.
- **Bug fixed:** metallic with a texture.
- **Test:** the saved-value test moves here.

### 4. VRChat Mobile Standard Lite
- **Typed:** specular anti-aliasing, the lightmap and ambient options, bicubic sampling.
- **Bugs fixed:** tiling follows the albedo; smoothness with a map is slider × map alpha;
  vertex colour multiply is recorded.

### 5. Fallback
The rest of the conventional names: normal, emission, metallic, roughness, occlusion,
specular, matcap. Unknown shaders get one report line saying the mapping is a guess. Its first
tests.

### 6. VRChat Mobile Toon Standard
- **Typed:**
  - the *ramp toon* family;
  - lookup layer (matcap) and angle layer (rim);
  - recolour regions as surface layers with blends *tint luminance* and *add to luminance*;
  - colour adjustment (hue);
  - modulators, starting with *audio*, including spread.
- **Bugs fixed:** section toggles are respected; AudioLink is only on when switched on; the
  mask channel is off by one; reflectance has a defined default.

### 7. Mochie Uber
- **Typed:**
  - subsurface;
  - packed and separate masks with remap (map filters);
  - texture filtering;
  - external lighting (light volumes, LTCGI);
  - angle layer profiles (environment rim, iridescence);
  - transition (base colour dissolve);
  - outline (5.1);
  - deformation (5.3: inflate, snapping, transform);
  - refraction;
  - frames (flipbooks);
  - UV distortion as a modulator;
  - the oscilloscope as the first procedural pattern kind;
  - mirror visibility.
- **Stays reported:** Uber X.
- **May be needed:** older Uber sources, if a material shows a difference. You offered to get
  them.

### 8. Poiyomi 10
The largest step, in four parts. Each part ends with tests and your test run.
- **8a. Core:** base colour and alpha, normals, the four lighting families with every Poiyomi
  lighting type mapped to one of them, shadows and shared lighting parameters, render.
- **8b. Layers:** decals, details, emissions, matcaps, cubemap, rims, back face, colour
  masking; placement and emission amounts; colour adjustment.
- **8c. Masks and live values:** the mask bus (global masks), palette (global themes),
  modulators (time, audio, light level, camera distance, contact, blacklight, sweep, noise),
  AudioLink sections, proximity colour.
- **8d. Patterns and geometry:**
  - glitter, voronoi, pathing, AL spectrum and AL volume colour as typed pattern kinds; the
    rarer kinds are reported;
  - transition (dissolve);
  - outline, Lil Fur, vertex options as deformation;
  - visibility.

### 9. Poiyomi 11 and Pro
11's PBR, sheen, glints and clear coat; mask levels and vertex-colour mask sources; black and
white; Pro: shell fur, outline layers, screen-space lighting, squish, TPS, GT7 tone mapping,
constellation as a pattern kind. Pro variants (tessellation, shatter and the others) are
reported as geometry effects.

### 10. Poiyomi 7.3 to 9.3
- **Version tables:** names and meanings that differ from 10, read from the sources on disk.
- **Effort:** 8.1 to 9.3 differ little. 7.3 is the most work.
- **Tests:** an untouched material per version, plus each known meaning change, such as
  brightness and saturation.

### 11. Finish
No free-form data left, and no `Material` or shader API calls outside the reader. The
catalogue's coverage table matches the code. The docs describe the new code only.

---

## Not in this plan
- **The atlasing pass:** merging sources into one output, settings that can't differ, UV-tile
  features, vertex colours.
- **Output writers,** starting with Poiyomi 10.
- **Release phases 2 and 3:** normal-map compositing and the mesh remapper. They don't depend
  on this refactor. They can run before or after it, but not in the middle of it.
- **Phase 5, animation:** modulator paths and animation paths share one syntax, so phase 5
  reads clips into the same targets.

## Before step 1
- **Commit the current state.** That commit is the reference for the old mappings.
- **Version:** these changes go into the unreleased 0.40.0, since nothing has been released.
- **Names:** keep the `Atlas` prefix that all the Atlaser's types use.
