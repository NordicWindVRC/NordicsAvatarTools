using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Level compensation (ATLAS_OUTPUT.md, "Level compensation"). The atlas has one lighting. A
    // source that the kept lighting shows brighter or darker than its own lighting did gets its
    // part of the base color map scaled, so it shows as bright as it was. The scale is the one
    // that makes the source's colors under the kept lighting look closest to its own look in the
    // standard scenes. A raise stays within the map's range: where it would take the source's
    // brightest texel above 1, the texels that stay below the knee get it in full and the
    // brighter ones are raised less, in their order, up to 1 (Raised).
    public static class AtlasLevelCompensation
    {
        // The value up to which a raise that doesn't fit is still applied in full.
        public const float Knee = 0.8f;
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
                      "gets its base color scaled to make up for it. No texel is raised above 1: where a raise doesn't fit, the brightest " +
                      "texels are raised less. Viewers who block the avatar's shaders see the scaled colors. Off: the base colors stay as they are."
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
                    float peak = ScaleMap(content.Map, scale, request.SharedColor);
                    // What the bake applied to the source's colors, for the comparison after writing.
                    if (content.Placed.HasValue)
                    {
                        float effective = Effective(AtlasSourceSamples.Bases(content.Material), scale, peak, request.SharedColor);
                        lock (applied) applied[content.Source] = effective;
                    }
                }
            });
            var list = scales.OrderBy(s => s.Key).Select(s => $"source {s.Key + 1} (× {Number(s.Value)})");
            plan.Notes.Add("These base colors are scaled so the materials show as bright as their own lighting made them: " +
                           string.Join(", ", list) + ". Where a raise would take a texel above 1, the brightest texels are raised less.");
        }

        /// <summary>Scales a source's part of the map, whose values include the shared color
        /// (<paramref name="shared"/>) the texture leaves out. A raise that would take a stored
        /// texel above 1 is applied by Raised, each texel keeping its hue. Returns the
        /// brightest stored value before the scale.</summary>
        public static float ScaleMap(NordicsTextureWrapper map, float scale, Color shared)
        {
            // The texture holds the map without the shared color, so those are the values
            // that stay within 1.
            float r = Stored(shared.r), g = Stored(shared.g), b = Stored(shared.b);
            float peak = scale > 1f ? AtlasPixels.Max(map, c => Mathf.Max(c.r * r, Mathf.Max(c.g * g, c.b * b))) : 0f;
            if (Mathf.Abs(scale - 1f) < 1e-4f) return peak;
            if (peak * scale <= 1f)
            {
                AtlasPixels.Map(map, c => new Color(c.r * scale, c.g * scale, c.b * scale, c.a));
                return peak;
            }
            AtlasPixels.Map(map, c =>
            {
                float value = Mathf.Max(c.r * r, Mathf.Max(c.g * g, c.b * b));
                if (value <= 1e-6f) return c;
                float k = Raised(value, scale, peak) / value;
                return new Color(c.r * k, c.g * k, c.b * k, c.a);
            });
            return peak;
        }

        /// <summary>A stored value after a raise by <paramref name="scale"/>, in a source whose
        /// brightest stored value is <paramref name="peak"/>. Where the raise fits (peak × scale
        /// within 1), value × scale. Otherwise the raise is applied in full up to the knee, and
        /// the range from the knee to the raised peak is drawn onto the knee to 1 by a curve
        /// that leaves the knee at the raise's own slope: brighter texels are raised less,
        /// none goes above 1, and they keep their order.</summary>
        public static float Raised(float value, float scale, float peak)
        {
            float target = value * scale;
            float top = peak * scale;
            if (top <= 1f || target <= Knee) return target;
            float power = (top - Knee) / (1f - Knee);
            float along = Mathf.Clamp01((target - Knee) / (top - Knee));
            return Knee + (1f - Knee) * (1f - Mathf.Pow(1f - along, power));
        }

        // The scale the source's own colors got on the whole: their values after over before.
        private static float Effective(IReadOnlyList<Color> bases, float scale, float peak, Color shared)
        {
            if (scale <= 1f || peak * scale <= 1f) return scale;
            float r = Stored(shared.r), g = Stored(shared.g), b = Stored(shared.b);
            double before = 0.0, after = 0.0;
            foreach (var c in bases)
            {
                float value = Mathf.Min(peak, Mathf.Max(c.r * r, Mathf.Max(c.g * g, c.b * b)));
                before += value;
                after += Raised(value, scale, peak);
            }
            return before > 1e-6 ? (float)(after / before) : scale;
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
