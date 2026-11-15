using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Mochie Uber and Uber (Outline), Mochie's avatar shader. Mapped from v1.35.2;
    /// see UNIFIED_MATERIAL_MOCHIE_UBER.md for the inventory this follows.
    /// </summary>
    public sealed class AtlasMochieUberParser : AtlasParserBase
    {
        public override string ParserId => "mochie-uber";
        public override string DisplayName => "Mochie/Uber";
        public override bool CanParse(Material m)
        {
            if (Name(m) == "Mochie/Uber" || Name(m) == "Mochie/Uber (Outline)") return true;
            // Without the shader: Uber's own combination of controls.
            return AtlasMaterialReader.Saved(m) != null && AtlasMaterialReader.Has(m, "_MirrorBehavior") &&
                   AtlasMaterialReader.Has(m, "_CubeMode") && AtlasMaterialReader.Has(m, "_PBRWorkflow") &&
                   AtlasMaterialReader.Has(m, "_RenderMode") && AtlasMaterialReader.Has(m, "_ColorPreservation");
        }

        // Feature masks: Uber either samples one texture per feature (red channel), or four packed
        // textures with a fixed channel per feature, or no masks at all.
        private enum UberMask
        {
            Reflection, Specular, PrimaryMatcap, SecondaryMatcap,
            Shadow, Diffuse, Subsurface, Detail,
            Rim, EnvironmentRim, Refraction, SpecularBlend,
            Emission, EmissionPulse, Filter, OutlineThickness
        }
        private static readonly string[] SeparateMasks = {
            "_ReflectionMask", "_SpecularMask", "_MatcapMask", "_MatcapBlendMask",
            "_ShadowMask", "_DiffuseMask", "_SubsurfaceMask", "_DetailMask",
            "_RimMask", "_ERimMask", "_RefractionMask", "_InterpMask",
            "_EmissMask", "_PulseMask", "_FilterMask", "_OutlineMask"
        };

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            Render(m, d);
            Surface(m, d);
            Lighting(m, d);
            Detail(m, d);
            Emission(m, d);
            Matcaps(m, d);
            Rims(m, d);
            Subsurface(m, d);
            Filtering(m, d);
            d.ExternalLighting.EnableLightVolumes = AtlasSemanticReader.Toggle(m, "_LightVolumesToggle", true);
            d.ExternalLighting.LightVolumeIntensity = F(m, "_LightVolumeStrength", 1f);
            Unmapped(m, d);
        }

        private static void Render(Material m, AtlasUnifiedMaterial d)
        {
            var r = d.Render;
            switch (I(m, "_BlendMode", 0))
            {
                case 0: r.Mode = AtlasSurfaceMode.Opaque; break;
                case 1: r.Mode = AtlasSurfaceMode.Cutout; break;
                case 2:
                    // Dithered: an opaque screen-door pattern that reads as transparency.
                    r.Mode = AtlasSurfaceMode.Cutout;
                    AtlasMaterialReader.MissingFeature(d, "render.dithered",
                        "Dithered transparency is approximated as cutout.");
                    break;
                case 4: r.Mode = AtlasSurfaceMode.Transparent; break;  // Fade: everything fades.
                case 5: r.Mode = AtlasSurfaceMode.Premultiplied; break; // Transparent: reflections stay.
                default: r.Mode = AtlasSurfaceMode.Unknown; break;
            }
            r.AlphaCutoff = F(m, "_Cutoff", 0.5f);
            r.AlphaToCoverage = AtlasSemanticReader.Toggle(m, "_ATM");
            r.ZWrite = AtlasSemanticReader.Toggle(m, "_ZWrite", true);
            int cull = I(m, "_CullingMode", 2);
            r.Cull = cull == 0 ? AtlasCullMode.Off : cull == 1 ? AtlasCullMode.Front : AtlasCullMode.Back;
            r.DoubleSided = r.Cull == AtlasCullMode.Off;
            d.Surface.UseVertexColors = AtlasSemanticReader.Toggle(m, "_VertexColor");
            r.ApplyVertexColor = d.Surface.UseVertexColors;
        }

        private void Surface(Material m, AtlasUnifiedMaterial d)
        {
            var s = d.Surface;
            AtlasMaterialReader.BaseColor(m, d, "_MainTex", "_Color");
            if (s.BaseColor.Map != null && AtlasMaterialReader.Has(m, "_MainTexScroll"))
            {
                Vector4 scroll = AtlasMaterialReader.Vector(m, "_MainTexScroll", Vector4.zero);
                s.BaseColor.Map.Mapping.ScrollSpeed = new Vector2(scroll.x, scroll.y);
                s.BaseColor.Map.Mapping.RotationDegrees = F(m, "_MainTexRot", 0f);
            }
            if (d.Render.Mode != AtlasSurfaceMode.Opaque && I(m, "_UseAlphaMask", 0) == 1)
            {
                s.AlphaMask = AtlasMaterialReader.Mask(m, d, "_AlphaMask", "_AlphaMaskChannel");
                if (s.AlphaMask != null) s.AlphaMask.Strength = F(m, "_AlphaStrength", 1f);
            }

            bool lit = I(m, "_RenderMode", 1) == 1;
            if (!lit) return; // With shading off the shader ignores every surface map.

            var normal = AtlasMaterialReader.Normal(m, d, "_BumpMap", "_BumpScale");
            if (normal != null) normal.NormalBlend = AtlasNormalBlend.Replace;

            int workflow = I(m, "_PBRWorkflow", 0);
            bool specular = workflow == 1 || workflow == 2;
            s.Model = specular ? AtlasShadingModel.StandardSpecular : AtlasShadingModel.StandardMetallic;
            s.Workflow = specular ? AtlasSurfaceWorkflow.Specular : AtlasSurfaceWorkflow.Metallic;
            switch (workflow)
            {
                case 0: // Separate maps; a texture replaces its slider. Roughness, not smoothness.
                    MapOrConstant(m, d, s.Metallic, "_MetallicGlossMap", "_UseMetallicMap", "_Metallic", 0f);
                    MapOrConstant(m, d, s.Roughness, "_SpecGlossMap", "_UseSpecMap", "_Glossiness", 0.5f);
                    Occlusion(m, d, "_OcclusionMap", AtlasChannel.Green);
                    break;
                case 1: // Specular colour, with smoothness from its own map.
                case 2: // Specular colour, with smoothness in its alpha.
                    AtlasMaterialReader.Specular(m, d, "_SpecCol", "_SpecGlossMap");
                    s.Metallic.Constant = 0f;
                    s.Roughness.Constant = F(m, "_GlossMapScale", 1f);
                    s.Roughness.InvertResult = true;
                    if (workflow == 2)
                    {
                        s.Roughness.Sample = s.Specular.Map;
                        s.Roughness.Channel = AtlasChannel.Alpha;
                    }
                    else if (AtlasSemanticReader.Toggle(m, "_UseSmoothMap"))
                    {
                        s.Roughness.Sample = AtlasMaterialReader.Sample(m, "_SmoothnessMap", AtlasColorInterpretation.LinearData, d);
                        s.Roughness.Channel = AtlasChannel.Red;
                    }
                    Occlusion(m, d, "_OcclusionMap", AtlasChannel.Green);
                    break;
                case 3: // One packed texture, a channel per property.
                    var packed = AtlasMaterialReader.Sample(m, "_PackedMap", AtlasColorInterpretation.LinearData, d);
                    s.PackedSurfaceMap = packed;
                    if (packed != null)
                    {
                        s.Metallic.Sample = packed; s.Metallic.Constant = 1f;
                        s.Metallic.Channel = Channel(m, "_MetallicChannel", AtlasChannel.Red);
                        s.Roughness.Sample = packed; s.Roughness.Constant = 1f;
                        s.Roughness.Channel = Channel(m, "_RoughnessChannel", AtlasChannel.Green);
                        s.Occlusion.Sample = packed;
                        s.Occlusion.Constant = F(m, "_OcclusionStrength", 1f);
                        s.Occlusion.Channel = Channel(m, "_OcclusionChannel", AtlasChannel.Blue);
                        s.Occlusion.CombineMode = AtlasScalarCombineMode.StrengthAroundOne;
                        if (AtlasSemanticReader.Toggle(m, "_EnablePackedHeight"))
                        {
                            s.Height.Sample = packed;
                            s.Height.Channel = Channel(m, "_HeightChannel", AtlasChannel.Alpha);
                        }
                    }
                    break;
                default:
                    s.Workflow = AtlasSurfaceWorkflow.Custom;
                    AtlasMaterialReader.MissingFeature(d, "surface.workflow", "Unknown Mochie Uber surface workflow.");
                    break;
            }
            if (workflow != 3)
            {
                s.Height.Sample = AtlasMaterialReader.Sample(m, "_ParallaxMap", AtlasColorInterpretation.LinearData, d);
                s.Height.Channel = AtlasChannel.Green;
            }
            s.HasHeightMap = s.Height.Sample != null;
            if (s.HasHeightMap)
            {
                s.Height.Constant = F(m, "_Parallax", 0.02f);
                s.HeightStrength = s.Height.Constant;
                s.HeightOffset = F(m, "_ParallaxOffset", 0f);
                s.HeightSteps = I(m, "_ParallaxSteps", 0);
            }

            s.SpecularHighlights = I(m, "_Specular", 0) != 0;
            s.GlossyReflections = I(m, "_Reflections", 0) != 0;
            s.GeometricSpecularAA = AtlasSemanticReader.Toggle(m, "_GSAA");

            if (s.GlossyReflections)
            {
                var reflections = AtlasSemanticReader.Feature(d, "surface.reflections");
                AtlasSemanticReader.NumberSet(m, reflections, "strength", "_ReflectionStr", "fresnelStrength", "_FresnelStrength");
                AtlasSemanticReader.ToggleSet(m, reflections, "fresnel", "_FresnelToggle", "litCubemap", "_LitCubemap");
                AtlasSemanticReader.Color(m, reflections, "tint", "_ReflCol");
                if (I(m, "_Reflections", 0) == 2)
                    AtlasSemanticReader.Texture(m, d, reflections, "cubemap", "_ReflCube", AtlasColorInterpretation.ColorSRGB);
                if (AtlasSemanticReader.Toggle(m, "_ReflUseRough"))
                    AtlasSemanticReader.Number(m, reflections, "roughnessOverride", "_ReflRough");
                AddMask(reflections, Mask(m, d, UberMask.Reflection));
            }
            if (s.SpecularHighlights)
            {
                var highlights = AtlasSemanticReader.Feature(d, "surface.specular");
                int type = I(m, "_Specular", 1);
                highlights.Parameters["shape"] = new AtlasSemanticValue { Kind = AtlasSemanticValueKind.Choice,
                    Choice = type == 2 ? "stretched" : type == 3 ? "round+stretched" : "round" };
                AtlasSemanticReader.NumberSet(m, highlights, "strength", "_SpecStr", "anisotropicStrength", "_AnisoStr");
                AtlasSemanticReader.ToggleSet(m, highlights, "stepped", "_SharpSpecular");
                AtlasSemanticReader.Color(m, highlights, "tint", "_SpecCol");
                AddMask(highlights, Mask(m, d, UberMask.Specular));
            }
            if (AtlasSemanticReader.Toggle(m, "_ClearCoat"))
                AtlasSemanticReader.Feature(d, "surface.clearcoat");
            if (AtlasSemanticReader.Toggle(m, "_HardenNormals"))
                AtlasMaterialReader.MissingFeature(d, "surface.flat-shading", "Hard-edged (flat) shading is not mapped yet.");
        }

        private static void Lighting(Material m, AtlasUnifiedMaterial d)
        {
            var l = d.Lighting;
            if (I(m, "_RenderMode", 1) == 0)
            {
                l.Model = AtlasShadingModel.Unlit;
                l.Mode = AtlasLightingMode.Unlit;
                d.Surface.Model = AtlasShadingModel.Unlit;
                d.Surface.Workflow = AtlasSurfaceWorkflow.None;
                return;
            }
            switch (I(m, "_ShadowMode", 0))
            {
                case 0: // No toon shadows: physically based lighting.
                    l.Model = d.Surface.Model;
                    l.Mode = d.Surface.Workflow == AtlasSurfaceWorkflow.Specular
                        ? AtlasLightingMode.StandardSpecular : AtlasLightingMode.StandardMetallic;
                    break;
                case 1: // A ramp described by numbers: two widths blended, an offset and a tint.
                    l.Model = AtlasShadingModel.Toon;
                    l.Mode = AtlasLightingMode.ToonRamp;
                    l.ShadowOffset = F(m, "_RampPos", 0f);
                    SetNumber(l, "rampWidth0", F(m, "_RampWidth0", 0.005f));
                    SetNumber(l, "rampWidth1", F(m, "_RampWidth1", 0.5f));
                    SetNumber(l, "rampBlend", F(m, "_RampWeight", 0f));
                    break;
                case 2:
                    l.Model = AtlasShadingModel.Toon;
                    l.Mode = AtlasLightingMode.ToonRamp;
                    l.ShadowRamp = AtlasMaterialReader.Sample(m, "_ShadowRamp", AtlasColorInterpretation.ColorSRGB, d);
                    break;
                default:
                    l.Mode = AtlasLightingMode.Unknown;
                    AtlasMaterialReader.MissingFeature(d, "lighting.mode", "Unknown Mochie Uber shadow mode.");
                    break;
            }
            if (l.Model == AtlasShadingModel.Toon)
            {
                l.ShadowColor = AtlasMaterialReader.Color(m, "_ShadowTint", Color.black);
                l.ShadowStrength = F(m, "_ShadowStr", 1f);
                var shadows = AtlasSemanticReader.Feature(d, "lighting.shadows");
                AtlasSemanticReader.Texture(m, d, shadows, "paintedShadow", "_DetailShadowMap");
                AtlasSemanticReader.DeclaredChoice(m, d, shadows, "conditions", "_ShadowConditions");
                AddMask(shadows, Mask(m, d, UberMask.Shadow));
            }
            // Colour preservation keeps strong light from washing out the base colour.
            l.LimitBrightness = AtlasSemanticReader.Toggle(m, "_ColorPreservation", true);
            l.DirectIntensity = F(m, "_RTDirectCont", 1f);
            l.IndirectIntensity = F(m, "_RTIndirectCont", 1f);
            l.EnableAdditionalLights = F(m, "_AddCont", 1f) > 0f;
            l.LimitAdditionalLights = AtlasSemanticReader.Toggle(m, "_ClampAdditive", true);
            if (AtlasSemanticReader.Toggle(m, "_StaticLightDirToggle"))
            {
                Vector4 dir = AtlasMaterialReader.Vector(m, "_StaticLightDir", new Vector4(0f, 0.75f, 1f, 0f));
                l.Parameters["fixedLightDirection"] = new AtlasSemanticValue { Kind = AtlasSemanticValueKind.Vector, Vector = dir };
            }
            SetNumber(l, "disneyDiffuse", F(m, "_DisneyDiffuse", 0f));
            SetNumber(l, "lightProbeStrength", F(m, "_SHStr", 1f));
            SetNumber(l, "vertexLightContribution", F(m, "_VLightCont", 1f));
            SetNumber(l, "additionalLightContribution", F(m, "_AddCont", 1f));
        }

        private static void Detail(Material m, AtlasUnifiedMaterial d)
        {
            if (I(m, "_RenderMode", 1) == 0) return;
            int uv = I(m, "_UVSec", 0);
            var mask = Mask(m, d, UberMask.Detail);
            var detail = AtlasMaterialReader.Detail(m, d, "_DetailAlbedoMap", "_DetailAlbedoStrength");
            if (detail != null)
            {
                detail.Map.Mapping.UVIndex = uv;
                detail.Mask = mask;
                int blend = I(m, "_DetailAlbedoBlending", 3);
                detail.DetailBlend = blend == 0 ? AtlasDetailBlendMode.Add : blend == 1 ? AtlasDetailBlendMode.Alpha :
                    blend == 2 ? AtlasDetailBlendMode.Multiply : blend == 3 ? AtlasDetailBlendMode.MultiplyDouble :
                    blend == 4 ? AtlasDetailBlendMode.Overlay : blend == 5 ? AtlasDetailBlendMode.Screen :
                    blend == 6 ? AtlasDetailBlendMode.Lerp : AtlasDetailBlendMode.Unknown;
            }
            var detailNormal = AtlasMaterialReader.Normal(m, d, "_DetailNormalMap", "_DetailNormalMapScale");
            if (detailNormal != null)
            {
                detailNormal.Map.Mapping.UVIndex = uv;
                detailNormal.Mask = mask;
                detailNormal.NormalBlend = AtlasNormalBlend.Whiteout;
            }
            var surfaceDetail = AtlasSemanticReader.Feature(d, "surface.detail");
            AtlasSemanticReader.Texture(m, d, surfaceDetail, "metallic", "_DetailMetallic");
            AtlasSemanticReader.Texture(m, d, surfaceDetail, "roughness", "_DetailRoughnessMap");
            AtlasSemanticReader.Texture(m, d, surfaceDetail, "occlusion", "_DetailOcclusionMap");
            AtlasSemanticReader.NumberSet(m, surfaceDetail, "metallicStrength", "_DetailMetallicStrength",
                "roughnessStrength", "_DetailRoughStrength", "occlusionStrength", "_DetailOcclusionStrength");
            AtlasSemanticReader.DeclaredChoice(m, d, surfaceDetail, "metallicBlend", "_DetailMetallicBlending");
            AtlasSemanticReader.DeclaredChoice(m, d, surfaceDetail, "roughnessBlend", "_DetailRoughBlending");
            AtlasSemanticReader.DeclaredChoice(m, d, surfaceDetail, "occlusionBlend", "_DetailOcclusionBlending");
            if (surfaceDetail.Textures.Count == 0) d.Features.Remove(surfaceDetail);
            else AddMask(surfaceDetail, mask);
        }

        private static void Emission(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasSemanticReader.Toggle(m, "_EmissionToggle")) return;
            var mask = Mask(m, d, UberMask.Emission);
            AddEmission(m, d, "_EmissionMap", "_EmissionColor", "_EmissIntensity", mask);
            AddEmission(m, d, "_EmissionMap2", "_EmissionColor2", "_EmissIntensity2", mask);
            if (AtlasSemanticReader.Toggle(m, "_PulseToggle"))
            {
                var pulse = AtlasSemanticReader.Feature(d, "emission.modulation");
                AtlasSemanticReader.DeclaredChoice(m, d, pulse, "pulseWaveform", "_PulseWaveform");
                AtlasSemanticReader.NumberSet(m, pulse, "pulseStrength", "_PulseStr", "pulseSpeed", "_PulseSpeed");
                AddMask(pulse, Mask(m, d, UberMask.EmissionPulse));
            }
            if (AtlasSemanticReader.Toggle(m, "_ReactToggle"))
            {
                var react = AtlasSemanticReader.Feature(d, "emission.modulation");
                AtlasSemanticReader.NumberSet(m, react, "lightThreshold", "_ReactThresh", "lightCrossfade", "_Crossfade");
                AtlasSemanticReader.ToggleSet(m, react, "lightIncludesShadows", "_UseShadowsForLREmiss", "lightSmoothCrossover", "_CrossMode");
            }
        }

        private static void AddEmission(Material m, AtlasUnifiedMaterial d, string map, string color, string intensity,
            AtlasMaskDefinition mask)
        {
            var layer = AtlasMaterialReader.Emission(m, d, map, color, intensity);
            if (layer == null) return;
            layer.Mask = mask;
            layer.Enabled = layer.Intensity > 0f && layer.Tint.maxColorComponent > 0.00001f;
        }

        private static void Matcaps(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasSemanticReader.Toggle(m, "_MatcapToggle")) return;
            AddMatcap(m, d, "_Matcap", "_MatcapColor", "_MatcapBlending", "_MatcapStr", "_UnlitMatcap", Mask(m, d, UberMask.PrimaryMatcap));
            AddMatcap(m, d, "_Matcap1", "_MatcapColor1", "_MatcapBlending1", "_MatcapStr1", "_UnlitMatcap1", Mask(m, d, UberMask.SecondaryMatcap));
        }

        private static void AddMatcap(Material m, AtlasUnifiedMaterial d, string texture, string color, string blend,
            string strength, string unlit, AtlasMaskDefinition mask)
        {
            var layer = AtlasMaterialReader.Matcap(m, d, texture, strength);
            if (layer == null) return;
            layer.Tint = AtlasMaterialReader.Color(m, color, Color.white);
            layer.Mask = mask;
            bool alpha = I(m, blend, 0) == 1;
            bool isUnlit = AtlasSemanticReader.Toggle(m, unlit);
            layer.Blend = alpha ? AtlasBlendOperation.Replace : AtlasBlendOperation.Add;
            layer.Quality = AtlasNormalizationQuality.Exact;
            if (alpha) layer.BlendWeights.Replace = 1f;
            else if (isUnlit) layer.BlendWeights.UnlitAdd = 1f;
            else layer.BlendWeights.Add = 1f;
        }

        private static void Rims(Material m, AtlasUnifiedMaterial d)
        {
            if (AtlasSemanticReader.Toggle(m, "_RimLighting"))
            {
                var rim = new AtlasRimLayer {
                    Order = d.Rims.Count, Enabled = true,
                    Map = AtlasMaterialReader.Sample(m, "_RimTex", AtlasColorInterpretation.ColorSRGB, d),
                    Tint = AtlasMaterialReader.Color(m, "_RimCol", Color.white),
                    Intensity = F(m, "_RimStr", 1f),
                    Width = F(m, "_RimWidth", 0.5f),
                    // Sharpness 0..0.5: higher is a harder edge.
                    Softness = 1f - Mathf.Clamp01(F(m, "_RimEdge", 0f) * 2f),
                    // A lit rim is scaled by the scene light; an unlit one glows.
                    Environmental = !AtlasSemanticReader.Toggle(m, "_UnlitRim"),
                    Blend = Blend7(I(m, "_RimBlending", 0)),
                    Mask = Mask(m, d, UberMask.Rim),
                    Quality = AtlasNormalizationQuality.Approximate
                };
                d.Rims.Add(rim);
            }
            if (AtlasSemanticReader.Toggle(m, "_EnvironmentRim"))
            {
                var rim = new AtlasRimLayer {
                    Order = d.Rims.Count, Enabled = true,
                    Tint = AtlasMaterialReader.Color(m, "_ERimTint", Color.white),
                    Intensity = F(m, "_ERimStr", 1f),
                    Width = F(m, "_ERimWidth", 0.5f),
                    Softness = 1f - Mathf.Clamp01(F(m, "_ERimEdge", 0f) * 2f),
                    Blend = Blend7(I(m, "_ERimBlending", 0)),
                    Mask = Mask(m, d, UberMask.EnvironmentRim),
                    Quality = AtlasNormalizationQuality.Approximate
                };
                // Coloured by the reflection probe rather than a fixed colour.
                rim.Options = AtlasSemanticReader.Feature(d, "lighting.environmental-rim");
                if (AtlasSemanticReader.Toggle(m, "_ERimUseRough"))
                    AtlasSemanticReader.Number(m, rim.Options, "roughness", "_ERimRoughness");
                d.Rims.Add(rim);
            }
        }

        private static void Subsurface(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasSemanticReader.Toggle(m, "_Subsurface")) return;
            d.Subsurfaces.Add(new AtlasSubsurfaceLayer {
                Order = d.Subsurfaces.Count, Enabled = true,
                Map = AtlasMaterialReader.Sample(m, "_ThicknessMap", AtlasColorInterpretation.LinearData, d),
                Tint = AtlasMaterialReader.Color(m, "_ScatterCol", Color.white),
                Opacity = F(m, "_ScatterIntensity", 1f),
                Thickness = F(m, "_ThicknessMapPower", 1f),
                Mask = Mask(m, d, UberMask.Subsurface)
            });
            var feature = AtlasSemanticReader.Feature(d, "lighting.subsurface");
            AtlasSemanticReader.NumberSet(m, feature, "power", "_ScatterPow", "normalStrength", "_ScatterDist",
                "ambient", "_ScatterAmbient", "wrap", "_ScatterWrap");
            AtlasSemanticReader.ToggleSet(m, feature, "tintByBaseColor", "_ScatterBaseColorTint");
            AtlasSemanticReader.Texture(m, d, feature, "color", "_ScatterTex", AtlasColorInterpretation.ColorSRGB);
        }

        private static void Filtering(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasSemanticReader.Toggle(m, "_Filtering")) return;
            var c = d.ColorProcessing;
            c.Enabled = true;
            c.HueShift = F(m, "_Hue", 0f); // 0..1 = one revolution.
            c.HueColorSpace = I(m, "_HueMode", 0) == 1 ? AtlasHueColorSpace.Oklab : AtlasHueColorSpace.HSV;
            c.Saturation = F(m, "_Saturation", 1f);
            c.BrightnessMultiplier = F(m, "_Brightness", 1f);
            c.Contrast = F(m, "_Contrast", 1f);
            c.HueMask = Mask(m, d, UberMask.Filter);
            var adjust = AtlasSemanticReader.Feature(d, "color.adjustment");
            AtlasSemanticReader.ToggleSet(m, adjust, "autoHueShift", "_AutoShift", "monochromaticTint", "_MonoTint",
                "invert", "_Invert", "appliesToFinalColor", "_PostFiltering");
            AtlasSemanticReader.NumberSet(m, adjust, "autoHueSpeed", "_AutoShiftSpeed", "hdr", "_HDR", "toneMapping", "_ACES");
            AtlasSemanticReader.Vector(m, adjust, "rgbMultiplier", "_RGB");
            if (AtlasSemanticReader.Toggle(m, "_PostFiltering"))
                AtlasMaterialReader.MissingFeature(d, "color.adjustment",
                    "Filtering applies to the lit result; it's approximated as a base colour adjustment.");

            if (!AtlasSemanticReader.Toggle(m, "_TeamFiltering")) return;
            var teamMask = AtlasMaterialReader.Sample(m, "_TeamColorMask", AtlasColorInterpretation.LinearData, d);
            for (int i = 0; i < 4; ++i)
            {
                d.ColorMasks.Add(new AtlasColorMaskLayer {
                    Index = i, Enabled = teamMask != null,
                    Mask = teamMask == null ? null : new AtlasMaskDefinition { Sample = teamMask, Channel = (AtlasChannel)i },
                    Tint = AtlasMaterialReader.Color(m, "_TeamColor" + i, Color.white),
                    Blend = AtlasColorMaskBlendMode.Multiply
                });
            }
        }

        // Features without a typed home yet, reported once each when switched on.
        private static void Unmapped(Material m, AtlasUnifiedMaterial d)
        {
            if (Name(m).Contains("Outline"))
                AtlasMaterialReader.MissingFeature(d, "geometry.outline", "Outlines are not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_Refraction"))
                AtlasMaterialReader.MissingFeature(d, "layer.refraction", "Refraction is not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_Iridescence"))
                AtlasMaterialReader.MissingFeature(d, "layer.iridescence", "Iridescence is not mapped yet.");
            if (I(m, "_CubeMode", 0) != 0)
                AtlasMaterialReader.MissingFeature(d, "layer.cubemap", "Cubemap base colour is not mapped yet.");
            if (I(m, "_MirrorBehavior", 0) != 0)
                AtlasMaterialReader.MissingFeature(d, "render.visibility", "Mirror behaviour is not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_BCDissolveToggle"))
                AtlasMaterialReader.MissingFeature(d, "layer.transition", "Base colour dissolve is not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_EnableSpritesheet") || AtlasSemanticReader.Toggle(m, "_EnableSpritesheet1"))
                AtlasMaterialReader.MissingFeature(d, "layer.decal", "Flipbook layers are not mapped yet.");
            if (I(m, "_DistortionStyle", 0) != 0)
                AtlasMaterialReader.MissingFeature(d, "mapping.distortion", "UV distortion is not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_VertexManipulationToggle"))
                AtlasMaterialReader.MissingFeature(d, "geometry.deformation", "Vertex manipulation is not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_AudioLinkToggle"))
                AtlasMaterialReader.MissingFeature(d, "modulation.audio", "AudioLink effects are not mapped yet.");
            if (AtlasSemanticReader.Toggle(m, "_MetallicFiltering") || AtlasSemanticReader.Toggle(m, "_RoughnessFiltering") ||
                AtlasSemanticReader.Toggle(m, "_SmoothnessFiltering") || AtlasSemanticReader.Toggle(m, "_AOFiltering") ||
                AtlasSemanticReader.Toggle(m, "_HeightFiltering"))
                AtlasMaterialReader.MissingFeature(d, "surface.map-filters",
                    "Surface map filters (remap, lightness, contrast, occlusion tint) are not applied yet.");
            // The paid Uber X adds these; the free version's code is a stub, so they're unsupported.
            foreach (string toggle in new[] { "_DistanceFadeToggle", "_DissolveStyle", "_Screenspace", "_CloneToggle",
                         "_GlitchToggle", "_ShatterToggle", "_WireframeToggle" })
            {
                if (F(m, toggle, 0f) <= 0f) continue;
                AtlasMaterialReader.MissingFeature(d, "material.uber-x",
                    "Uber X special features are not supported; their source isn't available.");
                break;
            }
        }

        private static void Occlusion(Material m, AtlasUnifiedMaterial d, string texture, AtlasChannel channel)
        {
            var s = d.Surface;
            s.Occlusion.Sample = AtlasMaterialReader.Sample(m, texture, AtlasColorInterpretation.LinearData, d);
            s.Occlusion.Channel = channel;
            s.Occlusion.Constant = F(m, "_OcclusionStrength", 1f);
            s.Occlusion.CombineMode = AtlasScalarCombineMode.StrengthAroundOne;
        }

        // Uber's separate metallic and roughness maps replace their sliders and are read from red.
        private static void MapOrConstant(Material m, AtlasUnifiedMaterial d, AtlasScalarInput input, string texture,
            string useToggle, string slider, float fallback)
        {
            var sample = AtlasMaterialReader.Sample(m, texture, AtlasColorInterpretation.LinearData, d);
            bool useMap = sample != null && AtlasSemanticReader.Toggle(m, useToggle, true);
            input.Sample = useMap ? sample : null;
            input.Channel = AtlasChannel.Red;
            input.Constant = useMap ? 1f : F(m, slider, fallback);
        }

        private static AtlasMaskDefinition Mask(Material m, AtlasUnifiedMaterial d, UberMask mask)
        {
            int mode = I(m, "_MaskingMode", 0);
            if (mode == 1)
                return AtlasMaterialReader.Mask(m, d, SeparateMasks[(int)mask]);
            if (mode != 2) return null;
            int index = (int)mask;
            var packed = AtlasMaterialReader.Sample(m, "_PackedMask" + (index / 4), AtlasColorInterpretation.LinearData, d);
            if (packed == null) return null;
            // The specular-blend channel is stored inverted.
            return new AtlasMaskDefinition { Sample = packed, Channel = (AtlasChannel)(index % 4),
                Invert = mask == UberMask.SpecularBlend };
        }

        private static void AddMask(AtlasSemanticFeature feature, AtlasMaskDefinition mask)
        {
            if (mask != null) feature.Masks.Add(mask);
        }

        private static AtlasBlendOperation Blend7(int value)
        {
            switch (value)
            {
                case 0: return AtlasBlendOperation.Add;
                case 1: return AtlasBlendOperation.Subtract;
                case 2: return AtlasBlendOperation.Multiply;
                case 3: return AtlasBlendOperation.MultiplyDouble;
                case 4: return AtlasBlendOperation.Overlay;
                case 5: return AtlasBlendOperation.Screen;
                case 6: return AtlasBlendOperation.Replace;
                default: return AtlasBlendOperation.Unknown;
            }
        }

        private static AtlasChannel Channel(Material m, string property, AtlasChannel fallback)
        {
            if (!AtlasMaterialReader.Has(m, property)) return fallback;
            int v = I(m, property, 0);
            return v >= 0 && v <= 3 ? (AtlasChannel)v : fallback;
        }

        private static void SetNumber(AtlasLightingDefinition l, string key, float value)
            => l.Parameters[key] = new AtlasSemanticValue { Kind = AtlasSemanticValueKind.Number, Number = value };
        private static float F(Material m, string property, float fallback) => AtlasMaterialReader.Float(m, property, fallback);
        private static int I(Material m, string property, int fallback) => Mathf.RoundToInt(AtlasMaterialReader.Float(m, property, fallback));
    }
}
