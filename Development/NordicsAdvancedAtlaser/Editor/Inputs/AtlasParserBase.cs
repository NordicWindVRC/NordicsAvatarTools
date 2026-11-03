using System;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public abstract class AtlasParserBase : IAtlasSourceShaderParser
    {
        public abstract string ParserId { get; }
        public abstract string DisplayName { get; }
        public virtual int Priority => 50;
        public abstract bool CanParse(Material material);
        protected static string Name(Material m) => m != null && m.shader != null ? m.shader.name : "";

        public virtual AtlasUnifiedMaterial Parse(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var output = AtlasMaterialReader.Create(material);
            Populate(material, output);
            return output;
        }
        protected virtual void Populate(Material m, AtlasUnifiedMaterial data)
        {
            AtlasMaterialReader.BaseColor(m,data,"_MainTex","_Color");
            AtlasMaterialReader.Normal(m,data,"_BumpMap","_BumpScale");
            AtlasMaterialReader.Emission(m,data,"_EmissionMap","_EmissionColor");
            AtlasMaterialReader.Occlusion(m,data,"_OcclusionStrength","_OcclusionMap");
        }
    }

    public abstract class AtlasPoiyomiParserBase : AtlasParserBase
    {
        public override int Priority => 100;
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            d.Surface.Model = AtlasShadingModel.Toon;
            d.Lighting.Model = AtlasShadingModel.Toon;
            AtlasMaterialReader.BaseColor(m,d,"_MainTex","_Color","_MainTexUV",true);
            AtlasMaterialReader.Normal(m,d,"_BumpMap","_BumpScale",uvProperty:"_BumpMapUV",poiyomiUV:true);
            AtlasMaterialReader.Normal(m,d,"_Bump2ndMap","_Bump2ndScale","_UseBump2ndMap","_Bump2ndMapUV",true);
            AtlasMaterialReader.Normal(m,d,"_DetailNormalMap","_DetailNormalMapScale","_DetailEnabled","_DetailNormalMapUV",true,"_DetailMask");
            AtlasMaterialReader.Normal(m,d,"_BentNormalMap");
            AtlasMaterialReader.Detail(m,d,"_DetailTex","_DetailTexIntensity","_DetailTint","_DetailEnabled","_DetailTexUV",true,"_DetailMask");
            AtlasMaterialReader.Occlusion(m,d,"_LightDataAOStrengthR","_LightingAOMaps",AtlasChannel.Red);
            AtlasMaterialReader.Metallic(m,d,"_Metallic","_MetallicGlossMap");
            AtlasMaterialReader.Roughness(m,d,"_Smoothness","_MetallicGlossMap",true);
            AtlasMaterialReader.NamedMask(m,d,"_AlphaMask",uv:"_AlphaMaskUV",poiyomiUV:true,purpose:AtlasMaskPurpose.Alpha);
            AtlasMaterialReader.NamedMask(m,d,"_ColorMask",purpose:AtlasMaskPurpose.BaseColor);
            for (int i=0;i<4;i++)
            {
                string suffix=i==0?"":i.ToString();
                AtlasMaterialReader.Decal(m,d,"_DecalTexture"+suffix,"_DecalColor"+suffix,
                    "_DecalEnabled"+suffix,"_DecalTextureUV"+suffix,true,"_DecalMask"+suffix);
                AtlasMaterialReader.Decal(m,d,"_Decal"+i+"Texture",enabled:"_Decal"+i+"Enabled",mask:"_Decal"+i+"Mask");
                AtlasMaterialReader.NamedMask(m,d,"_GlobalMaskTexture"+i,uv:"_GlobalMaskTexture"+i+"UV",poiyomiUV:true,purpose:AtlasMaskPurpose.Global);
            }
            AtlasPoiyomiGlobalMaskParser.Populate(m,d);
            AtlasPoiyomiUnifiedParser.Populate(m,d,AtlasPoiyomiVersionDetector.Detect(m));
            AtlasPoiyomiFeatureParser.Populate(m,d);
            AtlasPoiyomiAdvancedParser.Populate(m,d);
            bool globalEnabled=AtlasSemanticReader.Toggle(m,"_GlobalMaskTexturesEnable",true);
            foreach(var mask in d.Masks)
                if(mask.Purpose==AtlasMaskPurpose.Global) mask.Enabled=globalEnabled;
            AtlasMaterialReader.MissingFeature(d,"material.advanced-effects",
                "Advanced Poiyomi procedural and animated effects, and global-mask modifiers, are not fully normalized yet. See UNIFIED_MATERIAL.md.");
        }
    }
}
