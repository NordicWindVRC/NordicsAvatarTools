using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: RGBA Color Masking. Each enabled channel is a colour layer, and optionally a normal
    // layer and metallic and smoothness layers, all masked by its channel of the mask texture or
    // the vertex colour.
    public abstract partial class AtlasPoiyomiParser
    {
        private static readonly string[] MaskChannelNames = { "Red", "Green", "Blue", "Alpha" };

        private static void ColorMasking(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_RGBMaskEnabled")) return;
            for (int i = 0; i < 4; i++)
            {
                string name = MaskChannelNames[i], letter = "RGBA"[i].ToString();
                if (!On(m, "_RGBA" + name + "Enable")) continue;
                var scaleOffset = AtlasMaterialReader.Vector(m, "_RGBA" + name + "ScaleOffset", new Vector4(1f, 1f, 0f, 0f));

                // Colour: lerp(b, blend(b, colour × texture), mask × saturate(alpha + alpha add)), and glow.
                var layer = new AtlasSurfaceLayer
                {
                    Mask = ColorMask(m, d, "_Rgb" + name + "MaskChannel", i),
                    SourceAlphaIsOpacity = true,
                    SourceAlphaOffset = Float(m, "_" + name + "AlphaAdd", 0f),
                    EmissionAmount = Float(m, "_RGBA" + name + "EmissionStrength", 0f)
                };
                layer.Source.Color = TintColor(m, d, "_" + name + "Color", "_" + name + "ColorThemeIndex", layer.Source);
                layer.Source.Map = ScaledOffset(Texture(m, d, "_" + name + "Texture"), scaleOffset);
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, PoiyomiBlend(Int(m, "_RGBA" + name + "BlendType", 0))));
                d.Layers.Add(layer);

                ColorMaskNormal(m, d, i, letter, scaleOffset);
                ColorMaskSurface(m, d, i, name);
            }
        }

        // The channel's normal: whiteout over the base normal, or replacing it when no source is chosen.
        private static void ColorMaskNormal(Material m, AtlasUnifiedMaterial d, int index, string letter, Vector4 scaleOffset)
        {
            float scale = Float(m, "_RgbNormal" + letter + "Scale", 0f);
            var map = Texture(m, d, "_RgbNormal" + letter, AtlasTextureInterpretation.TangentNormal);
            if (map == null || scale <= 0f) return;
            var sources = AtlasMaterialReader.Vector(m, "_RgbNormal" + letter + "BlendSources", Vector4.zero);
            var layer = new AtlasSurfaceLayer { Mask = ColorMask(m, d, "_RgbNormal" + letter + "MaskChannel", index) };
            layer.Source.Map = ScaledOffset(map, scaleOffset);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace, scale) { ReplaceNormal = sources.x <= 0.5f });
            d.Layers.Add(layer);
            if (sources.y > 0.5f || sources.z > 0.5f || sources.w > 0.5f)
                d.Report(AtlasIssueKind.Unmapped, "rgba.normal-sources", "Blending a colour mask normal with another channel's normal isn't mapped yet.");
        }

        // Metallic and smoothness: this channel's value in the shared maps replaces the surface's,
        // by the mask. Only with Reflections & Specular, and only where a map is set.
        private static void ColorMaskSurface(Material m, AtlasUnifiedMaterial d, int index, string name)
        {
            if (!On(m, "_RGBAPBR" + name + "Enabled") || !On(m, "_MochieBRDF")) return;
            if (On(m, "_RGBA" + name + "PBRSplitMaskSample"))
                d.Report(AtlasIssueKind.Unmapped, "rgba.pbr-sampling", "Colour mask metallic and smoothness with their own UVs aren't mapped yet.");
            var channel = Channels[index];
            var metallic = Texture(m, d, "_RGBAMetallicMaps", AtlasTextureInterpretation.Data);
            if (metallic != null)
                d.Layers.Add(ValueLayer(m, d, index, metallic, new AtlasLayerTarget(AtlasSurfaceTarget.Metallic, AtlasBlend.Replace)
                    { Channel = channel, InvertChannel = On(m, "_RGBA" + name + "MetallicInvert") }));
            var smoothness = Texture(m, d, "_RGBASmoothnessMaps", AtlasTextureInterpretation.Data);
            if (smoothness != null)
                d.Layers.Add(ValueLayer(m, d, index, smoothness, new AtlasLayerTarget(AtlasSurfaceTarget.Roughness, AtlasBlend.Replace)
                    { Channel = channel, InvertChannel = !On(m, "_RGBA" + name + "SmoothnessInvert") }));
        }

        private static AtlasSurfaceLayer ValueLayer(Material m, AtlasUnifiedMaterial d, int index, AtlasTextureReference map, AtlasLayerTarget target)
        {
            var layer = new AtlasSurfaceLayer { Mask = ColorMask(m, d, "_Rgb" + MaskChannelNames[index] + "MaskChannel", index) };
            layer.Source.Map = map;
            layer.Targets.Add(target);
            return layer;
        }

        // The channel of the mask texture, or of the vertex colour, with its global mask
        // (_Rgb<Name>MaskChannel → _Rgb<Name>GlobalMaskChannel).
        private static AtlasMask ColorMask(Material m, AtlasUnifiedMaterial d, string channelProperty, int fallback)
        {
            var channel = Channels[Mathf.Clamp(Int(m, channelProperty, fallback), 0, 3)];
            var mask = Int(m, "_RGBMaskType", 0) == 1 ? new AtlasMask { VertexColor = true, Channel = channel } : Mask(m, d, "_RGBMask", channel);
            return BusMask(m, mask, channelProperty.Replace("MaskChannel", "GlobalMaskChannel"), channelProperty.Replace("MaskChannel", "GlobalMaskBlendType"));
        }

        // Scale & Offset merges into the texture's own: tiling × scale, offset + offset.
        private static AtlasTextureReference ScaledOffset(AtlasTextureReference map, Vector4 scaleOffset)
        {
            if (map == null) return null;
            map.Tiling = new Vector2(map.Tiling.x * scaleOffset.x, map.Tiling.y * scaleOffset.y);
            map.Offset += new Vector2(scaleOffset.z, scaleOffset.w);
            return map;
        }
    }
}
