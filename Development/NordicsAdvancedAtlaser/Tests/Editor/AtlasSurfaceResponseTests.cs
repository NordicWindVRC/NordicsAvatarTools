using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Reflection and specular, and ambient occlusion (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasSurfaceResponseTests
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

        private static float Value(NordicsTextureWrapper map) => map.GetPixel(1, 1).r;

        private static float Evaluate(AtlasScalarInput input)
        {
            using (var map = AtlasMapEvaluator.Scalar(input, 4, 4)) return Value(map);
        }

        [Test]
        public void AScalarInputIsItsConstantTimesItsChannel()
        {
            var map = new AtlasTextureReference { Texture = Solid(new Color(0f, 0.8f, 0f, 1f)), Interpretation = AtlasTextureInterpretation.Data };
            Assert.AreEqual(0.3f, Evaluate(new AtlasScalarInput(0.3f)), 1e-4f, "a constant");
            Assert.AreEqual(0.4f, Evaluate(new AtlasScalarInput(0.5f) { Map = map, Channel = AtlasChannel.Green }), 1e-4f, "constant × channel");
            Assert.AreEqual(0.1f, Evaluate(new AtlasScalarInput(0.5f) { Map = map, Channel = AtlasChannel.Green, InvertSample = true }), 1e-4f, "the sample inverted");
            Assert.AreEqual(0.2f, Evaluate(new AtlasScalarInput(1f) { Map = map, Channel = AtlasChannel.Green, InvertResult = true }), 1e-4f, "smoothness as roughness");
            var occlusion = new AtlasScalarInput(0.5f) { Map = map, Channel = AtlasChannel.Green, Combine = AtlasScalarCombine.StrengthAroundOne };
            Assert.AreEqual(0.9f, Evaluate(occlusion), 1e-4f, "strength around one: 1 − (1 − 0.8) × 0.5");

            var remapped = new AtlasScalarInput(0.5f) { Map = map, Channel = AtlasChannel.Green, Remap = new AtlasScalarRemap() };
            Assert.IsFalse(AtlasMapEvaluator.CanEvaluate(remapped));
            Assert.AreEqual(0.5f, Evaluate(remapped), 1e-4f, "outside the subset: the constant alone");
        }

        private static AtlasUnifiedMaterial Lit(float highlights, float reflections, float metallic = 0f, float roughness = 0.5f)
        {
            var material = new AtlasUnifiedMaterial();
            material.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            var response = material.Surface.Response;
            response.Highlights.Strength = highlights;
            response.Highlights.Enabled = highlights > 0f;
            response.Reflections.Strength = reflections;
            response.Reflections.Enabled = reflections > 0f;
            response.Metallic.Constant = metallic;
            response.Roughness.Constant = roughness;
            return material;
        }

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < materials.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource { Material = materials[i], UVRegion = new Rect(i / (float)materials.Length, 0f, 1f / materials.Length, 1f) });
            return job;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;

        private static float Evaluate(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index)
        {
            var request = plan.Maps.Single(m => m.Map == map);
            using (var result = request.Evaluate(index, job.Sources[index].Material, 4, 4)) return Value(result);
        }

        [Test]
        public void HighlightAndReflectionStrengthsGoIntoMasksRelativeToTheLargest()
        {
            var flat = new AtlasUnifiedMaterial();
            flat.Lighting.Family = AtlasLightingFamily.Flat;
            Assert.IsFalse(new AtlasSpecularFeature().IsPresent(flat), "flat lighting shows no highlights");
            var job = Job(Lit(2f, 1f, metallic: 0.6f, roughness: 0.25f), Lit(1f, 0f), flat);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var response = plan.Output.Surface.Response;
            Assert.AreEqual(2f, response.Highlights.Strength);
            Assert.AreEqual(1f, response.Reflections.Strength);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.SpecularMask, job, 0), 1e-4f, "the strongest");
            Assert.AreEqual(0.5f, Evaluate(plan, AtlasVirtualMap.SpecularMask, job, 1), 1e-4f, "half of the strongest");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.ReflectionMask, job, 1), 1e-4f, "reflections off");
            Assert.AreEqual(0.6f, Evaluate(plan, AtlasVirtualMap.Metallic, job, 0), 1e-4f);
            Assert.AreEqual(0.25f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 0), 1e-4f);
            // The flat one: no metallic, fully rough, no highlights or reflections.
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Metallic, job, 2), 1e-4f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 2), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.SpecularMask, job, 2), 1e-4f);

            // Toon Standard has no masks: only metallic and roughness.
            var toon = AtlasOutputPipeline.Resolve(Job(Lit(1f, 1f)), AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            CollectionAssert.AreEquivalent(new[] { AtlasVirtualMap.Metallic, AtlasVirtualMap.Roughness },
                toon.Maps.Select(m => m.Map).Where(m => m != AtlasVirtualMap.BaseColor).ToArray());
        }

        [Test]
        public void OcclusionIsBakedAndSourcesWithoutItAreUnoccluded()
        {
            var occluded = new AtlasUnifiedMaterial();
            occluded.Surface.Response.Occlusion.Map = new AtlasTextureReference { Texture = Solid(new Color(0.2f, 0.2f, 0.2f, 1f)), Interpretation = AtlasTextureInterpretation.Data };
            occluded.Surface.Response.Occlusion.Constant = 0.5f;
            var job = Job(occluded, new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var output = plan.Output.Surface.Response.Occlusion;
            Assert.AreEqual(AtlasScalarCombine.StrengthAroundOne, output.Combine);
            Assert.AreEqual(1f, output.Constant);
            Assert.AreEqual(0.6f, Evaluate(plan, AtlasVirtualMap.Occlusion, job, 0), 1e-4f, "1 − (1 − 0.2) × 0.5");
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Occlusion, job, 1), 1e-4f);
        }

        [Test]
        public void SmoothnessIsStoredInvertedNextToMetallicAndTheMasks()
        {
            var job = Job(Lit(1f, 1f, metallic: 0.6f, roughness: 0.25f), Lit(1f, 1f));
            var capabilities = Poiyomi;
            var plan = AtlasOutputPipeline.Resolve(job, capabilities);
            var config = new AtlasOutputConfiguration { mainTexture = { width = 256, height = 128 } };
            var use = AtlasOutputOverview.TextureUses(config, capabilities, plan.Maps.Select(m => m.Map).Distinct().ToList(), new AtlasVirtualMap[0])
                .Single(u => u.Texture.Id == "Metallic");
            using (var packed = AtlasTexturePacker.Pack(use, plan, job))
            {
                var c = packed.GetPixel(packed.Width / 4, packed.Height / 2);
                Assert.AreEqual(0.6f, c.r, 0.01f, "metallic");
                Assert.AreEqual(0.75f, c.g, 0.01f, "smoothness = 1 − roughness");
                Assert.AreEqual(1f, c.b, 0.01f, "reflection mask");
                Assert.AreEqual(1f, c.a, 0.01f, "specular mask");
            }
        }

        [Test]
        public void TheWritersSetTheSurfaceMapsTheParsersReadBack()
        {
            var surface = Solid(new Color(0.6f, 0.75f, 0.5f, 1f));
            var occlusionTexture = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders)
            {
                var unified = new AtlasUnifiedMaterial();
                var response = unified.Surface.Response;
                response.Metallic.Constant = 1f;
                response.Metallic.Map = new AtlasTextureReference { Texture = surface };
                response.Roughness.Constant = 1f;
                response.Roughness.Map = new AtlasTextureReference { Texture = surface };
                response.Highlights.Strength = 1f;
                response.Reflections.Strength = 1f;
                response.Occlusion.Constant = 1f;
                // Poiyomi keeps occlusion in its own texture; Toon Standard in the surface texture.
                response.Occlusion.Map = new AtlasTextureReference { Texture = shader.ShaderId == "poiyomi10" ? occlusionTexture : surface };
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(unified, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Surface.Response;
                Assert.AreSame(surface, parsed.Metallic.Map?.Texture, shader.DisplayName + ": metallic");
                Assert.AreEqual(AtlasChannel.Red, parsed.Metallic.Channel, shader.DisplayName);
                Assert.AreSame(surface, parsed.Roughness.Map?.Texture, shader.DisplayName + ": roughness");
                Assert.AreEqual(AtlasChannel.Green, parsed.Roughness.Channel, shader.DisplayName);
                Assert.IsTrue(parsed.Roughness.InvertResult, shader.DisplayName + ": stored as smoothness");
                Assert.AreSame(response.Occlusion.Map.Texture, parsed.Occlusion.Map?.Texture, shader.DisplayName + ": occlusion");
                Assert.AreEqual(1f, parsed.Occlusion.Constant, 1e-5f, shader.DisplayName);

                var without = new Material(shader.FindShader());
                created.Add(without);
                shader.Write(new AtlasUnifiedMaterial(), without);
                var none = AtlasShaderRegistry.Detect(without).Parse(without).Surface.Response;
                Assert.IsNull(none.Metallic.Map, shader.DisplayName + ": no metallic map");
                Assert.IsNull(none.Occlusion.Map, shader.DisplayName + ": no occlusion map");
            }
        }
    }
}
