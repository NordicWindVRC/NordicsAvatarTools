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
    //   anyway (shown on every part, listed), and otherwise combines it with the most similar.
    // - Combined in a rim slot, the slot's first group's rim shows on each member's parts.
    //   Combined in a matcap slot, the rims are drawn into one texture: added on the same parts,
    //   averaged by area on different parts.
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

        private sealed class Instance
        {
            public int Source;
            public readonly List<AtlasAngleLayer> Layers = new List<AtlasAngleLayer>();
            public AtlasMask Mask => Layers[0].Mask;
        }

        private sealed class Group
        {
            public string Key, Look, Label, Parts;
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
                        group = new Group { Look = look, Key = Hash(look), Label = Label(instance.Layers[0]) };
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
                Key = g.Key, Label = g.Label, Sources = g.Members.Select(m => m.Source).Distinct().ToList()
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

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var groups = Groups(job, context.Sources, context.Notes);
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            (AtlasInstancePlacement Mode, string Target) Placed(Group g) =>
                choice != null && choice.Values.TryGetValue(AtlasInstancePlacements.Id(g.Key), out var value)
                    ? ((AtlasInstancePlacement)Mathf.RoundToInt(value.Number), value.Text) : (AtlasInstancePlacement.Automatic, "");

            int matcapFree = Mathf.Max(0, capabilities.MatcapSlots - context.Plan.MatcapSlotsUsed);
            var rimSlots = new List<List<Group>>();
            var matcapSlots = new List<List<Group>>();
            var waiting = new List<Group>();
            var off = new List<Group>();
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
                else if (!Rim(g)) waiting.Add(g);
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
                if (candidates.Count == 0) off.Add(g);
                else candidates.OrderByDescending(s => Similarity(s[0], g)).First().Add(g);
            }

            var notes = context.Notes;
            for (int k = 0; k < rimSlots.Count; k++) PlaceRim(context, rimSlots[k], k);
            for (int k = 0; k < matcapSlots.Count; k++) PlaceMatcap(context, matcapSlots[k], context.Plan.MatcapSlotsUsed + k);
            context.Plan.MatcapSlotsUsed += matcapSlots.Count;
            if (off.Count > 0) notes.Add("Rims left out: " + string.Join(", ", off.Select(g => g.Label)) + ".");
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
                var allocated = AtlasMaskAllocator.Allocate(context, MaskRequest(job, slot, "rim " + (index + 1), false));
                if (allocated == null)
                {
                    context.Notes.Add($"No mask place is left for rim slot {index + 1}; " + string.Join(", ", slot.Select(g => g.Label)) + " left out.");
                    return;
                }
                mask = allocated.Mask ?? new AtlasMask();
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
            }
            if (capabilities.RimBandOnly && slot[0].Members[0].Layers.Any(l => l.Profile != AtlasAngleProfile.Band || !AtlasRimCurve.ViewOnly(l)))
                context.Notes.Add($"The output shader's rim is a band; {slot[0].Label} is fitted to it.");
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
