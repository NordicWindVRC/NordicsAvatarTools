using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Packing, the writers and the export rules.
    public sealed class AtlasOutputWritingTests
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
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static AtlasOutputCapabilities Layout() => new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        private AtlasGeneratorJob TwoSources()
        {
            var job = new AtlasGeneratorJob();
            foreach (var (color, x) in new[] { (Color.red, 0f), (Color.blue, 0.5f) })
            {
                var material = new AtlasUnifiedMaterial();
                material.Surface.BaseColor.Map = new AtlasTextureReference { Texture = Solid(color) };
                job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(x, 0f, 0.5f, 1f) });
            }
            return job;
        }

        [Test]
        public void BaseColorIsPackedIntoTheRGBOfItsTextureWithoutAlpha()
        {
            var config = new AtlasOutputConfiguration { mainTexture = { width = 32, height = 16 } };
            var job = TwoSources();
            var plan = AtlasOutputPipeline.Resolve(job, Layout());
            var use = AtlasOutputOverview.TextureUses(config, Layout(), new[] { AtlasVirtualMap.BaseColor }, new AtlasVirtualMap[0]).Single();
            using (var packed = AtlasTexturePacker.Pack(use, plan, job))
            {
                Assert.AreEqual((32, 16), (packed.Width, packed.Height));
                var left = packed.GetPixel(8, 8);
                var right = packed.GetPixel(24, 8);
                Assert.That(left.r, Is.EqualTo(1f).Within(0.02f));
                Assert.That(right.b, Is.EqualTo(1f).Within(0.02f));
                Assert.AreEqual(1f, left.a, "Alpha keeps its neutral value.");
                Assert.AreEqual(TextureFormat.RGB24, packed.GetOutputTextureFormat(), "No alpha channel when no map uses it.");
                Assert.IsTrue(packed.Settings.SRGB);
                Assert.IsTrue(packed.Settings.StreamingMipmaps);
            }
        }

        [Test]
        public void WritersSetTheBaseColorTextureTheParserReadsBack()
        {
            bool any = false;
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders)
            {
                any = true;
                var texture = Solid(Color.green);
                var output = new AtlasUnifiedMaterial();
                output.Surface.BaseColor.Map = new AtlasTextureReference { Texture = texture };
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(output, material);
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.NotNull(parser, shader.DisplayName);
                var read = parser.Parse(material).Surface.BaseColor;
                Assert.AreSame(texture, read.Map?.Texture, shader.DisplayName);
                Assert.AreEqual(Color.white, read.Color, shader.DisplayName);
                Assert.AreEqual(Vector2.one, read.Map.Tiling, shader.DisplayName);
                Assert.AreEqual(2000, material.renderQueue, shader.DisplayName);
            }
            if (!any) Assert.Ignore("No output shader is installed.");
        }

        [Test]
        public void CrunchIsKeptOnlyWhenItSavesTenPercent()
        {
            Assert.IsTrue(AtlasTextureExport.KeepCrunch(89, 100));
            Assert.IsFalse(AtlasTextureExport.KeepCrunch(90, 100));
            Assert.AreEqual(TextureFormat.DXT1Crunched, AtlasTextureExport.Crunched(AtlasTextureFormat.BC1));
            Assert.IsNull(AtlasTextureExport.Crunched(AtlasTextureFormat.BC7), "BC7 has no crunched form.");
            Assert.IsNull(AtlasTextureExport.Crunched(AtlasTextureFormat.ASTC6x6));
        }

        [Test]
        public void CrunchShrinksAPlainTextureWellBelowItsFormat()
        {
            using (var packed = new NordicsTextureWrapper(256, 256, new TextureSettings { Mipmaps = true }))
            {
                packed.Fill(new Color(0.2f, 0.5f, 0.8f, 1f));
                long crunched = AtlasTextureExport.CrunchedBytes(packed, AtlasTextureFormat.BC1);
                long plain = AtlasTextureMemory.Bytes(256, 256, AtlasTextureFormat.BC1);
                Assert.Greater(crunched, 0);
                Assert.Less(crunched, plain / 2, "A single color crunches to far less than BC1.");
                Assert.IsTrue(AtlasTextureExport.CrunchPays(packed, AtlasTextureFormat.BC1));
            }
        }

        [Test]
        public void FormatsMapToTheImportersFormats()
        {
            Assert.AreEqual(TextureImporterFormat.DXT1, AtlasTextureExport.ImporterFormat(AtlasTextureFormat.BC1, false));
            Assert.AreEqual(TextureImporterFormat.DXT1Crunched, AtlasTextureExport.ImporterFormat(AtlasTextureFormat.BC1, true));
            Assert.AreEqual(TextureImporterFormat.BC7, AtlasTextureExport.ImporterFormat(AtlasTextureFormat.BC7, true));
            Assert.AreEqual(TextureImporterFormat.ASTC_6x6, AtlasTextureExport.ImporterFormat(AtlasTextureFormat.ASTC6x6, false));
        }

        [Test]
        public void TexturesAreNamedAfterTheMaterial()
        {
            Assert.AreEqual("Body_BaseColor", AtlasManualBake.TextureName("Body", new AtlasOutputTexture("BaseColor", "Base color")));
        }

        [Test]
        public void ManualBakeReadsEveryAssignedSourceOnce()
        {
            var a = AtlasTestMaterials.Standard();
            var b = AtlasTestMaterials.Standard();
            created.Add(a);
            created.Add(b);
            var config = new AtlasOutputConfiguration();
            config.sources.Add(new AtlasSourceEntry { material = a, uvRegion = new Rect(0f, 0f, 0.5f, 1f) });
            config.sources.Add(new AtlasSourceEntry { material = b, uvRegion = new Rect(0.5f, 0f, 0.5f, 1f) });
            config.sources.Add(new AtlasSourceEntry { material = a, uvRegion = new Rect(0f, 0f, 1f, 1f) });
            config.sources.Add(new AtlasSourceEntry());
            config.features.Add(new AtlasFeatureSetting { featureId = "color", disabledSources = { b } });
            var job = AtlasManualBake.Job(config);
            Assert.AreEqual(2, job.Sources.Count);
            Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 1f), job.Sources[1].UVRegion);
            Assert.IsFalse(job.Sources[0].GeometryBakingAvailable);
            CollectionAssert.AreEqual(new[] { 1 }, job.Features.Single().DisabledSources.ToArray());
        }
    }
}
