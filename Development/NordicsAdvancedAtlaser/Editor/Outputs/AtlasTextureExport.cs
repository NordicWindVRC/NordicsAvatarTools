using System;
using NordicWind.TextureWrapper;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Export rules (ATLAS_OUTPUT.md, "Export rules"): PNG assets with the output's formats per
    // platform, no alpha channel unless a virtual map uses it, colour space per texture, and
    // crunch only where it pays.
    public static class AtlasTextureExport
    {
        // Crunch's default quality, as Unity's importer uses.
        public const int CrunchQuality = 50;

        // Writes `<folder>/<name>.png`, overwriting a texture of that name, and imports it with
        // the output's formats. Returns the imported texture.
        public static Texture2D Png(NordicsTextureWrapper packed, string folder, string name, AtlasTextureUse use, AtlasOutputConfiguration config)
        {
            packed.Export(new PngAssetTextureExporter(folder), name);
            string path = $"{folder}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"[Nordic's Advanced Atlaser] The exported texture '{path}' has no texture importer.");
            int maxSize = MaxSize(use.Width, use.Height);
            importer.maxTextureSize = maxSize;
            var pc = AtlasTextureMemory.Format(config.pcFormats, use.Kind);
            bool crunchPc = config.crunch && CrunchPays(packed, pc);
            importer.SetPlatformTextureSettings(Platform("Standalone", maxSize, ImporterFormat(pc, crunchPc)));
            importer.SetPlatformTextureSettings(Platform("Android", maxSize,
                ImporterFormat(AtlasTextureMemory.Format(config.androidFormats, use.Kind), false)));
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException($"[Nordic's Advanced Atlaser] '{path}' didn't import as a texture.");
            return texture;
        }

        // Crunch is kept only when the crunched data is below 90% of the same format without it.
        public static bool KeepCrunch(long crunchedBytes, long plainBytes) => crunchedBytes < 0.9 * plainBytes;

        // The crunched form of a format, or null when it has none.
        public static TextureFormat? Crunched(AtlasTextureFormat format)
        {
            switch (format)
            {
                case AtlasTextureFormat.BC1: return TextureFormat.DXT1Crunched;
                case AtlasTextureFormat.BC3: return TextureFormat.DXT5Crunched;
                default: return null;
            }
        }

        // Compresses a copy with crunch and compares its size with the format's plain size.
        public static bool CrunchPays(NordicsTextureWrapper packed, AtlasTextureFormat format)
        {
            var crunched = Crunched(format);
            if (crunched == null) return false;
            long size = CrunchedBytes(packed, crunched.Value);
            return KeepCrunch(size, AtlasTextureMemory.Bytes(packed.Width, packed.Height, format, packed.Settings.Mipmaps));
        }

        // The size of the crunched data for this texture, with its mip chain.
        public static long CrunchedBytes(NordicsTextureWrapper packed, TextureFormat crunched)
        {
            var copy = new Texture2D(packed.Width, packed.Height, TextureFormat.RGBA32, packed.Settings.Mipmaps, !packed.Settings.SRGB);
            try
            {
                copy.SetPixels(packed.GetEncodedPixels());
                copy.Apply(packed.Settings.Mipmaps, false);
                EditorUtility.CompressTexture(copy, crunched, CrunchQuality);
                return copy.GetRawTextureData().LongLength;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        public static TextureImporterFormat ImporterFormat(AtlasTextureFormat format, bool crunched)
        {
            switch (format)
            {
                case AtlasTextureFormat.BC1: return crunched ? TextureImporterFormat.DXT1Crunched : TextureImporterFormat.DXT1;
                case AtlasTextureFormat.BC3: return crunched ? TextureImporterFormat.DXT5Crunched : TextureImporterFormat.DXT5;
                case AtlasTextureFormat.BC5: return TextureImporterFormat.BC5;
                case AtlasTextureFormat.BC7: return TextureImporterFormat.BC7;
                case AtlasTextureFormat.ASTC4x4: return TextureImporterFormat.ASTC_4x4;
                case AtlasTextureFormat.ASTC5x5: return TextureImporterFormat.ASTC_5x5;
                case AtlasTextureFormat.ASTC6x6: return TextureImporterFormat.ASTC_6x6;
                case AtlasTextureFormat.ASTC8x8: return TextureImporterFormat.ASTC_8x8;
                case AtlasTextureFormat.ASTC10x10: return TextureImporterFormat.ASTC_10x10;
                case AtlasTextureFormat.ASTC12x12: return TextureImporterFormat.ASTC_12x12;
                default: return TextureImporterFormat.RGBA32;
            }
        }

        private static TextureImporterPlatformSettings Platform(string name, int maxSize, TextureImporterFormat format) =>
            new TextureImporterPlatformSettings
            {
                name = name, overridden = true, maxTextureSize = maxSize, format = format,
                compressionQuality = CrunchQuality,
                crunchedCompression = format == TextureImporterFormat.DXT1Crunched || format == TextureImporterFormat.DXT5Crunched
            };

        // The importer's size limit that keeps the texture at full size.
        private static int MaxSize(int width, int height)
        {
            int size = 32;
            while (size < Math.Max(width, height) && size < 16384) size *= 2;
            return size;
        }
    }
}
