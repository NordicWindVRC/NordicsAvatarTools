using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>Small helpers for mappings every parser repeats.</summary>
    internal static class AtlasSourceMapping
    {
        // A texture property with its tiling and offset, sampled from UV0; null when no texture is
        // assigned.
        public static AtlasTextureReference Texture(Material m, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color)
        {
            var texture = AtlasMaterialReader.Texture(m, property);
            if (texture == null) return null;
            return new AtlasTextureReference {
                Texture = texture, Interpretation = interpretation,
                Tiling = AtlasMaterialReader.TextureScale(m, property),
                Offset = AtlasMaterialReader.TextureOffset(m, property)
            };
        }

        // A colour property times a texture property. The colour is converted to linear.
        public static void Color(Material m, AtlasColorInput target, string texture, string color)
        {
            target.Color = AtlasMaterialReader.ColorLinear(m, color, UnityEngine.Color.white);
            target.Map = Texture(m, texture);
        }
    }
}
