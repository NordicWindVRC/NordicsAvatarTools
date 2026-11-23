using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Light Data and Shading (the lighting type).
    internal sealed partial class AtlasPoiyomiReader
    {
        // _LightingMode, in the keyword enum's order.
        private static readonly string[] LightingTypes =
            { "TextureRamp", "Multilayer Math", "Wrapped", "Skin", "ShadeMap", "Flat", "Realistic", "Cloth", "SDF", "PBR" };

        private void Lighting(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            LightData(m, d);
            int type = Int(m, "_LightingMode", 5);
            // 7.3: Toon (by its ramp type: texture, math gradient, shade map), Realistic, Wrapped,
            // Skin, Flat.
            if (major <= 7)
            {
                int ramp = Int(m, "_LightingRampType", 0);
                type = type == 0 ? (ramp == 1 ? 1 : ramp == 2 ? 4 : 0) : type == 1 ? 6 : type == 4 ? 5 : type;
            }
            switch (type)
            {
                case 0: TextureRamp(m, d); break;
                case 1: MultilayerMath(m, d); break;
                case 2: Wrapped(m, d); break;
                case 3: Skin(m, d); break;
                case 4: ShadeMap(m, d); break;
                case 5: lighting.Family = AtlasLightingFamily.Flat; break;
                case 6:
                    // Lambert, the light × lerp(1, light map, strength), tinted toward Shadow Tint in shadow.
                    lighting.Family = AtlasLightingFamily.PhysicallyBased;
                    lighting.Diffuse = AtlasDiffuseModel.Lambert;
                    lighting.ShadowStrength = Float(m, "_ShadowStrength", 1f);
                    lighting.ShadowTint = LinearColor(m, "_LightingShadowColor", Color.white);
                    break;
                case 7: Cloth(m, d); break;
                case 8: Sdf(m, d); break;
                case 9: Pbr(m, d); break;
                default:
                    string name = type >= 0 && type < LightingTypes.Length ? LightingTypes[type] : type.ToString();
                    d.Report(AtlasIssueKind.Unmapped, "lighting.type", "The " + name + " lighting type isn't mapped yet.");
                    break;
            }
            lighting.FlatLightMap = lighting.Family == AtlasLightingFamily.Flat && On(m, "_ForceFlatRampedLightmap", 1f);
            if (!On(m, "_ShadingEnabled", 1f))
            {
                lighting.Family = AtlasLightingFamily.Unlit;
                d.Report(AtlasIssueKind.Note, "lighting.shading-off",
                    "With shading off Poiyomi also skips the features applied with the lighting (grab pass, lava lamp, lighting-stage rims); read as unlit.");
            }
            // Shadow Masks scale the shadow strength.
            var shadowMask = PackedMask(m, d, "_LightingShadowMasks", "_LightingShadowMaskStrength", "lighting.shadow-masks");
            shadowMask = BusMask(m, shadowMask, "_LightDataShadowMaskGlobalMaskR", "_LightDataShadowMaskGlobalMaskBlendTypeR");
            if (shadowMask == null) return;
            if (lighting.Family == AtlasLightingFamily.RampToon && lighting.Ramp.RampStrengthMask == null)
                lighting.Ramp.RampStrengthMask = shadowMask;
            else
                d.Report(AtlasIssueKind.Unmapped, "lighting.shadow-masks", "Light Data's shadow masks aren't mapped for this lighting type, or with a second strength mask.");
        }

        // Light Data: where the light's color and direction come from, the light map (the shading
        // term), AO and shadow maps, received shadows, brightness limits, additional lights.
        private void LightData(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            int colorMode = Int(m, "_LightingColorMode", 0);
            lighting.ColorModel = colorMode == 1 ? AtlasLightColorModel.Separate : colorMode == 2 ? AtlasLightColorModel.Floored
                : colorMode == 3 ? AtlasLightColorModel.SplitProbes : AtlasLightColorModel.MergedProbes;
            lighting.FloorIntensity = Float(m, "_Unlit_Intensity", 1f);
            // Standard, without a real-time main light: 60% of the ambient light at the normal
            // stands in for it, and 50% stays ambient (read in 9.3, 10 and 11).
            if (colorMode == 1)
            {
                lighting.Contributions.AmbientAsMainLight = 0.6f;
                lighting.Contributions.AmbientWithoutMainLight = 0.5f;
            }
            if (colorMode == 0)
            {
                // Poi Custom samples the ambient light at the normal × Indirect Uses Normals.
                float directional = Float(m, "_LightingIndirectUsesNormals", 0f);
                lighting.Ambient = directional > 0f ? AtlasAmbientModel.Directional : AtlasAmbientModel.Average;
                lighting.DirectionalAmount = directional > 0f ? directional : 1f;
            }

            int direction = Int(m, "_LightingDirectionMode", 0);
            switch (direction)
            {
                case 1: case 2:
                    lighting.Direction = AtlasLightDirectionModel.Fixed;
                    lighting.DirectionLocal = direction == 1;
                    var fixedDirection = Vector3Of(m, "_LightngForcedDirection", Vector3.zero);
                    lighting.LightDirection = fixedDirection == Vector3.zero ? new Vector3(0.4f, 1f, 0.4f) : fixedDirection;
                    break;
                case 3: lighting.Direction = AtlasLightDirectionModel.ViewFallback; break;
                case 4: lighting.Direction = AtlasLightDirectionModel.SplitProbes; break;
                case 5:
                    lighting.Direction = AtlasLightDirectionModel.View;
                    lighting.ViewYaw = Float(m, "_LightingViewDirOffsetYaw", 0f);
                    lighting.ViewPitch = Float(m, "_LightingViewDirOffsetPitch", 0f);
                    break;
                default: lighting.Direction = AtlasLightDirectionModel.ProbeBlended; break;
            }

            // The light map; Poi Custom falls back to half-Lambert under a forced direction.
            int mapMode = Int(m, "_LightingMapMode", 0);
            if (mapMode == 0 && lighting.Direction == AtlasLightDirectionModel.Fixed) mapMode = 1;
            var ramp = lighting.Ramp;
            switch (mapMode)
            {
                case 0: ramp.Term = AtlasShadingTerm.RelativeBrightness; break;
                case 2: ramp.Term = AtlasShadingTerm.Lambert; break;
                case 3: ramp.Term = AtlasShadingTerm.ShadowsOnly; break;
                case 4:
                    // Without a map the shader uses half-Lambert.
                    var sdfMap = Texture(m, d, "_LightDataSDFMap", AtlasTextureInterpretation.Data);
                    if (sdfMap == null) break;
                    ramp.Term = AtlasShadingTerm.SdfFace;
                    ramp.Sdf = new AtlasSdfFace
                    {
                        Style = AtlasSdfStyle.Blended, Map = sdfMap,
                        VerticalBlend = Float(m, "_LightDataSDFBlendY", 1f), Softness = Float(m, "_LightDataSDFMapLOD", 0f),
                        MaskChannels = On(m, "_LightDataSDFChannelsBA")
                    };
                    break;
            }

            // AO Maps: occlusion; Shadow Map: the detail shadow, multiplying the light map.
            var occlusion = PackedMask(m, d, "_LightingAOMaps", "_LightDataAOStrength", "lighting.ao-maps");
            if (occlusion != null)
            {
                var input = d.Surface.Response.Occlusion;
                input.Map = occlusion.Map;
                input.Channel = occlusion.Channel;
                input.Constant = occlusion.Strength;
            }
            BusScalar(m, d.Surface.Response.Occlusion, "_LightDataAOGlobalMaskR", "_LightDataAOGlobalMaskBlendTypeR");
            ramp.TermMask = PackedMask(m, d, "_LightingDetailShadowMaps", "_LightingDetailShadowStrength", "lighting.detail-shadows");
            ramp.TermMask = BusMask(m, ramp.TermMask, "_LightDataDetailShadowGlobalMaskR", "_LightDataDetailShadowGlobalMaskBlendTypeR");
            if (ramp.TermMask != null)
                foreach (char c in "RGBA")
                    if (Float(m, "_LightingAddDetailShadowStrength" + c, c == 'R' ? 1f : 0f) != Float(m, "_LightingDetailShadowStrength" + c, c == 'R' ? 1f : 0f))
                    {
                        d.Report(AtlasIssueKind.Unmapped, "lighting.additional-detail-shadows", "Different detail shadow strengths for additional lights aren't mapped yet.");
                        break;
                    }

            lighting.ReceiveShadows = Float(m, "_LightingCastedShadows", 0f);
            lighting.ShadowSoftness = Float(m, "_LightingShadowSoftness", 0f);
            lighting.CleanShadows = On(m, "_LightingCleanCastedShadows");
            lighting.Monochrome = Float(m, "_LightingMonochromatic", 0f);
            lighting.LimitBrightness = On(m, "_LightingCapEnabled", 1f);
            lighting.BrightnessCeiling = Float(m, "_LightingCap", 1f);
            lighting.LimitByLuminance = Float(m, "_LightingCapLuminance", 0f);
            lighting.LimitKeepsHue = Int(m, "_LightingCapMode", 0) == 1;
            ramp.BackFaceShadow = Float(m, "_BackfaceForceShadow", 0f);
            lighting.MinimumLight = Float(m, "_LightingMinLightBrightness", 0f);
            lighting.FixedLightColor = On(m, "_LightingForceColorEnabled");
            if (lighting.FixedLightColor)
            {
                lighting.LightColor = TintColor(m, d, "_LightingForcedColor", "_LightingForcedColorThemeIndex");
            }

            lighting.Contributions.VertexLights = On(m, "_LightingVertexLightingEnabled", 1f) ? 1f : 0f;
            lighting.Contributions.AdditionalLights = On(m, "_LightingAdditiveEnable", 1f) ? 1f : 0f;
            var additional = lighting.AdditionalLights;
            int additiveType = Int(m, "_LightingAdditiveType", 3);
            additional.Shading = additiveType == 0 ? AtlasAdditionalLightShading.Lambert
                : additiveType == 1 ? AtlasAdditionalLightShading.Toon : AtlasAdditionalLightShading.SameAsMain;
            additional.GradientStart = Float(m, "_LightingAdditiveGradientStart", 0f);
            additional.GradientEnd = Float(m, "_LightingAdditiveGradientEnd", 0.5f);
            additional.LimitBrightness = On(m, "_LightingAdditiveLimited", 1f);
            additional.BrightnessCeiling = Float(m, "_LightingAdditiveLimit", 1f);
            additional.LimitKeepsHue = Int(m, "_LightingAdditiveLimitMode", 0) == 1;
            additional.ReceiveShadows = Float(m, "_LightingAdditiveCastedShadows", 1f);
            additional.Monochrome = Float(m, "_LightingAdditiveMonochromatic", 0f);
            additional.Passthrough = Float(m, "_LightingAdditivePassthrough", 0f);
            additional.Falloff = AtlasMaterialReader.Vector(m, "_LightingAdditiveAttenCurve", additional.Falloff);
            additional.Directional = !On(m, "_DisableDirectionalInAdd");
            additional.VertexLightsInMirrors = On(m, "_LightingMirrorVertexLightingEnabled", 1f);
            AddPass(m, d);
            // The PP Animations section.
            lighting.LightMultiplier = Float(m, "_PPLightingMultiplier", 1f);
            lighting.LightAddition = Float(m, "_PPLightingAddition", 0f);

            var volumes = lighting.LightVolumes;
            volumes.Enabled = On(m, "_LightingEnableLightVolumes", 1f);
            volumes.SpecularStrength = Float(m, "_LightingLightVolumesSpecular", 1f);
            volumes.Specular = volumes.SpecularStrength > 0f;
            volumes.Bias = Float(m, "_LightingLightVolumesNormalBias", 0f);
            volumes.SpecularMaxLights = Int(m, "_LightingLightVolumesSpecularMaxLights", 0);
        }

        // Poiyomi 11's PBR lighting type: Filament-style shading of one packed map (metallic,
        // occlusion, smoothness by channel), with its own clear coat, anisotropy, sheen and glints.
        // It replaces Reflections & Specular and Clear Coat.
        private void Pbr(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            lighting.Family = AtlasLightingFamily.PhysicallyBased;
            lighting.Diffuse = AtlasDiffuseModel.Lambert;
            lighting.PhysicalModel = AtlasPhysicalModel.Calibrated;
            var response = d.Surface.Response;
            var map = Texture(m, d, "_PoiPBRMaskMap", AtlasTextureInterpretation.Data);
            ReportPackedOptions(m, d, "_PoiPBRMaskMap");
            Packed(response.Metallic, map, PackedChannel(m, "_PoiPBRMetallicChannel", 0), Float(m, "_PoiPBRMetallic", 0f), false);
            Packed(response.Roughness, map, PackedChannel(m, "_PoiPBRSmoothnessChannel", 3), Float(m, "_PoiPBRSmoothness", 0.5f), true);
            response.Reflectance.Constant = Float(m, "_PoiPBRReflectance", 0.5f);
            // Occlusion: lerp(1, channel, strength) × Light Data's AO.
            var occlusion = PackedChannel(m, "_PoiPBROcclusionChannel", 1);
            if (map != null && occlusion.HasValue)
            {
                if (response.Occlusion.Map != null)
                    d.Report(AtlasIssueKind.Unmapped, "pbr.occlusion", "The PBR occlusion map together with Light Data's AO map isn't mapped yet; the PBR map is used.");
                response.Occlusion.Map = map;
                response.Occlusion.Channel = occlusion.Value;
                response.Occlusion.Constant = Float(m, "_PoiPBROcclusionStrength", 1f);
            }
            response.MultiBounceOcclusion = On(m, "_PoiPBRMultiBounceAO");
            BusScalar(m, response.Metallic, "_PoiPBRMetallicGlobalMask");
            BusScalar(m, response.Roughness, "_PoiPBRSmoothnessGlobalMask");
            BusScalar(m, response.Occlusion, "_PoiPBROcclusionGlobalMask");
            response.Highlights.Enabled = true;
            response.Reflections.Enabled = true;
            response.SpecularAntiAliasing.Enabled = On(m, "_PoiPBRGSAA", 1f);
            response.SpecularAntiAliasing.Variance = Float(m, "_PoiPBRGSAAVariance", 0.15f);
            response.SpecularAntiAliasing.Threshold = Float(m, "_PoiPBRGSAAThreshold", 0.2f);

            float coat = Float(m, "_PoiPBRClearCoat", 0f);
            if (coat > 0f)
            {
                var clearCoat = new AtlasClearCoat { NormalMapAmount = 1f };
                clearCoat.Strength.Constant = coat;
                clearCoat.Roughness.Constant = 1f - Float(m, "_PoiPBRClearCoatSmoothness", 0.9f);
                clearCoat.SpecularAntiAliasing.Enabled = response.SpecularAntiAliasing.Enabled;
                clearCoat.SpecularAntiAliasing.Variance = response.SpecularAntiAliasing.Variance;
                clearCoat.SpecularAntiAliasing.Threshold = response.SpecularAntiAliasing.Threshold;
                response.ClearCoat = clearCoat;
            }

            float anisotropy = Float(m, "_PoiPBRAnisotropy", 0f);
            if (anisotropy != 0f)
            {
                var stretch = response.Anisotropy;
                var channel = PackedChannel(m, "_PoiPBRAnisotropyChannel", 4);
                stretch.Enabled = true;
                stretch.Amount = anisotropy;
                stretch.MapMultiplies = true;
                stretch.Map = channel.HasValue ? map : null;
                if (channel.HasValue) stretch.Channel = channel.Value;
                stretch.Rotation = Float(m, "_PoiPBRAnisotropyRotation", 0f);
            }

            float sheen = Float(m, "_PoiPBRSheen", 0f);
            if (sheen > 0f)
            {
                var layer = new AtlasSheen
                {
                    Color = LinearColor(m, "_PoiPBRSheenColor", Color.white),
                    Roughness = Mathf.Clamp(1f - Float(m, "_PoiPBRSheenSmoothness", 0.5f), 0.07f, 1f)
                };
                Packed(layer.Strength, map, PackedChannel(m, "_PoiPBRSheenChannel", 4), sheen, false);
                response.Sheen = layer;
            }

            if (On(m, "_PoiPBRGlints"))
            {
                var channel = PackedChannel(m, "_PoiPBRGlintMaskChannel", 4);
                response.Glints = new AtlasGlints
                {
                    Space = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, "_PoiPBRGlintUV", 0), 0, 3),
                    Mask = BusMask(m, channel.HasValue && map != null ? new AtlasMask { Map = map, Channel = channel.Value } : null, "_PoiPBRGlintGlobalMask"),
                    Color = LinearColor(m, "_PoiPBRGlintColor", new Color(0.95f, 0.95f, 0.95f, 1f)),
                    BaseReflectanceMix = Float(m, "_PoiPBRGlintUseBaseColor", 0f),
                    ColorVariation = Float(m, "_PoiPBRGlintColorVariation", 0f),
                    Roughness = 1f - Float(m, "_PoiPBRGlintSmoothness", 0.5f),
                    Density = Float(m, "_PoiPBRGlintDensity", 2000f),
                    FlakeRoughness = Float(m, "_PoiPBRGlintMicroRoughness", 0.02f),
                    Size = Float(m, "_PoiPBRGlintSize", 0f),
                    EyeBlend = Float(m, "_PoiPBRGlintEyeBlend", 0f),
                    Ambient = Float(m, "_PoiPBRGlintAmbient", 0f),
                    VertexLights = On(m, "_PoiPBRGlintVertexLights")
                };
            }
        }

        // Mask options on a packed map feeding scalar inputs, which don't carry levels yet.
        private void ReportPackedOptions(Material m, AtlasUnifiedMaterial d, string property)
        {
            if (AtlasMaterialReader.Vector(m, property + "VertexMask", Vector4.zero) != Vector4.zero
                || AtlasMaterialReader.Vector(m, property + "MLInputMin", Vector4.zero) != Vector4.zero
                || AtlasMaterialReader.Vector(m, property + "MLInputMax", Vector4.one) != Vector4.one
                || AtlasMaterialReader.Vector(m, property + "MLMidpoint", Vector4.one * 0.5f) != Vector4.one * 0.5f
                || AtlasMaterialReader.Vector(m, property + "MLOutputMin", Vector4.zero) != Vector4.zero
                || AtlasMaterialReader.Vector(m, property + "MLOutputMax", Vector4.one) != Vector4.one
                || AtlasMaterialReader.Vector(m, property + "MLInvert", Vector4.zero) != Vector4.zero)
                d.Report(AtlasIssueKind.Unmapped, "mask.levels-packed", "Mask levels and vertex color sources on a map that isn't a plain mask aren't mapped yet.");
        }

        // A packed RGBA map whose channels each apply by a strength (<prefix>R … A): the channel in
        // use, as a mask with its strength. More than one channel in use is reported.
        private AtlasMask PackedMask(Material m, AtlasUnifiedMaterial d, string property, string strengthPrefix, string feature)
        {
            var map = Texture(m, d, property, AtlasTextureInterpretation.Data);
            if (map == null) return null;
            AtlasMask mask = null;
            for (int c = 0; c < 4; c++)
            {
                float strength = Float(m, strengthPrefix + "RGBA"[c], c == 0 ? 1f : 0f);
                if (strength <= 0f) continue;
                if (mask != null)
                {
                    d.Report(AtlasIssueKind.Unmapped, feature, "More than one channel of a packed " + property + " map isn't mapped yet; the first is used.");
                    break;
                }
                mask = MaskOptions(m, new AtlasMask { Map = map, Channel = Channels[c], Strength = strength }, property, c);
            }
            return mask;
        }

        // final = lerp(Shadow Tint × lerp(ambient, r × direct, ignore) × occlusion, direct, r),
        // r = lerp(1, ramp(term + offset), Shadow Strength).
        private void TextureRamp(Material m, AtlasUnifiedMaterial d)
        {
            d.Lighting.Family = AtlasLightingFamily.RampToon;
            var ramp = d.Lighting.Ramp;
            ramp.Ramp = AtlasSourceMapping.Texture(m, "_ToonRamp", AtlasTextureInterpretation.Color);
            ramp.Offset = Float(m, "_ShadowOffset", 0f);
            ramp.RampStrength = Float(m, "_ShadowStrength", 1f);
            ramp.ShadowTint.Color = LinearColor(m, "_LightingShadowColor", Color.white);
            float ignore = Float(m, "_LightingIgnoreAmbientColor", 1f);
            ramp.ShadowDirect = ignore;
            ramp.RampScaledShadowDirect = true;
            ramp.DirectOccluded = true;
            ramp.ShadowAmbient = 1f - ignore;
            ramp.LitAmbient = 0f;
            if (Int(m, "_ToonRampCount", 1) > 1)
                d.Report(AtlasIssueKind.Unmapped, "lighting.ramp-rows", "A ramp with several rows picked by UV isn't mapped yet.");
        }

        // The shadow side as most types share it: Shadow Tint × lerp(ambient, direct, ignore).
        private void ToonShadow(Material m, AtlasRampToon ramp)
        {
            ramp.ShadowTint.Color = LinearColor(m, "_LightingShadowColor", Color.white);
            float ignore = Float(m, "_LightingIgnoreAmbientColor", 1f);
            ramp.ShadowDirect = ignore;
            ramp.ShadowAmbient = 1f - ignore;
            ramp.LitAmbient = 0f;
            ramp.RampStrength = Float(m, "_ShadowStrength", 1f);
        }

        // A face shaded by an SDF texture's threshold.
        private void Sdf(Material m, AtlasUnifiedMaterial d)
        {
            d.Lighting.Family = AtlasLightingFamily.RampToon;
            var ramp = d.Lighting.Ramp;
            ToonShadow(m, ramp);
            ramp.Term = AtlasShadingTerm.SdfFace;
            ramp.Sdf = new AtlasSdfFace
            {
                Map = Texture(m, d, "_SDFShadingTexture", AtlasTextureInterpretation.Data),
                Forward = Vector3Of(m, "_SDFForward", Vector3.forward),
                Left = Vector3Of(m, "_SDFLeft", Vector3.left),
                Blur = Float(m, "_SDFBlur", 0.1f)
            };
        }

        // (direct × wrap(N·L) × Lit Side Color + wrapped ambient) × lerp(Shadow Tint, 1, r),
        // r a linear gradient of the wrapped light's relative brightness.
        private void Wrapped(Material m, AtlasUnifiedMaterial d)
        {
            d.Lighting.Family = AtlasLightingFamily.RampToon;
            var ramp = d.Lighting.Ramp;
            ramp.Term = AtlasShadingTerm.Wrapped;
            ramp.Wrap = Float(m, "_LightingWrappedWrap", 0f);
            ramp.WrapNormalization = Float(m, "_LightingWrappedNormalization", 0f);
            ramp.ShadowTint.Color = LinearColor(m, "_LightingShadowColor", Color.white);
            ramp.LitTint = LinearColor(m, "_LightingWrappedColor", Color.white);
            ramp.RampStrength = Float(m, "_ShadowStrength", 1f);
            // r = saturate((t − (1 − end)) / saturate(end − start + 0.001)), as keys from black to white.
            float start = Float(m, "_LightingGradientStart", 0f), end = Float(m, "_LightingGradientEnd", 0.5f);
            float low = 1f - end, width = Mathf.Max(Mathf.Clamp01(end - start + 0.001f), 1e-4f);
            foreach (float position in new[] { 0f, Mathf.Clamp01(low), Mathf.Clamp01(low + width), 1f })
            {
                if (ramp.Keys.Exists(k => Mathf.Approximately(k.Position, position))) continue;
                float value = Mathf.Clamp01((position - low) / width);
                ramp.Keys.Add(new AtlasRampKey(position, new Color(value, value, value, 1f)));
            }
        }

        // Unity-Chan style: the base color toward the 1st and 2nd shade colors by two steps.
        private void ShadeMap(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            lighting.Family = AtlasLightingFamily.RampToon;
            var ramp = lighting.Ramp;
            var first = ShadeBand(m, "_1st_ShadeColor", "_BaseColor_Step", 0.5f, "_BaseShade_Feather", lighting.ReceiveShadows);
            first.ColorFromBase = On(m, "_Use_BaseAs1st");
            if (!first.ColorFromBase) first.Color.Map = Texture(m, d, "_1st_ShadeMap");
            var second = ShadeBand(m, "_2nd_ShadeColor", "_ShadeColor_Step", 0f, "_1st2nd_Shades_Feather", lighting.ReceiveShadows);
            if (On(m, "_Use_1stAs2nd")) { second.ColorFromBase = first.ColorFromBase; second.Color.Map = first.Color.Map; }
            else second.Color.Map = Texture(m, d, "_2nd_ShadeMap");
            ramp.Bands.Add(first);
            ramp.Bands.Add(second);
            ramp.BandsReplaceBase = Int(m, "_ShadingShadeMapBlendType", 0) == 0;
            ramp.RampStrength = Float(m, "_ShadowStrength", 1f);
            // Both sides are lit by the direct light; the shade colors are occluded.
            ramp.ShadowDirect = 1f;
            ramp.DirectOccluded = true;
            ramp.ShadowAmbient = 0f;
            ramp.LitAmbient = 0f;
            if (On(m, "_Use_1stShadeMapAlpha_As_ShadowMask") || On(m, "_Use_2ndShadeMapAlpha_As_ShadowMask"))
                d.Report(AtlasIssueKind.Unmapped, "lighting.shade-map-alpha", "A shade map's alpha as a shadow mask isn't mapped yet.");
        }

        // A shade map step: mask = saturate(1 − (t − (step − feather)) / feather).
        private AtlasShadeBand ShadeBand(Material m, string color, string step, float stepFallback, string feather, float receiveShadows)
        {
            var band = new AtlasShadeBand();
            var c = LinearColor(m, color, Color.white);
            band.Color.Color = new Color(c.r, c.g, c.b, 1f);
            float width = Float(m, feather, 0.0001f);
            band.Border = Float(m, step, stepFallback) - width * 0.5f;
            band.Blur = width;
            band.ReceiveShadows = receiveShadows;
            return band;
        }

        // Pre-integrated skin: a lookup by half-Lambert and scattering.
        private void Skin(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            lighting.Family = AtlasLightingFamily.PhysicallyBased;
            lighting.Diffuse = AtlasDiffuseModel.PreintegratedSkin;
            lighting.ShadowTint = LinearColor(m, "_LightingShadowColor", Color.white);
            var skin = lighting.Skin;
            skin.Lut = AtlasSourceMapping.Texture(m, "_SkinLUT", AtlasTextureInterpretation.Data);
            skin.Scattering = Float(m, "_SssScale", 1f);
            skin.Thickness = Mask(m, d, "_SkinThicknessMap");
            if (skin.Thickness != null) skin.Thickness.Invert = On(m, "_SkinThicknessMapInvert");
            skin.ThicknessPower = Float(m, "_SkinThicknessPower", 1f);
            skin.Absorption = Vector3Of(m, "_SssTransmissionAbsorption", skin.Absorption);
            skin.Stability = Float(m, "_SkinScatteringStability", 0f);
            skin.NormalSoftness = On(m, "_SkinDiffuseSoftnessEnabled") ? Float(m, "_SssBumpBlur", 0.7f) : 0f;
            skin.ConstantShadow = Float(m, "_LightingIgnoreAmbientColor", 1f);
            skin.Strength = Float(m, "_ShadowStrength", 1f);
        }

        // Cloth: Burley diffuse, sheen or GGX highlights by the cloth map's mask, with its own
        // metallic (R), mask (G), reflectance (B) and smoothness (A).
        private void Cloth(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            lighting.Family = AtlasLightingFamily.PhysicallyBased;
            lighting.Diffuse = AtlasDiffuseModel.Disney;
            lighting.DisneyAmount = 1f;
            var map = Texture(m, d, "_ClothMetallicSmoothnessMap", AtlasTextureInterpretation.Data);
            ReportPackedOptions(m, d, "_ClothMetallicSmoothnessMap");
            var response = d.Surface.Response;
            // metallic = (R × Metallic)²: exact without a map; with one, the map is read linearly.
            float metallic = Float(m, "_ClothMetallic", 0f);
            response.Metallic.Constant = metallic * metallic;
            response.Metallic.Map = map;
            response.Metallic.Channel = AtlasChannel.Red;
            if (map != null && metallic > 0f)
                d.Report(AtlasIssueKind.Note, "lighting.cloth-metallic", "Cloth squares its metallic map; it's read linearly.");
            response.Roughness.Constant = Float(m, "_ClothSmoothness", 0.5f);
            response.Roughness.Map = map;
            response.Roughness.Channel = AtlasChannel.Alpha;
            response.Roughness.InvertSample = On(m, "_ClothMetallicSmoothnessMapInvert");
            response.Roughness.InvertResult = true;
            response.Reflectance.Constant = Float(m, "_ClothReflectance", 0.5f);
            response.Reflectance.Map = map;
            response.Reflectance.Channel = AtlasChannel.Blue;
            response.Highlights.Enabled = true;
            response.Reflections.Enabled = true;
            lighting.Cloth = new AtlasCloth
            {
                Mask = map == null ? null : new AtlasMask { Map = map, Channel = AtlasChannel.Green },
                Blend = On(m, "_ClothLerp")
            };
            BusScalar(m, response.Metallic, "_ClothMetallicGlobalMask");
            BusScalar(m, response.Reflectance, "_ClothReflectanceGlobalMask");
            BusScalar(m, response.Roughness, "_ClothSmoothnessGlobalMask");
            lighting.Cloth.Mask = BusMask(m, lighting.Cloth.Mask, "_ClothMaskGlobalMask");
        }

        // Up to three bands: the first gives the light map, the others shade the shadow color.
        private void MultilayerMath(Material m, AtlasUnifiedMaterial d)
        {
            d.Lighting.Family = AtlasLightingFamily.RampToon;
            var ramp = d.Lighting.Ramp;
            // The bands build their own half-Lambert terms; Light Data's light map stays the
            // term that specular and light masks read.
            if (Int(m, "_LightingMapMode", 0) != 4) ramp.BandsOwnTerm = true;
            ramp.Bands.Add(Band(m, d, "_Shadow"));
            foreach (string prefix in new[] { "_Shadow2nd", "_Shadow3rd" })
                if (LinearColor(m, prefix + "Color", Color.clear).a > 0f) ramp.Bands.Add(Band(m, d, prefix));
            ramp.SmoothEdges = On(m, "_LightingMulitlayerNonLinear", 1f);
            var border = LinearColor(m, "_ShadowBorderColor", new Color(1f, 0.1f, 0f, 1f));
            ramp.BorderColor = new Color(border.r * border.a, border.g * border.a, border.b * border.a, 1f);
            ramp.BorderRange = Float(m, "_ShadowBorderRange", 0.08f);
            ramp.EnvironmentStrength = Float(m, "_ShadowEnvStrength", 0f);
            ramp.ShadowColorFromBase = Float(m, "_ShadowMainStrength", 0f);
            ramp.RampStrength = Float(m, "_ShadowStrength", 1f);
            float ignore = Float(m, "_LightingIgnoreAmbientColor", 1f);
            ramp.ShadowDirect = ignore;
            ramp.ShadowAmbient = 1f - ignore;
            ramp.LitAmbient = 0f;
            if (Int(m, "_ShadowMaskType", 0) == 0)
                ramp.RampStrengthMask = Mask(m, d, "_ShadowStrengthMask");
            else
                d.Report(AtlasIssueKind.Unmapped, "lighting.flat-shadow-mask", "The Flat shadow mask type isn't mapped yet.");
            if (AtlasMaterialReader.Texture(m, "_MultilayerMathBlurMap") != null)
                d.Report(AtlasIssueKind.Unmapped, "lighting.band-blur-map", "The band blur map isn't mapped yet.");
            if (On(m, "_ShadowBorderMapToggle"))
                d.Report(AtlasIssueKind.Unmapped, "lighting.band-border-map", "The band border (AO) map isn't mapped yet.");
        }

        private AtlasShadeBand Band(Material m, AtlasUnifiedMaterial d, string prefix)
        {
            var band = new AtlasShadeBand();
            var color = LinearColor(m, prefix + "Color", Color.white);
            band.Color.Color = new Color(color.r, color.g, color.b, 1f);
            band.Color.Map = Texture(m, d, prefix + "ColorTex");
            band.ColorAddition = Float(m, prefix + "ColorTexAddition", 0f);
            band.Alpha = color.a;
            band.Border = Float(m, prefix + "Border", 0.5f);
            band.Blur = Float(m, prefix + "Blur", 0.1f);
            band.NormalStrength = Float(m, prefix + "NormalStrength", 1f);
            band.ReceiveShadows = Float(m, prefix + "Receive", 0f);
            return band;
        }
    }
}
