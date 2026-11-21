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
