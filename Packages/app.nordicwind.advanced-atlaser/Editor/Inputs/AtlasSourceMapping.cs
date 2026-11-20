using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>Small helpers for mappings every parser repeats.</summary>
    internal static class AtlasSourceMapping
    {
        // A texture property with its tiling and offset, sampled from UV0; null when no texture is
        // assigned. tilingFrom: another texture property whose tiling and offset it uses, for
        // shaders where several maps share one.
        public static AtlasTextureReference Texture(Material m, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color, string tilingFrom = null)
        {
            var texture = AtlasMaterialReader.Texture(m, property);
            if (texture == null) return null;
            string tiling = tilingFrom ?? property;
            return new AtlasTextureReference {
                Texture = texture, Interpretation = interpretation,
                Tiling = AtlasMaterialReader.TextureScale(m, tiling),
                Offset = AtlasMaterialReader.TextureOffset(m, tiling)
            };
        }

        // A color property times a texture property. The color is converted to linear.
        public static void Color(Material m, AtlasColorInput target, string texture, string color)
        {
            target.Color = AtlasMaterialReader.ColorLinear(m, color, UnityEngine.Color.white);
            target.Map = Texture(m, texture);
        }

        // Unity's detail maps (Standard, Standard Lite): detail albedo ×2 and detail normal on the
        // detail tiling and UV set (_UVSec), faded in by the detail mask's alpha, which uses the
        // albedo's tiling. Added as a color layer and a normal layer sharing the mask.
        public static void UnityDetail(Material m, AtlasUnifiedMaterial d, bool withNormal)
        {
            AtlasMask mask = null;
            var maskMap = Texture(m, "_DetailMask", AtlasTextureInterpretation.Data, "_MainTex");
            if (maskMap != null) mask = new AtlasMask { Map = maskMap, Channel = AtlasChannel.Alpha };
            var space = AtlasMaterialReader.Float(m, "_UVSec", 0f) > 0.5f ? AtlasTextureSpace.UV1 : AtlasTextureSpace.UV0;

            var albedo = Texture(m, "_DetailAlbedoMap");
            if (albedo != null)
            {
                albedo.Space = space;
                var layer = new AtlasSurfaceLayer { Mask = mask };
                layer.Source.Map = albedo;
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, AtlasBlend.MultiplyDouble));
                d.Layers.Add(layer);
            }

            var normal = withNormal ? Texture(m, "_DetailNormalMap", AtlasTextureInterpretation.TangentNormal, "_DetailAlbedoMap") : null;
            if (normal != null)
            {
                normal.Space = space;
                var layer = new AtlasSurfaceLayer { Mask = mask };
                layer.Source.Map = normal;
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace,
                    AtlasMaterialReader.Float(m, "_DetailNormalMapScale", 1f)));
                d.Layers.Add(layer);
            }
        }
    }
}
