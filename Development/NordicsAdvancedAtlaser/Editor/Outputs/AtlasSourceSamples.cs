using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A few samples of a source, for choices that weigh how things look (the automatic lighting,
    // the matcaps' visible strength), not for baking. Textures are read once as small copies,
    // kept by texture and content, so resolving in the inspector stays fast.
    public static class AtlasSourceSamples
    {
        private const int Size = 4;

        private static readonly Dictionary<(int Id, Hash128 Hash, int Width, int Height), Color[]> Copies =
            new Dictionary<(int, Hash128, int, int), Color[]>();

        /// <summary>A material's base colors: its color × a 4 × 4 copy of its base color texture
        /// (linear), or the color alone. Layers and vertex colors aren't part of them.</summary>
        public static List<Color> Bases(AtlasUnifiedMaterial material)
        {
            var color = material.Surface.BaseColor.Color;
            if (!(material.Surface.BaseColor.Map?.Texture is Texture2D texture)) return new List<Color> { color };
            return Small(texture, Size, Size).Select(p => new Color(p.r * color.r, p.g * color.g, p.b * color.b, 1f)).ToList();
        }

        /// <summary>A mask's average over a 4 × 4 copy of its map, through its channel, levels,
        /// invert and strength; 1 without a map. Bus channels and vertex colors aren't part of it.</summary>
        public static float MaskAverage(AtlasMask mask)
        {
            if (mask == null) return 1f;
            if (!(mask.Map?.Texture is Texture2D texture)) return mask.Evaluate();
            int channel = Mathf.Clamp((int)mask.Channel, 0, 3);
            return Small(texture, Size, Size).Average(p => mask.Evaluate(p[channel]));
        }

        /// <summary>A small copy of a texture (linear), made once per texture and content.</summary>
        public static Color[] Small(Texture2D texture, int width, int height)
        {
            var key = (texture.GetInstanceID(), texture.imageContentsHash, width, height);
            if (Copies.TryGetValue(key, out var pixels)) return pixels;
            using (var loaded = new NordicsTextureWrapper(texture, TextureDataType.Color))
            using (var sized = loaded.Resize(width, height, sampling: TextureSamplingMode.Area))
                pixels = sized.GetPixels();
            Copies[key] = pixels;
            return pixels;
        }

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
