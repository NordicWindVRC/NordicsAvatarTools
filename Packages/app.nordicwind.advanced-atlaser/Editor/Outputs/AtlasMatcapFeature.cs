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
    // - What a matcap shows (Content): its texture sampled as the slot looks it up, over the
    //   lookup disc through its scale and offset. One that reads black where it adds, screens,
    //   mixes or glows (white where it multiplies, without base color mix) changes nothing.
    // - Each group is measured on its largest source (AtlasMatcapLook): its impact, whether it
    //   brightens or darkens, and its role (lighting, rim, highlight), which
    //   the inspector shows under the matcap, next to its texture, and the build's log lists.
    // - An effect is a matcap whose role is highlight (shine, glints, facets); the others are
    //   environment matcaps (lighting, rims). A shared slot shows its one texture on every
    //   member's parts, which an effect can't share without putting its highlights where they
    //   don't belong.
    // - Lighting with a highlight (a pearl, a lit sphere with a hot spot) is an environment
    //   matcap that is guarded: it takes its own slot before the other environment matcaps do,
    //   and they share its slot only where no other will do. Out of slots it shares, since
    //   most of what it does is its body.
    // - The user places each group: its own slot, combined with another group's slot, or off
    //   (stored by the group's key). Automatic, where the user hasn't: matcaps that change
    //   nothing are left out; groups on the same parts with the same lookup are combined; effects
    //   get their own slots, the most visible first, keeping one for the environment matcaps
    //   where none has one, and those that don't fit are left out; environment matcaps take the rest and share what
    //   doesn't fit, with the environment slot most like them: one that does the same (blends,
    //   glows or both) first, since a slot has one blend and one glow strength.
    // - How visible a group is (Visible): its sources' area × each weight's effect (the shown
    //   brightness where it adds, screens or glows; its darkness where it multiplies) × its
    //   mask's average × the base color mix's darkening, lerp(1, luminance(base), mix), over the
    //   sources' base colors (AtlasSourceSamples). A glow on a dark base with a strong mix barely
    //   shows.
    // - A combined slot holds one generated texture, made with each texture's scale and offset.
    //   Stacks on different parts are averaged by how visible they are, not by area alone.
    //   An output whose slot has no texture transform also gets one for a matcap with them, and
    //   an output whose slot looks up by the view-space normal alone (Toon Standard) for a
    //   matcap with a border or a rotation: the texture is redrawn for the slot's lookup
    //   (Relook). A lookup fixed to the world (Poiyomi's double-sided mode) can't be shown by
    //   a slot that follows the view: its texture is looked up by the view instead (without
    //   its sideways offset, SlotMap), or redrawn as it looks from the front, by the user's
    //   choice. Groups on the same parts stack: their
    //   textures × colors × weights added (for add) or multiplied (for multiply), with every
    //   blend converted to one of the two. Stacks on different parts are averaged by area,
    //   since each part shows the one texture.
    // - A slot that lacks a blend a matcap uses (Toon Standard's add or multiply) takes the
    //   nearest: add, add to light, screen and mixed brighten, so they add; replace and multiply
    //   multiply. Listed as approximated.
    // - Where a slot can't do a source's matcaps as they are (blends it lacks, or base color
    //   mix it doesn't have), it is fitted to them per texel (SlotFit): its strength goes into
    //   the mask and the rest into the base color map, a base color shift, which may come out
    //   brighter or darker than the source's. Where viewers who block the shader see the base
    //   color alone (the output sets a fallback shader), how far it goes is the user's (Base
    //   color shift, shown while a slot is fitted): Automatic stays between the surface's own
    //   color and its average look with the matcap; Keep the base color shows one side of the
    //   matcap; Full shifts as far as the matcap needs, as an output whose blocked view is the
    //   same look always does. Without a base color map, the base color mix goes into the
    //   mask: the mask × lerp(1, the base color's luminance, mix).
    // - A slot shared by matcaps on different parts shows their averaged texture on each
    //   part. A fitted source keeps its own average look there (average in color): its fit
    //   is moved by the difference between its matcaps' average and the shared texture's.
    // - Average in color: a matcap without a slot keeps its average effect in the base color
    //   (Average). The user places a matcap so, and a lighting matcap that ends up without
    //   a slot is kept so. What changes with the view is lost.
    // - A matcap without a slot of its own goes where the least is lost (SlotLosses). Lighting
    //   is judged by its shading: it joins the slot that keeps the most of its members' level
    //   and contrast, or goes into the base color as its average where sharing would cost
    //   more. A rim or a highlight is where it is, so it is judged point by point
    //   (AtlasLayerCost), and is left out where sharing would cost more than it keeps.
    // - How each matcap ends up shown goes into the plan in a few words (Ways), for the
    //   inspector.
    // - A combined slot whose matcaps mix in the base color differently has none: each one's
    //   mix goes into its mask, as where the slot has no base color mix.
    // - A slot that only glows has its texture made from the glows. The slot's one glow
    //   strength is fitted to its matcaps' glows when the material is written (FitGlow).
    // - An output whose matcaps don't glow gets the glow added to the color: one more add of
    //   the matcap at the glow's strength (GlowAsColor), lit rather than glowing.
    // - An output whose slot takes one blend at a time (Toon Standard) gets a matcap with
    //   several weights merged into one, or fitted.
    // - Each slot's mask goes through the mask allocator.
    // - Baked (AtlasMatcapBake): a group placed Baked, or every group in the Baked mode (outputs
    //   without slots), is drawn into the base color and the emission as the virtual view sees
    //   it, instead of taking a slot. Automatic in the Baked mode leaves out effects that
    //   brighten (Brightens): a baked highlight stays in place from every view.
    // How far a matcap slot's fit may shift the base color (ATLAS_OUTPUT.md, "Base color shifts").
    public enum AtlasBaseColorShift { Automatic = 0, Keep = 1, Full = 2 }

    // How a matcap whose lookup is fixed to the world is shown by a slot that follows the view
    // (ATLAS_OUTPUT.md, the matcaps' row of the feature inventory): its texture looked up by
    // the view, or redrawn as it looks from the front.
    public enum AtlasMatcapView { ByView = 0, Front = 1 }

    public sealed class AtlasMatcapFeature : IAtlasInstanceFeature, IAtlasLitBakeFeature
    {
        public const string FeatureId = "matcaps";
        public const string ShiftOptionId = "base-color-shift";

        // The control, shown while a slot is fitted through the base color and viewers who
        // block the shader see the base color alone.
        public static AtlasFeatureOption ShiftOption => new AtlasFeatureOption
        {
            Id = ShiftOptionId, Label = "Base color shift", Default = 0f, Max = 2f,
            Choices = new[] { "Automatic", "Keep the base color", "Full" },
            Tooltip = "A matcap slot that can only brighten or only darken shows a matcap that does both by shifting the base color: " +
                      "raised so the slot darkens it back, or lowered so the slot adds to it. Viewers who block the avatar's shaders " +
                      "see the base color without the matcap. Automatic shifts no further than the material's average look with " +
                      "the matcap, so they see it as bright as it is on average; the matcap then shows weaker. Keep the base color " +
                      "shows only the side of the matcap the slot can. Full shifts as far as the matcap needs: exact with the " +
                      "shader, too bright or too dark without it."
        };

        private static AtlasBaseColorShift Shift(AtlasGeneratorJob job)
        {
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            return choice != null && choice.Values.TryGetValue(AtlasFeatureOption.ValueId(ShiftOptionId), out var value)
                ? (AtlasBaseColorShift)Mathf.Clamp(Mathf.RoundToInt(value.Number), 0, 2) : AtlasBaseColorShift.Automatic;
        }

        public const string ViewOptionId = "world-fixed-lookup";

        // The control, shown while a matcap whose lookup is fixed to the world is in a slot
        // that follows the view.
        public static AtlasFeatureOption ViewOption => new AtlasFeatureOption
        {
            Id = ViewOptionId, Label = "World-fixed matcaps", Default = 0f, Max = 1f,
            Choices = new[] { "Follow the view", "As seen from the front" },
            Tooltip = "A matcap looked up in the world rather than by the view looks different from each side; the output's " +
                      "matcap follows the view. Follow the view looks its texture up by the view, like any matcap: the middle " +
                      "of the texture where the surface faces the viewer, its edge at the outline; a sideways shift of the " +
                      "texture is left out, since it points the other way from behind. As seen from the front " +
                      "keeps its exact look from straight in front, and shows that pattern from the other sides too."
        };

        private static AtlasMatcapView View(AtlasGeneratorJob job)
        {
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            return choice != null && choice.Values.TryGetValue(AtlasFeatureOption.ValueId(ViewOptionId), out var value)
                ? (AtlasMatcapView)Mathf.Clamp(Mathf.RoundToInt(value.Number), 0, 1) : AtlasMatcapView.ByView;
        }

        public const int CombinedSize = 512;

        public static readonly AtlasFeatureMode Baked = new AtlasFeatureMode
        {
            Id = "baked", Label = "Baked into color", Kind = AtlasFeatureModeKind.Baked,
            RequiredMaps = new[] { AtlasVirtualMap.BaseColor }
        };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Matcap slots", Kind = AtlasFeatureModeKind.Native,
                OptionalMaps = new[] { AtlasVirtualMap.Mask1 },
                Requires = c => c.MatcapSlots > 0
            },
            Baked,
            AtlasFeatureMode.Off
        };

        public AtlasLitBake? LitBake(AtlasFeatureMode mode) => mode == Baked ? AtlasLitBake.Matcaps : (AtlasLitBake?)null;

        public bool CanBake(AtlasOutputCapabilities capabilities) => capabilities.Provides(AtlasVirtualMap.BaseColor);

        public bool CanAverage(AtlasOutputCapabilities capabilities) => capabilities.Provides(AtlasVirtualMap.BaseColor);

        // A matcap that is kept as its average where it ends up without a slot: one that stands
        // in for the light, which is as bright on average as it was. A rim's or a highlight's
        // average would light the whole surface.
        private bool KeepsAverage(Group group, AtlasOutputCapabilities capabilities) =>
            group.Measure.Role == AtlasMatcapRole.Lighting && CanAverage(capabilities);

        // How a matcap ends up shown, for the inspector (AtlasOutputPlan.Ways).
        private const string WayUnseen = "Left out: it changes nothing";
        private const string WayAveraged = "Kept as its average in the base color";
        private const string WayBaked = "Baked into the color";
        private const string WayNoSlot = "Left out: no slot is free";

        private static void SetWay(AtlasFeatureContext context, Group group, string way, bool leftOut = false) =>
            context.Plan.Ways[(FeatureId, group.Key)] = new AtlasInstanceWay(way, leftOut);

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
            // Layers the feature added (GlowAsColor), which aren't the source's.
            public readonly HashSet<AtlasLookupLayer> Added = new HashSet<AtlasLookupLayer>();
            public AtlasMask Mask => Layers[0].Mask;
        }

        // Matcaps that look the same in several sources.
        private sealed class Group
        {
            public string Key, Look, Lookup, Label, Name;
            public float Area;
            // Area × visible strength: the order of own slots and the shares of a combined texture.
            public float Visible;
            public bool Effect;
            // Lighting with a highlight on it, which sharing would smear.
            public bool Guarded;
            // The group's look on its largest source: its impact, direction and role.
            public AtlasMatcapMeasure Measure;
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
                        group = new Group { Look = look, Lookup = Lookup(instance), Key = Hash(look), Label = Label(instance), Name = Label(instance) };
                        groups.Add(group);
                    }
                    group.Members.Add(instance);
                }
            }
            foreach (var group in groups)
            {
                var members = group.Members.Select(m => m.Source).Distinct().ToList();
                group.Area = members.Sum(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height);
                group.Visible = group.Members.GroupBy(m => m.Source).Sum(s =>
                    job.Sources[s.Key].UVRegion.width * job.Sources[s.Key].UVRegion.height * Visibility(s.First(), job.Sources[s.Key].Material));
                var largest = group.Members.OrderByDescending(m => job.Sources[m.Source].UVRegion.width * job.Sources[m.Source].UVRegion.height).First();
                group.Measure = AtlasMatcapLook.Measure(largest.Layers, job.Sources[largest.Source].Material);
                group.Effect = group.Measure.Role == AtlasMatcapRole.Highlight;
                group.Guarded = group.Measure.HasHighlight;
                group.Label += " (" + (members.Count == 1 ? "source " : "sources ") + string.Join(", ", members.Select(i => i + 1)) + ")";
                group.Parts = string.Join(";", group.Members.OrderBy(m => m.Source)
                    .Select(m => m.Source + ":" + AtlasMaskKeys.Of(m.Mask, 1f, job.Sources[m.Source].Material)));
            }
            if (live > 0) notes?.Add("Live matcaps (animated, audio-driven) aren't merged yet; they're left out.");
            return groups.OrderByDescending(g => g.Visible).ThenByDescending(g => g.Area).ToList();
        }

        // How strongly a matcap shows on its source, per unit of area (Visible).
        private static float Visibility(Instance instance, AtlasUnifiedMaterial material)
        {
            var first = instance.Layers[0];
            float shown = ContentOf(first).Mean * Mathf.Max(0f, AtlasSourceSamples.Luminance(first.Source.Color));
            float strength = Glow(instance) * shown;
            foreach (var layer in instance.Layers.Where(l => l.Opacity > 0f))
                strength += layer.Opacity * (layer.Blend == AtlasBlend.Multiply ? Mathf.Max(0f, 1f - shown)
                    : layer.Blend == AtlasBlend.Replace ? 1f : shown);
            float mix = Mathf.Clamp01(first.BaseColorMix);
            float darkening = mix <= 0f ? 1f
                : AtlasSourceSamples.Bases(material).Average(b => Mathf.Lerp(1f, Mathf.Clamp01(AtlasSourceSamples.Luminance(b)), mix));
            return strength * AtlasSourceSamples.MaskAverage(first.Mask) * darkening;
        }

        // What a matcap texture shows as its slot looks it up: luminance (linear, before its
        // color) over the lookup disc, its darkest, mean and brightest value; and its mean color
        // (alpha included).
        private sealed class Content
        {
            public float Min, Mean, Max;
            public Color MeanColor = Color.white;
        }

        /// <summary>A matcap texture's mean color (linear, before its color, alpha included) over
        /// the lookup disc: its average look over the visible surface. White without a texture.</summary>
        internal static Color MeanColor(AtlasLookupLayer layer) => ContentOf(layer).MeanColor;

        private const int ContentSize = 32, ContentSteps = 24;
        private static readonly Dictionary<string, Content> Contents = new Dictionary<string, Content>();

        private static Content ContentOf(AtlasLookupLayer layer)
        {
            var map = layer.Source.Map;
            if (!(map?.Texture is Texture2D texture)) return new Content { Min = 1f, Mean = 1f, Max = 1f };
            bool line = layer.Projection == AtlasLookupProjection.Gradient;
            string key = texture.GetInstanceID() + "|" + texture.imageContentsHash + "|" + map.Tiling + "|" + map.Offset + "|" + layer.Tiling + "|" + line;
            if (Contents.TryGetValue(key, out var content)) return content;
            var pixels = AtlasSourceSamples.Small(texture, ContentSize, ContentSize);
            var values = new List<float>();
            var sum = new Color(0f, 0f, 0f, 0f);
            void Add(Vector2 lookup)
            {
                var sample = SampleRepeat(pixels, Vector2.Scale(lookup, map.Tiling) + map.Offset);
                values.Add(Mathf.Max(0f, AtlasSourceSamples.Luminance(sample)));
                sum += sample;
            }
            if (line)
                // The gradient projection looks up u = v = 1 − |N·V|.
                for (int i = 0; i < ContentSteps; i++)
                {
                    float t = (i + 0.5f) / ContentSteps;
                    Add(new Vector2(t, t));
                }
            else
                // The disc the lookup covers: uv = projection × 0.5 × tiling + 0.5.
                for (int y = 0; y < ContentSteps; y++)
                    for (int x = 0; x < ContentSteps; x++)
                    {
                        var p = new Vector2((x + 0.5f) / ContentSteps * 2f - 1f, (y + 0.5f) / ContentSteps * 2f - 1f);
                        if (p.sqrMagnitude > 1f) continue;
                        Add(new Vector2(p.x * 0.5f * layer.Tiling.x + 0.5f, p.y * 0.5f * layer.Tiling.y + 0.5f));
                    }
            values.Sort();
            float total = values.Sum();
            content = new Content
            {
                Min = values[0], Max = values[values.Count - 1], Mean = total / values.Count,
                MeanColor = sum / values.Count
            };
            Contents[key] = content;
            return content;
        }

        private static Color SampleRepeat(Color[] pixels, Vector2 uv)
        {
            float x = (uv.x - Mathf.Floor(uv.x)) * ContentSize - 0.5f, y = (uv.y - Mathf.Floor(uv.y)) * ContentSize - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color At(int i, int j) => pixels[((j % ContentSize + ContentSize) % ContentSize) * ContentSize + ((i % ContentSize + ContentSize) % ContentSize)];
            return Color.Lerp(Color.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Color.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }

        // Whether a group mostly brightens (its heaviest blend adds, or it only glows): an
        // effect that does is a highlight, which a bake would freeze in place. One that mostly
        // multiplies is shading.
        private static bool Brightens(Group group) => group.Members.Any(m =>
        {
            var shown = Shown(m.Layers.Where(l => !m.Added.Contains(l)).ToList());
            return shown.Count == 0 ? Glow(m) > 0f : Main(shown) == AtlasBlend.Add;
        });


        // A matcap that changes nothing where its slot looks it up: black where it adds, screens,
        // mixes or glows; white where it multiplies, without base color or light color mix.
        private static bool NoEffect(Instance instance)
        {
            var first = instance.Layers[0];
            var content = ContentOf(first);
            var color = first.Source.Color;
            float brightest = content.Max * Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            float darkest = content.Min * Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            if (Glow(instance) > 0f && brightest > 1e-3f) return false;
            foreach (var layer in instance.Layers.Where(l => l.Opacity > 0f))
            {
                if (layer.Blend == AtlasBlend.Replace) return false;
                if (layer.Blend == AtlasBlend.Multiply)
                {
                    if (darkest < 1f - 1e-3f || layer.BaseColorMix > 1e-4f || layer.LightColorMix > 1e-4f) return false;
                }
                else if (brightest > 1e-3f) return false;
            }
            return true;
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
                Key = g.Key, Label = g.Label, Name = g.Name, Kind = g.Measure.Summary(), Sources = g.Members.Select(m => m.Source).Distinct().ToList(),
                HasPreview = true, Preview = g.Members[0].Layers[0].Source.Map?.Texture, PreviewColor = g.Members[0].Layers[0].Source.Color
            }).ToList();

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var groups = Groups(job, context.Sources, context.Notes);
            // Matcaps that change nothing where they're looked up.
            var unseen = groups.Where(g => g.Members.All(NoEffect)).ToList();
            if (!capabilities.MatcapEmission)
            {
                var glowing = GlowAsColor(groups.Except(unseen).ToList());
                if (glowing.Count > 0)
                    context.Notes.Add("The output's matcaps don't glow, so the matcap glow of " + string.Join(", ", glowing) +
                                      " is added to the color instead (lit, so it doesn't glow in the dark).");
            }
            var measured = groups.Except(unseen).ToList();
            if (measured.Count > 0)
                context.Notes.Add("Matcap roles, as measured on their materials: " +
                                  string.Join(", ", measured.Select(g => g.Label + ": " + g.Measure.RoleLabel)) + ".");
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            (AtlasInstancePlacement Mode, string Target) Placed(Group g) =>
                choice != null && choice.Values.TryGetValue(AtlasInstancePlacements.Id(g.Key), out var value)
                    ? ((AtlasInstancePlacement)Mathf.RoundToInt(value.Number), value.Text) : (AtlasInstancePlacement.Automatic, "");

            if (context.Mode == Baked)
            {
                var leftOut = groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Off).ToList();
                var automaticUnseen = unseen.Where(g => Placed(g).Mode == AtlasInstancePlacement.Automatic).ToList();
                var effects = groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Automatic && g.Effect && Brightens(g) && !automaticUnseen.Contains(g)).ToList();
                if (automaticUnseen.Count > 0)
                    context.Notes.Add("Matcaps that change nothing where they're looked up are left out: " + string.Join(", ", automaticUnseen.Select(g => g.Label)) + ".");
                if (effects.Count > 0)
                    context.Notes.Add("Matcap highlights (shine, glints, reflections that brighten) aren't baked by Automatic, since a baked highlight stays in place from every view: " +
                                      string.Join(", ", effects.Select(g => g.Label)) + ". Placing one Baked keeps it.");
                if (leftOut.Count > 0)
                    context.Notes.Add("Matcaps left out: " + string.Join(", ", leftOut.Select(g => g.Label)) + ".");
                var kept = groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Averaged && CanAverage(capabilities)).ToList();
                Bake(context, groups.Except(leftOut).Except(automaticUnseen).Except(effects).Except(kept).ToList());
                Average(context, kept, PlacedAverage);
                foreach (var g in automaticUnseen) SetWay(context, g, WayUnseen, true);
                foreach (var g in effects) SetWay(context, g, "Left out: a baked highlight would stay in place", true);
                return;
            }
            var baked = groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Baked && CanBake(capabilities)).ToList();
            Bake(context, baked);
            Average(context, groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Averaged && CanAverage(capabilities)).ToList(), PlacedAverage);

            var slots = new List<List<Group>>();
            var slotOf = new Dictionary<string, List<Group>>();
            var waiting = new List<Group>();
            var off = new List<Group>();
            // Without a slot, though not by the user's choice.
            var stranded = new List<Group>();
            // Kept as their average, where sharing a slot would cost more shading.
            var apart = new List<Group>();
            // Rims and highlights left out, where sharing a slot would cost more than it keeps.
            var costly = new List<Group>();
            var view = View(job);
            var discs = new Dictionary<Group, float[]>();
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
            var automatic = groups.Where(g => Placed(g).Mode == AtlasInstancePlacement.Automatic).ToList();
            unseen = unseen.Where(automatic.Contains).ToList();
            automatic.RemoveAll(unseen.Contains);
            // On the same parts with the same lookup, one texture shows exactly what both did.
            List<Group> Same(Group g) => slots.Find(s => s[0].Parts == g.Parts && s[0].Lookup == g.Lookup && s[0].Effect == g.Effect);
            // Effects get their own slots, the most visible first (the groups' order), keeping one
            // for the environment matcaps where none has one yet; the ones that don't fit are left out.
            var cut = new List<Group>();
            bool environmentNeedsSlot = automatic.Any(g => !g.Effect) && !slots.Any(s => s.Any(x => !x.Effect));
            int forEffects = capabilities.MatcapSlots - slots.Count - (environmentNeedsSlot ? 1 : 0);
            foreach (var g in automatic.Where(g => g.Effect))
            {
                var same = Same(g);
                if (same != null) Join(same, g);
                else if (forEffects > 0 && Own(g)) forEffects--;
                else cut.Add(g);
            }
            // The guarded ones first, so a slot of their own goes to them while there is one.
            foreach (var g in automatic.Where(g => !g.Effect).OrderByDescending(g => g.Guarded))
            {
                var same = Same(g);
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
                if (slots.Count == 0)
                {
                    if (KeepsAverage(g, capabilities)) stranded.Add(g);
                    else
                    {
                        off.Add(g);
                        SetWay(context, g, WayNoSlot, true);
                    }
                    continue;
                }
                // An environment matcap shares an environment slot where there is one.
                var candidates = g.Effect ? slots : slots.Where(s => s.Any(x => !x.Effect)).ToList();
                if (candidates.Count == 0) candidates = slots;
                // A guarded slot isn't shared while another will do.
                var unguarded = candidates.Where(s => !s.Any(x => x.Guarded)).ToList();
                if (unguarded.Count > 0) candidates = unguarded;
                var target = candidates.OrderByDescending(s => Similarity(s, g)).First();
                // A matcap that blends goes where the least is lost, among the slots that do
                // what it does.
                var alike = Blends(g.Members[0])
                    ? candidates.Where(s => Kind(s.Select(x => x.Members[0])) == Kind(new[] { g.Members[0] })).ToList() : new List<List<Group>>();
                if (alike.Count > 0)
                {
                    float Added(List<Group> s) => SlotCost(s.Concat(new[] { g }).ToList(), capabilities, view, discs) - SlotCost(s, capabilities, view, discs);
                    target = alike.OrderBy(Added).ThenByDescending(s => Similarity(s, g)).First();
                    float visible = Mathf.Max(0f, g.Visible);
                    if (g.Measure.Role == AtlasMatcapRole.Lighting)
                    {
                        // As its average it loses its contrast and costs the others nothing.
                        if (CanAverage(capabilities) && Added(target) > visible * (g.Measure.AverageLoss + LiveMargin))
                        {
                            apart.Add(g);
                            continue;
                        }
                    }
                    // A rim or a highlight has no average to keep: left out, it costs all of
                    // itself and the others nothing.
                    else if (visible > 0f && Added(target) >= visible)
                    {
                        costly.Add(g);
                        continue;
                    }
                }
                Join(target, g);
            }
            if (unseen.Count > 0)
                context.Notes.Add("Matcaps that change nothing where they're looked up (black where they add or glow, white where they multiply) are left out: " +
                                  string.Join(", ", unseen.Select(g => g.Label)) + ".");
            if (cut.Count > 0)
                context.Notes.Add("Matcap effects (shine, glints, reflections) don't share a slot, so the weakest are left out where the slots run out: " +
                                  string.Join(", ", cut.Select(g => g.Label)) + ". Placing one in its own slot keeps it.");
            if (costly.Count > 0)
                context.Notes.Add("Sharing a slot would move or weaken these matcap rims and highlights more than it keeps of them, so they are left out: " +
                                  string.Join(", ", costly.Select(g => g.Label)) + ". Placing one combined with another shares the slot.");
            foreach (var g in unseen) SetWay(context, g, WayUnseen, true);
            foreach (var g in cut) SetWay(context, g, WayNoSlot, true);
            foreach (var g in costly) SetWay(context, g, "Left out: sharing a slot would change it too much", true);

            var approximated = new List<string>();
            // The slots that got no mask place.
            var unplaced = new HashSet<int>();
            context.Plan.MatcapSlotsUsed = slots.Count;
            for (int k = 0; k < slots.Count; k++)
            {
                if (Place(context, slots[k], k, approximated)) continue;
                unplaced.Add(k);
                var lost = slots[k].Where(g => !KeepsAverage(g, capabilities)).ToList();
                stranded.AddRange(slots[k].Except(lost));
                foreach (var g in lost) SetWay(context, g, "Left out: no mask place is free", true);
                if (lost.Count > 0)
                    context.Notes.Add($"No mask place is left for matcap slot {k + 1}; " + string.Join(", ", lost.Select(g => g.Label)) + " left out.");
            }
            Average(context, stranded, "No matcap slot is left for {0}; the average effect is kept in the base color instead");
            Average(context, apart, "Sharing a slot would change the shading of {0} and of the slot's own matcaps more than it keeps, so the average " +
                                    "effect is kept in the base color instead; placing one combined with another shares the slot");
            if (context.Plan.BaseColorShifts)
            {
                var shift = Shift(job);
                context.Notes.Add(shift == AtlasBaseColorShift.Keep
                    ? "The base color is kept as it is (Base color shift), so a matcap slot that lacks a matcap's blends shows only the " +
                      "side of it that its blend can."
                    : shift == AtlasBaseColorShift.Full
                        ? "The base color is shifted as far as the fitted matcap slots need (Base color shift: Full); viewers who " +
                          "block the avatar's shaders see it brighter or darker than the material."
                        : "The base color is shifted for the fitted matcap slots no further than the material's average look with the " +
                          "matcap, since viewers who block the avatar's shaders see the base color alone; the matcap shows weaker " +
                          "there. Base color shift: Full shows it exactly.");
            }
            if (off.Count > 0)
                context.Notes.Add("Matcaps left out: " + string.Join(", ", off.Select(g => g.Label)) + ".");
            for (int k = 0; k < slots.Count; k++)
                if (slots[k].Count > 1)
                    context.Notes.Add($"Matcap slot {k + 1} combines " + string.Join(", ", slots[k].Select(g => g.Label)) + ".");
            // What a slot shared across parts keeps of each lighting matcap's shading. A bake
            // isn't chosen here on its own: it stays in place from every view and is made with
            // the light rig's settings. Where one would keep clearly more, the log says so.
            string Percent(float share) => Mathf.RoundToInt(100f * Mathf.Clamp01(share)) + "%";
            var bakes = new List<string>();
            void Hint(Group g, float loss)
            {
                if (CanBake(capabilities) && BakeKeepsMore(g.Measure, loss))
                    bakes.Add(g.Label + " (" + Percent(1f - g.Measure.BakeCost) + " instead of " + Percent(1f - loss) + ")");
            }
            for (int k = 0; k < slots.Count; k++)
            {
                if (unplaced.Contains(k)) continue;
                var losses = SlotLosses(slots[k], capabilities, view, discs);
                var lit = slots[k].Where(g => g.Measure.Role == AtlasMatcapRole.Lighting && losses[g] > 0.005f).ToList();
                if (lit.Count > 0)
                    context.Notes.Add($"Matcap slot {k + 1} shows one texture on different parts; of each matcap's level and contrast it keeps: " +
                                      string.Join(", ", lit.Select(g => g.Label + " " + Percent(1f - losses[g]))) + ".");
                var placed = slots[k].Where(g => g.Measure.Role != AtlasMatcapRole.Lighting && losses[g] > 0.005f).ToList();
                if (placed.Count > 0)
                    context.Notes.Add($"Matcap slot {k + 1} shows one texture on different parts; of each rim's and highlight's look, point by point, it keeps: " +
                                      string.Join(", ", placed.Select(g => g.Label + " " + Percent(1f - losses[g]))) + ".");
                foreach (var g in slots[k])
                {
                    Hint(g, losses[g]);
                    string kept = AtlasInstancePlacements.Kept(1f - losses[g]);
                    SetWay(context, g, (slots[k].Count > 1 ? "Shares matcap slot " : "In matcap slot ") + (k + 1) + (kept.Length > 0 ? "; " + kept : ""));
                }
            }
            foreach (var g in apart.Concat(stranded)) Hint(g, g.Measure.AverageLoss);
            if (bakes.Count > 0)
                context.Notes.Add("A bake would keep more of the shading of " + string.Join(", ", bakes) +
                                  ". Placing one Baked into color does that; its shading then stays where it is drawn, from every side.");
            if (approximated.Count > 0)
                context.Notes.Add("Matcap blends the output shader doesn't have are approximated with add or multiply: " + string.Join(", ", approximated.Distinct()) + ".");
        }

        private static readonly AtlasNormalFeature Normals = new AtlasNormalFeature();

        // Draws the groups' matcaps into the base color, and their glows and overlay adds into
        // the emission where the output has one turned on (otherwise into the color, lit), as
        // the virtual view sees them (AtlasMatcapBake).
        private static void Bake(AtlasFeatureContext context, List<Group> groups)
        {
            if (groups.Count == 0) return;
            var job = context.Job;
            var settings = job.Baking ?? new AtlasBakeSettings();
            float strength = Mathf.Clamp01(settings.strength) * Mathf.Clamp01(settings.matcaps);
            context.Notes.Add("Matcaps baked into the textures as seen from the front: " + string.Join(", ", groups.Select(g => g.Label)) + ".");
            foreach (var group in groups) SetWay(context, group, WayBaked);
            if (strength <= 0f) return;
            var emissionFeature = AtlasOutputFeatures.Find("emission");
            var emissionChoice = job.Features.Find(f => f.FeatureId == "emission");
            bool emissionOn = context.Capabilities.Provides(AtlasVirtualMap.Emission) &&
                              (emissionFeature == null || AtlasOutputFeatures.Effective(emissionFeature, emissionChoice?.ModeId, context.Capabilities)?.Kind != AtlasFeatureModeKind.Off);
            // Each source's baked layers in its layer order (not GlowAsColor's: the bake splits a
            // glow between the emission and the color itself).
            var layers = new Dictionary<int, List<AtlasLookupLayer>>();
            var leftOut = new HashSet<string>();
            foreach (var group in groups)
                foreach (var member in group.Members)
                {
                    if (!layers.TryGetValue(member.Source, out var list)) layers[member.Source] = list = new List<AtlasLookupLayer>();
                    var own = member.Layers.Where(l => !member.Added.Contains(l)).ToList();
                    list.AddRange(own);
                    foreach (var layer in own) foreach (var line in AtlasMatcapBake.LeftOut(layer)) leftOut.Add(line);
                }
            foreach (var entry in layers.ToList())
            {
                var material = job.Sources[entry.Key].Material;
                int Index(AtlasLookupLayer l)
                {
                    int at = material.Layers.IndexOf(l);
                    return at >= 0 ? at * 2 : int.MaxValue;
                }
                layers[entry.Key] = entry.Value.Select((l, k) => (l, k)).OrderBy(e => Index(e.l)).ThenBy(e => e.k).Select(e => e.l).ToList();
            }
            if (leftOut.Count > 0)
                context.Notes.Add("Baked matcaps leave out " + string.Join(", ", leftOut) + ".");
            var unknown = layers.Keys.Where(i => job.Sources[i].Surface == null).ToList();
            if (unknown.Count > 0)
                context.Notes.Add($"Baked matcaps need the meshes, which aren't known here; they're left out on {AtlasLiveMasks.Numbers(unknown)}.");
            var rigs = layers.Keys.ToDictionary(i => i, i => AtlasLightRig.Create(settings, AtlasNormalFeature.UsesSymmetricRig(settings, job.Sources[i])));
            // Seen from the settings' camera; centered for sources whose UVs overlap, so mirrored
            // halves match, as with the symmetric rig.
            var cameras = layers.Keys.ToDictionary(i => i, i => AtlasNormalFeature.UsesSymmetricRig(settings, job.Sources[i])
                ? AtlasLightRig.Direction(0f, settings.matcapViewHeight) : AtlasMatcapBake.Camera(settings));

            NordicsTextureWrapper NormalsOf(int i, List<AtlasLookupLayer> list, int w, int h) =>
                list.Any(l => l.SurfaceNormalAmount > 0f) && Normals.IsPresent(job.Sources[i]) ? AtlasMapEvaluator.Normal(job.Sources[i], w, h, 1f) : null;

            var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            if (request == null) context.Notes.Add("Matcaps are baked into the base color, which is off; their color is left out.");
            else
                // A matcap that adds or screens can take the color above 1.
                request.Add(new AtlasMapContribution
                {
                    Feature = FeatureId, Label = "baked matcaps", Stage = AtlasMapStage.Layers, CanRaise = true,
                    Applies = i => layers.ContainsKey(i) && job.Sources[i].Surface != null,
                    Apply = content =>
                    {
                        int i = content.Source;
                        var sections = job.Sources[i].Surface();
                        if (sections == null || sections.Count == 0) return;
                        var list = layers[i];
                        using (var normals = NormalsOf(i, list, content.Width, content.Height))
                            AtlasMatcapBake.ApplyColor(content.Map, content.Material, list, sections, normals, rigs[i], strength, emissionOn, cameras[i]);
                    }
                });
            if (!emissionOn || !layers.Values.Any(list => list.Any(l => (AtlasMatcapBake.AddsAfterLighting(l) || l.EmissionAmount > 0f) &&
                                                                        AtlasMatcapBake.GlowShare(l) > 0f))) return;
            // Into the emission map, relative to its strength. The emission feature resolves before
            // this one (AtlasOutputFeatures), so its map is there if any source has an emission.
            BakeEmission(context, layers, rigs, cameras, strength, NormalsOf);
        }

        private static void BakeEmission(AtlasFeatureContext context, Dictionary<int, List<AtlasLookupLayer>> layers, Dictionary<int, AtlasLightRig> rigs,
            Dictionary<int, Vector3> cameras, float strength, System.Func<int, List<AtlasLookupLayer>, int, int, NordicsTextureWrapper> normalsOf)
        {
            var job = context.Job;
            var request = context.Maps.FirstOrDefault(r => r.Map == AtlasVirtualMap.Emission);
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
                context.Maps.Add(request);
                output = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                output.Source.Map = request.Reference;
                output.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
                context.Output.Layers.Add(output);
            }
            else output = context.Output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Source.Map == request.Reference);
            var target = output?.Targets.FirstOrDefault(t => t.Target == AtlasSurfaceTarget.Emission);
            if (target == null) return;
            float glowAt = Mathf.Max(target.Opacity, 1e-4f);
            request.Add(new AtlasMapContribution
            {
                Feature = FeatureId, Label = "baked matcap glow", Stage = AtlasMapStage.Overlays, CanRaise = true,
                Applies = i => layers.ContainsKey(i) && job.Sources[i].Surface != null,
                Apply = content =>
                {
                    int i = content.Source, w = content.Width, h = content.Height;
                    var sections = job.Sources[i].Surface();
                    if (sections == null || sections.Count == 0) return;
                    var list = layers[i];
                    bool mixes = list.Any(l => l.BaseColorMix > 0f);
                    using (var baseColor = mixes ? AtlasColorStack.Evaluate(content.Material, w, h, multiplyColor: true) : null)
                    using (var normals = normalsOf(i, list, w, h))
                        AtlasMatcapBake.ApplyEmission(content.Map, baseColor, content.Material, list, sections, normals, rigs[i], strength / glowAt, cameras[i]);
                }
            });
            // Brighter than the emission's strength: the map's brightest value goes into the
            // strength (the map's range step).
            if (request.Carry == null) AtlasEmissionFeature.CarryInStrength(request, target);
        }

        // How alike a slot and a group are, for combining what doesn't fit: doing the same
        // (blends, glows or both) first, then the same lookup, the same main blend and the
        // nearest color.
        private static float Similarity(List<Group> slot, Group b)
        {
            var a = slot[0];
            int kind = Kind(slot.Select(g => g.Members[0])), other = Kind(new[] { b.Members[0] });
            float score = kind == other ? 8f : (kind & other) != 0 ? 4f : 0f;
            score += a.Lookup == b.Lookup ? 4f : 0f;
            var la = a.Members[0].Layers;
            var lb = b.Members[0].Layers;
            if (Main(la) == Main(lb)) score += 2f;
            var ca = la[0].Source.Color;
            var cb = lb[0].Source.Color;
            return score - (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b));
        }

        // An output whose matcaps don't glow adds the glow to the color: one more add layer of
        // the matcap at the glow's strength with its base color mix, as Poiyomi glows by matcap
        // × strength × mask × lerp(1, base color, mix), only after the lighting.
        private static List<string> GlowAsColor(List<Group> groups)
        {
            var labels = new List<string>();
            foreach (var group in groups)
            {
                foreach (var member in group.Members)
                {
                    float glow = Glow(member);
                    if (glow <= 0f) continue;
                    var first = member.Layers[0];
                    var added = Copy(first, first.Mask, AtlasBlend.Add, AtlasPlacement.Surface, glow, first.Source.Map, first.Source.Color);
                    added.EmissionAmount = 0f;
                    member.Layers.Add(added);
                    member.Added.Add(added);
                }
                if (group.Members.Any(m => m.Added.Count > 0)) labels.Add(group.Label);
            }
            return labels;
        }

        // The weights that show (a matcap that only glows has one of 0).
        private static List<AtlasLookupLayer> Shown(List<AtlasLookupLayer> layers) => layers.Where(l => l.Opacity > 0f).ToList();

        // Whether a slot can't show these weights as they are: a blend it lacks, or several
        // blends where it takes one at a time.
        private static bool Lacks(List<AtlasLookupLayer> layers, AtlasOutputCapabilities capabilities)
        {
            var shown = Shown(layers);
            return shown.Any(l => (capabilities.MatcapBlends & Flag(l)) == 0) ||
                   (!capabilities.MatcapWeights && shown.Select(l => (l.Blend, l.Placement)).Distinct().Count() > 1);
        }

        // What matcaps do: 1 blends, 2 glows, 3 both.
        private static int Kind(IEnumerable<Instance> instances) =>
            (instances.Any(Blends) ? 1 : 0) | (instances.Any(m => Glow(m) > 0f) ? 2 : 0);

        private static bool Blends(Instance instance) => instance.Layers.Any(l => l.Opacity > 0f);

        // Poiyomi's matcap emission strength (on the instance's first layer).
        private static float Glow(Instance instance) => instance.Layers.Sum(l => Mathf.Max(0f, l.EmissionAmount));

        // A slot whose matcaps only glow.
        private static bool GlowsOnly(List<Group> slot) => Kind(slot.Select(g => g.Members[0])) == 2;

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

        // Places a slot; false where no mask place is left for it, so nothing of it is placed.
        private bool Place(AtlasFeatureContext context, List<Group> slot, int index, List<string> approximated)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            var main = Main(slot.SelectMany(g => g.Members[0].Layers).ToList());
            var view = View(job);
            bool combined = Combined(slot, capabilities, view);
            // The sources the slot can't show as they are, fitted to (SlotFit) where the output
            // has a base color map; otherwise the base color mix goes into the mask.
            var baseColor = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            var fits = baseColor != null ? Fits(job, slot, main, combined, capabilities, view) : null;
            if (fits != null && fits.Count == 0) fits = null;
            // Without a base color mix in the slot, or with matcaps combined that mix differently.
            bool mixesDiffer = combined && slot.Any(g => Mathf.Abs(BaseColorMix(g.Members[0]) - BaseColorMix(slot[0].Members[0])) > 1e-4f);
            bool fold = fits == null && (!capabilities.MatcapBaseColorMix || mixesDiffer) && slot.Any(g => BaseColorMix(g.Members[0]) > 0f);
            var request = MaskRequest(job, slot, index, fold, capabilities.MatcapMaskInvert, fits);
            int live = AtlasLiveMasks.Split(context, FeatureId, "matcap slot " + (index + 1), request,
                i => Member(slot, i) is Instance member ? AtlasLiveMasks.Of(member.Mask) : null);
            var mask = AtlasMaskAllocator.Allocate(context, request);
            if (mask == null && (fold || fits != null))
            {
                fold = false;
                fits = null;
                request = MaskRequest(job, slot, index, false, capabilities.MatcapMaskInvert, null);
                mask = AtlasMaskAllocator.Allocate(context, request);
            }
            if (fold && !capabilities.MatcapBaseColorMix) approximated.AddRange(slot.Where(g => BaseColorMix(g.Members[0]) > 0f).Select(g => g.Label));
            else if (fold)
                context.Notes.Add($"Matcap slot {index + 1}: " + string.Join(", ", slot.Where(g => BaseColorMix(g.Members[0]) > 0f).Select(g => g.Label)) +
                                  " mix in the base color differently from the matcaps they're combined with; the mix goes into the mask by the base color's luminance.");
            if (mask == null) return false;
            if (fits != null)
                approximated.AddRange(slot.Where(g => g.Members.Any(m => fits.TryGetValue(m.Source, out var fit) && fit.Approximated)).Select(g => g.Label));
            if (mask.IsConstant && mask.Constant <= 0f) return true;
            if (fits != null)
            {
                FitBaseColor(baseColor, fits);
                context.Plan.BaseColorShifts |= capabilities.VRChatFallback;
                var shared = fits.Where(f => f.Value.Shared).Select(f => f.Key).OrderBy(i => i).ToList();
                if (shared.Count > 0)
                    context.Notes.Add($"Matcap slot {index + 1} shows one averaged texture on different parts; " +
                                      AtlasLiveMasks.Numbers(shared) + (shared.Count == 1 ? " keeps" : " keep") +
                                      " the average look of their own matcaps through the base color.");
            }
            // Every slot has a mask object of its own, which the writers group its layers by.
            var slotMask = mask.Mask ?? new AtlasMask();
            if (live >= 0) AtlasLiveMasks.Read(slotMask, live);
            float scale = mask.IsConstant ? mask.Constant : 1f;
            if (slot.Select(g => g.Members.Select(m => m.Source)).SelectMany(s => s).GroupBy(s => s).Any(s => s.Count() > 1))
                context.Notes.Add($"Matcap slot {index + 1} has several matcaps on one source; the first one's mask is used there.");

            // A lookup fixed to the world looks different from each side, which a slot that
            // follows the view can't show: how it is shown is the user's choice.
            var worldFixed = capabilities.MatcapLookup ? new List<string>()
                : slot.Where(g => WorldFixed(g.Members[0].Layers[0]) && g.Members[0].Layers[0].Source.Map?.Texture != null).Select(g => g.Label).ToList();
            if (worldFixed.Count > 0)
            {
                context.Plan.MatcapViews = true;
                context.Notes.Add($"Matcap slot {index + 1}: the lookup of " + string.Join(", ", worldFixed) +
                                  " is fixed to the world, which the output's slot can't follow; " +
                                  (view == AtlasMatcapView.Front ? "its texture is redrawn as it looks from the front."
                                      : slot.Any(g => SlotMap(g.Members[0].Layers[0], capabilities, view) != g.Members[0].Layers[0].Source.Map)
                                          ? "it is looked up by the view instead, without its sideways texture offset."
                                          : "it is looked up by the view instead."));
            }

            var output = context.Output.Layers;
            int first = output.Count;
            AddLayers(context, slot, index, slotMask, scale, approximated, fits != null);
            // What the output shows of a world-fixed lookup follows the view.
            if (!capabilities.MatcapLookup)
                for (int i = first; i < output.Count; i++)
                    if (output[i] is AtlasLookupLayer shown && WorldFixed(shown)) shown.Projection = AtlasLookupProjection.ViewStabilized;
            // Folded into the mask or fitted: the slot's layers don't mix in the base color again.
            if (fold || fits != null)
                for (int i = first; i < output.Count; i++)
                    if (output[i] is AtlasLookupLayer added) added.BaseColorMix = 0f;
            SetGlow(context, slot, index, main, request, mask, combined, fold || fits != null,
                output.Skip(first).OfType<AtlasLookupLayer>().ToList());
            return true;
        }

        // The slot's glow strength, on its first layer. Poiyomi glows by matcap × strength × mask
        // × lerp(1, base color, mix), where the mask is the written one (1 where the allocator
        // made it a constant, which then scales the weights). A slot showing one matcap with its
        // own mask and mix glows as it did. A combined slot, or one whose base color mix went into
        // the mask or the fit, has one strength for what glowed differently: it is fitted when the
        // material is written (FitGlow) and listed.
        private static void SetGlow(AtlasFeatureContext context, List<Group> slot, int index, AtlasBlend main, AtlasMaskRequest request,
            AtlasAllocatedMask mask, bool combined, bool mixRemoved, List<AtlasLookupLayer> layers)
        {
            if (layers.Count == 0) return;
            foreach (var layer in layers.Skip(1)) layer.EmissionAmount = 0f;
            var members = slot.SelectMany(g => g.Members).ToList();
            float scale = mask.IsConstant ? mask.Constant : 1f;
            float strongest = members.Max(Glow);
            layers[0].EmissionAmount = strongest * scale;
            if (strongest <= 0f || !context.Capabilities.MatcapEmission || (!combined && !mixRemoved)) return;

            var parts = TextureParts(slot, main, combined);
            float mixOut = mixRemoved || !context.Capabilities.MatcapBaseColorMix ? 0f : Mathf.Clamp01(layers[0].BaseColorMix);
            // Listed where one strength can't match: glows per texture part that differ, or a
            // glow whose base color mix moved into the mask or the fit.
            float Ratio(Instance m) => parts[m] > 1e-6f ? Glow(m) / parts[m] : Glow(m) > 0f ? float.PositiveInfinity : 0f;
            var ratios = members.Where(m => Glow(m) > 0f || parts[m] > 1e-6f).Select(Ratio).ToList();
            bool differ = ratios.Any(r => float.IsInfinity(r) || Mathf.Abs(r - ratios[0]) > 1e-4f * Mathf.Max(1f, Mathf.Abs(ratios[0])));
            var fitted = slot.Where(g => differ ? g.Members.Any(m => Glow(m) > 0f || parts[m] > 1e-6f)
                    : mixRemoved && g.Members.Any(m => Glow(m) > 0f && BaseColorMix(m) > 0f))
                .Select(g => g.Label).ToList();
            if (fitted.Count > 0)
                context.Notes.Add($"Matcap slot {index + 1} has one glow strength for " + string.Join(", ", fitted) + "; it is fitted to their glows.");
            if (!GlowsOnly(slot))
            {
                var glowing = slot.Where(g => !Blends(g.Members[0]) && Glow(g.Members[0]) > 0f).Select(g => g.Label).ToList();
                if (glowing.Count > 0)
                    context.Notes.Add($"Matcap slot {index + 1}: " + string.Join(", ", glowing) + " only glow, but show the slot's blend there too.");
            }
            var job = context.Job;
            var target = layers[0];
            context.Plan.BeforeWrite.Add(() => target.EmissionAmount = FitGlow(job, slot, request, mask, parts, mixOut));
        }

        // Each matcap's part of the slot's texture: its weight (its blend, or its glow in a slot
        // that only glows) × its stack's area share (Combine); 1 where the slot shows its own.
        private static Dictionary<Instance, float> TextureParts(List<Group> slot, AtlasBlend main, bool combined)
        {
            var result = new Dictionary<Instance, float>();
            bool glows = GlowsOnly(slot);
            float strongest = slot.Max(g => Glow(g.Members[0]));
            var stacks = slot.GroupBy(g => g.Parts ?? "").ToList();
            float total = stacks.Sum(StackWeight);
            foreach (var stack in stacks)
            {
                float share = stacks.Count == 1 ? 1f : total > 0f ? StackWeight(stack) / total : 1f / stacks.Count;
                foreach (var group in stack)
                    foreach (var member in group.Members)
                        result[member] = combined ? share * TextureWeight(member, main, glows, strongest) : 1f;
            }
            return result;
        }

        private static float TextureWeight(Instance instance, AtlasBlend main, bool glows, float strongest) =>
            glows ? (strongest > 0f ? Glow(instance) / strongest : 0f) : Weight(instance, main);

        private const int GlowSize = 16;

        // The glow strength E that best matches the sources' glows (least squares over their
        // texels, by area): E × part × written mask × lerp(1, b, the slot's mix) against each
        // matcap's strength × mask × lerp(1, b, its mix), with b the base color's luminance
        // before the matcaps.
        private static float FitGlow(AtlasGeneratorJob job, List<Group> slot, AtlasMaskRequest request, AtlasAllocatedMask mask,
            Dictionary<Instance, float> parts, float mixOut)
        {
            double ay = 0.0, aa = 0.0;
            using (AtlasEvaluationCache.Scope())
                foreach (var members in slot.SelectMany(g => g.Members).GroupBy(m => m.Source))
                {
                    var source = job.Sources[members.Key];
                    var material = source.Material;
                    double area = Mathf.Max(1e-6f, source.UVRegion.width * source.UVRegion.height) / (GlowSize * GlowSize);
                    using (var written = mask.IsConstant ? null : request.Evaluate(members.Key, material, GlowSize, GlowSize))
                    using (var color = AtlasColorStack.Evaluate(material, GlowSize, GlowSize, multiplyColor: true))
                        foreach (var member in members)
                        {
                            float glow = Glow(member), mix = BaseColorMix(member), part = parts[member];
                            using (var own = AtlasMapEvaluator.ScaledMask(member.Mask, 1f, GlowSize, GlowSize, material))
                                for (int y = 0; y < GlowSize; y++)
                                    for (int x = 0; x < GlowSize; x++)
                                    {
                                        float b = Mathf.Max(0f, Luminance(color.GetPixel(x, y)));
                                        double a = part * (written != null ? written.GetPixel(x, y).r : 1f) * Mathf.Lerp(1f, b, mixOut);
                                        double t = glow * own.GetPixel(x, y).r * Mathf.Lerp(1f, b, mix);
                                        ay += area * a * t;
                                        aa += area * a * a;
                                    }
                        }
                }
            return aa > 1e-12 ? Mathf.Max(0f, (float)(ay / aa)) : 0f;
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
            var view = View(context.Job);
            if (!Combined(slot, capabilities, view))
            {
                var shown = Shown(template);
                bool supported = shown.All(l => (capabilities.MatcapBlends & Flag(l)) != 0);
                bool fits = supported && (capabilities.MatcapWeights || shown.Count <= 1);
                if (fits && !fitted)
                {
                    // A slot taking one blend at a time gets the one that shows.
                    foreach (var layer in capabilities.MatcapWeights || shown.Count == 0 ? template : shown)
                    {
                        var copy = Copy(layer, slotMask, layer.Blend, layer.Placement, layer.Opacity * scale, layer.Source.Map, layer.Source.Color);
                        output.Add(copy);
                        // Its hue shift (and hue cycling) goes with it; an added layer has the matcap's.
                        var from = slot[0].Members[0].Added.Contains(layer) ? template[0] : layer;
                        AtlasHueCycling.CopyLayerHue(context.Output, context.Job.Sources[slot[0].Members[0].Source].Material, from, copy);
                        AtlasAudio.CopyLayerAudio(context.Output, context.Job.Sources[slot[0].Members[0].Source].Material, layer, copy);
                    }
                    return;
                }
                var blend = Main(template);
                if (!supported || shown.Any(l => l.Blend != blend)) approximated.Add(slot[0].Label);
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
            // A slot that only glows is made from the glows, relative to the strongest.
            bool glows = GlowsOnly(slot);
            float strongest = slot.Max(g => Glow(g.Members[0]));
            var parts = slot.Select(g => (Map: SlotMap(g.Members[0].Layers[0], capabilities, view), g.Members[0].Layers[0].Source.Color,
                Weight: TextureWeight(g.Members[0], main, glows, strongest), g.Parts, Area: g.Visible)).ToList();
            // Each matcap's own lookup, where the slot has only the view-space normal's.
            var lookups = slot.Select(g => Relooked(g.Members[0].Layers[0], capabilities, view) ? Relook(g.Members[0].Layers[0], view) : null).ToList();
            var relooked = slot.Where((g, i) => lookups[i] != null).Select(g => g.Label).ToList();
            if (relooked.Count > 0)
                context.Notes.Add($"Matcap slot {index + 1}: the output's slot looks up by the view-space normal over the whole texture, so the texture of " +
                                  string.Join(", ", relooked) + " (another lookup, a border or a rotation) is redrawn for it.");
            var reference = new AtlasTextureReference { Space = AtlasTextureSpace.View, Interpretation = AtlasTextureInterpretation.Color };
            context.Plan.GeneratedTextures.Add(new AtlasGeneratedTexture
            {
                Reference = reference, Name = "Matcap" + (index + 1), Width = CombinedSize, Height = CombinedSize,
                Pixels = () => Combine(parts, main, CombinedSize, lookups)
            });
            var combined = Copy(template[0], slotMask, main, AtlasPlacement.Surface, glows ? 0f : scale, reference, Color.white);
            if (!capabilities.MatcapLookup)
            {
                // The generated texture is made for the slot's lookup.
                combined.Projection = AtlasLookupProjection.ViewStabilized;
                combined.Tiling = Vector2.one;
                combined.LookupRotation = 0f;
            }
            output.Add(combined);
        }

        // A matcap's weight in a combined texture: its layers of the slot's blend.
        private static float Weight(Instance instance, AtlasBlend main) =>
            Mathf.Min(1f, instance.Layers.Where(l => Nearest(l) == main).Sum(l => l.Opacity));

        // One generated texture for the slot: several groups, or a tint, texture transform or
        // lookup the slot doesn't have.
        private static bool Combined(List<Group> slot, AtlasOutputCapabilities capabilities, AtlasMatcapView view)
        {
            var tint = slot[0].Members[0].Layers[0].Source.Color;
            bool tinted = Mathf.Abs(tint.r - 1f) + Mathf.Abs(tint.g - 1f) + Mathf.Abs(tint.b - 1f) > 1e-3f;
            return slot.Count > 1 || (tinted && !capabilities.MatcapColor) ||
                   (Transformed(slot[0].Members[0].Layers[0].Source.Map) && !capabilities.MatcapTextureTransform) ||
                   Relooked(slot[0].Members[0].Layers[0], capabilities, view);
        }

        // A matcap whose texture is redrawn for the slot: the slot looks up by the view-space
        // normal over the whole texture, the matcap by another projection, a border or a
        // rotation. A lookup fixed to the world that is looked up by the view needs it only
        // for its border or rotation.
        private static bool Relooked(AtlasLookupLayer layer, AtlasOutputCapabilities capabilities, AtlasMatcapView view) =>
            !capabilities.MatcapLookup && layer.Source.Map?.Texture != null &&
            !((layer.Projection == AtlasLookupProjection.ViewStabilized || layer.Projection == AtlasLookupProjection.ViewCorrected ||
               (WorldFixed(layer) && view == AtlasMatcapView.ByView)) &&
              layer.Tiling == Vector2.one && layer.LookupRotation == 0f);

        // A lookup fixed to the world, not to the view: it looks different from each side.
        private static bool WorldFixed(AtlasLookupLayer layer) =>
            layer.Projection == AtlasLookupProjection.ReflectionPlanar || layer.Projection == AtlasLookupProjection.LightHalfVector;

        // A matcap's texture as the slot reads it. A lookup fixed to the world that is looked up
        // by the view loses its sideways texture offset. That offset shifts the texture along a
        // world axis, which points the other way from behind; kept by the view, it would pull
        // one side of the texture (a matcap's dark edge and its corners) into view on the same
        // side of the surface from everywhere, as a band. The upward offset is the same from
        // every side and stays.
        private static AtlasTextureReference SlotMap(AtlasLookupLayer layer, AtlasOutputCapabilities capabilities, AtlasMatcapView view)
        {
            var map = layer.Source.Map;
            if (map == null || capabilities.MatcapLookup || !WorldFixed(layer) || view != AtlasMatcapView.ByView || map.Offset.x == 0f)
                return map;
            var shown = new AtlasTextureReference();
            AtlasFieldwise.Copy(map, shown);
            shown.Offset = new Vector2(0f, map.Offset.y);
            return shown;
        }

        // Where a matcap's own lookup reads its texture for the normal that a view-space normal
        // lookup has at `uv`, seen from straight in front: the slot's coordinate to the
        // matcap's. Exact for a lookup that follows the view. A lookup fixed to the world (the
        // planar reflection, the light's half vector) looks different from each side, which
        // no slot that follows the view shows:
        // - By the view: its texture is looked up by the view-space normal, with its border
        //   and rotation. The texture keeps its place on the surface as any matcap does: its
        //   middle where the surface faces the viewer, its edge at the outline.
        // - As seen from the front: its own lookup from straight in front, exact there. The
        //   planar reflection reads at twice the normal's angle, so the texture's edge lies
        //   halfway out and the outline reads its middle again, from every side.
        private static Func<Vector2, Vector2> Relook(AtlasLookupLayer layer, AtlasMatcapView view)
        {
            var read = layer;
            if (WorldFixed(layer) && view == AtlasMatcapView.ByView)
            {
                read = new AtlasLookupLayer();
                AtlasFieldwise.Copy(layer, read);
                read.Projection = AtlasLookupProjection.ViewStabilized;
            }
            var right = Vector3.Cross(Vector3.forward, Vector3.up).normalized;
            return uv =>
            {
                var p = new Vector2(uv.x * 2f - 1f, uv.y * 2f - 1f);
                // Beyond the disc no normal looks up: the edge's value fills the corners.
                if (p.sqrMagnitude > 1f) p.Normalize();
                var normal = right * p.x + Vector3.up * p.y + Vector3.forward * Mathf.Sqrt(Mathf.Max(0f, 1f - p.sqrMagnitude));
                return AtlasMatcapBake.Rotate(AtlasMatcapBake.Lookup(read, normal, Vector3.forward, AtlasMatcapLook.Light), read.LookupRotation);
            };
        }

        // A matcap texture with its own scale or offset.
        private static bool Transformed(AtlasTextureReference map) =>
            map?.Texture != null && (map.Tiling != Vector2.one || map.Offset != Vector2.zero);

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
            // How far the base color may shift, and the source's matcaps on average over the
            // lookup disc (per channel, 0–1): where its average look lies between the chain
            // with black matcaps and with full ones.
            public AtlasBaseColorShift Shift = AtlasBaseColorShift.Full;
            public Color Mean = Color.white;
            // The slot can't show the source's matcaps as they are (a blend it lacks, a base
            // color mix it doesn't have).
            public bool Approximated;
            // A slot shared with matcaps on other parts: where its texture's average lies
            // between T0 and T1 (per channel, 0–1). The fit keeps the source's own average.
            public bool Shared;
            public Color SlotMean = Color.white;
            // Poiyomi's chain: the matcap (instance) each layer belongs to, in order.
            public readonly List<(int Matcap, AtlasBlend Blend, float Weight, Color Color, float Mix)> Layers =
                new List<(int, AtlasBlend, float, Color, float)>();
        }

        private static bool Chained(AtlasLookupLayer layer) =>
            layer.Placement == AtlasPlacement.Surface && layer.Opacity > 0f &&
            (layer.Blend == AtlasBlend.Replace || layer.Blend == AtlasBlend.Multiply || layer.Blend == AtlasBlend.Add ||
             layer.Blend == AtlasBlend.Screen || layer.Blend == AtlasBlend.Mixed);

        // The fit of each source the slot can't show as it is, and of every source that blends
        // in a slot shared across parts, which shows none of them its own texture. A slot with its own
        // texture and strength (not combined) is fitted for all of its sources or none.
        private static Dictionary<int, SlotFit> Fits(AtlasGeneratorJob job, List<Group> slot, AtlasBlend main, bool combined,
            AtlasOutputCapabilities capabilities, AtlasMatcapView view)
        {
            // An output whose blocked view is the same look has nothing to bound.
            var shift = capabilities.VRChatFallback ? Shift(job) : AtlasBaseColorShift.Full;
            var result = new Dictionary<int, SlotFit>();
            var ends = combined ? Ends(slot, main) : (Zero: 0f, Full: Color.white);
            // On different parts, each part shows the slot's averaged texture.
            bool shared = combined && slot.GroupBy(g => g.Parts ?? "").Count() > 1;
            var slotMean = shared ? CombinedValue(slot, main, g => Clamped(SlotMeanColor(g, capabilities, view))) : Color.white;
            // A shared slot shows no source its own texture, so every source that blends is
            // fitted; not where the slot's differing base color mixes go into its masks (Place).
            bool mixesDiffer = slot.Any(g => Mathf.Abs(BaseColorMix(g.Members[0]) - BaseColorMix(slot[0].Members[0])) > 1e-4f);
            bool everySource = shared && !(capabilities.MatcapBaseColorMix && mixesDiffer);
            bool lacking = combined || Lacks(slot[0].Members[0].Layers, capabilities);
            bool all = true;
            foreach (var members in slot.SelectMany(g => g.Members).GroupBy(m => m.Source))
            {
                var layers = job.Sources[members.Key].Material.Layers;
                var ordered = members.OrderBy(m => layers.IndexOf(m.Layers[0])).ToList();
                bool mix = !capabilities.MatcapBaseColorMix && ordered.Any(m => BaseColorMix(m) > 0f);
                bool blends = lacking && ordered.Any(m => m.Layers.Any(l => Chained(l) && l.Blend != main));
                if (!mix && !blends && !(everySource && ordered.Any(m => m.Layers.Any(Chained))))
                {
                    all = false;
                    continue;
                }
                var fit = new SlotFit
                {
                    Mask = Member(slot, members.Key).Mask, Add = main != AtlasBlend.Multiply, Shift = shift, Approximated = mix || blends
                };
                // The source's matcaps on average over the lookup disc.
                var mean = Color.black;
                foreach (var member in ordered) mean += MeanColor(member.Layers[0]) / ordered.Count;
                fit.Mean = Clamped(mean);
                for (int k = 0; k < ordered.Count; k++)
                    foreach (var layer in ordered[k].Layers.Where(Chained))
                        fit.Layers.Add((k, layer.Blend, layer.Opacity, layer.Source.Color, Mathf.Clamp01(layer.BaseColorMix)));
                if (combined)
                {
                    fit.Zero = ends.Zero;
                    fit.Full = ends.Full;
                    fit.Shared = shared;
                    for (int c = 0; c < 3; c++)
                    {
                        float span = ends.Full[c] - ends.Zero;
                        fit.SlotMean[c] = Mathf.Abs(span) > 1e-5f ? Mathf.Clamp01((slotMean[c] - ends.Zero) / span) : fit.Mean[c];
                    }
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

        /// <summary>Whether a bake would keep clearly more of a matcap's shading than a way that
        /// loses <paramref name="loss"/> of it: a lighting matcap without a highlight, whose
        /// bake loses less by more than the margin a way that isn't frozen is preferred by.</summary>
        public static bool BakeKeepsMore(AtlasMatcapMeasure measure, float loss) =>
            measure.Role == AtlasMatcapRole.Lighting && !measure.HasHighlight && loss > measure.BakeCost + LiveMargin;

        // A slot that follows the view is preferred to a matcap's average while it costs less
        // than a quarter of the matcap's shading more: its shading moves with the view, which
        // the measure of level and contrast doesn't count. A bake is named in the log from the
        // same margin.
        private const float LiveMargin = 0.25f;

        // A group's matcap texture × its color (luminance) at the points of the slot's lookup
        // disc, as the generated texture reads it (SlotMap, Relook).
        private static float[] DiscValues(Group group, AtlasOutputCapabilities capabilities, AtlasMatcapView view, Dictionary<Group, float[]> known)
        {
            if (known.TryGetValue(group, out var values)) return values;
            var layer = group.Members[0].Layers[0];
            var color = layer.Source.Color;
            var map = SlotMap(layer, capabilities, view);
            var pixels = map?.Texture is Texture2D texture ? AtlasSourceSamples.Small(texture, ContentSize, ContentSize) : null;
            var lookup = !capabilities.MatcapLookup && Relooked(layer, capabilities, view) ? Relook(layer, view) : null;
            var list = new List<float>();
            for (int y = 0; y < ContentSteps; y++)
                for (int x = 0; x < ContentSteps; x++)
                {
                    var uv = new Vector2((x + 0.5f) / ContentSteps, (y + 0.5f) / ContentSteps);
                    var p = uv * 2f - Vector2.one;
                    if (p.sqrMagnitude > 1f) continue;
                    // A slot with the matcap's own lookup reads through its border.
                    if (capabilities.MatcapLookup) uv = new Vector2(p.x * 0.5f * layer.Tiling.x + 0.5f, p.y * 0.5f * layer.Tiling.y + 0.5f);
                    else if (lookup != null) uv = lookup(uv);
                    var sample = pixels != null ? SampleRepeat(pixels, Vector2.Scale(uv, map.Tiling) + map.Offset) : Color.white;
                    list.Add(Luminance(new Color(color.r * sample.r, color.g * sample.g, color.b * sample.b, 1f)));
                }
            return known[group] = list.ToArray();
        }

        // How far the values lie from their mean on average, as a share of the way from
        // `zero` to `full`.
        private static float Deviation(float[] values, float zero, float full)
        {
            float span = Mathf.Abs(full - zero);
            if (span < 1e-5f || values.Length == 0) return 0f;
            float mean = values.Average();
            return values.Average(v => Mathf.Abs(v - mean)) / span;
        }

        // The values' distances from their mean, as shares of the way from `zero` to `full`: a
        // texture's shape over the lookup disc.
        private static float[] Shape(float[] values, float zero, float full)
        {
            var shape = new float[values.Length];
            float span = Mathf.Abs(full - zero);
            if (span < 1e-5f || values.Length == 0) return shape;
            float mean = values.Average();
            for (int p = 0; p < values.Length; p++) shape[p] = (values[p] - mean) / span;
            return shape;
        }

        // The share of each matcap's look that a slot loses. A slot on one set of parts shows
        // its matcaps as they are. Shared across parts it shows one averaged texture: each
        // source keeps its level through its fit, and what varies over the lookup disc
        // becomes the shared texture's (both between their black and white ends).
        // - Lighting is judged by its shading (level + contrast, AtlasMatcapLook): its contrast
        //   becomes the shared texture's, which varies more or less than its own matcaps'
        //   texture does. An even matcap has no contrast to compare: what the shared texture's
        //   variation shows as on it is taken from its range.
        // - A rim or a highlight is where it is, so it is judged point by point
        //   (AtlasLayerCost): its effect at each point of the disc, its level + its range ×
        //   its texture's shape there, against the same with the shared texture's shape. What
        //   the shared texture shows where the matcap shows nothing counts double, so the
        //   loss can be more than all of it.
        private static Dictionary<Group, float> SlotLosses(List<Group> slot, AtlasOutputCapabilities capabilities, AtlasMatcapView view,
            Dictionary<Group, float[]> discs)
        {
            var losses = slot.ToDictionary(g => g, g => 0f);
            var stacks = slot.GroupBy(g => g.Parts ?? "").ToList();
            if (stacks.Count < 2) return losses;
            bool add = Main(slot.SelectMany(g => g.Members[0].Layers).ToList()) != AtlasBlend.Multiply;
            var main = add ? AtlasBlend.Add : AtlasBlend.Multiply;
            float total = stacks.Sum(StackWeight);
            float[] shared = null;
            float zero = 0f, full = 0f;
            var own = new List<(List<Group> Groups, float[] Values, float Zero, float Full)>();
            foreach (var stack in stacks)
            {
                float share = total > 0f ? StackWeight(stack) / total : 1f / stacks.Count;
                float[] values = null;
                float z = add ? 0f : 1f, f = add ? 0f : 1f;
                foreach (var group in stack)
                {
                    float weight = Weight(group.Members[0], main);
                    var disc = DiscValues(group, capabilities, view, discs);
                    if (values == null) values = Enumerable.Repeat(add ? 0f : 1f, disc.Length).ToArray();
                    for (int p = 0; p < disc.Length; p++)
                        values[p] = add ? values[p] + disc[p] * weight : values[p] * Mathf.Lerp(1f, disc[p], weight);
                    float white = Luminance(group.Members[0].Layers[0].Source.Color);
                    f = add ? f + white * weight : f * Mathf.Lerp(1f, white, weight);
                    if (!add) z *= 1f - weight;
                }
                if (shared == null) shared = new float[values.Length];
                for (int p = 0; p < values.Length; p++) shared[p] += values[p] * share;
                zero += z * share;
                full += f * share;
                own.Add((stack.ToList(), values, z, f));
            }
            float spread = Deviation(shared, zero, full);
            var sharedShape = Shape(shared, zero, full);
            foreach (var (groups, values, z, f) in own)
            {
                float deviation = Deviation(values, z, f);
                float[] shape = null;
                foreach (var group in groups)
                {
                    var measure = group.Measure;
                    if (measure.Role == AtlasMatcapRole.Lighting)
                    {
                        float shading = measure.Level + measure.Contrast;
                        if (shading <= 1e-6f) continue;
                        float shown = deviation > 1e-4f ? measure.Contrast * spread / deviation : measure.Range * spread;
                        losses[group] = Mathf.Abs(measure.Contrast - shown) / shading;
                        continue;
                    }
                    shape = shape ?? Shape(values, z, f);
                    var cost = new AtlasLayerCost();
                    for (int p = 0; p < shape.Length; p++)
                        cost.Add(measure.SignedLevel + measure.Range * shape[p], measure.SignedLevel + measure.Range * sharedShape[p]);
                    if (cost.Impact > 1e-6f) losses[group] = cost.Cost / cost.Impact;
                }
            }
            return losses;
        }

        // What a slot loses of its matcaps' looks, by how visible each is.
        private static float SlotCost(List<Group> slot, AtlasOutputCapabilities capabilities, AtlasMatcapView view, Dictionary<Group, float[]> discs) =>
            SlotLosses(slot, capabilities, view, discs).Sum(l => Mathf.Max(0f, l.Key.Visible) * l.Value);

        // A group's matcap texture on average (linear, before its color) as the slot shows it:
        // over the slot's lookup disc, read as the generated texture reads it (SlotMap, Relook).
        // A slot with the matcap's own lookup shows what the matcap does.
        private static Color SlotMeanColor(Group group, AtlasOutputCapabilities capabilities, AtlasMatcapView view)
        {
            var layer = group.Members[0].Layers[0];
            if (capabilities.MatcapLookup) return MeanColor(layer);
            var map = SlotMap(layer, capabilities, view);
            if (!(map?.Texture is Texture2D texture)) return Color.white;
            var lookup = Relooked(layer, capabilities, view) ? Relook(layer, view) : null;
            var pixels = AtlasSourceSamples.Small(texture, ContentSize, ContentSize);
            var sum = new Color(0f, 0f, 0f, 0f);
            int count = 0;
            for (int y = 0; y < ContentSteps; y++)
                for (int x = 0; x < ContentSteps; x++)
                {
                    var uv = new Vector2((x + 0.5f) / ContentSteps, (y + 0.5f) / ContentSteps);
                    if ((uv * 2f - Vector2.one).sqrMagnitude > 1f) continue;
                    if (lookup != null) uv = lookup(uv);
                    sum += SampleRepeat(pixels, Vector2.Scale(uv, map.Tiling) + map.Offset);
                    count++;
                }
            return sum / count;
        }

        // Average in color: matcaps without a slot keep their average effect in the base color,
        // a bake. Each texel's color goes through the matcap's blends with its texture at its
        // mean over the lookup disc: between the chain with a black matcap and with a white
        // one (Chain). A blend added after the lighting is added to the color, lit. A source's
        // matcaps apply in its layer order. What changes with the view is lost.
        private const string PlacedAverage = "Matcaps kept as their average effect in the base color (Average in color), without a slot: {0}";

        // `note` says why, with {0} for the matcaps.
        private static void Average(AtlasFeatureContext context, List<Group> groups, string note)
        {
            if (groups.Count == 0) return;
            var job = context.Job;
            string labels = string.Join(", ", groups.Select(g => g.Label));
            var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            if (request == null)
            {
                context.Notes.Add("A matcap's average goes into the base color, which is off; left out: " + labels + ".");
                foreach (var group in groups) SetWay(context, group, "Left out: the base color is off", true);
                return;
            }
            foreach (var group in groups) SetWay(context, group, WayAveraged);
            var averages = new Dictionary<int, List<SlotFit>>();
            foreach (var members in groups.SelectMany(g => g.Members).GroupBy(m => m.Source))
            {
                var layers = job.Sources[members.Key].Material.Layers;
                var list = new List<SlotFit>();
                foreach (var member in members.OrderBy(m => layers.IndexOf(m.Layers[0])))
                {
                    var average = new SlotFit { Mask = member.Mask, Mean = Clamped(MeanColor(member.Layers[0])) };
                    foreach (var layer in member.Layers.Where(l => l.Opacity > 0f))
                        average.Layers.Add((0, layer.Blend, layer.Opacity, layer.Source.Color, Mathf.Clamp01(layer.BaseColorMix)));
                    if (average.Layers.Count > 0) list.Add(average);
                }
                if (list.Count > 0) averages[members.Key] = list;
            }
            // A matcap that adds or screens can take the color above 1.
            request.Add(new AtlasMapContribution
            {
                Feature = FeatureId, Label = "matcap averages", Stage = AtlasMapStage.Layers, CanRaise = true,
                Applies = averages.ContainsKey,
                Apply = content =>
                {
                    foreach (var average in averages[content.Source])
                        using (var mask = AtlasMapEvaluator.ScaledMask(average.Mask, 1f, content.Width, content.Height, content.Material))
                            AtlasPixels.Map(content.Map, mask, (c, m) =>
                            {
                                var black = Chain(average, m.r, c, 0f);
                                var full = Chain(average, m.r, c, 1f);
                                return new Color(Mathf.Lerp(black.r, full.r, average.Mean.r), Mathf.Lerp(black.g, full.g, average.Mean.g),
                                    Mathf.Lerp(black.b, full.b, average.Mean.b), c.a);
                            });
                }
            });
            // A glow the output's matcaps would show isn't part of the color.
            bool glows = context.Capabilities.MatcapEmission && groups.Any(g => g.Members.Any(m => Glow(m) > 0f));
            context.Notes.Add(string.Format(note, labels) + (glows ? ". Their glow is left out." : "."));
        }

        // The generated texture's value (Combine) with every matcap texture black and white.
        private static (float Zero, Color Full) Ends(List<Group> slot, AtlasBlend main) =>
            (CombinedValue(slot, main, g => Color.black).r, CombinedValue(slot, main, g => Color.white));

        // The generated texture's value (Combine) where each group's matcap texture reads
        // `texture`: black, white, or its mean over the lookup disc.
        private static Color CombinedValue(List<Group> slot, AtlasBlend main, Func<Group, Color> texture)
        {
            bool add = main != AtlasBlend.Multiply;
            var stacks = slot.GroupBy(g => g.Parts ?? "").ToList();
            float total = stacks.Sum(StackWeight);
            var value = Color.black;
            foreach (var stack in stacks)
            {
                float share = stacks.Count == 1 ? 1f : total > 0f ? StackWeight(stack) / total : 1f / stacks.Count;
                var f = add ? Color.black : Color.white;
                foreach (var group in stack)
                {
                    float weight = Weight(group.Members[0], main);
                    var color = group.Members[0].Layers[0].Source.Color;
                    var read = texture(group);
                    for (int c = 0; c < 3; c++)
                        f[c] = add ? f[c] + color[c] * read[c] * weight : f[c] * Mathf.Lerp(1f, color[c] * read[c], weight);
                }
                for (int c = 0; c < 3; c++) value[c] += f[c] * share;
            }
            value.a = 1f;
            return value;
        }

        private static Color Clamped(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

        // A stack's weight among those on other parts: how visible its groups are together.
        private static float StackWeight(IEnumerable<Group> stack) => stack.Sum(g => Mathf.Max(0f, g.Visible));

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
            // The source's average look with its own matcaps.
            var average = Color.black;
            for (int c = 0; c < 3; c++) average[c] = Mathf.Lerp(black[c], full[c], fit.Mean[c]);
            if (fit.Shared)
                // The slot shows the shared texture here, whose average differs from the
                // source's own matcaps': the ends move by the difference, as far as black
                // stays a color, so the source keeps its average look and the shared
                // texture varies it.
                for (int c = 0; c < 3; c++)
                {
                    float moved = Mathf.Max(average[c] - Mathf.Lerp(black[c], full[c], fit.SlotMean[c]), -black[c]);
                    black[c] += moved;
                    full[c] += moved;
                }
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
            if (fit.Shift == AtlasBaseColorShift.Full) return result;

            // The bounds on the shift, per channel (ATLAS_OUTPUT.md, "Base color shifts"). The
            // blocked view shows the base color alone, so the map stays where it is right for
            // that view too.
            // - Keep: the surface's own color.
            // - Automatic: between the surface's own color and its average look with the
            //   matcaps.
            var bounded = result;
            bool changed = false;
            for (int c = 0; c < 3; c++)
            {
                float low = b[c], high = b[c];
                if (fit.Shift == AtlasBaseColorShift.Automatic)
                {
                    low = Mathf.Min(b[c], average[c]);
                    high = Mathf.Max(b[c], average[c]);
                }
                bounded[c] = Mathf.Clamp(result[c], low, high);
                changed |= Mathf.Abs(bounded[c] - result[c]) > 1e-6f;
            }
            if (!changed) return result;
            // The strength for the bounded base color: an adding slot reaches the matcaps'
            // brightest (base + T1 × s), a multiplying one their darkest (base × lerp(1, T0, s)),
            // as far as the base color lets it.
            float level = Luminance(bounded);
            if (fit.Add) strength = Mathf.Clamp01((Luminance(full) - level) / Mathf.Max(1e-4f, Luminance(fit.Full)));
            else strength = level > 1e-5f && fit.Zero < 1f - 1e-4f ? Mathf.Clamp01((1f - Luminance(black) / level) / (1f - fit.Zero)) : 0f;
            return bounded;
        }

        // The base color map as the fits make it where their sources are: a shift, right only
        // together with the slot. It can come out above 1 (the map's range step), unless the
        // base color is kept.
        private static void FitBaseColor(AtlasMapRequest request, Dictionary<int, SlotFit> fits)
        {
            request.Add(new AtlasMapContribution
            {
                Feature = FeatureId, Label = "matcap slot fit", Stage = AtlasMapStage.Layers, Kind = AtlasContributionKind.Shift,
                CanRaise = fits.Values.Any(f => f.Shift != AtlasBaseColorShift.Keep),
                Applies = fits.ContainsKey,
                Apply = content =>
                {
                    var fit = fits[content.Source];
                    using (var mask = AtlasMapEvaluator.ScaledMask(fit.Mask, 1f, content.Width, content.Height, content.Material))
                        AtlasPixels.Map(content.Map, mask, (c, m) => Solve(fit, m.r, c, out _));
                }
            });
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
        /// the stacks on different parts averaged by their <c>Area</c> (the parts' total: their
        /// area, or how visible they are).</summary>
        /// <param name="lookups">Per part: where its own lookup reads its texture for the slot's
        /// coordinate; null where the slot's lookup is its own.</param>
        public static Color[] Combine(IReadOnlyList<(AtlasTextureReference Map, Color Color, float Weight, string Parts, float Area)> parts,
            AtlasBlend blend, int size, IReadOnlyList<Func<Vector2, Vector2>> lookups = null)
        {
            var indexed = parts.Select((p, i) => (Part: p, Lookup: lookups != null && i < lookups.Count ? lookups[i] : null)).ToList();
            var stacks = indexed.GroupBy(p => p.Part.Parts ?? "").ToList();
            if (stacks.Count == 1)
                return Stack(parts.Select(p => (p.Map, p.Color, p.Weight)).ToList(), blend, size, indexed.Select(p => p.Lookup).ToList());
            float total = stacks.Sum(s => s.Sum(p => Mathf.Max(0f, p.Part.Area)));
            var result = new Color[size * size];
            foreach (var stack in stacks)
            {
                float share = total > 0f ? stack.Sum(p => Mathf.Max(0f, p.Part.Area)) / total : 1f / stacks.Count;
                var pixels = Stack(stack.Select(p => (p.Part.Map, p.Part.Color, p.Part.Weight)).ToList(), blend, size, stack.Select(p => p.Lookup).ToList());
                for (int i = 0; i < result.Length; i++) result[i] += pixels[i] * share;
            }
            for (int i = 0; i < result.Length; i++) result[i].a = 1f;
            return result;
        }

        // The texture as the slot shows it: sampled at the lookup coordinate (the matcap's own,
        // where the slot's is another) × scale + offset, repeated as Poiyomi samples it.
        private static Color[] Transformed(NordicsTextureWrapper texture, AtlasTextureReference map, int size, Func<Vector2, Vector2> lookup = null)
        {
            var result = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    if (lookup != null) uv = lookup(uv);
                    result[y * size + x] = texture.Sample(Vector2.Scale(uv, map.Tiling) + map.Offset, TextureSamplingMode.Bilinear, TextureWrapMode.Repeat);
                }
            return result;
        }

        private static Color[] Stack(IReadOnlyList<(AtlasTextureReference Map, Color Color, float Weight)> parts, AtlasBlend blend, int size,
            IReadOnlyList<Func<Vector2, Vector2>> lookups = null)
        {
            var result = new Color[size * size];
            bool add = blend != AtlasBlend.Multiply;
            for (int i = 0; i < result.Length; i++) result[i] = add ? new Color(0f, 0f, 0f, 1f) : Color.white;
            for (int k = 0; k < parts.Count; k++)
            {
                var part = parts[k];
                var lookup = lookups != null && k < lookups.Count ? lookups[k] : null;
                Color[] pixels = null;
                if (part.Map?.Texture is Texture2D texture)
                    using (var loaded = new NordicsTextureWrapper(texture, TextureDataType.Color))
                    using (var sized = loaded.Resize(size, size, sampling: TextureSamplingMode.Area))
                        pixels = lookup != null || Transformed(part.Map) ? Transformed(sized, part.Map, size, lookup) : sized.GetPixels();
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
