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
        public static NordicsTextureWrapper Evaluate(AtlasUnifiedMaterial source, int width, int height, bool multiplyColor,
            bool emissionOverride = true)
        {
            NordicsTextureWrapper result;
            using (AtlasEvaluationCache.Step("base color texture"))
                result = AtlasMapEvaluator.Evaluate(AtlasVirtualMap.BaseColor, source, width, height);
            try
            {
                var color = source.Surface.BaseColor.Color;
                if (multiplyColor) Tint(result, color);
                // base = color input × vertex color (2.1), where the vertex colors are baked.
                using (AtlasEvaluationCache.Step("vertex colors"))
                    AtlasVertexColors.MultiplyColor(result, source, width, height);
                using (AtlasEvaluationCache.Step("base color adjustments"))
                    Adjust(result, source, AtlasAdjustmentTarget.BaseColor, null, width, height);
                // Layers kept live (or left out) by their own feature aren't baked.
                for (int i = 0; i < source.Layers.Count; i++)
                    if (source.Layers[i] is AtlasSurfaceLayer layer && CanBake(source, i, layer) && !AtlasLiveLayers.Skipped(source, layer))
                        foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.BaseColor))
                            using (AtlasEvaluationCache.Step($"layer {i + 1} ({Describe(layer, target)})"))
                                Apply(result, source, layer, target, width, height);
                using (AtlasEvaluationCache.Step("surface color adjustments"))
                    Adjust(result, source, AtlasAdjustmentTarget.SurfaceColor, null, width, height);
                if (emissionOverride) Override(result, source, color, multiplyColor, width, height);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>Whether anything is layered on or adjusts the base color, which then can't be
        /// left to a shared color multiplied afterward.</summary>
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
            return Overriding(source) != null;
        }

        // The baked emission layers that also replace the base color (OverridesBaseColor).
        private static List<AtlasLayer> Overriding(AtlasUnifiedMaterial source)
        {
            List<AtlasLayer> result = null;
            for (int i = 0; i < source.Layers.Count; i++)
                if (source.Layers[i] is AtlasSurfaceLayer layer && layer.OverridesBaseColor &&
                    layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) && CanBakeEmission(source, i, layer))
                    (result ?? (result = new List<AtlasLayer>())).Add(layer);
            return result;
        }

        // The last step on the base color b: lerp(b, saturate(e), max(e)) with e the emission of
        // the layers that replace it (unclamped, as Poiyomi has it). A map without the base
        // color's color is replaced as b × color, then divided by it again.
        private static void Override(NordicsTextureWrapper result, AtlasUnifiedMaterial source, Color color, bool multiplied, int width, int height)
        {
            var layers = Overriding(source);
            if (layers == null) return;
            using (AtlasEvaluationCache.Step("emission replacing the base color"))
            using (var emission = EvaluateEmission(source, width, height, 1f, layers, clamp: false))
                AtlasPixels.Map(result, emission, (b, e) =>
                {
                    float amount = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                    if (amount <= 0f) return b;
                    var replaced = b;
                    for (int c = 0; c < 3; c++)
                    {
                        float tint = multiplied ? 1f : color[c];
                        float value = Mathf.LerpUnclamped(b[c] * tint, Mathf.Clamp01(e[c]), amount);
                        replaced[c] = multiplied ? value : tint > 1e-5f ? value / tint : b[c];
                    }
                    return replaced;
                });
        }

        // A path no modulator drives: "layers[3]", "colorAdjustments[0]".
        // Audio modulators that leave their target as it is without music count as static: the
        // part is baked at rest (AtlasAudio).
        public static bool IsStatic(AtlasUnifiedMaterial source, string path) =>
            !source.Modulators.Any(m => m.Target != null && !AtlasAudio.AtRest(m) &&
                                        (m.Target == path || m.Target.StartsWith(path + ".") || m.Target.StartsWith(path + "[")));

        // A texture that stays still on UV0: tiled over the surface, or placed as a decal that
        // CanPlace bakes.
        private static bool StillOnUV0(AtlasTextureReference map) =>
            map.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Frames == null &&
            map.Scroll == Vector2.zero && map.Spin == 0f && map.Distortion == null && (map.Decal == null || CanPlace(map));

        /// <summary>Whether a decal's placement is baked: placed by center, scale and rotation, not
        /// spinning, without symmetry, mirror sides, parallax depth, or a texture rotation or
        /// MSDF text.</summary>
        public static bool CanPlace(AtlasTextureReference map)
        {
            var decal = map.Decal;
            return decal != null && decal.Symmetry == AtlasDecalSymmetry.None && decal.Mirror == AtlasDecalMirror.None &&
                decal.Depth == 0f && decal.Spin == 0f && decal.Scale.x > 0f && decal.Scale.y > 0f &&
                map.Rotation == 0f && map.Interpretation != AtlasTextureInterpretation.Msdf;
        }

        /// <summary>Whether a surface layer's color targets are baked: enabled, static, on the
        /// surface on both faces and in every view, its source a color or a still UV0 texture
        /// (tiled or a decal), its mask bakeable.</summary>
        public static bool CanBake(AtlasUnifiedMaterial source, int index, AtlasSurfaceLayer layer) =>
            Bakeable(source, index, layer, emission: false);

        /// <summary>The same for a layer's emission targets, which may also take the base color as
        /// their source's multiplier.</summary>
        public static bool CanBakeEmission(AtlasUnifiedMaterial source, int index, AtlasSurfaceLayer layer, bool anyModulators = false) =>
            Bakeable(source, index, layer, emission: true, anyModulators);

        private static bool Bakeable(AtlasUnifiedMaterial source, int index, AtlasSurfaceLayer layer, bool emission, bool anyModulators = false) =>
            layer.Enabled && (layer.Placement == AtlasPlacement.Surface || (emission && layer.Placement == AtlasPlacement.Emission)) &&
            layer.Faces == AtlasFaces.Both && layer.Mirror == AtlasMirrorVisibility.Both && layer.HideInShadow <= 0f && layer.Screen == null &&
            (emission || !layer.SourceTimesBaseColor) && !layer.SourceIntensityIsAlpha && layer.Source.AudioTheme < 0 &&
            (!emission || !layer.EmissionTimesAlpha) &&
            (layer.Source.Map == null || StillOnUV0(layer.Source.Map)) &&
            (layer.Mask == null || AtlasMapEvaluator.CanEvaluate(layer.Mask, source)) &&
            (anyModulators || IsStatic(source, "layers[" + index + "]"));

        /// <summary>A source's emission: its baked emission layers added up, divided by
        /// <paramref name="outputStrength"/>, the strength the output's emission is drawn at.</summary>
        public static NordicsTextureWrapper EvaluateEmission(AtlasUnifiedMaterial source, int width, int height, float outputStrength,
            ICollection<AtlasLayer> only = null, bool anyModulators = false, bool clamp = true, bool unitStrength = false)
        {
            var result = new NordicsTextureWrapper(width, height, AtlasMapEvaluator.Settings(AtlasVirtualMap.BaseColor));
            result.Fill(new Color(0f, 0f, 0f, 1f));
            try
            {
                for (int i = 0; i < source.Layers.Count; i++)
                {
                    // Modulated layers too where asked (an AudioLink emission's own map).
                    if (!(source.Layers[i] is AtlasSurfaceLayer layer) ||
                        !CanBakeEmission(source, i, layer, anyModulators && only != null && only.Contains(layer))) continue;
                    // Only the given layers; otherwise every one not kept elsewhere.
                    if (only != null ? !only.Contains(layer) : AtlasLiveLayers.Skipped(source, layer)) continue;
                    foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.Emission))
                        using (AtlasEvaluationCache.Step($"emission layer {i + 1} ({Describe(layer, target)})"))
                        using (var layerSource = Source(layer, width, height, out var coverage))
                        using (coverage)
                        {
                            Adjust(layerSource, source, AtlasAdjustmentTarget.Layer, layer, width, height);
                            // The surface color as it is before emissions replace it: the end of S1,
                            // with the view-dependent surface layers (matcaps, rims) at their
                            // average look, as Poiyomi applies them before its emissions read it.
                            if (layer.SourceTimesBaseColor)
                                using (var baseColor = Evaluate(source, width, height, multiplyColor: true, emissionOverride: false))
                                {
                                    AtlasViewLayers.Apply(source, baseColor, width, height);
                                    layerSource.Blit(baseColor, blend: TextureBlendMode.Multiply);
                                }
                            using (var mask = Mask(layer, source, layerSource, coverage, width, height))
                                // Unit strength: the emission's color, map and mask alone (an emission
                                // AudioLink gives its strength).
                                result.Blit(layerSource, mask: mask, maskOptions: mask != null ? new TextureMaskOptions { Channel = TextureChannel.R } : null,
                                    blend: TextureBlendMode.Add, strength: unitStrength ? 1f : Mathf.Max(0f, target.Opacity));
                        }
                }
                float scale = 1f / Mathf.Max(outputStrength, 1e-4f);
                result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(scale, scale, scale, 1f) } });
                if (clamp) result.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = 1f } });
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>The most a source's baked emission reaches: each layer's brightest color
        /// channel × its strength, added up (maps reach at most 1).</summary>
        public static float EmissionStrength(AtlasUnifiedMaterial source, ICollection<AtlasLayer> only = null)
        {
            float strength = 0f;
            for (int i = 0; i < source.Layers.Count; i++)
                if (source.Layers[i] is AtlasSurfaceLayer layer && CanBakeEmission(source, i, layer) &&
                    (only != null ? only.Contains(layer) : !AtlasLiveLayers.Skipped(source, layer)))
                    foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.Emission))
                        strength += Mathf.Max(0f, target.Opacity) * Mathf.Max(0f, Mathf.Max(layer.Source.Color.r, Mathf.Max(layer.Source.Color.g, layer.Source.Color.b)));
            return strength;
        }

        /// <summary>Whether a color adjustment is baked: static, on the base color, the surface
        /// color or a layer's source, an operation the wrapper has, its mask bakeable.</summary>
        public static bool CanBake(AtlasUnifiedMaterial source, int index, AtlasColorAdjustment adjustment)
        {
            if (adjustment.Target == AtlasAdjustmentTarget.FinalColor || adjustment.Target == AtlasAdjustmentTarget.Emission) return false;
            if (adjustment.Mask != null && !AtlasMapEvaluator.CanEvaluate(adjustment.Mask, source)) return false;
            // A cycling hue is baked at its start (AtlasHueCycling).
            if (!IsStatic(source, "colorAdjustments[" + index + "]") && !AtlasHueCycling.IsCycling(source, index)) return false;
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
                if (color && layer.Source.Map?.Decal != null && !CanPlace(layer.Source.Map))
                    notes.Add("A decal with symmetry, mirror sides, parallax depth, spin, a rotated texture or MSDF text isn't baked yet; it's left out.");
                else if (color && !CanBake(source, i, layer))
                    notes.Add("A color layer that is live (modulated, scrolling, animated), lit, on one side or in mirrors, or masked by a global mask written by layers or the second pass isn't baked yet; it's left out.");
                if (layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Metallic || t.Target == AtlasSurfaceTarget.Roughness ||
                                            t.Target == AtlasSurfaceTarget.Occlusion || t.Target == AtlasSurfaceTarget.Alpha))
                    notes.Add("Layers targeting alpha, metallic, roughness or occlusion aren't baked yet; they're left out.");
            }
            var proximity = AtlasProximityColorFeature.Tints(source);
            for (int i = 0; i < source.ColorAdjustments.Count; i++)
            {
                var adjustment = source.ColorAdjustments[i];
                // Proximity color's tints, a final color's hue cycling and tone mapping are their
                // own features'.
                if (proximity.Any(p => p.Adjustment == adjustment) || AtlasToneMappingFeature.Is(adjustment)) continue;
                if (adjustment.Target == AtlasAdjustmentTarget.FinalColor && AtlasHueCycling.IsCycling(source, i)) continue;
                if (adjustment.Target == AtlasAdjustmentTarget.FinalColor)
                    notes.Add("Final color adjustments aren't merged yet; they're left out.");
                else if (adjustment.Target != AtlasAdjustmentTarget.Emission && !CanBake(source, i, adjustment))
                    notes.Add("A color adjustment that is live, masked by a global mask written by layers or the second pass, or an operation without a baked form yet (" +
                        adjustment.Operation + ") isn't baked; it's left out.");
            }
            return notes.Distinct();
        }

        // One surface layer's color target, by its blend, opacity and mask.
        private static void Apply(NordicsTextureWrapper result, AtlasUnifiedMaterial material, AtlasSurfaceLayer layer, AtlasLayerTarget target,
            int width, int height)
        {
            using (var source = Source(layer, width, height, out var coverage))
            using (coverage)
            {
                Adjust(source, material, AtlasAdjustmentTarget.Layer, layer, width, height);
                using (var mask = Mask(layer, material, source, coverage, width, height))
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

        // For timing lines: the layer's blend, source and mask.
        private static string Describe(AtlasSurfaceLayer layer, AtlasLayerTarget target)
        {
            var parts = new List<string> { target.Blend.ToString() };
            if (layer.Source.Map?.Decal != null) parts.Add("decal");
            else if (layer.Source.Map?.Texture != null) parts.Add("texture");
            var mask = layer.Mask;
            if (mask != null)
                parts.Add(mask.VertexColor ? "vertex color mask" : mask.Map != null ? (mask.Bus >= 0 ? "mask and global mask" : "mask") :
                    mask.Bus >= 0 ? "global mask" : "mask");
            if (mask?.Levels != null) parts.Add("levels");
            return string.Join(", ", parts);
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
        // color alone, the map's alpha as its alpha. A decal also gives its coverage (1 where it's
        // placed), null otherwise.
        private static NordicsTextureWrapper Source(AtlasSurfaceLayer layer, int width, int height, out NordicsTextureWrapper coverage)
        {
            coverage = null;
            var color = layer.Source.Color;
            if (layer.Source.Map?.Decal != null && layer.Source.Map.Texture is Texture2D decalTexture)
                return Decal(layer, decalTexture, width, height, out coverage);
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

        // A placed decal (Poiyomi's decalUV): the UV turned by the rotation around the center, then
        // the rectangle center ± scale / 2 mapped to 0–1, where the texture is sampled with its
        // tiling and offset. With "clip outside" nothing is sampled outside that rectangle; the
        // coverage is 1 where something was.
        private static NordicsTextureWrapper Decal(AtlasSurfaceLayer layer, Texture2D texture, int width, int height,
            out NordicsTextureWrapper coverage)
        {
            var map = layer.Source.Map;
            var decal = map.Decal;
            var color = layer.Source.Color;
            var result = new NordicsTextureWrapper(width, height, AtlasMapEvaluator.Settings(AtlasVirtualMap.BaseColor));
            coverage = AtlasMapEvaluator.Constant(AtlasVirtualMap.Metallic, 0f, width, height);
            float theta = decal.Rotation * Mathf.Deg2Rad, cs = Mathf.Cos(theta), sn = Mathf.Sin(theta);
            int x0 = 0, y0 = 0, x1 = width, y1 = height;
            if (decal.ClipOutside)
            {
                // The placed rectangle's bounds in UV: its corners turned back by the rotation.
                float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
                foreach (var corner in new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) })
                {
                    var q = Vector2.Scale(corner, decal.Scale);
                    var uv = decal.Center + new Vector2(q.x * cs + q.y * sn, -q.x * sn + q.y * cs);
                    minU = Mathf.Min(minU, uv.x); maxU = Mathf.Max(maxU, uv.x);
                    minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y);
                }
                x0 = Mathf.Clamp(Mathf.FloorToInt(minU * width), 0, width); x1 = Mathf.Clamp(Mathf.CeilToInt(maxU * width), 0, width);
                y0 = Mathf.Clamp(Mathf.FloorToInt(minV * height), 0, height); y1 = Mathf.Clamp(Mathf.CeilToInt(maxV * height), 0, height);
            }
            var pixels = result.GetPixels();
            var covered = coverage.GetPixels();
            var loaded = AtlasMapEvaluator.Loaded(texture, TextureDataType.Color, out bool owned);
            try
            {
                // Each row on its own core; the loaded texture is only read.
                System.Threading.Tasks.Parallel.For(y0, y1, y =>
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var d = new Vector2((x + 0.5f) / width, (y + 0.5f) / height) - decal.Center;
                        var p = new Vector2((d.x * cs - d.y * sn) / decal.Scale.x + 0.5f, (d.x * sn + d.y * cs) / decal.Scale.y + 0.5f);
                        if (decal.ClipOutside && (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)) continue;
                        var sample = loaded.Sample(Vector2.Scale(p, map.Tiling) + map.Offset, TextureSamplingMode.Bilinear, TextureWrapMode.Repeat);
                        int i = y * width + x;
                        pixels[i] = layer.SourceMapIsMask ? new Color(color.r, color.g, color.b, color.a * sample.a) : color * sample;
                        covered[i] = Color.white;
                    }
                });
            }
            finally
            {
                if (owned) loaded.Dispose();
            }
            coverage.SetPixels(covered);
            result.SetPixels(pixels);
            // Only where the decal is counts as used.
            result.Blit(coverage, sourceChannel: TextureChannel.R, destinationChannel: TextureChannel.Usage);
            return result;
        }

        // The layer's mask × (with "source alpha is opacity") its source's alpha plus the offset, ×
        // a decal's coverage; null when none applies.
        private static NordicsTextureWrapper Mask(AtlasSurfaceLayer layer, AtlasUnifiedMaterial material, NordicsTextureWrapper source,
            NordicsTextureWrapper coverage, int width, int height)
        {
            bool alpha = layer.SourceAlphaIsOpacity || layer.SourceMapIsMask;
            if (layer.Mask == null && !alpha && coverage == null) return null;
            var mask = layer.Mask != null ? AtlasMapEvaluator.ScaledMask(layer.Mask, 1f, width, height, material)
                : AtlasMapEvaluator.Constant(AtlasVirtualMap.Metallic, 1f, width, height);
            if (coverage != null) mask.Blit(coverage, blend: TextureBlendMode.Multiply);
            if (!alpha) return mask;
            using (var opacity = AtlasMapEvaluator.Constant(AtlasVirtualMap.Metallic, 1f, width, height))
            {
                AtlasPixels.Map(opacity, source, (o, s) => new Color(s.a, s.a, s.a, o.a));
                if (layer.SourceAlphaOffset != 0f)
                    opacity.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Add,
                        Parameters = { Color = new Color(layer.SourceAlphaOffset, layer.SourceAlphaOffset, layer.SourceAlphaOffset, 0f) } });
                opacity.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Clamp, Parameters = { Amount = 0f, Amount2 = 1f } });
                mask.Blit(opacity, blend: TextureBlendMode.Multiply);
            }
            return mask;
        }

        // The baked adjustments on one target, in the material's order, in one pass; those that
        // change nothing are skipped.
        private static void Adjust(NordicsTextureWrapper map, AtlasUnifiedMaterial source, AtlasAdjustmentTarget target, AtlasLayer layer,
            int width, int height)
        {
            var operations = new List<ColorOperation>();
            var masks = new List<NordicsTextureWrapper>();
            try
            {
                for (int i = 0; i < source.ColorAdjustments.Count; i++)
                {
                    var adjustment = source.ColorAdjustments[i];
                    if (adjustment.Target != target || (target == AtlasAdjustmentTarget.Layer && adjustment.Layer != layer)) continue;
                    if (!CanBake(source, i, adjustment) || IsIdentity(adjustment)) continue;
                    var operation = Operation(adjustment);
                    if (adjustment.Mask != null)
                    {
                        var mask = AtlasMapEvaluator.ScaledMask(adjustment.Mask, 1f, width, height, source);
                        masks.Add(mask);
                        operation.Mask = mask;
                        operation.MaskOptions = new TextureMaskOptions { Channel = TextureChannel.R };
                    }
                    operations.Add(operation);
                }
                if (operations.Count > 0) map.ApplyColor(operations);
            }
            finally
            {
                foreach (var mask in masks) mask.Dispose();
            }
        }

        // An adjustment that leaves every color as it is.
        private static bool IsIdentity(AtlasColorAdjustment adjustment)
        {
            switch (adjustment.Operation)
            {
                case AtlasColorOperation.Hue: return !adjustment.HueSet && Mathf.Approximately(Mathf.Repeat(adjustment.Amount, 1f), 0f);
                case AtlasColorOperation.Saturation:
                case AtlasColorOperation.Contrast:
                case AtlasColorOperation.Brightness:
                case AtlasColorOperation.Power: return Mathf.Approximately(adjustment.Amount, 1f);
                case AtlasColorOperation.Tint: return adjustment.Color.r == 1f && adjustment.Color.g == 1f && adjustment.Color.b == 1f;
                case AtlasColorOperation.Lightness:
                case AtlasColorOperation.Chromatize: return adjustment.Amount == 0f;
                default: return false;
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
