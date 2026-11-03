using System;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi property names are interpreted here only. Output generators consume
    // AtlasGlobalMaskResource/Binding and never need the original property IDs.
    public static class AtlasPoiyomiGlobalMaskParser
    {
        private struct Routing
        {
            public string Source;
            public AtlasGlobalMaskTarget Target;
            public Routing(string source, AtlasGlobalMaskTarget target) { Source=source; Target=target; }
        }
        private static readonly Routing[] Routes = {
            new Routing("_MainHueGlobalMask",AtlasGlobalMaskTarget.HueShift),
            new Routing("_MainSaturationGlobalMask",AtlasGlobalMaskTarget.Saturation),
            new Routing("_MainBrightnessGlobalMask",AtlasGlobalMaskTarget.Brightness),
            new Routing("_MainGammaGlobalMask",AtlasGlobalMaskTarget.Gamma),
            new Routing("_AlphaGlobalMask",AtlasGlobalMaskTarget.Alpha),
            new Routing("_DetailTexGlobalMask",AtlasGlobalMaskTarget.DetailColor),
            new Routing("_DetailNormalGlobalMask",AtlasGlobalMaskTarget.DetailNormal),
            new Routing("_Bump2ndScaleMaskGlobalMask",AtlasGlobalMaskTarget.SecondaryNormalStrength),
        };

        public static void Populate(Material material, AtlasUnifiedMaterial target)
        {
            bool enabled=AtlasSemanticReader.Toggle(material,"_GlobalMaskTexturesEnable",true);
            for (int index=0; index<4; ++index)
            {
                string id="_GlobalMaskTexture"+index;
                var sample=AtlasMaterialReader.Sample(material,id,AtlasColorInterpretation.LinearData,target,id+"UV",true);
                var resource=new AtlasGlobalMaskResource { Enabled=enabled,Texture=sample,
                    SplitChannels=AtlasSemanticReader.Toggle(material,id+"Split") };
                if(sample!=null)
                {
                    Vector4 pan=Vector(material,id+"Pan",Vector4.zero);
                    sample.Mapping.ScrollSpeed=new Vector2(pan.x,pan.y);
                    for(int c=0;c<4;c++)
                    {
                        var mapping=resource.ChannelMappings[c];
                        var main=sample.Mapping;
                        mapping.Space=main.Space; mapping.UVIndex=main.UVIndex;
                        mapping.Scale=main.Scale; mapping.Offset=main.Offset;
                        mapping.RotationDegrees=main.RotationDegrees;
                        mapping.ScrollSpeed=main.ScrollSpeed;
                        mapping.Sampling=main.Sampling;mapping.Quality=main.Quality;
                        if(c==0 || !resource.SplitChannels)continue;
                        string suffix=c==1?"_G":c==2?"_B":"_A";
                        Vector4 st=Vector(material,id+"SplitTilingOffset"+suffix,new Vector4(1,1,0,0));
                        Vector4 splitPan=Vector(material,id+"SplitPan"+suffix,Vector4.zero);
                        mapping.Scale=new Vector2(st.x,st.y);
                        mapping.Offset=new Vector2(st.z,st.w);
                        mapping.ScrollSpeed=new Vector2(splitPan.x,splitPan.y);
                    }
                }
                target.GlobalMaskResources.Add(resource);
            }
            if (!enabled) return;
            foreach(var route in Routes)
                AddRoute(material,target,route.Source,route.Target,-1);
            // These semantic layer bindings are reusable across shader families.
            for(int i=0;i<4;i++)
            {
                string suffix=i==0?"":i.ToString();
                AddRoute(material,target,"_EmissionGlobalMask"+suffix,AtlasGlobalMaskTarget.Emission,i);
                AddRoute(material,target,"_MatcapGlobalMask"+suffix,AtlasGlobalMaskTarget.Matcap,i);
            }
            // Modifiers that depend on vertex colors, camera, mirror or distance
            // require additional geometry/view context and are not claimed as exact.
            if(AtlasSemanticReader.Toggle(material,"_GlobalMaskModifiersEnable") ||
               AtlasSemanticReader.Toggle(material,"_GlobalMaskOptionsEnable"))
                target.Issues.Add(new AtlasNormalizationIssue("global-mask.modifiers",
                    AtlasNormalizationSeverity.Warning,
                    "Global mask view, mirror, distance or vertex-color modifiers require additional normalization."));
        }
        private static void AddRoute(Material material,AtlasUnifiedMaterial target,string property,
            AtlasGlobalMaskTarget semantic,int layerIndex)
        {
            if(!AtlasMaterialReader.Has(material,property))return;
            int value=Mathf.RoundToInt(AtlasMaterialReader.Float(material,property,0f));
            if(value==0)return;
            if(value<1 || value>16)
            {
                target.Issues.Add(new AtlasNormalizationIssue("global-mask.binding",
                    AtlasNormalizationSeverity.Warning,"Global mask selector outside 1–16."));
                return;
            }
            int packed=value-1;
            int resource=packed/4;
            var binding=new AtlasGlobalMaskBinding {
                Target=semantic,LayerIndex=layerIndex,ResourceIndex=resource,
                Channel=(AtlasChannel)(packed%4),Blend=AtlasBlendOperation.Multiply
            };
            string blendProperty=property+"BlendType";
            if(AtlasMaterialReader.Has(material,blendProperty))
            {
                int blend=Mathf.RoundToInt(AtlasMaterialReader.Float(material,blendProperty,2f));
                binding.Blend=DecodeBlend(blend);
                if(binding.Blend==AtlasBlendOperation.Unknown)
                    target.Issues.Add(new AtlasNormalizationIssue("global-mask.binding",
                        AtlasNormalizationSeverity.Warning,"Unrecognized global-mask combination operation."));
            }
            target.GlobalMaskBindings.Add(binding);
        }
        public static AtlasBlendOperation DecodeBlend(int value)
        {
            switch(value)
            {
                case 0:return AtlasBlendOperation.Replace;
                case 1:return AtlasBlendOperation.Subtract;
                case 2:return AtlasBlendOperation.Multiply;
                case 3:return AtlasBlendOperation.Divide;
                case 4:return AtlasBlendOperation.Min;
                case 5:return AtlasBlendOperation.Max;
                case 6:return AtlasBlendOperation.Average;
                case 7:return AtlasBlendOperation.Add;
                default:return AtlasBlendOperation.Unknown;
            }
        }
        private static Vector4 Vector(Material material,string property,Vector4 fallback)
        {
            return AtlasMaterialReader.Has(material,property)?material.GetVector(property):fallback;
        }
    }
}
