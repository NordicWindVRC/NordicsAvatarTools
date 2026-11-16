using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Reflections & Specular (with its anisotropics and 2nd specular), Clear Coat, LTCGI.
    public abstract partial class AtlasPoiyomiParser
    {
        // Without Reflections & Specular, Poiyomi has no highlights or reflections (Cloth has its own).
        private static void Specular(Material m, AtlasUnifiedMaterial d)
        {
            var response = d.Surface.Response;
            bool section = On(m, "_MochieBRDF");
            if (d.Lighting.Cloth != null)
            {
                if (section)
                    d.Report(AtlasIssueKind.Unmapped, "brdf.with-cloth", "Reflections & Specular on top of Cloth shading isn't mapped; Cloth's own is used.");
            }
            else if (!section)
            {
                response.Highlights.Enabled = false;
                response.Reflections.Enabled = false;
            }
            else SpecularSection(m, d);
            ClearCoat(m, d);
            Ltcgi(m, d);
        }

        // The Mochie BRDF: metallic, smoothness, reflection and specular masks from one packed map.
        private static void SpecularSection(Material m, AtlasUnifiedMaterial d)
        {
            var response = d.Surface.Response;
            var map = Texture(m, d, "_MochieMetallicMaps", AtlasTextureInterpretation.Data);
            Packed(response.Metallic, map, PackedChannel(m, "_MochieMetallicMapsMetallicChannel", 0),
                Float(m, "_MochieMetallicMultiplier", 0f), On(m, "_MochieMetallicMapInvert"));
            // Roughness = 1 − smoothness; Invert Smoothness turns the slider and map into roughness.
            Packed(response.Roughness, map, PackedChannel(m, "_MochieMetallicMapsRoughnessChannel", 1),
                Float(m, "_MochieRoughnessMultiplier", 1f), !On(m, "_MochieRoughnessMapInvert"));

            var reflections = response.Reflections;
            reflections.Enabled = true;
            reflections.Mask = PackedStrength(d, map, PackedChannel(m, "_MochieMetallicMapsReflectionMaskChannel", 2),
                Float(m, "_MochieReflectionStrength", 1f), On(m, "_MochieReflectionMaskInvert"), out reflections.Strength);
            reflections.Tint = TintColor(m, d, "_MochieReflectionTint", "_MochieReflectionTintThemeIndex");
            reflections.FresnelStrength = Float(m, "_RefSpecFresnelStrength", 0.5f);
            reflections.ExposureOcclusion = Float(m, "_SFExposureOcclusion", 0f);
            Fallback(m, reflections, "_MochieReflCube", "_MochieForceFallback", "_MochieLitFallback");

            var highlights = response.Highlights;
            highlights.Enabled = true;
            highlights.Mask = PackedStrength(d, map, PackedChannel(m, "_MochieMetallicMapsSpecularMaskChannel", 3),
                Float(m, "_MochieSpecularStrength", 1f), On(m, "_MochieSpecularMaskInvert"), out highlights.Strength);
            highlights.Tint = TintColor(m, d, "_MochieSpecularTint", "_MochieSpecularTintThemeIndex");
            highlights.IgnoreShadows = On(m, "_IgnoreCastedShadows");
            if (On(m, "_Specular2ndLayer"))
            {
                highlights.SecondLobeStrength = Float(m, "_MochieSpecularStrength2", 1f);
                // The same map and invert as the first lobe's roughness, with its own constant.
                highlights.SecondLobeRoughnessConstant = Float(m, "_MochieRoughnessMultiplier2", 1f);
            }

            SpecularNormal(m, d, Float(m, "_PBRNormalSelect", 1f));
            var antiAliasing = response.SpecularAntiAliasing;
            antiAliasing.Enabled = On(m, "_MochieGSAAEnabled", 1f);
            antiAliasing.Variance = Float(m, "_PoiGSAAVariance", 0.15f);
            antiAliasing.Threshold = Float(m, "_PoiGSAAThreshold", 0.1f);

            if (On(m, "_GGXAnisotropics"))
            {
                var anisotropy = response.Anisotropy;
                anisotropy.Enabled = true;
                anisotropy.Amount = Float(m, "_Anisotropy", 0f);
                anisotropy.Map = Texture(m, d, "_AnisotropyMap", AtlasTextureInterpretation.Data);
                anisotropy.Channel = Channels[Mathf.Clamp(Int(m, "_AnisotropyMapChannel", 0), 0, 3)];
                int vertex = Int(m, "_AnisotropyMapVertexColor", 0);
                if (vertex > 0) { anisotropy.VertexColorChannel = Channels[Mathf.Clamp(vertex - 1, 0, 3)]; anisotropy.Map = null; }
                anisotropy.ReflectionStretch = Float(m, "_ReflectionAnisotropicStretch", 1f);
                anisotropy.RoughnessAnisotropy = Float(m, "_RoughnessAnisotropy", 1f);
                if (On(m, "_AnisotropyDirectionEnabled"))
                    anisotropy.DirectionMap = Texture(m, d, "_AnisotropyDirectionMap", AtlasTextureInterpretation.Data);
            }
            if (On(m, "_PBRSplitMaskSample"))
                d.Report(AtlasIssueKind.Unmapped, "brdf.split-masks", "Sampling the reflection and specular masks with their own UVs isn't mapped yet.");
            if (On(m, "_BRDFTPSDepthEnabled"))
                d.Report(AtlasIssueKind.Unsupported, "tps", "TPS depth masks aren't supported.");
            BusScalar(m, response.Metallic, "_MochieMetallicGlobalMask");
            BusScalar(m, response.Roughness, "_MochieSmoothnessGlobalMask");
            reflections.Mask = BusMask(m, reflections.Mask, "_MochieReflectionStrengthGlobalMask");
            highlights.Mask = BusMask(m, highlights.Mask, "_MochieSpecularStrengthGlobalMask");
        }

        // Pixel Normal Mix: 1 the normal map, 0 the mesh normal; in between isn't mapped.
        private static void SpecularNormal(Material m, AtlasUnifiedMaterial d, float mix)
        {
            d.Surface.Response.SpecularFromMeshNormal = mix < 0.5f;
            if (mix > 0f && mix < 1f)
                d.Report(AtlasIssueKind.Unmapped, "brdf.normal-mix", "A specular normal between the mesh normal and the normal map isn't mapped yet; the nearer is used.");
        }

        // A second reflective layer over the surface, dielectric.
        private static void ClearCoat(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_ClearCoatBRDF")) return;
            var coat = new AtlasClearCoat();
            var map = Texture(m, d, "_ClearCoatMaps", AtlasTextureInterpretation.Data);
            Packed(coat.Strength, map, PackedChannel(m, "_ClearCoatMapsClearCoatMaskChannel", 0),
                Float(m, "_ClearCoatStrength", 1f), On(m, "_ClearCoatMaskInvert"));
            Packed(coat.Roughness, map, PackedChannel(m, "_ClearCoatMapsRoughnessChannel", 1),
                Float(m, "_ClearCoatSmoothness", 1f), !On(m, "_ClearCoatSmoothnessMapInvert"));
            var reflections = coat.Reflections;
            reflections.Mask = PackedStrength(d, map, PackedChannel(m, "_ClearCoatMapsReflectionMaskChannel", 2),
                Float(m, "_ClearCoatReflectionStrength", 1f), On(m, "_ClearCoatReflectionMaskInvert"), out reflections.Strength);
            reflections.Tint = TintColor(m, d, "_ClearCoatReflectionTint", "_ClearCoatReflectionTintThemeIndex");
            reflections.FresnelStrength = Float(m, "_ClearcoatFresnelStrength", 0.5f);
            reflections.ExposureOcclusion = Float(m, "_ClearcoatExposureOcclusion", 0f);
            Fallback(m, reflections, "_ClearCoatFallback", "_ClearCoatForceFallback", "_ClearCoatLitFallback");
            var highlights = coat.Highlights;
            highlights.Mask = PackedStrength(d, map, PackedChannel(m, "_ClearCoatMapsSpecularMaskChannel", 3),
                Float(m, "_ClearCoatSpecularStrength", 1f), On(m, "_ClearCoatSpecularMaskInvert"), out highlights.Strength);
            highlights.Tint = TintColor(m, d, "_ClearCoatSpecularTint", "_ClearCoatSpecularTintThemeIndex");
            highlights.IgnoreShadows = On(m, "_CCIgnoreCastedShadows");
            coat.NormalMapAmount = Float(m, "_ClearCoatNormalSelect", 0f);
            coat.SpecularAntiAliasing.Enabled = On(m, "_ClearCoatGSAAEnabled", 1f);
            coat.SpecularAntiAliasing.Variance = Float(m, "_ClearCoatGSAAVariance", 0.15f);
            coat.SpecularAntiAliasing.Threshold = Float(m, "_ClearCoatGSAAThreshold", 0.1f);
            d.Surface.Response.ClearCoat = coat;
            if (On(m, "_ClearCoatTPSDepthMaskEnabled"))
                d.Report(AtlasIssueKind.Unsupported, "tps", "TPS depth masks aren't supported.");
            BusScalar(m, coat.Strength, "_ClearCoatGlobalMask");
            BusScalar(m, coat.Roughness, "_ClearCoatSmoothnessGlobalMask");
            reflections.Mask = BusMask(m, reflections.Mask, "_ClearCoatReflectionStrengthGlobalMask");
            highlights.Mask = BusMask(m, highlights.Mask, "_ClearCoatSpecularStrengthGlobalMask");
        }

        private static void Ltcgi(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_LTCGIEnabled")) return;
            var ltcgi = d.Lighting.Ltcgi;
            ltcgi.Enabled = true;
            ltcgi.DiffuseTint = TintColor(m, d, "_LTCGI_DiffuseColor", "_LTCGI_DiffuseColorThemeIndex");
            ltcgi.SpecularTint = TintColor(m, d, "_LTCGI_SpecularColor", "_LTCGI_SpecularColorThemeIndex");
            ltcgi.DiffuseStrength = Float(m, "_LTCGI_DiffuseMultiplier", 1f);
            ltcgi.SpecularStrength = Float(m, "_LTCGI_SpecularMultiplier", 1f);
            // Its own values when told to, or without Reflections & Specular.
            ltcgi.UseSurfaceResponse = On(m, "_LTCGI_UsePBR", 1f) && On(m, "_MochieBRDF");
            ltcgi.Metallic = Float(m, "_LTCGI_Metallic", 0f);
            ltcgi.Roughness = 1f - Float(m, "_LTCGI_Smoothness", 0f);
            ltcgi.ExposureOcclusion = On(m, "_LTCGI_UseEO");
        }

        // A packed-map channel selector (R, G, B, A, White): the channel, or null for white.
        private static AtlasChannel? PackedChannel(Material m, string property, int fallback)
        {
            int channel = Int(m, property, fallback);
            return channel >= 0 && channel < 4 ? Channels[channel] : (AtlasChannel?)null;
        }

        // value = constant × channel, inverted after when asked: a scalar input exactly.
        private static void Packed(AtlasScalarInput input, AtlasTextureReference map, AtlasChannel? channel, float constant, bool invertResult)
        {
            input.Constant = constant;
            input.Map = channel.HasValue ? map : null;
            if (channel.HasValue) input.Channel = channel.Value;
            input.InvertResult = invertResult;
        }

        // A strength × a packed channel, inverted after when asked, as a strength and a mask. With
        // a map, inverting is exact only at strength 1, and noted otherwise.
        private static AtlasMask PackedStrength(AtlasUnifiedMaterial d, AtlasTextureReference map, AtlasChannel? channel,
            float strength, bool invert, out float resultStrength)
        {
            if (map == null || !channel.HasValue)
            {
                resultStrength = invert ? 1f - strength : strength;
                return null;
            }
            resultStrength = strength;
            if (invert && strength != 1f)
                d.Report(AtlasIssueKind.Note, "brdf.inverted-mask", "An inverted reflection or specular mask below full strength is read as strength × (1 − mask).");
            return new AtlasMask { Map = map, Channel = channel.Value, Invert = invert };
        }

        private static void Fallback(Material m, AtlasReflections reflections, string cube, string force, string lit)
        {
            var map = AtlasSourceMapping.Texture(m, cube);
            reflections.CubemapLit = On(m, lit, 1f);
            if (map == null) return;
            if (On(m, force)) { reflections.Source = AtlasReflectionSource.Cubemap; reflections.Cubemap = map; }
            else reflections.FallbackCubemap = map;
        }

    }
}
