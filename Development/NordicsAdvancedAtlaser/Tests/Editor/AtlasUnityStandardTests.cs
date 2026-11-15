using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasUnityStandardTests
    {
        private Material material;
        private Texture2D textureA, textureB, textureC;

        [SetUp]
        public void Create()
        {
            material = AtlasTestMaterials.Standard();
            textureA = new Texture2D(4, 4);
            textureB = new Texture2D(4, 4);
            textureC = new Texture2D(4, 4);
        }

        [TearDown]
        public void Destroy()
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(textureA);
            Object.DestroyImmediate(textureB);
            Object.DestroyImmediate(textureC);
        }

        private AtlasUnifiedMaterial Parse() => AtlasShaderRegistry.Detect(material).Parse(material);

        [Test]
        public void UntouchedMaterialMapsToTheDefaults()
        {
            Assert.IsInstanceOf<AtlasUnityStandardParser>(AtlasShaderRegistry.Detect(material));
            var d = Parse();
            Assert.AreEqual(AtlasSurfaceMode.Opaque, d.Render.SurfaceMode);
            Assert.IsTrue(d.Render.Pass.DepthWrite);
            Assert.AreEqual(AtlasCull.Back, d.Render.Pass.Cull);
            Assert.IsNull(d.Surface.BaseColor.Map);
            Assert.AreEqual(0f, Vector4.Distance(Color.white, d.Surface.BaseColor.Color), 1e-5f);
            Assert.AreEqual(AtlasAlphaSource.BaseColorAlpha, d.Surface.Alpha.Source);
            Assert.AreEqual(0f, d.Surface.Response.Metallic.Evaluate(), 1e-5f);
            Assert.AreEqual(0.5f, d.Surface.Response.Roughness.Evaluate(), 1e-5f);
            Assert.IsNull(d.Surface.Response.SpecularColor);
            Assert.AreEqual(1f, d.Surface.Response.Occlusion.Evaluate(0f), 1e-5f);
            Assert.IsTrue(d.Surface.Response.Highlights.Enabled);
            Assert.IsTrue(d.Surface.Response.Reflections.Enabled);
            Assert.IsNull(d.Surface.Normal.Map);
            Assert.IsFalse(d.Surface.Parallax.Enabled);
            Assert.IsEmpty(d.Layers);
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, d.Lighting.Family);
            Assert.AreEqual(AtlasDiffuseModel.Disney, d.Lighting.Diffuse);
            Assert.AreEqual(AtlasAmbientModel.Directional, d.Lighting.Ambient);
            Assert.IsEmpty(d.Issues);
        }

        [Test]
        public void MainMapsUseTheAlbedoTilingAndNeedTheirKeyword()
        {
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(2f, 3f));
            material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0f));
            material.SetTexture("_BumpMap", textureB);
            material.SetTextureScale("_BumpMap", new Vector2(5f, 5f));
            material.SetFloat("_BumpScale", 0.5f);
            material.SetTexture("_OcclusionMap", textureC);
            material.SetFloat("_OcclusionStrength", 0.4f);

            Assert.IsNull(Parse().Surface.Normal.Map, "Without its keyword the shader ignores the normal map.");
            material.EnableKeyword("_NORMALMAP");
            var d = Parse();
            Assert.AreSame(textureB, d.Surface.Normal.Map.Texture);
            Assert.AreEqual(AtlasTextureInterpretation.TangentNormal, d.Surface.Normal.Map.Interpretation);
            Assert.AreEqual(new Vector2(2f, 3f), d.Surface.Normal.Map.Tiling);
            Assert.AreEqual(new Vector2(0.5f, 0f), d.Surface.Normal.Map.Offset);
            Assert.AreEqual(0.5f, d.Surface.Normal.Strength, 1e-5f);
            var occlusion = d.Surface.Response.Occlusion;
            Assert.AreSame(textureC, occlusion.Map.Texture);
            Assert.AreEqual(new Vector2(2f, 3f), occlusion.Map.Tiling);
            Assert.AreEqual(AtlasChannel.Green, occlusion.Channel);
            Assert.AreEqual(0.8f, occlusion.Evaluate(0.5f), 1e-5f);
        }

        [Test]
        public void MetallicSliderIsConvertedToLinear()
        {
            material.SetFloat("_Metallic", 0.5f);
            material.SetFloat("_Glossiness", 0.8f);
            var d = Parse();
            Assert.AreEqual(Mathf.GammaToLinearSpace(0.5f), d.Surface.Response.Metallic.Evaluate(), 1e-5f);
            Assert.AreEqual(0.2f, d.Surface.Response.Roughness.Evaluate(), 1e-5f);
        }

        [Test]
        public void MetallicMapReplacesTheSliderAndCarriesSmoothness()
        {
            // A leftover slider value must not scale the map.
            material.SetFloat("_Metallic", 0.3f);
            material.SetFloat("_Glossiness", 0.9f);
            material.SetFloat("_GlossMapScale", 0.6f);
            material.SetTexture("_MetallicGlossMap", textureA);
            material.EnableKeyword("_METALLICGLOSSMAP");
            var response = Parse().Surface.Response;
            Assert.AreEqual(1f, response.Metallic.Constant);
            Assert.AreSame(textureA, response.Metallic.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, response.Metallic.Channel);
            Assert.AreSame(textureA, response.Roughness.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, response.Roughness.Channel);
            Assert.AreEqual(1f - 0.6f * 0.5f, response.Roughness.Evaluate(0.5f), 1e-5f);
        }

        [Test]
        public void SmoothnessFromAlbedoAlphaLeavesTheColourAlphaAsTransparency()
        {
            material.SetTexture("_MainTex", textureA);
            material.SetFloat("_GlossMapScale", 0.5f);
            material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            var d = Parse();
            Assert.AreEqual(AtlasAlphaSource.BaseColorConstantAlpha, d.Surface.Alpha.Source);
            Assert.AreSame(textureA, d.Surface.Response.Roughness.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, d.Surface.Response.Roughness.Channel);
            Assert.AreEqual(0.5f, d.Surface.Response.Roughness.Constant, 1e-5f);
        }

        [TestCase("_ALPHATEST_ON", AtlasSurfaceMode.Cutout)]
        [TestCase("_ALPHABLEND_ON", AtlasSurfaceMode.Fade)]
        [TestCase("_ALPHAPREMULTIPLY_ON", AtlasSurfaceMode.Transparent)]
        public void RenderingModesAreNamedByLook(string keyword, AtlasSurfaceMode expected)
        {
            material.EnableKeyword(keyword);
            material.SetFloat("_Cutoff", 0.3f);
            material.SetFloat("_ZWrite", 0f);
            var d = Parse();
            Assert.AreEqual(expected, d.Render.SurfaceMode);
            Assert.IsFalse(d.Render.Pass.DepthWrite);
            if (expected == AtlasSurfaceMode.Cutout) Assert.AreEqual(0.3f, d.Render.Cutoff, 1e-5f);
        }

        [Test]
        public void HighlightsAndReflectionsCanBeTurnedOff()
        {
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            var response = Parse().Surface.Response;
            Assert.IsFalse(response.Highlights.Enabled);
            Assert.AreEqual(AtlasReflectionSource.AmbientColor, response.Reflections.Source);
        }

        [Test]
        public void HeightMapIsSingleStepParallax()
        {
            material.SetTexture("_ParallaxMap", textureA);
            material.SetFloat("_Parallax", 0.05f);
            Assert.IsFalse(Parse().Surface.Parallax.Enabled);
            material.EnableKeyword("_PARALLAXMAP");
            var parallax = Parse().Surface.Parallax;
            Assert.IsTrue(parallax.Enabled);
            Assert.AreSame(textureA, parallax.Height.Map.Texture);
            Assert.AreEqual(AtlasChannel.Green, parallax.Height.Channel);
            Assert.AreEqual(0.05f, parallax.Depth, 1e-5f);
            Assert.AreEqual(0.5f, parallax.ReferenceHeight);
            Assert.AreEqual(AtlasParallaxMethod.SingleStep, parallax.Method);
        }

        [Test]
        public void EmissionIsAnEmissionLayerInLinear()
        {
            var colour = new Color(2f, 0.5f, 0.25f, 1f);
            material.SetColor("_EmissionColor", colour);
            material.SetTexture("_EmissionMap", textureA);
            Assert.IsEmpty(Parse().Layers, "Without its keyword the shader shows no emission.");
            material.EnableKeyword("_EMISSION");
            var layer = (AtlasSurfaceLayer)Parse().Layers[0];
            Assert.AreEqual(AtlasPlacement.Emission, layer.Placement);
            Assert.AreEqual(0f, Vector4.Distance(colour.linear, layer.Source.Color), 1e-4f);
            Assert.AreSame(textureA, layer.Source.Map.Texture);
            Assert.AreEqual(1, layer.Targets.Count);
            Assert.AreEqual(AtlasSurfaceTarget.Emission, layer.Targets[0].Target);
            Assert.AreEqual(AtlasBlend.Add, layer.Targets[0].Blend);
        }

        [Test]
        public void DetailIsAColourLayerAndANormalLayerSharingTheMask()
        {
            material.SetTexture("_MainTex", textureC);
            material.SetTextureScale("_MainTex", new Vector2(2f, 2f));
            material.SetTexture("_DetailMask", textureC);
            material.SetTexture("_DetailAlbedoMap", textureA);
            material.SetTextureScale("_DetailAlbedoMap", new Vector2(8f, 8f));
            material.SetTexture("_DetailNormalMap", textureB);
            material.SetFloat("_DetailNormalMapScale", 0.7f);
            material.SetFloat("_UVSec", 1f);
            material.EnableKeyword("_DETAIL_MULX2");
            material.EnableKeyword("_NORMALMAP");
            var layers = Parse().Layers;
            Assert.AreEqual(2, layers.Count);
            var colour = (AtlasSurfaceLayer)layers[0];
            var normal = (AtlasSurfaceLayer)layers[1];
            Assert.AreSame(colour.Mask, normal.Mask);
            Assert.AreSame(textureC, colour.Mask.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, colour.Mask.Channel);
            Assert.AreEqual(new Vector2(2f, 2f), colour.Mask.Map.Tiling);
            Assert.AreSame(textureA, colour.Source.Map.Texture);
            Assert.AreEqual(AtlasTextureSpace.UV1, colour.Source.Map.Space);
            Assert.AreEqual(AtlasBlend.MultiplyDouble, colour.Targets[0].Blend);
            Assert.AreEqual(AtlasSurfaceTarget.BaseColor, colour.Targets[0].Target);
            Assert.AreSame(textureB, normal.Source.Map.Texture);
            Assert.AreEqual(new Vector2(8f, 8f), normal.Source.Map.Tiling);
            Assert.AreEqual(AtlasTextureSpace.UV1, normal.Source.Map.Space);
            Assert.AreEqual(AtlasSurfaceTarget.Normal, normal.Targets[0].Target);
            Assert.AreEqual(0.7f, normal.Targets[0].Opacity, 1e-5f);
        }

        [Test]
        public void SpecularSetupKeepsTheSpecularColourAndAMapReplacesIt()
        {
            var shader = Shader.Find("Standard (Specular setup)");
            if (shader == null) Assert.Ignore("Standard (Specular setup) is not available.");
            Object.DestroyImmediate(material);
            material = new Material(shader);
            var colour = new Color(0.8f, 0.4f, 0.2f, 1f);
            material.SetColor("_SpecColor", colour);
            Assert.IsInstanceOf<AtlasUnityStandardSpecularParser>(AtlasShaderRegistry.Detect(material));
            var specular = Parse().Surface.Response.SpecularColor;
            Assert.AreEqual(0f, Vector4.Distance(colour.linear, specular.Color), 1e-4f);
            Assert.IsNull(specular.Map);

            material.SetTexture("_SpecGlossMap", textureA);
            material.EnableKeyword("_SPECGLOSSMAP");
            var response = Parse().Surface.Response;
            Assert.AreEqual(Color.white, response.SpecularColor.Color);
            Assert.AreSame(textureA, response.SpecularColor.Map.Texture);
            Assert.AreSame(textureA, response.Roughness.Map.Texture);
            Assert.AreEqual(0f, response.Metallic.Evaluate());
        }

        [Test]
        public void SavedValuesReadTheSameAsTheShader()
        {
            material.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 0.5f));
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(2f, 3f));
            material.SetTexture("_MetallicGlossMap", textureB);
            material.SetFloat("_GlossMapScale", 0.7f);
            material.SetTexture("_DetailAlbedoMap", textureC);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.EnableKeyword("_DETAIL_MULX2");
            material.EnableKeyword("_ALPHABLEND_ON");
            var installed = Parse();
            AtlasUnifiedMaterial saved;
            using (new AtlasTestMaterials.SavedValuesOnly()) saved = new AtlasUnityStandardParser().Parse(material);
            Assert.AreEqual(0f, Vector4.Distance(installed.Surface.BaseColor.Color, saved.Surface.BaseColor.Color), 1e-5f);
            Assert.AreEqual(installed.Surface.BaseColor.Map.Tiling, saved.Surface.BaseColor.Map.Tiling);
            Assert.AreSame(installed.Surface.Response.Metallic.Map.Texture, saved.Surface.Response.Metallic.Map.Texture);
            Assert.AreEqual(installed.Surface.Response.Roughness.Constant, saved.Surface.Response.Roughness.Constant, 1e-5f);
            Assert.AreEqual(installed.Render.SurfaceMode, saved.Render.SurfaceMode);
            Assert.AreEqual(installed.Layers.Count, saved.Layers.Count);
        }
    }
}
