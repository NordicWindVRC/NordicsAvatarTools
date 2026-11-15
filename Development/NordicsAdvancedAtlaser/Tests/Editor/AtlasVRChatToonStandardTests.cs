using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Toon Standard ships with the VRChat SDK, which every VRChat project has; without it these
    // tests are ignored.
    public sealed class AtlasVRChatToonStandardTests
    {
        private Material material;
        private Texture2D textureA, textureB;

        [SetUp]
        public void Create()
        {
            var shader = Shader.Find("VRChat/Mobile/Toon Standard");
            if (shader == null) Assert.Ignore("VRChat Mobile Toon Standard is not installed.");
            material = new Material(shader);
            textureA = new Texture2D(4, 4);
            textureB = new Texture2D(4, 4);
        }

        [TearDown]
        public void Destroy()
        {
            if (material != null) Object.DestroyImmediate(material);
            if (textureA != null) Object.DestroyImmediate(textureA);
            if (textureB != null) Object.DestroyImmediate(textureB);
        }

        private AtlasUnifiedMaterial Parse()
        {
            var parser = AtlasShaderRegistry.Detect(material);
            Assert.IsInstanceOf<AtlasVRChatToonStandardParser>(parser);
            return parser.Parse(material);
        }

        [Test]
        public void UntouchedMaterialIsRampToonWithEverySectionOff()
        {
            var d = Parse();
            Assert.AreEqual(AtlasLightingFamily.RampToon, d.Lighting.Family);
            Assert.AreEqual(AtlasAmbientModel.Average, d.Lighting.Ambient);
            Assert.IsFalse(d.Lighting.ReceiveShadows);
            Assert.IsTrue(d.Lighting.LimitBrightness);
            Assert.AreEqual(0.5f, d.Lighting.Ramp.ShadowColorFromBase, 1e-5f);
            Assert.AreEqual(AtlasAlphaSource.None, d.Surface.Alpha.Source);
            Assert.AreEqual(AtlasCull.Back, d.Render.Pass.Cull);
            Assert.IsTrue(d.Render.TwoSidedLighting);
            Assert.IsFalse(d.Surface.VertexColor.MultiplyColor);
            Assert.IsFalse(d.Surface.Response.Highlights.Enabled);
            Assert.IsFalse(d.Surface.Response.Reflections.Enabled);
            Assert.IsNull(d.Surface.Normal.Map);
            Assert.IsEmpty(d.Layers);
            Assert.IsEmpty(d.ColorAdjustments);
            Assert.IsEmpty(d.Modulators);
            Assert.IsEmpty(d.Outlines);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void SettingsOfSectionsThatAreOffHaveNoEffect()
        {
            material.SetTexture("_BumpMap", textureA);
            material.SetTexture("_DetailAlbedoMap", textureA);
            material.SetTexture("_ColorMask", textureA);
            material.SetTexture("_Matcap", textureA);
            material.SetFloat("_RimIntensity", 1f);
            material.SetFloat("_HueShift", 1f);
            material.SetTexture("_OcclusionMap", textureA);
            var d = Parse();
            Assert.IsNull(d.Surface.Normal.Map);
            Assert.IsEmpty(d.Layers);
            Assert.IsEmpty(d.ColorAdjustments);
            Assert.IsNull(d.Surface.Response.Occlusion.Map);
        }

        [Test]
        public void RampPresetIsRecognisedByItsTexture()
        {
            textureA.name = "ShadowRampToon3Band";
            material.SetTexture("_Ramp", textureA);
            material.SetFloat("_ShadowBoost", 0.3f);
            material.SetFloat("_MinBrightness", 0.05f);
            material.SetFloat("_LimitBrightness", 0f);
            var lighting = Parse().Lighting;
            Assert.AreSame(textureA, lighting.Ramp.Ramp.Texture);
            Assert.AreEqual(AtlasRampPreset.ThreeBands, lighting.Ramp.Preset);
            Assert.AreEqual(0.3f, lighting.Ramp.ShadowLift, 1e-5f);
            Assert.AreEqual(0.05f, lighting.MinimumBrightness, 1e-5f);
            Assert.IsFalse(lighting.LimitBrightness);
        }

        [Test]
        public void SpecularMapsChannelsAndReflectanceFollowTheGlossMap()
        {
            material.EnableKeyword("USE_SPECULAR");
            material.SetTexture("_MetallicMap", textureA);
            material.SetInt("_MetallicMapChannel", 2);
            material.SetFloat("_MetallicStrength", 0.6f);
            material.SetTexture("_GlossMap", textureB);
            material.SetInt("_GlossMapChannel", 1);
            material.SetFloat("_GlossStrength", 0.8f);
            material.SetFloat("_Reflectance", 0.4f);
            material.SetFloat("_SpecularSharpness", 0.7f);
            var response = Parse().Surface.Response;
            Assert.IsTrue(response.Highlights.Enabled);
            Assert.IsTrue(response.Reflections.Enabled);
            Assert.AreSame(textureA, response.Metallic.Map.Texture);
            Assert.AreEqual(AtlasChannel.Blue, response.Metallic.Channel);
            Assert.AreEqual(0.6f, response.Metallic.Constant, 1e-5f);
            Assert.AreEqual(AtlasChannel.Green, response.Roughness.Channel);
            Assert.AreEqual(1f - 0.8f * 0.5f, response.Roughness.Evaluate(0.5f), 1e-5f);
            Assert.AreSame(textureB, response.Reflectance.Map.Texture);
            Assert.AreEqual(0.2f, response.Reflectance.Evaluate(0.5f), 1e-5f);
            Assert.AreEqual(0.7f, response.Highlights.Sharpness, 1e-5f);
            Assert.IsTrue(response.SpecularAntiAliasing.Enabled);
            Assert.AreEqual(1f, response.OcclusionDirect);
            Assert.AreEqual(0f, response.OcclusionReflections);
        }

        [Test]
        public void ColorMaskIsARecolourLayerPerChannel()
        {
            material.EnableKeyword("USE_COLOR_MASK");
            material.SetTexture("_ColorMask", textureA);
            material.SetInt("_ColorMaskBlendMode", 1);
            material.SetColor("_ColorMaskColor1", new Color(1f, 0f, 0f, 0.5f));
            material.SetColor("_ColorMaskColor2", new Color(0f, 1f, 0f, 0f));
            material.SetFloat("_ColorMaskEmissionStrength2", 1.5f);
            material.SetColor("_ColorMaskColor3", new Color(0f, 0f, 1f, 0f));
            material.SetColor("_ColorMaskColor4", new Color(1f, 1f, 1f, 0f));
            var layers = Parse().Layers.Cast<AtlasSurfaceLayer>().ToList();
            Assert.AreEqual(2, layers.Count, "Regions with neither opacity nor emission are skipped.");
            Assert.AreEqual(AtlasChannel.Red, layers[0].Mask.Channel);
            Assert.AreEqual(AtlasBlend.AddToLuminance, layers[0].Targets[0].Blend);
            Assert.AreEqual(0.5f, layers[0].Targets[0].Opacity, 1e-5f);
            Assert.AreEqual(1, layers[0].Targets.Count);
            Assert.AreEqual(AtlasChannel.Green, layers[1].Mask.Channel);
            Assert.AreEqual(AtlasSurfaceTarget.Emission, layers[1].Targets[0].Target);
            Assert.AreEqual(1.5f, layers[1].Targets[0].Opacity, 1e-5f);
        }

        [TestCase(0, AtlasBlend.Replace)]
        [TestCase(1, AtlasBlend.Add)]
        [TestCase(2, AtlasBlend.Multiply)]
        [TestCase(3, AtlasBlend.MultiplyDouble)]
        public void DetailModesMapToBlends(int mode, AtlasBlend blend)
        {
            material.EnableKeyword("USE_DETAIL_MAPS");
            material.EnableKeyword("USE_NORMAL_MAPS");
            material.SetInt("_DetailMode", mode);
            material.SetTexture("_DetailAlbedoMap", textureA);
            material.SetTexture("_DetailNormalMap", textureB);
            material.SetFloat("_DetailNormalMapScale", 0.4f);
            material.SetTexture("_DetailMask", textureA);
            material.SetInt("_DetailUV", 1);
            var layers = Parse().Layers.Cast<AtlasSurfaceLayer>().ToList();
            Assert.AreEqual(2, layers.Count);
            Assert.AreEqual(blend, layers[0].Targets[0].Blend);
            Assert.AreEqual(mode == 0, layers[0].SourceAlphaIsOpacity);
            Assert.AreEqual(AtlasChannel.Alpha, layers[0].Mask.Channel);
            Assert.AreEqual(AtlasTextureSpace.UV1, layers[0].Source.Map.Space);
            Assert.AreEqual(AtlasSurfaceTarget.Normal, layers[1].Targets[0].Target);
            Assert.AreEqual(0.4f, layers[1].Targets[0].Opacity, 1e-5f);
            Assert.AreSame(layers[0].Mask, layers[1].Mask);
        }

        [Test]
        public void MatcapIsALitLookupLayerWithoutTiling()
        {
            material.EnableKeyword("USE_MATCAP");
            material.SetTexture("_Matcap", textureA);
            material.SetTextureScale("_Matcap", new Vector2(3f, 3f));
            material.SetInt("_MatcapType", 1);
            material.SetFloat("_MatcapStrength", 0.6f);
            var layer = (AtlasLookupLayer)Parse().Layers.Single();
            Assert.AreEqual(AtlasPlacement.Surface, layer.Placement);
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            Assert.AreEqual(0.6f, layer.Opacity, 1e-5f);
            Assert.AreEqual(AtlasLookupProjection.ViewStabilised, layer.Projection);
            Assert.AreEqual(AtlasTextureSpace.View, layer.Source.Map.Space);
            Assert.AreEqual(Vector2.one, layer.Source.Map.Tiling);
        }

        [Test]
        public void HueShiftsBecomeColourAdjustmentsInTurns()
        {
            material.EnableKeyword("USE_HUE_SHIFT");
            material.EnableKeyword("USE_DETAIL_MAPS");
            material.SetFloat("_HueShift", Mathf.PI);
            material.SetFloat("_EmissionHueShift", Mathf.PI / 2f);
            material.SetFloat("_DetailHueShift", Mathf.PI / 4f);
            material.SetTexture("_DetailAlbedoMap", textureA);
            material.SetColor("_EmissionColor", Color.red);
            material.SetTexture("_HueShiftMask", textureB);
            var d = Parse();
            Assert.AreEqual(3, d.ColorAdjustments.Count);
            var baseColor = d.ColorAdjustments.Single(a => a.Target == AtlasAdjustmentTarget.BaseColor);
            Assert.AreEqual(0.5f, baseColor.Amount, 1e-5f);
            Assert.AreSame(textureB, baseColor.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, baseColor.Mask.Channel);
            var emission = d.Layers.Single(l => l.Placement == AtlasPlacement.Emission);
            Assert.AreEqual(0.25f, d.ColorAdjustments.Single(a => a.Layer == emission).Amount, 1e-5f);
            var detail = d.Layers.First();
            Assert.AreEqual(0.125f, d.ColorAdjustments.Single(a => a.Layer == detail).Amount, 1e-5f);
        }

        [Test]
        public void AudioLinkIsAnEmissionLayerScaledByAnAudioModulator()
        {
            material.EnableKeyword("USE_AUDIOLINK");
            material.SetInt("_ALMaskByEmission", 0);
            material.SetTexture("_AudioLinkMask", textureA);
            material.SetInt("_ALMaskChannel", 2);
            material.SetInt("_ALMaskUVChannel", 2);
            material.SetInt("_AudioLinkMode", 1);
            material.SetInt("_ALScrollCenterOut", 1);
            material.SetFloat("_ALScrollScale", 2f);
            material.SetInt("_ALBand", 2);
            material.SetInt("_ALBlendMode", 1);
            material.SetColor("_ALTint", new Color(1f, 1f, 1f, 0.5f));
            material.SetFloat("_ALIntensity", 2f);
            material.SetInt("_ALEnableFallback", 1);
            var d = Parse();
            var layer = (AtlasSurfaceLayer)d.Layers.Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Targets[0].Blend);
            Assert.AreEqual(1f, layer.Targets[0].Opacity, 1e-5f);
            Assert.AreEqual(2f, layer.Source.Color.r, 1e-4f);
            // Channel 0 means RGB; 2 is green.
            Assert.AreEqual(AtlasChannel.Green, layer.Mask.Channel);
            Assert.AreEqual(AtlasTextureSpace.UV2, layer.Mask.Map.Space);
            var modulator = d.Modulators.Single();
            Assert.AreEqual("layers[0].source.color", modulator.Target);
            Assert.AreEqual(AtlasModulatorMode.Multiply, modulator.Mode);
            Assert.AreEqual(AtlasAudioBand.HighMids, modulator.Audio.Band);
            Assert.AreEqual(AtlasAudioSpread.History, modulator.Audio.Spread);
            Assert.AreEqual(AtlasSpreadCoordinate.ViewAngle, modulator.Audio.Coordinate);
            Assert.AreEqual(128f, modulator.Audio.HistoryOffset, 1e-4f);
            Assert.AreEqual(128f, modulator.Audio.HistoryLength, 1e-4f);
            Assert.IsTrue(modulator.Audio.FallbackBeat);
            Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
        }

        [Test]
        public void AudioLinkBarFillsAlongV()
        {
            material.EnableKeyword("USE_AUDIOLINK");
            material.SetInt("_AudioLinkMode", 2);
            material.SetFloat("_ALBarSmoothing", 0.3f);
            material.SetInt("_ALEffectUVChannel", 1);
            var audio = Parse().Modulators.Single().Audio;
            Assert.AreEqual(AtlasAudioSpread.Fill, audio.Spread);
            Assert.AreEqual(AtlasSpreadCoordinate.UVV, audio.Coordinate);
            Assert.AreEqual(AtlasTextureSpace.UV1, audio.CoordinateUV);
            Assert.AreEqual(0.3f, audio.FillSoftness, 1e-5f);
            Assert.AreEqual(7f / 15f, audio.Smoothing, 1e-5f);
        }

        [Test]
        public void RimIsAnOverlayAngleLayer()
        {
            material.EnableKeyword("USE_RIMLIGHT");
            material.SetFloat("_RimRange", 0.4f);
            material.SetFloat("_RimSharpness", 0.2f);
            material.SetFloat("_RimIntensity", 0.8f);
            material.SetFloat("_RimAlbedoTint", 0.5f);
            material.SetFloat("_RimEnvironmental", 1f);
            var rim = (AtlasAngleLayer)Parse().Layers.Single();
            Assert.AreEqual(AtlasPlacement.Overlay, rim.Placement);
            Assert.AreEqual(0.4f, rim.Width, 1e-5f);
            Assert.AreEqual(0.2f, rim.Softness, 1e-5f);
            Assert.AreEqual(0.8f, rim.Opacity, 1e-5f);
            Assert.AreEqual(0.5f, rim.BaseColorMix, 1e-5f);
            Assert.AreEqual(AtlasLightResponse.ScaleByLight, rim.LightResponse);
            Assert.AreEqual(AtlasBlend.Add, rim.Blend);
        }

        [Test]
        public void OutlineVariantHasAnUnlitOutline()
        {
            var shader = Shader.Find("VRChat/Mobile/Toon Standard (Outline)");
            if (shader == null) Assert.Ignore("The Outline variant is not installed.");
            Object.DestroyImmediate(material);
            material = new Material(shader);
            material.SetFloat("_OutlineThickness", 0.2f);
            material.SetColor("_OutlineColor", Color.red);
            material.SetFloat("_OutlineFromAlbedo", 0.25f);
            material.SetTexture("_OutlineMask", textureA);
            material.SetTextureScale("_OutlineMask", new Vector2(4f, 4f));
            material.SetInt("_OutlineMaskChannel", 3);
            var outline = Parse().Outlines.Single();
            Assert.AreEqual(0.002f, outline.Width, 1e-6f);
            Assert.AreEqual(0.01f, outline.ScreenWidthLimit, 1e-6f);
            Assert.AreEqual(0.25f, outline.BaseColorMix, 1e-5f);
            Assert.IsFalse(outline.Lit);
            Assert.AreEqual(AtlasCull.Front, outline.Pass.Cull);
            Assert.AreEqual(AtlasChannel.Alpha, outline.WidthMask.Channel);
            Assert.AreEqual(Vector2.one, outline.WidthMask.Map.Tiling, "The shader samples the mask on raw UV0.");
            Assert.AreEqual(1f, outline.Color.Color.r, 1e-4f);
        }

        [Test]
        public void SavedValuesReadTheSameAsTheShader()
        {
            material.EnableKeyword("USE_SPECULAR");
            material.EnableKeyword("USE_RIMLIGHT");
            material.EnableKeyword("USE_NORMAL_MAPS");
            material.SetTexture("_BumpMap", textureA);
            material.SetFloat("_GlossStrength", 0.3f);
            material.SetColor("_Color", new Color(0.3f, 0.6f, 0.9f));
            material.SetInt("_Culling", 0);
            var installed = Parse();
            AtlasUnifiedMaterial saved;
            using (new AtlasTestMaterials.SavedValuesOnly()) saved = new AtlasVRChatToonStandardParser().Parse(material);
            Assert.AreEqual(0f, Vector4.Distance(installed.Surface.BaseColor.Color, saved.Surface.BaseColor.Color), 1e-5f);
            Assert.AreSame(installed.Surface.Normal.Map.Texture, saved.Surface.Normal.Map.Texture);
            Assert.AreEqual(installed.Surface.Response.Roughness.Constant, saved.Surface.Response.Roughness.Constant, 1e-5f);
            Assert.AreEqual(AtlasCull.Off, saved.Render.Pass.Cull);
            Assert.AreEqual(installed.Layers.Count, saved.Layers.Count);
        }
    }
}
