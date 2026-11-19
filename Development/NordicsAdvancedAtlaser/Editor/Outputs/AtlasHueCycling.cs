using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Hue cycling: a hue adjustment whose amount a time modulator turns round (a saw adding 0–1
    // turns at a frequency; a reverse saw turns the other way). The bakes take it at its start
    // (its amount alone); outputs that cycle a hue keep the turning live on top (a hue turns
    // round, so the start baked in and the turn added live give the same hue).
    public static class AtlasHueCycling
    {
        /// <summary>The turning modulator of the adjustment at <paramref name="index"/>, when it
        /// is a hue adjustment whose only modulator is that turn; otherwise null.</summary>
        public static AtlasModulator Turn(AtlasUnifiedMaterial material, int index)
        {
            if (material == null || index < 0 || index >= material.ColorAdjustments.Count) return null;
            if (material.ColorAdjustments[index].Operation != AtlasColorOperation.Hue) return null;
            string path = "colorAdjustments[" + index + "]";
            var modulators = material.Modulators.Where(m => m.Target != null && (m.Target == path || m.Target.StartsWith(path + ".") || m.Target.StartsWith(path + "["))).ToList();
            if (modulators.Count != 1) return null;
            var turn = modulators[0];
            bool saw = turn.Time.Waveform == AtlasWaveform.Saw || turn.Time.Waveform == AtlasWaveform.ReverseSaw;
            return turn.Target == path + ".amount" && turn.Source == AtlasModulatorSource.Time && turn.Mode == AtlasModulatorMode.Add &&
                   saw && Mathf.Approximately(turn.Min, 0f) && Mathf.Approximately(turn.Max, 1f) ? turn : null;
        }

        public static bool IsCycling(AtlasUnifiedMaterial material, int index) => Turn(material, index) != null;

        /// <summary>Turns per second, signed.</summary>
        public static float Rate(AtlasModulator turn) =>
            turn.Time.Frequency * (turn.Time.Waveform == AtlasWaveform.ReverseSaw ? -1f : 1f);

        // The cycling hue adjustments of a target (and layer), with their indices.
        public static IEnumerable<(int Index, AtlasColorAdjustment Adjustment, AtlasModulator Turn)> Of(AtlasUnifiedMaterial material,
            AtlasAdjustmentTarget target, AtlasLayer layer = null)
        {
            if (material == null) yield break;
            for (int i = 0; i < material.ColorAdjustments.Count; i++)
            {
                var adjustment = material.ColorAdjustments[i];
                if (adjustment.Target != target || (layer != null && adjustment.Layer != layer)) continue;
                var turn = Turn(material, i);
                if (turn != null) yield return (i, adjustment, turn);
            }
        }

        // What must match for sources to share one cycling: its rate and how it turns.
        public static string Key(AtlasColorAdjustment adjustment, AtlasModulator turn) =>
            Rate(turn).ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) + "|" + adjustment.HueMethod;

        /// <summary>Adds a cycling hue to the output: amount 0 (the start is baked), turning at
        /// the rate. Returns the adjustment.</summary>
        public static AtlasColorAdjustment Add(AtlasUnifiedMaterial output, AtlasAdjustmentTarget target, AtlasLayer layer,
            AtlasColorAdjustment like, AtlasModulator turn, AtlasMask mask)
        {
            var adjustment = new AtlasColorAdjustment
            {
                Target = target, Layer = layer, Operation = AtlasColorOperation.Hue, Amount = 0f, HueMethod = like.HueMethod, Mask = mask,
                IncludeEmission = like.IncludeEmission
            };
            output.ColorAdjustments.Add(adjustment);
            var copy = new AtlasModulator();
            AtlasFieldwise.Copy(turn, copy);
            copy.Target = "colorAdjustments[" + (output.ColorAdjustments.Count - 1) + "].amount";
            copy.Mask = null;
            output.Modulators.Add(copy);
            return adjustment;
        }

        /// <summary>Copies a source layer's hue adjustments (cycling or not) onto its output copy,
        /// for outputs whose slot has a hue shift of its own (matcaps, rims, glitter).</summary>
        public static void CopyLayerHue(AtlasUnifiedMaterial output, AtlasUnifiedMaterial source, AtlasLayer from, AtlasLayer to)
        {
            if (source == null) return;
            for (int i = 0; i < source.ColorAdjustments.Count; i++)
            {
                var adjustment = source.ColorAdjustments[i];
                if (adjustment.Target != AtlasAdjustmentTarget.Layer || adjustment.Layer != from || adjustment.Operation != AtlasColorOperation.Hue) continue;
                var turn = Turn(source, i);
                if (turn != null)
                {
                    var added = Add(output, AtlasAdjustmentTarget.Layer, to, adjustment, turn, null);
                    added.Amount = adjustment.Amount;
                    added.HueSet = adjustment.HueSet;
                }
                else if (AtlasColorStack.IsStatic(source, "colorAdjustments[" + i + "]"))
                {
                    var copy = new AtlasColorAdjustment();
                    AtlasFieldwise.Copy(adjustment, copy);
                    copy.Layer = to;
                    copy.Mask = null;
                    output.ColorAdjustments.Add(copy);
                }
            }
        }

        /// <summary>The output's hue adjustment of a layer and its turn (null when it doesn't turn).</summary>
        public static (AtlasColorAdjustment Adjustment, AtlasModulator Turn) LayerHue(AtlasUnifiedMaterial output, AtlasLayer layer)
        {
            for (int i = 0; i < output.ColorAdjustments.Count; i++)
                if (output.ColorAdjustments[i].Target == AtlasAdjustmentTarget.Layer && output.ColorAdjustments[i].Layer == layer &&
                    output.ColorAdjustments[i].Operation == AtlasColorOperation.Hue)
                    return (output.ColorAdjustments[i], Turn(output, i));
            return (null, null);
        }
    }

    // Hue cycling of the base color and of the final color (ATLAS_OUTPUT.md, "Feature
    // inventory"): one rate from the reference source (Hue Cycling from); each source's start
    // is baked into its colors, and its hue mask goes into a mask, 0 where it doesn't cycle, so
    // the rest of the atlas stays still. Sources cycling at another rate stay at their start.
    public sealed class AtlasHueCyclingFeature : IAtlasSharedValueFeature
    {
        public const string FeatureId = "huecycling";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native, OptionalMaps = new[] { AtlasVirtualMap.HueCyclingMask, AtlasVirtualMap.Mask1 },
                Requires = c => c.HueCycling
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Hue Cycling";
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public int Order => 3;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Hue Cycling";

        private static (int Index, AtlasColorAdjustment Adjustment, AtlasModulator Turn)? First(AtlasUnifiedMaterial material, AtlasAdjustmentTarget target)
        {
            foreach (var cycling in AtlasHueCycling.Of(material, target)) return cycling;
            return null;
        }

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            First(source, AtlasAdjustmentTarget.BaseColor) != null || First(source, AtlasAdjustmentTarget.FinalColor) != null;

        private static string Setup(AtlasUnifiedMaterial material)
        {
            var color = First(material, AtlasAdjustmentTarget.BaseColor);
            var final = First(material, AtlasAdjustmentTarget.FinalColor);
            return (color.HasValue ? AtlasHueCycling.Key(color.Value.Adjustment, color.Value.Turn) : "-") + "/" +
                   (final.HasValue ? AtlasHueCycling.Key(final.Value.Adjustment, final.Value.Turn) : "-");
        }

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Where(IsPresent).Select(Setup).Distinct().Count() > 1;

        public void Resolve(AtlasFeatureContext context)
        {
            Cycle(context, AtlasAdjustmentTarget.BaseColor, "base color");
            Cycle(context, AtlasAdjustmentTarget.FinalColor, "final color");
        }

        private static void Cycle(AtlasFeatureContext context, AtlasAdjustmentTarget target, string name)
        {
            var job = context.Job;
            var cycling = context.Sources.Select(i => (Index: i, Found: First(job.Sources[i].Material, target)))
                .Where(c => c.Found.HasValue).ToDictionary(c => c.Index, c => c.Found.Value);
            if (cycling.Count == 0) return;
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            int reference = choice != null && cycling.ContainsKey(choice.ReferenceSource) ? choice.ReferenceSource
                : cycling.Keys.OrderByDescending(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height).ThenBy(i => i).First();
            var template = cycling[reference];
            string key = AtlasHueCycling.Key(template.Adjustment, template.Turn);
            var kept = cycling.Where(c => AtlasHueCycling.Key(c.Value.Adjustment, c.Value.Turn) == key).ToDictionary(c => c.Key, c => c.Value);
            var still = cycling.Keys.Where(i => !kept.ContainsKey(i)).ToList();
            if (still.Count > 0)
                context.Notes.Add($"The {name}'s hue cycling of " + AtlasLiveMasks.Numbers(still) + " turns at another rate than source " +
                                  (reference + 1) + "'s; it stays at its start (Hue Cycling from picks the rate).");

            // Each source's hue mask, 0 where it doesn't cycle.
            var keys = Enumerable.Range(0, job.Sources.Count).Select(i => kept.TryGetValue(i, out var c)
                ? AtlasMaskKeys.Of(c.Adjustment.Mask, 1f, job.Sources[i].Material) : AtlasMaskKeys.Constant(0f)).ToList();
            NordicWind.TextureWrapper.NordicsTextureWrapper Evaluate(int i, AtlasUnifiedMaterial material, int w, int h) =>
                kept.TryGetValue(i, out var c)
                    ? AtlasMapEvaluator.ScaledMask(c.Adjustment.Mask, 1f, w, h, material)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.HueCyclingMask, 0f, w, h);
            AtlasMask mask = null;
            bool everywhere = keys.All(k => k == AtlasMaskKeys.Constant(1f));
            if (!everywhere)
            {
                if (target == AtlasAdjustmentTarget.BaseColor && context.Capabilities.Provides(AtlasVirtualMap.HueCyclingMask))
                {
                    var request = new AtlasMapRequest
                    {
                        Map = AtlasVirtualMap.HueCyclingMask,
                        Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                        Evaluate = Evaluate
                    };
                    for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
                    context.Maps.Add(request);
                    mask = new AtlasMask { Map = request.Reference, Channel = AtlasChannel.Red };
                }
                else
                {
                    var allocated = AtlasMaskAllocator.Allocate(context, new AtlasMaskRequest
                    {
                        Name = name + " hue cycling", CanInvert = true, Key = i => keys[i], Evaluate = Evaluate
                    });
                    if (allocated == null || allocated.IsConstant)
                    {
                        if (allocated == null)
                            context.Notes.Add($"No mask place is left for the {name}'s hue cycling; it stays at its start.");
                        if (allocated == null || allocated.Constant <= 0f) return;
                    }
                    else mask = allocated.Mask;
                }
            }
            var added = AtlasHueCycling.Add(context.Output, target, null, template.Adjustment, template.Turn, mask);
            added.HueSet = false;
            if (target == AtlasAdjustmentTarget.BaseColor)
            {
                var layered = kept.Keys.Where(i => job.Sources[i].Material.Layers.OfType<AtlasSurfaceLayer>()
                    .Any(l => l.Enabled && l.Targets.Any(t => t.Target == AtlasSurfaceTarget.BaseColor))).ToList();
                if (layered.Count > 0)
                    context.Notes.Add("The base color's hue cycling turns the color layers baked over it too (" + AtlasLiveMasks.Numbers(layered) + ").");
            }
        }
    }
}
