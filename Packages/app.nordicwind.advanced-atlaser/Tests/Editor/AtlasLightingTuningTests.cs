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

        // A texture whose left half is unused and black, and whose right half is the mesh's gray.
        [Test]
        public void BaseColorsAreSampledWhereTheMeshUsesTheTexture()
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = x < size / 2 ? Color.black : new Color(0.5f, 0.5f, 0.5f, 1f);
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Map = new AtlasTextureReference { Texture = texture };
            Assert.Less(AtlasSourceSamples.Bases(material).Min(AtlasSourceSamples.Luminance), 0.01f, "the whole texture: the black half too");

            // The mesh's triangles, within the gray half.
            var mesh = new AtlasUVTriangles
            {
                UV = new[] { new Vector2(0.6f, 0.1f), new Vector2(0.95f, 0.1f), new Vector2(0.95f, 0.9f), new Vector2(0.6f, 0.9f) },
                Triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            AtlasSourceSamples.Cover(material, new[] { mesh });
            var used = AtlasSourceSamples.Bases(material);
            Assert.IsNotEmpty(used);
            foreach (var color in used) Assert.AreEqual(0.5f, color.r, 0.01f, "only what the mesh shows");

            AtlasSourceSamples.Cover(material, null);
            Assert.Less(AtlasSourceSamples.Bases(material).Min(AtlasSourceSamples.Luminance), 0.01f, "without triangles: the whole texture again");
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

        // A gray source whose own lighting is 1.5 times as bright as a plain one's.
        private static AtlasUnifiedMaterial Source(float level, float lightMultiplier = 1f)
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(level, level, level, 1f);
            material.Lighting.LightMultiplier = lightMultiplier;
            return material;
        }

        // Three sources lit by the first one's lighting: a plain one, and two whose own lighting
        // is 1.5 times as bright, one of them almost white.
        private static AtlasGeneratorJob Brighter(out AtlasUnifiedMaterial dim, out AtlasUnifiedMaterial nearWhite)
        {
            dim = Source(0.4f, 1.5f);
            nearWhite = Source(0.9f, 1.5f);
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = Source(0.3f), UVRegion = new Rect(0f, 0f, 0.5f, 1f) });
            job.Sources.Add(new AtlasGeneratorSource { Material = dim, UVRegion = new Rect(0.5f, 0f, 0.25f, 1f) });
            job.Sources.Add(new AtlasGeneratorSource { Material = nearWhite, UVRegion = new Rect(0.75f, 0f, 0.25f, 1f) });
            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasLightingFeature.FeatureId, ReferenceSource = 0 });
            return job;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;

        [Test]
        public void ASourceTheAtlasLightsDarkerGetsItsBaseColorScaledWithinTheMapsRange()
        {
            var job = Brighter(out var dim, out var nearWhite);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            // As if the output shader keeps the chosen lighting as it is.
            AtlasLevelCompensation.Apply(job, plan, plan.Output.Lighting);
            Assert.IsFalse(plan.LevelScales.ContainsKey(0), "the lighting is this source's own");
            Assert.AreEqual(1.5f, plan.LevelScales[1], 0.05f, "what its own lighting had over the atlas's");
            var placed = new Vector2Int(4, 4);
            using (var map = request.Evaluate(1, dim, 4, 4, placed))
                Assert.AreEqual(0.4f * plan.LevelScales[1], map.GetPixel(1, 1).r, 1e-3f);
            Assert.AreEqual(plan.LevelScales[1], plan.LevelApplied[1], 1e-4f, "applied in full");
            using (var map = request.Evaluate(2, nearWhite, 4, 4, placed))
                Assert.AreEqual(1f, map.GetPixel(1, 1).r, 1e-3f, "a raise stops where the brightest texel reaches 1");
            Assert.AreEqual(1f / 0.9f, plan.LevelApplied[2], 1e-3f);
            using (var map = request.Evaluate(0, job.Sources[0].Material, 4, 4, placed))
                Assert.AreEqual(0.3f, map.GetPixel(1, 1).r, 1e-4f, "the others keep their colors");
            var note = plan.Notes.Single(n => n.Contains("scaled"));
            StringAssert.Contains("source 2", note);
            StringAssert.Contains("source 3", note);
            StringAssert.DoesNotContain("source 1", note);

            // A source lit as it was needs no scale; one lit darker than its own gets one.
            Assert.AreEqual(1f, AtlasLevelCompensation.Scale(Source(0.3f), new AtlasLighting()));
            Assert.Less(AtlasLevelCompensation.Scale(Source(0.3f), new AtlasLighting { LightMultiplier = 1.5f }), 0.75f, "lit brighter: scaled down");
        }

        [Test]
        public void TurnedOffLevelCompensationLeavesTheBaseColors()
        {
            var job = Brighter(out var dim, out _);
            job.Features[0].Switches[AtlasLevelCompensation.SwitchId] = false;
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            AtlasLevelCompensation.Apply(job, plan, plan.Output.Lighting);
            Assert.AreEqual(2, plan.LevelScales.Count, "what it would do, for the inspector's control");
            Assert.IsFalse(request.Contributions.Any(c => c.Label == "level compensation"));
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("scaled")));
            using (var map = request.Evaluate(1, dim, 4, 4, new Vector2Int(4, 4)))
                Assert.AreEqual(0.4f, map.GetPixel(1, 1).r, 1e-4f);
        }

        [Test]
        public void AScaledSourceIsComparedAsTheAtlasShowsIt()
        {
            var job = Brighter(out _, out _);
            job.Sources.RemoveAt(2);
            var kept = new AtlasUnifiedMaterial();
            StringAssert.Contains("source 2", AtlasLook.LightingNotes(job, kept).Single());
            Assert.IsEmpty(AtlasLook.LightingNotes(job, kept, new Dictionary<int, float> { [1] = 1.5f }).ToList(),
                "scaled by what its lighting had over the atlas's, it looks as it did");
        }

        [Test]
        public void TheInspectorShowsLevelCompensationWhileASourceWouldBeScaled()
        {
            var shader = Installed("poiyomi10");
            var overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), shader, new Material[] { null, null },
                new[] { Source(0.3f), Source(0.4f, 1.5f) });
            var lighting = overview.Features.Single(s => s.Feature is AtlasLightingFeature);
            Assert.IsTrue(lighting.Switches.Any(s => s.Id == AtlasLevelCompensation.SwitchId));

            overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), shader, new Material[] { null, null },
                new[] { Untouched(shader), Untouched(shader) });
            lighting = overview.Features.Single(s => s.Feature is AtlasLightingFeature);
            Assert.IsFalse(lighting.Switches.Any(s => s.Id == AtlasLevelCompensation.SwitchId), "the shader's own lighting, kept as it is");
        }

        // A hard-edged ramp read by N·L: lit where N·L is above a half. Toon Standard reads its
        // ramp by half-Lambert, so the same keys would put the edge at N·L = 0.
        [Test]
        public void AGeneratedRampIsFittedToWhereTheChosenLightingPutsItsEdge()
        {
            var shader = Installed("mobiletoon");
            var source = Source(0.5f);
            source.Lighting.Family = AtlasLightingFamily.RampToon;
            var ramp = source.Lighting.Ramp;
            ramp.Term = AtlasShadingTerm.Lambert;
            ramp.Keys.Add(new AtlasRampKey(0f, Color.black));
            ramp.Keys.Add(new AtlasRampKey(0.49f, Color.black));
            ramp.Keys.Add(new AtlasRampKey(0.51f, Color.white));
            ramp.Keys.Add(new AtlasRampKey(1f, Color.white));
            var job = Job(source);
            var mapped = AtlasOutputPipeline.Resolve(job, shader.Capabilities).Output.Lighting;
            Assert.Greater(AtlasRampTexture.Evaluate(mapped.Ramp.Keys, 0.6f).r, 0.8f, "by its values: the edge at the middle of the ramp");

            var plan = AtlasOutputPipeline.Resolve(job, shader);
            var tuned = plan.Output.Lighting;
            Assert.AreEqual(AtlasRampTexture.Width, tuned.Ramp.Keys.Count, "a key per texel");
            Assert.Less(AtlasRampTexture.Evaluate(tuned.Ramp.Keys, 0.6f).r, 0.2f, "N·L = 0.2 is still in shadow");
            Assert.Greater(AtlasRampTexture.Evaluate(tuned.Ramp.Keys, 0.9f).r, 0.8f, "N·L = 0.8 is lit");
            Assert.IsTrue(AtlasLightingFeature.NeedsGeneratedRamp(plan.Output, shader.Capabilities), "the build makes the texture");
            StringAssert.Contains("shadow ramp", plan.Notes.Single(n => n.Contains("tuned")));
            using (var kept = AtlasKeptLighting.For(shader))
                Assert.Less(AtlasLightingResponse.Difference(source.Lighting, kept.Of(tuned), Gray),
                    AtlasLightingResponse.Difference(source.Lighting, kept.Of(mapped), Gray) - AtlasLightingTuning.Margin, "closer by the margin");
        }

        [Test]
        public void RampKeysOutOfOrderAreReadInPositionOrder()
        {
            var keys = new List<AtlasRampKey> { new AtlasRampKey(1f, Color.white), new AtlasRampKey(0f, Color.black) };
            Assert.AreEqual(0.25f, AtlasRampTexture.Evaluate(keys, 0.25f).r, 1e-5f);
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
