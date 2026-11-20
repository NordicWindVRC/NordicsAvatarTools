using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The base color stack (ATLAS_OUTPUT.md, "Feature inventory", Color): the color, color layers and static
    // adjustments baked into the base color.
    public sealed class AtlasColorStackTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private Texture2D Solid(Color color, int size = 4)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static Color Evaluate(AtlasUnifiedMaterial material, bool multiplyColor = true)
        {
            using (var map = AtlasColorStack.Evaluate(material, 4, 4, multiplyColor)) return map.GetPixel(1, 1);
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, message + " r");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, message + " g");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, message + " b");
        }

        // An emission that takes the base color reads it after the surface matcaps and rims, as
        // Poiyomi applies them; a bake takes them at their average look.
        [Test]
        public void AnEmissionTakingTheBaseColorSeesTheSurfaceMatcapsAndRimsOnAverage()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(0.01f, 0.01f, 0.01f, 1f);
            var emission = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission, SourceTimesBaseColor = true };
            emission.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
            material.Layers.Add(emission);
            float Glow()
            {
                using (var map = AtlasColorStack.EvaluateEmission(material, 4, 4, 1f)) return map.GetPixel(1, 1).r;
            }
            Assert.AreEqual(0.01f, Glow(), 1e-4f, "the base color alone");

            // A matcap screened on the surface; without a texture, its color everywhere.
            var matcap = new AtlasLookupLayer { Placement = AtlasPlacement.Surface, Blend = AtlasBlend.Screen };
            matcap.Source.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            material.Layers.Insert(0, matcap);
            float screened = 1f - 0.99f * 0.5f;
            Assert.AreEqual(screened, Glow(), 1e-3f, "1 − (1 − 0.01)(1 − 0.5)");

            // A rim added on the surface: its strength averaged over the visible surface.
            var rim = new AtlasAngleLayer { Placement = AtlasPlacement.Surface, Profile = AtlasAngleProfile.Band, Width = 0.3f, Softness = 0.1f, Blend = AtlasBlend.Add };
            rim.Color.Color = Color.white;
            float coverage = AtlasViewLayers.Coverage(rim);
            Assert.Greater(coverage, 0f);
            Assert.Less(coverage, 0.3f, "only near the edges");
            material.Layers.Insert(1, rim);
            Assert.AreEqual(screened + coverage, Glow(), 1e-3f);

            // Matcaps added after lighting don't change the base color.
            matcap.Placement = AtlasPlacement.Overlay;
            Assert.AreEqual(0.01f + coverage, Glow(), 1e-3f);
        }

        private static AtlasSurfaceLayer ColorLayer(Color color, AtlasBlend blend, float opacity)
        {
            var layer = new AtlasSurfaceLayer();
            layer.Source.Color = color;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, blend, opacity));
            return layer;
        }

        [Test]
        public void TheColorIsSharedWhenEverySourceHasItAndNothingIsLayered()
        {
            var job = new AtlasGeneratorJob();
            foreach (var x in new[] { 0f, 0.5f })
            {
                var material = new AtlasUnifiedMaterial();
                material.Surface.BaseColor.Color = new Color(2f, 0.5f, 0.5f, 1f);
                job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(x, 0f, 0.5f, 1f) });
            }
            var capabilities = AtlasShaderRegistry.Output("poiyomi10").Capabilities;
            var plan = AtlasOutputPipeline.Resolve(job, capabilities);
            Assert.AreEqual(new Color(2f, 0.5f, 0.5f, 1f), plan.Output.Surface.BaseColor.Color, "shared, above 1 too");
            using (var map = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, job.Sources[0].Material, 4, 4))
                AssertColor(Color.white, map.GetPixel(1, 1), "not multiplied in");

            job.Sources[1].Material.Surface.BaseColor.Color = Color.white;
            plan = AtlasOutputPipeline.Resolve(job, capabilities);
            Assert.AreEqual(Color.white, plan.Output.Surface.BaseColor.Color, "different colors: white");
            using (var map = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, job.Sources[0].Material, 4, 4))
                AssertColor(new Color(2f, 0.5f, 0.5f, 1f), map.GetPixel(1, 1), "multiplied in");
        }

        [Test]
        public void ColorLayersBlendByTheirModeOpacityAndMask()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(1f, 0.5f, 0f, 1f);
            material.Layers.Add(ColorLayer(new Color(1f, 0f, 0f, 1f), AtlasBlend.Multiply, 0.5f));
            Assert.IsTrue(AtlasColorStack.HasStack(material));
            // b × lerp(1, l, o)
            AssertColor(new Color(1f, 0.25f, 0f), Evaluate(material), "multiply at half");

            material.Layers[0] = ColorLayer(new Color(1f, 0f, 0f, 1f), AtlasBlend.TintLuminance, 1f);
            // luminance(1, 0.5, 0) = 0.3 + 0.295 = 0.595, × red
            AssertColor(new Color(0.595f, 0f, 0f), Evaluate(material), "tint luminance");

            material.Layers[0] = ColorLayer(new Color(0.25f, 0.25f, 0.25f, 1f), AtlasBlend.MultiplyDouble, 1f);
            AssertColor(new Color(1.14875f, 0.574375f, 0f), Evaluate(material), "Unity's detail multiply");

            var masked = ColorLayer(new Color(0f, 0f, 1f, 1f), AtlasBlend.Replace, 1f);
            masked.Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.5f, 0f, 0f, 1f)), Interpretation = AtlasTextureInterpretation.Data } };
            material.Layers[0] = masked;
            AssertColor(new Color(0.5f, 0.25f, 0.5f), Evaluate(material), "replace under a half mask");

            var alphaBlended = ColorLayer(Color.white, AtlasBlend.Replace, 1f);
            alphaBlended.Source.Color = new Color(0f, 0f, 1f, 1f);
            alphaBlended.Source.Map = new AtlasTextureReference { Texture = Solid(new Color(1f, 1f, 1f, 0.25f)) };
            alphaBlended.SourceAlphaIsOpacity = true;
            material.Layers[0] = alphaBlended;
            AssertColor(new Color(0.75f, 0.375f, 0.25f), Evaluate(material), "the source's alpha as opacity");
        }

        [Test]
        public void StaticAdjustmentsApplyOnTheirTargetsInOrder()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(0.8f, 0.4f, 0.2f, 1f);
            material.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.BaseColor, Operation = AtlasColorOperation.Brightness, Amount = 0.5f });
            var layer = ColorLayer(new Color(0f, 0f, 1f, 1f), AtlasBlend.Add, 1f);
            material.Layers.Add(layer);
            material.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.Layer, Layer = layer, Operation = AtlasColorOperation.Brightness, Amount = 0.5f });
            material.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.SurfaceColor, Operation = AtlasColorOperation.Lightness, Amount = 0.1f });
            // (0.4, 0.2, 0.1) + the layer's (0, 0, 0.5), then + 0.1.
            AssertColor(new Color(0.5f, 0.3f, 0.7f), Evaluate(material), "base, layer source, surface color");

            material.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.SurfaceColor, Operation = AtlasColorOperation.Hue, HueMethod = AtlasHueMethod.GrayAxisRotation, Amount = 0.25f });
            Assert.IsFalse(AtlasColorStack.CanBake(material, 3, material.ColorAdjustments[3]), "gray-axis hue has no baked form yet");
            AssertColor(new Color(0.5f, 0.3f, 0.7f), Evaluate(material), "left out");
            Assert.IsTrue(AtlasColorStack.LeftOut(material).Any(n => n.Contains("GrayAxisRotation") || n.Contains("Hue")));
        }

        [Test]
        public void LivePartsAreLeftOut()
        {
            var material = new AtlasUnifiedMaterial();
            var layer = ColorLayer(new Color(0f, 0f, 1f, 1f), AtlasBlend.Replace, 1f);
            material.Layers.Add(layer);
            material.Modulators.Add(new AtlasModulator { Target = "layers[0].targets[0].opacity" });
            Assert.IsFalse(AtlasColorStack.CanBake(material, 0, layer), "modulated");
            AssertColor(Color.white, Evaluate(material), "left out");
            Assert.IsTrue(AtlasColorStack.LeftOut(material).Any());

            material.Modulators.Clear();
            layer.Source.Map = new AtlasTextureReference { Texture = Solid(Color.white), Scroll = new Vector2(0.1f, 0f) };
            Assert.IsFalse(AtlasColorStack.CanBake(material, 0, layer), "scrolling");
            layer.Source.Map.Scroll = Vector2.zero;
            Assert.IsTrue(AtlasColorStack.CanBake(material, 0, layer));
        }
    }
}
