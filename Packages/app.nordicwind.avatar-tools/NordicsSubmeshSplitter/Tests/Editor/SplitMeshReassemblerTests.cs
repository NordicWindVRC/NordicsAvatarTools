using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using NordicWind.SubmeshSplitter.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.SubmeshSplitter.Tests
{
    // Synthetic body: two disjoint triangles in separate submeshes, one bone each,
    // one "Blink" blendshape. Each split part is a full clone isolated to one submesh,
    // matching the editor's clone-and-isolate generation.
    public sealed class SplitMeshReassemblerTests
    {
        private static readonly Vector3[] Positions =
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(3, 1, 0),
        };

        private readonly List<Object> _created = new List<Object>();
        private GameObject _root;
        private SkinnedMeshRenderer _target;
        private Mesh _original;
        private Transform[] _bones;

        [SetUp]
        public void SetUp()
        {
            _root = Track(new GameObject("Avatar"));
            _bones = new[] { new GameObject("Bone0").transform, new GameObject("Bone1").transform };
            foreach (var bone in _bones) bone.SetParent(_root.transform, false);
            _bones[1].localPosition = new Vector3(3, 0, 0);

            _original = Track(new Mesh { name = "Body" });
            _original.vertices = Positions;
            _original.subMeshCount = 2;
            _original.SetTriangles(new[] { 0, 1, 2 }, 0);
            _original.SetTriangles(new[] { 3, 4, 5 }, 1);
            _original.RecalculateNormals();
            _original.bindposes = new[] { _bones[0].worldToLocalMatrix, _bones[1].worldToLocalMatrix };
            var counts = new byte[] { 1, 1, 1, 1, 1, 1 };
            var weights = new BoneWeight1[6];
            for (int i = 0; i < 6; i++) weights[i] = new BoneWeight1 { boneIndex = i < 3 ? 0 : 1, weight = 1f };
            SetWeights(_original, counts, weights);
            _original.AddBlendShapeFrame("Blink", 100f, BlinkDeltas(), null, null);

            var body = Track(new GameObject("Body"));
            body.transform.SetParent(_root.transform, false);
            _target = body.AddComponent<SkinnedMeshRenderer>();
            _target.sharedMesh = _original;
            _target.bones = _bones;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void MergeKeepsOnlyReferencedVertices()
        {
            var result = Merge(Part(0), Part(1), out _);
            Assert.That(result.vertexCount, Is.EqualTo(6), "Each isolated clone carries all 6 source vertices; only 3 each are referenced.");
            Assert.That(result.subMeshCount, Is.EqualTo(2));
        }

        [Test]
        public void MergePreservesTrianglePositionsPerSubmesh()
        {
            var result = Merge(Part(0), Part(1), out _);
            var vertices = result.vertices;
            for (int sub = 0; sub < 2; sub++)
            {
                var triangles = result.GetTriangles(sub);
                Assert.That(triangles.Length, Is.EqualTo(3));
                for (int i = 0; i < 3; i++)
                    AssertNear(vertices[triangles[i]], Positions[sub * 3 + i]);
            }
        }

        [Test]
        public void MergeReusesOriginalBonesAndBindposes()
        {
            var result = Merge(Part(0), Part(1), out var mergedBones);
            Assert.That(mergedBones, Is.EqualTo(_bones));
            Assert.That(result.bindposes, Is.EqualTo(_original.bindposes));
            using (var weights = result.GetAllBoneWeights())
            {
                Assert.That(weights.Length, Is.EqualTo(6));
                for (int i = 0; i < 6; i++)
                    Assert.That(weights[i].boneIndex, Is.EqualTo(i < 3 ? 0 : 1), "vertex " + i);
            }
        }

        [Test]
        public void MergeRestoresBlendshapeDeltasForKeptVertices()
        {
            var result = Merge(Part(0), Part(1), out _);
            int index = result.GetBlendShapeIndex("Blink");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.GetBlendShapeFrameWeight(index, 0), Is.EqualTo(100f));
            var deltas = new Vector3[result.vertexCount];
            result.GetBlendShapeFrameVertices(index, 0, deltas,
                new Vector3[result.vertexCount], new Vector3[result.vertexCount]);
            var expected = BlinkDeltas();
            for (int i = 0; i < 6; i++) AssertNear(deltas[i], expected[i]);
        }

        [Test]
        public void MergeRejectsChangedBlendshapeFrameWeights()
        {
            var changed = Part(1);
            changed.sharedMesh.ClearBlendShapes();
            changed.sharedMesh.AddBlendShapeFrame("Blink", 50f, BlinkDeltas(), null, null);
            Assert.Throws<InvalidOperationException>(() => Merge(Part(0), changed, out _));
        }

        [Test]
        public void MergeKeepsFrontFaceForMirroredPart()
        {
            var mirrored = Part(0);
            mirrored.transform.localScale = new Vector3(-1, 1, 1);
            var result = Merge(mirrored, Part(1), out _);
            var vertices = result.vertices;
            var triangle = result.GetTriangles(0);
            var normal = Vector3.Cross(vertices[triangle[1]] - vertices[triangle[0]],
                vertices[triangle[2]] - vertices[triangle[0]]);
            Assert.That(normal.z, Is.GreaterThan(0f), "Winding must be reversed when the part transform mirrors.");
        }

        [Test]
        public void MergeRejectsEmptyMesh()
        {
            var empty = Part(1);
            empty.sharedMesh = Track(new Mesh { name = "Empty" });
            Assert.Throws<InvalidOperationException>(() => Merge(Part(0), empty, out _));
        }

        private Mesh Merge(SkinnedMeshRenderer a, SkinnedMeshRenderer b, out Transform[] mergedBones)
        {
            return Track(SplitMeshReassembler.Merge(_target, new[] { a, b }, _original, out mergedBones));
        }

        private SkinnedMeshRenderer Part(int submesh)
        {
            var go = Track(new GameObject("Part" + submesh));
            go.transform.SetParent(_root.transform, false);
            var mesh = Track(Object.Instantiate(_original));
            mesh.name = "Part" + submesh;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(new[] { submesh * 3, submesh * 3 + 1, submesh * 3 + 2 }, 0);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = _bones;
            return renderer;
        }

        private static Vector3[] BlinkDeltas()
        {
            var deltas = new Vector3[6];
            for (int i = 0; i < 6; i++) deltas[i] = new Vector3(0, 0, i + 1);
            return deltas;
        }

        private static void SetWeights(Mesh mesh, byte[] counts, BoneWeight1[] weights)
        {
            using (var c = new NativeArray<byte>(counts, Allocator.Temp))
            using (var w = new NativeArray<BoneWeight1>(weights, Allocator.Temp))
                mesh.SetBoneWeights(c, w);
        }

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That((actual - expected).sqrMagnitude, Is.LessThan(1e-8f), $"expected {expected} but was {actual}");
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
