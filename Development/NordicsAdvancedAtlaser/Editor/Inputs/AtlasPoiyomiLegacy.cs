using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi 9 and older: read on 10's parser. The features Poiyomi 10 rewrote are read
    // with 10's meaning and noted once each while in use; their settings 10 dropped are part of
    // that note. Settings whose meaning changed are handled where they are read (major < 10).
    public abstract partial class AtlasPoiyomiParser
    {
        private static void Legacy(Material m, AtlasUnifiedMaterial d)
        {
            string v = "Poiyomi " + major + "'s ";
            void Note(string feature, string what) =>
                d.Report(AtlasIssueKind.Note, "poiyomi" + major + "." + feature, v + what + " is read with Poiyomi 10's meaning; its look may differ.");

            bool decals = false;
            for (int i = 0; i < 4; i++) decals |= On(m, "_DecalEnabled" + (i == 0 ? "" : i.ToString()));
            if (decals) Note("decals", "decal placement and blending (and its TPS depth masks)");
            if (On(m, "_EnableALDecal")) Note("audiolink-decal", "AudioLink decal (its UV position, scale and rotation)");
            if (On(m, "_EnableDissolve")) Note("dissolve", "dissolve (edge hardness, detail edge smoothing, continuous dissolve, per-tile alphas, clamps, centre-out power)");
            if (On(m, "_GlitterEnable")) Note("glitter", "glitter (its centre size; 8's panning, jitter and linear emissive frequency)");
            bool matcaps = false;
            foreach (string p in new[] { "_MatcapEnable", "_Matcap2Enable", "_Matcap3Enable", "_Matcap4Enable" }) matcaps |= On(m, p);
            if (matcaps) Note("matcap", "matcap");
            if (On(m, "_EnableFlipbook")) Note("flipbook", "flipbook (its scale and offset, panning and replace amount; 8's multiply and add amounts)");
            if (On(m, "_EnableDepthRimLighting")) Note("depth-rim", "depth rim");
            if ((On(m, "_EnableRimLighting") && Int(m, "_RimStyle", 0) == 0) || (On(m, "_EnableRim2Lighting") && Int(m, "_Rim2Style", 0) == 0))
                Note("rim", "Poiyomi rim (its sharpness; 8's clamp)");
            if (On(m, "_ClearCoatBRDF")) Note("clear-coat", "clear coat");
            if (On(m, "_MochieBRDF")) Note("reflections", "reflections and specular");
            if (On(m, "_StylizedSpecular")) Note("stylized-specular", "stylized specular");
            if (On(m, "_SubsurfaceScattering")) Note("sss", "subsurface scattering");
            if (On(m, "_RGBMaskEnabled")) Note("rgb-mask", "RGBA colour masking (8's vertex colour source, multiplicative blending and normals)");
            if (On(m, "_VoronoiEnabled")) Note("voronoi", "voronoi");
            if (On(m, "_VideoEffectsEnable")) Note("video", "video screen");
            if (On(m, "_BacklightEnabled")) Note("backlight", "backlight");
            if (On(m, "_EnableDistortion")) Note("distortion", "UV distortion");
            if (On(m, "_EnableOutlines") && On(m, "_EnableAudioLink"))
            {
                var mod = AtlasMaterialReader.Vector(m, "_AudioLinkOutlineColorMod", new Vector4(0f, 1f, 0f, 0f));
                if (mod.x != 0f || mod.y != 1f) Note("outline-audiolink", "outline colour by AudioLink");
            }
            if (major <= 7)
            {
                Note("lighting", "lighting (its ramp mask, HSL shadows, shadow mix, colour modes and limits)");
                void Old(string toggle, string feature, string what)
                {
                    if (On(m, toggle)) d.Report(AtlasIssueKind.Unmapped, "poiyomi7." + feature, "Poiyomi 7.3's " + what + " isn't mapped.");
                }
                Old("_EnableSpecular", "specular", "specular (its two specular sections)");
                Old("_EnableSpecular1", "specular", "specular (its two specular sections)");
                Old("_EnableMetallic", "metallics", "metallics");
                Old("_EnableBRDF", "brdf", "BRDF section");
                Old("_EnableClearCoat", "clear-coat", "clear coat");
                Old("_EnableSSS", "sss", "subsurface scattering");
                Old("_PanoToggle", "panosphere", "panosphere section");
                if (AtlasSourceMapping.Texture(m, "_LightingAOTex", AtlasTextureInterpretation.Data) != null
                    || AtlasSourceMapping.Texture(m, "_LightingDetailShadows", AtlasTextureInterpretation.Data) != null
                    || AtlasSourceMapping.Texture(m, "_LightingShadowMask", AtlasTextureInterpretation.Data) != null)
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi7.light-maps", "Poiyomi 7.3's AO, detail shadow and ramp mask maps aren't mapped.");
                if (Float(m, "_MainEmissionStrength", 0f) > 0f)
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi7.basic-emission", "Poiyomi 7.3's basic emission isn't mapped.");
                if (AtlasSourceMapping.Texture(m, "_MainFadeTexture", AtlasTextureInterpretation.Data) != null)
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi7.fade-mask", "Poiyomi 7.3's distance fade mask isn't mapped; the fade applies everywhere.");
                if (On(m, "_EnableAudioLink") && (On(m, "_EnableEmissionStrengthAudioLink") || On(m, "_EnableEmissionCenterOutAudioLink")
                    || On(m, "_EnableEmission1StrengthAudioLink") || On(m, "_EnableEmission1CenterOutAudioLink")))
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi7.emission-audiolink", "Poiyomi 7.3's emission AudioLink isn't mapped.");
                if (On(m, "_PoiParallax")) Note("parallax", "parallax (its bias and internal parallax under the same toggle)");
            }
            if (major <= 8)
            {
                if (On(m, "_CubeMapEnabled")) Note("cubemap", "cubemap (its replace, multiply and add weights)");
                if (On(m, "_EnableIridescence"))
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi8.iridescence", "Poiyomi 8's iridescence (removed in 9) isn't mapped.");
                if (On(m, "_BackFaceEnabled") && Float(m, "_BackFaceAlpha", 1f) != 1f)
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi8.back-face-alpha", "Poiyomi 8.0's back face alpha isn't mapped.");
                if (legacyColorBlend)
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi8.blend-modes", "Poiyomi 8.0's burn, dodge and light blend modes are read as multiply, screen or overlay.");
                if (legacyMaskBlend)
                    d.Report(AtlasIssueKind.Unmapped, "poiyomi8.mask-blend", "A Poiyomi 8.1 global mask blend past Add (screen, add, overlay or mixed in its inspector, 0 in the shader) is read as replace.");
            }
            // 9.0's settings 9.3 already dropped.
            if (AtlasMaterialReader.Texture(m, "_AnisoNoiseMap") != null)
                d.Report(AtlasIssueKind.Unmapped, "poiyomi9.aniso-noise", "Poiyomi 9.0's anisotropy noise map isn't mapped.");
            if (Float(m, "_RefSpecFresnel", 1f) != 1f || Float(m, "_RefSpecFresnelBack", 1f) != 1f || Float(m, "_ClearcoatFresnel", 1f) != 1f)
                d.Report(AtlasIssueKind.Unmapped, "poiyomi9.fresnel", "Poiyomi 9.0's reflection and clear coat fresnel strengths aren't mapped.");
            var hsvg = AtlasMaterialReader.Vector(m, "_OutlineTexHSVG", new Vector4(0f, 1f, 1f, 1f));
            if (On(m, "_EnableOutlines") && hsvg != new Vector4(0f, 1f, 1f, 1f))
                d.Report(AtlasIssueKind.Unmapped, "poiyomi9.outline-hsvg", "Poiyomi 9.0's outline texture HSVG isn't mapped.");
        }
    }
}
