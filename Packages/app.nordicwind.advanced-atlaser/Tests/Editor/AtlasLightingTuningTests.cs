using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Tuning the lighting by look (ATLAS_OUTPUT.md, "Tuning the lighting"): the lighting an
    // output shader keeps, read without an atlas's material, and the values searched where it
    // looks different from the chosen lighting.
    public sealed class AtlasLightingTuningTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static IAtlasOutputShader Installed(string id)
        {
            var shader = AtlasShaderRegistry.InstalledOutputShaders.FirstOrDefault(s => s.ShaderId == id);
            if (shader == null) Assert.Ignore(id + " isn't installed.");
            return shader;
        }

        private static AtlasGeneratorJob Job(AtlasUnifiedMaterial source)
        {
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = source, UVRegion = new Rect(0f, 0f, 1f, 1f) });
            return job;
        }

        // A material of the shader as it is created, read by its parser.
        private AtlasUnifiedMaterial Untouched(IAtlasOutputShader shader)
        {
            var material = new Material(shader.FindShader());
            created.Add(material);
            return AtlasShaderRegistry.Detect(material).Parse(material);
        }

        private static readonly Color[] Gray = { new Color(0.5f, 0.5f, 0.5f, 1f) };

        [Test]
        public void TheLimitClampsEachLightBeforeTheMultiplierOrTheShadedLight()
        {
            var bright = AtlasLook.Scenes[0];
            var lighting = new AtlasLighting { LimitBrightness = true, BrightnessCeiling = 1f };
            var lit = AtlasLightingResponse.Lit(lighting, Gray[0], 1f, bright);
            Assert.AreEqual(0.5f * 1.3f, lit.r, 1e-5f, "each light within the ceiling: the lit side passes it");

            lighting.LimitTotal = true;
            lit = AtlasLightingResponse.Lit(lighting, Gray[0], 1f, bright);
            Assert.AreEqual(0.5f, lit.r, 1e-5f, "the shaded light at the ceiling");
            lighting.MinimumBrightness = 0.1f;
            lit = AtlasLightingResponse.Lit(lighting, Gray[0], 1f, bright);
            Assert.AreEqual(0.55f, lit.r, 1e-5f, "the minimum brightness is added after the limit");

            // A main light of 2: limited to 1, then × 1.5.
            var strong = new AtlasLookScene("strong", 2f, 0f);
            var multiplied = new AtlasLighting { LimitBrightness = true, LightMultiplier = 1.5f };
            AtlasLightingResponse.Light(multiplied, strong, 1f, out float direct, out _);
            Assert.AreEqual(1.5f, direct, 1e-5f, "the multiplier applies to the limited light");

            // The minimum light raises both light colors.
            var floored = new AtlasLighting { MinimumLight = 0.2f };
            AtlasLightingResponse.Light(floored, AtlasLook.Scenes.Single(s => s.Name == "dark"), 1f, out direct, out float ambient);
            Assert.AreEqual(0.2f, direct, 1e-5f);
            Assert.AreEqual(0.2f, ambient, 1e-5f);
        }

        [Test]
        public void TheKeptLightingIsTheWrittenMaterialsLighting()
        {
            foreach (string id in new[] { "poiyomi10", "mobiletoon" })
            {
                var shader = AtlasShaderRegistry.InstalledOutputShaders.FirstOrDefault(s => s.ShaderId == id);
                if (shader == null) continue;
                var output = new AtlasUnifiedMaterial();
                output.Lighting.Family = AtlasLightingFamily.RampToon;
                output.Lighting.Ramp.ShadowLift = 0.2f;
                output.Lighting.MinimumBrightness = 0.05f;
                output.Surface.BaseColor.Color = new Color(0.8f, 0.6f, 0.4f, 1f);
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(output, material);
                var written = AtlasKeptMaterial.Read(material).Lighting;
                using (var kept = AtlasKeptLighting.For(shader))
                {
                    Assert.IsNotNull(kept, id);
                    var read = kept.Of(output.Lighting);
                    Assert.IsEmpty(AtlasFieldwise.Differences(AtlasLightingFeature.Canonical(written), AtlasLightingFeature.Canonical(read)), id);
                    // Asked again with another floor, it reads that one.
                    output.Lighting.MinimumBrightness = 0.09f;
                    var again = kept.Of(output.Lighting);
                    Assert.IsNotEmpty(AtlasFieldwise.Differences(AtlasLightingFeature.Canonical(read), AtlasLightingFeature.Canonical(again)), id);
                }
            }
            Assert.IsNull(AtlasKeptLighting.For(null), "no shader: nothing to read");
        }

        [Test]
        public void AnOutputOfTheSourcesOwnShaderKeepsTheMappedLighting()
        {
            foreach (string id in new[] { "poiyomi10", "mobiletoon" })
            {
                var shader = AtlasShaderRegistry.InstalledOutputShaders.FirstOrDefault(s => s.ShaderId == id);
                if (shader == null) continue;
                var job = Job(Untouched(shader));
                var mapped = AtlasOutputPipeline.Resolve(job, shader.Capabilities).Output.Lighting;
                var plan = AtlasOutputPipeline.Resolve(job, shader);
                Assert.IsEmpty(AtlasFieldwise.Differences(mapped, plan.Output.Lighting), id);
                Assert.IsFalse(plan.Notes.Any(n => n.Contains("tuned")), id);
            }
        }

        // A flat-lit source that takes its light from the probes' brightest direction where a
        // world has no main light, as Toon Standard does. Poiyomi's Standard light color, the
        // mapping by values, takes 60% of the ambient light there; its Poi Custom light color
        // merges the probes' brightest light in, which looks closer.
        [Test]
        public void ALightColorModelThatLooksCloserReplacesTheMappedOne()
        {
            var shader = Installed("poiyomi10");
            var source = new AtlasUnifiedMaterial();
            source.Surface.BaseColor.Color = Gray[0];
            source.Lighting.Family = AtlasLightingFamily.Flat;
            source.Lighting.Ambient = AtlasAmbientModel.Average;
            source.Lighting.Contributions.AmbientAsMainLight = 1f;
            source.Lighting.Contributions.AmbientStandIn = AtlasAmbientStandIn.Brightest;
            var job = Job(source);

            float Difference(AtlasLighting lighting)
            {
                using (var kept = AtlasKeptLighting.For(shader))
                    return AtlasLightingResponse.Difference(source.Lighting, kept.Of(lighting), Gray);
            }
            var mapped = AtlasOutputPipeline.Resolve(job, shader.Capabilities).Output.Lighting;
            var plan = AtlasOutputPipeline.Resolve(job, shader);
            var tuned = plan.Output.Lighting;
            Assert.AreEqual(AtlasLightColorModel.Separate, mapped.ColorModel, "by its values");
            Assert.AreNotEqual(AtlasLightColorModel.Separate, tuned.ColorModel, "by its look");
            Assert.Less(Difference(tuned), Difference(mapped) - AtlasLightingTuning.Margin, "closer by the margin");
            Assert.AreEqual(1, plan.Notes.Count(n => n.Contains("tuned")), "one line");
            StringAssert.Contains("light color model", plan.Notes.Single(n => n.Contains("tuned")));
            Assert.AreEqual(AtlasLightingFamily.Flat, tuned.Family, "the family is the artist's");
        }

        [Test]
        public void TuningNeverLeavesTheLightingFartherFromTheChosenOne()
        {
            foreach (string id in new[] { "poiyomi10", "mobiletoon" })
            {
                var shader = AtlasShaderRegistry.InstalledOutputShaders.FirstOrDefault(s => s.ShaderId == id);
                if (shader == null) continue;
                foreach (var family in new[] { AtlasLightingFamily.Flat, AtlasLightingFamily.RampToon, AtlasLightingFamily.PhysicallyBased })
                {
                    var source = new AtlasUnifiedMaterial();
                    source.Surface.BaseColor.Color = Gray[0];
                    source.Lighting.Family = family;
                    source.Lighting.LightMultiplier = 1.4f;
                    source.Lighting.MinimumLight = 0.3f;
                    source.Lighting.ShadowStrength = 0.8f;
                    var job = Job(source);
                    var mapped = AtlasOutputPipeline.Resolve(job, shader.Capabilities).Output.Lighting;
                    var tuned = AtlasOutputPipeline.Resolve(job, shader).Output.Lighting;
                    using (var kept = AtlasKeptLighting.For(shader))
                    {
                        float before = AtlasLightingResponse.Difference(source.Lighting, kept.Of(mapped), Gray);
                        float after = AtlasLightingResponse.Difference(source.Lighting, kept.Of(tuned), Gray);
                        Assert.LessOrEqual(after, before + 1e-4f, $"{id}, {family}");
                    }
                }
            }
        }
    }
}
