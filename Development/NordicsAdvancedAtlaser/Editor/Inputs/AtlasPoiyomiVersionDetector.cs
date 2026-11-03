using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasPoiyomiVariant
    {
        None = 0,
        Unknown = 1,
        Toon = 2,
        Pro = 3,
    }

    // Hierarchical detection:
    // 1) detect Poiyomi family,
    // 2) refine to shader variant,
    // 3) then detect the major version.
    // Uses Poiyomi's shader_master_label first because it survives locked shaders.
    public static class AtlasPoiyomiVersionDetector
    {
        private static readonly Regex VersionRegex = new Regex(
            @"(?<!\d)(7(?:\.\d+)?|8(?:\.\d+)?|9(?:\.\d+)?|10(?:\.\d+)?)(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsPoiyomi(Material material)
        {
            if (material == null || material.shader == null) return false;
            return IsPoiyomiFromMetadata(material.shader.name, AssetDatabase.GetAssetPath(material.shader));
        }

        public static AtlasPoiyomiVariant DetectVariant(Material material)
        {
            if (material == null || material.shader == null) return AtlasPoiyomiVariant.None;
            return DetectVariantFromMetadata(material.shader.name, AssetDatabase.GetAssetPath(material.shader));
        }

        public static int Detect(Material material)
        {
            if (material == null || material.shader == null) return 0;
            return DetectFromMetadata(
                material.shader.name,
                AssetDatabase.GetAssetPath(material.shader),
                GetShaderMasterLabel(material.shader));
        }

        public static bool IsPoiyomiFromMetadata(string name, string path)
        {
            string combined = ((name ?? string.Empty) + "\n" + (path ?? string.Empty))
                .Replace('\\', '/')
                .ToLowerInvariant();
            return combined.Contains("poiyomi") || combined.Contains(".poiyomi/");
        }

        public static AtlasPoiyomiVariant DetectVariantFromMetadata(string name, string path)
        {
            if (!IsPoiyomiFromMetadata(name, path)) return AtlasPoiyomiVariant.None;
            string combined = ((name ?? string.Empty) + "\n" + (path ?? string.Empty))
                .Replace('\\', '/')
                .ToLowerInvariant();
            if (combined.Contains("toon")) return AtlasPoiyomiVariant.Toon;
            if (combined.Contains("pro")) return AtlasPoiyomiVariant.Pro;
            return AtlasPoiyomiVariant.Unknown;
        }

        // Public for deterministic tests against actual shader names from released sources.
        public static int DetectFromMetadata(string name, string path, string shaderMasterLabel)
        {
            if (DetectVariantFromMetadata(name, path) != AtlasPoiyomiVariant.Toon)
                return 0;

            int major = ExtractMajorVersion(shaderMasterLabel);
            if (major == 0) major = ExtractMajorVersion(name);
            if (major == 0) major = ExtractMajorVersion(path);
            if (major != 0) return major;

            // 7.x used bullet-delimited shader names.
            if (!string.IsNullOrEmpty(name) && name.IndexOf('•') >= 0) return 7;

            // When the family and variant are known but the exact version is not,
            // prefer the generic Poiyomi Toon parser instead of guessing 9 or 10.
            return 0;
        }

        // Compatibility overload for older tests/callers.
        public static int DetectFromMetadata(string name, string path, bool hasSecondNormal)
        {
            return DetectFromMetadata(name, path, null);
        }

        public static string GetShaderMasterLabel(Shader shader)
        {
            if (shader == null) return null;
            try
            {
                int index = shader.FindPropertyIndex("shader_master_label");
                if (index < 0) return null;
                return shader.GetPropertyDescription(index);
            }
            catch
            {
                return null;
            }
        }

        private static int ExtractMajorVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var matches = VersionRegex.Matches(text.Replace('\\', '/'));
            foreach (Match match in matches)
            {
                if (!match.Success) continue;
                string token = match.Groups[1].Value;
                int major;
                if (int.TryParse(token.Split('.')[0], out major) && major >= 7 && major <= 10)
                    return major;
            }
            return 0;
        }
    }
}
