using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasArchitectureTests
    {
        [Test]
        public void SourceReaderUsesEvaluatedSetNotSerializedBaseList()
        {
            var owner = new GameObject("Source reader test");
            var source = new GameObject("Effective source");
            try
            {
                var renderer = source.AddComponent<SkinnedMeshRenderer>();
                var fakeMerge = owner.AddComponent<AtlasTestSourceContainer>();
                fakeMerge.renderersSet.Effective.Add(renderer);
                var method = typeof(AtlasMaterialDiscovery).GetMethod("TryReadEffectiveSources",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(method);
                var args = new object[] { fakeMerge, null, null };
                Assert.IsTrue((bool)method.Invoke(null, args), (string)args[2]);
                var resolved = (List<SkinnedMeshRenderer>)args[1];
                Assert.AreEqual(1, resolved.Count);
                Assert.AreSame(renderer, resolved[0]);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(source);
            }
        }
        [Test]
        public void DiscoveryWithoutAAOUsesOnlyLocalRendererAndDeduplicatesMaterials()
        {
            var owner = new GameObject("Discovery owner");
            var unrelated = new GameObject("Unrelated child");
            var material = new Material(Shader.Find("Standard"));
            try
            {
                unrelated.transform.SetParent(owner.transform);
                var local = owner.AddComponent<SkinnedMeshRenderer>();
                local.sharedMaterials = new[] { material, material };
                unrelated.AddComponent<SkinnedMeshRenderer>().sharedMaterial = material;
                var result = AtlasMaterialDiscovery.Discover(owner);
                Assert.IsFalse(result.HasAAO);
                Assert.AreEqual(1, result.Renderers.Count);
                Assert.AreSame(local, result.Renderers[0]);
                Assert.AreEqual(1, result.Materials.Count);
                Assert.AreSame(material, result.Materials[0]);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(material);
            }
        }
        [Test]
        public void DiscoveryHandlesMissingOwner()
        {
            var result = AtlasMaterialDiscovery.Discover(null);
            Assert.IsFalse(result.HasAAO);
            Assert.IsEmpty(result.Renderers);
            Assert.IsEmpty(result.Materials);
        }
        [Test]
        public void BlendShapeExclusionIsEmptyByDefaultAndSupportsRendererNamePairs()
        {
            var go = new GameObject("Atlaser Exclusion Test");
            try
            {
                var atlaser = go.AddComponent<NordicsAdvancedAtlaser>();
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                Assert.NotNull(atlaser.excludedBlendShapes);
                Assert.AreEqual(0, atlaser.excludedBlendShapes.Count);
                atlaser.excludedBlendShapes.Add(new AtlasExcludedBlendShape {
                    renderer = renderer, blendShapeName = "Hide Breasts" });
                Assert.AreSame(renderer, atlaser.excludedBlendShapes[0].renderer);
                Assert.AreEqual("Hide Breasts", atlaser.excludedBlendShapes[0].blendShapeName);
                Assert.AreEqual(0.001f, atlaser.excludedBlendShapes[0].threshold);
                Assert.AreEqual(AtlasBlendShapeSelectionMode.AnyVertex, atlaser.excludedBlendShapes[0].selectionMode);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void GeometryBakingIsEnabledByDefaultAndOutputDefaultsMatchTheDesign()
        {
            var go = new GameObject("Atlaser Test");
            try
            {
                var atlaser = go.AddComponent<NordicsAdvancedAtlaser>();
                Assert.IsTrue(atlaser.enableGeometryBaking);
                // The bake resolution is automatic now (AtlasOutputLayout.BakeSize).
                Assert.IsNull(typeof(NordicsAdvancedAtlaser).GetField("geometryBakingResolution"));
                Assert.IsNull(typeof(NordicsAdvancedAtlaser).GetField("GeometryNormalPadding"));
                var output = go.AddComponent<NordicsAtlasOutput>();
                Assert.IsNull(typeof(AtlasOutputConfiguration).GetField("MaxGeometryBakeDimension"));
                Assert.NotNull(output.configuration);
                Assert.AreEqual(4096, output.configuration.mainTexture.width);
                Assert.AreEqual(4096, output.configuration.mainTexture.height);
                Assert.AreEqual(4, output.configuration.regionPadding);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void MainTextureSettingsAreNotResetByValidation()
        {
            var go = new GameObject("Atlas output Main setting test");
            try
            {
                var output = go.AddComponent<NordicsAtlasOutput>();
                output.configuration.mainTexture.width = 4096;
                output.configuration.mainTexture.height = 1024;
                // OnValidate used to overwrite this with hidden legacy 2048x2048.
                output.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
                Assert.AreEqual(4096, output.configuration.mainTexture.width);
                Assert.AreEqual(1024, output.configuration.mainTexture.height);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void OutputComponentHasNoLegacyMainTextureShadow()
        {
            Assert.IsNull(typeof(NordicsAtlasOutput).GetField("MainTexture",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
            Assert.IsNull(typeof(NordicsAtlasOutput).GetMethod("Migrate"));
        }

        [TestCase(".poiyomi/Poiyomi Toon", "", AtlasPoiyomiVariant.Toon)]
        [TestCase(".poiyomi/Poiyomi Pro", "", AtlasPoiyomiVariant.Pro)]
        [TestCase("Hidden/Whatever", "Assets/Poiyomi/Shaders/Toon/Foo.shader", AtlasPoiyomiVariant.Toon)]
        [TestCase("Standard", "", AtlasPoiyomiVariant.None)]
        public void PoiyomiFamilyAndVariantDetection(string name, string path, AtlasPoiyomiVariant expected)
        {
            Assert.AreEqual(expected, AtlasPoiyomiVersionDetector.DetectVariantFromMetadata(name, path));
        }

        [TestCase(".poiyomi/• Poiyomi Toon •", "Assets/_PoiyomiShaders/Shaders/Pro/Shaders/S_Poiyomi_Toon.shader", null, 7)]
        [TestCase(".poiyomi/Poiyomi 7.3/• Poiyomi Toon •", "", null, 7)]
        [TestCase(".poiyomi/Poiyomi 8.1/Poiyomi Toon", "", null, 8)]
        [TestCase(".poiyomi/Old Versions/8.1/Poiyomi Toon", "", null, 8)]
        [TestCase(".poiyomi/Poiyomi Toon", "Assets/_PoiyomiShaders/Shaders/9.3/Toon/Poiyomi Toon.shader", null, 9)]
        [TestCase(".poiyomi/Poiyomi Toon", "Assets/_PoiyomiShaders/Shaders/10.0/Toon/Poiyomi Toon.shader", null, 10)]
        [TestCase(".poiyomi/Poiyomi Toon", "", "<color=#E75898ff>Poiyomi 9.3.64</color>", 9)]
        [TestCase(".poiyomi/Poiyomi Toon", "", "<color=#E75898ff>Poiyomi 10.0.28</color>", 10)]
        [TestCase(".poiyomi/Poiyomi Toon", "", "<color=#E75898ff>Poiyomi 11.0.50</color>", 11)]
        [TestCase(".poiyomi/Poiyomi Toon", "Packages/com.poiyomi.pro/_PoiyomiShaders/Shaders/11.0/Toon/Poiyomi Toon.shader", null, 11)]
        [TestCase(".poiyomi/Poiyomi Pro", "", "<color=#E75898ff>Poiyomi 11.0.50</color>", 11)]
        [TestCase(".poiyomi/Old Versions/10.0/Poiyomi Pro", "", "<color=#E75898ff>Poiyomi 10.0.29</color>", 10)]
        [TestCase(".poiyomi/Poiyomi Toon", "", null, 0)]
        [TestCase("Standard", "", null, 0)]
        public void PoiyomiSourceNameAndPathDetection(string name, string path, string label, int expected)
        {
            Assert.AreEqual(expected, AtlasPoiyomiVersionDetector.DetectFromMetadata(name, path, label));
        }

        [Test]
        public void UnifiedModelHasNoRawSourcePropertyDictionary()
        {
            Assert.IsNull(typeof(AtlasUnifiedMaterial).GetField("Properties"));
            Assert.IsNull(typeof(AtlasUnifiedMaterial).GetField("DiagnosticParserId"));
            Assert.IsNull(typeof(AtlasUnifiedMaterial).GetField("DiagnosticSourceShader"));
        }

        [Test]
        public void GeneratorInputCannotAccessOriginalSourceMaterialOrParser()
        {
            Assert.IsNull(typeof(AtlasGeneratorSource).GetField("SourceMaterial"));
            Assert.IsNull(typeof(AtlasGeneratorSource).GetField("Parser"));
            Assert.IsNull(typeof(AtlasGeneratorJob).GetField("Component"));
            Assert.IsNull(typeof(AtlasGeneratorJob).GetField("SourcesMaterials"));
        }
        [Test]
        public void MissingSubmeshesProduceEmptyMaskAndFlatCorrection()
        {
            var shader = Shader.Find("Standard");
            if(shader == null) Assert.Ignore("Standard shader is not available in this project.");
            var material=new Material(shader);
            try
            {
                using(var bake=AtlasSubmeshBaker.Bake(material,16,16,null,
                    new List<SkinnedMeshRenderer>(),null,new List<string>()))
                {
                    Assert.AreEqual(1,bake.Submeshes.Count);
                    Assert.IsTrue(bake.Submeshes[0].CurrentSubmeshMissing);
                    Assert.IsFalse(bake.Submeshes[0].CurrentMask.IsPixelUsed(0,0));
                    var normal=bake.Submeshes[0].TangentNormalCorrection.GetPixel(0,0);
                    Assert.AreEqual(1f,normal.b,0.001f);
                }
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Mirrors the reflection contract without introducing a hard AAO dependency.
    public sealed class AtlasTestSourceContainer : MonoBehaviour
    {
        public AtlasTestEffectiveSources renderersSet = new AtlasTestEffectiveSources();
    }

    [System.Serializable]
    public sealed class AtlasTestEffectiveSources
    {
        public readonly List<SkinnedMeshRenderer> Effective = new List<SkinnedMeshRenderer>();
        public List<SkinnedMeshRenderer> GetAsList() => Effective;
    }
}