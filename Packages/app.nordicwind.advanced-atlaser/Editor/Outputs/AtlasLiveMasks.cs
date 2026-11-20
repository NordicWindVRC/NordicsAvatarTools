using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A mask bus channel a feature reads in one source: its number and how it's combined.
    public struct AtlasBusRead
    {
        public int Channel;
        public AtlasMaskBlend Blend;
        public AtlasBusRead(int channel, AtlasMaskBlend blend) { Channel = channel; Blend = blend; }
    }

    // A feature's sources whose live mask steps stay live: their channels, and the live steps they
    // share (from the first live step to the end of each channel).
    public sealed class AtlasLiveSplit
    {
        public readonly Dictionary<int, int[]> Channels = new Dictionary<int, int[]>();
        public List<AtlasMaskBusStep> Steps;
        public bool Contains(int source) => Channels.ContainsKey(source);
    }

    // Live global masks (ATLAS_OUTPUT.md, "Masks"): a value that reads a bus channel whose last
    // steps change live (back faces, mirrors, cameras, distance, blacklights) is affine in the
    // channel's value when it reads it by multiply or replace, and those steps keep 0 at 0. So
    // in a source whose channel is prefix × live:
    //   value = own + (value with the prefix − own) × live, own = the value with the channel at 0.
    // The atlas keeps own in the feature's own map (the other sources as they are) and the
    // difference in an allocated mask place that doubles as a global mask channel, after which
    // the same live steps run; the feature reads the channel by add. Outside the live sources the
    // channel is 0, so the live steps don't reach them.
    public static class AtlasLiveMasks
    {
        public const string LiveFromId = "liveFrom";

        // Steps that change live and keep 0 at 0.
        private static bool Keepable(AtlasMaskBusStep step, AtlasOutputCapabilities capabilities)
        {
            if (!capabilities.LiveMaskSteps.Contains(step.Kind)) return false;
            switch (step.Kind)
            {
                case AtlasMaskBusStepKind.Faces:
                case AtlasMaskBusStepKind.Mirror:
                case AtlasMaskBusStepKind.Camera:
                    return true;
                default:
                    return step.Blend == AtlasMaskBlend.Multiply;
            }
        }

        private static List<AtlasMaskBusStep> Steps(AtlasUnifiedMaterial material, int channel) =>
            material.MaskBus.Steps.Where(s => s.Channel == channel).ToList();

        public static bool HasLive(AtlasUnifiedMaterial material, int channel) =>
            channel >= 0 && Steps(material, channel).Any(s => AtlasMaskBusEvaluator.IsLive(s.Kind));

        /// <summary>The channel's live steps when they can stay live: every step from the first
        /// live one on changes live and keeps 0 at 0, and the steps before can be baked. Null
        /// otherwise, or without live steps.</summary>
        public static List<AtlasMaskBusStep> Tail(AtlasUnifiedMaterial material, int channel, AtlasOutputCapabilities capabilities)
        {
            var steps = Steps(material, channel);
            int first = steps.FindIndex(s => AtlasMaskBusEvaluator.IsLive(s.Kind));
            if (first < 0) return null;
            var tail = steps.Skip(first).ToList();
            if (!tail.All(s => Keepable(s, capabilities))) return null;
            if (!steps.Take(first).All(AtlasMaskBusEvaluator.CanBake)) return null;
            return tail;
        }

        private static string Signature(List<AtlasMaskBusStep> tail) =>
            string.Join("|", tail.Select(s => AtlasFieldwise.Signature(s, "Channel")));

        /// <summary>The sources of the feature whose live steps stay live, or null. Sources whose
        /// reads can't (another blend, steps that don't keep 0, a different live setup than the
        /// one kept) are baked as normally seen, in one note.</summary>
        public static AtlasLiveSplit Plan(AtlasFeatureContext context, string featureId, string name, Func<int, IEnumerable<AtlasBusRead>> reads)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var candidates = new Dictionary<int, (string Signature, List<AtlasMaskBusStep> Tail, int[] Channels)>();
            // Sources whose live setup isn't the one kept (the others are in the pipeline's note on
            // live global masks baked as normally seen).
            var baked = new List<int>();
            if (!capabilities.GlobalMasks) return null;
            foreach (int i in context.Sources)
            {
                var material = job.Sources[i].Material;
                var live = (reads(i) ?? Enumerable.Empty<AtlasBusRead>()).Where(r => HasLive(material, r.Channel)).ToList();
                if (live.Count == 0) continue;
                if (live.Any(r => r.Blend != AtlasMaskBlend.Multiply && r.Blend != AtlasMaskBlend.Replace)) continue;
                var tails = live.Select(r => Tail(material, r.Channel, capabilities)).ToList();
                var signatures = tails.Select(t => t == null ? null : Signature(t)).Distinct().ToList();
                if (signatures.Count != 1 || signatures[0] == null) continue;
                candidates[i] = (signatures[0], tails[0], live.Select(r => r.Channel).Distinct().ToArray());
            }
            AtlasLiveSplit split = null;
            if (candidates.Count > 0)
            {
                // One live setup per feature: the chosen source's, or the one on the largest area.
                var choice = job.Features.Find(f => f.FeatureId == featureId);
                string kept = null;
                if (choice != null && choice.Values.TryGetValue(LiveFromId, out var value) &&
                    candidates.TryGetValue(Mathf.RoundToInt(value.Number) - 1, out var chosen))
                    kept = chosen.Signature;
                var setups = candidates.GroupBy(c => c.Value.Signature).ToList();
                if (setups.Count > 1) context.Plan.LiveSetups[featureId] = setups.Select(g => g.Min(c => c.Key)).OrderBy(i => i).ToList();
                kept = kept ?? candidates.GroupBy(c => c.Value.Signature)
                    .OrderByDescending(g => g.Sum(c => job.Sources[c.Key].UVRegion.width * job.Sources[c.Key].UVRegion.height))
                    .First().Key;
                split = new AtlasLiveSplit();
                foreach (var candidate in candidates)
                {
                    if (candidate.Value.Signature != kept) { baked.Add(candidate.Key); continue; }
                    split.Channels[candidate.Key] = candidate.Value.Channels;
                    split.Steps = split.Steps ?? candidate.Value.Tail;
                }
            }
            if (baked.Count > 0)
                context.Notes.Add($"The {name}'s live global masks in " + Numbers(baked) +
                                  " differ from the ones kept live; they're baked as normally seen (Live from picks the kept ones).");
            return split;
        }

        /// <summary>Splits a map request: its own part in the request, the live part in a global
        /// mask channel. The channel, or −1 when nothing stays live.</summary>
        public static int Split(AtlasFeatureContext context, string featureId, string name, AtlasMapRequest request,
            Func<int, IEnumerable<AtlasBusRead>> reads, bool decreasing = false)
        {
            if (request == null) return -1;
            var split = Plan(context, featureId, name, reads);
            if (split == null) return -1;
            var map = request.Map;
            var inner = request.Evaluate ?? ((i, m, w, h) => AtlasMapEvaluator.Evaluate(map, m, w, h));
            int channel = Place(context, split, name, inner, decreasing);
            if (channel < 0) return -1;
            request.Evaluate = Own(split, inner);
            Kept(context, split);
            return channel;
        }

        /// <summary>Splits a mask request the same way (before it's allocated).</summary>
        public static int Split(AtlasFeatureContext context, string featureId, string name, AtlasMaskRequest request,
            Func<int, IEnumerable<AtlasBusRead>> reads)
        {
            var split = Plan(context, featureId, name, reads);
            if (split == null) return -1;
            int channel = Place(context, split, name, request.Evaluate);
            if (channel < 0) return -1;
            OwnPart(request, split, context.Job, name);
            Kept(context, split);
            return channel;
        }

        /// <summary>Records the sources' channels as kept live (for the pipeline's note).</summary>
        public static void Kept(AtlasFeatureContext context, AtlasLiveSplit split)
        {
            foreach (var entry in split.Channels)
                foreach (int channel in entry.Value)
                    context.Plan.LiveKept.Add((entry.Key, channel));
        }

        /// <summary>A request in the context for the map, the last one added.</summary>
        public static AtlasMapRequest Request(AtlasFeatureContext context, AtlasVirtualMap map) => context.Maps.FindLast(m => m.Map == map);

        public static string Numbers(IEnumerable<int> indices)
        {
            var list = indices.OrderBy(i => i).Select(i => (i + 1).ToString()).ToList();
            return (list.Count == 1 ? "source " : "sources ") + string.Join(", ", list);
        }

        // Evaluates with the source's live channels at 0 (own) or without their live steps.
        private static NordicsTextureWrapper Under(AtlasLiveSplit split, int index, AtlasUnifiedMaterial material, AtlasBusView view,
            Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> evaluate, int width, int height)
        {
            using (AtlasMaskBusEvaluator.View(material, split.Channels[index], view))
                return evaluate(index, material, width, height);
        }

        /// <summary>The live part of a one-value map: (the value without the live steps − the
        /// value with the channel at 0) in the live sources, 0 elsewhere; <paramref name="decreasing"/>
        /// for a value stored inverted (roughness read as smoothness).</summary>
        public static NordicsTextureWrapper Difference(AtlasLiveSplit split, int index, AtlasUnifiedMaterial material, int width, int height,
            Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> evaluate, bool decreasing, bool color = false)
        {
            if (!split.Contains(index)) return AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, width, height);
            var full = Under(split, index, material, AtlasBusView.Prefix, evaluate, width, height);
            using (var own = Under(split, index, material, AtlasBusView.Zero, evaluate, width, height))
                AtlasPixels.Map(full, own, (a, b) =>
                {
                    if (color)
                        return new Color(Mathf.Max(0f, a.r - b.r), Mathf.Max(0f, a.g - b.g), Mathf.Max(0f, a.b - b.b), 1f);
                    float v = Mathf.Clamp01(decreasing ? b.r - a.r : a.r - b.r);
                    return new Color(v, v, v, 1f);
                });
            return full;
        }

        /// <summary>A map's own part: the live sources evaluated with their live channels at 0.</summary>
        public static Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> Own(AtlasLiveSplit split,
            Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> evaluate) =>
            (index, material, width, height) => split.Contains(index)
                ? Under(split, index, material, AtlasBusView.Zero, evaluate, width, height)
                : evaluate(index, material, width, height);

        /// <summary>The request's own part (OwnPart of its evaluation). Returns the evaluation
        /// before, for the live part.</summary>
        public static Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> OwnPart(AtlasMapRequest request, AtlasLiveSplit split)
        {
            var map = request.Map;
            var inner = request.Evaluate ?? ((i, m, w, h) => AtlasMapEvaluator.Evaluate(map, m, w, h));
            request.Evaluate = Own(split, inner);
            return inner;
        }

        /// <summary>A mask request's own part: its key and evaluation in the live sources with
        /// their live channels at 0.</summary>
        public static Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> OwnPart(AtlasMaskRequest request, AtlasLiveSplit split,
            AtlasGeneratorJob job, string name)
        {
            var inner = request.Evaluate;
            var key = request.Key;
            request.Key = i => split.Contains(i)
                ? "own " + name + "@" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(job.Sources[i].Material)
                : key(i);
            request.InverseKey = null;
            request.CanInvert = false;
            request.Evaluate = Own(split, inner);
            return inner;
        }

        /// <summary>The live part as a global mask channel: an allocated mask place (its channel
        /// number the place's) holding the difference, then the live steps. −1 when no place is
        /// left (the live sources are then baked as normally seen, and noted).</summary>
        public static int Place(AtlasFeatureContext context, AtlasLiveSplit split, string name,
            Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> evaluate, bool decreasing = false)
        {
            var job = context.Job;
            var allocated = AtlasMaskAllocator.Allocate(context, new AtlasMaskRequest
            {
                Name = "live " + name,
                Key = i => split.Contains(i)
                    ? "live " + name + "@" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(job.Sources[i].Material)
                    : AtlasMaskKeys.Constant(0f),
                Evaluate = (i, material, w, h) => Difference(split, i, material, w, h, evaluate, decreasing)
            });
            if (allocated?.Mask == null)
            {
                context.Notes.Add($"No mask place is left for the {name}'s live global masks; " + Numbers(split.Channels.Keys) +
                                  " are baked as normally seen.");
                return -1;
            }
            int channel = Array.IndexOf(AtlasMaskAllocator.Places, allocated.Place);
            AddSteps(context, channel, new AtlasMask { Map = allocated.Mask.Map, Channel = allocated.Mask.Channel }, split.Steps);
            return channel;
        }

        /// <summary>A channel that is 1 before its live steps (a global mask texture the output
        /// leaves unset), for a part whose map already holds the difference (a color). −1 when
        /// none is left.</summary>
        public static int Unit(AtlasFeatureContext context, AtlasLiveSplit split, string name)
        {
            var plan = context.Plan;
            if (plan.NextUnitChannel >= 16)
            {
                context.Notes.Add($"No global mask channel is left for the {name}'s live global masks; " + Numbers(split.Channels.Keys) +
                                  " are baked as normally seen.");
                return -1;
            }
            int channel = plan.NextUnitChannel++;
            AddSteps(context, channel, null, split.Steps);
            return channel;
        }

        private static void AddSteps(AtlasFeatureContext context, int channel, AtlasMask texture, List<AtlasMaskBusStep> live)
        {
            var steps = context.Output.MaskBus.Steps;
            if (steps.Any(s => s.Channel == channel)) return;
            if (texture != null) steps.Add(new AtlasMaskBusStep { Channel = channel, Kind = AtlasMaskBusStepKind.Texture, Mask = texture });
            foreach (var step in live)
            {
                var copy = new AtlasMaskBusStep();
                AtlasFieldwise.Copy(step, copy);
                copy.Channel = channel;
                steps.Add(copy);
            }
        }

        /// <summary>A mask reading the channel by add (own value 1 without a map: the feature's
        /// mask object gets the channel).</summary>
        public static void Read(AtlasMask mask, int channel)
        {
            mask.Bus = channel;
            mask.BusBlend = AtlasMaskBlend.Add;
        }

        public static void Read(AtlasScalarInput input, int channel)
        {
            input.Bus = channel;
            input.BusBlend = AtlasMaskBlend.Add;
        }

        /// <summary>The bus reads of a mask (none without one).</summary>
        public static IEnumerable<AtlasBusRead> Of(AtlasMask mask)
        {
            if (mask != null && mask.Bus >= 0) yield return new AtlasBusRead(mask.Bus, mask.BusBlend);
        }

        public static IEnumerable<AtlasBusRead> Of(AtlasScalarInput input)
        {
            if (input != null && input.Bus >= 0) yield return new AtlasBusRead(input.Bus, input.BusBlend);
        }
    }
}
