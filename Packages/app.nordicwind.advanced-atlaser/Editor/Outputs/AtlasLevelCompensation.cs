using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Level compensation (ATLAS_OUTPUT.md, "Level compensation"). The atlas has one lighting. A
    // source that the kept lighting shows brighter or darker than its own lighting did gets its
    // part of the base color map scaled, so it shows as bright as it was. The scale is the one
    // that makes the source's colors under the kept lighting look closest to its own look in the
    // standard scenes. A raise stays within the map's range: it stops where the source's
    // brightest texel reaches 1.
    public static class AtlasLevelCompensation
    {
        // Stored with the lighting feature's settings.
        public const string SwitchId = "level-compensation";

        // A lighting difference beyond a factor of two isn't made up for by the base color.
        private const float Lowest = 0.5f, Highest = 2f;
        private const int Points = 9;
        private const int Refinements = 4;

        // The control, shown while a source would be scaled.
        public static AtlasConflictSwitch Switch => new AtlasConflictSwitch
        {
            Id = SwitchId, Label = "Keep each material's brightness", Default = true,
            Tooltip = "The atlas material has one lighting. On: a material that this lighting shows brighter or darker than its own did " +
                      "gets its base color scaled to make up for it, never raising a texel above 1. Viewers who block the avatar's shaders " +
                      "see the scaled colors. Off: the base colors stay as they are."
        };

        /// <summary>Whether the user left level compensation on.</summary>
        public static bool Enabled(AtlasGeneratorJob job)
        {
            var choice = job.Features.Find(f => f.FeatureId == AtlasLightingFeature.FeatureId);
            return choice == null || !choice.Switches.TryGetValue(SwitchId, out bool on) || on;
        }

        /// <summary>Finds each source's scale against the kept lighting (the plan's
        /// LevelScales) and, unless turned off, adds it to the base color map as its last
        /// contribution, with a note naming the sources.</summary>
        public static void Apply(AtlasGeneratorJob job, AtlasOutputPlan plan, AtlasLighting kept)
        {
            var request = plan.Maps.FirstOrDefault(m => m.Map == AtlasVirtualMap.BaseColor);
            if (request == null || kept == null) return;
            foreach (int i in request.Sources)
            {
                var material = job.Sources[i].Material;
                if (material == null) continue;
                float scale = Scale(material, kept);
                if (Mathf.Abs(scale - 1f) > 1e-3f) plan.LevelScales[i] = scale;
            }
            if (plan.LevelScales.Count == 0 || !Enabled(job)) return;

            var scales = plan.LevelScales;
            var applied = plan.LevelApplied;
            request.Add(new AtlasMapContribution
            {
                Feature = AtlasLightingFeature.FeatureId, Label = "level compensation", Stage = AtlasMapStage.Level,
                Kind = AtlasContributionKind.Shift, Applies = scales.ContainsKey,
                Apply = content =>
                {
                    float scale = scales[content.Source];
                    if (scale > 1f)
                    {
                        // The texture holds the map without the shared color, so those are the
                        // texels that stay within 1.
                        var shared = request.SharedColor;
                        float r = Stored(shared.r), g = Stored(shared.g), b = Stored(shared.b);
                        float peak = AtlasPixels.Max(content.Map, c => Mathf.Max(c.r * r, Mathf.Max(c.g * g, c.b * b)));
                        if (peak > 1e-5f) scale = Mathf.Clamp(1f / peak, 1f, scale);
                    }
                    // What the bake applied, for the comparison after writing.
                    if (content.Placed.HasValue)
                        lock (applied) applied[content.Source] = scale;
                    if (Mathf.Abs(scale - 1f) < 1e-4f) return;
                    AtlasPixels.Map(content.Map, c => new Color(c.r * scale, c.g * scale, c.b * scale, c.a));
                }
            });
            var list = scales.OrderBy(s => s.Key).Select(s => $"source {s.Key + 1} (× {Number(s.Value)})");
            plan.Notes.Add("These base colors are scaled so the materials show as bright as their own lighting made them: " +
                           string.Join(", ", list) + ". A raise stops where a material's brightest texel reaches 1.");
        }

        private static string Number(float value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        // What a channel of the map is divided by when it is stored: the shared color, where it has one.
        private static float Stored(float shared) => shared > 1e-5f ? 1f / shared : 1f;

        /// <summary>The scale of a source's base colors that makes them look closest, under the
        /// kept lighting, to what its own lighting showed; 1 where its look is kept as it is or
        /// no scale brings it closer by the tuning margin.</summary>
        public static float Scale(AtlasUnifiedMaterial material, AtlasLighting kept)
        {
            var bases = AtlasLook.Representative(AtlasSourceSamples.Bases(material));
            if (bases.Count == 0) return 1f;
            var own = AtlasLook.Shown(material.Lighting, bases);
            float Cost(float scale) =>
                AtlasLook.Difference(own, AtlasLook.Shown(kept, bases.Select(c => new Color(c.r * scale, c.g * scale, c.b * scale, 1f)).ToList()));
            float unscaled = Cost(1f);
            if (unscaled <= AtlasLightingTuning.Margin) return 1f;
            float best = unscaled, at = 1f;
            void Try(float candidate)
            {
                candidate = Mathf.Clamp(candidate, Lowest, Highest);
                float cost = Cost(candidate);
                if (cost < best - 1e-5f)
                {
                    best = cost;
                    at = candidate;
                }
            }
            // Factors spread evenly by ratio, then steps that halve around the best one.
            float ratio = Mathf.Pow(Highest / Lowest, 1f / (Points - 1));
            for (int i = 0; i < Points; i++) Try(Lowest * Mathf.Pow(ratio, i));
            for (int i = 0; i < Refinements; i++)
            {
                ratio = Mathf.Sqrt(ratio);
                float center = at;
                Try(center / ratio);
                Try(center * ratio);
            }
            return unscaled - best < AtlasLightingTuning.Margin ? 1f : at;
        }
    }
}
