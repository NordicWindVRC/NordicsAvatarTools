# Nordic's Advanced Atlaser — Unified material

`AtlasUnifiedMaterial` describes what a material **looks like**, independent of the shader it
came from. Input parsers translate each source material into it; output writers build their
materials only from it.

## Source shader inventories

One document per supported input shader family. Each one lists what the shader lets the user
change, what that does to the look, and how the unified material should represent it.

They're merged into one proposed feature set in
[UNIFIED_MATERIAL_FEATURES.md](UNIFIED_MATERIAL_FEATURES.md) (second refinement pass), which is
the target for the unified material refactoring.

| Shader family | Document | Status |
|---|---|---|
| VRChat Mobile (Toon Lit, Standard Lite, Toon Standard) | [UNIFIED_MATERIAL_VRCHAT_MOBILE.md](UNIFIED_MATERIAL_VRCHAT_MOBILE.md) | First draft |
| Unity Standard (metallic and specular setup) | [UNIFIED_MATERIAL_UNITY_STANDARD.md](UNIFIED_MATERIAL_UNITY_STANDARD.md) | First draft |
| Mochie Uber (and Outline) | [UNIFIED_MATERIAL_MOCHIE_UBER.md](UNIFIED_MATERIAL_MOCHIE_UBER.md) | First draft; parser added |
| Poiyomi Toon 10 (and its avatar variants) | [UNIFIED_MATERIAL_POIYOMI.md](UNIFIED_MATERIAL_POIYOMI.md) | First draft |

## Guidelines

These are the design rules. Where the code doesn't follow them yet, see
[*Known gaps*](#known-gaps) below.

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
  - **Option names** that come from the shader's own declaration (about 30 reads, all feeding
    untyped values today) can't be read without it, and are reported. They move into the
    parsers' own option tables as those values are typed.
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
- **Layers are lists** where a material can really have several: normals, emissions, decals,
  matcaps, rims and so on. A shader brand never justifies another list or class.

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
  yet is reported as an issue, saying how faithful the mapping is (exact, approximate,
  unresolved). Issues are a temporary state, not a place to keep unmapped data: there is no bag
  of raw properties to pass along.
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
  `decal[0].alpha` or `colorProcessing.hueShift`. A parser maps an animated source property to
  its target, a writer maps the target to its own output property, and the animation clips are
  rewritten to match. Kept short and expandable.
- **The material's current value is always applied.** An animated hue shift, for example, is
  still applied with its current value rather than skipped. Effects that animation shows or
  enables don't need to be skipped: blending with their current value simply doesn't apply them.

### Lifetime

- It's built fresh for each build and never saved, so it needs no migration or
  backward-compatibility code.

## Known gaps

Where the current code doesn't follow the guidelines yet:

- **Lighting modes** (`AtlasLightingMode`, 15 values) still include several modes that exist
  because one shader has them, rather than a few lighting families with parameters.
- **Free-form semantic features** (`AtlasSemanticFeature`, about 40 IDs, half of them from the
  advanced Poiyomi parser) hold many effects as text-keyed values instead of typed features.
  They should shrink as features become typed or are resolved into simpler ones.
- **Colours** are stored as authored in the material (sRGB), not linear.
- **Animation** is only declared, for decal alpha and emission. Nothing reads animation clips yet.
- **Coverage** is partial. Poiyomi in particular reads its main features, but most procedural,
  animated and special effects aren't mapped. Every Poiyomi material also gets a generic
  "advanced effects not fully normalised" issue instead of a list of its unmapped effects.
- **No output writer exists yet**, so no mapping has been verified by rebuilding a material.

## Structures

| Shared union member | Main properties |
|---|---|
| `Render` | Surface mode, source/destination GPU blend factors, depth test, cull mode, ZWrite, alpha-to-coverage, queue, alpha cutoff, vertex color |
| `Surface` | Metallic/specular workflow, base color, metallic, roughness, specular, occlusion, height, packed channels, reflection toggles, geometric specular AA |
| `Lighting` | `AtlasLightingMode`, direct/indirect intensities, ramp, shadow strength, border, blur, offset, cap, brightness, wrapping, extra lights, lightmap and `ToonBands[]` |
| `Normals[]` | Normal map, UV, strength, mask, blend, tangent/object/world space |
| `Emissions[]` | Texture, tint, intensity, enablement, mask, hue, saturation, replacement and animation toggles |
| `Matcaps[]` | Texture, blend operation and simultaneous blend weights, mask/channel, projection, UV mode, border, rotation, intensity, custom normal, lighting interaction |
| `Details[]` / `Decals[]` | Texture, UV, blend, mask, tint, strength and normal effect |
| `ColorMasks[]` | RGBA-selectable masks, tint, emission strength and canonical blend mode |
| `ColorProcessing` | Hue, brightness (additive offset and multiplier), contrast, saturation (1 = unchanged), gamma, chromatize, tint with its mode (*Multiply* or *Perceptual*), clamp, and masks |
| `AudioReactive` | Pulse/scroll/bar modes, band, blend, masks, intensity, fallback controls |
| `ExternalLighting` | Light volumes, AreaLit and LTCGI activation/intensity |
| `Rims[]`, `Subsurfaces[]`, `Reflections[]` | Typed effect layers, each extendable independently |
| `Features[]` | Shared semantic feature IDs (e.g. `surface.clearcoat`, `environment.rain`, `geometry.wind`) with typed values/textures/masks; no source property names |
| `Issues[]` | Warnings for unresolved source semantics; output generators must not silently assume pixel equivalence |

The additional `AtlasSemanticFeature` mechanism is a **temporary** place for features that
aren't typed yet (see *Known gaps*). Keys refer to semantics (e.g. `rippleDensity`), never to
the source shader's property identifier (e.g. `_RippleDensity`). Repeated instances must be
layers, not `Feature` objects named after source shader versions.

## Lighting mode normalization

`AtlasLightingMode` describes algorithms, not a source shader identity. For example:

| Input | Source mode | Unified mode |
|---|---|---|
| Unity Standard | Metallic | `StandardMetallic` |
| Unity Standard Specular | Specular | `StandardSpecular` |
| VRChat Mobile Toon Lit | Built-in directional/SH vertex lighting | `MobileVertexToon` |
| VRChat Mobile Toon Standard | Shadow ramp | `ToonRamp` |
| Poiyomi 7.x | Enum 2: Wrapped | `Wrapped` |
| Poiyomi 8/9/10 | Enum 2: Wrapped | `Wrapped` |
| Poiyomi 7.x | Enum 4: Flat | `Flat` |
| Poiyomi 8/9/10 | Enum 5: Flat | `Flat` |
| Poiyomi 8/9/10 | Enum 1: Multilayer Math | `ToonMultilayer` |

The Poiyomi source enum values are **only present in its input parser**.
`Lighting` has one set of shared properties and an optional semantic parameter
collection for less-common lighting controls. A new input mode does not add a new
lighting configuration type.

## Matcap blend modes

`AtlasMatcapLayer` supports both an ordinary `Blend` enum (Mobile Toon Standard's
Add or Multiply) and `BlendWeights`, where Replace, Multiply, Add, Mixed, Screen,
and UnlitAdd are independently weighted (Poiyomi 7–10). Blend weights are not
collapsed into a single mode when multiple weights are active. `AtlasMatcapUVMode`
represents actual projection variants, including Poiyomi's UTS style, top pinch,
double-sided, gradient, and light direction.

A numeric source enum is translated into a canonical mode in the input parser.
Not all Poiyomi matcap compositing details are fully proven to be equivalent to
these operations, so the Poiyomi matcap's `Quality` is currently `Approximate`.
A generator must not present it as pixel-perfect reproduction without validation.

## Scalar conventions

- `AtlasScalarInput` holds `Constant`, a sampled channel, and inversion flags.
- `CombineMode=Multiply` calculates `Constant * Sample` (or just `Constant` without a map).
- `CombineMode=StrengthAroundOne` calculates `1 - (1 - Sample) * Constant` and is
  appropriate for Unity Standard's occlusion-strength convention.
- Smoothness is canonicalized to roughness through `InvertResult`.
- UV mapping, channel selection, blend factors, masks and texture sample mode are typed;
  source shader property identifiers never leave the parsers.
- `Enabled=false` is different from a missing texture; disabled layers can remain represented.

## Input parser status (since 0.24.0)

The requested parser classes are implemented and now populate significantly more
of the unified union. **This is not a claim of 100% verified semantic coverage.**

| Input parser | Currently handled feature groups | Remaining gaps |
|---|---|---|
| Unity Standard | Core PBR, metallic/smoothness, specular/reflection toggles, detail UV/masks, parallax, alpha/render states, emission | Verify all keyword/packed-channel branches with fixtures |
| Standard Specular | Same shared Standard reader with specular workflow | Verify smoothness specular-map channel variations |
| VRChat Mobile Standard Lite | PBR, detail, spec AA, lightmap selection, bicubic, toggles | Verify feature-keyword activation in compiled variants |
| VRChat Mobile Toon Lit | Base texture, vertex color, fixed lighting mode | Validate fixed directional/SH behavior in output writers |
| VRChat Mobile Toon Standard | Toon ramp, specular controls, matcap, rim, emission, detail blending, color masks, hue, AudioLink | Shader's specialized AudioLink blends and conditional feature activation need visual verification |
| Mochie Uber | Shading on/off, blending, alpha mask, four surface workflows, parallax, toon shadows, detail, emission, matcaps, rims, subsurface, filtering, team colours, light volumes, feature masks | Outline, refraction, iridescence, flipbooks, UV distortion, vertex manipulation, AudioLink and map filters are reported, not mapped. Uber X is unsupported. Mochie Standard is no longer parsed |
| Poiyomi Toon 7–11 and Pro 10–11 | Version-aware lighting modes, shadow bands, matcap weighted blends, normals, emission layers, masks, clearcoat, color adjustments, rim, SSS, fur (Lil Fur strands, Pro shells), 11.x PBR lighting | Most procedural, animated and special effects, and global-mask routing, aren't mapped yet. Pro-only effects and variants, and 11.x mask levels, are reported, not mapped |
| Fallback | Conservative conventional texture, scalar, tint, mask, specular and matcap recognition | Arbitrary shaders cannot be automatically guaranteed; unresolved features are reported. No tests yet |

## Parser responsibilities and testing

1. Identify source shader and version (Poiyomi reads `shader_master_label` from locked
   or unlocked shaders if present).
2. Resolve every source property that affects the look into unified features: represent it,
   or fold it into something simpler (see *Coverage and fidelity*). Properties with no effect
   in that material, such as settings of disabled features, are ignored.
3. Translate modes and conventions where required. Preserve shared 1:1 values.
4. Return `Issues` for unresolved or partial behavior. **Never claim correctness from
   merely referencing a property name.**
5. Verify with source shader/material fixtures. Input semantic coverage and output
   reproduction coverage must be tracked separately.

No output material generation is enabled in this release. The Texture Wrapper
submesh bakes and original NDMF/AAO integration remain in place.


## Shared global masks (since 0.25.0)

`GlobalMaskResources` contains reusable texture resources with UV mapping and optional
per-channel split scale/offset/panning. `GlobalMaskBindings` references a resource
index, RGBA channel, semantic target, layer index and blend operation. These types
are shader-independent, so output generators do not need Poiyomi property IDs.

The Poiyomi parser reads four texture resources and maps the 0=Off, 1–16=RGBA
selector to (resource, channel). Blend values 0–7 become Replace, Subtract,
Multiply, Divide, Min, Max, Average and Add. The core color adjustments, alpha,
detail and secondary-normal-strength bindings are supported, with provisional
emission/matcap selectors only when matching properties exist.

**Not complete Poiyomi parity:** other feature-specific routing, generated mask
modifiers, vertex-color injection, view/distance/mirror conditions and procedural
mask writers are not yet normalized. These must be added with semantic targets
before claiming fully equivalent results. No output material is generated yet.


## Poiyomi inspector-based mappings (since 0.26.0)

The shared material now stores decal positioning/opacity/emission, render depth/color masks, secondary normal mask metadata, LTCGI controls, and animation **target declarations** alongside its previous feature objects. The advanced Poiyomi reader maps source fields into semantic features for alpha-map channel weights, detailed light/shadow settings, rim options, reflections/clearcoat, environmental rim, backlight, glitter, proximity color, AudioLink and Color Adjust. Unknown enum choices are explicitly flagged instead of copying input-shader integers. Missing or hidden properties are skipped so the parser can also read previous Toon versions.

**Limits:** Shader property declarations and screenshots do not provide animation clips or curves. `AnimatableParameters` denotes semantic targets only; the NDMF animation-binding pass must resolve actual `.anim` curves. Source version differences and advanced shader math (mask combination, distance modes, glitter shape sampling, nonlinear shadows) still require fidelity tests. Features exposed as `AtlasSemanticFeature` are part of the normalized union and do not contain source shader identifiers. Output material generation remains disabled.
