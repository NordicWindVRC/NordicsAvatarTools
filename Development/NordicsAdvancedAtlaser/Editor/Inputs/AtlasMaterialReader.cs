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

        public static bool Has(Material material, string property)
        {
            if (material == null || string.IsNullOrEmpty(property)) return false;
            var saved = Saved(material);
            return saved != null ? saved.Has(property) : material.HasProperty(property);
        }

        public static Vector4 Vector(Material material, string name, Vector4 fallback)
        {
            if (!Has(material, name)) return fallback;
            var saved = Saved(material);
            if (saved != null) return saved.Vectors.TryGetValue(name, out var value) ? value : fallback;
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
            return material.GetTextureScale(name);
        }

        public static Vector2 TextureOffset(Material material, string name)
        {
            var saved = Saved(material);
            if (saved != null) return saved.Textures.TryGetValue(name, out var t) ? t.Offset : Vector2.zero;
            return material.GetTextureOffset(name);
        }

        public static float Float(Material material, string name, float fallback)
        {
            if (!Has(material, name)) return fallback;
            var saved = Saved(material);
            if (saved != null) return saved.Floats.TryGetValue(name, out var value) ? value : fallback;
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0) return fallback;
            var type = material.shader.GetPropertyType(index);
            if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector || type == ShaderPropertyType.Texture) return fallback;
            // Material.GetFloat() is not reliable for native ShaderLab Int properties.
            return type == ShaderPropertyType.Int ? material.GetInt(name) : material.GetFloat(name);
        }

        // The colour as stored: gamma-encoded, as the material inspector shows it.
        public static Color Color(Material material, string name, Color fallback)
        {
            if (!Has(material, name)) return fallback;
            var saved = Saved(material);
            if (saved != null) return saved.Vectors.TryGetValue(name, out var value) ? (Color)value : fallback;
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Color) return fallback;
            return material.GetColor(name);
        }

        // The colour converted to linear, as the unified material stores colours. The fallback
        // is linear already. Alpha is unchanged.
        public static Color ColorLinear(Material material, string name, Color fallback) =>
            Has(material, name) ? Color(material, name, fallback).linear : fallback;

        public static Texture Texture(Material material, string name)
        {
            if (!Has(material, name)) return null;
            var saved = Saved(material);
            if (saved != null) return saved.Textures.TryGetValue(name, out var t) ? t.Texture : null;
            int index = material.shader == null ? -1 : material.shader.FindPropertyIndex(name);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Texture) return null;
            return material.GetTexture(name);
        }
    }
}
