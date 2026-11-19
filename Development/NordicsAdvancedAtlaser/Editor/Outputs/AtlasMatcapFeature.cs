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
    // - A slot without base color mix takes a matcap's base color mix through its mask: the
    //   mask × lerp(1, the base color's luminance, mix).
    // - A slot that lacks a blend a matcap uses (Toon Standard's add or multiply) takes the
    //   nearest: add, add to light, screen and mixed brighten, so they add; replace and multiply
    //   multiply. Listed as approximated.
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
            // The base color mix the slot can't do, folded into the mask (where a place is left).
            bool fold = !capabilities.MatcapBaseColorMix && slot.Any(g => BaseColorMix(g.Members[0]) > 0f);
            var request = MaskRequest(job, slot, index, fold, capabilities.MatcapMaskInvert);
            int live = AtlasLiveMasks.Split(context, FeatureId, "matcap slot " + (index + 1), request,
                i => Member(slot, i) is Instance member ? AtlasLiveMasks.Of(member.Mask) : null);
            var mask = AtlasMaskAllocator.Allocate(context, request);
            if (mask == null && fold)
            {
                fold = false;
                mask = AtlasMaskAllocator.Allocate(context, MaskRequest(job, slot, index, false, capabilities.MatcapMaskInvert));
            }
            if (fold) approximated.AddRange(slot.Where(g => BaseColorMix(g.Members[0]) > 0f).Select(g => g.Label));
            if (mask == null)
            {
                context.Notes.Add($"No mask place is left for matcap slot {index + 1}; " + string.Join(", ", slot.Select(g => g.Label)) + " left out.");
                return;
            }
            if (mask.IsConstant && mask.Constant <= 0f) return;
            // Every slot has a mask object of its own, which the writers group its layers by.
            var slotMask = mask.Mask ?? new AtlasMask();
            if (live >= 0) AtlasLiveMasks.Read(slotMask, live);
            float scale = mask.IsConstant ? mask.Constant : 1f;
            if (slot.Select(g => g.Members.Select(m => m.Source)).SelectMany(s => s).GroupBy(s => s).Any(s => s.Count() > 1))
                context.Notes.Add($"Matcap slot {index + 1} has several matcaps on one source; the first one's mask is used there.");

            var output = context.Output.Layers;
            int first = output.Count;
            AddLayers(context, slot, index, slotMask, scale, approximated);
            // Folded into the mask: the slot's layers don't mix in the base color again.
            if (fold)
                for (int i = first; i < output.Count; i++)
                    if (output[i] is AtlasLookupLayer added) added.BaseColorMix = 0f;
        }

        private static float BaseColorMix(Instance instance) => Mathf.Clamp01(instance.Layers[0].BaseColorMix);

        private static AtlasMaskRequest MaskRequest(AtlasGeneratorJob job, List<Group> slot, int index, bool fold, bool canInvert)
        {
            float Mix(int i) => fold && Member(slot, i) is Instance m ? BaseColorMix(m) : 0f;
            return new AtlasMaskRequest
            {
                Name = "matcap " + (index + 1),
                CanInvert = canInvert,
                Key = i => Member(slot, i) is Instance m
                    ? AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[i].Material) + (Mix(i) > 0f
                        ? "|base " + Mix(i).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "@" +
                          System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(job.Sources[i].Material)
                        : "")
                    : AtlasMaskKeys.Constant(0f),
                InverseKey = i => Member(slot, i) is Instance m
                    ? Mix(i) > 0f ? null : AtlasMaskKeys.Inverse(m.Mask, 1f, job.Sources[i].Material)
                    : AtlasMaskKeys.Constant(1f),
                Evaluate = (i, material, w, h) =>
                {
                    if (!(Member(slot, i) is Instance m)) return AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, w, h);
                    var result = AtlasMapEvaluator.ScaledMask(m.Mask, 1f, w, h, material);
                    float mix = Mix(i);
                    if (mix > 0f)
                        using (var color = AtlasColorStack.Evaluate(material, w, h, multiplyColor: true))
                            AtlasPixels.Map(result, color, (value, c) =>
                            {
                                float factor = Mathf.Lerp(1f, Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b), mix);
                                return new Color(value.r * factor, value.g * factor, value.b * factor, value.a * factor);
                            });
                    return result;
                }
            };
        }

        private void AddLayers(AtlasFeatureContext context, List<Group> slot, int index, AtlasMask slotMask, float scale, List<string> approximated)
        {
            var capabilities = context.Capabilities;
            var template = slot[0].Members[0].Layers;
            var output = context.Output.Layers;
            var tint = template[0].Source.Color;
            bool tinted = Mathf.Abs(tint.r - 1f) + Mathf.Abs(tint.g - 1f) + Mathf.Abs(tint.b - 1f) > 1e-3f;
            if (slot.Count == 1 && (capabilities.MatcapColor || !tinted))
            {
                bool fits = template.All(l => (capabilities.MatcapBlends & Flag(l)) != 0);
                if (fits)
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
                approximated.Add(slot[0].Label);
                var blend = Main(template);
                float opacity = Mathf.Min(1f, template.Where(l => Nearest(l) == blend).Sum(l => l.Opacity));
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
                Weight: Mathf.Min(1f, g.Members[0].Layers.Where(l => Nearest(l) == main).Sum(l => l.Opacity)), g.Parts, g.Area)).ToList();
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
