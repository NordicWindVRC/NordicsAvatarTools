# Nordic's Advanced Atlaser — Unified material contract

`AtlasUnifiedMaterial` is a **non-serialized normalized union** of the rendering features
supported by all input shaders. It is the only material description passed to output generators.
The source `Material`, shader, parser, and raw shader property names never enter the output job.

## Design invariant

- **One feature definition per meaning**, not per shader or shader version.
- `Lighting` is **one object** containing a `Mode` enum and the union of related properties;
  it is not a hierarchy of Poiyomi/Mochie/Mobile lighting configurations.
- Reuse a lighting mode when algorithms are compatible and missing parameters can be
  filled with **behaviorally equivalent defaults**. Use distinct modes only for real differences.
- Arrays represent real multiple layers: normals, emissions, decals, matcaps, rim effects,
  shadows, etc. A shader brand does not justify another array or configuration class.
- A common float/color/toggle maps 1:1. Translate numeric enums and conventions only when
  the source and target meanings differ. Where a feature has not been verified, record an issue.
- Output shader writers may support any subset of the unified features. **Input coverage and
  output coverage are different metrics.**
- No backward-compatibility configuration or migration code is retained during development.

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

The additional `AtlasSemanticFeature` mechanism is for uncommon **features** or newly
shared parameters; keys refer to semantics (e.g. `rippleDensity`), never to the source
shader's property identifier (e.g. `_RippleDensity`). Repeated instances must be
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
| Mochie Standard | Core PBR, packed maps, detail, sampling, masks, reflectance, SSS, color filters, rain/wind/puddles, AreaLit/LTCGI/light volumes | Some enum, swizzle and procedural semantics are recorded as approximate |
| Poiyomi Toon 7–10 | Version-aware lighting modes, shadow bands, matcap weighted blends, normals, emission layers, masks, clearcoat, color adjustments, rim, SSS | Thousands of advanced properties, animated effects and global-mask routing not yet completely interpreted |
| Fallback | Conservative conventional texture, scalar, tint, mask, specular and matcap recognition | Arbitrary shaders cannot be automatically guaranteed; unresolved features are reported |

## Parser responsibilities and testing

1. Identify source shader and version (Poiyomi reads `shader_master_label` from locked
   or unlocked shaders if present).
2. Map each rendering-relevant source property into its corresponding semantic feature.
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
