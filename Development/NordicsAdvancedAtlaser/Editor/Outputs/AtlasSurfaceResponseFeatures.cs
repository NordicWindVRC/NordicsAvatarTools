using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Reflection and specular (ATLAS_OUTPUT.md, "Feature inventory"): each source's metallic and
    // roughness baked into maps; the strengths of its highlights and reflections into masks,
    // relative to the largest, which the output uses. Sources without them get no metallic, full
    // roughness and no highlights or reflections. Reflectance is one value in the output: where
    // the sources need different ones, it comes from the reference source.
    public sealed class AtlasSpecularFeature : IAtlasSharedValueFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Metallic and smoothness maps", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Metallic, AtlasVirtualMap.Roughness },
                OptionalMaps = new[] { AtlasVirtualMap.ReflectionMask, AtlasVirtualMap.SpecularMask }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "specular";
        public string DisplayName => "Reflection and Specular";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 3;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Reflectance";

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities)
        {
            if (capabilities.Reflectance == AtlasReflectanceSupport.None || sources.Count < 2) return false;
            float first = OutputReflectance(sources[0].Surface.Response, capabilities.Reflectance, out _);
            return sources.Skip(1).Any(s => !Same(OutputReflectance(s.Surface.Response, capabilities.Reflectance, out _), first));
        }

        private static bool Same(float a, float b) => Mathf.Abs(a - b) <= 0.005f;

        // The output's reflectance value that gives the source its reflectance. Where the output
        // multiplies it by the smoothness map, that is the source's reflectance over its
        // smoothness: exact where the source's reflectance follows its smoothness (Toon
        // Standard's gloss map × _Reflectance) or both are constants. `exact` is false where the
        // output can't give the source's reflectance everywhere.
        public static float OutputReflectance(AtlasSurfaceResponse response, AtlasReflectanceSupport support, out bool exact)
        {
            var reflectance = response.Reflectance;
            if (support != AtlasReflectanceSupport.FollowsSmoothness)
            {
                exact = reflectance.Map == null;
                return Mathf.Clamp01(reflectance.Constant);
            }
            var roughness = response.Roughness;
            // Smoothness is the map × this strength (roughness inverted after the multiply), or
            // the constant alone without a map.
            float smoothness = roughness.InvertResult ? roughness.Constant : 1f - roughness.Constant;
            bool follows = reflectance.Map != null && roughness.Map != null && roughness.InvertResult && !roughness.InvertSample &&
                           !reflectance.InvertSample && !reflectance.InvertResult && reflectance.Map.Texture == roughness.Map.Texture &&
                           reflectance.Channel == roughness.Channel;
            bool constants = reflectance.Map == null && roughness.Map == null;
            if (smoothness <= 0f)
            {
                // No smoothness, so no reflectance either.
                exact = reflectance.Constant <= 0f;
                return Mathf.Clamp01(reflectance.Constant);
            }
            float value = reflectance.Constant / smoothness;
            exact = (follows || constants) && value <= 1f;
            return Mathf.Clamp01(value);
        }

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
            var support = context.Capabilities.Reflectance;
            int reference = Reference(context);
            if (support != AtlasReflectanceSupport.None)
                output.Reflectance.Constant = OutputReflectance(job.Sources[reference].Material.Surface.Response, support, out _);
            output.Reflections.Enabled = reflections > 0f;
            output.Reflections.Strength = reflections;
            output.Highlights.Enabled = highlights > 0f;
            output.Highlights.Strength = highlights;

            output.Metallic.Constant = 1f;
            output.Metallic.Map = Request(context, AtlasVirtualMap.Metallic, active, 0f, (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Metallic, w, h, m));
            output.Roughness.Constant = 1f;
            output.Roughness.Map = Request(context, AtlasVirtualMap.Roughness, active, 1f, (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Roughness, w, h, m));
            // The masks follow the mask allocator's rules within their own channels: one value
            // everywhere needs no map (folded into the strength), and a highlight mask equal to the
            // reflection mask reads the reflection mask's channel.
            string[] reflectionKeys = null;
            if (context.Capabilities.Provides(AtlasVirtualMap.ReflectionMask) && reflections > 0f)
            {
                reflectionKeys = Keys(context, active, r => r.Reflections.Enabled, r => r.Reflections.Mask, r => r.Reflections.Strength / reflections);
                if (Constant(reflectionKeys, out float constant)) output.Reflections.Strength = reflections * constant;
                else
                    output.Reflections.Mask = new AtlasMask
                    {
                        Map = Request(context, AtlasVirtualMap.ReflectionMask, active, 0f, (m, w, h) => Strength(m, m.Surface.Response.Reflections.Enabled,
                            m.Surface.Response.Reflections.Mask, m.Surface.Response.Reflections.Strength / reflections, w, h))
                    };
            }
            if (context.Capabilities.Provides(AtlasVirtualMap.SpecularMask) && highlights > 0f)
            {
                var keys = Keys(context, active, r => r.Highlights.Enabled, r => r.Highlights.Mask, r => r.Highlights.Strength / highlights);
                if (Constant(keys, out float constant)) output.Highlights.Strength = highlights * constant;
                else if (output.Reflections.Mask != null && keys.SequenceEqual(reflectionKeys))
                    output.Highlights.Mask = new AtlasMask { Map = output.Reflections.Mask.Map };
                else
                    output.Highlights.Mask = new AtlasMask
                    {
                        Map = Request(context, AtlasVirtualMap.SpecularMask, active, 0f, (m, w, h) => Strength(m, m.Surface.Response.Highlights.Enabled,
                            m.Surface.Response.Highlights.Mask, m.Surface.Response.Highlights.Strength / highlights, w, h))
                    };
            }
            Notes(context, active);
            ReflectanceNotes(context, reference);
            StrengthNotes(context, AtlasVirtualMap.SpecularMask, "highlight", r => r.Highlights.Enabled ? r.Highlights.Strength : 0f);
            StrengthNotes(context, AtlasVirtualMap.ReflectionMask, "reflection", r => r.Reflections.Enabled ? r.Reflections.Strength : 0f);
        }

        // Without a mask, the output can't keep different strengths: one line naming the weaker sources.
        private static void StrengthNotes(AtlasFeatureContext context, AtlasVirtualMap mask, string name, System.Func<AtlasSurfaceResponse, float> strength)
        {
            if (context.Capabilities.Provides(mask)) return;
            var strengths = context.Sources.Select(i => (index: i, value: strength(context.Job.Sources[i].Material.Surface.Response))).ToList();
            float strongest = strengths.Max(s => s.value);
            var weaker = strengths.Where(s => s.value < strongest - 0.005f).Select(s => s.index).ToList();
            if (weaker.Count > 0)
                context.Notes.Add($"The output shader has no {name} strength or mask; the weaker {name}s of {Numbers(weaker)} aren't kept.");
        }

        // Sources whose reflectance the output doesn't keep, by source number.
        private static void ReflectanceNotes(AtlasFeatureContext context, int reference)
        {
            var support = context.Capabilities.Reflectance;
            var sources = context.Sources.Select(i => (index: i, response: context.Job.Sources[i].Material.Surface.Response)).ToList();
            if (support == AtlasReflectanceSupport.None)
            {
                var lost = sources.Where(s => s.response.Reflectance.Map != null || !Same(s.response.Reflectance.Constant, 0.5f)).ToList();
                if (lost.Count > 0)
                    context.Notes.Add($"The output shader's reflectance is fixed at 0.5 (4%); the reflectance of {Numbers(lost.Select(s => s.index))} isn't kept.");
                return;
            }
            float used = OutputReflectance(context.Job.Sources[reference].Material.Surface.Response, support, out _);
            var differ = sources.Where(s => !Same(OutputReflectance(s.response, support, out _), used)).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"Reflectance is taken from source {reference + 1}; the reflectance of {Numbers(differ.Select(s => s.index))} differs.");
            var inexact = sources.Where(s => { OutputReflectance(s.response, support, out bool exact); return !exact; }).ToList();
            if (inexact.Count > 0 && support == AtlasReflectanceSupport.FollowsSmoothness)
                context.Notes.Add($"The output shader's reflectance follows its smoothness map; the reflectance of {Numbers(inexact.Select(s => s.index))} doesn't, so it matches only where the smoothness is full.");
        }

        private static string Numbers(IEnumerable<int> indices)
        {
            var list = indices.Select(i => (i + 1).ToString()).ToList();
            return (list.Count == 1 ? "source " : "sources ") + string.Join(", ", list);
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

        // Per source, the mask key of the feature's strength mask (scaled to the output strength).
        private static string[] Keys(AtlasFeatureContext context, HashSet<int> active, System.Func<AtlasSurfaceResponse, bool> enabled,
            System.Func<AtlasSurfaceResponse, AtlasMask> mask, System.Func<AtlasSurfaceResponse, float> strength)
        {
            var keys = new string[context.Job.Sources.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                var material = context.Job.Sources[i].Material;
                var response = material.Surface.Response;
                keys[i] = active.Contains(i) && enabled(response)
                    ? AtlasMaskKeys.Of(mask(response), Mathf.Clamp01(strength(response)), material)
                    : AtlasMaskKeys.Constant(0f);
            }
            return keys;
        }

        private static bool Constant(string[] keys, out float value)
        {
            value = 1f;
            return keys.Length > 0 && keys.All(k => k == keys[0]) && AtlasMaskKeys.IsConstant(keys[0], out value);
        }

        private static NordicsTextureWrapper Strength(AtlasUnifiedMaterial material, bool enabled, AtlasMask mask, float strength, int width, int height) =>
            enabled ? AtlasMapEvaluator.ScaledMask(mask, Mathf.Clamp01(strength), width, height, material)
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
            var materials = active.Select(i => context.Job.Sources[i].Material).ToList();
            var responses = materials.Select(m => m.Surface.Response).ToList();
            if (materials.Any(m => !AtlasMapEvaluator.CanEvaluate(m.Surface.Response.Metallic, m) || !AtlasMapEvaluator.CanEvaluate(m.Surface.Response.Roughness, m)))
                context.Notes.Add("A metallic or roughness map on another UV, remapped or on a global mask written by layers or the second pass isn't baked yet; its slider value is used.");
            if (materials.Any(m => (m.Surface.Response.Highlights.Mask != null && !AtlasMapEvaluator.CanEvaluate(m.Surface.Response.Highlights.Mask, m)) ||
                                   (m.Surface.Response.Reflections.Mask != null && !AtlasMapEvaluator.CanEvaluate(m.Surface.Response.Reflections.Mask, m))))
                context.Notes.Add("A highlight or reflection mask on another UV or a global mask written by layers or the second pass isn't baked yet; it counts as 1.");
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
                (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Occlusion, w, h, m));
            if (active.Any(i => !AtlasMapEvaluator.CanEvaluate(context.Job.Sources[i].Material.Surface.Response.Occlusion, context.Job.Sources[i].Material)))
                context.Notes.Add("An occlusion map on another UV, remapped or on a global mask written by layers or the second pass isn't baked yet; it's left out.");
        }
    }
}
