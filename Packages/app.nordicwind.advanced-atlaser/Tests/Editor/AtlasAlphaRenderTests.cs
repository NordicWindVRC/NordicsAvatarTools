using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Alpha, the surface mode and the fallback shader (ATLAS_OUTPUT.md, "Feature inventory",
    // "Effects and render").
    public sealed class AtlasAlphaRenderTests
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

        // Like Poiyomi 10: base color with alpha, and the blended modes.
        private static AtlasOutputCapabilities Blended(params AtlasSurfaceMode[] modes)
        {
            var capabilities = new AtlasOutputCapabilities
            {
                Textures =
                {
                    new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                        .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
                        .With(AtlasVirtualMap.Alpha, AtlasTextureChannels.A)
                },
                VRChatFallback = true,
                BlendedDepthWrite = modes.Contains(AtlasSurfaceMode.Fade)
            };
            foreach (var mode in modes) capabilities.SurfaceModes.Add(mode);
            return capabilities;
        }

        private static readonly AtlasSurfaceMode[] PoiyomiModes = { AtlasSurfaceMode.Cutout, AtlasSurfaceMode.Transparent, AtlasSurfaceMode.Fade };

        private static AtlasUnifiedMaterial Source(AtlasSurfaceMode mode, float cutoff = 0.5f)
        {
            var material = new AtlasUnifiedMaterial();
            material.Render.SurfaceMode = mode;
            material.Render.Cutoff = cutoff;
            return material;
        }

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] sources)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < sources.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource { Material = sources[i], UVRegion = new Rect(i / (float)sources.Length, 0f, 1f / sources.Length, 1f) });
            return job;
        }

        [Test]
        public void AutomaticTakesTheMostSeeThroughModeTheOutputSupports()
        {
            var poiyomi = Blended(PoiyomiModes);
            Assert.AreEqual(AtlasSurfaceMode.Cutout, AtlasSurfaceModeFeature.OutputMode(
                Job(Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Cutout)), poiyomi));
            var allBlended = AtlasSurfaceModeFeature.Choose(Job(Source(AtlasSurfaceMode.Fade), Source(AtlasSurfaceMode.Transparent)), poiyomi);
            Assert.AreEqual((AtlasSurfaceMode.Fade, false), (allBlended.Mode, allBlended.DepthWrite), "every source blended");
            // Transparent parts among solid ones: a fade that writes depth, so the solid parts sort
            // like opaque ones and the transparent ones keep their alpha.
            foreach (var job in new[] {
                Job(Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Transparent)),
                Job(Source(AtlasSurfaceMode.Cutout), Source(AtlasSurfaceMode.Fade), Source(AtlasSurfaceMode.Transparent)) })
            {
                var mixed = AtlasSurfaceModeFeature.Choose(job, poiyomi);
                Assert.AreEqual((AtlasSurfaceMode.Fade, true), (mixed.Mode, mixed.DepthWrite));
            }
            // Without that, the solid parts decide, and the transparent ones are drawn solid: no
            // emulation stands in.
            var cutoutOnly = Blended(AtlasSurfaceMode.Cutout);
            Assert.AreEqual(AtlasSurfaceMode.Opaque, AtlasSurfaceModeFeature.OutputMode(
                Job(Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Transparent)), cutoutOnly));
            Assert.AreEqual(AtlasSurfaceMode.Cutout, AtlasSurfaceModeFeature.OutputMode(
                Job(Source(AtlasSurfaceMode.Cutout), Source(AtlasSurfaceMode.Transparent)), cutoutOnly));
            Assert.AreEqual(AtlasAlphaConversionKind.Solid,
                AtlasAlphaFeature.Conversion(Source(AtlasSurfaceMode.Transparent).Render, AtlasSurfaceMode.Cutout).Kind);

            // A transparent source whose alpha is turned off counts as opaque.
            var turnedOff = Job(Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Transparent));
            turnedOff.Features.Add(new AtlasFeatureChoice { FeatureId = "alpha", DisabledSources = { 1 } });
            Assert.AreEqual(AtlasSurfaceMode.Opaque, AtlasSurfaceModeFeature.OutputMode(turnedOff, poiyomi));
            // Not beyond the most see-through source, and down to what the output supports.
            Assert.AreEqual(AtlasSurfaceMode.Cutout, AtlasSurfaceModeFeature.OutputMode(
                Job(Source(AtlasSurfaceMode.Fade)), Blended(AtlasSurfaceMode.Cutout)), "down to what the output supports");
            Assert.AreEqual(AtlasSurfaceMode.Opaque, AtlasSurfaceModeFeature.OutputMode(
                Job(Source(AtlasSurfaceMode.Fade)), new AtlasOutputCapabilities()));
            // Additive isn't merged yet: it counts as opaque.
            Assert.AreEqual(AtlasSurfaceMode.Opaque, AtlasSurfaceModeFeature.OutputMode(
                Job(Source(AtlasSurfaceMode.Additive)), poiyomi));
        }

        [Test]
        public void AChosenModeIsUsedWhereTheOutputSupportsIt()
        {
            var job = Job(Source(AtlasSurfaceMode.Cutout));
            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasSurfaceModeFeature.FeatureId, ModeId = "transparent" });
            Assert.AreEqual(AtlasSurfaceMode.Transparent, AtlasSurfaceModeFeature.OutputMode(job, Blended(PoiyomiModes)));
            job.Features[0].ModeId = "dithered";
            Assert.AreEqual(AtlasSurfaceMode.Cutout, AtlasSurfaceModeFeature.OutputMode(job, Blended(PoiyomiModes)), "unsupported: automatic");
            job.Features[0].ModeId = "fade-depth";
            var fadeDepth = AtlasSurfaceModeFeature.Choose(job, Blended(PoiyomiModes));
            Assert.AreEqual((AtlasSurfaceMode.Fade, true), (fadeDepth.Mode, fadeDepth.DepthWrite));
            job.Features[0].ModeId = "fade";
            Assert.IsFalse(AtlasSurfaceModeFeature.Choose(job, Blended(PoiyomiModes)).DepthWrite, "a plain fade doesn't write depth");
        }

        [Test]
        public void AlphaIsBakedForEverySourceWhenTheModeUsesIt()
        {
            var plan = AtlasOutputPipeline.Resolve(Job(Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Cutout, 0.3f)), Blended(PoiyomiModes));
            Assert.AreEqual(AtlasSurfaceMode.Cutout, plan.Output.Render.SurfaceMode);
            Assert.AreEqual(AtlasSurfaceModeFeature.SharedCutoff, plan.Output.Render.Cutoff);
            Assert.AreEqual(AtlasAlphaSource.BaseColorAlpha, plan.Output.Surface.Alpha.Source);
            var alpha = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Alpha);
            CollectionAssert.AreEqual(new[] { 0, 1 }, alpha.Sources, "the opaque one too, as solid");

            var opaque = AtlasOutputPipeline.Resolve(Job(Source(AtlasSurfaceMode.Opaque), Source(AtlasSurfaceMode.Opaque)), Blended(PoiyomiModes));
            Assert.AreEqual(AtlasSurfaceMode.Opaque, opaque.Output.Render.SurfaceMode);
            Assert.IsFalse(opaque.Maps.Any(m => m.Map == AtlasVirtualMap.Alpha));
        }

        private static float Evaluate(AtlasUnifiedMaterial source, AtlasAlphaConversion conversion = default)
        {
            using (var map = AtlasMapEvaluator.Alpha(source, 4, 4, conversion))
                return map.GetPixel(2, 2).r;
        }

        [Test]
        public void AlphaComesFromItsSource()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(1f, 1f, 1f, 0.5f);
            material.Surface.BaseColor.Map = new AtlasTextureReference { Texture = Solid(new Color(1f, 1f, 1f, 0.25f)) };
            Assert.AreEqual(0.125f, Evaluate(material), 1e-4f, "color alpha × texture alpha");
            material.Surface.Alpha.Source = AtlasAlphaSource.BaseColorConstantAlpha;
            Assert.AreEqual(0.5f, Evaluate(material), 1e-4f, "the color's alpha alone");
            material.Surface.Alpha.Source = AtlasAlphaSource.None;
            Assert.AreEqual(1f, Evaluate(material), 1e-4f, "none");

            material.Surface.Alpha.Source = AtlasAlphaSource.Mask;
            material.Surface.Alpha.Mask = new AtlasMask
            {
                Map = new AtlasTextureReference { Texture = Solid(new Color(0f, 0.75f, 0f, 1f)), Interpretation = AtlasTextureInterpretation.Data },
                Channel = AtlasChannel.Green, Invert = true
            };
            Assert.AreEqual(0.25f, Evaluate(material), 1e-4f, "the mask's green, inverted");
            material.Surface.Alpha.MaskMultiplier = 2f;
            Assert.AreEqual(0.5f, Evaluate(material), 1e-4f, "× the multiplier");
        }

        [Test]
        public void AMaskCombinesWithTheBaseColorAlphaAndOffsetAndStrengthFollow()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Map = new AtlasTextureReference { Texture = Solid(new Color(1f, 1f, 1f, 0.8f)) };
            material.Surface.Alpha.Mask = new AtlasMask
            {
                Map = new AtlasTextureReference { Texture = Solid(new Color(0.5f, 0f, 0f, 1f)), Interpretation = AtlasTextureInterpretation.Data }
            };
            material.Surface.Alpha.MaskBlend = AtlasAlphaMaskBlend.Multiply;
            Assert.AreEqual(0.4f, Evaluate(material), 1e-4f, "multiply");
            material.Surface.Alpha.MaskBlend = AtlasAlphaMaskBlend.Subtract;
            Assert.AreEqual(0.3f, Evaluate(material), 1e-4f, "subtract");
            material.Surface.Alpha.MaskBlend = AtlasAlphaMaskBlend.Add;
            Assert.AreEqual(1f, Evaluate(material), 1e-4f, "add, saturated");
            material.Surface.Alpha.MaskBlend = AtlasAlphaMaskBlend.Replace;
            material.Surface.Alpha.Offset = -0.2f;
            Assert.AreEqual(0.3f, Evaluate(material), 1e-4f, "the offset");
            material.Surface.Alpha.Strength = 0.5f;
            Assert.AreEqual(0.65f, Evaluate(material), 1e-4f, "lerp(1, alpha, strength)");
        }

        private static AtlasUnifiedMaterial Constant(float alpha)
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(1f, 1f, 1f, alpha);
            material.Surface.Alpha.Source = AtlasAlphaSource.BaseColorConstantAlpha;
            return material;
        }

        [Test]
        public void ACutoutSourcesCutoffMovesToTheSharedOneOrBecomesHard()
        {
            var remap = AtlasAlphaFeature.Conversion(Source(AtlasSurfaceMode.Cutout, 0.25f).Render, AtlasSurfaceMode.Cutout);
            Assert.AreEqual(AtlasAlphaConversionKind.Remap, remap.Kind);
            Assert.AreEqual(0.5f, Evaluate(Constant(0.25f), remap), 1e-4f, "the cutoff lands on the shared one");
            Assert.Less(Evaluate(Constant(0.2f), remap), 0.5f, "below stays below");
            Assert.Greater(Evaluate(Constant(0.3f), remap), 0.5f, "above stays above");
            Assert.AreEqual(1f, Evaluate(Constant(1f), remap), 1e-4f);

            var step = AtlasAlphaFeature.Conversion(Source(AtlasSurfaceMode.Cutout, 0.5f).Render, AtlasSurfaceMode.Fade);
            Assert.AreEqual(AtlasAlphaConversionKind.Step, step.Kind);
            Assert.AreEqual(0f, Evaluate(Constant(0.45f), step), 1e-4f);
            Assert.AreEqual(1f, Evaluate(Constant(0.55f), step), 1e-4f);
            Assert.AreEqual(AtlasAlphaConversionKind.None,
                AtlasAlphaFeature.Conversion(Source(AtlasSurfaceMode.Fade).Render, AtlasSurfaceMode.Fade).Kind);
        }

        [Test]
        public void AlphaIsPackedIntoTheBaseColorAlpha()
        {
            var cutout = Source(AtlasSurfaceMode.Cutout);
            cutout.Surface.BaseColor.Map = new AtlasTextureReference { Texture = Solid(new Color(1f, 0f, 0f, 0f)) };
            var opaque = Source(AtlasSurfaceMode.Opaque);
            opaque.Surface.BaseColor.Map = new AtlasTextureReference { Texture = Solid(new Color(0f, 0f, 1f, 0f)) };
            var job = Job(cutout, opaque);
            var capabilities = Blended(PoiyomiModes);
            var plan = AtlasOutputPipeline.Resolve(job, capabilities);
            var config = new AtlasOutputConfiguration { mainTexture = { width = 32, height = 16 } };
            var use = AtlasOutputOverview.TextureUses(config, capabilities, plan.Maps.Select(m => m.Map).Distinct().ToList(),
                new AtlasVirtualMap[0]).Single();
            Assert.IsTrue(use.Alpha);
            Assert.AreEqual(AtlasTextureKind.ColorAlpha, use.Kind);
            using (var packed = AtlasTexturePacker.Pack(use, plan, job))
            {
                Assert.AreEqual(0f, packed.GetPixel(8, 8).a, 0.02f, "the cutout source's alpha");
                Assert.AreEqual(1f, packed.GetPixel(24, 8).a, 0.02f, "the opaque source is solid, whatever its texture's alpha");
                Assert.AreEqual(TextureFormat.RGBA32, packed.GetOutputTextureFormat());
            }
        }

        [Test]
        public void TheFallbackFollowsTheSurfaceModeUnlessChosen()
        {
            var render = new AtlasRender { SurfaceMode = AtlasSurfaceMode.Cutout };
            Assert.AreEqual("ToonCutout", PoiyomiToon10OutputShader.Fallback(render));
            render.SurfaceMode = AtlasSurfaceMode.Opaque;
            render.Pass.Cull = AtlasCull.Off;
            Assert.AreEqual("ToonDoubleSided", PoiyomiToon10OutputShader.Fallback(render));
            render.SurfaceMode = AtlasSurfaceMode.Fade;
            render.Pass.DepthWrite = false;
            render.Fallback = AtlasFallbackShader.Unlit;
            Assert.AreEqual("UnlitFadeDoubleSided", PoiyomiToon10OutputShader.Fallback(render));
            render.Pass.DepthWrite = true;
            Assert.AreEqual("UnlitCutoutDoubleSided", PoiyomiToon10OutputShader.Fallback(render), "a fade with depth falls back to cutout");
            render.Fallback = AtlasFallbackShader.Hidden;
            Assert.AreEqual("Hidden", PoiyomiToon10OutputShader.Fallback(render));

            var plan = AtlasOutputPipeline.Resolve(Job(Source(AtlasSurfaceMode.Opaque)), Blended(PoiyomiModes));
            Assert.AreEqual(AtlasFallbackShader.Automatic, plan.Output.Render.Fallback);
            Assert.IsFalse(AtlasOutputFeatures.Available(AtlasOutputFeatures.Find("fallback"), new AtlasOutputCapabilities()).Any(),
                "not offered where the writer can't set it");
            Assert.IsFalse(AtlasOutputFeatures.Available(AtlasOutputFeatures.Find(AtlasSurfaceModeFeature.FeatureId), Blended(PoiyomiModes))
                .Contains(AtlasFeatureMode.Off), "the surface mode can't be off");
        }

        [Test]
        public void PoiyomiTenWritesEachModeAsItsPresetAndReadsItBack()
        {
            var output = AtlasShaderRegistry.Output("poiyomi10");
            var shader = output.FindShader();
            if (shader == null) Assert.Ignore("Poiyomi Toon 10 isn't installed.");
            foreach (var (mode, depth, queue, type) in new[] {
                (AtlasSurfaceMode.Opaque, true, 2000, "Opaque"), (AtlasSurfaceMode.Cutout, true, 2450, "TransparentCutout"),
                (AtlasSurfaceMode.Fade, false, 3000, "Transparent"), (AtlasSurfaceMode.Fade, true, 2460, "TransparentCutout"),
                (AtlasSurfaceMode.Transparent, false, 3000, "Transparent") })
            {
                var unified = new AtlasUnifiedMaterial();
                unified.Render.SurfaceMode = mode;
                unified.Render.Pass.DepthWrite = depth;
                unified.Render.Cutoff = 0.5f;
                var material = new Material(shader);
                created.Add(material);
                output.Write(unified, material);
                Assert.AreEqual(queue, material.renderQueue, mode.ToString());
                Assert.AreEqual(type, material.GetTag("RenderType", false), mode.ToString());
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
                Assert.AreEqual(mode, parsed.Render.SurfaceMode, mode + " reads back");
                Assert.AreEqual(mode == AtlasSurfaceMode.Opaque ? AtlasAlphaSource.None : AtlasAlphaSource.BaseColorAlpha,
                    parsed.Surface.Alpha.Source, mode + ": alpha");
                if (mode == AtlasSurfaceMode.Cutout) Assert.AreEqual(0.5f, parsed.Render.Cutoff, 1e-6f);
                Assert.AreEqual(depth, parsed.Render.Pass.DepthWrite, mode + ": depth write");
                Assert.AreEqual(mode == AtlasSurfaceMode.Fade && depth, parsed.Render.CutoffInTransparent, mode + ": TransClipping");
            }
        }
    }
}
