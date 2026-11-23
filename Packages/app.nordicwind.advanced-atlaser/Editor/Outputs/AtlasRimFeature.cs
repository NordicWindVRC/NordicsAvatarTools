using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Rim lighting (ATLAS_OUTPUT.md, "Feature inventory"): view-angle angle layers in the output's
    // rim slots, or drawn into matcap slots.
    // - A source's rim is an instance: its angle layer, with the unlit-side layer a style adds
    //   (UTS2's antipodean rim, lilToon's indirect rim). Rims that look the same in several
    //   sources are one group, which shares a slot: each source's mask says where it shows.
    // - The user places each group: its own rim slot, as a matcap, combined with another group,
    //   or off. Automatic gives it a rim slot while one is free (on an output whose rim has no
    //   mask, only when the rim is the same everywhere), then a free matcap slot, then a rim slot
    //   anyway, which shows it on every part (listed), and otherwise combines it with another.
    // - Combined in a rim slot, the slot's first group's rim shows on each member's parts.
    //   Combined in a matcap slot, the rims are drawn into one texture: added on the same parts,
    //   averaged by area on different parts.
    // - Automatic uses a way that shows a rim otherwise than it is only where that costs less
    //   than leaving the rim out (AtlasLayerCost, ATLAS_OUTPUT.md, "Comparing layers"): a rim
    //   on every part adds it where it wasn't, and a shared slot shows another rim in its
    //   place. The rims' effects are taken on their sources' base colors under the atlas's
    //   lighting, over the facing angles (AtlasRimResponse.Effects), by the area each rim's
    //   mask covers. Among the slots a rim can share, it joins the one that costs the least.
    //   A rim the user places goes where the user put it.
    // - How each rim ends up shown goes into the plan in a few words (Ways), for the inspector.
    // - A rim depending only on the view angle draws into a matcap exactly (AtlasRimCurve).
    public sealed class AtlasRimFeature : IAtlasInstanceFeature
    {
        public const string FeatureId = "rims";
        public const int MatcapSize = 256;

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Rim slots", Kind = AtlasFeatureModeKind.Native,
                OptionalMaps = new[] { AtlasVirtualMap.Mask1 },
                Requires = c => c.RimSlots > 0 || c.MatcapSlots > 0
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Rim Lighting";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 1;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && InstancesOf(-1, source, out _).Count > 0;

        public bool CanPlaceAsMatcap(AtlasOutputCapabilities capabilities) => capabilities.MatcapSlots > 0;

        public bool CanBake(AtlasOutputCapabilities capabilities) => false;

        public bool CanAverage(AtlasOutputCapabilities capabilities) => false;

        private sealed class Instance
        {
            public int Source;
            public readonly List<AtlasAngleLayer> Layers = new List<AtlasAngleLayer>();
            public AtlasMask Mask => Layers[0].Mask;
        }

        private sealed class Group
        {
            public string Key, Look, Label, Name, Parts;
            public float Area;
            public readonly List<Instance> Members = new List<Instance>();
            public AtlasAngleLayer First => Members[0].Layers[0];
            // Drawable into a matcap: every layer depends on the view angle alone, without a texture.
            public bool Drawable => Members[0].Layers.All(l => AtlasRimCurve.ViewOnly(l) && l.Color.Map == null);
        }

        // A rim: a view-angle angle layer with a plain color (not an environmental rim).
        private static bool IsRim(AtlasLayer layer) =>
            layer is AtlasAngleLayer angle && angle.Enabled && angle.Source == AtlasAngleSource.ViewAngle &&
            angle.ColorSource == AtlasAngleColorSource.Color && !angle.SpecularOnly &&
            angle.Profile != AtlasAngleProfile.HueRotation && angle.Profile != AtlasAngleProfile.Ramp;

        // A layer a style adds on the unlit side of the rim before it.
        private static bool UnlitSide(AtlasAngleLayer layer, AtlasAngleLayer before) =>
            layer.LightSide.Invert && layer.LightSide.Mode != AtlasLightSideMode.None && ReferenceEquals(layer.Mask, before.Mask);

        private static List<Instance> InstancesOf(int index, AtlasUnifiedMaterial source, out int live)
        {
            live = 0;
            var result = new List<Instance>();
            Instance current = null;
            for (int i = 0; i < source.Layers.Count; i++)
            {
                if (!IsRim(source.Layers[i]))
                {
                    current = null;
                    continue;
                }
                var layer = (AtlasAngleLayer)source.Layers[i];
                if (!AtlasColorStack.IsStatic(source, "layers[" + i + "]"))
                {
                    live++;
                    current = null;
                    continue;
                }
                if (current != null && current.Layers.Count == 1 && UnlitSide(layer, current.Layers[0])) current.Layers.Add(layer);
                else
                {
                    current = new Instance { Source = index };
                    current.Layers.Add(layer);
                    result.Add(current);
                }
            }
            return result;
        }

        private static string Look(Instance instance) =>
            string.Join("|", instance.Layers.Select(l => AtlasFieldwise.Signature(l, "Mask", "Enabled")));

        private static string Label(AtlasAngleLayer layer)
        {
            var c = layer.Color.Color;
            var shown = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f).gamma;
            return "Rim #" + ColorUtility.ToHtmlStringRGB(shown);
        }

        private static List<Group> Groups(AtlasGeneratorJob job, IEnumerable<int> sources, List<string> notes)
        {
            var groups = new List<Group>();
            int live = 0;
            foreach (int i in sources)
            {
                var material = job.Sources[i].Material;
                var instances = InstancesOf(i, material, out int left);
                foreach (var instance in instances)
                {
                    string look = Look(instance);
                    var group = groups.Find(g => g.Look == look);
                    if (group == null)
                    {
                        group = new Group { Look = look, Key = Hash(look), Label = Label(instance.Layers[0]), Name = Label(instance.Layers[0]) };
                        groups.Add(group);
                    }
                    group.Members.Add(instance);
                }
                live += left;
            }
            foreach (var group in groups)
            {
                var members = group.Members.Select(m => m.Source).Distinct().ToList();
                group.Area = members.Sum(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height);
                group.Label += " (" + (members.Count == 1 ? "source " : "sources ") + string.Join(", ", members.Select(i => i + 1)) + ")";
                group.Parts = string.Join(";", group.Members.OrderBy(m => m.Source)
                    .Select(m => m.Source + ":" + AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[m.Source].Material)));
            }
            if (live > 0) notes?.Add("Live rims (animated, audio-driven) aren't merged yet; they're left out.");
            return groups.OrderByDescending(g => g.Area).ToList();
        }

        private static string Hash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Take(6).Select(b => b.ToString("x2")));
        }

        public IReadOnlyList<AtlasFeatureInstance> Instances(AtlasGeneratorJob job, IReadOnlyList<int> sources) =>
            Groups(job, sources, null).Select(g => new AtlasFeatureInstance
            {
                Key = g.Key, Label = g.Label, Name = g.Name, Sources = g.Members.Select(m => m.Source).Distinct().ToList(),
                // A rim is recognized by its color.
                HasPreview = true, PreviewColor = g.First.Color.Color
            }).ToList();

        // The source's member of the slot (the first, if several).
        private static Instance Member(List<Group> slot, int source) =>
            slot.SelectMany(g => g.Members).FirstOrDefault(m => m.Source == source);

        // Per source of the job: the slot's mask there (0 where it has no member).
        private static string[] Keys(AtlasGeneratorJob job, List<Group> slot)
        {
            var keys = new string[job.Sources.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = Member(slot, i) is Instance m ? AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[i].Material) : AtlasMaskKeys.Constant(0f);
            return keys;
        }

        private static bool Everywhere(AtlasGeneratorJob job, Group g) =>
            Keys(job, new List<Group> { g }).All(k => k == AtlasMaskKeys.Constant(1f));

        // The rims' effects on the sources, for placing them by cost: each source's sample
        // points (its darkest, average and brightest base color under the atlas's lighting).
        private sealed class Looks
        {
            private readonly AtlasGeneratorJob job;
            private readonly AtlasLighting lighting;
            private readonly Dictionary<int, List<AtlasLookPoint>> points = new Dictionary<int, List<AtlasLookPoint>>();

            public Looks(AtlasGeneratorJob job, AtlasLighting lighting)
            {
                this.job = job;
                this.lighting = lighting;
            }

            public int Count => job.Sources.Count;

            public float Area(int source) => job.Sources[source].UVRegion.width * job.Sources[source].UVRegion.height;

            // The part of its source's area a member's mask covers, by the mask's average.
            public float Cover(Instance member) => Area(member.Source) * Mathf.Clamp01(AtlasSourceSamples.MaskAverage(member.Mask));

            // A rim's effect on a source; null for a source without a material.
            public Color[] Effects(IReadOnlyList<(AtlasAngleLayer Layer, float Share)> rim, int source)
            {
                var material = job.Sources[source].Material;
                if (material == null) return null;
                if (!points.TryGetValue(source, out var list))
                    points[source] = list = AtlasReferenceLook.Points(AtlasLook.Representative(AtlasSourceSamples.Bases(material)), lighting);
                return AtlasRimResponse.Effects(rim, list);
            }
        }

        private static List<(AtlasAngleLayer Layer, float Share)> Whole(IEnumerable<AtlasAngleLayer> layers) => layers.Select(l => (l, 1f)).ToList();

        // How much a rim changes its sources' look, by area: what leaving it out costs.
        private static float Impact(Group group, Looks looks) =>
            group.Members.Sum(m => looks.Cover(m) * AtlasRimResponse.Cost(looks.Effects(Whole(m.Layers), m.Source), null).Impact);

        // What a rim costs in a rim slot it shares, by area: the slot's first rim shows in its
        // place.
        private static float SharedCost(Group group, List<Group> slot, Looks looks)
        {
            if (group == slot[0]) return 0f;
            var shown = Whole(slot[0].Members[0].Layers);
            return group.Members.Sum(m =>
                looks.Cover(m) * AtlasRimResponse.Cost(looks.Effects(Whole(m.Layers), m.Source), looks.Effects(shown, m.Source)).Cost);
        }

        // What a rim slot costs: each member's rim shown as the slot's first and, where the
        // slot has no mask, that rim on every part that has none of the slot's.
        private static float RimSlotCost(List<Group> slot, Looks looks, bool masked)
        {
            float cost = slot.Sum(g => SharedCost(g, slot, looks));
            if (masked) return cost;
            var shown = Whole(slot[0].Members[0].Layers);
            var covered = new Dictionary<int, float>();
            foreach (var member in slot.SelectMany(g => g.Members))
                covered[member.Source] = (covered.TryGetValue(member.Source, out float c) ? c : 0f) + looks.Cover(member);
            for (int i = 0; i < looks.Count; i++)
            {
                float rest = looks.Area(i) - (covered.TryGetValue(i, out float c) ? c : 0f);
                if (rest > 1e-6f) cost += rest * AtlasRimResponse.Cost(null, looks.Effects(shown, i)).Cost;
            }
            return cost;
        }

        // What each stack of a matcap slot with drawn rims costs and how much it changes its
        // sources' look, by area. Rims on the same parts are stacked exactly. Stacks on
        // different parts are averaged by area, so each part shows every stack's rims at its
        // share.
        private static List<(List<Group> Stack, float Cost, float Impact)> StackCosts(List<Group> slot, Looks looks)
        {
            var stacks = slot.GroupBy(g => g.Parts).Select(s => s.ToList()).ToList();
            float total = stacks.Sum(s => Mathf.Max(0f, s[0].Area));
            var shown = new List<(AtlasAngleLayer Layer, float Share)>();
            foreach (var stack in stacks)
            {
                float share = stacks.Count == 1 ? 1f : total > 0f ? Mathf.Max(0f, stack[0].Area) / total : 1f / stacks.Count;
                shown.AddRange(stack.SelectMany(g => g.Members[0].Layers).Select(l => (l, share)));
            }
            var result = new List<(List<Group> Stack, float Cost, float Impact)>();
            foreach (var stack in stacks)
            {
                var own = Whole(stack.SelectMany(g => g.Members[0].Layers));
                float cost = 0f, impact = 0f;
                foreach (var member in stack[0].Members)
                {
                    var effects = looks.Effects(own, member.Source);
                    float cover = looks.Cover(member);
                    impact += cover * AtlasRimResponse.Cost(effects, null).Impact;
                    if (stacks.Count > 1) cost += cover * AtlasRimResponse.Cost(effects, looks.Effects(shown, member.Source)).Cost;
                }
                result.Add((stack, cost, impact));
            }
            return result;
        }

        private static float SlotCost(List<Group> slot, bool rim, Looks looks, bool masked) =>
            rim ? RimSlotCost(slot, looks, masked) : StackCosts(slot, looks).Sum(s => s.Cost);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var groups = Groups(job, context.Sources, context.Notes);
            var looks = new Looks(job, context.Output.Lighting);
            // A way of showing a rim is worth using where it costs less than leaving it out.
            bool Worth(Group g, float cost)
            {
                float impact = Impact(g, looks);
                return impact <= 0f || cost < impact;
            }
            void Way(Group g, string way, bool leftOut = false) => context.Plan.Ways[(FeatureId, g.Key)] = new AtlasInstanceWay(way, leftOut);
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            (AtlasInstancePlacement Mode, string Target) Placed(Group g) =>
                choice != null && choice.Values.TryGetValue(AtlasInstancePlacements.Id(g.Key), out var value)
                    ? ((AtlasInstancePlacement)Mathf.RoundToInt(value.Number), value.Text) : (AtlasInstancePlacement.Automatic, "");

            int matcapFree = Mathf.Max(0, capabilities.MatcapSlots - context.Plan.MatcapSlotsUsed);
            var rimSlots = new List<List<Group>>();
            var matcapSlots = new List<List<Group>>();
            var waiting = new List<Group>();
            var off = new List<Group>();
            // Left out by Automatic: a rim the slot would show on every part, and a rim a
            // shared slot would show as another.
            var spread = new List<Group>();
            var costly = new List<Group>();
            bool Rim(Group g)
            {
                if (rimSlots.Count >= capabilities.RimSlots) return false;
                rimSlots.Add(new List<Group> { g });
                return true;
            }
            bool Matcap(Group g)
            {
                if (!g.Drawable || matcapSlots.Count >= matcapFree) return false;
                matcapSlots.Add(new List<Group> { g });
                return true;
            }
            List<Group> SlotOf(string key) =>
                rimSlots.Concat(matcapSlots).FirstOrDefault(s => s.Any(g => g.Key == key));

            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Own))
                if (!Rim(g)) waiting.Add(g);
            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.AsMatcap))
                if (!Matcap(g)) waiting.Add(g);
            off.AddRange(groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Off));
            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Automatic))
            {
                // On the same parts as a drawn rim: drawn into its texture, exactly.
                var same = matcapSlots.Find(s => s[0].Parts == g.Parts && g.Drawable);
                if (same != null) same.Add(g);
                else if ((capabilities.RimMask || Everywhere(job, g)) && Rim(g)) { }
                else if (Matcap(g)) { }
                else if (rimSlots.Count >= capabilities.RimSlots) waiting.Add(g);
                // A slot without a mask shows the rim on every part.
                else if (Worth(g, RimSlotCost(new List<Group> { g }, looks, capabilities.RimMask))) Rim(g);
                else spread.Add(g);
            }
            foreach (var g in groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Combined))
            {
                var target = SlotOf(Placed(g).Target);
                if (target != null && (rimSlots.Contains(target) || g.Drawable)) target.Add(g);
                else waiting.Add(g);
            }
            foreach (var g in waiting)
            {
                var candidates = rimSlots.Concat(matcapSlots.Where(s => g.Drawable)).ToList();
                if (candidates.Count == 0)
                {
                    off.Add(g);
                    Way(g, "Left out: no slot is free", true);
                    continue;
                }
                var target = candidates.OrderByDescending(s => Similarity(s[0], g)).First();
                if (Placed(g).Mode == AtlasInstancePlacement.Automatic)
                {
                    // Where the least is lost, and only where that is less than leaving it out.
                    float Added(List<Group> s)
                    {
                        bool rim = rimSlots.Contains(s);
                        return SlotCost(s.Concat(new[] { g }).ToList(), rim, looks, capabilities.RimMask) - SlotCost(s, rim, looks, capabilities.RimMask);
                    }
                    target = candidates.OrderBy(Added).ThenByDescending(s => Similarity(s[0], g)).First();
                    if (!Worth(g, Added(target)))
                    {
                        costly.Add(g);
                        continue;
                    }
                }
                target.Add(g);
            }

            var notes = context.Notes;
            // How each rim is shown; a slot that can't be placed says so instead.
            for (int k = 0; k < rimSlots.Count; k++)
            {
                var keys = Keys(job, rimSlots[k]);
                // Without a mask, a rim that isn't the same everywhere shows on every part.
                bool everyPart = !capabilities.RimMask && !(keys.All(key => key == keys[0]) && AtlasMaskKeys.IsConstant(keys[0], out _));
                foreach (var g in rimSlots[k])
                {
                    float impact = Impact(g, looks);
                    string kept = AtlasInstancePlacements.Kept(impact > 0f ? 1f - SharedCost(g, rimSlots[k], looks) / impact : 1f);
                    Way(g, (rimSlots[k].Count > 1 ? "Shares rim slot " : "In rim slot ") + (k + 1) + (everyPart ? ", on every part" : "") +
                           (kept.Length > 0 ? "; " + kept : ""));
                }
            }
            for (int k = 0; k < matcapSlots.Count; k++)
                foreach (var (stack, cost, impact) in StackCosts(matcapSlots[k], looks))
                {
                    string kept = AtlasInstancePlacements.Kept(impact > 0f ? 1f - cost / impact : 1f);
                    foreach (var g in stack)
                        Way(g, (matcapSlots[k].Count > 1 ? "Shares matcap slot " : "Drawn into matcap slot ") + (context.Plan.MatcapSlotsUsed + k + 1) +
                               (kept.Length > 0 ? "; " + kept : ""));
                }
            foreach (var g in spread) Way(g, "Left out: the rim slot would show it on every part", true);
            foreach (var g in costly) Way(g, "Left out: sharing a slot would change it too much", true);
            for (int k = 0; k < rimSlots.Count; k++) PlaceRim(context, rimSlots[k], k);
            for (int k = 0; k < matcapSlots.Count; k++) PlaceMatcap(context, matcapSlots[k], context.Plan.MatcapSlotsUsed + k);
            context.Plan.MatcapSlotsUsed += matcapSlots.Count;
            if (off.Count > 0) notes.Add("Rims left out: " + string.Join(", ", off.Select(g => g.Label)) + ".");
            if (spread.Count > 0)
                notes.Add("The output shader's rim has no mask, and on every part these rims would add more than they keep, so they are left out: " +
                          string.Join(", ", spread.Select(g => g.Label)) + ". Placing one in its own slot shows it on every part.");
            if (costly.Count > 0)
                notes.Add("A shared slot would show another rim in place of these and change them more than it keeps, so they are left out: " +
                          string.Join(", ", costly.Select(g => g.Label)) + ". Placing one combined with another shares the slot.");
            foreach (var slot in rimSlots.Concat(matcapSlots).Where(s => s.Count > 1))
                notes.Add((rimSlots.Contains(slot) ? "A rim slot shows " + slot[0].Label + " for " : "A matcap slot draws ") +
                          string.Join(", ", slot.Select(g => g.Label)) + ".");
            var textured = groups.Where(g => g.Members[0].Layers.Any(l => l.Color.Map != null)).ToList();
            if (textured.Count > 0)
                notes.Add("Rim color textures aren't baked yet; their color is used: " + string.Join(", ", textured.Select(g => g.Label)) + ".");
        }

        // How alike two groups are: the same profile, then the nearest color.
        private static float Similarity(Group a, Group b)
        {
            float score = a.First.Profile == b.First.Profile ? 2f : 0f;
            var ca = a.First.Color.Color;
            var cb = b.First.Color.Color;
            return score - (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b));
        }

        private static AtlasMaskRequest MaskRequest(AtlasGeneratorJob job, List<Group> slot, string name, bool canInvert) => new AtlasMaskRequest
        {
            Name = name,
            CanInvert = canInvert,
            Key = i => Member(slot, i) is Instance m ? AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[i].Material) : AtlasMaskKeys.Constant(0f),
            InverseKey = i => Member(slot, i) is Instance m ? AtlasMaskKeys.Inverse(m.Mask, 1f, job.Sources[i].Material) : AtlasMaskKeys.Constant(1f),
            Evaluate = (i, material, w, h) => Member(slot, i) is Instance m
                ? AtlasMapEvaluator.ScaledMask(m.Mask, 1f, w, h, material)
                : AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, w, h)
        };

        // A rim slot: the slot's first group's layers, with the slot's mask.
        private static void PlaceRim(AtlasFeatureContext context, List<Group> slot, int index)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var keys = Keys(job, slot);
            AtlasMask mask;
            float scale = 1f;
            if (keys.All(k => k == keys[0]) && AtlasMaskKeys.IsConstant(keys[0], out float constant))
            {
                if (constant <= 0f) return;
                mask = new AtlasMask();
                scale = constant;
            }
            else if (capabilities.RimMask)
            {
                var request = MaskRequest(job, slot, "rim " + (index + 1), false);
                int live = AtlasLiveMasks.Split(context, FeatureId, "rim slot " + (index + 1), request,
                    i => Member(slot, i) is Instance member ? AtlasLiveMasks.Of(member.Mask) : null);
                var allocated = AtlasMaskAllocator.Allocate(context, request);
                if (allocated == null)
                {
                    context.Notes.Add($"No mask place is left for rim slot {index + 1}; " + string.Join(", ", slot.Select(g => g.Label)) + " left out.");
                    foreach (var g in slot) context.Plan.Ways[(FeatureId, g.Key)] = new AtlasInstanceWay("Left out: no mask place is free", true);
                    return;
                }
                mask = allocated.Mask ?? new AtlasMask();
                if (live >= 0) AtlasLiveMasks.Read(mask, live);
                if (allocated.IsConstant) scale = allocated.Constant;
            }
            else
            {
                // The output's rim has no mask: it shows everywhere.
                mask = new AtlasMask();
                context.Notes.Add("The output shader's rim has no mask; " + string.Join(", ", slot.Select(g => g.Label)) + " shows on every part.");
            }
            foreach (var layer in slot[0].Members[0].Layers)
            {
                var copy = new AtlasAngleLayer();
                AtlasFieldwise.Copy(layer, copy);
                copy.Mask = mask;
                copy.Opacity = layer.Opacity * scale;
                // Its color texture isn't baked yet (listed).
                copy.Color.Map = null;
                context.Output.Layers.Add(copy);
                AtlasHueCycling.CopyLayerHue(context.Output, job.Sources[slot[0].Members[0].Source].Material, layer, copy);
                AtlasAudio.CopyLayerAudio(context.Output, job.Sources[slot[0].Members[0].Source].Material, layer, copy);
            }
            // The slot's rim as the output shader shows it: written alone to a material of the
            // shader and read back, layer by layer. Nothing where the shader isn't known.
            int first = context.Output.Layers.Count - slot[0].Members[0].Layers.Count;
            var holder = new AtlasUnifiedMaterial();
            for (int k = first; k < context.Output.Layers.Count; k++) holder.Layers.Add(context.Output.Layers[k]);
            var kept = AtlasKeptMaterial.Shown(capabilities, holder)?.Layers.Where(IsRim).Cast<AtlasAngleLayer>().ToList() ?? new List<AtlasAngleLayer>();
            if (kept.Count > 0 && kept[0].Profile == AtlasAngleProfile.Band &&
                slot[0].Members[0].Layers.Any(l => l.Profile != AtlasAngleProfile.Band || !AtlasRimCurve.ViewOnly(l)))
                context.Notes.Add($"The output shader's rim is a band; {slot[0].Label} is fitted to it.");
            FitAddedAfterLighting(context, slot, first, kept);
        }

        // Where the output shader shows a rim as one added after lighting (its kept form) and
        // the rim blends otherwise, sits on the surface or glows, the rim gets the strength,
        // base color mix and lighting that look most like it on its group's sources
        // (AtlasRimResponse).
        private static void FitAddedAfterLighting(AtlasFeatureContext context, List<Group> slot, int first, List<AtlasAngleLayer> kept)
        {
            var job = context.Job;
            var sources = slot[0].Members.Select(m => m.Source).Distinct()
                .Select(i => ((IReadOnlyList<Color>)AtlasSourceSamples.Bases(job.Sources[i].Material), job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height))
                .ToList();
            for (int k = first; k < context.Output.Layers.Count && k - first < kept.Count; k++)
            {
                if (!(context.Output.Layers[k] is AtlasAngleLayer copy) || AtlasRimResponse.AddsAsIs(copy) || !AtlasRimResponse.AddsAsIs(kept[k - first])) continue;
                if (!AtlasRimCurve.FitBand(copy, out _, out _, out float peak) || peak <= 0f) continue;
                var fitted = AtlasRimResponse.Fit(copy, peak, sources, context.Output.Lighting);
                var color = copy.Color.Color;
                copy.Color.Color = new Color(color.r, color.g, color.b, 1f);
                copy.SourceAlphaIsOpacity = false;
                copy.Placement = AtlasPlacement.Overlay;
                copy.Blend = AtlasBlend.Add;
                copy.EmissionAmount = 0f;
                copy.LightColorMix = 0f;
                copy.HideInShadow = 0f;
                copy.LightResponse = fitted.Lit ? AtlasLightResponse.ScaleByLight : AtlasLightResponse.IgnoreLight;
                copy.BaseColorMix = fitted.BaseColorMix;
                copy.Opacity = fitted.Strength;
                context.Notes.Add($"The output shader's rim adds its color after lighting; {slot[0].Label}'s blend, lighting and glow are fitted to it " +
                                  $"(strength {fitted.Strength.ToString("0.##", CultureInfo.InvariantCulture)}, base color mix " +
                                  $"{fitted.BaseColorMix.ToString("0.##", CultureInfo.InvariantCulture)}, {(fitted.Lit ? "lit by the scene" : "unlit")}).");
            }
        }

        // A matcap slot: the rims drawn into a generated matcap, added after the matcaps.
        private static void PlaceMatcap(AtlasFeatureContext context, List<Group> slot, int index)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var allocated = AtlasMaskAllocator.Allocate(context, MaskRequest(job, slot, "matcap " + (index + 1), capabilities.MatcapMaskInvert));
            if (allocated == null)
            {
                context.Notes.Add($"No mask place is left for matcap slot {index + 1}; " + string.Join(", ", slot.Select(g => g.Label)) + " left out.");
                foreach (var g in slot) context.Plan.Ways[(FeatureId, g.Key)] = new AtlasInstanceWay("Left out: no mask place is free", true);
                return;
            }
            if (allocated.IsConstant && allocated.Constant <= 0f) return;
            var first = slot[0].First;
            // Colors above 1 go into the slot's color where it has one (and its texture's alpha is
            // the opacity: Poiyomi's matcap color × texture, alpha included).
            float peak = Mathf.Max(1f, slot.Max(g => g.Members[0].Layers.Max(l => l.Color.Color.maxColorComponent)));
            float scale = capabilities.MatcapColor ? peak : 1f;
            var blend = first.Blend;
            bool fits = (capabilities.MatcapBlends & Flag(blend, first.Placement)) != 0;
            if (!fits)
            {
                blend = blend == AtlasBlend.Multiply || blend == AtlasBlend.Replace ? AtlasBlend.Multiply : AtlasBlend.Add;
                context.Notes.Add($"Matcap slot {index + 1}'s rim blend is approximated with {blend.ToString().ToLowerInvariant()}: {slot[0].Label}.");
            }
            var parts = slot.Select(g => (Group: g, g.Parts, g.Area)).ToList();
            var reference = new AtlasTextureReference { Space = AtlasTextureSpace.View, Interpretation = AtlasTextureInterpretation.Color };
            context.Plan.GeneratedTextures.Add(new AtlasGeneratedTexture
            {
                Reference = reference, Name = "RimMatcap" + (index + 1), Width = MatcapSize, Height = MatcapSize,
                // Alone in a slot that reads alpha as opacity, the rim's strength is in alpha (Draw).
                Alpha = capabilities.MatcapColor && parts.Count == 1 && parts[0].Group.Members[0].Layers.Count == 1,
                Pixels = () => Draw(parts, blend, scale, capabilities.MatcapColor, MatcapSize)
            });
            var layer = new AtlasLookupLayer
            {
                Projection = AtlasLookupProjection.ViewStabilized,
                Mask = allocated.Mask ?? new AtlasMask(),
                Blend = blend,
                Placement = blend == AtlasBlend.Add && first.Placement == AtlasPlacement.Overlay && (capabilities.MatcapBlends & AtlasMatcapBlends.AddToLight) != 0
                    ? AtlasPlacement.Overlay : AtlasPlacement.Surface,
                Opacity = allocated.IsConstant ? allocated.Constant : 1f,
                EmissionAmount = first.EmissionAmount,
                BaseColorMix = first.BaseColorMix,
                LightColorMix = first.LightColorMix,
                HideInShadow = first.HideInShadow,
                OnOutlines = first.OnOutlines,
                SurfaceNormalAmount = first.SurfaceNormalAmount,
                CenterEye = !first.PerEye,
                SourceAlphaIsOpacity = true
            };
            layer.Source.Map = reference;
            layer.Source.Color = new Color(scale, scale, scale, 1f);
            context.Output.Layers.Add(layer);
        }

        private static AtlasMatcapBlends Flag(AtlasBlend blend, AtlasPlacement placement)
        {
            switch (blend)
            {
                case AtlasBlend.Add: return placement == AtlasPlacement.Overlay ? AtlasMatcapBlends.AddToLight | AtlasMatcapBlends.Add : AtlasMatcapBlends.Add;
                case AtlasBlend.Multiply: return AtlasMatcapBlends.Multiply;
                case AtlasBlend.Replace: return AtlasMatcapBlends.Replace;
                case AtlasBlend.Screen: return AtlasMatcapBlends.Screen;
                case AtlasBlend.Mixed: return AtlasMatcapBlends.Mixed;
                default: return AtlasMatcapBlends.None;
            }
        }

        // The rims drawn into one texture. One rim alone, where the slot reads its texture's alpha
        // as opacity: its color (÷ scale) with the rim × opacity in alpha, exact for every blend.
        // Otherwise premultiplied: color × rim × opacity added, or lerp(1, color, rim × opacity)
        // multiplied; those on the same parts stacked, those on different parts averaged by area.
        private static Color[] Draw(List<(Group Group, string Parts, float Area)> parts, AtlasBlend blend, float scale, bool alpha, int size)
        {
            var pixels = new Color[size * size];
            var facing = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f, ny = (y + 0.5f) / size * 2f - 1f;
                    float d2 = nx * nx + ny * ny;
                    facing[y * size + x] = d2 >= 1f ? 0f : Mathf.Sqrt(1f - d2);
                }
            if (alpha && parts.Count == 1 && parts[0].Group.Members[0].Layers.Count == 1)
            {
                var layer = parts[0].Group.First;
                var c = layer.Color.Color / scale;
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(c.r, c.g, c.b, AtlasRimCurve.Evaluate(layer, facing[i]) * layer.Opacity * layer.Color.Color.a);
                return pixels;
            }
            bool multiply = blend == AtlasBlend.Multiply;
            var stacks = parts.GroupBy(p => p.Parts).ToList();
            float total = stacks.Sum(s => Mathf.Max(0f, s.First().Area));
            foreach (var stack in stacks)
            {
                float share = stacks.Count == 1 ? 1f : total > 0f ? Mathf.Max(0f, stack.First().Area) / total : 1f / stacks.Count;
                var layers = stack.SelectMany(p => p.Group.Members[0].Layers).ToList();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var value = multiply ? Color.white : Color.black;
                    foreach (var layer in layers)
                    {
                        float w = AtlasRimCurve.Evaluate(layer, facing[i]) * layer.Opacity * layer.Color.Color.a;
                        var c = layer.Color.Color;
                        value = multiply
                            ? new Color(value.r * Mathf.Lerp(1f, c.r, w), value.g * Mathf.Lerp(1f, c.g, w), value.b * Mathf.Lerp(1f, c.b, w), 1f)
                            : new Color(value.r + c.r * w / scale, value.g + c.g * w / scale, value.b + c.b * w / scale, 1f);
                    }
                    pixels[i] += value * share;
                }
            }
            for (int i = 0; i < pixels.Length; i++) pixels[i].a = 1f;
            return pixels;
        }
    }
}
