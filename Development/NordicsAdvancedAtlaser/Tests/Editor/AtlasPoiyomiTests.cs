using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Poiyomi Toon 10. Most tests use a Standard material carrying Poiyomi's saved properties, read
    // as Poiyomi's, so they run without Poiyomi installed.
    public sealed class AtlasPoiyomiTests
    {
        private Material material;
        private Texture2D textureA, textureB;

        [SetUp]
        public void Create()
        {
            material = AtlasTestMaterials.Standard();
            textureA = new Texture2D(4, 4);
            textureB = new Texture2D(4, 4);
        }

        [TearDown]
        public void Destroy()
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(textureA);
            Object.DestroyImmediate(textureB);
        }

        // Poiyomi's family and version 10 markers, so detection sees Toon 10.
        private void AddToon10Markers()
        {
            foreach (string name in new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_ALDecalPosition" })
                AtlasTestMaterials.AddSavedFloat(material, name);
            AtlasTestMaterials.AddSavedColor(material, "_LightingShadowColor");
        }

        private AtlasUnifiedMaterial ParseSaved()
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasPoiyomiToon10Parser().Parse(material);
        }

        private AtlasUnifiedMaterial ParseSaved9()
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasPoiyomiToon9Parser().Parse(material);
        }

        private AtlasUnifiedMaterial ParseSaved8()
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasPoiyomiToon8Parser().Parse(material);
        }

        private AtlasUnifiedMaterial ParseSaved7()
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasPoiyomiToon7Parser().Parse(material);
        }

        [Test]
        public void ToonTenIsDetectedFromSavedValues()
        {
            AddToon10Markers();
            using (new AtlasTestMaterials.SavedValuesOnly())
                Assert.IsInstanceOf<AtlasPoiyomiToon10Parser>(AtlasShaderRegistry.Detect(material));
        }

        [Test]
        public void TextureUVPanningAndChannelSettingsFoldIn()
        {
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(3f, 3f));
            AtlasTestMaterials.AddSavedFloat(material, "_MainTexUV", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_MainTexPan", new Color(2f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_UVSettingsTiling1", new Color(2f, 2f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_MainTexStochastic", 1f);
            var map = ParseSaved().Surface.BaseColor.Map;
            Assert.AreEqual(AtlasTextureSpace.UV1, map.Space);
            Assert.AreEqual(new Vector2(6f, 6f), map.Tiling);
            Assert.AreEqual(0.1f, map.Scroll.x, 1e-5f, "Panning is in UV per 20 seconds.");
            Assert.AreEqual(AtlasTextureSampling.Stochastic, map.Sampling);
        }

        [TestCase(4, 1, 1, AtlasSurfaceMode.Additive)]
        [TestCase(5, 4, 1, AtlasSurfaceMode.SoftAdditive)]
        [TestCase(7, 2, 3, AtlasSurfaceMode.MultiplicativeDouble)]
        [TestCase(3, 1, 10, AtlasSurfaceMode.Transparent)]
        [TestCase(0, 5, 10, AtlasSurfaceMode.Custom)]
        public void RenderPresetsAndChangedFactors(int preset, int source, int destination, AtlasSurfaceMode expected)
        {
            material.SetFloat("_Mode", preset);
            material.SetFloat("_SrcBlend", source);
            material.SetFloat("_DstBlend", destination);
            var render = ParseSaved().Render;
            Assert.AreEqual(expected, render.SurfaceMode);
            if (expected == AtlasSurfaceMode.Custom)
            {
                Assert.AreEqual(AtlasBlendFactor.SourceAlpha, render.SourceBlend);
                Assert.AreEqual(AtlasBlendFactor.OneMinusSourceAlpha, render.DestinationBlend);
            }
        }

        [Test]
        public void TransClippingIsFadeWithCutoff()
        {
            material.SetFloat("_Mode", 9);
            material.SetFloat("_SrcBlend", 5);
            material.SetFloat("_DstBlend", 10);
            var render = ParseSaved().Render;
            Assert.AreEqual(AtlasSurfaceMode.Fade, render.SurfaceMode);
            Assert.IsTrue(render.CutoffInTransparent);
        }

        [Test]
        public void AlphaMapOffsetAndFades()
        {
            AtlasTestMaterials.AddSavedTexture(material, "_AlphaMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MainAlphaMaskMode", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaMod", -0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaFresnel", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaFresnelAlpha", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaForceOpaque", 0f);
            var alpha = ParseSaved().Surface.Alpha;
            Assert.AreEqual(AtlasAlphaSource.BaseColorAlpha, alpha.Source);
            Assert.AreSame(textureA, alpha.Mask.Map.Texture);
            Assert.AreEqual(AtlasAlphaMaskBlend.Add, alpha.MaskBlend);
            Assert.AreEqual(-0.2f, alpha.Offset, 1e-5f);
            var fade = alpha.Modifiers.Single();
            Assert.AreEqual(AtlasAlphaModifierKind.ViewAngleFade, fade.Kind);
            Assert.AreEqual(0.7f, fade.Intensity, 1e-5f);
        }

        [Test]
        public void ForceOpaqueIsTheDefault()
        {
            Assert.AreEqual(AtlasAlphaSource.None, ParseSaved().Surface.Alpha.Source);
        }

        [Test]
        public void NormalSectionsBecomeNormalLayersAndModifiers()
        {
            // Standard declares _DetailNormalMap itself, so it's set, not added.
            material.SetTexture("_DetailNormalMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_UseBump2ndMap", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_Bump2ndMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_Bump2ndScale", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_DetailEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_NormalCorrect", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_NormalCorrectAmount", 0.6f);
            var d = ParseSaved();
            // The detail section also adds its colour layer.
            var layers = d.Layers.Cast<AtlasSurfaceLayer>().Where(l => l.Targets[0].Target == AtlasSurfaceTarget.Normal).ToList();
            Assert.AreEqual(2, layers.Count);
            Assert.AreSame(textureA, layers[0].Source.Map.Texture);
            Assert.AreEqual(0.5f, layers[0].Targets[0].Opacity, 1e-5f);
            Assert.AreSame(textureB, layers[1].Source.Map.Texture);
            Assert.AreEqual(0.6f, d.Surface.Normal.CorrectAmount, 1e-5f);
        }

        [Test]
        public void TextureRampIsTheOneFormulaWithItsAmbientSplit()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 0f);
            AtlasTestMaterials.AddSavedTexture(material, "_ToonRamp", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_ShadowOffset", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ShadowStrength", 0.8f);
            AtlasTestMaterials.AddSavedColor(material, "_LightingShadowColor", Color.red);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingIgnoreAmbientColor", 0.25f);
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(AtlasLightingFamily.RampToon, lighting.Family);
            var ramp = lighting.Ramp;
            Assert.AreSame(textureA, ramp.Ramp.Texture);
            Assert.AreEqual(0.2f, ramp.Offset, 1e-5f);
            Assert.AreEqual(0.8f, ramp.RampStrength, 1e-5f);
            Assert.AreEqual(1f, ramp.ShadowTint.Color.r, 1e-4f);
            Assert.AreEqual(0f, ramp.ShadowTint.Color.g, 1e-4f);
            Assert.AreEqual(0.25f, ramp.ShadowDirect, 1e-5f);
            Assert.IsTrue(ramp.RampScaledShadowDirect && ramp.DirectOccluded);
            Assert.AreEqual(0.75f, ramp.ShadowAmbient, 1e-5f);
            Assert.AreEqual(0f, ramp.LitAmbient);
        }

        [Test]
        public void MultilayerMathBecomesBands()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_ShadowColor", Color.red);
            AtlasTestMaterials.AddSavedFloat(material, "_ShadowBorder", 0.4f);
            AtlasTestMaterials.AddSavedColor(material, "_Shadow2ndColor", new Color(0f, 1f, 0f, 0.5f));
            AtlasTestMaterials.AddSavedFloat(material, "_Shadow2ndBorder", 0.2f);
            AtlasTestMaterials.AddSavedColor(material, "_Shadow3rdColor", Color.clear);
            AtlasTestMaterials.AddSavedFloat(material, "_ShadowMainStrength", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMulitlayerNonLinear", 0f);
            AtlasTestMaterials.AddSavedTexture(material, "_ShadowStrengthMask", textureB);
            var ramp = ParseSaved().Lighting.Ramp;
            Assert.AreEqual(2, ramp.Bands.Count, "A band without alpha does nothing.");
            Assert.AreEqual(1f, ramp.Bands[0].Color.Color.r, 1e-4f);
            Assert.AreEqual(0f, ramp.Bands[0].Color.Color.g, 1e-4f);
            Assert.AreEqual(0.4f, ramp.Bands[0].Border, 1e-5f);
            Assert.AreEqual(0.5f, ramp.Bands[1].Alpha, 1e-5f);
            Assert.AreEqual(0.2f, ramp.Bands[1].Border, 1e-5f);
            Assert.AreEqual(0.3f, ramp.ShadowColorFromBase, 1e-5f);
            Assert.IsFalse(ramp.SmoothEdges);
            Assert.AreSame(textureB, ramp.RampStrengthMask.Map.Texture);
        }

        [Test]
        public void RealisticIsLambertWithShadowStrengthAndTint()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 6f);
            AtlasTestMaterials.AddSavedFloat(material, "_ShadowStrength", 0.4f);
            AtlasTestMaterials.AddSavedColor(material, "_LightingShadowColor", Color.red);
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, lighting.Family);
            Assert.AreEqual(AtlasDiffuseModel.Lambert, lighting.Diffuse);
            Assert.AreEqual(0.4f, lighting.ShadowStrength, 1e-5f);
            Assert.AreEqual(0f, lighting.ShadowTint.g, 1e-4f);
        }

        [TestCase(5, AtlasLightingFamily.Flat, false)]
        [TestCase(9, AtlasLightingFamily.PhysicallyBased, false)]
        [TestCase(10, AtlasLightingFamily.PhysicallyBased, true)]
        public void FlatIsMappedAndUnknownTypesAreReported(int type, AtlasLightingFamily family, bool reported)
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", type);
            var d = ParseSaved();
            Assert.AreEqual(family, d.Lighting.Family);
            Assert.AreEqual(reported, d.Issues.Any(i => i.Feature == "lighting.type"));
        }

        [Test]
        public void SdfFaceShading()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 8f);
            AtlasTestMaterials.AddSavedTexture(material, "_SDFShadingTexture", textureA);
            AtlasTestMaterials.AddSavedColor(material, "_SDFForward", new Color(0f, 0f, -1f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_SDFBlur", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingIgnoreAmbientColor", 0f);
            var ramp = ParseSaved().Lighting.Ramp;
            Assert.AreEqual(AtlasShadingTerm.SdfFace, ramp.Term);
            Assert.AreEqual(AtlasSdfStyle.Threshold, ramp.Sdf.Style);
            Assert.AreSame(textureA, ramp.Sdf.Map.Texture);
            Assert.AreEqual(Vector3.back, ramp.Sdf.Forward);
            Assert.AreEqual(0.3f, ramp.Sdf.Blur, 1e-5f);
            Assert.AreEqual(1f, ramp.ShadowAmbient, 1e-5f);
        }

        [Test]
        public void SdfLightMapIsTheBlendedTerm()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMapMode", 4f);
            AtlasTestMaterials.AddSavedTexture(material, "_LightDataSDFMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_LightDataSDFBlendY", 0.5f);
            var ramp = ParseSaved().Lighting.Ramp;
            Assert.AreEqual(AtlasShadingTerm.SdfFace, ramp.Term);
            Assert.AreEqual(AtlasSdfStyle.Blended, ramp.Sdf.Style);
            Assert.AreSame(textureB, ramp.Sdf.Map.Texture);
            Assert.AreEqual(0.5f, ramp.Sdf.VerticalBlend, 1e-5f);
        }

        [Test]
        public void WrappedGradientBecomesKeys()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingWrappedWrap", 1.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingGradientStart", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingGradientEnd", 0.6f);
            var ramp = ParseSaved().Lighting.Ramp;
            Assert.AreEqual(AtlasShadingTerm.Wrapped, ramp.Term);
            Assert.AreEqual(1.2f, ramp.Wrap, 1e-5f);
            // r rises from 0 at 1 − end to 1 over end − start + 0.001.
            Assert.AreEqual(0f, ramp.Keys.Single(k => Mathf.Approximately(k.Position, 0.4f)).Color.r, 1e-5f);
            Assert.AreEqual(1f, ramp.Keys.Last().Color.r, 1e-5f);
            Assert.AreEqual(1f, ramp.ShadowAmbient);
            Assert.AreEqual(1f, ramp.LitAmbient);
        }

        [Test]
        public void ShadeMapBecomesTwoReplacingBands()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_Use_BaseAs1st", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_BaseColor_Step", 0.6f);
            AtlasTestMaterials.AddSavedFloat(material, "_BaseShade_Feather", 0.2f);
            AtlasTestMaterials.AddSavedTexture(material, "_2nd_ShadeMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_ShadingShadeMapBlendType", 0f);
            var ramp = ParseSaved().Lighting.Ramp;
            Assert.AreEqual(2, ramp.Bands.Count);
            Assert.IsTrue(ramp.Bands[0].ColorFromBase);
            Assert.AreEqual(0.5f, ramp.Bands[0].Border, 1e-5f, "The step less half the feather.");
            Assert.AreEqual(0.2f, ramp.Bands[0].Blur, 1e-5f);
            Assert.AreSame(textureB, ramp.Bands[1].Color.Map.Texture);
            Assert.IsTrue(ramp.BandsReplaceBase);
            Assert.AreEqual(1f, ramp.ShadowDirect);
        }

        [Test]
        public void SkinIsPreintegratedDiffuse()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 3f);
            AtlasTestMaterials.AddSavedTexture(material, "_SkinLUT", textureA);
            AtlasTestMaterials.AddSavedTexture(material, "_SkinThicknessMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_SkinThicknessMapInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_SssScale", 0.6f);
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(AtlasDiffuseModel.PreintegratedSkin, lighting.Diffuse);
            Assert.AreSame(textureA, lighting.Skin.Lut.Texture);
            Assert.IsTrue(lighting.Skin.Thickness.Invert);
            Assert.AreEqual(0.6f, lighting.Skin.Scattering, 1e-5f);
        }

        [Test]
        public void ClothMapFeedsTheResponseAndSheenMask()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 7f);
            AtlasTestMaterials.AddSavedTexture(material, "_ClothMetallicSmoothnessMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_ClothMetallic", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_ClothSmoothness", 0.8f);
            var d = ParseSaved();
            var response = d.Surface.Response;
            Assert.AreEqual(AtlasDiffuseModel.Disney, d.Lighting.Diffuse);
            Assert.AreEqual(0.25f, response.Metallic.Constant, 1e-5f);
            Assert.AreEqual(AtlasChannel.Alpha, response.Roughness.Channel);
            Assert.AreEqual(0.2f, response.Roughness.Evaluate(1f), 1e-5f);
            Assert.AreEqual(AtlasChannel.Green, d.Lighting.Cloth.Mask.Channel);
        }

        [Test]
        public void LightDataModesShadowsAndLimits()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingColorMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingIndirectUsesNormals", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingDirectionMode", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingViewDirOffsetYaw", 10f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMapMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingCastedShadows", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingCapEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingCap", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMinLightBrightness", 0.1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingEnableLightVolumes", 0f);
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(AtlasLightColorModel.MergedProbes, lighting.ColorModel);
            Assert.AreEqual(AtlasAmbientModel.Directional, lighting.Ambient);
            Assert.AreEqual(0.5f, lighting.DirectionalAmount, 1e-5f);
            Assert.AreEqual(AtlasLightDirectionModel.View, lighting.Direction);
            Assert.AreEqual(10f, lighting.ViewYaw, 1e-5f);
            Assert.AreEqual(AtlasShadingTerm.Lambert, lighting.Ramp.Term);
            Assert.AreEqual(0.7f, lighting.ReceiveShadows, 1e-5f);
            Assert.IsTrue(lighting.LimitBrightness);
            Assert.AreEqual(2f, lighting.BrightnessCeiling, 1e-5f);
            Assert.AreEqual(0.1f, lighting.MinimumLight, 1e-5f);
            Assert.IsFalse(lighting.LightVolumes.Enabled);
        }

        [Test]
        public void ForcedDirectionTurnsTheCustomLightMapIntoHalfLambert()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingDirectionMode", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_LightngForcedDirection", new Color(1f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMapMode", 0f);
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(AtlasLightDirectionModel.Fixed, lighting.Direction);
            Assert.IsTrue(lighting.DirectionLocal);
            Assert.AreEqual(Vector3.right, lighting.LightDirection);
            Assert.AreEqual(AtlasShadingTerm.HalfLambert, lighting.Ramp.Term);
        }

        [Test]
        public void PackedLightDataMapsBecomeMasks()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 0f);
            AtlasTestMaterials.AddSavedTexture(material, "_LightingAOMaps", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_LightDataAOStrengthR", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightDataAOStrengthG", 0.6f);
            AtlasTestMaterials.AddSavedTexture(material, "_LightingShadowMasks", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingShadowMaskStrengthR", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingShadowMaskStrengthG", 1f);
            var d = ParseSaved();
            var occlusion = d.Surface.Response.Occlusion;
            Assert.AreSame(textureA, occlusion.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, occlusion.Channel);
            Assert.AreEqual(0.6f, occlusion.Constant, 1e-5f);
            var strengthMask = d.Lighting.Ramp.RampStrengthMask;
            Assert.AreSame(textureB, strengthMask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, strengthMask.Channel);
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "lighting.shadow-masks"), "Two channels in use are reported.");
        }

        [Test]
        public void AdditionalLights()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingAdditiveEnable", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingVertexLightingEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingAdditiveType", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingAdditiveGradientEnd", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingAdditivePassthrough", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_DisableDirectionalInAdd", 1f);
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(0f, lighting.Contributions.AdditionalLights);
            Assert.AreEqual(1f, lighting.Contributions.VertexLights);
            var additional = lighting.AdditionalLights;
            Assert.AreEqual(AtlasAdditionalLightShading.Toon, additional.Shading);
            Assert.AreEqual(0.3f, additional.GradientEnd, 1e-5f);
            Assert.AreEqual(0.4f, additional.Passthrough, 1e-5f);
            Assert.IsFalse(additional.Directional);
        }

        [Test]
        public void WithoutReflectionsAndSpecularThereAreNone()
        {
            var response = ParseSaved().Surface.Response;
            Assert.IsFalse(response.Highlights.Enabled);
            Assert.IsFalse(response.Reflections.Enabled);
        }

        [Test]
        public void ReflectionsAndSpecularFromThePackedMap()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MochieBRDF", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_MochieMetallicMaps", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieMetallicMultiplier", 0.8f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieRoughnessMultiplier", 0.6f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieMetallicMapsRoughnessChannel", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieReflectionStrength", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieSpecularMaskInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Specular2ndLayer", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieSpecularStrength2", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PBRNormalSelect", 0f);
            var response = ParseSaved().Surface.Response;
            Assert.AreSame(textureA, response.Metallic.Map.Texture);
            Assert.AreEqual(0.8f, response.Metallic.Constant, 1e-5f);
            Assert.AreEqual(AtlasChannel.Alpha, response.Roughness.Channel);
            Assert.AreEqual(0.4f, response.Roughness.Evaluate(1f), 1e-5f, "Roughness is 1 − smoothness × map.");
            Assert.AreEqual(0.5f, response.Reflections.Strength, 1e-5f);
            Assert.AreEqual(AtlasChannel.Blue, response.Reflections.Mask.Channel);
            Assert.IsTrue(response.Highlights.Mask.Invert);
            Assert.AreEqual(2f, response.Highlights.SecondLobeStrength, 1e-5f);
            Assert.IsTrue(response.SpecularFromMeshNormal);
        }

        [Test]
        public void ClearCoatAndAnisotropy()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MochieBRDF", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GGXAnisotropics", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Anisotropy", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_AnisotropyMapVertexColor", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ClearCoatBRDF", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ClearCoatStrength", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_ClearCoatSmoothness", 0.9f);
            var response = ParseSaved().Surface.Response;
            Assert.IsTrue(response.Anisotropy.Enabled);
            Assert.AreEqual(0.4f, response.Anisotropy.Amount, 1e-5f);
            Assert.AreEqual(AtlasChannel.Green, response.Anisotropy.VertexColorChannel);
            var coat = response.ClearCoat;
            Assert.AreEqual(0.7f, coat.Strength.Evaluate(), 1e-5f);
            Assert.AreEqual(0.1f, coat.Roughness.Evaluate(), 1e-5f);
            Assert.AreEqual(0f, coat.NormalMapAmount);
        }

        [Test]
        public void LtcgiUsesItsOwnValuesWithoutReflectionsAndSpecular()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LTCGIEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LTCGI_UsePBR", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LTCGI_Smoothness", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_LTCGI_DiffuseMultiplier", 1.5f);
            var ltcgi = ParseSaved().Lighting.Ltcgi;
            Assert.IsTrue(ltcgi.Enabled);
            Assert.IsFalse(ltcgi.UseSurfaceResponse);
            Assert.AreEqual(0.3f, ltcgi.Roughness, 1e-5f);
            Assert.AreEqual(1.5f, ltcgi.DiffuseStrength, 1e-5f);
        }

        [Test]
        public void DetailTextureMultipliesByTwice()
        {
            // Standard declares _DetailMask itself, so it's set, not added.
            material.SetTexture("_DetailMask", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_DetailEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DetailTex", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_DetailTexIntensity", 0.5f);
            var layer = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Targets[0].Target == AtlasSurfaceTarget.BaseColor);
            Assert.AreSame(textureA, layer.Source.Map.Texture);
            Assert.AreSame(textureB, layer.Mask.Map.Texture);
            Assert.AreEqual(AtlasBlend.MultiplyDouble, layer.Targets[0].Blend);
            Assert.AreEqual(0.5f, layer.Targets[0].Opacity, 1e-5f);
        }

        [Test]
        public void BackFaceIsALayerOnBackFaces()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_BackFaceEnabled", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceColor", Color.red);
            AtlasTestMaterials.AddSavedFloat(material, "_BackFaceEmissionStrength", 2f);
            var layer = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single();
            Assert.AreEqual(AtlasFaces.Back, layer.Faces);
            Assert.AreEqual(AtlasBlend.Replace, layer.Targets.Single().Blend);
            Assert.IsTrue(layer.SourceAlphaIsOpacity);
            Assert.AreEqual(2f, layer.EmissionAmount, 1e-5f);
        }

        [Test]
        public void DecalPlacementFoldsAspectAndSideOffsets()
        {
            var wide = new Texture2D(8, 4);
            try
            {
                AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled", 1f);
                AtlasTestMaterials.AddSavedTexture(material, "_DecalTexture", wide);
                AtlasTestMaterials.AddSavedFloat(material, "_Decal0AspectRatioMode", 1f);
                AtlasTestMaterials.AddSavedColor(material, "_DecalScale", new Color(1f, 1f, 1f, 0f));
                AtlasTestMaterials.AddSavedColor(material, "_DecalSideOffset", new Color(0.1f, 0.3f, 0f, 0f));
                AtlasTestMaterials.AddSavedColor(material, "_DecalPosition", new Color(0.5f, 0.5f, 0f, 0f));
                AtlasTestMaterials.AddSavedFloat(material, "_DecalRotationSpeed", 10f);
                AtlasTestMaterials.AddSavedFloat(material, "_DecalBlendType", 2f);
                AtlasTestMaterials.AddSavedFloat(material, "_DecalBlendAlpha", 0.5f);
                AtlasTestMaterials.AddSavedFloat(material, "_DecalAlphaIntensity", 2f);
                var layer = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single();
                var decal = layer.Source.Map.Decal;
                Assert.AreEqual(1.4f, decal.Scale.x, 1e-5f, "Widened by left and right.");
                Assert.AreEqual(0.5f, decal.Scale.y, 1e-5f, "Shrunk to the 2:1 texture.");
                Assert.AreEqual(0.6f, decal.Centre.x, 1e-5f, "Moved by half of right − left.");
                Assert.AreEqual(20f, decal.Spin, 1e-5f);
                Assert.AreEqual(AtlasBlend.Multiply, layer.Targets[0].Blend);
                Assert.AreEqual(1f, layer.Targets[0].Opacity, 1e-5f);
                Assert.IsTrue(layer.ReplacesEmission);
            }
            finally { Object.DestroyImmediate(wide); }
        }

        [Test]
        public void EmissionsAreEmissionLayers()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionStrength", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionBaseColorAsMap", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionReplace0", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission1", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_EmissionMask1", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionMask1Channel", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionMaskInvert1", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionBlinkingEnabled1", 1f);
            var d = ParseSaved();
            var layers = d.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Placement == AtlasPlacement.Emission).ToList();
            Assert.AreEqual(2, layers.Count);
            Assert.AreEqual(3f, layers[0].Targets.Single().Opacity, 1e-5f);
            Assert.IsTrue(layers[0].SourceTimesBaseColor);
            Assert.IsTrue(layers[0].OverridesBaseColor);
            Assert.AreEqual(AtlasChannel.Blue, layers[1].Mask.Channel);
            Assert.IsTrue(layers[1].Mask.Invert);
            var blink = d.Modulators.Single();
            Assert.AreEqual("layers[1].targets[0].opacity", blink.Target, "The second emission's strength blinks.");
            Assert.AreEqual(AtlasModulatorSource.Time, blink.Source);
        }

        [Test]
        public void ColorMaskChannelsBecomeLayers()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_RGBMaskEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RGBMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBARedEnable", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RedTexture", textureB);
            AtlasTestMaterials.AddSavedColor(material, "_RGBARedScaleOffset", new Color(2f, 2f, 0.5f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_RGBARedBlendType", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RedAlphaAdd", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBARedEmissionStrength", 1.5f);
            var layer = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single();
            Assert.AreSame(textureA, layer.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, layer.Mask.Channel);
            Assert.AreSame(textureB, layer.Source.Map.Texture);
            Assert.AreEqual(new Vector2(2f, 2f), layer.Source.Map.Tiling);
            Assert.AreEqual(0.5f, layer.Source.Map.Offset.x, 1e-5f);
            Assert.AreEqual(AtlasBlend.Multiply, layer.Targets.Single().Blend);
            Assert.AreEqual(0.2f, layer.SourceAlphaOffset, 1e-5f);
            Assert.AreEqual(1.5f, layer.EmissionAmount, 1e-5f);
        }

        [Test]
        public void ColorMaskByVertexColourWithANormal()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_RGBMaskEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBMaskType", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBAGreenEnable", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RgbNormalG", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_RgbNormalGScale", 0.5f);
            var layers = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().ToList();
            Assert.AreEqual(2, layers.Count);
            Assert.IsTrue(layers[0].Mask.VertexColor);
            Assert.AreEqual(AtlasChannel.Green, layers[0].Mask.Channel);
            var normal = layers[1].Targets.Single();
            Assert.AreEqual(AtlasSurfaceTarget.Normal, normal.Target);
            Assert.IsTrue(normal.ReplaceNormal, "No blend source chosen: replace.");
            Assert.AreEqual(0.5f, normal.Opacity, 1e-5f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ColorMaskSmoothnessNeedsReflectionsAndSpecular(bool section)
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MochieBRDF", section ? 1f : 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBMaskEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBABlueEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBAPBRBlueEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RGBASmoothnessMaps", textureB);
            var targets = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().SelectMany(l => l.Targets)
                .Where(t => t.Target == AtlasSurfaceTarget.Roughness).ToList();
            Assert.AreEqual(section ? 1 : 0, targets.Count);
            if (!section) return;
            Assert.AreEqual(AtlasChannel.Blue, targets[0].Channel);
            Assert.IsTrue(targets[0].InvertChannel, "Smoothness, read as roughness.");
        }

        [Test]
        public void MatcapBlendWeightsBecomeLayersInOrder()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapEnable", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_Matcap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapUVMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapReplace", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapAdd", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapAddToLight", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapBorder", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapRotation", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapEmissionStrength", 2f);
            var layers = ParseSaved().Layers.OfType<AtlasLookupLayer>().ToList();
            Assert.AreEqual(3, layers.Count);
            Assert.AreEqual(AtlasBlend.Replace, layers[0].Blend);
            Assert.AreEqual(0.5f, layers[0].Opacity, 1e-5f);
            Assert.AreEqual(AtlasBlend.Add, layers[1].Blend);
            Assert.AreEqual(AtlasPlacement.Surface, layers[1].Placement);
            Assert.AreEqual(AtlasPlacement.Overlay, layers[2].Placement, "Unlit add goes after lighting.");
            Assert.AreEqual(AtlasLookupProjection.ViewCorrected, layers[0].Projection);
            Assert.AreEqual(0.8f, layers[0].Tiling.x, 1e-5f);
            Assert.AreEqual(90f, layers[0].LookupRotation, 1e-5f);
            Assert.AreEqual(2f, layers[0].EmissionAmount, 1e-5f);
            Assert.AreEqual(0f, layers[1].EmissionAmount);
        }

        [Test]
        public void SecondMatcapUsesItsOwnNames()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_Matcap2Enable", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_MatCap2ndBlendUV1", new Color(0.3f, 0.4f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_Matcap1CustomNormal", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_Matcap1NormalMap", textureB);
            var layer = ParseSaved().Layers.OfType<AtlasLookupLayer>().Single();
            Assert.AreEqual(AtlasLookupProjection.ViewStabilised, layer.Projection, "Top pinch is the default.");
            Assert.AreEqual(0.4f, layer.UVBlend.y, 1e-5f);
            Assert.AreSame(textureB, layer.NormalMap.Texture);
        }

        [TestCase(0f, false)]
        [TestCase(10f, true)]
        public void CubeMapIsALookupLayer(float rotationX, bool reported)
        {
            AtlasTestMaterials.AddSavedFloat(material, "_CubeMapEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_CubeMapUVMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_CubemapBlendType", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_CubeMapBlendAmount", 0.6f);
            AtlasTestMaterials.AddSavedColor(material, "_CubeMapRotation", new Color(rotationX, 45f, 0f, 0f));
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasLookupLayer>().Single();
            Assert.AreEqual(AtlasLookupProjection.WorldNormal, layer.Projection);
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            Assert.AreEqual(0.6f, layer.Opacity, 1e-5f);
            Assert.AreEqual(45f, layer.Rotation.y, 1e-5f, "The xz plane.");
            Assert.AreEqual(0.5f, layer.Source.Color.a, 1e-5f, "Without a cubemap: grey at half alpha.");
            Assert.AreEqual(reported, d.Issues.Any(i => i.Feature == "cubemap.rotation-order"));
        }

        [Test]
        public void PoiyomiRimIsAFallingStepOnFacing()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimWidth", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimBlur", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimPower", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimPoiBlendMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimBlendStrength", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimShadowToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimShadowMaskStrength", 0.6f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimStrength", 3f);
            var rim = ParseSaved().Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleProfile.Step, rim.Profile);
            Assert.AreEqual(AtlasAngleBase.Facing, rim.StepBase);
            Assert.IsTrue(rim.Falling);
            Assert.AreEqual(0.475f, rim.High, 1e-5f, "lerp(−0.05, 1, width)");
            Assert.AreEqual(0.275f, rim.Low, 1e-5f);
            Assert.AreEqual(2f, rim.Power, 1e-5f);
            Assert.AreEqual(AtlasBlend.Replace, rim.Blend);
            Assert.AreEqual(0.7f, rim.Opacity, 1e-5f);
            Assert.AreEqual(0.6f, rim.HideInShadow, 1e-5f);
            Assert.AreEqual(3f, rim.EmissionAmount, 1e-5f);
            Assert.AreEqual(AtlasPlacement.Surface, rim.Placement);
        }

        [Test]
        public void SecondRimInUts2Style()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRim2Lighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Rim2Style", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Rim2Light_Power", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_Rim2Light_InsideMask", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_Rim2Light_FeatherOff", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightDirection_MaskOn2", 1f);
            var d = ParseSaved();
            var rim = d.Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleBase.Edge, rim.StepBase);
            Assert.AreEqual(8f, rim.Power, 1e-4f, "2^(3 − 3 × power)");
            Assert.AreEqual(0.3f, rim.Low, 1e-5f);
            Assert.AreEqual(0.3f, rim.High, 1e-5f, "Feather off: a hard step.");
            Assert.AreEqual(AtlasLightSideMode.Subtract, rim.LightSide.Mode, "The light direction mask.");
        }

        [Test]
        public void LilToonRimBlendsAfterLighting()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimStyle", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimBorder", 0.6f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimBlur", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimMaskOnlyMask", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RimColorTex", textureA);
            var rim = ParseSaved().Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasPlacement.Overlay, rim.Placement);
            Assert.AreEqual(0.5f, rim.Low, 1e-5f);
            Assert.AreEqual(0.7f, rim.High, 1e-5f);
            Assert.AreEqual(AtlasBlend.Add, rim.Blend);
            Assert.AreEqual(AtlasFaces.Front, rim.Faces);
            Assert.AreSame(textureA, rim.Mask.Map.Texture, "Mask only: the texture masks.");
            Assert.IsNull(rim.Color.Map);
        }

        [Test]
        public void Uts2AntipodeanRimIsADarkSideLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimStyle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Add_Antipodean_RimLight", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Tweak_LightDirection_MaskLevel", 0.1f);
            var rims = ParseSaved().Layers.OfType<AtlasAngleLayer>().ToList();
            Assert.AreEqual(2, rims.Count);
            Assert.AreEqual(AtlasLightSideMode.None, rims[0].LightSide.Mode, "No direction mask on the lit rim.");
            var dark = rims[1].LightSide;
            Assert.AreEqual(AtlasLightSideMode.Subtract, dark.Mode);
            Assert.IsTrue(dark.Invert);
            Assert.AreEqual(0.1f, dark.Level, 1e-5f);
        }

        [Test]
        public void LilToonDirectionSplitsLitAndUnlitRims()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimStyle", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimDirStrength", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimDirRange", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimIndirBorder", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimIndirBlur", 0.2f);
            var rims = ParseSaved().Layers.OfType<AtlasAngleLayer>().ToList();
            Assert.AreEqual(2, rims.Count);
            var lit = rims[0].LightSide;
            Assert.AreEqual(AtlasLightSideMode.Mask, lit.Mode);
            Assert.IsTrue(lit.BeforeStep);
            Assert.AreEqual(-0.2f, lit.Low, 1e-5f);
            Assert.AreEqual(0.5f, lit.Strength, 1e-5f);
            Assert.AreEqual(AtlasLightSideMode.Scale, rims[1].LightSide.Mode);
            Assert.IsTrue(rims[1].LightSide.Invert);
            Assert.AreEqual(0.3f, rims[1].Low, 1e-5f);
            Assert.AreEqual(0.5f, rims[1].High, 1e-5f);
        }

        [Test]
        public void DepthRimIsADepthEdgeLayerPerWeight()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDepthRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthRimAdditiveLighting", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthRimType", 0f);
            var rims = ParseSaved().Layers.OfType<AtlasAngleLayer>().ToList();
            Assert.AreEqual(2, rims.Count, "Unlit add, and screen (on by default).");
            Assert.AreEqual(AtlasAngleSource.Depth, rims[0].Source);
            Assert.AreEqual(AtlasPlacement.Overlay, rims[0].Placement);
            Assert.AreEqual(AtlasBlend.Screen, rims[1].Blend);
            Assert.AreEqual(2, rims[1].DepthEdge.Samples);
            Assert.IsTrue(rims[1].DepthEdge.Binary);
        }

        [Test]
        public void EnvironmentalRimSamplesTheProbeAlongTheNormal()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEnvironmentalRim", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimEnviroSharpness", 0.1f);
            var rim = ParseSaved().Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleColorSource.EnvironmentAlongNormal, rim.ColorSource);
            Assert.AreEqual(0.1f, rim.Low, 1e-5f);
            Assert.AreEqual(0.45f, rim.High, 1e-5f);
            Assert.IsTrue(rim.Smooth && rim.Falling);
            Assert.AreEqual(AtlasAngleBase.Facing, rim.StepBase);
        }

        [Test]
        public void BacklightIsABacklightLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_BacklightEnabled", 1f);
            var layer = ParseSaved().Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleSource.Backlight, layer.Source);
            Assert.AreEqual(0.625f, layer.Low, 1e-5f);
            Assert.AreEqual(0.675f, layer.High, 1e-5f);
            Assert.AreEqual(AtlasFaces.Front, layer.Faces);
            Assert.AreEqual(1f, layer.LightColorMix);
        }

        [Test]
        public void SubsurfaceWithoutAMapIsFullyThick()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_SubsurfaceScattering", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_SSSStrength", 0.5f);
            var layer = ParseSaved().Layers.OfType<AtlasSubsurfaceLayer>().Single();
            Assert.AreEqual(0.5f * Mathf.PI, layer.Intensity, 1e-5f, "The layer divides by π.");
            Assert.AreEqual(0f, layer.ThicknessOffset, 1e-5f, "saturate(1 − 0 − 0) = saturate(−mod) at mod −1.");
            Assert.AreEqual(AtlasSubsurfaceLight.Direct, layer.Light);
        }

        [Test]
        public void AnisotropicsAreStrandHighlightLayers()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAniso", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_AnisoReplace", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_Aniso0Power", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_Aniso0ToonMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Aniso1SwitchDirection", 1f);
            var layers = ParseSaved().Layers.OfType<AtlasHighlightLayer>().ToList();
            Assert.AreEqual(2, layers.Count, "Add (on by default) and replace.");
            Assert.AreEqual(AtlasPlacement.Overlay, layers[0].Placement);
            Assert.AreEqual(AtlasBlend.Replace, layers[1].Blend);
            Assert.AreEqual(0.4f, layers[1].Opacity, 1e-5f);
            var lobes = layers[0].Lobes;
            Assert.AreEqual(AtlasHighlightKind.Strand, layers[0].Kind);
            Assert.AreEqual(200f, lobes[0].Exponent, 1e-3f, "Power × 1000.");
            Assert.IsTrue(lobes[0].Stylised);
            Assert.IsTrue(lobes[0].Bitangent);
            Assert.IsFalse(lobes[1].Bitangent, "Switched to the tangent.");
        }

        [Test]
        public void StylizedReflectionsAreAToonHighlightLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_StylizedSpecular", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Is_BlendAddToHiColor", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_HighColor_Power", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_Layer2Strength", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_StylizedSpecularIgnoreShadow", 1f);
            var layer = ParseSaved().Layers.OfType<AtlasHighlightLayer>().Single();
            Assert.AreEqual(AtlasHighlightKind.Toon, layer.Kind);
            Assert.AreEqual(AtlasPlacement.Overlay, layer.Placement);
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            Assert.AreEqual(0.3f, layer.Lobes[0].Size, 1e-5f);
            Assert.AreEqual(0.5f, layer.Lobes[1].Strength, 1e-5f);
            Assert.IsTrue(layer.IgnoreShadows);
        }

        [Test]
        public void StylizedReflectionsInLilToonModeAreReported()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_StylizedSpecular", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_StylizedReflectionMode", 1f);
            var d = ParseSaved();
            Assert.IsEmpty(d.Layers.OfType<AtlasHighlightLayer>());
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "stylized.liltoon"));
        }

        [Test]
        public void ColorAdjustInTheShadersOrderWithItsMasks()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustToggle", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_MainColorAdjustTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShift", 0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftColorSpace", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftSelectOrShift", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainGamma", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_Saturation", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainBrightness", 0.2f);
            var adjustments = ParseSaved().ColorAdjustments;
            CollectionAssert.AreEqual(new[] { AtlasColorOperation.Hue, AtlasColorOperation.Power, AtlasColorOperation.Saturation,
                AtlasColorOperation.Brightness, AtlasColorOperation.Clamp }, adjustments.Select(a => a.Operation).ToArray());
            Assert.IsTrue(adjustments.All(a => a.Target == AtlasAdjustmentTarget.BaseColor));
            var hue = adjustments[0];
            Assert.AreEqual(AtlasHueMethod.HSV, hue.HueMethod);
            Assert.IsTrue(hue.HueSet, "Hue select.");
            Assert.AreEqual(0.25f, hue.Amount, 1e-5f);
            Assert.AreEqual(AtlasChannel.Red, hue.Mask.Channel);
            Assert.AreEqual(AtlasChannel.Alpha, adjustments[1].Mask.Channel, "Gamma uses the alpha mask.");
            Assert.AreEqual(1.5f, adjustments[2].Amount, 1e-5f);
            Assert.AreEqual(AtlasChannel.Blue, adjustments[2].Mask.Channel);
            Assert.AreEqual(1.2f, adjustments[3].Amount, 1e-5f);
            Assert.IsNull(adjustments[4].Mask);
        }

        [Test]
        public void ColorAdjustTintIsAHueTint()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustToggle", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_MainTintColor", new Color(1f, 0f, 0f, 0.5f));
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustClamp", 0f);
            var tint = ParseSaved().ColorAdjustments.Single();
            Assert.AreEqual(AtlasColorOperation.HueTint, tint.Operation);
            Assert.AreEqual(0.5f, tint.Amount, 1e-5f);
            Assert.AreEqual(0f, tint.Color.g, 1e-4f);
        }

        [Test]
        public void PostProcessingAdjustsTheFinalColor()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_PostProcess", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPContrast", 1.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPPosterization", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPPosterizationAmount", 4.5f);
            var adjustments = ParseSaved().ColorAdjustments;
            CollectionAssert.AreEqual(new[] { AtlasColorOperation.Contrast, AtlasColorOperation.Posterize }, adjustments.Select(a => a.Operation).ToArray());
            Assert.IsTrue(adjustments.All(a => a.Target == AtlasAdjustmentTarget.FinalColor));
            Assert.AreEqual(4f, adjustments[1].Amount, "Whole steps.");
        }

        [Test]
        public void LayerHueShiftsAdjustEveryLayerOfTheSection()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapReplace", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapAdd", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapHueShiftEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MatcapHueShift", 0.1f);
            var d = ParseSaved();
            var layers = d.Layers.OfType<AtlasLookupLayer>().ToList();
            Assert.AreEqual(2, layers.Count);
            Assert.AreEqual(2, d.ColorAdjustments.Count);
            Assert.AreSame(layers[0], d.ColorAdjustments[0].Layer);
            Assert.AreSame(layers[1], d.ColorAdjustments[1].Layer);
            Assert.AreEqual(AtlasAdjustmentTarget.Layer, d.ColorAdjustments[0].Target);
            Assert.AreEqual(AtlasHueMethod.Oklab, d.ColorAdjustments[0].HueMethod);
            Assert.IsFalse(d.ColorAdjustments[0].HueSet, "Hue shift is the default.");
        }

        [Test]
        public void GlobalMasksBecomeMaskBusSteps()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskTexturesEnable", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_GlobalMaskTexture1", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskVertexColorRed", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskVertexColorRedBlendType", 7f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskModifiersBackfaceEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskBackface_3", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskOptionsEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskSlider_5", 0.2f);
            var steps = ParseSaved().MaskBus.Steps;
            CollectionAssert.AreEqual(new[] { 4, 5, 6, 7 }, steps.Where(s => s.Kind == AtlasMaskBusStepKind.Texture).Select(s => s.Channel).ToArray(),
                "Texture 2 fills 2R … 2A.");
            var vertex = steps.Single(s => s.Kind == AtlasMaskBusStepKind.VertexColor);
            Assert.AreEqual(1, vertex.Channel, "1G");
            Assert.AreEqual(AtlasMaskBlend.Add, vertex.Blend);
            var faces = steps.Single(s => s.Kind == AtlasMaskBusStepKind.Faces);
            Assert.AreEqual(3, faces.Channel);
            Assert.AreEqual(AtlasFaces.Back, faces.Faces);
            Assert.AreEqual(0.2f, steps.Single(s => s.Kind == AtlasMaskBusStepKind.Offset).Amount, 1e-5f);
        }

        [Test]
        public void MasksAndScalarsReadTheMaskBus()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionMask0GlobalMask", 6f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionMask0GlobalMaskBlendType", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieBRDF", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieMetallicGlobalMask", 1f);
            var d = ParseSaved();
            var emission = d.Layers.OfType<AtlasSurfaceLayer>().Single();
            Assert.AreEqual(5, emission.Mask.Bus, "2G");
            Assert.AreEqual(AtlasMaskBlend.Max, emission.Mask.BusBlend);
            Assert.AreEqual(0, d.Surface.Response.Metallic.Bus);
            Assert.AreEqual(AtlasMaskBlend.Multiply, d.Surface.Response.Metallic.BusBlend, "Multiply by default.");
        }

        [Test]
        public void DecalWritesItsCoverageIntoTheBus()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DecalTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0ApplyGlobalMaskIndex", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0ApplyGlobalMaskBlendType", 0f);
            var d = ParseSaved();
            var write = d.MaskBus.Steps.Single();
            Assert.AreEqual(AtlasMaskBusStepKind.LayerCoverage, write.Kind);
            Assert.AreEqual(2, write.Channel);
            Assert.AreEqual(AtlasMaskBlend.Replace, write.Blend);
            Assert.AreSame(d.Layers.Single(), write.Layer);
        }

        [Test]
        public void ThemeColoursAreResolved()
        {
            material.SetColor("_Color", Color.white);
            AtlasTestMaterials.AddSavedFloat(material, "_ColorThemeIndex", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_GlobalThemeColor0", Color.red);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalThemeHue0", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalThemeHueShiftColorSpace0", 1f);
            var color = ParseSaved().Surface.BaseColor.Color;
            Assert.AreEqual(0f, color.r, 1e-3f, "Red turned half a hue in HSV: cyan.");
            Assert.AreEqual(1f, color.g, 1e-3f);
            Assert.AreEqual(1f, color.b, 1e-3f);
        }

        [Test]
        public void HueSpeedTurnsTheHueWithASaw()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftSpeed", -2f);
            var turning = ParseSaved().Modulators.Single();
            Assert.AreEqual("colorAdjustments[0].amount", turning.Target);
            Assert.AreEqual(AtlasModulatorMode.Add, turning.Mode);
            Assert.AreEqual(AtlasWaveform.ReverseSaw, turning.Time.Waveform, "A negative speed turns backwards.");
            Assert.AreEqual(0.1f, turning.Time.Frequency, 1e-5f, "Speed in turns per 20 seconds.");
        }

        [Test]
        public void EmissionLightBasedScrollingAndFlashing()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableGITDEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GITDEMaxLight", 0.8f);
            AtlasTestMaterials.AddSavedFloat(material, "_ScrollingEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissiveScroll_Interval", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionBlinkingEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissiveBlink_Mode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissiveBlink_Duty", 0.25f);
            var modulators = ParseSaved().Modulators;
            Assert.AreEqual(3, modulators.Count);
            Assert.IsTrue(modulators.All(x => x.Target == "layers[0].targets[0].opacity"));
            var glow = modulators[0];
            Assert.AreEqual(AtlasModulatorSource.LightLevel, glow.Source);
            Assert.IsTrue(glow.LightLevel.Linear && glow.LightLevel.DirectOnly);
            Assert.AreEqual(0.8f, glow.LightLevel.High, 1e-5f);
            Assert.AreEqual(1f, glow.Min, "Full strength in the dark.");
            Assert.AreEqual(AtlasModulatorSource.Sweep, modulators[1].Source);
            Assert.AreEqual(5f, modulators[1].Sweep.Interval, 1e-5f);
            Assert.AreEqual(AtlasWaveform.Pulse, modulators[2].Time.Waveform);
            Assert.AreEqual(0.25f, modulators[2].Time.Duty, 1e-5f);
        }

        [Test]
        public void BlacklightsWriteIntoTheBus()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_BlackLightMaskingEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_BlackLightMasking1GlobalMaskIndex", 4f);
            AtlasTestMaterials.AddSavedColor(material, "_BlackLightMasking1Range", new Color(0.2f, 0.6f, 0f, 0f));
            var step = ParseSaved().MaskBus.Steps.Single();
            Assert.AreEqual(AtlasMaskBusStepKind.Blacklight, step.Kind);
            Assert.AreEqual(3, step.Channel);
            Assert.AreEqual(2f, step.Amount, "The second blacklight's default key.");
            Assert.AreEqual(0.6f, step.Max, 1e-5f);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EmissionAudioLinkNeedsTheSection(bool section)
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", section ? 1f : 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionAL0Enabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionAL0StrengthBand", 2f);
            AtlasTestMaterials.AddSavedColor(material, "_EmissionAL0StrengthMod", new Color(0f, 2f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_EmissionAL0Multipliers", new Color(1f, 1f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_AudioLinkSmoothingHighMid", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionAL0ColorOverrideEnabled", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_EmissionAL0ColorOverrideRamp", new Color(0f, 1f, 0.2f, 0.8f));
            var modulators = ParseSaved().Modulators;
            if (!section) { Assert.IsEmpty(modulators); return; }
            Assert.AreEqual(2, modulators.Count, "The multipliers (1, 1) leave the strength as is.");
            var add = modulators[0];
            Assert.AreEqual(AtlasModulatorMode.Add, add.Mode);
            Assert.AreEqual(AtlasAudioBand.HighMids, add.Audio.Band);
            Assert.AreEqual(0.5f, add.Audio.Smoothing, 1e-5f);
            Assert.IsTrue(add.Audio.IdleWithoutAudioLink);
            Assert.AreEqual(2f, add.Max, 1e-5f);
            Assert.AreEqual(AtlasModulatorMode.BlendToColor, modulators[1].Mode);
            Assert.AreEqual("layers[0].source.color", modulators[1].Target);
            Assert.AreEqual(0.8f, modulators[1].Curve.w, 1e-5f);
        }

        [Test]
        public void DecalAudioLinkScaleAndChronoRotation()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DecalTexture", textureA);
            AtlasTestMaterials.AddSavedColor(material, "_AudioLinkDecal0Scale", new Color(0f, 0f, 0.5f, 0.5f));
            AtlasTestMaterials.AddSavedFloat(material, "_DecalRotationCTALSpeed0", 0.25f);
            var modulators = ParseSaved().Modulators;
            CollectionAssert.AreEqual(new[] { "layers[0].source.map.decal.scale.x", "layers[0].source.map.decal.scale.y", "layers[0].source.map.decal.rotation" },
                modulators.Select(x => x.Target).ToArray());
            var chrono = modulators[2];
            Assert.AreEqual(AtlasAudioSpread.Chrono, chrono.Audio.Spread);
            Assert.AreEqual(90f, chrono.Max, 1e-4f, "Turns × 360 degrees.");
        }

        [Test]
        public void ColorChordThemeGoesOnTheColourInput()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ColorThemeIndex", 6f);
            var d = ParseSaved();
            Assert.AreEqual(1, d.Surface.BaseColor.AudioTheme, "ColorChord 1.");
            Assert.IsFalse(d.Issues.Any(i => i.Feature == "palette.audiolink"));
        }

        [Test]
        public void HueTurnsWithAudioLinkChronotensity()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueALCTEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainALHueShiftCTIndex", 3f);
            var chrono = ParseSaved().Modulators.Single();
            Assert.AreEqual("colorAdjustments[0].amount", chrono.Target);
            Assert.AreEqual(3, chrono.Audio.ChronoMotion);
            Assert.AreEqual(1f, chrono.Max, 1e-5f, "Motion speed 1.");
        }

        [Test]
        public void ProximityColorTintsByCameraDistance()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_FXProximityColor", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_FXProximityColorMinColor", Color.red);
            AtlasTestMaterials.AddSavedFloat(material, "_FXProximityColorMinDistance", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_FXProximityColorMaxDistance", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_FXProximityColorOverrideEmission", 1f);
            var d = ParseSaved();
            CollectionAssert.AreEqual(new[] { AtlasAdjustmentTarget.FinalColor, AtlasAdjustmentTarget.Emission },
                d.ColorAdjustments.Select(a => a.Target).ToArray());
            Assert.AreEqual(0f, d.ColorAdjustments[0].Color.g, 1e-4f, "The min colour is the tint.");
            CollectionAssert.AreEqual(new[] { "colorAdjustments[0].color", "colorAdjustments[1].color" }, d.Modulators.Select(x => x.Target).ToArray());
            var distance = d.Modulators[0];
            Assert.AreEqual(AtlasModulatorSource.CameraDistance, distance.Source);
            Assert.AreEqual(AtlasModulatorMode.BlendToColor, distance.Mode);
            Assert.AreEqual(1f, distance.CameraDistance.Near, 1e-5f);
            Assert.AreEqual(3f, distance.CameraDistance.Far, 1e-5f);
        }

        [Test]
        public void EmissionFluorescenceAndCentreOut()
        {
            // Standard declares _EmissionMap itself, so it's set, not added.
            material.SetTexture("_EmissionMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionFluorescence", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionCenterOutEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionCenterOutSpeed", 2f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasSurfaceLayer>().Single();
            Assert.AreEqual(AtlasTextureSpace.FacingGradient, layer.Source.Map.Space);
            Assert.AreEqual(0.1f, layer.Source.Map.Scroll.x, 1e-5f, "Panning at the speed, in UV per 20 seconds.");
            Assert.AreEqual(0.1f, layer.Source.Map.Scroll.y, 1e-5f);
            var fluorescence = d.ColorAdjustments.Single();
            Assert.AreEqual(AtlasColorOperation.Fluorescence, fluorescence.Operation);
            Assert.AreSame(layer, fluorescence.Layer);
            Assert.AreEqual(0.5f, fluorescence.Amount, 1e-5f);
        }

        [Test]
        public void OutlineShapeWidthColourAndBlending()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineExpansionMode", 4f);
            AtlasTestMaterials.AddSavedColor(material, "_OutlineDropShadowOffset", new Color(0f, -1f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_LineWidth", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineVertexColorMask", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineTintMix", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiUTSStyleOutlineBlend", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineSrcBlend", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineDstBlend", 10f);
            var outline = ParseSaved().Outlines.Single();
            Assert.AreEqual(AtlasOutlineShape.DropShadow, outline.Shape);
            Assert.AreEqual(Vector3.down, outline.Direction);
            Assert.AreEqual(0.02f, outline.Width, 1e-6f, "Size in hundredths of a unit.");
            Assert.IsTrue(outline.ObjectSpace);
            Assert.AreEqual(AtlasChannel.Green, outline.WidthVertexMask.Channel);
            Assert.AreEqual(0.5f, outline.BaseColorMultiply, 1e-5f);
            Assert.IsTrue(outline.BaseColorSquared);
            Assert.AreEqual(0.5f, outline.FixedSize, 1e-5f, "Fixed size is on by default.");
            Assert.AreEqual(AtlasCull.Front, outline.Pass.Cull);
            Assert.AreEqual(AtlasBlendFactor.SourceAlpha, outline.SourceBlend);
            Assert.AreEqual(AtlasBlendFactor.OneMinusSourceAlpha, outline.DestinationBlend);
        }

        [Test]
        public void OutlineHighlightFadeAndAudioLinkSize()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineHighlightEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineHighlightBorder", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineAlphaDistanceFade", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineAlphaDistanceFadeMax", 2f);
            AtlasTestMaterials.AddSavedColor(material, "_AudioLinkOutlineSize", new Color(0f, 3f, 0f, 0f));
            var d = ParseSaved();
            var outline = d.Outlines.Single();
            Assert.IsTrue(outline.Highlight.Enabled);
            Assert.AreEqual(0.7f, outline.Highlight.Border, 1e-5f);
            Assert.AreEqual(2f, outline.AlphaModifiers.Single().MaxDistance, 1e-5f);
            var size = d.Modulators.Single();
            Assert.AreEqual("outlines[0].width", size.Target);
            Assert.AreEqual(0.03f, size.Max, 1e-6f);
        }

        [Test]
        public void DistortedUVSharesTheDistortion()
        {
            material.SetTexture("_MainTex", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_MainTexUV", 7f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDistortion", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DistortionUvToDistort", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DistortionFlowTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_DistortionStrength", 0.1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DistortionStrength1", 0.02f);
            var map = ParseSaved().Surface.BaseColor.Map;
            Assert.AreEqual(AtlasTextureSpace.UV1, map.Space, "The distorted channel.");
            var distortion = map.Distortion;
            Assert.AreSame(textureA, distortion.NormalMap.Texture);
            Assert.AreEqual(0.05f, distortion.Strength, 1e-6f, "Averaged with the second map.");
            Assert.AreEqual(-0.01f, distortion.ConstantOffset.x, 1e-6f, "A missing second map reads as −1.");
        }

        [Test]
        public void UVChannelRotationFoldsIntoTheTexture()
        {
            material.SetTexture("_MainTex", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_UVSettingsAngle0", 30f);
            AtlasTestMaterials.AddSavedFloat(material, "_UVSettingsRotate0", 10f);
            var d = ParseSaved();
            var map = d.Surface.BaseColor.Map;
            Assert.AreEqual(30f, map.Rotation, 1e-5f);
            Assert.AreEqual(10f, map.Spin, 1e-5f);
            Assert.IsFalse(d.Issues.Any(i => i.Feature == "uv.channel-rotation"), "Exact without the channel's own tiling.");
        }

        [Test]
        public void ParallaxHeightmappingIsMultiStep()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_PoiParallax", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_HeightMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_HeightStrength", 0.02f);
            AtlasTestMaterials.AddSavedFloat(material, "_HeightStepsMax", 64f);
            var parallax = ParseSaved().Surface.Parallax;
            Assert.IsTrue(parallax.Enabled);
            Assert.AreEqual(AtlasParallaxMethod.MultiStep, parallax.Method);
            Assert.AreSame(textureB, parallax.Height.Map.Texture);
            Assert.AreEqual(0.02f, parallax.Depth, 1e-6f);
            Assert.AreEqual(64, parallax.Steps);
        }

        [Test]
        public void DissolveIsATransitionLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDissolve", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveType", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_SphericalDissolveRadius", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_SphericalDissolveInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveAlpha", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveSoftness", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveEdgeEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveEdgeWidth", 0.05f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveEdgeFlip", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveAlphaOnly", 1f);
            var layer = ParseSaved().Layers.OfType<AtlasTransitionLayer>().Single();
            Assert.AreEqual(AtlasTransitionSource.Sphere, layer.Source);
            Assert.AreEqual(2f, layer.Radius, 1e-5f);
            Assert.IsTrue(layer.InvertThreshold);
            Assert.AreEqual(0.4f, layer.Amount, 1e-5f);
            Assert.AreEqual(0.2f, layer.Softness, 1e-5f);
            Assert.IsTrue(layer.Padded);
            Assert.AreEqual(0.05f, layer.EdgeWidth, 1e-5f);
            Assert.IsTrue(layer.EdgeFlip && layer.EdgeReplacesColor);
            Assert.IsTrue(layer.AlphaOnly);
        }

        [Test]
        public void ContinuousDissolveAndUndissolvedOutput()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDissolve", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContinuousDissolveEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContinuousDissolveWave", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContinuousDissolveSpeed", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContinuousDissolvePhase", 0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContinuousDissolveBlend", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_DissolveInverseApplyGlobalMaskIndex", 2f);
            var d = ParseSaved();
            var continuous = d.Modulators.Single();
            Assert.AreEqual("layers[0].amount", continuous.Target);
            Assert.AreEqual(AtlasWaveform.Pulse, continuous.Time.Waveform);
            Assert.AreEqual(0.375f, continuous.Time.Offset, 1e-5f, "(phase + half a cycle) ÷ speed.");
            Assert.AreEqual(AtlasModulatorMode.Multiply, continuous.Mode);
            var output = d.MaskBus.Steps.Single();
            Assert.AreEqual(1, output.Channel);
            Assert.IsTrue(output.InvertCoverage, "The undissolved part.");
        }

        [Test]
        public void InstalledToonTenIsDetectedWithoutWarnings()
        {
            Shader shader = null;
            foreach (string name in new[] { ".poiyomi/Poiyomi Toon", ".poiyomi/Old Versions/10.0/Poiyomi Toon" })
            {
                var candidate = Shader.Find(name);
                if (candidate != null && AtlasPoiyomiVersionDetector.DetectFromMetadata(candidate.name,
                        UnityEditor.AssetDatabase.GetAssetPath(candidate), AtlasPoiyomiVersionDetector.GetShaderMasterLabel(candidate)) == 10)
                { shader = candidate; break; }
            }
            if (shader == null) Assert.Ignore("Poiyomi Toon 10 is not installed.");
            var poiyomi = new Material(shader);
            try
            {
                var parser = AtlasShaderRegistry.Detect(poiyomi);
                Assert.IsInstanceOf<AtlasPoiyomiToon10Parser>(parser);
                var d = parser.Parse(poiyomi);
                Assert.AreEqual(AtlasSurfaceMode.Opaque, d.Render.SurfaceMode);
                CollectionAssert.IsEmpty(d.Issues.Where(i => i.IsWarning).Select(i => i.Feature).ToArray());
            }
            finally { Object.DestroyImmediate(poiyomi); }
        }

        [Test]
        public void FlipbookIsAPlacedTextureArrayLayer()
        {
            var array = new Texture2DArray(4, 4, 8, TextureFormat.RGBA32, false);
            try
            {
                AtlasTestMaterials.AddSavedFloat(material, "_EnableFlipbook", 1f);
                AtlasTestMaterials.AddSavedTexture(material, "_FlipbookTexArray", array);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookFPS", 12f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookFrameOffset", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookStartAndEnd", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookStartFrame", 2f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookEndFrame", 6f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookCrossfadeEnabled", 1f);
                AtlasTestMaterials.AddSavedColor(material, "_FlipbookCrossfadeRange", new Color(0.5f, 1f, 0f, 1f));
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookTiled", 1f);
                AtlasTestMaterials.AddSavedColor(material, "_FlipbookSideOffset", new Color(0.1f, 0.3f, 0f, 0f));
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookRotationSpeed", 5f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookBlendType", 8f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookAlpha", 0.5f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookColorReplaces", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookIntensityControlsAlpha", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookEmissionStrength", 2f);
                var layer = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single();
                var map = layer.Source.Map;
                Assert.AreSame(array, map.Texture);
                Assert.AreEqual(AtlasFrameSource.TextureArray, map.Frames.Source);
                Assert.AreEqual(12f, map.Frames.Rate, 1e-5f);
                Assert.AreEqual(1f, map.Frames.Offset, 1e-5f);
                Assert.AreEqual(2, map.Frames.First);
                Assert.AreEqual(4, map.Frames.Count, "Frames 2 to 5.");
                Assert.IsTrue(map.Frames.Crossfade);
                Assert.AreEqual(0.5f, map.Frames.CrossfadeRange.x, 1e-5f);
                Assert.IsFalse(map.Decal.ClipOutside, "Tiled.");
                Assert.AreEqual(1.4f, map.Decal.Scale.x, 1e-5f, "Widened by left and right.");
                Assert.AreEqual(10f, map.Decal.Spin, 1e-5f);
                Assert.AreEqual(AtlasBlend.Add, layer.Targets[0].Blend);
                Assert.AreEqual(0.5f, layer.Targets[0].Opacity, 1e-5f);
                Assert.IsTrue(layer.SourceMapIsMask);
                Assert.IsTrue(layer.SourceIntensityIsAlpha);
                Assert.AreEqual(2f, layer.EmissionAmount, 1e-5f);
            }
            finally { Object.DestroyImmediate(array); }
        }

        [Test]
        public void SecondFlipbookManualFrameWrapsAndTakesAudioLink()
        {
            var array = new Texture2DArray(4, 4, 8, TextureFormat.RGBA32, false);
            try
            {
                AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_EnableFlipbook1", 1f);
                AtlasTestMaterials.AddSavedTexture(material, "_FlipbookTexArray1", array);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookManualFrameControl1", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookCurrentFrame1", 10f);
                AtlasTestMaterials.AddSavedColor(material, "_AudioLinkFlipbookFrame1", new Color(0f, 4f, 0f, 0f));
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookChronotensityEnabled1", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookChronotensitySpeed1", 2f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookALColorOverrideEnabled1", 1f);
                AtlasTestMaterials.AddSavedColor(material, "_FlipbookALColorOverride1", Color.red);
                var d = ParseSaved();
                var frames = d.Layers.OfType<AtlasSurfaceLayer>().Single().Source.Map.Frames;
                Assert.IsTrue(frames.Fixed);
                Assert.AreEqual(2f, frames.FixedFrame, 1e-5f, "10 mod 8.");
                CollectionAssert.AreEqual(new[] { "layers[0].source.map.frames.offset", "layers[0].source.map.frames.offset", "layers[0].source.color" },
                    d.Modulators.Select(x => x.Target).ToArray());
                Assert.AreEqual(4f, d.Modulators[0].Max, 1e-5f);
                Assert.AreEqual(AtlasAudioSpread.Chrono, d.Modulators[1].Audio.Spread);
                Assert.AreEqual(AtlasModulatorMode.BlendToColor, d.Modulators[2].Mode);
                Assert.AreEqual(Color.red, d.Modulators[2].Color);
            }
            finally { Object.DestroyImmediate(array); }
        }

        [Test]
        public void MirrorAndCameraVisibilityWithAMirrorTexture()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableMirrorOptions", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VisibilityMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VisibilityVRCRegular", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_VisibilityVRCCameraScreenshot", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_MirrorTextureEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_MirrorTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MirrorTextureBlendType", 2f);
            AtlasTestMaterials.AddSavedColor(material, "_MirrorColor", new Color(1f, 0f, 0f, 0.5f));
            var d = ParseSaved();
            var visibility = d.Render.Visibility;
            Assert.IsFalse(visibility.Normal);
            Assert.IsFalse(visibility.Screenshot);
            Assert.IsTrue(visibility.MirrorVR);
            Assert.IsTrue(visibility.CameraDesktop);
            var layers = d.Layers.OfType<AtlasSurfaceLayer>().ToArray();
            Assert.AreEqual(2, layers.Length);
            Assert.IsTrue(layers.All(l => l.Mirror == AtlasMirrorVisibility.MirrorOnly));
            Assert.AreSame(textureA, layers[0].Source.Map.Texture);
            Assert.AreEqual(AtlasBlend.Multiply, layers[0].Targets[0].Blend);
            Assert.AreEqual(0.5f, layers[0].Targets[0].Opacity, 1e-5f, "Texture alpha × colour alpha.");
            Assert.AreEqual(Color.red, layers[1].Source.Color);
            Assert.AreEqual(0.5f, layers[1].Targets[0].Opacity, 1e-5f, "× lerp(1, colour, colour alpha).");
        }

        [Test]
        public void GenericMirrorOnlyHidesCameras()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableMirrorOptions", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VisibilityMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_Mirror", 1f);
            var visibility = ParseSaved().Render.Visibility;
            Assert.IsFalse(visibility.Normal);
            Assert.IsFalse(visibility.CameraVR);
            Assert.IsTrue(visibility.MirrorDesktop);
        }

        [Test]
        public void UVTileDiscardHidesTiles()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableUDIMDiscardOptions", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_UDIMDiscardUV", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_UDIMDiscardRow0_1", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_UDIMDiscardRow2_3", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableUDIMFaceDiscardOptions", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_UDIMFaceDiscardFace", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_UDIMFaceDiscardRow0_0", 1f);
            var tiles = ParseSaved().Render.HiddenTiles;
            Assert.AreEqual(2, tiles.Count);
            Assert.AreEqual(AtlasTextureSpace.UV1, tiles[0].Space);
            Assert.AreEqual((1 << 1) | (1 << 11), tiles[0].Tiles, "Row 0 column 1, row 2 column 3.");
            Assert.AreEqual(AtlasFaces.Both, tiles[0].Faces);
            Assert.AreEqual(AtlasFaces.Back, tiles[1].Faces);
            Assert.AreEqual(1, tiles[1].Tiles);
        }

        [Test]
        public void GlitterIsAPatternLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterShape", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterBlendType", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterUV", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_GlitterUVPanning", new Color(2f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterFrequency", 150f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterLayers", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterLinearBrightness", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterLinearSpeed", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterBrightness", 9f);
            AtlasTestMaterials.AddSavedColor(material, "_GlitterColor", new Color(1f, 1f, 1f, 0.5f));
            AtlasTestMaterials.AddSavedTexture(material, "_GlitterTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterSpriteSheetEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterSpriteSheetColumns", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterSpriteSheetRows", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterSpriteSheetFrameSelection", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_GlitterMask", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterMaskInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterHideInShadow", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterHueShiftEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterHueShiftRandomOffset", 1f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            var glitter = layer.Glitter;
            Assert.AreEqual(AtlasPatternKind.Glitter, layer.Kind);
            Assert.AreEqual(AtlasGlitterMode.Linear, glitter.Mode);
            Assert.AreEqual(AtlasGlitterShape.Square, glitter.Shape);
            Assert.AreEqual(AtlasTextureSpace.UV1, glitter.Space);
            Assert.AreEqual(0.1f, glitter.Scroll.x, 1e-5f, "Panning is in UV per 20 seconds.");
            Assert.AreEqual(150f, glitter.Density, 1e-5f);
            Assert.AreEqual(3, glitter.Layers);
            Assert.AreEqual(5f, glitter.MaxBrightness, 1e-5f, "Linear mode's own brightness.");
            Assert.AreEqual(0.5f, glitter.Speed, 1e-5f);
            Assert.AreSame(textureA, glitter.Texture.Texture);
            Assert.AreEqual(4, glitter.Texture.Frames.Columns);
            Assert.AreEqual(2, glitter.Texture.Frames.Rows);
            Assert.IsFalse(glitter.RandomFrame, "Time based.");
            Assert.IsTrue(glitter.HueRandomOffset);
            Assert.AreEqual(AtlasBlend.Replace, layer.Blend);
            Assert.AreEqual(AtlasPlacement.Surface, layer.Placement);
            Assert.AreEqual(0.5f, layer.Opacity, 1e-5f, "The colour's alpha.");
            Assert.AreEqual(0.5f, layer.HideInShadow, 1e-5f);
            Assert.IsTrue(layer.Mask.Invert);
            Assert.AreEqual(1, d.ColorAdjustments.Count(a => a.Layer == layer));
        }

        [Test]
        public void GlitterAddsEmissionAndTakesAudioLink()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterALEnabled", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_GlitterALSizeAdd", new Color(0f, 0.2f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterALChronoSparkleSpeed", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlitterALColorOverrideEnabled", 1f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreEqual(AtlasPlacement.Emission, layer.Placement);
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            Assert.AreEqual(3f, layer.Glitter.MaxBrightness, 1e-5f);
            CollectionAssert.AreEqual(new[] { "layers[0].glitter.size", "layers[0].glitter.sparkleOffset", "layers[0].color" },
                d.Modulators.Select(x => x.Target).ToArray());
            Assert.AreEqual(0.2f, d.Modulators[0].Max, 1e-5f);
            Assert.AreEqual(AtlasAudioSpread.Chrono, d.Modulators[1].Audio.Spread);
        }

        [Test]
        public void VoronoiIsAPatternLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiType", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiSpace", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiUV", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiBlend", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_VoronoiSpeed", new Color(2f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_VoronoiOuterColor", new Color(0f, 0f, 0f, 1f));
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiInnerEmissionStrength", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_AudioLinkVoronoiGradientMaxAdd", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_AudioLinkVoronoiChronoSpeedYSpeed", 50f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            var voronoi = layer.Voronoi;
            Assert.AreEqual(AtlasPatternKind.Voronoi, layer.Kind);
            Assert.AreEqual(AtlasVoronoiType.Cells2D, voronoi.Type);
            Assert.AreEqual(AtlasVoronoiSpace.UV, voronoi.Space);
            Assert.AreEqual(AtlasTextureSpace.UV1, voronoi.UV);
            Assert.AreEqual(1f, voronoi.Scroll.x, 1e-5f, "Speed × 10 per 20 seconds.");
            Assert.AreEqual(Color.black, voronoi.OuterColor);
            Assert.AreEqual(4f, voronoi.InnerEmission, 1e-5f);
            Assert.IsTrue(voronoi.EmissionOnly);
            Assert.AreEqual(AtlasPlacement.Emission, layer.Placement);
            CollectionAssert.AreEqual(new[] { "layers[0].voronoi.gradient.y", "layers[0].voronoi.offset.y" },
                d.Modulators.Select(x => x.Target).ToArray());
            Assert.AreEqual(-0.3f, d.Modulators[0].Max, 1e-5f, "The gradient's end falls.");
            Assert.AreEqual(0.5f, d.Modulators[1].Max, 1e-5f, "Chrono × 0.01.");
        }

        [Test]
        public void TruchetIsAPatternLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableTruchet", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_TruchetPanOffset", new Color(2f, 0f, 0.25f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_TruchetDensity", 20f);
            AtlasTestMaterials.AddSavedFloat(material, "_TruchetBlendType", 8f);
            AtlasTestMaterials.AddSavedFloat(material, "_TruchetAlpha", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_TruchetEmissionStrength", 3f);
            AtlasTestMaterials.AddSavedColor(material, "_TruchetColor", new Color(1f, 0f, 0f, 0.2f));
            AtlasTestMaterials.AddSavedTexture(material, "_TruchetTex", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_TruchetChronoPanToggle", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_TruchetChronoPanSpeed", new Color(0f, 1f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_TruchetALAlphaToggle", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_TruchetALAlpha", new Color(0.5f, 1f, 0f, 0f));
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            var truchet = layer.Truchet;
            Assert.AreEqual(AtlasPatternKind.Truchet, layer.Kind);
            Assert.AreEqual(20f, truchet.Density, 1e-5f);
            Assert.AreEqual(0.1f, truchet.Scroll.x, 1e-5f);
            Assert.AreEqual(0.25f, truchet.Offset.x, 1e-5f);
            Assert.AreSame(textureA, truchet.Texture.Texture);
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            Assert.AreEqual(0.5f, layer.Opacity, 1e-5f);
            Assert.AreEqual(3f, layer.EmissionAmount, 1e-5f);
            Assert.AreEqual(Color.red, layer.Color, "The colour's alpha is unused.");
            CollectionAssert.AreEqual(new[] { "layers[0].opacity", "layers[0].truchet.textureOffset.y" },
                d.Modulators.Select(x => x.Target).ToArray());
            Assert.AreEqual(AtlasModulatorMode.Multiply, d.Modulators[0].Mode);
        }

        [Test]
        public void PathingHasFourChannels()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnablePathing", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_PathingMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_PathSurfaceBlendMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathTypeG", 3f);
            AtlasTestMaterials.AddSavedColor(material, "_PathColorG", new Color(0f, 1f, 0f, 0.5f));
            AtlasTestMaterials.AddSavedColor(material, "_PathSpeed", new Color(0.1f, 0.2f, 0.1f, 0.1f));
            AtlasTestMaterials.AddSavedColor(material, "_PathTime", new Color(-999f, 0.4f, -999f, -999f));
            AtlasTestMaterials.AddSavedColor(material, "_PathSegments", new Color(0f, -4f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_PathGlobalMaskG", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathALWidthOffset", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_AudioLinkPathWidthOffsetG", new Color(0f, 0.1f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_PathALHistory", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathALHistoryR", 1f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            var path = layer.Path;
            Assert.AreEqual(AtlasPatternKind.Path, layer.Kind);
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            Assert.AreSame(textureA, path.Map.Texture);
            var green = path.Channels[1];
            Assert.AreEqual(AtlasPathType.Dashed, green.Type);
            Assert.AreEqual(0.5f, green.Color.a, 1e-5f);
            Assert.AreEqual(0.2f, green.Speed, 1e-5f);
            Assert.IsTrue(green.ManualTime);
            Assert.AreEqual(0.4f, green.Time, 1e-5f);
            Assert.AreEqual(4f, green.Segments, 1e-5f);
            Assert.AreEqual(1, green.Mask.Bus, "1G.");
            Assert.IsFalse(path.Channels[0].ManualTime);
            Assert.AreEqual(AtlasPathType.Loop, path.Channels[0].Type);
            var width = d.Modulators.Single();
            Assert.AreEqual("layers[0].path.channels[1].length", width.Target);
            Assert.AreEqual(0.2f, width.Max, 1e-5f, "Poiyomi adds to the half length.");
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "audiolink.path-history"));
        }

        [Test]
        public void InternalParallaxIsAPatternLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiInternalParallax", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_ParallaxInternalMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalHeightmapMode", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_ParallaxInternalMapPan", new Color(0.2f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalIterations", 8f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalMaxDepth", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalTiled", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalHueShiftEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalHueShiftPerLevel", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalAudioLinkEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalALHueShiftSpeed", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalALHistoryEnabled", 1f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            var parallax = layer.InternalParallax;
            Assert.AreEqual(AtlasPatternKind.InternalParallax, layer.Kind);
            Assert.AreEqual(AtlasInternalParallaxMode.Heightmap, parallax.Mode);
            Assert.AreSame(textureA, parallax.Map.Texture);
            Assert.AreEqual(Vector2.zero, parallax.Map.Scroll, "The pan is the effect's own scroll.");
            Assert.AreEqual(0.2f, parallax.Scroll.x, 1e-5f, "Per second.");
            Assert.AreEqual(8, parallax.Iterations);
            Assert.AreEqual(0.3f, parallax.MaxDepth, 1e-5f);
            Assert.IsFalse(parallax.Tiled);
            Assert.AreEqual(0.5f, parallax.HuePerLevel, 1e-5f);
            Assert.AreEqual(AtlasBlend.Add, layer.Blend, "Poiyomi's default surface blend.");
            Assert.AreEqual("colorAdjustments[0].amount", d.Modulators.Single().Target, "The hue turns with chronotensity.");
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "audiolink.parallax-history"));
        }

        [Test]
        public void DepthFxColoursByTheSceneBehind()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableTouchGlow", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthColorToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthColorBlendMode", 2f);
            AtlasTestMaterials.AddSavedTexture(material, "_DepthTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthTextureUV", 8f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthColorMaxDepth", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthEmissionStrength", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthAlphaToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthAlphaMinValue", 0.25f);
            var layer = ParseSaved().Layers.OfType<AtlasPatternLayer>().Single();
            var depth = layer.Depth;
            Assert.AreEqual(AtlasPatternKind.Depth, layer.Kind);
            Assert.IsTrue(depth.ColorEnabled);
            Assert.AreEqual(AtlasDepthBlend.Add, depth.Blend);
            Assert.IsTrue(depth.AlongDepth);
            Assert.AreSame(textureA, depth.Texture.Texture);
            Assert.AreEqual(0.5f, depth.ColorRange.y, 1e-5f);
            Assert.AreEqual(2f, layer.EmissionAmount, 1e-5f);
            Assert.IsTrue(depth.AlphaEnabled);
            Assert.AreEqual(0.25f, depth.AlphaValues.x, 1e-5f);
        }

        [Test]
        public void AudioVolumeColorNeedsAudioLink()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableVolumeColor", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_ALVolumeColorLow", new Color(0.5f, 0f, 0f, 1f));
            Assert.IsEmpty(ParseSaved().Layers);

            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALVolumeColorDirection", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALVolumeColorBlendType", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALHighEmission", 3f);
            var layer = ParseSaved().Layers.OfType<AtlasPatternLayer>().Single();
            var volume = layer.AudioVolumeColor;
            Assert.IsTrue(volume.AlongV);
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            float red = Mathf.GammaToLinearSpace(0.5f);
            Assert.AreEqual(red * red, volume.Low.r, 1e-4f, "Poiyomi multiplies the colour by itself without a theme.");
            Assert.AreEqual(3f, volume.HighEmission, 1e-5f);
        }

        [Test]
        public void StatsOverlayIsReported()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_TextEnabled", 1f);
            Assert.IsTrue(ParseSaved().Issues.Any(i => i.Feature == "poiyomi.stats-overlay"));
        }

        [Test]
        public void VideoEffectsAreAScreenLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_VideoEffectsEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VideoType", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_VideoBacklight", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_VideoEmissionEnabled", 0f);
            AtlasTestMaterials.AddSavedColor(material, "_VideoResolution", new Color(64f, 32f, 0f, 0f));
            AtlasTestMaterials.AddSavedTexture(material, "_VideoPixelTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_VideoPixelateToResolution", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_VideoMaskTexture", textureB);
            var layer = ParseSaved().Layers.OfType<AtlasPatternLayer>().Single();
            var screen = layer.VideoScreen;
            Assert.AreEqual(AtlasPatternKind.VideoScreen, layer.Kind);
            Assert.AreEqual(AtlasScreenType.CRT, screen.Type);
            Assert.AreEqual(5f, screen.Backlight, 1e-5f);
            Assert.IsFalse(screen.Emission);
            Assert.IsTrue(screen.Pixelate);
            Assert.AreEqual(new Vector2(64f, 32f), screen.PixelTexture.Tiling, "Once per screen pixel.");
            Assert.AreSame(textureB, layer.Mask.Map.Texture);
        }

        [Test]
        public void AudioSpectrumNeedsAudioLinkAndSharesTheScreen()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableALDecal", 1f);
            Assert.IsEmpty(ParseSaved().Layers);

            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VideoEffectsEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalUseVideoEffects", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalUVMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalRotationSpeed", 15f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalVolumeStep", 8f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalMirrorBand", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalBlendType", 8f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalBlendAlpha", 0.75f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalDebug", 1f);
            var d = ParseSaved();
            var layers = d.Layers.OfType<AtlasPatternLayer>().ToArray();
            Assert.AreEqual(2, layers.Length);
            var layer = layers[1];
            var spectrum = layer.AudioSpectrum;
            Assert.AreEqual(AtlasPatternKind.AudioSpectrum, layer.Kind);
            Assert.IsTrue(spectrum.Circle);
            Assert.AreEqual(30f, spectrum.Placement.Spin, 1e-5f);
            Assert.IsTrue(spectrum.Placement.ClipOutside);
            Assert.AreEqual(8f, spectrum.VolumeSteps, 1e-5f);
            Assert.AreEqual(AtlasSpectrumMirror.Mirror, spectrum.MirrorBands);
            Assert.AreSame(layers[0].VideoScreen, spectrum.Screen);
            Assert.AreEqual(AtlasBlend.Add, layer.Blend);
            Assert.AreEqual(0.75f, layer.Opacity, 1e-5f);
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "spectrum.debug"));
        }

        [Test]
        public void VertexBasicsBecomeDeformationsInPoiyomiOrder()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_VertexManipulationsEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexBasicsMaskChannel", 5f);
            AtlasTestMaterials.AddSavedColor(material, "_VertexManipulationLocalScale", new Color(2f, 1f, 1f, 1f));
            AtlasTestMaterials.AddSavedColor(material, "_VertexManipulationLocalRotation", new Color(0f, 90f, 0f, 1f));
            AtlasTestMaterials.AddSavedColor(material, "_VertexManipulationLocalRotationSpeed", new Color(0f, 20f, 0f, 1f));
            AtlasTestMaterials.AddSavedColor(material, "_VertexManipulationLocalTranslation", new Color(0f, 0.1f, 0f, 2f));
            AtlasTestMaterials.AddSavedFloat(material, "_VertexBarrelMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexBarrelAlpha", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexWindEnabled", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_VertexManipulationWorldTranslation", new Color(0f, 0f, 1f, 1f));
            AtlasTestMaterials.AddSavedFloat(material, "_VertexManipulationHeight", 0.05f);
            AtlasTestMaterials.AddSavedTexture(material, "_VertexManipulationHeightMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexRoundingEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexRoundingDivision", 0.05f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexRoundingSpace", 1f);
            var deformations = ParseSaved().Deformations;
            CollectionAssert.AreEqual(new[]
            {
                AtlasDeformationKind.Scale, AtlasDeformationKind.RotateAxes, AtlasDeformationKind.Translate, AtlasDeformationKind.Barrel,
                AtlasDeformationKind.Wind, AtlasDeformationKind.TranslateWorld, AtlasDeformationKind.Displace, AtlasDeformationKind.Snap
            }, deformations.Select(x => x.Kind).ToArray());
            Assert.AreEqual(2f, deformations[0].Amount.x, 1e-5f);
            Assert.IsTrue(deformations[0].Mask.VertexColor, "Vertex colour green.");
            Assert.AreEqual(AtlasChannel.Green, deformations[0].Mask.Channel);
            Assert.AreEqual(90f, deformations[1].Amount.y, 1e-5f);
            Assert.AreEqual(1f, deformations[1].Spin.y, 1e-5f, "Degrees per 20 seconds.");
            Assert.AreEqual(0.2f, deformations[2].Amount.y, 1e-5f, "× w.");
            Assert.AreEqual(0.5f, deformations[3].Strength, 1e-5f);
            Assert.IsTrue(deformations[4].Wind.Enabled);
            Assert.AreEqual(1f, deformations[5].Amount.z, 1e-5f);
            Assert.AreSame(textureA, deformations[6].Map.Texture);
            Assert.AreEqual(0.05f, deformations[6].Amount.x, 1e-5f);
            Assert.AreEqual(20f, deformations[7].Precision, 1e-3f);
            Assert.IsTrue(deformations[7].Local);
            Assert.IsTrue(deformations[7].Centred);
        }

        [Test]
        public void VertexAudioLinkAddsToTheDeformations()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexManipulationsEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexAudioLinkEnabled", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_VertexLocalTranslationALMax", new Color(0f, 0.1f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_VertexManipulationHeightAL", new Color(0f, 0.02f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_VertexSpectrumMotion", 1f);
            var d = ParseSaved();
            CollectionAssert.AreEqual(new[] { AtlasDeformationKind.Translate, AtlasDeformationKind.Displace }, d.Deformations.Select(x => x.Kind).ToArray());
            CollectionAssert.AreEqual(new[] { "deformations[0].amount.y", "deformations[1].amount.x" }, d.Modulators.Select(x => x.Target).ToArray());
            Assert.AreEqual(0.1f, d.Modulators[0].Max, 1e-5f);
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "audiolink.vertex-spectrum"));
        }

        [Test]
        public void LookAtIsAChainOfRigs()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexLookAtEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_LookAtMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_LookAtGreenMaskEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LookAtGreenTargetUp", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_LookAtGreenPivotOffset", new Color(0f, 1.5f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_LookAtGreenMaxYawAngle", new Color(-45f, 45f, -180f, 180f));
            AtlasTestMaterials.AddSavedFloat(material, "_LookAtVisibilityEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LookAtVisibilityVRCCameraScreenshot", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexLookAtAudioLinkEnabled", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_VertexLookAtAlphaAudiolink", new Color(-1f, 0f, 0f, 0f));
            var d = ParseSaved();
            var lookAt = d.Deformations.Single();
            Assert.AreEqual(AtlasDeformationKind.LookAt, lookAt.Kind);
            Assert.AreEqual(2, lookAt.Rigs.Count, "Red is on by default.");
            Assert.AreEqual(AtlasChannel.Red, lookAt.Rigs[0].Mask.Channel);
            var green = lookAt.Rigs[1];
            Assert.AreEqual(AtlasChannel.Green, green.Mask.Channel);
            Assert.AreEqual(AtlasLookAtUp.WorldUp, green.TargetUp);
            Assert.AreEqual(1.5f, green.Pivot.y, 1e-5f);
            Assert.AreEqual(45f, green.Yaw.y, 1e-5f);
            Assert.IsFalse(lookAt.Visibility.Screenshot);
            Assert.IsTrue(lookAt.Visibility.Normal);
            Assert.AreEqual("deformations[0].strength", d.Modulators.Single().Target);
        }

        [Test]
        public void GlitchDepthBulgeAndViewClipPreventionFollowInOrder()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchingEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchStrength", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchThreshold", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchMirrorEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchMirror", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchingAudioLinkEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchingAudioLinkBand", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDepthBulge", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthBulgeHeight", -0.05f);
            AtlasTestMaterials.AddSavedFloat(material, "_UzumoreCategoryToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_UzumoreAmount", 0.2f);
            var deformations = ParseSaved().Deformations;
            CollectionAssert.AreEqual(new[] { AtlasDeformationKind.Glitch, AtlasDeformationKind.DepthBulge, AtlasDeformationKind.CameraPush },
                deformations.Select(x => x.Kind).ToArray());
            var glitch = deformations[0];
            Assert.AreEqual(3f, glitch.Amount.x, 1e-5f);
            Assert.AreEqual(0.4f, glitch.Threshold, 1e-5f);
            Assert.AreEqual(AtlasMirrorVisibility.NotInMirror, glitch.Mirror);
            Assert.AreEqual(2, glitch.AudioBand);
            Assert.IsTrue(glitch.AudioOverride);
            Assert.IsNull(glitch.Map, "Random amounts.");
            Assert.AreEqual(-0.05f, deformations[1].Amount.x, 1e-5f);
            Assert.AreEqual(0.2f, deformations[2].Amount.x, 1e-5f);
        }

        [Test]
        public void GlitchWithAnEmptyMapIsStill()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchingEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexGlitchUseMap", 1f);
            Assert.IsEmpty(ParseSaved().Deformations);
        }

        [Test]
        public void LilFurFillsTheFur()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_FurLayerNum", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurRenderingMode", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_FurVector", new Color(0f, 0.5f, 1f, 0.05f));
            AtlasTestMaterials.AddSavedTexture(material, "_FurMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_FurLengthChannel", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurMaskStrengthG", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurAlphaChannel", 4f);
            AtlasTestMaterials.AddSavedTexture(material, "_FurNoiseMask", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_FurWindEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurWindPrimaryAmplitude", 0.4f);
            AtlasTestMaterials.AddSavedColor(material, "_FurRimColor", Color.white);
            AtlasTestMaterials.AddSavedFloat(material, "_FurCull", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurStencilRef", 3f);
            var fur = ParseSaved().Fur;
            Assert.IsTrue(fur.Enabled);
            Assert.IsTrue(fur.Transparent);
            Assert.AreEqual(3, fur.LayerCount);
            Assert.AreEqual(0.05f, fur.Length, 1e-5f, "The vector's w.");
            Assert.AreEqual(0.5f, fur.Direction.y, 1e-5f);
            Assert.AreEqual(AtlasChannel.Green, fur.LengthMask.Channel);
            Assert.AreEqual(0.5f, fur.LengthMask.Strength, 1e-5f);
            Assert.IsNull(fur.AlphaMask, "Off.");
            Assert.AreSame(textureB, fur.StrandNoise.Texture);
            Assert.IsTrue(fur.Wind.Enabled);
            Assert.AreEqual(0.4f, fur.Wind.PrimaryAmplitude, 1e-5f);
            Assert.AreEqual(Color.white, fur.RimColor);
            Assert.AreEqual(AtlasCull.Back, fur.Pass.Cull);
            Assert.IsTrue(fur.Pass.Stencil.Enabled);
        }

        [Test]
        public void ToonWithoutFurPropertiesHasNoFur()
        {
            Assert.IsFalse(ParseSaved().Fur.Enabled);
        }

        [TestCase("_MainBlackWhiteStrength", null, typeof(AtlasPoiyomiToon11Parser))]
        [TestCase("_MainBlackWhiteStrength", "_ConstellationEnable", typeof(AtlasPoiyomiPro11Parser))]
        [TestCase(null, "_ConstellationEnable", typeof(AtlasPoiyomiPro10Parser))]
        public void ElevenAndProAreDetectedFromSavedValues(string elevenMarker, string proMarker, System.Type parser)
        {
            AddToon10Markers();
            if (elevenMarker != null) AtlasTestMaterials.AddSavedFloat(material, elevenMarker);
            if (proMarker != null) AtlasTestMaterials.AddSavedFloat(material, proMarker);
            using (new AtlasTestMaterials.SavedValuesOnly())
            {
                var detected = AtlasShaderRegistry.Detect(material);
                Assert.IsInstanceOf(parser, detected);
                var features = detected.Parse(material).Issues.Select(i => i.Feature).ToArray();
                // Every section is mapped or reported on its own; no catch-all reports.
                Assert.IsFalse(features.Contains("poiyomi.v11-sections"));
                Assert.IsFalse(features.Contains("poiyomi.pro-sections"));
            }
        }

        [Test]
        public void MaskLevelsReshapeAMask()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_BackFaceEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_BackFaceMask", textureA);
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceMaskMLInputMin", new Color(0.5f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceMaskMLMidpoint", new Color(0.25f, 0.5f, 0.5f, 0.5f));
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceMaskMLInvert", new Color(1f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceMaskMLNormalize", new Color(1f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceMaskMLBoundsMin", new Color(0.2f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_BackFaceMaskMLBoundsMax", new Color(0.6f, 1f, 1f, 1f));
            var mask = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single().Mask;
            Assert.AreEqual(0.4f, mask.Levels.InputMin, 1e-5f, "Normalised into the bounds.");
            Assert.AreEqual(0.6f, mask.Levels.InputMax, 1e-5f);
            Assert.AreEqual(0.25f, mask.Levels.Midpoint, 1e-5f);
            Assert.IsTrue(mask.Levels.Invert, "Without a vertex colour source the levels invert.");
            Assert.IsFalse(mask.Invert);
            Assert.AreEqual(1f, mask.Evaluate(0.3f), 1e-5f, "Below the input range, inverted.");
        }

        [Test]
        public void VertexColourMaskSources()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_EmissionMaskVertexMask", new Color(3f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_EmissionMaskInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled1", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DecalTexture1", textureA);
            AtlasTestMaterials.AddSavedTexture(material, "_DecalMask", textureB);
            AtlasTestMaterials.AddSavedColor(material, "_DecalMaskVertexMask", new Color(1f, 4f, 0f, 0f));
            AtlasTestMaterials.AddSavedColor(material, "_DecalMaskMLInvert", new Color(0f, 1f, 0f, 0f));
            var layers = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().ToArray();
            var decal = layers.Single(l => l.Placement == AtlasPlacement.Surface).Mask;
            Assert.AreSame(textureB, decal.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, decal.Channel, "Decal 1 reads green.");
            Assert.IsTrue(decal.TimesVertexColor);
            Assert.AreEqual(AtlasChannel.Alpha, decal.VertexChannel, "The decal mask's source is per channel.");
            Assert.IsTrue(decal.Invert, "With a vertex colour source the invert comes after it.");
            var emission = layers.Single(l => l.Placement == AtlasPlacement.Emission).Mask;
            Assert.IsTrue(emission.VertexColor, "No texture: the vertex colour alone.");
            Assert.AreEqual(AtlasChannel.Blue, emission.Channel);
            Assert.IsTrue(emission.Invert, "The emission mask's own invert.");
        }

        [Test]
        public void PbrLightingTypeIsCalibratedPhysicallyBased()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", 9f);
            AtlasTestMaterials.AddSavedFloat(material, "_MochieBRDF", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_PoiPBRMaskMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRMetallic", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRSmoothness", 0.8f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRReflectance", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRMultiBounceAO", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRClearCoat", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRAnisotropy", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRAnisotropyRotation", 30f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRSheen", 0.6f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRSheenChannel", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRGlints", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiPBRGlintDensity", 500f);
            var d = ParseSaved();
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, d.Lighting.Family);
            Assert.AreEqual(AtlasPhysicalModel.Calibrated, d.Lighting.PhysicalModel);
            var response = d.Surface.Response;
            Assert.AreSame(textureA, response.Metallic.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, response.Metallic.Channel);
            Assert.AreEqual(AtlasChannel.Alpha, response.Roughness.Channel, "Smoothness in alpha.");
            Assert.AreEqual(0.2f, response.Roughness.Evaluate(1f), 1e-5f);
            Assert.AreEqual(AtlasChannel.Green, response.Occlusion.Channel);
            Assert.AreEqual(0.3f, response.Reflectance.Constant, 1e-5f);
            Assert.IsTrue(response.MultiBounceOcclusion);
            Assert.AreEqual(0.5f, response.ClearCoat.Strength.Constant, 1e-5f);
            Assert.AreEqual(0.1f, response.ClearCoat.Roughness.Constant, 1e-5f);
            Assert.IsTrue(response.Anisotropy.MapMultiplies);
            Assert.IsNull(response.Anisotropy.Map, "White.");
            Assert.AreEqual(30f, response.Anisotropy.Rotation, 1e-5f);
            Assert.AreEqual(0.6f, response.Sheen.Strength.Constant, 1e-5f);
            Assert.AreEqual(AtlasChannel.Blue, response.Sheen.Strength.Channel);
            Assert.AreEqual(500f, response.Glints.Density, 1e-5f);
            Assert.IsTrue(response.Highlights.Enabled, "The BRDF section doesn't apply.");
        }

        [Test]
        public void ElevenLightingAdditions()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingCapMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingAdditiveLimitMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_BackfaceForceShadow", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingLightVolumesSpecularMaxLights", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMapMode", 4f);
            AtlasTestMaterials.AddSavedTexture(material, "_LightDataSDFMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_LightDataSDFChannelsBA", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableEnvironmentalRim", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimEnviroExposureOcclusion", 0.3f);
            var d = ParseSaved();
            Assert.IsTrue(d.Lighting.LimitKeepsHue);
            Assert.IsTrue(d.Lighting.AdditionalLights.LimitKeepsHue);
            Assert.AreEqual(0.5f, d.Lighting.Ramp.BackFaceShadow, 1e-5f);
            Assert.AreEqual(4, d.Lighting.LightVolumes.SpecularMaxLights);
            Assert.IsTrue(d.Lighting.Ramp.Sdf.MaskChannels);
            Assert.AreEqual(0.3f, d.Layers.OfType<AtlasAngleLayer>().Single().ExposureOcclusion, 1e-5f);
        }

        [Test]
        public void PackedMapMaskOptionsAreReported()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MochieBRDF", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_MochieMetallicMaps", textureA);
            AtlasTestMaterials.AddSavedColor(material, "_MochieMetallicMapsVertexMask", new Color(1f, 0f, 0f, 0f));
            Assert.IsTrue(ParseSaved().Issues.Any(i => i.Feature == "mask.levels-packed"));
        }

        [Test]
        public void BlackAndWhiteComesFirst()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainBlackWhiteToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainBlackWhiteStrength", 0.8f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainBlackWhiteContrast", 1.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainBlackWhiteSelect", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Saturation", 0.5f);
            var adjustments = ParseSaved().ColorAdjustments;
            Assert.AreEqual(AtlasColorOperation.BlackWhite, adjustments[0].Operation);
            Assert.AreEqual(0.8f, adjustments[0].Amount, 1e-5f);
            Assert.AreEqual(1.5f, adjustments[0].BlackWhite.Contrast, 1e-5f);
            Assert.AreEqual(Mathf.GammaToLinearSpace(0.5f), adjustments[0].BlackWhite.Typical, 1e-5f, "The texture tone is an sRGB value.");
            Assert.IsTrue(adjustments[0].BlackWhite.Select);
            Assert.AreEqual(AtlasColorOperation.Saturation, adjustments[1].Operation);
        }

        [Test]
        public void DecalMsdfSpriteSheetAndMirrorFlip()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DecalTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0MSDF", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0FlipInMirror", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DecalSmoothBorder", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0SpriteSheetEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0SpriteSheetColumns", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0SpriteSheetRows", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_Decal0SpriteSheetFPS", 6f);
            AtlasTestMaterials.AddSavedColor(material, "_Decal0SpriteSheetArea", new Color(0.5f, 1f, 0f, 0f));
            var map = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().Single().Source.Map;
            Assert.AreEqual(AtlasTextureInterpretation.Msdf, map.Interpretation);
            Assert.IsTrue(map.Decal.FlipInMirror);
            Assert.IsTrue(map.Decal.SmoothBorder);
            Assert.AreEqual(AtlasFrameSource.SpriteSheet, map.Frames.Source);
            Assert.AreEqual(4, map.Frames.Columns);
            Assert.AreEqual(6f, map.Frames.Rate, 1e-5f);
            Assert.AreEqual(0.5f, map.Frames.Area.x, 1e-5f);
        }

        [Test]
        public void ColorMaskRegionsGainAlphaAndOcclusion()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_RGBMaskEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RGBMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBAGreenEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBAGreenAffectsAlpha", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBAGreenAOStrength", 0.7f);
            AtlasTestMaterials.AddSavedTexture(material, "_RGBAAOMaps", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_RGBAGreenAOMaskChannel", 3f);
            var layers = ParseSaved().Layers.OfType<AtlasSurfaceLayer>().ToArray();
            Assert.AreEqual(3, layers.Length, "Colour, alpha, occlusion.");
            Assert.AreEqual(AtlasSurfaceTarget.Alpha, layers[1].Targets[0].Target);
            Assert.AreEqual(AtlasChannel.Alpha, layers[1].Targets[0].Channel);
            var occlusion = layers[2];
            Assert.AreSame(textureB, occlusion.Source.Map.Texture);
            Assert.AreEqual(AtlasSurfaceTarget.Occlusion, occlusion.Targets[0].Target);
            Assert.AreEqual(AtlasBlend.Darken, occlusion.Targets[0].Blend);
            Assert.AreEqual(0.7f, occlusion.Targets[0].Opacity, 1e-5f);
            Assert.AreEqual(AtlasChannel.Green, occlusion.Targets[0].Channel, "The region's AO channel.");
            Assert.AreEqual(AtlasChannel.Alpha, occlusion.Mask.Channel, "Its own mask channel.");
        }

        [Test]
        public void VertexColourHueDitherModeAndDitheredShadows()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainVertexColoringEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainVertexColoring", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainVertexColorHueShiftEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainVertexColorHueShift", 0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainVertexColorHueShiftSpeed", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaDithering", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaDitherMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaDitheredShadows", 1f);
            var d = ParseSaved();
            var vertex = d.Surface.VertexColor;
            Assert.IsTrue(vertex.HueEnabled);
            Assert.AreEqual(0.25f, vertex.HueShift, 1e-5f);
            Assert.AreEqual(0.1f, vertex.HueSpeed, 1e-5f, "Turns per 20 seconds.");
            Assert.IsTrue(d.Surface.Alpha.DitherCutout);
            Assert.IsTrue(d.Render.DitheredShadows);
        }

        [TestCase(0, AtlasPlacement.Overlay, AtlasLightResponse.ScaleByLight)]
        [TestCase(1, AtlasPlacement.Surface, AtlasLightResponse.IgnoreLight)]
        [TestCase(2, AtlasPlacement.Overlay, AtlasLightResponse.ScaleByDirectLight)]
        public void LilToonRimApplicationStage(int stage, AtlasPlacement placement, AtlasLightResponse response)
        {
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(2f, 2f));
            material.SetFloat("_Mode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimStyle", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimApplicationStage", stage);
            AtlasTestMaterials.AddSavedFloat(material, "_RimAAStrength", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimApplyTransparency", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_RimColorTex", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_RimUseMainTexUV", 1f);
            var rim = ParseSaved().Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(placement, rim.Placement);
            Assert.AreEqual(response, rim.LightResponse);
            Assert.AreEqual(0.3f, rim.AntiAliasing, 1e-5f);
            Assert.IsTrue(rim.TimesAlpha, "Fade mode.");
            Assert.AreEqual(new Vector2(2f, 2f), rim.Color.Map.Tiling, "Through the main texture's tiling.");
        }

        [Test]
        public void SecondBacklight()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_BacklightEnabled__1", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_BacklightBorder__1", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_BacklightApplyToOutlines__1", 0f);
            var backlight = ParseSaved().Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleSource.Backlight, backlight.Source);
            Assert.AreEqual(0.375f, backlight.Low, 1e-5f, "Border − blur / 2.");
            Assert.IsFalse(backlight.OnOutlines);
        }

        [Test]
        public void ContactsWriteTheMaskBus()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactsEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactEnable_2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactTarget_2", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactRadius_2", 0.1f);
            AtlasTestMaterials.AddSavedColor(material, "_GlobalMaskContactTag_2", new Color(3f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactPeople_2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactPosition_2", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactThemeColor_2", 1f);
            var d = ParseSaved();
            var step = d.MaskBus.Steps.Single();
            Assert.AreEqual(AtlasMaskBusStepKind.Contact, step.Kind);
            Assert.AreEqual(4, step.Channel, "2R.");
            Assert.AreEqual(0.1f, step.Contact.InnerRadius, 1e-5f);
            Assert.AreEqual(3, step.Contact.Tags[0]);
            Assert.AreEqual(AtlasContactPeople.Others, step.Contact.People);
            Assert.IsFalse(step.Contact.AroundTouch, "Whole mesh.");
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "contact.person-color"));
        }

        [Test]
        public void GlobalMaskTexturesTakeMaskLevels()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskTexturesEnable", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_GlobalMaskTexture0", textureA);
            AtlasTestMaterials.AddSavedColor(material, "_GlobalMaskTexture0MLOutputMax", new Color(1f, 0.5f, 1f, 1f));
            var steps = ParseSaved().MaskBus.Steps;
            Assert.IsNull(steps[0].Mask.Levels, "Red unchanged.");
            Assert.AreEqual(0.5f, steps[1].Mask.Levels.OutputMax, 1e-5f, "Green's levels.");
            Assert.AreSame(textureA, steps[1].Mask.Map.Texture);
        }

        [Test]
        public void OutlineMaskChannelsPerProperty()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_OutlineMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineMaskChannel", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineColorMaskChannel", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineHighlightMaskChannel", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineGlobalMask", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineEdgeFade", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineThinFade", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineCameraDepth", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineZClip", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineOffsetUnits", -2f);
            var outline = ParseSaved().Outlines.Single();
            Assert.IsNull(outline.WidthMask, "The size mask is off.");
            Assert.AreSame(textureA, outline.ColorMask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, outline.ColorMask.Channel);
            Assert.AreEqual(AtlasChannel.Blue, outline.Highlight.Mask.Channel);
            Assert.IsNull(outline.AlphaMask);
            Assert.AreEqual(2, outline.Mask.Bus, "1B.");
            Assert.AreEqual(0.4f, outline.EdgeFade, 1e-5f);
            Assert.IsTrue(outline.SmoothThin);
            Assert.IsFalse(outline.WritesCameraDepth);
            Assert.IsFalse(outline.Pass.DepthClip);
            Assert.AreEqual(-2f, outline.Pass.DepthOffsetUnits, 1e-5f);
        }

        [Test]
        public void LightFacingOutlineSideWidths()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineExpansionMode", 2f);
            AtlasTestMaterials.AddSavedColor(material, "_OutlineRimFalloff", new Color(0.2f, 0.8f, 0f, 1f));
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineRimSizeFollowsLight", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineRimDarkWidth", 2f);
            var outline = ParseSaved().Outlines.Single();
            Assert.AreEqual(AtlasOutlineShape.LightFacing, outline.Shape);
            Assert.AreEqual(0.2f, outline.LightFalloff.x, 1e-5f);
            Assert.AreEqual(0.5f, outline.SizeFollowsLight, 1e-5f);
            Assert.AreEqual(0.02f, outline.DarkSideWidth, 1e-6f, "In hundredths, like the width.");
        }

        [Test]
        public void OutlineColorAdjustAndEmissionColour()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineHueShift", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineHue", 0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineHueOffsetSpeed", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineSaturation", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineGamma", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineEmissionCustomColor", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_OutlineEmissionColor", Color.red);
            var d = ParseSaved();
            var outline = d.Outlines.Single();
            CollectionAssert.AreEqual(new[] { AtlasColorOperation.Hue, AtlasColorOperation.Saturation, AtlasColorOperation.Clamp, AtlasColorOperation.Power },
                outline.ColorAdjustments.Select(a => a.Operation).ToArray());
            Assert.AreEqual(0.25f, outline.ColorAdjustments[0].Amount, 1e-5f);
            Assert.AreEqual(1.5f, outline.ColorAdjustments[1].Amount, 1e-5f);
            var turning = d.Modulators.Single();
            Assert.AreEqual("outlines[0].colorAdjustments[0].amount", turning.Target);
            Assert.AreEqual(0.2f, turning.Time.Frequency, 1e-5f, "Turns per 20 seconds.");
            Assert.AreEqual(Color.red, outline.EmissionColor.Color);
            Assert.IsFalse(d.Issues.Any(i => i.Feature == "outline.color-adjust"));
        }

        [Test]
        public void ElevenSmallSurfaceAdditions()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_NormalCorrect", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_NormalCorrectVertexColorMask", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ScreenSpaceUVObjectScale", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ScreenSpaceUVWorldSize", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_PostProcess", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPContrast", 1.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPIncludeEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPMaskGlobalMask", 2f);
            var d = ParseSaved();
            Assert.AreEqual(AtlasChannel.Green, d.Surface.Normal.CorrectMask.Channel);
            Assert.IsTrue(d.Surface.Normal.CorrectMask.VertexColor);
            Assert.IsTrue(d.ScreenSpace.ObjectRelative);
            Assert.AreEqual(0.5f, d.ScreenSpace.WorldSize, 1e-5f);
            var contrast = d.ColorAdjustments.Single();
            Assert.IsTrue(contrast.IncludeEmission);
            Assert.AreEqual(1, contrast.Mask.Bus, "1G.");
        }

        [Test]
        public void ElevenSmallLayerAdditions()
        {
            var array = new Texture2DArray(4, 4, 2, TextureFormat.RGBA32, false);
            try
            {
                AtlasTestMaterials.AddSavedFloat(material, "_EnableFlipbook", 1f);
                AtlasTestMaterials.AddSavedTexture(material, "_FlipbookTexArray", array);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookDepth", 0.3f);
                AtlasTestMaterials.AddSavedFloat(material, "_FlipbookFlipInMirror", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_EnableEmission", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_EmissionApplyTransparency", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_AlphaPremultiply", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_GlitterEnable", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_GlitterAngleOffset", 30f);
                AtlasTestMaterials.AddSavedFloat(material, "_EnableDissolve", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_DissolveHueShiftEnabled", 1f);
                AtlasTestMaterials.AddSavedFloat(material, "_DissolveGamma", 2f);
                var d = ParseSaved();
                var flipbook = d.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Source.Map?.Frames != null);
                Assert.AreEqual(0.3f, flipbook.Source.Map.Decal.Depth, 1e-5f);
                Assert.IsTrue(flipbook.Source.Map.Decal.FlipInMirror);
                Assert.IsTrue(d.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Placement == AtlasPlacement.Emission).EmissionTimesAlpha);
                Assert.AreEqual(30f, d.Layers.OfType<AtlasPatternLayer>().Single().Glitter.AngleOffset, 1e-5f);
                var transition = d.Layers.OfType<AtlasTransitionLayer>().Single();
                Assert.IsTrue(d.ColorAdjustments.Any(a => a.Layer == transition && a.Operation == AtlasColorOperation.Power && a.Amount == 2f));
            }
            finally { Object.DestroyImmediate(array); }
        }

        [Test]
        public void ElevenPathingMotionAndMasks()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnablePathing", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_PathingMap", textureA);
            AtlasTestMaterials.AddSavedTexture(material, "_PathingMaskMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_PathingMaskChannelG", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathingMaskChannelR", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathDirectionChannelG", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathMotionG", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathEasingG", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathEdgesG", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_PathHeadSoftness", new Color(0.1f, 0.3f, 0.1f, 0.1f));
            AtlasTestMaterials.AddSavedFloat(material, "_PathOverlapMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathAntialiasing", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PathingUseColorMapAlpha", 0f);
            var path = ParseSaved().Layers.OfType<AtlasPatternLayer>().Single().Path;
            Assert.AreEqual(AtlasPathOverlap.Brightest, path.Overlap);
            Assert.IsTrue(path.Antialiasing);
            Assert.IsFalse(path.ColorMapAlphaMasks);
            Assert.IsNull(path.Channels[0].PackedMask, "None.");
            var green = path.Channels[1];
            Assert.AreSame(textureB, green.PackedMask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, green.PackedMask.Channel);
            Assert.AreEqual(2, green.GradientChannel, "The map's blue.");
            Assert.AreEqual(-1, path.Channels[0].GradientChannel, "Its own.");
            Assert.IsTrue(green.PingPong);
            Assert.AreEqual(AtlasPathEasing.InOut, green.Easing);
            Assert.IsTrue(green.SeparateEdges);
            Assert.AreEqual(0.3f, green.HeadSoftness, 1e-5f);
        }

        [Test]
        public void ElevenVoronoiAndInternalParallax()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiCellMotion", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiHueShiftEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_VoronoiHueShift", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_AudioLinkVoronoiCellsEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_AudioLinkVoronoiCellsBand", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiInternalParallax", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_ParallaxInternalMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalEmissionStrength", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalRefractionEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalRefractionIOR", 1.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalFogEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalSmooth", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxInternalAlpha", 0.4f);
            var d = ParseSaved();
            var layers = d.Layers.OfType<AtlasPatternLayer>().ToArray();
            var voronoi = layers.Single(l => l.Kind == AtlasPatternKind.Voronoi);
            Assert.AreEqual(0.5f, voronoi.Voronoi.CellMotion, 1e-5f);
            Assert.AreEqual(1, voronoi.Voronoi.AudioCells.Band);
            var hue = d.ColorAdjustments.Single(a => a.Layer == voronoi);
            Assert.AreEqual(0.2f, hue.Amount, 1e-5f);
            var parallax = layers.Single(l => l.Kind == AtlasPatternKind.InternalParallax);
            Assert.AreEqual(3f, parallax.EmissionAmount, 1e-5f);
            Assert.IsTrue(parallax.InternalParallax.Refraction);
            Assert.AreEqual(1.5f, parallax.InternalParallax.RefractionIor, 1e-5f);
            Assert.IsTrue(parallax.InternalParallax.Fog);
            Assert.IsTrue(parallax.InternalParallax.Smooth);
            Assert.AreEqual(0.4f, parallax.InternalParallax.AlphaAdd, 1e-5f);
        }

        [Test]
        public void ElevenAudioSpectrum()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableALDecal", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalFlip", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalUVMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalRadialMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalArcDegrees", 180f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalDisplayMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalSamplingMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalFrequencyMax", 8000f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalResponse", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalColorMode", 2f);
            AtlasTestMaterials.AddSavedTexture(material, "_ALDecalGradient", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalMasterOpacity", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_ALDecalPreview", 3f);
            var d = ParseSaved();
            var spectrum = d.Layers.OfType<AtlasPatternLayer>().Single().AudioSpectrum;
            Assert.IsFalse(spectrum.FlipU);
            Assert.IsTrue(spectrum.FlipV);
            Assert.IsTrue(spectrum.Circle);
            Assert.IsTrue(spectrum.Arc);
            Assert.AreEqual(180f, spectrum.ArcDegrees, 1e-5f);
            Assert.AreEqual(AtlasSpectrumDisplay.SmoothLine, spectrum.Display);
            Assert.IsTrue(spectrum.FrequencyMapping);
            Assert.AreEqual(8000f, spectrum.FrequencyMax, 1e-5f);
            Assert.AreEqual(AtlasSpectrumResponse.Responsive, spectrum.Response);
            Assert.AreEqual(AtlasSpectrumColorMode.Gradient, spectrum.ColorMode);
            Assert.AreSame(textureA, spectrum.Gradient.Texture);
            Assert.AreEqual(0.5f, spectrum.MasterOpacity, 1e-5f);
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "spectrum.debug"), "A static preview level.");
        }

        [Test]
        public void ProScreenShadowsAndToneMapping()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_SSAOEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_SSAOIntensity", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_SSAOApplyGlobalMaskIndex", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContactShadowsEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContactShadowsApplyMode", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContactShadowsLightSource", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ContactShadowsCustomLightSpace", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GT7ToneMapping", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GT7TM_ColorAccuracy", 0.8f);
            AtlasTestMaterials.AddSavedFloat(material, "_NeutralEmission", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_NeutralEmissionHueCorrection", 0.5f);
            var d = ParseSaved();
            var layers = d.Layers.OfType<AtlasPatternLayer>().ToArray();
            var ao = layers.Single(l => l.Kind == AtlasPatternKind.AmbientOcclusion);
            Assert.AreEqual(2f, ao.AmbientOcclusion.Intensity, 1e-5f);
            Assert.AreEqual(AtlasBlend.Multiply, ao.Blend);
            Assert.AreSame(ao, d.MaskBus.Steps.Single().Layer, "Written into 1C.");
            var contact = layers.Single(l => l.Kind == AtlasPatternKind.ContactShadow).ContactShadow;
            Assert.IsTrue(contact.ApplyToColor);
            Assert.IsTrue(contact.ApplyToLightMap);
            Assert.IsTrue(contact.CustomDirection);
            Assert.AreEqual(AtlasDirectionSpace.Object, contact.DirectionSpace);
            var gt7 = d.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.ToneMapGt7);
            Assert.AreEqual(AtlasAdjustmentTarget.FinalColor, gt7.Target);
            Assert.AreEqual(0.8f, gt7.Settings.x, 1e-5f);
            var neutral = d.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.NeutralEmission);
            Assert.AreEqual(AtlasAdjustmentTarget.Emission, neutral.Target);
            Assert.AreEqual(0.5f, neutral.Settings.y, 1e-5f);
        }

        [Test]
        public void ProTpsSquishAndOutlineBake()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_TPSPenetratorEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_TPS_PenetratorLength", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TPS_Channel", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableSquish", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_SquishSource", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_SquishStrength", 0.5f);
            AtlasTestMaterials.AddSavedTexture(material, "_SquishGradient", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_SquishApplyGlobalMaskIndex", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_OutlineCurvatureData", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineCurvatureTipStyle", 1f);
            var d = ParseSaved();
            CollectionAssert.AreEqual(new[] { AtlasDeformationKind.Tps, AtlasDeformationKind.Squish }, d.Deformations.Select(x => x.Kind).ToArray());
            Assert.AreEqual(0.2f, d.Deformations[0].Tps.Length, 1e-5f);
            Assert.AreEqual(3, d.Deformations[0].Tps.Channel);
            var squish = d.Deformations[1].Squish;
            Assert.IsTrue(squish.Orifice);
            Assert.AreEqual(0.5f, squish.Strength, 1e-5f);
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreSame(squish, layer.Squish, "The deformation and its colour share the settings.");
            Assert.AreSame(layer, d.MaskBus.Steps.Single().Layer);
            var curvature = d.Outlines.Single().Curvature;
            Assert.AreSame(textureB, curvature.Data.Texture);
            Assert.AreEqual(AtlasOutlineTipStyle.Taper, curvature.TipStyle);
        }

        [Test]
        public void TpsAnimationToggleOffIsOff()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_TPSPenetratorEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_TPS_AnimatedToggle", 0f);
            Assert.IsEmpty(ParseSaved().Deformations);
        }

        [Test]
        public void ProConstellationWithAudioLink()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationBlendMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationDensity", 20f);
            AtlasTestMaterials.AddSavedColor(material, "_ConstellationOffset", new Color(0.1f, 0.2f, 0.3f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationRandomColors", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationApplyGlobalMaskIndex", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationAudioLinkEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationALThemeColor", 2f);
            AtlasTestMaterials.AddSavedColor(material, "_ConstellationALSizeMod", new Color(0f, 0.5f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationALHueShiftSpeed", 0.2f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreEqual(AtlasPatternKind.Constellation, layer.Kind);
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            Assert.AreEqual(3f, layer.EmissionAmount, 1e-5f);
            var c = layer.Constellation;
            Assert.AreEqual(20f, c.Density, 1e-5f);
            Assert.AreEqual(0.3f, c.Phase, 1e-5f);
            Assert.AreEqual(new Vector2(0.1f, 0.2f), c.Offset);
            Assert.IsTrue(c.RandomColors);
            Assert.AreEqual(2, c.AudioThemeColor);
            Assert.AreSame(layer, d.MaskBus.Steps.Single().Layer);
            string path = "layers[" + d.Layers.IndexOf(layer) + "]";
            CollectionAssert.AreEquivalent(new[] { path + ".constellation.starSize", path + ".constellation.hueOffset" },
                d.Modulators.Select(x => x.Target).ToArray());
        }

        [Test]
        public void ConstellationWithoutAudioLinkIgnoresItsAudioSettings()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationRandomColors", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationAudioLinkEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationALThemeColor", 2f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreEqual(AtlasBlend.Add, layer.Blend, "Add by default.");
            Assert.AreEqual(0, layer.Constellation.AudioThemeColor);
            Assert.IsEmpty(d.Modulators);
        }

        [Test]
        public void TwoPassReadsTheSecondPassAndItsMasks()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_ModeTwoPass", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_SrcBlend2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DstBlend2", 10f);
            AtlasTestMaterials.AddSavedFloat(material, "_Cull2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ZWrite2", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_Stencil2Ref", 7f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassAlphaCutoff", 0.2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassAlphaPower", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassOverrideFirstPassColor", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassAlphaOverride", 0.4f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassFirstPassGlobalMaskIndex", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassSecondPassGlobalMaskIndex", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TwoPassSecondPassApplyGlobalMaskBlendType", 7f);
            var d = ParseSaved();
            var second = d.Render.SecondPass;
            Assert.IsTrue(second.Enabled);
            Assert.AreEqual(AtlasSurfaceMode.Transparent, second.SurfaceMode);
            Assert.AreEqual(AtlasCull.Front, second.Pass.Cull);
            Assert.IsFalse(second.Pass.DepthWrite);
            Assert.AreEqual(7, second.Pass.Stencil.Reference);
            Assert.IsTrue(second.Pass.Stencil.Enabled);
            Assert.AreEqual(0.2f, second.Cutoff, 1e-5f);
            Assert.AreEqual(2f, second.AlphaPower, 1e-5f);
            Assert.AreEqual(AtlasPassColor.AlphaOnly, second.ColorSource);
            Assert.AreEqual(0.4f, second.Alpha, 1e-5f);
            Assert.AreEqual(AtlasCull.Back, d.Render.Pass.Cull, "The first pass keeps its own settings.");
            var steps = d.MaskBus.Steps.Where(x => x.Kind == AtlasMaskBusStepKind.SecondPass).ToArray();
            Assert.AreEqual(2, steps.Length);
            Assert.AreEqual(0, steps[0].Channel);
            Assert.IsFalse(steps[0].Inside);
            Assert.AreEqual(1, steps[1].Channel);
            Assert.IsTrue(steps[1].Inside);
            Assert.AreEqual(AtlasMaskBlend.Add, steps[1].Blend);
        }

        [Test]
        public void WithoutTwoPassThereIsNoSecondPass()
        {
            var d = ParseSaved();
            Assert.IsFalse(d.Render.SecondPass.Enabled);
            Assert.IsFalse(d.MaskBus.Steps.Any(x => x.Kind == AtlasMaskBusStepKind.SecondPass));
        }

        [Test]
        public void ProFurReadsShells()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_FurLayerCount", 20f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurLength", 0.01f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurGravityStrength", -0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurTipTaper", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurContact", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_GlobalMaskContactsEnable", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_FurTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_FurTextureApplyToSkin", 0f);
            AtlasTestMaterials.AddSavedTexture(material, "_FurHeightMap", textureB);
            AtlasTestMaterials.AddSavedColor(material, "_FurHeightMapMinMax", new Color(0.2f, 0.8f, 0f, 1f));
            AtlasTestMaterials.AddSavedFloat(material, "_FurUseVertexColorNormals", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurHueShiftEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurHueShift", 0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurHueShiftSpeed", 1f);
            var d = ParseSaved();
            var fur = d.Fur;
            Assert.IsTrue(fur.Enabled);
            Assert.AreEqual(AtlasFurMethod.Shells, fur.Method);
            Assert.AreEqual(20, fur.LayerCount);
            Assert.AreEqual(0.01f, fur.Length, 1e-6f);
            Assert.AreEqual(-0.25f, fur.Gravity, 1e-6f);
            Assert.AreEqual(0.3f, fur.TipTaper, 1e-6f);
            Assert.AreEqual(2, fur.ContactIndex);
            Assert.AreSame(textureA, fur.Color.Map.Texture);
            Assert.IsFalse(fur.ColorAppliesToSurface);
            Assert.AreSame(textureB, fur.StrandNoise.Texture);
            Assert.AreEqual(new Vector2(0.2f, 0.8f), fur.StrandNoiseRange);
            Assert.AreEqual(0.5f, fur.RootOcclusion, 1e-6f, "Pro's default.");
            Assert.AreEqual(AtlasFurVertexDirection.Signed, fur.VertexDirection);
            Assert.AreEqual(5f, fur.LevelOfDetailStart, 1e-6f);
            Assert.AreEqual(0.25f, fur.ColorAdjustments.Single().Amount, 1e-6f);
            Assert.AreEqual("fur.colorAdjustments[0].amount", d.Modulators.Single().Target);
        }

        [Test]
        public void LilFurElevenAdditions()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_FurLayerNum", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_VertexColor2FurVector", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurContact", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurContactPush", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurCutoutLength", 0.8f);
            AtlasTestMaterials.AddSavedFloat(material, "_FurLengthChannel", 2f);
            AtlasTestMaterials.AddSavedColor(material, "_FurMaskVertexMask", new Color(0f, 0f, 4f, 0f));
            var d = ParseSaved();
            var fur = d.Fur;
            Assert.AreEqual(AtlasFurMethod.Strands, fur.Method);
            Assert.AreEqual(AtlasFurVertexDirection.Unsigned, fur.VertexDirection);
            Assert.AreEqual(0, fur.ContactIndex, "Without the global mask contacts there is no contact.");
            Assert.AreEqual(2f, fur.ContactPush, 1e-6f);
            Assert.AreEqual(0.8f, fur.CutoutLength, 1e-6f);
            // No mask texture: the blue channel's vertex colour source (alpha) alone.
            Assert.IsTrue(fur.LengthMask.VertexColor);
            Assert.AreEqual(AtlasChannel.Alpha, fur.LengthMask.Channel);
        }

        [Test]
        public void GrabPassReadsItsEffects()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableGrabpass", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RefractionIndex", 1.5f);
            AtlasTestMaterials.AddSavedTexture(material, "_GrabPassBlendMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableBlur", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabBlurPattern", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabBlurMaskChannel", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabBlurMaskInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabPixelationEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabPixelSize", 16f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabRampEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabRampPreset", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabSrcBlend", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabDstBlend", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabAlphaGlobalMask", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabPassHueShiftToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabPassHueShift", 0.3f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasRefractionLayer>().Single();
            Assert.AreEqual(AtlasPlacement.Overlay, layer.Placement);
            Assert.IsTrue(layer.Refracts);
            Assert.AreEqual(1.5f, layer.IOR, 1e-5f);
            Assert.IsTrue(layer.ByAlpha);
            Assert.IsFalse(layer.SelfGrab);
            Assert.AreEqual(AtlasBlendFactor.One, layer.SourceBlend);
            Assert.AreEqual(AtlasBlendFactor.Zero, layer.DestinationBlend);
            Assert.AreEqual(AtlasBlurPattern.Hexagon, layer.Blur.Pattern);
            Assert.AreSame(textureA, layer.Blur.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, layer.Blur.Mask.Channel);
            Assert.IsTrue(layer.Blur.Mask.Invert);
            Assert.AreEqual(16f, layer.Pixelation.Size, 1e-5f);
            Assert.AreEqual(AtlasScreenRampPalette.Sepia, layer.Ramp.Palette);
            Assert.AreEqual(AtlasChannel.Red, layer.AlphaMask.Channel, "The alpha mask reads red by default.");
            Assert.AreEqual(2, layer.AlphaMask.Bus);
            Assert.IsNull(layer.ColorMask);
            Assert.AreSame(layer, d.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.Hue).Layer);
        }

        [Test]
        public void SelfGrabBlendsWithTheFirstPass()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableGrabpass", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ModeTwoPass", 0f);
            var d = ParseSaved();
            var layer = d.Layers.OfType<AtlasRefractionLayer>().Single();
            Assert.IsTrue(layer.SelfGrab);
            Assert.IsFalse(layer.ByAlpha);
            Assert.IsTrue(d.Render.SecondPass.Enabled);
        }

        [Test]
        public void GrabPassInvertedChannelWithoutAMapIsOff()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableGrabpass", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabAlphaMaskInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabRefractionMaskChannel", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_GrabRefractionMaskInvert", 1f);
            var layer = ParseSaved().Layers.OfType<AtlasRefractionLayer>().Single();
            Assert.AreEqual(0f, layer.Amount, 1e-5f);
            Assert.IsFalse(layer.Refracts);
        }

        [Test]
        public void LavaLampIsAPatternLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_InternalWaterEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LavaShape", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_LavaLampAlpha", 0.7f);
            AtlasTestMaterials.AddSavedFloat(material, "_BindDataMode", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_LavaLampMask", textureA);
            AtlasTestMaterials.AddSavedColor(material, "_LavaFlowDirection0", new Color(1f, 0f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_LavaTopLightBrightness0", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_LavaAudioLinkEnable", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_LavaAudioLinkBand", 2f);
            var d = ParseSaved();
            Assert.IsFalse(d.Issues.Any(i => i.Feature == "poiyomi.lava-lamp"));
            Assert.IsEmpty(d.Layers.OfType<AtlasRefractionLayer>(), "Toon materials have no grab pass.");
            var layer = d.Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreEqual(AtlasPatternKind.LavaLamp, layer.Kind);
            Assert.AreEqual(AtlasPlacement.Overlay, layer.Placement);
            Assert.AreEqual(0.7f, layer.Opacity, 1e-6f);
            Assert.AreSame(textureA, layer.Mask.Map.Texture);
            var lamp = layer.LavaLamp;
            Assert.AreEqual(AtlasLavaShape.Hearts, lamp.Shape);
            Assert.AreEqual(AtlasBindData.On, lamp.BindData);
            Assert.AreEqual(Vector3.right, lamp.FlowDirection);
            Assert.AreEqual(2f, lamp.TopEmission, 1e-6f);
            Assert.AreEqual(-1, lamp.AudioBand, "AudioLink's section is off.");
        }

        [Test]
        public void LavaLampOffAddsNothing()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_InternalWaterEnable", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_LavaShape", 3f);
            Assert.IsEmpty(ParseSaved().Layers);
        }

        [Test]
        public void TripleOutlineReadsEachLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlineLayer1", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlineLayer2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlineLayer3", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_Outline2Width", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_Outline2ExpansionMode", 4f);
            AtlasTestMaterials.AddSavedColor(material, "_Outline2DropShadowOffset", new Color(0f, -1f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_Outline2Cull", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_Outline2StencilRef", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_OutlineSrcBlend", 5f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableAudioLink", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_AudioLinkOutline2Size", new Color(0f, 2f, 0f, 0f));
            var d = ParseSaved();
            Assert.AreEqual(2, d.Outlines.Count, "Layer 3 is off.");
            Assert.AreEqual(0.01f, d.Outlines[0].Width, 1e-6f);
            var second = d.Outlines[1];
            Assert.AreEqual(0.04f, second.Width, 1e-6f);
            Assert.AreEqual(AtlasOutlineShape.DropShadow, second.Shape);
            Assert.AreEqual(new Vector3(0f, -1f, 0f), second.Direction);
            Assert.AreEqual(AtlasCull.Back, second.Pass.Cull);
            Assert.AreEqual(AtlasCull.Front, d.Outlines[0].Pass.Cull);
            Assert.AreEqual(3, second.Pass.Stencil.Reference);
            Assert.AreEqual(AtlasBlendFactor.SourceAlpha, second.SourceBlend, "The blend factors are shared.");
            Assert.AreEqual(AtlasBlendFactor.SourceAlpha, d.Outlines[0].SourceBlend);
            var width = d.Modulators.Single(x => x.Target == "outlines[1].width");
            Assert.AreEqual(0.02f, width.Max, 1e-6f);
        }

        [Test]
        public void TripleOutlineWithoutLayerOne()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlines", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlineLayer1", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableOutlineLayer2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Outline2Width", 3f);
            var outline = ParseSaved().Outlines.Single();
            Assert.AreEqual(0.03f, outline.Width, 1e-6f);
        }

        [Test]
        public void TriplanarProjectionIsReadAndDropsTheConstellation()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarBlendType", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarScaleWithObject", 0.5f);
            AtlasTestMaterials.AddSavedTexture(material, "_TriplanarMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarMaskChannel", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarMaskInvert", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ConstellationEnable", 1f);
            var d = ParseSaved();
            var triplanar = d.Triplanar;
            Assert.IsTrue(triplanar.Enabled);
            Assert.AreEqual(AtlasTriplanarSource.Mesh, triplanar.Source);
            Assert.AreEqual(AtlasTriplanarBlend.Asymmetric, triplanar.Blend);
            Assert.AreEqual(0.5f, triplanar.ScaleWithObject, 1e-6f);
            Assert.AreSame(textureA, triplanar.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Blue, triplanar.Mask.Channel);
            Assert.IsTrue(triplanar.Mask.Levels.Invert);
            Assert.IsFalse(triplanar.Mask.Invert);
            Assert.IsEmpty(d.Layers.OfType<AtlasPatternLayer>(), "The triplanar variant draws no constellation.");
        }

        [Test]
        public void TriplanarInvertedWithoutAMaskHidesEverything()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarMode", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_TriplanarMaskInvert", 1f);
            var triplanar = ParseSaved().Triplanar;
            Assert.AreEqual(AtlasTriplanarSource.Depth, triplanar.Source);
            Assert.AreEqual(0f, triplanar.MaskStrength, 1e-6f);
        }

        [Test]
        public void WithoutTheVariantThereIsNoTriplanar()
        {
            Assert.IsFalse(ParseSaved().Triplanar.Enabled);
        }

        [Test]
        public void TessellationIsRead()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_TessellationType", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_TessellationUniform", 4f);
            AtlasTestMaterials.AddSavedColor(material, "_TessellationDistance", new Color(1f, 5f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_TessellationPhong", 0.5f);
            var tessellation = ParseSaved().Tessellation;
            Assert.IsTrue(tessellation.Enabled);
            Assert.AreEqual(AtlasTessellationMode.Distance, tessellation.Mode);
            Assert.AreEqual(4f, tessellation.Factor, 1e-6f);
            Assert.AreEqual(new Vector2(1f, 5f), tessellation.Distance);
            Assert.AreEqual(0.5f, tessellation.Phong, 1e-6f);
        }

        [Test]
        public void GeometryEffectsAreReportedOnce()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_GeoDissolveAlpha", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_ShatterWaveEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParticleDensity", 0.15f);
            var d = ParseSaved();
            Assert.IsFalse(d.Tessellation.Enabled);
            foreach (var feature in new[] { "geometry.dissolve", "geometry.shatter-wave", "geometry.particles" })
                Assert.AreEqual(1, d.Issues.Count(i => i.Feature == feature), feature);
        }

        [Test]
        public void ShatterWaveOffIsNotReported()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_ShatterWaveEnabled", 0f);
            Assert.IsFalse(ParseSaved().Issues.Any(i => i.Feature.StartsWith("geometry.")));
        }

        [Test]
        public void SkinToneIsAPatternLayer()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_PoiSkinLUT", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinMelanin", 0.6f);
            AtlasTestMaterials.AddSavedTexture(material, "_PoiSkinDetailMap", textureB);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinFrecklesEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinFreckleMapUV", 1f);
            AtlasTestMaterials.AddSavedColor(material, "_PoiSkinFreckleMapTiling", new Color(4f, 4f, 0f, 0f));
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinBlendMode", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinBlend", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinBrightness", 2f);
            var layer = ParseSaved().Layers.OfType<AtlasPatternLayer>().Single();
            Assert.AreEqual(AtlasPatternKind.SkinTone, layer.Kind);
            Assert.AreEqual(AtlasBlend.Multiply, layer.Blend);
            Assert.AreEqual(0.5f, layer.Opacity, 1e-6f);
            Assert.AreEqual(2f, layer.Color.r, 1e-5f);
            var skin = layer.SkinTone;
            Assert.AreSame(textureA, skin.Lut.Texture);
            Assert.AreEqual(0.6f, skin.Melanin, 1e-6f);
            Assert.AreEqual(AtlasChannel.Green, skin.FreckleMask.Channel);
            Assert.AreEqual(AtlasTextureSpace.UV1, skin.FreckleMask.Map.Space);
            Assert.AreEqual(new Vector2(4f, 4f), skin.FreckleMask.Map.Tiling);
            Assert.IsNull(skin.TanMask, "The tan is off.");
            // Applies where the red channel is dark.
            Assert.AreEqual(AtlasChannel.Red, layer.Mask.Channel);
            Assert.IsTrue(layer.Mask.Levels.Invert);
        }

        [Test]
        public void SkinFrecklesWithoutAMapAreOff()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PoiSkinFrecklesEnabled", 1f);
            var layer = ParseSaved().Layers.OfType<AtlasPatternLayer>().Single();
            Assert.IsNull(layer.SkinTone.FreckleMask);
            Assert.IsNull(layer.Mask);
        }

        [Test]
        public void AdditionalLightsCombineByMaxByDefault()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_AddBlendOp", 4f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaBoostFA", 10f);
            var additional = ParseSaved().Lighting.AdditionalLights;
            Assert.AreEqual(AtlasLightCombine.Max, additional.Combine);
            Assert.AreEqual(10f, additional.MaxAlphaBoost, 1e-6f);
        }

        [TestCase(0, 1, 1, false)]
        [TestCase(0, 5, 1, true)]
        [TestCase(1, 1, 1, false)]
        public void AdditionalLightsAddedWithOtherFactorsAreReported(int op, int source, int destination, bool reported)
        {
            AtlasTestMaterials.AddSavedFloat(material, "_AddBlendOp", op);
            AtlasTestMaterials.AddSavedFloat(material, "_AddSrcBlend", source);
            AtlasTestMaterials.AddSavedFloat(material, "_AddDstBlend", destination);
            var d = ParseSaved();
            Assert.AreEqual(AtlasLightCombine.Add, d.Lighting.AdditionalLights.Combine);
            Assert.AreEqual(reported, d.Issues.Any(i => i.Feature == "lighting.add-pass-blend"));
            Assert.AreEqual(op == 1, d.Issues.Any(i => i.Feature == "render.blend-op"));
        }

        [Test]
        public void ElevenGlobalsAreRead()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_PoiTimeSource", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RenderingEarlyZEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPLightingMultiplier", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPLightingAddition", 0.1f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPEmissionMultiplier", 3f);
            AtlasTestMaterials.AddSavedFloat(material, "_PPFinalColorMultiplier", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_ForceFlatRampedLightmap", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_StochasticDeliotHeitzDensity", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainTexStochastic", 1f);
            material.SetTexture("_MainTex", textureA);
            var d = ParseSaved();
            Assert.AreEqual(AtlasClock.Network, d.Clock);
            Assert.IsTrue(d.Render.DepthPrepass);
            Assert.AreEqual(2f, d.Lighting.LightMultiplier, 1e-6f);
            Assert.AreEqual(0.1f, d.Lighting.LightAddition, 1e-6f);
            Assert.IsTrue(d.Lighting.FlatLightMap);
            Assert.AreEqual(3f, d.ColorAdjustments.Single(a => a.Target == AtlasAdjustmentTarget.Emission).Amount, 1e-6f);
            Assert.AreEqual(0.5f, d.ColorAdjustments.Single(a => a.Target == AtlasAdjustmentTarget.FinalColor).Amount, 1e-6f);
            Assert.AreEqual(2f, d.Surface.BaseColor.Map.StochasticDensity, 1e-6f);
        }

        [Test]
        public void ShadingOffIsUnlit()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_ShadingEnabled", 0f);
            var d = ParseSaved();
            Assert.AreEqual(AtlasLightingFamily.Unlit, d.Lighting.Family);
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "lighting.shading-off"));
        }

        [Test]
        public void ToonNineIsDetectedFromSavedValues()
        {
            foreach (string name in new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_AlphaMaskInvert" })
                AtlasTestMaterials.AddSavedFloat(material, name);
            AtlasTestMaterials.AddSavedColor(material, "_LightingShadowColor");
            using (new AtlasTestMaterials.SavedValuesOnly())
            {
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.IsInstanceOf<AtlasPoiyomiToon9Parser>(parser);
                // An untouched material: nothing in use, so nothing noted.
                Assert.IsFalse(parser.Parse(material).Issues.Any(i => i.Feature.StartsWith("poiyomi9.")));
            }
        }

        [Test]
        public void ToonNineDepthRimHasTwoOrTenSamples()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDepthRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DepthRimType", 1f);
            var d = ParseSaved9();
            var layer = d.Layers.OfType<AtlasAngleLayer>().First(l => l.DepthEdge != null);
            Assert.AreEqual(10, layer.DepthEdge.Samples);
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "poiyomi9.depth-rim"));
            Assert.AreEqual(4, ParseSaved().Layers.OfType<AtlasAngleLayer>().First(l => l.DepthEdge != null).DepthEdge.Samples, "10: four samples.");
        }

        [Test]
        public void ToonNineSubsurfaceReadsTheAlpha()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_SubsurfaceScattering", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_SSSThicknessMapChannel", 1f);
            var layer9 = ParseSaved9().Layers.OfType<AtlasSubsurfaceLayer>().Single();
            Assert.AreEqual(AtlasChannel.Alpha, layer9.ThicknessChannel);
            Assert.AreEqual(AtlasChannel.Red, ParseSaved().Layers.OfType<AtlasSubsurfaceLayer>().Single().ThicknessChannel);
        }

        [Test]
        public void ToonNineRewrittenFeaturesAreNotedOnce()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled2", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableDissolve", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRim2Lighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_Rim2Style", 1f);
            var d = ParseSaved9();
            Assert.AreEqual(1, d.Issues.Count(i => i.Feature == "poiyomi9.decals"));
            Assert.AreEqual(1, d.Issues.Count(i => i.Feature == "poiyomi9.dissolve"));
            Assert.AreEqual(1, d.Issues.Count(i => i.Feature == "poiyomi9.rim"));
            Assert.IsTrue(d.Issues.Where(i => i.Feature.StartsWith("poiyomi9.")).All(i => i.Kind == AtlasIssueKind.Note));
            Assert.IsFalse(ParseSaved().Issues.Any(i => i.Feature.StartsWith("poiyomi9.")), "10 has no such notes.");
        }

        [Test]
        public void ToonEightIsDetectedFromSavedValues()
        {
            foreach (string name in new[] { "_ShaderOptimizerEnabled", "_LightingMode", "_ALDecalBlendType" })
                AtlasTestMaterials.AddSavedFloat(material, name);
            AtlasTestMaterials.AddSavedColor(material, "_LightingShadowColor");
            using (new AtlasTestMaterials.SavedValuesOnly())
            {
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.IsInstanceOf<AtlasPoiyomiToon8Parser>(parser);
                Assert.IsFalse(parser.Parse(material).Issues.Any(i => i.Feature.StartsWith("poiyomi8.")), "An untouched material.");
            }
        }

        [Test]
        public void ToonEightPoiyomiRimBlendIsTheOldProperty()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimWidth", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimBlendMode", 1f);
            Assert.AreEqual(AtlasBlend.Replace, ParseSaved8().Layers.OfType<AtlasAngleLayer>().Single().Blend, "8: Add 0, Replace 1.");
            Assert.AreEqual(AtlasBlend.Add, ParseSaved().Layers.OfType<AtlasAngleLayer>().Single().Blend, "10 reads _RimPoiBlendMode.");
        }

        [Test]
        public void ToonEightLilRimAdds()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableRimLighting", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimStyle", 2f);
            AtlasTestMaterials.AddSavedFloat(material, "_RimBlendMode", 0f);
            Assert.IsTrue(ParseSaved8().Layers.OfType<AtlasAngleLayer>().All(l => l.Blend == AtlasBlend.Add));
            Assert.IsTrue(ParseSaved().Layers.OfType<AtlasAngleLayer>().All(l => l.Blend == AtlasBlend.Replace));
        }

        [Test]
        public void ToonEightAlphaMapIsTheClippingMask()
        {
            AtlasTestMaterials.AddSavedTexture(material, "_ClippingMask", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_Inverse_Clipping", 1f);
            var alpha = ParseSaved8().Surface.Alpha;
            Assert.AreSame(textureA, alpha.Mask.Map.Texture);
            Assert.IsTrue(alpha.Mask.Invert);
            Assert.AreEqual(AtlasAlphaMaskBlend.Multiply, alpha.MaskBlend);
            Assert.IsNull(ParseSaved().Surface.Alpha.Mask, "10 has no clipping mask.");
        }

        [Test]
        public void ToonEightOldBlendModesAreApproximatedAndReported()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_DecalEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_DecalTexture", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_DecalBlendType", 7f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaGlobalMask", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_AlphaGlobalMaskBlendType", 8f);
            var d = ParseSaved8();
            Assert.AreEqual(AtlasBlend.Screen, d.Layers.OfType<AtlasSurfaceLayer>().Single().Targets[0].Blend, "8.0's colour dodge.");
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "poiyomi8.blend-modes"));
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "poiyomi8.mask-blend"));
            Assert.IsTrue(d.Issues.Any(i => i.Feature == "poiyomi8.decals"));
            var ten = ParseSaved();
            Assert.AreEqual(AtlasBlend.Subtract, ten.Layers.OfType<AtlasSurfaceLayer>().Single().Targets[0].Blend);
            Assert.IsFalse(ten.Issues.Any(i => i.Feature.StartsWith("poiyomi8.")));
        }

        [Test]
        public void ToonEightIridescenceIsReported()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_EnableIridescence", 1f);
            Assert.IsTrue(ParseSaved8().Issues.Any(i => i.Feature == "poiyomi8.iridescence"));
        }

        [Test]
        public void ToonSevenIsDetectedFromSavedValues()
        {
            foreach (string name in new[] { "_ShaderOptimizerEnabled", "_LightingMode" })
                AtlasTestMaterials.AddSavedFloat(material, name);
            AtlasTestMaterials.AddSavedColor(material, "_LightingShadowColor");
            using (new AtlasTestMaterials.SavedValuesOnly())
            {
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.IsInstanceOf<AtlasPoiyomiToon7Parser>(parser);
                var issues = parser.Parse(material).Issues.Where(i => i.Feature.StartsWith("poiyomi7.")).ToArray();
                // An untouched material: only 7.3's lighting is noted.
                CollectionAssert.AreEqual(new[] { "poiyomi7.lighting" }, issues.Select(i => i.Feature).ToArray());
            }
        }

        [Test]
        public void ToonSevenReadsItsOwnNames()
        {
            material.SetTexture("_MainTex", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MainTextureUV", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ForceOpaque", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_DitheringEnabled", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainMaxAlpha", 0.5f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainDistanceFadeMax", 2f);
            var d = ParseSaved7();
            Assert.AreEqual(AtlasTextureSpace.UV1, d.Surface.BaseColor.Map.Space);
            Assert.AreNotEqual(AtlasAlphaSource.None, d.Surface.Alpha.Source, "Not forced opaque.");
            Assert.IsTrue(d.Surface.Alpha.Dithering);
            var fade = d.Surface.Alpha.Modifiers.Single(x => x.Kind == AtlasAlphaModifierKind.DistanceFade);
            Assert.AreEqual(0.5f, fade.MaxAlpha, 1e-6f);
            Assert.AreEqual(2f, fade.MaxDistance, 1e-6f);
            var ten = ParseSaved();
            Assert.AreEqual(AtlasTextureSpace.UV0, ten.Surface.BaseColor.Map.Space);
            Assert.AreEqual(AtlasAlphaSource.None, ten.Surface.Alpha.Source, "10's force opaque defaults on.");
        }

        [Test]
        public void ToonSevenUvFourIsTheDistortedUv()
        {
            material.SetTexture("_MainTex", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_MainTextureUV", 4f);
            Assert.AreEqual(AtlasTextureSpace.UV0, ParseSaved7().Surface.BaseColor.Map.Space, "Distorted UV without distortion: UV0.");
        }

        [TestCase(4, 0, AtlasLightingFamily.Flat)]
        [TestCase(1, 0, AtlasLightingFamily.PhysicallyBased)]
        public void ToonSevenLightingTypesAreRenumbered(int mode, int ramp, AtlasLightingFamily family)
        {
            AtlasTestMaterials.AddSavedFloat(material, "_LightingMode", mode);
            AtlasTestMaterials.AddSavedFloat(material, "_LightingRampType", ramp);
            Assert.AreEqual(family, ParseSaved7().Lighting.Family);
        }

        [Test]
        public void ToonSevenParallaxAndOldSections()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxMap", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxHeightMapEnabled", 1f);
            AtlasTestMaterials.AddSavedTexture(material, "_ParallaxHeightMap", textureA);
            AtlasTestMaterials.AddSavedFloat(material, "_ParallaxStrength", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableSpecular", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_EnableSpecular1", 1f);
            var d = ParseSaved7();
            var parallax = d.Surface.Parallax;
            Assert.IsTrue(parallax.Enabled);
            Assert.AreSame(textureA, parallax.Height.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, parallax.Height.Channel);
            Assert.AreEqual(0.3f, parallax.Depth, 1e-6f);
            Assert.AreEqual(1, d.Issues.Count(i => i.Feature == "poiyomi7.specular"));
        }

        [Test]
        public void MainBrightnessAddsBeforeNine()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainColorAdjustToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainBrightness", 0.2f);
            var eight = ParseSaved8().ColorAdjustments.Single(a => a.Target == AtlasAdjustmentTarget.BaseColor && a.Operation != AtlasColorOperation.Clamp);
            Assert.AreEqual(AtlasColorOperation.Lightness, eight.Operation);
            Assert.AreEqual(0.2f, eight.Amount, 1e-6f);
            var nine = ParseSaved9().ColorAdjustments.Single(a => a.Target == AtlasAdjustmentTarget.BaseColor && a.Operation != AtlasColorOperation.Clamp);
            Assert.AreEqual(AtlasColorOperation.Brightness, nine.Operation);
            Assert.AreEqual(1.2f, nine.Amount, 1e-6f);
        }

        [Test]
        public void ToonSevenColorAdjustIsUnderTheHueToggleInHsv()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShiftToggle", 1f);
            AtlasTestMaterials.AddSavedFloat(material, "_MainHueShift", 0.25f);
            AtlasTestMaterials.AddSavedFloat(material, "_Saturation", 0.5f);
            var adjustments = ParseSaved7().ColorAdjustments.Where(a => a.Target == AtlasAdjustmentTarget.BaseColor).ToArray();
            var hue = adjustments.Single(a => a.Operation == AtlasColorOperation.Hue);
            Assert.AreEqual(0.25f, hue.Amount, 1e-6f);
            Assert.AreEqual(AtlasHueMethod.HSV, hue.HueMethod);
            Assert.IsTrue(adjustments.Any(a => a.Operation == AtlasColorOperation.Saturation));
            Assert.IsEmpty(ParseSaved().ColorAdjustments.Where(a => a.Target == AtlasAdjustmentTarget.BaseColor),
                "In 10 the hue toggle alone doesn't switch the section on.");
        }
    }
}
