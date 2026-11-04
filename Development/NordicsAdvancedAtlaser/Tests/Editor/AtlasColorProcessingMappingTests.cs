using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Poiyomi's saturation and brightness use different scales than the unified material, and
    // brightness changed from additive (7.x, 8.x) to multiplicative (9.x and later).
    public sealed class AtlasColorProcessingMappingTests
    {
        [TestCase(7, true, true)]
        [TestCase(8, true, true)]
        [TestCase(9, false, true)]
        [TestCase(10, false, false)]
        [TestCase(0, false, false)] // unknown version: newest convention
        public void PoiyomiSaturationAndBrightnessMapToUnifiedScales(int major, bool additive, bool clamps)
        {
            var color = new AtlasColorProcessingDefinition();
            AtlasPoiyomiUnifiedParser.MapSaturationAndBrightness(color, major, 0.5f, 0.2f);
            Assert.AreEqual(1.5f, color.Saturation, 1e-5f);
            Assert.AreEqual(additive ? 0.2f : 0f, color.Brightness, 1e-5f);
            Assert.AreEqual(additive ? 1f : 1.2f, color.BrightnessMultiplier, 1e-5f);
            Assert.AreEqual(clamps, color.ClampOutput);
        }

        [TestCase(7)]
        [TestCase(9)]
        [TestCase(10)]
        public void PoiyomiDefaultsLeaveColorsUnchanged(int major)
        {
            var color = new AtlasColorProcessingDefinition();
            AtlasPoiyomiUnifiedParser.MapSaturationAndBrightness(color, major, 0f, 0f);
            Assert.AreEqual(1f, color.Saturation, 1e-5f);
            Assert.AreEqual(0f, color.Brightness, 1e-5f);
            Assert.AreEqual(1f, color.BrightnessMultiplier, 1e-5f);
        }

        [Test]
        public void Poiyomi10TintIsPerceptualWhenShaderInstalled()
        {
            var shader = Shader.Find(".poiyomi/Poiyomi Toon");
            if (shader == null) Assert.Ignore("Poiyomi Toon is not installed.");
            var material = new Material(shader);
            try
            {
                if (AtlasPoiyomiVersionDetector.Detect(material) != 10) Assert.Ignore("Installed Poiyomi Toon is not 10.x.");
                var untouched = new AtlasPoiyomi10Parser().Parse(material).ColorProcessing;
                Assert.AreEqual(1f, untouched.Saturation, 1e-5f);
                Assert.AreEqual(1f, untouched.BrightnessMultiplier, 1e-5f);
                Assert.AreEqual(AtlasTintMode.Perceptual, untouched.TintMode);
                Assert.AreEqual(0f, untouched.Tint.Color.a, 1e-5f); // tint is off by default

                material.SetColor("_MainTintColor", new Color(1f, 0f, 0f, 0.5f));
                Assert.AreEqual(0.5f, new AtlasPoiyomi10Parser().Parse(material).ColorProcessing.Tint.Color.a, 1e-5f);
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
