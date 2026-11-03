using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>Common functional controls of Unity Standard and VRChat Standard Lite.</summary>
    internal static class AtlasStandardFeatureParser
    {
        public static void Populate(Material m, AtlasUnifiedMaterial d, bool specularWorkflow,
            bool enableSmoothnessChannel, bool mobileLite)
        {
            d.Surface.Model = specularWorkflow ? AtlasShadingModel.StandardSpecular : AtlasShadingModel.StandardMetallic;
            d.Surface.Workflow = specularWorkflow ? AtlasSurfaceWorkflow.Specular : AtlasSurfaceWorkflow.Metallic;
            d.Lighting.Model = d.Surface.Model;
            d.Lighting.Mode = specularWorkflow ? AtlasLightingMode.StandardSpecular : AtlasLightingMode.StandardMetallic;
            AtlasMaterialReader.StandardRenderMode(m,d);
            d.Surface.SpecularHighlights = AtlasSemanticReader.Toggle(m,"_SpecularHighlights",true);
            d.Surface.GlossyReflections = AtlasSemanticReader.Toggle(m,"_GlossyReflections",true);
            if (specularWorkflow)
                AtlasMaterialReader.Specular(m,d,"_SpecColor","_SpecGlossMap");
            else
                AtlasMaterialReader.Metallic(m,d,"_Metallic","_MetallicGlossMap",AtlasChannel.Red);

            string glossTexture = specularWorkflow ? "_SpecGlossMap" : "_MetallicGlossMap";
            AtlasMaterialReader.Roughness(m,d,"_Glossiness",glossTexture,true,AtlasChannel.Alpha);
            // With a gloss map Unity uses _GlossMapScale rather than _Glossiness.
            if (d.Surface.Roughness.Sample != null)
                d.Surface.Roughness.Constant = AtlasMaterialReader.Float(m,"_GlossMapScale",1f);
            if (enableSmoothnessChannel && AtlasMaterialReader.Float(m,"_SmoothnessTextureChannel",0f)>0.5f)
            {
                d.Surface.Roughness.Sample = d.Surface.BaseColor.Map;
                d.Surface.Roughness.Channel = AtlasChannel.Alpha;
                d.Surface.Roughness.Constant = AtlasMaterialReader.Float(m,"_GlossMapScale",1f);
            }
            if (d.Surface.Roughness.Sample == null)
                d.Surface.Roughness.Constant = AtlasMaterialReader.Float(m,"_Glossiness",0.5f);

            var detailNormal=AtlasMaterialReader.Normal(m,d,"_DetailNormalMap","_DetailNormalMapScale");
            var detail=AtlasMaterialReader.Detail(m,d,"_DetailAlbedoMap",mask:"_DetailMask");
            var mask=AtlasMaterialReader.Mask(m,d,"_DetailMask");
            if (mask!=null) mask.Channel=AtlasChannel.Alpha;
            int uv = Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_UVSec",0f));
            if (detailNormal!=null)
            {
                detailNormal.Map.Mapping.UVIndex=uv;
                detailNormal.Mask=mask;
                detailNormal.NormalBlend=AtlasNormalBlend.Whiteout;
            }
            if (detail!=null)
            {
                detail.Map.Mapping.UVIndex=uv;
                detail.Mask=mask;
                detail.DetailBlend=AtlasDetailBlendMode.MultiplyDouble;
            }
            foreach(var layer in d.Emissions)
                layer.Enabled = m.IsKeywordEnabled("_EMISSION") ||
                    (mobileLite && layer.Map!=null && layer.Tint.maxColorComponent>0.0001f);

            d.Surface.Height.Sample=AtlasMaterialReader.Sample(m,"_ParallaxMap",AtlasColorInterpretation.LinearData,d);
            d.Surface.Height.Channel=AtlasChannel.Green;
            d.Surface.Height.Constant=AtlasMaterialReader.Float(m,"_Parallax",0.02f);
            d.Surface.HasHeightMap = d.Surface.Height.Sample!=null;
            if (d.Surface.Occlusion.Sample!=null)
                d.Surface.Occlusion.CombineMode=AtlasScalarCombineMode.StrengthAroundOne;

            if (mobileLite)
            {
                d.Surface.GeometricSpecularAA=AtlasSemanticReader.Toggle(m,"_EnableGeometricSpecularAA",false);
                d.Surface.SpecularAAScreenSpaceVariance=AtlasMaterialReader.Float(m,"_SpecularAAScreenSpaceVariance",0.1f);
                d.Surface.SpecularAAThreshold=AtlasMaterialReader.Float(m,"_SpecularAAThreshold",0.2f);
                switch (Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_LightmapType",0)))
                {
                    case 0: d.Lighting.LightmapMode=AtlasLightmapMode.Default;break;
                    case 1: d.Lighting.LightmapMode=AtlasLightmapMode.MonochromaticSphericalHarmonics;break;
                    case 2: d.Lighting.LightmapMode=AtlasLightmapMode.MonochromaticNoSpecular;break;
                    default: d.Lighting.LightmapMode=AtlasLightmapMode.Unknown;break;
                }
                var baseSample=d.Surface.BaseColor.Map;
                if(baseSample!=null && AtlasSemanticReader.Toggle(m,"_Bicubic"))
                    baseSample.Mapping.Sampling=AtlasSampleFilter.Bicubic;
            }
        }
    }
}
