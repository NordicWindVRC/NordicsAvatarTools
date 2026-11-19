using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Emission (ATLAS_OUTPUT.md, "Feature inventory"): each source's static emission layers
    // added up into the output's emission map, relative to the strongest source's emission, which
    // the output glows at. Sources without emission are dark. Only the native mode: emission
    // baked into the color wouldn't glow.
    public sealed class AtlasEmissionFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Emission map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Emission }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "emission";
        public string DisplayName => "Emission";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && source.Layers.OfType<AtlasSurfaceLayer>().Any(l => l.Enabled &&
                l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission && t.Opacity > 0f) && l.Source.Color.maxColorComponent > 0f);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            Cycling(context, active);
            float strength = active.Max(i => AtlasColorStack.EmissionStrength(job.Sources[i].Material));
            Notes(context, active);
            if (strength <= 0f) return;
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.Emission,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Evaluate = (index, material, width, height) => active.Contains(index)
                    ? AtlasColorStack.EvaluateEmission(material, width, height, strength)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, width, height)
            };
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            // The output's emission: one layer, the map at the strongest source's strength.
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Map = request.Reference;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            context.Output.Layers.Add(layer);
            Live(context, request, strength);
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
            var inner = request.Evaluate;
            var live = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.EmissionLive,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Evaluate = (i, material, width, height) => AtlasLiveMasks.Difference(split, i, material, width, height, inner, false, color: true)
            };
            for (int i = 0; i < job.Sources.Count; i++) live.Sources.Add(i);
            context.Maps.Add(live);
            request.Evaluate = AtlasLiveMasks.Own(split, inner);
            AtlasLiveMasks.Kept(context, split);
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission, Mask = new AtlasMask { Bus = channel, BusBlend = AtlasMaskBlend.Multiply } };
            layer.Source.Map = live.Reference;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            context.Output.Layers.Add(layer);
        }

        // Emissions whose hue cycles (AtlasHueCycling): each rate in an emission slot of its own
        // (past the baked and the live global mask ones), the map holding those emissions at
        // their start, the slot turning their hue. The rest stay at their start in the map.
        private void Cycling(AtlasFeatureContext context, HashSet<int> active)
        {
            var job = context.Job;
            var found = new List<(int Source, AtlasSurfaceLayer Layer, AtlasColorAdjustment Adjustment, AtlasModulator Turn)>();
            foreach (int i in active)
            {
                var material = job.Sources[i].Material;
                for (int k = 0; k < material.Layers.Count; k++)
                {
                    if (!(material.Layers[k] is AtlasSurfaceLayer layer) || !layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) ||
                        !AtlasColorStack.CanBakeEmission(material, k, layer)) continue;
                    foreach (var cycling in AtlasHueCycling.Of(material, AtlasAdjustmentTarget.Layer, layer))
                    {
                        found.Add((i, layer, cycling.Adjustment, cycling.Turn));
                        break;
                    }
                }
            }
            if (found.Count == 0) return;
            int free = context.Capabilities.HueCycling ? context.Capabilities.EmissionSlots - 2 : 0;
            var groups = found.GroupBy(f => AtlasHueCycling.Key(f.Adjustment, f.Turn))
                .OrderByDescending(g => g.Select(f => f.Source).Distinct().Sum(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height)).ToList();
            var still = new List<int>();
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
                float strength = layers.Max(e => AtlasColorStack.EmissionStrength(job.Sources[e.Key].Material, e.Value));
                if (strength <= 0f) continue;
                var request = new AtlasMapRequest
                {
                    Map = g == 0 ? AtlasVirtualMap.EmissionCycle1 : AtlasVirtualMap.EmissionCycle2,
                    Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                    Evaluate = (index, material, width, height) => layers.TryGetValue(index, out var only)
                        ? AtlasColorStack.EvaluateEmission(material, width, height, strength, only)
                        : AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, width, height)
                };
                for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
                context.Maps.Add(request);
                var slot = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                slot.Source.Map = request.Reference;
                slot.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
                context.Output.Layers.Add(slot);
                var first = groups[g].First();
                AtlasHueCycling.Add(context.Output, AtlasAdjustmentTarget.Layer, slot, first.Adjustment, first.Turn, null);
            }
            if (still.Count > 0)
                context.Notes.Add("The emission's hue cycling of " + AtlasLiveMasks.Numbers(still.Distinct()) + " stays at its start" +
                                  (context.Capabilities.HueCycling ? ": no emission slot is left for it." : ": the output shader has no hue cycling."));
        }

        /// <summary>The output's emission layers whose hue cycles, in slot order.</summary>
        public static List<AtlasSurfaceLayer> CyclingOf(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                AtlasHueCycling.LayerHue(output, l).Turn != null).ToList();

        // The output's live emission layer (shown through a global mask channel), if any.
        public static AtlasSurfaceLayer LiveOf(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                l.Mask != null && l.Mask.Bus >= 0);

        // The output's emission layer, if any.
        public static AtlasSurfaceLayer Of(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                (l.Mask == null || l.Mask.Bus < 0) && AtlasHueCycling.LayerHue(output, l).Turn == null);

        private static void Notes(AtlasFeatureContext context, IEnumerable<int> active)
        {
            foreach (int i in active)
            {
                var material = context.Job.Sources[i].Material;
                for (int k = 0; k < material.Layers.Count; k++)
                {
                    if (!(material.Layers[k] is AtlasSurfaceLayer layer) || !layer.Enabled) continue;
                    if (layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) && !AtlasColorStack.CanBakeEmission(material, k, layer))
                    {
                        context.Notes.Add("An emission that is live (blinking, scrolling, audio, light based), replaces the base color, follows transparency, or is masked by a global mask written by layers or the second pass isn't baked yet; it's left out.");
                        return;
                    }
                }
            }
        }
    }
}
