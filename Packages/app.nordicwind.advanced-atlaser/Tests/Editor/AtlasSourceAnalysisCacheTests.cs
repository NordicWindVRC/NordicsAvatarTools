using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasSourceAnalysisCacheTests
    {
        private Material material;

        [SetUp]
        public void SetUp() => material = AtlasTestMaterials.Standard();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(material);

        [Test]
        public void AnUnchangedMaterialIsParsedOnce()
        {
            var first = AtlasSourceAnalysisCache.Get(material);
            int parses = AtlasSourceAnalysisCache.ParseCount;
            Assert.AreSame(first, AtlasSourceAnalysisCache.Get(material));
            Assert.AreEqual(parses, AtlasSourceAnalysisCache.ParseCount);
            Assert.NotNull(first.Unified);
            Assert.IsNull(first.Error);
        }

        [Test]
        public void AChangedMaterialIsParsedAgain()
        {
            var first = AtlasSourceAnalysisCache.Get(material);
            material.SetColor("_Color", Color.red);
            EditorUtility.SetDirty(material);
            var second = AtlasSourceAnalysisCache.Get(material);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(Color.red, second.Unified.Surface.BaseColor.Color);
        }

        [Test]
        public void AnImportParsesEverySourceAgain()
        {
            var first = AtlasSourceAnalysisCache.Get(material);
            AtlasSourceAnalysisCache.Invalidate();
            Assert.AreNotSame(first, AtlasSourceAnalysisCache.Get(material));
        }

        [Test]
        public void NoMaterialHasNoAnalysis()
        {
            Assert.IsNull(AtlasSourceAnalysisCache.Get(null));
        }
    }
}
