using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Most tests use a Standard material as the host: it declares the same property names, and its
    // saved values are read as Standard Lite's.
    public sealed class AtlasVRChatStandardLiteTests
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

        private AtlasUnifiedMaterial ParseSaved()
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasVRChatStandardLiteParser().Parse(material);
        }

        [Test]
        public void SlidersMultiplyTheMetallicSmoothnessMap()
        {
            material.SetFloat("_Metallic", 0.5f);
            material.SetFloat("_Glossiness", 0.8f);
            material.SetFloat("_GlossMapScale", 0.1f); // Standard's; Standard Lite has none
            material.SetTexture("_MetallicGlossMap", textureA);
            var response = ParseSaved().Surface.Response;
            Assert.AreEqual(Mathf.GammaToLinearSpace(0.5f), response.Metallic.Constant, 1e-5f);
            Assert.AreSame(textureA, response.Metallic.Map.Texture);
            Assert.AreEqual(AtlasChannel.Red, response.Metallic.Channel);
            Assert.AreEqual(0.8f, response.Roughness.Constant, 1e-5f);
            Assert.AreSame(textureA, response.Roughness.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, response.Roughness.Channel);
            Assert.AreEqual(1f - 0.8f * 0.5f, response.Roughness.Evaluate(0.5f), 1e-5f);
        }

        [Test]
        public void MainMapsUseTheAlbedoTilingAndTheNormalNeedsNoKeyword()
        {
            material.SetTexture("_MainTex", textureA);
            material.SetTextureScale("_MainTex", new Vector2(3f, 2f));
            material.SetTexture("_BumpMap", textureB);
            material.SetTextureScale("_BumpMap", new Vector2(9f, 9f));
            material.SetFloat("_BumpScale", 0.6f);
            material.SetTexture("_OcclusionMap", textureB);
            material.SetFloat("_OcclusionStrength", 0.5f);
            var d = ParseSaved();
            Assert.AreSame(textureB, d.Surface.Normal.Map.Texture);
            Assert.AreEqual(new Vector2(3f, 2f), d.Surface.Normal.Map.Tiling);
            Assert.AreEqual(0.6f, d.Surface.Normal.Strength, 1e-5f);
            Assert.AreEqual(new Vector2(3f, 2f), d.Surface.Response.Occlusion.Map.Tiling);
            Assert.AreEqual(AtlasChannel.Green, d.Surface.Response.Occlusion.Channel);
            Assert.AreEqual(0.75f, d.Surface.Response.Occlusion.Evaluate(0.5f), 1e-5f);
        }

        [Test]
        public void AlwaysOpaqueWithVertexColourUnlessDisabled()
        {
            var d = ParseSaved();
            Assert.IsTrue(d.Surface.VertexColor.MultiplyColor);
            Assert.AreEqual(AtlasAlphaSource.None, d.Surface.Alpha.Source);
            Assert.AreEqual(AtlasSurfaceMode.Opaque, d.Render.SurfaceMode);
            material.EnableKeyword("DISABLE_VERTEX_COLORING");
            Assert.IsFalse(ParseSaved().Surface.VertexColor.MultiplyColor);
        }

        [Test]
        public void LightingIsLambertWithoutShadowsOrAddPass()
        {
            var lighting = ParseSaved().Lighting;
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, lighting.Family);
            Assert.AreEqual(AtlasDiffuseModel.Lambert, lighting.Diffuse);
            Assert.AreEqual(AtlasAmbientModel.Directional, lighting.Ambient);
            Assert.IsFalse(lighting.ReceiveShadows);
            Assert.AreEqual(0f, lighting.Contributions.AdditionalLights);
            Assert.AreEqual(1f, lighting.Contributions.VertexLights);
        }

        [Test]
        public void KeywordsTurnOnEmissionDetailAndAntiAliasing()
        {
            material.SetColor("_EmissionColor", Color.red);
            material.SetTexture("_DetailAlbedoMap", textureA);
            material.SetTexture("_DetailNormalMap", textureB);
            // Properties Standard doesn't declare go straight into the saved data.
            AtlasTestMaterials.AddSavedFloat(material, "_SpecularAAScreenSpaceVariance", 0.3f);
            AtlasTestMaterials.AddSavedFloat(material, "_SpecularAAThreshold", 0.4f);
            var before = ParseSaved();
            Assert.IsEmpty(before.Layers);
            Assert.IsFalse(before.Surface.Response.SpecularAntiAliasing.Enabled);

            material.EnableKeyword("_EMISSION");
            material.EnableKeyword("_DETAIL");
            material.EnableKeyword("_ENABLE_GEOMETRIC_SPECULAR_AA");
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            var d = ParseSaved();
            Assert.AreEqual(3, d.Layers.Count, "Emission, detail colour, detail normal.");
            Assert.AreEqual(AtlasPlacement.Emission, d.Layers[0].Placement);
            Assert.AreEqual(AtlasBlend.MultiplyDouble, ((AtlasSurfaceLayer)d.Layers[1]).Targets[0].Blend);
            Assert.AreEqual(AtlasSurfaceTarget.Normal, ((AtlasSurfaceLayer)d.Layers[2]).Targets[0].Target);
            var antiAliasing = d.Surface.Response.SpecularAntiAliasing;
            Assert.IsTrue(antiAliasing.Enabled);
            Assert.AreEqual(0.3f, antiAliasing.Variance, 1e-5f);
            Assert.AreEqual(0.4f, antiAliasing.Threshold, 1e-5f);
            Assert.IsFalse(d.Surface.Response.Highlights);
            Assert.IsFalse(d.Surface.Response.Reflections);
        }

        [Test]
        public void InstalledShaderIsDetectedAndItsDefaultsMap()
        {
            var shader = Shader.Find("VRChat/Mobile/Standard Lite");
            if (shader == null) Assert.Ignore("VRChat Mobile Standard Lite is not installed.");
            var lite = new Material(shader);
            try
            {
                var parser = AtlasShaderRegistry.Detect(lite);
                Assert.IsInstanceOf<AtlasVRChatStandardLiteParser>(parser);
                var d = parser.Parse(lite);
                Assert.AreEqual(1f, d.Surface.Response.Metallic.Evaluate(), 1e-5f);
                Assert.AreEqual(0f, d.Surface.Response.Roughness.Evaluate(), 1e-5f);
                Assert.AreEqual(1f, d.Surface.Normal.Strength);
                Assert.IsFalse(d.Issues.Exists(i => i.IsWarning));
            }
            finally { Object.DestroyImmediate(lite); }
        }
    }
}
