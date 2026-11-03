using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Conservative generic parser: well-established texture/property conventions only.
    /// Unknown controls remain explicitly unsupported, not misclassified as a semantic layer.
    /// </summary>
    public sealed class AtlasFallbackParser : AtlasParserBase
    {
        public override string ParserId => "fallback";
        public override string DisplayName => "Fallback";
        public override int Priority => -1000;
        public override bool CanParse(Material material) => material != null;
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            if(AtlasMaterialReader.Has(m,"_MainTex"))
                AtlasMaterialReader.BaseColor(m,d,"_MainTex",
                    AtlasMaterialReader.Has(m,"_Color")?"_Color": "_BaseColor");
            else if(AtlasMaterialReader.Has(m,"_BaseMap"))
                AtlasMaterialReader.BaseColor(m,d,"_BaseMap","_BaseColor");
            else
                AtlasMaterialReader.BaseColor(m,d,"_BaseColorMap","_BaseColor");

            string normal=First(m,"_BumpMap","_NormalMap","_NormalTex","_NormalTexture");
            AtlasMaterialReader.Normal(m,d,normal,First(m,"_BumpScale","_NormalStrength","_NormalScale"));
            string emission=First(m,"_EmissionMap","_EmissionTex","_EmissiveMap");
            AtlasMaterialReader.Emission(m,d,emission,First(m,"_EmissionColor","_EmissionTint"),
                First(m,"_EmissionStrength","_EmissionIntensity"));
            string metallic=First(m,"_MetallicGlossMap","_MetallicMap");
            AtlasMaterialReader.Metallic(m,d,"_Metallic",metallic);
            string roughness=First(m,"_RoughnessMap","_SmoothnessMap");
            if(roughness!=null || AtlasMaterialReader.Has(m,"_Roughness"))
                AtlasMaterialReader.Roughness(m,d,"_Roughness",roughness,false,AtlasChannel.Red);
            else if(AtlasMaterialReader.Has(m,"_Smoothness") || AtlasMaterialReader.Has(m,"_Glossiness"))
                AtlasMaterialReader.Roughness(m,d,First(m,"_Smoothness","_Glossiness"),metallic,true,AtlasChannel.Alpha);
            AtlasMaterialReader.Specular(m,d,First(m,"_SpecColor","_SpecularColor"),
                First(m,"_SpecGlossMap","_SpecularMap"));
            AtlasMaterialReader.Occlusion(m,d,First(m,"_OcclusionStrength","_AOIntensity"),
                First(m,"_OcclusionMap","_AOMap"));
            if(d.Surface.Occlusion.Sample!=null)
                d.Surface.Occlusion.CombineMode=AtlasScalarCombineMode.StrengthAroundOne;
            AtlasMaterialReader.NamedMask(m,d,First(m,"_AlphaMask","_OpacityMask"),purpose:AtlasMaskPurpose.Alpha);
            var matcap=AtlasMaterialReader.Matcap(m,d,First(m,"_Matcap","_MatCap"));
            if(matcap!=null)
            {
                matcap.Quality=AtlasNormalizationQuality.Unresolved;
                AtlasMaterialReader.MissingFeature(d,"matcap.blend",
                    "Fallback recognized a matcap texture but cannot infer its blend equation without a shader mapping.");
            }
            AtlasMaterialReader.MissingFeature(d,"material.fallback",
                "Unrecognized shader: only conventional property meanings were mapped. Unknown functional controls were not inferred.");
        }
        private static string First(Material m, params string[] names)
        {
            foreach(var name in names) if(AtlasMaterialReader.Has(m,name))return name;
            return null;
        }
    }
}
