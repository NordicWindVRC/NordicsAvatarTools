using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Subsurface scattering and proximity color (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasEffectTests
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

        private static Color Pixel(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index)
        {
            using (var result = plan.Maps.Single(m => m.Map == map).Evaluate(index, job.Sources[index].Material, 4, 4))
                return result.GetPixel(1, 1);
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        private static AtlasUnifiedMaterial Subsurface(Color color, float intensity, float offset)
        {
            var material = new AtlasUnifiedMaterial();
            var layer = new AtlasSubsurfaceLayer { Intensity = intensity, ThicknessOffset = offset, Light = AtlasSubsurfaceLight.Direct };
            layer.Color.Color = color;
            material.Layers.Add(layer);
            return material;
        }

        [Test]
        public void SubsurfaceColorAndThicknessArePerSource()
        {
            var job = Job(Subsurface(Color.red, 2f, 0f), Subsurface(Color.green, 1f, -0.5f), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layer = plan.Output.Layers.OfType<AtlasSubsurfaceLayer>().Single();
            Assert.AreEqual(2f, layer.Intensity, 1e-5f, "the strongest");
            Assert.AreEqual(-0.5f, layer.ThicknessOffset, 1e-5f, "the smallest offset");
            Assert.AreEqual(1f, Pixel(plan, AtlasVirtualMap.SubsurfaceColor, job, 0).r, 1e-4f);
            Assert.AreEqual(0.5f, Pixel(plan, AtlasVirtualMap.SubsurfaceColor, job, 1).g, 1e-4f, "half the strongest");
            Assert.AreEqual(0f, Pixel(plan, AtlasVirtualMap.SubsurfaceColor, job, 2).r, 1e-4f, "none: black");
            Assert.AreEqual(0.5f, Pixel(plan, AtlasVirtualMap.SubsurfaceThickness, job, 0).r, 1e-4f, "its offset 0 against −0.5");
            Assert.AreEqual(0f, Pixel(plan, AtlasVirtualMap.SubsurfaceThickness, job, 1).r, 1e-4f);
            StringAssert.Contains("has no subsurface scattering", Notes(AtlasOutputPipeline.Resolve(job, ToonStandard)));
        }

        private static AtlasUnifiedMaterial Proximity(Color near, Color far, float distance)
        {
            var material = new AtlasUnifiedMaterial();
            material.ColorAdjustments.Add(new AtlasColorAdjustment
            {
                Target = AtlasAdjustmentTarget.FinalColor, Operation = AtlasColorOperation.Tint, Amount = 1f, Color = near
            });
            var modulator = new AtlasModulator
            {
                Target = "colorAdjustments[0].color", Source = AtlasModulatorSource.CameraDistance, Mode = AtlasModulatorMode.BlendToColor, Color = far
            };
            modulator.CameraDistance.Near = 0.1f;
            modulator.CameraDistance.Far = distance;
            material.Modulators.Add(modulator);
            return material;
        }

        [Test]
        public void ProximityColorCoversTheAtlasFromTheReferenceSource()
        {
            var job = Job(Proximity(Color.black, Color.white, 1f), Proximity(Color.black, Color.white, 2f), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var tints = AtlasProximityColorFeature.Of(plan.Output);
            Assert.AreEqual(1, tints.Count, Notes(plan));
            Assert.AreEqual(1f, tints[0].Modulator.CameraDistance.Far, 1e-5f, "the larger area's");
            StringAssert.Contains("proximity color of source 2 differs", Notes(plan));
            StringAssert.Contains("covers the whole atlas, source 3 included", Notes(plan));
            Assert.IsFalse(plan.Notes.Any(n => n.StartsWith("Final color adjustments")), Notes(plan));
            StringAssert.Contains("has no proximity color", Notes(AtlasOutputPipeline.Resolve(job, ToonStandard)));
        }

        [Test]
        public void TheWriterSetsTheEffectsTheParserReadsBack()
        {
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            var texture = new Texture2D(4, 4);
            created.Add(texture);
            var output = Proximity(new Color(0.2f, 0f, 0f, 1f), Color.white, 1.5f);
            var sss = new AtlasSubsurfaceLayer
            {
                Thickness = new AtlasTextureReference { Texture = texture }, ThicknessChannel = AtlasChannel.Alpha, ThicknessOffset = -0.3f,
                Intensity = Mathf.PI, Power = 3f, Distortion = 0.5f, Light = AtlasSubsurfaceLight.Direct, ShadowStrength = 0.4f
            };
            sss.Color.Map = sss.Thickness;
            sss.Color.Color = new Color(1f, 0.5f, 0.5f, 1f);
            output.Layers.Add(sss);
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material);

            var layer = parsed.Layers.OfType<AtlasSubsurfaceLayer>().Single();
            Assert.AreSame(texture, layer.Thickness?.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, layer.ThicknessChannel);
            Assert.AreEqual(-0.3f, layer.ThicknessOffset, 1e-4f);
            Assert.AreEqual(Mathf.PI, layer.Intensity, 1e-3f);
            Assert.AreEqual(3f, layer.Power, 1e-4f);
            Assert.AreEqual(0.5f, layer.Distortion, 1e-4f);
            Assert.AreEqual(0.4f, layer.ShadowStrength, 1e-4f);
            Assert.AreEqual(0.5f, layer.Color.Color.g, 1e-3f);

            var tint = AtlasProximityColorFeature.Tints(parsed).Single();
            Assert.AreEqual(AtlasAdjustmentTarget.FinalColor, tint.Adjustment.Target);
            Assert.AreEqual(0.2f, tint.Adjustment.Color.r, 1e-3f);
            Assert.AreEqual(1.5f, tint.Modulator.CameraDistance.Far, 1e-4f);
            Assert.AreEqual(0.1f, tint.Modulator.CameraDistance.Near, 1e-4f);
        }
    }
}
