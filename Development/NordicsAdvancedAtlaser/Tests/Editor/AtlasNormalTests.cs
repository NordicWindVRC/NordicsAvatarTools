using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Normal maps (ATLAS_OUTPUT.md, "Feature inventory"): the established order, strengths
    // normalized to the largest, and the writers.
    public sealed class AtlasNormalTests
    {
        private readonly List<Object> created = new List<Object>();
        private readonly List<System.IDisposable> disposables = new List<System.IDisposable>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            foreach (var d in disposables) d.Dispose();
            created.Clear();
            disposables.Clear();
        }

        // A texture holding one tangent-space normal everywhere, encoded as a normal map stores it.
        private Texture2D NormalTexture(Vector3 normal, int size = 4)
        {
            normal.Normalize();
            var encoded = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(encoded, size * size).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private AtlasUnifiedMaterial WithNormal(Vector3 normal, float strength)
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.Normal.Map = new AtlasTextureReference { Texture = NormalTexture(normal), Interpretation = AtlasTextureInterpretation.TangentNormal };
            material.Surface.Normal.Strength = strength;
            return material;
        }

        private static AtlasOutputCapabilities WithNormalTexture() => new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB),
                PoiyomiToon10OutputShader.NormalTexture()
            }
        };

        private static AtlasGeneratorJob Job(params AtlasGeneratorSource[] sources)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i].UVRegion = new Rect(i / (float)sources.Length, 0f, 1f / sources.Length, 1f);
                job.Sources.Add(sources[i]);
            }
            return job;
        }

        private static Vector3 Read(NordicsTextureWrapper map, int x = 1, int y = 1)
        {
            var c = map.GetPixel(x, y);
            return new Vector3(c.r, c.g, c.b);
        }

        [Test]
        public void ASourceHasNormalsFromItsMapAPreBakedMapOrTheGeometryBake()
        {
            var feature = new AtlasNormalFeature();
            Assert.IsTrue(feature.IsPresent(new AtlasGeneratorSource { Material = WithNormal(Vector3.forward, 1f) }));
            Assert.IsFalse(feature.IsPresent(new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial() }));
            Assert.IsTrue(feature.IsPresent(new AtlasGeneratorSource
                { Material = new AtlasUnifiedMaterial(), PrebakedNormalMap = NormalTexture(Vector3.forward) }));
            Assert.IsTrue(feature.IsPresent(new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial(), GeometryBakingAvailable = true }));
        }

        [Test]
        public void TheOutputTakesTheLargestStrengthAndEverySourceIsBaked()
        {
            var job = Job(new AtlasGeneratorSource { Material = WithNormal(Vector3.forward, 0.5f) },
                new AtlasGeneratorSource { Material = WithNormal(Vector3.forward, 2f) },
                new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial() });
            var plan = AtlasOutputPipeline.Resolve(job, WithNormalTexture());
            Assert.AreEqual(2f, plan.Output.Surface.Normal.Strength);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Normal);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, request.Sources, "the one without normals too, as flat");
            Assert.AreSame(request.Reference, plan.Output.Surface.Normal.Map);

            // Nothing that bends the normal: no map at all.
            var flat = AtlasOutputPipeline.Resolve(Job(new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial(), GeometryBakingAvailable = true }),
                WithNormalTexture());
            Assert.IsFalse(flat.Maps.Any(m => m.Map == AtlasVirtualMap.Normal));
        }

        [Test]
        public void AWeakerSourceIsBakedAtItsShareOfTheOutputStrength()
        {
            var tilted = new Vector3(0.6f, 0f, 0.8f);
            using (var full = AtlasMapEvaluator.Normal(new AtlasGeneratorSource { Material = WithNormal(tilted, 1f) }, 4, 4, 1f))
                Assert.AreEqual(0.6f, Read(full).x, 1e-3f, "at the output strength: as it is");
            using (var half = AtlasMapEvaluator.Normal(new AtlasGeneratorSource { Material = WithNormal(tilted, 1f) }, 4, 4, 2f))
            {
                // x and y scaled by 1 / 2, then normalized: (0.3, 0, 0.8).
                var expected = new Vector3(0.3f, 0f, 0.8f).normalized;
                Assert.AreEqual(expected.x, Read(half).x, 1e-3f);
                Assert.AreEqual(expected.z, Read(half).z, 1e-3f);
            }
            using (var flat = AtlasMapEvaluator.Normal(new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial() }, 4, 4, 1f))
                Assert.AreEqual(1f, Read(flat).z, 1e-5f, "no normals: flat");
        }

        [Test]
        public void TheCorrectionComesBeforeThePrimaryNormalAndTheyCombine()
        {
            var correction = new NordicsTextureWrapper(4, 4, AtlasMapEvaluator.Settings(AtlasVirtualMap.Normal));
            disposables.Add(correction);
            correction.Fill(new Color(0f, 0.6f, 0.8f, 1f));
            var bakes = new AtlasBakedInputSet { Width = 4, Height = 4 };
            bakes.Submeshes.Add(new AtlasSubmeshBake { TangentNormalCorrection = correction, CorrectionIsFlat = false });
            var source = new AtlasGeneratorSource { Material = WithNormal(new Vector3(0.6f, 0f, 0.8f), 1f), Bakes = bakes, GeometryBakingAvailable = true };
            Assert.AreSame(correction, AtlasMapEvaluator.Correction(source));
            Assert.AreEqual(1f, AtlasNormalFeature.LargestStrength(source));
            using (var combined = AtlasMapEvaluator.Normal(source, 4, 4, 1f))
            {
                // Whiteout: (0.6 + 0, 0 + 0.6, 0.8 × 0.8), normalized.
                var expected = new Vector3(0.6f, 0.6f, 0.64f).normalized;
                var n = Read(combined);
                Assert.AreEqual(expected.x, n.x, 1e-3f);
                Assert.AreEqual(expected.y, n.y, 1e-3f);
            }
            bakes.Submeshes[0].CorrectionIsFlat = true;
            Assert.IsNull(AtlasMapEvaluator.Correction(source), "a flat correction is left out");
        }

        [Test]
        public void ANormalLayerIsMaskedAndOnlyBakeableLayersCount()
        {
            var material = new AtlasUnifiedMaterial();
            var layer = new AtlasSurfaceLayer
            {
                Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = NormalTexture(Vector3.forward), Interpretation = AtlasTextureInterpretation.Data } }
            };
            layer.Source.Map = new AtlasTextureReference { Texture = NormalTexture(new Vector3(0.6f, 0f, 0.8f)), Interpretation = AtlasTextureInterpretation.TangentNormal };
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace, 1f));
            material.Layers.Add(layer);
            Assert.IsTrue(AtlasMapEvaluator.CanBakeNormalLayer(layer));
            // The mask texture's red is 0.5 (a flat normal encoded): the layer at half influence.
            using (var map = AtlasMapEvaluator.Normal(new AtlasGeneratorSource { Material = material }, 4, 4, 1f))
            {
                float x = Read(map).x;
                Assert.Greater(x, 0.05f);
                Assert.Less(x, 0.6f - 0.05f);
            }
            layer.Source.Map.Space = AtlasTextureSpace.UV1;
            Assert.IsFalse(AtlasMapEvaluator.CanBakeNormalLayer(layer), "another UV channel isn't baked yet");
            Assert.AreEqual(0f, AtlasNormalFeature.LargestStrength(new AtlasGeneratorSource { Material = material }));
        }

        [Test]
        public void TheNormalMapPacksAsANormalTexture()
        {
            var job = Job(new AtlasGeneratorSource { Material = WithNormal(new Vector3(0.6f, 0f, 0.8f), 1f) },
                new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial() });
            var capabilities = WithNormalTexture();
            var plan = AtlasOutputPipeline.Resolve(job, capabilities);
            var config = new AtlasOutputConfiguration { mainTexture = { width = 32, height = 16 } };
            var use = AtlasOutputOverview.TextureUses(config, capabilities, plan.Maps.Select(m => m.Map).Distinct().ToList(),
                new AtlasVirtualMap[0]).Single(u => u.Texture.Normal);
            Assert.AreEqual(AtlasTextureKind.Normal, use.Kind);
            using (var packed = AtlasTexturePacker.Pack(use, plan, job))
            {
                Assert.IsTrue(packed.Settings.IsNormal);
                // The normal map keeps the main texture's aspect at its own size (128 × 64 at the
                // smallest), so positions are relative.
                Assert.AreEqual((use.Width, use.Height), (packed.Width, packed.Height));
                Assert.AreEqual(0.6f, Read(packed, packed.Width / 4, packed.Height / 2).x, 0.02f, "the source's normal");
                Assert.AreEqual(1f, Read(packed, packed.Width * 3 / 4, packed.Height / 2).z, 0.02f, "the one without normals is flat");
            }
        }

        [Test]
        public void TheWritersSetTheNormalMapTheParsersReadBack()
        {
            var texture = NormalTexture(new Vector3(0.6f, 0f, 0.8f));
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders)
            {
                var unified = new AtlasUnifiedMaterial();
                unified.Surface.Normal.Map = new AtlasTextureReference { Texture = texture, Interpretation = AtlasTextureInterpretation.TangentNormal };
                unified.Surface.Normal.Strength = 1.5f;
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(unified, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
                Assert.AreSame(texture, parsed.Surface.Normal.Map?.Texture, shader.DisplayName);
                Assert.AreEqual(1.5f, parsed.Surface.Normal.Strength, 1e-5f, shader.DisplayName);

                var without = new Material(shader.FindShader());
                created.Add(without);
                shader.Write(new AtlasUnifiedMaterial(), without);
                Assert.IsNull(AtlasShaderRegistry.Detect(without).Parse(without).Surface.Normal.Map, shader.DisplayName + ": none");
            }
        }
    }
}
