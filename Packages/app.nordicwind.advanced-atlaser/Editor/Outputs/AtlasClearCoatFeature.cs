using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Clear coat (ATLAS_OUTPUT.md, "Feature inventory"): a second, dielectric reflective layer.
    // Each source's coat strength and roughness are baked into maps (no coat: strength 0); its
    // highlight and reflection strengths into masks relative to the strongest, under the mask
    // allocator's rules (one value everywhere: no mask; a highlight mask equal to the reflection
    // mask reads its channel). The rest of the coat (tints, fresnel, cubemaps, the normal map
    // amount, anti-aliasing) is one setup from the reference source.
    // An output without a coat but with a gloss map takes it into the gloss instead: in coated
    // parts, gloss = lerp(base, max(base, coat), coat strength × coat reflection strength), so
    // they get the coat's sharper highlights and reflections on the one layer it has.
    public sealed class AtlasClearCoatFeature : IAtlasSharedValueFeature
    {
        public const string FeatureId = "clearcoat";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Clear coat maps", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.ClearCoatStrength, AtlasVirtualMap.ClearCoatRoughness },
                OptionalMaps = new[] { AtlasVirtualMap.ClearCoatReflectionMask, AtlasVirtualMap.ClearCoatSpecularMask }
            },
            new AtlasFeatureMode
            {
                Id = "gloss", Label = "Into the gloss map", Kind = AtlasFeatureModeKind.Emulated,
                RequiredMaps = new[] { AtlasVirtualMap.Metallic, AtlasVirtualMap.Roughness },
                Requires = c => !c.Provides(AtlasVirtualMap.ClearCoatStrength)
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Clear Coat";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 4;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Clear Coat";

        private static AtlasClearCoat Coat(AtlasUnifiedMaterial material) => material?.Surface.Response.ClearCoat;

        public bool IsPresent(AtlasUnifiedMaterial source) => Coat(source) != null;

        // The coat without what the maps and masks keep per part.
        private static string Setup(AtlasClearCoat coat)
        {
            if (coat == null) return "";
            var copy = new AtlasClearCoat();
            AtlasFieldwise.Copy(coat, copy);
            copy.Strength.Constant = copy.Roughness.Constant = 0f;
            copy.Strength.Map = copy.Roughness.Map = null;
            copy.Highlights.Strength = copy.Reflections.Strength = 0f;
            copy.Highlights.Mask = copy.Reflections.Mask = null;
            return AtlasFieldwise.Signature(copy);
        }

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Select(Coat).Where(c => c != null).Select(Setup).Distinct().Count() > 1;

        public void Resolve(AtlasFeatureContext context)
        {
            if (context.Mode.Kind == AtlasFeatureModeKind.Emulated)
            {
                IntoGloss(context);
                return;
            }
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            int reference = Reference(context);
            var output = new AtlasClearCoat();
            AtlasFieldwise.Copy(Coat(job.Sources[reference].Material), output);
            context.Output.Surface.Response.ClearCoat = output;

            output.Strength.Constant = 1f;
            output.Strength.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ClearCoatStrength, active, 0f,
                (m, w, h) => AtlasMapEvaluator.Scalar(Coat(m).Strength, w, h, m));
            Plain(output.Strength);
            output.Roughness.Constant = 1f;
            output.Roughness.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ClearCoatRoughness, active, 1f,
                (m, w, h) => AtlasMapEvaluator.Scalar(Coat(m).Roughness, w, h, m));
            Plain(output.Roughness);
            int live = AtlasLiveMasks.Split(context, FeatureId, "clear coat", AtlasLiveMasks.Request(context, AtlasVirtualMap.ClearCoatStrength),
                i => AtlasLiveMasks.Of(Coat(job.Sources[i].Material).Strength));
            if (live >= 0) AtlasLiveMasks.Read(output.Strength, live);
            live = AtlasLiveMasks.Split(context, FeatureId, "clear coat smoothness", AtlasLiveMasks.Request(context, AtlasVirtualMap.ClearCoatRoughness),
                i => AtlasLiveMasks.Of(Coat(job.Sources[i].Material).Roughness), decreasing: true);
            if (live >= 0)
            {
                AtlasLiveMasks.Read(output.Roughness, live);
                output.Roughness.InvertSample = output.Roughness.InvertResult = true;
            }

            float reflections = active.Max(i => Coat(job.Sources[i].Material).Reflections.Strength);
            float highlights = active.Max(i => Coat(job.Sources[i].Material).Highlights.Strength);
            output.Reflections.Strength = reflections;
            output.Reflections.Mask = null;
            output.Highlights.Strength = highlights;
            output.Highlights.Mask = null;
            string[] reflectionKeys = null;
            if (context.Capabilities.Provides(AtlasVirtualMap.ClearCoatReflectionMask) && reflections > 0f)
            {
                reflectionKeys = Keys(context, active, c => c.Reflections.Mask, c => c.Reflections.Strength / reflections);
                if (Constant(reflectionKeys, out float constant)) output.Reflections.Strength = reflections * constant;
                else
                {
                    output.Reflections.Mask = new AtlasMask
                    {
                        Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ClearCoatReflectionMask, active, 0f, (m, w, h) =>
                            AtlasMapEvaluator.ScaledMask(Coat(m).Reflections.Mask, Mathf.Clamp01(Coat(m).Reflections.Strength / reflections), w, h, m))
                    };
                    live = AtlasLiveMasks.Split(context, FeatureId, "clear coat reflection strength",
                        AtlasLiveMasks.Request(context, AtlasVirtualMap.ClearCoatReflectionMask), i => AtlasLiveMasks.Of(Coat(job.Sources[i].Material).Reflections.Mask));
                    if (live >= 0) AtlasLiveMasks.Read(output.Reflections.Mask, live);
                }
            }
            if (context.Capabilities.Provides(AtlasVirtualMap.ClearCoatSpecularMask) && highlights > 0f)
            {
                var keys = Keys(context, active, c => c.Highlights.Mask, c => c.Highlights.Strength / highlights);
                if (Constant(keys, out float constant)) output.Highlights.Strength = highlights * constant;
                else if (output.Reflections.Mask != null && keys.SequenceEqual(reflectionKeys))
                    output.Highlights.Mask = new AtlasMask
                    {
                        Map = output.Reflections.Mask.Map, Bus = output.Reflections.Mask.Bus, BusBlend = output.Reflections.Mask.BusBlend
                    };
                else
                {
                    output.Highlights.Mask = new AtlasMask
                    {
                        Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ClearCoatSpecularMask, active, 0f, (m, w, h) =>
                            AtlasMapEvaluator.ScaledMask(Coat(m).Highlights.Mask, Mathf.Clamp01(Coat(m).Highlights.Strength / highlights), w, h, m))
                    };
                    live = AtlasLiveMasks.Split(context, FeatureId, "clear coat highlight strength",
                        AtlasLiveMasks.Request(context, AtlasVirtualMap.ClearCoatSpecularMask), i => AtlasLiveMasks.Of(Coat(job.Sources[i].Material).Highlights.Mask));
                    if (live >= 0) AtlasLiveMasks.Read(output.Highlights.Mask, live);
                }
            }

            string used = Setup(Coat(job.Sources[reference].Material));
            var differ = context.Sources.Where(i => Setup(Coat(job.Sources[i].Material)) != used).Select(i => (i + 1).ToString()).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"The clear coat is taken from source {reference + 1}; the clear coat of " +
                                  (differ.Count == 1 ? "source " : "sources ") + string.Join(", ", differ) + " differs.");
            var materials = context.Sources.Select(i => job.Sources[i].Material).ToList();
            if (materials.Any(m => !AtlasMapEvaluator.CanEvaluate(Coat(m).Strength, m) || !AtlasMapEvaluator.CanEvaluate(Coat(m).Roughness, m)))
                context.Notes.Add("A clear coat map on another UV, remapped or on a global mask written by layers or the second pass isn't baked yet; its slider value is used.");
        }

        // The coat in the surface's roughness map: each coated source's roughness lowered toward
        // the coat's where it's coated. Without a roughness map (no source has highlights), the
        // feature asks for the surface maps itself: no metallic, rough wherever there's no coat.
        private static void IntoGloss(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            if (context.Plan.Modes.TryGetValue("specular", out var specularMode) && specularMode.Kind == AtlasFeatureModeKind.Off)
            {
                context.Notes.Add("Reflection and Specular is off, so the clear coat has no gloss to go into; it's left out.");
                return;
            }
            var response = context.Output.Surface.Response;
            var request = context.Maps.Find(m => m.Map == AtlasVirtualMap.Roughness);
            if (request == null)
            {
                var none = new HashSet<int>();
                response.Metallic.Constant = 1f;
                response.Metallic.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Metallic, none, 0f, null);
                response.Roughness.Constant = 1f;
                response.Roughness.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Roughness, none, 1f, null);
                request = context.Maps.Find(m => m.Map == AtlasVirtualMap.Roughness);
            }
            response.Highlights.Enabled = response.Reflections.Enabled = true;
            if (response.Highlights.Strength <= 0f) response.Highlights.Strength = 1f;
            if (response.Reflections.Strength <= 0f) response.Reflections.Strength = 1f;
            request.Add(new AtlasMapContribution
            {
                Feature = FeatureId, Label = "clear coat gloss", Stage = AtlasMapStage.Layers,
                Applies = active.Contains,
                Apply = content =>
                {
                    var material = content.Material;
                    int width = content.Width, height = content.Height;
                    var coat = Coat(material);
                    if (coat == null) return;
                    using (var coatRoughness = AtlasMapEvaluator.Scalar(coat.Roughness, width, height, material))
                    using (var amount = AtlasMapEvaluator.Scalar(coat.Strength, width, height, material))
                    using (var reflections = AtlasMapEvaluator.ScaledMask(coat.Reflections.Mask, Mathf.Clamp01(coat.Reflections.Strength), width, height, material))
                    {
                        // The amount, strength × reflection strength, rides along in the coat's green.
                        AtlasPixels.Map(amount, reflections, (a, r) => new Color(Mathf.Clamp01(a.r * r.r), 0f, 0f, 1f));
                        AtlasPixels.Map(coatRoughness, amount, (c, a) => new Color(c.r, a.r, 0f, 1f));
                        AtlasPixels.Map(content.Map, coatRoughness, (b, c) =>
                        {
                            float baseGloss = 1f - b.r, coatGloss = 1f - c.r;
                            float gloss = Mathf.Lerp(baseGloss, Mathf.Max(baseGloss, coatGloss), c.g);
                            float roughness = 1f - gloss;
                            return new Color(roughness, roughness, roughness, b.a);
                        });
                    }
                }
            });
            context.Notes.Add("The output shader has no clear coat; it raises the gloss in the coated parts instead " +
                              "(a surface under the coat is as glossy as the coat there; the coat's tints and normal map amount aren't kept).");
            var materials = context.Sources.Select(i => job.Sources[i].Material).ToList();
            if (materials.Any(m => !AtlasMapEvaluator.CanEvaluate(Coat(m).Strength, m) || !AtlasMapEvaluator.CanEvaluate(Coat(m).Roughness, m)))
                context.Notes.Add("A clear coat map on another UV, remapped or on a global mask written by layers or the second pass isn't baked yet; its slider value is used.");
        }

        // A map read as it is: its value, red, not inverted.
        private static void Plain(AtlasScalarInput input)
        {
            input.Channel = AtlasChannel.Red;
            input.InvertSample = input.InvertResult = false;
            input.Remap = null;
        }

        // Per source, the mask key of a strength mask (scaled to the output strength); 0 where
        // the source has no coat.
        private static string[] Keys(AtlasFeatureContext context, HashSet<int> active, System.Func<AtlasClearCoat, AtlasMask> mask,
            System.Func<AtlasClearCoat, float> strength)
        {
            var keys = new string[context.Job.Sources.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                var material = context.Job.Sources[i].Material;
                var coat = Coat(material);
                keys[i] = active.Contains(i) && coat != null
                    ? AtlasMaskKeys.Of(mask(coat), Mathf.Clamp01(strength(coat)), material)
                    : AtlasMaskKeys.Constant(0f);
            }
            return keys;
        }

        private static bool Constant(string[] keys, out float value)
        {
            value = 1f;
            return keys.Length > 0 && keys.All(k => k == keys[0]) && AtlasMaskKeys.IsConstant(keys[0], out value);
        }

        // The chosen source when it has a coat, otherwise the one with the largest region.
        private static int Reference(AtlasFeatureContext context)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == FeatureId);
            if (choice != null && context.Sources.Contains(choice.ReferenceSource)) return choice.ReferenceSource;
            return context.Sources.OrderByDescending(i => context.Job.Sources[i].UVRegion.width * context.Job.Sources[i].UVRegion.height)
                .ThenBy(i => i).First();
        }
    }
}
