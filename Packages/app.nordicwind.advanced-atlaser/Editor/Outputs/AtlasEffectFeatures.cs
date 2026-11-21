using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Subsurface scattering (ATLAS_OUTPUT.md, "Feature inventory"): light through thin parts.
    // Native only: it depends too much on the light to bake. One setup from the reference source
    // (Subsurface from); each source's color × strength (relative to the strongest; black where
    // a source has none, which turns it off there) goes into the subsurface color map, and its
    // thickness into the subsurface thickness map, relative to the output's offset.
    public sealed class AtlasSubsurfaceFeature : IAtlasSharedValueFeature
    {
        public const string FeatureId = "subsurface";
        private static readonly string[] PerSource = { "Mask", "Color", "Thickness", "ThicknessChannel", "ThicknessOffset", "Intensity", "Enabled" };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.SubsurfaceColor, AtlasVirtualMap.SubsurfaceThickness }
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Subsurface Scattering";
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public int Order => 1;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Subsurface";

        public static AtlasSubsurfaceLayer Layer(AtlasUnifiedMaterial material)
        {
            if (material == null) return null;
            for (int i = 0; i < material.Layers.Count; i++)
                if (material.Layers[i] is AtlasSubsurfaceLayer layer && layer.Enabled && AtlasColorStack.IsStatic(material, "layers[" + i + "]"))
                    return layer;
            return null;
        }

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && source.Layers.OfType<AtlasSubsurfaceLayer>().Any(l => l.Enabled);

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Select(Layer).Where(l => l != null).Select(l => AtlasFieldwise.Signature(l, PerSource)).Distinct().Count() > 1;

        // A still map on UV0 the bake can sample.
        private static bool Bakeable(AtlasTextureReference map) =>
            map?.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Scroll == Vector2.zero && map.Decal == null && map.Frames == null;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var layers = context.Sources.Select(i => (Index: i, Layer: Layer(job.Sources[i].Material))).Where(s => s.Layer != null).ToDictionary(s => s.Index, s => s.Layer);
            var live = context.Sources.Where(i => !layers.ContainsKey(i)).ToList();
            if (live.Count > 0)
                context.Notes.Add("Live subsurface scattering (animated, audio-driven) isn't merged yet; it's left out (" + AtlasLiveMasks.Numbers(live) + ").");
            if (layers.Count == 0) return;
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            int reference = choice != null && layers.ContainsKey(choice.ReferenceSource) ? choice.ReferenceSource
                : layers.Keys.OrderByDescending(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height).ThenBy(i => i).First();
            var template = layers[reference];
            float strongest = layers.Values.Max(l => l.Intensity);
            if (strongest <= 0f) return;
            // The output's offset: the smallest, so every source's thickness + its offset − it is ≥ 0.
            float offset = layers.Values.Min(l => l.ThicknessOffset);

            var output = new AtlasSubsurfaceLayer();
            AtlasFieldwise.Copy(template, output);
            output.Mask = null;
            output.Intensity = strongest;
            output.ThicknessOffset = offset;
            output.ThicknessPower = 1f;
            output.Color.Color = Color.white;

            var color = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.SubsurfaceColor,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Content = (i, material, w, h) =>
                {
                    if (!layers.TryGetValue(i, out var layer)) return AtlasMapEvaluator.Constant(AtlasVirtualMap.SubsurfaceColor, 0f, w, h);
                    var c = layer.Color.Color * (layer.Intensity / strongest);
                    var result = Bakeable(layer.Color.Map)
                        ? AtlasMapEvaluator.SampleColor(layer.Color.Map, (Texture2D)layer.Color.Map.Texture, w, h)
                        : AtlasMapEvaluator.Constant(AtlasVirtualMap.SubsurfaceColor, 1f, w, h);
                    if (layer.Mask != null)
                        using (var mask = AtlasMapEvaluator.ScaledMask(layer.Mask, 1f, w, h, material))
                            AtlasPixels.Map(result, mask, (p, m) => new Color(p.r * m.r, p.g * m.r, p.b * m.r, p.a));
                    AtlasPixels.Map(result, p => new Color(p.r * c.r, p.g * c.g, p.b * c.b, 1f));
                    return result;
                },
                // Colors above 1: the map's brightest value goes into the layer's color.
                ContentCanRaise = true,
                Carry = peak => output.Color.Color = new Color(peak, peak, peak, 1f)
            };
            var thickness = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.SubsurfaceThickness,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                Content = (i, material, w, h) =>
                {
                    if (!layers.TryGetValue(i, out var layer)) return AtlasMapEvaluator.Constant(AtlasVirtualMap.SubsurfaceThickness, 1f, w, h);
                    var input = new AtlasScalarInput(1f) { Map = Bakeable(layer.Thickness) ? layer.Thickness : null, Channel = layer.ThicknessChannel };
                    var result = layer.Thickness == null || input.Map == null
                        ? AtlasMapEvaluator.Constant(AtlasVirtualMap.SubsurfaceThickness, 0f, w, h)
                        : AtlasMapEvaluator.Scalar(input, w, h, material);
                    float shift = layer.ThicknessOffset - offset;
                    AtlasPixels.Map(result, p =>
                    {
                        float v = Mathf.Clamp01(p.r + shift);
                        return new Color(v, v, v, 1f);
                    });
                    return result;
                }
            };
            for (int i = 0; i < job.Sources.Count; i++)
            {
                color.Sources.Add(i);
                thickness.Sources.Add(i);
            }
            context.Maps.Add(color);
            context.Maps.Add(thickness);
            output.Color.Map = color.Reference;
            output.Thickness = thickness.Reference;
            output.ThicknessChannel = AtlasChannel.Alpha;
            context.Output.Layers.Add(output);

            string used = AtlasFieldwise.Signature(template, PerSource);
            var differ = layers.Where(l => AtlasFieldwise.Signature(l.Value, PerSource) != used).Select(l => l.Key).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"Subsurface scattering is taken from source {reference + 1}; the subsurface scattering of " + AtlasLiveMasks.Numbers(differ) + " differs.");
            var approximated = layers.Where(l => l.Value.ThicknessPower != 1f || l.Value.Ambient != 0f || l.Value.Light != AtlasSubsurfaceLight.Direct)
                .Select(l => l.Key).ToList();
            if (approximated.Count > 0)
                context.Notes.Add("The output shader's subsurface scattering has no thickness power, ambient part or light from the ambient; " +
                                  AtlasLiveMasks.Numbers(approximated) + " use them, so they look different.");
            var unbaked = layers.Where(l => (l.Value.Color.Map != null && !Bakeable(l.Value.Color.Map)) || (l.Value.Thickness != null && !Bakeable(l.Value.Thickness)))
                .Select(l => l.Key).ToList();
            if (unbaked.Count > 0)
                context.Notes.Add("A subsurface color or thickness map on another UV or moving isn't baked; its color and full thickness are used (" + AtlasLiveMasks.Numbers(unbaked) + ").");
        }
    }

    // Proximity color (ATLAS_OUTPUT.md, "Feature inventory"): the final color and or the
    // emission tinted by a color that blends from a near to a far one with the camera's
    // distance (a tint adjustment and its camera distance modulator). One setup from the
    // reference source (Proximity Color from). The output's has no mask, so it covers the whole
    // atlas, which is listed when some sources don't have it; Off is the choice then.
    public sealed class AtlasProximityColorFeature : IAtlasSharedValueFeature
    {
        public const string FeatureId = "proximitycolor";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode { Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native, Requires = c => c.ProximityColor },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Proximity Color";
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public int Order => 2;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Proximity color";

        // The source's proximity tints: tint adjustments of the final color or emission whose
        // color a camera distance modulator blends to another.
        public static List<(AtlasColorAdjustment Adjustment, AtlasModulator Modulator)> Tints(AtlasUnifiedMaterial material)
        {
            var result = new List<(AtlasColorAdjustment, AtlasModulator)>();
            if (material == null) return result;
            for (int i = 0; i < material.ColorAdjustments.Count; i++)
            {
                var adjustment = material.ColorAdjustments[i];
                if (adjustment.Operation != AtlasColorOperation.Tint ||
                    (adjustment.Target != AtlasAdjustmentTarget.FinalColor && adjustment.Target != AtlasAdjustmentTarget.Emission)) continue;
                string path = "colorAdjustments[" + i + "].color";
                var modulator = material.Modulators.FirstOrDefault(m => m.Target == path && m.Source == AtlasModulatorSource.CameraDistance &&
                                                                       m.Mode == AtlasModulatorMode.BlendToColor);
                if (modulator != null) result.Add((adjustment, modulator));
            }
            return result;
        }

        public bool IsPresent(AtlasUnifiedMaterial source) => Tints(source).Count > 0;

        private static string Setup(AtlasUnifiedMaterial material) =>
            string.Join("|", Tints(material).Select(t => AtlasFieldwise.Signature(t.Adjustment) + AtlasFieldwise.Signature(t.Modulator, "Target")));

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Where(IsPresent).Select(Setup).Distinct().Count() > 1;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            int reference = choice != null && context.Sources.Contains(choice.ReferenceSource) ? choice.ReferenceSource
                : context.Sources.OrderByDescending(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height).ThenBy(i => i).First();
            var output = context.Output;
            foreach (var (adjustment, modulator) in Tints(job.Sources[reference].Material))
            {
                var tint = new AtlasColorAdjustment();
                AtlasFieldwise.Copy(adjustment, tint);
                tint.Mask = null;
                tint.Layer = null;
                output.ColorAdjustments.Add(tint);
                var distance = new AtlasModulator();
                AtlasFieldwise.Copy(modulator, distance);
                distance.Target = "colorAdjustments[" + (output.ColorAdjustments.Count - 1) + "].color";
                distance.Mask = null;
                output.Modulators.Add(distance);
            }
            string used = Setup(job.Sources[reference].Material);
            var differ = context.Sources.Where(i => Setup(job.Sources[i].Material) != used).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"Proximity color is taken from source {reference + 1}; the proximity color of " + AtlasLiveMasks.Numbers(differ) + " differs.");
            var without = Enumerable.Range(0, job.Sources.Count).Where(i => !context.Sources.Contains(i)).ToList();
            if (without.Count > 0)
                context.Notes.Add("The output shader's proximity color has no mask, so it covers the whole atlas, " + AtlasLiveMasks.Numbers(without) +
                                  " included, which didn't have it (Proximity Color: Off leaves it out).");
        }

        /// <summary>The output's proximity tints (for the writers).</summary>
        public static List<(AtlasColorAdjustment Adjustment, AtlasModulator Modulator)> Of(AtlasUnifiedMaterial output) => Tints(output);
    }
}
