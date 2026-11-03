using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Semantic feature translators shared by Poiyomi versions where properties match.
    /// Unlike the old AtlasLayer.Parameters dictionary, semantic IDs and parameter names
    /// here are independent of the input shader. Version-specific translators may extend them.
    /// </summary>
    internal static class AtlasPoiyomiFeatureParser
    {
        public static void Populate(Material material, AtlasUnifiedMaterial output)
        {
            Rim(material,output);
            Subsurface(material,output);
            Clearcoat(material,output);
            Outline(material,output);
        }

        private static AtlasSemanticValue Number(float value)
        { return new AtlasSemanticValue { Kind=AtlasSemanticValueKind.Number, Number=value }; }

        private static void Rim(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasMaterialReader.Has(m,"_EnableRimLighting")) return;
            var rim = new AtlasRimLayer {
                Enabled=AtlasMaterialReader.Float(m,"_EnableRimLighting",0)>0.5f,
                Order=d.Rims.Count,
                Map=AtlasMaterialReader.Sample(m,"_RimTex",AtlasColorInterpretation.ColorSRGB,d,"_RimTexUV",true),
                Tint=AtlasMaterialReader.Color(m,"_RimLightColor",Color.white),
                Opacity=AtlasMaterialReader.Float(m,"_RimBlendStrength",1),
                Mask=AtlasMaterialReader.Mask(m,d,"_RimMask","_RimMaskChannel","_RimMaskInvert","_RimMaskUV",true),
                Quality=AtlasNormalizationQuality.Approximate
            };
            // These blend modes are defined by semantic operations rather than Poiyomi enum IDs.
            switch (Mathf.RoundToInt(AtlasMaterialReader.Float(m,"_RimPoiBlendMode",0)))
            {
                case 0: rim.Blend=AtlasBlendOperation.Add; break;
                case 1: rim.Blend=AtlasBlendOperation.Replace; break;
                case 2: rim.Blend=AtlasBlendOperation.Multiply; break;
                case 4: rim.Blend=AtlasBlendOperation.Screen; break;
                default: rim.Blend=AtlasBlendOperation.Unknown; rim.Quality=AtlasNormalizationQuality.Unresolved; break;
            }
            d.Rims.Add(rim);
            if (rim.Enabled)
                AtlasMaterialReader.MissingFeature(d,"lighting.rim", "Rim blend and tint are mapped, but exact style and lighting response require additional translation.");
        }

        private static void Subsurface(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasMaterialReader.Has(m,"_SubsurfaceScattering")) return;
            var sss=new AtlasSubsurfaceLayer {
                Enabled=AtlasMaterialReader.Float(m,"_SubsurfaceScattering",0)>0.5f,
                Order=d.Subsurfaces.Count,
                Map=AtlasMaterialReader.Sample(m,"_SSSThicknessMap",AtlasColorInterpretation.LinearData,d,"_SSSThicknessMapUV",true),
                Tint=AtlasMaterialReader.Color(m,"_SSSColor",Color.white),
                Opacity=AtlasMaterialReader.Float(m,"_SSSStrength",0.5f),
                Quality=AtlasNormalizationQuality.Approximate
            };
            d.Subsurfaces.Add(sss);
            if (sss.Enabled)
                AtlasMaterialReader.MissingFeature(d,"lighting.subsurface", "Thickness-map packing and scattering light response are not yet fully normalized.");
        }

        private static void Clearcoat(Material m, AtlasUnifiedMaterial d)
        {
            if (!AtlasMaterialReader.Has(m,"_ClearCoatBRDF")) return;
            var coat=AtlasSemanticReader.Feature(d,"surface.clearcoat",
                AtlasMaterialReader.Float(m,"_ClearCoatBRDF",0)>0.5f);
            coat.Quality=AtlasNormalizationQuality.Approximate;
            coat.Parameters["weight"]=Number(Mathf.Clamp01(AtlasMaterialReader.Float(m,"_ClearCoatStrength",1)));
            coat.Scalars["roughness"]=new AtlasScalarInput {
                Constant=Mathf.Clamp01(AtlasMaterialReader.Float(m,"_ClearCoatSmoothness",1)),
                Sample=AtlasMaterialReader.Sample(m,"_ClearCoatMaps",AtlasColorInterpretation.LinearData,d,"_ClearCoatMapsUV",true),
                Channel=AtlasChannel.Green,
                InvertResult=true
            };
            var mask=AtlasMaterialReader.Mask(m,d,"_ClearCoatMaps",uvProperty:"_ClearCoatMapsUV",poiyomiUV:true);
            if(mask!=null) coat.Masks.Add(mask);
            coat.Colors["reflection.tint"]=new AtlasColorInput { Color=AtlasMaterialReader.Color(m,"_ClearCoatReflectionTint",Color.white) };
            coat.Colors["specular.tint"]=new AtlasColorInput { Color=AtlasMaterialReader.Color(m,"_ClearCoatSpecularTint",Color.white) };
            if(coat.Enabled)
                AtlasMaterialReader.MissingFeature(d,"surface.clearcoat", "Clearcoat strength and roughness are represented; additional reflection and masking modes need exact mapping.");
        }

        private static void Outline(Material m, AtlasUnifiedMaterial d)
        {
            if(!AtlasMaterialReader.Has(m,"_EnableOutlines")) return;
            var outline=new AtlasSemanticFeature {
                Id="geometry.outline", Enabled=AtlasMaterialReader.Float(m,"_EnableOutlines",0)>0.5f,
                Quality=AtlasNormalizationQuality.Unresolved
            };
            outline.Textures["color"]=AtlasMaterialReader.Sample(m,"_OutlineTexture",AtlasColorInterpretation.ColorSRGB,d,"_OutlineTextureUV",true);
            outline.Parameters["emission.intensity"]=Number(AtlasMaterialReader.Float(m,"_OutlineEmission",0));
            d.Features.Add(outline);
            if(outline.Enabled)
                AtlasMaterialReader.MissingFeature(d,"geometry.outline", "Outline geometry expansion, sizing and blending need a semantic generator or a dedicated bake.");
        }
    }
}
