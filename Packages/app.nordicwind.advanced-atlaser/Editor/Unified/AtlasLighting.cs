namespace NordicWind.AdvancedAtlaser.Editor
{
    // 3.1: how the surface is shaded.
    public enum AtlasLightingFamily
    {
        // The base color as is.
        Unlit,
        // Uniform light, no shading across the surface: base × (main light × shadow + ambient).
        Flat,
        // A shading term mapped through a ramp to brightness and shade color.
        RampToon,
        // Energy-conserving diffuse plus the surface response.
        PhysicallyBased
    }

    // The physically based family's specular and energy conventions.
    public enum AtlasPhysicalModel
    {
        // Unity Standard's.
        Standard,
        // Filament-style: GGX with height-correlated Smith visibility, multi-scattering energy
        // compensation from a DFG lookup, diffuse × (1 − metallic) × (1 − the reflected energy),
        // specular occlusion from N·V and occlusion, anisotropy as α(1 ± a) along a rotated tangent,
        // and Charlie sheen (Poiyomi 11's PBR lighting type).
        Calibrated
    }

    public enum AtlasDiffuseModel
    {
        Lambert,
        // Burley's diffuse: edges darken or brighten with roughness, as Unity Standard on PC.
        Disney,
        // Pre-integrated skin: a color lookup by half-Lambert and scattering replaces N·L (Skin).
        PreintegratedSkin
    }

    // Where the direct and ambient light colors come from.
    public enum AtlasLightColorModel
    {
        // Direct from the main light, ambient from the probes.
        Separate,
        // The probes' dominant light joins the direct light, keeping the total's luminance.
        MergedProbes,
        // Ambient = the brighter of the probes' average and their downward color, at least a
        // floor of 0.05, times the floor intensity; direct = at least the ambient.
        Floored,
        // The probes split into a direct and an ambient part along their dominant direction,
        // the direct part added to the main light.
        SplitProbes
    }

    // Where the main light's direction comes from.
    public enum AtlasLightDirectionModel
    {
        // The main light's.
        Light,
        // The main light's, weighted by its luminance, plus the probes' dominant direction.
        ProbeBlended,
        // The probes' dominant direction, with the main light, as SplitProbes splits them.
        SplitProbes,
        // The main light's; without one, between the view's forward and up directions.
        ViewFallback,
        // From the surface toward the camera, turned by ViewYaw and ViewPitch (degrees).
        View,
        // LightDirection, in world or object space.
        Fixed
    }

    // How additional (point and spot) lights shade.
    public enum AtlasAdditionalLightShading
    {
        // As the main light.
        SameAsMain,
        // N·L.
        Lambert,
        // smoothstep(GradientEnd, GradientStart, 1 − half-Lambert) between the light and its passthrough.
        Toon
    }

    public enum AtlasAmbientModel
    {
        // The ambient light's average color, the same in every direction.
        Average,
        // Ambient light sampled along the normal.
        Directional
    }

    // 3: lighting. Each family's own parameters are added as parsers need them.
    public sealed class AtlasLighting
    {
        public AtlasLightingFamily Family = AtlasLightingFamily.PhysicallyBased;
        public AtlasPhysicalModel PhysicalModel = AtlasPhysicalModel.Standard;
        // Every light color (direct, ambient, additional) × LightMultiplier + LightAddition, at
        // least 0.
        public float LightMultiplier = 1f;
        public float LightAddition;
        // Flat: features reading the shading's light map see smoothstep(0.4, 0.6, the half-Lambert
        // N·L; the vertex normal's for additional lights) instead of 1.
        public bool FlatLightMap;
        // 3.2: the ramp toon family's ramp.
        public readonly AtlasRampToon Ramp = new AtlasRampToon();
        // 3.3: the physically based family's diffuse; Disney blends from Lambert by its amount.
        public AtlasDiffuseModel Diffuse = AtlasDiffuseModel.Lambert;
        public float DisneyAmount = 1f;
        // 3.4: shared parameters. Directional blends from the average by its amount.
        public AtlasAmbientModel Ambient = AtlasAmbientModel.Directional;
        public float DirectionalAmount = 1f;
        public AtlasLightColorModel ColorModel = AtlasLightColorModel.Separate;
        // Floored: scales the ambient light and its floor.
        public float FloorIntensity = 1f;
        public AtlasLightDirectionModel Direction = AtlasLightDirectionModel.Light;
        // Fixed: the direction, in object space when DirectionLocal.
        public UnityEngine.Vector3 LightDirection = new UnityEngine.Vector3(0f, 0.75f, 1f);
        public bool DirectionLocal;
        public float ViewYaw, ViewPitch;
        // Replaces the direct light's color.
        public bool FixedLightColor;
        public UnityEngine.Color LightColor = UnityEngine.Color.white;
        // 0–1: how much the main light's shadow darkens, lerp(1, shadow, value).
        public float ReceiveShadows = 1f;
        // 0–1: blurs the received shadow.
        public float ShadowSoftness;
        // Smooths the received shadow's stair-stepped edge.
        public bool CleanShadows;
        // 0–1: the light toward its luminance (gray).
        public float Monochrome;
        public readonly AtlasLightContributions Contributions = new AtlasLightContributions();
        // Physically based: the light × lerp(1, term, ShadowStrength) × lerp(1, ShadowTint,
        // (1 − term) × ShadowStrength), the term as the ramp's (3.2). Skin uses both its own way.
        public float ShadowStrength;
        public UnityEngine.Color ShadowTint = UnityEngine.Color.white;
        // 3.3: the pre-integrated skin diffuse's parameters.
        public readonly AtlasSkin Skin = new AtlasSkin();
        // 3.3: cloth (sheen) highlights where its mask is set; null: none.
        public AtlasCloth Cloth;
        // Clamps the light (direct + ambient) per channel to the ceiling.
        public bool LimitBrightness;
        public float BrightnessCeiling = 1f;
        // 0–1: the direct light's limit blends from per channel to scaling by luminance, keeping the hue.
        public float LimitByLuminance;
        // The limit scales the color so its brightest channel is at the ceiling, keeping the hue
        // (LimitByLuminance then unused).
        public bool LimitKeepsHue;
        // 0–1: raises the light's luminance (direct and ambient) to at least this, keeping the hue.
        public float MinimumLight;
        // 0–1: base × value is added after lighting, like emission.
        public float MinimumBrightness;
        // 3.5
        public readonly AtlasLightVolumes LightVolumes = new AtlasLightVolumes();
        // 3.5
        public readonly AtlasLtcgi Ltcgi = new AtlasLtcgi();
        public readonly AtlasAdditionalLights AdditionalLights = new AtlasAdditionalLights();
    }

    // 3.4: how per-pixel and vertex lights other than the main light behave.
    // How each additional light joins the surface: added, or the brighter of the two per channel.
    public enum AtlasLightCombine { Add, Max }

    public sealed class AtlasAdditionalLights
    {
        // Poiyomi's default is Max (its add pass blend operation), with the alpha × MaxAlphaBoost
        // (saturated) in that pass.
        public AtlasLightCombine Combine = AtlasLightCombine.Add;
        public float MaxAlphaBoost = 1f;
        public AtlasAdditionalLightShading Shading = AtlasAdditionalLightShading.SameAsMain;
        public float GradientStart, GradientEnd = 0.5f;
        // Clamps each light's luminance to the ceiling (its brightest channel with LimitKeepsHue).
        public bool LimitBrightness;
        public float BrightnessCeiling = 1f;
        public bool LimitKeepsHue;
        // 0–1, as the main light's.
        public float ReceiveShadows = 1f;
        public float Monochrome;
        // 0–1: the light's share that lights the surface regardless of direction, like ambient.
        public float Passthrough;
        // Remaps the distance falloff by a four-key curve (values at 0, ⅓, ⅔, 1); linear by default.
        public UnityEngine.Vector4 Falloff = new UnityEngine.Vector4(0f, 1f / 3f, 2f / 3f, 1f);
        // Directional lights beyond the main one.
        public bool Directional = true;
        public bool VertexLightsInMirrors = true;
    }

    // 3.3: pre-integrated skin. The shadow side S = lerp(A × ShadowTint, ShadowTint, ConstantShadow)
    // × occlusion; L = lerp(D, min(S + LUT × D × shadow, D), Strength).
    public sealed class AtlasSkin
    {
        // Sampled at (half-Lambert, scattering × lerp(luminance(light), 1, Stability)); color.
        public AtlasTextureReference Lut;
        // scattering = saturate((1 − thickness)^ThicknessPower × Scattering × 2).
        public float Scattering = 1f;
        // Red channel; null: thickness 0 everywhere.
        public AtlasMask Thickness;
        public float ThicknessPower = 1f;
        // The light's absorption by depth: base × exp((1 − scattering) × Absorption).
        public UnityEngine.Vector3 Absorption = new UnityEngine.Vector3(-8f, -40f, -64f);
        public float Stability;
        // 0–1: the diffuse uses a normal blended toward the mesh normal by this.
        public float NormalSoftness;
        public float ConstantShadow;
        public float Strength = 1f;
    }

    // 3.3: cloth. The specular is sheen (Charlie distribution, Neubelt visibility) where the mask
    // is set, GGX elsewhere; the metallic part loses its diffuse.
    public sealed class AtlasCloth
    {
        // Null: cloth everywhere.
        public AtlasMask Mask;
        // Blends the two by the mask instead of switching at 0.5.
        public bool Blend;
    }

    // 3.2: a face shaded by a signed-distance texture instead of N·L.
    public enum AtlasSdfStyle
    {
        // b = smoothstep(a − Blur × 0.1, a + Blur × 0.1, sdf), a = 1 − (horizontal L·Forward × 0.5 + 0.5);
        // the texture mirrors (u negated) when the light comes from the Left side.
        Threshold,
        // term = saturate(L·Forward × 0.5 + sdf × 0.5 + 0.25), L and Forward with y × VerticalBlend;
        // sdf from red, or green when the light comes from the other side than Left.
        Blended
    }

    public sealed class AtlasSdfFace
    {
        public AtlasSdfStyle Style = AtlasSdfStyle.Threshold;
        public AtlasTextureReference Map;
        // Object space.
        public UnityEngine.Vector3 Forward = UnityEngine.Vector3.forward;
        public UnityEngine.Vector3 Left = UnityEngine.Vector3.left;
        public float Blur = 0.1f;
        public float VerticalBlend = 1f;
        // Blended: samples a blurrier mip, 0–1.
        public float Softness;
        // The map's blue blends the term toward half-Lambert; its alpha scales the shadow strength.
        public bool MaskChannels;
    }

    // 3.5: LTCGI, area lights from video screens in worlds that have them.
    public sealed class AtlasLtcgi
    {
        public bool Enabled;
        // Linear.
        public UnityEngine.Color DiffuseTint = UnityEngine.Color.white;
        public UnityEngine.Color SpecularTint = UnityEngine.Color.white;
        public float DiffuseStrength = 1f;
        public float SpecularStrength = 1f;
        // Uses the surface response's metallic and roughness; otherwise these.
        public bool UseSurfaceResponse = true;
        public float Metallic;
        public float Roughness = 1f;
        // Reflections' exposure occlusion applies.
        public bool ExposureOcclusion;
    }

    // 3.5: VRChat light volumes, which replace the light probes where the world has them.
    public sealed class AtlasLightVolumes
    {
        public bool Enabled;
        // 0–1: blends from the probes to the volumes.
        public float Strength = 1f;
        public bool Specular;
        public float SpecularStrength = 1f;
        // Offsets the sample point along the normal.
        public float Bias;
        // Only the first this many volume lights add highlights; 0: all.
        public int SpecularMaxLights;
    }

    // A ramp described by numbers: piecewise linear between keys.
    public struct AtlasRampKey
    {
        // 0–1 along the shading term.
        public float Position;
        // Linear.
        public UnityEngine.Color Color;
        public AtlasRampKey(float position, UnityEngine.Color color) { Position = position; Color = color; }
    }

    public enum AtlasRampConditions { Always, RealtimeLightOnly, BakedLightOnly }

    public enum AtlasShadingTerm
    {
        // N·L × 0.5 + 0.5, times the received shadow.
        HalfLambert,
        // saturate(N·L), times the received shadow.
        Lambert,
        // The received shadow alone.
        ShadowsOnly,
        // Where the point's light (half-Lambert × direct + ambient along the normal) sits between
        // the darkest and the brightest the light allows, by luminance, smoothstepped.
        RelativeBrightness,
        // Relative brightness of the wrapped light: direct × saturate(wrap(N·L)) + the ambient
        // wrapped alike. wrap(x) = (x + w) / ((1 + w)(1 + w × n)), w = min(Wrap, 1), n = WrapNormalization;
        // Wrap above 1 blends toward 1 − 0.85n.
        Wrapped,
        // The ramp's Sdf.
        SdfFace
    }

    // Names a ramp's shape, so a writer can use its own equivalent.
    public enum AtlasRampPreset { None, Flat, Realistic, RealisticSoft, RealisticVerySoft, TwoBands, ThreeBands, FourBands }

    // 3.2, one formula for every source; the lit color is base × L:
    //   b = lerp(1, min(1, ramp(term) × occlusion(direct) + lift), strength × strength mask)
    //   A = ambient, darkened by occlusion (indirect)
    //   D = direct + LitAmbient × A
    //   S = C ⊙ (ShadowDirect × direct × (RampScaledShadowDirect ? b : 1) × (DirectOccluded ? occlusion : 1)
    //           + ShadowAmbient × A) + UntintedAmbient × A
    //   C = ShadowTint ⊙ lerp(1, base, ShadowColorFromBase), varied by Bands
    //   L = lerp(S, D, b)
    public sealed class AtlasRampToon
    {
        public AtlasShadingTerm Term = AtlasShadingTerm.HalfLambert;
        // 0–1: back faces' term × (1 − it), so they fall into shadow.
        public float BackFaceShadow;
        // Sampled along u at the shading term; a color. Without a ramp or keys: white, fully lit.
        public AtlasTextureReference Ramp;
        // Instead of a texture: piecewise linear keys, in position order.
        public readonly System.Collections.Generic.List<AtlasRampKey> Keys = new System.Collections.Generic.List<AtlasRampKey>();
        // Instead of a texture or keys: bands (the first gives b, the others vary C in the shadow).
        public readonly System.Collections.Generic.List<AtlasShadeBand> Bands = new System.Collections.Generic.List<AtlasShadeBand>();
        // Bands: smoothstep edges instead of linear.
        public bool SmoothEdges;
        // Bands: each band builds its own half-Lambert term (with its normal strength and
        // received shadow). Term then stays the light map that the features reading it see
        // (specular and matcap light masks, the light's side for rims).
        public bool BandsOwnTerm;
        // Bands: the first band's edge widened by BorderRange brightens C toward white × BorderColor.
        public UnityEngine.Color BorderColor = UnityEngine.Color.black;
        public float BorderRange;
        // Bands: lifts C toward the lit side by the ambient light, 0–1.
        public float EnvironmentStrength;
        // Added to the term before the ramp texture.
        public float Offset;
        // The Wrapped term's parameters: 0–2, 0–1.
        public float Wrap, WrapNormalization;
        // The SdfFace term's face; null otherwise.
        public AtlasSdfFace Sdf;
        // Bands: the band colors replace the base color in the shadow instead of multiplying it.
        public bool BandsReplaceBase;
        // Tints the direct light on the lit side.
        public UnityEngine.Color LitTint = UnityEngine.Color.white;
        // lerp(1, ramp, strength × mask).
        public float RampStrength = 1f;
        public AtlasMask RampStrengthMask;
        // A painted mask multiplying N·L before the ramp, to draw shadows in.
        public AtlasMask TermMask;
        public AtlasRampConditions Conditions = AtlasRampConditions.Always;
        // The shadow becomes a dither pattern in proportion to its darkness.
        public bool Dithered;
        public AtlasRampPreset Preset = AtlasRampPreset.None;
        // 0–1
        public float ShadowLift;
        public readonly AtlasColorInput ShadowTint = new AtlasColorInput();
        // 0–1: C toward the base color.
        public float ShadowColorFromBase;
        // How much direct light the shadow side keeps, scaled by b or occlusion as set.
        public float ShadowDirect;
        public bool RampScaledShadowDirect;
        public bool DirectOccluded;
        public float ShadowAmbient = 1f;
        public float UntintedAmbient;
        public float LitAmbient = 1f;
    }

    // A band of a layered ramp. Edge e = (t − lo) / (hi − lo), lo = Border − Blur/2, hi = Border + Blur/2.
    public sealed class AtlasShadeBand
    {
        // The band's shade color: color × texture, the texture's RGB + ColorAddition (saturated)
        // and faded toward white by the texture's alpha.
        public readonly AtlasColorInput Color = new AtlasColorInput();
        public float ColorAddition;
        // The color (and map) times the base color.
        public bool ColorFromBase;
        // The first band's color applies when its alpha is above 0; the others blend in by it.
        public float Alpha = 1f;
        public float Border = 0.5f;
        public float Blur = 0.1f;
        // How much of the normal map the band's term uses, 0–1.
        public float NormalStrength = 1f;
        // How much the received shadow darkens the band's term, 0–1.
        public float ReceiveShadows;
    }

    // 3.4: how much each kind of light counts, 0–1.
    public sealed class AtlasLightContributions
    {
        // The main directional light.
        public float MainLight = 1f;
        // Other per-pixel lights.
        public float AdditionalLights = 1f;
        // Unity's four per-vertex lights.
        public float VertexLights = 1f;
        // Ambient and light probes, including the lights Unity folds into them.
        public float Ambient = 1f;
        // Without a real-time main light (most worlds are lit by probes alone): the ambient color
        // × AmbientAsMainLight stands in for the main light (0: none), and the ambient light counts
        // by AmbientWithoutMainLight instead of Ambient.
        public float AmbientAsMainLight;
        public float AmbientWithoutMainLight = 1f;
    }
}
