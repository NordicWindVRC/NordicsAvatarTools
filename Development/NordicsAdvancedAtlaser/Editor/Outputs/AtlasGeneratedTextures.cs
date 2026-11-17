using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A texture the build makes besides the atlas maps (a combined matcap): its linear pixels,
    // made when the build gets to it and set on Reference before the material is written.
    public sealed class AtlasGeneratedTexture
    {
        public AtlasTextureReference Reference;
        public string Name;
        public bool Srgb = true;
        public int Width, Height;
        // Row by row from the bottom, Width × Height, linear.
        public Func<Color[]> Pixels;
    }

    public static class AtlasGeneratedTextures
    {
        /// <summary>The texture in memory, with mipmaps, clamped.</summary>
        public static Texture2D Make(AtlasGeneratedTexture generated, string prefix, bool readable = false)
        {
            var texture = new Texture2D(generated.Width, generated.Height, TextureFormat.RGBA32, true, !generated.Srgb)
            {
                name = prefix + "_" + generated.Name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = generated.Pixels();
            if (generated.Srgb)
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    pixels[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)),
                        Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), Mathf.Clamp01(c.a));
                }
            texture.SetPixels(pixels);
            texture.Apply(true, !readable);
            return texture;
        }

        /// <summary>The texture as a PNG asset in <paramref name="folder"/>, imported with its color
        /// space, mipmaps and clamping.</summary>
        public static Texture2D SavePng(AtlasGeneratedTexture generated, string folder, string prefix)
        {
            var texture = Make(generated, prefix, readable: true);
            string path = folder + "/" + texture.name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = generated.Srgb;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
