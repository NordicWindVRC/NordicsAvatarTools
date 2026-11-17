using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Reflection and specular (ATLAS_OUTPUT.md, "Feature inventory"): each source's metallic and
    // roughness baked into maps; the strengths of its highlights and reflections into masks,
    // relative to the largest, which the output uses. Sources without them get no metallic, full
    // roughness and no highlights or reflections. Reflectance is shared, from the reference source.
    public sealed class AtlasSpecularFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Metallic and smoothness maps", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Metallic, AtlasVirtualMap.Roughness },
                OptionalMaps = new[] { AtlasVirtualMap.ReflectionMask, AtlasVirtualMap.SpecularMask },
                UsesReferenceSource = true
            },
            AtlasFeatureMode.Off
        };

        public string Id => "specular";
        public string DisplayName => "Reflection and Specular";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 3;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        // Unlit and flat lighting show no highlights or reflections.
        public bool IsPresent(AtlasUnifiedMaterial source)
        {
            if (source == null) return false;
            var family = source.Lighting.Family;
            if (family == AtlasLightingFamily.Unlit || family == AtlasLightingFamily.Flat) return false;
            return source.Surface.Response.Highlights.Enabled || source.Surface.Response.Reflections.Enabled;
        }

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            float reflections = 0f, highlights = 0f;
            foreach (int i in active)
            {
                var response = job.Sources[i].Material.Surface.Response;
                if (response.Reflections.Enabled) reflections = Mathf.Max(reflections, response.Reflections.Strength);
                if (response.Highlights.Enabled) highlights = Mathf.Max(highlights, response.Highlights.Strength);
            }
            var output = context.Output.Surface.Response;
            var reference = job.Sources[Reference(context)].Material.Surface.Response;
            output.Reflectance.Constant = reference.Reflectance.Constant;
            output.Reflections.Enabled = reflections > 0f;
            output.Reflections.Strength = reflections;
            output.Highlights.Enabled = highlights > 0f;
            output.Highlights.Strength = highlights;

            output.Metallic.Constant = 1f;
            output.Metallic.Map = Request(context, AtlasVirtualMap.Metallic, active, 0f, (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Metallic, w, h));
            output.Roughness.Constant = 1f;
            output.Roughness.Map = Request(context, AtlasVirtualMap.Roughness, active, 1f, (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Roughness, w, h));
            if (context.Capabilities.Provides(AtlasVirtualMap.ReflectionMask) && reflections > 0f)
                output.Reflections.Mask = new AtlasMask
                {
                    Map = Request(context, AtlasVirtualMap.ReflectionMask, active, 0f, (m, w, h) => Strength(m.Surface.Response.Reflections.Enabled,
                        m.Surface.Response.Reflections.Mask, m.Surface.Response.Reflections.Strength / reflections, w, h))
                };
            if (context.Capabilities.Provides(AtlasVirtualMap.SpecularMask) && highlights > 0f)
                output.Highlights.Mask = new AtlasMask
                {
                    Map = Request(context, AtlasVirtualMap.SpecularMask, active, 0f, (m, w, h) => Strength(m.Surface.Response.Highlights.Enabled,
                        m.Surface.Response.Highlights.Mask, m.Surface.Response.Highlights.Strength / highlights, w, h))
                };
            Notes(context, active);
        }

        // The reference source: the chosen one when it has the feature, otherwise the one with the
        // largest region among those that have it.
        private static int Reference(AtlasFeatureContext context)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == "specular");
            if (choice != null && context.Sources.Contains(choice.ReferenceSource)) return choice.ReferenceSource;
            return context.Sources.OrderByDescending(i => context.Job.Sources[i].UVRegion.width * context.Job.Sources[i].UVRegion.height)
                .ThenBy(i => i).First();
        }

        private static NordicsTextureWrapper Strength(bool enabled, AtlasMask mask, float strength, int width, int height) =>
            enabled ? AtlasMapEvaluator.ScaledMask(mask, Mathf.Clamp01(strength), width, height)
                : AtlasMapEvaluator.Constant(AtlasVirtualMap.SpecularMask, 0f, width, height);

        // A one-value map for every source: the active ones evaluated, the others at `neutral`.
        internal static AtlasTextureReference Request(AtlasFeatureContext context, AtlasVirtualMap map, HashSet<int> active, float neutral,
            System.Func<AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> evaluate)
        {
            var request = new AtlasMapRequest
            {
                Map = map,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                Evaluate = (index, material, width, height) => active.Contains(index)
                    ? evaluate(material, width, height)
                    : AtlasMapEvaluator.Constant(map, neutral, width, height)
            };
            for (int i = 0; i < context.Job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            return request.Reference;
        }

        private static void Notes(AtlasFeatureContext context, IEnumerable<int> active)
        {
            var responses = active.Select(i => context.Job.Sources[i].Material.Surface.Response).ToList();
            if (responses.Any(r => !AtlasMapEvaluator.CanEvaluate(r.Metallic) || !AtlasMapEvaluator.CanEvaluate(r.Roughness)))
                context.Notes.Add("A metallic or roughness map on another UV, remapped or on the mask bus isn't baked yet; its slider value is used.");
            if (responses.Any(r => (r.Highlights.Mask != null && !AtlasMapEvaluator.CanEvaluate(r.Highlights.Mask)) ||
                                   (r.Reflections.Mask != null && !AtlasMapEvaluator.CanEvaluate(r.Reflections.Mask))))
                context.Notes.Add("A highlight or reflection mask on the mask bus, vertex colors or levels isn't baked yet; it counts as 1.");
            if (responses.Any(r => r.SpecularColor != null))
                context.Notes.Add("The specular workflow's specular color isn't converted yet; the metallic and roughness are used.");
            context.Notes.Add("Highlight and reflection settings other than their strengths and masks (tints, fresnel, steps, anisotropy, cubemaps) aren't merged yet; the output shader's defaults are used.");
        }
    }

    // Ambient occlusion (ATLAS_OUTPUT.md, "Feature inventory"): each source's occlusion,
    // 1 − (1 − map) × strength, baked into the output's occlusion map, drawn at strength 1. Sources
    // without one are unoccluded.
    public sealed class AtlasOcclusionFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Occlusion map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Occlusion }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "occlusion";
        public string DisplayName => "Ambient Occlusion";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 4;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source?.Surface.Response.Occlusion.Map?.Texture != null;

        public void Resolve(AtlasFeatureContext context)
        {
            var active = new HashSet<int>(context.Sources);
            var occlusion = context.Output.Surface.Response.Occlusion;
            occlusion.Constant = 1f;
            occlusion.Combine = AtlasScalarCombine.StrengthAroundOne;
            occlusion.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Occlusion, active, 1f,
                (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Occlusion, w, h));
            if (active.Any(i => !AtlasMapEvaluator.CanEvaluate(context.Job.Sources[i].Material.Surface.Response.Occlusion)))
                context.Notes.Add("An occlusion map on another UV, remapped or on the mask bus isn't baked yet; it's left out.");
        }
    }
}
