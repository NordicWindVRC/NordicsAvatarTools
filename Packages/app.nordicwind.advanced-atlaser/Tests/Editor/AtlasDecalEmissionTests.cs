using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Decals in the base color and emission (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasDecalEmissionTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        // 4 × 4: the left half red, the right half blue.
        private Texture2D Halves()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    texture.SetPixel(x, y, x < 2 ? Color.red : Color.blue);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static AtlasUnifiedMaterial WithDecal(Texture2D texture, float rotation)
        {
            var material = new AtlasUnifiedMaterial();
            var layer = new AtlasSurfaceLayer();
            layer.Source.Map = new AtlasTextureReference
            {
                Texture = texture,
                Decal = new AtlasDecal { Center = new Vector2(0.5f, 0.5f), Scale = new Vector2(0.5f, 0.5f), Rotation = rotation }
            };
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, AtlasBlend.Replace, 1f));
            material.Layers.Add(layer);
            return material;
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 0.02f, message + " r");
            Assert.AreEqual(expected.g, actual.g, 0.02f, message + " g");
            Assert.AreEqual(expected.b, actual.b, 0.02f, message + " b");
        }

        [Test]
        public void ADecalIsPlacedTurnedAndClipped()
        {
            var texture = Halves();
            // Pixel (x, y) of 10 × 10 is UV ((x + 0.5) / 10, (y + 0.5) / 10); the decal covers 0.25–0.75.
            using (var map = AtlasColorStack.Evaluate(WithDecal(texture, 0f), 10, 10, true))
            {
                AssertColor(Color.red, map.GetPixel(3, 5), "left of the center: the left half");
                AssertColor(Color.blue, map.GetPixel(6, 5), "right of the center: the right half");
                AssertColor(Color.white, map.GetPixel(0, 0), "outside: the base color");
            }
            // Turned 90°: the UV is turned before sampling, so above the center samples the left half.
            using (var map = AtlasColorStack.Evaluate(WithDecal(texture, 90f), 10, 10, true))
            {
                AssertColor(Color.red, map.GetPixel(5, 6), "above the center");
                AssertColor(Color.blue, map.GetPixel(5, 3), "below the center");
            }
        }

        [Test]
        public void OnlyPlacementsTheBakeReproducesAreBaked()
        {
            var material = WithDecal(Halves(), 0f);
            var layer = (AtlasSurfaceLayer)material.Layers[0];
            Assert.IsTrue(AtlasColorStack.CanBake(material, 0, layer));
            layer.Source.Map.Decal.Spin = 30f;
            Assert.IsFalse(AtlasColorStack.CanBake(material, 0, layer), "spinning");
            layer.Source.Map.Decal.Spin = 0f;
            layer.Source.Map.Decal.Symmetry = AtlasDecalSymmetry.Copy;
            Assert.IsFalse(AtlasColorStack.CanBake(material, 0, layer), "symmetry");
            Assert.IsTrue(AtlasColorStack.LeftOut(material).Any(n => n.Contains("decal")));
        }

        private static AtlasUnifiedMaterial Emissive(Color color, float strength)
        {
            var material = new AtlasUnifiedMaterial();
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Color = color;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            material.Layers.Add(layer);
            return material;
        }

        [Test]
        public void EmissionIsBakedRelativeToTheStrongestSource()
        {
            var job = new AtlasGeneratorJob();
            var sources = new[] { Emissive(new Color(2f, 1f, 0f, 1f), 3f), Emissive(Color.white, 1f), new AtlasUnifiedMaterial() };
            for (int i = 0; i < sources.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource { Material = sources[i], UVRegion = new Rect(i / 3f, 0f, 1f / 3f, 1f) });
            Assert.AreEqual(6f, AtlasColorStack.EmissionStrength(sources[0]), 1e-5f);
            Assert.IsFalse(new AtlasEmissionFeature().IsPresent(sources[2]));

            var plan = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("poiyomi10").Capabilities);
            var emission = AtlasEmissionFeature.Of(plan.Output);
            Assert.IsNotNull(emission);
            Assert.AreEqual(6f, emission.Targets[0].Opacity, 1e-5f, "the strongest source's");
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Emission);
            Color Evaluate(int index)
            {
                using (var map = request.Evaluate(index, job.Sources[index].Material, 4, 4)) return map.GetPixel(1, 1);
            }
            AssertColor(new Color(1f, 0.5f, 0f), Evaluate(0), "(2, 1, 0) × 3 / 6");
            AssertColor(new Color(1f / 6f, 1f / 6f, 1f / 6f), Evaluate(1), "white × 1 / 6");
            AssertColor(Color.black, Evaluate(2), "no emission: dark");
        }

        [Test]
        public void TheWritersSetTheEmissionTheParsersReadBack()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            created.Add(texture);
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders)
            {
                var output = new AtlasUnifiedMaterial();
                var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                layer.Source.Map = new AtlasTextureReference { Texture = texture };
                // Above both shaders' strength sliders: the rest goes into the color.
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 25f));
                output.Layers.Add(layer);
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(output, material);
                var parsed = AtlasEmissionFeature.Of(AtlasShaderRegistry.Detect(material).Parse(material));
                Assert.IsNotNull(parsed, shader.DisplayName);
                Assert.AreSame(texture, parsed.Source.Map?.Texture, shader.DisplayName);
                float total = parsed.Source.Color.r * parsed.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
                Assert.AreEqual(25f, total, 0.05f, shader.DisplayName + ": color × strength");

                var without = new Material(shader.FindShader());
                created.Add(without);
                shader.Write(new AtlasUnifiedMaterial(), without);
                Assert.IsNull(AtlasEmissionFeature.Of(AtlasShaderRegistry.Detect(without).Parse(without)), shader.DisplayName + ": none");
            }
        }
    }
}
