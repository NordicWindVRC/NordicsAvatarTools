using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Rim Lighting 0 and 1, in their three styles, as angle layers with the step profile.
    // A style's unlit-side rim (UTS2's antipodean, lilToon's indirect) is a second layer.
    public abstract partial class AtlasPoiyomiParser
    {
        private static void Rims(Material m, AtlasUnifiedMaterial d)
        {
            for (int i = 0; i < 2; i++)
            {
                // Rim 1's names insert a 2: _RimWidth → _Rim2Width, _Set_RimLightMask → _Set_Rim2LightMask.
                int index = i;
                string N(string rim0, string rim1) => index == 0 ? rim0 : rim1;
                if (!On(m, N("_EnableRimLighting", "_EnableRim2Lighting"))) continue;
                int from = d.Layers.Count;
                switch (Int(m, N("_RimStyle", "_Rim2Style"), 0))
                {
                    case 1: Uts2Rim(m, d, N); break;
                    case 2: LilRim(m, d, N); break;
                    default: PoiyomiRim(m, d, N); break;
                }
                LayerHue(m, d, from, N("_RimHueShift", "_Rim2HueShift"));
                if (Int(m, N("_RimApplyAlpha", "_Rim2ApplyAlpha"), 0) > 0)
                    d.Report(AtlasIssueKind.Unmapped, "rim.alpha", "A rim applied to alpha isn't mapped yet.");
                BusLayers(m, d, from, N("_RimGlobalMask", "_Rim2GlobalMask"));
                BusWrite(m, d, d.Layers.Count > from ? d.Layers[from] : null, N("_RimApplyGlobalMaskIndex", "_Rim2ApplyGlobalMaskIndex"),
                    N("_RimApplyGlobalMaskBlendType", "_Rim2ApplyGlobalMaskBlendType"));
            }
        }

        private delegate string RimName(string rim0, string rim1);

        // rim = 1 − linstep(saturate(w − blur), w, facing^power), w = lerp(−0.05, 1, width).
        private static void PoiyomiRim(Material m, AtlasUnifiedMaterial d, RimName n)
        {
            float width = Mathf.Lerp(-0.05f, 1f, Float(m, n("_RimWidth", "_Rim2Width"), 0.8f));
            var layer = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Surface,
                Profile = AtlasAngleProfile.Step,
                StepBase = On(m, n("_RimLightingInvert", "_Rim2LightingInvert")) ? AtlasAngleBase.Edge : AtlasAngleBase.Facing,
                LinearAngle = On(m, n("_RimLinear", "_Rim2Linear")),
                Power = Float(m, n("_RimPower", "_Rim2Power"), 1f),
                Low = Mathf.Clamp01(width - Float(m, n("_RimBlur", "_Rim2Blur"), 0.65f)),
                High = width,
                Falling = true,
                // A width at or below zero shows nothing.
                Enabled = width > 0f,
                SourceAlphaIsOpacity = true,
                SurfaceNormalAmount = Float(m, n("_Is_NormalMapToRimLight", "_Is_NormalMapToRim2Light"), 1f),
                BaseColorMix = Float(m, n("_RimBaseColorMix", "_Rim2BaseColorMix"), 0f),
                Opacity = Float(m, n("_RimBlendStrength", "_Rim2BlendStrength"), 1f),
                EmissionAmount = Float(m, n("_RimStrength", "_Rim2Strength"), 0f)
            };
            // 8 kept the Poiyomi style's blend in _RimBlendMode, numbered as 10's _RimPoiBlendMode.
            int blend = major <= 8 ? Int(m, n("_RimBlendMode", "_Rim2BlendMode"), 0) : Int(m, n("_RimPoiBlendMode", "_RimPoi2BlendMode"), 0);
            layer.Blend = blend == 1 ? AtlasBlend.Replace : blend == 2 ? AtlasBlend.Multiply : blend == 3 ? AtlasBlend.Mixed
                : blend == 4 ? AtlasBlend.Screen : AtlasBlend.Add;
            layer.Color.Color = TintColor(m, d, n("_RimLightColor", "_Rim2LightColor"), n("_RimLightColorThemeIndex", "_Rim2LightColorThemeIndex"), layer.Color)
                * Float(m, n("_RimBrightness", "_Rim2Brightness"), 1f);
            layer.Color.Map = Texture(m, d, n("_RimTex", "_Rim2Tex"));
            layer.Mask = Mask(m, d, n("_RimMask", "_Rim2Mask"), Channels[Mathf.Clamp(Int(m, n("_RimMaskChannel", "_Rim2MaskChannel"), 0), 0, 3)]);
            if (layer.Mask != null) layer.Mask.Invert = On(m, n("_RimMaskInvert", "_Rim2MaskInvert"));
            if (On(m, n("_RimShadowToggle", "_Rim2ShadowToggle")))
            {
                float strength = Float(m, n("_RimShadowMaskStrength", "_Rim2ShadowMaskStrength"), 1f);
                bool invert = On(m, n("_RimShadowMaskInvert", "_Rim2ShadowMaskInvert"));
                if (Int(m, n("_RimShadowMaskRampType", "_Rim2ShadowMaskRampType"), 0) == 1)
                {
                    // Custom: × lerp(1, smoothstep(min, max, half-Lambert), strength).
                    var range = AtlasMaterialReader.Vector(m, n("_RimShadowAlpha", "_Rim2ShadowAlpha"), new Vector4(0f, 0f, 0f, 1f));
                    var side = layer.LightSide;
                    side.Mode = AtlasLightSideMode.Mask;
                    side.Smooth = true;
                    side.Low = range.x;
                    side.High = range.y;
                    side.Invert = invert;
                    side.Strength = strength;
                }
                // Shadow map: the rim × lerp(1, the ramped light map, strength); inverted isn't mapped.
                else if (!invert) layer.HideInShadow = strength;
                else ReportRimLightDirection(d);
                layer.LightSide.ShrinkInShadow = Float(m, n("_RimShadowWidth", "_Rim2ShadowWidth"), 0f);
            }
            if (Float(m, n("_RimBiasIntensity", "_Rim2BiasIntensity"), 0f) > 0f)
                d.Report(AtlasIssueKind.Unmapped, "rim.bias", "A rim's bias map isn't mapped yet.");
            d.Layers.Add(layer);
            RimAudioLink(m, d, n("_AudioLinkRim", "_AudioLinkRim2"), d.Layers.Count - 1, Float(m, n("_RimBrightness", "_Rim2Brightness"), 1f));
        }

        // UTS2: rim = linstep(inside, 1, (1 − N·V)^(2^(3 − 3 × power))), added; a hard step with
        // feather off.
        private static void Uts2Rim(Material m, AtlasUnifiedMaterial d, RimName n)
        {
            float inside = Float(m, n("_RimLight_InsideMask", "_Rim2Light_InsideMask"), 0.0001f);
            var layer = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Surface,
                Profile = AtlasAngleProfile.Step,
                StepBase = AtlasAngleBase.Edge,
                Power = Mathf.Pow(2f, 3f - 3f * Float(m, n("_RimLight_Power", "_Rim2Light_Power"), 0.1f)),
                Low = inside,
                High = On(m, n("_RimLight_FeatherOff", "_Rim2Light_FeatherOff")) ? inside : 1f,
                Blend = AtlasBlend.Add,
                SurfaceNormalAmount = Float(m, n("_Is_NormalMapToRimLight", "_Is_NormalMapToRim2Light"), 1f),
                LightColorMix = Float(m, n("_Is_LightColor_RimLight", "_Is_LightColor_Rim2Light"), 1f)
            };
            var color = TintColor(m, d, n("_RimLightColor", "_Rim2LightColor"), n("_RimLightColorThemeIndex", "_Rim2LightColorThemeIndex"));
            layer.Color.Color = new Color(color.r, color.g, color.b, 1f);
            layer.Mask = Mask(m, d, n("_Set_RimLightMask", "_Set_Rim2LightMask"),
                Channels[Mathf.Clamp(Int(m, n("_Set_RimLightMaskChannel", "_Set_Rim2LightMaskChannel"), 1), 0, 3)]);
            if (Float(m, n("_Tweak_RimLightMaskLevel", "_Tweak_Rim2LightMaskLevel"), 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "rim.mask-level", "A rim mask's level isn't mapped yet.");
            // Light direction mask: saturate(rim − (1 − vertex half-Lambert) − level).
            float level = Float(m, n("_Tweak_LightDirection_MaskLevel", "_Tweak_LightDirection_MaskLevel2"), 0f);
            if (On(m, n("_LightDirection_MaskOn", "_LightDirection_MaskOn2")))
            {
                layer.LightSide.Mode = AtlasLightSideMode.Subtract;
                layer.LightSide.Level = level;
            }
            d.Layers.Add(layer);

            // Antipodean: a second rim on the unlit side, saturate(rim − half-Lambert − level).
            if (!On(m, n("_Add_Antipodean_RimLight", "_Add_Antipodean_Rim2Light"))) return;
            var dark = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Surface,
                Profile = AtlasAngleProfile.Step,
                StepBase = AtlasAngleBase.Edge,
                Power = Mathf.Pow(2f, 3f - 3f * Float(m, n("_Ap_RimLight_Power", "_Ap_Rim2Light_Power"), 0.1f)),
                Low = inside,
                High = On(m, n("_Ap_RimLight_FeatherOff", "_Ap_Rim2Light_FeatherOff")) ? inside : 1f,
                Blend = AtlasBlend.Add,
                SurfaceNormalAmount = layer.SurfaceNormalAmount,
                LightColorMix = Float(m, n("_Is_LightColor_Ap_RimLight", "_Is_LightColor_Ap_Rim2Light"), 1f),
                Mask = layer.Mask
            };
            var apColor = TintColor(m, d, n("_Ap_RimLightColor", "_Ap_Rim2LightColor"), n("_RimApColorThemeIndex", "_Rim2ApColorThemeIndex"));
            dark.Color.Color = new Color(apColor.r, apColor.g, apColor.b, 1f);
            dark.LightSide.Mode = AtlasLightSideMode.Subtract;
            dark.LightSide.Invert = true;
            dark.LightSide.Level = level;
            d.Layers.Add(dark);
        }

        // lilToon: rim = edge((1 − abs(N·V))^power, border, blur), blended after lighting.
        private static void LilRim(Material m, AtlasUnifiedMaterial d, RimName n)
        {
            float border = Float(m, n("_RimBorder", "_Rim2Border"), 0.5f), blur = Float(m, n("_RimBlur", "_Rim2Blur"), 0.65f);
            // 8's lilToon rim always adds.
            int blend = major <= 8 ? 1 : Int(m, n("_RimBlendMode", "_Rim2BlendMode"), 1);
            float lighting = Float(m, n("_RimEnableLighting", "_Rim2EnableLighting"), 1f);
            float parallax = Float(m, n("_RimVRParallaxStrength", "_Rim2VRParallaxStrength"), 1f);
            // Poiyomi 11's application stage: 0 after lighting (lit by all of it), 1 on the base colour
            // (lit with the surface), 2 after lighting lit by the direct light alone.
            int stage = Int(m, n("_RimApplicationStage", "_Rim2ApplicationStage"), 0);
            var response = lighting < 0.5f ? AtlasLightResponse.IgnoreLight : stage == 2 ? AtlasLightResponse.ScaleByDirectLight : AtlasLightResponse.ScaleByLight;
            if (stage == 1) response = AtlasLightResponse.IgnoreLight;
            int mode = Int(m, "_Mode", 0);
            var layer = new AtlasAngleLayer
            {
                Placement = stage == 1 ? AtlasPlacement.Surface : AtlasPlacement.Overlay,
                Profile = AtlasAngleProfile.Step,
                StepBase = AtlasAngleBase.Edge,
                AntiAliasing = Float(m, n("_RimAAStrength", "_Rim2AAStrength"), 1f),
                // Fade and transparent only.
                TimesAlpha = On(m, n("_RimApplyTransparency", "_Rim2ApplyTransparency")) && (mode == 2 || mode == 3),
                Power = Float(m, n("_RimFresnelPower", "_Rim2FresnelPower"), 3.5f),
                Low = Mathf.Clamp01(border - blur * 0.5f),
                High = Mathf.Clamp01(border + blur * 0.5f),
                Blend = blend == 0 ? AtlasBlend.Replace : blend == 2 ? AtlasBlend.Screen : blend == 3 ? AtlasBlend.Multiply : AtlasBlend.Add,
                SourceAlphaIsOpacity = true,
                SurfaceNormalAmount = Float(m, n("_RimNormalStrength", "_Rim2NormalStrength"), 1f),
                BaseColorMix = Float(m, n("_RimMainStrength", "_Rim2MainStrength"), 0f),
                LightResponse = response,
                PerEye = parallax >= 0.5f,
                HideInShadow = Float(m, n("_RimShadowMask", "_Rim2ShadowMask"), 0.5f),
                Faces = On(m, n("_RimBackfaceMask", "_Rim2BackfaceMask"), 1f) ? AtlasFaces.Front : AtlasFaces.Both
            };
            layer.Color.Color = TintColor(m, d, n("_RimColor", "_Rim2Color"), n("_RimLightColorThemeIndex", "_Rim2LightColorThemeIndex"), layer.Color);
            var texture = Texture(m, d, n("_RimColorTex", "_Rim2ColorTex"));
            ReportPackedOptions(m, d, n("_RimColorTex", "_Rim2ColorTex"));
            // Poiyomi 11: sampled through the main texture's UV, tiling and panning, then its own.
            var main = d.Surface.BaseColor.Map;
            if (texture != null && On(m, n("_RimUseMainTexUV", "_Rim2UseMainTexUV")))
            {
                if (main == null) texture.Space = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, "_MainTexUV", 0), 0, 3);
                else
                {
                    texture.Space = main.Space;
                    texture.Offset = Vector2.Scale(main.Offset, texture.Tiling) + texture.Offset;
                    texture.Scroll = Vector2.Scale(main.Scroll, texture.Tiling) + texture.Scroll;
                    texture.Tiling = Vector2.Scale(main.Tiling, texture.Tiling);
                }
            }
            // Mask only: the texture's red is a mask instead of a colour.
            if (On(m, n("_RimMaskOnlyMask", "_Rim2MaskOnlyMask")))
            {
                if (texture != null) { texture.Interpretation = AtlasTextureInterpretation.Data; layer.Mask = new AtlasMask { Map = texture }; }
            }
            else layer.Color.Map = texture;
            if ((lighting > 0f && lighting < 1f) || (parallax > 0f && parallax < 1f))
                d.Report(AtlasIssueKind.Unmapped, "rim.partial", "A rim's partial lighting or VR parallax isn't mapped yet; the nearer is used.");
            d.Layers.Add(layer);

            // Light direction: the lit rim × lerp(1, lit, strength), an unlit rim × unlit × strength,
            // before each one's edge; lit = linstep(−range, 1, half-Lambert), unlit the same of 1 − it.
            float direction = Float(m, n("_RimDirStrength", "_Rim2DirStrength"), 0f);
            if (direction <= 0f) return;
            var lit = layer.LightSide;
            lit.Mode = AtlasLightSideMode.Mask;
            lit.BeforeStep = true;
            lit.Low = -Float(m, n("_RimDirRange", "_Rim2DirRange"), 0f);
            lit.Strength = direction;
            float indirBorder = Float(m, n("_RimIndirBorder", "_Rim2IndirBorder"), 0.5f), indirBlur = Float(m, n("_RimIndirBlur", "_Rim2IndirBlur"), 0.1f);
            var dark = new AtlasAngleLayer
            {
                Profile = AtlasAngleProfile.Step,
                StepBase = AtlasAngleBase.Edge,
                Power = layer.Power,
                AntiAliasing = layer.AntiAliasing,
                TimesAlpha = layer.TimesAlpha,
                Placement = layer.Placement,
                Low = Mathf.Clamp01(indirBorder - indirBlur * 0.5f),
                High = Mathf.Clamp01(indirBorder + indirBlur * 0.5f),
                Blend = layer.Blend,
                SourceAlphaIsOpacity = true,
                SurfaceNormalAmount = layer.SurfaceNormalAmount,
                LightResponse = layer.LightResponse,
                PerEye = layer.PerEye,
                HideInShadow = layer.HideInShadow,
                Faces = layer.Faces,
                Mask = layer.Mask
            };
            dark.Color.Color = LinearColor(m, n("_RimIndirColor", "_Rim2IndirColor"), Color.white);
            dark.Color.Map = layer.Color.Map;
            var unlit = dark.LightSide;
            unlit.Mode = AtlasLightSideMode.Scale;
            unlit.BeforeStep = true;
            unlit.Invert = true;
            unlit.Low = -Float(m, n("_RimIndirRange", "_Rim2IndirRange"), 0f);
            unlit.Strength = direction;
            d.Layers.Add(dark);
        }

        private static void ReportRimLightDirection(AtlasUnifiedMaterial d) =>
            d.Report(AtlasIssueKind.Unmapped, "rim.light-direction", "A rim's light direction options aren't mapped yet.");
    }
}
