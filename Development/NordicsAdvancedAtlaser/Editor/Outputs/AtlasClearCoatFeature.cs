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
                    output.Reflections.Mask = new AtlasMask
                    {
                        Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ClearCoatReflectionMask, active, 0f, (m, w, h) =>
                            AtlasMapEvaluator.ScaledMask(Coat(m).Reflections.Mask, Mathf.Clamp01(Coat(m).Reflections.Strength / reflections), w, h, m))
                    };
            }
            if (context.Capabilities.Provides(AtlasVirtualMap.ClearCoatSpecularMask) && highlights > 0f)
            {
                var keys = Keys(context, active, c => c.Highlights.Mask, c => c.Highlights.Strength / highlights);
                if (Constant(keys, out float constant)) output.Highlights.Strength = highlights * constant;
                else if (output.Reflections.Mask != null && keys.SequenceEqual(reflectionKeys))
                    output.Highlights.Mask = new AtlasMask { Map = output.Reflections.Mask.Map };
                else
                    output.Highlights.Mask = new AtlasMask
                    {
                        Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ClearCoatSpecularMask, active, 0f, (m, w, h) =>
                            AtlasMapEvaluator.ScaledMask(Coat(m).Highlights.Mask, Mathf.Clamp01(Coat(m).Highlights.Strength / highlights), w, h, m))
                    };
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
