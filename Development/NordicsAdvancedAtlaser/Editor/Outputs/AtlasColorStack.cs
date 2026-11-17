using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// The base color stack (S1 for color) as a bake: the base color texture and color, the
    /// adjustments on the base color, the surface layers targeting color in order (each layer's
    /// source with its own adjustments, under its mask and opacity), then the adjustments on the
    /// surface color. Only static parts placed in UV space are baked; what is left out is listed
    /// by LeftOut, one line per kind.
    /// </summary>
    public static class AtlasColorStack
    {
        /// <summary>The source's base color, with its color multiplied in when
        /// <paramref name="multiplyColor"/>.</summary>
        public static NordicsTextureWrapper Evaluate(AtlasUnifiedMaterial source, int width, int height, bool multiplyColor)
        {
            var result = AtlasMapEvaluator.Evaluate(AtlasVirtualMap.BaseColor, source, width, height);
            try
            {
                var color = source.Surface.BaseColor.Color;
                if (multiplyColor) Tint(result, color);
                Adjust(result, source, AtlasAdjustmentTarget.BaseColor, null, width, height);
                for (int i = 0; i < source.Layers.Count; i++)
                    if (source.Layers[i] is AtlasSurfaceLayer layer && CanBake(source, i, layer))
                        foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.BaseColor))
                            Apply(result, source, layer, target, width, height);
                Adjust(result, source, AtlasAdjustmentTarget.SurfaceColor, null, width, height);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>Whether anything is layered on or adjusts the base color, which then can't be
        /// left to a shared color multiplied afterwards.</summary>
        public static bool HasStack(AtlasUnifiedMaterial source)
        {
            for (int i = 0; i < source.Layers.Count; i++)
                if (source.Layers[i] is AtlasSurfaceLayer layer && CanBake(source, i, layer) &&
                    layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.BaseColor)) return true;
            for (int i = 0; i < source.ColorAdjustments.Count; i++)
            {
                var adjustment = source.ColorAdjustments[i];
                if ((adjustment.Target == AtlasAdjustmentTarget.BaseColor || adjustment.Target == AtlasAdjustmentTarget.SurfaceColor) &&
                    CanBake(source, i, adjustment)) return true;
            }
            return false;
        }

        // A path no modulator drives: "layers[3]", "colorAdjustments[0]".
        public static bool IsStatic(AtlasUnifiedMaterial source, string path) =>
            !source.Modulators.Any(m => m.Target != null && (m.Target == path || m.Target.StartsWith(path + ".") || m.Target.StartsWith(path + "[")));

        // A texture that stays still on UV0, tiled over the surface.
        private static bool StillOnUV0(AtlasTextureReference map) =>
            map.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Decal == null && map.Frames == null &&
            map.Scroll == Vector2.zero && map.Spin == 0f && map.Distortion == null;

        /// <summary>Whether a surface layer's color targets are baked: enabled, static, on the
        /// surface on both faces and in every view, its source a color or a still UV0 texture, its
        /// mask bakeable.</summary>
        public static bool CanBake(AtlasUnifiedMaterial source, int index, AtlasSurfaceLayer layer) =>
            layer.Enabled && layer.Placement == AtlasPlacement.Surface && layer.Faces == AtlasFaces.Both &&
            layer.Mirror == AtlasMirrorVisibility.Both && layer.HideInShadow <= 0f && layer.Screen == null &&
            !layer.SourceTimesBaseColor && !layer.SourceIntensityIsAlpha && layer.Source.AudioTheme < 0 &&
            (layer.Source.Map == null || StillOnUV0(layer.Source.Map)) &&
            (layer.Mask == null || AtlasMapEvaluator.CanEvaluate(layer.Mask)) &&
            IsStatic(source, "layers[" + index + "]");

        /// <summary>Whether a color adjustment is baked: static, on the base color, the surface
        /// color or a layer's source, an operation the wrapper has, its mask bakeable.</summary>
        public static bool CanBake(AtlasUnifiedMaterial source, int index, AtlasColorAdjustment adjustment)
        {
            if (adjustment.Target == AtlasAdjustmentTarget.FinalColor || adjustment.Target == AtlasAdjustmentTarget.Emission) return false;
            if (adjustment.Mask != null && !AtlasMapEvaluator.CanEvaluate(adjustment.Mask)) return false;
            if (!IsStatic(source, "colorAdjustments[" + index + "]")) return false;
            switch (adjustment.Operation)
            {
                case AtlasColorOperation.Hue: return adjustment.HueMethod != AtlasHueMethod.GrayAxisRotation;
                case AtlasColorOperation.Saturation: return adjustment.Luminance == new Vector3(0.3f, 0.59f, 0.11f);
                case AtlasColorOperation.Contrast:
                case AtlasColorOperation.Brightness:
                case AtlasColorOperation.Tint:
                case AtlasColorOperation.Power:
                case AtlasColorOperation.Clamp:
                case AtlasColorOperation.Lightness:
                case AtlasColorOperation.Chromatize: return true;
                default: return false;
            }
        }

        /// <summary>One line per kind of base color input that isn't baked yet.</summary>
        public static IEnumerable<string> LeftOut(AtlasUnifiedMaterial source)
        {
            var notes = new List<string>();
            for (int i = 0; i < source.Layers.Count; i++)
            {
                if (!(source.Layers[i] is AtlasSurfaceLayer layer) || !layer.Enabled) continue;
                bool color = layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.BaseColor);
                if (color && layer.Source.Map?.Decal != null) notes.Add("Decals aren't baked yet; they're left out.");
                else if (color && !CanBake(source, i, layer))
                    notes.Add("A color layer that is live (modulated, scrolling, animated), lit, on one side or in mirrors, or masked by the mask bus or vertex colors isn't baked yet; it's left out.");
                if (layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Metallic || t.Target == AtlasSurfaceTarget.Roughness ||
                                            t.Target == AtlasSurfaceTarget.Occlusion || t.Target == AtlasSurfaceTarget.Alpha))
                    notes.Add("Layers targeting alpha, metallic, roughness or occlusion aren't baked yet; they're left out.");
            }
            for (int i = 0; i < source.ColorAdjustments.Count; i++)
            {
                var adjustment = source.ColorAdjustments[i];
                if (adjustment.Target == AtlasAdjustmentTarget.FinalColor)
                    notes.Add("Final color adjustments aren't merged yet; they're left out.");
                else if (adjustment.Target != AtlasAdjustmentTarget.Emission && !CanBake(source, i, adjustment))
                    notes.Add("A color adjustment that is live, masked by the mask bus or vertex colors, or an operation without a baked form yet (" +
                        adjustment.Operation + ") isn't baked; it's left out.");
            }
            if (source.Surface.VertexColor.MultiplyColor)
                notes.Add("Vertex colors on the base color aren't baked yet; they're left out.");
            return notes.Distinct();
        }

        // One surface layer's color target, by its blend, opacity and mask.
        private static void Apply(NordicsTextureWrapper result, AtlasUnifiedMaterial material, AtlasSurfaceLayer layer, AtlasLayerTarget target,
            int width, int height)
        {
            using (var source = Source(layer, width, height))
            {
                Adjust(source, material, AtlasAdjustmentTarget.Layer, layer, width, height);
                using (var mask = Mask(layer, source, width, height))
                {
                    var options = mask != null ? new TextureMaskOptions { Channel = TextureChannel.R } : null;
                    float opacity = Mathf.Max(0f, target.Opacity);
                    switch (target.Blend)
                    {
                        case AtlasBlend.TintLuminance:
                        case AtlasBlend.AddToLuminance:
                            // lerp(b, luminance(b) × l, o) or lerp(b, min(luminance(b) + l, 1), o).
                            using (var luminance = result.Clone())
                            {
                                luminance.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Saturation, Parameters = { Amount = 0f } });
                                luminance.Blit(source, blend: target.Blend == AtlasBlend.TintLuminance ? TextureBlendMode.Multiply : TextureBlendMode.Add);
                                if (target.Blend == AtlasBlend.AddToLuminance)
                                    luminance.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = 1f } });
                                result.Blit(luminance, mask: mask, maskOptions: options, strength: opacity);
                            }
                            return;
                        case AtlasBlend.MultiplyDouble:
                            // Unity's detail multiply: × 2 in gamma, 4.595 in linear.
                            source.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(4.595f, 4.595f, 4.595f, 1f) } });
                            result.Blit(source, mask: mask, maskOptions: options, blend: TextureBlendMode.Multiply, strength: opacity);
                            return;
                        case AtlasBlend.Subtract:
                            result.Blit(source, mask: mask, maskOptions: options, blend: TextureBlendMode.Subtract, strength: opacity);
                            result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = float.MaxValue } });
                            return;
                        default:
                            result.Blit(source, mask: mask, maskOptions: options, blend: Blend(target.Blend), strength: opacity);
                            return;
                    }
                }
            }
        }

        private static TextureBlendMode Blend(AtlasBlend blend)
        {
            switch (blend)
            {
                case AtlasBlend.Multiply: return TextureBlendMode.Multiply;
                case AtlasBlend.Add: return TextureBlendMode.Add;
                case AtlasBlend.Screen: return TextureBlendMode.Screen;
                case AtlasBlend.Overlay: return TextureBlendMode.Overlay;
                case AtlasBlend.Lighten: return TextureBlendMode.Lighten;
                case AtlasBlend.Darken: return TextureBlendMode.Darken;
                case AtlasBlend.Mixed: return TextureBlendMode.Mixed;
                default: return TextureBlendMode.Replace;
            }
        }

        // The layer's source: its color × its map (RGB and alpha); with "source map is mask", the
        // color alone, the map's alpha as its alpha.
        private static NordicsTextureWrapper Source(AtlasSurfaceLayer layer, int width, int height)
        {
            var color = layer.Source.Color;
            var result = new NordicsTextureWrapper(width, height, AtlasMapEvaluator.Settings(AtlasVirtualMap.BaseColor));
            result.Fill(color);
            if (layer.Source.Map?.Texture is Texture2D texture)
                using (var sampled = AtlasMapEvaluator.SampleColor(layer.Source.Map, texture, width, height))
                {
                    if (layer.SourceMapIsMask)
                        result.Blit(sampled, sourceChannel: TextureChannel.A, destinationChannel: TextureChannel.A, blend: TextureBlendMode.Multiply);
                    else result.Blit(sampled, blend: TextureBlendMode.Multiply);
                }
            return result;
        }

        // The layer's mask × (with "source alpha is opacity") its source's alpha plus the offset;
        // null when neither applies.
        private static NordicsTextureWrapper Mask(AtlasSurfaceLayer layer, NordicsTextureWrapper source, int width, int height)
        {
            bool alpha = layer.SourceAlphaIsOpacity || layer.SourceMapIsMask;
            if (layer.Mask == null && !alpha) return null;
            var mask = layer.Mask != null ? AtlasMapEvaluator.ScaledMask(layer.Mask, 1f, width, height)
                : AtlasMapEvaluator.Constant(AtlasVirtualMap.Metallic, 1f, width, height);
            if (!alpha) return mask;
            using (var opacity = AtlasMapEvaluator.Constant(AtlasVirtualMap.Metallic, 1f, width, height))
            {
                foreach (var channel in new[] { TextureChannel.R, TextureChannel.G, TextureChannel.B })
                    opacity.Blit(source, sourceChannel: TextureChannel.A, destinationChannel: channel);
                if (layer.SourceAlphaOffset != 0f)
                    opacity.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Add,
                        Parameters = { Color = new Color(layer.SourceAlphaOffset, layer.SourceAlphaOffset, layer.SourceAlphaOffset, 0f) } });
                opacity.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = 1f } });
                mask.Blit(opacity, blend: TextureBlendMode.Multiply);
            }
            return mask;
        }

        // The baked adjustments on one target, in the material's order.
        private static void Adjust(NordicsTextureWrapper map, AtlasUnifiedMaterial source, AtlasAdjustmentTarget target, AtlasLayer layer,
            int width, int height)
        {
            for (int i = 0; i < source.ColorAdjustments.Count; i++)
            {
                var adjustment = source.ColorAdjustments[i];
                if (adjustment.Target != target || (target == AtlasAdjustmentTarget.Layer && adjustment.Layer != layer)) continue;
                if (!CanBake(source, i, adjustment)) continue;
                using (var mask = adjustment.Mask != null ? AtlasMapEvaluator.ScaledMask(adjustment.Mask, 1f, width, height) : null)
                {
                    var operation = Operation(adjustment);
                    if (mask != null)
                    {
                        operation.Mask = mask;
                        operation.MaskOptions = new TextureMaskOptions { Channel = TextureChannel.R };
                    }
                    map.ApplyColor(operation);
                }
            }
        }

        // The wrapper's form of a bakeable adjustment.
        private static ColorOperation Operation(AtlasColorAdjustment adjustment)
        {
            switch (adjustment.Operation)
            {
                case AtlasColorOperation.Hue:
                    return new ColorOperation
                    {
                        Opcode = adjustment.HueSet ? ColorOperationCode.HueReplace : ColorOperationCode.HueShift,
                        Parameters = { Amount = adjustment.Amount, ColorSpace = adjustment.HueMethod == AtlasHueMethod.HSV ? TextureColorSpace.HSV : TextureColorSpace.OKLab }
                    };
                case AtlasColorOperation.Saturation: return new ColorOperation { Opcode = ColorOperationCode.Saturation, Parameters = { Amount = adjustment.Amount } };
                case AtlasColorOperation.Contrast: return new ColorOperation { Opcode = ColorOperationCode.Contrast, Parameters = { Amount = adjustment.Amount } };
                case AtlasColorOperation.Brightness: return new ColorOperation { Opcode = ColorOperationCode.Brightness, Parameters = { Amount = adjustment.Amount } };
                case AtlasColorOperation.Tint: return new ColorOperation { Opcode = ColorOperationCode.Tint, Parameters = { Color = adjustment.Color } };
                case AtlasColorOperation.Power: return new ColorOperation { Opcode = ColorOperationCode.Gamma, Parameters = { Amount = adjustment.Amount } };
                case AtlasColorOperation.Clamp: return new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = 1f } };
                case AtlasColorOperation.Lightness:
                    return new ColorOperation { Opcode = ColorOperationCode.Add, Parameters = { Color = new Color(adjustment.Amount, adjustment.Amount, adjustment.Amount, 0f) } };
                case AtlasColorOperation.Chromatize: return new ColorOperation { Opcode = ColorOperationCode.Chromatize, Parameters = { Amount = adjustment.Amount } };
                default: throw new System.ArgumentOutOfRangeException(nameof(adjustment), adjustment.Operation, "No baked form.");
            }
        }

        private static void Tint(NordicsTextureWrapper map, Color color) =>
            map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(color.r, color.g, color.b, 1f) } });
    }
}
