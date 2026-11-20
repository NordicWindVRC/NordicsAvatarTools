using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Glitter and sparkle (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasGlitterTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;
        private static AtlasOutputCapabilities ToonStandard => AtlasShaderRegistry.Output("mobiletoon").Capabilities;

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < materials.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource
                {
                    Material = materials[i], UVRegion = new Rect(0f, 0f, (materials.Length - i) / (float)materials.Length, 1f)
                });
            return job;
        }

        private static AtlasUnifiedMaterial Glittery(float density, float opacity = 1f, float size = 0.3f)
        {
            var material = new AtlasUnifiedMaterial();
            // Flat lighting: no highlights of its own, so the surface maps come from the glitter alone.
            material.Lighting.Family = AtlasLightingFamily.Flat;
            material.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Glitter, Opacity = opacity, Color = Color.white, Blend = AtlasBlend.Add, Placement = AtlasPlacement.Emission,
                Glitter = new AtlasGlitter { Density = density, Size = size, RandomPosition = 0f, MinBrightness = 3f, MaxBrightness = 3f }
            });
            return material;
        }

        private static Color Pixel(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index, int x, int y, int size = 16)
        {
            using (var result = plan.Maps.Single(m => m.Map == map).Evaluate(index, job.Sources[index].Material, size, size))
                return result.GetPixel(x, y);
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void NativeGlitterKeepsEachSourcesDensityOnAPreservedChannel()
        {
            var job = Job(Glittery(300f), Glittery(600f, 0.5f), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layer = plan.Output.Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreEqual(300f, layer.Glitter.Density, "the reference source's");
            int channel = layer.Glitter.Space - AtlasTextureSpace.UV0;
            Assert.GreaterOrEqual(channel, 1, Notes(plan));
            Assert.AreEqual(new Vector2(0.2f, 0.4f), plan.SourceRemaps[1].PreservedUVs.Single(p => p.Channel == channel).Convert(new Vector2(0.1f, 0.2f)),
                "twice the density: twice the UV");
            Assert.AreEqual(1f, layer.Opacity, 1e-5f);
            var mask = plan.Maps.Single(m => m.Map == AtlasMaskAllocator.Places[0]);
            Assert.AreEqual(1f, Pixel(plan, mask.Map, job, 0, 1, 1).r, 1e-4f);
            Assert.AreEqual(0.5f, Pixel(plan, mask.Map, job, 1, 1, 1).r, 1e-4f);
            Assert.AreEqual(0f, Pixel(plan, mask.Map, job, 2, 1, 1).r, 1e-4f);
            Assert.AreNotSame(job.Sources[0].Material.Layers[0], layer, "a copy");
            Assert.AreEqual(AtlasTextureSpace.UV0, ((AtlasPatternLayer)job.Sources[0].Material.Layers[0]).Glitter.Space, "the source is unchanged");

            var other = Glittery(300f);
            ((AtlasPatternLayer)other.Layers[0]).Glitter.Shape = AtlasGlitterShape.Square;
            plan = AtlasOutputPipeline.Resolve(Job(Glittery(300f), other), Poiyomi);
            StringAssert.Contains("differs and is left out", Notes(plan));
        }

        [Test]
        public void ToonStandardDrawsTheGlitterAsGlowingShinyPoints()
        {
            var job = Job(Glittery(2f));
            Assert.AreEqual("off", AtlasOutputPipeline.Resolve(job, ToonStandard).Modes[AtlasGlitterFeature.FeatureId].Id,
                "without native glitter, the default is off");
            var choice = new AtlasFeatureChoice { FeatureId = AtlasGlitterFeature.FeatureId, ModeId = "emulated" };
            job.Features.Add(choice);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual("emulated", plan.Modes[AtlasGlitterFeature.FeatureId].Id);
            var emission = AtlasEmissionFeature.Of(plan.Output);
            Assert.IsNotNull(emission, Notes(plan));
            Assert.AreEqual(3f, emission.Targets[0].Opacity, 1e-5f, "the glitter's brightness");
            // A particle at the cell's corner (no random position): pixel (0, 0) is on it.
            Assert.AreEqual(1f, Pixel(plan, AtlasVirtualMap.Emission, job, 0, 0, 0).r, 1e-3f);
            Assert.AreEqual(1f, Pixel(plan, AtlasVirtualMap.Metallic, job, 0, 0, 0).r, 1e-3f);
            Assert.AreEqual(0f, Pixel(plan, AtlasVirtualMap.Roughness, job, 0, 0, 0).r, 1e-3f);
            // Between particles: nothing.
            Assert.AreEqual(0f, Pixel(plan, AtlasVirtualMap.Emission, job, 0, 4, 4).r, 1e-3f);
            Assert.AreEqual(1f, Pixel(plan, AtlasVirtualMap.Roughness, job, 0, 4, 4).r, 1e-3f);
            StringAssert.Contains("drawn as still points", Notes(plan));

            // No glow: no emission.
            choice.Values[AtlasFeatureOption.ValueId(AtlasGlitterFeature.EmissionOption)] = (0f, "");
            plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.IsNull(AtlasEmissionFeature.Of(plan.Output));
            Assert.IsTrue(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Roughness));
        }

        [Test]
        public void TheWriterSetsTheGlitterTheParserReadsBack()
        {
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            var mask = new Texture2D(4, 4);
            created.Add(mask);
            var output = new AtlasUnifiedMaterial();
            output.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.Glitter, Opacity = 0.6f, Color = new Color(1f, 0.5f, 0.25f, 1f), Blend = AtlasBlend.Add, Placement = AtlasPlacement.Emission,
                Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = mask }, Channel = AtlasChannel.Blue, Invert = true },
                Glitter = new AtlasGlitter
                {
                    Mode = AtlasGlitterMode.Linear, Shape = AtlasGlitterShape.Square, Space = AtlasTextureSpace.UV2, Density = 450f, Size = 0.2f,
                    Layers = 2, MaxBrightness = 4f, MinBrightness = 0.5f, Speed = 0.5f, RandomColors = true
                }
            });
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Layers.OfType<AtlasPatternLayer>().Single(l => l.Kind == AtlasPatternKind.Glitter);
            var g = parsed.Glitter;
            Assert.AreEqual(AtlasGlitterMode.Linear, g.Mode);
            Assert.AreEqual(AtlasGlitterShape.Square, g.Shape);
            Assert.AreEqual(AtlasTextureSpace.UV2, g.Space);
            Assert.AreEqual(450f, g.Density, 1e-3f);
            Assert.AreEqual(0.2f, g.Size, 1e-4f);
            Assert.AreEqual(2, g.Layers);
            Assert.AreEqual(4f, g.MaxBrightness, 1e-4f);
            Assert.AreEqual(0.5f, g.MinBrightness, 1e-4f);
            Assert.AreEqual(0.5f, g.Speed, 1e-4f);
            Assert.IsTrue(g.RandomColors);
            Assert.AreEqual(0.6f, parsed.Opacity, 1e-3f);
            Assert.AreEqual(0.25f, parsed.Color.b, 1e-3f);
            Assert.AreSame(mask, parsed.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Blue, parsed.Mask.Channel);
            Assert.IsTrue(parsed.Mask.Invert);
        }
    }
}
