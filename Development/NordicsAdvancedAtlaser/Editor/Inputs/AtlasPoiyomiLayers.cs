using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: the surface layers: detail texture, back face, colour masking, decals (4), emissions (4).
    internal sealed partial class AtlasPoiyomiReader
    {
        // Decal and Poiyomi blend types, by their enum values.
        private AtlasBlend PoiyomiBlend(int type)
        {
            // 8.0 offered more modes, some on numbers 10 reuses: burns read as multiply, colour
            // dodge (7, subtract in 10) as screen, the light modes (10–19) as overlay; reported.
            if (major <= 8 && (type == 3 || type == 4 || type == 7 || (type >= 10 && type < 20)))
            {
                legacyColorBlend = true;
                return type == 7 ? AtlasBlend.Screen : type < 10 ? AtlasBlend.Multiply : AtlasBlend.Overlay;
            }
            switch (type)
            {
                case 1: return AtlasBlend.Darken;
                case 2: return AtlasBlend.Multiply;
                case 5: return AtlasBlend.Lighten;
                case 6: return AtlasBlend.Screen;
                case 7: return AtlasBlend.Subtract;
                case 8: return AtlasBlend.Add;
                case 9: return AtlasBlend.Overlay;
                case 20: return AtlasBlend.Mixed;
                default: return AtlasBlend.Replace;
            }
        }

        private void SurfaceLayers(Material m, AtlasUnifiedMaterial d)
        {
            DetailTexture(m, d);
            SkinTone(m, d);
            BackFace(m, d);
            ColorMasking(m, d);
            // Poiyomi dissolves before it shades and applies decals after, so decals stay on top.
            Dissolve(m, d);
            // The grabbed screen goes in before the decals too: everything after it draws on top.
            GrabPass(m, d);
            for (int i = 0; i < 4; i++) Decal(m, d, i);
            for (int i = 0; i < 4; i++) Emission(m, d, i);
        }

        // base × lerp(1, texture × tint × brightness × 2 (in gamma), mask.r × intensity).
        private void DetailTexture(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_DetailEnabled")) return;
            var layer = new AtlasSurfaceLayer { Mask = Mask(m, d, "_DetailMask") };
            // Without a texture the shader uses grey (0.2176 linear), which × 2 in gamma is neutral.
            layer.Source.Map = Texture(m, d, "_DetailTex");
            var tint = TintColor(m, d, "_DetailTint", "_DetailTintThemeIndex") * Float(m, "_DetailBrightness", 1f)
                * (layer.Source.Map == null ? 0.21763764f : 1f);
            layer.Source.Color = new Color(tint.r, tint.g, tint.b, 1f);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, AtlasBlend.MultiplyDouble, Float(m, "_DetailTexIntensity", 1f)));
            d.Layers.Add(layer);
            layer.Mask = BusMask(m, layer.Mask, "_DetailTexGlobalMask");
        }

        // Back faces take the colour × texture, by mask × alpha (or replacing the alpha), and glow.
        private void BackFace(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_BackFaceEnabled")) return;
            var layer = new AtlasSurfaceLayer
            {
                Faces = AtlasFaces.Back,
                Mask = Mask(m, d, "_BackFaceMask", Channels[Mathf.Clamp(Int(m, "_BackFaceMaskChannel", 0), 0, 3)]),
                EmissionAmount = Float(m, "_BackFaceEmissionStrength", 0f)
            };
            layer.Source.Color = TintColor(m, d, "_BackFaceColor", "_BackFaceColorThemeIndex", layer.Source);
            layer.Source.Map = Texture(m, d, "_BackFaceTexture");
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, AtlasBlend.Replace));
            if (On(m, "_BackFaceReplaceAlpha"))
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Alpha, AtlasBlend.Replace) { Channel = AtlasChannel.Alpha });
            else layer.SourceAlphaIsOpacity = true;
            d.Layers.Add(layer);
            LayerHue(m, d, d.Layers.Count - 1, "_BackFaceHueShift", "_BackFaceShiftColorSpace");
            if (Float(m, "_BackFaceDetailIntensity", 1f) != 1f || Float(m, "_BackFaceEmissionLimiter", 1f) != 1f)
                d.Report(AtlasIssueKind.Unmapped, "backface.mods", "Back Face's detail intensity and emission multiplier aren't mapped yet.");
        }

        // A placed texture blended into the base colour by its alpha, replacing the emission under it.
        private void Decal(Material m, AtlasUnifiedMaterial d, int index)
        {
            string s = index == 0 ? "" : index.ToString(), n = index.ToString();
            if (!On(m, "_DecalEnabled" + s)) return;
            // An MSDF texture is a shape: white, its alpha from the distance field.
            var map = Texture(m, d, "_DecalTexture" + s, On(m, "_Decal" + n + "MSDF") ? AtlasTextureInterpretation.Msdf : AtlasTextureInterpretation.Color);
            if (map == null)
            {
                d.Report(AtlasIssueKind.Unmapped, "decal.no-texture", "A decal without a texture (a placed colour) isn't mapped yet.");
                return;
            }
            var decal = Placed(map, AtlasMaterialReader.Vector(m, "_DecalScale" + s, Vector4.one), Int(m, "_Decal" + n + "AspectRatioMode", 0),
                AtlasMaterialReader.Vector(m, "_DecalSideOffset" + s, Vector4.zero),
                AtlasMaterialReader.Vector(m, "_DecalPosition" + s, new Vector4(0.5f, 0.5f, 0f, 0f)));
            decal.Rotation = Float(m, "_DecalRotation" + s, 0f);
            // Poiyomi turns by speed × 2t.
            decal.Spin = Float(m, "_DecalRotationSpeed" + s, 0f) * 2f;
            decal.ClipOutside = !On(m, "_DecalTiled" + s);
            decal.Symmetry = (AtlasDecalSymmetry)Mathf.Clamp(Int(m, "_DecalSymmetryMode" + s, 0), 0, 2);
            decal.Mirror = (AtlasDecalMirror)Mathf.Clamp(Int(m, "_DecalMirroredUVMode" + s, 0), 0, 4);
            decal.Depth = Float(m, "_Decal" + n + "Depth", 0f);
            decal.SmoothBorder = On(m, "_DecalSmoothBorder" + s);
            decal.FlipInMirror = On(m, "_Decal" + n + "FlipInMirror");
            if (On(m, "_Decal" + n + "SpriteSheetEnabled"))
            {
                // frame = floor(start + t × FPS) mod frames.
                string p = "_Decal" + n + "SpriteSheet";
                map.Frames = new AtlasFrames
                {
                    Source = AtlasFrameSource.SpriteSheet,
                    Columns = Mathf.Max(1, Int(m, p + "Columns", 1)), Rows = Mathf.Max(1, Int(m, p + "Rows", 1)),
                    Count = Mathf.Max(0, Int(m, p + "Frames", 0)), Rate = Float(m, p + "FPS", 10f),
                    Offset = Float(m, p + "Frame", 0f),
                    Area = AtlasMaterialReader.Vector(m, p + "Area", new Vector4(1f, 1f, 0f, 0f)), Inset = Float(m, p + "Inset", 0f)
                };
            }

            map.Decal = decal;
            var layer = new AtlasSurfaceLayer
            {
                SourceAlphaIsOpacity = true,
                ReplacesEmission = true,
                EmissionAmount = Float(m, "_DecalEmissionStrength" + s, 0f),
                OnOutlines = On(m, "_Decal" + n + "OnOutlines")
            };
            layer.Source.Color = TintColor(m, d, "_DecalColor" + s, index == 0 ? "_DecalColorThemeIndex" : "_DecalColor" + n + "ThemeIndex", layer.Source);
            layer.Source.Map = map;
            int channel = Int(m, "_Decal" + n + "MaskChannel", index);
            if (index < 4 && channel < 4) layer.Mask = Mask(m, d, "_DecalMask", Channels[Mathf.Clamp(channel, 0, 3)]);
            int face = Int(m, "_Decal" + n + "FaceMask", 0);
            layer.Faces = face == 1 ? AtlasFaces.Front : face == 2 ? AtlasFaces.Back : AtlasFaces.Both;
            float opacity = Float(m, "_DecalBlendAlpha" + s, 1f) * Float(m, "_DecalAlphaIntensity" + s, 1f);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, PoiyomiBlend(Int(m, "_DecalBlendType" + s, 0)), opacity));
            d.Layers.Add(layer);
            DecalAudioLink(m, d, index, d.Layers.Count - 1);

            if (Int(m, "_DecalOverrideAlpha" + s, 0) > 0)
                d.Report(AtlasIssueKind.Unmapped, "decal.alpha-override", "A decal's alpha blend mode isn't mapped yet.");
            // Hue, then gamma, saturation, chromatize, brightness, clamped.
            if (On(m, "_DecalHueShiftEnabled" + s))
            {
                Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_DecalHueShift" + s, "_DecalHueShiftSpeed" + s,
                    "_DecalHueShiftColorSpace" + s, "_DecalHueShiftSelectOrShift" + s);
                if (Float(m, "_Decal" + n + "HueAngleStrength", 0f) != 0f)
                    d.Report(AtlasIssueKind.Unmapped, "decal.hue-angle", "A decal's hue shift by view angle isn't mapped yet.");
            }
            if (On(m, "_DecalColorAdjustEnabled" + s))
            {
                float gamma = Float(m, "_DecalGamma" + s, 1f), saturation = Float(m, "_DecalSaturation" + s, 0f);
                float chromatize = Float(m, "_DecalChromatize" + s, 0f), brightness = Float(m, "_DecalBrightness" + s, 0f);
                if (gamma != 1f) LayerAdjust(d, layer, AtlasColorOperation.Power, gamma);
                if (saturation != 0f) LayerAdjust(d, layer, AtlasColorOperation.Saturation, 1f + saturation);
                if (chromatize != 0f) LayerAdjust(d, layer, AtlasColorOperation.Chromatize, chromatize);
                if (brightness != 0f) LayerAdjust(d, layer, AtlasColorOperation.Brightness, 1f + brightness);
                LayerAdjust(d, layer, AtlasColorOperation.Clamp, 1f);
            }
            if (On(m, "_Decal" + n + "VideoEnabled"))
                d.Report(AtlasIssueKind.Unmapped, "decal.video", "A decal's video texture isn't mapped yet.");
            if (On(m, "_Decal" + n + "ChannelSeparationEnable"))
                d.Report(AtlasIssueKind.Unmapped, "decal.chromatic-aberration", "A decal's chromatic aberration isn't mapped yet.");
            layer.Mask = BusMask(m, layer.Mask, "_Decal" + n + "GlobalMask");
            BusWrite(m, d, layer, "_Decal" + n + "ApplyGlobalMaskIndex", "_Decal" + n + "ApplyGlobalMaskBlendType");
        }

        // Emission: colour × map (× base colour) × strength × alpha, masked; modulators from EmissionModulators.
        private void Emission(Material m, AtlasUnifiedMaterial d, int index)
        {
            string s = index == 0 ? "" : index.ToString(), n = index.ToString();
            if (!On(m, index == 0 ? "_EnableEmission" : "_EnableEmission" + n)) return;
            var layer = new AtlasSurfaceLayer
            {
                Placement = AtlasPlacement.Emission,
                SourceAlphaIsOpacity = true,
                SourceTimesBaseColor = On(m, "_EmissionBaseColorAsMap" + s),
                OverridesBaseColor = On(m, "_EmissionReplace" + n),
                // Poiyomi 11, with premultiplied alpha.
                EmissionTimesAlpha = On(m, "_EmissionApplyTransparency" + s) && On(m, "_AlphaPremultiply")
            };
            layer.Source.Color = TintColor(m, d, "_EmissionColor" + s, index == 0 ? "_EmissionColorThemeIndex" : "_EmissionColor" + n + "ThemeIndex", layer.Source);
            layer.Source.Map = Texture(m, d, "_EmissionMap" + s);
            // Centre out: the map sampled at 0.5 + 0.5 × facing (both axes) × its tiling, panning at the speed.
            if (On(m, "_EmissionCenterOutEnabled" + s) && layer.Source.Map != null)
            {
                var map = layer.Source.Map;
                map.Space = On(m, "_EmissionCenterOutLinear" + s) ? AtlasTextureSpace.FacingGradientLinear : AtlasTextureSpace.FacingGradient;
                map.Offset = Vector2.zero;
                map.Scroll = Vector2.one * Float(m, "_EmissionCenterOutSpeed" + s, 5f) * 0.05f;
            }
            layer.Mask = Mask(m, d, "_EmissionMask" + s,
                Channels[Mathf.Clamp(Int(m, index == 0 ? "_EmissionMaskChannel" : "_EmissionMask" + n + "Channel", 0), 0, 3)]);
            if (layer.Mask != null) layer.Mask.Invert = On(m, "_EmissionMaskInvert" + s);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, Float(m, "_EmissionStrength" + s, 0f)));
            d.Layers.Add(layer);

            // Hue, then saturation, both with the hue shift on.
            if (On(m, "_EmissionHueShiftEnabled" + s))
            {
                Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_EmissionHueShift" + s, "_EmissionHueShiftSpeed" + s,
                    "_EmissionHueShiftColorSpace" + s, "_EmissionHueSelectOrShift" + s);
                float saturation = Float(m, "_EmissionSaturation" + s, 0f);
                if (saturation != 0f) LayerAdjust(d, layer, AtlasColorOperation.Saturation, 1f + saturation);
            }
            EmissionModulators(m, d, index, d.Layers.Count - 1);
            layer.Mask = BusMask(m, layer.Mask, "_EmissionMask" + n + "GlobalMask");
        }

        // An emission's strength (its target's opacity) driven by light, blinking and scrolling.
        private void EmissionModulators(Material m, AtlasUnifiedMaterial d, int index, int layer)
        {
            string s = index == 0 ? "" : index.ToString(), n = index.ToString();
            string target = "layers[" + layer + "].targets[0].opacity";
            // AudioLink first: the shader adds and multiplies the strength before the others.
            EmissionAudioLink(m, d, index, layer);
            if (On(m, "_EnableGITDEmission" + s))
            {
                // Light based: lerp(min multiplier, max multiplier, linstep(min light, max light, luminance)).
                var glow = new AtlasModulator
                {
                    Target = target, Source = AtlasModulatorSource.LightLevel,
                    Min = Float(m, "_GITDEMinEmissionMultiplier" + s, 1f), Max = Float(m, "_GITDEMaxEmissionMultiplier" + s, 0f)
                };
                glow.LightLevel.Linear = true;
                glow.LightLevel.Low = Float(m, "_GITDEMinLight" + s, 0f);
                glow.LightLevel.High = Float(m, "_GITDEMaxLight" + s, 1f);
                // World: the direct light; Mesh: the lit surface.
                bool mesh = Int(m, "_GITDEWorldOrMesh" + s, 0) == 1;
                glow.LightLevel.DirectOnly = !mesh;
                glow.LightLevel.IncludeShadows = mesh;
                d.Modulators.Add(glow);
            }
            if (On(m, "_ScrollingEmission" + s))
            {
                if (On(m, "_EmissionScrollingUseCurve" + s))
                    d.Report(AtlasIssueKind.Unmapped, "emission.scroll-curve", "Scrolling emission with a curve isn't mapped yet.");
                else
                {
                    var sweep = new AtlasModulator { Target = target, Source = AtlasModulatorSource.Sweep, Min = 0f, Max = 1f };
                    sweep.Sweep.Direction = Vector3Of(m, "_EmissiveScroll_Direction" + s, new Vector3(0f, -10f, 0f));
                    sweep.Sweep.Width = Float(m, "_EmissiveScroll_Width" + s, 10f);
                    sweep.Sweep.Speed = Float(m, "_EmissiveScroll_Velocity" + s, 10f);
                    sweep.Sweep.Interval = Float(m, "_EmissiveScroll_Interval" + s, 20f);
                    sweep.Sweep.Offset = Float(m, "_EmissionScrollingOffset" + s, 0f);
                    sweep.Sweep.VertexColorPosition = On(m, "_EmissionScrollingVertexColor" + s);
                    d.Modulators.Add(sweep);
                }
            }
            if (On(m, "_EmissionBlinkingEnabled" + s))
            {
                // Smooth: a sine between min and max at velocity radians per second; flashing: max while
                // frac((t + offset) × velocity / 2π) ≤ duty.
                bool flashing = Int(m, "_EmissiveBlink_Mode" + s, 0) == 1;
                var blink = new AtlasModulator
                {
                    Target = target, Source = AtlasModulatorSource.Time,
                    Min = Float(m, "_EmissiveBlink_Min" + s, 0f), Max = Float(m, "_EmissiveBlink_Max" + s, 1f)
                };
                blink.Time.Waveform = flashing ? AtlasWaveform.Pulse : AtlasWaveform.Sine;
                blink.Time.Frequency = Float(m, "_EmissiveBlink_Velocity" + s, 4f) / (2f * Mathf.PI);
                blink.Time.Offset = Float(m, "_EmissionBlinkingOffset" + s, 0f);
                blink.Time.Duty = Float(m, "_EmissiveBlink_Duty" + s, 0.5f);
                d.Modulators.Add(blink);
            }
            float fluorescence = Float(m, "_EmissionFluorescence" + s, 0f);
            if (fluorescence > 0f) LayerAdjust(d, d.Layers[layer], AtlasColorOperation.Fluorescence, fluorescence);
        }
    }
}
