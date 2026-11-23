using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Unity Standard (metallic setup) as an output (ATLAS_OUTPUT.md, "Output shaders"): its
    // writer is its parser in reverse, so what is written is read back.
    public sealed class AtlasUnityStandardOutputTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static IAtlasOutputShader Shader => AtlasShaderRegistry.Output("standard");
        private static AtlasOutputCapabilities Standard => Shader.Capabilities;

        private Texture2D Texture(string name)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name };
            created.Add(texture);
            return texture;
        }

        private Material Written(AtlasUnifiedMaterial output)
        {
            var material = new Material(Shader.FindShader());
            created.Add(material);
            Shader.Write(output, material);
            return material;
        }

        private static AtlasUnifiedMaterial Parse(Material material) => AtlasShaderRegistry.Detect(material).Parse(material);

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

        private static AtlasSurfaceLayer Detail(Texture2D texture, float tiling, AtlasSurfaceTarget target = AtlasSurfaceTarget.BaseColor, float opacity = 1f)
        {
            var layer = new AtlasSurfaceLayer();
            layer.Source.Map = new AtlasTextureReference
            {
                Texture = texture, Tiling = Vector2.one * tiling,
                Interpretation = target == AtlasSurfaceTarget.Normal ? AtlasTextureInterpretation.TangentNormal : AtlasTextureInterpretation.Color
            };
            layer.Targets.Add(new AtlasLayerTarget(target, target == AtlasSurfaceTarget.Normal ? AtlasBlend.Replace : AtlasBlend.MultiplyDouble, opacity));
            return layer;
        }

        private static AtlasUnifiedMaterial With(params AtlasLayer[] layers)
        {
            var material = new AtlasUnifiedMaterial();
            material.Layers.AddRange(layers);
            return material;
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void StandardIsAnOutputWithItsMainMaps()
        {
            Assert.IsNotNull(Shader.FindShader(), "built in");
            Assert.AreEqual(AtlasShaderFamilies.Unity, Shader.Family);
            var capabilities = Standard;
            Assert.AreSame(capabilities.TextureFor(AtlasVirtualMap.BaseColor), capabilities.TextureFor(AtlasVirtualMap.Alpha), "the albedo's alpha");
            // One texture serves the metallic map (R, smoothness in A) and the occlusion map (G).
            var surface = capabilities.TextureFor(AtlasVirtualMap.Metallic);
            Assert.AreSame(surface, capabilities.TextureFor(AtlasVirtualMap.Roughness));
            Assert.AreSame(surface, capabilities.TextureFor(AtlasVirtualMap.Occlusion));
            Assert.AreEqual(AtlasTextureChannels.R, surface.Channels.Find(c => c.Map == AtlasVirtualMap.Metallic).Channels);
            Assert.AreEqual(AtlasTextureChannels.G, surface.Channels.Find(c => c.Map == AtlasVirtualMap.Occlusion).Channels);
            var smoothness = surface.Channels.Find(c => c.Map == AtlasVirtualMap.Roughness);
            Assert.AreEqual(AtlasTextureChannels.A, smoothness.Channels);
            Assert.IsTrue(smoothness.Invert, "stored as smoothness");
            Assert.IsFalse(surface.Srgb);
            Assert.IsFalse(capabilities.Provides(AtlasVirtualMap.ReflectionMask));
            Assert.IsFalse(capabilities.Provides(AtlasVirtualMap.SpecularMask));
            Assert.AreEqual(AtlasReflectanceSupport.None, capabilities.Reflectance);
            Assert.AreEqual(AtlasVertexColorSupport.None, capabilities.VertexColors);
            Assert.IsTrue(capabilities.BaseColorTint);
            CollectionAssert.AreEquivalent(
                new[] { AtlasSurfaceMode.Opaque, AtlasSurfaceMode.Cutout, AtlasSurfaceMode.Transparent, AtlasSurfaceMode.Fade }, capabilities.SurfaceModes);
            Assert.IsFalse(capabilities.BlendedDepthWrite);

            // Its own maps are native; what it has no control for is baked or not offered.
            string Mode(string feature) => AtlasOutputFeatures.Effective(AtlasOutputFeatures.Find(feature), "", capabilities)?.Id;
            Assert.AreEqual("native", Mode("color"));
            Assert.AreEqual("native", Mode("alpha"));
            Assert.AreEqual("native", Mode("normal"));
            Assert.AreEqual("native", Mode("specular"));
            Assert.AreEqual("native", Mode("occlusion"));
            Assert.AreEqual("native", Mode("emission"));
            Assert.AreEqual("live", Mode(AtlasDetailTextureFeature.FeatureId));
            Assert.AreEqual("live", Mode(AtlasDetailNormalFeature.FeatureId));
            Assert.AreEqual("baked", Mode(AtlasVertexColorFeature.FeatureId));
            Assert.AreEqual("average", Mode(AtlasMatcapFeature.FeatureId));
            Assert.IsNull(Mode(AtlasRimFeature.FeatureId));
        }

        [Test]
        public void TheWriterSetsTheMainMapsTheParserReadsBack()
        {
            var albedo = Texture("albedo");
            var normal = Texture("normal");
            var surface = Texture("surface");
            var glow = Texture("glow");
            var output = new AtlasUnifiedMaterial();
            output.Surface.BaseColor.Color = new Color(0.5f, 0.25f, 1f, 1f);
            output.Surface.BaseColor.Map = new AtlasTextureReference { Texture = albedo };
            output.Surface.Normal.Map = new AtlasTextureReference { Texture = normal, Interpretation = AtlasTextureInterpretation.TangentNormal };
            output.Surface.Normal.Strength = 1.5f;
            var response = output.Surface.Response;
            response.Metallic.Map = new AtlasTextureReference { Texture = surface };
            response.Roughness.Map = new AtlasTextureReference { Texture = surface };
            response.Occlusion.Map = new AtlasTextureReference { Texture = surface };
            var emission = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            emission.Source.Map = new AtlasTextureReference { Texture = glow };
            emission.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 3f));
            output.Layers.Add(emission);

            var material = Written(output);
            foreach (string keyword in new[] { "_NORMALMAP", "_METALLICGLOSSMAP", "_EMISSION" })
                Assert.IsTrue(material.IsKeywordEnabled(keyword), keyword);
            foreach (string keyword in new[] { "_SPECULARHIGHLIGHTS_OFF", "_GLOSSYREFLECTIONS_OFF", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", "_DETAIL_MULX2", "_PARALLAXMAP" })
                Assert.IsFalse(material.IsKeywordEnabled(keyword), keyword);
            // The inspector derives the emission keyword from this flag when it validates.
            Assert.AreEqual(MaterialGlobalIlluminationFlags.BakedEmissive, material.globalIlluminationFlags);

            var parsed = Parse(material);
            Assert.AreSame(albedo, parsed.Surface.BaseColor.Map.Texture);
            Assert.AreEqual(0.5f, parsed.Surface.BaseColor.Color.r, 1e-3f);
            Assert.AreEqual(0.25f, parsed.Surface.BaseColor.Color.g, 1e-3f);
            Assert.AreEqual(AtlasAlphaSource.BaseColorAlpha, parsed.Surface.Alpha.Source, "the albedo's alpha stays alpha");
            Assert.AreSame(normal, parsed.Surface.Normal.Map.Texture);
            Assert.AreEqual(1.5f, parsed.Surface.Normal.Strength, 1e-5f);
            var read = parsed.Surface.Response;
            Assert.AreSame(surface, read.Metallic.Map.Texture);
            Assert.AreEqual(1f, read.Metallic.Constant, 1e-6f);
            Assert.AreSame(surface, read.Roughness.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, read.Roughness.Channel);
            Assert.AreEqual(1f, read.Roughness.Constant, 1e-6f, "the smoothness scale");
            Assert.IsTrue(read.Roughness.InvertResult);
            Assert.AreSame(surface, read.Occlusion.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, read.Occlusion.Channel);
            Assert.IsTrue(read.Highlights.Enabled);
            Assert.AreEqual(AtlasReflectionSource.Probes, read.Reflections.Source);
            var readEmission = AtlasEmissionFeature.Of(parsed);
            Assert.AreSame(glow, readEmission.Source.Map.Texture);
            Assert.AreEqual(3f, readEmission.Source.Color.r, 0.01f, "the strength, in the color");
        }

        // Without metallic and smoothness maps nothing in the atlas has highlights or
        // reflections: the surface is rough and not metallic, and both are turned off.
        [Test]
        public void WithoutASurfaceMapHighlightsAndReflectionsAreOff()
        {
            var material = Written(new AtlasUnifiedMaterial());
            Assert.IsTrue(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"));
            Assert.IsTrue(material.IsKeywordEnabled("_GLOSSYREFLECTIONS_OFF"));
            foreach (string keyword in new[] { "_NORMALMAP", "_METALLICGLOSSMAP", "_EMISSION", "_DETAIL_MULX2" })
                Assert.IsFalse(material.IsKeywordEnabled(keyword), keyword);
            Assert.AreEqual(MaterialGlobalIlluminationFlags.EmissiveIsBlack, material.globalIlluminationFlags);
            var parsed = Parse(material);
            Assert.AreEqual(0f, parsed.Surface.Response.Metallic.Constant, 1e-6f);
            Assert.AreEqual(1f, Roughness(parsed.Surface.Response.Roughness), 1e-6f, "full roughness");
            Assert.IsFalse(parsed.Surface.Response.Highlights.Enabled);
            Assert.AreEqual(AtlasReflectionSource.AmbientColor, parsed.Surface.Response.Reflections.Source);
            Assert.IsNull(AtlasEmissionFeature.Of(parsed));

            // With the maps, each follows the atlas: highlights without reflections.
            var output = new AtlasUnifiedMaterial();
            output.Surface.Response.Metallic.Map = new AtlasTextureReference { Texture = Texture("surface") };
            output.Surface.Response.Reflections.Enabled = false;
            material = Written(output);
            Assert.IsFalse(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"));
            Assert.IsTrue(material.IsKeywordEnabled("_GLOSSYREFLECTIONS_OFF"));
        }

        // A constant roughness as the shading sees it: the slider is smoothness.
        private static float Roughness(AtlasScalarInput roughness) =>
            roughness.InvertResult ? 1f - roughness.Constant : roughness.Constant;

        [TestCase(AtlasSurfaceMode.Opaque, 0f, "", null, 1f, 2000)]
        [TestCase(AtlasSurfaceMode.Cutout, 1f, "TransparentCutout", "_ALPHATEST_ON", 1f, 2450)]
        [TestCase(AtlasSurfaceMode.Fade, 2f, "Transparent", "_ALPHABLEND_ON", 0f, 3000)]
        [TestCase(AtlasSurfaceMode.Transparent, 3f, "Transparent", "_ALPHAPREMULTIPLY_ON", 0f, 3000)]
        public void TheSurfaceModesAreSetUpAsTheInspectorDoes(AtlasSurfaceMode mode, float preset, string type, string keyword, float depthWrite, int queue)
        {
            // A material written before in another mode ends up the same.
            var material = Written(new AtlasUnifiedMaterial { Render = { SurfaceMode = AtlasSurfaceMode.Cutout } });
            var output = new AtlasUnifiedMaterial();
            output.Render.SurfaceMode = mode;
            output.Render.Cutoff = 0.3f;
            Shader.Write(output, material);
            Assert.AreEqual(preset, material.GetFloat("_Mode"));
            if (type != "") Assert.AreEqual(type, material.GetTag("RenderType", false));
            foreach (string other in new[] { "_ALPHATEST_ON", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON" })
                Assert.AreEqual(other == keyword, material.IsKeywordEnabled(other), other);
            Assert.AreEqual(depthWrite, material.GetFloat("_ZWrite"));
            Assert.AreEqual(queue, material.renderQueue);
            var blended = mode == AtlasSurfaceMode.Fade || mode == AtlasSurfaceMode.Transparent;
            Assert.AreEqual(blended ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.Zero,
                material.GetFloat("_DstBlend"));
            Assert.AreEqual(mode == AtlasSurfaceMode.Fade ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One,
                material.GetFloat("_SrcBlend"));

            var parsed = Parse(material).Render;
            Assert.AreEqual(mode, parsed.SurfaceMode);
            Assert.AreEqual(depthWrite > 0.5f, parsed.Pass.DepthWrite);
            if (mode == AtlasSurfaceMode.Cutout) Assert.AreEqual(0.3f, parsed.Cutoff, 1e-6f);
        }

        // Standard's detail mask is one texture's alpha for both details: the allocator's one
        // place on this output.
        [Test]
        public void BothDetailsReadOneMaskFromTheDetailMasksAlpha()
        {
            var texture = Texture("detail");
            var bump = Texture("bump");
            var job = Job(With(Detail(texture, 10f), Detail(bump, 10f, AtlasSurfaceTarget.Normal, 0.8f)), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Standard);
            var live = AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor);
            var liveNormal = AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.Normal);
            Assert.IsNotNull(live, Notes(plan));
            Assert.IsNotNull(liveNormal, Notes(plan));
            Assert.AreEqual(1, AtlasDetailFeatures.UV(live), "UV1, the shader's other UV set");
            Assert.AreEqual(1, AtlasDetailFeatures.UV(liveNormal), "the same tiling: the same channel");
            Assert.AreEqual(AtlasChannel.Alpha, live.Mask.Channel);
            Assert.AreSame(live.Mask.Map, liveNormal.Mask.Map, "one mask");
            Assert.AreEqual(0.8f, liveNormal.Targets[0].Opacity, 1e-5f);
            var mask = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Mask1);
            using (var with = mask.Evaluate(0, job.Sources[0].Material, 4, 4))
            using (var without = mask.Evaluate(1, job.Sources[1].Material, 4, 4))
            {
                Assert.AreEqual(1f, with.GetPixel(1, 1).r, 1e-4f);
                Assert.AreEqual(0f, without.GetPixel(1, 1).r, 1e-4f, "none on the source without details");
            }

            // A detail normal on another tiling needs a UV channel of its own, which the shader
            // doesn't have: it is baked into the normal map.
            job = Job(With(Detail(texture, 10f), Detail(bump, 20f, AtlasSurfaceTarget.Normal)), new AtlasUnifiedMaterial());
            plan = AtlasOutputPipeline.Resolve(job, Standard);
            Assert.IsNotNull(AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.BaseColor), Notes(plan));
            Assert.IsNull(AtlasDetailFeatures.Of(plan.Output, AtlasSurfaceTarget.Normal));
            StringAssert.Contains("no UV channel is free for it", Notes(plan));
        }

        [Test]
        public void TheWriterSetsTheDetailsTheParserReadsBack()
        {
            var texture = Texture("detail");
            var bump = Texture("bump");
            var maskTexture = Texture("mask");
            var output = new AtlasUnifiedMaterial();
            var detail = Detail(texture, 1f);
            detail.Source.Map.Space = AtlasTextureSpace.UV1;
            detail.Mask = new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Alpha };
            var normal = Detail(bump, 1f, AtlasSurfaceTarget.Normal, 0.8f);
            normal.Source.Map.Space = AtlasTextureSpace.UV1;
            normal.Mask = detail.Mask;
            output.Layers.Add(detail);
            output.Layers.Add(normal);
            var material = Written(output);
            Assert.IsTrue(material.IsKeywordEnabled("_DETAIL_MULX2"));
            Assert.IsTrue(material.IsKeywordEnabled("_NORMALMAP"), "the detail normal needs it");
            Assert.AreEqual(1f, material.GetFloat("_UVSec"));

            var parsed = Parse(material);
            Assert.IsNull(parsed.Surface.Normal.Map, "no main normal map");
            var layers = parsed.Layers.OfType<AtlasSurfaceLayer>().ToList();
            var color = layers.Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.BaseColor));
            Assert.AreSame(texture, color.Source.Map.Texture);
            Assert.AreEqual(AtlasTextureSpace.UV1, color.Source.Map.Space);
            Assert.AreEqual(Vector2.one, color.Source.Map.Tiling, "the tiling is in the UV channel");
            Assert.AreEqual(AtlasBlend.MultiplyDouble, color.Targets[0].Blend);
            Assert.AreSame(maskTexture, color.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, color.Mask.Channel);
            var parsedNormal = layers.Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Normal));
            Assert.AreSame(bump, parsedNormal.Source.Map.Texture);
            Assert.AreEqual(0.8f, parsedNormal.Targets[0].Opacity, 1e-5f);

            // Written again without details, they are gone.
            Shader.Write(new AtlasUnifiedMaterial(), material);
            Assert.IsFalse(material.IsKeywordEnabled("_DETAIL_MULX2"));
            Assert.IsEmpty(Parse(material).Layers);
        }

        // Standard has no minimum light. A source's keeps it visible in the dark, so the base
        // color × the floor goes into the emission, on every source of the atlas, and the kept
        // lighting says so. An output with a floor of its own keeps what it can there, and the
        // rest goes into the emission. The user can turn it off.
        [Test]
        public void AMinimumLightGoesIntoTheEmission()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var lit = new AtlasUnifiedMaterial();
            lit.Surface.BaseColor.Color = gray;
            lit.Lighting.MinimumLight = 0.2f;
            var job = Job(lit);
            var plan = AtlasOutputPipeline.Resolve(job, Shader);
            Assert.AreEqual(0.2f, plan.LightFloor, 1e-6f);
            Assert.AreEqual(0.2f, plan.LightFloorMissing, 1e-6f);
            Assert.AreEqual(0.2f, plan.KeptLighting.MinimumBrightness, 1e-6f);
            StringAssert.Contains("has no minimum light", Notes(plan));
            var emission = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Emission);
            var target = plan.Output.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Source.Map == emission.Reference)
                .Targets.Single(t => t.Target == AtlasSurfaceTarget.Emission);
            var color = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            using (var glow = emission.Evaluate(0, lit, 4, 4))
            using (var own = color.Before(AtlasMapStage.Layers, 0, lit, 4, 4))
            {
                // The source's own color: the level compensation of the base color isn't in it.
                Assert.AreEqual(0.5f, own.GetPixel(1, 1).r, 1e-3f);
                Assert.AreEqual(0.5f * 0.2f, glow.GetPixel(1, 1).r * target.Opacity, 2e-3f, "the source's color × the floor");
            }

            // Without a floor, or with the emission turned off, there is none.
            var plain = new AtlasUnifiedMaterial();
            plain.Surface.BaseColor.Color = gray;
            Assert.IsFalse(AtlasOutputPipeline.Resolve(Job(plain), Shader).Maps.Any(m => m.Map == AtlasVirtualMap.Emission));
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "emission", ModeId = "off" });
            plan = AtlasOutputPipeline.Resolve(job, Shader);
            Assert.AreEqual(0f, plan.LightFloor);
            Assert.AreEqual(0f, plan.LightFloorMissing, "no emission to keep it in: no control either");
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Emission));

            // Turned off by its own control, which stays while the floor is missing.
            job.Features.Clear();
            var lighting = new AtlasFeatureChoice { FeatureId = AtlasLightingFeature.FeatureId };
            lighting.Switches[AtlasLightFloor.SwitchId] = false;
            job.Features.Add(lighting);
            plan = AtlasOutputPipeline.Resolve(job, Shader);
            Assert.AreEqual(0f, plan.LightFloor);
            Assert.AreEqual(0.2f, plan.LightFloorMissing, 1e-6f);
            Assert.AreEqual(0f, plan.KeptLighting.MinimumBrightness);
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Emission));

            // Toon Standard has a minimum brightness of its own, up to 0.1: the rest is added,
            // so the two together are the atlas's floor.
            var toon = AtlasShaderRegistry.Output("mobiletoon");
            if (toon.FindShader() == null) return;
            plan = AtlasOutputPipeline.Resolve(Job(lit), toon);
            Assert.GreaterOrEqual(plan.LightFloor, 0.1f - 1e-4f, "what its slider can't hold");
            Assert.AreEqual(0.2f, plan.KeptLighting.MinimumBrightness, 1e-4f, "its own and the emission's");
            StringAssert.Contains("is added to the emission", Notes(plan));
            Assert.IsTrue(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Emission));
        }

        // The shader lights its normal map and darkens by its occlusion map only what the
        // world's light reaches, so the floor in the emission is shaded by the lit bakes: flat
        // otherwise. Where a map is baked into the base color, the color has it already.
        [Test]
        public void TheMinimumLightInTheEmissionIsShadedByTheLitBakes()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var occlusionTexture = Texture("occlusion");
            occlusionTexture.SetPixels(Enumerable.Repeat(gray, 16).ToArray());
            occlusionTexture.Apply();
            var lit = new AtlasUnifiedMaterial();
            lit.Surface.BaseColor.Color = gray;
            lit.Lighting.MinimumLight = 0.2f;
            lit.Surface.Response.Occlusion.Map = new AtlasTextureReference { Texture = occlusionTexture, Interpretation = AtlasTextureInterpretation.Data };
            lit.Surface.Normal.Map = new AtlasTextureReference { Texture = Texture("normal"), Interpretation = AtlasTextureInterpretation.TangentNormal };
            var job = Job(lit);

            float Glow(AtlasOutputPlan plan, out float flat)
            {
                var emission = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Emission);
                var target = plan.Output.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Source.Map == emission.Reference)
                    .Targets.Single(t => t.Target == AtlasSurfaceTarget.Emission);
                using (var glow = emission.Evaluate(0, lit, 4, 4))
                using (var own = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Before(AtlasMapStage.Layers, 0, lit, 4, 4))
                {
                    flat = own.GetPixel(1, 1).r * plan.LightFloor;
                    return glow.GetPixel(1, 1).r * target.Opacity;
                }
            }

            var shaded = AtlasOutputPipeline.Resolve(job, Shader);
            CollectionAssert.AreEquivalent(new[] { AtlasLitBake.NormalShading, AtlasLitBake.Occlusion }, shaded.LightFloorBakes);
            StringAssert.Contains("with normal-map shading and occlusion baked in", Notes(shaded));
            StringAssert.Contains("shading need the meshes", Notes(shaded), "no meshes in this job");
            float shadedGlow = Glow(shaded, out float unshaded);
            Assert.Less(shadedGlow, unshaded - 1e-3f, "darker where the occlusion map is");

            // Turned down in the Baking section, the floor is flat.
            job.Baking = new AtlasBakeSettings { occlusion = 0f, normalShading = 0f };
            var plain = AtlasOutputPipeline.Resolve(job, Shader);
            Assert.IsEmpty(plain.LightFloorBakes);
            Assert.AreEqual(Glow(plain, out unshaded), unshaded, 2e-3f);

            // The floor starts from the source's own color, so it is occluded the same whether
            // the atlas keeps the occlusion as a map or bakes it into the base color.
            job.Baking = null;
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "occlusion", ModeId = "baked" });
            var baked = AtlasOutputPipeline.Resolve(job, Shader);
            CollectionAssert.Contains(baked.LightFloorBakes, AtlasLitBake.Occlusion);
            Assert.AreEqual(shadedGlow, Glow(baked, out _), 2e-3f, "occluded once either way");

            // Turned off, the occlusion isn't in the floor either.
            job.Features[job.Features.Count - 1].ModeId = "off";
            var off = AtlasOutputPipeline.Resolve(job, Shader);
            CollectionAssert.DoesNotContain(off.LightFloorBakes, AtlasLitBake.Occlusion);
            Assert.AreEqual(Glow(off, out unshaded), unshaded, 2e-3f);
        }

        // What Standard has no control for goes into its textures or is left out, and its
        // lighting is its own whatever the atlas chose.
        [Test]
        public void APlanResolvesAgainstTheShader()
        {
            var toon = new AtlasUnifiedMaterial();
            toon.Lighting.Family = AtlasLightingFamily.RampToon;
            toon.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var rim = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Surface, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Facing,
                Falling = true, Power = 1f, Low = 0.2f, High = 0.6f, SourceAlphaIsOpacity = true
            };
            rim.Color.Color = Color.red;
            toon.Layers.Add(rim);
            var cutout = new AtlasUnifiedMaterial();
            cutout.Render.SurfaceMode = AtlasSurfaceMode.Cutout;
            var plan = AtlasOutputPipeline.Resolve(Job(toon, cutout), Shader);
            StringAssert.Contains("The output shader has no rim lighting", Notes(plan));
            Assert.AreEqual(AtlasSurfaceMode.Cutout, plan.Output.Render.SurfaceMode);
            Assert.IsTrue(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Alpha));
            Assert.IsNotNull(plan.KeptLighting);
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, plan.KeptLighting.Family);
            Assert.AreEqual(AtlasDiffuseModel.Disney, plan.KeptLighting.Diffuse);

            // A transparent source next to solid ones is drawn solid: the shader can't blend
            // while writing depth.
            var glass = new AtlasUnifiedMaterial();
            glass.Render.SurfaceMode = AtlasSurfaceMode.Fade;
            plan = AtlasOutputPipeline.Resolve(Job(toon, glass), Shader);
            Assert.AreEqual(AtlasSurfaceMode.Opaque, plan.Output.Render.SurfaceMode);
            StringAssert.Contains("drawn solid", Notes(plan));
            Assert.AreEqual(AtlasSurfaceMode.Fade, AtlasOutputPipeline.Resolve(Job(glass), Shader).Output.Render.SurfaceMode, "alone it fades");
        }
    }
}
