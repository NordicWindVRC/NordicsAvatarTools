# Nordic's Advanced Atlaser — Unified material

`AtlasUnifiedMaterial` describes what a material **looks like**, independent of the shader it
came from. Input parsers translate each source material into it; output writers build their
materials only from it.

## Source shader inventories

One document per supported input shader family. Each one lists what the shader lets the user
change, what that does to the look, and how the unified material should represent it.

They're merged into one feature set in
[UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md), the unified material's
specification. The refactoring itself is planned in
[UNIFIED_MATERIAL_REFACTOR.md](UNIFIED_MATERIAL_REFACTOR.md).

| Shader family | Document | Status |
|---|---|---|
| VRChat Mobile (Toon Lit, Standard Lite, Toon Standard) | [UNIFIED_MATERIAL_VRCHAT_MOBILE.md](UNIFIED_MATERIAL_VRCHAT_MOBILE.md) | First draft |
| Unity Standard (metallic and specular setup) | [UNIFIED_MATERIAL_UNITY_STANDARD.md](UNIFIED_MATERIAL_UNITY_STANDARD.md) | First draft |
| Mochie Uber (and Outline) | [UNIFIED_MATERIAL_MOCHIE_UBER.md](UNIFIED_MATERIAL_MOCHIE_UBER.md) | First draft |
| Poiyomi Toon 10 (and its avatar variants) | [UNIFIED_MATERIAL_POIYOMI.md](UNIFIED_MATERIAL_POIYOMI.md) | First draft |

## Guidelines

These are the design rules. The code is being rebuilt to follow them; see
[*Input parser status*](#input-parser-status) below.

### Role

- **It decouples inputs from outputs**, the way the Texture Wrapper decouples graphics work
  from the Atlaser. Parsers know their source shader; writers know their output shader; neither
  knows the other. The source `Material`, shader, parser and property names never reach a writer.
- **It's shader-neutral.** No property names, source enum values, or structures named after a
  shader or shader version. Everything a parser knows about its source stays in the parser.

### Shader availability

- **Parsing never requires the source shader to be installed, but may use it when it is.**
  Parsing mostly works without the shader and fully works with it.
  - A material keeps its values even when its shader is missing, for example materials copied
    into a Quest project without Poiyomi. Parsers read them through `AtlasMaterialReader`,
    which uses the shader when it's there and the material's saved values when it isn't.
    Parsers never call the material or shader API directly.
  - When the shader is installed, a parser may use it for what saved values can't provide,
    such as option names from the shader's declaration or a variant that differs only by
    name.
  - Anything lost without the shader is reported, never guessed.
  - **Typed features never depend on the shader.** Everything that decides the look is read
    from plain values, and each parser holds its own option tables as features get typed.
    The installed shader only adds detail to untyped values, reports and edge cases.
  - **Detection without the shader** uses the property names a material saved: Poiyomi by
    family, Pro and version marker properties; Mochie Uber by its own combination of controls.
    VRChat Mobile and Unity Standard shaders are always present.
  - **Option names** that come from the shader's own declaration can't be read without it.
    Parsers hold their own option tables instead, so typed features never need them.
  - A material parsed this way gets an info note saying so.
- **Output shaders must be installed.** The new materials are made with them, so the output
  picker only offers installed ones. An output whose shader isn't installed shows a warning
  and is skipped at build time with one diagnostic.

### Features

- **A feature is a visual effect, defined by what it does**: base colour, normals, toon
  shading, matcap, rim, decal, emission, and so on. It is *not* a copy of a shader's settings.
- **Variants are modes and parameters of one feature**, not separate features. For example,
  the many lighting modes of different shaders map onto a few lighting families with
  parameters, not one entry per mode.
- **Merge similar features.** Ten input shaders with a hundred features each must not become a
  thousand features. Adding an input shader should mostly add parser code, not model features.
- **A feature only one shader has is fine**, as long as it's described generically, so other
  shaders can map into it later.
- **Mapped by meaning, not copied 1:1.** A value is copied directly only when it means the same
  in the source and the unified material; otherwise the parser translates it.
- **Layers are one ordered list** of a few layer types (surface, transition, lookup, angle,
  subsurface, procedural pattern, refraction). A shader brand never justifies another list or
  class.

### Coverage and fidelity

- **Coverage means the look, not the parameter surface.** Most of a shader's properties (Poiyomi
  has thousands) exist to tweak and animate the result. Parsers resolve them into fewer, more
  basic features and modes, so the output recreates the original's *look*, not its low-level
  controls. A property is covered when its effect on the look is accounted for, either
  represented or folded into something simpler, or when it has no effect in that material
  (settings of disabled features, editor state).
- **Static colour and texture maths is resolved**, and output writers may bake it into textures
  with the Texture Wrapper: colour adjustments, gradient maps, tints, fixed UV transforms,
  combined masks. **What changes at runtime stays a feature**: anything that depends on the
  view, lights or time, and anything driven by animation.
- **The goal is 100% coverage** of every supported input shader. Anything a parser can't map
  yet is reported, one line per feature per material. Report lines are a temporary state, not
  a place to keep unmapped data: there is no bag of raw properties to pass along.
- **The benchmark is a round trip through the same shader.** Poiyomi → Poiyomi, rebuilt only
  from the unified material, must be visually indistinguishable from the original, even where
  individual values differ. There's no shortcut that copies the source material.
- **Writers may support a subset** of the unified features and report what they can't
  reproduce. Input coverage (what parsers read) and output coverage (what writers reproduce) are
  tracked separately.

### Conventions

- **Conventions are normalised once, in the parser**, so writers never need to know how a
  source expressed something: hue in turns (1 = 360°), saturation 1 = unchanged, roughness
  rather than smoothness, colours in **linear** space (matching the Texture Wrapper), and typed
  texture channels, UV sets and sampling.
- **Textures are references**: the source texture plus how it's sampled (UV set, tiling and
  offset, channel, and whether it holds colour, data or a normal map). The unified material
  holds no pixels; writers decide what to bake.

### Animation

- **Animated values are targets in the unified material**, written as paths such as
  `layers[3].opacity` or `fur.length`. A parser maps an animated source property to
  its target, a writer maps the target to its own output property, and the animation clips are
  rewritten to match. Kept short and expandable.
- **The material's current value is always applied.** An animated hue shift, for example, is
  still applied with its current value rather than skipped. Effects that animation shows or
  enables don't need to be skipped: blending with their current value simply doesn't apply them.

### Lifetime

- It's built fresh for each build and never saved, so it needs no migration or
  backward-compatibility code.

## Code

The model is in `Editor/Unified/`, one file per part of the
[feature catalogue](UNIFIED_MATERIAL_FEATURES.md), which is its specification:

| File | Catalogue | Contents |
|---|---|---|
| `AtlasUnifiedMaterial.cs` | — | The root: surface, lighting, layers, colour adjustments, the mask bus, modulators, outlines, fur, render, and the report lines (`Issues`) |
| `AtlasBuildingBlocks.cs` | 1 | Texture reference (with decal placement), scalar input, colour input, mask (with mask bus reading), blend, placement |
| `AtlasMaskBus.cs` | 1.5 | The mask bus and its producer steps |
| `AtlasSurface.cs` | 2 | Base colour and vertex colour, alpha, primary normal, surface response (highlights, reflections, anisotropy, clear coat), parallax |
| `AtlasLighting.cs` | 3 | Lighting family, ramp toon (with bands, SDF face, wrapped), diffuse models (with skin and cloth), light colour and direction models, received shadows, light contributions, additional lights, brightness limits, light volumes, LTCGI |
| `AtlasLayers.cs` | 4 | The parts every layer shares; the surface layer and its targets; the lookup layer (matcap, cubemap); the angle layer (rim, depth rim, backlight); subsurface, pattern, transition, refraction and highlight layers |
| `AtlasColorAdjustment.cs` | 1.10 | Colour adjustments: one operation each, on a target |
| `AtlasModulators.cs` | 1.9 | Modulators and the audio source with spread |
| `AtlasOutline.cs` | 5.1 | Outlines |
| `AtlasDeformation.cs` | 5.3 | Deformations (inflate, rotate, translate, snap) |
| `AtlasFur.cs` | 5.2 | Fur |
| `AtlasRender.cs` | 6 | Surface mode, cutoff, blend factors, pass settings |

Features are added as the parsers that need them are rebuilt, each with its specification in
the catalogue.

**Reading:** parsers read only through `AtlasMaterialReader` (shader or saved values) and the
small helpers in `AtlasSourceMapping`. Colours are converted to linear with
`AtlasMaterialReader.ColorLinear`.

**Reporting:** `AtlasUnifiedMaterial.Report` adds at most one line per kind and feature:
- *unmapped:* affects the look, no typed home yet;
- *unsupported:* can't be read, such as Uber X;
- *ignored:* deliberately not mapped, such as world-only features;
- *note:* how the material was read, such as with its shader missing.

Unmapped and unsupported lines are build warnings; the others are information.

## Input parser status

The parsers are being rebuilt on the new model, one at a time, least complex first; see
[UNIFIED_MATERIAL_REFACTOR.md](UNIFIED_MATERIAL_REFACTOR.md). Until its parser is rebuilt, a
shader's materials go to the fallback parser.

| Input parser | Status |
|---|---|
| VRChat Mobile Toon Lit | **Rebuilt** (step 2): base colour texture × vertex colour, flat lighting, opaque. Full coverage |
| Unity Standard (metallic and specular) | **Rebuilt** (step 3): rendering modes, albedo, metallic or specular, smoothness, normal, parallax, occlusion, emission, detail, highlights and reflections, physically based lighting. Full coverage |
| VRChat Mobile Standard Lite | **Rebuilt** (step 4): base colour × vertex colour, metallic and smoothness map times sliders, normal, occlusion, emission, detail, highlights, reflections, specular anti-aliasing, physically based lighting without shadows. Full coverage |
| Fallback | **Rebuilt** (step 5): common property names with Unity's conventions: base colour, alpha mask, normal, metallic or specular colour, roughness or smoothness, occlusion, emission, culling, depth write and surface mode from blend factors. One warning per material says the result is a guess; a matcap is reported |
| VRChat Mobile Toon Standard (and Outline) | **Rebuilt** (step 6): every section by its keyword: ramp toon lighting, specular, matcap, emission, detail, occlusion, rim, hue shift, colour mask, outline, AudioLink. AudioLink's rim mask and UV-based mode are reported |
| Mochie Uber (and Outline) | **Rebuilt** (step 7, in parts 7a, 7b-1, 7b-2, 7b-3; 7b-3: AudioLink as modulators, the oscilloscope, the remaining filters, highlight, reflection and shadow extras, stencil) (7b-2: dissolve, flipbooks, refraction, iridescence, UV distortion, matcap normals, multi-step parallax, map filters): render and alpha, base colour and cube mode, shading off as flat, workflows, detail, masks, ramp or physically based lighting, reflections, highlights, matcaps, rims, subsurface, emission with pulse and light reactivity, filtering, light volumes, mirror and near clip, vertex manipulation, outline, hard edges, clear coat, static light direction. Four details stay reported (flipbook alpha, AudioLink rim pulse, AudioLink on vertex rotation and UV distortion); Uber X is unsupported |
| Poiyomi Toon 10 | **Rebuilt** (step 8): textures and UV options, main colour, alpha options, normals, rendering, all lighting types, Light Data, reflections and specular, clear coat, LTCGI, detail texture, back face, colour masking, decals, emissions, matcaps, cubemap, rims, depth rim, environmental rim, backlight, subsurface, anisotropics, stylized reflections, colour adjust, post processing, the mask bus, theme colours, time and light modulators, AudioLink, proximity colour, outlines, UV options, dissolve, flipbooks, mirror and camera visibility, UV tile discard, glitter, pathing, voronoi, truchet, internal parallax, depth FX, AL volume colour, video effects, AL spectrum, vertex options, depth bulge, view clip prevention, Lil Fur |
| Poiyomi Toon 11, Poiyomi Pro 10 and 11 | Being rebuilt (step 9) |
| Poiyomi Toon 7.3 to 9.3 | Being rebuilt (step 10) |

## Parser responsibilities and testing

1. Identify the source shader and version (Poiyomi reads `shader_master_label` from locked or
   unlocked shaders if present), with or without the shader installed.
2. Resolve every source property that affects the look into unified features: represent it,
   or fold it into something simpler (see *Coverage and fidelity*). Properties with no effect
   in that material, such as settings of disabled features, are ignored.
3. Translate modes and conventions with the parser's own option tables.
4. Report what can't be mapped yet. **Never claim correctness from merely referencing a
   property name.**
5. Test with materials built from saved values, so tests don't need the shader installed.
