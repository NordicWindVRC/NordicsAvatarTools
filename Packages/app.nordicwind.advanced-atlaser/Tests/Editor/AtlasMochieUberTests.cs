using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Mochie Uber is free; without it in the project these tests are ignored, apart from the
    // saved-value detection.
    public sealed class AtlasMochieUberTests
    {
        private Material material;
        private Texture2D textureA, textureB;

        [SetUp]
        public void Create()
        {
            textureA = new Texture2D(4, 4);
            textureB = new Texture2D(4, 4);
        }

        [TearDown]
        public void Destroy()
        {
            if (material != null) Object.DestroyImmediate(material);
            Object.DestroyImmediate(textureA);
            Object.DestroyImmediate(textureB);
        }

        private void Uber()
        {
            var shader = Shader.Find("Mochie/Uber");
            if (shader == null) Assert.Ignore("Mochie Uber is not installed.");
            material = new Material(shader);
            material.EnableKeyword("_SHADING_ON");
        }

        private AtlasUnifiedMaterial Parse()
        {
            var parser = AtlasShaderRegistry.Detect(material);
            Assert.IsInstanceOf<AtlasMochieUberParser>(parser);
            return AtlasTestMaterials.Checked(parser.Parse(material));
        }

        [Test]
        public void DetectedFromSavedValuesAlone()
        {
            material = AtlasTestMaterials.Standard();
            foreach (string name in new[] { "_MirrorBehavior", "_CubeMode", "_PBRWorkflow", "_RenderMode", "_ColorPreservation" })
                AtlasTestMaterials.AddSavedFloat(material, name);
            using (new AtlasTestMaterials.SavedValuesOnly())
                Assert.IsInstanceOf<AtlasMochieUberParser>(AtlasShaderRegistry.Detect(material));
        }

        // Uber turns the UV clockwise around its origin after the tiling and offset; read as the
        // unified rotation (counterclockwise around the center, before them), every UV lands on
        // the same texel.
        [Test]
        public void MainTextureRotationGivesTheSameUV()
        {
            Uber();
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(2f, 2f));
            material.SetTextureOffset("_MainTex", new Vector2(0.1f, 0.3f));
            material.SetFloat("_MainTexRot", 30f);
            var map = Parse().Surface.BaseColor.Map;
            float a = -30f * Mathf.Deg2Rad;
            foreach (var uv in new[] { new Vector2(0.2f, 0.7f), new Vector2(0.9f, 0.1f), Vector2.zero })
            {
                var t = new Vector2(uv.x * 2f + 0.1f, uv.y * 2f + 0.3f);
                var uber = new Vector2(Mathf.Cos(a) * t.x - Mathf.Sin(a) * t.y, Mathf.Sin(a) * t.x + Mathf.Cos(a) * t.y);
                float b = map.Rotation * Mathf.Deg2Rad;
                var p = uv - new Vector2(0.5f, 0.5f);
                var turned = new Vector2(Mathf.Cos(b) * p.x - Mathf.Sin(b) * p.y, Mathf.Sin(b) * p.x + Mathf.Cos(b) * p.y) + new Vector2(0.5f, 0.5f);
                var unified = Vector2.Scale(turned, map.Tiling) + map.Offset;
                Assert.That(Vector2.Distance(uber, unified), Is.LessThan(1e-4f), uv.ToString());
            }
        }

        [Test]
        public void UntouchedShadedMaterialIsPhysicallyBasedWithoutReports()
        {
            Uber();
            var d = Parse();
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, d.Lighting.Family);
            Assert.AreEqual(AtlasAmbientModel.Average, d.Lighting.Ambient, "Light-probe strength defaults to 0.");
            Assert.IsTrue(d.Lighting.LimitBrightness, "Color preservation defaults to on.");
            Assert.AreEqual(AtlasSurfaceMode.Opaque, d.Render.SurfaceMode);
            Assert.IsFalse(d.Surface.Response.Highlights.Enabled);
            Assert.IsFalse(d.Surface.Response.Reflections.Enabled);
            Assert.AreEqual(0.5f, d.Surface.Response.Roughness.Evaluate(), 1e-5f);
            Assert.IsFalse(d.Surface.Response.Roughness.InvertResult);
            Assert.AreEqual(1f, d.Surface.Response.OcclusionDirect);
            Assert.AreEqual(0f, d.Surface.Response.OcclusionIndirect);
            Assert.IsTrue(d.Lighting.LightVolumes.Enabled);
            // Baked lighting: without a real-time light, 0.6 of the probes as the main light and 0.5 as ambient.
            Assert.AreEqual(0.6f, d.Lighting.Contributions.AmbientAsMainLight, 1e-6f);
            Assert.AreEqual(0.5f, d.Lighting.Contributions.AmbientWithoutMainLight, 1e-6f);
            Assert.IsTrue(d.Lighting.AdditionalLights.LimitBrightness, "Clamp Additive defaults to on.");
            Assert.IsEmpty(d.Layers);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void ShadingOffIsFlatLightClampedToTheBaseColor()
        {
            Uber();
            material.DisableKeyword("_SHADING_ON");
            var d = Parse();
            Assert.AreEqual(AtlasLightingFamily.Flat, d.Lighting.Family);
            Assert.IsTrue(d.Lighting.LimitBrightness);
            Assert.IsFalse(d.Surface.Response.Highlights.Enabled);
        }

        [Test]
        public void MetallicMapReplacesTheSliderAndMapsUseTheMainTransform()
        {
            Uber();
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(2f, 2f));
            material.SetVector("_MainTexScroll", new Vector4(0.1f, 0.2f, 0f, 0f));
            material.SetFloat("_Metallic", 0.3f);
            material.SetTexture("_MetallicGlossMap", textureB);
            material.SetInt("_UseMetallicMap", 1);
            var metallic = Parse().Surface.Response.Metallic;
            Assert.AreEqual(1f, metallic.Constant);
            Assert.AreSame(textureB, metallic.Map.Texture);
            Assert.AreEqual(new Vector2(2f, 2f), metallic.Map.Tiling);
            Assert.AreEqual(new Vector2(0.1f, 0.2f), metallic.Map.Scroll);
        }

        [Test]
        public void PackedMasksPointAtTheirChannel()
        {
            Uber();
            material.EnableKeyword("_PACKED_MASKING_ON");
            material.EnableKeyword("_REFLECTIONS_ON");
            material.SetTexture("_PackedMask0", textureA);
            material.SetFloat("_ReflectionStr", 0.5f);
            var reflections = Parse().Surface.Response.Reflections;
            Assert.IsTrue(reflections.Enabled);
            Assert.AreEqual(0.5f, reflections.Strength, 1e-5f);
            Assert.AreSame(textureA, reflections.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, reflections.Mask.Channel);
        }

        [Test]
        public void ManualBlendShadowsBecomeRampKeys()
        {
            Uber();
            material.SetInt("_ShadowMode", 1);
            material.SetFloat("_RampPos", 0f);
            material.SetFloat("_RampWidth0", 0.5f);
            material.SetFloat("_RampWeight", 0f);
            material.SetColor("_ShadowTint", Color.black);
            material.SetFloat("_ShadowStr", 0.8f);
            var lighting = Parse().Lighting;
            Assert.AreEqual(AtlasLightingFamily.RampToon, lighting.Family);
            var keys = lighting.Ramp.Keys;
            // N·L −1 → black, 0 → black, 0.5 → white, 1 → white; as half-Lambert u 0, 0.5, 0.75, 1.
            Assert.AreEqual(0f, keys.First().Position, 1e-5f);
            Assert.AreEqual(0f, keys.Single(k => Mathf.Approximately(k.Position, 0.5f)).Color.r, 1e-5f);
            Assert.AreEqual(1f, keys.Single(k => Mathf.Approximately(k.Position, 0.75f)).Color.r, 1e-5f);
            Assert.AreEqual(1f, keys.Last().Position, 1e-5f);
            Assert.AreEqual(0.8f, lighting.Ramp.RampStrength, 1e-5f);
        }

        [Test]
        public void EmissionPulseIsATimeModulatorInCyclesPerSecond()
        {
            Uber();
            material.EnableKeyword("_EMISSION_ON");
            material.EnableKeyword("_PULSE_ON");
            material.SetColor("_EmissionColor", Color.red);
            material.SetInt("_PulseWaveform", 3);
            material.SetFloat("_PulseSpeed", 2f);
            material.SetFloat("_PulseStr", 0.25f);
            var d = Parse();
            var layer = (AtlasSurfaceLayer)d.Layers.Single();
            Assert.AreEqual(AtlasPlacement.Emission, layer.Placement);
            var pulse = d.Modulators.Single();
            Assert.AreEqual("layers[0].source.color", pulse.Target);
            Assert.AreEqual(AtlasModulatorSource.Time, pulse.Source);
            Assert.AreEqual(AtlasWaveform.Saw, pulse.Time.Waveform);
            Assert.AreEqual(0.4f, pulse.Time.Frequency, 1e-5f);
            Assert.AreEqual(0.75f, pulse.Min, 1e-5f);
            Assert.AreEqual(1f, pulse.Max);
        }

        [Test]
        public void RimIsAPowerBandAndLerpHalvesTheOpacity()
        {
            Uber();
            material.EnableKeyword("_RIM_ON");
            material.SetFloat("_RimWidth", 0.6f);
            material.SetFloat("_RimEdge", 0.2f);
            material.SetFloat("_RimStr", 0.8f);
            material.SetInt("_RimBlending", 6);
            material.SetInt("_UnlitRim", 0);
            var rim = (AtlasAngleLayer)Parse().Layers.Single();
            Assert.AreEqual(AtlasAngleProfile.PowerBand, rim.Profile);
            Assert.AreEqual(4f, rim.Power, 1e-4f);
            Assert.AreEqual(0.2f, rim.Edge, 1e-5f);
            Assert.AreEqual(AtlasBlend.Replace, rim.Blend);
            Assert.AreEqual(0.4f, rim.Opacity, 1e-5f);
            Assert.AreEqual(AtlasLightResponse.ScaleByLight, rim.LightResponse);
        }

        [Test]
        public void MatcapsAreOverlayLookupLayers()
        {
            Uber();
            material.EnableKeyword("_MATCAP_ON");
            material.SetTexture("_Matcap", textureA);
            material.SetInt("_MatcapBlending", 1);
            material.SetInt("_UnlitMatcap", 1);
            var layer = (AtlasLookupLayer)Parse().Layers.Single();
            Assert.AreEqual(AtlasPlacement.Overlay, layer.Placement);
            Assert.IsTrue(layer.SourceAlphaIsOpacity);
            Assert.AreEqual(AtlasLightResponse.IgnoreLight, layer.LightResponse);
            Assert.IsTrue(layer.BlurFromRoughness);
            Assert.AreEqual(AtlasTextureSpace.View, layer.Source.Map.Space);
        }

        [Test]
        public void SubsurfaceIsALitLayer()
        {
            Uber();
            material.EnableKeyword("_SUBSURFACE_ON");
            material.SetFloat("_ScatterPow", 3f);
            material.SetTexture("_ThicknessMap", textureA);
            var layer = (AtlasSubsurfaceLayer)Parse().Layers.Single();
            Assert.AreEqual(AtlasPlacement.Lit, layer.Placement);
            Assert.AreEqual(3f, layer.Power, 1e-5f);
            Assert.AreSame(textureA, layer.Thickness.Texture);
        }

        [Test]
        public void FilteringBecomesColorAdjustmentsOnTheSurfaceOrFinalColor()
        {
            Uber();
            material.EnableKeyword("_FILTERING_ON");
            material.SetFloat("_Hue", 0.25f);
            material.SetInt("_HueMode", 1);
            material.SetFloat("_Saturation", 1.5f);
            material.SetInt("_AutoShift", 1);
            var d = Parse();
            var hue = d.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.Hue);
            Assert.AreEqual(AtlasAdjustmentTarget.SurfaceColor, hue.Target);
            Assert.AreEqual(AtlasHueMethod.Oklab, hue.HueMethod);
            Assert.AreEqual(0.25f, hue.Amount, 1e-5f);
            Assert.AreEqual(1.5f, d.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.Saturation).Amount, 1e-5f);
            Assert.AreEqual("colorAdjustments[0].amount", d.Modulators.Single().Target);

            material.EnableKeyword("_POST_FILTERING_ON");
            Assert.IsTrue(Parse().ColorAdjustments.All(a => a.Target == AtlasAdjustmentTarget.FinalColor));
        }

        [TestCase("_ALPHATEST_ON", 1, AtlasSurfaceMode.Cutout)]
        [TestCase("_ALPHATEST_ON", 2, AtlasSurfaceMode.Dithered)]
        [TestCase("_ALPHABLEND_ON", 4, AtlasSurfaceMode.Fade)]
        [TestCase("_ALPHAPREMULTIPLY_ON", 5, AtlasSurfaceMode.Transparent)]
        public void BlendingModes(string keyword, int mode, AtlasSurfaceMode expected)
        {
            Uber();
            material.EnableKeyword(keyword);
            material.SetInt("_BlendMode", mode);
            material.SetFloat("_AlphaStrength", 0.5f);
            var d = Parse();
            Assert.AreEqual(expected, d.Render.SurfaceMode);
            Assert.AreEqual(0.5f, d.Surface.Alpha.Strength, 1e-5f);
        }

        [Test]
        public void RemainingUnmappedDetailsAreReportedOnce()
        {
            Uber();
            material.EnableKeyword("_AUDIOLINK_ON");
            material.EnableKeyword("_RIM_ON");
            material.SetFloat("_AudioLinkRimPulse", 0.5f);
            material.EnableKeyword("_FLIPBOOK_ON");
            material.SetInt("_EnableSpritesheet", 1);
            material.SetTexture("_Flipbook0", new Texture2DArray(4, 4, 2, TextureFormat.RGBA32, false));
            material.SetInt("_UseSpritesheetAlpha", 1);
            var d = Parse();
            Assert.IsTrue(d.Issues.Exists(i => i.Feature == "audiolink.rim-pulse"));
            Assert.IsTrue(d.Issues.Exists(i => i.Feature == "flipbook.alpha"));
            Assert.AreEqual(2, d.Issues.Count(i => i.IsWarning));
            Object.DestroyImmediate(material.GetTexture("_Flipbook0"));
        }

        [Test]
        public void AudioLinkTargetsAreModulators()
        {
            Uber();
            material.EnableKeyword("_AUDIOLINK_ON");
            material.EnableKeyword("_EMISSION_ON");
            material.EnableKeyword("_RIM_ON");
            material.SetColor("_EmissionColor", Color.red);
            material.SetFloat("_AudioLinkEmissionMultiplier", 0.5f);
            material.SetInt("_AudioLinkEmissionBand", 2);
            material.SetFloat("_RimWidth", 0.5f);
            material.SetFloat("_AudioLinkRimWidth", 1f);
            var d = Parse();
            var emissionIndex = d.Layers.FindIndex(l => l.Placement == AtlasPlacement.Emission);
            var emission = d.Modulators.Single(x => x.Target == "layers[" + emissionIndex + "].source.color");
            Assert.AreEqual(AtlasModulatorSource.Audio, emission.Source);
            Assert.AreEqual(AtlasModulatorMode.Multiply, emission.Mode);
            Assert.AreEqual(0.5f, emission.Min, 1e-5f);
            Assert.AreEqual(1f, emission.Max, 1e-5f);
            Assert.AreEqual(AtlasAudioBand.HighMids, emission.Audio.Band);
            Assert.AreEqual(1f, emission.Audio.ValueWithoutAudioLink);
            var rimIndex = d.Layers.FindIndex(l => l is AtlasAngleLayer);
            var width = d.Modulators.Single(x => x.Target == "layers[" + rimIndex + "].power");
            Assert.AreEqual(AtlasModulatorMode.Replace, width.Mode);
            Assert.AreEqual(10f, width.Min, 1e-4f);
            Assert.AreEqual(5f, width.Max, 1e-4f);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void OscilloscopeIsAPatternLayer()
        {
            Uber();
            material.EnableKeyword("_AUDIOLINK_ON");
            material.SetFloat("_OscilloscopeStrength", 0.5f);
            material.SetVector("_OscilloscopeMarginTB", new Vector4(0.9f, 0.1f, 0f, 0f));
            var pattern = (AtlasPatternLayer)Parse().Layers.Single();
            Assert.AreEqual(AtlasPatternKind.Oscilloscope, pattern.Kind);
            Assert.AreEqual(AtlasPlacement.Overlay, pattern.Placement);
            Assert.AreEqual(0.5f, pattern.Opacity, 1e-5f);
            Assert.AreEqual(new Vector2(0.1f, 0.9f), pattern.VRange);
        }

        [Test]
        public void FiltersInvertTeamColorsMonoTintAndToneMapping()
        {
            Uber();
            material.EnableKeyword("_FILTERING_ON");
            material.SetFloat("_Invert", 1f);
            material.SetInt("_TeamFiltering", 1);
            material.SetTexture("_TeamColorMask", textureA);
            material.SetColor("_TeamColor1", Color.blue);
            material.SetInt("_MonoTint", 1);
            material.SetFloat("_Hue", 0.5f);
            material.SetFloat("_HDR", 0.5f);
            material.SetFloat("_ACES", 1f);
            var adjustments = Parse().ColorAdjustments;
            Assert.AreEqual(AtlasColorOperation.SmoothInvert, adjustments[0].Operation);
            Assert.IsNull(adjustments[0].Mask);
            Assert.AreEqual(AtlasColorOperation.ChannelTint, adjustments[1].Operation);
            Assert.AreSame(textureA, adjustments[1].ChannelMask.Texture);
            Assert.AreEqual(1f, adjustments[1].ChannelColors[1].b, 1e-4f);
            Assert.AreEqual(AtlasColorOperation.Hue, adjustments[2].Operation);
            Assert.IsTrue(adjustments[2].HueSet);
            Assert.IsTrue(adjustments.Exists(a => a.Operation == AtlasColorOperation.SrgbDecode && Mathf.Approximately(a.Amount, 0.5f)));
            Assert.IsTrue(adjustments.Exists(a => a.Operation == AtlasColorOperation.ToneMapAces));
        }

        [Test]
        public void HighlightReflectionAndShadowExtras()
        {
            Uber();
            material.EnableKeyword("_SPECULAR_ON");
            material.EnableKeyword("_SPECULAR_COMBINED_ON");
            material.EnableKeyword("_REFLECTIONS_ON");
            material.EnableKeyword("_PACKED_MASKING_ON");
            material.SetTexture("_PackedMask2", textureA);
            material.SetInt("_SharpSpecular", 1);
            material.SetInt("_SharpSpecStr", 4);
            material.SetFloat("_AnisoAngleY", 0.3f);
            material.SetInt("_RealtimeSpec", 1);
            material.SetInt("_ReflStepping", 1);
            material.SetInt("_ReflSteps", 5);
            material.SetInt("_ShadowMode", 2);
            material.SetInt("_ShadowConditions", 1);
            material.SetInt("_DitheredShadows", 1);
            material.SetFloat("_DisneyDiffuse", 0.4f);
            var d = Parse();
            var highlights = d.Surface.Response.Highlights;
            Assert.AreEqual(AtlasHighlightShape.Both, highlights.Shape);
            Assert.AreEqual(4, highlights.Steps);
            Assert.AreEqual(0.3f, highlights.Stretched.Width, 1e-5f);
            Assert.IsTrue(highlights.RealtimeLightOnly);
            Assert.AreSame(textureA, highlights.ShapeMask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, highlights.ShapeMask.Channel);
            Assert.IsFalse(highlights.ShapeMask.Invert);
            Assert.AreEqual(5, d.Surface.Response.Reflections.Steps);
            Assert.AreEqual(AtlasRampConditions.RealtimeLightOnly, d.Lighting.Ramp.Conditions);
            Assert.IsTrue(d.Lighting.Ramp.Dithered);
            Assert.AreEqual(AtlasDiffuseModel.Disney, d.Lighting.Diffuse);
            Assert.AreEqual(0.4f, d.Lighting.DisneyAmount, 1e-5f);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void StencilAndAlphaMaskMultiplier()
        {
            Uber();
            material.SetInt("_StencilToggle", 1);
            material.SetFloat("_StencilRef", 3f);
            material.SetFloat("_StencilPass", 2f);
            material.SetFloat("_StencilCompare", 3f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetInt("_BlendMode", 1);
            material.SetInt("_UseAlphaMask", 1);
            material.SetTexture("_AlphaMask", textureA);
            material.SetColor("_Color", new Color(1f, 1f, 1f, 0.5f));
            var d = Parse();
            var stencil = d.Render.Pass.Stencil;
            Assert.IsTrue(stencil.Enabled);
            Assert.AreEqual(3, stencil.Reference);
            Assert.AreEqual(AtlasStencilOperation.Replace, stencil.Pass);
            Assert.AreEqual(AtlasDepthTest.Equal, stencil.Compare);
            Assert.AreEqual(0.5f, d.Surface.Alpha.MaskMultiplier, 1e-5f);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void BaseColorDissolveIsATransitionLayer()
        {
            Uber();
            material.EnableKeyword("_BASECOLOR_DISSOLVE_ON");
            material.SetTexture("_BCNoiseTex", textureA);
            material.SetTexture("_MainTex2", textureB);
            material.SetFloat("_BCDissolveStr", 0.4f);
            material.SetFloat("_BCRimWidth", 1f);
            var layer = (AtlasTransitionLayer)Parse().Layers.Single();
            Assert.AreSame(textureA, layer.Threshold.Texture);
            Assert.AreSame(textureB, layer.Revealed.Map.Texture);
            Assert.AreEqual(0.4f, layer.Amount, 1e-5f);
            Assert.AreEqual(0.035f, layer.EdgeWidth, 1e-6f);
        }

        [Test]
        public void SpriteSheetFlipbookIsAPlacedAnimatedLayer()
        {
            Uber();
            material.EnableKeyword("_FLIPBOOK_ON");
            material.SetInt("_EnableSpritesheet", 1);
            material.SetInt("_SpritesheetMode0", 1);
            material.SetTexture("_Spritesheet", textureA);
            material.SetVector("_SpritesheetPos", new Vector4(0.1f, -0.2f, 0f, 0f));
            material.SetVector("_RowsColumns", new Vector4(4f, 2f, 0f, 0f));
            material.SetFloat("_FPS", 12f);
            material.SetInt("_SpritesheetBlending", 6);
            material.SetInt("_UnlitSpritesheet", 1);
            var layer = (AtlasSurfaceLayer)Parse().Layers.Single();
            var map = layer.Source.Map;
            Assert.AreEqual(0.6f, map.Decal.Center.x, 1e-5f);
            Assert.AreEqual(0.3f, map.Decal.Center.y, 1e-5f);
            Assert.AreEqual(AtlasFrameSource.SpriteSheet, map.Frames.Source);
            Assert.AreEqual(4, map.Frames.Columns);
            Assert.AreEqual(2, map.Frames.Rows);
            Assert.AreEqual(12f, map.Frames.Rate, 1e-5f);
            Assert.IsTrue(layer.SourceAlphaIsOpacity);
            Assert.AreEqual(AtlasPlacement.Overlay, layer.Placement);
            Assert.AreEqual(AtlasBlend.Replace, layer.Targets[0].Blend);
            Assert.AreEqual(0.5f, layer.Targets[0].Opacity, 1e-5f);
        }

        [Test]
        public void RefractionAndIridescenceAreLayers()
        {
            Uber();
            material.EnableKeyword("_REFRACTION_ON");
            material.SetFloat("_RefractionIOR", 1.5f);
            material.SetInt("_UnlitRefraction", 1);
            material.SetInt("_Iridescence", 1);
            material.SetFloat("_IridescenceWidth", 0.5f);
            var layers = Parse().Layers;
            var refraction = (AtlasRefractionLayer)layers.Single(l => l is AtlasRefractionLayer);
            Assert.AreEqual(1.5f, refraction.IOR, 1e-5f);
            Assert.AreEqual(AtlasPlacement.Overlay, refraction.Placement);
            var iridescence = (AtlasAngleLayer)layers.Single(l => l is AtlasAngleLayer);
            Assert.AreEqual(AtlasAngleProfile.HueRotation, iridescence.Profile);
            Assert.AreEqual(5f, iridescence.Power, 1e-4f);
            Assert.IsTrue(iridescence.SpecularOnly);
            Assert.IsTrue(iridescence.ScaleByMetallic);
        }

        [Test]
        public void UVDistortionIsSharedByTheMapsItShifts()
        {
            Uber();
            material.EnableKeyword("_UV_DISTORTION_ON");
            material.SetFloat("_DistortUVStr", 2f);
            material.SetInt("_DistortMainUV", 1);
            material.SetTexture("_MainTex", textureA);
            material.SetTexture("_OcclusionMap", textureB);
            var d = Parse();
            var distortion = d.Surface.BaseColor.Map.Distortion;
            Assert.IsNotNull(distortion);
            Assert.AreEqual(AtlasDistortionSource.Noise, distortion.Source);
            Assert.AreEqual(0.2f, distortion.Strength, 1e-5f);
            Assert.AreSame(distortion, d.Surface.Response.Occlusion.Map.Distortion);
        }

        [Test]
        public void MatcapNormalMapIsRead()
        {
            Uber();
            material.EnableKeyword("_MATCAP_ON");
            material.SetInt("_MatcapNormal0Toggle", 1);
            material.SetTexture("_MatcapNormal0", textureA);
            material.SetFloat("_MatcapNormal0Str", 0.5f);
            material.SetInt("_MatcapNormal0Mix", 1);
            var layer = (AtlasLookupLayer)Parse().Layers.Single();
            Assert.AreSame(textureA, layer.NormalMap.Texture);
            Assert.AreEqual(0.5f, layer.NormalStrength, 1e-5f);
            Assert.IsTrue(layer.NormalWithSurface);
        }

        [Test]
        public void HeightIsMultiStepParallaxAndMapFiltersAreRemaps()
        {
            Uber();
            material.EnableKeyword("_PARALLAXMAP_ON");
            material.SetTexture("_ParallaxMap", textureA);
            material.SetInt("_ParallaxSteps", 10);
            material.SetFloat("_ParallaxOffset", 0.2f);
            material.SetInt("_MetallicFiltering", 1);
            material.SetFloat("_MetallicRemapMax", 0.5f);
            material.SetInt("_AOFiltering", 1);
            material.SetColor("_AOTint", Color.red);
            var d = Parse();
            var parallax = d.Surface.Parallax;
            Assert.IsTrue(parallax.Enabled);
            Assert.AreEqual(AtlasParallaxMethod.MultiStep, parallax.Method);
            Assert.AreEqual(10, parallax.Steps);
            Assert.AreEqual(0.2f, parallax.HeightOffset, 1e-5f);
            Assert.AreEqual(0.5f, d.Surface.Response.Metallic.Remap.OutputMax, 1e-5f);
            Assert.IsNotNull(d.Surface.Response.Occlusion.Remap);
            Assert.AreEqual(1f, d.Surface.Response.OcclusionTint.Color.r, 1e-4f);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void MirrorNearClipAndHidingAreRenderVisibility()
        {
            Uber();
            material.SetInt("_MirrorBehavior", 3);
            material.SetInt("_NearClipToggle", 1);
            material.SetFloat("_NearClip", 0.2f);
            material.SetTexture("_NearClipMask", textureA);
            material.SetInt("_Hide", 1);
            var render = Parse().Render;
            Assert.IsFalse(render.Visibility.MirrorVR);
            Assert.IsFalse(render.Visibility.MirrorDesktop);
            Assert.IsTrue(render.Visibility.Normal);
            Assert.IsTrue(render.NearClip.Enabled);
            Assert.AreEqual(0.2f, render.NearClip.Distance, 1e-5f);
            Assert.AreSame(textureA, render.NearClip.Mask.Map.Texture);
            Assert.IsTrue(render.Invisible);

            material.SetInt("_MirrorBehavior", 2);
            material.SetTexture("_MirrorTex", textureB);
            Assert.AreSame(textureB, Parse().Render.MirrorBaseColor.Texture);
        }

        [Test]
        public void VertexManipulationBecomesDeformationsInOrder()
        {
            Uber();
            material.EnableKeyword("_VERTEX_MANIP_ON");
            material.SetVector("_VertexExpansion", new Vector4(1f, 2f, 3f, 0f));
            material.SetVector("_VertexRotation", new Vector4(10f, 0f, 0f, 0f));
            material.SetVector("_VertexPosition", new Vector4(0f, 0.1f, 0f, 0f));
            material.SetFloat("_VertexRounding", 0.5f);
            material.SetFloat("_VertexRoundingPrecision", 50f);
            var deformations = Parse().Deformations;
            Assert.AreEqual(4, deformations.Count);
            Assert.AreEqual(AtlasDeformationKind.Inflate, deformations[0].Kind);
            Assert.AreEqual(0.002f, deformations[0].Amount.y, 1e-6f);
            Assert.AreEqual(AtlasDeformationKind.Rotate, deformations[1].Kind);
            Assert.AreEqual(new Vector3(100f, 90f, 90f), deformations[1].Amount);
            Assert.AreEqual(AtlasDeformationKind.Translate, deformations[2].Kind);
            Assert.AreEqual(AtlasDeformationKind.Snap, deformations[3].Kind);
            Assert.AreEqual(0.5f, deformations[3].Strength, 1e-5f);
            Assert.AreEqual(50f, deformations[3].Precision, 1e-5f);
        }

        [Test]
        public void CubeModeIsAViewDirectionLookupOnTheBaseColor()
        {
            Uber();
            material.EnableKeyword("_CUBEMAP_COMBINED_ON");
            material.SetInt("_CubeBlendMode", 2);
            material.SetFloat("_CubeBlend", 0.4f);
            material.SetVector("_CubeRotate0", new Vector4(0f, 30f, 0f, 0f));
            material.SetInt("_AutoRotate0", 1);
            var cube = (AtlasLookupLayer)Parse().Layers.First();
            Assert.AreEqual(AtlasLookupProjection.ViewDirection, cube.Projection);
            Assert.AreEqual(AtlasPlacement.Surface, cube.Placement);
            Assert.AreEqual(AtlasBlend.Multiply, cube.Blend);
            Assert.AreEqual(0.4f, cube.Opacity, 1e-5f);
            Assert.AreEqual(new Vector3(90f, 90f, 90f), cube.Rotation);
            Assert.AreEqual(new Vector3(0f, 30f, 0f), cube.Spin);
        }

        [Test]
        public void HardEdgesClearCoatAndStaticLightDirection()
        {
            Uber();
            material.SetInt("_HardenNormals", 1);
            material.SetInt("_ClearCoat", 1);
            material.SetInt("_StaticLightDirToggle", 1);
            material.SetVector("_StaticLightDir", new Vector4(1f, 0f, 0f, 0f));
            var d = Parse();
            Assert.IsTrue(d.Surface.Normal.FlatShading);
            Assert.IsTrue(d.Surface.Response.SpecularFromMeshNormal);
            Assert.AreEqual(AtlasLightDirectionModel.Fixed, d.Lighting.Direction);
            Assert.AreEqual(Vector3.right, d.Lighting.LightDirection);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void OutlineVariantHasAnOutline()
        {
            var shader = Shader.Find("Mochie/Uber (Outline)");
            if (shader == null) Assert.Ignore("Mochie Uber (Outline) is not installed.");
            material = new Material(shader);
            material.SetFloat("_OutlineThicc", 0.2f);
            material.SetFloat("_OutlineMult", 0.5f);
            material.SetFloat("_OutlineRange", 1f);
            material.SetInt("_ApplyAlbedoTint", 1);
            material.SetInt("_ApplyOutlineLighting", 1);
            var outline = Parse().Outlines.Single();
            Assert.AreEqual(0.001f, outline.Width, 1e-6f);
            Assert.AreEqual(1f, outline.HideWithin, 1e-5f);
            Assert.IsTrue(outline.MultiplyByBaseColor);
            Assert.IsTrue(outline.Lit);
            Assert.AreEqual(AtlasCull.Front, outline.Pass.Cull);
        }

        [Test]
        public void SavedValuesReadTheSameAsTheShader()
        {
            Uber();
            material.EnableKeyword("_EMISSION_ON");
            material.SetColor("_EmissionColor", Color.green);
            material.SetFloat("_Glossiness", 0.3f);
            material.SetColor("_Color", new Color(0.3f, 0.6f, 0.9f));
            var installed = Parse();
            AtlasUnifiedMaterial saved;
            using (new AtlasTestMaterials.SavedValuesOnly()) saved = new AtlasMochieUberParser().Parse(material);
            Assert.AreEqual(0f, Vector4.Distance(installed.Surface.BaseColor.Color, saved.Surface.BaseColor.Color), 1e-5f);
            Assert.AreEqual(installed.Surface.Response.Roughness.Constant, saved.Surface.Response.Roughness.Constant, 1e-5f);
            Assert.AreEqual(installed.Layers.Count, saved.Layers.Count);
            Assert.AreEqual(installed.Lighting.Family, saved.Lighting.Family);
        }
    }
}
