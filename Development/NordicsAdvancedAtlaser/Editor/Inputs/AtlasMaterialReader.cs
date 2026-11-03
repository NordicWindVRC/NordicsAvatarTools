using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Source-side translation helpers. Source property IDs are allowed ONLY in this file and
    /// parser implementations; none are copied into AtlasUnifiedMaterial.
    /// Every output receives the same semantic types, regardless of the input shader.
    /// </summary>
    internal static class AtlasMaterialReader
    {
        public static AtlasUnifiedMaterial Create(Material material)
        {
            var data = new AtlasUnifiedMaterial();
            data.Render.Queue = material.renderQueue;
            data.Render.DoubleSided = Float(material, "_Cull", 2f) == 0f;
            data.Render.ZWrite = Float(material, "_ZWrite", 1f) > 0.5f;
            int sourceBlend = Mathf.RoundToInt(Float(material,"_SrcBlend",1f));
            int destinationBlend = Mathf.RoundToInt(Float(material,"_DstBlend",0f));
            if (sourceBlend>=0 && sourceBlend<=10) data.Render.SourceBlend=(AtlasBlendFactor)sourceBlend;
            if (destinationBlend>=0 && destinationBlend<=10) data.Render.DestinationBlend=(AtlasBlendFactor)destinationBlend;
            int depthTest=Mathf.RoundToInt(Float(material,"_ZTest",4f));
            if(depthTest>=0 && depthTest<=8) data.Render.DepthTest=(AtlasDepthTest)depthTest;
            int cull=Mathf.RoundToInt(Float(material,"_Cull",2f));
            if(cull>=0 && cull<=2) data.Render.Cull=(AtlasCullMode)cull;
            data.Render.DoubleSided=data.Render.Cull==AtlasCullMode.Off;
            data.Render.AlphaToCoverage=Float(material,"_AlphaToMask",0f)>0.5f;
            data.Render.AlphaCutoff = Mathf.Clamp01(Float(material, "_Cutoff", 0.5f));
            string renderTag = material.GetTag("RenderType", false, "");
            if (renderTag == "TransparentCutout") data.Render.Mode = AtlasSurfaceMode.Cutout;
            else if (renderTag == "Transparent") data.Render.Mode = AtlasSurfaceMode.Transparent;
            else if (data.Render.Queue >= 3000) data.Render.Mode = AtlasSurfaceMode.Transparent;
            else if (data.Render.Queue >= 2450) data.Render.Mode = AtlasSurfaceMode.Cutout;
            return data;
        }

        public static bool Has(Material material, string property)
        { return material != null && !string.IsNullOrEmpty(property) && material.HasProperty(property); }
        public static float Float(Material material, string name, float fallback)
        {
            if (!Has(material, name)) return fallback;
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0) return fallback;
            var type = material.shader.GetPropertyType(index);
            if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector || type == ShaderPropertyType.Texture) return fallback;
            // Material.GetFloat() is not reliable for native ShaderLab Int properties.
            // VRChat Toon Standard and Poiyomi use Int extensively for modes/toggles.
            return type == ShaderPropertyType.Int ? material.GetInt(name) : material.GetFloat(name);
        }
        public static Color Color(Material material, string name, Color fallback)
        {
            if (!Has(material, name)) return fallback;
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Color) return fallback;
            return material.GetColor(name);
        }
        public static Texture Texture(Material material, string name)
        {
            if (!Has(material, name)) return null;
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Texture) return null;
            return material.GetTexture(name);
        }
        public static AtlasTextureSample Sample(Material material, string property,
            AtlasColorInterpretation interpretation, AtlasUnifiedMaterial output,
            string uvProperty = null, bool poiyomiUV = false)
        {
            var texture = Texture(material, property);
            if (texture == null) return null;
            var sample = new AtlasTextureSample { Texture = texture, Interpretation = interpretation };
            sample.Mapping.Scale = material.GetTextureScale(property);
            sample.Mapping.Offset = material.GetTextureOffset(property);
            if (!string.IsNullOrEmpty(uvProperty) && Has(material, uvProperty))
            {
                int mode = Mathf.RoundToInt(Float(material, uvProperty, 0));
                if (poiyomiUV) DecodePoiyomiUV(mode, sample.Mapping, output);
                else if (mode >= 0 && mode <= 3) sample.Mapping.UVIndex = mode;
                else
                {
                    sample.Mapping.Space = AtlasCoordinateSpace.Unknown;
                    sample.Mapping.Quality = AtlasNormalizationQuality.Unresolved;
                    output.Issues.Add(new AtlasNormalizationIssue("texture.mapping", AtlasNormalizationSeverity.Warning,
                        "Unknown source UV selection; source-specific UV mode was not passed to output."));
                }
            }
            return sample;
        }
        private static void DecodePoiyomiUV(int mode, AtlasUVMapping uv, AtlasUnifiedMaterial output)
        {
            if (mode >= 0 && mode <= 3) { uv.Space = AtlasCoordinateSpace.UV; uv.UVIndex = mode; return; }
            switch (mode)
            {
                case 4: uv.Space = AtlasCoordinateSpace.Panosphere; break;
                case 5: uv.Space = AtlasCoordinateSpace.WorldPosition; break;
                case 6: uv.Space = AtlasCoordinateSpace.Polar; break;
                case 7: uv.Space = AtlasCoordinateSpace.Distorted; break;
                case 8: uv.Space = AtlasCoordinateSpace.ObjectPosition; break;
                case 9: uv.Space = AtlasCoordinateSpace.ViewNormal; break;
                case 10: uv.Space = AtlasCoordinateSpace.ScreenPosition; break;
                default: uv.Space = AtlasCoordinateSpace.Unknown; break;
            }
            // Mapping enum is normalized, but some projections need extra input parameters
            // or a bake to exactly reproduce the original Poiyomi projection.
            if (uv.Space == AtlasCoordinateSpace.WorldPosition || uv.Space == AtlasCoordinateSpace.ObjectPosition ||
                uv.Space == AtlasCoordinateSpace.ScreenPosition || uv.Space == AtlasCoordinateSpace.ViewNormal)
                uv.Quality = AtlasNormalizationQuality.Approximate;
            else uv.Quality = AtlasNormalizationQuality.Unresolved;
            if (uv.Quality == AtlasNormalizationQuality.Unresolved)
                output.Issues.Add(new AtlasNormalizationIssue("texture.mapping", AtlasNormalizationSeverity.Warning,
                    "Nonstandard source UV projection requires semantic parameters/baking before exact reproduction."));
        }
        public static AtlasMaskDefinition Mask(Material m, AtlasUnifiedMaterial d, string property,
            string channelProperty = null, string invertProperty = null, string uvProperty = null, bool poiyomiUV = false)
        {
            var map = Sample(m, property, AtlasColorInterpretation.LinearData, d, uvProperty, poiyomiUV);
            if (map == null) return null;
            var result = new AtlasMaskDefinition { Sample = map, Invert = Float(m, invertProperty, 0f) > 0.5f };
            var channel = Mathf.RoundToInt(Float(m, channelProperty, 0));
            result.Channel = channel == 1 ? AtlasChannel.Green : channel == 2 ? AtlasChannel.Blue :
                channel == 3 ? AtlasChannel.Alpha : AtlasChannel.Red;
            if (channel < 0 || channel > 3)
                d.Issues.Add(new AtlasNormalizationIssue("mask.channel", AtlasNormalizationSeverity.Warning,
                    "Mask channel selection could not be translated precisely."));
            return result;
        }
        public static void BaseColor(Material m, AtlasUnifiedMaterial d, string texture, string tint,
            string uvProperty = null, bool poiyomiUV = false)
        {
            // No texture is legitimate: the constant color is still a real base-color input.
            if (!Has(m, texture) && !Has(m, tint)) return;
            d.Surface.BaseColor.Color = Color(m, tint, UnityEngine.Color.white);
            d.Surface.BaseColor.Map = Sample(m, texture, AtlasColorInterpretation.ColorSRGB, d, uvProperty, poiyomiUV);
        }
        public static AtlasNormalLayer Normal(Material m, AtlasUnifiedMaterial d, string texture,
            string strength = null, string enabledProperty = null, string uvProperty = null, bool poiyomiUV = false,
            string maskProperty = null)
        {
            var sample = Sample(m, texture, AtlasColorInterpretation.TangentNormal, d, uvProperty, poiyomiUV);
            if (sample == null) return null;
            var layer = new AtlasNormalLayer {
                Order = d.Normals.Count, Map = sample, Enabled = Float(m, enabledProperty, 1f) > 0.5f,
                Strength = Float(m, strength, 1f), NormalBlend = d.Normals.Count == 0 ? AtlasNormalBlend.Replace : AtlasNormalBlend.Unknown,
                Blend = AtlasBlendOperation.Replace,
                Mask = Mask(m,d,maskProperty)
            };
            if (layer.NormalBlend == AtlasNormalBlend.Unknown)
            {
                layer.Quality = AtlasNormalizationQuality.Unresolved;
                d.Issues.Add(new AtlasNormalizationIssue("normal.blending", AtlasNormalizationSeverity.Warning,
                    "Multiple normal layers detected; their source-specific compositing operation is not yet resolved."));
            }
            d.Normals.Add(layer);
            return layer;
        }
        public static AtlasEmissionLayer Emission(Material m, AtlasUnifiedMaterial d, string texture,
            string tint = null, string strength = null, string enabled = null, string uv = null,
            bool poiyomiUV = false, string mask = null)
        {
            var sample = Sample(m, texture, AtlasColorInterpretation.ColorSRGB, d, uv, poiyomiUV);
            var color = Color(m, tint, UnityEngine.Color.white);
            if (sample == null && !Has(m,tint)) return null;
            var layer = new AtlasEmissionLayer {
                Order = d.Emissions.Count, Map = sample, Tint = color, Intensity = Float(m,strength,1f),
                Enabled = Float(m,enabled,1f)>0.5f, Blend = AtlasBlendOperation.Add,
                Mask = Mask(m,d,mask)
            };
            d.Emissions.Add(layer);
            return layer;
        }
        public static AtlasDetailLayer Detail(Material m, AtlasUnifiedMaterial d, string texture,
            string strength = null, string tint = null, string enabled = null, string uv = null, bool poiyomiUV = false,
            string mask = null)
        {
            var sample = Sample(m,texture,AtlasColorInterpretation.ColorSRGB,d,uv,poiyomiUV);
            if (sample == null) return null;
            var layer = new AtlasDetailLayer { Order=d.Details.Count, Map=sample,
                Opacity=Float(m,strength,1f), Tint=Color(m,tint,UnityEngine.Color.white),
                Enabled=Float(m,enabled,1f)>0.5f, Mask=Mask(m,d,mask), Blend=AtlasBlendOperation.Multiply };
            d.Details.Add(layer);
            return layer;
        }
        public static AtlasDecalLayer Decal(Material m, AtlasUnifiedMaterial d, string texture,
            string tint = null, string enabled = null, string uv = null, bool poiyomiUV = false, string mask = null)
        {
            var sample = Sample(m,texture,AtlasColorInterpretation.ColorSRGB,d,uv,poiyomiUV);
            if (sample == null) return null;
            var layer = new AtlasDecalLayer { Order=d.Decals.Count,Map=sample,
                Tint=Color(m,tint,UnityEngine.Color.white),Enabled=Float(m,enabled,1f)>0.5f,
                Mask=Mask(m,d,mask),Blend=AtlasBlendOperation.Alpha };
            d.Decals.Add(layer);
            return layer;
        }
        public static AtlasMatcapLayer Matcap(Material m, AtlasUnifiedMaterial d, string texture,
            string strength = null, string enabled = null, string mask = null)
        {
            var sample = Sample(m,texture,AtlasColorInterpretation.ColorSRGB,d);
            if (sample == null) return null;
            // Matcap projection is normalized as view-normal space, not UV0.
            sample.Mapping.Space = AtlasCoordinateSpace.ViewNormal;
            var layer = new AtlasMatcapLayer { Order=d.Matcaps.Count,Map=sample,
                Intensity=Float(m,strength,1f),Enabled=Float(m,enabled,1f)>0.5f,
                Mask=Mask(m,d,mask),Projection=AtlasMatcapProjection.ViewNormal,
                Blend=AtlasBlendOperation.Unknown,Quality=AtlasNormalizationQuality.Unresolved };
            d.Matcaps.Add(layer);
            return layer;
        }
        public static void NamedMask(Material m, AtlasUnifiedMaterial d, string texture,
            string channel = null, string invert = null, string uv = null, bool poiyomiUV = false,
            AtlasMaskPurpose purpose = AtlasMaskPurpose.General)
        {
            var mask = Mask(m,d,texture,channel,invert,uv,poiyomiUV);
            if (mask == null) return;
            d.Masks.Add(new AtlasNamedMask { Order=d.Masks.Count, Map=mask.Sample,
                Purpose=purpose, Channel=mask.Channel, Invert=mask.Invert,Blend=AtlasBlendOperation.Multiply });
            if (purpose == AtlasMaskPurpose.Alpha) d.Surface.AlphaMask = mask;
            else if (purpose == AtlasMaskPurpose.BaseColor) d.Surface.BaseColorMask = mask;
        }
        public static void Metallic(Material m, AtlasUnifiedMaterial d, string scalar, string texture,
            AtlasChannel channel = AtlasChannel.Red)
        {
            if (!Has(m,scalar) && !Has(m,texture)) return;
            d.Surface.Metallic.Constant=Mathf.Clamp01(Float(m,scalar,0f));
            d.Surface.Metallic.Sample=Sample(m,texture,AtlasColorInterpretation.LinearData,d);
            d.Surface.Metallic.Channel=channel;
        }
        public static void Roughness(Material m, AtlasUnifiedMaterial d, string scalar, string texture,
            bool inputIsSmoothness, AtlasChannel channel = AtlasChannel.Alpha)
        {
            if (!Has(m,scalar) && !Has(m,texture)) return;
            d.Surface.Roughness.Constant=Mathf.Clamp01(Float(m,scalar,0.5f));
            d.Surface.Roughness.Sample=Sample(m,texture,AtlasColorInterpretation.LinearData,d);
            d.Surface.Roughness.Channel=channel;
            d.Surface.Roughness.InvertResult=inputIsSmoothness;
        }
        public static void Occlusion(Material m, AtlasUnifiedMaterial d, string scalar, string texture,
            AtlasChannel channel = AtlasChannel.Green)
        {
            if (!Has(m,scalar) && !Has(m,texture)) return;
            d.Surface.Occlusion.Constant=Mathf.Clamp01(Float(m,scalar,1f));
            d.Surface.Occlusion.Sample=Sample(m,texture,AtlasColorInterpretation.LinearData,d);
            d.Surface.Occlusion.Channel=channel;
        }
        public static void Specular(Material m, AtlasUnifiedMaterial d, string tint, string texture)
        {
            if (!Has(m,tint) && !Has(m,texture)) return;
            d.Surface.Specular.Color=Color(m,tint,new UnityEngine.Color(.04f,.04f,.04f,1f));
            d.Surface.Specular.Map=Sample(m,texture,AtlasColorInterpretation.ColorSRGB,d);
        }
        public static void StandardRenderMode(Material m, AtlasUnifiedMaterial d)
        {
            if (!Has(m,"_Mode")) return;
            switch (Mathf.RoundToInt(Float(m,"_Mode",0)))
            {
                case 0: d.Render.Mode=AtlasSurfaceMode.Opaque; break;
                case 1: d.Render.Mode=AtlasSurfaceMode.Cutout; break;
                case 2: d.Render.Mode=AtlasSurfaceMode.Transparent; break;
                case 3: d.Render.Mode=AtlasSurfaceMode.Premultiplied; break;
                default: d.Render.Mode=AtlasSurfaceMode.Unknown; break;
            }
        }
        public static void MissingFeature(AtlasUnifiedMaterial d, string semanticId, string reason)
        {
            d.Issues.Add(new AtlasNormalizationIssue(semanticId, AtlasNormalizationSeverity.Warning, reason));
        }
    }
}
