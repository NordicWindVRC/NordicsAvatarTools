using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace NordicWind.AvatarStatistics.Editor
{
    // Read-only platform estimates. Never changes the build target or reimports an asset.
    internal static class PlatformTextureMemoryCalculator
    {
        internal static long Calculate(Texture texture, string path, string platform, long currentBytes, out bool fallback)
        {
            fallback = false;
            bool currentPlatform = (platform == "Android" && EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) ||
                                   (platform == "Standalone" && EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                                    BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) == BuildTargetGroup.Standalone);
            // The active import is authoritative, including NPOT handling and importer edge cases.
            if (currentPlatform) return currentBytes;
            var importer = !string.IsNullOrEmpty(path) ? AssetImporter.GetAtPath(path) as TextureImporter : null;
            if (importer == null || !(texture is Texture2D)) { fallback = true; return currentBytes; }
            try
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                var defaults = importer.GetDefaultPlatformTextureSettings();
                var effective = settings.overridden ? settings : defaults;
                var format = effective.format == TextureImporterFormat.Automatic
                    ? importer.GetAutomaticFormat(platform) : effective.format;
                if (format == TextureImporterFormat.Automatic) throw new InvalidOperationException("Automatic format unresolved");
                // TextureImporterFormat uses TextureFormat values for these formats (as in Unity's importer UI).
                // Crunch is only an on-disk encoding; calculate the decoded GPU block format.
                string name = format.ToString().Replace("Crunched", "");
                if (!Enum.TryParse(name, out TextureFormat textureFormat))
                    throw new InvalidOperationException("Unknown texture format: " + name);
                var gpuFormat = GraphicsFormatUtility.GetGraphicsFormat(textureFormat, importer.sRGBTexture);
                if (gpuFormat == GraphicsFormat.None) throw new InvalidOperationException("No graphics format");

                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (width < 1 || height < 1) throw new InvalidOperationException("Missing source dimensions");
                int maxSize = effective.maxTextureSize > 0 ? effective.maxTextureSize : importer.maxTextureSize;
                if (maxSize < 1) throw new InvalidOperationException("Invalid maximum resolution");
                // Non-default NPOT scaling can change the imported dimensions before max-size resizing.
                // Keep these cases explicitly fallback instead of assigning an unjustified exact value.
                if (importer.npotScale != TextureImporterNPOTScale.None &&
                    (!IsPowerOfTwo(width) || !IsPowerOfTwo(height)))
                    throw new InvalidOperationException("NPOT scaling requires import");
                if (Math.Max(width, height) > maxSize)
                {
                    double scale = (double)maxSize / Math.Max(width, height);
                    width = Math.Max(1, Mathf.RoundToInt((float)(width * scale)));
                    height = Math.Max(1, Mathf.RoundToInt((float)(height * scale)));
                }
                int mipCount = importer.mipmapEnabled ? 1 + Mathf.FloorToInt(Mathf.Log(Math.Max(width, height), 2f)) : 1;
                return GraphicsFormatUtility.ComputeMipChainSize(width, height, gpuFormat, mipCount);
            }
            catch
            {
                fallback = true;
                return currentBytes;
            }
        }

        private static bool IsPowerOfTwo(int n) { return (n & (n - 1)) == 0; }
    }
}
