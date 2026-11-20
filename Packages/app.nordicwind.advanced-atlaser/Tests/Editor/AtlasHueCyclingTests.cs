using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Hue cycling (ATLAS_OUTPUT.md, "Feature inventory"): hue shifts with a speed.
    public sealed class AtlasHueCyclingTests
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

        // A hue shift of `start` turns cycling at `rate` turns per second.
        private static AtlasColorAdjustment Cycle(AtlasUnifiedMaterial material, AtlasAdjustmentTarget target, float start, float rate, AtlasLayer layer = null)
        {
            var adjustment = new AtlasColorAdjustment
            {
                Target = target, Layer = layer, Operation = AtlasColorOperation.Hue, Amount = start, HueMethod = AtlasHueMethod.HSV
            };
            material.ColorAdjustments.Add(adjustment);
            var turn = new AtlasModulator
            {
                Target = "colorAdjustments[" + (material.ColorAdjustments.Count - 1) + "].amount",
                Source = AtlasModulatorSource.Time, Mode = AtlasModulatorMode.Add, Min = 0f, Max = 1f
            };
            turn.Time.Waveform = rate >= 0f ? AtlasWaveform.Saw : AtlasWaveform.ReverseSaw;
            turn.Time.Frequency = Mathf.Abs(rate);
            material.Modulators.Add(turn);
            return adjustment;
        }

        private static AtlasUnifiedMaterial Red()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = Color.red;
            return material;
        }

        private static float Evaluate(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index, int channel = 0)
        {
            using (var result = plan.Maps.Single(m => m.Map == map).Evaluate(index, job.Sources[index].Material, 4, 4))
                return result.GetPixel(1, 1)[channel];
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void ACyclingHueIsBakedAtItsStart()
        {
            var material = Red();
            Cycle(material, AtlasAdjustmentTarget.BaseColor, 1f / 3f, 0.5f);
            Assert.IsTrue(AtlasHueCycling.IsCycling(material, 0));
            Assert.AreEqual(0.5f, AtlasHueCycling.Rate(AtlasHueCycling.Turn(material, 0)), 1e-5f);
            using (var color = AtlasColorStack.Evaluate(material, 4, 4, true))
            {
                var c = color.GetPixel(1, 1);
                Assert.Greater(c.g, 0.9f, "red turned a third: green");
                Assert.Less(c.r, 0.1f);
            }
        }

        [Test]
        public void TheBaseColorCyclesWhereItsSourcesDidAtTheReferenceRate()
        {
            var a = Red();
            Cycle(a, AtlasAdjustmentTarget.BaseColor, 0f, 0.5f);
            var b = Red();
            Cycle(b, AtlasAdjustmentTarget.BaseColor, 0.25f, 0.5f);
            var faster = Red();
            Cycle(faster, AtlasAdjustmentTarget.BaseColor, 0f, 2f);
            var job = Job(a, b, new AtlasUnifiedMaterial(), faster);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var cycling = AtlasHueCycling.Of(plan.Output, AtlasAdjustmentTarget.BaseColor).Single();
            Assert.AreEqual(0f, cycling.Adjustment.Amount, "the starts are baked");
            Assert.AreEqual(0.5f, AtlasHueCycling.Rate(cycling.Turn), 1e-5f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.HueCyclingMask, job, 0), 1e-4f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.HueCyclingMask, job, 1), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.HueCyclingMask, job, 2), 1e-4f, "doesn't cycle");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.HueCyclingMask, job, 3), 1e-4f, "another rate: still");
            StringAssert.Contains("turns at another rate", Notes(plan));

            StringAssert.Contains("has no hue cycling", Notes(AtlasOutputPipeline.Resolve(job, ToonStandard)));
        }

        [Test]
        public void ACyclingEmissionGetsAnEmissionSlotOfItsOwn()
        {
            var material = new AtlasUnifiedMaterial();
            var glow = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            glow.Source.Color = Color.red;
            glow.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
            material.Layers.Add(glow);
            Cycle(material, AtlasAdjustmentTarget.Layer, 0f, 0.25f, glow);
            var job = Job(material, new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var slot = AtlasEmissionFeature.CyclingOf(plan.Output).Single();
            Assert.AreEqual(0.25f, AtlasHueCycling.Rate(AtlasHueCycling.LayerHue(plan.Output, slot).Turn), 1e-5f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.EmissionCycle1, job, 0), 1e-4f, "red");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.EmissionCycle1, job, 1), 1e-4f);
            Assert.IsNull(AtlasEmissionFeature.Of(plan.Output), "nothing else glows");

            var toon = AtlasOutputPipeline.Resolve(job, ToonStandard);
            StringAssert.Contains("stays at its start", Notes(toon));
            Assert.IsNotNull(AtlasEmissionFeature.Of(toon.Output), "baked at its start");
        }

        [Test]
        public void TheWriterSetsTheCyclingTheParserReadsBack()
        {
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            var texture = new Texture2D(4, 4);
            created.Add(texture);
            var output = new AtlasUnifiedMaterial();
            var color = Cycle(output, AtlasAdjustmentTarget.BaseColor, 0f, 0.5f);
            color.Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = texture }, Channel = AtlasChannel.Red };
            var final = Cycle(output, AtlasAdjustmentTarget.FinalColor, 0f, -0.25f);
            final.Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = texture }, Channel = AtlasChannel.Green };
            var glow = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            glow.Source.Map = new AtlasTextureReference { Texture = texture };
            glow.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 2f));
            output.Layers.Add(glow);
            Cycle(output, AtlasAdjustmentTarget.Layer, 0f, 1f, glow);
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material);

            var parsedColor = AtlasHueCycling.Of(parsed, AtlasAdjustmentTarget.BaseColor).Single();
            Assert.AreEqual(0.5f, AtlasHueCycling.Rate(parsedColor.Turn), 1e-4f);
            Assert.AreEqual(AtlasHueMethod.HSV, parsedColor.Adjustment.HueMethod);
            Assert.AreSame(texture, parsedColor.Adjustment.Mask?.Map?.Texture);
            var parsedFinal = AtlasHueCycling.Of(parsed, AtlasAdjustmentTarget.FinalColor).Single();
            Assert.AreEqual(-0.25f, AtlasHueCycling.Rate(parsedFinal.Turn), 1e-4f);
            Assert.AreEqual(AtlasChannel.Green, parsedFinal.Adjustment.Mask.Channel);
            var emission = parsed.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission)).ToList();
            var cycling = emission.Select(l => AtlasHueCycling.Of(parsed, AtlasAdjustmentTarget.Layer, l).FirstOrDefault()).Where(c => c.Turn != null).ToList();
            Assert.AreEqual(1, cycling.Count, "emission 2 cycles");
            Assert.AreEqual(1f, AtlasHueCycling.Rate(cycling[0].Turn), 1e-4f);
        }
    }
}
