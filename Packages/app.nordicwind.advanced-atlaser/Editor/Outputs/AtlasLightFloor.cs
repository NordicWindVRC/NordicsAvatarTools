using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The minimum light on an output shader without one (ATLAS_OUTPUT.md, "The look model"). The
    // lighting's floors (AtlasLightingFeature.Floor) keep a surface visible without light. An
    // output shader that keeps none of it but has an emission map gets it there: the base color
    // as the atlas shows it × the floor is added to the emission, which is what a minimum
    // brightness is (AtlasLighting.MinimumBrightness). It adds to the world's light where a
    // minimum light only raises it, so the kept lighting carries it: the level compensation and
    // the comparison after writing count it.
    public static class AtlasLightFloor
    {
        public static void Apply(AtlasGeneratorJob job, AtlasOutputPlan plan, AtlasOutputCapabilities capabilities, AtlasLighting kept)
        {
            float floor = AtlasLightingFeature.Floor(plan.Output.Lighting);
            // A shader with a floor of its own keeps what it can of it.
            if (floor <= 0.005f || AtlasLightingFeature.Floor(kept) > 0f) return;
            if (!AtlasEmissionFeature.On(job, capabilities)) return;
            var color = plan.Maps.FindLast(m => m.Map == AtlasVirtualMap.BaseColor);
            if (color == null) return;
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
                        AtlasPixels.Map(content.Map, shown, (c, b) => new Color(c.r + b.r * floor, c.g + b.g * floor, c.b + b.b * floor, c.a));
                }
            });
            if (request.Carry == null) AtlasEmissionFeature.CarryInStrength(request, target);
            kept.MinimumBrightness = floor;
            plan.LightFloor = floor;
            plan.Notes.Add("The output shader has no minimum light, so the base color × " + floor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                           " is added to the emission: the material stays visible without light. It adds to the world's light, where a minimum light only raises it.");
        }
    }
}
