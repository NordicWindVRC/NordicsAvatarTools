using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The minimum light an output shader can't keep (ATLAS_OUTPUT.md, "Minimum light"). The
    // lighting's floors (AtlasLightingFeature.Floor) keep a surface visible without light. An
    // output shader has none (Unity Standard), or one that stops below the atlas's (Toon
    // Standard's minimum brightness at 0.1). Where it has an emission map, the rest goes there:
    // the base color as the atlas shows it × the rest is added to the emission, which is what a
    // minimum brightness is (AtlasLighting.MinimumBrightness). It adds to the world's light
    // where a minimum light only raises it, so the kept lighting carries it: the level
    // compensation and the comparison after writing count it. It can be the only reason an
    // atlas has an emission texture, so the user can turn it off.
    //
    // The shader lights its normal map and darkens by its occlusion map only what the world's
    // light reaches, so the emission would show the surface flat. The floor is shaded by the
    // lit bakes instead (ATLAS_OUTPUT.md, "Virtual light rigs"): the normal-map shading and
    // the occlusion of the sources whose normal and occlusion maps the output keeps as maps,
    // with the Baking section's rig and strengths. Where they are baked into the base color,
    // the base color as shown has them already.
    public static class AtlasLightFloor
    {
        // Stored with the lighting feature's settings.
        public const string SwitchId = "light-floor";

        // The control, shown while the output shader can't keep the whole floor itself.
        public static AtlasConflictSwitch Switch => new AtlasConflictSwitch
        {
            Id = SwitchId, Label = "Minimum light in the emission", Default = true,
            Tooltip = "The materials have a minimum light, which keeps them visible in the dark, and the output shader can't keep all of it. " +
                      "On: the rest is added to the emission, as the base color at that brightness, with normal-map shading and occlusion " +
                      "baked in by the Baking section's lights. Where nothing else glows, this is what the atlas has an emission texture " +
                      "for. Off: the atlas is darker without light than the materials were."
        };

        /// <summary>Whether the user left it on.</summary>
        public static bool Enabled(AtlasGeneratorJob job)
        {
            var choice = job.Features.Find(f => f.FeatureId == AtlasLightingFeature.FeatureId);
            return choice == null || !choice.Switches.TryGetValue(SwitchId, out bool on) || on;
        }

        /// <summary>Finds the part of <paramref name="floor"/> (the floor of the lighting the
        /// atlas chose) that the kept lighting lacks (the plan's LightFloorMissing) and, unless
        /// turned off, adds it to the emission map of every source, with a note. The kept
        /// lighting then carries it as a minimum brightness.</summary>
        public static void Apply(AtlasGeneratorJob job, AtlasOutputPlan plan, AtlasOutputCapabilities capabilities, AtlasLighting kept, float floor)
        {
            float own = AtlasLightingFeature.Floor(kept);
            float rest = floor - own;
            if (rest <= 0.005f || !AtlasEmissionFeature.On(job, capabilities)) return;
            var color = plan.Maps.FindLast(m => m.Map == AtlasVirtualMap.BaseColor);
            if (color == null) return;
            plan.LightFloorMissing = rest;
            if (!Enabled(job)) return;
            var request = AtlasEmissionFeature.Map(job, plan, out var target);
            if (target == null) return;

            var settings = job.Baking ?? new AtlasBakeSettings();
            float normalShading = Mathf.Clamp01(settings.strength) * Mathf.Clamp01(settings.normalShading);
            float occlusion = Mathf.Clamp01(settings.strength) * Mathf.Clamp01(settings.occlusion);
            var normals = new AtlasNormalFeature();
            // The sources whose normals and occlusion the output keeps as maps.
            var bent = new HashSet<int>(Sources(job).Where(i => normalShading > 0f && AsMap(job, plan, "normal", i) && normals.IsPresent(job.Sources[i])));
            var occluded = new HashSet<int>(Sources(job).Where(i => occlusion > 0f && AsMap(job, plan, "occlusion", i) && Occluded(job.Sources[i].Material)));
            if (bent.Count > 0) plan.LightFloorBakes.Add(AtlasLitBake.NormalShading);
            if (occluded.Count > 0) plan.LightFloorBakes.Add(AtlasLitBake.Occlusion);
            var unknown = bent.Where(i => job.Sources[i].Surface == null).ToList();
            if (unknown.Count > 0)
                plan.Notes.Add($"The minimum light's normal-map shading needs the meshes, which aren't known here; it's left out on {AtlasLiveMasks.Numbers(unknown)}.");

            // The map gives its value in full, so the floor is added as it is.
            request.Add(new AtlasMapContribution
            {
                Feature = AtlasLightingFeature.FeatureId, Label = "minimum light", Stage = AtlasMapStage.Level, CanRaise = true,
                Applies = i => job.Sources[i].Material != null,
                Apply = content =>
                {
                    int i = content.Source;
                    using (var shown = color.Shown(i, content.Material, content.Width, content.Height))
                    {
                        if (occluded.Contains(i)) Occlude(shown, job.Sources[i], settings, occlusion);
                        if (bent.Contains(i)) Bend(shown, job.Sources[i], settings, normalShading, content.Placed);
                        AtlasPixels.Map(content.Map, shown, (c, b) => new Color(c.r + b.r * rest, c.g + b.g * rest, c.b + b.b * rest, c.a));
                    }
                }
            });
            if (request.Carry == null) AtlasEmissionFeature.CarryInStrength(request, target);
            kept.MinimumBrightness += rest;
            plan.LightFloor = rest;
            string shaded = plan.LightFloorBakes.Count == 0 ? ""
                : ", with " + string.Join(" and ", plan.LightFloorBakes.Select(b => b == AtlasLitBake.NormalShading ? "normal-map shading" : "occlusion")) + " baked in";
            plan.Notes.Add((own > 0f
                               ? $"The output shader keeps {Number(own)} of the atlas's minimum light of {Number(floor)}, so the base color × {Number(rest)}"
                               : $"The output shader has no minimum light, so the base color × {Number(rest)}") +
                           $" is added to the emission{shaded}: the material stays visible without light. It adds to the world's light, where a minimum light only raises it.");
        }

        private static IEnumerable<int> Sources(AtlasGeneratorJob job) =>
            Enumerable.Range(0, job.Sources.Count).Where(i => job.Sources[i].Material != null);

        // The feature is kept by the output shader's own map on this source: the plan has it in
        // a native mode, and the user didn't turn it off for the source.
        private static bool AsMap(AtlasGeneratorJob job, AtlasOutputPlan plan, string featureId, int source) =>
            plan.Modes.TryGetValue(featureId, out var mode) && mode.Kind == AtlasFeatureModeKind.Native &&
            !(job.Features.Find(f => f.FeatureId == featureId)?.DisabledSources.Contains(source) ?? false);

        private static bool Occluded(AtlasUnifiedMaterial material)
        {
            var occlusion = material.Surface.Response.Occlusion;
            return occlusion.Map?.Texture != null && AtlasMapEvaluator.CanEvaluate(occlusion, material);
        }

        // The source's normal-map shading under the rig, as the normal maps' baked mode gives the
        // base color (AtlasNormalShading).
        private static void Bend(NordicsTextureWrapper map, AtlasGeneratorSource source, AtlasBakeSettings settings, float strength, Vector2Int? placed)
        {
            var sections = source.Surface?.Invoke();
            if (sections == null || sections.Count == 0) return;
            var rig = AtlasLightRig.Create(settings, AtlasNormalFeature.UsesSymmetricRig(settings, source));
            using (var normals = AtlasMapEvaluator.Normal(source, map.Width, map.Height, 1f, placed))
                AtlasNormalShading.Apply(map, normals, sections, rig, strength);
        }

        // The source's occlusion by the share of the rig's light it darkens, as the occlusion's
        // baked mode gives the base color (AtlasOcclusionShading).
        private static void Occlude(NordicsTextureWrapper map, AtlasGeneratorSource source, AtlasBakeSettings settings, float strength)
        {
            var material = source.Material;
            var response = material.Surface.Response;
            var rig = AtlasLightRig.Create(settings, AtlasNormalFeature.UsesSymmetricRig(settings, source));
            var shares = AtlasOcclusionShading.Shares(rig, response.OcclusionDirect, response.OcclusionIndirect);
            using (var occlusion = AtlasMapEvaluator.Scalar(response.Occlusion, map.Width, map.Height, material))
            using (var metallic = response.MultiBounceOcclusion ? AtlasMapEvaluator.Scalar(response.Metallic, map.Width, map.Height, material) : null)
                AtlasOcclusionShading.Apply(map, occlusion, metallic, shares, response.OcclusionTint?.Color ?? Color.black, response.MultiBounceOcclusion, strength);
        }

        private static string Number(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
