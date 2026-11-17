using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Reads a source material's values. Parsers read only through this class, never through the
    /// Material or Shader API, so parsing works without the source shader installed: when it's
    /// missing, values come from what the material saved.
    /// </summary>
    internal static class AtlasMaterialReader
    {
        // Tests only: read saved values even when the shader is installed.
        internal static bool ForceSavedProperties;
        private static Material cachedMaterial;
        private static AtlasSavedProperties cachedProperties;

        public static bool ShaderMissing(Material material) =>
            material != null && (material.shader == null || material.shader.name == "Hidden/InternalErrorShader");

        // Saved values when they must be used, otherwise null (read through the shader instead).
        public static AtlasSavedProperties Saved(Material material)
        {
            if (material == null || !(ForceSavedProperties || ShaderMissing(material))) return null;
            if (cachedMaterial != material || cachedProperties == null)
            {
                cachedMaterial = material;
                cachedProperties = AtlasSavedProperties.Read(material);
            }
            return cachedProperties;
        }

        // Called at the start of detection and parsing, so edits between parses are seen.
        public static void ClearCache() { cachedMaterial = null; cachedProperties = null; }

        // The installed shader's name, or empty when it's missing.
        public static string ShaderName(Material material) =>
            material == null || Saved(material) != null || material.shader == null ? "" : material.shader.name;

        // The installed shader's asset path, or empty when it's missing.
        public static string ShaderPath(Material material) =>
            ShaderName(material).Length == 0 ? "" : UnityEditor.AssetDatabase.GetAssetPath(material.shader);

        // A shader property's display name, or null (Poiyomi's master label carries its version).
        public static string PropertyLabel(Shader shader, string property)
        {
            if (shader == null) return null;
            try
            {
                int index = shader.FindPropertyIndex(property);
                return index < 0 ? null : shader.GetPropertyDescription(index);
            }
            catch (System.Exception e)
            {
                // A shader that failed to import has no readable properties.
                Debug.LogWarning("[Nordic's Advanced Atlaser] Couldn't read " + property + " of " + shader.name + ": " + e.Message);
                return null;
            }
        }

        // The installed shader's label of a property, or null.
        public static string PropertyLabel(Material material, string property) =>
            ShaderName(material).Length == 0 ? null : PropertyLabel(material.shader, property);

        // One of the material's own (override) tags, or empty. Tags are saved with the material, so
        // they can be read without its shader.
        public static string Tag(Material material, string name)
        {
            if (material == null) return "";
            var saved = Saved(material);
            if (saved != null) return saved.Tags.TryGetValue(name, out var value) ? value : "";
            return material.GetTag(name, false, "");
        }

        // The shader's name as the user chose it, or empty when it can't be known. A shader locked
        // by Thry's optimizer is named Hidden/Locked/<name>/<material id>. With the shader missing,
        // the name is the one the optimizer tagged the material with when it was last locked, which
        // is out of date if the shader was changed after unlocking.
        public static string OriginalShaderName(Material material)
        {
            string name = ShaderName(material);
            return name.Length > 0 ? UnlockedName(name) : Tag(material, "OriginalShader");
        }

        // A locked shader's name without the optimizer's prefix and material id.
        public static string UnlockedName(string name)
        {
            const string locked = "Hidden/Locked/";
            if (string.IsNullOrEmpty(name) || !name.StartsWith(locked, System.StringComparison.Ordinal)) return name ?? "";
            int end = name.LastIndexOf('/');
            return end > locked.Length ? name.Substring(locked.Length, end - locked.Length) : name.Substring(locked.Length);
        }

        public static bool Has(Material material, string property)
        {
            if (material == null || string.IsNullOrEmpty(property)) return false;
            var saved = Saved(material);
            return saved != null ? saved.Has(property) : material.HasProperty(Declared(material, property));
        }

        // The name the installed shader declares a property under. Thry's optimizer renames the
        // properties a material marks as renamed when locked (tag <name>Animated = 2) to
        // <name>_<suffix> throughout the locked shader and moves their values there; the suffix is
        // the material's rename tag or its name. Saved values keep the original name.
        private static string Declared(Material material, string name)
        {
            if (material.HasProperty(name) || material.GetTag(name + "Animated", false, "") != "2") return name;
            string renamed = name + "_" + RenameSuffix(material.GetTag("thry_rename_suffix", false, material.name));
            return material.HasProperty(renamed) ? renamed : name;
        }

        // The optimizer's property-name form of a suffix: spaces removed; letters, digits and the
        // underscore kept; every other byte written as two hexadecimal digits.
        internal static string RenameSuffix(string text)
        {
            var result = new System.Text.StringBuilder();
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text.Trim().Replace(" ", "")))
            {
                bool word = (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') || b == '_';
                if (word) result.Append((char)b);
                else result.Append(b.ToString("X2"));
            }
            return result.ToString();
        }

        public static Vector4 Vector(Material material, string name, Vector4 fallback)
        {
            if (!Has(material, name)) return fallback;
            var saved = Saved(material);
            if (saved != null) return saved.Vectors.TryGetValue(name, out var value) ? value : fallback;
            name = Declared(material, name);
            int index = material.shader.FindPropertyIndex(name);
            if (index < 0) return fallback;
            var type = material.shader.GetPropertyType(index);
            return type == ShaderPropertyType.Vector || type == ShaderPropertyType.Color ? material.GetVector(name) : fallback;
        }

        public static bool Keyword(Material material, string keyword)
        {
            if (material == null) return false;
            var saved = Saved(material);
            return saved != null ? saved.Keywords.Contains(keyword) : material.IsKeywordEnabled(keyword);
        }

        public static Vector2 TextureScale(Material material, string name)
        {
            var saved = Saved(material);
            if (saved != null) return saved.Textures.TryGetValue(name, out var t) ? t.Scale : Vector2.one;
            return material.GetTextureScale(Declared(material, name));
        }

        public static Vector2 TextureOffset(Material material, string name)
        {
            var saved = Saved(material);
            if (saved != null) return saved.Textures.TryGetValue(name, out var t) ? t.Offset : Vector2.zero;
            return material.GetTextureOffset(Declared(material, name));
        }

        public static float Float(Material material, string name, float fallback)
        {
            if (!Has(material, name)) return fallback;
            var saved = Saved(material);
            if (saved != null) return saved.Floats.TryGetValue(name, out var value) ? value : fallback;
            name = Declared(material, name);
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0) return fallback;
            var type = material.shader.GetPropertyType(index);
            if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector || type == ShaderPropertyType.Texture) return fallback;
            // Material.GetFloat() is not reliable for native ShaderLab Int properties.
            return type == ShaderPropertyType.Int ? material.GetInt(name) : material.GetFloat(name);
        }

        // The color as stored: gamma-encoded, as the material inspector shows it.
        public static Color Color(Material material, string name, Color fallback)
        {
            if (!Has(material, name)) return fallback;
            var saved = Saved(material);
            if (saved != null) return saved.Vectors.TryGetValue(name, out var value) ? (Color)value : fallback;
            name = Declared(material, name);
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Color) return fallback;
            return material.GetColor(name);
        }

        // The color converted to linear, as the unified material stores colors. The fallback
        // is linear already. Alpha is unchanged.
        public static Color ColorLinear(Material material, string name, Color fallback) =>
            Has(material, name) ? Color(material, name, fallback).linear : fallback;

        public static Texture Texture(Material material, string name)
        {
            if (!Has(material, name)) return null;
            var saved = Saved(material);
            if (saved != null) return saved.Textures.TryGetValue(name, out var t) ? t.Texture : null;
            name = Declared(material, name);
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Texture) return null;
            return material.GetTexture(name);
        }
    }
}
