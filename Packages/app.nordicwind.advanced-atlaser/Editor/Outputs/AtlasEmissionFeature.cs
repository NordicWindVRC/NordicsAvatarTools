using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Emission (ATLAS_OUTPUT.md, "Feature inventory"): each source's static emission layers
    // added up into the output's emission map, relative to the strongest source's emission, which
    // the output glows at. Sources without emission are dark. Other features add their glow to
    // the map in full (a baked matcap's glow, glitter points): the map gives them its value ×
    // the strength, and what goes above the map's range goes into the strength
    // (CarryInStrength).
    // An output without an emission map can have the emission drawn into its base color
    // instead. That shows what the emission draws, under light, and doesn't glow in the dark,
    // so it is used only when chosen.
    public sealed class AtlasEmissionFeature : IAtlasOutputFeature
    {
        public static readonly AtlasFeatureMode InColor = new AtlasFeatureMode
        {
            Id = "color", Label = "Drawn into the color", Kind = AtlasFeatureModeKind.Emulated,
            RequiredMaps = new[] { AtlasVirtualMap.BaseColor },
            Requires = c => !c.Provides(AtlasVirtualMap.Emission),
            OptIn = true
        };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Emission map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Emission }
            },
            InColor,
            AtlasFeatureMode.Off
        };

        public string Id => "emission";
        public string DisplayName => "Emission";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        // An emission that glows, or that only AudioLink makes glow (strength 0, AudioLink adding to it).
        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && source.Layers.OfType<AtlasSurfaceLayer>().Any(l => l.Enabled && l.Source.Color.maxColorComponent > 0f &&
                l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission && (t.Opacity > 0f || GlowsWithMusic(source, l))));

        private static bool GlowsWithMusic(AtlasUnifiedMaterial source, AtlasSurfaceLayer layer) =>
            AtlasAudio.Of(source, layer).Any(m => m.Target.EndsWith(".targets[0].opacity") && m.Mode == AtlasModulatorMode.Add && m.Max > 0f);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            if (context.Mode == InColor)
            {
                DrawIntoColor(context, active);
                return;
            }
            Cycling(context, active);
            float strength = active.Max(i => AtlasColorStack.EmissionStrength(job.Sources[i].Material));
            Notes(context, active);
            if (strength <= 0f) return;
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.Emission,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Content = (index, material, width, height) => active.Contains(index)
                    ? AtlasColorStack.EvaluateEmission(material, width, height, strength)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, width, height)
            };
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            // The map is relative to the strength, which the output multiplies it by:
            // contributions work with the full emission.
            request.SharedColor = new Color(strength, strength, strength, 1f);
            context.Maps.Add(request);
            // The output's emission: one layer, the map at the strongest source's strength.
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Map = request.Reference;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            context.Output.Layers.Add(layer);
            CarryInStrength(request, layer.Targets[0]);
            Live(context, request, strength);
        }

        // Each source's emission added to its base color, after the surface's layers. A hue
        // that cycles is drawn at its start and AudioLink at rest. Where nothing takes what
        // the map can't hold (an output without a base color of its own), the sum stops at 1.
        private static void DrawIntoColor(AtlasFeatureContext context, HashSet<int> active)
        {
            Notes(context, active);
            var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            if (request == null)
            {
                context.Notes.Add("Without a base color map the emission has nothing to be drawn into; it's left out.");
                return;
            }
            request.Add(new AtlasMapContribution
            {
                Feature = "emission", Label = "emission drawn into the color", Stage = AtlasMapStage.Overlays,
                Kind = AtlasContributionKind.Bake, CanRaise = true, Applies = active.Contains,
                Apply = content =>
                {
                    bool carried = request.Carry != null;
                    using (var emission = AtlasColorStack.EvaluateEmission(content.Material, content.Width, content.Height, 1f, clamp: false))
                        AtlasPixels.Map(content.Map, emission, (c, e) => carried
                            ? new Color(c.r + e.r, c.g + e.g, c.b + e.b, c.a)
                            : new Color(Mathf.Min(1f, c.r + e.r), Mathf.Min(1f, c.g + e.g), Mathf.Min(1f, c.b + e.b), c.a));
                }
            });
            context.Notes.Add("The emission of " + AtlasLiveMasks.Numbers(active) +
                              " is drawn into the base color: it shows under light and doesn't glow in the dark.");
        }

        /// <summary>Lets an emission map's range go into its strength: where a feature's
        /// contribution takes the baked map above 1, the map is divided by its brightest value
        /// and the strength is multiplied by it.</summary>
        public static void CarryInStrength(AtlasMapRequest request, AtlasLayerTarget target)
        {
            float? strength = null;
            request.Carry = peak =>
            {
                // The strength as the features left it, read when the map is first finished.
                if (strength == null) strength = target.Opacity;
                target.Opacity = strength.Value * peak;
            };
        }

        // Live global masks (AtlasLiveMasks): the live sources' emission with their channels at 0
        // stays in the map; the rest, a color, goes into a second emission map shown through a
        // channel that is 1 before the same live steps.
        private void Live(AtlasFeatureContext context, AtlasMapRequest request, float strength)
        {
            var job = context.Job;
            if (!context.Capabilities.Provides(AtlasVirtualMap.EmissionLive)) return;
            var split = AtlasLiveMasks.Plan(context, Id, "emission", i => job.Sources[i].Material.Layers.OfType<AtlasSurfaceLayer>()
                .Where(l => l.Enabled && l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission)).SelectMany(l => AtlasLiveMasks.Of(l.Mask)));
            if (split == null) return;
            int channel = AtlasLiveMasks.Unit(context, split, "emission");
            if (channel < 0) return;
            var inner = AtlasLiveMasks.OwnPart(request, split);
            var live = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.EmissionLive,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Content = (i, material, width, height) => AtlasLiveMasks.Difference(split, i, material, width, height, inner, false, color: true)
            };
            for (int i = 0; i < job.Sources.Count; i++) live.Sources.Add(i);
            context.Maps.Add(live);
            AtlasLiveMasks.Kept(context, split);
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission, Mask = new AtlasMask { Bus = channel, BusBlend = AtlasMaskBlend.Multiply } };
            layer.Source.Map = live.Reference;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            context.Output.Layers.Add(layer);
        }

        // Emissions whose hue cycles (AtlasHueCycling) or that AudioLink drives (AtlasAudio):
        // each setup in an emission slot of its own (past the baked and the live global mask
        // ones), the map holding those emissions at their start and at rest, the slot turning
        // their hue and carrying their AudioLink. The rest stay in the map, at their start and
        // at rest.
        private void Cycling(AtlasFeatureContext context, HashSet<int> active)
        {
            var job = context.Job;
            var found = new List<(int Source, AtlasSurfaceLayer Layer, AtlasColorAdjustment Adjustment, AtlasModulator Turn, string Audio)>();
            foreach (int i in active)
            {
                var material = job.Sources[i].Material;
                for (int k = 0; k < material.Layers.Count; k++)
                {
                    if (!(material.Layers[k] is AtlasSurfaceLayer layer) || !layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) ||
                        !AtlasColorStack.CanBakeEmission(material, k, layer)) continue;
                    var cycling = AtlasHueCycling.Of(material, AtlasAdjustmentTarget.Layer, layer).FirstOrDefault();
                    string audio = AtlasAudio.Signature(material, layer);
                    if (cycling.Turn != null || audio.Length > 0) found.Add((i, layer, cycling.Adjustment, cycling.Turn, audio));
                }
            }
            if (found.Count == 0) return;
            int free = Mathf.Max(0, context.Capabilities.EmissionSlots - 2);
            // AudioLink adds to an emission's own strength, so one slot holds emissions of one strength.
            var groups = found.GroupBy(f => (f.Turn != null ? AtlasHueCycling.Key(f.Adjustment, f.Turn) : "-") + "#" + f.Audio +
                                            (f.Audio.Length > 0 ? "@" + f.Layer.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity : ""))
                .OrderByDescending(g => g.Select(f => f.Source).Distinct().Sum(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height)).ToList();
            var still = new List<int>();
            // Slots whose maps come out the same (emissions that differ only in their AudioLink
            // band, say) share one map.
            var made = new Dictionary<string, AtlasTextureReference>();
            for (int g = 0; g < groups.Count; g++)
            {
                if (g >= free || g >= 2)
                {
                    still.AddRange(groups[g].Select(f => f.Source));
                    continue;
                }
                var layers = groups[g].GroupBy(f => f.Source).ToDictionary(s => s.Key, s => (ICollection<AtlasLayer>)new HashSet<AtlasLayer>(s.Select(f => (AtlasLayer)f.Layer)));
                foreach (var entry in layers)
                    foreach (var layer in entry.Value) AtlasLiveLayers.Add(job.Sources[entry.Key].Material, layer);
                // With AudioLink: the map holds the emission at unit strength (÷ its brightest color,
                // which the slot's color takes), and the slot has the emission's own strength,
                // which AudioLink adds to; otherwise the map is ÷ the slot's strength.
                bool audio = groups[g].First().Audio.Length > 0;
                float peak = groups[g].Max(f => f.Layer.Source.Color.maxColorComponent);
                float strength = audio ? groups[g].First().Layer.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity
                    : layers.Max(e => AtlasColorStack.EmissionStrength(job.Sources[e.Key].Material, e.Value));
                if (audio ? peak <= 0f : strength <= 0f) continue;
                var slot = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                string signature = MapSignature(job, layers, audio, peak, strength);
                if (made.TryGetValue(signature, out var shared)) slot.Source.Map = shared;
                else slot.Source.Map = made[signature] = Request(context, g == 0 ? AtlasVirtualMap.EmissionCycle1 : AtlasVirtualMap.EmissionCycle2,
                    layers, audio, peak, strength);
                slot.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
                if (audio) slot.Source.Color = new Color(peak, peak, peak, 1f);
                context.Output.Layers.Add(slot);
                var first = groups[g].First();
                if (first.Turn != null) AtlasHueCycling.Add(context.Output, AtlasAdjustmentTarget.Layer, slot, first.Adjustment, first.Turn, null);
                AtlasAudio.CopyLayerAudio(context.Output, job.Sources[first.Source].Material, first.Layer, slot);
            }
            if (still.Count > 0)
                context.Notes.Add("The emission's hue cycling or AudioLink of " + AtlasLiveMasks.Numbers(still.Distinct()) + " stays at its start and at rest" +
                                  (free > 0 ? ": no emission slot is left for it." : ": the output shader has no emission slot for it."));
        }

        // A slot's map: each source's emissions of the slot (an AudioLink slot's at unit strength
        // over their brightest color; otherwise over the slot's strength), dark elsewhere.
        private static AtlasTextureReference Request(AtlasFeatureContext context, AtlasVirtualMap map,
            Dictionary<int, ICollection<AtlasLayer>> layers, bool audio, float peak, float strength)
        {
            var job = context.Job;
            var request = new AtlasMapRequest
            {
                Map = map,
                    Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                    Content = (index, material, width, height) => layers.TryGetValue(index, out var only)
                        ? audio ? AtlasColorStack.EvaluateEmission(material, width, height, peak, only, unitStrength: true)
                            : AtlasColorStack.EvaluateEmission(material, width, height, strength, only)
                        : AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, width, height)
                };
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            return request.Reference;
        }

        // What a slot's map holds, equal for slots whose maps come out the same: per source, its
        // emission layers with what changes them as they're baked (their hue adjustments and
        // modulators). An AudioLink slot's map leaves out their strengths, which the slot holds.
        private static string MapSignature(AtlasGeneratorJob job, Dictionary<int, ICollection<AtlasLayer>> layers, bool audio, float peak, float strength)
        {
            var text = new System.Text.StringBuilder(audio ? "unit/" + AtlasFieldwise.Signature(peak) : "/" + AtlasFieldwise.Signature(strength));
            foreach (var entry in layers.OrderBy(e => e.Key))
            {
                var material = job.Sources[entry.Key].Material;
                text.Append('|').Append(entry.Key).Append(':');
                foreach (string layer in entry.Value.Select(l => LayerSignature(material, l, audio)).OrderBy(s => s, System.StringComparer.Ordinal))
                    text.Append(layer).Append(',');
            }
            return text.ToString();
        }

        private static string LayerSignature(AtlasUnifiedMaterial material, AtlasLayer layer, bool unitStrength)
        {
            string path = "layers[" + material.Layers.IndexOf(layer) + "]";
            var text = new System.Text.StringBuilder(unitStrength ? AtlasFieldwise.Signature(layer, "Targets") : AtlasFieldwise.Signature(layer));
            foreach (var adjustment in material.ColorAdjustments.Where(a => a.Layer == layer))
                text.Append(AtlasFieldwise.Signature(adjustment, "Layer"));
            foreach (var modulator in material.Modulators.Where(m => m.Target != null && m.Target.StartsWith(path) &&
                                                                     !(unitStrength && m.Target.StartsWith(path + ".targets["))))
                text.Append(modulator.Target.Substring(path.Length)).Append(AtlasFieldwise.Signature(modulator, "Target"));
            return text.ToString();
        }

        /// <summary>The output's emission layers whose hue cycles, in slot order.</summary>
        public static List<AtlasSurfaceLayer> CyclingOf(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                (AtlasHueCycling.LayerHue(output, l).Turn != null || AtlasAudio.HasAudio(output, l)) && AtlasAudioLinkFeature.Of(output) != l).ToList();

        // The output's live emission layer (shown through a global mask channel), if any.
        public static AtlasSurfaceLayer LiveOf(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                l.Mask != null && l.Mask.Bus >= 0);

        /// <summary>The output has an emission map and the emission isn't turned off.</summary>
        public static bool On(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            var feature = AtlasOutputFeatures.Find("emission");
            var choice = job.Features.Find(f => f.FeatureId == "emission");
            return capabilities.Provides(AtlasVirtualMap.Emission) &&
                   (feature == null || AtlasOutputFeatures.Effective(feature, choice?.ModeId, capabilities)?.Kind != AtlasFeatureModeKind.Off);
        }

        /// <summary>The plan's emission map and its layer's emission target, for a feature that
        /// adds to the emission after this one resolved: this feature's map, or one made here,
        /// dark, where no source has an emission. The target is null where the map has no
        /// layer of its own.</summary>
        public static AtlasMapRequest Map(AtlasGeneratorJob job, AtlasOutputPlan plan, out AtlasLayerTarget target)
        {
            var request = plan.Maps.FirstOrDefault(r => r.Map == AtlasVirtualMap.Emission);
            AtlasSurfaceLayer output;
            if (request == null)
            {
                request = new AtlasMapRequest
                {
                    Map = AtlasVirtualMap.Emission,
                    Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                    Content = (i, m, w, h) => AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, w, h)
                };
                for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
                plan.Maps.Add(request);
                output = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                output.Source.Map = request.Reference;
                output.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
                plan.Output.Layers.Add(output);
            }
            else output = plan.Output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Source.Map == request.Reference);
            target = output?.Targets.FirstOrDefault(t => t.Target == AtlasSurfaceTarget.Emission);
            return request;
        }

        // The output's emission layer, if any.
        public static AtlasSurfaceLayer Of(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                (l.Mask == null || l.Mask.Bus < 0) && AtlasHueCycling.LayerHue(output, l).Turn == null && !AtlasAudio.HasAudio(output, l));

        private static void Notes(AtlasFeatureContext context, IEnumerable<int> active)
        {
            foreach (int i in active)
            {
                var material = context.Job.Sources[i].Material;
                for (int k = 0; k < material.Layers.Count; k++)
                {
                    if (!(material.Layers[k] is AtlasSurfaceLayer layer) || !layer.Enabled) continue;
                    // Driven by AudioLink alone: the AudioLink feature's (it lists what it doesn't keep).
                    string path = "layers[" + k + "]";
                    bool audioOnly = !material.Modulators.Any(m => !AtlasAudio.IsAudio(m) && m.Target != null &&
                                                                   (m.Target == path || m.Target.StartsWith(path + ".") || m.Target.StartsWith(path + "[")));
                    if (layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) && !AtlasColorStack.CanBakeEmission(material, k, layer) &&
                        !(audioOnly && AtlasColorStack.CanBakeEmission(material, k, layer, anyModulators: true)))
                    {
                        context.Notes.Add("An emission that is live (blinking, scrolling, audio, light based), follows transparency, or is masked by a global mask written by layers or the second pass isn't baked yet; it's left out.");
                        return;
                    }
                }
            }
        }
    }
}
