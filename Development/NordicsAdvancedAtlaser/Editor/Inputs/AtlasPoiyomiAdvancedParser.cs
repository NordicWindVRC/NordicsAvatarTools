using System;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Additional Poiyomi Toon 10.x functional mappings. Only source readers here know
    /// Poiyomi property identifiers; output generators see common semantic features.
    /// Unknown modes remain flagged rather than being passed on as source integers.
    /// Mappings are guarded so older versions can share this reader safely.
    /// </summary>
    internal static class AtlasPoiyomiAdvancedParser
    {
        private static bool Has(Material m, string id) { return AtlasMaterialReader.Has(m, id); }
        private static float F(Material m, string id, float fallback = 0f) { return AtlasMaterialReader.Float(m,id,fallback); }
        private static bool B(Material m, string id, bool fallback = false) { return AtlasSemanticReader.Toggle(m,id,fallback); }
        private static Color C(Material m, string id, Color fallback) { return AtlasMaterialReader.Color(m,id,fallback); }
        private static AtlasSemanticFeature Feature(AtlasUnifiedMaterial d,string id,bool enabled=true)
        { return AtlasSemanticReader.Feature(d,id,enabled); }
        private static void N(Material m, AtlasSemanticFeature f, params string[] pairs)
        { AtlasSemanticReader.NumberSet(m,f,pairs); }
        private static void T(Material m, AtlasSemanticFeature f, params string[] pairs)
        { AtlasSemanticReader.ToggleSet(m,f,pairs); }
        private static void E(Material m, AtlasUnifiedMaterial d,AtlasSemanticFeature f,string semantic,string source)
        { AtlasSemanticReader.DeclaredChoice(m,d,f,semantic,source); }
        private static void Tex(Material m,AtlasUnifiedMaterial d,AtlasSemanticFeature f,string semantic,string source,
            AtlasColorInterpretation interpretation = AtlasColorInterpretation.LinearData)
        { AtlasSemanticReader.Texture(m,d,f,semantic,source,interpretation); }
        public static void Populate(Material m,AtlasUnifiedMaterial d)
        {
            Normals(m,d);
            Alpha(m,d);
            Decals(m,d);
            LightData(m,d);
            Shadows(m,d);
            Rim(m,d);
            Reflection(m,d);
            EnvironmentalRim(m,d);
            Backlight(m,d);
            LTCGI(m,d);
            Glitter(m,d);
            Proximity(m,d);
            Audio(m,d);
            Rendering(m,d);
            ColorAdjust(m,d);
        }
        private static void Normals(Material m,AtlasUnifiedMaterial d)
        {
            if(d.Normals.Count>0)
            {
                var primary=d.Normals[0];
                primary.StochasticSampling=B(m,"_BumpMapStochastic");
            }
            for(int i=0;i<d.Normals.Count;i++)
            {
                var layer=d.Normals[i];
                if(layer.Map==null || layer.Map.Texture==null) continue;
                if(layer.BentNormal)
                {
                    layer.SpecularOcclusionStrength=F(m,"_BentNormalSpecOccStrength",1f);
                    layer.AffectIndirectDiffuse=B(m,"_BentNormalIndirectDiffuse");
                }
                else if(i>0)
                {
                    // This applies to the first subsequent normal (normal 2) if present.
                    layer.StochasticSampling=B(m,"_Bump2ndMapStochastic");
                    layer.StrengthGlobalMask=d.GlobalMaskBindings.Find(x=>x.Target==AtlasGlobalMaskTarget.SecondaryNormalStrength);
                }
            }
            var normal=Feature(d,"surface.normal.secondary",B(m,"_UseBump2ndMap"));
            Tex(m,d,normal,"strengthMask","_Bump2ndScaleMask");
            // A separate feature-local strength mask, not a generic tangent normal map.
            var bent=Feature(d,"surface.bent-normal",true);
            N(m,bent,"specularOcclusionStrength","_BentNormalSpecOccStrength");
            T(m,bent,"useForIndirectDiffuse","_BentNormalIndirectDiffuse");
        }
        private static void Alpha(Material m,AtlasUnifiedMaterial d)
        {
            if(!Has(m,"_AlphaMask"))return;
            var f=Feature(d,"surface.alpha-map",true);
            Tex(m,d,f,"texture","_AlphaMask");
            E(m,d,f,"channelBlend","_AlphaMaskChannelBlendMode");
            E(m,d,f,"blendMode","_AlphaMaskBlendType");
            N(m,f,"redWeight","_AlphaMaskR","greenWeight","_AlphaMaskG",
                "blueWeight","_AlphaMaskB","alphaWeight","_AlphaMaskA",
                "gamma","_AlphaMaskGamma","blendStrength","_AlphaMaskBlendStrength",
                "blendOffset","_AlphaMaskValue");
            T(m,f,"invert","_AlphaMaskInvert");
            AtlasSemanticReader.Vector(m,f,"range","_AlphaMaskMinMax");
            if(Has(m,"_Cutoff"))d.Render.AlphaCutoff=F(m,"_Cutoff",0.5f);
        }
        private static void Decals(Material m,AtlasUnifiedMaterial d)
        {
            // One semantic instance per physical decal slot. Do not create one config per shader.
            for(int i=0;i<4;i++)
            {
                string s=i==0?"":i.ToString();
                var map=AtlasMaterialReader.Sample(m,"_DecalTexture"+s,AtlasColorInterpretation.ColorSRGB,d,"_DecalTextureUV"+s,true);
                if(map==null && !Has(m,"_DecalEnabled"+s))continue;
                AtlasDecalLayer decal=d.Decals.Find(x=>x.Order==i);
                if(decal==null)
                {
                    decal=new AtlasDecalLayer {Order=i,Map=map};
                    d.Decals.Add(decal);
                }
                decal.Map=map??decal.Map;
                decal.Enabled=B(m,"_DecalEnabled"+s);
                decal.Tint=C(m,"_DecalColor"+s,Color.white);
                var pos=Has(m,"_DecalPosition"+s)?m.GetVector("_DecalPosition"+s):new Vector4(.5f,.5f,0f,0f);
                decal.Position=new Vector2(pos.x,pos.y);
                var scale=Has(m,"_DecalScale"+s)?m.GetVector("_DecalScale"+s):Vector4.one;
                decal.Scale=new Vector2(scale.x,scale.y);
                decal.RotationDegrees=F(m,"_DecalRotation"+s);
                decal.RotationSpeed=F(m,"_DecalRotationSpeed"+s);
                if(Has(m,"_DecalSideOffset"+s))decal.SideOffset=m.GetVector("_DecalSideOffset"+s);
                decal.Tiled=B(m,"_DecalTiled"+s);
                decal.EmissionStrength=F(m,"_DecalEmissionStrength"+s);
                decal.Alpha=F(m,"_DecalBlendAlpha"+s,1f);
                decal.AlphaIntensity=F(m,"_DecalAlphaIntensity"+s,1f);
                decal.Depth=F(m,"_Decal"+i+"Depth");
                var feature=decal.Options=Feature(d,"decal.options."+i,decal.Enabled);
                E(m,d,feature,"aspectMode","_Decal"+i+"AspectRatioMode");
                E(m,d,feature,"mirroredUV","_DecalMirroredUVMode"+s);
                E(m,d,feature,"symmetry","_DecalSymmetryMode"+s);
                E(m,d,feature,"colorBlend","_DecalBlendType"+s);
                E(m,d,feature,"alphaBlend","_Decal"+i+"OverrideAlphaMode");
                N(m,feature,"alphaIntensity","_DecalAlphaIntensity"+s,"depth","_Decal"+i+"Depth");
                T(m,feature,"overrideAlpha","_DecalOverrideAlpha"+s);
                decal.EmissionAnimation=new AtlasAnimatableParameter {Target="decal["+i+"].emissionStrength"};
                decal.AlphaAnimation=new AtlasAnimatableParameter {Target="decal["+i+"].alpha"};
                d.AnimatableParameters.Add(decal.EmissionAnimation);
                d.AnimatableParameters.Add(decal.AlphaAnimation);
                // The animated 'A' in Thry is material metadata, not an animation curve.
                // Curves need a separate Unity animation/animator binding pass.
            }
        }
        private static void LightData(Material m,AtlasUnifiedMaterial d)
        {
            var l=d.Lighting;
            var f=Feature(d,"lighting.light-data");
            E(m,d,f,"colorMode","_LightingColorMode");
            E(m,d,f,"lightmapMode","_LightingMapMode");
            E(m,d,f,"directionMode","_LightingDirectionMode");
            T(m,f,"forceLightColor","_LightingForceColor","limitBrightness","_LightingCapEnabled",
                "receiveCastShadows","_LightingCastedShadows","cleanCastShadows","_LightingCastedShadowsClean",
                "vertexLights","_LightingVertexLights","mirrorVertexLights","_LightingVertexLightsMirror");
            N(m,f,"luminanceLimit","_LightingCapLuminance","indirectNormalStrength","_LightingIndirectUsesNormals",
                "castShadowSoftness","_LightingShadowSoftness","grayscaleLighting","_LightingMonochromaticStrength");
            var add=Feature(d,"lighting.additional-lights");
            T(m,add,"pixelLights","_LightingAdditiveEnable","ignoreDirectional","_LightingAdditiveIgnoreDirectional",
                "limitBrightness","_LightingAdditiveLimited");
            N(m,add,"maxBrightness","_LightingAdditiveLimit","receiveCastShadows","_LightingAdditiveShadowStrength",
                "grayscaleLighting","_LightingAdditiveMonochromaticStrength",
                "passthrough","_LightingAdditivePassthrough","attenuation","_LightingAdditiveAttenCurve");
            var volumes=Feature(d,"lighting.light-volumes",d.ExternalLighting.EnableLightVolumes);
            N(m,volumes,"specularIntensity","_LightingLightVolumesSpecular","normalBias","_LightingLightVolumesNormalBias");
            l.LimitBrightness=B(m,"_LightingCapEnabled");
        }
        private static void Shadows(Material m,AtlasUnifiedMaterial d)
        {
            var f=Feature(d,"lighting.shadows",B(m,"_EnableShadows",true));
            N(m,f,"baseColorBlend","_ShadowBaseColorBlend","shadowStrength","_ShadowStrength",
                "environmentShadowStrength","_ShadowEnvStrength","borderRange","_ShadowBorderRange");
            T(m,f,"nonlinearLightmap","_ShadowNonLinearLightmap");
            E(m,d,f,"shadowMapMode","_ShadowMapType");
            E(m,d,f,"additiveLightingMode","_LightingAdditiveType");
            var skin=Feature(d,"lighting.skin-scattering",d.Lighting.Mode==AtlasLightingMode.Skin);
            Tex(m,d,skin,"thickness","_SSSThicknessMap");
            Tex(m,d,skin,"lookup","_LightingSkinLUT",AtlasColorInterpretation.ColorSRGB);
            N(m,skin,"thicknessPower","_LightingSkinThicknessPower","scatteringStability","_LightingSkinStability");
            T(m,skin,"softenDiffuseNormals","_LightingSkinSoftenNormals");
            // Each band's actual controls are mapped by the shared lighting parser.
        }
        private static void Rim(Material m,AtlasUnifiedMaterial d)
        {
            for(int i=0;i<2;i++)
            {
                string s=i==0?"":i.ToString();
                if(!Has(m,"_RimStyle"+s) && !Has(m,"_EnableRimLighting"+s))continue;
                var feature=Feature(d,"lighting.rim.options."+i,B(m,"_EnableRimLighting"+s));
                E(m,d,feature,"style","_RimStyle"+s);
                E(m,d,feature,"blendMode","_RimPoiBlendMode"+s);
                E(m,d,feature,"colorSpace","_RimHueShiftColorSpace"+s);
                N(m,feature,"width","_RimWidth"+s,"blur","_RimBlur"+s,
                    "power","_RimPower"+s,"normalStrength","_RimNormalStrength"+s,
                    "mixBaseColor","_RimBaseColorMix"+s,"brightness","_RimBrightness"+s,
                    "emission","_RimStrength"+s,"hueShift","_RimHueShift"+s);
                T(m,feature,"linear","_RimLinear"+s,"invert","_RimLightingInvert"+s);
                if(i<d.Rims.Count)
                {
                    var rim=d.Rims[i];
                    rim.Width=F(m,"_RimWidth"+s,rim.Width);
                    rim.Softness=F(m,"_RimBlur"+s,rim.Softness);
                    rim.Power=F(m,"_RimPower"+s,rim.Power);
                    rim.NormalStrength=F(m,"_RimNormalStrength"+s,1f);
                    rim.BaseColorMix=F(m,"_RimBaseColorMix"+s);
                    rim.EmissionStrength=F(m,"_RimStrength"+s);
                    rim.Brightness=F(m,"_RimBrightness"+s,1f);
                    rim.Linear=B(m,"_RimLinear"+s);
                    rim.Invert=B(m,"_RimLightingInvert"+s);
                }
            }
        }
        private static void Reflection(Material m,AtlasUnifiedMaterial d)
        {
            var specular=Feature(d,"surface.reflections");
            N(m,specular,"reflectionVisibility","_MochieReflectionStrength",
                "specularVisibility","_MochieSpecularStrength",
                "fresnelStrength","_MochieFresnelStrength",
                "exposureOcclusion","_MochieExposureOcclusion");
            var coat=Feature(d,"surface.clearcoat",B(m,"_ClearCoatBRDF"));
            N(m,coat,"reflectionVisibility","_ClearCoatReflectionStrength",
                "specularVisibility","_ClearCoatSpecularStrength",
                "fresnelStrength","_ClearCoatFresnelStrength",
                "exposureOcclusion","_ClearCoatExposureOcclusion");
            AtlasSemanticReader.Color(m,coat,"reflectionTint","_ClearCoatReflectionTint");
            AtlasSemanticReader.Color(m,coat,"specularTint","_ClearCoatSpecularTint");
            Tex(m,d,coat,"fallbackCubemap","_ClearCoatCubeMap");
        }
        private static void EnvironmentalRim(Material m,AtlasUnifiedMaterial d)
        {
            if(!Has(m,"_EnableEnvironmentalRim"))return;
            var rim=new AtlasRimLayer {Order=d.Rims.Count,Enabled=B(m,"_EnableEnvironmentalRim"),Environmental=true,
                Mask=AtlasMaterialReader.Mask(m,d,"_RimEnviroMask", "_RimEnviroChannel",uvProperty:"_RimEnviroMaskUV",poiyomiUV:true),
                Width=F(m,"_RimEnviroWidth",0.5f),Softness=F(m,"_RimEnviroBlur"),
                Power=F(m,"_RimEnviroSharpness"),Intensity=F(m,"_RimEnviroIntensity",1f)};
            d.Rims.Add(rim);
            var f=Feature(d,"lighting.environmental-rim",rim.Enabled);
            N(m,f,"minimumBrightness","_RimEnviroMinBrightness");
        }
        private static void Backlight(Material m,AtlasUnifiedMaterial d)
        {
            if(!Has(m,"_BacklightEnabled"))return;
            var f=Feature(d,"lighting.backlight",B(m,"_BacklightEnabled"));
            AtlasSemanticReader.Color(m,f,"color","_BacklightColor");
            Tex(m,d,f,"texture","_BacklightColorTex",AtlasColorInterpretation.ColorSRGB);
            N(m,f,"mainColorPower","_BacklightMainStrength","normalStrength","_BacklightNormalStrength",
                "border","_BacklightBorder","blur","_BacklightBlur","directivity","_BacklightDirectivity",
                "viewDirectionStrength","_BacklightViewStrength");
            T(m,f,"receiveShadows","_BacklightReceiveShadow","useBackfaceMask","_BacklightBackfaceMask");
        }
        private static void LTCGI(Material m,AtlasUnifiedMaterial d)
        {
            if(!Has(m,"_LTCGIEnabled"))return;
            var l=d.ExternalLighting;
            l.EnableLTCGI=B(m,"_LTCGIEnabled");
            l.LTCGIAnimationToggle=B(m,"_LTCGI_AnimToggle");
            l.LTCGIDiffuseTint=C(m,"_LTCGI_DiffuseColor",Color.white);
            l.LTCGISpecularTint=C(m,"_LTCGI_SpecularColor",Color.white);
            l.LTCGIUseMaterialReflections=B(m,"_LTCGI_UsePBR");
            l.LTCGIUseExposureOcclusion=B(m,"_LTCGI_UseEO");
            l.LTCGISpecularMultiplier=F(m,"_LTCGI_SpecularMultiplier",1f);
            l.LTCGIDiffuseMultiplier=F(m,"_LTCGI_DiffuseMultiplier",1f);
            var f=Feature(d,"lighting.ltcgi",l.EnableLTCGI);
            N(m,f,"smoothness","_LTCGI_Smoothness","metallic","_LTCGI_Metallic");
        }
        private static void Glitter(Material m,AtlasUnifiedMaterial d)
        {
            if(!Has(m,"_GlitterEnable"))return;
            var f=Feature(d,"surface.glitter",B(m,"_GlitterEnable"));
            E(m,d,f,"uvMode","_GlitterUV"); E(m,d,f,"mode","_GlitterMode");
            E(m,d,f,"shape","_GlitterShape"); E(m,d,f,"blend","_GlitterBlendType");
            Tex(m,d,f,"shapeTexture","_GlitterTexture");
            Tex(m,d,f,"colorMap","_GlitterColorMap",AtlasColorInterpretation.ColorSRGB);
            Tex(m,d,f,"mask","_GlitterMask");
            AtlasSemanticReader.Color(m,f,"color","_GlitterColor");
            T(m,f,"randomColors","_GlitterRandomColors","randomPosition","_GlitterRandomLocation",
                "randomSize","_GlitterRandomSize","useSurfaceColor","_GlitterUseSurfaceColor",
                "distanceLOD","_GlitterDistanceLOD","useNormals","_GlitterUseNormals",
                "spriteSheet","_GlitterSpriteSheetEnabled");
            N(m,f,"layers","_GlitterLayers","density","_GlitterFrequency","size","_GlitterSize",
                "minBrightness","_GlitterMinBrightness","maxBrightness","_GlitterBrightness",
                "speed","_GlitterSpeed","angleRange","_GlitterAngleRange",
                "bias","_GlitterBias","contrast","_GlitterContrast",
                "spriteColumns","_GlitterSpriteSheetColumns","spriteRows","_GlitterSpriteSheetRows",
                "spriteFPS","_GlitterSpriteSheetFPS");
            AtlasSemanticReader.Vector(m,f,"sizeRange","_GlitterMinMaxSize");
            AtlasSemanticReader.Vector(m,f,"panning","_GlitterUVPanning");
        }
        private static void Proximity(Material m,AtlasUnifiedMaterial d)
        {
            if(!Has(m,"_FXProximityColor"))return;
            var f=Feature(d,"surface.proximity-color",B(m,"_FXProximityColor"));
            AtlasSemanticReader.Color(m,f,"nearColor","_FXProximityColorMinColor");
            AtlasSemanticReader.Color(m,f,"farColor","_FXProximityColorMaxColor");
            E(m,d,f,"distanceMode","_FXProximityColorType");
            N(m,f,"nearDistance","_FXProximityColorMinDistance",
                "farDistance","_FXProximityColorMaxDistance");
            T(m,f,"forceBackface","_FXProximityColorBackFace","solidTransition","_FXProximityColorSolid",
                "overrideColor","_FXProximityColorOverrideColor","overrideAlpha","_FXProximityColorOverrideAlpha",
                "overrideEmission","_FXProximityColorOverrideEmission");
            AtlasSemanticReader.Vector(m,f,"transitionCurve","_FXProximityColorCurve");
        }
        private static void Audio(Material m,AtlasUnifiedMaterial d)
        {
            var f=Feature(d,"audio.global",B(m,"_AudioLinkAnimToggle"));
            d.AudioReactive.Enabled=B(m,"_AudioLinkAnimToggle",d.AudioReactive.Enabled);
            d.AudioReactive.AnimatableToggle=B(m,"_AudioLinkAnimToggle");
            for(int i=0;i<4;i++)
            {
                string[] bands={"Bass","LowMid","HighMid","Treble"};
                string band=bands[i];
                d.AudioReactive.BandSmoothing[i]=F(m,"_AudioLinkSmoothing"+band);
                if(Has(m,"_AudioLinkBandOverrideSliders"))
                {
                    Vector4 overrides=m.GetVector("_AudioLinkBandOverrideSliders");
                    d.AudioReactive.BandOverrides[i]=i==0?overrides.x:i==1?overrides.y:i==2?overrides.z:overrides.w;
                }
            }
            T(m,f,"enableBandOverrides","_AudioLinkBandOverridesEnabled");
        }
        private static void Rendering(Material m,AtlasUnifiedMaterial d)
        {
            var r=d.Render;
            r.ZClip=B(m,"_ZClip",true);
            r.DepthOffsetFactor=F(m,"_OffsetFactor");
            r.DepthOffsetUnits=F(m,"_OffsetUnits");
            r.IgnoreFog=B(m,"_IgnoreFog");
            r.FlipBackfaceNormals=B(m,"_FlipBackfaceNormals");
            r.EarlyDepth=B(m,"_EarlyZ");
            r.ReduceClipDistance=B(m,"_ReduceClipDistance");
            r.Instancing=B(m,"_EnableInstancing");
            r.ColorWriteMask=Mathf.RoundToInt(F(m,"_ColorMask",15f));
            var f=Feature(d,"render.options");
            T(m,f,"flipBackfaceNormals","_FlipBackfaceNormals","ignoreFog","_IgnoreFog",
                "reduceClipDistance","_ReduceClipDistance","earlyDepth","_EarlyZ");
            N(m,f,"offsetFactor","_OffsetFactor","offsetUnits","_OffsetUnits");
        }
        private static void ColorAdjust(Material m,AtlasUnifiedMaterial d)
        {
            var color=d.ColorProcessing;
            color.Enabled=B(m,"_MainColorAdjustToggle");
            color.Saturation=F(m,"_Saturation",color.Saturation);
            color.Chromatize=F(m,"_MainChromatize");
            color.ClampOutput=B(m,"_MainColorAdjustClamp");
            color.Tint.Color=C(m,"_MainTintColor",Color.white);
            color.Tint.Map=AtlasMaterialReader.Sample(m,"_MainTintTexture",AtlasColorInterpretation.ColorSRGB,d);
            var f=Feature(d,"color.adjustment",color.Enabled);
            N(m,f,"bakeMode","_BakeColorAdjust");
            E(m,d,f,"hueMode","_MainHueShiftMode");
            Tex(m,d,f,"adjustmentMask","_MainColorAdjustTexture");
        }
    }
}
