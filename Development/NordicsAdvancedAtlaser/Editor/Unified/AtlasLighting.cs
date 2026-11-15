namespace NordicWind.AdvancedAtlaser.Editor
{
    // 3.1: how the surface is shaded.
    public enum AtlasLightingFamily
    {
        // The base colour as is.
        Unlit,
        // Uniform light, no shading across the surface: base × (main light × shadow + ambient).
        Flat,
        // A shading term mapped through a ramp to brightness and shade colour.
        RampToon,
        // Energy-conserving diffuse plus the surface response.
        PhysicallyBased
    }

    public enum AtlasDiffuseModel
    {
        Lambert,
        // Burley's diffuse: edges darken or brighten with roughness, as Unity Standard on PC.
        Disney
    }

    public enum AtlasAmbientModel
    {
        // The ambient light's average colour, the same in every direction.
        Average,
        // Ambient light sampled along the normal.
        Directional
    }

    // 3: lighting. Each family's own parameters are added as parsers need them.
    public sealed class AtlasLighting
    {
        public AtlasLightingFamily Family = AtlasLightingFamily.PhysicallyBased;
        // 3.2: the ramp toon family's ramp.
        public readonly AtlasRampToon Ramp = new AtlasRampToon();
        // 3.3: the physically based family's diffuse; Disney blends from Lambert by its amount.
        public AtlasDiffuseModel Diffuse = AtlasDiffuseModel.Lambert;
        public float DisneyAmount = 1f;
        // 3.4: shared parameters. Directional blends from the average by its amount.
        public AtlasAmbientModel Ambient = AtlasAmbientModel.Directional;
        public float DirectionalAmount = 1f;
        // The main light's shadow.
        public bool ReceiveShadows = true;
        public readonly AtlasLightContributions Contributions = new AtlasLightContributions();
        // Clamps the light (direct + ambient) to 1 per channel.
        public bool LimitBrightness;
        // 0–1: base × value is added after lighting, like emission.
        public float MinimumBrightness;
        // 3.5
        public readonly AtlasLightVolumes LightVolumes = new AtlasLightVolumes();
        // Replaces the main light's direction with a fixed world direction.
        public bool FixedLightDirection;
        public UnityEngine.Vector3 LightDirection = new UnityEngine.Vector3(0f, 0.75f, 1f);
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
        HalfLambert
    }

    // Names a ramp's shape, so a writer can use its own equivalent.
    public enum AtlasRampPreset { None, Flat, Realistic, RealisticSoft, RealisticVerySoft, TwoBands, ThreeBands, FourBands }

    // 3.2: brightness = min(1, ramp(term) × occlusion + shadow lift);
    // light = lerp(base × shadow colour from base, 1, brightness) × light colour + ambient.
    public sealed class AtlasRampToon
    {
        public AtlasShadingTerm Term = AtlasShadingTerm.HalfLambert;
        // Sampled along u at the shading term; a colour. Without a ramp or keys: white, fully lit.
        public AtlasTextureReference Ramp;
        // Instead of a texture: piecewise linear keys, in position order.
        public readonly System.Collections.Generic.List<AtlasRampKey> Keys = new System.Collections.Generic.List<AtlasRampKey>();
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
        // 0–1: 0 black shadows, 1 shadows in the base colour.
        public float ShadowColorFromBase;
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
    }
}
