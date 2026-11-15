using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Input parsing must not need the source shader installed; output shaders are only offered
    // and built when installed.
    public sealed class AtlasShaderAvailabilityTests
    {
        // Makes the reader use the material's saved values, as it does when the shader is missing.
        private sealed class SavedValuesOnly : IDisposable
        {
            private static readonly FieldInfo Flag = typeof(AtlasShaderRegistry).Assembly
                .GetType("NordicWind.AdvancedAtlaser.Editor.AtlasMaterialReader")
                .GetField("ForceSavedProperties", BindingFlags.NonPublic | BindingFlags.Static);
            public SavedValuesOnly() { Flag.SetValue(null, true); }
            public void Dispose() { Flag.SetValue(null, false); }
        }

        // Material.SetFloat drops names its shader doesn't declare, so properties from another
        // shader are written into the material's saved data directly, as an asset would have them.
        private static void AddSaved(Material material, string list, string name, Action<SerializedProperty> set)
        {
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
        private static void AddSavedFloat(Material material, string name) =>
            AddSaved(material, "m_Floats", name, value => value.floatValue = 0f);
        private static void AddSavedColor(Material material, string name) =>
            AddSaved(material, "m_Colors", name, value => value.colorValue = Color.white);

        private static Material StandardMaterial()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The Standard shader is not available.");
            return new Material(shader);
        }

        [Test]
        public void SavedValuesGiveTheSameResultAsTheShader()
        {
            var material = StandardMaterial();
            var albedo = new Texture2D(4, 4);
            try
            {
                material.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1f));
                material.SetTexture("_MainTex", albedo);
                material.SetTextureScale("_MainTex", new Vector2(2f, 3f));
                material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0.25f));

                var installed = new AtlasFallbackParser().Parse(material);
                AtlasUnifiedMaterial saved;
                using (new SavedValuesOnly()) saved = new AtlasFallbackParser().Parse(material);

                // The values pass through serialization, so they can differ by float rounding.
                Assert.AreEqual(0f, Vector4.Distance(installed.Surface.BaseColor.Color, saved.Surface.BaseColor.Color), 1e-5f);
                Assert.AreSame(albedo, saved.Surface.BaseColor.Map.Texture);
                Assert.AreEqual(new Vector2(2f, 3f), saved.Surface.BaseColor.Map.Tiling);
                Assert.AreEqual(new Vector2(0.5f, 0.25f), saved.Surface.BaseColor.Map.Offset);
                Assert.IsTrue(saved.Issues.Exists(i => i.Feature == "shader.not-installed"));
                Assert.IsFalse(installed.Issues.Exists(i => i.Feature == "shader.not-installed"));
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(albedo); }
        }

        [Test]
        public void PoiyomiProIsDetectedFromSavedValuesAlone()
        {
            // A Standard material carrying Poiyomi Pro 11's properties: neither Poiyomi nor its
            // shader name is involved, as when Poiyomi isn't installed.
            var material = StandardMaterial();
            try
            {
                foreach (string name in new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_MainBlackWhiteStrength", "_ConstellationEnable" })
                    AddSavedFloat(material, name);
                AddSavedColor(material, "_LightingShadowColor");
                using (new SavedValuesOnly())
                {
                    Assert.AreEqual(AtlasPoiyomiVariant.Pro, AtlasPoiyomiVersionDetector.DetectVariant(material));
                    Assert.AreEqual(11, AtlasPoiyomiVersionDetector.Detect(material));
                }
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCase(new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor" }, AtlasPoiyomiVariant.Toon, 7)]
        [TestCase(new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor", "_ALDecalBlendType" }, AtlasPoiyomiVariant.Toon, 8)]
        [TestCase(new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor", "_AlphaMaskInvert", "_ALDecalBlendType" }, AtlasPoiyomiVariant.Toon, 9)]
        [TestCase(new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor", "_ALDecalPosition" }, AtlasPoiyomiVariant.Toon, 10)]
        [TestCase(new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor", "_ALDecalPosition", "_PoiPBRMetallic" }, AtlasPoiyomiVariant.Toon, 11)]
        [TestCase(new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_LightingShadowColor", "_ALDecalPosition", "_TPSPenetratorEnabled" }, AtlasPoiyomiVariant.Pro, 10)]
        [TestCase(new[] { "_LightingMode", "_MainTex" }, AtlasPoiyomiVariant.None, 0)]
        public void PoiyomiFamilyVariantAndVersionFromPropertyNames(string[] names, AtlasPoiyomiVariant variant, int major)
        {
            var set = new HashSet<string>(names);
            Assert.AreEqual(variant, AtlasPoiyomiVersionDetector.DetectVariantFromProperties(set.Contains));
            Assert.AreEqual(major, AtlasPoiyomiVersionDetector.DetectFromProperties(set.Contains));
        }

        [Test]
        public void OnlyInstalledOutputShadersAreOffered()
        {
            var installed = AtlasShaderRegistry.InstalledOutputShaders;
            foreach (var output in AtlasShaderRegistry.OutputShaders)
                Assert.AreEqual(output.FindShader() != null, installed.Contains(output), output.DisplayName);
            var mobileToon = AtlasShaderRegistry.Output("mobiletoon");
            Assert.AreEqual(Shader.Find("VRChat/Mobile/Toon Standard") != null, AtlasShaderRegistry.IsInstalled(mobileToon));
        }

        [Test]
        public void PoiyomiToon10OutputNeedsVersion10()
        {
            var shader = AtlasShaderRegistry.Output("poiyomi10").FindShader();
            if (shader == null) Assert.Ignore("Poiyomi Toon 10 is not installed.");
            Assert.AreEqual(10, AtlasPoiyomiVersionDetector.DetectFromMetadata(shader.name,
                UnityEditor.AssetDatabase.GetAssetPath(shader), AtlasPoiyomiVersionDetector.GetShaderMasterLabel(shader)));
        }
    }
}
