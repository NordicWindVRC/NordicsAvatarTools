using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// The property values a material saved, read without its shader. Unity keeps them in the
    /// material asset even when the shader isn't installed; the shader API can't see them then,
    /// because <see cref="Material.HasProperty"/> asks the shader.
    /// </summary>
    internal sealed class AtlasSavedProperties
    {
        public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
        // Colours and vectors share one list in the material asset.
        public readonly Dictionary<string, Vector4> Vectors = new Dictionary<string, Vector4>();
        public readonly Dictionary<string, (Texture Texture, Vector2 Scale, Vector2 Offset)> Textures =
            new Dictionary<string, (Texture, Vector2, Vector2)>();
        public readonly HashSet<string> Keywords = new HashSet<string>();

        public bool Has(string name) => Floats.ContainsKey(name) || Vectors.ContainsKey(name) || Textures.ContainsKey(name);

        public IEnumerable<string> Names
        {
            get
            {
                foreach (var name in Floats.Keys) yield return name;
                foreach (var name in Vectors.Keys) yield return name;
                foreach (var name in Textures.Keys) yield return name;
            }
        }

        public static AtlasSavedProperties Read(Material material)
        {
            var result = new AtlasSavedProperties();
            if (material == null) return result;
            using (var serialized = new SerializedObject(material))
            {
                var saved = serialized.FindProperty("m_SavedProperties");
                if (saved != null)
                {
                    ReadPairs(saved.FindPropertyRelative("m_Floats"), (name, value) => result.Floats[name] = value.floatValue);
                    ReadPairs(saved.FindPropertyRelative("m_Ints"), (name, value) => result.Floats[name] = value.intValue);
                    ReadPairs(saved.FindPropertyRelative("m_Colors"), (name, value) => result.Vectors[name] = value.colorValue);
                    ReadPairs(saved.FindPropertyRelative("m_TexEnvs"), (name, value) => result.Textures[name] = (
                        value.FindPropertyRelative("m_Texture")?.objectReferenceValue as Texture,
                        value.FindPropertyRelative("m_Scale")?.vector2Value ?? Vector2.one,
                        value.FindPropertyRelative("m_Offset")?.vector2Value ?? Vector2.zero));
                }
                // Keywords move to the invalid list when the shader that declares them is missing.
                foreach (var list in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
                {
                    var keywords = serialized.FindProperty(list);
                    if (keywords == null || !keywords.isArray) continue;
                    for (int i = 0; i < keywords.arraySize; ++i) result.Keywords.Add(keywords.GetArrayElementAtIndex(i).stringValue);
                }
                var legacy = serialized.FindProperty("m_ShaderKeywords");
                if (legacy != null && !string.IsNullOrEmpty(legacy.stringValue))
                    foreach (var keyword in legacy.stringValue.Split(' '))
                        if (keyword.Length > 0) result.Keywords.Add(keyword);
            }
            return result;
        }

        private static void ReadPairs(SerializedProperty list, System.Action<string, SerializedProperty> read)
        {
            if (list == null || !list.isArray) return;
            for (int i = 0; i < list.arraySize; ++i)
            {
                var pair = list.GetArrayElementAtIndex(i);
                var name = pair.FindPropertyRelative("first");
                var value = pair.FindPropertyRelative("second");
                if (name == null || value == null || string.IsNullOrEmpty(name.stringValue)) continue;
                read(name.stringValue, value);
            }
        }
    }
}
