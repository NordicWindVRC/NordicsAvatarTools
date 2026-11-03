using NUnit.Framework;
using NordicWind.BlendshapeToggle.Editor;
using NordicWind.SubmeshSplitter;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.BlendshapeToggle.Tests
{
    // A toggle on a renderer split by Nordic's Submesh Splitter edits the split part for its
    // material slot; the original renderer only receives a placeholder shape.
    public sealed class ToggleTargetTests
    {
        private GameObject _body;
        private Mesh _mesh;

        [SetUp]
        public void SetUp()
        {
            _body = new GameObject("Body");
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(3, 1, 0) };
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            _mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            var renderer = _body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = _mesh;
            renderer.sharedMaterials = new Material[2];
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_body);
            Object.DestroyImmediate(_mesh);
        }

        [Test]
        public void ToggleWithoutSplitterTargetsItsOwnRenderer()
        {
            var toggle = _body.AddComponent<NordicsBlendshapeToggle>();
            toggle.materialSlot = 1;
            var target = ToggleTarget.Resolve(toggle);
            Assert.That(target.Renderer, Is.SameAs(_body.GetComponent<SkinnedMeshRenderer>()));
            Assert.That(target.Slot, Is.EqualTo(1));
            Assert.That(target.Placeholder, Is.Null);
        }

        [Test]
        public void ToggleOnSplitRendererTargetsThePartForItsSlot()
        {
            var parts = new SkinnedMeshRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                parts[i] = new GameObject("Submesh_" + i).AddComponent<SkinnedMeshRenderer>();
                parts[i].transform.SetParent(_body.transform, false);
                parts[i].sharedMesh = _mesh;
                parts[i].sharedMaterials = new Material[1];
            }
            var splitter = _body.AddComponent<NordicsSubmeshSplitter>();
            var serialized = new SerializedObject(splitter);
            serialized.FindProperty("hasGenerated").boolValue = true;
            var list = serialized.FindProperty("generatedRenderers");
            list.arraySize = 2;
            for (int i = 0; i < 2; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var toggle = _body.AddComponent<NordicsBlendshapeToggle>();
            toggle.materialSlot = 1;
            var target = ToggleTarget.Resolve(toggle);
            Assert.That(target.Renderer, Is.SameAs(parts[1]));
            Assert.That(target.Slot, Is.EqualTo(0));
            Assert.That(target.Placeholder, Is.SameAs(_body.GetComponent<SkinnedMeshRenderer>()));
        }
    }
}
