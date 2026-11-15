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
            @"(?<!\d)(7(?:\.\d+)?|8(?:\.\d+)?|9(?:\.\d+)?|1[01](?:\.\d+)?)(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsPoiyomi(Material material)
        {
            var saved = AtlasMaterialReader.Saved(material);
            if (saved != null) return IsPoiyomiFromProperties(saved.Has);
            if (material == null || material.shader == null) return false;
            return IsPoiyomiFromMetadata(material.shader.name, AssetDatabase.GetAssetPath(material.shader));
        }

        public static AtlasPoiyomiVariant DetectVariant(Material material)
        {
            var saved = AtlasMaterialReader.Saved(material);
            if (saved != null) return DetectVariantFromProperties(saved.Has);
            if (material == null || material.shader == null) return AtlasPoiyomiVariant.None;
            return DetectVariantFromMetadata(material.shader.name, AssetDatabase.GetAssetPath(material.shader));
        }

        public static int Detect(Material material)
        {
            var saved = AtlasMaterialReader.Saved(material);
            if (saved != null) return DetectFromProperties(saved.Has);
            if (material == null || material.shader == null) return 0;
            return DetectFromMetadata(
                material.shader.name,
                AssetDatabase.GetAssetPath(material.shader),
                GetShaderMasterLabel(material.shader));
        }

        // ---- Without the shader: from the properties a material saved -------------------------
        // Markers were chosen from the released shaders: each version's markers first appear in
        // that version and stay in every later one, so the newest version with a marker wins.
        // The family markers exist in every version from 7 to 11; the Pro markers only in Pro.
        private static readonly string[] FamilyMarkers = { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor" };
        private static readonly (int Major, string[] Markers)[] VersionMarkers =
        {
            (11, new[] { "_MainBlackWhiteStrength", "_LightingCapMode", "_PoiPBRMetallic", "_AlphaDitherMode", "_RimApplicationStage" }),
            (10, new[] { "_ALDecalBilateral", "_ALDecalMirroredUVMode", "_ALDecalPosition" }),
            (9, new[] { "_AlphaMaskInvert", "_AudioLinkBandOverrideSliders", "_AnisoGlobalMask" }),
            (8, new[] { "_ALDecalBlendType", "_ALDecalLineWidth", "_ALDecalBaseBoost" }),
        };
        private static readonly string[] ProMarkers = { "_ConstellationEnable", "_TPSPenetratorEnabled", "_SSAOEnabled" };

        public static bool IsPoiyomiFromProperties(Func<string, bool> has)
        {
            foreach (var marker in FamilyMarkers) if (!has(marker)) return false;
            return true;
        }
        public static AtlasPoiyomiVariant DetectVariantFromProperties(Func<string, bool> has)
        {
            if (!IsPoiyomiFromProperties(has)) return AtlasPoiyomiVariant.None;
            foreach (var marker in ProMarkers) if (has(marker)) return AtlasPoiyomiVariant.Pro;
            return AtlasPoiyomiVariant.Toon;
        }
        // 7 when it's Poiyomi but no newer marker is present.
        public static int DetectFromProperties(Func<string, bool> has)
        {
            if (!IsPoiyomiFromProperties(has)) return 0;
            foreach (var (major, markers) in VersionMarkers)
                foreach (var marker in markers)
                    if (has(marker)) return major;
            return 7;
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
        // Poiyomi 11 keeps 10's shader names, so the master label decides between them.
        public static int DetectFromMetadata(string name, string path, string shaderMasterLabel)
        {
            var variant = DetectVariantFromMetadata(name, path);
            if (variant != AtlasPoiyomiVariant.Toon && variant != AtlasPoiyomiVariant.Pro)
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
                if (int.TryParse(token.Split('.')[0], out major) && major >= 7 && major <= 11)
                    return major;
            }
            return 0;
        }
    }
}
