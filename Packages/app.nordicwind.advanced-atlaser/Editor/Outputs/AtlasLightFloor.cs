using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The minimum light an output shader can't keep (ATLAS_OUTPUT.md, "Minimum light"). The
    // lighting's floors (AtlasLightingFeature.Floor) keep a surface visible without light. An
    // output shader has none (Unity Standard), or one that stops below the atlas's (Toon
    // Standard's minimum brightness at 0.1). Where it has an emission map, what is missing goes
    // there, per texel:
    //
    //   the surface's color as the source shows it × the atlas's floor
    //   − the base color as the atlas shows it × the shader's own floor, not below 0.
    //
    // - The first is what a source shows without light: a minimum light shines on its color
    //   after its matcaps, which often darken it. So it is the source's own color (before the
    //   base color's level compensation, which makes up for the lighting under the world's
    //   light) with the matcaps the output shows at their average (AtlasMatcapForm), and those
    //   no slot shows drawn as the virtual view sees them: under the world's light the output
    //   shader shades the form itself, in the floor nothing else does.
    // - It is shaded by the lit bakes (ATLAS_OUTPUT.md, "Virtual light rigs"): each source's
    //   normal-map shading and occlusion, with the Baking section's rig and strengths, since the
    //   shader lights its normal map and darkens by its occlusion map only what the world's
    //   light reaches.
    // - The second is what the output shader's own floor shows: its base color texture alone
    //   (Toon Standard's floor is added before its matcap).
    //
    // It adds to the world's light where a minimum light only raises it, so the kept lighting
    // carries it as a minimum brightness (AtlasLighting.MinimumBrightness): the level
    // compensation and the comparison after writing count it. It can be the only reason an
    // atlas has an emission texture, so the user can turn it off.
    public static class AtlasLightFloor
    {
        // Stored with the lighting feature's settings.
        public const string SwitchId = "light-floor";

        // The control, shown while the output shader can't keep the whole floor itself.
        public static AtlasConflictSwitch Switch => new AtlasConflictSwitch
        {
            Id = SwitchId, Label = "Minimum light in the emission", Default = true,
            Tooltip = "The materials have a minimum light, which keeps them visible in the dark, and the output shader can't keep all of it. " +
                      "On: the rest is added to the emission, as the base color at that brightness, with normal-map shading, occlusion " +
                      "and the shading of matcaps kept as their average baked in by the Baking section's settings. Where nothing else glows, this is what the atlas has an emission texture " +
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
            // The sources with normals and occlusion the atlas keeps, as maps or baked.
            var bent = new HashSet<int>(Sources(job).Where(i => normalShading > 0f && Kept(job, plan, "normal", i) && normals.IsPresent(job.Sources[i])));
            var occluded = new HashSet<int>(Sources(job).Where(i => occlusion > 0f && Kept(job, plan, "occlusion", i) && Occluded(job.Sources[i].Material)));
            // The sources with matcaps drawn as shading: those no slot shows.
            var forms = plan.MatcapForms;
            var formed = new HashSet<int>(Sources(job).Where(i => forms.Any(f => f.Draw != null && f.Strength > 0f && f.Applies(i))));
            if (bent.Count > 0) plan.LightFloorBakes.Add(AtlasLitBake.NormalShading);
            if (occluded.Count > 0) plan.LightFloorBakes.Add(AtlasLitBake.Occlusion);
            if (formed.Count > 0) plan.LightFloorBakes.Add(AtlasLitBake.Matcaps);
            var unknown = bent.Concat(formed).Distinct().Where(i => job.Sources[i].Surface == null).OrderBy(i => i).ToList();
            if (unknown.Count > 0)
                plan.Notes.Add($"The minimum light's normal-map and matcap shading need the meshes, which aren't known here; they're left out on {AtlasLiveMasks.Numbers(unknown)}.");

            // The map gives its value in full, so the floor is added as it is.
            request.Add(new AtlasMapContribution
            {
                Feature = AtlasLightingFeature.FeatureId, Label = "minimum light", Stage = AtlasMapStage.Level, CanRaise = true,
                Applies = i => job.Sources[i].Material != null,
                Apply = content =>
                {
                    int i = content.Source, w = content.Width, h = content.Height;
                    // The source's own color, as it shows it without light.
                    using (var wanted = color.Before(AtlasMapStage.Layers, i, content.Material, w, h))
                    {
                        Matcaps(wanted, forms, i, content.Material);
                        if (occluded.Contains(i)) Occlude(wanted, job.Sources[i], settings, occlusion);
                        if (bent.Contains(i)) Bend(wanted, job.Sources[i], settings, normalShading, content.Placed);
                        // What the shader's own floor shows of the base color texture.
                        using (var shown = own > 0f ? color.Shown(i, content.Material, w, h) : null)
                        {
                            var pixels = content.Map.GetPixels();
                            var source = wanted.GetPixels();
                            var kept = shown?.GetPixels();
                            for (int p = 0; p < pixels.Length; p++)
                            {
                                var held = kept != null ? kept[p] : Color.black;
                                pixels[p].r += Mathf.Max(0f, source[p].r * floor - held.r * own);
                                pixels[p].g += Mathf.Max(0f, source[p].g * floor - held.g * own);
                                pixels[p].b += Mathf.Max(0f, source[p].b * floor - held.b * own);
                            }
                            content.Map.SetPixels(pixels);
                        }
                    }
                }
            });
            if (request.Carry == null) AtlasEmissionFeature.CarryInStrength(request, target);
            kept.MinimumBrightness += rest;
            plan.LightFloor = rest;
            var names = plan.LightFloorBakes.Select(b => b == AtlasLitBake.NormalShading ? "normal-map shading"
                : b == AtlasLitBake.Occlusion ? "occlusion" : "matcap shading").ToList();
            string shaded = names.Count == 0 ? ""
                : ", with " + (names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1]) + " baked in";
            plan.Notes.Add((own > 0f
                               ? $"The output shader's minimum light stops at {Number(own)} of the atlas's {Number(floor)}, so what that leaves of the materials' color × {Number(floor)}"
                               : $"The output shader has no minimum light, so the materials' color × {Number(floor)}") +
                           $" is added to the emission{shaded}: the material stays visible without light. It adds to the world's light, where a minimum light only raises it.");
        }

        private static IEnumerable<int> Sources(AtlasGeneratorJob job) =>
            Enumerable.Range(0, job.Sources.Count).Where(i => job.Sources[i].Material != null);

        // The atlas keeps the feature on this source: the plan has it in a mode that isn't off,
        // and the user didn't turn it off for the source.
        private static bool Kept(AtlasGeneratorJob job, AtlasOutputPlan plan, string featureId, int source) =>
            plan.Modes.TryGetValue(featureId, out var mode) && mode.Kind != AtlasFeatureModeKind.Off &&
            !(job.Features.Find(f => f.FeatureId == featureId)?.DisabledSources.Contains(source) ?? false);

        // A drawn matcap can't brighten the floor without limit where its average is dark.
        private const float MaxGain = 4f;

        // The source's color with the matcaps the output shows: each form's average, and for
        // those no slot shows, × its matcaps drawn as shading over that average, lerped from 1 by
        // the form's strength.
        private static void Matcaps(NordicsTextureWrapper map, List<AtlasMatcapForm> forms, int source, AtlasUnifiedMaterial material)
        {
            foreach (var form in forms.Where(f => f.Applies(source)))
            {
                bool draw = form.Draw != null && form.Strength > 0f;
                using (var drawn = draw ? map.Clone() : null)
                {
                    form.Average(map, source, material);
                    if (!draw || !form.Draw(drawn, source, material)) continue;
                    float strength = form.Strength;
                    var shading = drawn.GetPixels();
                    var pixels = map.GetPixels();
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        var mean = pixels[p];
                        pixels[p].r = mean.r * Mathf.LerpUnclamped(1f, Gain(shading[p].r, mean.r), strength);
                        pixels[p].g = mean.g * Mathf.LerpUnclamped(1f, Gain(shading[p].g, mean.g), strength);
                        pixels[p].b = mean.b * Mathf.LerpUnclamped(1f, Gain(shading[p].b, mean.b), strength);
                    }
                    map.SetPixels(pixels);
                }
            }
        }

        private static float Gain(float drawn, float mean) => mean > 1e-5f ? Mathf.Clamp(drawn / mean, 0f, MaxGain) : 1f;

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
