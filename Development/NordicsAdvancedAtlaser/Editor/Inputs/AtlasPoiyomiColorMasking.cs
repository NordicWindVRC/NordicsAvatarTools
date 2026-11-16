using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: RGBA Color Masking. Each enabled channel is a colour layer, and optionally a normal
    // layer and metallic and smoothness layers, all masked by its channel of the mask texture or
    // the vertex colour.
    internal sealed partial class AtlasPoiyomiReader
    {
        private static readonly string[] MaskChannelNames = { "Red", "Green", "Blue", "Alpha" };

        private void ColorMasking(Material m, AtlasUnifiedMaterial d)
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
                var blend = PoiyomiBlend(Int(m, "_RGBA" + name + "BlendType", 0));
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, blend));
                d.Layers.Add(layer);
                ColorMaskAlpha(m, d, i, name, layer, blend);

                ColorMaskNormal(m, d, i, letter, scaleOffset);
                ColorMaskSurface(m, d, i, name);
                ColorMaskOcclusion(m, d, i, name, scaleOffset);
            }
            ReportPackedOptions(m, d, "_RGBAMetallicMaps");
            ReportPackedOptions(m, d, "_RGBASmoothnessMaps");
            ReportPackedOptions(m, d, "_RGBAAOMaps");
        }

        // Affect Alpha (Poiyomi 11): alpha = lerp(alpha, saturate(blend(alpha, saturate(colour alpha + add))), mask),
        // a layer of the same colour on the alpha.
        private void ColorMaskAlpha(Material m, AtlasUnifiedMaterial d, int index, string name, AtlasSurfaceLayer color, AtlasBlend blend)
        {
            if (!On(m, "_RGBA" + name + "AffectsAlpha")) return;
            if (color.SourceAlphaOffset != 0f)
                d.Report(AtlasIssueKind.Unmapped, "rgba.affects-alpha", "A colour mask region's alpha add on the material's alpha isn't mapped yet.");
            var layer = new AtlasSurfaceLayer { Mask = color.Mask };
            layer.Source.Color = color.Source.Color;
            layer.Source.Map = color.Source.Map;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Alpha, blend) { Channel = AtlasChannel.Alpha });
            d.Layers.Add(layer);
        }

        // AO (Poiyomi 11): occlusion = min(occlusion, lerp(1, the AO map's channel, mask × strength)), the
        // map sampled with its own settings or the region's (custom sampling), and the region's scale and offset.
        private void ColorMaskOcclusion(Material m, AtlasUnifiedMaterial d, int index, string name, Vector4 scaleOffset)
        {
            float strength = Float(m, "_RGBA" + name + "AOStrength", 0f);
            if (strength <= 0f) return;
            string p = "_RGBA" + name + "AO";
            AtlasTextureReference map;
            if (On(m, p + "CustomSampling"))
            {
                map = AtlasSourceMapping.Texture(m, "_RGBAAOMaps", AtlasTextureInterpretation.Data);
                if (map != null)
                {
                    var st = AtlasMaterialReader.Vector(m, p + "ScaleOffset", new Vector4(1f, 1f, 0f, 0f));
                    map.Tiling = new Vector2(st.x, st.y);
                    map.Offset = new Vector2(st.z, st.w);
                    map.Space = UVSpace(m, d, p + "UV");
                    var pan = AtlasMaterialReader.Vector(m, p + "Pan", Vector4.zero);
                    map.Scroll = new Vector2(pan.x, pan.y) * 0.05f;
                    if (On(m, p + "Stochastic")) map.Sampling = AtlasTextureSampling.Stochastic;
                }
            }
            else map = Texture(m, d, "_RGBAAOMaps", AtlasTextureInterpretation.Data);
            if (map == null) return;
            var layer = new AtlasSurfaceLayer { Mask = RegionMask(m, d, index, name, p + "MaskChannel", p + "GlobalMaskChannel", p + "GlobalMaskBlendType") };
            layer.Source.Map = ScaledOffset(map, scaleOffset);
            // The darker wins, as Poiyomi's min; the same where regions don't overlap.
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Occlusion, AtlasBlend.Darken, strength) { Channel = Channels[index] });
            d.Layers.Add(layer);
        }

        // A region's own mask channel (4: the colour's) and global mask (17: the colour's).
        private AtlasMask RegionMask(Material m, AtlasUnifiedMaterial d, int index, string name, string channel, string global, string globalBlend)
        {
            int c = Int(m, channel, 4);
            int g = Int(m, global, 17);
            string colorChannel = "_Rgb" + name + "MaskChannel";
            var maskChannel = Channels[Mathf.Clamp(c > 3 ? Int(m, colorChannel, index) : c, 0, 3)];
            // Poiyomi 10's vertex colour mask type.
            var mask = Int(m, "_RGBMaskType", 0) == 1 ? new AtlasMask { VertexColor = true, Channel = maskChannel } : Mask(m, d, "_RGBMask", maskChannel);
            return g > 16 ? BusMask(m, mask, "_Rgb" + name + "GlobalMaskChannel", "_Rgb" + name + "GlobalMaskBlendType") : BusMask(m, mask, global, globalBlend);
        }

        // The channel's normal: whiteout over the base normal, or replacing it when no source is chosen.
        private void ColorMaskNormal(Material m, AtlasUnifiedMaterial d, int index, string letter, Vector4 scaleOffset)
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
        private void ColorMaskSurface(Material m, AtlasUnifiedMaterial d, int index, string name)
        {
            if (!On(m, "_RGBAPBR" + name + "Enabled") || !On(m, "_MochieBRDF")) return;
            if (On(m, "_RGBA" + name + "PBRSplitMaskSample"))
                d.Report(AtlasIssueKind.Unmapped, "rgba.pbr-sampling", "Colour mask metallic and smoothness with their own UVs aren't mapped yet.");
            var channel = Channels[index];
            var metallic = Texture(m, d, "_RGBAMetallicMaps", AtlasTextureInterpretation.Data);
            if (metallic != null)
                d.Layers.Add(ValueLayer(m, d, index, name, metallic, new AtlasLayerTarget(AtlasSurfaceTarget.Metallic, AtlasBlend.Replace)
                    { Channel = channel, InvertChannel = On(m, "_RGBA" + name + "MetallicInvert") }));
            var smoothness = Texture(m, d, "_RGBASmoothnessMaps", AtlasTextureInterpretation.Data);
            if (smoothness != null)
            {
                // Smoothness blends (Poiyomi 11) on roughness: min and max swap; add, subtract and
                // multiply don't carry over.
                int blend = Int(m, "_RGBA" + name + "SmoothnessBlend", 0);
                var roughnessBlend = blend == 4 ? AtlasBlend.Lighten : blend == 5 ? AtlasBlend.Darken : AtlasBlend.Replace;
                if (blend != 0 && blend != 4 && blend != 5)
                    d.Report(AtlasIssueKind.Unmapped, "rgba.smoothness-blend", "A colour mask region's smoothness blended by add, subtract or multiply isn't mapped yet; replaced.");
                d.Layers.Add(ValueLayer(m, d, index, name, smoothness, new AtlasLayerTarget(AtlasSurfaceTarget.Roughness, roughnessBlend)
                    { Channel = channel, InvertChannel = !On(m, "_RGBA" + name + "SmoothnessInvert") }));
            }
            if (On(m, "_RGBASmoothnessAffectsSecondSpecular") && On(m, "_Specular2ndLayer"))
                d.Report(AtlasIssueKind.Unmapped, "rgba.second-specular", "Colour mask smoothness on the second specular lobe isn't mapped yet.");
        }

        // Metallic and smoothness read the region's PBR mask channel and global mask (Poiyomi 11; the colour's in 10).
        private AtlasSurfaceLayer ValueLayer(Material m, AtlasUnifiedMaterial d, int index, string name, AtlasTextureReference map, AtlasLayerTarget target)
        {
            string p = "_RGBA" + name + "PBR";
            var layer = new AtlasSurfaceLayer { Mask = RegionMask(m, d, index, name, p + "MaskChannel", p + "GlobalMaskChannel", p + "GlobalMaskBlendType") };
            layer.Source.Map = map;
            layer.Targets.Add(target);
            return layer;
        }

        // The channel of the mask texture, or of the vertex colour, with its global mask
        // (_Rgb<Name>MaskChannel → _Rgb<Name>GlobalMaskChannel).
        private AtlasMask ColorMask(Material m, AtlasUnifiedMaterial d, string channelProperty, int fallback)
        {
            var channel = Channels[Mathf.Clamp(Int(m, channelProperty, fallback), 0, 3)];
            var mask = Int(m, "_RGBMaskType", 0) == 1 ? new AtlasMask { VertexColor = true, Channel = channel } : Mask(m, d, "_RGBMask", channel);
            return BusMask(m, mask, channelProperty.Replace("MaskChannel", "GlobalMaskChannel"), channelProperty.Replace("MaskChannel", "GlobalMaskBlendType"));
        }

        // Scale & Offset merges into the texture's own: tiling × scale, offset + offset.
        private AtlasTextureReference ScaledOffset(AtlasTextureReference map, Vector4 scaleOffset)
        {
            if (map == null) return null;
            map.Tiling = new Vector2(map.Tiling.x * scaleOffset.x, map.Tiling.y * scaleOffset.y);
            map.Offset += new Vector2(scaleOffset.z, scaleOffset.w);
            return map;
        }
    }
}
