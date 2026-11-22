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
        private static AtlasOutputCapabilities Standard => AtlasShaderRegistry.Output("standard").Capabilities;

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
            var job = Job(material);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Blend, "screen brightens: added");
            Assert.AreEqual(1f, layer.Opacity, 1e-5f, "fitted: the strength is in the mask");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("approximated")), string.Join("\n", plan.Notes));
            // On white: replace 0.3 leaves 0.7 with a black matcap, and a white one gives 1.
            Assert.AreEqual(0.3f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 0), 1e-3f);
            Assert.AreEqual(0.7f, Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0), 1e-3f);
        }

        [Test]
        public void ThePlacementChoicesGiveOwnSlotsCombineAndLeaveOut()
        {
            var textures = new[] { Solid(Color.red, "red"), Solid(Color.green, "green"), Solid(Color.blue, "blue") };
            var materials = textures.Select(t => WithMatcap(t, null, Color.white, (AtlasBlend.Add, 1f))).ToArray();

            // Toon Standard's one slot, automatic: the one with the most impact gets it, the rest are combined in.
            var job = Job(materials);
            var instances = new AtlasMatcapFeature().Instances(job, new[] { 0, 1, 2 });
            Assert.AreEqual(3, instances.Count);
            string Key(string name) => instances.Single(i => i.Label.StartsWith(name)).Key;
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var combined = Lookups(plan).Single();
            Assert.AreEqual(1, plan.GeneratedTextures.Count);
            Assert.AreSame(plan.GeneratedTextures[0].Reference, combined.Source.Map, "the slot shows the generated texture");
            var pixel = plan.GeneratedTextures[0].Pixels()[0];
            // On different parts: averaged by area (1/2, 1/3 and 1/6 of the atlas) × what each
            // adds (its luminance).
            float red = 0.5f * 0.2126f, green = 1f / 3f * 0.7152f, blue = 1f / 6f * 0.0722f, total = red + green + blue;
            Assert.AreEqual(red / total, pixel.r, 1e-3f);
            Assert.AreEqual(green / total, pixel.g, 1e-3f);
            Assert.AreEqual(blue / total, pixel.b, 1e-3f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("combines")), string.Join("\n", plan.Notes));

            // The second in its own slot, the others off.
            job = Job(materials);
            Place(job, Key("green"), AtlasInstancePlacement.Own);
            Place(job, Key("red"), AtlasInstancePlacement.Off);
            Place(job, Key("blue"), AtlasInstancePlacement.Off);
            plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreSame(textures[1], Lookups(plan).Single().Source.Map.Texture);
            Assert.AreEqual(0, plan.GeneratedTextures.Count);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("left out")), string.Join("\n", plan.Notes));

            // Poiyomi's four slots: the second combined with the first, the third on its own.
            job = Job(materials);
            Place(job, Key("green"), AtlasInstancePlacement.Combined, Key("red"));
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layers = Lookups(plan);
            Assert.AreEqual(2, layers.Count, "two slots");
            Assert.AreSame(plan.GeneratedTextures.Single().Reference, layers[0].Source.Map);
            Assert.AreSame(textures[2], layers[1].Source.Map.Texture);
            Assert.AreNotSame(layers[0].Mask, layers[1].Mask);
        }

        // A matcap that only glows: one layer of weight 0 carrying the emission, as the parser
        // writes it.
        private AtlasUnifiedMaterial Glowing(string name, Color color, float glow)
        {
            var material = WithMatcap(Solid(Color.white, name), null, color, (AtlasBlend.Mixed, 0f));
            material.Layers[0].EmissionAmount = glow;
            return material;
        }

        private static void BeforeWrite(AtlasOutputPlan plan)
        {
            foreach (var step in plan.BeforeWrite) step();
        }

        // Poiyomi's four slots and five matcaps: the one with the least impact, which only
        // glows, joins the other one that only glows, not the blend with the nearest color.
        [Test]
        public void AMatcapThatOnlyGlowsIsCombinedWithAnotherThatGlows()
        {
            var materials = new[]
            {
                WithMatcap(Solid(Color.white, "first"), null, Color.white, (AtlasBlend.Add, 1f)),
                WithMatcap(Solid(Color.white, "second"), null, new Color(0.6f, 0.6f, 0.6f), (AtlasBlend.Add, 1f)),
                Glowing("glow", new Color(0.2f, 0.2f, 0.2f), 0.4f),
                WithMatcap(Solid(Color.white, "third"), null, new Color(0.2f, 0.2f, 0.2f), (AtlasBlend.Multiply, 1f)),
                Glowing("small glow", Color.white, 0.5f)
            };
            var plan = AtlasOutputPipeline.Resolve(Job(materials), Poiyomi);
            string combines = plan.Notes.Single(n => n.Contains("combines"));
            StringAssert.Contains("glow (source 3)", combines);
            StringAssert.Contains("small glow (source 5)", combines);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("show the slot's blend")), string.Join(" | ", plan.Notes));
        }

        // Combined matcaps that only glow: the texture is made from their glows, the slot doesn't
        // blend, and its one strength is fitted to both glows.
        [Test]
        public void ACombinedSlotThatOnlyGlowsKeepsTheGlow()
        {
            var job = Job(Glowing("strong", Color.white, 0.8f), Glowing("weak", Color.white, 0.4f));
            var keys = new AtlasMatcapFeature().Instances(job, new[] { 0, 1 }).Select(i => i.Key).ToList();
            Place(job, keys[1], AtlasInstancePlacement.Combined, keys[0]);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(0f, layer.Opacity, "no blend");
            // The regions are 2/3 and 1/3 of the atlas: the stacks averaged by area × glow (how
            // visible they are), the weaker glow at half.
            const float a = 2f / 3f, b = 1f / 3f;
            float strong = a * 0.8f / (a * 0.8f + b * 0.4f), weak = 1f - strong;
            Assert.AreEqual(strong * 1f + weak * 0.5f, plan.GeneratedTextures.Single().Pixels()[0].r, 1e-3f);
            BeforeWrite(plan);
            // Least squares by area: glow × part against 0.8 and 0.4.
            float expected = (a * strong * 0.8f + b * (weak * 0.5f) * 0.4f) / (a * strong * strong + b * (weak * 0.5f) * (weak * 0.5f));
            Assert.AreEqual(expected, layer.EmissionAmount, 1e-3f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("one glow strength")), string.Join(" | ", plan.Notes));
        }

        // Combined matcaps that mix in the base color differently: the mix goes into each one's
        // mask, and the glow is fitted to what each glowed with its own mix.
        [Test]
        public void CombinedMatcapsWithDifferentBaseColorMixesFoldItIntoTheMasks()
        {
            const float gray = 0.25f;
            var mixed = WithMatcap(Solid(Color.white, "mixed"), null, Color.white, (AtlasBlend.Add, 1f));
            ((AtlasLookupLayer)mixed.Layers[0]).BaseColorMix = 1f;
            mixed.Layers[0].EmissionAmount = 0.6f;
            mixed.Surface.BaseColor.Color = new Color(gray, gray, gray, 1f);
            var plain = WithMatcap(Solid(Color.white, "plain"), null, Color.white, (AtlasBlend.Add, 1f));
            plain.Layers[0].EmissionAmount = 0.6f;
            var job = Job(mixed, plain);
            var keys = new AtlasMatcapFeature().Instances(job, new[] { 0, 1 }).Select(i => i.Key).ToList();
            Place(job, keys[1], AtlasInstancePlacement.Combined, keys[0]);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(0f, layer.BaseColorMix, "the slot mixes in nothing");
            Assert.AreEqual(gray, Evaluate(plan, AtlasVirtualMap.Mask1, job, 0), 1e-3f, "the mix in the mask");
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 1), 1e-3f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("mix in the base color differently")), string.Join(" | ", plan.Notes));
            BeforeWrite(plan);
            // Regions 2/3 and 1/3; the shares by area × (weight + glow) × the mix's darkening;
            // masks 0.25 and 1; glows 0.6 × 0.25 and 0.6.
            const float a = 2f / 3f, b = 1f / 3f;
            float darkened = a * 1.6f * gray, bright = b * 1.6f;
            float sm = darkened / (darkened + bright), sp = 1f - sm;
            float expected = (a * (sm * gray) * (0.6f * gray) + b * sp * 0.6f) / (a * (sm * gray) * (sm * gray) + b * sp * sp);
            Assert.AreEqual(expected, layer.EmissionAmount, 1e-3f);
        }

        // Toon Standard's matcap doesn't glow: the glow is added to the color, merged with the
        // matcap's add since its slot takes one blend at a time. Poiyomi glows itself.
        [Test]
        public void AMatcapGlowIsAddedToTheColorWhereTheOutputDoesntGlow()
        {
            var material = WithMatcap(Solid(Color.white), null, Color.white, (AtlasBlend.Add, 0.5f));
            material.Layers[0].EmissionAmount = 0.3f;
            var plan = AtlasOutputPipeline.Resolve(Job(material), ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            Assert.AreEqual(0.8f, layer.Opacity, 1e-5f, "the add and the glow");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("don't glow")), string.Join(" | ", plan.Notes));

            plan = AtlasOutputPipeline.Resolve(Job(Glowing("glow", Color.white, 0.4f)), ToonStandard);
            layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Blend, "only the glow, as an add");
            Assert.AreEqual(0.4f, layer.Opacity, 1e-5f);

            plan = AtlasOutputPipeline.Resolve(Job(material), Poiyomi);
            layer = Lookups(plan).Single();
            Assert.AreEqual(0.5f, layer.Opacity, 1e-5f);
            Assert.AreEqual(0.3f, layer.EmissionAmount, 1e-5f);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("don't glow")), string.Join(" | ", plan.Notes));
        }

        // A matcap texture's scale and offset: Poiyomi keeps them on its slot; Toon Standard,
        // whose matcap has none, gets a generated texture sampled with them (a scale of 0 reads
        // one point everywhere: here the red half).
        [Test]
        public void AMatcapTexturesScaleAndOffsetAreKeptOrBakedIntoATexture()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { name = "halves" };
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = i % 4 < 2 ? Color.red : Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            var material = WithMatcap(texture, null, Color.white, (AtlasBlend.Add, 1f));
            var map = ((AtlasLookupLayer)material.Layers[0]).Source.Map;
            map.Tiling = Vector2.zero;
            map.Offset = new Vector2(0.25f, 0.5f);

            var poiyomi = AtlasOutputPipeline.Resolve(Job(material), Poiyomi);
            Assert.AreEqual(0, poiyomi.GeneratedTextures.Count);
            Assert.AreEqual(new Vector2(0.25f, 0.5f), Lookups(poiyomi).Single().Source.Map.Offset);

            var toon = AtlasOutputPipeline.Resolve(Job(material), ToonStandard);
            var generated = toon.GeneratedTextures.Single().Pixels();
            foreach (var pixel in new[] { generated[0], generated[generated.Length / 2], generated[generated.Length - 1] })
            {
                Assert.AreEqual(1f, pixel.r, 1e-3f);
                Assert.AreEqual(0f, pixel.g, 1e-3f, "the red half everywhere");
            }
        }

        // A glow on a dark base with a strong base color mix barely shows: sharing a slot with a
        // brighter one, it takes a small share of the texture, though its region is larger.
        [Test]
        public void ACombinedTextureIsSharedByHowVisibleTheMatcapsAre()
        {
            var faint = Glowing("faint", Color.white, 0.34f);
            ((AtlasLookupLayer)faint.Layers[0]).BaseColorMix = 0.94f;
            faint.Surface.BaseColor.Color = new Color(0.02f, 0.02f, 0.02f, 1f);
            var glass = Glowing("glass", Color.white, 0.5f);
            var job = Job(faint, glass);
            var keys = new AtlasMatcapFeature().Instances(job, new[] { 0, 1 }).Select(i => i.Key).ToList();
            Assert.AreEqual("glass (source 2)", new AtlasMatcapFeature().Instances(job, new[] { 0, 1 })[0].Label, "the one that changes the look more first");
            Place(job, keys[1], AtlasInstancePlacement.Combined, keys[0]);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            // Regions 2/3 and 1/3: by area alone the faint glow would take 2/3 of the texture.
            float faintVisible = 2f / 3f * 0.34f * Mathf.Lerp(1f, 0.02f, 0.94f), glassVisible = 1f / 3f * 0.5f;
            float faintShare = faintVisible / (faintVisible + glassVisible);
            Assert.Less(faintShare, 0.15f);
            var pixel = plan.GeneratedTextures.Single().Pixels()[0];
            // Glass at weight 1 (the strongest glow), the faint one at 0.34 / 0.5.
            Assert.AreEqual((1f - faintShare) * 1f + faintShare * 0.68f, pixel.r, 1e-3f);
        }

        // Slots are taken in the order of the matcaps' measured impact. A faint add on a white
        // base changes the look less than a similar one on a dark base, though it is on more
        // of the atlas and adds almost as much.
        [Test]
        public void SlotsAreTakenInTheOrderOfMeasuredImpact()
        {
            var onWhite = WithMatcap(Solid(Color.white, "on white"), null, Color.white, (AtlasBlend.Add, 0.2f));
            var onDark = WithMatcap(Solid(Color.white, "on dark"), null, Color.white, (AtlasBlend.Add, 0.3f));
            onDark.Surface.BaseColor.Color = new Color(0.1f, 0.1f, 0.1f, 1f);
            var job = Job(onWhite, onDark);
            Assert.AreEqual("on dark (source 2)", new AtlasMatcapFeature().Instances(job, new[] { 0, 1 })[0].Label);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreEqual("on dark", Lookups(plan)[0].Source.Map.Texture.name, "the first slot");
            Assert.AreEqual("on white", Lookups(plan)[1].Source.Map.Texture.name);
        }

        // Brighter toward the right: its value is its horizontal coordinate; upright, brighter
        // toward the top.
        private Texture2D Ramp(string name, bool upright = false)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { name = name };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = ((upright ? y : x) + 0.5f) / size;
                    pixels[y * size + x] = new Color(v, v, v, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        // Toon Standard's slot looks up by the view-space normal over the whole texture. A
        // matcap looked up otherwise gets its texture redrawn for that lookup.
        [Test]
        public void AMatcapLookedUpOtherwiseThanTheSlotIsRedrawnForIt()
        {
            float At(AtlasOutputPlan plan, float u)
            {
                var pixels = plan.GeneratedTextures.Single(t => t.Name == "Matcap1").Pixels();
                int size = AtlasMatcapFeature.CombinedSize;
                return pixels[size / 2 * size + Mathf.Clamp(Mathf.RoundToInt(u * size - 0.5f), 0, size - 1)].r;
            }

            // A border of 0.43 (tiling 0.86): the slot's right edge reads the texture at 0.93.
            var bordered = WithMatcap(Ramp("bordered"), null, Color.white, (AtlasBlend.Add, 1f));
            ((AtlasLookupLayer)bordered.Layers[0]).Tiling = new Vector2(0.86f, 0.86f);
            var plan = AtlasOutputPipeline.Resolve(Job(bordered), ToonStandard);
            Assert.AreEqual(0.93f, At(plan, 1f), 0.02f);
            Assert.AreEqual(0.5f, At(plan, 0.5f), 0.02f, "the center stays the center");
            Assert.AreEqual(Vector2.one, Lookups(plan).Single().Tiling, "the slot's own lookup");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("redrawn") && n.Contains("bordered")), string.Join(" | ", plan.Notes));

            // Looked up by the reflection, the matcap is fixed to the world and looks different
            // from each side, which the slot can't show: how it is shown is the user's choice.
            var mirrored = WithMatcap(Ramp("mirrored"), null, Color.white, (AtlasBlend.Add, 1f));
            ((AtlasLookupLayer)mirrored.Layers[0]).Projection = AtlasLookupProjection.ReflectionPlanar;
            // By the view, the default: its texture as it is, looked up like any matcap.
            plan = AtlasOutputPipeline.Resolve(Job(mirrored), ToonStandard);
            Assert.IsEmpty(plan.GeneratedTextures);
            Assert.AreEqual(AtlasLookupProjection.ViewStabilized, Lookups(plan).Single().Projection);
            Assert.IsTrue(plan.MatcapViews);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("looked up by the view instead")), string.Join(" | ", plan.Notes));
            Assert.IsFalse(AtlasOutputPipeline.Resolve(Job(bordered), ToonStandard).MatcapViews, "a border follows the view");
            // Its border is kept: redrawn as the bordered one is.
            ((AtlasLookupLayer)mirrored.Layers[0]).Tiling = new Vector2(0.86f, 0.86f);
            Assert.AreEqual(0.93f, At(AtlasOutputPipeline.Resolve(Job(mirrored), ToonStandard), 1f), 0.02f);
            ((AtlasLookupLayer)mirrored.Layers[0]).Tiling = Vector2.one;
            // A sideways texture offset is a shift in the world, which points the other way from
            // behind: by the view it is left out, so the center stays the texture's center.
            var shifted = WithMatcap(Ramp("shifted"), null, Color.white, (AtlasBlend.Add, 1f));
            ((AtlasLookupLayer)shifted.Layers[0]).Projection = AtlasLookupProjection.ReflectionPlanar;
            ((AtlasLookupLayer)shifted.Layers[0]).Source.Map.Offset = new Vector2(0.25f, 0f);
            plan = AtlasOutputPipeline.Resolve(Job(shifted), ToonStandard);
            Assert.AreEqual(0.5f, At(plan, 0.5f), 0.02f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("without its sideways texture offset")), string.Join(" | ", plan.Notes));
            // As seen from the front, the offset is the matcap's own: the center reads 0.75.
            var shiftedFront = Job(shifted);
            var frontChoice = new AtlasFeatureChoice { FeatureId = AtlasMatcapFeature.FeatureId };
            frontChoice.Values[AtlasFeatureOption.ValueId(AtlasMatcapFeature.ViewOptionId)] = ((int)AtlasMatcapView.Front, "");
            shiftedFront.Features.Add(frontChoice);
            Assert.AreEqual(0.75f, At(AtlasOutputPipeline.Resolve(shiftedFront, ToonStandard), 0.5f), 0.02f);

            // As seen from the front: halfway to the edge the normal is tilted 30°, its
            // reflection 60°, which reads 0.87 of the way out on the other side of the texture.
            var front = Job(mirrored);
            var choice = new AtlasFeatureChoice { FeatureId = AtlasMatcapFeature.FeatureId };
            choice.Values[AtlasFeatureOption.ValueId(AtlasMatcapFeature.ViewOptionId)] = ((int)AtlasMatcapView.Front, "");
            front.Features.Add(choice);
            plan = AtlasOutputPipeline.Resolve(front, ToonStandard);
            Assert.AreEqual(0.5f - 0.5f * Mathf.Sin(60f * Mathf.Deg2Rad), At(plan, 0.75f), 0.02f);
            Assert.AreEqual(AtlasLookupProjection.ViewStabilized, Lookups(plan).Single().Projection);
            var overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), ToonStandard, new Material[] { null }, new[] { mirrored });
            Assert.AreEqual(2, overview.Features.Single(s => s.Feature is AtlasMatcapFeature).Options
                .Single(o => o.Id == AtlasMatcapFeature.ViewOptionId).Choices.Length);

            // Poiyomi's slot keeps the matcap's lookup: its texture is used as it is.
            var poiyomi = AtlasOutputPipeline.Resolve(Job(mirrored), Poiyomi);
            Assert.IsEmpty(poiyomi.GeneratedTextures);
            Assert.AreEqual(AtlasLookupProjection.ReflectionPlanar, Lookups(poiyomi).Single().Projection);
        }

        // A glint: black with one small bright spot, all its light in a few texels.
        private Texture2D Glint(string name)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { name = name };
            var pixels = Enumerable.Repeat(Color.black, size * size).ToArray();
            for (int y = 15; y <= 16; y++) for (int x = 15; x <= 16; x++) pixels[y * size + x] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        // A matcap that reads black where it adds (a texture scale of 0 on a black corner, or a
        // black texture) changes nothing: left out and listed, not given a slot.
        [Test]
        public void AMatcapThatChangesNothingIsLeftOut()
        {
            var black = WithMatcap(Solid(Color.black, "black"), null, Color.white, (AtlasBlend.Screen, 1f), (AtlasBlend.Mixed, 1f));
            black.Layers[0].EmissionAmount = 0.8f;
            var shown = WithMatcap(Solid(Color.white, "shown"), null, Color.white, (AtlasBlend.Add, 0.5f));
            foreach (var capabilities in new[] { Poiyomi, ToonStandard })
            {
                var plan = AtlasOutputPipeline.Resolve(Job(black, shown), capabilities);
                Assert.AreSame(shown.Layers.OfType<AtlasLookupLayer>().First().Source.Map.Texture, Lookups(plan).Single().Source.Map.Texture);
                Assert.IsTrue(plan.Notes.Any(n => n.Contains("change nothing") && n.Contains("black (source 1)")), string.Join(" | ", plan.Notes));
            }
        }

        // Effects (most of their light in a few texels) get their own slots and aren't shared;
        // where the slots run out the weakest are left out, and environment matcaps share.
        [Test]
        public void EffectsGetTheirOwnSlotsAndTheWeakestAreLeftOut()
        {
            var shading = Enumerable.Range(0, 4).Select(i => WithMatcap(Solid(new Color(0.5f, 0.5f, 0.5f, 1f), "shading" + i), null, Color.white,
                (AtlasBlend.Add, 0.5f))).ToList();
            var strong = WithMatcap(Glint("strong glint"), null, Color.white, (AtlasBlend.Add, 1f));
            var weak = WithMatcap(Glint("weak glint"), null, Color.white, (AtlasBlend.Add, 0.2f));

            // Toon Standard's one slot: the environment matcaps share it, both effects are left out.
            var job = Job(shading[0], strong, shading[1], weak);
            var toon = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(1, Lookups(toon).Count);
            string cut = toon.Notes.Single(n => n.Contains("effects"));
            StringAssert.Contains("strong glint", cut);
            StringAssert.Contains("weak glint", cut);
            Assert.IsFalse(toon.Notes.Any(n => n.Contains("combines") && n.Contains("glint")), string.Join(" | ", toon.Notes));

            // Poiyomi's four: both effects get their own, the four shading matcaps share two.
            job = Job(shading[0], strong, shading[1], weak, shading[2], shading[3]);
            var poiyomi = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var textures = Lookups(poiyomi).Select(l => l.Source.Map.Texture).ToList();
            Assert.IsTrue(textures.Any(t => t != null && t.name == "strong glint"), "the strong effect has a slot of its own");
            Assert.IsTrue(textures.Any(t => t != null && t.name == "weak glint"));
            Assert.IsFalse(poiyomi.Notes.Any(n => n.Contains("effects")), string.Join(" | ", poiyomi.Notes));
            Assert.IsFalse(poiyomi.Notes.Any(n => n.Contains("combines") && n.Contains("glint")), string.Join(" | ", poiyomi.Notes));

            // Three slots taken by environment matcaps the user placed: one effect fits, the weak one is cut.
            job = Job(shading[0], strong, shading[1], weak, shading[2], shading[3]);
            var instances = new AtlasMatcapFeature().Instances(job, Enumerable.Range(0, 6).ToList());
            foreach (var instance in instances.Where(i => i.Label.StartsWith("shading")).Take(3))
                Place(job, instance.Key, AtlasInstancePlacement.Own);
            poiyomi = AtlasOutputPipeline.Resolve(job, Poiyomi);
            textures = Lookups(poiyomi).Select(l => l.Source.Map.Texture).ToList();
            Assert.IsTrue(textures.Any(t => t != null && t.name == "strong glint"));
            Assert.IsFalse(textures.Any(t => t != null && t.name == "weak glint"));
            StringAssert.Contains("weak glint", poiyomi.Notes.Single(n => n.Contains("effects")));
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

        // Two shading matcaps on different materials share Toon Standard's one slot, which
        // shows their averaged texture on both. Each material keeps the average look of its
        // own matcap through its base color.
        [Test]
        public void ASharedSlotKeepsEachMaterialsOwnAverageLook()
        {
            const float gray = 0.5f;
            AtlasUnifiedMaterial Shaded(float value, string name)
            {
                var material = WithMatcap(Solid(new Color(value, value, value, 1f), name), null, Color.white,
                    (AtlasBlend.Multiply, 0.8f), (AtlasBlend.Screen, 0.2f));
                material.Surface.BaseColor.Color = new Color(gray, gray, gray, 1f);
                return material;
            }

            var job = Job(Shaded(0.4f, "darker"), Shaded(0.6f, "brighter"));
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            float shown = plan.GeneratedTextures.Single(t => t.Name == "Matcap1").Pixels()[0].r;
            float Look(int source) =>
                Evaluate(plan, AtlasVirtualMap.BaseColor, job, source) * plan.Output.Surface.BaseColor.Color.r *
                Mathf.Lerp(1f, shown, layer.Opacity * Evaluate(plan, AtlasVirtualMap.Mask1, job, source));

            // The chain with a black matcap and with a white one, and between them by the texture.
            float black = gray * (1f - 0.8f), white = Mathf.Lerp(gray, 1f - (1f - gray) * (1f - 1f), 0.2f);
            Assert.AreEqual(Mathf.Lerp(black, white, 0.4f), Look(0), 0.01f, "the darker matcap's material");
            Assert.AreEqual(Mathf.Lerp(black, white, 0.6f), Look(1), 0.01f, "the brighter matcap's material");
            StringAssert.Contains("keep the average look of their own matcaps", string.Join(" | ", plan.Notes));
        }

        // Two matcaps the slot shows as they are, on different materials, share it. The slot
        // shows neither its own texture, so both are fitted and keep their own average look.
        [Test]
        public void EverySourceOfASharedSlotKeepsItsOwnAverageLook()
        {
            const float gray = 0.5f;
            AtlasUnifiedMaterial Shaded(float value, string name)
            {
                var material = WithMatcap(Solid(new Color(value, value, value, 1f), name), null, Color.white, (AtlasBlend.Multiply, 0.8f));
                material.Surface.BaseColor.Color = new Color(gray, gray, gray, 1f);
                return material;
            }

            var job = Job(Shaded(0.4f, "darker"), Shaded(0.6f, "brighter"));
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            float shown = plan.GeneratedTextures.Single(t => t.Name == "Matcap1").Pixels()[0].r;
            float Look(int source) =>
                Evaluate(plan, AtlasVirtualMap.BaseColor, job, source) * plan.Output.Surface.BaseColor.Color.r *
                Mathf.Lerp(1f, shown, layer.Opacity * Evaluate(plan, AtlasVirtualMap.Mask1, job, source));
            Assert.AreEqual(gray * Mathf.Lerp(1f, 0.4f, 0.8f), Look(0), 0.01f, "the darker matcap's material");
            Assert.AreEqual(gray * Mathf.Lerp(1f, 0.6f, 0.8f), Look(1), 0.01f, "the brighter matcap's material");
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("approximated")), string.Join(" | ", plan.Notes));
        }

        // Toon Standard's one slot, a shaded matcap on the larger material and an even one on
        // the other. Sharing would give the even one the other's shading and weaken that, so
        // the even one is kept as its average and the slot stays the shaded matcap's own.
        [Test]
        public void AnEvenMatcapIsKeptInTheColorRatherThanGivenAnothersShading()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var ramp = Ramp("shaded");
            var shaded = WithMatcap(ramp, null, Color.white, (AtlasBlend.Multiply, 0.8f));
            shaded.Surface.BaseColor.Color = gray;
            var even = WithMatcap(Solid(gray, "even"), null, Color.white, (AtlasBlend.Multiply, 0.8f));
            even.Surface.BaseColor.Color = gray;
            var job = Job(shaded, even);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreSame(ramp, Lookups(plan).Single().Source.Map.Texture, "the slot shows the shaded matcap alone");
            Assert.IsEmpty(plan.GeneratedTextures);
            Assert.AreEqual(0.5f * Mathf.Lerp(1f, 0.5f, 0.8f), Evaluate(plan, AtlasVirtualMap.BaseColor, job, 1) * plan.Output.Surface.BaseColor.Color.r, 2e-3f,
                "the even matcap's average");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("Sharing a slot")), string.Join(" | ", plan.Notes));
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("combines")), string.Join(" | ", plan.Notes));
        }

        // Two shaded matcaps on different materials, lit from the side and from above: sharing
        // the slot keeps most of both, and the log says how much.
        [Test]
        public void ASharedSlotSaysHowMuchShadingItKeeps()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var side = WithMatcap(Ramp("side"), null, Color.white, (AtlasBlend.Multiply, 0.8f));
            side.Surface.BaseColor.Color = gray;
            var above = WithMatcap(Ramp("above", true), null, Color.white, (AtlasBlend.Multiply, 0.8f));
            above.Surface.BaseColor.Color = gray;
            var plan = AtlasOutputPipeline.Resolve(Job(side, above), ToonStandard);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("combines")), string.Join(" | ", plan.Notes));
            string kept = plan.Notes.Single(n => n.Contains("level and contrast"));
            StringAssert.Contains("side (source 1)", kept);
            StringAssert.Contains("above (source 2)", kept);
        }

        // The log names a bake only where it would keep clearly more of a lighting matcap's
        // shading; a spot or a highlight would stay in place.
        [Test]
        public void TheLogNamesABakeOnlyWhereItKeepsClearlyMore()
        {
            var lighting = new AtlasMatcapMeasure { Role = AtlasMatcapRole.Lighting, BakeShadingLoss = 0.1f, BakeLoss = 1.5f };
            Assert.IsTrue(AtlasMatcapFeature.BakeKeepsMore(lighting, 0.5f));
            Assert.IsFalse(AtlasMatcapFeature.BakeKeepsMore(lighting, 0.3f), "within a quarter of its shading");
            var pearl = new AtlasMatcapMeasure { Role = AtlasMatcapRole.Lighting, BakeShadingLoss = 0.1f, Standout = 1.2f };
            Assert.IsFalse(AtlasMatcapFeature.BakeKeepsMore(pearl, 0.9f), "its spot would stay in place");
            var glint = new AtlasMatcapMeasure { Role = AtlasMatcapRole.Highlight, BakeShadingLoss = 0f, BakeLoss = 0f };
            Assert.IsFalse(AtlasMatcapFeature.BakeKeepsMore(glint, 0.9f));
        }

        // A matcap placed as its average takes no slot: its average effect is in the base color.
        [Test]
        public void AMatcapPlacedAsItsAverageGoesIntoTheBaseColor()
        {
            foreach (var capabilities in new[] { Poiyomi, ToonStandard })
            {
                var material = WithMatcap(Solid(new Color(0.5f, 0.5f, 0.5f, 1f)), null, Color.white, (AtlasBlend.Multiply, 0.8f), (AtlasBlend.Add, 0.2f));
                material.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
                var job = Job(material);
                Place(job, new AtlasMatcapFeature().Instances(job, new[] { 0 }).Single().Key, AtlasInstancePlacement.Averaged);
                var plan = AtlasOutputPipeline.Resolve(job, capabilities);
                Assert.IsEmpty(Lookups(plan));
                Assert.IsEmpty(plan.GeneratedTextures);
                Assert.AreEqual(0, plan.MatcapSlotsUsed);
                // The base color × lerp(1, 0.5, 0.8), plus 0.5 × 0.2.
                Assert.AreEqual(0.5f * 0.6f + 0.1f, Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r, 2e-3f);
                Assert.IsTrue(plan.Notes.Any(n => n.Contains("Average in color")), string.Join(" | ", plan.Notes));
            }
        }

        // A lighting matcap that ends up without a slot (here: combined with a matcap that
        // isn't there, and no slot to join) keeps its average effect, where its mask is.
        [Test]
        public void ALightingMatcapWithoutASlotKeepsItsAverage()
        {
            var mask = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.5f, 0f, 0f, 1f), "mask") } };
            var material = WithMatcap(Solid(new Color(0.5f, 0.5f, 0.5f, 1f)), mask, Color.white, (AtlasBlend.Multiply, 0.8f));
            material.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var job = Job(material);
            Place(job, new AtlasMatcapFeature().Instances(job, new[] { 0 }).Single().Key, AtlasInstancePlacement.Combined, "gone");
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.IsEmpty(Lookups(plan));
            // The matcap's mean 0.5 at weight 0.8 × the mask 0.5.
            Assert.AreEqual(0.5f * Mathf.Lerp(1f, 0.5f, 0.4f), Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r, 2e-3f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("No matcap slot is left")), string.Join(" | ", plan.Notes));
        }

        // An output without matcap slots lights the form itself. A lighting matcap keeps its
        // average effect in the base color there: baked as shading it would shade the form a
        // second time, and stay in place from every view. A highlight has no average to keep.
        // A bake is the user's choice.
        [Test]
        public void WithoutSlotsALightingMatcapKeepsItsAverageAndAHighlightIsLeftOut()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var lit = WithMatcap(Solid(gray), null, Color.white, (AtlasBlend.Multiply, 0.8f));
            lit.Surface.BaseColor.Color = gray;
            var shiny = WithMatcap(Glint("glint"), null, Color.white, (AtlasBlend.Add, 1f));
            shiny.Surface.BaseColor.Color = gray;
            var job = Job(lit, shiny);
            var feature = new AtlasMatcapFeature();
            CollectionAssert.AreEqual(new[] { "average", "baked", "off" }, AtlasOutputFeatures.Available(feature, Standard).Select(m => m.Id).ToArray());
            Assert.IsFalse(AtlasOutputFeatures.Available(feature, Poiyomi).Any(m => m.Id == "average"), "only without slots");

            var plan = AtlasOutputPipeline.Resolve(job, Standard);
            Assert.AreEqual("average", plan.Modes[AtlasMatcapFeature.FeatureId].Id);
            Assert.IsEmpty(Lookups(plan));
            float tint = plan.Output.Surface.BaseColor.Color.r;
            Assert.AreEqual(0.5f * Mathf.Lerp(1f, 0.5f, 0.8f), Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * tint, 2e-3f, "× the matcap's mean");
            Assert.AreEqual(0.5f, Evaluate(plan, AtlasVirtualMap.BaseColor, job, 1) * tint, 2e-3f, "the highlight is left out");
            string notes = string.Join(" | ", plan.Notes);
            StringAssert.Contains("these matcaps keep their average effect in the base color: matcap (source 1)", notes);
            StringAssert.Contains("these matcap rims and highlights are left out: glint (source 2)", notes);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("baked into the textures")), notes);
            Assert.IsTrue(plan.Ways.Values.Any(w => w.LeftOut), "the inspector says the highlight is left out");

            // Chosen, the lighting matcap is drawn as shading.
            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasMatcapFeature.FeatureId, ModeId = "baked" });
            plan = AtlasOutputPipeline.Resolve(job, Standard);
            Assert.AreEqual("baked", plan.Modes[AtlasMatcapFeature.FeatureId].Id);
            StringAssert.Contains("Matcaps baked into the textures", string.Join(" | ", plan.Notes));
        }

        // An averaged matcap's glow keeps its average in the emission, where the output has an
        // emission map: it still glows in the dark. Without one it goes into the color, lit.
        [Test]
        public void WithoutSlotsAMatcapsGlowKeepsItsAverageInTheEmission()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var material = WithMatcap(Solid(gray), null, Color.white, (AtlasBlend.Multiply, 0.8f));
            material.Surface.BaseColor.Color = gray;
            ((AtlasLookupLayer)material.Layers[0]).EmissionAmount = 0.6f;
            var job = Job(material, new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Standard);
            string notes = string.Join(" | ", plan.Notes);
            StringAssert.Contains("is kept as its average in the emission", notes);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("added to the color")), notes);
            var emission = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Emission);
            var target = plan.Output.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Source.Map == emission.Reference)
                .Targets.Single(t => t.Target == AtlasSurfaceTarget.Emission);
            using (var glow = emission.Evaluate(0, job.Sources[0].Material, 4, 4))
            using (var none = emission.Evaluate(1, job.Sources[1].Material, 4, 4))
            {
                Assert.AreEqual(0.5f * 0.6f, glow.GetPixel(1, 1).r * target.Opacity, 2e-3f, "the matcap's mean × its glow");
                Assert.AreEqual(0f, none.GetPixel(1, 1).r, 1e-4f, "dark on the source without one");
            }
            // The color holds the matcap's blend alone.
            Assert.AreEqual(0.5f * Mathf.Lerp(1f, 0.5f, 0.8f), Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r, 2e-3f);

            // A glow that follows the light is lit: it goes into the color.
            ((AtlasLookupLayer)material.Layers[0]).HideInShadow = 1f;
            plan = AtlasOutputPipeline.Resolve(job, Standard);
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Emission), "nothing glows without light");
            StringAssert.Contains("that follows the light is added to the color", string.Join(" | ", plan.Notes));
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

        // Poiyomi multiplies by lerp(1, matcap × lerp(1, base, mix), weight); Toon Standard by
        // lerp(1, matcap, strength), so the fit goes into the mask and the base color map.
        [Test]
        public void AMultipliedMatcapsBaseColorMixGoesIntoTheMaskAndTheBaseColor()
        {
            const float weight = 0.8f, gray = 0.5f;
            var material = WithMatcap(Solid(Color.white), null, Color.white, (AtlasBlend.Multiply, weight));
            ((AtlasLookupLayer)material.Layers[0]).BaseColorMix = 1f;
            material.Surface.BaseColor.Color = new Color(gray, gray, gray, 1f);
            var job = Job(material);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            Assert.AreEqual(0f, layer.BaseColorMix);
            float strength = layer.Opacity * Evaluate(plan, AtlasVirtualMap.Mask1, job, 0);
            float baseColor = Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r;
            foreach (float matcap in new[] { 0f, 0.25f, 1f })
                Assert.AreEqual(gray * Mathf.Lerp(1f, matcap * gray, weight), baseColor * Mathf.Lerp(1f, matcap, strength), 1e-3f,
                    "exact for one matcap on a gray base: matcap " + matcap);
        }

        [Test]
        public void StackedMultipliedMatcapsWithBaseColorMixMatchWithoutAndWithFullMatcaps()
        {
            const float gray = 0.6f;
            var material = WithMatcap(Solid(Color.white, "a"), null, new Color(0.5f, 0.5f, 0.5f, 1f), (AtlasBlend.Multiply, 0.8f));
            material.Layers.AddRange(WithMatcap(Solid(Color.white, "b"), null, new Color(0.9f, 0.9f, 0.9f, 1f), (AtlasBlend.Multiply, 0.6f)).Layers);
            foreach (var lookup in material.Layers.OfType<AtlasLookupLayer>()) lookup.BaseColorMix = 1f;
            material.Surface.BaseColor.Color = new Color(gray, gray, gray, 1f);
            var job = Job(material);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            float strength = layer.Opacity * Evaluate(plan, AtlasVirtualMap.Mask1, job, 0);
            float baseColor = Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r;
            // The combined texture with the white matcaps; black ones give the weights' leftover.
            float full = plan.GeneratedTextures.Single(t => t.Name == "Matcap1").Pixels()[0].r;
            float none = (1f - 0.8f) * (1f - 0.6f);
            Assert.AreEqual(gray * none, baseColor * Mathf.Lerp(1f, none, strength), 1e-3f, "black matcaps");
            // The second matcap mixes in the base color as the first left it.
            float first = gray * (1f - 0.8f + 0.8f * 0.5f * gray);
            Assert.AreEqual(first * (1f - 0.6f + 0.6f * 0.9f * first), baseColor * Mathf.Lerp(1f, full, strength), 1e-3f, "white matcaps");
        }

        // Multiply and screen both rise with the matcap: one multiplied matcap over a brighter
        // base color shows both, exact where the matcap is black or white.
        [Test]
        public void AScreenTheSlotLacksGoesIntoABrighterBaseColor()
        {
            const float gray = 0.5f;
            var material = WithMatcap(Solid(Color.white), null, Color.white, (AtlasBlend.Multiply, 0.8f), (AtlasBlend.Screen, 0.4f));
            material.Surface.BaseColor.Color = new Color(gray, gray, gray, 1f);
            var job = Job(material);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = Lookups(plan).Single();
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            float strength = layer.Opacity * Evaluate(plan, AtlasVirtualMap.Mask1, job, 0);
            float baseColor = Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r;
            Assert.Greater(baseColor, gray, "brighter than the source's");
            float multiplied = gray * (1f - 0.8f);
            Assert.AreEqual(multiplied, baseColor * Mathf.Lerp(1f, 0f, strength), 1e-3f, "black matcap");
            Assert.AreEqual(Mathf.Lerp(gray, 1f, 0.4f), baseColor * Mathf.Lerp(1f, 1f, strength), 1e-3f, "white matcap");
        }

        // Toon Standard's slot, on an output whose blocked view shows the base color alone.
        private static AtlasOutputCapabilities WithFallback()
        {
            var capabilities = new AtlasOutputCapabilities();
            AtlasFieldwise.Copy(ToonStandard, capabilities);
            capabilities.VRChatFallback = true;
            return capabilities;
        }

        // A matcap of 0.9 that multiplies by 0.5 and adds 0.4, on a base color of 0.5: the
        // source shows 0.25 where the matcap is black, 0.9 where it is white, and 0.835 at the
        // matcap's own value. A multiplying slot shows it over a raised base color.
        private AtlasGeneratorJob BrightensAndDarkens(AtlasBaseColorShift shift)
        {
            var material = WithMatcap(Solid(new Color(0.9f, 0.9f, 0.9f, 1f)), null, Color.white, (AtlasBlend.Multiply, 0.5f), (AtlasBlend.Add, 0.4f));
            material.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var job = Job(material);
            var choice = new AtlasFeatureChoice { FeatureId = AtlasMatcapFeature.FeatureId };
            choice.Values[AtlasFeatureOption.ValueId(AtlasMatcapFeature.ShiftOptionId)] = ((int)shift, "");
            job.Features.Add(choice);
            return job;
        }

        // The fitted base color (with the shared color) and the slot's strength.
        private static (float BaseColor, float Strength) Fitted(AtlasOutputPlan plan, AtlasGeneratorJob job) =>
            (Evaluate(plan, AtlasVirtualMap.BaseColor, job, 0) * plan.Output.Surface.BaseColor.Color.r,
                Lookups(plan).Single().Opacity * Evaluate(plan, AtlasVirtualMap.Mask1, job, 0));

        [Test]
        public void ABaseColorShiftStopsAtTheAverageLookWhereTheBlockedViewShowsTheBaseColor()
        {
            var job = BrightensAndDarkens(AtlasBaseColorShift.Automatic);
            var plan = AtlasOutputPipeline.Resolve(job, WithFallback());
            var (baseColor, strength) = Fitted(plan, job);
            Assert.AreEqual(0.835f, baseColor, 1e-3f, "raised to the average look, not to the brightest");
            Assert.AreEqual(0.25f, baseColor * Mathf.Lerp(1f, 0f, strength), 1e-3f, "a black matcap still shows the darkest");
            Assert.IsTrue(plan.BaseColorShifts, "the inspector offers the choice");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("average look")), string.Join(" | ", plan.Notes));

            job = BrightensAndDarkens(AtlasBaseColorShift.Full);
            plan = AtlasOutputPipeline.Resolve(job, WithFallback());
            (baseColor, strength) = Fitted(plan, job);
            Assert.AreEqual(0.9f, baseColor, 1e-3f, "as far as the matcap needs");
            Assert.AreEqual(0.25f, baseColor * Mathf.Lerp(1f, 0f, strength), 1e-3f);

            job = BrightensAndDarkens(AtlasBaseColorShift.Keep);
            plan = AtlasOutputPipeline.Resolve(job, WithFallback());
            (baseColor, strength) = Fitted(plan, job);
            Assert.AreEqual(0.5f, baseColor, 1e-3f, "the surface's own color");
            Assert.AreEqual(0.25f, baseColor * Mathf.Lerp(1f, 0f, strength), 1e-3f, "the darkening side is kept");
            Assert.IsFalse(plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Contributions.Any(c => c.CanRaise));
        }

        [Test]
        public void AnOutputWhoseBlockedViewIsTheSameLookShiftsAsFarAsTheMatcapNeeds()
        {
            // The stored choice is another output shader's: it doesn't apply here.
            var job = BrightensAndDarkens(AtlasBaseColorShift.Keep);
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(0.9f, Fitted(plan, job).BaseColor, 1e-3f);
            Assert.IsFalse(plan.BaseColorShifts, "nothing to choose");
        }

        [Test]
        public void TheInspectorOffersTheBaseColorShiftWhileASlotIsFitted()
        {
            var fitted = BrightensAndDarkens(AtlasBaseColorShift.Automatic).Sources[0].Material;
            var overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), WithFallback(), new Material[] { null }, new[] { fitted });
            var matcaps = overview.Features.Single(s => s.Feature is AtlasMatcapFeature);
            var option = matcaps.Options.Single(o => o.Id == AtlasMatcapFeature.ShiftOptionId);
            Assert.AreEqual(3, option.Choices.Length);

            // A matcap the slot shows as it is needs no shift.
            var plain = WithMatcap(Solid(Color.white), null, Color.white, (AtlasBlend.Add, 0.5f));
            overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), WithFallback(), new Material[] { null }, new[] { plain });
            Assert.IsFalse(overview.Features.Single(s => s.Feature is AtlasMatcapFeature).Options.Any(o => o.Id == AtlasMatcapFeature.ShiftOptionId));
            // Each matcap's row says how it ends up shown.
            Assert.AreEqual("In matcap slot 1", overview.Features.Single(s => s.Feature is AtlasMatcapFeature).Instances.Single().Way);
        }

        [Test]
        public void TheOverviewListsGeneratedAndReusedTexturesWithTheirMemory()
        {
            var materials = new List<Material> { null, null };
            var unified = new List<AtlasUnifiedMaterial>
            {
                WithMatcap(Solid(Color.white, "shine"), null, Color.white, (AtlasBlend.Add, 1f)),
                WithMatcap(Solid(Color.red, "red"), null, Color.white, (AtlasBlend.Add, 1f))
            };
            var config = new AtlasOutputConfiguration { mainTexture = { width = 1024, height = 1024 } };
            // Toon Standard's one slot: the two combined into a generated texture.
            var overview = AtlasOutputOverview.Build(config, ToonStandard, materials, unified);
            var generated = overview.Textures.Single(t => t.Generated && t.Texture.Id == "Matcap1");
            Assert.AreEqual((512, 512), (generated.Width, generated.Height));
            Assert.AreEqual(AtlasTextureMemory.Bytes(512, 512, AtlasTextureMemory.Format(config.pcFormats, AtlasTextureKind.Color)), generated.PcBytes);
            Assert.AreEqual(overview.Textures.Sum(t => t.Enabled ? t.PcBytes : 0), overview.PcBytes, "in the total");

            // Poiyomi: each in a slot of its own, the materials' textures used as they are.
            overview = AtlasOutputOverview.Build(config, Poiyomi, materials, unified);
            var reused = overview.Textures.Where(t => t.Source != null).ToList();
            CollectionAssert.AreEquivalent(new[] { "shine", "red" }, reused.Select(t => t.Source.name).ToArray());
            Assert.Greater(reused[0].PcBytes, 0);
            Assert.IsFalse(overview.Textures.Any(t => t.Generated && t.Texture.Id.StartsWith("Matcap")));
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
