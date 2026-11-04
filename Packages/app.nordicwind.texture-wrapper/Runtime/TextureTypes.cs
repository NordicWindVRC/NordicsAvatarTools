using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.TextureWrapper
{
    public enum TextureDataType { Color, LinearData, Mask, NormalTangent, NormalObject, NormalWorld }
    public enum TextureSamplingMode { Nearest, Bilinear, Bicubic, Area }
    public enum TextureChannel { R, G, B, A, AverageRGB, Usage }
    public enum TextureBlendMode { Replace, Darken, Multiply, Lighten, Screen, Subtract, Add, Overlay, Mixed, Divide, Average, NormalLerp, NormalRNM, NormalWhiteout }
    public enum TextureMaskBlendMode { Replace, Subtract, Multiply, Divide, Min, Max, Average, Add }
    public enum TriangleRasterizationMode { Fast, Quality }
    public enum TextureCompression { None, Low, Normal, High }
    public enum TextureOutputChannels { Auto, RGB, RGBA }
    public enum ColorOperationCode { HueShift, HueReplace, Saturation, Brightness, Contrast, Gamma, Exposure, Tint, Multiply, Add, Invert, Clamp, Levels, Posterize, GradientMap, Chromatize, Colorize }
    public enum TextureColorSpace { LinearRGB, SRGB, HSV, OKLab }

    [Serializable]
    public sealed class TextureSettings
    {
        public TextureDataType DataType = TextureDataType.Color;
        public bool SRGB = true;
        public bool Mipmaps = true;
        public FilterMode FilterMode = FilterMode.Bilinear;
        public TextureWrapMode WrapMode = TextureWrapMode.Repeat;
        public int AnisoLevel = 1;
        public TextureCompression Compression = TextureCompression.Normal;
        public bool Crunch = false;
        public bool AlphaIsTransparency = false;
        public bool PreserveAlpha = true; // Legacy option; OutputChannels determines exported alpha.
        public TextureOutputChannels OutputChannels = TextureOutputChannels.Auto;
        public bool StreamingMipmaps = false;
        public int MipmapLimitGroup = 0;
        public TextureSettings Clone() => (TextureSettings)MemberwiseClone();
        public bool IsNormal => DataType == TextureDataType.NormalTangent || DataType == TextureDataType.NormalObject || DataType == TextureDataType.NormalWorld;
        internal string Fingerprint() => string.Join("|", (int)DataType, SRGB, Mipmaps, (int)FilterMode, (int)WrapMode, AnisoLevel, (int)Compression, Crunch, AlphaIsTransparency, PreserveAlpha, (int)OutputChannels, StreamingMipmaps, MipmapLimitGroup);
        public void Validate()
        {
            if (AnisoLevel < 0 || AnisoLevel > 16) throw new ArgumentOutOfRangeException(nameof(AnisoLevel));
            if (!Enum.IsDefined(typeof(TextureDataType), DataType)) throw new ArgumentOutOfRangeException(nameof(DataType));
            if (!Enum.IsDefined(typeof(TextureCompression), Compression)) throw new ArgumentOutOfRangeException(nameof(Compression));
            if (!Enum.IsDefined(typeof(TextureOutputChannels), OutputChannels)) throw new ArgumentOutOfRangeException(nameof(OutputChannels));
        }
    }
    public static class TextureProcessingSettings
    {
        private static int _minimum = 65536;
        private static int _threads = Environment.ProcessorCount;
        public static int MinParallelPixels { get => _minimum; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _minimum = value; } }
        public static int MaxThreads { get => _threads; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _threads = value; } }
    }
    public sealed class TextureMaskOptions
    {
        public TextureChannel Channel = TextureChannel.AverageRGB;
        public float Min = 0f, Max = 1f, Gamma = 1f, Strength = 1f, Offset = 0f;
        public bool Invert;
        public bool UseChannelBlending;
        public Vector4 ChannelStrengths = new Vector4(1f, 0f, 0f, 0f);
        public TextureMaskBlendMode ChannelBlendMode = TextureMaskBlendMode.Replace;
        internal void Validate()
        {
            if (!MathUtil.Finite(Min) || !MathUtil.Finite(Max) || !MathUtil.Finite(Gamma) || !MathUtil.Finite(Strength) || !MathUtil.Finite(Offset) || Gamma < 0f)
                throw new ArgumentException("Mask parameters must be finite and gamma must not be negative.");
            if (!MathUtil.Finite(ChannelStrengths) || !Enum.IsDefined(typeof(TextureChannel), Channel) || !Enum.IsDefined(typeof(TextureMaskBlendMode), ChannelBlendMode)) throw new ArgumentException("Invalid mask channel configuration.");
        }
    }
    public sealed class ColorOperationParameters
    {
        public float Amount = 0f;
        public float Amount2 = 1f;
        public float Amount3 = 0f;
        public Color Color = Color.white;
        public TextureColorSpace ColorSpace = TextureColorSpace.HSV;
        public NordicsTextureWrapper Gradient;
        /// <summary>Optional. Multiplies <see cref="Color"/> per pixel (RGBA), sampled at the
        /// pixel's position, for operations that use a color: Tint, Colorize, Multiply, Add.</summary>
        public NordicsTextureWrapper ColorMap;
    }
    public sealed class ColorOperation
    {
        public ColorOperationCode Opcode;
        public NordicsTextureWrapper Mask;
        public TextureMaskOptions MaskOptions;
        public ColorOperationParameters Parameters = new ColorOperationParameters();
    }
    public sealed class TrianglePaintOptions
    {
        public TriangleRasterizationMode Mode = TriangleRasterizationMode.Quality;
        public int SamplesPerAxis = 4;
    }
    /// <summary>Safety rules for <c>BakeNormalDifference</c>, so a correction never makes
    /// shading worse than no correction. Where a rule applies, the correction fades to flat
    /// (0, 0, 1), which keeps the target mesh's own shading.</summary>
    public sealed class NormalDifferenceOptions
    {
        /// <summary>Optional coverage map, the same size as the bake: how often each texel is
        /// covered, counted per UV winding. R and G hold the reference mesh's counter-clockwise
        /// and clockwise triangles, B and A the target's. Paint each triangle with
        /// <c>PaintTriangle</c> and <see cref="CoverageColor"/>. Texels covered by more than one
        /// surface (overlapping UVs, such as mirrored halves or stacked copies) get no correction,
        /// because one texel can't hold the right value for both.</summary>
        public NordicsTextureWrapper Coverage;
        /// <summary>Largest correction kept, in degrees from no correction. Larger ones more
        /// likely come from a mismatch than from real shape, so they fade out over the last 10°.
        /// Never above 90°: a normal map can't store a correction pointing into the surface.</summary>
        public float MaxAngle = 90f;

        /// <summary>The color to paint a triangle with into <see cref="Coverage"/>.</summary>
        public static Color CoverageColor(Vector2 uv0, Vector2 uv1, Vector2 uv2, bool target)
        {
            bool counterClockwise = (uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv1.y - uv0.y) * (uv2.x - uv0.x) >= 0f;
            if (target) return counterClockwise ? new Color(0, 0, 1, 0) : new Color(0, 0, 0, 1);
            return counterClockwise ? new Color(1, 0, 0, 0) : new Color(0, 1, 0, 0);
        }
    }
    /// <summary>What <c>BakeNormalDifference</c> did, for one summary line per bake.</summary>
    public struct NormalDifferenceStatistics
    {
        /// <summary>Texels that got a value.</summary>
        public int Baked;
        /// <summary>Texels whose correction was reduced or removed because surfaces overlap.</summary>
        public int Overlapping;
        /// <summary>Texels whose correction was reduced or removed by <c>MaxAngle</c>, including
        /// ones that would point into the surface.</summary>
        public int Limited;
        /// <summary>The largest correction kept, in degrees.</summary>
        public float LargestAngle;
    }
    // TBN vectors may be baked from mesh vertices by a higher-level caller.
    public sealed class TangentBasisData
    {
        public NordicsTextureWrapper Tangent;
        public NordicsTextureWrapper Bitangent;
        public NordicsTextureWrapper Normal;
    }
    internal static class MathUtil
    {
        internal static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        internal static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        internal static bool Finite(Vector4 v) => Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w);
        internal static bool Finite(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
        internal static float Saturate(float v) => Mathf.Clamp01(v);
        internal static Color Lerp(Color a, Color b, float t) => a + (b - a) * t;
        internal static Vector3 Normal(Color c) => new Vector3(c.r, c.g, c.b);
        internal static Color Pack(Vector3 v, float alpha) => new Color(v.x, v.y, v.z, alpha);
        internal static Vector3 SafeNormal(Vector3 n) => n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.forward;

        // sRGB transfer functions (IEC 61966-2-1), computed in double precision so every
        // 8-bit value survives a decode/encode round trip exactly.
        internal static float SRGBToLinear(float v)
        {
            double d = v;
            return (float)(d <= 0.04045 ? d / 12.92 : Math.Pow((d + 0.055) / 1.055, 2.4));
        }
        internal static float LinearToSRGB(float v)
        {
            double d = v;
            return (float)(d <= 0.0031308 ? d * 12.92 : 1.055 * Math.Pow(d, 1.0 / 2.4) - 0.055);
        }
        /// <summary>Linear value of each 8-bit sRGB code, for decoding sRGB textures.</summary>
        internal static readonly float[] SRGBByteToLinear = BuildSRGBTable();
        private static float[] BuildSRGBTable()
        {
            var table = new float[256];
            for (int i = 0; i < table.Length; i++) table[i] = SRGBToLinear(i / 255f);
            return table;
        }
    }
}
