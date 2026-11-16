using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasVRChatToonLitTests
    {
        private static AtlasUnifiedMaterial ParseSaved(Material material)
        {
            using (new AtlasTestMaterials.SavedValuesOnly())
                return new AtlasVRChatToonLitParser().Parse(material);
        }

        [Test]
        public void UntouchedMaterialIsTextureTimesVertexColourLitFlat()
        {
            var material = AtlasTestMaterials.Standard();
            try
            {
                var unified = ParseSaved(material);
                Assert.IsNull(unified.Surface.BaseColor.Map);
                Assert.AreEqual(Color.white, unified.Surface.BaseColor.Color);
                Assert.IsTrue(unified.Surface.VertexColor.MultiplyColor);
                Assert.IsFalse(unified.Surface.VertexColor.SrgbData);
                Assert.AreEqual(AtlasAlphaSource.None, unified.Surface.Alpha.Source);
                Assert.AreEqual(AtlasLightingFamily.Flat, unified.Lighting.Family);
                Assert.AreEqual(AtlasAmbientModel.Average, unified.Lighting.Ambient);
                Assert.AreEqual(1f, unified.Lighting.ReceiveShadows);
                Assert.AreEqual(1f, unified.Lighting.Contributions.MainLight);
                Assert.AreEqual(0f, unified.Lighting.Contributions.AdditionalLights);
                Assert.AreEqual(0f, unified.Lighting.Contributions.VertexLights);
                Assert.AreEqual(1f, unified.Lighting.Contributions.Ambient);
                Assert.AreEqual(AtlasSurfaceMode.Opaque, unified.Render.SurfaceMode);
                Assert.AreEqual(AtlasCull.Back, unified.Render.Pass.Cull);
                Assert.IsFalse(unified.Issues.Exists(i => i.IsWarning));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void TextureTilingAndOffsetAreRead()
        {
            var material = AtlasTestMaterials.Standard();
            var texture = new Texture2D(4, 4);
            try
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", new Vector2(2f, 3f));
                material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0.25f));
                var map = ParseSaved(material).Surface.BaseColor.Map;
                Assert.AreSame(texture, map.Texture);
                Assert.AreEqual(AtlasTextureInterpretation.Color, map.Interpretation);
                Assert.AreEqual(AtlasTextureSpace.UV0, map.Space);
                Assert.AreEqual(new Vector2(2f, 3f), map.Tiling);
                Assert.AreEqual(new Vector2(0.5f, 0.25f), map.Offset);
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void InstalledShaderIsDetectedAndReadsTheSameAsSavedValues()
        {
            var shader = Shader.Find("VRChat/Mobile/Toon Lit");
            if (shader == null) Assert.Ignore("VRChat Mobile Toon Lit is not installed.");
            var material = new Material(shader);
            var texture = new Texture2D(4, 4);
            try
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", new Vector2(4f, 1f));
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.IsInstanceOf<AtlasVRChatToonLitParser>(parser);
                var installed = parser.Parse(material);
                var saved = ParseSaved(material);
                Assert.AreSame(installed.Surface.BaseColor.Map.Texture, saved.Surface.BaseColor.Map.Texture);
                Assert.AreEqual(installed.Surface.BaseColor.Map.Tiling, saved.Surface.BaseColor.Map.Tiling);
                Assert.AreEqual(AtlasLightingFamily.Flat, installed.Lighting.Family);
                Assert.IsFalse(installed.Issues.Exists(i => i.IsWarning));
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(texture); }
        }
    }
}
