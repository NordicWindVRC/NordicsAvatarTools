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
    public static class AtlasLightFloor
    {
        // Stored with the lighting feature's settings.
        public const string SwitchId = "light-floor";

        // The control, shown while the output shader can't keep the whole floor itself.
        public static AtlasConflictSwitch Switch => new AtlasConflictSwitch
        {
            Id = SwitchId, Label = "Minimum light in the emission", Default = true,
            Tooltip = "The materials have a minimum light, which keeps them visible in the dark, and the output shader can't keep all of it. " +
                      "On: the rest is added to the emission, as the base color at that brightness. Where nothing else glows, this is " +
                      "what the atlas has an emission texture for. Off: the atlas is darker without light than the materials were."
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
            // The map gives its value in full, so the floor is added as it is.
            request.Add(new AtlasMapContribution
            {
                Feature = AtlasLightingFeature.FeatureId, Label = "minimum light", Stage = AtlasMapStage.Level, CanRaise = true,
                Applies = i => job.Sources[i].Material != null,
                Apply = content =>
                {
                    using (var shown = color.Shown(content.Source, content.Material, content.Width, content.Height))
                        AtlasPixels.Map(content.Map, shown, (c, b) => new Color(c.r + b.r * rest, c.g + b.g * rest, c.b + b.b * rest, c.a));
                }
            });
            if (request.Carry == null) AtlasEmissionFeature.CarryInStrength(request, target);
            kept.MinimumBrightness += rest;
            plan.LightFloor = rest;
            plan.Notes.Add((own > 0f
                               ? $"The output shader keeps {Number(own)} of the atlas's minimum light of {Number(floor)}, so the base color × {Number(rest)}"
                               : $"The output shader has no minimum light, so the base color × {Number(rest)}") +
                           " is added to the emission: the material stays visible without light. It adds to the world's light, where a minimum light only raises it.");
        }

        private static string Number(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
