using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The unified material's building blocks and the parts of the build that read it.
    public sealed class AtlasUnifiedMaterialTests
    {
        [Test]
        public void SmoothnessBecomesRoughnessByInvertingTheResult()
        {
            var roughness = new AtlasScalarInput(0.8f) { InvertResult = true };
            Assert.AreEqual(0.2f, roughness.Evaluate(), 1e-4f);
        }

        [Test]
        public void OcclusionStrengthKeepsWhiteWhite()
        {
            var occlusion = new AtlasScalarInput(0.4f) {
                Map = new AtlasTextureReference { Texture = Texture2D.whiteTexture },
                Combine = AtlasScalarCombine.StrengthAroundOne };
            Assert.AreEqual(0.8f, occlusion.Evaluate(0.5f), 1e-4f);
            Assert.AreEqual(1f, occlusion.Evaluate(1f), 1e-4f);
            Assert.AreEqual(1f, new AtlasSurfaceResponse().Occlusion.Evaluate(0f), 1e-4f, "No map: no occlusion.");
        }

        [Test]
        public void RemapActsOnTheSampleBeforeStrengthAndInversion()
        {
            var remap = new AtlasScalarRemap { OutputMin = 0.2f, OutputMax = 0.6f };
            Assert.AreEqual(0.4f, remap.Apply(0.5f), 1e-5f);
            var occlusion = new AtlasScalarInput(0.5f) { Map = new AtlasTextureReference(),
                Combine = AtlasScalarCombine.StrengthAroundOne, Remap = remap };
            // The sample 0.5 becomes 0.4, then strength 0.5 around one: 1 - 0.6 * 0.5.
            Assert.AreEqual(0.7f, occlusion.Evaluate(0.5f), 1e-5f);
            var roughness = new AtlasScalarInput(1f) { Map = new AtlasTextureReference(), InvertResult = true,
                Remap = new AtlasScalarRemap { Lightness = 0.1f } };
            Assert.AreEqual(0.4f, roughness.Evaluate(0.5f), 1e-5f);
        }

        [Test]
        public void MaskWithoutMapAppliesEverywhereAndStrengthFadesIt()
        {
            Assert.AreEqual(1f, new AtlasMask().Evaluate(0f));
            var mask = new AtlasMask { Map = new AtlasTextureReference(), Invert = true, Strength = 0.5f };
            Assert.AreEqual(0.75f, mask.Evaluate(0.5f), 1e-4f);
        }

        [Test]
        public void ReportAddsOneLinePerKindAndFeature()
        {
            var material = new AtlasUnifiedMaterial();
            material.Report(AtlasIssueKind.Unmapped, "outline", "first");
            material.Report(AtlasIssueKind.Unmapped, "outline", "second");
            material.Report(AtlasIssueKind.Note, "outline", "a note");
            Assert.AreEqual(2, material.Issues.Count);
            Assert.AreEqual("first", material.Issues[0].Message);
            Assert.IsTrue(material.Issues[0].IsWarning);
            Assert.IsFalse(material.Issues[1].IsWarning);
        }

        [Test]
        public void AutoSetupSizesTheAtlasByTheLargestBaseColourOnly()
        {
            var small = new Texture2D(128, 256);
            var large = new Texture2D(1024, 512);
            var hugeNormal = new Texture2D(2048, 2048);
            try
            {
                var first = new AtlasUnifiedMaterial();
                first.Surface.BaseColor.Map = new AtlasTextureReference { Texture = small };
                var second = new AtlasUnifiedMaterial();
                second.Surface.BaseColor.Map = new AtlasTextureReference { Texture = large };
                second.Surface.Normal.Map = new AtlasTextureReference { Texture = hugeNormal };
                var main = new AtlasTextureSize();
                AtlasOutputAutoSetup.SetMainTextureDimensions(new List<AtlasUnifiedMaterial> { first, second }, main);
                Assert.AreEqual(1024, main.width);
                Assert.AreEqual(512, main.height);
            }
            finally { Object.DestroyImmediate(small); Object.DestroyImmediate(large); Object.DestroyImmediate(hugeNormal); }
        }

        [Test]
        public void FallbackReadsTheBaseColourInLinear()
        {
            // A built-in shader no other parser reads.
            var shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) Assert.Ignore("Legacy Shaders/Diffuse is not available.");
            var material = new Material(shader);
            var albedo = new Texture2D(4, 4);
            try
            {
                var authored = new Color(0.5f, 0.25f, 1f, 0.5f);
                material.SetColor("_Color", authored);
                material.SetTexture("_MainTex", albedo);
                material.SetTextureScale("_MainTex", new Vector2(2f, 3f));
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.IsInstanceOf<AtlasFallbackParser>(parser);
                var unified = parser.Parse(material);
                Assert.AreEqual(0f, Vector4.Distance(authored.linear, unified.Surface.BaseColor.Color), 1e-5f);
                Assert.AreSame(albedo, unified.Surface.BaseColor.Map.Texture);
                Assert.AreEqual(new Vector2(2f, 3f), unified.Surface.BaseColor.Map.Tiling);
                Assert.AreEqual(1, unified.Issues.FindAll(i => i.IsWarning).Count);
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(albedo); }
        }
    }
}
