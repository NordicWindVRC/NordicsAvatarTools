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

        // The base color texels a source's meshes use, by its unified material: up to 16 colors
        // from the darkest to the brightest. A texture's unused parts (often black, and often
        // most of it) would otherwise make the material look darker than it shows.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AtlasUnifiedMaterial, List<Color>> Used =
            new System.Runtime.CompilerServices.ConditionalWeakTable<AtlasUnifiedMaterial, List<Color>>();
        private const int UsedSize = 16;

        /// <summary>Tells the samples where a source's meshes use its base color texture (its
        /// UV0 triangles), so its base colors are taken there. Without triangles, or for a
        /// texture on another UV channel or with a scale or offset, they are taken from the
        /// whole texture.</summary>
        public static void Cover(AtlasUnifiedMaterial material, IReadOnlyList<AtlasUVTriangles> coverage)
        {
            if (material == null) return;
            Used.Remove(material);
            var map = material.Surface.BaseColor.Map;
            if (coverage == null || coverage.Count == 0 || !(map?.Texture is Texture2D texture) || map.Space != AtlasTextureSpace.UV0 ||
                map.Tiling != Vector2.one || map.Offset != Vector2.zero)
                return;
            using (var mask = AtlasCoverage.Paint(coverage, UsedSize, UsedSize))
            {
                if (mask == null) return;
                var pixels = Small(texture, UsedSize, UsedSize);
                var colors = new List<Color>();
                for (int y = 0; y < UsedSize; y++)
                    for (int x = 0; x < UsedSize; x++)
                        if (mask.IsPixelUsed(x, y)) colors.Add(pixels[y * UsedSize + x]);
                if (colors.Count == 0) return;
                // As many samples as the whole texture gives, each the average of a share of
                // the used texels in order of brightness.
                colors.Sort((a, b) => Luminance(a).CompareTo(Luminance(b)));
                int count = Mathf.Min(Size * Size, colors.Count);
                var samples = new List<Color>(count);
                for (int i = 0; i < count; i++)
                {
                    int from = i * colors.Count / count, to = (i + 1) * colors.Count / count;
                    var sum = new Color(0f, 0f, 0f, 0f);
                    for (int k = from; k < to; k++) sum += colors[k];
                    samples.Add(sum / (to - from));
                }
                Used.Add(material, samples);
            }
        }

        /// <summary>A material's base colors: its color × the base color texture where its
        /// meshes use it (Cover), otherwise × a 4 × 4 copy of the whole texture (linear), or the
        /// color alone. Layers and vertex colors aren't part of them.</summary>
        public static List<Color> Bases(AtlasUnifiedMaterial material)
        {
            var color = material.Surface.BaseColor.Color;
            if (!(material.Surface.BaseColor.Map?.Texture is Texture2D texture)) return new List<Color> { color };
            var pixels = Used.TryGetValue(material, out var used) ? (IEnumerable<Color>)used : Small(texture, Size, Size);
            return pixels.Select(p => new Color(p.r * color.r, p.g * color.g, p.b * color.b, 1f)).ToList();
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

        /// <summary>A scalar input's values over a 4 × 4 copy of its map, through its channel,
        /// invert, combine and remap, clamped to 0–1; its value alone without a map. Bus channels
        /// aren't part of them.</summary>
        public static float[] Scalars(AtlasScalarInput input)
        {
            if (!(input.Map?.Texture is Texture2D texture)) return new[] { Mathf.Clamp01(input.Evaluate()) };
            return Small(texture, Size, Size).Select(p => Mathf.Clamp01(input.Evaluate(Channel(p, input.Channel)))).ToArray();
        }

        /// <summary>A mask's values over a 4 × 4 copy of its map, through its levels, invert and
        /// strength; 1 without a mask or a map. Bus channels and vertex colors aren't part of them.</summary>
        public static float[] Masks(AtlasMask mask)
        {
            if (mask == null) return new[] { 1f };
            if (!(mask.Map?.Texture is Texture2D texture)) return new[] { mask.Evaluate() };
            return Small(texture, Size, Size).Select(p => mask.Evaluate(Channel(p, mask.Channel))).ToArray();
        }

        private static float Channel(Color p, AtlasChannel channel) =>
            channel == AtlasChannel.Luminance ? Luminance(p) : p[Mathf.Clamp((int)channel, 0, 3)];

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
