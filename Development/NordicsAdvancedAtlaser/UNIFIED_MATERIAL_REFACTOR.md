# Unified material — refactor plan

**Status:** steps 1 to 9 done (2026-11-16); step 9g-4f waiting for the test run. Phase 4 of the first release: rebuilding the unified material from
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

### 3. Unity Standard (metallic and specular) (done)
`AtlasUnityStandardParser` and `AtlasUnityStandardSpecularParser`, following the shader's
keywords. Specs written for alpha (2.2), surface response (2.4), parallax (2.5), the physically
based diffuse model (3.3), the surface layer's parameters (4.1) and the surface modes opaque,
cutout, fade and transparent (6). `AtlasSourceMapping.Texture` can take another property's
tiling. Every section of the draft is mapped; its five fixes are done. Planned:
- **Typed:**
  - surface response: metallic, specular colour, roughness, occlusion, highlights and
    reflections toggles;
  - parallax;
  - the *physically based* family;
  - render surface modes (opaque, cutout, fade, transparent);
  - emission and the detail map as surface layers, with blend *multiply ×2*.
- **Bug fixed:** metallic with a texture.
- **Test:** the saved-value test moves here.

### 4. VRChat Mobile Standard Lite (done)
`AtlasVRChatStandardLiteParser`. Typed specular anti-aliasing (2.4). Unity's detail mapping
moved to `AtlasSourceMapping.UnityDetail`, shared with Standard. Lightmap type and bicubic
sampling turned out not to be visual on avatars, so they're ignored rather than typed. Every
section of the draft is mapped; its four Standard Lite fixes are done. Planned:
- **Typed:** specular anti-aliasing, the lightmap and ambient options, bicubic sampling.
- **Bugs fixed:** tiling follows the albedo; smoothness with a map is slider × map alpha;
  vertex colour multiply is recorded.

### 5. Fallback (done)
Common property names with Unity's conventions: base colour, alpha mask, normal, metallic
(or specular colour when the shader has no metallic), roughness or smoothness, occlusion,
emission, and the render properties (culling, depth write, surface mode from the blend
factors). One warning per material says the result is a guess. A matcap is reported rather
than mapped: its blend can't be guessed, and the lookup layer comes in step 6.
`AtlasFallbackTests` is its first test set; `AtlasTestMaterials.AddSavedTexture` is new.

### 6. VRChat Mobile Toon Standard (done)
`AtlasVRChatToonStandardParser` (both variants). Typed and specified: ramp toon (3.2), limit
and minimum brightness (3.4), highlight sharpness and occlusion amounts (2.4), the lookup layer
(4.3), the angle layer (4.4), colour adjustment with hue (1.10), the modulator and audio source
with spread (1.9), source alpha as opacity (4.1), two-sided lighting (6), and **outline (5.1)**,
which the Outline variant needs first (planned for step 7). AudioLink's rim mask and UV-based
mode are reported. Planned:
- **Typed:**
  - the *ramp toon* family;
  - lookup layer (matcap) and angle layer (rim);
  - recolour regions as surface layers with blends *tint luminance* and *add to luminance*;
  - colour adjustment (hue);
  - modulators, starting with *audio*, including spread.
- **Bugs fixed:** section toggles are respected; AudioLink is only on when switched on; the
  mask channel is off by one; reflectance has a defined default.

### 7. Mochie Uber
Split in two, like Poiyomi: Uber has about 590 properties.
- **7a (done):** the core sections. Typed: dithered surface mode, alpha strength, vertex colour
  amount, grouped highlights and reflections (strength, tint, mask, roughness override,
  fresnel, cubemap, fallback, and the source *ambient colour* for Unity's reflections off,
  which Standard and Standard Lite now use), occlusion indirect, ramp keys, ramp strength and
  term mask, light volumes, the lookup layer's centre eye, tiling, blur, source alpha and light
  response, the angle layer's power band and environment colour, the subsurface layer, the
  time and light-level modulator sources and modulator masks, colour operations saturation,
  contrast, brightness, tint and hue methods HSV and Oklab, and the *surface colour*
  adjustment target.
- **7b-1 (done):** geometry and render: cube mode (lookup projection *view direction*), mirror
  visibility and mirror base colour, invisible, near clip, deformations (inflate, rotate,
  translate, snap), the outline extended (vertex colour width, hide within, multiply by base
  colour, lit, emission), flat shading, specular from the mesh normal, fixed light direction.
- **7b-2 (done):** the transition layer (dissolve), decal placement and frames on texture
  references (flipbooks), the refraction layer, the angle profiles hue rotation and ramp with
  *specular only* and *scale by metallic* (iridescence), shared UV distortion on texture
  references, lookup normal maps, multi-step parallax, scalar remaps (applied to the sample,
  before strength and inversion) and the occlusion tint (map filters).
- **7b-3 (done):** AudioLink as audio modulators (with *value without AudioLink*), the
  oscilloscope as the first procedural pattern layer, colour operations smooth invert, sRGB
  decode, ACES and channel tint, hue *set*, highlight steps, shapes and options, reflection
  steps and light-based strength, ramp conditions and dithering, stencil per pass, the alpha
  mask multiplier, Disney and directional amounts. Four details stay reported.

Planned for step 7 as a whole:
- **Typed:**
  - subsurface;
  - packed and separate masks with remap (map filters);
  - texture filtering;
  - external lighting (light volumes, LTCGI);
  - angle layer profiles (environment rim, iridescence);
  - transition (base colour dissolve);
  - outline (5.1);
  - deformation (5.3: inflate, snapping, transform);
  - outline: already typed in step 6; Uber extends it;
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
- **8a-1 (done):** textures (Poiyomi's UV choice, panning, stochastic, UV channel settings),
  main colour, alpha options (alpha map blend, offset, fade modifiers, sharpened coverage,
  dithering), normals (second, detail, bent, normal correct), rendering (presets including
  additive, soft additive, multiplicative and ×2; depth clip; stencil masks; fog; reduced near
  clipping). `AtlasPoiyomiParser` is the new parser, split by inspector section.
- **8a-2 (done):** the lighting types TextureRamp, Multilayer Math (bands), Flat and Realistic,
  on one ramp toon formula that every source is a special case of (decided in this step).
  Toon Standard moved to the formula's parameters.
- **8a-3 (done):** Light Data: the light colour and direction models, the light map as the
  shading term, AO, detail shadow and shadow mask maps, received shadows, brightness limits,
  forced colour, additional lights, light volumes.
- **8a-4 (done):** the lighting types Wrapped, ShadeMap, SDF (and the SDF light map), Skin and
  Cloth; Realistic's shadow strength and tint. Every Poiyomi lighting type now maps.
- **8a. Core:** base colour and alpha, normals, the four lighting families with every Poiyomi
  lighting type mapped to one of them, shadows and shared lighting parameters, render.
- **8b. Layers,** in eight parts:
  - **8b-1 (done):** Reflections & Specular (packed map, masks, tints, fresnel, exposure
    occlusion, fallback cubemap, GGX anisotropy, 2nd specular, GSAA), Clear Coat, LTCGI.
  - **8b-2 (done):** surface layers: detail texture, back face, decals, emissions.
  - **8b-3 (done):** RGBA colour masking.
  - **8b-4 (done):** matcaps and cubemap (lookup layers).
  - **8b-5 (done):** the two rims in their three styles (angle layers, step profile).
  - **8b-6 (done):** depth rim, environmental rim, backlight, subsurface; the rims'
    light-direction options.
  - **8b-7 (done):** the anisotropics and stylized reflections highlight styles, as a new
    highlight layer type (decided in this step); lilToon-mode stylized reflections reported.
  - **8b-8 (done):** colour adjust and post processing, and the layers' own hue shifts and
    colour adjusts.
- **8c. Masks and live values,** in four parts:
  - **8c-1 (done):** the mask bus (global mask textures, vertex colours, modifiers, options;
    every feature's global mask and *apply to global mask*), theme colours resolved to colours.
  - **8c-2 (done):** time and light: hue speeds, emission blinking, scrolling and light-based
    glow; blacklight masking.
  - **8c-3 (done):** AudioLink: the section's smoothing, emission, decal, matcap, rim, alpha and
    hue modulation, ColorChord and AudioLink themes on colour inputs.
  - **8c-4 (done):** proximity colour, fluorescence, centre-out emission.
- **8d. Patterns and geometry,** in eleven parts:
  - **8d-1 (done):** outlines (shapes, width options, colour, highlight, distance fade, render
    settings, blending, stencil, AudioLink).
  - **8d-2 (done):** the UV options: distortion, UV channel rotation, stochastic modes,
    parallax heightmapping; projection settings reported.
  - **8d-3 (done):** dissolve (transition).
  - **8d-4 (done):** flipbooks (frames), visibility (mirror and camera, UV tile and face discard).
  - **8d-5 (done):** glitter as a pattern kind.
  - **8d-6 (done):** voronoi, truchet and pathing as pattern kinds.
  - **8d-7 (done):** internal parallax, depth FX and AL volume colour; the stats overlay
    reported.
  - **8d-8 (done):** video effects and the AL spectrum.
  - **8d-9 (done):** vertex options' Basics & Fun as deformations.
  - **8d-10 (done):** look-at, glitching, depth bulge, view clip prevention.
  - **8d-11 (done):** Lil Fur; the section report is removed.

### 9. Poiyomi 11 and Pro
11's PBR, sheen, glints and clear coat; mask levels and vertex-colour mask sources; black and
white; Pro: shell fur, outline layers, screen-space lighting, squish, TPS, GT7 tone mapping,
constellation as a pattern kind. Pro variants (tessellation, shatter and the others) are
reported as geometry effects. Toon 11 adds about 1,000 properties over 10.0.29, so in seven parts:
- **9a (done):** parsers for Toon 11, Pro 10 and Pro 11 (Toon 11 keeps 10's names; its additions
  read as their defaults in 10); mask levels and vertex-colour mask sources on every mask the
  shared helper builds; the rest of 11 and Pro reported, one line each.
- **9b (done):** 11's lighting: the PBR type, sheen, glints, clear coat, lighting limits, Light
  Data and shading additions.
- **9c (done):** colour, alpha and layers: Black & White, RGBA colour masking, the colour adjust
  texture, vertex colour hue, alpha dithering, decal sprite sheets, MSDF, flip in mirror and
  smooth border.
- **9d (done):** rims (application stage, anti-aliasing, transparency, main texture UV), two
  backlights, contacts and the global mask textures' levels.
- **9e (done):** the outline additions.
- **9f:** the smaller effect additions, in three parts:
  - **9f-1 (done):** the small ones: normal correct's vertex colour mask, cubemap material
    smoothness, dissolve colour adjust, flipbook depth, mirror flip and video effects, emission
    transparency, glitter angle offset, screen-space UV sizing, the height map channel, post
    processing's emission and global mask.
  - **9f-2 (done):** pathing, voronoi and internal parallax.
  - **9f-3 (done):** the reworked AL spectrum.
- **9g:** Pro (about 260 properties over Toon 11, and the variants), in four parts:
  - **9g-1 (done):** GT7 tone mapping, neutral emission, SSAO and contact shadows.
  - **9g-2 (done):** TPS, squish and the outline curvature bake.
  - **9g-3 (done):** constellation as a pattern kind.
  - **9g-4:** the variants. Each adds its own properties (60 to 265; about 1,300 in all, Toon's
    Two Pass and Grab Pass included), so in four parts:
    - **9g-4a (done):** Two Pass's second pass and its pass masks; Pro Fur's shells; Lil Fur
      11's additions (mask options, contact, vertex colour modes, cutout length).
    - **9g-4b (done):** Grab Pass (Toon and Pro) and Self Grab: the refraction layer rewritten
      with the screen-grab effects (blur patterns, pixelation, colour, ramp, masks, blend
      factors); Uber's refraction moved onto it.
    - **9g-4c (done):** Triple Outline (the outline list) and Triplanar Projection (a
      material-level mapping setting).
    - **9g-4d (done):** the geometry variants: tessellation mapped (5.4); Geometric Dissolve,
      ShatterWave and the particles reported, one line each; Geom adds no settings.
    - **9g-4f (done):** the leftovers a property audit of Pro 11.0.50 found: the Skin section
      as a pattern kind; the light, emission and final colour multipliers; the flat light map;
      the additional lights' combine (max, Poiyomi's default, with its alpha boost; other add
      pass factors reported); the time source; Early Z as a depth prepass; the stochastic
      density; shading off as unlit; parallax curvature and the world AO blocker reported. The
      rest were already handled or reported, or are editor-only, internal look-up tables or
      unused properties (the dissolve edge's second border, TPS idle gravity, the SSS bleed
      weights). The "poiyomi.v11-sections" and "poiyomi.pro-sections" reports are gone.
    - **9g-4e (done):** Pro Grab Pass's lava lamp as a pattern kind (the container and its SDF
      volume, blobs, shading, reservoirs, haze, AudioLink).

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
