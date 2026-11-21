using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The look model (ATLAS_OUTPUT.md, "The look model"): a lighting's level and contrast in
    // the standard scenes, the sources whose own lighting looks different from the kept one,
    // and a layer's cost.
    public sealed class AtlasLookTests
    {
        private static readonly Color[] Gray = { new Color(0.5f, 0.5f, 0.5f, 1f) };

        [Test]
        public void ALightingsLookIsItsLevelAndContrastInAScene()
        {
            var bright = AtlasLook.Scenes[0];
            var dim = AtlasLook.Scenes[1];
            var unlit = AtlasLook.Lighting(new AtlasLighting { Family = AtlasLightingFamily.Unlit }, Gray, bright);
            Assert.AreEqual(Mathf.LinearToGammaSpace(0.5f), unlit.Level, 1e-4f, "unlit: the base color as displayed");
            Assert.AreEqual(0f, unlit.Contrast, 1e-5f, "the same from every angle to the light");

            var lighting = new AtlasLighting();
            var inBright = AtlasLook.Lighting(lighting, Gray, bright);
            var inDim = AtlasLook.Lighting(lighting, Gray, dim);
            Assert.Greater(inBright.Contrast, 0.1f, "the lit side shows brighter than the shadow side");
            Assert.Greater(inBright.Level, inDim.Level, "a dimmer scene shows darker");
        }

        // Probes of 0.5 on average, 0.25 more along their dominant direction, and no main light.
        [Test]
        public void WithoutAMainLightEachLightColorModelMakesItsLightOfTheProbes()
        {
            var probes = AtlasLook.Scenes.Single(s => s.Name == "probes-only");
            var lighting = new AtlasLighting { Ambient = AtlasAmbientModel.Average };
            void Light(float expectedDirect, float expectedAmbient, string message, float nDotL = 1f)
            {
                AtlasLightingResponse.Light(lighting, probes, nDotL, out float direct, out float ambient);
                Assert.AreEqual(expectedDirect, direct, 1e-5f, message + ": direct");
                Assert.AreEqual(expectedAmbient, ambient, 1e-5f, message + ": ambient");
            }
            Light(0f, 0.5f, "separate: no light but the ambient");

            lighting.Contributions.AmbientAsMainLight = 0.6f;
            lighting.Contributions.AmbientWithoutMainLight = 0.5f;
            Light(0.3f, 0.25f, "a share of the ambient light stands in for the main light");

            lighting.Contributions.AmbientAsMainLight = 1f;
            lighting.Contributions.AmbientWithoutMainLight = 1f;
            lighting.Contributions.AmbientStandIn = AtlasAmbientStandIn.Brightest;
            Light(0.75f, 0.5f, "the probes' brightest light stands in");

            lighting.ColorModel = AtlasLightColorModel.MergedProbes;
            Light((0.75f * 0.75f + 0.5f * 0.5f) / 1.25f, 0.5f, "merged: the brightest and the average, each by its share");

            lighting.ColorModel = AtlasLightColorModel.Floored;
            Light(0.5f, 0.5f, "floored: the direct light is at least the ambient");

            lighting.ColorModel = AtlasLightColorModel.SplitProbes;
            Light(0.5f + 0.25f * 2f / 3f, 0.5f - 0.25f * 2f / 3f, "split: along the dominant direction and against it");

            lighting.ColorModel = AtlasLightColorModel.Separate;
            lighting.Ambient = AtlasAmbientModel.Directional;
            lighting.Contributions.AmbientAsMainLight = 0f;
            Light(0f, 0.25f, "ambient along the normal, facing away from the probes' light", -1f);

            // With a main light, the separate model's direct light is the light itself.
            lighting.Contributions.AmbientAsMainLight = 1f;
            AtlasLightingResponse.Light(lighting, AtlasLook.Scenes[0], 1f, out float lit, out _);
            Assert.AreEqual(1f, lit, 1e-5f);

            // In the dark, the floored model's floor is the light.
            lighting.ColorModel = AtlasLightColorModel.Floored;
            AtlasLightingResponse.Light(lighting, AtlasLook.Scenes.Single(s => s.Name == "dark"), 1f, out float floor, out _);
            Assert.AreEqual(0.05f, floor, 1e-5f);
        }

        [Test]
        public void SourcesWhoseOwnLightingLooksDifferentFromTheKeptOneAreListed()
        {
            var plain = new AtlasUnifiedMaterial();
            var floored = new AtlasUnifiedMaterial();
            floored.Lighting.MinimumBrightness = 0.5f;
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = plain, UVRegion = new Rect(0f, 0f, 0.5f, 1f) });
            job.Sources.Add(new AtlasGeneratorSource { Material = floored, UVRegion = new Rect(0.5f, 0f, 0.5f, 1f) });
            // The output keeps the first source's lighting, without the second one's minimum brightness.
            var kept = new AtlasUnifiedMaterial();
            var notes = AtlasLook.LightingNotes(job, kept).ToList();
            Assert.AreEqual(1, notes.Count, "one line for the output");
            StringAssert.Contains("source 2", notes[0]);
            StringAssert.DoesNotContain("source 1", notes[0]);
            StringAssert.Contains("darker", notes[0]);

            floored.Lighting.MinimumBrightness = 0f;
            Assert.IsEmpty(AtlasLook.LightingNotes(job, kept).ToList(), "the same lighting: nothing to list");
            Assert.IsEmpty(AtlasLook.LightingNotes(job, null).ToList(), "no parser for the output shader: nothing to compare");
        }

        [Test]
        public void TheOverviewNamesTheMaterialsTheChosenLightingShowsDifferently()
        {
            var capabilities = AtlasShaderRegistry.Output("poiyomi10").Capabilities;
            var plain = new AtlasUnifiedMaterial();
            var floored = new AtlasUnifiedMaterial();
            floored.Lighting.MinimumBrightness = 0.5f;
            // Equal areas: automatic takes the first source's lighting.
            var overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), capabilities, new Material[] { null, null },
                new[] { plain, floored });
            var lighting = overview.Features.Single(s => s.Feature is AtlasLightingFeature);
            Assert.IsTrue(lighting.SharedValuesDiffer);
            StringAssert.Contains("source 2", lighting.SharedValueEffect);
            StringAssert.DoesNotContain("source 1", lighting.SharedValueEffect);

            overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), capabilities, new Material[] { null, null },
                new[] { new AtlasUnifiedMaterial(), new AtlasUnifiedMaterial() });
            Assert.IsNull(overview.Features.Single(s => s.Feature is AtlasLightingFeature).SharedValueEffect, "the same lighting: nothing to say");
        }

        [Test]
        public void ALayerShownDifferentlyCostsWhatItLosesPlusTwiceWhatItAdds()
        {
            // The source's layer brightens by 0.4.
            var weaker = new AtlasLayerCost();
            weaker.Add(0.4f, 0.3f);
            Assert.AreEqual(0.3f, weaker.Kept, 1e-6f);
            Assert.AreEqual(0.1f, weaker.Missing, 1e-6f);
            Assert.AreEqual(0f, weaker.Added, 1e-6f);
            Assert.AreEqual(0.4f, weaker.Impact, 1e-6f, "what leaving the layer out costs");
            Assert.IsTrue(weaker.BetterThanLeftOut, "a weaker effect is still the effect");

            var stronger = new AtlasLayerCost();
            stronger.Add(0.4f, 0.7f);
            Assert.AreEqual(0.4f, stronger.Kept, 1e-6f);
            Assert.AreEqual(0.3f, stronger.Added, 1e-6f);
            Assert.AreEqual(0.6f, stronger.Cost, 1e-6f, "what it adds counts double");
            Assert.IsFalse(stronger.BetterThanLeftOut, "it adds more than half of what it keeps");

            var opposite = new AtlasLayerCost();
            opposite.Add(0.4f, -0.2f);
            Assert.AreEqual(0f, opposite.Kept, 1e-6f, "darkening where the source brightens keeps nothing");
            Assert.AreEqual(0.4f, opposite.Missing, 1e-6f);
            Assert.AreEqual(0.2f, opposite.Added, 1e-6f);

            var colors = new AtlasLayerCost();
            colors.Add(new Color(0.3f, 0.3f, 0.3f), new Color(0.3f, 0.3f, 0.3f));
            Assert.AreEqual(0.3f, colors.Kept, 1e-6f, "the mean of the channels");
            Assert.AreEqual(0f, colors.Cost, 1e-6f, "the same effect costs nothing");
        }
    }
}
