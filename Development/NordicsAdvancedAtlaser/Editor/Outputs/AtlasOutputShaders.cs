using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Output shaders declare what they can express and their texture layouts; the shared
    // output features decide what to produce. Registrations are discovered automatically by
    // AtlasShaderRegistry.
    public sealed class PoiyomiToon10OutputShader : IAtlasOutputShader
    {
        public string ShaderId => "poiyomi10";
        public string DisplayName => "Poiyomi Toon 10";
        public string Family => AtlasShaderFamilies.Poiyomi;
        public int Rank => 100;
        // Poiyomi 11 keeps 10's shader name; with Poiyomi Pro installed, 10 lives under Old Versions.
        public Shader FindShader()
        {
            foreach (string name in new[] { ".poiyomi/Poiyomi Toon", ".poiyomi/Old Versions/10.0/Poiyomi Toon" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                int major = AtlasPoiyomiVersionDetector.DetectFromMetadata(shader.name,
                    UnityEditor.AssetDatabase.GetAssetPath(shader), AtlasPoiyomiVersionDetector.GetShaderMasterLabel(shader));
                if (major == 10) return shader;
            }
            return null;
        }
        public AtlasOutputCapabilities Capabilities { get; } = new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
                    .With(AtlasVirtualMap.Alpha, AtlasTextureChannels.A),
                NormalTexture(),
                // Poiyomi's packed maps (_MochieMetallicMaps): R metallic, G smoothness, B reflection
                // mask, A specular mask, read as they are (multipliers 1).
                new AtlasOutputTexture("Metallic", "Metallic and smoothness") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Metallic, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.Roughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                    .With(AtlasVirtualMap.ReflectionMask, AtlasTextureChannels.B, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.SpecularMask, AtlasTextureChannels.A, AtlasOutputTexture.Gray(0f)),
                // _LightingAOMaps: occlusion in R.
                new AtlasOutputTexture("Occlusion", "Ambient occlusion") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Occlusion, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f)),
                EmissionTexture(),
                // _LightingShadowMasks reads R, _LightingDetailShadowMaps G (by their channel strengths).
                new AtlasOutputTexture("LightingMasks", "Shadow and detail masks") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.ShadowStrengthMask, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f))
                    .With(AtlasVirtualMap.DetailShadow, AtlasTextureChannels.G, AtlasOutputTexture.Gray(1f)),
                // _ShadowColorTex / _1st_ShadeMap, _Shadow2ndColorTex / _2nd_ShadeMap, _Shadow3rdColorTex.
                new AtlasOutputTexture("ShadowColor1", "Shadow color 1") { SizeMode = AtlasTextureSizeMode.MainAspect }
                    .With(AtlasVirtualMap.ShadowColor1, AtlasTextureChannels.RGBA),
                new AtlasOutputTexture("ShadowColor2", "Shadow color 2") { SizeMode = AtlasTextureSizeMode.MainAspect }
                    .With(AtlasVirtualMap.ShadowColor2, AtlasTextureChannels.RGBA),
                new AtlasOutputTexture("ShadowColor3", "Shadow color 3") { SizeMode = AtlasTextureSizeMode.MainAspect }
                    .With(AtlasVirtualMap.ShadowColor3, AtlasTextureChannels.RGBA),
                // _SkinThicknessMap reads R.
                new AtlasOutputTexture("SkinThickness", "Skin thickness") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.SkinThickness, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
            },
            SurfaceModes = { AtlasSurfaceMode.Cutout, AtlasSurfaceMode.Transparent, AtlasSurfaceMode.Fade },
            VRChatFallback = true,
            BlendedDepthWrite = true,
            VertexColors = AtlasVertexColorSupport.Color | AtlasVertexColorSupport.Alpha | AtlasVertexColorSupport.Amount | AtlasVertexColorSupport.Srgb,
            // UV Tile Discard: on both sides (_UDIMDiscard*) and one side (_UDIMFaceDiscard*),
            // each reading UV0 to UV3.
            TileDiscard = true,
            FaceTileDiscard = true,
            UVChannels = 3,
            // _SDFShadingTextureUV and _LightDataSDFMapUV pick the channel.
            SdfFace = true
        };

        // Poiyomi 10's rendering presets (_Mode), as its shader's on_value_actions set them, by
        // property: _Mode, queue, RenderType, _AlphaForceOpaque, _Cutoff, _SrcBlend, _DstBlend,
        // _AddSrcBlend, _ZWrite, _AlphaPremultiply, _OutlineSrcBlend, _OutlineDstBlend,
        // _OutlineDstBlendAlpha. The properties every preset sets alike follow in Write.
        private static readonly (AtlasSurfaceMode Mode, int Preset, int Queue, string Type, int ForceOpaque, float Cutoff,
            int Src, int Dst, int AddSrc, int ZWrite, int Premultiply, int OutlineSrc, int OutlineDst, int OutlineDstAlpha)[] Presets =
        {
            (AtlasSurfaceMode.Opaque, 0, 2000, "Opaque", 1, 0f, 1, 0, 1, 1, 0, 1, 0, 0),
            (AtlasSurfaceMode.Cutout, 1, 2450, "TransparentCutout", 0, 0.5f, 1, 0, 1, 1, 0, 1, 0, 1),
            (AtlasSurfaceMode.Fade, 2, 3000, "Transparent", 0, 0.002f, 5, 10, 5, 0, 0, 5, 10, 1),
            // TransClipping: a fade that writes depth.
            (AtlasSurfaceMode.Fade, 9, 2460, "TransparentCutout", 0, 0.01f, 5, 10, 5, 1, 0, 5, 10, 1),
            (AtlasSurfaceMode.Transparent, 3, 3000, "Transparent", 0, 0f, 1, 10, 1, 0, 1, 1, 10, 1)
        };

        // The main normal map: linear, a size keeping the main texture's aspect.
        public static AtlasOutputTexture NormalTexture() =>
            new AtlasOutputTexture("Normal", "Normal map") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false, Normal = true }
                .With(AtlasVirtualMap.Normal, AtlasTextureChannels.RGB, new Color(0f, 0f, 1f, 1f));

        // Poiyomi 10's main section: _Color (sRGB-authored, so set from the linear color's
        // gamma value) times _MainTex, whose alpha holds the baked alpha; the normal map at the
        // output's strength; then the rendering preset of the surface mode, and VRChat's fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            var normal = output.Surface.Normal;
            material.SetTexture("_BumpMap", normal.Map?.Texture);
            material.SetFloat("_BumpScale", normal.Map?.Texture != null ? normal.Strength : 1f);
            material.SetTextureScale("_BumpMap", Vector2.one);
            material.SetTextureOffset("_BumpMap", Vector2.zero);
            material.SetFloat("_BumpMapUV", 0);
            // Vertex colors: base × lerp(1, vertex RGB (sRGB decoded with Linear Colors), amount),
            // alpha × lerp(1, vertex alpha, alpha amount).
            var vertex = output.Surface.VertexColor;
            bool vertexColors = vertex.MultiplyColor || vertex.MultiplyAlpha;
            material.SetFloat("_MainVertexColoringEnabled", vertexColors ? 1 : 0);
            material.SetFloat("_MainVertexColoring", vertex.MultiplyColor ? Mathf.Clamp01(vertex.Amount) : 0f);
            material.SetFloat("_MainUseVertexColorAlpha", vertex.MultiplyAlpha ? Mathf.Clamp01(vertex.AlphaAmount) : 0f);
            material.SetFloat("_MainVertexColoringLinearSpace", vertex.SrgbData ? 1 : 0);
            WriteTileDiscard(output.Render.HiddenTiles, material);
            WriteLighting(output.Lighting, material);
            WriteSurfaceResponse(output.Surface.Response, material);
            WriteEmission(output, material);
            var render = output.Render;
            var preset = Array.Find(Presets, p => p.Mode == render.SurfaceMode &&
                (p.Mode != AtlasSurfaceMode.Fade || (p.ZWrite == 1) == render.Pass.DepthWrite));
            if (preset.Type == null) preset = Presets[0];
            material.SetFloat("_Mode", preset.Preset);
            material.SetOverrideTag("RenderType", preset.Type);
            material.SetFloat("_AlphaForceOpaque", preset.ForceOpaque);
            material.SetFloat("_Cutoff", preset.Mode == AtlasSurfaceMode.Cutout ? render.Cutoff : preset.Cutoff);
            material.SetFloat("_BlendOp", 0);
            material.SetFloat("_BlendOpAlpha", 4);
            material.SetFloat("_SrcBlend", preset.Src);
            material.SetFloat("_DstBlend", preset.Dst);
            material.SetFloat("_SrcBlendAlpha", 1);
            material.SetFloat("_DstBlendAlpha", 1);
            material.SetFloat("_AddSrcBlend", preset.AddSrc);
            material.SetFloat("_AddDstBlend", 1);
            material.SetFloat("_AddSrcBlendAlpha", 0);
            material.SetFloat("_AddDstBlendAlpha", 1);
            material.SetFloat("_AlphaToCoverage", preset.Mode == AtlasSurfaceMode.Cutout && render.AlphaToCoverage ? 1 : 0);
            material.SetFloat("_ZWrite", preset.ZWrite);
            material.SetFloat("_ZTest", 4);
            material.SetFloat("_AlphaPremultiply", preset.Premultiply);
            material.SetFloat("_OutlineSrcBlend", preset.OutlineSrc);
            material.SetFloat("_OutlineDstBlend", preset.OutlineDst);
            material.SetFloat("_OutlineSrcBlendAlpha", 1);
            material.SetFloat("_OutlineDstBlendAlpha", preset.OutlineDstAlpha);
            material.SetFloat("_OutlineBlendOp", 0);
            material.SetFloat("_OutlineBlendOpAlpha", 4);
            material.renderQueue = render.Queue >= 0 ? render.Queue : preset.Queue;
            material.SetOverrideTag("VRCFallback", Fallback(render));
        }

        // The emission map, sRGB, a size keeping the main texture's aspect.
        public static AtlasOutputTexture EmissionTexture() =>
            new AtlasOutputTexture("Emission", "Emission") { SizeMode = AtlasTextureSizeMode.MainAspect }
                .With(AtlasVirtualMap.Emission, AtlasTextureChannels.RGB, new Color(0f, 0f, 0f, 1f));

        // Emission 0 (_EnableEmission with its _EMISSION keyword): _EmissionColor (HDR, set from
        // the linear color's gamma value) × _EmissionMap × _EmissionStrength (0–20; above that the
        // rest goes into the color).
        private static void WriteEmission(AtlasUnifiedMaterial output, Material material)
        {
            var emission = AtlasEmissionFeature.Of(output);
            var map = emission?.Source.Map?.Texture;
            material.SetFloat("_EnableEmission", map != null ? 1 : 0);
            if (map == null)
            {
                material.DisableKeyword("_EMISSION");
                return;
            }
            material.EnableKeyword("_EMISSION");
            float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
            float strength = Mathf.Min(total, 20f);
            float rest = total / Mathf.Max(strength, 1e-4f);
            material.SetTexture("_EmissionMap", map);
            material.SetTextureScale("_EmissionMap", Vector2.one);
            material.SetTextureOffset("_EmissionMap", Vector2.zero);
            material.SetFloat("_EmissionMapUV", 0);
            material.SetColor("_EmissionColor", new Color(rest, rest, rest, 1f).gamma);
            material.SetFloat("_EmissionStrength", strength);
        }

        // Reflections & Specular (MOCHIE_PBR) from the packed map: metallic = R, smoothness = G,
        // the reflection and specular masks B and A × their strengths; and the AO maps
        // (PROP_LIGHTINGAOMAPS): occlusion = lerp(1, R, strength). Thry sets these keywords from
        // its inspector; a written material sets them itself.
        // Lighting: Light Data and the lighting type, the Poiyomi parser's reading written back
        // (AtlasPoiyomiLighting). Parts drawn on the texture are cleared by the Lighting feature.
        private static readonly string[] LightingKeywords =
        {
            "_LIGHTINGMODE_TEXTURERAMP", "_LIGHTINGMODE_MULTILAYER_MATH", "_LIGHTINGMODE_WRAPPED", "_LIGHTINGMODE_SKIN",
            "_LIGHTINGMODE_SHADEMAP", "_LIGHTINGMODE_FLAT", "_LIGHTINGMODE_REALISTIC", "_LIGHTINGMODE_CLOTH", "_LIGHTINGMODE_SDF"
        };

        private static void WriteLighting(AtlasLighting l, Material m)
        {
            m.SetFloat("_LightingColorMode", l.ColorModel == AtlasLightColorModel.Separate ? 1 : l.ColorModel == AtlasLightColorModel.Floored ? 2
                : l.ColorModel == AtlasLightColorModel.SplitProbes ? 3 : 0);
            m.SetFloat("_Unlit_Intensity", l.FloorIntensity);
            m.SetFloat("_LightingIndirectUsesNormals", l.Ambient == AtlasAmbientModel.Directional ? l.DirectionalAmount : 0f);
            int direction = 0;
            switch (l.Direction)
            {
                case AtlasLightDirectionModel.Fixed:
                    direction = l.DirectionLocal ? 1 : 2;
                    m.SetVector("_LightngForcedDirection", l.LightDirection);
                    break;
                case AtlasLightDirectionModel.ViewFallback: direction = 3; break;
                case AtlasLightDirectionModel.SplitProbes: direction = 4; break;
                case AtlasLightDirectionModel.View:
                    direction = 5;
                    m.SetFloat("_LightingViewDirOffsetYaw", l.ViewYaw);
                    m.SetFloat("_LightingViewDirOffsetPitch", l.ViewPitch);
                    break;
            }
            m.SetFloat("_LightingDirectionMode", direction);
            var ramp = l.Ramp;
            bool multilayer = l.Family == AtlasLightingFamily.RampToon && ramp.Term != AtlasShadingTerm.Wrapped && ramp.Bands.Count > 0 && !IsShadeMap(ramp);
            // Multilayer Math builds its own half-Lambert terms; Shade Map keeps the light map's.
            bool blendedSdf = ramp.Term == AtlasShadingTerm.SdfFace && ramp.Sdf?.Style == AtlasSdfStyle.Blended;
            if (blendedSdf)
            {
                // Light map mode SDF: a blended SDF texture's red or green by the light's side.
                TextureOn(m, "_LightDataSDFMap", ramp.Sdf.Map);
                m.SetFloat("_LightDataSDFBlendY", ramp.Sdf.VerticalBlend);
                m.SetFloat("_LightDataSDFMapLOD", ramp.Sdf.Softness);
                m.SetFloat("_LightDataSDFChannelsBA", ramp.Sdf.MaskChannels ? 1 : 0);
            }
            m.SetFloat("_LightingMapMode", blendedSdf ? 4 : multilayer || ramp.Term == AtlasShadingTerm.HalfLambert ? 1 : ramp.Term == AtlasShadingTerm.Lambert ? 2
                : ramp.Term == AtlasShadingTerm.ShadowsOnly ? 3 : 0);
            // Shadow masks scale the shadow strength; detail shadows multiply the light map.
            PackedMask(m, "_LightingShadowMasks", "_LightingShadowMaskStrength", ramp.RampStrengthMask);
            PackedMask(m, "_LightingDetailShadowMaps", "_LightingDetailShadowStrength", ramp.TermMask);
            for (int c = 0; c < 4; c++)
                m.SetFloat("_LightingAddDetailShadowStrength" + "RGBA"[c], m.GetFloat("_LightingDetailShadowStrength" + "RGBA"[c]));
            m.SetFloat("_LightingCastedShadows", l.ReceiveShadows);
            m.SetFloat("_LightingShadowSoftness", l.ShadowSoftness);
            m.SetFloat("_LightingCleanCastedShadows", l.CleanShadows ? 1 : 0);
            m.SetFloat("_LightingMonochromatic", l.Monochrome);
            m.SetFloat("_LightingCapEnabled", l.LimitBrightness ? 1 : 0);
            m.SetFloat("_LightingCap", l.BrightnessCeiling);
            m.SetFloat("_LightingCapLuminance", l.LimitByLuminance);
            m.SetFloat("_LightingCapMode", l.LimitKeepsHue ? 1 : 0);
            m.SetFloat("_BackfaceForceShadow", ramp.BackFaceShadow);
            m.SetFloat("_LightingMinLightBrightness", l.MinimumLight);
            m.SetFloat("_LightingForceColorEnabled", l.FixedLightColor ? 1 : 0);
            m.SetColor("_LightingForcedColor", l.LightColor.gamma);
            m.SetFloat("_LightingForcedColorThemeIndex", 0);
            m.SetFloat("_LightingVertexLightingEnabled", l.Contributions.VertexLights > 0f ? 1 : 0);
            m.SetFloat("_LightingAdditiveEnable", l.Contributions.AdditionalLights > 0f ? 1 : 0);
            var additional = l.AdditionalLights;
            m.SetFloat("_LightingAdditiveType", additional.Shading == AtlasAdditionalLightShading.Lambert ? 0
                : additional.Shading == AtlasAdditionalLightShading.Toon ? 1 : 3);
            m.SetFloat("_LightingAdditiveGradientStart", additional.GradientStart);
            m.SetFloat("_LightingAdditiveGradientEnd", additional.GradientEnd);
            m.SetFloat("_LightingAdditiveLimited", additional.LimitBrightness ? 1 : 0);
            m.SetFloat("_LightingAdditiveLimit", additional.BrightnessCeiling);
            m.SetFloat("_LightingAdditiveLimitMode", additional.LimitKeepsHue ? 1 : 0);
            m.SetFloat("_LightingAdditiveCastedShadows", additional.ReceiveShadows);
            m.SetFloat("_LightingAdditiveMonochromatic", additional.Monochrome);
            m.SetFloat("_LightingAdditivePassthrough", additional.Passthrough);
            m.SetVector("_LightingAdditiveAttenCurve", additional.Falloff);
            m.SetFloat("_DisableDirectionalInAdd", additional.Directional ? 0 : 1);
            m.SetFloat("_LightingMirrorVertexLightingEnabled", additional.VertexLightsInMirrors ? 1 : 0);
            // The add pass: Max (4) with the alpha boost, or add.
            m.SetFloat("_AddBlendOp", additional.Combine == AtlasLightCombine.Max ? 4 : 0);
            m.SetFloat("_AlphaBoostFA", additional.MaxAlphaBoost);
            m.SetFloat("_PPLightingMultiplier", l.LightMultiplier);
            m.SetFloat("_PPLightingAddition", l.LightAddition);
            var volumes = l.LightVolumes;
            m.SetFloat("_LightingEnableLightVolumes", volumes.Enabled ? 1 : 0);
            m.SetFloat("_LightingLightVolumesSpecular", volumes.Specular ? volumes.SpecularStrength : 0f);
            m.SetFloat("_LightingLightVolumesNormalBias", volumes.Bias);
            m.SetFloat("_LightingLightVolumesSpecularMaxLights", volumes.SpecularMaxLights);
            // LTCGI (POI_LTCGI), which Poiyomi discourages on avatars now that worlds light them
            // through light volumes.
            var ltcgi = l.Ltcgi;
            m.SetFloat("_LTCGIEnabled", ltcgi.Enabled ? 1 : 0);
            if (ltcgi.Enabled) m.EnableKeyword("POI_LTCGI");
            else m.DisableKeyword("POI_LTCGI");
            m.SetFloat("_LTCGI_AnimToggle", 1);
            m.SetColor("_LTCGI_DiffuseColor", ltcgi.DiffuseTint.gamma);
            m.SetFloat("_LTCGI_DiffuseColorThemeIndex", 0);
            m.SetColor("_LTCGI_SpecularColor", ltcgi.SpecularTint.gamma);
            m.SetFloat("_LTCGI_SpecularColorThemeIndex", 0);
            m.SetFloat("_LTCGI_DiffuseMultiplier", ltcgi.DiffuseStrength);
            m.SetFloat("_LTCGI_SpecularMultiplier", ltcgi.SpecularStrength);
            m.SetFloat("_LTCGI_UsePBR", ltcgi.UseSurfaceResponse ? 1 : 0);
            m.SetFloat("_LTCGI_Metallic", ltcgi.Metallic);
            m.SetFloat("_LTCGI_Smoothness", 1f - ltcgi.Roughness);
            m.SetFloat("_LTCGI_UseEO", ltcgi.ExposureOcclusion ? 1 : 0);

            bool shading = l.Family != AtlasLightingFamily.Unlit;
            m.SetFloat("_ShadingEnabled", shading ? 1 : 0);
            if (shading) m.EnableKeyword("VIGNETTE_MASKED");
            else m.DisableKeyword("VIGNETTE_MASKED");
            int type = LightingType(l, m);
            m.SetFloat("_LightingMode", type);
            for (int k = 0; k < LightingKeywords.Length; k++)
                if (k == type) m.EnableKeyword(LightingKeywords[k]);
                else m.DisableKeyword(LightingKeywords[k]);
        }

        // A texture slot set from a reference: its texture, tiling and offset, and its UV channel
        // (UV0 to UV3); no texture without one.
        private static void TextureOn(Material m, string property, AtlasTextureReference map)
        {
            m.SetTexture(property, map?.Texture);
            m.SetTextureScale(property, map?.Tiling ?? Vector2.one);
            m.SetTextureOffset(property, map?.Offset ?? Vector2.zero);
            int uv = map == null ? 0 : (int)(map.Space - AtlasTextureSpace.UV0);
            m.SetFloat(property + "UV", uv >= 0 && uv <= 3 ? uv : 0);
        }

        // A mask in one channel of a packed texture slot: the channel's strength set, the others 0.
        // Without a mask: no texture, the default strengths (R at 1).
        private static void PackedMask(Material m, string property, string strengths, AtlasMask mask)
        {
            var texture = mask?.Map?.Texture;
            m.SetTexture(property, texture);
            m.SetTextureScale(property, Vector2.one);
            m.SetTextureOffset(property, Vector2.zero);
            m.SetFloat(property + "UV", 0);
            int channel = texture == null ? 0 : Mathf.Clamp((int)mask.Channel, 0, 3);
            for (int c = 0; c < 4; c++)
                m.SetFloat(strengths + "RGBA"[c], c == channel ? (texture == null ? 1f : Mathf.Clamp01(mask.Strength)) : 0f);
        }

        // Shade Map's reading: two steps, both sides lit by the direct light, the shades occluded.
        private static bool IsShadeMap(AtlasRampToon ramp) =>
            ramp.Bands.Count == 2 && ramp.DirectOccluded && Mathf.Approximately(ramp.ShadowDirect, 1f) && ramp.ShadowAmbient <= 0f && !ramp.RampScaledShadowDirect;

        // The lighting type and its settings.
        private static int LightingType(AtlasLighting l, Material m)
        {
            var ramp = l.Ramp;
            switch (l.Family)
            {
                case AtlasLightingFamily.Unlit:
                    return 5;
                case AtlasLightingFamily.Flat:
                    m.SetFloat("_ForceFlatRampedLightmap", l.FlatLightMap ? 1 : 0);
                    return 5;
                case AtlasLightingFamily.PhysicallyBased:
                    if (l.Diffuse == AtlasDiffuseModel.PreintegratedSkin)
                    {
                        var skin = l.Skin;
                        m.SetColor("_LightingShadowColor", l.ShadowTint.gamma);
                        m.SetTexture("_SkinLUT", skin.Lut?.Texture);
                        m.SetFloat("_SssScale", skin.Scattering);
                        m.SetTexture("_SkinThicknessMap", skin.Thickness?.Map?.Texture);
                        m.SetTextureScale("_SkinThicknessMap", Vector2.one);
                        m.SetTextureOffset("_SkinThicknessMap", Vector2.zero);
                        m.SetFloat("_SkinThicknessMapUV", 0);
                        m.SetFloat("_SkinThicknessMapInvert", 0);
                        m.SetFloat("_SkinThicknessPower", skin.ThicknessPower);
                        m.SetVector("_SssTransmissionAbsorption", skin.Absorption);
                        m.SetFloat("_SkinScatteringStability", skin.Stability);
                        m.SetFloat("_SkinDiffuseSoftnessEnabled", skin.NormalSoftness > 0f ? 1 : 0);
                        m.SetFloat("_SssBumpBlur", skin.NormalSoftness > 0f ? skin.NormalSoftness : 0.7f);
                        m.SetFloat("_LightingIgnoreAmbientColor", skin.ConstantShadow);
                        m.SetFloat("_ShadowStrength", skin.Strength);
                        return 3;
                    }
                    // Realistic: Lambert, the light × lerp(1, light map, strength), tinted in shadow.
                    m.SetFloat("_ShadowStrength", l.ShadowStrength);
                    m.SetColor("_LightingShadowColor", l.ShadowTint.gamma);
                    return 6;
            }
            if (ramp.Term == AtlasShadingTerm.SdfFace && ramp.Sdf?.Style == AtlasSdfStyle.Threshold)
            {
                // SDF: the face's texture by threshold, mirrored by the light's side; the shadow
                // as most types share it.
                TextureOn(m, "_SDFShadingTexture", ramp.Sdf.Map);
                m.SetVector("_SDFForward", ramp.Sdf.Forward);
                m.SetVector("_SDFLeft", ramp.Sdf.Left);
                m.SetFloat("_SDFBlur", ramp.Sdf.Blur);
                m.SetColor("_LightingShadowColor", ramp.ShadowTint.Color.gamma);
                m.SetFloat("_LightingIgnoreAmbientColor", ramp.ShadowDirect);
                m.SetFloat("_ShadowStrength", ramp.RampStrength);
                return 8;
            }
            if (ramp.Term == AtlasShadingTerm.Wrapped)
            {
                m.SetFloat("_LightingWrappedWrap", ramp.Wrap);
                m.SetFloat("_LightingWrappedNormalization", ramp.WrapNormalization);
                m.SetColor("_LightingShadowColor", ramp.ShadowTint.Color.gamma);
                m.SetColor("_LightingWrappedColor", ramp.LitTint.gamma);
                m.SetFloat("_ShadowStrength", ramp.RampStrength);
                // The keys from black to white: r = saturate((t − (1 − end)) / (end − start + 0.001)).
                float low = 0f, high = 1f;
                foreach (var key in ramp.Keys)
                {
                    if (key.Color.r <= 0f) low = Mathf.Max(low, key.Position);
                    if (key.Color.r >= 1f) high = Mathf.Min(high, key.Position);
                }
                float end = 1f - low;
                m.SetFloat("_LightingGradientEnd", end);
                m.SetFloat("_LightingGradientStart", end - (high - low) + 0.001f);
                return 2;
            }
            float ignore = ramp.ShadowDirect;
            if (ramp.Bands.Count > 0)
            {
                m.SetFloat("_ShadowStrength", ramp.RampStrength);
                // Shade Map: two steps, both sides lit by the direct light, the shades occluded.
                if (IsShadeMap(ramp))
                {
                    var first = ramp.Bands[0];
                    var second = ramp.Bands[1];
                    m.SetColor("_1st_ShadeColor", first.Color.Color.gamma);
                    m.SetFloat("_BaseShade_Feather", first.Blur);
                    m.SetFloat("_BaseColor_Step", first.Border + first.Blur * 0.5f);
                    m.SetFloat("_Use_BaseAs1st", first.ColorFromBase ? 1 : 0);
                    TextureOn(m, "_1st_ShadeMap", first.Color.Map);
                    m.SetColor("_2nd_ShadeColor", second.Color.Color.gamma);
                    m.SetFloat("_1st2nd_Shades_Feather", second.Blur);
                    m.SetFloat("_ShadeColor_Step", second.Border + second.Blur * 0.5f);
                    m.SetFloat("_Use_1stAs2nd", 0);
                    TextureOn(m, "_2nd_ShadeMap", second.Color.Map);
                    m.SetFloat("_ShadingShadeMapBlendType", ramp.BandsReplaceBase ? 0 : 1);
                    return 4;
                }
                // Multilayer Math: up to three bands.
                string[] prefixes = { "_Shadow", "_Shadow2nd", "_Shadow3rd" };
                for (int k = 0; k < prefixes.Length; k++)
                {
                    var band = k < ramp.Bands.Count ? ramp.Bands[k] : null;
                    var color = band != null ? band.Color.Color : Color.clear;
                    m.SetColor(prefixes[k] + "Color", new Color(color.r, color.g, color.b, band != null ? band.Alpha : 0f).gamma);
                    TextureOn(m, prefixes[k] + "ColorTex", band?.Color.Map);
                    if (band == null) continue;
                    m.SetFloat(prefixes[k] + "ColorTexAddition", band.ColorAddition);
                    m.SetFloat(prefixes[k] + "Border", band.Border);
                    m.SetFloat(prefixes[k] + "Blur", band.Blur);
                    m.SetFloat(prefixes[k] + "NormalStrength", band.NormalStrength);
                    m.SetFloat(prefixes[k] + "Receive", band.ReceiveShadows);
                }
                m.SetFloat("_LightingMulitlayerNonLinear", ramp.SmoothEdges ? 1 : 0);
                m.SetColor("_ShadowBorderColor", new Color(ramp.BorderColor.r, ramp.BorderColor.g, ramp.BorderColor.b, 1f).gamma);
                m.SetFloat("_ShadowBorderRange", ramp.BorderRange);
                m.SetFloat("_ShadowEnvStrength", ramp.EnvironmentStrength);
                m.SetFloat("_ShadowMainStrength", ramp.ShadowColorFromBase);
                m.SetFloat("_LightingIgnoreAmbientColor", ignore);
                m.SetFloat("_ShadowMaskType", 0);
                m.SetTexture("_ShadowStrengthMask", null);
                return 1;
            }
            // Texture Ramp: the ramp by the light map, Shadow Tint × lerp(ambient, r × direct, ignore).
            m.SetTexture("_ToonRamp", ramp.Ramp?.Texture);
            m.SetFloat("_ShadowOffset", ramp.Offset);
            m.SetFloat("_ShadowStrength", ramp.RampStrength);
            m.SetColor("_LightingShadowColor", ramp.ShadowTint.Color.gamma);
            m.SetFloat("_LightingIgnoreAmbientColor", ignore);
            return 0;
        }

        // UV Tile Discard (vertex mode: a triangle with a vertex in a hidden tile is hidden; never
        // outside the 4 × 4 grid) for both sides, and Face Discard for one side, which needs the
        // first one's keyword.
        private static void WriteTileDiscard(List<AtlasTileDiscard> hidden, Material material)
        {
            var both = hidden.FirstOrDefault(t => t.Faces == AtlasFaces.Both);
            var face = hidden.FirstOrDefault(t => t.Faces != AtlasFaces.Both);
            bool on = both != null || face != null;
            material.SetFloat("_EnableUDIMDiscardOptions", on ? 1 : 0);
            if (on) material.EnableKeyword("POI_UDIMDISCARD");
            else material.DisableKeyword("POI_UDIMDISCARD");
            material.SetFloat("_UDIMDiscardUV", on ? Mathf.Clamp((int)((both ?? face).Space - AtlasTextureSpace.UV0), 0, 3) : 0);
            material.SetFloat("_UDIMDiscardMode", 0);
            Rows(material, "_UDIMDiscardRow", both?.Tiles ?? 0);
            material.SetFloat("_EnableUDIMFaceDiscardOptions", face != null ? 1 : 0);
            if (face == null) return;
            material.SetFloat("_UDIMFaceDiscardUV", Mathf.Clamp((int)(face.Space - AtlasTextureSpace.UV0), 0, 3));
            // 0 discards back faces, 1 front faces.
            material.SetFloat("_UDIMFaceDiscardFace", face.Faces == AtlasFaces.Front ? 1 : 0);
            Rows(material, "_UDIMFaceDiscardRow", face.Tiles);
        }

        // Bit row × 4 + column into _…Row{row}_{column}.
        private static void Rows(Material material, string prefix, int tiles)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    material.SetFloat(prefix + row + "_" + column, ((tiles >> (row * 4 + column)) & 1) != 0 ? 1 : 0);
        }

        private static void WriteSurfaceResponse(AtlasSurfaceResponse response, Material material)
        {
            var packed = response.Metallic.Map?.Texture;
            material.SetFloat("_MochieBRDF", packed != null ? 1 : 0);
            if (packed != null)
            {
                material.EnableKeyword("MOCHIE_PBR");
                material.SetTexture("_MochieMetallicMaps", packed);
                material.SetTextureScale("_MochieMetallicMaps", Vector2.one);
                material.SetTextureOffset("_MochieMetallicMaps", Vector2.zero);
                material.SetFloat("_MochieMetallicMapsUV", 0);
                material.SetFloat("_MochieMetallicMultiplier", 1);
                material.SetFloat("_MochieRoughnessMultiplier", 1);
                material.SetFloat("_MochieMetallicMapsMetallicChannel", 0);
                material.SetFloat("_MochieMetallicMapsRoughnessChannel", 1);
                material.SetFloat("_MochieMetallicMapsReflectionMaskChannel", response.Reflections.Mask != null ? 2 : 4);
                material.SetFloat("_MochieMetallicMapsSpecularMaskChannel", response.Highlights.Mask != null ? 3 : 4);
                material.SetFloat("_MochieMetallicMapInvert", 0);
                material.SetFloat("_MochieRoughnessMapInvert", 0);
                material.SetFloat("_MochieReflectionMaskInvert", 0);
                material.SetFloat("_MochieSpecularMaskInvert", 0);
                material.SetFloat("_MochieReflectionStrength", response.Reflections.Enabled ? Mathf.Clamp01(response.Reflections.Strength) : 0f);
                material.SetFloat("_MochieSpecularStrength", response.Highlights.Enabled ? Mathf.Clamp(response.Highlights.Strength, 0f, 5f) : 0f);
            }
            else material.DisableKeyword("MOCHIE_PBR");

            var occlusion = response.Occlusion.Map?.Texture;
            material.SetTexture("_LightingAOMaps", occlusion);
            if (occlusion != null)
            {
                material.EnableKeyword("PROP_LIGHTINGAOMAPS");
                material.SetTextureScale("_LightingAOMaps", Vector2.one);
                material.SetTextureOffset("_LightingAOMaps", Vector2.zero);
                material.SetFloat("_LightingAOMapsUV", 0);
                material.SetFloat("_LightDataAOStrengthR", Mathf.Clamp01(response.Occlusion.Constant));
                material.SetFloat("_LightDataAOStrengthG", 0);
                material.SetFloat("_LightDataAOStrengthB", 0);
                material.SetFloat("_LightDataAOStrengthA", 0);
            }
            else material.DisableKeyword("PROP_LIGHTINGAOMAPS");
        }

        // VRChat's fallback tag, as Thry's editor writes it: a shader type, the render type
        // (none for opaque) and DoubleSided when nothing is culled; or Hidden. A fade that writes
        // depth falls back to cutout, which keeps its solid parts solid.
        public static string Fallback(AtlasRender render)
        {
            if (render.Fallback == AtlasFallbackShader.Hidden) return "Hidden";
            string type = render.Fallback == AtlasFallbackShader.Unlit ? "Unlit"
                : render.Fallback == AtlasFallbackShader.Standard ? "Standard" : "Toon";
            string mode = render.SurfaceMode == AtlasSurfaceMode.Cutout || render.SurfaceMode == AtlasSurfaceMode.Dithered ? "Cutout"
                : render.SurfaceMode == AtlasSurfaceMode.Fade ? (render.Pass.DepthWrite ? "Cutout" : "Fade")
                : render.SurfaceMode == AtlasSurfaceMode.Transparent ? "Transparent" : "";
            return type + mode + (render.Pass.Cull == AtlasCull.Off ? "DoubleSided" : "");
        }
    }
    public sealed class VRChatMobileToonOutputShader : IAtlasOutputShader
    {
        public string ShaderId => "mobiletoon";
        public string DisplayName => "VRChat Mobile Toon Standard";
        public string Family => AtlasShaderFamilies.VRChat;
        public int Rank => 10;
        public Shader FindShader() => Shader.Find("VRChat/Mobile/Toon Standard");
        public AtlasOutputCapabilities Capabilities { get; } = new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB),
                PoiyomiToon10OutputShader.NormalTexture(),
                // One texture for _MetallicMap (R), _GlossMap (G, smoothness) and _OcclusionMap (B).
                new AtlasOutputTexture("Surface", "Metallic, gloss and occlusion") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Metallic, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.Roughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                    .With(AtlasVirtualMap.Occlusion, AtlasTextureChannels.B, AtlasOutputTexture.Gray(1f)),
                PoiyomiToon10OutputShader.EmissionTexture()
            },
            Reflectance = AtlasReflectanceSupport.FollowsSmoothness,
            // _VertexColor: the base color × the vertex RGB as stored.
            VertexColors = AtlasVertexColorSupport.Color,
            // _Ramp is its only shading control.
            GeneratedRamp = true
        };

        // Lighting: Toon Standard's one model, base × (lerp(base × Shadow Tint, 1, min(1, ramp(N·L
        // × 0.5 + 0.5) + Shadow Boost)) × direct + ambient). The ramp is the source's texture, or
        // the SDK ramp nearest to its family.
        private static readonly (AtlasRampPreset Preset, string Texture)[] RampTextures =
        {
            (AtlasRampPreset.Flat, "ShadowRampFlat"), (AtlasRampPreset.Realistic, "ShadowRampRealistic"),
            (AtlasRampPreset.RealisticSoft, "ShadowRampRealisticSoft"), (AtlasRampPreset.RealisticVerySoft, "ShadowRampRealisticVerySoft"),
            (AtlasRampPreset.TwoBands, "ShadowRampToon2Band"), (AtlasRampPreset.ThreeBands, "ShadowRampToon3Band"),
            (AtlasRampPreset.FourBands, "ShadowRampToon4Band")
        };

        private static void WriteLighting(AtlasLighting l, Material m)
        {
            var ramp = l.Ramp;
            var texture = ramp.Ramp?.Texture;
            if (texture == null)
            {
                var preset = ramp.Preset != AtlasRampPreset.None ? ramp.Preset
                    : l.Family == AtlasLightingFamily.PhysicallyBased ? AtlasRampPreset.Realistic
                    : l.Family == AtlasLightingFamily.RampToon ? AtlasRampPreset.None : AtlasRampPreset.Flat;
                texture = PresetTexture(preset);
            }
            m.SetTexture("_Ramp", texture);
            m.SetFloat("_ShadowBoost", Mathf.Clamp01(ramp.ShadowLift));
            // Banded shading keeps its shadow color in the first band.
            var tint = ramp.Bands.Count > 0 ? ramp.Bands[0].Color.Color : ramp.ShadowTint.Color;
            m.SetFloat("_ShadowAlbedo", Mathf.Clamp01(tint.r * 0.2126f + tint.g * 0.7152f + tint.b * 0.0722f));
            m.SetFloat("_MinBrightness", Mathf.Clamp(l.MinimumBrightness, 0f, 0.1f));
            m.SetFloat("_LimitBrightness", l.LimitBrightness ? 1 : 0);
        }

        // The VRChat SDK's ramp texture for a preset; null: none (white, fully lit).
        private static Texture PresetTexture(AtlasRampPreset preset)
        {
            foreach (var (candidate, name) in RampTextures)
            {
                if (candidate != preset) continue;
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets(name + " t:Texture2D"))
                {
                    var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (texture != null && texture.name == name) return texture;
                }
            }
            return null;
        }

        // Toon Standard: _Color (sRGB-authored) times _MainTex; the normal map, behind its
        // USE_NORMAL_MAPS keyword. It's opaque only, and a VRChat shader, so it needs no fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.SetFloat("_VertexColor", output.Surface.VertexColor.MultiplyColor ? 1 : 0);
            WriteLighting(output.Lighting, material);
            var normal = output.Surface.Normal;
            if (normal.Map?.Texture != null)
            {
                material.EnableKeyword("USE_NORMAL_MAPS");
                material.SetTexture("_BumpMap", normal.Map.Texture);
                material.SetFloat("_BumpScale", normal.Strength);
                material.SetTextureScale("_BumpMap", Vector2.one);
                material.SetTextureOffset("_BumpMap", Vector2.zero);
            }
            else material.DisableKeyword("USE_NORMAL_MAPS");

            // USE_SPECULAR: metallic = _MetallicMap's channel × strength, smoothness = _GlossMap's ×
            // strength, reflectance = gloss × _Reflectance. USE_OCCLUSION_MAP: lerp(1, channel, strength).
            var response = output.Surface.Response;
            var surface = response.Metallic.Map?.Texture;
            if (surface != null)
            {
                material.EnableKeyword("USE_SPECULAR");
                material.SetTexture("_MetallicMap", surface);
                material.SetFloat("_MetallicMapChannel", 0);
                material.SetFloat("_MetallicStrength", 1);
                material.SetTexture("_GlossMap", response.Roughness.Map?.Texture ?? surface);
                material.SetFloat("_GlossMapChannel", 1);
                material.SetFloat("_GlossStrength", 1);
                material.SetFloat("_Reflectance", Mathf.Clamp01(response.Reflectance.Constant));
            }
            else material.DisableKeyword("USE_SPECULAR");
            var occlusion = response.Occlusion.Map?.Texture;
            if (occlusion != null)
            {
                material.EnableKeyword("USE_OCCLUSION_MAP");
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionMapChannel", 2);
                material.SetFloat("_OcclusionStrength", Mathf.Clamp01(response.Occlusion.Constant));
            }
            else material.DisableKeyword("USE_OCCLUSION_MAP");

            // Emission: _EmissionColor (set from the linear color's gamma value) × _EmissionStrength
            // (0–2; above that the rest goes into the color) × _EmissionMap on UV0.
            var emission = AtlasEmissionFeature.Of(output);
            var emissionMap = emission?.Source.Map?.Texture;
            material.SetTexture("_EmissionMap", emissionMap);
            if (emissionMap != null)
            {
                float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
                float strength = Mathf.Min(total, 2f);
                float rest = total / Mathf.Max(strength, 1e-4f);
                material.SetColor("_EmissionColor", new Color(rest, rest, rest, 1f).gamma);
                material.SetFloat("_EmissionStrength", strength);
                material.SetFloat("_EmissionUV", 0);
                material.SetTextureScale("_EmissionMap", Vector2.one);
                material.SetTextureOffset("_EmissionMap", Vector2.zero);
            }
            else material.SetColor("_EmissionColor", Color.black);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
