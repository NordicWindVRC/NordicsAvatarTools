using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.BlendshapeToggle.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.BlendshapeToggle.Tests
{
    // The geometry step runs on the processed mesh, after the placeholder was added in Generating.
    public sealed class ToggleBakeTests
    {
        private static readonly Vector3[] Positions =
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(3, 1, 0),
        };

        private readonly List<Object> _created = new List<Object>();
        private SkinnedMeshRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            var mesh = Track(new Mesh());
            mesh.vertices = Positions;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            mesh.AddBlendShapeFrame("Other", 100f, Enumerable.Repeat(Vector3.up, 6).ToArray(), null, null);
            mesh.AddBlendShapeFrame("Show", 100f, new Vector3[6], null, null); // placeholder from Generating
            _renderer = Track(new GameObject("Parachute")).AddComponent<SkinnedMeshRenderer>();
            _renderer.sharedMesh = mesh;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created) if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private TogglePlan Plan(bool reverse) => new TogglePlan
            { Name = "Show", Reverse = reverse, Scale = 0f, Center = new Vector3(5, 5, 5), Slot = 0, Target = _renderer };

        [Test]
        public void PlaceholderIsReplacedInPlaceAndOtherShapesAreKept()
        {
            var result = Track(ToggleBake.Apply(_renderer, new[] { Plan(false) }));
            Assert.That(result.blendShapeCount, Is.EqualTo(2));
            Assert.That(result.GetBlendShapeName(1), Is.EqualTo("Show"));
            var other = new Vector3[6];
            result.GetBlendShapeFrameVertices(0, 0, other, new Vector3[6], new Vector3[6]);
            Assert.That(other[4], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void ReversedToggleStartsCollapsedAndRestoresTheProcessedGeometry()
        {
            var result = Track(ToggleBake.Apply(_renderer, new[] { Plan(true) }));
            var deltas = new Vector3[6];
            result.GetBlendShapeFrameVertices(1, 0, deltas, new Vector3[6], new Vector3[6]);
            var vertices = result.vertices;
            for (int i = 0; i < 3; i++)
            {
                Assert.That(vertices[i], Is.EqualTo(new Vector3(5, 5, 5)), "collapsed at weight 0");
                Assert.That(vertices[i] + deltas[i], Is.EqualTo(Positions[i]), "restored at weight 100");
            }
            Assert.That(vertices[4], Is.EqualTo(Positions[4]), "other submesh untouched");
        }

        [Test]
        public void ShapeWithoutPlaceholderIsAppended()
        {
            var plan = Plan(false);
            plan.Name = "Hide";
            var result = Track(ToggleBake.Apply(_renderer, new[] { plan }));
            Assert.That(result.GetBlendShapeName(2), Is.EqualTo("Hide"));
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
