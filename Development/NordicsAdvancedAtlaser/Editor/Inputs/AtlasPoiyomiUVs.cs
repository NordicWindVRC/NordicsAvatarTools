using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Distortion UV, the projections' settings, Parallax Heightmapping.
    public abstract partial class AtlasPoiyomiParser
    {
        // Texture mode: ((map 1 × 2 − 1) × strength 1 + (map 2 × 2 − 1) × strength 2) ÷ 2 × the mask,
        // added to the chosen UV. A missing map reads as −1 (a constant shift).
        private static void UVOptions(Material m, AtlasUnifiedMaterial d)
        {
            distortion = null;
            distortedChannel = 0;
            if (On(m, "_UVSettingsShiftBackfaceUV"))
                d.Report(AtlasIssueKind.Unmapped, "uv.backface-shift", "Shifting back faces' UVs isn't mapped yet.");
            if (!On(m, "_EnableDistortion")) return;
            distortedChannel = Mathf.Clamp(Int(m, "_DistortionUvToDistort", 0), 0, 3);
            if (Int(m, "_DistortionMode", 0) != 0)
            {
                d.Report(AtlasIssueKind.Unmapped, "uv.distortion-mode", "Gerstner, simplex, voronoi, curl and FBM distortions aren't mapped yet; read without distortion.");
                return;
            }
            float strength = Float(m, "_DistortionStrength", 0.05f) * 0.5f, strength2 = Float(m, "_DistortionStrength1", 0.02f) * 0.5f;
            distortion = new AtlasUVDistortion
            {
                Source = AtlasDistortionSource.NormalMap,
                NormalMap = Texture(m, d, "_DistortionFlowTexture", AtlasTextureInterpretation.Data),
                Strength = strength,
                NormalMap2 = Texture(m, d, "_DistortionFlowTexture1", AtlasTextureInterpretation.Data),
                Strength2 = strength2,
                Mask = Mask(m, d, "_DistortionMask", Channels[Mathf.Clamp(Int(m, "_DistortionMaskChannel", 0), 0, 3)])
            };
            if (distortion.NormalMap == null) distortion.ConstantOffset -= Vector2.one * strength;
            if (distortion.NormalMap2 == null) distortion.ConstantOffset -= Vector2.one * strength2;
            if (AudioLinkOn(m) && On(m, "_EnableDistortionAudioLink"))
                d.Report(AtlasIssueKind.Unmapped, "audiolink.distortion", "A distortion's strength by AudioLink isn't mapped yet.");
        }

        // The projections' settings that differ from the defaults (polar standard around (0.5, 0.5)
        // on UV0, mono panosphere, local x and y, world x and z).
        private static void ReportProjectionSettings(Material m, AtlasUnifiedMaterial d, AtlasTextureSpace space)
        {
            bool custom;
            switch (space)
            {
                case AtlasTextureSpace.Polar:
                    var centre = AtlasMaterialReader.Vector(m, "_PolarCenter", new Vector4(0.5f, 0.5f, 0f, 0f));
                    custom = Int(m, "_PolarMode", 0) != 0 || Int(m, "_PolarUV", 0) != 0 || centre.x != 0.5f || centre.y != 0.5f
                        || Float(m, "_PolarRadialScale", 1f) != 1f || Float(m, "_PolarLengthScale", 1f) != 1f || Float(m, "_PolarSpiralPower", 0f) != 0f;
                    break;
                case AtlasTextureSpace.Panosphere:
                    custom = Int(m, "_StereoEnabled", 0) != 0 || !On(m, "_PanoUseBothEyes", 1f);
                    break;
                case AtlasTextureSpace.ObjectPosition:
                    custom = Int(m, "_UVModLocalPos0", 0) != 0 || Int(m, "_UVModLocalPos1", 1) != 1;
                    break;
                default:
                    custom = Int(m, "_UVModWorldPos0", 0) != 0 || Int(m, "_UVModWorldPos1", 2) != 2;
                    break;
            }
            if (custom)
                d.Report(AtlasIssueKind.Unmapped, "uv.projection-settings", "Polar, panosphere and local or world UV settings other than the defaults aren't mapped yet.");
        }

        // Parallax occlusion mapping: multi-step, the heightmap's red, strength as the depth, the offset.
        private static void Parallax(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_PoiParallax")) return;
            var parallax = d.Surface.Parallax;
            parallax.Enabled = true;
            parallax.Method = AtlasParallaxMethod.MultiStep;
            parallax.Height.Map = Texture(m, d, "_HeightMap", AtlasTextureInterpretation.Data);
            parallax.Depth = Float(m, "_HeightStrength", 0.005f);
            parallax.HeightOffset = Float(m, "_HeightOffset", 0f);
            parallax.Steps = Mathf.Max(1, Int(m, "_HeightStepsMax", 128));
            if (Int(m, "_ParallaxUV", 0) != 0)
                d.Report(AtlasIssueKind.Unmapped, "parallax.channel", "Parallax on a UV channel other than UV0 isn't mapped yet.");
            if (Mask(m, d, "_Heightmask") != null || Int(m, "_HeightmaskGlobalMask", 0) > 0)
                d.Report(AtlasIssueKind.Unmapped, "parallax.mask", "A parallax mask isn't mapped yet.");
            d.Report(AtlasIssueKind.Note, "parallax.occlusion", "Poiyomi's cone-stepped parallax occlusion is read as multi-step parallax.");
        }
    }
}
