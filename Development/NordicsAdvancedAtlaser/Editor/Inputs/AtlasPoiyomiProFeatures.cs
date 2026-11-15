using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Poiyomi Pro features that have no unified home yet. Each one is reported once when it's
    /// switched on, or when the material uses a Pro variant shader built around it.
    /// Pro Fur is typed and read by <see cref="AtlasPoiyomiFurParser"/>.
    /// </summary>
    internal static class AtlasPoiyomiProFeatures
    {
        private static readonly (string Toggle, string Id, string Message)[] Sections =
        {
            ("_SSAOEnabled", "lighting.screen-space-occlusion", "Screen-space ambient occlusion is not mapped yet."),
            ("_ContactShadowsEnabled", "lighting.contact-shadows", "Contact shadows are not mapped yet."),
            ("_TPSPenetratorEnabled", "geometry.tps-deformation", "TPS deformation is not mapped yet."),
            ("_EnableSquish", "geometry.squish", "Squish deformation is not mapped yet."),
            ("_ConstellationEnable", "layer.constellation", "The constellation pattern is not mapped yet."),
            ("_GT7ToneMapping", "color.tone-mapping", "GT7 tone mapping is not mapped yet."),
            ("_NeutralEmission", "emission.neutral", "Neutral surface emission is not mapped yet."),
        };

        // Variant shaders whose extra pass or stage is their whole point.
        private static readonly (string NamePart, string Id, string Message)[] Variants =
        {
            ("Geometric Dissolve", "geometry.geometric-dissolve", "The geometric dissolve variant is not mapped yet."),
            ("ShatterWave", "geometry.shatter-wave", "The ShatterWave variant is not mapped yet."),
            ("Particle", "geometry.particles", "The geometric particles variant is not mapped yet."),
            ("Tessellated", "geometry.tessellation", "Tessellation is not mapped yet."),
            ("Triplanar Projection", "mapping.triplanar-projection", "The triplanar projection variant is not mapped yet."),
            ("Triple Outline", "geometry.outline", "Triple outlines are not mapped yet."),
            ("Grab Pass", "layer.screen-grab", "Screen-grab effects (refraction, blur, pixelation) are not mapped yet."),
            ("Self Grab", "layer.screen-grab", "Screen-grab effects (refraction, blur, pixelation) are not mapped yet."),
        };

        public static void Report(Material m, AtlasUnifiedMaterial d)
        {
            foreach (var (toggle, id, message) in Sections)
                if (AtlasMaterialReader.Float(m, toggle, 0f) > 0.5f)
                    AtlasMaterialReader.MissingFeature(d, id, message);
            if (AtlasMaterialReader.Texture(m, "_OutlineCurvatureData") != null)
                AtlasMaterialReader.MissingFeature(d, "geometry.outline", "Baked outline curvature data is not mapped yet.");

            string shader = AtlasMaterialReader.ShaderName(m);
            foreach (var (part, id, message) in Variants)
                if (shader.Contains(part))
                    AtlasMaterialReader.MissingFeature(d, id, message);
        }
    }
}
