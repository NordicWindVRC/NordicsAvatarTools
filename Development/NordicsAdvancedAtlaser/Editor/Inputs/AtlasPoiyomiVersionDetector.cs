using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasPoiyomiVariant
    {
        None = 0,
        Toon = 2,
        Pro = 3,
    }

    // Detects the Poiyomi family, then Toon or Pro, then the major version. With the shader
    // installed it goes by the shader's name (a locked shader's name holds the original), its
    // master label and its path; without it, by the property names the material saved.
    public static class AtlasPoiyomiVersionDetector
    {
        // "Poiyomi 10.0.29", "Poiyomi Toon V7.3.050".
        private static readonly Regex LabelVersion = new Regex(@"Poiyomi\D*(\d+)\.\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LabelFullVersion = new Regex(@"Poiyomi\D*(\d+)\.(\d+)\.(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The full version from the shader's master label, which locking keeps; null
        /// without the shader or a label.</summary>
        public static Version DetectFull(Material material) =>
            FullVersionFromLabel(AtlasMaterialReader.PropertyLabel(material, "shader_master_label"));

        public static Version FullVersionFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            var match = LabelFullVersion.Match(label);
            return match.Success ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)) : null;
        }

        /// <summary>Whether the version multiplies its direct highlights by π (GetSpecFresTerm, for
        /// Reflections & Specular and Clear Coat): Poiyomi 10 from 10.0.26. Checked in the released
        /// and installed shaders: 8.1.167, 9.0.62 to 9.3.67 and 10.0.21 to 10.0.25 without,
        /// 10.0.26 to 10.0.29 with, 11.0.50 (Toon and Pro) without.</summary>
        public static bool HighlightsTimesPi(Version version) =>
            version != null && version.Major == 10 && version >= FirstHighlightsTimesPi;

        public static readonly Version FirstHighlightsTimesPi = new Version(10, 0, 26);
        // The newest Poiyomi 11 checked for it.
        public static readonly Version NewestCheckedEleven = new Version(11, 0, 50);
        // A version folder in a shader name ("Old Versions/8.1/", "Poiyomi 7.3/") or in the shaders'
        // asset path ("/Shaders/9.3/").
        private static readonly Regex NameVersion = new Regex(@"(?:^|[/ ])(\d+)\.\d+(?=/)", RegexOptions.CultureInvariant);
        private static readonly Regex PathVersion = new Regex(@"/Shaders/(\d+)\.\d+/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ProWord = new Regex(@"\bPro\b", RegexOptions.CultureInvariant);
        private static readonly Regex WorldWord = new Regex(@"\bWorld\b", RegexOptions.CultureInvariant);

        public static bool IsPoiyomi(Material material) => DetectVariant(material) != AtlasPoiyomiVariant.None;

        public static AtlasPoiyomiVariant DetectVariant(Material material)
        {
            var saved = AtlasMaterialReader.Saved(material);
            if (saved != null) return DetectVariantFromProperties(saved.Has);
            string name = AtlasMaterialReader.ShaderName(material);
            if (name.Length == 0) return AtlasPoiyomiVariant.None;
            return DetectVariantFromMetadata(name, AtlasMaterialReader.ShaderPath(material));
        }

        // The major version; 0 when it's Poiyomi but the version can't be told.
        public static int Detect(Material material)
        {
            var saved = AtlasMaterialReader.Saved(material);
            if (saved != null) return DetectFromProperties(saved.Has);
            string name = AtlasMaterialReader.ShaderName(material);
            if (name.Length == 0) return 0;
            return DetectFromMetadata(name, AtlasMaterialReader.ShaderPath(material),
                AtlasMaterialReader.PropertyLabel(material, "shader_master_label"));
        }

        // A world variant (Toon World, Pro World, Pro World Fur). Without the shader it's known
        // only when the material carries the name it was locked with.
        public static bool IsWorld(Material material) => IsWorldName(AtlasMaterialReader.OriginalShaderName(material));

        public static bool IsWorldName(string name) => WorldWord.IsMatch(LastSegment(AtlasMaterialReader.UnlockedName(name)));

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

        // ---- With the shader: from its name, path and master label ----------------------------
        // Poiyomi's shaders are named ".poiyomi/…"; other names count when the asset sits in a
        // Poiyomi folder. The Extras (stencil and scanner helpers, the fake shadow) aren't materials
        // this parser reads.
        public static bool IsPoiyomiFromMetadata(string name, string path)
        {
            name = AtlasMaterialReader.UnlockedName(name);
            if (name.IndexOf("/Extras/", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return IsPoiyomiName(name) || (path ?? "").Replace('\\', '/').IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Pro when the shader's own name says so; every other Poiyomi shader is Toon, whatever its
        // name: 8.0's "Poiyomi Outline" and 8.1's "Poiyomi Grab Pass" don't say Toon. The path
        // decides only for shaders not named ".poiyomi/…": 7.3 lives in a folder called Pro.
        public static AtlasPoiyomiVariant DetectVariantFromMetadata(string name, string path)
        {
            if (!IsPoiyomiFromMetadata(name, path)) return AtlasPoiyomiVariant.None;
            name = AtlasMaterialReader.UnlockedName(name);
            if (ProWord.IsMatch(LastSegment(name))) return AtlasPoiyomiVariant.Pro;
            bool proFolder = (path ?? "").Replace('\\', '/').IndexOf("/Pro/", StringComparison.OrdinalIgnoreCase) >= 0;
            return !IsPoiyomiName(name) && proFolder ? AtlasPoiyomiVariant.Pro : AtlasPoiyomiVariant.Toon;
        }

        // The master label carries the version in every release and survives locking; the name
        // and the path carry it for the versions kept under a version folder. 7.x used
        // bullet-delimited shader names. 0: Poiyomi, but the version can't be told.
        public static int DetectFromMetadata(string name, string path, string shaderMasterLabel)
        {
            if (DetectVariantFromMetadata(name, path) == AtlasPoiyomiVariant.None) return 0;
            name = AtlasMaterialReader.UnlockedName(name);
            int major = Major(LabelVersion, shaderMasterLabel);
            if (major == 0) major = Major(NameVersion, name);
            if (major == 0) major = Major(PathVersion, (path ?? "").Replace('\\', '/'));
            if (major == 0 && name.IndexOf('•') >= 0) major = 7;
            return major;
        }

        // For output shaders found by name.
        public static string GetShaderMasterLabel(Shader shader) => AtlasMaterialReader.PropertyLabel(shader, "shader_master_label");

        private static bool IsPoiyomiName(string name) => name.StartsWith(".poiyomi/", StringComparison.OrdinalIgnoreCase);

        private static string LastSegment(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int slash = name.LastIndexOf('/');
            return slash < 0 ? name : name.Substring(slash + 1);
        }

        private static int Major(Regex pattern, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var match = pattern.Match(text);
            return match.Success && int.TryParse(match.Groups[1].Value, out int major) ? major : 0;
        }
    }
}
