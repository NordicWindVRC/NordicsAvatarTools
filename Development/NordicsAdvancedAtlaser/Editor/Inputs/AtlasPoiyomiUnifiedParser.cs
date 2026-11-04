using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Shared feature translators for Poiyomi Toon 7–10. Versions select property conventions,
    /// while the destination always uses the SAME union lighting/matcap/etc definitions.
    /// Complex features that still require shader-level analysis are explicitly flagged.
    /// </summary>
    public static class AtlasPoiyomiUnifiedParser
    {
        public static void Populate(Material m, AtlasUnifiedMaterial d, int major)
        {
            Lighting(m,d,major);
            Matcaps(m,d);
            Emissions(m,d);
            Surface(m,d);
            ColorProcessing(m,d,major);
            d.ExternalLighting.EnableLightVolumes=AtlasSemanticReader.Toggle(m,"_LightingEnableLightVolumes");
            var external=AtlasSemanticReader.Feature(d,"lighting.external");
            AtlasSemanticReader.ToggleSet(m,external,"lightVolumeSpecular","_LightingLightVolumesSpecular");
            AtlasSemanticReader.NumberSet(m,external,"lightVolumeNormalBias","_LightingLightVolumesNormalBias");
        }
        /// <summary>
        /// Converts Poiyomi's saturation and brightness to the unified scales. Verified against the
        /// 7.3, 8.1, 9.3 and 10.0 shader sources:
        /// - Saturation (all versions): 0 = unchanged, mixed toward luminance by -value.
        ///   Unified: 1 = unchanged, so 1 + value.
        /// - Brightness: 7.x and 8.x add it (color + value); 9.x and later multiply by
        ///   (1 + value). Unknown versions (0) use the newest convention.
        /// - 7.x to 9.x always clamp to 0–1 after brightness. 10.x clamps only when its
        ///   clamp toggle is on, which the advanced parser reads.
        /// </summary>
        public static void MapSaturationAndBrightness(AtlasColorProcessingDefinition color,int major,float saturation,float brightness)
        {
            color.Saturation=1f+saturation;
            bool additive=major==7||major==8;
            color.Brightness=additive?brightness:0f;
            color.BrightnessMultiplier=additive?1f:1f+brightness;
            if(major>=7&&major<=9)color.ClampOutput=true;
        }
        // Literal enum mappings verified against Poiyomi 7.3 and 8.1/9.3/10.0 shader sources.
        // Kept outside the unified model: only the parser knows the source numeric values.
        public static AtlasLightingMode ToLightingMode(int major,int value)
        {
            if(major==7)
            {
                switch(value)
                {
                    case 0:return AtlasLightingMode.LegacyToon;
                    case 1:return AtlasLightingMode.Realistic;
                    case 2:return AtlasLightingMode.Wrapped;
                    case 3:return AtlasLightingMode.Skin;
                    case 4:return AtlasLightingMode.Flat;
                    default:return AtlasLightingMode.Unknown;
                }
            }
            if(major>=8 && major<=10)
            {
                switch(value)
                {
                    case 0:return AtlasLightingMode.ToonRamp;
                    case 1:return AtlasLightingMode.ToonMultilayer;
                    case 2:return AtlasLightingMode.Wrapped;
                    case 3:return AtlasLightingMode.Skin;
                    case 4:return AtlasLightingMode.ShadeMap;
                    case 5:return AtlasLightingMode.Flat;
                    case 6:return AtlasLightingMode.Realistic;
                    case 7:return AtlasLightingMode.Cloth;
                    case 8:return AtlasLightingMode.SignedDistanceField;
                    default:return AtlasLightingMode.Unknown;
                }
            }
            return AtlasLightingMode.Unknown;
        }
        private static void Lighting(Material m, AtlasUnifiedMaterial d, int major)
        {
            var l=d.Lighting;
            int mode=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_LightingMode",-1));
            l.Mode=ToLightingMode(major,mode);
            if(l.Mode==AtlasLightingMode.Unknown)
                AtlasMaterialReader.MissingFeature(d,"lighting.mode","Lighting mode is not yet identified or has an unknown version-specific value.");

            l.ShadowStrength=AtlasMaterialReader.Float(m,"_ShadowStrength",1f);
            l.ShadowOffset=AtlasMaterialReader.Float(m,"_ShadowOffset",0f);
            l.ShadowBorder=AtlasMaterialReader.Float(m,"_ShadowBorder",0f);
            l.ShadowBlur=AtlasMaterialReader.Float(m,"_ShadowBlur",0f);
            l.ShadowColor=AtlasMaterialReader.Color(m,"_LightingShadowColor",
                AtlasMaterialReader.Color(m,"_ShadowColor",Color.white));
            l.MinimumBrightness=AtlasMaterialReader.Float(m,"_LightingMinLightBrightness",0f);
            l.LimitBrightness=AtlasSemanticReader.Toggle(m,"_LightingCapEnabled");
            l.MaximumBrightness=AtlasMaterialReader.Float(m,"_LightingCap",1f);
            l.WrapAmount=AtlasMaterialReader.Float(m,"_LightingWrappedWrap",0f);
            l.WrapNormalization=AtlasMaterialReader.Float(m,"_LightingWrappedNormalization",0f);
            l.ShadowSoftness=AtlasMaterialReader.Float(m,"_LightingShadowSoftness",0f);
            l.ReceiveShadows=AtlasSemanticReader.Toggle(m,"_LightingCastedShadows",true);
            l.UseNormalsForIndirect=AtlasSemanticReader.Toggle(m,"_LightingIndirectUsesNormals",true);
            l.Monochromatic=AtlasSemanticReader.Toggle(m,"_LightingMonochromatic");
            l.EnableAdditionalLights=AtlasSemanticReader.Toggle(m,"_LightingAdditiveEnable",true);
            l.LimitAdditionalLights=AtlasSemanticReader.Toggle(m,"_LightingAdditiveLimited");
            l.AdditionalLightLimit=AtlasMaterialReader.Float(m,"_LightingAdditiveLimit",1f);
            l.ShadowRamp=AtlasMaterialReader.Sample(m,"_LightingRamp",AtlasColorInterpretation.ColorSRGB,d);
            if(l.ShadowRamp==null)l.ShadowRamp=AtlasMaterialReader.Sample(m,"_LightingRampTex",AtlasColorInterpretation.ColorSRGB,d);
            // Consecutive shadow bands represent successive colors, not per-version configs.
            for(int i=0;i<3;i++)
            {
                string prefix=i==0?"_Shadow":i==1?"_Shadow2nd":"_Shadow3rd";
                if(!AtlasMaterialReader.Has(m,prefix+"Color") && !AtlasMaterialReader.Has(m,prefix+"ColorTex"))continue;
                l.ToonBands.Add(new AtlasLightBand {
                    Threshold=AtlasMaterialReader.Float(m,prefix+"Border",0f),
                    Softness=AtlasMaterialReader.Float(m,prefix+"Blur",0f),
                    Color=AtlasMaterialReader.Color(m,prefix+"Color",Color.white),
                    ColorMap=AtlasMaterialReader.Sample(m,prefix+"ColorTex",AtlasColorInterpretation.ColorSRGB,d)
                });
            }
            // Additional options stay in ONE lighting object, using reusable semantic property IDs.
            Put(l,"gradientStart",AtlasMaterialReader.Float(m,"_LightingGradientStart",0f),m,"_LightingGradientStart");
            Put(l,"gradientEnd",AtlasMaterialReader.Float(m,"_LightingGradientEnd",1f),m,"_LightingGradientEnd");
            Put(l,"additionalGradientStart",AtlasMaterialReader.Float(m,"_LightingAdditiveGradientStart",0f),m,"_LightingAdditiveGradientStart");
            Put(l,"additionalGradientEnd",AtlasMaterialReader.Float(m,"_LightingAdditiveGradientEnd",1f),m,"_LightingAdditiveGradientEnd");
            Put(l,"additionalPassthrough",AtlasMaterialReader.Float(m,"_LightingAdditivePassthrough",0f),m,"_LightingAdditivePassthrough");
            Put(l,"multilayerNonlinear",AtlasMaterialReader.Float(m,"_LightingMulitlayerNonLinear",0f),m,"_LightingMulitlayerNonLinear");
            Put(l,"shadowMainStrength",AtlasMaterialReader.Float(m,"_ShadowMainStrength",1f),m,"_ShadowMainStrength");
            Put(l,"shadowEnvironmentStrength",AtlasMaterialReader.Float(m,"_ShadowEnvStrength",1f),m,"_ShadowEnvStrength");
            Put(l,"shadowFlatBorder",AtlasMaterialReader.Float(m,"_ShadowFlatBorder",0f),m,"_ShadowFlatBorder");
            Put(l,"shadowFlatBlur",AtlasMaterialReader.Float(m,"_ShadowFlatBlur",0f),m,"_ShadowFlatBlur");
            PutEnum(l,"lightingColorMode",m,"_LightingColorMode",d);
            PutEnum(l,"lightingMapMode",m,"_LightingMapMode",d);
            PutEnum(l,"lightingDirectionMode",m,"_LightingDirectionMode",d);
            Put(l,"lightingCapLuminance",AtlasMaterialReader.Float(m,"_LightingCapLuminance",0f),m,"_LightingCapLuminance");
            Put(l,"castedShadowSoftness",AtlasMaterialReader.Float(m,"_LightingShadowSoftness",0f),m,"_LightingShadowSoftness");
            Put(l,"additionalAttenuationCurve",AtlasMaterialReader.Float(m,"_LightingAdditiveAttenCurve",0f),m,"_LightingAdditiveAttenCurve");
        }
        private static void PutEnum(AtlasLightingDefinition l,string key,Material m,
            string property,AtlasUnifiedMaterial d)
        {
            if(!AtlasMaterialReader.Has(m,property))return;
            string choice;
            if(AtlasSemanticReader.TryDeclaredChoice(m,property,out choice))
                l.Parameters[key]=new AtlasSemanticValue {Kind=AtlasSemanticValueKind.Choice,Choice=choice};
            else
                d.Issues.Add(new AtlasNormalizationIssue("lighting."+key,
                    AtlasNormalizationSeverity.Warning,
                    "Source lighting enum has no verified semantic option mapping."));
        }
        private static void Put(AtlasLightingDefinition l,string key,float val,Material m,string prop)
        {
            if(!AtlasMaterialReader.Has(m,prop))return;
            l.Parameters[key]=new AtlasSemanticValue {Kind=AtlasSemanticValueKind.Number,Number=val};
        }
        private static void Matcaps(Material m,AtlasUnifiedMaterial d)
        {
            for(int index=0;index<4;index++)
            {
                string prefix=index==0?"_Matcap":"_Matcap"+(index+1);
                if(!AtlasMaterialReader.Has(m,prefix))continue;
                // Each physical slot produces one semantic layer, even if previous slots are empty.
                AtlasMatcapLayer layer=AtlasMaterialReader.Matcap(m,d,prefix,
                    prefix+"Intensity",prefix+"Enable",prefix+"Mask");
                if(layer==null)continue;
                layer.Tint=AtlasMaterialReader.Color(m,prefix+"Color",Color.white);
                layer.Intensity=AtlasMaterialReader.Float(m,prefix+"Intensity",1f);
                layer.Border=AtlasMaterialReader.Float(m,prefix+"Border",0f);
                layer.Rotation=AtlasMaterialReader.Float(m,prefix+"Rotation",0f);
                layer.BaseColorMix=AtlasMaterialReader.Float(m,prefix+"BaseColorMix",0f);
                layer.LightColorMix=AtlasMaterialReader.Float(m,prefix+"LightColorMix",0f);
                layer.EmissionStrength=AtlasMaterialReader.Float(m,prefix+"EmissionStrength",0f);
                layer.HideInShadow=AtlasMaterialReader.Float(m,prefix+"LightMask",0f);
                layer.HideWithReflectionProbe=AtlasSemanticReader.Toggle(m,prefix+"HideWithReflProbe");
                layer.Mask=AtlasMaterialReader.Mask(m,d,prefix+"Mask",prefix+"MaskChannel",prefix+"MaskInvert");
                layer.CustomNormal=AtlasMaterialReader.Sample(m,"_Matcap"+index+"NormalMap",AtlasColorInterpretation.TangentNormal,d);
                layer.CustomNormalStrength=AtlasMaterialReader.Float(m,"_Matcap"+index+"NormalMapScale",1f);
                int uvMode=Mathf.RoundToInt(AtlasMaterialReader.Float(m,prefix+"UVMode",0f));
                switch(uvMode)
                {
                    case 0:layer.UVMode=AtlasMatcapUVMode.UTSStyle;break;
                    case 1:layer.UVMode=AtlasMatcapUVMode.TopPinch;break;
                    case 2:layer.UVMode=AtlasMatcapUVMode.DoubleSided;break;
                    case 3:layer.UVMode=AtlasMatcapUVMode.Gradient;break;
                    case 4:layer.UVMode=AtlasMatcapUVMode.LightDirection;break;
                    default:layer.UVMode=AtlasMatcapUVMode.Unknown;break;
                }
                layer.BlendWeights.Replace=AtlasMaterialReader.Float(m,prefix+"Replace",0f);
                layer.BlendWeights.Multiply=AtlasMaterialReader.Float(m,prefix+"Multiply",0f);
                layer.BlendWeights.Add=AtlasMaterialReader.Float(m,prefix+"Add",0f);
                layer.BlendWeights.Mixed=AtlasMaterialReader.Float(m,prefix+"Mixed",0f);
                layer.BlendWeights.Screen=AtlasMaterialReader.Float(m,prefix+"Screen",0f);
                layer.BlendWeights.UnlitAdd=AtlasMaterialReader.Float(m,prefix+"AddToLight",0f);
                // Poiyomi's blend weights are simultaneous, not a single enum.
                // The full weighted behavior is preserved even if Blend is Unknown.
                layer.Quality=layer.UVMode==AtlasMatcapUVMode.Unknown?AtlasNormalizationQuality.Unresolved:AtlasNormalizationQuality.Approximate;
                if (layer.BlendWeights.Screen>0f && layer.BlendWeights.Replace==0f &&
                    layer.BlendWeights.Multiply==0f && layer.BlendWeights.Add==0f && layer.BlendWeights.Mixed==0f)
                    layer.Blend=AtlasBlendOperation.Screen;
                else if(layer.BlendWeights.Replace>0f && layer.BlendWeights.Multiply==0f &&
                    layer.BlendWeights.Add==0f && layer.BlendWeights.Mixed==0f && layer.BlendWeights.Screen==0f)
                    layer.Blend=AtlasBlendOperation.Replace;
                else if(layer.BlendWeights.Multiply>0f && layer.BlendWeights.Add==0f &&
                    layer.BlendWeights.Mixed==0f && layer.BlendWeights.Screen==0f)
                    layer.Blend=AtlasBlendOperation.Multiply;
                else if(layer.BlendWeights.Add>0f && layer.BlendWeights.Mixed==0f && layer.BlendWeights.Screen==0f)
                    layer.Blend=AtlasBlendOperation.Add;
                else layer.Blend=AtlasBlendOperation.Unknown;
            }
        }
        private static void Emissions(Material m,AtlasUnifiedMaterial d)
        {
            for(int i=0;i<4;i++)
            {
                string s=i==0?"":i.ToString();
                string tex="_EmissionMap"+s;
                if(!AtlasMaterialReader.Has(m,tex))continue;
                var layer=AtlasMaterialReader.Emission(m,d,tex,"_EmissionColor"+s,
                    "_EmissionStrength"+s,"_EnableEmission"+s,"_EmissionMapUV"+s,true,"_EmissionMask"+s);
                if(layer==null)continue;
                layer.UseBaseColorAsMap=AtlasSemanticReader.Toggle(m,"_EmissionBaseColorAsMap"+s);
                layer.ReplaceBaseColor=AtlasSemanticReader.Toggle(m,"_EmissionReplace"+s);
                layer.HueShift=AtlasMaterialReader.Float(m,"_EmissionHueShift"+s,0f);
                layer.Saturation=AtlasMaterialReader.Float(m,"_EmissionSaturation"+s,1f);
                layer.BlinkEnabled=AtlasSemanticReader.Toggle(m,"_EmissionBlinkingEnabled"+s);
                layer.BlinkOffset=AtlasMaterialReader.Float(m,"_EmissionBlinkingOffset"+s,0f);
                layer.AudioReactive=AtlasSemanticReader.Toggle(m,"_EmissionAL"+i+"Enabled");
                layer.Mask=AtlasMaterialReader.Mask(m,d,"_EmissionMask"+s,
                    "_EmissionMask"+s+"Channel","_EmissionMaskInvert"+s);
            }
        }
        private static void Surface(Material m,AtlasUnifiedMaterial d)
        {
            var render=AtlasSemanticReader.Feature(d,"render.alpha");
            AtlasSemanticReader.ToggleSet(m,render,"forceOpaque","_AlphaForceOpaque",
                "alphaDithering","_AlphaDithering","alphaToCoverage","_AlphaToCoverage",
                "distanceFade","_AlphaDistanceFade","fresnelAlpha","_AlphaFresnel");
            AtlasSemanticReader.NumberSet(m,render,"alphaModifier","_AlphaMod",
                "alphaMipScale","_AlphaMipScale","alphaDitherGradient","_AlphaDitherGradient",
                "distanceMin","_AlphaDistanceFadeMin","distanceMax","_AlphaDistanceFadeMax");
            d.Render.AlphaToCoverage=AtlasSemanticReader.Toggle(m,"_AlphaToCoverage");
            d.Render.AlphaPremultiplied=AtlasSemanticReader.Toggle(m,"_AlphaPremultiply");
            var surface=AtlasSemanticReader.Feature(d,"surface.specular");
            AtlasSemanticReader.Texture(m,d,surface,"packedMetallic","_MochieMetallicMaps");
            AtlasSemanticReader.NumberSet(m,surface,"specularStrength","_MochieSpecularStrength",
                "metallicMultiplier","_MochieMetallicMultiplier");
            AtlasSemanticReader.Color(m,surface,"specularTint","_MochieSpecularTint");
            var clearcoat=AtlasSemanticReader.Feature(d,"surface.clearcoat",AtlasSemanticReader.Toggle(m,"_ClearCoatBRDF"));
            AtlasSemanticReader.NumberSet(m,clearcoat,"strength","_ClearCoatStrength",
                "smoothness","_ClearCoatSmoothness","reflectionStrength","_ClearCoatReflectionStrength",
                "specularStrength","_ClearCoatSpecularStrength");
            AtlasSemanticReader.Texture(m,d,clearcoat,"packedMask","_ClearCoatMaps");
            var reflections=AtlasSemanticReader.Feature(d,"surface.reflections");
            AtlasSemanticReader.Texture(m,d,reflections,"cubemap","_CubeMap");
            AtlasSemanticReader.NumberSet(m,reflections,"cubemapSmoothness","_CubeMapSmoothness");
        }
        private static void ColorProcessing(Material m,AtlasUnifiedMaterial d,int major)
        {
            d.ColorProcessing.HueShift=AtlasMaterialReader.Float(m,"_MainHueShift",0f);
            MapSaturationAndBrightness(d.ColorProcessing,major,
                AtlasMaterialReader.Float(m,"_Saturation",0f),AtlasMaterialReader.Float(m,"_MainBrightness",0f));
            d.ColorProcessing.Gamma=AtlasMaterialReader.Float(m,"_MainGamma",1f);
            var feature=AtlasSemanticReader.Feature(d,"color.adjustment",AtlasSemanticReader.Toggle(m,"_MainColorAdjustToggle"));
            AtlasSemanticReader.Texture(m,d,feature,"mask","_MainColorAdjustTexture");
            AtlasSemanticReader.Texture(m,d,feature,"tintTexture","_MainTintTexture",AtlasColorInterpretation.ColorSRGB);
            AtlasSemanticReader.Color(m,feature,"tint","_MainTintColor");
            AtlasSemanticReader.NumberSet(m,feature,"chromatize","_MainChromatize",
                "hueShiftSpeed","_MainHueShiftSpeed","gradationStrength","_MainGradationStrength");
        }
    }
}
