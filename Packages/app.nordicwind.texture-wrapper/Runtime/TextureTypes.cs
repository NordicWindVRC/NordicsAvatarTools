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
