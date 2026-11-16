using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasMeshRemapperTests
    {
        private readonly List<Object> created = new List<Object>();
        private Material a, b, c, output;

        [SetUp]
        public void Create()
        {
            a = Keep(AtlasTestMaterials.Standard());
            b = Keep(AtlasTestMaterials.Standard());
            c = Keep(AtlasTestMaterials.Standard());
            output = Keep(AtlasTestMaterials.Standard());
        }

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        private Mesh Mesh(Vector2[] uv, params int[][] submeshes)
        {
            var mesh = Keep(new Mesh());
            mesh.vertices = uv.Select((u, i) => new Vector3(i, 0f, 0f)).ToArray();
            mesh.uv = uv;
            mesh.subMeshCount = submeshes.Length;
            for (int s = 0; s < submeshes.Length; s++) mesh.SetTriangles(submeshes[s], s);
            return mesh;
        }

        private static readonly Vector2[] Quad = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

        private AtlasRemapResult Remap(Mesh mesh, Material[] materials, Dictionary<Material, AtlasRemapTarget> targets)
        {
            var result = AtlasMeshRemapper.Remap(mesh, materials, targets);
            if (result != null) Keep(result.Mesh);
            return result;
        }

        private static void AssertUV(Vector2 expected, Vector2 actual, string message = null)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(1e-5f), $"{message} expected {expected}, was {actual}");
        }

        [Test]
        public void SubmeshesOfOneOutputMergeAndTheirUVsLandInTheirRegions()
        {
            var mesh = Mesh(Quad.Concat(Quad).ToArray(), new[] { 0, 1, 2, 0, 2, 3 }, new[] { 4, 5, 6, 4, 6, 7 });
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [a] = new AtlasRemapTarget { Region = new Rect(0f, 0f, 0.5f, 1f), Output = output },
                [b] = new AtlasRemapTarget { Region = new Rect(0.5f, 0f, 0.5f, 1f), Output = output }
            };
            var result = Remap(mesh, new[] { a, b }, targets);
            Assert.AreEqual(1, result.Mesh.subMeshCount);
            CollectionAssert.AreEqual(new[] { output }, result.Materials);
            Assert.AreEqual(8, result.Mesh.vertexCount);
            Assert.AreEqual(0, result.SplitVertices);
            var uv = result.Mesh.uv;
            AssertUV(new Vector2(0.5f, 0f), uv[1]);
            AssertUV(new Vector2(0.5f, 1f), uv[2]);
            AssertUV(new Vector2(1f, 1f), uv[6]);
            Assert.AreEqual(12, result.Mesh.GetTriangles(0).Length);
            AssertUV(new Vector2(1f, 0f), mesh.uv[1], "The original mesh is unchanged:");
        }

        [Test]
        public void AVertexSharedWithAnotherMaterialIsSplitWithAllItsData()
        {
            var mesh = Mesh(Quad, new[] { 0, 1, 2 }, new[] { 0, 2, 3 });
            mesh.boneWeights = Enumerable.Range(0, 4).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1f }).ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
            mesh.AddBlendShapeFrame("Shape", 100f, Enumerable.Range(0, 4).Select(i => new Vector3(i, 0f, 0f)).ToArray(), null, null);
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [a] = new AtlasRemapTarget { Region = new Rect(0.5f, 0.5f, 0.5f, 0.5f), Output = output }
            };
            var result = Remap(mesh, new[] { a, c }, targets);
            // Vertices 0 and 2 are used by both submeshes.
            Assert.AreEqual(2, result.SplitVertices);
            Assert.AreEqual(6, result.Mesh.vertexCount);
            CollectionAssert.AreEqual(new[] { output, c }, result.Materials);
            var uv = result.Mesh.uv;
            var deltas = new Vector3[6];
            result.Mesh.GetBlendShapeFrameVertices(0, 0, deltas, null, null);
            var weights = result.Mesh.boneWeights;
            var atlased = result.Mesh.GetTriangles(0);
            var kept = result.Mesh.GetTriangles(1);
            foreach (int v in atlased)
                Assert.IsTrue(uv[v].x >= 0.5f && uv[v].y >= 0.5f, $"Atlased vertex {v} in the region.");
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }.Select(i => Quad[i]).ToArray(), kept.Select(v => uv[v]).ToArray(), "The other material's UVs are unchanged.");
            for (int i = 0; i < 3; i++)
            {
                int original = new[] { 0, 2, 3 }[i];
                Assert.AreEqual(original, deltas[kept[i]].x, 1e-6f, "Blend shape deltas follow their vertex.");
                Assert.AreEqual(original, weights[kept[i]].boneIndex0, "Bone weights follow their vertex.");
            }
            Assert.AreEqual("Shape", result.Mesh.GetBlendShapeName(0));
            Assert.AreEqual(4, result.Mesh.bindposes.Length);
        }

        [Test]
        public void TrianglesInAnotherTileMoveByWholeTilesAndStraddlingOnesAreClamped()
        {
            var uv = new[]
            {
                new Vector2(1.2f, 0.2f), new Vector2(1.4f, 0.2f), new Vector2(1.4f, 0.6f),
                new Vector2(0.9f, 0f), new Vector2(1.1f, 0f), new Vector2(1f, 0.5f)
            };
            var mesh = Mesh(uv, new[] { 0, 1, 2, 3, 4, 5 });
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [a] = new AtlasRemapTarget { Region = new Rect(0f, 0f, 0.5f, 0.5f), Output = output }
            };
            var result = Remap(mesh, new[] { a }, targets);
            var moved = result.Mesh.uv;
            AssertUV(new Vector2(0.1f, 0.1f), moved[0], "One tile over: the same texels.");
            AssertUV(new Vector2(0.2f, 0.3f), moved[2]);
            AssertUV(new Vector2(0.45f, 0f), moved[3], "Straddling: clamped.");
            AssertUV(new Vector2(0.5f, 0f), moved[4]);
            Assert.AreEqual(1, result.Clamped[a]);
        }

        [Test]
        public void OtherSubmeshesKeepTheirOrder()
        {
            var mesh = Mesh(Quad.Concat(Quad).Concat(Quad).ToArray(), new[] { 0, 1, 2 }, new[] { 4, 5, 6, 4, 6, 7 }, new[] { 8, 9, 10 });
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [a] = new AtlasRemapTarget { Region = new Rect(0f, 0f, 0.5f, 1f), Output = output },
                [b] = new AtlasRemapTarget { Region = new Rect(0.5f, 0f, 0.5f, 1f), Output = output }
            };
            var result = Remap(mesh, new[] { a, c, b }, targets);
            CollectionAssert.AreEqual(new[] { output, c }, result.Materials);
            Assert.AreEqual(6, result.Mesh.GetTriangles(0).Length, "Both sources' triangles in the first submesh.");
            Assert.AreEqual(6, result.Mesh.GetTriangles(1).Length);
        }

        [Test]
        public void AMeshWithoutSourcesIsLeftAlone()
        {
            var mesh = Mesh(Quad, new[] { 0, 1, 2, 0, 2, 3 });
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [a] = new AtlasRemapTarget { Region = new Rect(0f, 0f, 0.5f, 1f), Output = output }
            };
            Assert.IsNull(AtlasMeshRemapper.Remap(mesh, new[] { c }, targets));
        }

        [Test]
        public void TheBuildGeneratesCompressedTexturesAndAMaterialInMemory()
        {
            var shader = AtlasShaderRegistry.InstalledOutputShaders.FirstOrDefault();
            if (shader == null) Assert.Ignore("No output shader is installed.");
            var texture = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            texture.SetPixels(Enumerable.Repeat(Color.red, 16).ToArray());
            texture.Apply();
            var job = new AtlasGeneratorJob();
            var unified = new AtlasUnifiedMaterial();
            unified.Surface.BaseColor.Map = new AtlasTextureReference { Texture = texture };
            job.Sources.Add(new AtlasGeneratorSource { Material = unified, UVRegion = new Rect(0f, 0f, 1f, 1f) });
            var config = new AtlasOutputConfiguration { outputName = "Test", mainTexture = { width = 64, height = 64 } };
            var plan = AtlasOutputPipeline.Resolve(job, shader.Capabilities);
            var generated = AtlasOutputGenerator.Generate(job, plan, config, shader, mobile: false);
            Keep(generated.Material);
            foreach (var t in generated.Textures) Keep(t);
            var atlas = generated.Textures.Single();
            Assert.AreEqual(64, atlas.width);
            Assert.IsFalse(atlas.isReadable, "No CPU copy is kept.");
            Assert.That(atlas.format, Is.EqualTo(TextureFormat.DXT1).Or.EqualTo(TextureFormat.DXT1Crunched));
            Assert.Greater(atlas.mipmapCount, 1);
            Assert.AreEqual("Test_BaseColor", atlas.name);
            Assert.AreSame(atlas, AtlasShaderRegistry.Detect(generated.Material).Parse(generated.Material).Surface.BaseColor.Map.Texture);
            using (var serialized = new SerializedObject(atlas))
                Assert.IsTrue(serialized.FindProperty("m_StreamingMipmaps").boolValue, "Streaming mipmaps, as NDMF checks.");
        }
    }
}
