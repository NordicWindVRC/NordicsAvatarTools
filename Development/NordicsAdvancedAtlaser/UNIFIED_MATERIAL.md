# Nordic's Advanced Atlaser — Unified material

`AtlasUnifiedMaterial` describes what a material **looks like**, independent of the shader it
came from. Input parsers translate each source material into it; output writers build their
materials only from it.

## Source shader inventories

One document per supported input shader family. Each one lists what the shader lets the user
change, what that does to the look, and how the unified material represents it.

They're merged into one feature set in
[UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md), the unified material's
specification. How outputs turn unified materials into atlas materials is in
[ATLAS_OUTPUT.md](ATLAS_OUTPUT.md).

| Shader family | Document | Status |
|---|---|---|
| VRChat Mobile (Toon Lit, Standard Lite, Toon Standard) | [UNIFIED_MATERIAL_VRCHAT_MOBILE.md](UNIFIED_MATERIAL_VRCHAT_MOBILE.md) | Inventory and parser |
| Unity Standard (metallic and specular setup) | [UNIFIED_MATERIAL_UNITY_STANDARD.md](UNIFIED_MATERIAL_UNITY_STANDARD.md) | Inventory and parser |
| Mochie Uber (and Outline) | [UNIFIED_MATERIAL_MOCHIE_UBER.md](UNIFIED_MATERIAL_MOCHIE_UBER.md) | Inventory and parser |
| Poiyomi Toon 10 (and its avatar variants) | [UNIFIED_MATERIAL_POIYOMI.md](UNIFIED_MATERIAL_POIYOMI.md) | Inventory and parser |

## Guidelines

These are the design rules the code follows; see [*Input parsers*](#input-parsers)
below.

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
  - **Variants that differ only by a pass** (Poiyomi's Two Pass, Grab Pass, the furs and so on)
    are told by a property only the variant declares. With the shader installed that settles
    it. Without it, a saved property may be left over from a variant the material used before:
    the shader name Thry's optimizer tagged the material with when it was locked decides, and
    without that name the variant is assumed and reported. Mochie Uber and Uber (Outline)
    declare the same properties, so without the shader the outline isn't read, with a note.
  - **Locked Poiyomi materials** are read through their locked shader. Thry's optimizer renames
    the properties a material marks as *renamed when locked* to `<name>_<suffix>`;
    `AtlasMaterialReader` reads those under their new names.
  - **Typed features never depend on the shader.** Everything that decides the look is read
    from plain values, and each parser holds its own option tables as features get typed.
    The installed shader only adds detail to untyped values, reports and edge cases.
  - **Detection without the shader** uses the property names a material saved: Poiyomi by
    family, Pro and version marker properties; Mochie Uber by its own combination of controls.
    VRChat Mobile and Unity Standard shaders are always present.
  - **Detection with the shader** goes by its name. For Poiyomi: a locked shader's name holds
    the original; a name with the word *Pro* is Pro and every other Poiyomi shader is Toon
    (8.0's *Poiyomi Outline* and 8.1's *Poiyomi Grab Pass* don't say Toon); the master label
    gives the version. A version newer than the newest parser, or none at all, is read by the
    newest parser and reported.
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
  subsurface, procedural pattern, refraction, highlight). A shader brand never justifies
  another list or class.

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
| `AtlasDeformation.cs` | 5.3, 5.4 | Deformations (transforms, distortions, wind, look-at rigs, glitch, TPS, squish and others) and tessellation |
| `AtlasFur.cs` | 5.2 | Fur |
| `AtlasRender.cs` | 6 | Surface mode, cutoff, blend factors, pass settings, visibility, the second pass |

A feature is added to the model together with its specification in the catalogue, when a
parser first needs it.

**Code style:**
- **Plain data classes:** public fields with defaults, one class per catalogue feature. No
  inheritance beyond what a list of mixed items needs: layers are one list, so they share one
  base class.
- **Neutral enums** with the catalogue's names. No string-keyed values and no dictionaries of
  parameters; the only strings are modulator paths and report text.
- **One parser per shader family,** reading top to bottom in the order of the shader's
  inspector. Poiyomi's reading is in `AtlasPoiyomiReader`, split into one file per inspector
  section (`AtlasPoiyomi*.cs`); a reader is made for each parse and holds the state the sections
  share (the version being read, the shared UV distortion), so the parsers keep no state.
- **Option tables** (source enum value → unified choice) are small static arrays or switches
  next to where they're used. Typed features never ask the shader.
- **Shared helpers only for real repetition,** such as a texture reference from a texture
  property with its tiling and UV choice.
- **Units are converted once, in the parser:** colours to linear, hue to turns, smoothness to
  roughness.

**Modulator paths** are checked by `AtlasModulatorPath.Resolve`, which finds the value a path
points at; parser tests run every modulator through it.

**Reading:** parsers read only through `AtlasMaterialReader` (shader or saved values) and the
small helpers in `AtlasSourceMapping`. Colours are converted to linear with
`AtlasMaterialReader.ColorLinear`.

**Reporting:** `AtlasUnifiedMaterial.Report` adds at most one line per kind and feature:
- *unmapped:* affects the look but has no typed home;
- *unsupported:* can't be read, such as Uber X;
- *ignored:* deliberately not mapped, such as a world variant's world-only features;
- *note:* how the material was read, such as with its shader missing.

Unmapped and unsupported lines are build warnings; the others are information.

## Input parsers

Every supported shader has its own parser; any other shader's materials go to the fallback
parser. Each inventory document describes its parser in detail.

| Input parser | What it reads | Reported or out of scope |
|---|---|---|
| VRChat Mobile Toon Lit | Base colour texture × vertex colour, flat lighting without received shadows, the ambient colour standing in for a missing main light, opaque | — |
| Unity Standard (metallic and specular) | Rendering modes, albedo, metallic or specular, smoothness, normal, parallax, occlusion, emission, detail, highlights and reflections, physically based lighting | — |
| VRChat Mobile Standard Lite | Base colour × vertex colour, metallic and smoothness map times sliders, normal, occlusion, emission, detail, highlights, reflections, specular anti-aliasing, physically based lighting without shadows | — |
| VRChat Mobile Toon Standard (and Outline) | Every section by its keyword: ramp toon lighting, specular, matcap, emission, detail, occlusion, rim, hue shift, colour mask, outline, AudioLink | AudioLink's rim mask and UV-based mode |
| Mochie Uber (and Outline) | Render and alpha, base colour and cube mode, shading off as flat, workflows and map filters, detail, masks, ramp or physically based lighting, reflections, highlights, matcaps, rims, subsurface, refraction, iridescence, emission with pulse and light reactivity, filtering, dissolve, flipbooks, UV distortion, vertex manipulation, outline, hard edges, clear coat, static light direction, light volumes, mirror and near clip, AudioLink, the oscilloscope, stencil | Flipbook alpha, AudioLink rim pulse, AudioLink on vertex rotation and UV distortion; Uber X is unsupported |
| Poiyomi Toon 10 and 11 (and their avatar variants) | Every section: textures and UV options, main colour and colour adjust, alpha, normals, rendering, all lighting types and Light Data, reflections and specular, clear coat, LTCGI, surface layers (detail, back face, colour masking, decals, emissions, skin tone), matcaps and cubemap, rims and the other angle layers, anisotropics and stylized reflections, post processing, the mask bus and contacts, theme colours, modulators (time, light, AudioLink), outlines, dissolve, flipbooks, visibility, UV tile discard, the procedural patterns, vertex options, fur; the Two Pass, Grab Pass and Lil Fur variants | A few details, each reported (for example hextile sampling, the newer UV distortion modes, lilToon-style stylized reflections); the stats overlay is out of scope |
| Poiyomi Pro 10 and 11 (and their variants) | Everything Poiyomi Toon reads, plus SSAO, contact shadows, TPS, squish, constellation, the lava lamp, GT7 tone mapping and neutral emission, the outline bake, shell fur, Triple Outline, Self Grab, Triplanar Projection and tessellation | Geometric Dissolve, ShatterWave and the particle variants; the world variants are read as their avatar shaders, with one ignored line |
| Poiyomi Toon 7.3, 8.x and 9.x | Read on the Poiyomi 10 parser with version tables for renamed and re-meaning settings | Features Poiyomi 10 rewrote are read with 10's meaning and noted; 7.3's specular, metallics, BRDF, clear coat, SSS and panosphere sections are reported |
| Fallback | Common property names with Unity's conventions: base colour, alpha mask, normal, metallic or specular colour, roughness or smoothness, occlusion, emission, culling, depth write and surface mode from blend factors | One warning per material says the result is a guess; a matcap is reported |

## Future input shaders

Shaders common on VRChat avatars that don't have a parser yet, in suggested order. Until one
does, its materials go to the fallback parser. A parser starts with an inventory of the
shader's inspector, as the existing `UNIFIED_MATERIAL_*.md` documents do.

**VRChat Mobile (Quest avatars).** Android and Quest avatars may only use the shaders that ship
with the current VRChat SDK, under `VRChat/Mobile/` (see VRChat's
[Quest content limitations](https://creators.vrchat.com/platforms/android/quest-content-limitations/)).
Toon Lit, Standard Lite and Toon Standard already have parsers. The rest are small:

| Shader | Notes |
|---|---|
| Matcap Lit | A matcap on a base texture; a lookup layer |
| Diffuse, Bumped Diffuse, Bumped Mapped Specular | Unity's legacy mobile shaders: base colour, normal map, a simple specular |
| Particles/Additive, Particles/Multiply | Particle materials on avatars; additive and multiplicative surface modes |

**PC avatars:**

| Shader | Notes |
|---|---|
| lilToon (lilxyzw) | Very common, especially on Japanese avatars; open source ([GitHub](https://github.com/lilxyzw/lilToon)). Poiyomi already maps lilToon-style rims and stylized reflections. |
| UnlitWF (whiteflare) | Common on Japanese avatars; stepped shading and metallics despite the name ([GitHub](https://github.com/whiteflare/Unlit_WF_ShaderSuite)). |
| Unity-Chan Toon Shader (UTS2, UTS3) | Found in older and Japanese assets; UTS2 is no longer developed. Poiyomi already maps UTS-style rims and shade maps. |
| Silent's Cel Shading | Anime cel shading following Unity's lighting, with Standard-style metalness and smoothness. |
| Sunao Shader | Japanese avatar toon shader; inventory to be made from its source. |
| MToon | VRM's toon shader, on avatars converted from VRM. |
| Orels Toon and Standard | Community shaders on some avatars; inventory to be made from their source. |
| Mochie Uber X | The paid build of Uber, adding geometry and dissolve effects. Its materials get an *unsupported* report until its source is available. |
| Xiexe XSToon, Arktoon, CubedParadox Flat Lit Toon | Legacy shaders on older avatars; lowest priority. Arktoon remains as an MIT-licensed archive. |

## Parser responsibilities and testing

1. Identify the source shader and version (Poiyomi reads `shader_master_label` from locked or
   unlocked shaders if present), with or without the shader installed.
2. Resolve every source property that affects the look into unified features: represent it,
   or fold it into something simpler (see *Coverage and fidelity*). Properties with no effect
   in that material, such as settings of disabled features, are ignored.
3. Translate modes and conventions with the parser's own option tables.
4. Report what can't be mapped. **Never claim correctness from merely referencing a
   property name.**
5. Test with materials built from saved values, so tests don't need the shader installed:
   a Standard material carrying the source shader's saved properties
   (`AtlasTestMaterials.AddSaved*`), read with `AtlasTestMaterials.SavedValuesOnly`.

**Tests per parser:**
- an untouched material maps to the feature defaults with no unmapped lines;
- each converted value: units, option tables, combined slider and map;
- each known source quirk, with a test that fails on the naive reading;
- with the shader installed, reading through the shader gives the same result as reading saved
  values.
