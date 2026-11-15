using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The fallback reads common property names. A Standard material is the host; names it doesn't
    // declare go straight into its saved data.
    public sealed class AtlasFallbackTests
    {
        private Material material;
        private Texture2D texture;

        [SetUp]
        public void Create()
        {
            material = AtlasTestMaterials.Standard();
            texture = new Texture2D(4, 4);
        }

        [TearDown]
        public void Destroy()
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(texture);
        }

        private AtlasUnifiedMaterial ParseSaved()
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasFallbackParser().Parse(material);
        }

        [Test]
        public void TheMappingIsReportedAsAGuessOnce()
        {
            var d = ParseSaved();
            Assert.AreEqual(1, d.Issues.FindAll(i => i.IsWarning).Count);
            Assert.AreEqual("material", d.Issues.Find(i => i.IsWarning).Feature);
        }

        [Test]
        public void MetallicTimesMapAndSmoothnessFromTheMapAlpha()
        {
            material.SetFloat("_Metallic", 0.5f);
            material.SetFloat("_Glossiness", 0.8f);
            material.SetTexture("_MetallicGlossMap", texture);
            var response = ParseSaved().Surface.Response;
            Assert.AreEqual(0.5f, response.Metallic.Constant, 1e-5f);
            Assert.AreSame(texture, response.Metallic.Map.Texture);
            Assert.IsNull(response.SpecularColor);
            Assert.IsTrue(response.Roughness.InvertResult);
            Assert.AreEqual(0.8f, response.Roughness.Constant, 1e-5f);
            Assert.AreSame(texture, response.Roughness.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, response.Roughness.Channel);
        }

        [Test]
        public void RoughnessIsReadAsRoughness()
        {
            AtlasTestMaterials.AddSavedFloat(material, "_Roughness", 0.3f);
            var roughness = ParseSaved().Surface.Response.Roughness;
            Assert.IsFalse(roughness.InvertResult);
            Assert.AreEqual(0.3f, roughness.Evaluate(), 1e-5f);
        }

        [Test]
        public void AlphaMaskIsTheAlphaSource()
        {
            AtlasTestMaterials.AddSavedTexture(material, "_AlphaMask", texture);
            var alpha = ParseSaved().Surface.Alpha;
            Assert.AreEqual(AtlasAlphaSource.Mask, alpha.Source);
            Assert.AreSame(texture, alpha.Mask.Map.Texture);
        }

        [Test]
        public void EmissionOnlyWhenSomethingGlows()
        {
            material.SetColor("_EmissionColor", Color.black);
            Assert.IsEmpty(ParseSaved().Layers);
            material.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f));
            var layer = (AtlasSurfaceLayer)ParseSaved().Layers[0];
            Assert.AreEqual(AtlasPlacement.Emission, layer.Placement);
            Assert.AreEqual(Mathf.GammaToLinearSpace(0.5f), layer.Source.Color.g, 1e-4f);
            Assert.AreEqual(AtlasSurfaceTarget.Emission, layer.Targets[0].Target);
        }

        [Test]
        public void MatcapIsReportedNotGuessed()
        {
            AtlasTestMaterials.AddSavedTexture(material, "_Matcap", texture);
            var d = ParseSaved();
            Assert.IsTrue(d.Issues.Exists(i => i.Feature == "matcap" && i.IsWarning));
            Assert.IsEmpty(d.Layers);
        }

        [TestCase(5, 10, false, AtlasSurfaceMode.Fade)]
        [TestCase(1, 10, false, AtlasSurfaceMode.Transparent)]
        [TestCase(1, 0, false, AtlasSurfaceMode.Opaque)]
        [TestCase(1, 0, true, AtlasSurfaceMode.Cutout)]
        [TestCase(1, 1, false, AtlasSurfaceMode.Custom)]
        public void SurfaceModeFromBlendFactors(int source, int destination, bool alphaTest, AtlasSurfaceMode expected)
        {
            material.SetFloat("_SrcBlend", source);
            material.SetFloat("_DstBlend", destination);
            if (alphaTest) material.EnableKeyword("_ALPHATEST_ON");
            var render = ParseSaved().Render;
            Assert.AreEqual(expected, render.SurfaceMode);
            if (expected == AtlasSurfaceMode.Custom)
            {
                Assert.AreEqual(AtlasBlendFactor.One, render.SourceBlend);
                Assert.AreEqual(AtlasBlendFactor.One, render.DestinationBlend);
            }
        }

        [Test]
        public void CullingAndDepthWrite()
        {
            material.SetFloat("_ZWrite", 0f);
            AtlasTestMaterials.AddSavedFloat(material, "_Cull", 0f);
            var pass = ParseSaved().Render.Pass;
            Assert.AreEqual(AtlasCull.Off, pass.Cull);
            Assert.IsFalse(pass.DepthWrite);
        }

        [Test]
        public void SpecularColourWhenTheShaderHasNoMetallic()
        {
            var shader = Shader.Find("Legacy Shaders/Specular");
            if (shader == null) Assert.Ignore("Legacy Shaders/Specular is not available.");
            var specular = new Material(shader);
            try
            {
                var colour = new Color(0.5f, 0.25f, 1f, 1f);
                specular.SetColor("_SpecColor", colour);
                var parser = AtlasShaderRegistry.Detect(specular);
                Assert.IsInstanceOf<AtlasFallbackParser>(parser);
                var response = parser.Parse(specular).Surface.Response;
                Assert.AreEqual(0f, Vector4.Distance(colour.linear, response.SpecularColor.Color), 1e-4f);
                Assert.AreEqual(0f, response.Metallic.Evaluate());
            }
            finally { Object.DestroyImmediate(specular); }
        }
    }
}
