using System;
using System.Reflection;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Builds source materials for parser tests without the source shader installed: a Standard
    // material carries the source shader's properties in its saved data, and the reader is told
    // to read saved values, as it does when a shader is missing.
    internal static class AtlasTestMaterials
    {
        // While alive, the reader uses the material's saved values even though Standard is installed.
        public sealed class SavedValuesOnly : IDisposable
        {
            private static readonly FieldInfo Flag = typeof(AtlasShaderRegistry).Assembly
                .GetType("NordicWind.AdvancedAtlaser.Editor.AtlasMaterialReader")
                .GetField("ForceSavedProperties", BindingFlags.NonPublic | BindingFlags.Static);
            public SavedValuesOnly() { Flag.SetValue(null, true); }
            public void Dispose() { Flag.SetValue(null, false); }
        }

        public static Material Standard()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The Standard shader is not available.");
            return new Material(shader);
        }

        // Material.SetFloat drops names its shader doesn't declare, so properties of another
        // shader are written into the material's saved data directly, as an asset would have them.
        // Call it after the material's own Set* calls: the entry is appended, which breaks the
        // name order Unity looks saved properties up in, so a later Set* can miss its entry.
        // Only for names the host shader doesn't declare in the same list: for those it does, Unity
        // keeps the existing entry and drops the added one, so use Set* instead. A name it declares
        // with another type (Poiyomi 7.3's float _ParallaxMap, Standard's texture) lives in another
        // list and is kept.
        public static void AddSaved(Material material, string list, string name, Action<SerializedProperty> set)
        {
            // Unity would keep the existing entry and silently drop this one.
            int index0 = material.shader != null ? material.shader.FindPropertyIndex(name) : -1;
            if (index0 >= 0 && ListOf(material.shader.GetPropertyType(index0)) == list)
                throw new ArgumentException(name + " is declared by " + material.shader.name + ": set it with Set*, not AddSaved.", nameof(name));
            using (var serialized = new SerializedObject(material))
            {
                var entries = serialized.FindProperty("m_SavedProperties").FindPropertyRelative(list);
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                var entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("first").stringValue = name;
                set(entry.FindPropertyRelative("second"));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // The saved-properties list a declared property of this type is stored in.
        private static string ListOf(UnityEngine.Rendering.ShaderPropertyType type)
        {
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Texture: return "m_TexEnvs";
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                case UnityEngine.Rendering.ShaderPropertyType.Vector: return "m_Colors";
                case UnityEngine.Rendering.ShaderPropertyType.Int: return "m_Ints";
                default: return "m_Floats";
            }
        }

        public static void AddSavedFloat(Material material, string name, float value = 0f) =>
            AddSaved(material, "m_Floats", name, v => v.floatValue = value);

        public static void AddSavedColor(Material material, string name, Color value) =>
            AddSaved(material, "m_Colors", name, v => v.colorValue = value);

        public static void AddSavedColor(Material material, string name) => AddSavedColor(material, name, Color.white);

        public static void AddSavedTexture(Material material, string name, Texture texture) =>
            AddSaved(material, "m_TexEnvs", name, v =>
            {
                v.FindPropertyRelative("m_Texture").objectReferenceValue = texture;
                v.FindPropertyRelative("m_Scale").vector2Value = Vector2.one;
                v.FindPropertyRelative("m_Offset").vector2Value = Vector2.zero;
            });
    }
}
