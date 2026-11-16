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
        Direction,
        // A multi-channel signed distance field: white, with alpha saturate((median(r, g, b) − 0.5) /
        // the median's change across a pixel), so shapes stay sharp at any size.
        Msdf
    }

    public enum AtlasTextureSpace
    {
        UV0, UV1, UV2, UV3, ObjectPosition, WorldPosition, Panosphere, Polar,
        // View-space normal, as matcaps use.
        View,
        Screen,
        // u = v = 0.5 + 0.5 × abs(N·V) (the view from between the eyes): rings from the centre
        // of the view outward; Linear: the angle instead, 0.5 + 0.5 × (1 − angle / 90°).
        FacingGradient, FacingGradientLinear
    }

    public enum AtlasTextureSampling
    {
        Default,
        // Hides visible tiling.
        Stochastic,
        // Sharp texels with anti-aliased edges, for pixel art.
        Pixel
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
        // Stochastic: the tile grid's density (1: the standard cell size).
        public float StochasticDensity = 1f;
        // A decal: placed on the wrapped UV; null: tiled over the surface.
        public AtlasDecal Decal;
        // Animation frames; null: one image.
        public AtlasFrames Frames;
        // A UV offset, possibly shared with other references; null: none.
        public AtlasUVDistortion Distortion;
    }

    // 1.1: placement of a decal on the UV, which wraps (0–1) first.
    public enum AtlasDecalSymmetry
    {
        None,
        // u > 0.5 mirrors onto the left: u = abs(u − 0.5) + 0.5.
        Copy,
        // The left half shifts onto the right: u + 0.5 where u < 0.5.
        Flip
    }

    public enum AtlasDecalMirror
    {
        None,
        // On the mesh's mirrored (right-hand) side, u = 1 − u.
        Flip,
        LeftOnly,
        RightOnly,
        // Right side only, flipped.
        FlipRightOnly
    }

    public sealed class AtlasDecal
    {
        // The decal's centre, in UV.
        public Vector2 Centre = new Vector2(0.5f, 0.5f);
        public Vector2 Scale = Vector2.one;
        // Degrees, and degrees per second.
        public float Rotation;
        public float Spin;
        // Nothing is sampled outside the placed area.
        public bool ClipOutside = true;
        public AtlasDecalSymmetry Symmetry = AtlasDecalSymmetry.None;
        public AtlasDecalMirror Mirror = AtlasDecalMirror.None;
        // Parallax depth: the decal appears this far behind (positive) the surface.
        public float Depth;
        // With ClipOutside, the edge is anti-aliased over a pixel instead of cut.
        public bool SmoothBorder;
        // In mirrors, u = 1 − u (text reads the right way round).
        public bool FlipInMirror;
    }

    public enum AtlasFrameSource
    {
        // Columns × rows of frames, read left to right, top to bottom.
        SpriteSheet,
        // One frame per slice of a texture array.
        TextureArray
    }

    // 1.1: an animation in a texture. Frame f = First + (t × Rate + Offset) mod Count, or
    // FixedFrame when Fixed; with Crossfade, frame floor(f) blends toward the next one of the range
    // by smoothstep(CrossfadeRange.x, CrossfadeRange.y, frac(f)).
    public sealed class AtlasFrames
    {
        public AtlasFrameSource Source = AtlasFrameSource.SpriteSheet;
        public int Columns = 1;
        public int Rows = 1;
        // Frames per second, unless a fixed frame is shown.
        public float Rate = 30f;
        // Frames added before wrapping.
        public float Offset;
        // The range played; Count 0: from First to the last frame.
        public int First;
        public int Count;
        public bool Fixed;
        // A frame index (fractional with Crossfade).
        public float FixedFrame;
        public bool Crossfade;
        public Vector2 CrossfadeRange = new Vector2(0.75f, 1f);
        // Sprite sheets: the part of the texture the grid covers (from the top left), and an inset
        // pulling each frame's samples toward its centre (0–1).
        public Vector2 Area = Vector2.one;
        public float Inset;
    }

    // How the screen texture space is laid out: square, centred on each eye's optical centre (so
    // it fuses in stereo); with ObjectRelative, sized to WorldSize world units at the object's
    // distance and anchored to its origin, so it keeps its size on the object.
    public sealed class AtlasScreenSpace
    {
        public bool ObjectRelative;
        public float WorldSize = 1f;
    }

    public enum AtlasClock { Local, Network }

    public enum AtlasTriplanarSource { Mesh, Depth }
    public enum AtlasTriplanarBlend { Normal, Sharp, Asymmetric }

    // 1.1: the whole material projected three ways (Poiyomi Pro's Triplanar Projection). UV0
    // becomes three planar projections of a position p ÷ lerp(1, the object's scale,
    // ScaleWithObject): (p.z × sign n.x, p.y), (p.x × sign n.y, p.z) and (−p.x × sign n.z, p.y),
    // + 0.5. Mesh: p is the object-space position and n the object normal. Depth: p is the scene
    // behind, read from the depth texture relative to the object's origin, and n its normal
    // (from depth samples DepthBlur pixels apart), so the material lands on its surroundings,
    // within MaxSize of the origin (faded over the last EdgeFade.y − EdgeFade.x) and while the
    // camera is nearer than CameraFade.y (faded from CameraFade.x); nothing without a depth
    // texture. The surface is evaluated once per projection and blended by weights from n
    // (Normal: |n| − Sharpness, normalised; Sharp: |n|⁶, normalised; Asymmetric: the sides where
    // the horizontal normal passes 0.67, the top where |n.y| passes 0.675): colour, alpha, normals,
    // emission, additive light and the mask bus. Then base = lerp(the first projection's, the
    // blend, mask), alpha = the blended alpha × mask × the fades, and emission and lighting ×
    // that alpha. The mask is Mask's value (sampled per projection and blended) × MaskStrength.
    public sealed class AtlasTriplanar
    {
        public bool Enabled;
        public AtlasTriplanarSource Source = AtlasTriplanarSource.Depth;
        public AtlasTriplanarBlend Blend = AtlasTriplanarBlend.Normal;
        public float Sharpness = 0.2f;
        public float ScaleWithObject = 1f;
        public AtlasMask Mask;
        public float MaskStrength = 1f;
        public float MaxSize = 1f;
        public Vector2 EdgeFade = new Vector2(0f, 0.5f);
        public Vector2 CameraFade = new Vector2(10f, 15f);
        public float DepthBlur = 1f;
    }

    public enum AtlasDistortionSource { Noise, NormalMap }

    // 1.1: a UV offset (in UV units) shared by the references it shifts.
    public sealed class AtlasUVDistortion
    {
        public AtlasDistortionSource Source = AtlasDistortionSource.Noise;
        // Noise: simplex.
        public Vector2 NoiseScale = Vector2.one;
        public float NoiseSpeed = 0.5f;
        public int NoiseOctaves = 1;
        // NormalMap: its xy is the offset; a second map adds its own (× Strength2).
        public AtlasTextureReference NormalMap;
        public float Strength = 1f;
        public AtlasTextureReference NormalMap2;
        public float Strength2;
        // Added to the offset (before the mask).
        public Vector2 ConstantOffset;
        // Null: everywhere.
        public AtlasMask Mask;
    }

    // 1.2: v = lerp(min, max, v); v = saturate(lerp(0.5, v, contrast)); v += saturate(v × intensity);
    // v = saturate(v + lightness).
    public sealed class AtlasScalarRemap
    {
        public float OutputMin;
        public float OutputMax = 1f;
        public float Contrast = 1f;
        public float Intensity;
        public float Lightness;

        public float Apply(float v)
        {
            v = Mathf.LerpUnclamped(OutputMin, OutputMax, v);
            v = Mathf.Clamp01(Mathf.LerpUnclamped(0.5f, v, Contrast));
            v += Mathf.Clamp01(v * Intensity);
            return Mathf.Clamp01(v + Lightness);
        }
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
        // Applied to the sampled value (constant × channel; for StrengthAroundOne the channel
        // before the strength), before InvertResult; null: none.
        public AtlasScalarRemap Remap;
        // A mask bus channel (0–15) combined with the value by BusBlend; −1: none.
        public int Bus = -1;
        public AtlasMaskBlend BusBlend = AtlasMaskBlend.Multiply;

        public AtlasScalarInput() { }
        public AtlasScalarInput(float constant) { Constant = constant; }

        // The value for a sampled channel value; without a map the sample is ignored.
        public float Evaluate(float sample = 1f)
        {
            float channel = InvertSample ? 1f - sample : sample;
            bool aroundOne = Combine == AtlasScalarCombine.StrengthAroundOne;
            float value = Map == null ? (aroundOne ? 1f : Constant) : (aroundOne ? channel : Constant * channel);
            if (Remap != null) value = Remap.Apply(value);
            if (aroundOne && Map != null) value = 1f - (1f - value) * Constant;
            return InvertResult ? 1f - value : value;
        }
    }

    // 1.3: colour × texture. Linear; values above 1 are allowed (HDR).
    public sealed class AtlasColorInput
    {
        public Color Color = Color.white;
        public AtlasTextureReference Map;
        // The colour comes from AudioLink: ColorChord colours 0–3, or the world's theme colours 4–7;
        // without AudioLink, Color. −1: none.
        public int AudioTheme = -1;
    }

    // 1.4: where something applies, 0–1. Without a map it applies everywhere.
    // How a value combines with a mask bus channel (1.5), in Poiyomi's order.
    public enum AtlasMaskBlend { Replace, Subtract, Multiply, Divide, Min, Max, Average, Add }

    // 1.4: a curve on a mask value v: t = saturate((v − InputMin) / (InputMax − InputMin))
    // (step(InputMax, v) for an empty range), t = pow(t, log 0.5 / log Midpoint) (Midpoint clamped
    // to 0.01–0.99, none at 0.5), 1 − t when Invert, then lerp(OutputMin, OutputMax, t).
    public sealed class AtlasMaskLevels
    {
        public float InputMin;
        public float InputMax = 1f;
        public float Midpoint = 0.5f;
        public float OutputMin;
        public float OutputMax = 1f;
        public bool Invert;

        public float Evaluate(float v)
        {
            float range = InputMax - InputMin;
            float t = range > 0.00001f ? Mathf.Clamp01((v - InputMin) / range) : (v >= InputMax ? 1f : 0f);
            if (Midpoint != 0.5f) t = Mathf.Pow(t, Mathf.Log(0.5f) / Mathf.Log(Mathf.Clamp(Midpoint, 0.01f, 0.99f)));
            if (Invert) t = 1f - t;
            return Mathf.Lerp(OutputMin, OutputMax, t);
        }
    }

    // The value: the map's channel (1 without a map; the vertex colour's channel with VertexColor),
    // through Levels, × a vertex colour channel (TimesVertexColor), inverted, lerp(1, v, Strength),
    // then blended with a bus channel.
    public sealed class AtlasMask
    {
        public AtlasTextureReference Map;
        // A mask bus channel (0–15) combined with the mask's own value (after its invert and
        // strength; 1 without a map) by BusBlend; −1: none.
        public int Bus = -1;
        public AtlasMaskBlend BusBlend = AtlasMaskBlend.Multiply;
        // The channel is read from the vertex colour instead of a map.
        public bool VertexColor;
        public AtlasChannel Channel = AtlasChannel.Red;
        // Null: the value as it is.
        public AtlasMaskLevels Levels;
        public bool TimesVertexColor;
        public AtlasChannel VertexChannel = AtlasChannel.Red;
        public bool Invert;
        // lerp(1, value, strength)
        public float Strength = 1f;

        // Without vertex colours: the map's sample through the levels and the invert.
        public float Evaluate(float sample = 1f)
        {
            if (Map == null) return 1f;
            float value = Levels != null ? Levels.Evaluate(sample) : sample;
            if (Invert) value = 1f - value;
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
