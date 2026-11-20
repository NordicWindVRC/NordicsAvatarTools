using System;
using System.Linq;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Which of an output's platform formats a texture gets.
    public enum AtlasTextureKind { Color, ColorAlpha, Normal, Data }

    // GPU memory estimates (ATLAS_OUTPUT.md, "Memory"): the size of the compressed texture with
    // its mip chain, which is what VRChat's performance rank counts. Crunch and file sizes
    // don't count: crunch only changes the download size.
    public static class AtlasTextureMemory
    {
        public static long Bytes(int width, int height, AtlasTextureFormat format, bool mipmaps = true)
        {
            if (width < 1 || height < 1) return 0;
            Block(format, out int blockWidth, out int blockHeight, out int blockBytes);
            long total = 0;
            while (true)
            {
                total += (long)Ceil(width, blockWidth) * Ceil(height, blockHeight) * blockBytes;
                if (!mipmaps || (width == 1 && height == 1)) return total;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
        }

        public static AtlasTextureFormat Format(AtlasPlatformFormats formats, AtlasTextureKind kind)
        {
            switch (kind)
            {
                case AtlasTextureKind.ColorAlpha: return formats.colorAlpha;
                case AtlasTextureKind.Normal: return formats.normal;
                case AtlasTextureKind.Data: return formats.data;
                default: return formats.color;
            }
        }

        /// <summary>A format's compression block width in pixels (1: uncompressed).</summary>
        public static int BlockSize(AtlasTextureFormat format)
        {
            Block(format, out int width, out _, out _);
            return width;
        }

        /// <summary>The normal map's block size on the platform being built.</summary>
        public static int NormalBlock(AtlasOutputConfiguration config)
        {
            var target = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            bool mobile = target == UnityEditor.BuildTarget.Android || target == UnityEditor.BuildTarget.iOS;
            var formats = mobile ? config?.androidFormats : config?.pcFormats;
            return formats == null ? 1 : BlockSize(Format(formats, AtlasTextureKind.Normal));
        }

        // "2.7 MB": megabytes of 1024², as Unity shows texture memory.
        public static string Label(long bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

        private static int Ceil(int value, int block) => (value + block - 1) / block;

        private static void Block(AtlasTextureFormat format, out int width, out int height, out int bytes)
        {
            switch (format)
            {
                case AtlasTextureFormat.BC1: width = height = 4; bytes = 8; return;
                case AtlasTextureFormat.BC3:
                case AtlasTextureFormat.BC5:
                case AtlasTextureFormat.BC7:
                case AtlasTextureFormat.ASTC4x4: width = height = 4; bytes = 16; return;
                case AtlasTextureFormat.ASTC5x5: width = height = 5; bytes = 16; return;
                case AtlasTextureFormat.ASTC6x6: width = height = 6; bytes = 16; return;
                case AtlasTextureFormat.ASTC8x8: width = height = 8; bytes = 16; return;
                case AtlasTextureFormat.ASTC10x10: width = height = 10; bytes = 16; return;
                case AtlasTextureFormat.ASTC12x12: width = height = 12; bytes = 16; return;
                default: width = height = 1; bytes = 4; return;
            }
        }
    }

    // The sizes a texture can have, by its size mode.
    public static class AtlasTextureSizes
    {
        public static readonly int[] All = { 128, 256, 512, 1024, 2048, 4096, 8192 };

        // Widths whose height, at the main texture's aspect, is also one of the sizes.
        public static int[] MainAspectWidths(int mainWidth, int mainHeight) =>
            All.Where(w => HeightAt(w, mainWidth, mainHeight) > 0).ToArray();

        // The height at the main texture's aspect, or 0 when it isn't one of the sizes.
        public static int HeightAt(int width, int mainWidth, int mainHeight)
        {
            if (mainWidth <= 0 || mainHeight <= 0) return 0;
            long scaled = (long)width * mainHeight;
            if (scaled % mainWidth != 0) return 0;
            long height = scaled / mainWidth;
            return Array.IndexOf(All, (int)height) >= 0 ? (int)height : 0;
        }

        public static int Closest(int value, int[] options) =>
            options.Length == 0 ? value : options.OrderBy(v => Math.Abs((long)v - value)).First();

        // A texture's size by its mode: the main size, the closest size at the main aspect, a
        // square, or any two sizes.
        public static void Normalize(AtlasTextureSizeMode mode, int mainWidth, int mainHeight, ref int width, ref int height)
        {
            switch (mode)
            {
                case AtlasTextureSizeMode.Main:
                    width = mainWidth;
                    height = mainHeight;
                    return;
                case AtlasTextureSizeMode.MainAspect:
                    var widths = MainAspectWidths(mainWidth, mainHeight);
                    if (widths.Length == 0)
                    {
                        width = mainWidth;
                        height = mainHeight;
                        return;
                    }
                    width = Closest(width, widths);
                    height = HeightAt(width, mainWidth, mainHeight);
                    return;
                case AtlasTextureSizeMode.Square:
                    width = height = Closest(width, All);
                    return;
                default:
                    width = Closest(width, All);
                    height = Closest(height, All);
                    return;
            }
        }
    }
}
