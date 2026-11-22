using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Vertex colors: live, baked or off (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasVertexColorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static readonly Vector2[] Quad = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        private static readonly Color Vertex = new Color(0.2f, 0.4f, 0.6f, 0.5f);

        private static AtlasUVTriangles Square(Color? color) => new AtlasUVTriangles
        {
            UV = Quad, Triangles = new[] { 0, 1, 2, 0, 2, 3 },
            Colors = color.HasValue ? Enumerable.Repeat(color.Value, 4).ToArray() : null
        };

        private static void AssertColor(Color expected, Color actual, string message, float tolerance = 2e-3f)
        {
            Assert.AreEqual(expected.r, actual.r, tolerance, message + " r");
            Assert.AreEqual(expected.g, actual.g, tolerance, message + " g");
            Assert.AreEqual(expected.b, actual.b, tolerance, message + " b");
        }

        [Test]
        public void VertexColorsArePaintedAndOverlapsAveraged()
        {
            using (var map = AtlasVertexColors.Paint(new[] { Square(Color.red), Square(Color.blue) }, 8, 8))
                AssertColor(new Color(0.5f, 0f, 0.5f), map.GetPixel(4, 4), "the average");
            Assert.IsNull(AtlasVertexColors.Paint(new[] { Square(null) }, 8, 8), "a mesh without colors: white");
        }

        private static AtlasUnifiedMaterial Colored(float amount, bool srgb = false)
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.VertexColor.MultiplyColor = true;
            material.Surface.VertexColor.Amount = amount;
            material.Surface.VertexColor.SrgbData = srgb;
            return material;
        }

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < materials.Length; i++)
            {
                var source = new AtlasGeneratorSource { Material = materials[i], UVRegion = new Rect(i / (float)materials.Length, 0f, 1f / materials.Length, 1f) };
                source.Coverage.Add(Square(Vertex));
                job.Sources.Add(source);
            }
            return job;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;

        private static Color BaseColor(AtlasOutputPlan plan, AtlasGeneratorJob job, int index)
        {
            using (var map = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(index, job.Sources[index].Material, 4, 4))
                return map.GetPixel(1, 1);
        }

        [Test]
        public void BakedVertexColorsMultiplyTheBaseColor()
        {
            var job = Job(Colored(0.5f));
            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasVertexColorFeature.FeatureId, ModeId = "baked" });
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.IsFalse(plan.Output.Surface.VertexColor.MultiplyColor, "not on the mesh");
            Assert.IsEmpty(plan.VertexColors);
            AssertColor(new Color(0.6f, 0.7f, 0.8f), BaseColor(plan, job, 0), "lerp(1, vertex, 0.5)");
        }

        [Test]
        public void LiveVertexColorsStayOnTheMeshRewrittenPerSource()
        {
            var plain = new AtlasUnifiedMaterial();
            var job = Job(Colored(0.5f), plain);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var output = plan.Output.Surface.VertexColor;
            Assert.IsTrue(output.MultiplyColor);
            Assert.AreEqual(0.5f, output.Amount, 1e-5f, "the sources' common amount");
            AssertColor(Color.white, BaseColor(plan, job, 0), "not baked");
            AssertColor(Vertex, plan.VertexColors[0](Vertex), "read the same way: unchanged");
            AssertColor(Color.white, plan.VertexColors[1](Vertex), "no vertex colors: white");

            var toon = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.AreEqual(1f, toon.Output.Surface.VertexColor.Amount, 1e-5f, "no amount setting");
            AssertColor(new Color(0.6f, 0.7f, 0.8f), toon.VertexColors[0](Vertex), "lerp(1, vertex, 0.5) on the mesh");
        }

        [Test]
        public void ConversionsMatchTheSourcesSetting()
        {
            var output = new AtlasVertexColor { MultiplyColor = true, Amount = 1f, SrgbData = true };
            var source = new AtlasVertexColor { MultiplyColor = true, Amount = 1f, SrgbData = false };
            var converted = AtlasVertexColorFeature.Convert(source, output, Vertex);
            Assert.AreEqual(0.2f, AtlasVertexColors.Decode(converted.r), 1e-4f, "decoded by the output: the source's linear value");
            Assert.AreEqual(0.4f, AtlasVertexColors.Encode(AtlasVertexColors.Decode(0.4f)), 1e-4f);

            var feature = new AtlasVertexColorFeature();
            var job = Job(Colored(1f, srgb: true), Colored(1f, srgb: false));
            Assert.AreEqual("srgb", feature.ConflictingSwitches(job, Poiyomi).Single().Id);
            Assert.IsEmpty(feature.ConflictingSwitches(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities), "no sRGB setting");
            Assert.IsEmpty(feature.ConflictingSwitches(Job(Colored(1f, true), Colored(0.5f, true)), Poiyomi));
        }

        [Test]
        public void MasksAndBusStepsReadTheVertexColors()
        {
            var material = new AtlasUnifiedMaterial();
            material.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 0, Kind = AtlasMaskBusStepKind.VertexColor, VertexChannel = AtlasChannel.Blue, VertexLinear = false, Blend = AtlasMaskBlend.Replace });
            var mask = new AtlasMask { VertexColor = true, Channel = AtlasChannel.Green };
            Assert.IsTrue(AtlasMapEvaluator.CanEvaluate(mask, material));
            using (var map = AtlasMapEvaluator.ScaledMask(mask, 1f, 4, 4, material)) Assert.AreEqual(1f, map.GetPixel(1, 1).r, 1e-3f, "nothing attached: white");

            AtlasVertexColors.Attach(material, new[] { Square(Vertex) }, false, false);
            using (var map = AtlasMapEvaluator.ScaledMask(mask, 1f, 4, 4, material)) Assert.AreEqual(0.4f, map.GetPixel(1, 1).r, 1e-3f, "green");
            using (var map = AtlasMaskBusEvaluator.Channel(material, 0, 4, 4)) Assert.AreEqual(0.6f, map.GetPixel(1, 1).r, 1e-3f, "blue");
            Assert.IsTrue(AtlasMaskBusEvaluator.CanBake(material, 0));
            AtlasVertexColors.Detach(material);
        }

        [Test]
        public void TheWritersSetTheVertexColorsTheParsersReadBack()
        {
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders)
            {
                var support = shader.Capabilities.VertexColors;
                var output = new AtlasUnifiedMaterial();
                var vertex = output.Surface.VertexColor;
                vertex.MultiplyColor = true;
                vertex.Amount = (support & AtlasVertexColorSupport.Amount) != 0 ? 0.5f : 1f;
                vertex.MultiplyAlpha = (support & AtlasVertexColorSupport.Alpha) != 0;
                vertex.AlphaAmount = 0.25f;
                vertex.SrgbData = (support & AtlasVertexColorSupport.Srgb) != 0;
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Surface.VertexColor;
                Assert.IsTrue(parsed.MultiplyColor, shader.DisplayName);
                Assert.AreEqual(vertex.Amount, parsed.Amount, 1e-5f, shader.DisplayName);
                Assert.AreEqual(vertex.MultiplyAlpha, parsed.MultiplyAlpha, shader.DisplayName);
                Assert.AreEqual(vertex.SrgbData, parsed.SrgbData, shader.DisplayName);

                var without = new Material(shader.FindShader());
                created.Add(without);
                shader.Write(new AtlasUnifiedMaterial(), without);
                // A shader without a switch for it always multiplies.
                Assert.AreEqual((support & AtlasVertexColorSupport.Always) != 0,
                    AtlasShaderRegistry.Detect(without).Parse(without).Surface.VertexColor.MultiplyColor, shader.DisplayName + ": none");
            }
        }

        [Test]
        public void TheRemapRewritesTheSourcesVertexColors()
        {
            var a = AtlasTestMaterials.Standard();
            var c = AtlasTestMaterials.Standard();
            var output = AtlasTestMaterials.Standard();
            created.AddRange(new Object[] { a, c, output });
            var mesh = new Mesh();
            created.Add(mesh);
            var uv = Quad.Concat(Quad).ToArray();
            mesh.vertices = uv.Select((u, i) => new Vector3(i, 0f, 0f)).ToArray();
            mesh.uv = uv;
            mesh.colors = Enumerable.Repeat(Vertex, 8).ToArray();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.SetTriangles(new[] { 4, 5, 6, 4, 6, 7 }, 1);
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [a] = new AtlasRemapTarget { Region = new Rect(0f, 0f, 1f, 1f), Output = output, VertexColor = color => Color.green }
            };
            var result = AtlasMeshRemapper.Remap(mesh, new[] { a, c }, targets);
            created.Add(result.Mesh);
            var colors = result.Mesh.colors;
            var triangles = result.Mesh.GetTriangles(0);
            AssertColor(Color.green, colors[triangles[0]], "the source's vertices");
            AssertColor(Vertex, colors[result.Mesh.GetTriangles(1)[0]], "another material's vertices keep theirs");
            Assert.AreEqual(VertexAttributeFormat.Float32, result.Mesh.GetVertexAttributeFormat(VertexAttribute.Color), "float colors stay float");

            // 8-bit colors, or none, are written in 8 bits.
            mesh.colors32 = Enumerable.Repeat((Color32)Vertex, 8).ToArray();
            result = AtlasMeshRemapper.Remap(mesh, new[] { a, c }, targets);
            created.Add(result.Mesh);
            Assert.AreEqual(VertexAttributeFormat.UNorm8, result.Mesh.GetVertexAttributeFormat(VertexAttribute.Color), "8-bit colors");
            AssertColor(Color.green, result.Mesh.colors[result.Mesh.GetTriangles(0)[0]], "8 bits: the source's vertices");
            mesh.colors32 = new Color32[0];
            result = AtlasMeshRemapper.Remap(mesh, new[] { a, c }, targets);
            created.Add(result.Mesh);
            Assert.AreEqual(VertexAttributeFormat.UNorm8, result.Mesh.GetVertexAttributeFormat(VertexAttribute.Color), "no colors: white in 8 bits");
            AssertColor(Color.white, result.Mesh.colors[result.Mesh.GetTriangles(1)[0]], "another material's vertices: white");
        }
    }
}
