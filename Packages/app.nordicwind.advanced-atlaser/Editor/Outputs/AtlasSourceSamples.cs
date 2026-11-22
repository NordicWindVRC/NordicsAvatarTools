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

        // What Cover found for a source, by its unified material.
        // - Used: the base color texels its meshes use, up to 16 colors from the darkest to the
        //   brightest. A texture's unused parts (often black, and often most of it) would
        //   otherwise make the material look darker than it shows. Without the material's
        //   color unless Final.
        // - Placed: its base colors by position, where layers or adjustments change them.
        private sealed class Samples
        {
            public List<Color> Used, Placed;
            public bool Final;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AtlasUnifiedMaterial, Samples> Covered =
            new System.Runtime.CompilerServices.ConditionalWeakTable<AtlasUnifiedMaterial, Samples>();
        private const int UsedSize = 16;

        /// <summary>Tells the samples where a source's meshes use its base color (its UV0
        /// triangles), so its base colors are taken there, and takes its samples anew. Without
        /// triangles they are taken from the whole of it; so are those of a plain base color
        /// texture on another UV channel or with a scale or offset.</summary>
        public static void Cover(AtlasUnifiedMaterial material, IReadOnlyList<AtlasUVTriangles> coverage)
        {
            if (material == null) return;
            Covered.Remove(material);
            bool stack = AtlasColorStack.HasStack(material);
            var samples = new Samples { Final = stack, Placed = stack ? Stack(material, Size) : null };
            var map = material.Surface.BaseColor.Map;
            bool plain = map?.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Tiling == Vector2.one && map.Offset == Vector2.zero;
            if (coverage != null && coverage.Count > 0 && (stack || plain))
                using (var mask = AtlasCoverage.Paint(coverage, UsedSize, UsedSize))
                    if (mask != null)
                    {
                        IReadOnlyList<Color> pixels = stack ? Stack(material, UsedSize) : (IReadOnlyList<Color>)Small((Texture2D)map.Texture, UsedSize, UsedSize);
                        var colors = new List<Color>();
                        for (int y = 0; y < UsedSize; y++)
                            for (int x = 0; x < UsedSize; x++)
                                if (mask.IsPixelUsed(x, y)) colors.Add(pixels[y * UsedSize + x]);
                        if (colors.Count > 0)
                        {
                            // As many samples as the whole texture gives, each the average of a share of
                            // the used texels in order of brightness.
                            colors.Sort((a, b) => Luminance(a).CompareTo(Luminance(b)));
                            int count = Mathf.Min(Size * Size, colors.Count);
                            samples.Used = new List<Color>(count);
                            for (int i = 0; i < count; i++)
                            {
                                int from = i * colors.Count / count, to = (i + 1) * colors.Count / count;
                                var sum = new Color(0f, 0f, 0f, 0f);
                                for (int k = from; k < to; k++) sum += colors[k];
                                samples.Used.Add(sum / (to - from));
                            }
                        }
                    }
            if (samples.Used != null || samples.Placed != null) Covered.Add(material, samples);
        }

        /// <summary>A material's base colors: where its meshes use it (Cover), otherwise by
        /// position over the whole of it (BasesByPosition).</summary>
        public static List<Color> Bases(AtlasUnifiedMaterial material)
        {
            if (!Covered.TryGetValue(material, out var samples) || samples.Used == null) return BasesByPosition(material);
            if (samples.Final) return samples.Used.Select(Opaque).ToList();
            var color = material.Surface.BaseColor.Color;
            return samples.Used.Select(p => new Color(p.r * color.r, p.g * color.g, p.b * color.b, 1f)).ToList();
        }

        /// <summary>A material's base colors by position, 4 × 4 over its UV 0–1 (linear): its
        /// color × its base color texture, or the color alone; where layers or adjustments
        /// change the base color, the base color stack as it is baked (AtlasColorStack), read
        /// at sample size. Sample by sample with Scalars and Masks, for what reads the base
        /// color together with another map at the same place. Vertex colors aren't part of
        /// them.</summary>
        public static List<Color> BasesByPosition(AtlasUnifiedMaterial material)
        {
            if (Covered.TryGetValue(material, out var samples) && samples.Placed != null) return samples.Placed.Select(Opaque).ToList();
            if (AtlasColorStack.HasStack(material)) return Stack(material, Size).Select(Opaque).ToList();
            var color = material.Surface.BaseColor.Color;
            if (!(material.Surface.BaseColor.Map?.Texture is Texture2D texture)) return new List<Color> { color };
            return Small(texture, Size, Size).Select(p => new Color(p.r * color.r, p.g * color.g, p.b * color.b, 1f)).ToList();
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        /// <summary>Whether an evaluation is for samples: its textures are then read as small
        /// copies (AtlasMapEvaluator).</summary>
        public static bool Sampling { get; private set; }

        // The base color stack with the material's color, evaluated at sample size.
        private static List<Color> Stack(AtlasUnifiedMaterial material, int size)
        {
            bool before = Sampling;
            Sampling = true;
            try
            {
                using (AtlasEvaluationCache.Scope())
                using (var map = AtlasColorStack.Evaluate(material, size, size, multiplyColor: true))
                    return map.GetPixels().ToList();
            }
            finally
            {
                Sampling = before;
            }
        }

        private const int SmallSize = 64;
        private static readonly Dictionary<(int Id, Hash128 Hash, TextureDataType Type), NordicsTextureWrapper> SmallTextures =
            new Dictionary<(int, Hash128, TextureDataType), NordicsTextureWrapper>();

        /// <summary>A small copy of a texture as the wrapper reads it, for evaluations at
        /// sample size: made once per texture, content and data type. The caller owns it.</summary>
        public static NordicsTextureWrapper SmallTexture(Texture2D texture, TextureDataType type)
        {
            var key = (texture.GetInstanceID(), texture.imageContentsHash, type);
            if (!SmallTextures.TryGetValue(key, out var small))
            {
                if (SmallTextures.Count >= 256)
                {
                    foreach (var entry in SmallTextures.Values) entry.Dispose();
                    SmallTextures.Clear();
                }
                using (var loaded = new NordicsTextureWrapper(texture, type))
                    small = loaded.Resize(SmallSize, SmallSize, sampling: TextureSamplingMode.Area);
                SmallTextures[key] = small;
            }
            return small.Clone();
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
