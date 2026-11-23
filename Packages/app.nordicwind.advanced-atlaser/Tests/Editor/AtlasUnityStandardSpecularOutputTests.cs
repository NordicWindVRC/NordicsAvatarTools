using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Unity Standard (Specular setup) as an output (ATLAS_OUTPUT.md, "Output shaders"): a
    // specular color in place of metallic, which holds each source's reflectance, metallic and
    // highlight and reflection strengths.
    public sealed class AtlasUnityStandardSpecularOutputTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static IAtlasOutputShader Shader => AtlasShaderRegistry.Output("standardspecular");
        private static AtlasOutputCapabilities Specular => Shader.Capabilities;

        private Texture2D Texture(string name)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name };
            created.Add(texture);
            return texture;
        }

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

        // A lit source with a base color, and its highlights and reflections at strengths
        // relative to Unity Standard's own (π and 1 on the unified scale).
        private static AtlasUnifiedMaterial Lit(Color baseColor, float highlights, float reflections)
        {
            var material = new AtlasUnifiedMaterial();
            material.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            material.Surface.BaseColor.Color = baseColor;
            var response = material.Surface.Response;
            response.Highlights.Enabled = highlights > 0f;
            response.Highlights.Strength = highlights * Mathf.PI;
            response.Reflections.Enabled = reflections > 0f;
            response.Reflections.Strength = reflections;
            return material;
        }

        private static Color At(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index)
        {
            using (var result = plan.Maps.Single(m => m.Map == map).Evaluate(index, job.Sources[index].Material, 4, 4))
                return result.GetPixel(1, 1);
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void TheSpecularSetupIsAnOutputAboveTheMetallicOne()
        {
            Assert.IsNotNull(Shader.FindShader(), "built in");
            Assert.AreEqual(AtlasShaderFamilies.Unity, Shader.Family);
            Assert.Greater(Shader.Rank, AtlasShaderRegistry.Output("standard").Rank, "it keeps more");
            var capabilities = Specular;
            Assert.IsFalse(capabilities.Provides(AtlasVirtualMap.Metallic));
            var texture = capabilities.TextureFor(AtlasVirtualMap.SpecularColor);
            Assert.IsTrue(texture.Srgb, "a color");
            Assert.AreSame(texture, capabilities.TextureFor(AtlasVirtualMap.Roughness));
            var smoothness = texture.Channels.Find(c => c.Map == AtlasVirtualMap.Roughness);
            Assert.AreEqual(AtlasTextureChannels.A, smoothness.Channels);
            Assert.IsTrue(smoothness.Invert, "stored as smoothness");
            // The occlusion map (G) and the detail mask (A) share a texture.
            Assert.AreSame(capabilities.TextureFor(AtlasVirtualMap.Occlusion), capabilities.TextureFor(AtlasVirtualMap.Mask1));

            string Mode(string feature) => AtlasOutputFeatures.Effective(AtlasOutputFeatures.Find(feature), "", capabilities)?.Id;
            Assert.AreEqual("specularcolor", Mode("specular"));
            Assert.AreEqual("native", Mode("occlusion"));
            Assert.AreEqual("native", Mode("emission"));
            Assert.AreEqual("live", Mode(AtlasDetailTextureFeature.FeatureId));
            // The metallic setup keeps its mode.
            Assert.AreEqual("native", AtlasOutputFeatures.Effective(AtlasOutputFeatures.Find("specular"), "", AtlasShaderRegistry.Output("standard").Capabilities).Id);
        }

        [Test]
        public void TheWriterSetsTheSpecularMapTheParserReadsBack()
        {
            var texture = Texture("specular");
            var output = new AtlasUnifiedMaterial();
            var response = output.Surface.Response;
            response.SpecularColor = new AtlasColorInput { Map = new AtlasTextureReference { Texture = texture } };
            response.Roughness.Map = new AtlasTextureReference { Texture = texture };
            var material = new Material(Shader.FindShader());
            created.Add(material);
            Shader.Write(output, material);
            Assert.IsTrue(material.IsKeywordEnabled("_SPECGLOSSMAP"));
            Assert.IsFalse(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"));
            Assert.IsFalse(material.IsKeywordEnabled("_GLOSSYREFLECTIONS_OFF"));
            var parser = AtlasShaderRegistry.Detect(material);
            Assert.IsInstanceOf<AtlasUnityStandardSpecularParser>(parser);
            var read = parser.Parse(material).Surface.Response;
            Assert.AreSame(texture, read.SpecularColor.Map.Texture);
            Assert.AreSame(texture, read.Roughness.Map.Texture);
            Assert.AreEqual(AtlasChannel.Alpha, read.Roughness.Channel);
            Assert.IsTrue(read.Roughness.InvertResult);
            Assert.AreEqual(Mathf.PI, read.Highlights.Strength, 1e-5f, "Unity's highlights on the unified scale");

            // Without a specular map the surface reflects nothing.
            Shader.Write(new AtlasUnifiedMaterial(), material);
            Assert.IsFalse(material.IsKeywordEnabled("_SPECGLOSSMAP"));
            Assert.IsTrue(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"));
            Assert.IsTrue(material.IsKeywordEnabled("_GLOSSYREFLECTIONS_OFF"));
            var none = AtlasShaderRegistry.Detect(material).Parse(material).Surface.Response;
            Assert.IsNull(none.SpecularColor.Map);
            Assert.AreEqual(0f, none.SpecularColor.Color.maxColorComponent, 1e-5f, "a black specular color");
        }

        // The specular color is what the surface reflects, per texel, so it holds what the
        // metallic setup has no control for: a source's highlight and reflection strengths
        // (their mean, since one color scales both), and nothing at all on a source without
        // either. A metallic source is converted.
        [Test]
        public void StrengthsAndMetallicGoIntoTheSpecularColor()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var weak = Lit(gray, 0.5f, 0.25f);
            var matte = Lit(gray, 0f, 0f);
            var metal = Lit(new Color(0.8f, 0.6f, 0.2f, 1f), 1f, 1f);
            metal.Surface.Response.Metallic.Constant = 1f;
            var job = Job(weak, matte, metal);
            var plan = AtlasOutputPipeline.Resolve(job, Specular);
            Assert.AreEqual("specularcolor", plan.Modes["specular"].Id);
            Assert.IsNotNull(plan.Output.Surface.Response.SpecularColor?.Map);
            Assert.IsTrue(plan.Output.Surface.Response.Highlights.Enabled);
            float tint = plan.Output.Surface.BaseColor.Color.r;

            // 4% × the mean of half the shader's highlights and a quarter of its reflections.
            float expected = 0.04f * (0.5f + 0.25f) * 0.5f;
            Assert.AreEqual(expected, At(plan, AtlasVirtualMap.SpecularColor, job, 0).r, 1e-4f);
            StringAssert.Contains("the mean of the two strengths is used where they differ (source 1)", Notes(plan));
            // The shader takes the specular color off the diffuse: the base color makes up for
            // the part the source's own 4% didn't take.
            Assert.AreEqual(0.5f * (1f - 0.04f) / (1f - expected), At(plan, AtlasVirtualMap.BaseColor, job, 0).r * tint, 1e-3f);

            // Without highlights or reflections: black, and the base color as it is.
            Assert.AreEqual(0f, At(plan, AtlasVirtualMap.SpecularColor, job, 1).maxColorComponent, 1e-6f);
            Assert.AreEqual(0.5f, At(plan, AtlasVirtualMap.BaseColor, job, 1).r * tint, 1e-3f);

            // A metal reflects its base color and has no diffuse.
            var reflected = At(plan, AtlasVirtualMap.SpecularColor, job, 2);
            Assert.AreEqual(0.8f, reflected.r, 1e-3f);
            Assert.AreEqual(0.2f, reflected.b, 1e-3f);
            Assert.AreEqual(0f, At(plan, AtlasVirtualMap.BaseColor, job, 2).r * tint, 1e-3f);
            StringAssert.Contains("the metallic of source 3 is converted", Notes(plan));

            // Against the shader, with its lighting.
            Assert.IsNotNull(AtlasOutputPipeline.Resolve(job, Shader).KeptLighting);
        }

        // A source of the specular workflow keeps its own specular color; a source at the
        // shader's own strengths keeps its reflectance exactly.
        [Test]
        public void ASpecularSourceKeepsItsColorAndAFullOneItsReflectance()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var tinted = Lit(gray, 1f, 1f);
            tinted.Surface.Response.SpecularColor = new AtlasColorInput { Color = new Color(0.2f, 0.3f, 0.4f, 1f) };
            var glossy = Lit(gray, 1f, 1f);
            glossy.Surface.Response.Reflectance.Constant = 1f;
            var job = Job(tinted, glossy);
            var plan = AtlasOutputPipeline.Resolve(job, Specular);
            float tint = plan.Output.Surface.BaseColor.Color.r;
            var kept = At(plan, AtlasVirtualMap.SpecularColor, job, 0);
            Assert.AreEqual(0.2f, kept.r, 1e-4f);
            Assert.AreEqual(0.4f, kept.b, 1e-4f);
            Assert.AreEqual(0.5f, At(plan, AtlasVirtualMap.BaseColor, job, 0).r * tint, 1e-3f, "its diffuse is its own");
            Assert.AreEqual(0.16f, At(plan, AtlasVirtualMap.SpecularColor, job, 1).r, 1e-4f, "F0 = 0.16 × reflectance²");
            Assert.AreEqual(0.5f, At(plan, AtlasVirtualMap.BaseColor, job, 1).r * tint, 1e-3f);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("mean of the two")), Notes(plan));
        }
    }
}
