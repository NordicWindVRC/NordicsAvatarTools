using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasVRChatStandardLiteParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-standardlite";
        public override string DisplayName => "VRChat/Mobile/Standard Lite";
        public override bool CanParse(Material m) => Name(m) == "VRChat/Mobile/Standard Lite";
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            base.Populate(m,d);
            AtlasStandardFeatureParser.Populate(m,d,false,false,true);
        }
    }

    public sealed class AtlasVRChatToonStandardParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-toonstandard";
        public override string DisplayName => "VRChat/Mobile/Toon Standard";
        public override bool CanParse(Material m) => Name(m) == "VRChat/Mobile/Toon Standard" || Name(m) == "VRChat/Mobile/Toon Standard (Outline)";
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            base.Populate(m,d);
            d.Surface.Model=AtlasShadingModel.Toon;
            d.Lighting.Model=AtlasShadingModel.Toon;
            d.Lighting.Mode=AtlasLightingMode.ToonRamp;
            d.Lighting.ShadowRamp=AtlasMaterialReader.Sample(m,"_Ramp",AtlasColorInterpretation.ColorSRGB,d);
            d.Lighting.ShadowBoost=AtlasMaterialReader.Float(m,"_ShadowBoost",0f);
            d.Lighting.ShadowAlbedoTint=AtlasMaterialReader.Float(m,"_ShadowAlbedo",0f);
            d.Lighting.MinimumBrightness=AtlasMaterialReader.Float(m,"_MinBrightness",0f);
            d.Lighting.LimitBrightness=AtlasSemanticReader.Toggle(m,"_LimitBrightness");
            if (AtlasMaterialReader.Has(m,"_Culling"))
            {
                int cull=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_Culling",2));
                d.Render.Cull=cull==0?AtlasCullMode.Off:cull==1?AtlasCullMode.Front:AtlasCullMode.Back;
                d.Render.DoubleSided=cull==0;
            }
            d.Surface.UseVertexColors=AtlasSemanticReader.Toggle(m,"_VertexColor");
            d.Render.ApplyVertexColor=d.Surface.UseVertexColors;
            d.Surface.Metallic.Constant=AtlasMaterialReader.Float(m,"_MetallicStrength",0f);
            d.Surface.Metallic.Sample=AtlasMaterialReader.Sample(m,"_MetallicMap",AtlasColorInterpretation.LinearData,d);
            d.Surface.Metallic.Channel=AtlasMobileToonChannel(m,"_MetallicMapChannel");
            d.Surface.Roughness.Constant=AtlasMaterialReader.Float(m,"_GlossStrength",0.5f);
            d.Surface.Roughness.Sample=AtlasMaterialReader.Sample(m,"_GlossMap",AtlasColorInterpretation.LinearData,d);
            d.Surface.Roughness.Channel=AtlasMobileToonChannel(m,"_GlossMapChannel");
            d.Surface.Roughness.InvertResult=true;
            d.Surface.Reflectance=AtlasMaterialReader.Float(m,"_Reflectance",0.04f);
            d.Surface.SpecularSharpness=AtlasMaterialReader.Float(m,"_SpecularSharpness",1f);

            var matcap=AtlasMaterialReader.Matcap(m,d,"_Matcap","_MatcapStrength",mask:"_MatcapMask");
            if (matcap!=null)
            {
                int blend=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_MatcapType",0f));
                matcap.Blend=blend==0?AtlasBlendOperation.Add:blend==1?AtlasBlendOperation.Multiply:AtlasBlendOperation.Unknown;
                matcap.Quality=blend==0 || blend==1 ? AtlasNormalizationQuality.Exact:AtlasNormalizationQuality.Unresolved;
                matcap.BlendWeights.Add=blend==0?1f:0f;
                matcap.BlendWeights.Multiply=blend==1?1f:0f;
                if(matcap.Mask!=null) matcap.Mask.Channel=AtlasMobileToonChannel(m,"_MatcapMaskChannel");
            }

            // Rim lighting is a single reusable feature layer, not a mobile-shader config object.
            if(AtlasMaterialReader.Has(m,"_RimColor"))
            {
                d.Rims.Add(new AtlasRimLayer {
                    Order=d.Rims.Count, Enabled=true,
                    Tint=AtlasMaterialReader.Color(m,"_RimColor",Color.black),
                    Intensity=AtlasMaterialReader.Float(m,"_RimIntensity",0f),
                    Range=AtlasMaterialReader.Float(m,"_RimRange",0f),
                    Softness=AtlasMaterialReader.Float(m,"_RimSharpness",0.1f),
                    AlbedoTint=AtlasMaterialReader.Float(m,"_RimAlbedoTint",0f),
                    Environmental=AtlasSemanticReader.Toggle(m,"_RimEnvironmental")
                });
            }
            if(d.Emissions.Count>0)
            {
                var emission=d.Emissions[0];
                emission.Intensity=AtlasMaterialReader.Float(m,"_EmissionStrength",1f);
                emission.Enabled=emission.Map!=null || emission.Tint.maxColorComponent>0.00001f;
                if(emission.Map!=null) emission.Map.Mapping.UVIndex=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_EmissionUV",0f));
            }
            d.Surface.Occlusion.Sample=AtlasMaterialReader.Sample(m,"_OcclusionMap",AtlasColorInterpretation.LinearData,d);
            d.Surface.Occlusion.Constant=AtlasMaterialReader.Float(m,"_OcclusionStrength",1f);
            d.Surface.Occlusion.Channel=AtlasMobileToonChannel(m,"_OcclusionMapChannel");
            d.Surface.Occlusion.CombineMode=AtlasScalarCombineMode.StrengthAroundOne;

            var detail=AtlasMaterialReader.Detail(m,d,"_DetailAlbedoMap",mask:"_DetailMask");
            var detailNormal=AtlasMaterialReader.Normal(m,d,"_DetailNormalMap","_DetailNormalMapScale",maskProperty:"_DetailMask");
            var detailMask=AtlasMaterialReader.Mask(m,d,"_DetailMask","_DetailMaskChannel");
            int detailUV=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_DetailUV",0f));
            if(detail!=null)
            {
                detail.Mask=detailMask;
                detail.Map.Mapping.UVIndex=detailUV;
                int mode=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_DetailMode",0f));
                detail.DetailBlend=mode==0?AtlasDetailBlendMode.Alpha:mode==1?AtlasDetailBlendMode.Add:
                    mode==2?AtlasDetailBlendMode.Multiply:mode==3?AtlasDetailBlendMode.MultiplyDouble:AtlasDetailBlendMode.Unknown;
                detail.Quality=mode>=0 && mode<=3?AtlasNormalizationQuality.Exact:AtlasNormalizationQuality.Unresolved;
            }
            if(detailNormal!=null)
            {
                detailNormal.Mask=detailMask;
                detailNormal.Map.Mapping.UVIndex=detailUV;
                detailNormal.NormalBlend=AtlasNormalBlend.Whiteout;
            }
            d.ColorProcessing.HueShift=AtlasMaterialReader.Float(m,"_HueShift",0f)/(2f*Mathf.PI);
            d.ColorProcessing.DetailHueShift=AtlasMaterialReader.Float(m,"_DetailHueShift",0f)/(2f*Mathf.PI);
            d.ColorProcessing.EmissionHueShift=AtlasMaterialReader.Float(m,"_EmissionHueShift",0f)/(2f*Mathf.PI);
            d.ColorProcessing.HueMask=AtlasMaterialReader.Mask(m,d,"_HueShiftMask","_HueShiftMaskChannel");
            var colorMask=AtlasMaterialReader.Mask(m,d,"_ColorMask");
            int maskBlend=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_ColorMaskBlendMode",0));
            for(int i=1;i<=4;i++)
            {
                string suffix=i.ToString();
                d.ColorMasks.Add(new AtlasColorMaskLayer {
                    Index=i-1, Enabled=colorMask!=null,
                    Mask=colorMask==null?null:new AtlasMaskDefinition { Sample=colorMask.Sample,Channel=(AtlasChannel)(i-1) },
                    Tint=AtlasMaterialReader.Color(m,"_ColorMaskColor"+suffix,Color.white),
                    EmissionStrength=AtlasMaterialReader.Float(m,"_ColorMaskEmissionStrength"+suffix,0f),
                    Blend=maskBlend==0?AtlasColorMaskBlendMode.Multiply:maskBlend==1?AtlasColorMaskBlendMode.Add:AtlasColorMaskBlendMode.Unknown
                });
            }
            PopulateAudioLink(m,d);
            if(Name(m).Contains("Outline"))
                AtlasMaterialReader.MissingFeature(d,"geometry.outline","Outline geometry is implemented as a separate shader variant; its fixed mesh expansion must be handled by an output generator.");
        }

        private static AtlasChannel AtlasMobileToonChannel(Material m,string name)
        {
            int index=Mathf.RoundToInt(AtlasMaterialReader.Float(m,name,0));
            return index==1?AtlasChannel.Green:index==2?AtlasChannel.Blue:index==3?AtlasChannel.Alpha:AtlasChannel.Red;
        }
        private static void PopulateAudioLink(Material m,AtlasUnifiedMaterial d)
        {
            if(!AtlasMaterialReader.Has(m,"_AudioLinkMode"))return;
            var a=d.AudioReactive;
            a.Enabled=true;
            switch(Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_AudioLinkMode",0f)))
            {
                case 0:a.Mode=AtlasAudioEffectMode.Pulse;break;
                case 1:a.Mode=AtlasAudioEffectMode.Scroll;break;
                case 2:a.Mode=AtlasAudioEffectMode.Bar;break;
                case 3:a.Mode=AtlasAudioEffectMode.UVHorizontal;break;
                default:a.Mode=AtlasAudioEffectMode.Unknown;break;
            }
            int blend=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_ALBlendMode",0f));
            a.Blend=blend==0?AtlasBlendOperation.Replace:blend==1?AtlasBlendOperation.Add:
                blend==2?AtlasBlendOperation.Multiply:AtlasBlendOperation.Unknown;
            a.Mask=AtlasMaterialReader.Mask(m,d,"_AudioLinkMask","_ALMaskChannel");
            if(a.Mask!=null) a.Mask.Sample.Mapping.UVIndex=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_ALMaskUVChannel",0));
            a.Tint=AtlasMaterialReader.Color(m,"_ALTint",Color.white);
            a.Intensity=AtlasMaterialReader.Float(m,"_ALIntensity",1f);
            a.Band=Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_ALBand",0f));
            a.Smoothing=AtlasMaterialReader.Float(m,"_ALSmoothing",0f);
            a.ScrollScale=AtlasMaterialReader.Float(m,"_ALScrollScale",1f);
            a.CenterOut=AtlasSemanticReader.Toggle(m,"_ALScrollCenterOut");
            a.BarSmoothing=AtlasMaterialReader.Float(m,"_ALBarSmoothing",0f);
            a.UseFallback=AtlasSemanticReader.Toggle(m,"_ALEnableFallback");
            a.FallbackSpeed=AtlasMaterialReader.Float(m,"_ALFallbackSpeed",0f);
            var advanced=AtlasSemanticReader.Feature(d,"audio.reactive.modulation");
            AtlasSemanticReader.NumberSet(m,advanced,
                "rimMaskStrength","_ALRimMaskStrength","rimMaskSmoothing","_ALRimMaskSmoothing");
            AtlasSemanticReader.ToggleSet(m,advanced,
                "maskByEmission","_ALMaskByEmission","useEffectMask","_ALEffectUseMask");
            AtlasSemanticReader.NumberSet(m,advanced,
                "effectMaskChannel","_ALEffectMaskChannel","effectUVChannel","_ALEffectUVChannel");
        }
    }

    public sealed class AtlasVRChatToonLitParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-toonlit";
        public override string DisplayName => "VRChat/Mobile/Toon Lit";
        public override bool CanParse(Material m) => Name(m) == "VRChat/Mobile/Toon Lit";
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            AtlasMaterialReader.BaseColor(m,d,"_MainTex",null);
            d.Surface.Model=AtlasShadingModel.Toon;
            d.Surface.Workflow=AtlasSurfaceWorkflow.None;
            d.Lighting.Model=AtlasShadingModel.Toon;
            d.Lighting.Mode=AtlasLightingMode.MobileVertexToon;
            d.Render.ApplyVertexColor=true;
            d.Surface.UseVertexColors=true;
            d.Render.Mode=AtlasSurfaceMode.Opaque;
            // Fixed shader behavior: one directional light, vertex SH, no alpha output.
        }
    }
}
