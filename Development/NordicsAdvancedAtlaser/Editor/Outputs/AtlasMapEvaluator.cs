using System;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // What a source's virtual map looks like over its own UV 0–1, computed on the CPU. It covers
    // an explicit bakeable subset (ATLAS_OUTPUT.md, "Bakeable subset") that grows feature by
    // feature; what is outside it is left out, never guessed at.
    public static class AtlasMapEvaluator
    {
        public static NordicsTextureWrapper Evaluate(AtlasVirtualMap map, AtlasUnifiedMaterial source, int width, int height)
        {
            switch (map)
            {
                case AtlasVirtualMap.BaseColor: return BaseColor(source, width, height);
                default: throw new ArgumentOutOfRangeException(nameof(map), map, "[Nordic's Advanced Atlaser] No evaluator for this virtual map.");
            }
        }

        // The settings a virtual map is baked with: colour maps sRGB, clamped at the atlas edges.
        public static TextureSettings Settings(AtlasVirtualMap map) => new TextureSettings
        {
            DataType = TextureDataType.Color, SRGB = true, WrapMode = TextureWrapMode.Clamp,
            OutputChannels = TextureOutputChannels.Auto
        };

        // The base colour texture as it is, with its tiling, offset and rotation on UV0; white
        // without a texture. A texture on another space (a projection, another UV channel) is
        // taken as it is. The colour it's multiplied by, vertex colours, layers and colour
        // adjustments aren't in the subset yet.
        private static NordicsTextureWrapper BaseColor(AtlasUnifiedMaterial source, int width, int height)
        {
            var reference = source.Surface.BaseColor.Map;
            if (!(reference?.Texture is Texture2D texture))
            {
                var white = new NordicsTextureWrapper(width, height, Settings(AtlasVirtualMap.BaseColor));
                white.Fill(Color.white);
                return white;
            }
            using (var loaded = new NordicsTextureWrapper(texture, TextureDataType.Color))
            {
                bool uv0 = reference.Space == AtlasTextureSpace.UV0;
                var tiling = uv0 ? reference.Tiling : Vector2.one;
                var offset = uv0 ? reference.Offset : Vector2.zero;
                float rotation = uv0 ? reference.Rotation : 0f;
                // The unified transform turns the UV counterclockwise around (0.5, 0.5), then
                // multiplies by the tiling around the origin and adds the offset. The wrapper
                // scales around the centre instead, which (tiling − 1) / 2 more offset makes up.
                var wrapperOffset = offset + (tiling - Vector2.one) * 0.5f;
                var result = loaded.Resize(width, height, scale: tiling, offset: wrapperOffset, rotation: rotation,
                    sampling: TextureSamplingMode.Area);
                result.Settings.WrapMode = TextureWrapMode.Clamp;
                return result;
            }
        }
    }
}
