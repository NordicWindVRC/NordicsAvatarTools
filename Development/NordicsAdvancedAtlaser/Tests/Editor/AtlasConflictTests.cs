using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // What the output can't give every source is decided by the user or reported
    // (ATLAS_OUTPUT.md, "Decisions": no result left to chance).
    public sealed class AtlasConflictTests
    {
        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] sources)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < sources.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource { Material = sources[i], UVRegion = new Rect(i / (float)sources.Length, 0f, 1f / sources.Length, 1f) });
            return job;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;

        private static AtlasUnifiedMaterial Cutout(bool alphaToCoverage)
        {
            var material = new AtlasUnifiedMaterial();
            material.Render.SurfaceMode = AtlasSurfaceMode.Cutout;
            material.Render.AlphaToCoverage = alphaToCoverage;
            return material;
        }

        [Test]
        public void AlphaToCoverageIsTheUsersChoiceOnlyWhereTheCutoutSourcesDisagree()
        {
            var feature = new AtlasSurfaceModeFeature();
            var agreeing = Job(Cutout(false), Cutout(false));
            Assert.IsEmpty(feature.ConflictingSwitches(agreeing, Poiyomi));
            Assert.IsFalse(AtlasOutputPipeline.Resolve(agreeing, Poiyomi).Output.Render.AlphaToCoverage, "agreed: off");

            var job = Job(Cutout(true), Cutout(false));
            Assert.AreSame(AtlasSurfaceModeFeature.AlphaToCoverage, feature.ConflictingSwitches(job, Poiyomi).Single());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.IsTrue(plan.Output.Render.AlphaToCoverage, "the default");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("alpha to coverage") && n.Contains("on for all")));

            var config = new AtlasOutputConfiguration();
            config.features.Add(new AtlasFeatureSetting
            {
                featureId = AtlasSurfaceModeFeature.FeatureId,
                values = { new AtlasSettingValue { id = AtlasSurfaceModeFeature.AlphaToCoverage.Id, toggle = false } }
            });
            AtlasOutputPipeline.AddChoices(config, new Material[2], job);
            Assert.IsFalse(AtlasOutputPipeline.Resolve(job, Poiyomi).Output.Render.AlphaToCoverage, "the user's choice");

            // A source whose alpha is turned off is solid, so it doesn't count.
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "alpha", DisabledSources = { 1 } });
            Assert.IsEmpty(feature.ConflictingSwitches(job, Poiyomi));
        }

        [Test]
        public void ColorsAboveOneAreKeptByTheSharedColor()
        {
            var bright = new AtlasUnifiedMaterial();
            bright.Surface.BaseColor.Color = new Color(2f, 1f, 1f, 1f);
            var plain = new AtlasUnifiedMaterial();
            plain.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var job = Job(bright, plain);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            using (var map = AtlasMapBaker.Bake(request, job, 64, 32))
            {
                Assert.AreEqual(2f, plan.Output.Surface.BaseColor.Color.r, 1e-3f, "the brightest value");
                Assert.AreEqual(2f, AtlasColorFeature.Peak(map) * 2f, 1e-3f, "the map is at most 1");
                var left = map.GetPixel(16, 16);
                Assert.AreEqual(1f, left.r, 1e-3f);
                Assert.AreEqual(0.5f, left.g, 1e-3f);
                Assert.AreEqual(0.25f, map.GetPixel(48, 16).r, 1e-3f, "0.5 / 2");
            }

            bright.Surface.BaseColor.Color = new Color(0.8f, 1f, 1f, 1f);
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            using (var map = AtlasMapBaker.Bake(plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor), job, 64, 32))
            {
                Assert.AreEqual(Color.white, plan.Output.Surface.BaseColor.Color, "nothing above 1: white");
                Assert.AreEqual(0.8f, map.GetPixel(16, 16).r, 1e-3f);
            }
        }

        private static AtlasUnifiedMaterial Lit(float highlights)
        {
            var material = new AtlasUnifiedMaterial();
            material.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            material.Surface.Response.Highlights.Enabled = true;
            material.Surface.Response.Highlights.Strength = highlights;
            material.Surface.Response.Reflections.Enabled = true;
            material.Surface.Response.Reflections.Strength = 1f;
            return material;
        }

        [Test]
        public void WhatTheOutputCantKeepIsReported()
        {
            var job = Job(Lit(1f), Lit(0.5f));
            var toon = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.IsTrue(toon.Notes.Any(n => n.Contains("weaker highlights of source 2")), "Toon Standard has no highlight strength");
            Assert.IsFalse(toon.Notes.Any(n => n.Contains("weaker reflections")), "equal reflections");
            var poiyomi = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.IsFalse(poiyomi.Notes.Any(n => n.Contains("weaker")), "Poiyomi keeps them in masks");
            Assert.IsFalse(poiyomi.Notes.Any(n => n.Contains("lit differently")), "the sources light alike");
        }
    }
}
