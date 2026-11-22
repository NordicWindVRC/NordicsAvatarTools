using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Reflection and specular (ATLAS_OUTPUT.md, "Feature inventory"): each source's metallic and
    // roughness baked into maps; the strengths of its highlights and reflections into masks,
    // relative to the largest, which the output uses. Sources without them get no metallic, full
    // roughness and no highlights or reflections. Reflectance is one value in the output, fitted
    // to the sources' highlights and reflections (AtlasSpecularResponse): where the output has no
    // strengths or masks for them, a lower reflectance is what dims them, and where its shader's
    // fresnel differs from a source's, the reflectance makes up for it as far as it can.
    // Automatic fits all sources by area; a chosen source is fitted alone.
    public sealed class AtlasSpecularFeature : IAtlasSharedValueFeature, IAtlasAutomaticReference
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
        public string AutomaticRule =>
            "Automatic fits the reflectance that changes the materials' highlights and reflections least, weighted by area. " +
            "Where the output shader has no strengths for them, they're dimmed through it.";

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities)
        {
            if (capabilities.Reflectance == AtlasReflectanceSupport.None || sources.Count < 2) return false;
            float first = OwnReflectance(sources[0], capabilities);
            return sources.Skip(1).Any(s => !Close(OwnReflectance(s, capabilities), first));
        }

        private static bool Same(float a, float b) => Mathf.Abs(a - b) <= 0.005f;
        // Fitted values: within the fit's steps.
        private static bool Close(float a, float b) => Mathf.Abs(a - b) <= 0.01f;

        // Whether the output gives a source its look exactly: its reflectance, its highlight and
        // reflection strengths where the output has none, and the fresnel its own shader has.
        // A source of another shader is fitted, since the same values look different there.
        private static bool Exact(AtlasUnifiedMaterial source, AtlasOutputCapabilities capabilities)
        {
            var response = source.Surface.Response;
            OutputReflectance(response, capabilities.Reflectance, out bool exact);
            return exact && AtlasSpecularResponse.SameShading(response, capabilities) && (capabilities.Provides(AtlasVirtualMap.SpecularMask) || Full(response.Highlights.Enabled, response.Highlights.Strength, response.Highlights.Mask)) &&
                   (capabilities.Provides(AtlasVirtualMap.ReflectionMask) || Full(response.Reflections.Enabled, response.Reflections.Strength, response.Reflections.Mask));
        }

        private static bool Full(bool enabled, float strength, AtlasMask mask) => enabled && Same(strength, 1f) && mask == null;

        /// <summary>The output's reflectance value for a source alone: exact where the output can
        /// give it, otherwise fitted (AtlasSpecularResponse).</summary>
        public static float OwnReflectance(AtlasUnifiedMaterial source, AtlasOutputCapabilities capabilities) =>
            Exact(source, capabilities)
                ? OutputReflectance(source.Surface.Response, capabilities.Reflectance, out _)
                : AtlasSpecularResponse.Fit(new[] { AtlasSpecularResponse.Sample(source) }, capabilities);

        // The output's reflectance value: the fitted sources' own where they're exact and agree,
        // otherwise fitted to them by area.
        private static float Reflectance(AtlasFeatureContext context, IReadOnlyList<int> fitted, Dictionary<int, float> own)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            if (fitted.All(i => Exact(job.Sources[i].Material, capabilities) && Same(own[i], own[fitted[0]]))) return own[fitted[0]];
            return AtlasSpecularResponse.Fit(fitted.Select(i => AtlasSpecularResponse.Sample(job.Sources[i].Material,
                job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height)).ToList(), capabilities);
        }

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
            int reference = Reference(context, out bool automatic);
            var own = new Dictionary<int, float>();
            if (support != AtlasReflectanceSupport.None)
            {
                foreach (int i in active) own[i] = OwnReflectance(job.Sources[i].Material, context.Capabilities);
                var fitted = automatic ? context.Sources.Where(active.Contains).Distinct().ToList() : new List<int> { reference };
                output.Reflectance.Constant = Reflectance(context, fitted, own);
            }
            output.Reflections.Enabled = reflections > 0f;
            output.Reflections.Strength = reflections;
            output.Highlights.Enabled = highlights > 0f;
            output.Highlights.Strength = highlights;

            output.Metallic.Constant = 1f;
            output.Metallic.Map = Request(context, AtlasVirtualMap.Metallic, active, 0f, (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Metallic, w, h, m));
            output.Roughness.Constant = 1f;
            output.Roughness.Map = Request(context, AtlasVirtualMap.Roughness, active, 1f, (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Roughness, w, h, m));
            // Live global masks (AtlasLiveMasks): metallic as it is, roughness as smoothness.
            int live = AtlasLiveMasks.Split(context, "specular", "metallic", AtlasLiveMasks.Request(context, AtlasVirtualMap.Metallic),
                i => AtlasLiveMasks.Of(job.Sources[i].Material.Surface.Response.Metallic));
            if (live >= 0) AtlasLiveMasks.Read(output.Metallic, live);
            live = AtlasLiveMasks.Split(context, "specular", "smoothness", AtlasLiveMasks.Request(context, AtlasVirtualMap.Roughness),
                i => AtlasLiveMasks.Of(job.Sources[i].Material.Surface.Response.Roughness), decreasing: true);
            if (live >= 0)
            {
                AtlasLiveMasks.Read(output.Roughness, live);
                output.Roughness.InvertSample = output.Roughness.InvertResult = true;
            }
            // The masks follow the mask allocator's rules within their own channels: one value
            // everywhere needs no map (folded into the strength), and a highlight mask equal to the
            // reflection mask reads the reflection mask's channel.
            string[] reflectionKeys = null;
            if (context.Capabilities.Provides(AtlasVirtualMap.ReflectionMask) && reflections > 0f)
            {
                reflectionKeys = Keys(context, active, r => r.Reflections.Enabled, r => r.Reflections.Mask, r => r.Reflections.Strength / reflections);
                if (Constant(reflectionKeys, out float constant)) output.Reflections.Strength = reflections * constant;
                else
                {
                    output.Reflections.Mask = new AtlasMask
                    {
                        Map = Request(context, AtlasVirtualMap.ReflectionMask, active, 0f, (m, w, h) => Strength(m, m.Surface.Response.Reflections.Enabled,
                            m.Surface.Response.Reflections.Mask, m.Surface.Response.Reflections.Strength / reflections, w, h))
                    };
                    live = AtlasLiveMasks.Split(context, "specular", "reflection strength", AtlasLiveMasks.Request(context, AtlasVirtualMap.ReflectionMask),
                        i => AtlasLiveMasks.Of(job.Sources[i].Material.Surface.Response.Reflections.Mask));
                    if (live >= 0) AtlasLiveMasks.Read(output.Reflections.Mask, live);
                }
            }
            if (context.Capabilities.Provides(AtlasVirtualMap.SpecularMask) && highlights > 0f)
            {
                var keys = Keys(context, active, r => r.Highlights.Enabled, r => r.Highlights.Mask, r => r.Highlights.Strength / highlights);
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
                        Map = Request(context, AtlasVirtualMap.SpecularMask, active, 0f, (m, w, h) => Strength(m, m.Surface.Response.Highlights.Enabled,
                            m.Surface.Response.Highlights.Mask, m.Surface.Response.Highlights.Strength / highlights, w, h))
                    };
                    live = AtlasLiveMasks.Split(context, "specular", "highlight strength", AtlasLiveMasks.Request(context, AtlasVirtualMap.SpecularMask),
                        i => AtlasLiveMasks.Of(job.Sources[i].Material.Surface.Response.Highlights.Mask));
                    if (live >= 0) AtlasLiveMasks.Read(output.Highlights.Mask, live);
                }
            }
            Notes(context, active);
            ReflectanceNotes(context, reference, automatic, output.Reflectance.Constant, own);
            StrengthNotes(context, AtlasVirtualMap.SpecularMask, "highlight", r => r.Highlights.Enabled ? r.Highlights.Strength : 0f,
                r => Full(r.Highlights.Enabled, r.Highlights.Strength, r.Highlights.Mask));
            StrengthNotes(context, AtlasVirtualMap.ReflectionMask, "reflection", r => r.Reflections.Enabled ? r.Reflections.Strength : 0f,
                r => Full(r.Reflections.Enabled, r.Reflections.Strength, r.Reflections.Mask));
        }

        // Without a mask, the output can't keep different strengths: one line naming the sources
        // whose strengths are approximated through the reflectance, or the weaker ones where the
        // output has no reflectance value either.
        private static void StrengthNotes(AtlasFeatureContext context, AtlasVirtualMap mask, string name, System.Func<AtlasSurfaceResponse, float> strength,
            System.Func<AtlasSurfaceResponse, bool> full)
        {
            if (context.Capabilities.Provides(mask)) return;
            if (context.Capabilities.Reflectance != AtlasReflectanceSupport.None)
            {
                var approximated = context.Sources.Where(i => !full(context.Job.Sources[i].Material.Surface.Response)).ToList();
                if (approximated.Count > 0)
                    context.Notes.Add($"The output shader has no {name} strength or mask; the {name}s of {Numbers(approximated)} are approximated through its reflectance.");
                return;
            }
            var strengths = context.Sources.Select(i => (index: i, value: strength(context.Job.Sources[i].Material.Surface.Response))).ToList();
            float strongest = strengths.Max(s => s.value);
            var weaker = strengths.Where(s => s.value < strongest - 0.005f).Select(s => s.index).ToList();
            if (weaker.Count > 0)
                context.Notes.Add($"The output shader has no {name} strength or mask; the weaker {name}s of {Numbers(weaker)} aren't kept.");
        }

        // Sources whose reflectance the output doesn't keep, by source number.
        private static void ReflectanceNotes(AtlasFeatureContext context, int reference, bool automatic, float used, Dictionary<int, float> own)
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
            var differ = sources.Where(s => own.TryGetValue(s.index, out float value) && !Close(value, used)).ToList();
            if (differ.Count > 0)
                context.Notes.Add((automatic
                                      ? "Reflectance is one value, fitted to change the sources' highlights and reflections least; "
                                      : $"Reflectance is fitted to source {reference + 1}; ") +
                                  $"{Numbers(differ.Select(s => s.index))} would need a different one.");
            var inexact = sources.Where(s => { OutputReflectance(s.response, support, out bool exact); return !exact; }).ToList();
            if (inexact.Count > 0 && support == AtlasReflectanceSupport.FollowsSmoothness)
                context.Notes.Add($"The output shader's reflectance follows its smoothness map; the reflectance of {Numbers(inexact.Select(s => s.index))} doesn't, so it matches only where the smoothness is full.");
        }

        private static string Numbers(IEnumerable<int> indices)
        {
            var list = indices.Select(i => (i + 1).ToString()).ToList();
            return (list.Count == 1 ? "source " : "sources ") + string.Join(", ", list);
        }

        // The reference source: the chosen one when it has the feature, otherwise (automatic) the
        // one with the largest region among those that have it.
        private static int Reference(AtlasFeatureContext context, out bool automatic)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == "specular");
            automatic = !(choice != null && context.Sources.Contains(choice.ReferenceSource));
            if (!automatic) return choice.ReferenceSource;
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

        // A one-value map for every source: the active ones evaluated, the others (and every
        // source without `evaluate`) at `neutral`.
        internal static AtlasTextureReference Request(AtlasFeatureContext context, AtlasVirtualMap map, HashSet<int> active, float neutral,
            System.Func<AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> evaluate)
        {
            var request = new AtlasMapRequest
            {
                Map = map,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                Content = (index, material, width, height) => active.Contains(index) && evaluate != null
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
    // Baked into color: no occlusion map; the darkening it would give under the virtual light
    // rig is baked into the base color (AtlasOcclusionShading), and its darkening of reflections
    // into the reflection mask where the output has one.
    public sealed class AtlasOcclusionFeature : IAtlasOutputFeature, IAtlasLitBakeFeature
    {
        public static readonly AtlasFeatureMode Baked = new AtlasFeatureMode
        {
            Id = "baked", Label = "Baked into color", Kind = AtlasFeatureModeKind.Baked,
            RequiredMaps = new[] { AtlasVirtualMap.BaseColor }
        };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Occlusion map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Occlusion }
            },
            Baked,
            AtlasFeatureMode.Off
        };

        public AtlasLitBake? LitBake(AtlasFeatureMode mode) => mode == Baked ? AtlasLitBake.Occlusion : (AtlasLitBake?)null;

        public string Id => "occlusion";
        public string DisplayName => "Ambient Occlusion";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 5;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source?.Surface.Response.Occlusion.Map?.Texture != null;

        public void Resolve(AtlasFeatureContext context)
        {
            var active = new HashSet<int>(context.Sources);
            if (active.Any(i => !AtlasMapEvaluator.CanEvaluate(context.Job.Sources[i].Material.Surface.Response.Occlusion, context.Job.Sources[i].Material)))
                context.Notes.Add("An occlusion map on another UV, remapped or on a global mask written by layers or the second pass isn't baked yet; it's left out.");
            if (context.Mode == Baked)
            {
                BakeIntoColor(context, active);
                return;
            }
            var occlusion = context.Output.Surface.Response.Occlusion;
            occlusion.Constant = 1f;
            occlusion.Combine = AtlasScalarCombine.StrengthAroundOne;
            occlusion.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Occlusion, active, 1f,
                (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Occlusion, w, h, m));
            int live = AtlasLiveMasks.Split(context, "occlusion", "ambient occlusion", AtlasLiveMasks.Request(context, AtlasVirtualMap.Occlusion),
                i => AtlasLiveMasks.Of(context.Job.Sources[i].Material.Surface.Response.Occlusion));
            if (live >= 0) AtlasLiveMasks.Read(occlusion, live);
        }

        // The base color × each source's occlusion as its shader applies it, by the share of the
        // rig's light it darkens (AtlasOcclusionShading); reflections × occlusion in the
        // reflection mask.
        private static void BakeIntoColor(AtlasFeatureContext context, HashSet<int> active)
        {
            var job = context.Job;
            var settings = job.Baking ?? new AtlasBakeSettings();
            float strength = Mathf.Clamp01(settings.strength) * Mathf.Clamp01(settings.occlusion);
            if (strength <= 0f) return;
            var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            if (request == null)
                context.Notes.Add("Ambient occlusion is baked into the base color, which is off; its darkening of diffuse light is left out.");
            else
            {
                var shares = new Dictionary<int, Vector2>();
                foreach (int i in active)
                {
                    var response = job.Sources[i].Material.Surface.Response;
                    var rig = AtlasLightRig.Create(settings, AtlasNormalFeature.UsesSymmetricRig(settings, job.Sources[i]));
                    shares[i] = AtlasOcclusionShading.Shares(rig, response.OcclusionDirect, response.OcclusionIndirect);
                }
                if (active.Any(i => job.Sources[i].Material.Surface.Response.OcclusionTint?.Map?.Texture != null))
                    context.Notes.Add("An occlusion tint texture isn't baked; its color is used.");
                request.Add(new AtlasMapContribution
                {
                    Feature = "occlusion", Label = "baked occlusion", Stage = AtlasMapStage.Shading, Applies = shares.ContainsKey,
                    Apply = content =>
                    {
                        var material = content.Material;
                        var response = material.Surface.Response;
                        using (var occlusion = AtlasMapEvaluator.Scalar(response.Occlusion, content.Width, content.Height, material))
                        using (var metallic = response.MultiBounceOcclusion ? AtlasMapEvaluator.Scalar(response.Metallic, content.Width, content.Height, material) : null)
                            AtlasOcclusionShading.Apply(content.Map, occlusion, metallic, shares[content.Source],
                                response.OcclusionTint?.Color ?? Color.black, response.MultiBounceOcclusion, strength);
                    }
                });
            }
            BakeIntoReflections(context, active, strength);
        }

        // Reflections × lerp(1, occlusion, the source's occlusion of reflections), in the
        // reflection mask: its own map is multiplied (a highlight mask that read the same map gets
        // a copy of it, since occlusion doesn't darken highlights), and one strength everywhere
        // gets a mask that holds the occlusion alone.
        private static void BakeIntoReflections(AtlasFeatureContext context, HashSet<int> active, float strength)
        {
            var job = context.Job;
            var output = context.Output.Surface.Response;
            var occluded = new HashSet<int>(active.Where(i =>
            {
                var r = job.Sources[i].Material.Surface.Response;
                return r.OcclusionReflections > 0f && r.Reflections.Enabled && r.Reflections.Strength > 0f;
            }));
            if (occluded.Count == 0 || !output.Reflections.Enabled || output.Reflections.Strength <= 0f) return;
            if (!context.Capabilities.Provides(AtlasVirtualMap.ReflectionMask))
            {
                context.Notes.Add($"The output has no reflection mask to bake the occlusion of reflections into; reflections stay unoccluded on {AtlasLiveMasks.Numbers(occluded)}.");
                return;
            }
            NordicsTextureWrapper Factor(AtlasUnifiedMaterial m, int w, int h)
            {
                var map = AtlasMapEvaluator.Scalar(m.Surface.Response.Occlusion, w, h, m);
                float amount = Mathf.Clamp01(m.Surface.Response.OcclusionReflections) * strength;
                AtlasPixels.Map(map, c =>
                {
                    float f = Mathf.Lerp(1f, Mathf.Clamp01(c.r), amount);
                    return new Color(f, f, f, 1f);
                });
                return map;
            }
            if (output.Reflections.Mask == null)
            {
                output.Reflections.Mask = new AtlasMask
                {
                    Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ReflectionMask, occluded, 1f, Factor)
                };
                return;
            }
            var request = context.Maps.LastOrDefault(r => r.Map == AtlasVirtualMap.ReflectionMask && r.Reference == output.Reflections.Mask.Map);
            if (request == null) return;
            if (output.Highlights.Mask?.Map == request.Reference && context.Capabilities.Provides(AtlasVirtualMap.SpecularMask))
            {
                // The map as it is before the occlusion goes into it.
                var copy = new AtlasMapRequest
                {
                    Map = AtlasVirtualMap.SpecularMask, Content = request.Snapshot(),
                    Reference = new AtlasTextureReference { Interpretation = request.Reference.Interpretation }
                };
                copy.Sources.AddRange(request.Sources);
                context.Maps.Add(copy);
                output.Highlights.Mask.Map = copy.Reference;
            }
            request.Add(new AtlasMapContribution
            {
                Feature = "occlusion", Label = "occlusion of reflections", Stage = AtlasMapStage.Shading, Applies = occluded.Contains,
                Apply = content =>
                {
                    using (var factor = Factor(content.Material, content.Width, content.Height))
                        AtlasPixels.Map(content.Map, factor, (c, f) => new Color(c.r * f.r, c.g * f.r, c.b * f.r, c.a));
                }
            });
        }
    }
}
