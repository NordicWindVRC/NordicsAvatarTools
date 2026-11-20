using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Mask levels and the mask bus baked (ATLAS_OUTPUT.md, "Masks").
    public sealed class AtlasMaskTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private AtlasTextureReference Gray(float value)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1f), 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return new AtlasTextureReference { Texture = texture, Interpretation = AtlasTextureInterpretation.Data };
        }

        private static float Channel(AtlasUnifiedMaterial source, int channel)
        {
            using (var map = AtlasMaskBusEvaluator.Channel(source, channel, 4, 4)) return map.GetPixel(1, 1).r;
        }

        private AtlasUnifiedMaterial Bus()
        {
            var source = new AtlasUnifiedMaterial();
            var steps = source.MaskBus.Steps;
            steps.Add(new AtlasMaskBusStep { Channel = 0, Kind = AtlasMaskBusStepKind.Texture, Mask = new AtlasMask { Map = Gray(0.8f) } });
            steps.Add(new AtlasMaskBusStep { Channel = 0, Kind = AtlasMaskBusStepKind.Offset, Amount = -0.3f });
            steps.Add(new AtlasMaskBusStep { Channel = 0, Kind = AtlasMaskBusStepKind.Remap, Min = 0.2f, Max = 0.6f });
            steps.Add(new AtlasMaskBusStep { Channel = 1, Kind = AtlasMaskBusStepKind.Mirror, Inside = true });
            steps.Add(new AtlasMaskBusStep { Channel = 2, Kind = AtlasMaskBusStepKind.Faces, Faces = AtlasFaces.Front });
            steps.Add(new AtlasMaskBusStep { Channel = 3, Kind = AtlasMaskBusStepKind.Faces, Faces = AtlasFaces.Back });
            steps.Add(new AtlasMaskBusStep { Channel = 4, Kind = AtlasMaskBusStepKind.Distance, Min = 1f, Max = 2f, MinValue = 0f, MaxValue = 0.3f });
            steps.Add(new AtlasMaskBusStep { Channel = 5, Kind = AtlasMaskBusStepKind.Contact, Contact = new AtlasContact(), Blend = AtlasMaskBlend.Multiply });
            steps.Add(new AtlasMaskBusStep { Channel = 6, Kind = AtlasMaskBusStepKind.Contact, Contact = new AtlasContact { Invert = true }, Blend = AtlasMaskBlend.Multiply });
            steps.Add(new AtlasMaskBusStep { Channel = 7, Kind = AtlasMaskBusStepKind.Blacklight, Blend = AtlasMaskBlend.Replace });
            steps.Add(new AtlasMaskBusStep { Channel = 8, Kind = AtlasMaskBusStepKind.SecondPass });
            return source;
        }

        [Test]
        public void BusChannelsRunTheirStepsAsNormallySeen()
        {
            var source = Bus();
            Assert.AreEqual(0.4f, Channel(source, 0), 1e-3f, "0.8, − 0.3, remapped to 0.2–0.6");
            Assert.AreEqual(0f, Channel(source, 1), 1e-3f, "only in mirrors");
            Assert.AreEqual(1f, Channel(source, 2), 1e-3f, "front faces");
            Assert.AreEqual(0f, Channel(source, 3), 1e-3f, "only back faces");
            Assert.AreEqual(0.3f, Channel(source, 4), 1e-3f, "far away: the far value");
            Assert.AreEqual(0f, Channel(source, 5), 1e-3f, "no touch");
            Assert.AreEqual(1f, Channel(source, 6), 1e-3f, "no touch, inverted");
            Assert.AreEqual(1f, Channel(source, 7), 1e-3f, "no blacklight: untouched");
            Assert.IsTrue(AtlasMaskBusEvaluator.CanBake(source, 0));
            Assert.IsFalse(AtlasMaskBusEvaluator.CanBake(source, 8), "the second pass isn't baked yet");
            Assert.IsTrue(AtlasMaskBusEvaluator.CanBake(source, 15), "an empty channel is 1");
        }

        [Test]
        public void BlendsArePoiyomisMaskBlend()
        {
            Assert.AreEqual(0.5f, AtlasMaskBusEvaluator.Blend(0.7f, 0.2f, AtlasMaskBlend.Subtract), 1e-5f);
            Assert.AreEqual(0.5f, AtlasMaskBusEvaluator.Blend(0.3f, 0.6f, AtlasMaskBlend.Divide), 1e-5f);
            Assert.AreEqual(0.45f, AtlasMaskBusEvaluator.Blend(0.3f, 0.6f, AtlasMaskBlend.Average), 1e-5f);
            Assert.AreEqual(1f, AtlasMaskBusEvaluator.Blend(0.8f, 0.5f, AtlasMaskBlend.Add), 1e-5f, "saturated");
            Assert.AreEqual(0f, AtlasMaskBusEvaluator.Blend(0.2f, 0.5f, AtlasMaskBlend.Subtract), 1e-5f, "saturated");
        }

        [Test]
        public void MasksApplyTheirLevelsAndReadTheBus()
        {
            var source = Bus();
            var levels = new AtlasMask { Map = Gray(0.5f), Levels = new AtlasMaskLevels { InputMin = 0.25f, InputMax = 0.75f, OutputMin = 0.2f, OutputMax = 0.6f } };
            Assert.IsTrue(AtlasMapEvaluator.CanEvaluate(levels));
            using (var map = AtlasMapEvaluator.ScaledMask(levels, 1f, 4, 4)) Assert.AreEqual(0.4f, map.GetPixel(1, 1).r, 1e-3f, "levels");

            var onBus = new AtlasMask { Bus = 0, BusBlend = AtlasMaskBlend.Replace };
            Assert.IsFalse(AtlasMapEvaluator.CanEvaluate(onBus), "the bus needs the material");
            Assert.IsTrue(AtlasMapEvaluator.CanEvaluate(onBus, source));
            using (var map = AtlasMapEvaluator.ScaledMask(onBus, 1f, 4, 4, source)) Assert.AreEqual(0.4f, map.GetPixel(1, 1).r, 1e-3f, "the channel");
            var multiplied = new AtlasMask { Map = Gray(0.5f), Bus = 0, BusBlend = AtlasMaskBlend.Multiply };
            using (var map = AtlasMapEvaluator.ScaledMask(multiplied, 1f, 4, 4, source)) Assert.AreEqual(0.2f, map.GetPixel(1, 1).r, 1e-3f, "0.5 × 0.4");
            Assert.IsFalse(AtlasMapEvaluator.CanEvaluate(new AtlasMask { Bus = 8 }, source));
        }

        [Test]
        public void ScalarsReadTheBusBeforeTheirResultIsInverted()
        {
            var source = Bus();
            var metallic = new AtlasScalarInput(0.8f) { Bus = 0, BusBlend = AtlasMaskBlend.Multiply };
            using (var map = AtlasMapEvaluator.Scalar(metallic, 4, 4, source)) Assert.AreEqual(0.32f, map.GetPixel(1, 1).r, 1e-3f, "0.8 × 0.4");
            var roughness = new AtlasScalarInput(0.8f) { Map = Gray(1f), Bus = 0, BusBlend = AtlasMaskBlend.Multiply, InvertResult = true };
            using (var map = AtlasMapEvaluator.Scalar(roughness, 4, 4, source)) Assert.AreEqual(0.68f, map.GetPixel(1, 1).r, 1e-3f, "smoothness 0.32");
        }

        [Test]
        public void LiveStepsAreReported()
        {
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial(), UVRegion = new Rect(0f, 0f, 0.5f, 1f) });
            job.Sources.Add(new AtlasGeneratorSource { Material = Bus(), UVRegion = new Rect(0.5f, 0f, 0.5f, 1f) });
            var plan = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("poiyomi10").Capabilities);
            Assert.IsTrue(plan.Notes.Any(n => n.StartsWith("Global masks that change live") && n.Contains("(source 2)")));
        }
    }
}
