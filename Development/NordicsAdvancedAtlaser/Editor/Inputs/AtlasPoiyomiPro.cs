using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi Pro: screen-space AO, contact shadows and constellation (pattern layers), TPS and
    // squish, the outline bake, GT7 tone mapping and neutral surface emission (colour operations). Pro is paid: the parser reads its properties;
    // none of its source is copied.
    internal sealed partial class AtlasPoiyomiReader
    {
        // After the surface layers, where Pro applies them to the base colour.
        private void ScreenShadows(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures) return;
            // An animation toggle left off is off.
            if (On(m, "_SSAOEnabled") && On(m, "_SSAOAnimationToggle", 1f))
            {
                var color = TintColor(m, d, "_SSAOColor", "_SSAOColorThemeIndex");
                var occlusion = new AtlasScreenOcclusion
                {
                    Intensity = Float(m, "_SSAOIntensity", 1f), Radius = Float(m, "_SSAORadius", 0.002f), Quality = Float(m, "_SSAOQuality", 2.4f),
                    CenterImportance = Float(m, "_SSAOCenterImportance", 0f), DepthBias = Float(m, "_SSAOBias", 0f), ConeBias = Float(m, "_SSAOCone", 0f),
                    Jitter = Float(m, "_SSAORandomScale", 0f), NormalAmount = Float(m, "_SSAOUseNormals", 0f), NormalRejection = Float(m, "_SSAONormalBias", 0f),
                    ColorMap = SsaoMap(m, "_SSAOEnableColorTexture") ? Texture(m, d, "_SSAOColorMap") : null, ColorMapAsRamp = On(m, "_SSAOAsRamp"),
                    Hsvg = AtlasMaterialReader.Vector(m, "_SSAOHSV", Vector4.zero), SurfaceColorMix = Float(m, "_SSAOUseSurfaceColor", 0f),
                    CameraClip = major < 10 ? 0f : Float(m, "_SSAOCameraClip", 0.1f), HideInLight = Float(m, "_SSAOHideByRampedLightMap", 0f), AlphaFade = Float(m, "_SSAOAlphaFade", 1f),
                    QualityFalloff = new Vector2(Float(m, "_SSAOFalloffStart", 6f), Float(m, "_SSAOFalloffEnd", 8f)),
                    MaxDistanceFalloff = AtlasMaterialReader.Vector(m, "_SSAOMaxDistanceFalloff", new Vector4(0.01f, 0.1f, 1f, 1f))
                };
                var mask = SsaoMap(m, "_SSAOEnableMaskTexture") ? Mask(m, d, "_SSAOMask", Channels[Mathf.Clamp(Int(m, "_SSAOMaskChannel", 0), 0, 3)]) : null;
                if (mask != null) mask.Invert = On(m, "_SSAOMaskInvert");
                var layer = new AtlasPatternLayer
                {
                    Kind = AtlasPatternKind.AmbientOcclusion, AmbientOcclusion = occlusion, Placement = AtlasPlacement.Surface,
                    Color = new Color(color.r, color.g, color.b, 1f), Blend = PoiyomiBlend(Int(m, "_SSAOBlendType", 2)),
                    Mask = BusMask(m, mask, "_SSAOGlobalMaskIndex", "_SSAOGlobalMaskBlendType")
                };
                d.Layers.Add(layer);
                BusWrite(m, d, layer, "_SSAOApplyGlobalMaskIndex", "_SSAOApplyGlobalMaskBlendType");
            }

            if (On(m, "_ContactShadowsEnabled") && On(m, "_ContactShadowsAnimationToggle", 1f))
            {
                int apply = Int(m, "_ContactShadowsApplyMode", 1);
                var color = TintColor(m, d, "_ContactShadowsColor", "_ContactShadowsColorThemeIndex");
                var shadow = new AtlasContactShadow
                {
                    Intensity = Float(m, "_ContactShadowsIntensity", 0.5f), MaxDistance = Float(m, "_ContactShadowsMaxDistance", 0.03f),
                    Quality = Float(m, "_ContactShadowsQuality", 4f), Thickness = Float(m, "_ContactShadowsThickness", 0.005f),
                    DepthBias = Float(m, "_ContactShadowsBias", 0.001f), CustomDirection = Int(m, "_ContactShadowsLightSource", 0) == 1,
                    Direction = Vector3Of(m, "_ContactShadowsCustomLightDir", Vector3.up),
                    DirectionSpace = (AtlasDirectionSpace)Mathf.Clamp(Int(m, "_ContactShadowsCustomLightSpace", 0), 0, 2),
                    CameraFalloff = AtlasMaterialReader.Vector(m, "_ContactShadowsCameraFalloff", new Vector4(5f, 10f, 0f, 0f)),
                    ApplyToColor = apply != 1, ApplyToLightMap = apply != 0,
                    NormalRejection = Float(m, "_ContactShadowsNormalBias", 0.3f), HideInLight = Float(m, "_ContactShadowsHideInLight", 0f),
                    Dither = Float(m, "_ContactShadowsNoiseScale", 1f), RejectionDepth = Float(m, "_ContactShadowsRejectionDepth", 0.1f)
                };
                var mask = Mask(m, d, "_ContactShadowsMask", Channels[Mathf.Clamp(Int(m, "_ContactShadowsMaskChannel", 0), 0, 3)]);
                if (mask != null) mask.Invert = On(m, "_ContactShadowsMaskInvert");
                var layer = new AtlasPatternLayer
                {
                    Kind = AtlasPatternKind.ContactShadow, ContactShadow = shadow, Placement = AtlasPlacement.Surface,
                    Color = color, Blend = PoiyomiBlend(Int(m, "_ContactShadowsBlendType", 2)),
                    Mask = BusMask(m, mask, "_ContactShadowsGlobalMaskIndex", "_ContactShadowsGlobalMaskBlendType")
                };
                d.Layers.Add(layer);
                BusWrite(m, d, layer, "_ContactShadowsApplyGlobalMaskIndex", "_ContactShadowsApplyGlobalMaskBlendType");
                if (On(m, "_ContactShadowsDebug"))
                    d.Report(AtlasIssueKind.Note, "contact-shadows.debug", "Contact shadows' debug view is read as off.");
            }
        }

        // TPS and Squish as deformations after the vertex options; squish's colour as a pattern layer.
        // Pro 9: the screen-space AO's colour map and mask each have a toggle, and it has no camera
        // clip (0 in Pro 10's meaning).
        private bool SsaoMap(Material m, string toggle) => major >= 10 || On(m, toggle);

        private void ProDeformations(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures) return;
            if (On(m, "_TPSPenetratorEnabled") && On(m, "_TPS_AnimatedToggle", 1f))
                AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Tps,
                    Tps = new AtlasTps
                    {
                        Length = Float(m, "_TPS_PenetratorLength", 1f), Scale = Vector3Of(m, "_TPS_PenetratorScale", Vector3.one),
                        Right = Vector3Of(m, "_TPS_PenetratorRight", Vector3.right), Up = Vector3Of(m, "_TPS_PenetratorUp", Vector3.up),
                        Forward = Vector3Of(m, "_TPS_PenetratorForward", Vector3.forward),
                        BakedMesh = AtlasSourceMapping.Texture(m, "_TPS_BakedMesh", AtlasTextureInterpretation.Data),
                        BakedVertexColors = On(m, "_TPS_IsSkinnedMeshRenderer"), TwoSidedRings = On(m, "_TPS_TwoSidedRings", 1f),
                        Channel = Int(m, "_TPS_Channel", 0), BezierStart = Float(m, "_TPS_BezierStart", 0f),
                        BezierSmoothness = Float(m, "_TPS_BezierSmoothness", 0.4f), SmoothStart = Float(m, "_TPS_SmoothStart", 1f),
                        Squeeze = Float(m, "_TPS_Squeeze", 0.3f), SqueezeDistance = Float(m, "_TPS_SqueezeDistance", 0.2f),
                        SqueezeMultipliers = AtlasMaterialReader.Vector(m, "_TPS_SqueezeTypeMultiplier", Vector4.one),
                        Bulge = Float(m, "_TPS_Buldge", 0.3f), BulgeDistance = Float(m, "_TPS_BuldgeDistance", 0.2f),
                        BulgeFalloff = Float(m, "_TPS_BuldgeFalloffDistance", 0.05f),
                        BulgeMultipliers = AtlasMaterialReader.Vector(m, "_TPS_BuldgeTypeMultiplier", Vector4.one),
                        PumpingStrength = Float(m, "_TPS_PumpingStrength", 0f), PumpingSpeed = Float(m, "_TPS_PumpingSpeed", 1f),
                        PumpingWidth = Float(m, "_TPS_PumpingWidth", 0.2f),
                        IdleShrinkWidth = Float(m, "_TPS_IdleSkrinkWidth", 1f), IdleShrinkLength = Float(m, "_TPS_IdleSkrinkLength", 1f)
                    }
                });

            if (!On(m, "_EnableSquish") || !On(m, "_SquishEnabled", 1f)) return;
            var squish = new AtlasSquish
            {
                Orifice = Int(m, "_SquishSource", 0) == 1, Contact = Int(m, "_SquishContact", 0),
                PlaneSize = AtlasMaterialReader.Vector(m, "_SquishPlaneSize", new Vector4(1f, 1f, 0f, 0f)),
                Strength = Float(m, "_SquishStrength", 0.97f), Spread = Float(m, "_SquishSpread", 0.15f), Max = Float(m, "_SquishMax", 1f),
                Falloff = Float(m, "_SquishFalloff", 0.05f), ClipOffset = Float(m, "_SquishClipOffset", 0.05f),
                Gradient = AtlasSourceMapping.Texture(m, "_SquishGradient", AtlasTextureInterpretation.Color),
                GradientMin = Float(m, "_SquishGradientMin", 0.135f), GradientMax = Float(m, "_SquishGradientMax", 0.97f),
                GradientIntensity = Float(m, "_SquishGradientIntensity", 1f),
                Replace = Float(m, "_SquishReplace", 0f), Multiply = Float(m, "_SquishMultiply", 1f), AddUnlit = Float(m, "_SquishAddToLight", 0f)
            };
            var mask = Mask(m, d, "_SquishMask", Channels[Mathf.Clamp(Int(m, "_SquishMaskChannel", 0), 0, 3)]);
            if (mask != null) mask.Invert = On(m, "_SquishMaskInvert");
            AddDeformation(d, new AtlasDeformation { Kind = AtlasDeformationKind.Squish, Squish = squish, Mask = mask });
            // Without a gradient the colour part does nothing.
            if (squish.Gradient == null) return;
            var layer = new AtlasPatternLayer { Kind = AtlasPatternKind.Squish, Squish = squish, Placement = AtlasPlacement.Surface, Mask = mask };
            d.Layers.Add(layer);
            BusWrite(m, d, layer, "_SquishApplyGlobalMaskIndex", "_SquishApplyGlobalMaskBlendType");
        }

        // After the truchet, where Pro applies it; the Triplanar Projection variant leaves it out.
        private void Constellation(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures || !On(m, "_ConstellationEnable") || d.Triplanar.Enabled) return;
            var offset = AtlasMaterialReader.Vector(m, "_ConstellationOffset", Vector4.zero);
            bool audio = AudioLinkOn(m) && On(m, "_ConstellationAudioLinkEnabled");
            bool random = On(m, "_ConstellationRandomColors");
            var constellation = new AtlasConstellation
            {
                Space = UVSpace(m, d, "_ConstellationUV"),
                Centre = Vector2Of(m, "_ConstellationCenter", new Vector2(0.5f, 0.5f)),
                Density = Float(m, "_ConstellationDensity", 10f),
                Scroll = Vector2Of(m, "_ConstellationPanSpeed", Vector2.zero),
                Offset = new Vector2(offset.x, offset.y), Phase = offset.z,
                LayerFade = Vector2Of(m, "_ConstellationLayerFade", new Vector2(0.2f, 1f)),
                Depth = Float(m, "_ConstellationDepth", 0f),
                StarSize = Float(m, "_ConstellationStarSize", 0.75f), StarBrightness = Float(m, "_ConstellationStarBrightness", 1f),
                StarVariation = Float(m, "_ConstellationStarVariation", 0f), StarMovement = Float(m, "_ConstellationStarMovement", 0.4f),
                StarSpeed = Float(m, "_ConstellationStarSpeedMultiplier", 1f), TwinkleSpeed = Float(m, "_ConstellationTwinkleSpeed", 5f),
                LineThickness = Float(m, "_ConstellationLineThickness", 0.5f),
                LineThicknessVariation = Float(m, "_ConstellationLineThicknessVariation", 0.7f),
                LineBrightness = Float(m, "_ConstellationLineBrightness", 1f), LineDensity = Float(m, "_ConstellationLineDensity", 1f),
                RandomColors = random,
                SaturationRange = Vector2Of(m, "_ConstellationSaturationRange", new Vector2(0.7f, 1f)),
                ValueRange = Vector2Of(m, "_ConstellationValueRange", new Vector2(0.8f, 1f)),
                AudioThemeColor = audio && random ? Mathf.Clamp(Int(m, "_ConstellationALThemeColor", 0), 0, 4) : 0
            };
            var mask = Mask(m, d, "_ConstellationMask");
            if (mask != null) mask.Invert = On(m, "_ConstellationMaskInvert");
            int blend = Int(m, "_ConstellationBlendMode", 2);
            var color = TintColor(m, d, "_ConstellationColor", "_ConstellationColorThemeIndex");
            var layer = new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Constellation, Constellation = constellation, Placement = AtlasPlacement.Surface,
                // The colour's alpha is unused.
                Color = new Color(color.r, color.g, color.b, 1f),
                Blend = blend == 0 ? AtlasBlend.Replace : blend == 1 ? AtlasBlend.Multiply : AtlasBlend.Add,
                EmissionAmount = Float(m, "_ConstellationEmissionStrength", 3f),
                Mask = BusMask(m, mask, "_ConstellationMaskGlobalMask", "_ConstellationMaskGlobalMaskBlendType")
            };
            d.Layers.Add(layer);
            BusWrite(m, d, layer, "_ConstellationApplyGlobalMaskIndex", "_ConstellationApplyGlobalMaskBlendType");

            if (!audio) return;
            string path = "layers[" + (d.Layers.Count - 1) + "]";
            AudioRange(m, d, path + ".emissionAmount", AtlasModulatorMode.Add, "_ConstellationALEmissionBand", "_ConstellationALEmissionMod");
            AudioRange(m, d, path + ".constellation.starBrightness", AtlasModulatorMode.Add, "_ConstellationALBrightnessBand", "_ConstellationALBrightnessMod");
            AudioRange(m, d, path + ".constellation.starSize", AtlasModulatorMode.Add, "_ConstellationALSizeBand", "_ConstellationALSizeMod");
            AudioRange(m, d, path + ".constellation.lineThickness", AtlasModulatorMode.Add, "_ConstellationALLineThicknessBand", "_ConstellationALLineThicknessMod");
            AudioRange(m, d, path + ".constellation.lineBrightness", AtlasModulatorMode.Add, "_ConstellationALLineBrightnessBand", "_ConstellationALLineBrightnessMod");
            AudioChrono(m, d, path + ".constellation.twinkleOffset", "_ConstellationALTwinkleBand", "_ConstellationALTwinkleType", Float(m, "_ConstellationALTwinkleSpeed", 0f));
            AudioChrono(m, d, path + ".constellation.movementOffset", "_ConstellationALMovementBand", "_ConstellationALMovementType", Float(m, "_ConstellationALMovementSpeed", 0f));
            if (random)
                AudioChrono(m, d, path + ".constellation.hueOffset", "_ConstellationALHueShiftBand", "_ConstellationALHueShiftType", Float(m, "_ConstellationALHueShiftSpeed", 0f));
            // On the resolved colour (each star's with random colours).
            AudioColorOverride(m, d, path + ".color", "_ConstellationALColorOverride");
        }

        // The geometry variants: tessellation is mapped; Geometric Dissolve, ShatterWave and the
        // particles are reported (one line each). Geom alone adds no settings.
        private void GeometryVariants(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures) return;
            if (IsVariant(m, d, "_TessellationType", "Tessellated"))
            {
                var tessellation = d.Tessellation;
                tessellation.Enabled = true;
                tessellation.Mode = (AtlasTessellationMode)Mathf.Clamp(Int(m, "_TessellationType", 1), 0, 2);
                tessellation.Factor = Float(m, "_TessellationUniform", 2.5f);
                tessellation.EdgeLength = Float(m, "_TessellationEdgeLength", 30f);
                tessellation.Distance = Vector2Of(m, "_TessellationDistance", new Vector2(2f, 8f));
                tessellation.Phong = Float(m, "_TessellationPhong", 0f);
                tessellation.CullMargin = Float(m, "_TessellationCullMargin", 0.1f);
            }
            if (IsVariant(m, d, "_GeoDissolveAlpha", "Geometric Dissolve"))
                d.Report(AtlasIssueKind.Unmapped, "geometry.dissolve", "Poiyomi Pro's Geometric Dissolve (triangles breaking away) isn't mapped yet.");
            if (IsVariant(m, d, "_ShatterWaveEnabled", "ShatterWave") && On(m, "_ShatterWaveEnabled", 1f))
                d.Report(AtlasIssueKind.Unmapped, "geometry.shatter-wave", "Poiyomi Pro's ShatterWave isn't mapped yet.");
            if (IsVariant(m, d, "_ParticleDensity", "Particle"))
                d.Report(AtlasIssueKind.Unmapped, "geometry.particles", "Poiyomi Pro's triangle particles aren't mapped yet.");
        }

        // The Triplanar Projection variant: its settings exist only there, and it's always on.
        private void Triplanar(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures || !IsVariant(m, d, "_TriplanarMode", "Triplanar")) return;
            var triplanar = d.Triplanar;
            triplanar.Enabled = true;
            triplanar.Source = Int(m, "_TriplanarMode", 0) == 1 ? AtlasTriplanarSource.Mesh : AtlasTriplanarSource.Depth;
            int blend = Int(m, "_TriplanarBlendType", 3);
            triplanar.Blend = blend == 1 ? AtlasTriplanarBlend.Sharp : blend == 2 ? AtlasTriplanarBlend.Asymmetric : AtlasTriplanarBlend.Normal;
            triplanar.Sharpness = Float(m, "_TriplanarBlendSharpeness", 0.2f);
            triplanar.ScaleWithObject = Float(m, "_TriplanarScaleWithObject", 1f);
            triplanar.MaxSize = Float(m, "_TriplanarMaxSize", 1f);
            var edge = AtlasMaterialReader.Vector(m, "_TriplanarEdgeFade", new Vector4(0f, 0.5f, 0f, 1f));
            triplanar.EdgeFade = new Vector2(edge.x, edge.y);
            var camera = AtlasMaterialReader.Vector(m, "_TriplanarCameraFade", new Vector4(10f, 15f, 0f, 1f));
            triplanar.CameraFade = new Vector2(camera.x, camera.y);
            triplanar.DepthBlur = Float(m, "_TriplanarDepthBlur", 1f);
            triplanar.MaskStrength = Float(m, "_TriplanarMaskStrength", 1f);
            // The invert comes before the vertex colour: the levels' invert.
            var mask = Mask(m, d, "_TriplanarMask", Channels[Mathf.Clamp(Int(m, "_TriplanarMaskChannel", 0), 0, 3)]);
            if (On(m, "_TriplanarMaskInvert"))
            {
                if (mask == null || mask.Map == null)
                    triplanar.MaskStrength = 0f;
                else
                {
                    mask.Levels = mask.Levels ?? new AtlasMaskLevels();
                    mask.Levels.Invert = !mask.Levels.Invert;
                }
            }
            triplanar.Mask = mask;
        }

        // The outline's baked curvature data (from Pro's bake tool).
        private AtlasOutlineCurvature OutlineCurvature(Material m)
        {
            var data = AtlasSourceMapping.Texture(m, "_OutlineCurvatureData", AtlasTextureInterpretation.Data);
            if (data == null) return null;
            return new AtlasOutlineCurvature
            {
                Data = data, VertexCount = Int(m, "_OutlineCurvatureVertexCount", 0),
                HardEdges = On(m, "_OutlineCurvatureHardEdges", 1f), HardEdgeLimit = Float(m, "_OutlineCurvatureHardEdgeLimit", 93f),
                TipStyle = Int(m, "_OutlineCurvatureTipStyle", 0) == 1 ? AtlasOutlineTipStyle.Taper : AtlasOutlineTipStyle.Miter,
                TipAngle = Float(m, "_OutlineCurvatureTipAngle", 60f),
                SmoothNormals = On(m, "_OutlineCurvatureSmoothNormals"), SmoothStrength = Float(m, "_OutlineCurvatureSmoothStrength", 1f),
                Crevices = On(m, "_OutlineCurvatureCrevices"), CreviceShrink = Float(m, "_OutlineCurvatureCreviceShrink", 1f),
                TextureUVs = On(m, "_OutlineCurvatureUVs", 1f)
            };
        }

        // GT7 tone mapping on the final colour (before emission); neutral surface emission on the emission.
        private void ProToneMapping(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures) return;
            if (On(m, "_GT7ToneMapping"))
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = AtlasAdjustmentTarget.FinalColor, Operation = AtlasColorOperation.ToneMapGt7, Amount = 1f,
                    Settings = new Vector4(Float(m, "_GT7TM_ColorAccuracy", 0.6f), Float(m, "_GT7TM_ChromaFadeStart", 0.98f), Float(m, "_GT7TM_ChromaFadeEnd", 1.16f), 0f)
                });
            if (On(m, "_NeutralEmission"))
                d.ColorAdjustments.Add(new AtlasColorAdjustment
                {
                    Target = AtlasAdjustmentTarget.Emission, Operation = AtlasColorOperation.NeutralEmission, Amount = 1f,
                    Settings = new Vector4(Float(m, "_NeutralEmissionHDRRetention", 0f), Float(m, "_NeutralEmissionHueCorrection", 0f), 0f, 0f)
                });
        }
    }
}
