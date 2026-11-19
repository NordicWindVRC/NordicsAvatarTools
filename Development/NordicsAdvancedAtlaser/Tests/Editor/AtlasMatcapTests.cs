using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Matcaps and the mask allocator (ATLAS_OUTPUT.md, "Masks" and "Feature inventory").
    public sealed class AtlasMatcapTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private Texture2D Solid(Color color, string name = "matcap", int size = 4)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { name = name };
            texture.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;
        private static AtlasOutputCapabilities ToonStandard => AtlasShaderRegistry.Output("mobiletoon").Capabilities;

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            // Earlier sources take larger regions, so automatic placement goes in source order.
            float x = 0f;
            for (int i = 0; i < materials.Length; i++)
            {
                float width = (materials.Length - i) / (materials.Length * (materials.Length + 1) / 2f);
                job.Sources.Add(new AtlasGeneratorSource { Material = materials[i], UVRegion = new Rect(x, 0f, width, 1f) });
                x += width;
            }
            return job;
        }

        // A matcap's layers, one per blend weight, sharing their mask and texture reference as a
        // parser writes them.
        private static AtlasUnifiedMaterial WithMatcap(Texture2D texture, AtlasMask mask, Color color, params (AtlasBlend Blend, float Opacity)[] weights)
        {
            var material = new AtlasUnifiedMaterial();
            var map = texture == null ? null : new AtlasTextureReference { Texture = texture, Space = AtlasTextureSpace.View };
            foreach (var weight in weights)
            {
                var layer = new AtlasLookupLayer { Mask = mask, Blend = weight.Blend, Opacity = weight.Opacity, Projection = AtlasLookupProjection.ViewCorrected };
                layer.Source.Map = map;
                layer.Source.Color = color;
                material.Layers.Add(layer);
            }
            return material;
        }

        private static List<AtlasLookupLayer> Lookups(AtlasOutputPlan plan) => plan.Output.Layers.OfType<AtlasLookupLayer>().ToList();

        private static float Evaluate(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index)
        {
            var request = plan.Maps.Single(m => m.Map == map);
            using (var result = request.Evaluate(index, job.Sources[index].Material, 4, 4)) return result.GetPixel(1, 1).r;
        }

        private static void Place(AtlasGeneratorJob job, string key, AtlasInstancePlacement placement, string target = "")
        {
            var choice = job.Features.Find(f => f.FeatureId == AtlasMatcapFeature.FeatureId);
            if (choice == null) job.Features.Add(choice = new AtlasFeatureChoice { FeatureId = AtlasMatcapFeature.FeatureId });
            choice.Values[AtlasInstancePlacements.Id(key)] = ((int)placement, target);
        }

        [Test]
        public void TheMaskAllocatorSkipsConstantsSharesEqualMasksReadsInversesInvertedAndPacksTheRest()
        {
            var job = Job(new AtlasUnifiedMaterial(), new AtlasUnifiedMaterial());
            var plan = new AtlasOutputPlan();
            var context = new AtlasFeatureContext
            {
                Job = job, Capabilities = Poiyomi, Plan = plan, Maps = plan.Maps, Notes = plan.Notes, Output = plan.Output, Sources = new[] { 0, 1 }
            };
            AtlasMaskRequest Request(params AtlasMask[] masks) => new AtlasMaskRequest
            {
                Name = "test", CanInvert = true,
                Key = i => AtlasMaskKeys.Of(masks[i], 1f, job.Sources[i].Material),
                InverseKey = i => AtlasMaskKeys.Inverse(masks[i], 1f, job.Sources[i].Material),
                Evaluate = (i, material, w, h) => AtlasMapEvaluator.ScaledMask(masks[i], 1f, w, h, material)
            };
            var first = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.25f, 0f, 0f, 1f)) } };
            var second = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.75f, 0f, 0f, 1f)) } };

            var constant = AtlasMaskAllocator.Allocate(context, Request(null, null));
            Assert.IsTrue(constant.IsConstant, "no mask anywhere: one value");
            Assert.AreEqual(1f, constant.Constant);
            Assert.AreEqual(0, plan.Maps.Count, "a constant needs no map");

            var placed = AtlasMaskAllocator.Allocate(context, Request(first, first));
            Assert.AreEqual(AtlasChannel.Red, placed.Mask.Channel, "the first place: Masks 1, red");
            Assert.AreEqual(AtlasVirtualMap.Mask1, plan.Maps.Single().Map);

            var equal = AtlasMaskAllocator.Allocate(context, Request(first, first));
            Assert.AreSame(placed.Mask.Map, equal.Mask.Map, "equal masks share the place");
            Assert.AreNotSame(placed.Mask, equal.Mask, "each reader has its own mask object");
            Assert.AreEqual(1, plan.Maps.Count);

            var inverted = new AtlasMask { Map = first.Map, Invert = true };
            var inverse = AtlasMaskAllocator.Allocate(context, Request(inverted, inverted));
            Assert.AreSame(placed.Mask.Map, inverse.Mask.Map, "the inverse reads the same place");
            Assert.IsTrue(inverse.Mask.Invert);
            Assert.AreEqual(1, plan.Maps.Count);

            var other = AtlasMaskAllocator.Allocate(context, Request(second, null));
            Assert.AreEqual(AtlasChannel.Green, other.Mask.Channel, "the next place: Masks 1, green");
            Assert.AreEqual(2, plan.Maps.Count);
            Assert.AreEqual(0.75f, Evaluate(plan, AtlasVirtualMap.Mask2, job, 0), 1e-4f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask2, job, 1), 1e-4f, "no mask: everywhere");
        }

        [Test]
        public void MatcapsThatLookTheSameShareASlotWithEachSourcesMask()
        {
            var texture = Solid(Color.white);
            var mask = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.4f, 0f, 0f, 1f)) } };
            var a = WithMatcap(texture, mask, Color.white, (AtlasBlend.Add, 0.5f), (AtlasBlend.Multiply, 0.25f));
            var b = WithMatcap(texture, null, Color.white, (AtlasBlend.Add, 0.5f), (AtlasBlend.Multiply, 0.25f));
            var job = Job(a, b, new AtlasUnifiedMaterial());
            var instances = new AtlasMatcapFeature().Instances(job, new[] { 0, 1 });
            Assert.AreEqual(1, instances.Count, "the same look in both sources: one instance");
            CollectionAssert.AreEqual(new[] { 0, 1 }, instances[0].Sources);

            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layers = Lookups(plan);
            Assert.AreEqual(2, layers.Count, "one slot, its two weights");
            Assert.AreSame(layers[0].Mask, layers[1].Mask, "the slot's layers share their mask object");
            Assert.AreEqual(AtlasBlend.Add, layers[0].Blend);
            Assert.AreEqual(0.25f, layers[1].Opacity, 1e-5f);
            Assert.AreEqual(0.4f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 0), 1e-4f, "source 1's mask");
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 1), 1e-4f, "source 2 has it everywhere");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 2), 1e-4f, "source 3 has none");
        }

        [Test]
        public void ToonStandardApproximatesBlendsItLacksWithTheHeaviestOfAddAndMultiply()
        {
            var material = WithMatcap(Solid(Color.white), null, Color.white, (AtlasBlend.Screen, 0.6f), (AtlasBlend.Replace, 0.3f));
            var plan = AtlasOutputPipeline.Resolve(Job(material), ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Blend, "screen brightens: added");
            Assert.AreEqual(0.6f, layer.Opacity, 1e-5f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("approximated")), string.Join("\n", plan.Notes));
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Mask1), "one source, everywhere: no mask map");
        }

        [Test]
        public void ThePlacementChoicesGiveOwnSlotsCombineAndLeaveOut()
        {
            var textures = new[] { Solid(Color.red, "red"), Solid(Color.green, "green"), Solid(Color.blue, "blue") };
            var materials = textures.Select(t => WithMatcap(t, null, Color.white, (AtlasBlend.Add, 1f))).ToArray();

            // Toon Standard's one slot, automatic: the largest gets it, the rest are combined in.
            var job = Job(materials);
            var keys = new AtlasMatcapFeature().Instances(job, new[] { 0, 1, 2 }).Select(i => i.Key).ToList();
            Assert.AreEqual(3, keys.Count);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var combined = Lookups(plan).Single();
            Assert.AreEqual(1, plan.GeneratedTextures.Count);
            Assert.AreSame(plan.GeneratedTextures[0].Reference, combined.Source.Map, "the slot shows the generated texture");
            var pixel = plan.GeneratedTextures[0].Pixels()[0];
            // On different parts: averaged by area (the regions are 1/2, 1/3 and 1/6 of the atlas).
            Assert.AreEqual(0.5f, pixel.r, 1e-3f);
            Assert.AreEqual(1f / 3f, pixel.g, 1e-3f);
            Assert.AreEqual(1f / 6f, pixel.b, 1e-3f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("combines")), string.Join("\n", plan.Notes));

            // The second in its own slot, the others off.
            job = Job(materials);
            Place(job, keys[1], AtlasInstancePlacement.Own);
            Place(job, keys[0], AtlasInstancePlacement.Off);
            Place(job, keys[2], AtlasInstancePlacement.Off);
            plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreSame(textures[1], Lookups(plan).Single().Source.Map.Texture);
            Assert.AreEqual(0, plan.GeneratedTextures.Count);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("left out")), string.Join("\n", plan.Notes));

            // Poiyomi's four slots: the second combined with the first, the third on its own.
            job = Job(materials);
            Place(job, keys[1], AtlasInstancePlacement.Combined, keys[0]);
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layers = Lookups(plan);
            Assert.AreEqual(2, layers.Count, "two slots");
            Assert.AreSame(plan.GeneratedTextures.Single().Reference, layers[0].Source.Map);
            Assert.AreSame(textures[2], layers[1].Source.Map.Texture);
            Assert.AreNotSame(layers[0].Mask, layers[1].Mask);
        }

        [Test]
        public void MatcapsOnDifferentPartsAreAveragedNotAdded()
        {
            // The same matcap on three materials, and on a fourth with a glow the slot can't show:
            // one slot on Toon Standard shows the matcap as it was, not twice as bright.
            var texture = Solid(Color.white);
            var plain = Enumerable.Range(0, 3).Select(_ => WithMatcap(texture, null, Color.white, (AtlasBlend.Screen, 1f), (AtlasBlend.Mixed, 1f))).ToList();
            var glowing = WithMatcap(texture, null, Color.white, (AtlasBlend.Screen, 1f), (AtlasBlend.Mixed, 1f));
            glowing.Layers[0].EmissionAmount = 0.2f;
            var job = Job(plain[0], plain[1], glowing, plain[2]);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            var pixel = plan.GeneratedTextures.Single().Pixels()[0];
            Assert.AreEqual(1f, pixel.r, 1e-3f, "averaged: the matcap itself, not 2");

            var parts = new List<(AtlasTextureReference Map, Color Color, float Weight, string Parts, float Area)>
            {
                (null, Color.red, 1f, "a", 1f),
                (null, Color.green, 1f, "b", 3f)
            };
            var averaged = AtlasMatcapFeature.Combine(parts, AtlasBlend.Add, 2)[0];
            Assert.AreEqual(0.25f, averaged.r, 1e-5f, "by area");
            Assert.AreEqual(0.75f, averaged.g, 1e-5f);
        }

        [Test]
        public void BaseColorMixGoesIntoTheMaskWhereTheSlotHasNone()
        {
            var material = WithMatcap(Solid(Color.white), null, Color.white, (AtlasBlend.Add, 1f));
            ((AtlasLookupLayer)material.Layers[0]).BaseColorMix = 1f;
            material.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var job = Job(material);
            var toon = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(0f, Lookups(toon).Single().BaseColorMix, "folded into the mask");
            Assert.AreEqual(0.5f, Evaluate(toon, AtlasVirtualMap.Mask1, job, 0), 1e-3f, "the base color's luminance");
            Assert.IsTrue(toon.Notes.Any(n => n.Contains("approximated")), string.Join(" | ", toon.Notes));

            var poiyomi = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreEqual(1f, Lookups(poiyomi).Single().BaseColorMix, "Poiyomi mixes it itself");
            Assert.IsFalse(poiyomi.Maps.Any(m => m.Map == AtlasVirtualMap.Mask1));
        }

        [Test]
        public void MaskTexturesStartAtHalfTheMainSize()
        {
            var config = new AtlasOutputConfiguration { mainTexture = { width = 4096, height = 2048 } };
            var use = AtlasOutputOverview.TextureUses(config, Poiyomi, new[] { AtlasVirtualMap.Mask1 }, new AtlasVirtualMap[0]).Single();
            Assert.AreEqual("Masks1", use.Texture.Id);
            Assert.AreEqual(2048, use.Width);
            Assert.AreEqual(1024, use.Height, "at the main texture's aspect");
            config.textures.Add(new AtlasTextureSetting { textureId = "Masks1", enabled = true, width = 4096, height = 2048 });
            Assert.AreEqual(4096, AtlasOutputOverview.TextureUses(config, Poiyomi, new[] { AtlasVirtualMap.Mask1 }, new AtlasVirtualMap[0]).Single().Width,
                "a chosen size stays");
        }

        [Test]
        public void CombineAddsOrMultipliesTheWeightedParts()
        {
            var parts = new List<(AtlasTextureReference Map, Color Color, float Weight)>
            {
                (null, new Color(0.5f, 0f, 0f, 1f), 1f),
                (null, Color.white, 0.5f)
            };
            var added = AtlasMatcapFeature.Combine(parts, AtlasBlend.Add, 2)[0];
            Assert.AreEqual(1f, added.r, 1e-5f);
            Assert.AreEqual(0.5f, added.g, 1e-5f);
            var multiplied = AtlasMatcapFeature.Combine(new[] { ((AtlasTextureReference)null, new Color(0.5f, 0.5f, 0.5f, 1f), 0.5f) }, AtlasBlend.Multiply, 2)[0];
            Assert.AreEqual(0.75f, multiplied.r, 1e-5f, "lerp(1, 0.5, 0.5)");
        }

        [Test]
        public void TheWritersSetTheMatcapsTheParsersReadBack()
        {
            var texture = Solid(Color.white);
            var maskTexture = Solid(Color.white, "mask");
            var poiyomi = AtlasShaderRegistry.Output("poiyomi10");
            if (poiyomi.FindShader() != null)
            {
                var output = new AtlasUnifiedMaterial();
                var mask = new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Green, Invert = true };
                var map = new AtlasTextureReference { Texture = texture, Space = AtlasTextureSpace.View };
                output.Layers.Add(new AtlasLookupLayer { Mask = mask, Blend = AtlasBlend.Add, Opacity = 0.5f, Projection = AtlasLookupProjection.ViewCorrected });
                output.Layers.Add(new AtlasLookupLayer { Mask = mask, Blend = AtlasBlend.Multiply, Opacity = 0.25f, Projection = AtlasLookupProjection.ViewCorrected });
                foreach (var layer in output.Layers.OfType<AtlasLookupLayer>()) layer.Source.Map = map;
                var second = new AtlasLookupLayer { Mask = new AtlasMask(), Blend = AtlasBlend.Replace, Opacity = 1f, Projection = AtlasLookupProjection.ViewStabilized };
                second.Source.Map = map;
                second.Source.Color = new Color(2f, 1f, 1f, 1f);
                output.Layers.Add(second);
                var material = new Material(poiyomi.FindShader());
                created.Add(material);
                poiyomi.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Layers.OfType<AtlasLookupLayer>().ToList();
                Assert.AreEqual(3, parsed.Count, "Poiyomi: two slots, three weights");
                Assert.AreSame(maskTexture, parsed[0].Mask?.Map?.Texture);
                Assert.AreEqual(AtlasChannel.Green, parsed[0].Mask.Channel);
                Assert.IsTrue(parsed[0].Mask.Invert);
                Assert.AreEqual(AtlasBlend.Multiply, parsed[0].Blend, "Poiyomi's weights in its order: multiply before add");
                Assert.AreEqual(0.25f, parsed[0].Opacity, 1e-5f);
                Assert.AreEqual(AtlasBlend.Add, parsed[1].Blend);
                Assert.AreEqual(0.5f, parsed[1].Opacity, 1e-5f);
                Assert.AreSame(texture, parsed[2].Source.Map?.Texture);
                Assert.AreEqual(AtlasBlend.Replace, parsed[2].Blend);
                Assert.AreEqual(AtlasLookupProjection.ViewStabilized, parsed[2].Projection);
                Assert.AreEqual(2f, parsed[2].Source.Color.r, 1e-3f, "color × intensity");
                Assert.AreEqual(1f, parsed[2].Source.Color.g, 1e-3f);
                Assert.IsNull(parsed[2].Mask?.Map?.Texture, "a slot without a mask texture");

                var none = new Material(poiyomi.FindShader());
                created.Add(none);
                poiyomi.Write(new AtlasUnifiedMaterial(), none);
                Assert.IsFalse(AtlasShaderRegistry.Detect(none).Parse(none).Layers.OfType<AtlasLookupLayer>().Any(), "Poiyomi: no matcaps");
            }

            var toon = AtlasShaderRegistry.Output("mobiletoon");
            if (toon.FindShader() != null)
            {
                var output = new AtlasUnifiedMaterial();
                var layer = new AtlasLookupLayer
                {
                    Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Blue },
                    Blend = AtlasBlend.Multiply, Opacity = 0.7f
                };
                layer.Source.Map = new AtlasTextureReference { Texture = texture, Space = AtlasTextureSpace.View };
                output.Layers.Add(layer);
                var material = new Material(toon.FindShader());
                created.Add(material);
                toon.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Layers.OfType<AtlasLookupLayer>().Single();
                Assert.AreSame(texture, parsed.Source.Map?.Texture, "Toon Standard: matcap");
                Assert.AreSame(maskTexture, parsed.Mask?.Map?.Texture);
                Assert.AreEqual(AtlasChannel.Blue, parsed.Mask.Channel);
                Assert.AreEqual(AtlasBlend.Multiply, parsed.Blend);
                Assert.AreEqual(0.7f, parsed.Opacity, 1e-5f);

                var none = new Material(toon.FindShader());
                created.Add(none);
                toon.Write(new AtlasUnifiedMaterial(), none);
                Assert.IsFalse(none.IsKeywordEnabled("USE_MATCAP"), "Toon Standard: no matcap");
            }
        }
    }
}
