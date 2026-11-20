using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Shadow ramps for outputs that shade only through a ramp texture (Toon Standard): a ramp's
    // shape as keys, and the keys as a small linear texture sampled along u.
    public static class AtlasRampTexture
    {
        public const int Width = 128;

        /// <summary>The ramp's brightness where its first band puts the edge, over the half-Lambert
        /// term: b = edge(t) (smoothstepped with SmoothEdges), lerp(1, b, ramp strength).</summary>
        public static List<AtlasRampKey> FromBands(AtlasRampToon ramp, int samples = 33)
        {
            var keys = new List<AtlasRampKey>();
            var band = ramp.Bands.FirstOrDefault();
            if (band == null) return keys;
            float low = band.Border - band.Blur * 0.5f, high = band.Border + band.Blur * 0.5f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                float e = high - low > 1e-5f ? Mathf.Clamp01((t - low) / (high - low)) : (t >= band.Border ? 1f : 0f);
                if (ramp.SmoothEdges) e = e * e * (3f - 2f * e);
                float b = band.Alpha > 0f ? Mathf.Lerp(1f, e, Mathf.Clamp01(ramp.RampStrength)) : 1f;
                keys.Add(new AtlasRampKey(t, new Color(b, b, b, 1f)));
            }
            return keys;
        }

        /// <summary>Piecewise linear between the keys; the first and last hold beyond them.</summary>
        public static Color Evaluate(IReadOnlyList<AtlasRampKey> keys, float t)
        {
            if (keys == null || keys.Count == 0) return Color.white;
            var sorted = keys.OrderBy(k => k.Position).ToList();
            if (t <= sorted[0].Position) return sorted[0].Color;
            for (int i = 1; i < sorted.Count; i++)
                if (t <= sorted[i].Position)
                {
                    float span = sorted[i].Position - sorted[i - 1].Position;
                    return Color.Lerp(sorted[i - 1].Color, sorted[i].Color, span > 0f ? (t - sorted[i - 1].Position) / span : 1f);
                }
            return sorted[sorted.Count - 1].Color;
        }

        /// <summary>The keys as a linear texture (values as they are), clamped and filtered.</summary>
        public static Texture2D Make(IReadOnlyList<AtlasRampKey> keys, string name, bool readable = false)
        {
            var texture = new Texture2D(Width, 4, TextureFormat.RGBA32, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[Width * 4];
            for (int x = 0; x < Width; x++)
            {
                var c = Evaluate(keys, (x + 0.5f) / Width);
                for (int y = 0; y < 4; y++) pixels[y * Width + x] = new Color(c.r, c.g, c.b, 1f);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, !readable);
            return texture;
        }

        /// <summary>The keys as a PNG asset in <paramref name="folder"/>, imported linear,
        /// clamped and uncompressed.</summary>
        public static Texture2D SavePng(IReadOnlyList<AtlasRampKey> keys, string folder, string name)
        {
            var texture = Make(keys, name, readable: true);
            string path = folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
