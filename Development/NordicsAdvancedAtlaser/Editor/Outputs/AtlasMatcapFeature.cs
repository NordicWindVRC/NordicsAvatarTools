using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Matcaps (ATLAS_OUTPUT.md, "Feature inventory"): 2D lookup layers in the output's matcap slots.
    // - A source's matcap is an instance: its lookup layers sharing texture, mask and settings
    //   (Poiyomi writes one per blend weight). Instances that look the same in several sources
    //   are one group, which shares a slot: each source's mask says where it shows, 0 elsewhere.
    // - The user places each group: its own slot, combined with another group's slot, or off
    //   (stored by the group's key). Automatic, where the user hasn't: groups on the same parts
    //   with the same lookup are combined; the largest groups get their own slots; the rest are
    //   combined into the slot most like them.
    // - A combined slot holds one generated texture. Groups on the same parts stack: their
    //   textures × colors × weights added (for add) or multiplied (for multiply), with every
    //   blend converted to one of the two. Stacks on different parts are averaged by area,
    //   since each part shows the one texture.
    // - A slot that lacks a blend a matcap uses (Toon Standard's add or multiply) takes the
    //   nearest: add, add to light, screen and mixed brighten, so they add; replace and multiply
    //   multiply. Listed as approximated.
    // - Where a slot can't do a source's matcaps as they are (blends it lacks, or base color
    //   mix it doesn't have), it is fitted to them per texel (SlotFit): its strength goes into
    //   the mask and the rest into the base color map, which may come out brighter than the
    //   source's. Without a base color map, the base color mix goes into the mask: the mask ×
    //   lerp(1, the base color's luminance, mix).
    // - Each slot's mask goes through the mask allocator.
    public sealed class AtlasMatcapFeature : IAtlasInstanceFeature
    {
        public const string FeatureId = "matcaps";
        public const int CombinedSize = 512;

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Matcap slots", Kind = AtlasFeatureModeKind.Native,
                OptionalMaps = new[] { AtlasVirtualMap.Mask1 },
                Requires = c => c.MatcapSlots > 0
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Matcaps";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && InstancesOf(-1, source, out _).Count > 0;

        // One source's matcap.
        private sealed class Instance
        {
            public int Source;
            public readonly List<AtlasLookupLayer> Layers = new List<AtlasLookupLayer>();
            public AtlasMask Mask => Layers[0].Mask;
        }

        // Matcaps that look the same in several sources.
        private sealed class Group
        {
            public string Key, Look, Lookup, Label;
            public float Area;
            public readonly List<Instance> Members = new List<Instance>();
            public string Parts;
        }

        // The 2D projections (cubemap lookups aren't matcaps).
        private static bool Flat(AtlasLookupProjection projection) =>
            projection == AtlasLookupProjection.ViewStabilized || projection == AtlasLookupProjection.ViewCorrected ||
            projection == AtlasLookupProjection.ReflectionPlanar || projection == AtlasLookupProjection.Gradient ||
            projection == AtlasLookupProjection.LightHalfVector;

        // A source's matcap instances, in layer order; `live` counts the live ones left out.
        private static List<Instance> InstancesOf(int index, AtlasUnifiedMaterial source, out int live)
        {
            live = 0;
            var result = new List<Instance>();
            Instance current = null;
            for (int i = 0; i < source.Layers.Count; i++)
            {
                if (!(source.Layers[i] is AtlasLookupLayer layer) || !layer.Enabled || !Flat(layer.Projection))
                {
                    current = null;
                    continue;
                }
                if (!AtlasColorStack.IsStatic(source, "layers[" + i + "]"))
                {
                    live++;
                    current = null;
                    continue;
                }
                var first = current?.Layers[0];
                if (first != null && ReferenceEquals(layer.Mask, first.Mask) && ReferenceEquals(layer.Source.Map, first.Source.Map) &&
                    layer.Projection == first.Projection && layer.Tiling == first.Tiling)
                    current.Layers.Add(layer);
                else
                {
                    current = new Instance { Source = index };
                    current.Layers.Add(layer);
                    result.Add(current);
                }
            }
            return result;
        }

        private static readonly string[] PerLayer = { "Mask", "Opacity", "Blend", "Placement", "EmissionAmount", "Enabled" };

        // How the matcap looks: its texture, color, lookup and every weight.
        private static string Look(Instance instance) =>
            AtlasFieldwise.Signature(instance.Layers[0], PerLayer) + "|" +
            string.Join(",", instance.Layers.Select(l => $"{l.Blend}/{l.Placement}:{l.Opacity:0.####}/{l.EmissionAmount:0.####}"));

        // The lookup alone (what a combined texture needs to share).
        private static string Lookup(Instance instance) =>
            AtlasFieldwise.Signature(instance.Layers[0], PerLayer.Concat(new[] { "Source" }).ToArray());

        private static string Label(Instance instance)
        {
            var texture = instance.Layers[0].Source.Map?.Texture;
            return texture != null ? texture.name : "Color matcap";
        }

        private List<Group> Groups(AtlasGeneratorJob job, IEnumerable<int> sources, List<string> notes)
        {
            var groups = new List<Group>();
            int live = 0;
            foreach (int i in sources)
            {
                var material = job.Sources[i].Material;
                var instances = InstancesOf(i, material, out int left);
                live += left;
                foreach (var instance in instances)
                {
                    string look = Look(instance);
                    var group = groups.Find(g => g.Look == look);
                    if (group == null)
                    {
                        group = new Group { Look = look, Lookup = Lookup(instance), Key = Hash(look), Label = Label(instance) };
                        groups.Add(group);
                    }
                    group.Members.Add(instance);
                }
            }
            foreach (var group in groups)
            {
                var members = group.Members.Select(m => m.Source).Distinct().ToList();
                group.Area = members.Sum(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height);
                group.Label += " (" + (members.Count == 1 ? "source " : "sources ") + string.Join(", ", members.Select(i => i + 1)) + ")";
                group.Parts = string.Join(";", group.Members.OrderBy(m => m.Source)
                    .Select(m => m.Source + ":" + AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[m.Source].Material)));
            }
            if (live > 0) notes?.Add("Live matcaps (animated, audio-driven) aren't merged yet; they're left out.");
            return groups.OrderByDescending(g => g.Area).ToList();
        }

        private static string Hash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Take(6).Select(b => b.ToString("x2")));
        }

        public bool CanPlaceAsMatcap(AtlasOutputCapabilities capabilities) => false;

        public IReadOnlyList<AtlasFeatureInstance> Instances(AtlasGeneratorJob job, IReadOnlyList<int> sources) =>
            Groups(job, sources, null).Select(g => new AtlasFeatureInstance
            {
                Key = g.Key, Label = g.Label, Sources = g.Members.Select(m => m.Source).Distinct().ToList()
            }).ToList();

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var groups = Groups(job, context.Sources, context.Notes);
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            (AtlasInstancePlacement Mode, string Target) Placed(Group g) =>
                choice != null && choice.Values.TryGetValue(AtlasInstancePlacements.Id(g.Key), out var value)
                    ? ((AtlasInstancePlacement)Mathf.RoundToInt(value.Number), value.Text) : (AtlasInstancePlacement.Automatic, "");

            var slots = new List<List<Group>>();
            var slotOf = new Dictionary<string, List<Group>>();
            var waiting = new List<Group>();
            var off = new List<Group>();
            bool Own(Group g)
            {
                if (slots.Count >= capabilities.MatcapSlots) return false;
                var slot = new List<Group> { g };
                slots.Add(slot);
                slotOf[g.Key] = slot;
                return true;
            }
            void Join(List<Group> slot, Group g)
            {
                slot.Add(g);
                slotOf[g.Key] = slot;
            }
            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Own))
                if (!Own(g)) waiting.Add(g);
            off.AddRange(groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Off));
            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Automatic))
            {
                // On the same parts with the same lookup: one texture shows exactly what both did.
                var same = slots.Find(s => s[0].Parts == g.Parts && s[0].Lookup == g.Lookup);
                if (same != null) Join(same, g);
                else if (!Own(g)) waiting.Add(g);
            }
            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Combined))
            {
                if (slotOf.TryGetValue(Placed(g).Target, out var target)) Join(target, g);
                else waiting.Add(g);
            }
            foreach (var g in waiting)
            {
                if (slots.Count == 0) off.Add(g);
                else Join(slots.OrderByDescending(s => Similarity(s[0], g)).First(), g);
            }

            var approximated = new List<string>();
            context.Plan.MatcapSlotsUsed = slots.Count;
            for (int k = 0; k < slots.Count; k++)
                Place(context, slots[k], k, approximated);
            if (off.Count > 0)
                context.Notes.Add("Matcaps left out: " + string.Join(", ", off.Select(g => g.Label)) + ".");
            for (int k = 0; k < slots.Count; k++)
                if (slots[k].Count > 1)
                    context.Notes.Add($"Matcap slot {k + 1} combines " + string.Join(", ", slots[k].Select(g => g.Label)) + ".");
            if (approximated.Count > 0)
                context.Notes.Add("Matcap blends the output shader doesn't have are approximated with add or multiply: " + string.Join(", ", approximated.Distinct()) + ".");
        }

        // How alike two groups are, for combining what doesn't fit: the same lookup first, then
        // the same main blend, then the nearest color.
        private static float Similarity(Group a, Group b)
        {
            float score = a.Lookup == b.Lookup ? 4f : 0f;
            var la = a.Members[0].Layers;
            var lb = b.Members[0].Layers;
            if (Main(la) == Main(lb)) score += 2f;
            var ca = la[0].Source.Color;
            var cb = lb[0].Source.Color;
            return score - (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b));
        }

        private static AtlasMatcapBlends Flag(AtlasLookupLayer layer)
        {
            switch (layer.Blend)
            {
                case AtlasBlend.Add: return layer.Placement == AtlasPlacement.Overlay ? AtlasMatcapBlends.AddToLight : AtlasMatcapBlends.Add;
                case AtlasBlend.Multiply: return AtlasMatcapBlends.Multiply;
                case AtlasBlend.Replace: return AtlasMatcapBlends.Replace;
                case AtlasBlend.Screen: return AtlasMatcapBlends.Screen;
                case AtlasBlend.Mixed: return AtlasMatcapBlends.Mixed;
                default: return AtlasMatcapBlends.None;
            }
        }

        // The nearest of add and multiply: what brightens adds; replace and multiply multiply.
        private static AtlasBlend Nearest(AtlasLookupLayer layer) =>
            layer.Blend == AtlasBlend.Multiply || layer.Blend == AtlasBlend.Replace ? AtlasBlend.Multiply : AtlasBlend.Add;

        // The blend carrying the most weight, as add or multiply.
        private static AtlasBlend Main(List<AtlasLookupLayer> layers) =>
            layers.GroupBy(Nearest).OrderByDescending(g => g.Sum(l => l.Opacity)).First().Key;

        private void Place(AtlasFeatureContext context, List<Group> slot, int index, List<string> approximated)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var main = Main(slot.SelectMany(g => g.Members[0].Layers).ToList());
            bool combined = Combined(slot, capabilities);
            // The sources the slot can't show as they are, fitted to (SlotFit) where the output
            // has a base color map; otherwise the base color mix goes into the mask.
            var baseColor = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            var fits = baseColor != null ? Fits(job, slot, main, combined, capabilities) : null;
            if (fits != null && fits.Count == 0) fits = null;
            bool fold = fits == null && !capabilities.MatcapBaseColorMix && slot.Any(g => BaseColorMix(g.Members[0]) > 0f);
            var request = MaskRequest(job, slot, index, fold, capabilities.MatcapMaskInvert, fits);
            int live = AtlasLiveMasks.Split(context, FeatureId, "matcap slot " + (index + 1), request,
                i => Member(slot, i) is Instance member ? AtlasLiveMasks.Of(member.Mask) : null);
            var mask = AtlasMaskAllocator.Allocate(context, request);
            if (mask == null && (fold || fits != null))
            {
                fold = false;
                fits = null;
                mask = AtlasMaskAllocator.Allocate(context, MaskRequest(job, slot, index, false, capabilities.MatcapMaskInvert, null));
            }
            if (fold) approximated.AddRange(slot.Where(g => BaseColorMix(g.Members[0]) > 0f).Select(g => g.Label));
            if (fits != null) approximated.AddRange(slot.Where(g => g.Members.Any(m => fits.ContainsKey(m.Source))).Select(g => g.Label));
            if (mask == null)
            {
                context.Notes.Add($"No mask place is left for matcap slot {index + 1}; " + string.Join(", ", slot.Select(g => g.Label)) + " left out.");
                return;
            }
            if (mask.IsConstant && mask.Constant <= 0f) return;
            if (fits != null) FitBaseColor(context, baseColor, fits);
            // Every slot has a mask object of its own, which the writers group its layers by.
            var slotMask = mask.Mask ?? new AtlasMask();
            if (live >= 0) AtlasLiveMasks.Read(slotMask, live);
            float scale = mask.IsConstant ? mask.Constant : 1f;
            if (slot.Select(g => g.Members.Select(m => m.Source)).SelectMany(s => s).GroupBy(s => s).Any(s => s.Count() > 1))
                context.Notes.Add($"Matcap slot {index + 1} has several matcaps on one source; the first one's mask is used there.");

            var output = context.Output.Layers;
            int first = output.Count;
            AddLayers(context, slot, index, slotMask, scale, approximated, fits != null);
            // Folded into the mask or fitted: the slot's layers don't mix in the base color again.
            if (fold || fits != null)
                for (int i = first; i < output.Count; i++)
                    if (output[i] is AtlasLookupLayer added) added.BaseColorMix = 0f;
        }

        private static float BaseColorMix(Instance instance) => Mathf.Clamp01(instance.Layers[0].BaseColorMix);

        private static AtlasMaskRequest MaskRequest(AtlasGeneratorJob job, List<Group> slot, int index, bool fold, bool canInvert,
            Dictionary<int, SlotFit> fits)
        {
            float Mix(int i) => fold && Member(slot, i) is Instance m ? BaseColorMix(m) : 0f;
            bool Fitted(int i) => fits != null && fits.ContainsKey(i);
            string Own(int i) => "@" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(job.Sources[i].Material);
            return new AtlasMaskRequest
            {
                Name = "matcap " + (index + 1),
                CanInvert = canInvert,
                Key = i => Member(slot, i) is Instance m
                    ? AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[i].Material) +
                      (Fitted(i) ? "|fit " + index + Own(i)
                          : Mix(i) > 0f ? "|base " + Mix(i).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + Own(i) : "")
                    : AtlasMaskKeys.Constant(0f),
                InverseKey = i => Member(slot, i) is Instance m
                    ? Mix(i) > 0f || Fitted(i) ? null : AtlasMaskKeys.Inverse(m.Mask, 1f, job.Sources[i].Material)
                    : AtlasMaskKeys.Constant(1f),
                Evaluate = (i, material, w, h) =>
                {
                    if (!(Member(slot, i) is Instance m)) return AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, w, h);
                    var result = AtlasMapEvaluator.ScaledMask(m.Mask, 1f, w, h, material);
                    float mix = Mix(i);
                    if (mix <= 0f && !Fitted(i)) return result;
                    using (var color = AtlasColorStack.Evaluate(material, w, h, multiplyColor: true))
                    {
                        if (Fitted(i))
                        {
                            var fit = fits[i];
                            // The fitted strength, where the matcap's mask is.
                            AtlasPixels.Map(result, color, (value, c) =>
                            {
                                if (value.r <= 1e-6f) return value;
                                Solve(fit, value.r, c, out float strength);
                                return value * (strength / value.r);
                            });
                        }
                        else
                            AtlasPixels.Map(result, color, (value, c) =>
                            {
                                float factor = Mathf.Lerp(1f, Mathf.Clamp01(Luminance(c)), mix);
                                return new Color(value.r * factor, value.g * factor, value.b * factor, value.a * factor);
                            });
                    }
                    return result;
                }
            };
        }

        private void AddLayers(AtlasFeatureContext context, List<Group> slot, int index, AtlasMask slotMask, float scale, List<string> approximated,
            bool fitted)
        {
            var capabilities = context.Capabilities;
            var template = slot[0].Members[0].Layers;
            var output = context.Output.Layers;
            if (!Combined(slot, capabilities))
            {
                bool fits = template.All(l => (capabilities.MatcapBlends & Flag(l)) != 0);
                if (fits && !fitted)
                {
                    foreach (var layer in template)
                    {
                        var copy = Copy(layer, slotMask, layer.Blend, layer.Placement, layer.Opacity * scale, layer.Source.Map, layer.Source.Color);
                        output.Add(copy);
                        // Its hue shift (and hue cycling) goes with it.
                        AtlasHueCycling.CopyLayerHue(context.Output, context.Job.Sources[slot[0].Members[0].Source].Material, layer, copy);
                        AtlasAudio.CopyLayerAudio(context.Output, context.Job.Sources[slot[0].Members[0].Source].Material, layer, copy);
                    }
                    return;
                }
                if (!fits) approximated.Add(slot[0].Label);
                var blend = Main(template);
                // Fitted, the strength is all in the mask.
                float opacity = fitted ? 1f : Mathf.Min(1f, template.Where(l => Nearest(l) == blend).Sum(l => l.Opacity));
                var single = Copy(template[0], slotMask, blend, AtlasPlacement.Surface, opacity * scale, template[0].Source.Map, template[0].Source.Color);
                output.Add(single);
                AtlasHueCycling.CopyLayerHue(context.Output, context.Job.Sources[slot[0].Members[0].Source].Material, template[0], single);
                AtlasAudio.CopyLayerAudio(context.Output, context.Job.Sources[slot[0].Members[0].Source].Material, template[0], single);
                return;
            }

            // Combined (or tinted where the slot has no color): one generated texture.
            var main = Main(slot.SelectMany(g => g.Members[0].Layers).ToList());
            if (slot.Any(g => g.Members[0].Layers.Any(l => Nearest(l) != main || (capabilities.MatcapBlends & Flag(l)) == 0 || l.Blend != main)))
                approximated.AddRange(slot.Select(g => g.Label));
            var parts = slot.Select(g => (Map: g.Members[0].Layers[0].Source.Map, g.Members[0].Layers[0].Source.Color,
                Weight: Weight(g.Members[0], main), g.Parts, g.Area)).ToList();
            var reference = new AtlasTextureReference { Space = AtlasTextureSpace.View, Interpretation = AtlasTextureInterpretation.Color };
            context.Plan.GeneratedTextures.Add(new AtlasGeneratedTexture
            {
                Reference = reference, Name = "Matcap" + (index + 1), Width = CombinedSize, Height = CombinedSize,
                Pixels = () => Combine(parts, main, CombinedSize)
            });
            var combined = Copy(template[0], slotMask, main, AtlasPlacement.Surface, scale, reference, Color.white);
            combined.EmissionAmount = slot.Max(g => g.Members[0].Layers[0].EmissionAmount);
            output.Add(combined);
        }

        // A matcap's weight in a combined texture: its layers of the slot's blend.
        private static float Weight(Instance instance, AtlasBlend main) =>
            Mathf.Min(1f, instance.Layers.Where(l => Nearest(l) == main).Sum(l => l.Opacity));

        // One generated texture for the slot: several groups, or a tint the slot has no color for.
        private static bool Combined(List<Group> slot, AtlasOutputCapabilities capabilities)
        {
            var tint = slot[0].Members[0].Layers[0].Source.Color;
            bool tinted = Mathf.Abs(tint.r - 1f) + Mathf.Abs(tint.g - 1f) + Mathf.Abs(tint.b - 1f) > 1e-3f;
            return slot.Count > 1 || (tinted && !capabilities.MatcapColor);
        }

        // A slot that can't show a source's matcaps as they are (blends it lacks, base color mix
        // it doesn't have) is fitted to them per texel. Poiyomi's matcaps act on the base color
        // in turn, each with its color × matcap × lerp(1, the base color so far, mix), by its
        // weights × mask: replace, multiply, add, screen, mixed. All of them rise with the
        // matcap, as the slot does with its texture T: base × lerp(1, T, s) for a multiplying
        // slot, base + T × s for an adding one. The fit matches the chain where the matcaps are
        // black (the slot's T0) and full (T1): from the rise k = (full − black) / (T1 − T0),
        // the strength s goes into the mask and the base color t into the base color map
        // (multiply: t = black − k T0 + k, s = k / t; add: s = k, t = black − k T0). Exact for
        // a single layer on a gray base. The strength is one value: s takes the channels'
        // luminance, and each channel of t then matches halfway between T0 and T1.
        private sealed class SlotFit
        {
            public AtlasMask Mask;
            public bool Add;
            // T0 and T1.
            public float Zero;
            public Color Full = Color.white;
            // Poiyomi's chain: the matcap (instance) each layer belongs to, in order.
            public readonly List<(int Matcap, AtlasBlend Blend, float Weight, Color Color, float Mix)> Layers =
                new List<(int, AtlasBlend, float, Color, float)>();
        }

        private static bool Chained(AtlasLookupLayer layer) =>
            layer.Placement == AtlasPlacement.Surface && layer.Opacity > 0f &&
            (layer.Blend == AtlasBlend.Replace || layer.Blend == AtlasBlend.Multiply || layer.Blend == AtlasBlend.Add ||
             layer.Blend == AtlasBlend.Screen || layer.Blend == AtlasBlend.Mixed);

        // The fit of each source the slot can't show as it is. A slot with its own texture and
        // strength (not combined) is fitted for all of its sources or none.
        private static Dictionary<int, SlotFit> Fits(AtlasGeneratorJob job, List<Group> slot, AtlasBlend main, bool combined,
            AtlasOutputCapabilities capabilities)
        {
            var result = new Dictionary<int, SlotFit>();
            var ends = combined ? Ends(slot, main) : (Zero: 0f, Full: Color.white);
            bool lacking = combined || slot[0].Members[0].Layers.Any(l => (capabilities.MatcapBlends & Flag(l)) == 0);
            bool all = true;
            foreach (var members in slot.SelectMany(g => g.Members).GroupBy(m => m.Source))
            {
                var layers = job.Sources[members.Key].Material.Layers;
                var ordered = members.OrderBy(m => layers.IndexOf(m.Layers[0])).ToList();
                bool mix = !capabilities.MatcapBaseColorMix && ordered.Any(m => BaseColorMix(m) > 0f);
                bool blends = lacking && ordered.Any(m => m.Layers.Any(l => Chained(l) && l.Blend != main));
                if (!mix && !blends)
                {
                    all = false;
                    continue;
                }
                var fit = new SlotFit { Mask = Member(slot, members.Key).Mask, Add = main != AtlasBlend.Multiply };
                for (int k = 0; k < ordered.Count; k++)
                    foreach (var layer in ordered[k].Layers.Where(Chained))
                        fit.Layers.Add((k, layer.Blend, layer.Opacity, layer.Source.Color, Mathf.Clamp01(layer.BaseColorMix)));
                if (combined)
                {
                    fit.Zero = ends.Zero;
                    fit.Full = ends.Full;
                }
                else
                {
                    // The matcap's own texture (× its color where the slot has one).
                    fit.Zero = 0f;
                    var color = capabilities.MatcapColor ? ordered[0].Layers[0].Source.Color : Color.white;
                    fit.Full = new Color(color.r, color.g, color.b, 1f);
                }
                result[members.Key] = fit;
            }
            if (!combined && !all) result.Clear();
            return result;
        }

        // The generated texture's value (Combine) with every matcap texture black and white.
        private static (float Zero, Color Full) Ends(List<Group> slot, AtlasBlend main)
        {
            bool add = main != AtlasBlend.Multiply;
            var stacks = slot.GroupBy(g => g.Parts ?? "").ToList();
            float total = stacks.Sum(s => Mathf.Max(0f, s.First().Area));
            float zero = 0f;
            var full = Color.black;
            foreach (var stack in stacks)
            {
                float share = stacks.Count == 1 ? 1f : total > 0f ? Mathf.Max(0f, stack.First().Area) / total : 1f / stacks.Count;
                float z = add ? 0f : 1f;
                var f = add ? Color.black : Color.white;
                foreach (var group in stack)
                {
                    float weight = Weight(group.Members[0], main);
                    var color = group.Members[0].Layers[0].Source.Color;
                    for (int c = 0; c < 3; c++)
                        f[c] = add ? f[c] + color[c] * weight : f[c] * Mathf.Lerp(1f, color[c], weight);
                    if (!add) z *= 1f - weight;
                }
                zero += z * share;
                for (int c = 0; c < 3; c++) full[c] += f[c] * share;
            }
            full.a = 1f;
            return (zero, full);
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // Poiyomi's chain on base color b with every matcap at `matcap` (0 or 1), by the mask.
        private static Color Chain(SlotFit fit, float mask, Color b, float matcap)
        {
            var x = b;
            var start = b;
            int current = -1;
            foreach (var (matcapIndex, blend, weight, color, mix) in fit.Layers)
            {
                // A matcap mixes in the base color as it is before its own blends.
                if (matcapIndex != current)
                {
                    current = matcapIndex;
                    start = x;
                }
                float alpha = weight * mask;
                for (int c = 0; c < 3; c++)
                {
                    float v = matcap * color[c] * Mathf.Lerp(1f, Mathf.Max(0f, start[c]), mix);
                    switch (blend)
                    {
                        case AtlasBlend.Replace: x[c] = Mathf.Lerp(x[c], v, alpha); break;
                        case AtlasBlend.Multiply: x[c] *= Mathf.Lerp(1f, v, alpha); break;
                        case AtlasBlend.Add: x[c] += v * alpha; break;
                        case AtlasBlend.Screen: x[c] = Mathf.Lerp(x[c], 1f - (1f - x[c]) * (1f - v), alpha); break;
                        case AtlasBlend.Mixed: x[c] *= 1f + v * alpha; break;
                    }
                }
            }
            return x;
        }

        // The base color t (with the shared color) and the strength s at base color b and mask.
        private static Color Solve(SlotFit fit, float mask, Color b, out float strength)
        {
            var black = Chain(fit, mask, b, 0f);
            var full = Chain(fit, mask, b, 1f);
            Color rise = Color.black, top = Color.black;
            for (int c = 0; c < 3; c++)
            {
                float span = fit.Full[c] - fit.Zero;
                rise[c] = Mathf.Abs(span) > 1e-5f ? (full[c] - black[c]) / span : 0f;
                top[c] = black[c] - rise[c] * fit.Zero + (fit.Add ? 0f : rise[c]);
            }
            if (fit.Add) strength = Mathf.Clamp01(Luminance(rise));
            else
            {
                float t = Luminance(top);
                strength = t > 1e-5f ? Mathf.Clamp01(Luminance(rise) / t) : 0f;
            }
            var result = new Color(0f, 0f, 0f, b.a);
            for (int c = 0; c < 3; c++)
            {
                float half = (fit.Zero + fit.Full[c]) * 0.5f;
                float target = (black[c] + full[c]) * 0.5f;
                result[c] = fit.Add
                    ? Mathf.Max(0f, target - strength * half)
                    : Mathf.Max(0f, target) / Mathf.Max(1e-4f, 1f - strength + strength * half);
            }
            return result;
        }

        // The base color map as the fits make it where their sources are. Above 1, it is divided
        // by its brightest value, which goes into the shared color.
        private static void FitBaseColor(AtlasFeatureContext context, AtlasMapRequest request, Dictionary<int, SlotFit> fits)
        {
            var inner = request.Evaluate ?? ((i, m, w, h) => AtlasMapEvaluator.Evaluate(AtlasVirtualMap.BaseColor, m, w, h));
            var output = context.Output.Surface.BaseColor;
            request.Evaluate = (i, material, w, h) =>
            {
                var result = inner(i, material, w, h);
                if (!fits.TryGetValue(i, out var fit)) return result;
                // The map holds the base color without the shared color.
                var tint = output.Color;
                using (var mask = AtlasMapEvaluator.ScaledMask(fit.Mask, 1f, w, h, material))
                    AtlasPixels.Map(result, mask, (c, m) =>
                    {
                        var t = Solve(fit, m.r, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a), out _);
                        return new Color(tint.r > 1e-5f ? t.r / tint.r : c.r, tint.g > 1e-5f ? t.g / tint.g : c.g,
                            tint.b > 1e-5f ? t.b / tint.b : c.b, c.a);
                    });
                return result;
            };
            if (request.Finish == null)
                request.Finish = map =>
                {
                    float peak = AtlasColorFeature.Peak(map);
                    if (peak <= 1f) return;
                    output.Color = new Color(output.Color.r * peak, output.Color.g * peak, output.Color.b * peak, output.Color.a);
                    map.ApplyColor(new ColorOperation
                    {
                        Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(1f / peak, 1f / peak, 1f / peak, 1f) }
                    });
                };
        }

        // The source's member of the slot (the first, if several).
        private static Instance Member(List<Group> slot, int source) =>
            slot.SelectMany(g => g.Members).FirstOrDefault(m => m.Source == source);

        private static AtlasLookupLayer Copy(AtlasLookupLayer layer, AtlasMask mask, AtlasBlend blend, AtlasPlacement placement, float opacity,
            AtlasTextureReference map, Color color)
        {
            var copy = new AtlasLookupLayer();
            AtlasFieldwise.Copy(layer, copy);
            copy.Mask = mask;
            copy.Blend = blend;
            copy.Placement = placement;
            copy.Opacity = opacity;
            copy.Source.Map = map;
            copy.Source.Color = color;
            return copy;
        }

        /// <summary>Matcap textures × colors on the same parts combined into one (linear): added
        /// by weight for add, lerp(1, part, weight) multiplied for multiply.</summary>
        public static Color[] Combine(IReadOnlyList<(AtlasTextureReference Map, Color Color, float Weight)> parts, AtlasBlend blend, int size) =>
            Stack(parts, blend, size);

        /// <summary>Matcaps combined into one texture (linear): those on the same parts (equal
        /// <c>Parts</c>) stacked as <see cref="Combine(IReadOnlyList{ValueTuple{AtlasTextureReference, Color, float}}, AtlasBlend, int)"/>,
        /// the stacks on different parts averaged by their area.</summary>
        public static Color[] Combine(IReadOnlyList<(AtlasTextureReference Map, Color Color, float Weight, string Parts, float Area)> parts,
            AtlasBlend blend, int size)
        {
            var stacks = parts.GroupBy(p => p.Parts ?? "").ToList();
            if (stacks.Count == 1) return Stack(parts.Select(p => (p.Map, p.Color, p.Weight)).ToList(), blend, size);
            float total = stacks.Sum(s => Mathf.Max(0f, s.First().Area));
            var result = new Color[size * size];
            foreach (var stack in stacks)
            {
                float share = total > 0f ? Mathf.Max(0f, stack.First().Area) / total : 1f / stacks.Count;
                var pixels = Stack(stack.Select(p => (p.Map, p.Color, p.Weight)).ToList(), blend, size);
                for (int i = 0; i < result.Length; i++) result[i] += pixels[i] * share;
            }
            for (int i = 0; i < result.Length; i++) result[i].a = 1f;
            return result;
        }

        private static Color[] Stack(IReadOnlyList<(AtlasTextureReference Map, Color Color, float Weight)> parts, AtlasBlend blend, int size)
        {
            var result = new Color[size * size];
            bool add = blend != AtlasBlend.Multiply;
            for (int i = 0; i < result.Length; i++) result[i] = add ? new Color(0f, 0f, 0f, 1f) : Color.white;
            foreach (var part in parts)
            {
                Color[] pixels = null;
                if (part.Map?.Texture is Texture2D texture)
                    using (var loaded = new NordicsTextureWrapper(texture, TextureDataType.Color))
                    using (var sized = loaded.Resize(size, size, sampling: TextureSamplingMode.Area))
                        pixels = sized.GetPixels();
                for (int i = 0; i < result.Length; i++)
                {
                    var c = (pixels != null ? pixels[i] : Color.white) * part.Color;
                    var r = result[i];
                    result[i] = add
                        ? new Color(r.r + c.r * part.Weight, r.g + c.g * part.Weight, r.b + c.b * part.Weight, 1f)
                        : new Color(r.r * Mathf.Lerp(1f, c.r, part.Weight), r.g * Mathf.Lerp(1f, c.g, part.Weight), r.b * Mathf.Lerp(1f, c.b, part.Weight), 1f);
                }
            }
            return result;
        }
    }
}
