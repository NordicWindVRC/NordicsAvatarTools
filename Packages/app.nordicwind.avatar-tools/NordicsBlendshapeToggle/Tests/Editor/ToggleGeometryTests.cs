using System;
using NUnit.Framework;
using NordicWind.BlendshapeToggle.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.BlendshapeToggle.Tests
{
    // Two disjoint triangles in separate submeshes; the toggle targets submesh 0.
    public sealed class ToggleGeometryTests
    {
        private GameObject _go;
        private GameObject _target;
        private Mesh _mesh;
        private NordicsBlendshapeToggle _toggle;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("NBT test");
            _target = new GameObject("NBT target");
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up,
                new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(3, 1, 0) };
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            _mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            _mesh.RecalculateBounds();
            var renderer = _go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = _mesh;
            renderer.sharedMaterials = new Material[2];
            _toggle = _go.AddComponent<NordicsBlendshapeToggle>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_target);
            Object.DestroyImmediate(_mesh);
        }

        [Test]
        public void ValidateSelectsOnlyVerticesOfTheChosenSubmesh()
        {
            Assert.That(ToggleGeometry.Validate(_toggle).Indices, Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SubmeshCenterIsTheCenterOfTheSelectedVertices()
        {
            AssertNear(ToggleGeometry.Validate(_toggle).Center, new Vector3(.5f, .5f, 0));
        }

        [Test]
        public void MeshCenterUsesMeshBounds()
        {
            _toggle.centerMode = ToggleCenterMode.MeshCenter;
            AssertNear(ToggleGeometry.Validate(_toggle).Center, _mesh.bounds.center);
        }

        [Test]
        public void TransformCenterIsConvertedIntoRendererLocalSpace()
        {
            _go.transform.SetPositionAndRotation(new Vector3(4, 2, 1), Quaternion.Euler(20, 35, 40));
            _go.transform.localScale = new Vector3(2, 3, 4);
            _target.transform.position = _go.transform.TransformPoint(new Vector3(.2f, .3f, .4f));
            _toggle.centerMode = ToggleCenterMode.Transform;
            _toggle.targetTransform = _target.transform;
            AssertNear(ToggleGeometry.Validate(_toggle).Center, new Vector3(.2f, .3f, .4f));
        }

        [Test]
        public void DeltasCollapseSelectedVerticesTowardTheCenter()
        {
            var geometry = ToggleGeometry.Validate(_toggle);
            var deltas = geometry.CreateDeltas(_mesh, .001f);
            var vertices = _mesh.vertices;
            AssertNear(vertices[0] + deltas[0], geometry.Center + (vertices[0] - geometry.Center) * .001f);
        }

        [Test]
        public void DeltasLeaveOtherSubmeshesUnchanged()
        {
            var deltas = ToggleGeometry.Validate(_toggle).CreateDeltas(_mesh, .001f);
            for (int i = 3; i < 6; i++) AssertNear(deltas[i], Vector3.zero);
        }

        [Test]
        public void ValidateRejectsExistingBlendshapeName()
        {
            _mesh.AddBlendShapeFrame("Existing", 100, new Vector3[6], null, null);
            _toggle.blendshapeName = "Existing";
            Assert.Throws<InvalidOperationException>(() => ToggleGeometry.Validate(_toggle));
        }

        [Test]
        public void ValidateRejectsVerticesSharedWithAnotherSubmesh()
        {
            _mesh.SetTriangles(new[] { 0, 4, 5 }, 1);
            Assert.Throws<InvalidOperationException>(() => ToggleGeometry.Validate(_toggle));
        }

        [Test]
        public void ValidateRejectsMissingTargetTransform()
        {
            _toggle.centerMode = ToggleCenterMode.Transform;
            Assert.Throws<InvalidOperationException>(() => ToggleGeometry.Validate(_toggle));
        }

        private static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That((actual - expected).sqrMagnitude, Is.LessThan(1e-8f), $"expected {expected} but was {actual}");
        }
    }
}
