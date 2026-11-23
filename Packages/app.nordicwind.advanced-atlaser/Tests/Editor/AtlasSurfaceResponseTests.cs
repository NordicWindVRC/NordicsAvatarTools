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

        // A material of Toon Standard, as its parser reads the shader's own fresnel.
        private static AtlasUnifiedMaterial OfToonStandard(AtlasUnifiedMaterial material)
        {
            material.Surface.Response.Fresnel = AtlasFresnel.ViewHalfTimesViewNormal;
            material.Surface.Response.ReflectanceFade = 0.25f;
            return material;
        }

        // The fit reads the output shader's fresnel from a material of the shader.
        private static AtlasOutputCapabilities ToonStandardInstalled()
        {
            var shader = AtlasShaderRegistry.Output("mobiletoon");
            if (shader.FindShader() == null) Assert.Ignore("VRChat Mobile Toon Standard is not installed.");
            return shader.Capabilities;
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

        private static AtlasUnifiedMaterial Coated(float strength, float roughness, Color tint)
        {
            var material = Lit(1f, 1f);
            var coat = material.Surface.Response.ClearCoat = new AtlasClearCoat();
            coat.Strength.Constant = strength;
            coat.Roughness.Constant = roughness;
            coat.Reflections.Tint = tint;
            return material;
        }

        [Test]
        public void ClearCoatStrengthAndRoughnessAreBakedPerSourceWithOneSetup()
        {
            var job = Job(Coated(0.8f, 0.2f, Color.red), Coated(1f, 0.6f, Color.white), Lit(1f, 1f));
            Assert.IsTrue(new AtlasClearCoatFeature().SharedValuesDiffer(job.Sources.Select(s => s.Material).ToList(), Poiyomi));
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var coat = plan.Output.Surface.Response.ClearCoat;
            Assert.IsNotNull(coat);
            Assert.AreEqual(0.8f, Evaluate(plan, AtlasVirtualMap.ClearCoatStrength, job, 0), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.ClearCoatStrength, job, 2), 1e-4f, "no coat");
            Assert.AreEqual(0.6f, Evaluate(plan, AtlasVirtualMap.ClearCoatRoughness, job, 1), 1e-4f);
            // Reflections everywhere a coat is, nowhere else: one mask, read by the highlights too.
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.ClearCoatReflectionMask, job, 0), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.ClearCoatReflectionMask, job, 2), 1e-4f);
            Assert.AreSame(coat.Reflections.Mask.Map, coat.Highlights.Mask.Map);
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.ClearCoatSpecularMask));
            Assert.AreEqual(Color.red, coat.Reflections.Tint, "from the largest region");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("clear coat is taken from source 1")), string.Join(" | ", plan.Notes));

            var toon = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.IsNull(toon.Output.Surface.Response.ClearCoat);
            Assert.IsTrue(toon.Notes.Any(n => n.Contains("has no clear coat")), string.Join(" | ", toon.Notes));
        }

        [Test]
        public void WithoutACoatTheOutputTakesItIntoTheGloss()
        {
            var toonStandard = AtlasShaderRegistry.Output("mobiletoon").Capabilities;
            // Base roughness 0.5 (gloss 0.5) under a coat of roughness 0.1 (gloss 0.9).
            var job = Job(Coated(1f, 0.1f, Color.white), Lit(1f, 1f), Coated(0.5f, 0.1f, Color.white));
            var plan = AtlasOutputPipeline.Resolve(job, toonStandard);
            Assert.AreEqual("gloss", plan.Modes[AtlasClearCoatFeature.FeatureId].Id);
            Assert.AreEqual(0.1f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 0), 1e-4f, "fully coated: the coat's gloss");
            Assert.AreEqual(0.5f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 1), 1e-4f, "no coat: unchanged");
            Assert.AreEqual(0.3f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 2), 1e-4f, "half coated: halfway, gloss 0.7");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("raises the gloss")), string.Join(" | ", plan.Notes));

            // A coat on a material without highlights of its own: the coat brings the surface maps.
            var coatOnly = Lit(0f, 0f);
            coatOnly.Surface.Response.ClearCoat = new AtlasClearCoat();
            coatOnly.Surface.Response.ClearCoat.Roughness.Constant = 0.2f;
            job = Job(coatOnly, Lit(0f, 0f));
            plan = AtlasOutputPipeline.Resolve(job, toonStandard);
            Assert.IsTrue(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Metallic));
            Assert.AreEqual(0.2f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 0), 1e-4f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Roughness, job, 1), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Metallic, job, 0), 1e-4f);
            Assert.IsTrue(plan.Output.Surface.Response.Highlights.Enabled);

            Assert.AreEqual("native", AtlasOutputPipeline.Resolve(job, Poiyomi).Modes[AtlasClearCoatFeature.FeatureId].Id, "Poiyomi keeps its coat");
        }

        [Test]
        public void TheWriterSetsTheClearCoatTheParserReadsBack()
        {
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            var packed = Solid(new Color(0.5f, 0.5f, 0.5f, 0.5f));
            var unified = new AtlasUnifiedMaterial();
            var coat = unified.Surface.Response.ClearCoat = new AtlasClearCoat();
            coat.Strength.Map = new AtlasTextureReference { Texture = packed };
            coat.Roughness.Map = new AtlasTextureReference { Texture = packed };
            coat.Reflections.Mask = new AtlasMask { Map = coat.Strength.Map };
            coat.Highlights.Mask = new AtlasMask { Map = coat.Strength.Map };
            coat.Reflections.Strength = 0.7f;
            coat.Reflections.Tint = new Color(1f, 0.5f, 0.25f, 1f);
            coat.Highlights.Strength = 2f;
            coat.NormalMapAmount = 0.4f;
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(unified, material);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Surface.Response.ClearCoat;
            Assert.IsNotNull(parsed);
            Assert.AreSame(packed, parsed.Strength.Map?.Texture);
            Assert.AreEqual(AtlasChannel.Red, parsed.Strength.Channel);
            Assert.AreSame(packed, parsed.Roughness.Map?.Texture);
            Assert.AreEqual(AtlasChannel.Green, parsed.Roughness.Channel);
            Assert.IsTrue(parsed.Roughness.InvertResult, "stored as smoothness");
            Assert.AreEqual(AtlasChannel.Blue, parsed.Reflections.Mask.Channel);
            Assert.AreEqual(AtlasChannel.Blue, parsed.Highlights.Mask.Channel, "shared with the reflection mask");
            Assert.AreEqual(0.7f, parsed.Reflections.Strength, 1e-4f);
            Assert.AreEqual(2f, parsed.Highlights.Strength, 1e-4f);
            Assert.AreEqual(0.25f, parsed.Reflections.Tint.b, 1e-3f);
            Assert.AreEqual(0.4f, parsed.NormalMapAmount, 1e-4f);

            // Constants without a map.
            coat.Strength.Map = coat.Roughness.Map = null;
            coat.Reflections.Mask = coat.Highlights.Mask = null;
            coat.Strength.Constant = 0.5f;
            coat.Roughness.Constant = 0.3f;
            shader.Write(unified, material);
            parsed = AtlasShaderRegistry.Detect(material).Parse(material).Surface.Response.ClearCoat;
            Assert.IsNull(parsed.Strength.Map);
            Assert.AreEqual(0.5f, parsed.Strength.Constant, 1e-4f);
            Assert.IsNull(parsed.Roughness.Map);
            Assert.AreEqual(0.3f, parsed.Roughness.InvertResult ? 1f - parsed.Roughness.Constant : parsed.Roughness.Constant, 1e-4f);
            Assert.IsNull(parsed.Reflections.Mask);

            shader.Write(new AtlasUnifiedMaterial(), material);
            Assert.IsNull(AtlasShaderRegistry.Detect(material).Parse(material).Surface.Response.ClearCoat, "none");
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
                // One strength everywhere: no masks, the channels keep their neutral value.
                Assert.AreEqual(0f, c.b, 0.01f, "no reflection mask");
                Assert.AreEqual(0f, c.a, 0.01f, "no specular mask");
            }
            Assert.IsNull(plan.Output.Surface.Response.Reflections.Mask);
            Assert.IsNull(plan.Output.Surface.Response.Highlights.Mask);
        }

        [Test]
        public void ConstantMasksNeedNoMapAndAnEqualHighlightMaskSharesTheReflectionChannel()
        {
            // Half strength everywhere: folded into the strength.
            var half = AtlasOutputPipeline.Resolve(Job(Lit(0.5f, 0.5f), Lit(0.5f, 0.5f)), Poiyomi);
            Assert.AreEqual(0.5f, half.Output.Surface.Response.Highlights.Strength, 1e-5f);
            Assert.IsFalse(half.Maps.Any(m => m.Map == AtlasVirtualMap.ReflectionMask || m.Map == AtlasVirtualMap.SpecularMask));

            // The same mask for both: one map, read by both.
            var mask = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.3f, 0.3f, 0.3f, 1f)) } };
            var masked = Lit(1f, 1f);
            masked.Surface.Response.Highlights.Mask = mask;
            masked.Surface.Response.Reflections.Mask = mask;
            var plan = AtlasOutputPipeline.Resolve(Job(masked, Lit(1f, 1f)), Poiyomi);
            var response = plan.Output.Surface.Response;
            Assert.AreSame(response.Reflections.Mask.Map, response.Highlights.Mask.Map);
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.SpecularMask));
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            response.Metallic.Map = new AtlasTextureReference { Texture = Solid(Color.white) };
            response.Reflections.Mask.Map.Texture = response.Metallic.Map.Texture;
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(plan.Output, material);
            Assert.AreEqual(2f, material.GetFloat("_MochieMetallicMapsSpecularMaskChannel"));
            Assert.AreEqual(2f, material.GetFloat("_MochieMetallicMapsReflectionMaskChannel"));
        }

        [Test]
        public void TheWritersSetTheSurfaceMapsTheParsersReadBack()
        {
            var surface = Solid(new Color(0.6f, 0.75f, 0.5f, 1f));
            var occlusionTexture = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders.Where(s => s.Capabilities.Provides(AtlasVirtualMap.Metallic)))
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
                // Poiyomi keeps occlusion in its own texture; Toon Standard and Unity Standard in
                // the surface texture.
                response.Occlusion.Map = new AtlasTextureReference { Texture = shader.Family == AtlasShaderFamilies.Poiyomi ? occlusionTexture : surface };
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(unified, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Surface.Response;
                Assert.AreSame(surface, parsed.Metallic.Map?.Texture, shader.DisplayName + ": metallic");
                Assert.AreEqual(AtlasChannel.Red, parsed.Metallic.Channel, shader.DisplayName);
                Assert.AreSame(surface, parsed.Roughness.Map?.Texture, shader.DisplayName + ": roughness");
                // Unity Standard reads smoothness from the alpha.
                Assert.AreEqual(shader.Family == AtlasShaderFamilies.Unity ? AtlasChannel.Alpha : AtlasChannel.Green, parsed.Roughness.Channel,
                    shader.DisplayName);
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

        // Reflectance that follows the gloss map, as Toon Standard's: gloss × reflectance.
        private static AtlasUnifiedMaterial Glossy(AtlasTextureReference gloss, float reflectance, float glossStrength)
        {
            var material = Lit(1f, 1f);
            var response = material.Surface.Response;
            response.Roughness.Map = gloss;
            response.Roughness.Channel = AtlasChannel.Alpha;
            response.Roughness.Constant = glossStrength;
            response.Roughness.InvertResult = true;
            response.Reflectance.Map = gloss;
            response.Reflectance.Channel = AtlasChannel.Alpha;
            response.Reflectance.Constant = reflectance;
            return material;
        }

        [Test]
        public void ReflectanceIsChosenOnlyWhereTheOutputSetsItAndTheSourcesDiffer()
        {
            var gloss = new AtlasTextureReference { Texture = Solid(Color.white), Interpretation = AtlasTextureInterpretation.Data };
            var toon = ToonStandardInstalled();
            var feature = new AtlasSpecularFeature();
            // Toon Standard's output reflectance is its smoothness map × one value: reflectance over smoothness strength.
            // The sources are Toon Standard's own, so the same values give the same look.
            var a = OfToonStandard(Glossy(gloss, 0.4f, 0.8f));
            var b = OfToonStandard(Glossy(gloss, 0.25f, 0.5f));
            var c = OfToonStandard(Lit(1f, 1f, roughness: 0.5f));
            Assert.AreEqual(0.5f, AtlasSpecularFeature.OutputReflectance(a.Surface.Response, toon.Reflectance, out bool exact), 1e-5f);
            Assert.IsTrue(exact);
            Assert.AreEqual(1f, AtlasSpecularFeature.OutputReflectance(c.Surface.Response, toon.Reflectance, out exact), 1e-5f, "0.5 over smoothness 0.5");
            Assert.IsTrue(exact, "both constants");
            c.Surface.Response.Roughness.Map = gloss;
            c.Surface.Response.Roughness.InvertResult = true;
            AtlasSpecularFeature.OutputReflectance(c.Surface.Response, toon.Reflectance, out exact);
            Assert.IsFalse(exact, "a constant reflectance against a smoothness map");

            Assert.IsFalse(feature.SharedValuesDiffer(new[] { a, b }, toon), "the same ratio");
            Assert.IsTrue(feature.SharedValuesDiffer(new[] { a, c }, toon));
            Assert.IsFalse(feature.SharedValuesDiffer(new[] { a, c }, Poiyomi), "Poiyomi's reflectance is fixed");

            var job = Job(a, c);
            var plan = AtlasOutputPipeline.Resolve(job, toon);
            // Automatic: the value that changes the two looks least, the smoother one's here (its
            // highlights change most with the reflectance).
            Assert.AreEqual(0.5f, plan.Output.Surface.Response.Reflectance.Constant, 0.01f, "automatic: fitted to both");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("fitted to change the sources' highlights and reflections least")));
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "specular", ReferenceSource = 1 });
            plan = AtlasOutputPipeline.Resolve(job, toon);
            Assert.AreEqual(1f, plan.Output.Surface.Response.Reflectance.Constant, 1e-5f, "the chosen source, exactly");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("fitted to source 2; source 1 would need a different one")));
            plan = AtlasOutputPipeline.Resolve(Job(a, b), toon);
            Assert.AreEqual(0.5f, plan.Output.Surface.Response.Reflectance.Constant, 1e-5f, "sources that agree keep their exact value");

            plan = AtlasOutputPipeline.Resolve(Job(a, c), Poiyomi);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("fixed at 0.5")));
        }

        // Toon Standard has no highlight or reflection strengths: a lower reflectance dims them.
        [Test]
        public void StrengthsTheOutputLacksAreFittedIntoTheReflectance()
        {
            var toon = ToonStandardInstalled();
            AtlasUnifiedMaterial Own(float highlights, float reflections) => OfToonStandard(Lit(highlights, reflections, roughness: 0.228f));
            // Reflectance 0.5 (4%) at smoothness 0.772: 0.5 / 0.772 at full strengths.
            float full = 0.5f / 0.772f;
            Assert.AreEqual(full, AtlasSpecularFeature.OwnReflectance(Own(1f, 1f), toon), 1e-5f, "exact");
            Assert.AreEqual(full, AtlasSpecularResponse.Fit(new[] { AtlasSpecularResponse.Sample(Own(1f, 1f)) }, toon), 0.003f,
                "the fit finds the exact value");

            float weak = AtlasSpecularFeature.OwnReflectance(Own(0.28f, 0.041f), toon);
            float weaker = AtlasSpecularFeature.OwnReflectance(Own(0.1f, 0.041f), toon);
            // F0 = 0.16 × r², so a strength is about its square root of the reflectance: the
            // fitted value lies between what the reflections and the highlights ask for.
            Assert.That(weak, Is.InRange(full * Mathf.Sqrt(0.041f) - 0.03f, full * Mathf.Sqrt(0.28f) + 0.06f), "highlights at 0.28, reflections at 0.041");
            Assert.Less(weak, full - 0.05f, "dimmer than at full strengths");
            Assert.LessOrEqual(weaker, weak, "weaker highlights, no higher reflectance");

            var plan = AtlasOutputPipeline.Resolve(Job(Own(0.28f, 0.041f)), toon);
            Assert.AreEqual(weak, plan.Output.Surface.Response.Reflectance.Constant, 1e-5f);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("highlights of source 1 are approximated through its reflectance")));
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("reflections of source 1 are approximated through its reflectance")));
        }

        // A source is drawn with its own shader's fresnel and the output with the output
        // shader's: the same values look the same only where the two shaders agree.
        [Test]
        public void ASourceIsDrawnWithItsOwnShadersFresnel()
        {
            var toon = ToonStandardInstalled();
            float value = 0.5f / 0.772f;
            double Cost(AtlasUnifiedMaterial material) => AtlasSpecularResponse.Cost(new[] { AtlasSpecularResponse.Sample(material) }, toon, value);

            var own = OfToonStandard(Lit(1f, 1f, roughness: 0.228f));
            Assert.IsTrue(AtlasSpecularResponse.SameShading(own.Surface.Response, toon));
            Assert.AreEqual(0.0, Cost(own), 1e-6, "the output's own shader: the same look at the same values");

            // Schlick's fresnel rises toward grazing angles where Toon Standard's barely does.
            var other = Lit(1f, 1f, roughness: 0.228f);
            Assert.IsFalse(AtlasSpecularResponse.SameShading(other.Surface.Response, toon));
            Assert.Greater(Cost(other), 1e-4, "another shader's fresnel looks different at the same values");
            Assert.AreNotEqual(value, AtlasSpecularFeature.OwnReflectance(other, toon), "so it is fitted, not taken as it is");

            // Reflections that rise less are closer to Toon Standard's.
            var faint = Lit(1f, 1f, roughness: 0.228f);
            faint.Surface.Response.Reflections.FresnelStrength = 0.25f;
            Assert.Less(Cost(faint), Cost(other));
        }

        // Poiyomi's fresnel strength slider and the unified strength: the slider's rise is
        // lerp(F0, 1, s) × s, the unified one (1 − F0) × strength.
        [Test]
        public void PoiyomisFresnelStrengthIsConvertedBothWays()
        {
            Assert.AreEqual(0f, AtlasPoiyomiFresnel.Strength(0f), 1e-6f);
            Assert.AreEqual(0.5f * 0.52f / 0.96f, AtlasPoiyomiFresnel.Strength(0.5f), 1e-5f, "the shader's default: about a quarter of the full rise");
            Assert.AreEqual(1f / 0.96f, AtlasPoiyomiFresnel.Strength(1f), 1e-5f);
            foreach (float slider in new[] { 0f, 0.1f, 0.5f, 0.75f, 1f })
                Assert.AreEqual(slider, AtlasPoiyomiFresnel.Slider(AtlasPoiyomiFresnel.Strength(slider)), 1e-5f);
        }
    }
}
