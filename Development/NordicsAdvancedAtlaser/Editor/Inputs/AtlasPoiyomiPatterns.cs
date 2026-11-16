using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: the procedural patterns (catalogue 4.6): Glitter / Sparkle, Pathing, Internal Parallax,
    // Voronoi, Truchet, Depth FX, AL Volume Color, Video Effects, AL Spectrum; the stats overlay is
    // reported.
    internal sealed partial class AtlasPoiyomiReader
    {
        // Before the mirror texture, as Poiyomi applies them.
        private void Patterns(Material m, AtlasUnifiedMaterial d)
        {
            Glitter(m, d);
            Pathing(m, d);
        }

        // After the mirror texture.
        private void CellPatterns(Material m, AtlasUnifiedMaterial d)
        {
            InternalParallax(m, d);
            Voronoi(m, d);
            Truchet(m, d);
            Constellation(m, d);
            if (On(m, "_TextEnabled"))
                d.Report(AtlasIssueKind.Ignored, "poiyomi.stats-overlay", "Poiyomi's stats overlay (text) is out of scope and ignored.");
        }

        private void InternalParallax(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_PoiInternalParallax")) return;
            var map = Texture(m, d, "_ParallaxInternalMap");
            // Without a map the levels are black: nothing is added.
            if (map == null) return;
            // Its pan is the effect's own scroll, in seconds.
            map.Scroll = Vector2.zero;
            var decal = Placed(map, AtlasMaterialReader.Vector(m, "_ParallaxInternalScale", Vector4.one), 0,
                AtlasMaterialReader.Vector(m, "_ParallaxInternalSideOffset", Vector4.zero),
                AtlasMaterialReader.Vector(m, "_ParallaxInternalPosition", new Vector4(0.5f, 0.5f, 0f, 0f)));
            decal.Rotation = Float(m, "_ParallaxInternalRotation", 0f);
            decal.ClipOutside = false;
            map.Decal = decal;
            var parallax = new AtlasInternalParallax
            {
                Mode = Int(m, "_ParallaxInternalHeightmapMode", 0) == 1 ? AtlasInternalParallaxMode.Heightmap : AtlasInternalParallaxMode.Basic,
                LevelMax = Int(m, "_ParallaxInternalBlendMode", 0) == 1,
                Map = map,
                Tiled = On(m, "_ParallaxInternalTiled", 1f),
                HeightFromAlpha = On(m, "_ParallaxInternalHeightFromAlpha"),
                Scroll = AtlasMaterialReader.Vector(m, "_ParallaxInternalMapPan", new Vector4(0f, 0f, 1f, 1f)),
                PerLevelOffset = AtlasMaterialReader.Vector(m, "_ParallaxInternalPerLevelOffset", Vector4.zero),
                Iterations = Mathf.Max(1, Int(m, "_ParallaxInternalIterations", 4)),
                MinIterations = Mathf.Max(1, Int(m, "_ParallaxInternalIterationsMin", 1)),
                IterationDistance = AtlasMaterialReader.Vector(m, "_ParallaxInternalIterationsDistance", new Vector4(5f, 15f, 0f, 0f)),
                MinDepth = Float(m, "_ParallaxInternalMinDepth", 0f),
                MaxDepth = Float(m, "_ParallaxInternalMaxDepth", 0.1f),
                MinFade = Float(m, "_ParallaxInternalMinFade", 1f),
                MaxFade = Float(m, "_ParallaxInternalMaxFade", 0.1f),
                MinColor = TintColor(m, d, "_ParallaxInternalMinColor", "_ParallaxInternalMinColorThemeIndex"),
                MaxColor = TintColor(m, d, "_ParallaxInternalMaxColor", "_ParallaxInternalMaxColorThemeIndex"),
                HuePerLevel = On(m, "_ParallaxInternalHueShiftEnabled") ? Float(m, "_ParallaxInternalHueShiftPerLevel", 0f) : 0f,
                // Poiyomi 11.
                MipMaps = On(m, "_ParallaxInternalMipMaps"),
                Smooth = On(m, "_ParallaxInternalSmooth"),
                SmoothSteps = Mathf.Max(8, Int(m, "_ParallaxInternalSmoothSteps", 64)),
                AlphaAdd = Float(m, "_ParallaxInternalAlpha", 0f),
                Refraction = On(m, "_ParallaxInternalRefractionEnabled"),
                RefractionIor = Float(m, "_ParallaxInternalRefractionIOR", 1.33f),
                RefractionNormalAmount = Float(m, "_ParallaxInternalRefractionNormal", 1f),
                Fog = On(m, "_ParallaxInternalFogEnabled"),
                FogColor = TintColor(m, d, "_ParallaxInternalFogColor", "_ParallaxInternalFogColorThemeIndex"),
                FogDensity = Float(m, "_ParallaxInternalFogDensity", 2f)
            };
            var mask = Mask(m, d, "_ParallaxInternalMapMask", Channels[Mathf.Clamp(Int(m, "_ParallaxInternalMapMaskChannel", 0), 0, 3)]);
            if (mask != null) mask.Invert = On(m, "_ParallaxInternalMapMaskInvert");
            var layer = new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.InternalParallax,
                InternalParallax = parallax,
                EmissionAmount = Float(m, "_ParallaxInternalEmissionStrength", 0f),
                Placement = AtlasPlacement.Surface,
                Blend = PoiyomiBlend(Int(m, "_ParallaxInternalSurfaceBlendMode", 8)),
                Mask = BusMask(m, mask, "_ParallaxInternalMapMaskGlobalMask")
            };
            d.Layers.Add(layer);
            string path = "layers[" + (d.Layers.Count - 1) + "]";
            AtlasColorAdjustment hue = null;
            if (On(m, "_ParallaxInternalHueShiftEnabled"))
                hue = Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_ParallaxInternalHueShift", "_ParallaxInternalHueShiftSpeed",
                    "_ParallaxInternalHueShiftColorSpace", "_ParallaxInternalHueSelectOrShift");

            if (!AudioLinkOn(m) || !On(m, "_ParallaxInternalAudioLinkEnabled")) return;
            AudioRange(m, d, path + ".emissionAmount", AtlasModulatorMode.Add, "_ParallaxInternalALEmissionBand", "_ParallaxInternalALEmissionMod");
            AudioRange(m, d, path + ".internalParallax.minDepth", AtlasModulatorMode.Add, "_ParallaxInternalALMinDepthBand", "_ParallaxInternalALMinDepthMod");
            AudioRange(m, d, path + ".internalParallax.maxDepth", AtlasModulatorMode.Add, "_ParallaxInternalALMaxDepthBand", "_ParallaxInternalALMaxDepthMod");
            var pan = AtlasMaterialReader.Vector(m, "_ParallaxInternalALPanSpeed", Vector4.zero);
            AudioChrono(m, d, path + ".internalParallax.panOffset.x", "_ParallaxInternalALPanBand", "_ParallaxInternalALPanType", pan.x);
            AudioChrono(m, d, path + ".internalParallax.panOffset.y", "_ParallaxInternalALPanBand", "_ParallaxInternalALPanType", pan.y);
            if (hue != null)
                AudioChrono(m, d, "colorAdjustments[" + d.ColorAdjustments.IndexOf(hue) + "].amount", "_ParallaxInternalALHueShiftBand",
                    "_ParallaxInternalALHueShiftType", Float(m, "_ParallaxInternalALHueShiftSpeed", 0f));
            AudioColorOverride(m, d, path + ".internalParallax.minColor", "_ParallaxInternalALMinColorOverride");
            AudioColorOverride(m, d, path + ".internalParallax.maxColor", "_ParallaxInternalALMaxColorOverride");
            if (On(m, "_ParallaxInternalALHistoryEnabled"))
                d.Report(AtlasIssueKind.Unmapped, "audiolink.parallax-history", "Internal parallax levels lit by AudioLink's history aren't mapped yet.");
        }

        // Depth FX: colour, emission and alpha by the distance to the scene behind.
        private void DepthEffect(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableTouchGlow")) return;
            var depth = new AtlasDepthEffect
            {
                ColorEnabled = On(m, "_DepthColorToggle"),
                Blend = (AtlasDepthBlend)Mathf.Clamp(Int(m, "_DepthColorBlendMode", 0), 0, 2),
                ColorRange = new Vector2(Float(m, "_DepthColorMinDepth", 0f), Float(m, "_DepthColorMaxDepth", 1f)),
                ColorValues = new Vector2(Float(m, "_DepthColorMinValue", 1f), Float(m, "_DepthColorMaxValue", 0f)),
                AlphaEnabled = On(m, "_DepthAlphaToggle"),
                AlphaRange = new Vector2(Float(m, "_DepthAlphaMinDepth", 0f), Float(m, "_DepthAlphaMaxDepth", 1f)),
                AlphaValues = new Vector2(Float(m, "_DepthAlphaMinValue", 1f), Float(m, "_DepthAlphaMaxValue", 0f))
            };
            // UV 8 is the depth gradient.
            if (Int(m, "_DepthTextureUV", 0) == 8)
            {
                depth.Texture = AtlasSourceMapping.Texture(m, "_DepthTexture", AtlasTextureInterpretation.Color);
                depth.AlongDepth = true;
                if (depth.Texture != null)
                {
                    var pan = AtlasMaterialReader.Vector(m, "_DepthTexturePan", Vector4.zero);
                    depth.Texture.Scroll = new Vector2(pan.x, pan.y) * 0.05f;
                }
            }
            else depth.Texture = Texture(m, d, "_DepthTexture");
            var color = TintColor(m, d, "_DepthColor", "_DepthColorThemeIndex");
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Depth,
                Depth = depth,
                Placement = AtlasPlacement.Surface,
                Color = new Color(color.r, color.g, color.b, 1f),
                EmissionAmount = Float(m, "_DepthEmissionStrength", 0f),
                Mask = BusMask(m, Mask(m, d, "_DepthMask", Channels[Mathf.Clamp(Int(m, "_DepthMaskChannel", 0), 0, 3)]), "_DepthMaskGlobalMask")
            });
        }

        // AL Volume Color: a three-colour gradient by AudioLink's spectrum along a UV axis.
        private void AudioVolumeColor(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableVolumeColor")) return;
            // Without AudioLink nothing shows.
            if (!AudioLinkOn(m)) return;
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.AudioVolumeColor,
                AudioVolumeColor = new AtlasAudioVolumeColor
                {
                    Space = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, "_ALVolumeColorUV", 0), 0, 3),
                    AlongV = Int(m, "_ALVolumeColorDirection", 0) == 1,
                    Low = VolumeColor(m, d, "_ALVolumeColorLow"),
                    Mid = VolumeColor(m, d, "_ALVolumeColorMid"),
                    High = VolumeColor(m, d, "_ALVolumeColorHigh"),
                    LowEmission = Float(m, "_ALLowEmission", 0f),
                    MidEmission = Float(m, "_ALMidEmission", 0f),
                    HighEmission = Float(m, "_ALHighEmission", 0f)
                },
                Placement = AtlasPlacement.Surface,
                Blend = PoiyomiBlend(Int(m, "_ALVolumeColorBlendType", 0)),
                Opacity = Mathf.Clamp01(Float(m, "_ALVolumeColorBlendAlpha", 1f))
            });
        }

        // Video Effects: the base colour shown as a screen, right after the main colour.
        private void VideoScreen(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_VideoEffectsEnable")) return;
            var resolution = (Vector2)AtlasMaterialReader.Vector(m, "_VideoResolution", new Vector4(1280f, 720f, 0f, 0f));
            var screen = new AtlasVideoScreen
            {
                Type = (AtlasScreenType)Mathf.Clamp(Int(m, "_VideoType", 3), 0, 5),
                Backlight = Float(m, "_VideoBacklight", 1f),
                Emission = On(m, "_VideoEmissionEnabled", 1f),
                Resolution = resolution,
                Pixelate = On(m, "_VideoPixelateToResolution"),
                Saturation = Float(m, "_VideoSaturation", 0f),
                Contrast = Float(m, "_VideoContrast", 0f),
                RefreshRate = Float(m, "_VideoCRTRefreshRate", 24f),
                FadeTime = Float(m, "_VideoCRTPixelEnergizedTime", 1.9f),
                GameboyRamp = AtlasSourceMapping.Texture(m, "_VideoGameboyRamp", AtlasTextureInterpretation.Color)
            };
            // The pixel texture repeats once per screen pixel.
            screen.PixelTexture = AtlasSourceMapping.Texture(m, "_VideoPixelTexture", AtlasTextureInterpretation.Color);
            if (screen.PixelTexture != null)
            {
                screen.PixelTexture.Space = UVSpace(m, d, "_VideoPixelTextureUV");
                screen.PixelTexture.Tiling = Vector2.Scale(screen.PixelTexture.Tiling, resolution);
                screen.PixelTexture.Offset = Vector2.Scale(screen.PixelTexture.Offset, resolution);
            }
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.VideoScreen,
                VideoScreen = screen,
                Placement = AtlasPlacement.Surface,
                Blend = AtlasBlend.Replace,
                Mask = BusMask(m, Mask(m, d, "_VideoMaskTexture", Channels[Mathf.Clamp(Int(m, "_VideoMaskTextureChannel", 0), 0, 3)]), "_VideoGlobalMask")
            });
        }

        // AL Spectrum: AudioLink's spectrum drawn in a placed area.
        private void AudioSpectrum(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableALDecal")) return;
            // Without AudioLink nothing shows.
            if (!AudioLinkOn(m)) return;
            var placement = Placed(null, AtlasMaterialReader.Vector(m, "_ALDecalScale", Vector4.one), 0,
                AtlasMaterialReader.Vector(m, "_ALDecalSideOffset", Vector4.zero),
                AtlasMaterialReader.Vector(m, "_ALDecalPosition", new Vector4(0.5f, 0.5f, 0f, 0f)));
            placement.Rotation = Float(m, "_ALDecalRotation", 0f);
            // Poiyomi turns by speed × 2t.
            placement.Spin = Float(m, "_ALDecalRotationSpeed", 0f) * 2f;
            placement.ClipOutside = !On(m, "_ALDecalTiled");
            placement.Symmetry = (AtlasDecalSymmetry)Mathf.Clamp(Int(m, "_ALDecalSymmetryMode", 0), 0, 2);
            placement.Mirror = (AtlasDecalMirror)Mathf.Clamp(Int(m, "_ALDecalMirroredUVMode", 0), 0, 4);
            var spectrum = new AtlasAudioSpectrum
            {
                Space = UVSpace(m, d, "_ALDecalUV"),
                Placement = placement,
                Circle = Int(m, "_ALDecalUVMode", 0) == 1,
                CircleDimensions = AtlasMaterialReader.Vector(m, "_ALDecaldCircleDimensions", new Vector4(0f, 1f, 0f, 1f)),
                LineWidth = Float(m, "_ALDecalLineWidth", 1f),
                Bilateral = On(m, "_ALDecalBilateral"),
                MirrorBands = (AtlasSpectrumMirror)Mathf.Clamp(Int(m, "_ALDecalMirrorBand", 0), 0, 2),
                BandRange = Float(m, "_ALDecalBandRange", 0.5f),
                Volume = Float(m, "_ALDecalVolume", 1f),
                BassBoost = Float(m, "_ALDecalBaseBoost", 1f),
                TrebleBoost = Float(m, "_ALDecalTrebleBoost", 1f),
                VolumeSteps = Float(m, "_ALDecalVolumeStep", 0f),
                VolumeClip = new Vector2(Float(m, "_ALDecalVolumeClipMin", 0f), Float(m, "_ALDecalVolumeClipMax", 1f)),
                BandSteps = Float(m, "_ALDecalBandStep", 0f),
                BandClip = new Vector2(Float(m, "_ALDecalBandClipMin", 0f), Float(m, "_ALDecalBandClipMax", 1f)),
                ShapeClip = On(m, "_ALDecalShapeClip"),
                ShapeClipVolumeWidth = Float(m, "_ALDecalShapeClipVolumeWidth", 0.5f),
                ShapeClipBandWidth = Float(m, "_ALDecalShapeClipBandWidth", 0.5f),
                ColorMode = (AtlasSpectrumColorMode)Mathf.Clamp(Int(m, "_ALDecalColorMode", 0), 0, 2),
                ColorChordEmission = Float(m, "_ALDecalColorChordEmission", 0f),
                // Poiyomi 11.
                Gradient = AtlasSourceMapping.Texture(m, "_ALDecalGradient", AtlasTextureInterpretation.Color),
                GradientEmission = Float(m, "_ALDecalGradientEmission", 0f),
                FlipU = (Int(m, "_ALDecalFlip", 0) & 1) != 0,
                FlipV = (Int(m, "_ALDecalFlip", 0) & 2) != 0,
                Arc = Int(m, "_ALDecalRadialMode", 0) == 1,
                InnerRadius = Float(m, "_ALDecalInnerRadius", 0.25f),
                OuterRadius = Float(m, "_ALDecalOuterRadius", 1f),
                StartAngle = Float(m, "_ALDecalStartAngle", 0f),
                ArcDegrees = Float(m, "_ALDecalArcDegrees", 360f),
                Display = (AtlasSpectrumDisplay)Mathf.Clamp(Int(m, "_ALDecalDisplayMode", 0), 0, 2),
                LineThickness = Float(m, "_ALDecalLineThickness", 2f),
                LitSteps = Float(m, "_ALDecalLitSegments", 1f),
                // 10 has no frequency mapping: the band range.
                FrequencyMapping = Int(m, "_ALDecalSamplingMode", AtlasMaterialReader.Has(m, "_ALDecalSamplingMode") ? 1 : 0) == 1,
                FrequencyMin = Float(m, "_ALDecalFrequencyMin", 20f),
                FrequencyMax = Float(m, "_ALDecalFrequencyMax", 14080f),
                BandSampling = (AtlasSpectrumBandSampling)Mathf.Clamp(Int(m, "_ALDecalBandSampling", 0), 0, 2),
                Response = (AtlasSpectrumResponse)Mathf.Clamp(Int(m, "_ALDecalResponse", 0), 0, 2),
                Compression = Float(m, "_ALDecalCompression", 1f),
                MaskFollowsSpectrum = Int(m, "_ALDecalMaskSpace", 0) == 1,
                MasterOpacity = Float(m, "_ALDecalMasterOpacity", 1f),
                EmissionStrength = Float(m, "_ALDecalEmissionStrength", 1f),
                TintEmission = On(m, "_ALDecalTintEmission"),
                ColorSource = (AtlasSpectrumColorSource)Mathf.Clamp(Int(m, "_ALDecalVolumeColorSource", 1), 0, 2),
                Low = TintColor(m, d, "_ALDecalVolumeColorLow", "_ALDecalVolumeColorLowThemeIndex"),
                Mid = TintColor(m, d, "_ALDecalVolumeColorMid", "_ALDecalVolumeColorMidThemeIndex"),
                High = TintColor(m, d, "_ALDecalVolumeColorHigh", "_ALDecalVolumeColorHighThemeIndex"),
                LowEmission = Float(m, "_ALDecalLowEmission", 0f),
                MidEmission = Float(m, "_ALDecalMidEmission", 0f),
                HighEmission = Float(m, "_ALDecalHighEmission", 0f),
                ColorMask = Texture(m, d, "_ALDecalColorMask"),
                OverrideAlpha = Float(m, "_ALDecalControlsAlpha", 0f)
            };
            if (On(m, "_ALDecalUseVideoEffects"))
                spectrum.Screen = d.Layers.OfType<AtlasPatternLayer>().Select(l => l.VideoScreen).FirstOrDefault(s => s != null);
            int preview = Int(m, "_ALDecalPreview", 0);
            if ((preview == 0 && On(m, "_ALDecalDebug")) || preview >= 2 || Int(m, "_ALDecalShowBounds", 0) == 2)
                d.Report(AtlasIssueKind.Note, "spectrum.debug", "An AL spectrum's debug preview is read as the live spectrum.");
            ReportPackedOptions(m, d, "_ALDecalColorMask");
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.AudioSpectrum,
                AudioSpectrum = spectrum,
                Placement = AtlasPlacement.Surface,
                Blend = PoiyomiBlend(Int(m, "_ALDecalBlendType", 0)),
                Opacity = Mathf.Clamp01(Float(m, "_ALDecalBlendAlpha", 1f)),
                Mask = BusMask(m, null, "_ALDecalGlobalMask")
            });
        }

        // Poiyomi multiplies the colour by its theme colour, which is the colour itself without a theme.
        private Color VolumeColor(Material m, AtlasUnifiedMaterial d, string property)
        {
            var color = LinearColor(m, property, Color.white) * TintColor(m, d, property, property + "ThemeIndex");
            return new Color(color.r, color.g, color.b, 1f);
        }

        private static readonly string[] PathChannels = { "R", "G", "B", "A" };

        private void Pathing(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnablePathing")) return;
            var path = new AtlasPath
            {
                Source = Int(m, "_PathSource", 0) == 1 ? AtlasPathSource.UV : AtlasPathSource.Texture,
                Map = Texture(m, d, "_PathingMap", AtlasTextureInterpretation.Data),
                Merged = Int(m, "_PathGradientType", 0) == 1,
                PointSampling = On(m, "_PathPointSampling", 1f),
                ColorMap = Texture(m, d, "_PathingColorMap"),
                Remapping = On(m, "_EnablePathRemapping"),
                OverrideAlpha = On(m, "_PathingOverrideAlpha"),
                Overlap = (AtlasPathOverlap)Mathf.Clamp(Int(m, "_PathOverlapMode", 0), 0, 2),
                Antialiasing = On(m, "_PathAntialiasing"),
                ColorMapAlphaMasks = On(m, "_PathingUseColorMapAlpha", 1f)
            };
            var packed = Texture(m, d, "_PathingMaskMap", AtlasTextureInterpretation.Data);
            var head = AtlasMaterialReader.Vector(m, "_PathHeadSoftness", Vector4.one * 0.1f);
            var tail = AtlasMaterialReader.Vector(m, "_PathTailSoftness", Vector4.one);
            if (path.Source == AtlasPathSource.UV) path.Space = UVSpace(m, d, "_PathingUVSelect");
            var emission = AtlasMaterialReader.Vector(m, "_PathEmissionStrength", Vector4.zero);
            var softness = AtlasMaterialReader.Vector(m, "_PathSoftness", Vector4.one);
            var speed = AtlasMaterialReader.Vector(m, "_PathSpeed", Vector4.one * 0.1f);
            var length = AtlasMaterialReader.Vector(m, "_PathWidth", Vector4.one * 0.03f);
            var gap = AtlasMaterialReader.Vector(m, "_PathGapLengths", Vector4.one * 0.1f);
            var time = AtlasMaterialReader.Vector(m, "_PathTime", Vector4.one * -999f);
            var offset = AtlasMaterialReader.Vector(m, "_PathOffset", Vector4.zero);
            var segments = AtlasMaterialReader.Vector(m, "_PathSegments", Vector4.zero);
            for (int i = 0; i < 4; i++)
            {
                string c = PathChannels[i];
                var channel = path.Channels[i];
                channel.Type = (AtlasPathType)Mathf.Clamp(Int(m, "_PathType" + c, 2), 0, 4);
                channel.Color = TintColor(m, d, "_PathColor" + c, "_PathColor" + c + "ThemeIndex");
                channel.AlongV = Int(m, "_PathSourceDir" + c, 0) == 1;
                channel.Emission = emission[i];
                channel.Softness = softness[i];
                channel.Speed = speed[i];
                channel.Length = length[i];
                channel.Gap = gap[i];
                // −999 runs on the clock.
                channel.ManualTime = time[i] != -999f;
                channel.Time = channel.ManualTime ? time[i] : 0f;
                channel.Offset = offset[i];
                channel.Segments = Mathf.Abs(segments[i]);
                channel.Remap = AtlasMaterialReader.Vector(m, "_PathRemap" + c, new Vector4(0f, 1f, 0f, 1f));
                channel.Mask = BusMask(m, null, "_PathGlobalMask" + c, "_PathGlobalMask" + c + "BlendType");
                // Poiyomi 11.
                channel.GradientChannel = Mathf.Clamp(Int(m, "_PathDirectionChannel" + c, 0), 0, 4) - 1;
                channel.PingPong = Int(m, "_PathMotion" + c, 0) == 1;
                channel.Easing = (AtlasPathEasing)Mathf.Clamp(Int(m, "_PathEasing" + c, 0), 0, 3);
                channel.SeparateEdges = Int(m, "_PathEdges" + c, 0) == 1;
                channel.HeadSoftness = head[i];
                channel.TailSoftness = tail[i];
                int maskChannel = Int(m, "_PathingMaskChannel" + c, 3);
                if (packed != null && maskChannel < 4)
                    channel.PackedMask = MaskOptions(m, new AtlasMask { Map = packed, Channel = Channels[Mathf.Clamp(maskChannel, 0, 3)] }, "_PathingMaskMap", maskChannel);
            }
            int blend = Int(m, "_PathSurfaceBlendMode", 0);
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Path,
                Path = path,
                Placement = AtlasPlacement.Surface,
                Blend = blend == 1 ? AtlasBlend.Add : blend == 2 ? AtlasBlend.Multiply : AtlasBlend.Replace
            });
            PathingAudioLink(m, d, "layers[" + (d.Layers.Count - 1) + "].path.channels[");
        }

        // Time and width offsets, chronotensity and emission per channel; the history, the
        // autocorrelator and the ColorChord strip are reported.
        private void PathingAudioLink(Material m, AtlasUnifiedMaterial d, string prefix)
        {
            if (!AudioLinkOn(m)) return;
            bool history = false, correlator = false, strip = false;
            for (int i = 0; i < 4; i++)
            {
                string c = PathChannels[i], channel = prefix + i + "].";
                if (On(m, "_PathALTimeOffset"))
                    AudioRange(m, d, channel + "offset", AtlasModulatorMode.Add, "_AudioLinkPathTimeOffsetBand" + c, "_AudioLinkPathTimeOffset" + c);
                if (On(m, "_PathALChrono"))
                    AudioChrono(m, d, channel + "offset", "_PathChronoBand" + c, "_PathChronoType" + c, Float(m, "_PathChronoSpeed" + c, 0f));
                if (On(m, "_PathALWidthOffset"))
                {
                    // Poiyomi adds to the half length.
                    var range = AtlasMaterialReader.Vector(m, "_AudioLinkPathWidthOffset" + c, Vector4.zero);
                    if (range.x != 0f || range.y != 0f)
                        d.Modulators.Add(AudioModulator(m, channel + "length", AtlasModulatorMode.Add, Int(m, "_AudioLinkPathWidthOffsetBand" + c, 0), range.x * 2f, range.y * 2f));
                }
                if (On(m, "_PathALEmissionOffset"))
                    AudioRange(m, d, channel + "emission", AtlasModulatorMode.Add, "_AudioLinkPathEmissionAddBand" + c, "_AudioLinkPathEmissionAdd" + c);
                history |= On(m, "_PathALHistory") && On(m, "_PathALHistory" + c);
                correlator |= On(m, "_PathALAutoCorrelator") && Int(m, "_PathALAutoCorrelator" + c, 0) > 0;
                strip |= On(m, "_PathALColorChord") && On(m, "_PathALCC" + c);
            }
            if (history) d.Report(AtlasIssueKind.Unmapped, "audiolink.path-history", "A path drawn by AudioLink's band history isn't mapped yet.");
            if (correlator) d.Report(AtlasIssueKind.Unmapped, "audiolink.path-autocorrelator", "A path drawn by AudioLink's autocorrelator isn't mapped yet.");
            if (strip) d.Report(AtlasIssueKind.Unmapped, "audiolink.colorchord-strip", "A path coloured by the ColorChord strip isn't mapped yet.");
        }

        private void Voronoi(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_VoronoiEnabled")) return;
            var speed = AtlasMaterialReader.Vector(m, "_VoronoiSpeed", Vector4.one);
            var voronoi = new AtlasVoronoi
            {
                Type = (AtlasVoronoiType)Mathf.Clamp(Int(m, "_VoronoiType", 1), 0, 2),
                Space = (AtlasVoronoiSpace)Mathf.Clamp(Int(m, "_VoronoiSpace", 0), 0, 2),
                Scale = Float(m, "_VoronoiScale", 5f),
                // Poiyomi moves by speed × 10 × t / 20.
                Scroll = new Vector3(speed.x, speed.y, speed.z) * 0.5f,
                Power = Float(m, "_VoronoiPower", 0.45454545f),
                Gradient = AtlasMaterialReader.Vector(m, "_VoronoiGradient", new Vector4(0f, 0.5f, 0f, 0f)),
                Noise = Texture(m, d, "_VoronoiNoise", AtlasTextureInterpretation.Data),
                NoiseChannel = Channels[Mathf.Clamp(Int(m, "_VoronoiNoiseChannel", 0), 0, 3)],
                NoiseIntensity = Float(m, "_VoronoiNoiseIntensity", 0.1f),
                OuterColor = TintColor(m, d, "_VoronoiOuterColor", "_VoronoiOuterColorThemeIndex"),
                InnerColor = TintColor(m, d, "_VoronoiInnerColor", "_VoronoiInnerColorThemeIndex"),
                OuterEmission = Float(m, "_VoronoiOuterEmissionStrength", 0f),
                InnerEmission = Float(m, "_VoronoiInnerEmissionStrength", 0f),
                RandomCellColor = On(m, "_VoronoiEnableRandomCellColor"),
                SaturationRange = AtlasMaterialReader.Vector(m, "_VoronoiRandomMinMaxSaturation", new Vector4(0.8f, 1f, 0f, 1f)),
                BrightnessRange = AtlasMaterialReader.Vector(m, "_VoronoiRandomMinMaxBrightness", new Vector4(0.8f, 1f, 0f, 1f)),
                EmissionOnly = Int(m, "_VoronoiBlend", 0) == 1,
                AffectsAlpha = On(m, "_VoronoiAffectsMaterialAlpha"),
                CellMotion = Float(m, "_VoronoiCellMotion", 0f),
                CellMotionSpeed = Float(m, "_VoronoiCellMotionSpeed", 1f)
            };
            if (AudioLinkOn(m) && On(m, "_AudioLinkVoronoiCellsEnabled"))
                voronoi.AudioCells = new AtlasVoronoiAudioCells
                {
                    Band = Mathf.Clamp(Int(m, "_AudioLinkVoronoiCellsBand", 4), 0, 4),
                    History = Float(m, "_AudioLinkVoronoiCellsHistory", 0.5f),
                    Emission = Float(m, "_AudioLinkVoronoiCellsEmission", 2f),
                    Dim = Float(m, "_AudioLinkVoronoiCellsDim", 0.5f)
                };
            if (voronoi.Space == AtlasVoronoiSpace.UV) voronoi.UV = UVSpace(m, d, "_VoronoiUV");
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Voronoi,
                Voronoi = voronoi,
                Placement = voronoi.EmissionOnly ? AtlasPlacement.Emission : AtlasPlacement.Surface,
                Blend = AtlasBlend.Replace,
                Mask = BusMask(m, Mask(m, d, "_VoronoiMask", Channels[Mathf.Clamp(Int(m, "_VoronoiMaskChannel", 0), 0, 3)]), "_VoronoiGlobalMask")
            });
            // Poiyomi 11: the resolved colour's hue, for colour and emission alike.
            if (On(m, "_VoronoiHueShiftEnabled"))
                Hue(m, d, AtlasAdjustmentTarget.Layer, d.Layers[d.Layers.Count - 1], "_VoronoiHueShift", "_VoronoiHueShiftSpeed",
                    "_VoronoiHueShiftColorSpace", "_VoronoiHueSelectOrShift");

            if (!AudioLinkOn(m)) return;
            string path = "layers[" + (d.Layers.Count - 1) + "].voronoi.";
            AudioRange(m, d, path + "innerEmission", AtlasModulatorMode.Add, "_AudioLinkVoronoiInnerEmissionBand", "_AudioLinkVoronoiInnerEmission");
            AudioRange(m, d, path + "outerEmission", AtlasModulatorMode.Add, "_AudioLinkVoronoiOuterEmissionBand", "_AudioLinkVoronoiOuterEmission");
            // The band widens the gradient: its start rises, its end falls.
            float min = Float(m, "_AudioLinkVoronoiGradientMinAdd", 0f), max = Float(m, "_AudioLinkVoronoiGradientMaxAdd", 0f);
            if (min != 0f) d.Modulators.Add(AudioModulator(m, path + "gradient.x", AtlasModulatorMode.Add, Int(m, "_AudioLinkVoronoiGradientMinAddBand", 0), 0f, min));
            if (max != 0f) d.Modulators.Add(AudioModulator(m, path + "gradient.y", AtlasModulatorMode.Add, Int(m, "_AudioLinkVoronoiGradientMaxAddBand", 0), 0f, -max));
            foreach (string axis in new[] { "X", "Y", "Z" })
                AudioChrono(m, d, path + "offset." + axis.ToLowerInvariant(), "_AudioLinkVoronoiChronoSpeed" + axis + "Band",
                    "_AudioLinkVoronoiChronoSpeed" + axis + "Type", Float(m, "_AudioLinkVoronoiChronoSpeed" + axis + "Speed", 0f) * 0.01f);
            AudioColorOverride(m, d, path + "outerColor", "_AudioLinkVoronoiOuterColorOverride");
            AudioColorOverride(m, d, path + "innerColor", "_AudioLinkVoronoiInnerColorOverride");
        }

        private void Truchet(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableTruchet")) return;
            var panOffset = AtlasMaterialReader.Vector(m, "_TruchetPanOffset", Vector4.zero);
            var truchet = new AtlasTruchet
            {
                Space = UVSpace(m, d, "_TruchetUV"),
                Density = Float(m, "_TruchetDensity", 10f),
                // Panning is in grid cells per 20 seconds.
                Scroll = new Vector2(panOffset.x, panOffset.y) * 0.05f,
                Offset = new Vector2(panOffset.z, panOffset.w),
                Thickness = Float(m, "_TruchetThiccness", 0.1f),
                EdgeSharpness = Float(m, "_TruchetEdgeSharpness", 1f),
                OverrideAlpha = On(m, "_TruchetOverrideAlpha"),
                HideWithoutAudioLink = AudioLinkOn(m) && On(m, "_TruchetHideWhenNoAL")
            };
            // Sampled along the path with its own tiling and panning.
            truchet.Texture = AtlasSourceMapping.Texture(m, "_TruchetTex", AtlasTextureInterpretation.Color);
            if (truchet.Texture != null)
            {
                var pan = AtlasMaterialReader.Vector(m, "_TruchetTexPan", Vector4.zero);
                truchet.Texture.Scroll = new Vector2(pan.x, pan.y) * 0.05f;
            }
            var mask = Mask(m, d, "_TruchetMask", Channels[Mathf.Clamp(Int(m, "_TruchetMaskChannel", 0), 0, 3)]);
            if (mask != null) mask.Invert = On(m, "_TruchetMaskInvert");
            if (Float(m, "_TruchetMaskIntensity", 1f) != 1f)
                d.Report(AtlasIssueKind.Unmapped, "truchet.mask-intensity", "A truchet mask's intensity isn't mapped yet.");
            var color = TintColor(m, d, "_TruchetColor", "_TruchetColorThemeIndex");
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Truchet,
                Truchet = truchet,
                Placement = AtlasPlacement.Surface,
                // The colour's alpha is unused.
                Color = new Color(color.r, color.g, color.b, 1f),
                Opacity = Float(m, "_TruchetAlpha", 1f),
                Blend = PoiyomiBlend(Int(m, "_TruchetBlendType", 0)),
                EmissionAmount = Float(m, "_TruchetEmissionStrength", 0f),
                Mask = BusMask(m, mask, "_TruchetMaskGlobalMask")
            });

            if (!AudioLinkOn(m)) return;
            string path = "layers[" + (d.Layers.Count - 1) + "]";
            if (On(m, "_TruchetALEmissionToggle"))
                AudioRange(m, d, path + ".emissionAmount", AtlasModulatorMode.Add, "_TruchetALEmissionBand", "_TruchetALEmission");
            if (On(m, "_TruchetALAlphaToggle"))
                AudioRange(m, d, path + ".opacity", AtlasModulatorMode.Multiply, "_TruchetALAlphaBand", "_TruchetALAlpha");
            if (On(m, "_TruchetChronoPanToggle"))
            {
                var speed = AtlasMaterialReader.Vector(m, "_TruchetChronoPanSpeed", Vector4.zero);
                AudioChrono(m, d, path + ".truchet.textureOffset.x", "_TruchetChronoPanBand", "_TruchetChronoPanType", speed.x);
                AudioChrono(m, d, path + ".truchet.textureOffset.y", "_TruchetChronoPanBand", "_TruchetChronoPanType", speed.y);
            }
            AudioColorOverride(m, d, path + ".color", "_TruchetALColorOverride");
        }

        // Poiyomi's UV choice as a texture space, for patterns that sample no texture of their own.
        private AtlasTextureSpace UVSpace(Material m, AtlasUnifiedMaterial d, string property, int fallback = 0)
        {
            int uv = Int(m, property, fallback);
            if (major <= 7 && uv == 4) uv = 7;
            switch (uv)
            {
                case 0: case 1: case 2: case 3: return AtlasTextureSpace.UV0 + uv;
                case 4: return AtlasTextureSpace.Panosphere;
                case 5: return AtlasTextureSpace.WorldPosition;
                case 6: return AtlasTextureSpace.Polar;
                case 8: return AtlasTextureSpace.ObjectPosition;
                case 9: return AtlasTextureSpace.View;
                case 10: return AtlasTextureSpace.Screen;
                default:
                    d.Report(AtlasIssueKind.Unmapped, "pattern.distorted-uv", "A pattern on distorted UVs isn't mapped yet; read as the distorted channel undistorted.");
                    return AtlasTextureSpace.UV0 + (distortion == null ? 0 : distortedChannel);
            }
        }

        private void Glitter(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_GlitterEnable")) return;
            int mode = Mathf.Clamp(Int(m, "_GlitterMode", 0), 0, 2);
            bool linear = mode == 1;
            var pan = AtlasMaterialReader.Vector(m, "_GlitterUVPanning", Vector4.zero);
            var glitter = new AtlasGlitter
            {
                Mode = (AtlasGlitterMode)mode,
                Shape = Int(m, "_GlitterShape", 0) == 1 ? AtlasGlitterShape.Square : AtlasGlitterShape.Circle,
                Space = UVSpace(m, d, "_GlitterUV"),
                // Panning is in UV per 20 seconds.
                Scroll = new Vector2(pan.x, pan.y) * 0.05f,
                Density = Float(m, "_GlitterFrequency", 300f),
                Layers = Mathf.Clamp(Int(m, "_GlitterLayers", 1), 1, 4),
                RandomPosition = Float(m, "_GlitterRandomLocation", 1f),
                Overlap = On(m, "_GlitterFullCoverage"),
                Size = Float(m, "_GlitterSize", 0.3f),
                RandomSize = On(m, "_GlitterRandomSize"),
                SizeRange = AtlasMaterialReader.Vector(m, "_GlitterMinMaxSize", new Vector4(0.1f, 0.5f, 0f, 1f)),
                NormalAmount = Float(m, "_GlitterUseNormals", 0f),
                MinBrightness = linear ? Float(m, "_GlitterLinearMinBrightness", 0f) : Float(m, "_GlitterMinBrightness", 0f),
                MaxBrightness = linear ? Float(m, "_GlitterLinearBrightness", 3f) : Float(m, "_GlitterBrightness", 3f),
                Speed = linear ? Float(m, "_GlitterLinearSpeed", 0.25f) : Float(m, "_GlitterSpeed", 10f),
                AngleRange = Float(m, "_GlitterAngleRange", 90f),
                Bias = Float(m, "_GlitterBias", 0.8f),
                AngleOffset = Float(m, "_GlitterAngleOffset", 0f),
                Contrast = Float(m, "_GlitterContrast", 300f),
                JaggyFix = Float(m, "_GlitterJaggyFix", 0f),
                DistanceLOD = On(m, linear ? "_GlitterLinearDistanceLOD" : "_GlitterDistanceLOD", 1f),
                LinearSensitivity = Float(m, "_GlitterLinearSensitivity", 0.25f),
                LinearContrast = Float(m, "_GlitterLinearContrast", 50f),
                LinearPostContrast = Float(m, "_GlitterLinearPostContrast", 1f),
                LinearAngleLimit = Float(m, "_GlitterLinearAngle", 0f),
                LinearLightDirection = Float(m, "_GlitterLinearLightDirection", 0f),
                LinearVRParallax = Float(m, "_GlitterLinearVRParallax", 0f),
                ColorMap = Texture(m, d, "_GlitterColorMap"),
                BaseColorMix = Float(m, "_GlitterUseSurfaceColor", 0f),
                RandomColors = On(m, "_GlitterRandomColors"),
                SaturationRange = AtlasMaterialReader.Vector(m, "_GlitterMinMaxSaturation", new Vector4(0.8f, 1f, 0f, 1f)),
                BrightnessRange = AtlasMaterialReader.Vector(m, "_GlitterMinMaxBrightness", new Vector4(0.8f, 1f, 0f, 1f)),
                RandomRotation = On(m, "_GlitterRandomRotation"),
                RandomDirection = On(m, "_GlitterRandomRotationDirection"),
                RotationSpeed = Float(m, "_GlitterTextureRotation", 0f),
                RandomRotationSpeed = AtlasMaterialReader.Vector(m, "_GlitterRandomRotationSpeed", Vector4.zero),
                LightScale = Float(m, "_GlitterScaleWithLighting", 0f)
            };

            // The shape texture is sampled over the particle's square with its own tiling only.
            glitter.Texture = AtlasSourceMapping.Texture(m, "_GlitterTexture", AtlasTextureInterpretation.Color);
            if (glitter.Texture != null)
            {
                if (On(m, "_GlitterTexturePixelMode")) glitter.Texture.Sampling = AtlasTextureSampling.Pixel;
                if (On(m, "_GlitterSpriteSheetEnabled"))
                {
                    glitter.Texture.Frames = new AtlasFrames
                    {
                        Source = AtlasFrameSource.SpriteSheet,
                        Columns = Mathf.Max(1, Int(m, "_GlitterSpriteSheetColumns", 1)),
                        Rows = Mathf.Max(1, Int(m, "_GlitterSpriteSheetRows", 1)),
                        Rate = Float(m, "_GlitterSpriteSheetFPS", 10f),
                        // 0: columns × rows.
                        Count = Mathf.Max(0, Int(m, "_GlitterSpriteSheetTotalFrames", 0))
                    };
                    glitter.RandomFrame = Int(m, "_GlitterSpriteSheetFrameSelection", 0) == 0;
                    glitter.RandomStartFrame = On(m, "_GlitterSpriteSheetRandomStart");
                }
            }

            bool replace = Int(m, "_GlitterBlendType", 0) == 1;
            var color = TintColor(m, d, "_GlitterColor", "_GlitterColorThemeIndex");
            var mask = Mask(m, d, "_GlitterMask", Channels[Mathf.Clamp(Int(m, "_GlitterMaskChannel", 0), 0, 3)]);
            if (mask != null) mask.Invert = On(m, "_GlitterMaskInvert");
            var layer = new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Glitter,
                Glitter = glitter,
                Color = new Color(color.r, color.g, color.b, 1f),
                Opacity = color.a,
                Blend = replace ? AtlasBlend.Replace : AtlasBlend.Add,
                Placement = replace ? AtlasPlacement.Surface : AtlasPlacement.Emission,
                HideInShadow = Float(m, "_GlitterHideInShadow", 0f),
                Mask = BusMask(m, mask, "_GlitterMaskGlobalMask")
            };
            d.Layers.Add(layer);
            string path = "layers[" + (d.Layers.Count - 1) + "]";

            if (On(m, "_GlitterHueShiftEnabled"))
            {
                Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_GlitterHueShift", "_GlitterHueShiftSpeed", "_GlitterHueShiftColorSpace", "_GlitterHueSelectOrShift");
                glitter.HueRandomOffset = On(m, "_GlitterHueShiftRandomOffset");
            }

            if (!AudioLinkOn(m) || !On(m, "_GlitterALEnabled")) return;
            // Poiyomi adds to the mask, never above it; here it adds to the opacity.
            AudioRange(m, d, path + ".opacity", AtlasModulatorMode.Add, "_GlitterALAlphaAddBand", "_GlitterALAlphaAdd");
            AudioRange(m, d, path + ".glitter.maxBrightness", AtlasModulatorMode.Add, "_GlitterALMaxBrightnessBand", "_GlitterALMaxBrightnessAdd");
            AudioRange(m, d, path + ".glitter.size", AtlasModulatorMode.Add, "_GlitterALSizeAddBand", "_GlitterALSizeAdd");
            AudioChrono(m, d, path + ".glitter.sparkleOffset", "_GlitterALChronoSparkleSpeedBand", "_GlitterALChronoSparkleSpeedType", Float(m, "_GlitterALChronoSparkleSpeed", 0f));
            AudioChrono(m, d, path + ".glitter.rotationOffset", "_GlitterALChronoRotationSpeedBand", "_GlitterALChronoRotationSpeedType", Float(m, "_GlitterALChronoRotationSpeed", 0f));
            AudioColorOverride(m, d, path + ".color", "_GlitterALColorOverride");
        }
    }
}
