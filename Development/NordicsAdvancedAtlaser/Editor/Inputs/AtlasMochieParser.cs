using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasMochieStandardParser : AtlasParserBase
    {
        public override string ParserId => "mochie-standard";
        public override string DisplayName => "Mochie/Standard";
        public override bool CanParse(Material m) => Name(m) == "Mochie/Standard";
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            AtlasMaterialReader.BaseColor(m,d,"_MainTex","_Color");
            int primaryWorkflow=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_PrimaryWorkflow",0f));
            bool specularWorkflow=primaryWorkflow==2;
            d.Surface.Model=specularWorkflow?AtlasShadingModel.StandardSpecular:AtlasShadingModel.StandardMetallic;
            d.Surface.Workflow=specularWorkflow?AtlasSurfaceWorkflow.Specular:AtlasSurfaceWorkflow.Metallic;
            d.Lighting.Model=d.Surface.Model;
            d.Lighting.Mode=specularWorkflow?AtlasLightingMode.StandardSpecular:AtlasLightingMode.StandardMetallic;
            d.Surface.ShadingMode=AtlasMaterialReader.Float(m,"_ShadingModel",0f)>0.5f?
                AtlasSurfaceShadingMode.Filament:AtlasSurfaceShadingMode.UnityStandard;
            int blendMode=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_BlendMode",0f));
            d.Render.Mode=blendMode==0?AtlasSurfaceMode.Opaque:blendMode==1?AtlasSurfaceMode.Cutout:
                blendMode==2?AtlasSurfaceMode.Transparent:blendMode==3?AtlasSurfaceMode.Premultiplied:AtlasSurfaceMode.Unknown;
            d.Render.Cull=(AtlasCullMode)Mathf.Clamp(Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_Culling",2)),0,2);
            d.Render.DoubleSided=d.Render.Cull==AtlasCullMode.Off;
            d.Render.ZWrite=AtlasSemanticReader.Toggle(m,"_ZWrite",true);
            d.Render.AlphaCutoff=AtlasMaterialReader.Float(m,"_Cutoff",0.5f);
            d.Render.AlphaToCoverage=AtlasSemanticReader.Toggle(m,"_AlphaToMask");
            d.Surface.UseVertexColors=AtlasSemanticReader.Toggle(m,"_VertexBaseColor");
            d.Render.ApplyVertexColor=d.Surface.UseVertexColors;
            d.Surface.SpecularHighlights=AtlasSemanticReader.Toggle(m,"_SpecularHighlightsToggle",true);
            d.Surface.GlossyReflections=AtlasSemanticReader.Toggle(m,"_ReflectionsToggle",true);
            d.Surface.GeometricSpecularAA=AtlasSemanticReader.Toggle(m,"_GSAAToggle");

            var normal=AtlasMaterialReader.Normal(m,d,"_NormalMap","_NormalStrength");
            AtlasMaterialReader.Metallic(m,d,"_MetallicStrength","_MetallicMap");
            AtlasMaterialReader.Specular(m,d,"_SpecCol","_SpecGlossMap");
            AtlasMaterialReader.Roughness(m,d,"_RoughnessStrength","_RoughnessMap",
                AtlasSemanticReader.Toggle(m,"_SmoothnessToggle"),AtlasChannel.Red);
            AtlasMaterialReader.Occlusion(m,d,"_OcclusionStrength","_OcclusionMap",AtlasChannel.Green);
            if(d.Surface.Occlusion.Sample!=null) d.Surface.Occlusion.CombineMode=AtlasScalarCombineMode.StrengthAroundOne;
            d.Surface.Metallic.Channel=ReadChannel(m,"_MetallicChannel",AtlasChannel.Red);
            d.Surface.Roughness.Channel=ReadChannel(m,"_RoughnessChannel",AtlasChannel.Red);
            d.Surface.Occlusion.Channel=ReadChannel(m,"_OcclusionChannel",AtlasChannel.Green);
            d.Surface.PackedSurfaceMap=AtlasMaterialReader.Sample(m,"_PackedMap",AtlasColorInterpretation.LinearData,d);
            if(primaryWorkflow==1 && d.Surface.PackedSurfaceMap!=null)
            {
                d.Surface.Metallic.Sample=d.Surface.PackedSurfaceMap;
                d.Surface.Roughness.Sample=d.Surface.PackedSurfaceMap;
                d.Surface.Occlusion.Sample=d.Surface.PackedSurfaceMap;
                d.Surface.Metallic.Constant=AtlasMaterialReader.Float(m,"_PackedMetallicStrength",1f);
                d.Surface.Roughness.Constant=AtlasMaterialReader.Float(m,"_PackedRoughnessStrength",1f);
                d.Surface.Occlusion.Constant=AtlasMaterialReader.Float(m,"_PackedOcclusionStrength",1f);
            }
            if(primaryWorkflow==2 && AtlasMaterialReader.Float(m,"_SmoothnessSource",0f)>0.5f)
            {
                d.Surface.Roughness.Sample=d.Surface.Specular.Map;
                d.Surface.Roughness.Channel=AtlasChannel.Alpha;
                d.Surface.Roughness.InvertResult=true;
            }
            d.Surface.Height.Sample=AtlasMaterialReader.Sample(m,"_HeightMap",AtlasColorInterpretation.LinearData,d);
            d.Surface.Height.Constant=AtlasMaterialReader.Float(m,"_HeightStrength",0f);
            d.Surface.Height.Channel=ReadChannel(m,"_HeightChannel",AtlasChannel.Red);
            d.Surface.HeightOffset=AtlasMaterialReader.Float(m,"_HeightOffset",0f);
            d.Surface.HeightSteps=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_HeightSteps",0));
            d.Surface.HasHeightMap=d.Surface.Height.Sample!=null;
            if(!AtlasSemanticReader.Toggle(m,"_SampleMetallic",true) && primaryWorkflow==0) d.Surface.Metallic.Sample=null;
            if(!AtlasSemanticReader.Toggle(m,"_SampleRoughness",true) && primaryWorkflow==0) d.Surface.Roughness.Sample=null;
            if(!AtlasSemanticReader.Toggle(m,"_SampleOcclusion",true) && primaryWorkflow==0) d.Surface.Occlusion.Sample=null;
            AtlasTextureSample main=d.Surface.BaseColor.Map;
            if(main!=null)
            {
                main.Mapping.UVIndex=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_UVMainSet",0));
                main.Mapping.RotationDegrees=AtlasMaterialReader.Float(m,"_UVMainRotation",0f);
                if(AtlasMaterialReader.Has(m,"_UVMainScroll"))
                {
                    Vector4 scroll=m.GetVector("_UVMainScroll");
                    main.Mapping.ScrollSpeed=new Vector2(scroll.x,scroll.y);
                }
                int samplingMode=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_PrimarySampleMode",0));
                if(samplingMode==1) main.Mapping.Sampling=AtlasSampleFilter.Stochastic;
                else if(samplingMode==2) main.Mapping.Sampling=AtlasSampleFilter.Supersampled;
                else if(samplingMode==3)
                {
                    main.Mapping.Space=AtlasCoordinateSpace.Triplanar;
                    main.Mapping.TriplanarBaseSpace=AtlasMaterialReader.Float(m,"_TriplanarCoordSpace",0)>0.5f?
                        AtlasCoordinateSpace.WorldPosition:AtlasCoordinateSpace.ObjectPosition;
                }
                if(AtlasSemanticReader.Toggle(m,"_BicubicSampling"))main.Mapping.Sampling=AtlasSampleFilter.Bicubic;
            }
            // Sampling mode, swizzle, and triplanar selection can modify all source maps;
            // these are retained as semantic controls, not silently guessed UV0 transforms.
            var sampling=AtlasSemanticReader.Feature(d,"texture.sampling");
            AtlasSemanticReader.NumberSet(m,sampling,"uvSwizzle","_UVMainSwizzle","mipScale","_MipMapScale");
            AtlasSemanticReader.DeclaredChoice(m,d,sampling,"primaryMode","_PrimarySampleMode");
            AtlasSemanticReader.DeclaredChoice(m,d,sampling,"triplanarSpace","_TriplanarCoordSpace");
            AtlasSemanticReader.DeclaredChoice(m,d,sampling,"mipRescale","_MipMapRescaling");
            AtlasSemanticReader.ToggleSet(m,sampling,"smoothnessInput","_SmoothnessToggle");
            sampling.Quality=AtlasNormalizationQuality.Approximate;

            var emission=AtlasMaterialReader.Emission(m,d,"_EmissionMap","_EmissionColor","_EmissionStrength");
            if(emission!=null) emission.Enabled=emission.Intensity!=0f;
            var detail=AtlasMaterialReader.Detail(m,d,"_DetailMainTex","_DetailMainTexStrength","_DetailColor",mask:"_DetailMask");
            var detailNormal=AtlasMaterialReader.Normal(m,d,"_DetailNormalMap","_DetailNormalStrength",maskProperty:"_DetailMask");
            int uvDetail=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_UVDetailSet",0));
            if(detail!=null)
            {
                detail.Map.Mapping.UVIndex=uvDetail;
                int blend=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_DetailMainTexBlend",2f));
                detail.DetailBlend=blend==0?AtlasDetailBlendMode.Add:blend==1?AtlasDetailBlendMode.Alpha:
                    blend==2?AtlasDetailBlendMode.Multiply:blend==3?AtlasDetailBlendMode.MultiplyDouble:
                    blend==4?AtlasDetailBlendMode.Overlay:blend==5?AtlasDetailBlendMode.Screen:
                    blend==6?AtlasDetailBlendMode.Lerp:AtlasDetailBlendMode.Unknown;
            }
            if(detailNormal!=null){detailNormal.Map.Mapping.UVIndex=uvDetail;detailNormal.NormalBlend=AtlasNormalBlend.Whiteout;}
            var detailSurface=AtlasSemanticReader.Feature(d,"surface.detail");
            AtlasSemanticReader.Texture(m,d,detailSurface,"metallic","_DetailMetallicMap");
            AtlasSemanticReader.Texture(m,d,detailSurface,"roughness","_DetailRoughnessMap");
            AtlasSemanticReader.Texture(m,d,detailSurface,"occlusion","_DetailOcclusionMap");
            AtlasSemanticReader.Texture(m,d,detailSurface,"packed","_DetailPackedMap");
            AtlasSemanticReader.NumberSet(m,detailSurface,
                "metallicStrength","_DetailMetallicStrength","roughnessStrength","_DetailRoughnessStrength",
                "occlusionStrength","_DetailOcclusionStrength","uvSet","_UVDetailSet",
                "uvRotation","_UVDetailRotation","metallicChannel","_DetailMetallicChannel","roughnessChannel","_DetailRoughnessChannel",
                "occlusionChannel","_DetailOcclusionChannel");
            AtlasSemanticReader.Vector(m,detailSurface,"scroll","_UVDetailScroll");
            AtlasSemanticReader.DeclaredChoice(m,d,detailSurface,"maskMode","_DetailMaskMode");
            AtlasSemanticReader.DeclaredChoice(m,d,detailSurface,"workflow","_DetailWorkflow");
            AtlasSemanticReader.DeclaredChoice(m,d,detailSurface,"sampling","_DetailSampleMode");
            AtlasSemanticReader.DeclaredChoice(m,d,detailSurface,"metallicBlend","_DetailMetallicBlend");
            AtlasSemanticReader.DeclaredChoice(m,d,detailSurface,"roughnessBlend","_DetailRoughnessBlend");
            AtlasSemanticReader.DeclaredChoice(m,d,detailSurface,"occlusionBlend","_DetailOcclusionBlend");
            // Detail scalar blend selectors have source-specific semantics until checked against shader includes.
            detailSurface.Quality=AtlasNormalizationQuality.Approximate;

            ReadMasks(m,d);
            ReadReflections(m,d);
            ReadSubsurface(m,d);
            ReadWeather(m,d);
            ReadColors(m,d);
            ReadExternalLighting(m,d);
            ReadAuxiliary(m,d);
        }
        private static AtlasChannel ReadChannel(Material m,string prop,AtlasChannel fallback)
        {
            if(!AtlasMaterialReader.Has(m,prop))return fallback;
            int v=Mathf.RoundToInt(AtlasMaterialReader.Float(m,prop,0));
            return v==0?AtlasChannel.Red:v==1?AtlasChannel.Green:v==2?AtlasChannel.Blue:
                v==3?AtlasChannel.Alpha:fallback;
        }
        private static void ReadMasks(Material m,AtlasUnifiedMaterial d)
        {
            Mask(m,d,"_AlphaMask","_AlphaMaskChannel",AtlasMaskPurpose.Alpha);
            Mask(m,d,"_EmissionMask","_EmissionMaskChannel",AtlasMaskPurpose.General);
            Mask(m,d,"_HeightMask","_HeightMaskChannel",AtlasMaskPurpose.General);
            Mask(m,d,"_RainMask","_RainMaskChannel",AtlasMaskPurpose.General);
            Mask(m,d,"_VertexMask","_VertexMaskChannel",AtlasMaskPurpose.General);
            var maskSettings=AtlasSemanticReader.Feature(d,"mask.sampling");
            AtlasSemanticReader.NumberSet(m,maskSettings,"alphaOpacity","_AlphaMaskOpacity");
            string[] kinds={"AlphaMask","EmissionMask","HeightMask","RainMask","VertexMask","DetailMask"};
            foreach(var kind in kinds)
            {
                string key=char.ToLowerInvariant(kind[0])+kind.Substring(1);
                AtlasSemanticReader.Number(m,maskSettings,key+".uvSet","_UV"+kind+"Set");
                AtlasSemanticReader.Number(m,maskSettings,key+".swizzle","_UV"+kind+"Swizzle");
                AtlasSemanticReader.Number(m,maskSettings,key+".rotation","_UV"+kind+"Rotation");
                AtlasSemanticReader.Vector(m,maskSettings,key+".scroll","_UV"+kind+"Scroll");
            }
        }
        private static void Mask(Material m,AtlasUnifiedMaterial d,string name,string channel,AtlasMaskPurpose purpose)
        {
            var mask=AtlasMaterialReader.Mask(m,d,name,channel);
            if(mask==null)return;
            d.Masks.Add(new AtlasNamedMask {Order=d.Masks.Count,Map=mask.Sample,Channel=mask.Channel,
                Invert=mask.Invert,Purpose=purpose});
            if(purpose==AtlasMaskPurpose.Alpha)d.Surface.AlphaMask=mask;
        }
        private static void ReadReflections(Material m,AtlasUnifiedMaterial d)
        {
            var reflection=AtlasSemanticReader.Feature(d,"surface.reflections",AtlasSemanticReader.Toggle(m,"_ReflectionsToggle"));
            AtlasSemanticReader.ToggleSet(m,reflection,"fresnel","_FresnelToggle","screenSpace","_SSRToggle",
                "vrScreenSpace","_VRSSR","specularOcclusion","_SpecularOcclusionToggle");
            AtlasSemanticReader.NumberSet(m,reflection,
                "strength","_ReflectionStrength","specularStrength","_SpecularHighlightStrength",
                "fresnelStrength","_FresnelStrength","screenSpaceStrength","_SSRStrength",
                "screenSpaceEdgeFade","_SSREdgeFade","screenSpaceHeight","_SSRHeight",
                "contactHardening","_ContactHardening","geometricAA","_GSAAStrength",
                "specularOcclusionStrength","_SpecularOcclusionStrength",
                "specularOcclusionContrast","_SpecularOcclusionContrast",
                "specularOcclusionBrightness","_SpecularOcclusionBrightness",
                "specularOcclusionHDR","_SpecularOcclusionHDR",
                "indirectOcclusionStrength","_IndirectSpecularOcclusionStrength",
                "realtimeOcclusionStrength","_RealtimeSpecularOcclusionStrength");
            AtlasSemanticReader.Color(m,reflection,"specularOcclusionTint","_SpecularOcclusionTint");
        }
        private static void ReadSubsurface(Material m,AtlasUnifiedMaterial d)
        {
            if(!AtlasMaterialReader.Has(m,"_Subsurface"))return;
            var layer=new AtlasSubsurfaceLayer {
                Order=d.Subsurfaces.Count,Enabled=AtlasSemanticReader.Toggle(m,"_Subsurface"),
                Map=AtlasMaterialReader.Sample(m,"_ThicknessMap",AtlasColorInterpretation.LinearData,d),
                Tint=AtlasMaterialReader.Color(m,"_ScatterCol",Color.white),
                Opacity=AtlasMaterialReader.Float(m,"_ScatterIntensity",1f),
                Thickness=AtlasMaterialReader.Float(m,"_ThicknessMapPower",1f)
            };
            d.Subsurfaces.Add(layer);
            var feature=AtlasSemanticReader.Feature(d,"lighting.subsurface");
            AtlasSemanticReader.NumberSet(m,feature,"power","_ScatterPow","distance","_ScatterDist",
                "ambient","_ScatterAmbient","shadow","_ScatterShadow","wrap","_WrappingFactor");
            AtlasSemanticReader.ToggleSet(m,feature,"tintByBaseColor","_ScatterBaseColorTint");
        }
        private static void ReadWeather(Material m,AtlasUnifiedMaterial d)
        {
            var wind=AtlasSemanticReader.Feature(d,"geometry.wind",AtlasSemanticReader.Toggle(m,"_WindToggle"));
            AtlasSemanticReader.NumberSet(m,wind,"strength","_WindStrength","layers","_WindLayers");
            AtlasSemanticReader.DeclaredChoice(m,d,wind,"maskingMode","_WindMaskingMode");
            AtlasSemanticReader.DeclaredChoice(m,d,wind,"symmetry","_WindSymmetry");
            AtlasSemanticReader.Texture(m,d,wind,"noise","_WindNoiseTex");
            AtlasSemanticReader.Vector(m,wind,"direction","_WindDirection");
            for(int i=0;i<3;i++)
            {
                string s=i.ToString();
                AtlasSemanticReader.Vector(m,wind,"layer"+s+".scale","_WindScale"+s);
                AtlasSemanticReader.Vector(m,wind,"layer"+s+".speed","_WindSpeed"+s);
                AtlasSemanticReader.Number(m,wind,"layer"+s+".strength","_WindContribution"+s);
                AtlasSemanticReader.Number(m,wind,"layer"+s+".smoothness","_WindSmoothness"+s);
            }
            var rain=AtlasSemanticReader.Feature(d,"environment.rain",AtlasMaterialReader.Float(m,"_RainMode",0)>0f);
            AtlasSemanticReader.NumberSet(m,rain,"speed","_RainSpeed","strength","_RainStrength",
                "dynamicDroplets","_DynamicDroplets","threshold","_RainThreshold","thresholdSize","_RainThresholdSize",
                "rippleSpeed","_RippleSpeed","rippleStrength","_RippleStrength",
                "rippleDensity","_RippleDensity","rippleSize","_RippleSize",
                "rippleHorizon","_RippleHorizonAdjustment","horizonDistance","_RippleHorizonAdjustmentDistance");
            AtlasSemanticReader.DeclaredChoice(m,d,rain,"mode","_RainMode");
            AtlasSemanticReader.Vector(m,rain,"scale","_RainScale");
            AtlasSemanticReader.Vector(m,rain,"rippleScale","_RippleScale");
            AtlasSemanticReader.Texture(m,d,rain,"dropletMask","_DropletMask");
            var puddle=AtlasSemanticReader.Feature(d,"environment.puddles",AtlasSemanticReader.Toggle(m,"_PuddleToggle"));
            AtlasSemanticReader.Texture(m,d,puddle,"surface","_PuddleTexture",AtlasColorInterpretation.ColorSRGB);
            AtlasSemanticReader.Color(m,puddle,"tint","_PuddleTint");
            AtlasSemanticReader.NumberSet(m,puddle,"strength","_PuddleStrength","metallic","_PuddleMetallic",
                "occlusionStrength","_PuddleOcclusionStrength","tintDepth","_PuddleTintDepth",
                "heightStrength","_PuddleHeightStrength","minimumThreshold","_PuddleThresholdMin",
                "maximumThreshold","_PuddleThresholdMax","rippleStrength","_PuddleRippleStrength",
                "rippleSpeed","_PuddleRippleSpeed","rippleSize","_PuddleRippleSize","rippleDensity","_PuddleRippleDensity");
        }
        private static void ReadColors(Material m,AtlasUnifiedMaterial d)
        {
            d.ColorProcessing.HueShift=AtlasMaterialReader.Float(m,"_Hue",0f);
            d.ColorProcessing.Saturation=AtlasMaterialReader.Float(m,"_Saturation",1f);
            d.ColorProcessing.Contrast=AtlasMaterialReader.Float(m,"_Contrast",1f);
            d.ColorProcessing.BrightnessMultiplier=AtlasMaterialReader.Float(m,"_Brightness",1f);
            d.ColorProcessing.HueColorSpace=AtlasMaterialReader.Float(m,"_HueMode",0f)>0.5f?
                AtlasHueColorSpace.Oklab:AtlasHueColorSpace.HSV;
            var color=AtlasSemanticReader.Feature(d,"color.adjustment");
            AtlasSemanticReader.NumberSet(m,color,"hueMode","_HueMode","monochromaticTint","_MonoTint",
                "detailHue","_HueDet","detailContrast","_ContrastDet","detailBrightness","_BrightnessDet",
                "detailSaturation","_SaturationDet","emissionHue","_HueEmiss",
                "emissionContrast","_ContrastEmiss","emissionSaturation","_SaturationEmiss",
                "emissionBrightness","_BrightnessEmiss","postHue","_HuePost",
                "postContrast","_ContrastPost","postSaturation","_SaturationPost",
                "postBrightness","_BrightnessPost","aces","_ACES","lookupStrength","_ColorGradingLUTStrength");
            AtlasSemanticReader.Texture(m,d,color,"gradingLookup","_ColorGradingLUT",AtlasColorInterpretation.ColorSRGB);
            var emission=AtlasSemanticReader.Feature(d,"emission.modulation");
            AtlasSemanticReader.NumberSet(m,emission,"pulseMode","_EmissionPulseWave","pulseSpeed","_EmissionPulseSpeed",
                "pulseStrength","_EmissionPulseStrength","audioBand","_AudioLinkEmission",
                "audioStrength","_AudioLinkEmissionStrength","audioMetadata","_AudioLinkEmissionMeta",
                "audioMinimum","_AudioLinkMin","audioMaximum","_AudioLinkMax");
        }
        private static void ReadExternalLighting(Material m,AtlasUnifiedMaterial d)
        {
            d.ExternalLighting.EnableLightVolumes=AtlasSemanticReader.Toggle(m,"_LightVolumesToggle");
            d.ExternalLighting.EnableAreaLit=AtlasSemanticReader.Toggle(m,"_AreaLitToggle");
            d.ExternalLighting.EnableLTCGI=AtlasSemanticReader.Toggle(m,"_LTCGI");
            d.ExternalLighting.IgnoreRealtimeGI=AtlasSemanticReader.Toggle(m,"_IgnoreRealtimeGI");
            d.ExternalLighting.LightVolumeIntensity=AtlasMaterialReader.Float(m,"_LightVolumeStrength",1f);
            d.ExternalLighting.AreaLitStrength=AtlasMaterialReader.Float(m,"_AreaLitStrength",1f);
            d.ExternalLighting.LTCGIStrength=AtlasMaterialReader.Float(m,"_LTCGIStrength",1f);
            var external=AtlasSemanticReader.Feature(d,"lighting.external");
            AtlasSemanticReader.NumberSet(m,external,"lightVolumeAdditiveStrength","_AdditiveLightVolumeStrength",
                "lightVolumeNormalBias","_LightVolumeBias","lightVolumeSpecularStrength","_LightVolumeSpecularityStrength",
                "areaLitOcclusionUV","_AreaLitOcclusionUVSet","areaLitSpecularOcclusion","_AreaLitSpecularOcclusion",
                "ltcgiRoughness","_LTCGIRoughness","bakeryMode","_BakeryMode",
                "bakerySpecularStrength","_BakeryLMSpecStrength");
            AtlasSemanticReader.ToggleSet(m,external,"additiveLightVolumes","_AdditiveLightVolumesToggle",
                "areaLitRoughnessMultiplier","_AreaLitRoughnessMultiplier","ltcgiSpecularOcclusion","_LTCGISpecularOcclusion",
                "lightVolumeSpecular","_LightVolumeSpecularity","bakerySHNonlinear","_BAKERY_SHNONLINEAR");
            AtlasSemanticReader.Color(m,external,"ltcgiDiffuseTint","_LTCGI_DiffuseColor");
            AtlasSemanticReader.Color(m,external,"ltcgiSpecularTint","_LTCGI_SpecularColor");
            foreach(string id in new[]{"_LightMesh","_LightTex0","_LightTex1","_LightTex2","_LightTex3","_AreaLitOcclusion","_LTCGI_mat","_LTCGI_amp","_RNM0","_RNM1","_RNM2"})
            {
                // Semantic keys are derived from the role within the external-lighting definition.
                string semantic=id.StartsWith("_LightTex")?"areaLit.texture"+id.Substring(9):
                    id.StartsWith("_RNM")?"bakery.directional"+id.Substring(4):
                    id=="_LightMesh"?"areaLit.mesh":id=="_AreaLitOcclusion"?"areaLit.occlusion":
                    id=="_LTCGI_mat"?"ltcgi.matrix":"ltcgi.amplitude";
                AtlasSemanticReader.Texture(m,d,external,semantic,id);
            }
        }
        private static void ReadAuxiliary(Material m,AtlasUnifiedMaterial d)
        {
            var render=AtlasSemanticReader.Feature(d,"render.additional");
            AtlasSemanticReader.NumberSet(m,render,"renderQueueOffset","_QueueOffset","depthTest","_ZTest");
            AtlasSemanticReader.DeclaredChoice(m,d,render,"blendMode","_BlendMode");
            AtlasSemanticReader.DeclaredChoice(m,d,render,"alphaSource","_AlphaSource");
            AtlasSemanticReader.DeclaredChoice(m,d,render,"primaryWorkflow","_PrimaryWorkflow");
            AtlasSemanticReader.DeclaredChoice(m,d,render,"specularModel","_ShadingModel");
            AtlasSemanticReader.ToggleSet(m,render,"fog","_UnityFogToggle","flipBackfaceNormals","_FlipBackfaceNormals");
            var surface=AtlasSemanticReader.Feature(d,"surface.channel-packing");
            AtlasSemanticReader.ToggleSet(m,surface,"heightFalloff","_HeightFalloff");
            AtlasSemanticReader.ToggleSet(m,surface,"sampleMetallic","_SampleMetallic",
                "sampleSpecular","_SampleSpecular","sampleRoughness","_SampleRoughness",
                "sampleOcclusion","_SampleOcclusion","packedHeight","_PackedHeight");
            AtlasSemanticReader.DeclaredChoice(m,d,surface,"smoothnessSource","_SmoothnessSource");
            AtlasSemanticReader.NumberSet(m,surface,"packedMetallicStrength","_PackedMetallicStrength",
                "packedRoughnessStrength","_PackedRoughnessStrength",
                "packedOcclusionStrength","_PackedOcclusionStrength",
                "heightMinSteps","_MinHeightSteps","heightMaxSteps","_MaxHeightSteps",
                "heightMinimumRange","_HeightMinRange","heightMaximumRange","_HeightMaxRange");
        }
    }
}
