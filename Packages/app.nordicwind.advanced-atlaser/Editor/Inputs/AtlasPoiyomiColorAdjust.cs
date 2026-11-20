using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Color Adjust (the base color), Post Processing (the final color), and the layers'
    // own hue shifts and color adjusts.
    internal sealed partial class AtlasPoiyomiReader
    {
        private void ColorAdjust(Material m, AtlasUnifiedMaterial d)
        {
            MainColorAdjust(m, d);
            PostProcessing(m, d);
            ProximityColor(m, d);
            ProToneMapping(m, d);
        }

        // The final color and or the emission × lerp(min color, max color, the camera distance
        // through its curve): a tint whose color a camera distance modulator blends to the max.
        private void ProximityColor(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_FXProximityColor")) return;
            var min = TintColor(m, d, "_FXProximityColorMinColor", "_FXProximityColorMinColorThemeIndex");
            var max = TintColor(m, d, "_FXProximityColorMaxColor", "_FXProximityColorMaxColorThemeIndex");
            bool solid = On(m, "_FXProximityColorSolid");
            foreach (var (toggle, fallback, target) in new[]
            {
                ("_FXProximityColorOverrideColor", 1f, AtlasAdjustmentTarget.FinalColor),
                ("_FXProximityColorOverrideEmission", 0f, AtlasAdjustmentTarget.Emission)
            })
            {
                if (!On(m, toggle, fallback)) continue;
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = target, Operation = AtlasColorOperation.Tint, Amount = 1f, Color = new Color(min.r, min.g, min.b, 1f)
                });
                var distance = new AtlasModulator
                {
                    Target = "colorAdjustments[" + (d.ColorAdjustments.Count - 1) + "].color",
                    Source = AtlasModulatorSource.CameraDistance, Mode = AtlasModulatorMode.BlendToColor, Color = new Color(max.r, max.g, max.b, 1f)
                };
                var source = distance.CameraDistance;
                source.PixelPosition = Int(m, "_FXProximityColorType", 1) == 1;
                source.Near = Float(m, "_FXProximityColorMinDistance", 0f);
                source.Far = Float(m, "_FXProximityColorMaxDistance", 1f);
                source.Curve = AtlasMaterialReader.Vector(m, "_FXProximityColorCurve", source.Curve);
                source.Step = solid;
                d.Modulators.Add(distance);
            }
            if (solid && Float(m, "_FXProximityColorEdgeWidth", 0.05f) > 0f && LinearColor(m, "_FXProximityColorEdgeColor", Color.white) != Color.white)
                d.Report(AtlasIssueKind.Unmapped, "proximity.edge", "Proximity color's edge color isn't mapped yet.");
            if (On(m, "_FXProximityColorBackFace"))
                d.Report(AtlasIssueKind.Unmapped, "proximity.backface", "Proximity color's forced back face color isn't mapped yet.");
            if (On(m, "_FXProximityColorOverrideAlpha"))
                d.Report(AtlasIssueKind.Unmapped, "proximity.alpha", "Proximity color overriding the alpha isn't mapped yet.");
        }

        // In the shader's order: hue (mask R), gradient map, gamma (A), saturation (B), chromatize
        // (R), tint, brightness (G), clamp.
        private void MainColorAdjust(Material m, AtlasUnifiedMaterial d)
        {
            // 7.3's hue shift toggle switches the whole section (8 split it off).
            if (!On(m, major <= 7 ? "_MainHueShiftToggle" : "_MainColorAdjustToggle")) return;
            var maps = Texture(m, d, "_MainColorAdjustTexture", AtlasTextureInterpretation.Data);
            ReportPackedOptions(m, d, "_MainColorAdjustTexture");
            // The mask texture's channels, each also reading its global mask: R hue, G brightness, B saturation, A gamma.
            string[] globals = { "_MainHueGlobalMask", "_MainBrightnessGlobalMask", "_MainSaturationGlobalMask", "_MainGammaGlobalMask" };
            AtlasMask MaskOf(int channel) => BusMask(m, maps == null ? null : new AtlasMask { Map = maps, Channel = Channels[channel] }, globals[channel]);
            void Add(AtlasColorOperation operation, float amount, AtlasMask mask) =>
                d.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.BaseColor, Operation = operation, Amount = amount, Mask = mask });

            // Black & White (Poiyomi 11) comes first; its mask is one of the texture's channels.
            if (On(m, "_MainBlackWhiteToggle"))
            {
                int channel = Int(m, "_MainBlackWhiteMaskChannel", 0);
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = AtlasAdjustmentTarget.BaseColor, Operation = AtlasColorOperation.BlackWhite,
                    Amount = Float(m, "_MainBlackWhiteStrength", 1f),
                    Mask = channel > 0 && maps != null ? new AtlasMask { Map = maps, Channel = Channels[Mathf.Clamp(channel - 1, 0, 3)] } : null,
                    BlackWhite = new AtlasBlackWhite
                    {
                        Lightness = Float(m, "_MainBlackWhiteLightness", 1f),
                        Contrast = Float(m, "_MainBlackWhiteContrast", 1f),
                        Tint = LinearColor(m, "_MainBlackWhiteTint", Color.white),
                        // The texture range sliders are sRGB tones.
                        Typical = Mathf.GammaToLinearSpace(Float(m, "_MainBlackWhiteTextureTone", 0.5f)),
                        Highlight = Mathf.GammaToLinearSpace(Float(m, "_MainBlackWhiteTextureHighlight", 0.7f)),
                        Picked = On(m, "_MainBlackWhitePick"),
                        PickedBase = LinearColor(m, "_MainBlackWhiteBaseColor", new Color(0.5f, 0.5f, 0.5f, 1f)),
                        PickedHighlight = LinearColor(m, "_MainBlackWhiteHighlightColor", Color.black),
                        HighlightStrength = Float(m, "_MainBlackWhiteHighlightStrength", 1.6f),
                        Select = On(m, "_MainBlackWhiteSelect"),
                        SelectColor = LinearColor(m, "_MainBlackWhiteSelectColor", Color.red),
                        SelectRange = Float(m, "_MainBlackWhiteSelectRange", 0.2f)
                    }
                });
            }

            if (major <= 7 || On(m, "_MainHueShiftToggle"))
            {
                if (On(m, "_MainHueShiftReplace", 1f))
                {
                    Hue(m, d, AtlasAdjustmentTarget.BaseColor, null, "_MainHueShift", "_MainHueShiftSpeed",
                        "_MainHueShiftColorSpace", "_MainHueShiftSelectOrShift", MaskOf(0));
                    // AudioLink's chronotensity × motion speed turns the hue too.
                    if (On(m, "_MainHueALCTEnabled") && AudioLinkOn(m))
                        AudioChrono(m, d, "colorAdjustments[" + (d.ColorAdjustments.Count - 1) + "].amount", "_MainALHueShiftBand",
                            "_MainALHueShiftCTIndex", Float(m, "_MainHueALMotionSpeed", 1f));
                }
                else d.Report(AtlasIssueKind.Unmapped, "coloradjust.hue-by-mask", "A hue shift offset by its mask (Hue Replace off) isn't mapped yet.");
            }
            float gradation = Float(m, "_MainGradationStrength", 0f);
            if (On(m, "_ColorGradingToggle") && gradation > 0f)
            {
                int channel = Int(m, "_MainGradationMaskChannel", 0);
                // Without a ramp the shader maps to white.
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = AtlasAdjustmentTarget.BaseColor, Operation = AtlasColorOperation.GradientMap, Amount = gradation,
                    Map = AtlasSourceMapping.Texture(m, "_MainGradationTex"), Mask = channel > 0 ? MaskOf(Mathf.Clamp(channel - 1, 0, 3)) : null
                });
            }
            float gamma = Float(m, "_MainGamma", 1f);
            if (gamma != 1f) Add(AtlasColorOperation.Power, gamma, MaskOf(3));
            float saturation = Float(m, "_Saturation", 0f);
            if (saturation != 0f) Add(AtlasColorOperation.Saturation, 1f + saturation, MaskOf(2));
            float chromatize = Float(m, "_MainChromatize", 0f);
            if (chromatize != 0f) Add(AtlasColorOperation.Chromatize, chromatize, MaskOf(0));
            var tint = LinearColor(m, "_MainTintColor", new Color(1f, 1f, 1f, 0f));
            if (tint.a > 0f)
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = AtlasAdjustmentTarget.BaseColor, Operation = AtlasColorOperation.HueTint, Amount = tint.a,
                    Color = new Color(tint.r, tint.g, tint.b, 1f), Map = Texture(m, d, "_MainTintTexture")
                });
            float brightness = Float(m, "_MainBrightness", 0f);
            // 7.3 and 8.x add the brightness; 9 and later multiply by 1 + it.
            if (brightness != 0f)
                Add(major <= 8 ? AtlasColorOperation.Lightness : AtlasColorOperation.Brightness, major <= 8 ? brightness : 1f + brightness, MaskOf(1));
            if (On(m, "_MainColorAdjustClamp", 1f)) Add(AtlasColorOperation.Clamp, 1f, null);
        }

        // The finished color: hue, tint, RGB, saturation, HDR boost (sRGB decode), contrast,
        // brightness, lightness, posterization, all by the mask.
        private void PostProcessing(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_PostProcess")) return;
            var mask = Mask(m, d, "_PPMask", Channels[Mathf.Clamp(Int(m, "_PPMaskChannel", 0), 0, 3)]);
            if (mask != null) mask.Invert = On(m, "_PPMaskInvert");
            mask = BusMask(m, mask, "_PPMaskGlobalMask");
            int before = d.ColorAdjustments.Count;
            void Add(AtlasColorOperation operation, float amount, Color? color = null) =>
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = AtlasAdjustmentTarget.FinalColor, Operation = operation, Amount = amount, Mask = mask, Color = color ?? Color.white
                });

            bool select = Int(m, "_ppHueSelectOrShift", 1) == 0;
            if (select || Float(m, "_PPHue", 0f) != 0f || Float(m, "_PPHueSpeed", 0f) != 0f)
                Hue(m, d, AtlasAdjustmentTarget.FinalColor, null, "_PPHue", "_PPHueSpeed", "_PPHueShiftColorSpace", "_ppHueSelectOrShift", mask);
            if (Float(m, "_PPHueOffset", 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "postprocess.hue-offset", "Post processing's hue offset by mask isn't mapped yet.");
            var tint = LinearColor(m, "_PPTint", Color.white);
            if (tint != Color.white) Add(AtlasColorOperation.Tint, 1f, new Color(tint.r, tint.g, tint.b, 1f));
            var rgb = AtlasMaterialReader.Vector(m, "_PPRGB", Vector4.one);
            if (rgb.x != 1f || rgb.y != 1f || rgb.z != 1f) Add(AtlasColorOperation.Tint, 1f, new Color(rgb.x, rgb.y, rgb.z, 1f));
            float saturation = Float(m, "_PPSaturation", 1f);
            if (saturation != 1f) Add(AtlasColorOperation.Saturation, saturation);
            float hdr = Float(m, "_PPHDR", 0f);
            if (hdr != 0f) Add(AtlasColorOperation.SrgbDecode, hdr);
            float contrast = Float(m, "_PPContrast", 1f);
            if (contrast != 1f) Add(AtlasColorOperation.Contrast, contrast);
            float brightness = Float(m, "_PPBrightness", 1f);
            if (brightness != 1f) Add(AtlasColorOperation.Brightness, brightness);
            float lightness = Float(m, "_PPLightness", 0f);
            if (lightness != 0f) Add(AtlasColorOperation.Lightness, lightness);
            if (On(m, "_PPPosterization")) Add(AtlasColorOperation.Posterize, Mathf.Floor(Float(m, "_PPPosterizationAmount", 4f)));
            // Poiyomi 11: the emission goes through the chain too.
            if (On(m, "_PPIncludeEmission"))
                for (int i = before; i < d.ColorAdjustments.Count; i++) d.ColorAdjustments[i].IncludeEmission = true;
            // The shader masks the chain as a whole; per operation it differs where the mask is partial.
            if (mask != null && d.ColorAdjustments.Count - before > 1)
                d.Report(AtlasIssueKind.Note, "postprocess.mask", "Post processing's mask is applied per operation, not to the chain as a whole.");
        }

        // Adds a hue shift (select or shift, Oklab or HSV). A shift speed (turns per 20 seconds)
        // adds a saw to the amount: a hue turns round, so adding frac(f t) is the same as frac(shift + f t).
        private AtlasColorAdjustment Hue(Material m, AtlasUnifiedMaterial d, AtlasAdjustmentTarget target, AtlasLayer layer,
            string shift, string speed, string space, string selectOrShift, AtlasMask mask = null)
        {
            var adjustment = new AtlasColorAdjustment
            {
                Target = target,
                Layer = layer,
                Operation = AtlasColorOperation.Hue,
                Amount = Float(m, shift, 0f),
                HueMethod = HueMethodOf(m, space),
                HueSet = Int(m, selectOrShift, 1) == 0,
                Mask = mask
            };
            d.ColorAdjustments.Add(adjustment);
            float rate = Float(m, speed, 0f) * 0.05f;
            if (rate != 0f)
            {
                var turning = new AtlasModulator
                {
                    Target = "colorAdjustments[" + (d.ColorAdjustments.Count - 1) + "].amount",
                    Source = AtlasModulatorSource.Time, Mode = AtlasModulatorMode.Add, Min = 0f, Max = 1f
                };
                turning.Time.Waveform = rate > 0f ? AtlasWaveform.Saw : AtlasWaveform.ReverseSaw;
                turning.Time.Frequency = Mathf.Abs(rate);
                d.Modulators.Add(turning);
            }
            return adjustment;
        }

        // The hue color space (0 Oklab, 1 HSV); 7.3 and 8.x always shift in HSV.
        private AtlasHueMethod HueMethodOf(Material m, string property) =>
            major <= 8 || Int(m, property, 0) == 1 ? AtlasHueMethod.HSV : AtlasHueMethod.Oklab;

        // A layer's hue shift, on every layer added since `from` (a section can add several).
        private void LayerHue(Material m, AtlasUnifiedMaterial d, int from, string prefix, string space = null, string select = null)
        {
            if (!On(m, prefix + "Enabled")) return;
            for (int i = from; i < d.Layers.Count; i++)
                Hue(m, d, AtlasAdjustmentTarget.Layer, d.Layers[i], prefix, prefix + "Speed",
                    space ?? prefix + "ColorSpace", select ?? prefix.Replace("HueShift", "HueSelectOrShift"));
        }

        private void LayerAdjust(AtlasUnifiedMaterial d, AtlasLayer layer, AtlasColorOperation operation, float amount, Vector3? luminance = null) =>
            d.ColorAdjustments.Add(new AtlasColorAdjustment
            {
                Target = AtlasAdjustmentTarget.Layer, Layer = layer, Operation = operation, Amount = amount,
                Luminance = luminance ?? new Vector3(0.3f, 0.59f, 0.11f)
            });

    }
}
