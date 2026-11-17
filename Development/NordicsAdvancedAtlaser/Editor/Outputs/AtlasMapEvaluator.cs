using System;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // What a source's virtual map looks like over its own UV 0–1, computed on the CPU. It covers
    // an explicit bakeable subset (ATLAS_OUTPUT.md, "Bakeable subset") that grows feature by
    // feature; what is outside it is left out, never guessed at.
    public static class AtlasMapEvaluator
    {
        public static NordicsTextureWrapper Evaluate(AtlasVirtualMap map, AtlasUnifiedMaterial source, int width, int height)
        {
            switch (map)
            {
                case AtlasVirtualMap.BaseColor: return BaseColor(source, width, height);
                case AtlasVirtualMap.Alpha: return Alpha(source, width, height, default);
                default: throw new ArgumentOutOfRangeException(nameof(map), map, "[Nordic's Advanced Atlaser] No evaluator for this virtual map.");
            }
        }

        // The settings a virtual map is baked with: color maps sRGB, data linear, clamped at the
        // atlas edges.
        public static TextureSettings Settings(AtlasVirtualMap map) => new TextureSettings
        {
            DataType = map == AtlasVirtualMap.BaseColor ? TextureDataType.Color : TextureDataType.LinearData,
            SRGB = map == AtlasVirtualMap.BaseColor, WrapMode = TextureWrapMode.Clamp,
            OutputChannels = TextureOutputChannels.Auto
        };

        // One value everywhere; a one-value map holds it in RGB.
        public static NordicsTextureWrapper Constant(AtlasVirtualMap map, float value, int width, int height)
        {
            var result = new NordicsTextureWrapper(width, height, Settings(map));
            result.Fill(new Color(value, value, value, 1f));
            return result;
        }

        /// <summary>
        /// A source's alpha (2.2) in RGB, converted for the output: the source (base color alpha:
        /// the color's alpha × the texture's; the color's alpha alone; a mask × its multiplier;
        /// none: 1), a mask combined by its blend, the offset and the strength. Vertex alpha,
        /// the fades, dithering and masks outside CanEvaluate are left out (the alpha feature
        /// reports them).
        /// </summary>
        public static NordicsTextureWrapper Alpha(AtlasUnifiedMaterial source, int width, int height, AtlasAlphaConversion conversion)
        {
            var alpha = source.Surface.Alpha;
            float colorAlpha = source.Surface.BaseColor.Color.a;
            var result = Constant(AtlasVirtualMap.Alpha, 1f, width, height);
            try
            {
                switch (alpha.Source)
                {
                    case AtlasAlphaSource.BaseColorAlpha:
                        if (source.Surface.BaseColor.Map?.Texture is Texture2D texture)
                            using (var sampled = Sample(source.Surface.BaseColor.Map, texture, TextureDataType.Color, width, height))
                                ChannelToRgb(result, sampled, TextureChannel.A);
                        Scale(result, colorAlpha);
                        if (alpha.Mask != null && CanEvaluate(alpha.Mask))
                            using (var mask = Mask(alpha.Mask, width, height))
                                Combine(result, mask, alpha.MaskBlend);
                        break;
                    case AtlasAlphaSource.BaseColorConstantAlpha:
                        Scale(result, colorAlpha);
                        break;
                    case AtlasAlphaSource.Mask:
                        if (alpha.Mask != null && CanEvaluate(alpha.Mask))
                            using (var mask = Mask(alpha.Mask, width, height))
                                result.Blit(mask);
                        Scale(result, alpha.MaskMultiplier);
                        break;
                }
                if (alpha.Offset != 0f) Add(result, alpha.Offset);
                Clamp(result);
                if (alpha.Strength < 1f)
                {
                    Scale(result, Mathf.Clamp01(alpha.Strength));
                    Add(result, 1f - Mathf.Clamp01(alpha.Strength));
                }
                Convert(result, conversion);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>Whether a mask is in the bakeable subset: a texture channel on UV0 with its
        /// invert and strength, without the mask bus, vertex colors or levels.</summary>
        public static bool CanEvaluate(AtlasMask mask) =>
            mask != null && mask.Map?.Texture is Texture2D && mask.Map.Space == AtlasTextureSpace.UV0 &&
            mask.Bus < 0 && !mask.VertexColor && !mask.TimesVertexColor && mask.Levels == null;

        // The mask's value in RGB.
        private static NordicsTextureWrapper Mask(AtlasMask mask, int width, int height)
        {
            var result = Constant(AtlasVirtualMap.Alpha, 1f, width, height);
            using (var sampled = Sample(mask.Map, (Texture2D)mask.Map.Texture, TextureDataType.LinearData, width, height))
            {
                if (mask.Channel == AtlasChannel.Luminance)
                {
                    // Saturation 0 leaves the luminance in every channel.
                    sampled.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Saturation, Parameters = { Amount = 0f } });
                    ChannelToRgb(result, sampled, TextureChannel.R);
                }
                else ChannelToRgb(result, sampled, Channels[(int)mask.Channel]);
            }
            if (mask.Invert) result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Invert });
            if (mask.Strength < 1f)
            {
                Scale(result, Mathf.Clamp01(mask.Strength));
                Add(result, 1f - Mathf.Clamp01(mask.Strength));
            }
            return result;
        }

        private static readonly TextureChannel[] Channels = { TextureChannel.R, TextureChannel.G, TextureChannel.B, TextureChannel.A };

        // A texture over the source's UV 0–1, with the reference's tiling, offset and rotation on UV0.
        private static NordicsTextureWrapper Sample(AtlasTextureReference reference, Texture2D texture, TextureDataType type, int width, int height)
        {
            using (var loaded = new NordicsTextureWrapper(texture, type))
            {
                bool uv0 = reference.Space == AtlasTextureSpace.UV0;
                var tiling = uv0 ? reference.Tiling : Vector2.one;
                var offset = uv0 ? reference.Offset : Vector2.zero;
                float rotation = uv0 ? reference.Rotation : 0f;
                var result = loaded.Resize(width, height, scale: tiling, offset: offset + (tiling - Vector2.one) * 0.5f,
                    rotation: rotation, sampling: TextureSamplingMode.Area);
                result.Settings.WrapMode = TextureWrapMode.Clamp;
                return result;
            }
        }

        private static void ChannelToRgb(NordicsTextureWrapper target, NordicsTextureWrapper source, TextureChannel channel)
        {
            foreach (var destination in new[] { TextureChannel.R, TextureChannel.G, TextureChannel.B })
                target.Blit(source, sourceChannel: channel, destinationChannel: destination);
        }

        private static void Combine(NordicsTextureWrapper alpha, NordicsTextureWrapper mask, AtlasAlphaMaskBlend blend)
        {
            switch (blend)
            {
                case AtlasAlphaMaskBlend.Replace: alpha.Blit(mask); break;
                case AtlasAlphaMaskBlend.Multiply: alpha.Blit(mask, blend: TextureBlendMode.Multiply); break;
                case AtlasAlphaMaskBlend.Add: alpha.Blit(mask, blend: TextureBlendMode.Add); break;
                case AtlasAlphaMaskBlend.Subtract: alpha.Blit(mask, blend: TextureBlendMode.Subtract); break;
            }
            Clamp(alpha);
        }

        // Under a cutout output, Levels moves the source's cutoff c to the shared 0.5 linearly
        // (c - m to c + m becomes 0 to 1, m the larger distance from c to 0 or 1), keeping the
        // rest in order; under the other modes a cutout source's alpha becomes 0 or 1 at c.
        private static void Convert(NordicsTextureWrapper alpha, AtlasAlphaConversion conversion)
        {
            float c = conversion.Cutoff;
            switch (conversion.Kind)
            {
                case AtlasAlphaConversionKind.Remap:
                    float m = Mathf.Max(c, 1f - c);
                    if (m <= 0f || Mathf.Approximately(c, AtlasSurfaceModeFeature.SharedCutoff)) return;
                    Levels(alpha, c - m, c + m);
                    break;
                case AtlasAlphaConversionKind.Step:
                    Levels(alpha, c - 1e-4f, c + 1e-4f);
                    break;
                default:
                    return;
            }
            Clamp(alpha);
        }

        private static void Scale(NordicsTextureWrapper map, float factor) =>
            map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Multiply, Parameters = { Color = new Color(factor, factor, factor, 1f) } });

        private static void Add(NordicsTextureWrapper map, float amount) =>
            map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Add, Parameters = { Color = new Color(amount, amount, amount, 0f) } });

        private static void Levels(NordicsTextureWrapper map, float low, float high) =>
            map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Levels, Parameters = { Amount = low, Amount2 = high } });

        private static void Clamp(NordicsTextureWrapper map) =>
            map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = 1f } });

        // The base color texture as it is, with its tiling, offset and rotation on UV0; white
        // without a texture. A texture on another space (a projection, another UV channel) is
        // taken as it is. The color it's multiplied by, vertex colors, layers and color
        // adjustments aren't in the subset yet.
        private static NordicsTextureWrapper BaseColor(AtlasUnifiedMaterial source, int width, int height)
        {
            var reference = source.Surface.BaseColor.Map;
            if (!(reference?.Texture is Texture2D texture))
            {
                var white = new NordicsTextureWrapper(width, height, Settings(AtlasVirtualMap.BaseColor));
                white.Fill(Color.white);
                return white;
            }
            using (var loaded = new NordicsTextureWrapper(texture, TextureDataType.Color))
            {
                bool uv0 = reference.Space == AtlasTextureSpace.UV0;
                var tiling = uv0 ? reference.Tiling : Vector2.one;
                var offset = uv0 ? reference.Offset : Vector2.zero;
                float rotation = uv0 ? reference.Rotation : 0f;
                // The unified transform turns the UV counterclockwise around (0.5, 0.5), then
                // multiplies by the tiling around the origin and adds the offset. The wrapper
                // scales around the center instead, which (tiling − 1) / 2 more offset makes up.
                var wrapperOffset = offset + (tiling - Vector2.one) * 0.5f;
                var result = loaded.Resize(width, height, scale: tiling, offset: wrapperOffset, rotation: rotation,
                    sampling: TextureSamplingMode.Area);
                result.Settings.WrapMode = TextureWrapMode.Clamp;
                return result;
            }
        }
    }
}
