using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Tiled layers kept live (ATLAS_OUTPUT.md, "UV channels"): detail textures and detail normals.
    public sealed class AtlasDetailTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private Texture2D Solid(Color color, string name = "detail")
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { name = name };
            texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
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

        private static AtlasSurfaceLayer Detail(Texture2D texture, float tiling, AtlasSurfaceTarget target = AtlasSurfaceTarget.BaseColor,
            AtlasBlend blend = AtlasBlend.MultiplyDouble, float opacity = 1f)
        {
            var layer = new AtlasSurfaceLayer();
            layer.Source.Map = new AtlasTextureReference
            {
                Texture = texture, Tiling = Vector2.one * tiling,
                Interpretation = target == AtlasSurfaceTarget.Normal ? AtlasTextureInterpretation.TangentNormal : AtlasTextureInterpretation.Color
            };
            layer.Targets.Add(new AtlasLayerTarget(target, target == AtlasSurfaceTarget.Normal ? AtlasBlend.Replace : blend, opacity));
            return layer;
        }

        private static AtlasUnifiedMaterial With(params AtlasLayer[] layers)
        {
            var material = new AtlasUnifiedMaterial();
            material.Layers.AddRange(layers);
            return material;
        }

        private static float Evaluate(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index)
        {
            using (var result = plan.Maps.Single(m => m.Map == map).Evaluate(index, job.Sources[index].Material, 4, 4))
                return result.GetPixel(1, 1).r;
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void ADetailTextureStaysLiveOnAChannelWithEachSourcesTiling()
        {
            var texture = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
            var near = Detail(texture, 10f);
            var far = Detail(texture, 20f, opacity: 0.5f);
            var job = Job(With(near), With(far), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreEqual("live", plan.Modes[AtlasDetailTextureFeature.FeatureId].Id);
            var live = AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor);
            Assert.IsNotNull(live, Notes(plan));
            Assert.AreSame(texture, live.Source.Map.Texture);
            Assert.AreEqual(Vector2.one, live.Source.Map.Tiling);
            int channel = AtlasDetailFeatures.UV(live);
            Assert.GreaterOrEqual(channel, 1);
            Assert.AreEqual(1f, live.Targets[0].Opacity, 1e-5f, "the strongest");
            var preserved = plan.SourceRemaps[0].PreservedUVs.Single(p => p.Channel == channel);
            Assert.AreEqual(new Vector2(1f, 2f), preserved.Convert(new Vector2(0.1f, 0.2f)), "UV0 × 10");
            Assert.AreEqual(new Vector2(2f, 4f), plan.SourceRemaps[1].PreservedUVs.Single(p => p.Channel == channel).Convert(new Vector2(0.1f, 0.2f)));
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.DetailTextureMask, job, 0), 1e-4f);
            Assert.AreEqual(0.5f, Evaluate(plan, AtlasVirtualMap.DetailTextureMask, job, 1), 1e-4f, "half the strongest");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.DetailTextureMask, job, 2), 1e-4f, "none");
            // The base color bake leaves it out.
            Assert.IsTrue(AtlasLiveLayers.Skipped(job.Sources[0].Material, near));
            using (var color = AtlasColorStack.Evaluate(job.Sources[0].Material, 4, 4, true))
                Assert.AreEqual(1f, color.GetPixel(1, 1).r, 1e-4f);
        }

        [Test]
        public void TheReferenceSourcesDetailIsKeptAndTheOthersBaked()
        {
            var first = Detail(Solid(Color.gray, "a"), 10f);
            var second = Detail(Solid(Color.gray, "b"), 10f);
            var job = Job(With(first), With(second));
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreSame(first.Source.Map.Texture, AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor).Source.Map.Texture, "the larger area's");
            Assert.IsFalse(AtlasLiveLayers.Skipped(job.Sources[1].Material, second), "baked");
            StringAssert.Contains("differs from the one kept live", Notes(plan));
            Assert.IsTrue(new AtlasDetailTextureFeature().SharedValuesDiffer(job.Sources.Select(s => s.Material).ToList(), Poiyomi));

            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasDetailTextureFeature.FeatureId, ReferenceSource = 1 });
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreSame(second.Source.Map.Texture, AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor).Source.Map.Texture, "the chosen one");
        }

        [Test]
        public void ABlendTheOutputLacksOrALaterLayerKeepsItBaked()
        {
            var added = Detail(Solid(Color.gray), 10f, blend: AtlasBlend.Add);
            var plan = AtlasOutputPipeline.Resolve(Job(With(added)), Poiyomi);
            Assert.IsNull(AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor));
            StringAssert.Contains("lacks its blend", Notes(plan));
            // Toon Standard adds.
            var toon = AtlasOutputPipeline.Resolve(Job(With(added)), ToonStandard);
            var live = AtlasDetailFeatures.Of(toon.Output, AtlasSurfaceTarget.BaseColor);
            Assert.IsNotNull(live, Notes(toon));
            Assert.AreEqual(1, AtlasDetailFeatures.UV(live), "Toon Standard reads UV1");

            var later = new AtlasSurfaceLayer();
            later.Source.Color = Color.red;
            later.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, AtlasBlend.Replace, 0.5f));
            plan = AtlasOutputPipeline.Resolve(Job(With(Detail(Solid(Color.gray), 10f), later)), Poiyomi);
            Assert.IsNull(AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor));
            StringAssert.Contains("a layer after it changes its result", Notes(plan));
        }

        [Test]
        public void BakedAndOffModesBakeOrLeaveItOut()
        {
            var layer = Detail(Solid(Color.gray), 10f);
            var job = Job(With(layer));
            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasDetailTextureFeature.FeatureId, ModeId = "baked" });
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.IsNull(AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor));
            Assert.IsFalse(AtlasLiveLayers.Skipped(job.Sources[0].Material, layer));
            job.Features[0].ModeId = "off";
            AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.IsTrue(AtlasLiveLayers.Skipped(job.Sources[0].Material, layer), "left out of the bake");
        }

        [Test]
        public void ADetailNormalStaysLiveAndTheNormalBakeLeavesItOut()
        {
            var bump = Solid(new Color(0.75f, 0.5f, 1f, 1f), "bump");
            var layer = Detail(bump, 8f, AtlasSurfaceTarget.Normal, opacity: 0.6f);
            var job = Job(With(layer), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var live = AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.Normal);
            Assert.IsNotNull(live, Notes(plan));
            Assert.AreEqual(0.6f, live.Targets[0].Opacity, 1e-5f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.DetailNormalMask, job, 0), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.DetailNormalMask, job, 1), 1e-4f);
            using (var normal = AtlasMapEvaluator.Normal(job.Sources[0], 4, 4, 1f))
                Assert.AreEqual(0.5f, normal.GetPixel(1, 1).r * 0.5f + 0.5f, 1e-3f, "flat: the detail isn't baked");
        }

        [Test]
        public void TheWritersSetTheDetailsTheParsersReadBack()
        {
            var texture = Solid(Color.gray, "detail");
            var bump = Solid(new Color(0.5f, 0.5f, 1f, 1f), "bump");
            var maskTexture = Solid(Color.white, "mask");
            var poiyomi = AtlasShaderRegistry.Output("poiyomi10");
            if (poiyomi.FindShader() != null)
            {
                var output = new AtlasUnifiedMaterial();
                var detail = Detail(texture, 1f, opacity: 0.7f);
                detail.Source.Map.Space = AtlasTextureSpace.UV2;
                detail.Source.Color = new Color(2f, 1f, 1f, 1f);
                detail.Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Red };
                var normal = Detail(bump, 1f, AtlasSurfaceTarget.Normal, opacity: 0.5f);
                normal.Source.Map.Space = AtlasTextureSpace.UV2;
                normal.Mask = new AtlasMask { Map = detail.Mask.Map, Channel = AtlasChannel.Green };
                output.Layers.Add(detail);
                output.Layers.Add(normal);
                var material = new Material(poiyomi.FindShader());
                created.Add(material);
                poiyomi.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
                var color = parsed.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.BaseColor));
                Assert.AreSame(texture, color.Source.Map.Texture, "Poiyomi");
                Assert.AreEqual(AtlasTextureSpace.UV2, color.Source.Map.Space);
                Assert.AreEqual(AtlasBlend.MultiplyDouble, color.Targets[0].Blend);
                Assert.AreEqual(0.7f, color.Targets[0].Opacity, 1e-4f);
                Assert.AreEqual(2f, color.Source.Color.r, 1e-3f, "tint × brightness");
                Assert.AreSame(maskTexture, color.Mask.Map.Texture);
                var parsedNormal = parsed.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Normal));
                Assert.AreSame(bump, parsedNormal.Source.Map.Texture);
                Assert.AreEqual(AtlasTextureSpace.UV2, parsedNormal.Source.Map.Space);
                Assert.AreEqual(0.5f, parsedNormal.Targets[0].Opacity, 1e-4f);
                Assert.AreEqual(AtlasChannel.Green, parsedNormal.Mask.Channel);
            }

            var toon = AtlasShaderRegistry.Output("mobiletoon");
            if (toon.FindShader() != null)
            {
                var output = new AtlasUnifiedMaterial();
                var detail = Detail(texture, 1f, blend: AtlasBlend.Add);
                detail.Source.Map.Space = AtlasTextureSpace.UV1;
                detail.Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Blue };
                var normal = Detail(bump, 1f, AtlasSurfaceTarget.Normal, opacity: 0.8f);
                normal.Source.Map.Space = AtlasTextureSpace.UV1;
                normal.Mask = detail.Mask;
                output.Layers.Add(detail);
                output.Layers.Add(normal);
                var material = new Material(toon.FindShader());
                created.Add(material);
                toon.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Layers.OfType<AtlasSurfaceLayer>().ToList();
                var color = parsed.Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.BaseColor));
                Assert.AreSame(texture, color.Source.Map.Texture, "Toon Standard");
                Assert.AreEqual(AtlasTextureSpace.UV1, color.Source.Map.Space);
                Assert.AreEqual(AtlasBlend.Add, color.Targets[0].Blend);
                Assert.AreEqual(AtlasChannel.Blue, color.Mask.Channel);
                var parsedNormal = parsed.Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Normal));
                Assert.AreEqual(0.8f, parsedNormal.Targets[0].Opacity, 1e-4f);
            }
        }
    }
}
