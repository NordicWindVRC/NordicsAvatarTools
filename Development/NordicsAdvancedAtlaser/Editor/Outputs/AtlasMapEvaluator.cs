using System;
using System.Linq;
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
                case AtlasVirtualMap.Metallic: return Scalar(source.Surface.Response.Metallic, width, height, source);
                case AtlasVirtualMap.Roughness: return Scalar(source.Surface.Response.Roughness, width, height, source);
                case AtlasVirtualMap.Occlusion: return Scalar(source.Surface.Response.Occlusion, width, height, source);
                case AtlasVirtualMap.ReflectionMask:
                case AtlasVirtualMap.SpecularMask: return Constant(map, 1f, width, height);
                case AtlasVirtualMap.Normal:
                    var single = new AtlasGeneratorSource { Material = source };
                    return Normal(single, width, height, Mathf.Max(1e-4f, AtlasNormalFeature.LargestStrength(single)));
                default: throw new ArgumentOutOfRangeException(nameof(map), map, "[Nordic's Advanced Atlaser] No evaluator for this virtual map.");
            }
        }

        // The settings a virtual map is baked with: color maps sRGB, data linear, clamped at the
        // atlas edges.
        public static TextureSettings Settings(AtlasVirtualMap map) => new TextureSettings
        {
            DataType = map == AtlasVirtualMap.BaseColor ? TextureDataType.Color
                : map == AtlasVirtualMap.Normal ? TextureDataType.NormalTangent : TextureDataType.LinearData,
            SRGB = map == AtlasVirtualMap.BaseColor, WrapMode = TextureWrapMode.Clamp,
            OutputChannels = TextureOutputChannels.Auto
        };

        // A flat tangent-space normal everywhere.
        public static NordicsTextureWrapper Flat(int width, int height)
        {
            var result = new NordicsTextureWrapper(width, height, Settings(AtlasVirtualMap.Normal));
            result.Fill(new Color(0f, 0f, 1f, 1f));
            return result;
        }

        /// <summary>
        /// A source's normals combined by whiteout, in order: its pre-baked normal map and the
        /// build's correction (each at strength 1), the primary normal map at its strength, then
        /// the surface layers targeting the normal at their opacity, each masked. Every strength is
        /// divided by <paramref name="outputStrength"/>, the strength the output's normal map is
        /// drawn at. Layers outside CanBakeNormalLayer are left out (the feature reports them).
        /// </summary>
        public static NordicsTextureWrapper Normal(AtlasGeneratorSource source, int width, int height, float outputStrength)
        {
            var result = Flat(width, height);
            try
            {
                float scale = 1f / Mathf.Max(outputStrength, 1e-4f);
                if (source.PrebakedNormalMap != null)
                    using (var prebaked = new NordicsTextureWrapper(source.PrebakedNormalMap, TextureDataType.NormalTangent))
                        result.Blit(prebaked, blend: TextureBlendMode.NormalWhiteout, strength: scale, sampling: TextureSamplingMode.Area);
                var correction = Correction(source);
                if (correction != null)
                    result.Blit(correction, blend: TextureBlendMode.NormalWhiteout, strength: scale, sampling: TextureSamplingMode.Area);
                var material = source.Material;
                var primary = material?.Surface.Normal;
                if (primary?.Map?.Texture is Texture2D texture)
                    using (var sampled = Sample(primary.Map, texture, TextureDataType.NormalTangent, width, height))
                        result.Blit(sampled, blend: TextureBlendMode.NormalWhiteout, strength: primary.Strength * scale);
                foreach (var layer in AtlasNormalFeature.NormalLayers(material).Where(l => CanBakeNormalLayer(l, material)))
                    foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.Normal))
                        using (var sampled = Sample(layer.Source.Map, (Texture2D)layer.Source.Map.Texture, TextureDataType.NormalTangent, width, height))
                        {
                            if (layer.Mask == null)
                                result.Blit(sampled, blend: TextureBlendMode.NormalWhiteout, strength: target.Opacity * scale);
                            else
                                using (var mask = Mask(layer.Mask, width, height, material))
                                    result.Blit(sampled, mask: mask, maskOptions: new TextureMaskOptions { Channel = TextureChannel.R },
                                        blend: TextureBlendMode.NormalWhiteout, strength: target.Opacity * scale);
                        }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>The build's normal correction for a source; null when it has none or it's flat.</summary>
        public static NordicsTextureWrapper Correction(AtlasGeneratorSource source) =>
            source?.Bakes?.Submeshes.FirstOrDefault(s => !s.CurrentSubmeshMissing && !s.CorrectionIsFlat && s.TangentNormalCorrection != null)
                ?.TangentNormalCorrection;

        /// <summary>Whether a normal layer is in the bakeable subset: a texture on UV0, tiled over
        /// the surface, on both faces and in every view, whiteout (not lerped), with a mask that
        /// CanEvaluate or none.</summary>
        public static bool CanBakeNormalLayer(AtlasSurfaceLayer layer, AtlasUnifiedMaterial source = null) =>
            layer != null && layer.Source.Map?.Texture is Texture2D && layer.Source.Map.Space == AtlasTextureSpace.UV0 &&
            layer.Source.Map.Decal == null && layer.Placement == AtlasPlacement.Surface && layer.Faces == AtlasFaces.Both &&
            layer.Mirror == AtlasMirrorVisibility.Both && (layer.Mask == null || CanEvaluate(layer.Mask, source)) &&
            layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.Normal).All(t => !t.ReplaceNormal);

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
                        // Vertex alpha is part of the base color (2.1).
                        AtlasVertexColors.MultiplyAlpha(result, source, width, height);
                        if (alpha.Mask != null && CanEvaluate(alpha.Mask, source))
                            using (var mask = Mask(alpha.Mask, width, height, source))
                                Combine(result, mask, alpha.MaskBlend);
                        break;
                    case AtlasAlphaSource.BaseColorConstantAlpha:
                        Scale(result, colorAlpha);
                        AtlasVertexColors.MultiplyAlpha(result, source, width, height);
                        break;
                    case AtlasAlphaSource.Mask:
                        if (alpha.Mask != null && CanEvaluate(alpha.Mask, source))
                            using (var mask = Mask(alpha.Mask, width, height, source))
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

        /// <summary>
        /// A scalar input (1.2) in RGB: the constant × the map's channel (the sample inverted
        /// first when asked; for strength around one, 1 − (1 − channel) × constant), combined with
        /// its mask bus channel, then the result inverted when asked, clamped to 0–1. An input
        /// outside CanEvaluate counts as its constant alone (the feature reports it). The bus
        /// needs the input's material, <paramref name="source"/>.
        /// </summary>
        public static NordicsTextureWrapper Scalar(AtlasScalarInput input, int width, int height, AtlasUnifiedMaterial source = null)
        {
            var texture = input.Map?.Texture as Texture2D;
            if (!CanEvaluate(input, source) || (texture == null && input.Bus < 0))
            {
                var probe = new AtlasScalarInput { Constant = input.Constant, Combine = input.Combine, InvertResult = input.InvertResult };
                return Constant(AtlasVirtualMap.Metallic, Mathf.Clamp01(probe.Evaluate()), width, height);
            }
            var result = Constant(AtlasVirtualMap.Metallic, 1f, width, height);
            try
            {
                if (texture != null)
                {
                    using (var sampled = Sample(input.Map, texture, TextureDataType.LinearData, width, height))
                        ReadChannel(result, sampled, input.Channel);
                    if (input.InvertSample) result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Invert });
                    if (input.Combine == AtlasScalarCombine.StrengthAroundOne)
                    {
                        Scale(result, input.Constant);
                        Add(result, 1f - input.Constant);
                    }
                    else Scale(result, input.Constant);
                }
                else Scale(result, new AtlasScalarInput { Constant = input.Constant, Combine = input.Combine }.Evaluate());
                if (input.Bus >= 0)
                    using (var channel = AtlasMaskBusEvaluator.Channel(source, input.Bus, width, height))
                        AtlasMaskBusEvaluator.Blend(result, channel, input.BusBlend);
                if (input.InvertResult) result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Invert });
                Clamp(result);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>Whether a scalar input is in the bakeable subset: a constant, or a map on UV0,
        /// tiled over the surface, without a remap; on a mask bus channel of
        /// <paramref name="source"/> that AtlasMaskBusEvaluator can bake.</summary>
        public static bool CanEvaluate(AtlasScalarInput input, AtlasUnifiedMaterial source = null) =>
            input != null && (input.Bus < 0 || AtlasMaskBusEvaluator.CanBake(source, input.Bus)) && (input.Map == null ||
                (input.Map.Texture is Texture2D && input.Map.Space == AtlasTextureSpace.UV0 && input.Map.Decal == null && input.Remap == null));

        /// <summary>A mask's value in RGB, × <paramref name="scale"/>; <paramref name="scale"/> alone
        /// without a mask or outside CanEvaluate. The bus needs the mask's material,
        /// <paramref name="source"/>.</summary>
        public static NordicsTextureWrapper ScaledMask(AtlasMask mask, float scale, int width, int height, AtlasUnifiedMaterial source = null)
        {
            if (mask == null || !CanEvaluate(mask, source)) return Constant(AtlasVirtualMap.Metallic, Mathf.Max(0f, scale), width, height);
            var result = Mask(mask, width, height, source);
            Scale(result, Mathf.Max(0f, scale));
            return result;
        }

        // A channel of a sampled texture into RGB (luminance by its weights).
        private static void ReadChannel(NordicsTextureWrapper target, NordicsTextureWrapper sampled, AtlasChannel channel)
        {
            if (channel == AtlasChannel.Luminance)
            {
                sampled.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Saturation, Parameters = { Amount = 0f } });
                ChannelToRgb(target, sampled, TextureChannel.R);
            }
            else ChannelToRgb(target, sampled, Channels[(int)channel]);
        }

        /// <summary>Whether a mask is in the bakeable subset: a texture channel on UV0, a vertex
        /// color channel, or none, with its levels, vertex color multiply, invert and strength,
        /// combined with a mask bus channel of <paramref name="source"/> that AtlasMaskBusEvaluator
        /// can bake. Vertex colors come from AtlasVertexColors (white without).</summary>
        public static bool CanEvaluate(AtlasMask mask, AtlasUnifiedMaterial source = null) =>
            mask != null &&
            (mask.VertexColor || mask.Map == null || (mask.Map.Texture is Texture2D && mask.Map.Space == AtlasTextureSpace.UV0)) &&
            (mask.Bus < 0 || AtlasMaskBusEvaluator.CanBake(source, mask.Bus));

        // The mask's value in RGB: the channel (of the map or the vertex colors) through the
        // levels, × a vertex color channel, inverted, lerp(1, v, strength) (1 without a source),
        // then combined with its bus channel.
        private static NordicsTextureWrapper Mask(AtlasMask mask, int width, int height, AtlasUnifiedMaterial source)
        {
            var result = Constant(AtlasVirtualMap.Alpha, 1f, width, height);
            try
            {
                bool vertex = mask.VertexColor;
                if (vertex || mask.Map?.Texture is Texture2D)
                {
                    if (vertex)
                    {
                        using (var channel = AtlasVertexColors.Channel(source, mask.Channel, false, width, height))
                            if (channel != null) result.Blit(channel);
                            else result.Fill(Color.white);
                    }
                    else
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
                    if (mask.Levels != null) AtlasMaskBusEvaluator.Each(result, mask.Levels.Evaluate);
                    if (mask.TimesVertexColor)
                        using (var channel = AtlasVertexColors.Channel(source, mask.VertexChannel, false, width, height))
                            if (channel != null) AtlasMaskBusEvaluator.Blend(result, channel, AtlasMaskBlend.Multiply);
                    if (mask.Invert) result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Invert });
                    if (mask.Strength < 1f)
                    {
                        Scale(result, Mathf.Clamp01(mask.Strength));
                        Add(result, 1f - Mathf.Clamp01(mask.Strength));
                    }
                }
                if (mask.Bus >= 0)
                    using (var channel = AtlasMaskBusEvaluator.Channel(source, mask.Bus, width, height))
                        AtlasMaskBusEvaluator.Blend(result, channel, mask.BusBlend);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static readonly TextureChannel[] Channels = { TextureChannel.R, TextureChannel.G, TextureChannel.B, TextureChannel.A };

        // A color texture over the source's UV 0–1, as Sample does it.
        public static NordicsTextureWrapper SampleColor(AtlasTextureReference reference, Texture2D texture, int width, int height) =>
            Sample(reference, texture, TextureDataType.Color, width, height);

        // A texture over the source's UV 0–1, with the reference's tiling, offset and rotation on UV0.
        private static NordicsTextureWrapper Sample(AtlasTextureReference reference, Texture2D texture, TextureDataType type, int width, int height)
        {
            bool uv0 = reference.Space == AtlasTextureSpace.UV0;
            var tiling = uv0 ? reference.Tiling : Vector2.one;
            var offset = uv0 ? reference.Offset : Vector2.zero;
            float rotation = uv0 ? reference.Rotation : 0f;
            return AtlasEvaluationCache.Copy(("sample", texture, type, tiling, offset, rotation, width, height), () =>
            {
                var loaded = Loaded(texture, type, out bool owned);
                try
                {
                    var result = loaded.Resize(width, height, scale: tiling, offset: offset + (tiling - Vector2.one) * 0.5f,
                        rotation: rotation, sampling: TextureSamplingMode.Area);
                    result.Settings.WrapMode = TextureWrapMode.Clamp;
                    return result;
                }
                finally
                {
                    if (owned) loaded.Dispose();
                }
            });
        }

        /// <summary>A texture loaded as the wrapper reads it, only to be read; shared within an
        /// evaluation (AtlasEvaluationCache). <paramref name="owned"/>: the caller disposes it.</summary>
        public static NordicsTextureWrapper Loaded(Texture2D texture, TextureDataType type, out bool owned) =>
            AtlasEvaluationCache.Shared(("load", texture, type), () => new NordicsTextureWrapper(texture, type), out owned);

        // One channel of a same-size map into the target's RGB, in one pass.
        private static void ChannelToRgb(NordicsTextureWrapper target, NordicsTextureWrapper source, TextureChannel channel) =>
            AtlasPixels.Map(target, source, (t, s) =>
            {
                float v = channel == TextureChannel.R ? s.r : channel == TextureChannel.G ? s.g : channel == TextureChannel.B ? s.b : s.a;
                return new Color(v, v, v, t.a);
            });

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
