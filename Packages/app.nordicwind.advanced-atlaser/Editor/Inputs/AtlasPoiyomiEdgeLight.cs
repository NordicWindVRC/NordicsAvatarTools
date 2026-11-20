using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Depth Rim Lighting, Environmental Rim and Backlight (angle layers), Subsurface Scattering.
    internal sealed partial class AtlasPoiyomiReader
    {
        private void EdgeLight(Material m, AtlasUnifiedMaterial d)
        {
            DepthRim(m, d);
            EnvironmentalRim(m, d);
            Backlight(m, d, "");
            // Poiyomi 11's second backlight: the same properties + "__1".
            Backlight(m, d, "__1");
            Subsurface(m, d);
        }

        // A silhouette from the depth texture, blended by weights like a matcap: one layer per weight.
        private void DepthRim(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableDepthRimLighting")) return;
            float ignoreLighting = Float(m, "_DepthRimIgnoreLighting", 0f);
            // Ignoring the lighting divides the color by it: the result shows as is, after lighting.
            var placement = ignoreLighting >= 0.5f ? AtlasPlacement.Overlay : AtlasPlacement.Surface;
            var weights = new (string Property, AtlasBlend Blend, AtlasPlacement Placement, float Fallback)[]
            {
                ("_DepthRimAdditiveLighting", AtlasBlend.Add, AtlasPlacement.Overlay, 0f), ("_DepthRimReplace", AtlasBlend.Replace, placement, 0f),
                ("_DepthRimAdd", AtlasBlend.Add, placement, 0f), ("_DepthRimScreen", AtlasBlend.Screen, placement, 1f),
                ("_DepthRimMultiply", AtlasBlend.Multiply, placement, 0f)
            };
            var mask = BusMask(m, Mask(m, d, "_DepthRimMask", Channels[Mathf.Clamp(Int(m, "_DepthRimMaskChannel", 0), 0, 3)]), "_DepthRimGlobalMask");
            var color = TintColor(m, d, "_DepthRimColor", "_DepthRimColorThemeIndex") * Float(m, "_DepthRimBrightness", 1f);
            float emission = Float(m, "_DepthRimEmission", 0f);
            bool first = true;
            for (int w = 0; w < weights.Length; w++)
            {
                float opacity = Float(m, weights[w].Property, weights[w].Fallback);
                if (opacity <= 0f && !(w == weights.Length - 1 && first && emission > 0f)) continue;
                var layer = new AtlasAngleLayer
                {
                    Source = AtlasAngleSource.Depth,
                    Profile = AtlasAngleProfile.Step,
                    Power = 1f,
                    Placement = weights[w].Placement,
                    Blend = weights[w].Blend,
                    Opacity = opacity,
                    Mask = mask,
                    BaseColorMix = Float(m, "_DepthRimMixBaseColor", 0f),
                    LightColorMix = Float(m, "_DepthRimMixLightColor", 0f),
                    SurfaceNormalAmount = Int(m, "_DepthRimNormalToUse", 1),
                    HideInShadow = Float(m, "_DepthRimMixRampedLightMap", 0f),
                    EmissionAmount = first ? emission : 0f
                };
                layer.Color.Color = new Color(color.r, color.g, color.b, 1f);
                var edge = layer.DepthEdge;
                // 9 and older: two or ten samples.
                edge.Samples = major < 10 ? new[] { 2, 10 }[Mathf.Clamp(Int(m, "_DepthRimType", 0), 0, 1)]
                    : new[] { 2, 4, 8 }[Mathf.Clamp(Int(m, "_DepthRimType", 2), 0, 2)];
                edge.Width = Float(m, "_DepthRimWidth", 0.08f);
                edge.Sharpness = Float(m, "_DepthRimSharpness", 0.5f);
                edge.Threshold = Float(m, "_DepthRimDepthThreshold", 0.25f);
                edge.Binary = On(m, "_DepthRimBinary", 1f);
                edge.FixedSizeDistance = Float(m, "_DepthRimCameraClip", 0.5f);
                edge.FadeNear = Float(m, "_DepthRimMinDistance", 0f);
                edge.FadeFar = Float(m, "_DepthRimMaxDistance", 0f);
                // Map to Light Direction (N·L): × lerp(1, saturate(N·L), amount), N·L = linstep(0.5, 1, half-Lambert).
                float lightDirection = Float(m, "_DepthRimHideInShadow", 0f);
                if (lightDirection > 0f && Int(m, "_DepthRimLightDirMethod", 0) == 0)
                {
                    layer.LightSide.Mode = AtlasLightSideMode.Mask;
                    layer.LightSide.Low = 0.5f;
                    layer.LightSide.Strength = lightDirection;
                }
                d.Layers.Add(layer);
                first = false;
            }
            if (Float(m, "_DepthRimHideInShadow", 0f) > 0f && Int(m, "_DepthRimLightDirMethod", 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "rim.light-direction", "A depth rim's screen-space light direction isn't mapped yet.");
            if (Float(m, "_DepthRimShadowMask", 0f) > 0f)
                d.Report(AtlasIssueKind.Unmapped, "rim.light-direction", "A depth rim's attenuation mix isn't mapped yet.");
            if (ignoreLighting > 0f && ignoreLighting < 1f)
                d.Report(AtlasIssueKind.Unmapped, "rim.partial", "A depth rim partly ignoring lighting isn't mapped yet; the nearer is used.");
        }

        // The reflection probe along the normal at the edge, less a threshold, × base, added after lighting.
        private void EnvironmentalRim(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableEnvironmentalRim")) return;
            float width = Float(m, "_RimEnviroWidth", 0.45f);
            var layer = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Overlay,
                Profile = AtlasAngleProfile.Step,
                StepBase = AtlasAngleBase.Facing,
                Power = 1f,
                Low = Mathf.Min(Float(m, "_RimEnviroSharpness", 0f), width),
                High = width,
                Falling = true,
                Smooth = true,
                ColorSource = AtlasAngleColorSource.EnvironmentAlongNormal,
                EnvironmentRoughnessOverride = true,
                EnvironmentRoughness = Float(m, "_RimEnviroBlur", 0.7f),
                EnvironmentThreshold = Float(m, "_RimEnviroMinBrightness", 0f),
                ExposureOcclusion = Float(m, "_RimEnviroExposureOcclusion", 0f),
                BaseColorMix = 1f,
                Blend = AtlasBlend.Add,
                Opacity = Float(m, "_RimEnviroIntensity", 1f),
                Mask = Mask(m, d, "_RimEnviroMask", Channels[Mathf.Clamp(Int(m, "_RimEnviroChannel", 0), 0, 3)])
            };
            d.Layers.Add(layer);
        }

        // Light shining through from behind, scaled by the light's color, added after lighting.
        private void Backlight(Material m, AtlasUnifiedMaterial d, string s)
        {
            if (!On(m, "_BacklightEnabled" + s)) return;
            float border = Float(m, "_BacklightBorder" + s, 0.65f), blur = Float(m, "_BacklightBlur" + s, 0.05f);
            var layer = new AtlasAngleLayer
            {
                Source = AtlasAngleSource.Backlight,
                Placement = AtlasPlacement.Overlay,
                Profile = AtlasAngleProfile.Step,
                Power = 1f,
                Low = Mathf.Clamp01(border - blur * 0.5f),
                High = Mathf.Clamp01(border + blur * 0.5f),
                Directivity = Mathf.Max(0f, Float(m, "_BacklightDirectivity" + s, 5f)),
                ViewStrength = Float(m, "_BacklightViewStrength" + s, 1f),
                ReceiveShadows = On(m, "_BacklightReceiveShadow" + s, 1f),
                SurfaceNormalAmount = Float(m, "_BacklightNormalStrength" + s, 1f),
                BaseColorMix = Float(m, "_BacklightMainStrength" + s, 0f),
                LightColorMix = 1f,
                SourceAlphaIsOpacity = true,
                Blend = AtlasBlend.Add,
                Faces = On(m, "_BacklightBackfaceMask" + s, 1f) ? AtlasFaces.Front : AtlasFaces.Both,
                OnOutlines = On(m, "_BacklightApplyToOutlines" + s, 1f)
            };
            layer.Color.Color = LinearColor(m, "_BacklightColor" + s, new Color(0.85f, 0.8f, 0.7f, 1f));
            layer.Color.Map = Texture(m, d, "_BacklightColorTex" + s);
            d.Layers.Add(layer);
            layer.Mask = BusMask(m, layer.Mask, "_BacklightGlobalMask" + s);
            BusWrite(m, d, layer, "_BacklightApplyGlobalMaskIndex" + s, "_BacklightApplyGlobalMaskBlendType" + s);
        }

        // pow(saturate(V · −(L + N × distortion)), spread) × strength × saturate(1 − thickness − mod)
        // × lerp(1, light color, mix) × color × map × lerp(1, light map, shadow strength) × lerp(1, base, mix).
        private void Subsurface(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_SubsurfaceScattering")) return;
            var map = Texture(m, d, "_SSSThicknessMap");
            ReportPackedOptions(m, d, "_SSSThicknessMap");
            float offset = Float(m, "_SSSThicknessMod", -1f);
            var layer = new AtlasSubsurfaceLayer
            {
                BaseColorMix = Float(m, "_SSSBaseColorMix", 0f),
                Thickness = map,
                // The channel enum: R 1, G 2, B 3, A 0.
                // 9 and older always read the alpha (their channel setting was unused).
                ThicknessChannel = major < 10 ? AtlasChannel.Alpha : Channels[(Mathf.Clamp(Int(m, "_SSSThicknessMapChannel", 0), 0, 3) + 3) % 4],
                // Without a map the shader's thickness is 1.
                ThicknessOffset = map == null ? offset + 1f : offset,
                ThicknessPower = 1f,
                // The layer divides by π.
                Intensity = Float(m, "_SSSStrength", 0.5f) * Mathf.PI,
                Power = Float(m, "_SSSSpread", 2f),
                Distortion = Float(m, "_SSSDistortion", 1f),
                Ambient = 0f,
                Light = AtlasSubsurfaceLight.Direct,
                LightColorMix = Float(m, "_SSSLightColorMix", 0f),
                ShadowStrength = Float(m, "_SSSShadowStrength", 0.25f)
            };
            layer.Color.Color = LinearColor(m, "_SSSColor", Color.red);
            layer.Color.Map = map;
            d.Layers.Add(layer);
            layer.Mask = BusMask(m, layer.Mask, "_SSSGlobalMaskIndex", "_SSSGlobalMaskBlendType");
        }
    }
}
