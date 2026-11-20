using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The vertex colors a source's meshes carry, for the bake (ATLAS_OUTPUT.md, "Vertex colors").
    // The Vertex colors feature attaches each active source's triangles to its unified material
    // while resolving, with whether its base color and alpha use is baked; the evaluators read
    // them from here. A material with nothing attached counts as white vertex colors, which is
    // what a mesh without colors gives a shader. The pipeline detaches them before resolving.
    public static class AtlasVertexColors
    {
        private sealed class Input
        {
            public readonly List<AtlasUVTriangles> Triangles = new List<AtlasUVTriangles>();
            public bool BakeColor, BakeAlpha;
        }

        private static readonly ConditionalWeakTable<AtlasUnifiedMaterial, Input> Inputs = new ConditionalWeakTable<AtlasUnifiedMaterial, Input>();

        public static void Attach(AtlasUnifiedMaterial material, IEnumerable<AtlasUVTriangles> triangles, bool bakeColor, bool bakeAlpha)
        {
            if (material == null) return;
            Detach(material);
            var input = new Input { BakeColor = bakeColor, BakeAlpha = bakeAlpha };
            if (triangles != null) input.Triangles.AddRange(triangles);
            Inputs.Add(material, input);
        }

        public static void Detach(AtlasUnifiedMaterial material)
        {
            if (material != null) Inputs.Remove(material);
        }

        // Whether the base color's vertex color multiply is baked into the base color map.
        public static bool BakesColor(AtlasUnifiedMaterial material) =>
            material != null && Inputs.TryGetValue(material, out var input) && input.BakeColor;

        // Whether the base color's vertex alpha multiply is baked into the alpha map.
        public static bool BakesAlpha(AtlasUnifiedMaterial material) =>
            material != null && Inputs.TryGetValue(material, out var input) && input.BakeAlpha;

        /// <summary>The material's vertex colors painted over its UV 0–1 (RGBA, as stored); null
        /// when it has none attached or its meshes carry no colors, which counts as white.</summary>
        public static NordicsTextureWrapper Map(AtlasUnifiedMaterial material, int width, int height) =>
            material != null && Inputs.TryGetValue(material, out var input)
                ? AtlasEvaluationCache.Copy(("vertex colors", material, width, height), () => Paint(input.Triangles, width, height))
                : null;

        /// <summary>
        /// Vertex colors painted in UV space: each texel gets the average of the colors of the
        /// triangles covering it, weighted by how much of it each covers; texels no triangle
        /// covers take the nearest painted one. A triangle in another UV tile is moved into 0–1
        /// by whole tiles, as for the coverage. Parts without colors paint white. Null when no
        /// part has colors or nothing lands in the texture.
        /// </summary>
        public static NordicsTextureWrapper Paint(IReadOnlyList<AtlasUVTriangles> parts, int width, int height)
        {
            if (parts == null) return null;
            var corners = new List<Vector2>();
            var colors = new List<Color>();
            bool any = false;
            foreach (var part in parts)
            {
                if (part?.UV == null || part.Triangles == null) continue;
                bool has = part.Colors != null && part.Colors.Length == part.UV.Length;
                any |= has;
                for (int i = 0; i + 2 < part.Triangles.Length; i += 3)
                {
                    int a = part.Triangles[i], b = part.Triangles[i + 1], c = part.Triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= part.UV.Length || b >= part.UV.Length || c >= part.UV.Length) continue;
                    Vector2 uv0 = part.UV[a], uv1 = part.UV[b], uv2 = part.UV[c];
                    if (!Finite(uv0) || !Finite(uv1) || !Finite(uv2)) continue;
                    var center = (uv0 + uv1 + uv2) / 3f;
                    var tile = new Vector2(Mathf.Floor(center.x), Mathf.Floor(center.y));
                    corners.Add(uv0 - tile);
                    corners.Add(uv1 - tile);
                    corners.Add(uv2 - tile);
                    colors.Add(has ? part.Colors[a] : Color.white);
                    colors.Add(has ? part.Colors[b] : Color.white);
                    colors.Add(has ? part.Colors[c] : Color.white);
                }
            }
            if (!any || corners.Count == 0) return null;
            var settings = new TextureSettings
            {
                DataType = TextureDataType.LinearData, SRGB = false, Mipmaps = false, WrapMode = TextureWrapMode.Clamp,
                Compression = TextureCompression.None
            };
            var paint = new TrianglePaintOptions { Mode = TriangleRasterizationMode.Quality, SamplesPerAxis = 2 };
            var sum = new NordicsTextureWrapper(width, height, settings);
            try
            {
                using (var weight = new NordicsTextureWrapper(width, height, settings))
                {
                    var white = new Color[corners.Count];
                    for (int i = 0; i < white.Length; i++) white[i] = Color.white;
                    sum.PaintTriangles(corners, colors, paint);
                    weight.PaintTriangles(corners, white, paint);
                    if (AtlasPixels.Max(weight, c => c.r) <= 0f)
                    {
                        sum.Dispose();
                        return null;
                    }
                    AtlasPixels.Map(sum, weight, (c, w) => w.r > 0f ? new Color(c.r / w.r, c.g / w.r, c.b / w.r, c.a / w.r) : c);
                    // Only the painted texels count as used, so the rest take the nearest of them.
                    sum.Blit(weight, sourceChannel: TextureChannel.R, destinationChannel: TextureChannel.Usage);
                }
                sum.Dilate();
                return sum;
            }
            catch
            {
                sum.Dispose();
                throw;
            }
        }

        private static bool Finite(Vector2 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);

        /// <summary>One channel of the material's vertex colors in RGB (luminance by its weights),
        /// sRGB decoded when asked; null when there are none (white).</summary>
        public static NordicsTextureWrapper Channel(AtlasUnifiedMaterial material, AtlasChannel channel, bool decode, int width, int height)
        {
            var map = Map(material, width, height);
            if (map == null) return null;
            AtlasPixels.Map(map, c =>
            {
                float v;
                switch (channel)
                {
                    case AtlasChannel.Red: v = c.r; break;
                    case AtlasChannel.Green: v = c.g; break;
                    case AtlasChannel.Blue: v = c.b; break;
                    case AtlasChannel.Alpha: v = c.a; break;
                    default: v = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f; break;
                }
                if (decode) v = Decode(v);
                return new Color(v, v, v, 1f);
            });
            return map;
        }

        /// <summary>The base color's vertex color multiply baked into a color map:
        /// RGB × lerp(1, vertex RGB (sRGB decoded when the source says so), amount).</summary>
        public static void MultiplyColor(NordicsTextureWrapper result, AtlasUnifiedMaterial material, int width, int height)
        {
            var settings = material.Surface.VertexColor;
            if (!settings.MultiplyColor || !BakesColor(material)) return;
            using (var map = Map(material, width, height))
            {
                if (map == null) return;
                float amount = Mathf.Clamp01(settings.Amount);
                bool srgb = settings.SrgbData;
                AtlasPixels.Map(result, map, (c, v) =>
                {
                    float r = srgb ? Decode(v.r) : v.r;
                    float g = srgb ? Decode(v.g) : v.g;
                    float b = srgb ? Decode(v.b) : v.b;
                    return new Color(c.r * Mathf.Lerp(1f, r, amount), c.g * Mathf.Lerp(1f, g, amount), c.b * Mathf.Lerp(1f, b, amount), c.a);
                });
            }
        }

        /// <summary>The base color's vertex alpha multiply baked into an alpha map (alpha in RGB):
        /// × lerp(1, vertex alpha, alpha amount).</summary>
        public static void MultiplyAlpha(NordicsTextureWrapper result, AtlasUnifiedMaterial material, int width, int height)
        {
            var settings = material.Surface.VertexColor;
            if (!settings.MultiplyAlpha || !BakesAlpha(material)) return;
            using (var map = Map(material, width, height))
            {
                if (map == null) return;
                float amount = Mathf.Clamp01(settings.AlphaAmount);
                AtlasPixels.Map(result, map, (c, v) =>
                {
                    float f = Mathf.Lerp(1f, v.a, amount);
                    return new Color(c.r * f, c.g * f, c.b * f, c.a);
                });
            }
        }

        /// <summary>UnityCG's GammaToLinearSpace, which the shaders use for vertex colors.</summary>
        public static float Decode(float c) => c * (c * (c * 0.305306011f + 0.682171111f) + 0.012522878f);

        /// <summary>The inverse of Decode on 0–1.</summary>
        public static float Encode(float linear)
        {
            linear = Mathf.Clamp01(linear);
            float low = 0f, high = 1f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (low + high) * 0.5f;
                if (Decode(mid) < linear) low = mid; else high = mid;
            }
            return (low + high) * 0.5f;
        }
    }
}
