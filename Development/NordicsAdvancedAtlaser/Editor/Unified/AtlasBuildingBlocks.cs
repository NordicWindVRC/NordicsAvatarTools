using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Building blocks the features are made of (section 1). Units follow section 1.0: colours are
    // linear, angles in degrees, strengths and opacities 0–1, time in seconds.

    public enum AtlasTextureInterpretation
    {
        Color,
        // Numbers, not colours: masks, metallic, roughness.
        Data,
        TangentNormal,
        // A tangent-space direction map, composited like a normal map (fur).
        Direction
    }

    public enum AtlasTextureSpace
    {
        UV0, UV1, UV2, UV3, ObjectPosition, WorldPosition, Panosphere, Polar,
        // View-space normal, as matcaps use.
        View,
        Screen, Triplanar
    }

    public enum AtlasTextureSampling
    {
        Default,
        // Hides visible tiling.
        Stochastic,
        Supersampled
    }

    public enum AtlasChannel { Red, Green, Blue, Alpha, Luminance }

    // 1.1: a texture and how it is sampled.
    public sealed class AtlasTextureReference
    {
        public Texture Texture;
        public AtlasTextureInterpretation Interpretation = AtlasTextureInterpretation.Color;
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        public Vector2 Tiling = Vector2.one;
        public Vector2 Offset = Vector2.zero;
        // Degrees, around the tile centre.
        public float Rotation;
        // UV units per second.
        public Vector2 Scroll;
        // Degrees per second.
        public float Spin;
        public AtlasTextureSampling Sampling = AtlasTextureSampling.Default;
    }

    public enum AtlasScalarCombine
    {
        // constant × channel
        Multiply,
        // 1 − (1 − channel) × constant: the constant is a strength, as for occlusion.
        StrengthAroundOne
    }

    // 1.2: a number, optionally driven by one channel of a texture.
    public sealed class AtlasScalarInput
    {
        public float Constant;
        public AtlasTextureReference Map;
        public AtlasChannel Channel = AtlasChannel.Red;
        // Inverts the channel before it is combined.
        public bool InvertSample;
        public AtlasScalarCombine Combine = AtlasScalarCombine.Multiply;
        // Inverts the result; for parsers turning smoothness into roughness.
        public bool InvertResult;

        public AtlasScalarInput() { }
        public AtlasScalarInput(float constant) { Constant = constant; }

        // The value for a sampled channel value; without a map the sample is ignored.
        public float Evaluate(float sample = 1f)
        {
            float channel = InvertSample ? 1f - sample : sample;
            float value;
            if (Map == null) value = Combine == AtlasScalarCombine.StrengthAroundOne ? 1f : Constant;
            else if (Combine == AtlasScalarCombine.StrengthAroundOne) value = 1f - (1f - channel) * Constant;
            else value = Constant * channel;
            return InvertResult ? 1f - value : value;
        }
    }

    // 1.3: colour × texture. Linear; values above 1 are allowed (HDR).
    public sealed class AtlasColorInput
    {
        public Color Color = Color.white;
        public AtlasTextureReference Map;
    }

    // 1.4: where something applies, 0–1. Without a map it applies everywhere.
    public sealed class AtlasMask
    {
        public AtlasTextureReference Map;
        public AtlasChannel Channel = AtlasChannel.Red;
        public bool Invert;
        // lerp(1, value, strength)
        public float Strength = 1f;

        public float Evaluate(float sample = 1f)
        {
            float value = Map == null ? 1f : Invert ? 1f - sample : sample;
            return Mathf.Lerp(1f, value, Strength);
        }
    }

    // 1.7: how a layer's value l combines with what's below b, with o = opacity × mask.
    public enum AtlasBlend
    {
        // lerp(b, l, o)
        Replace,
        // b × lerp(1, l, o)
        Multiply,
        // b × lerp(1, 2l, o) in gamma terms; factor 4.595 in linear space, as Unity's detail maps.
        MultiplyDouble,
        // b + l × o
        Add,
        // max(b − l × o, 0)
        Subtract,
        // lerp(b, 1 − (1 − b)(1 − l), o)
        Screen,
        // lerp(b, overlay(b, l), o)
        Overlay,
        // lerp(b, max(b, l), o)
        Lighten,
        // lerp(b, min(b, l), o)
        Darken,
        // lerp(b, b + b × l, o)
        Mixed,
        // lerp(b, luminance(b) × l, o)
        TintLuminance,
        // lerp(b, min(luminance(b) + l, 1), o)
        AddToLuminance
    }

    // 1.8: the evaluation stage a layer acts at.
    public enum AtlasPlacement
    {
        // S1: changes the surface inputs, before lighting.
        Surface,
        // S3: needs the light.
        Lit,
        // S4: added to the lit result, ignoring shadows.
        Overlay,
        // S5: glows, never shadowed.
        Emission
    }
}
