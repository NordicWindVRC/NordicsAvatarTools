using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.SubmeshSplitter.Editor;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.SubmeshSplitter.Tests
{
    // A synthetic avatar: Avatar/Body (original renderer + splitter) with two split parts,
    // each an isolated clone of the two-submesh Body mesh, as the editor generates them.
    public sealed class SplitReassemblyTests
    {
        private static readonly Vector3[] Positions =
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(3, 1, 0),
        };

        private readonly List<Object> _created = new List<Object>();
        private GameObject _avatar;
        private SkinnedMeshRenderer _body;
        private NordicsSubmeshSplitter _splitter;
        private Mesh _source;
        private Material[] _sourceMaterials;

        [SetUp]
        public void SetUp()
        {
            _avatar = Track(new GameObject("Avatar"));
            var bone = new GameObject("Hips").transform;
            bone.SetParent(_avatar.transform, false);

            _source = Track(new Mesh { name = "Body" });
            _source.vertices = Positions;
            _source.subMeshCount = 2;
            _source.SetTriangles(new[] { 0, 1, 2 }, 0);
            _source.SetTriangles(new[] { 3, 4, 5 }, 1);
            _source.RecalculateNormals();
            _source.bindposes = new[] { Matrix4x4.identity };
            using (var counts = new NativeArray<byte>(Enumerable.Repeat((byte)1, 6).ToArray(), Allocator.Temp))
            using (var weights = new NativeArray<BoneWeight1>(Enumerable.Repeat(new BoneWeight1 { boneIndex = 0, weight = 1 }, 6).ToArray(), Allocator.Temp))
                _source.SetBoneWeights(counts, weights);
            _source.AddBlendShapeFrame("Blink", 100f, Deltas(0, 0, 1), null, null);
            _source.AddBlendShapeFrame("Smile", 100f, Deltas(0, 1, 0), null, null);

            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(_avatar.transform, false);
            _body = bodyObject.AddComponent<SkinnedMeshRenderer>();
            _body.sharedMesh = _source;
            _body.bones = new[] { bone };
            _sourceMaterials = new[] { Track(new Material(Shader.Find("Standard"))), Track(new Material(Shader.Find("Standard"))) };
            _body.sharedMaterials = _sourceMaterials;
            _body.localBounds = new Bounds(new Vector3(0, 1, 0), new Vector3(3, 3, 3));
            _body.enabled = false; // the editor hides the original while split

            _splitter = bodyObject.AddComponent<NordicsSubmeshSplitter>();
            var container = new GameObject("Nordic_SplitMeshes").transform;
            container.SetParent(bodyObject.transform, false);
            _splitter.generatedRoot = container;
            _splitter.hasGenerated = true;
            _splitter.originalRendererWasEnabled = true;
            for (int i = 0; i < 2; i++)
            {
                var part = new GameObject("Submesh_" + i).AddComponent<SkinnedMeshRenderer>();
                part.transform.SetParent(container, false);
                var mesh = Track(Object.Instantiate(_source));
                mesh.subMeshCount = 1;
                mesh.SetTriangles(new[] { i * 3, i * 3 + 1, i * 3 + 2 }, 0);
                part.sharedMesh = mesh;
                part.bones = _body.bones;
                part.sharedMaterials = new Material[] { null }; // stale preview material
                _splitter.generatedRenderers.Add(part);
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void ReassemblyUsesTheOriginalRenderersMaterialsBoundsAndEnabledState()
        {
            var bounds = _body.localBounds;
            SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body));
            Assert.That(_body.sharedMesh, Is.Not.SameAs(_source));
            Assert.That(_body.sharedMaterials, Is.EqualTo(_sourceMaterials));
            Assert.That(_body.localBounds, Is.EqualTo(bounds));
            Assert.That(_body.enabled, Is.True);
        }

        [Test]
        public void ReassemblyRemovesTheSplitObjectsAndTheComponent()
        {
            var container = _splitter.generatedRoot;
            SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body));
            Assert.That(container == null, Is.True);
            Assert.That(_body.GetComponent<NordicsSubmeshSplitter>() == null, Is.True);
        }

        [Test]
        public void DisabledPartIsLeftOutOfTheMergedMesh()
        {
            _splitter.generatedRenderers[1].gameObject.SetActive(false);
            SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body));
            Assert.That(_body.sharedMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(_body.sharedMesh.vertexCount, Is.EqualTo(3));
            Assert.That(_body.sharedMaterials, Is.EqualTo(new[] { _sourceMaterials[0] }));
        }

        [Test]
        public void ShapeRemovedFromTheOriginalIsBakedAtItsWeight()
        {
            var snapshot = SourceSnapshot.Take(_body);
            snapshot.Weights["Blink"] = 50f;
            // A general optimizer (e.g. VRCFury Blendshape Optimizer) removed "Blink" from the original.
            var optimized = Track(Object.Instantiate(_source));
            optimized.ClearBlendShapes();
            optimized.AddBlendShapeFrame("Smile", 100f, Deltas(0, 1, 0), null, null);
            _body.sharedMesh = optimized;

            SplitReassembly.Run(_splitter, _avatar, snapshot);
            var result = _body.sharedMesh;
            Assert.That(result.GetBlendShapeIndex("Blink"), Is.EqualTo(-1));
            Assert.That(result.GetBlendShapeName(0), Is.EqualTo("Smile"));
            Assert.That(result.vertices[0].z, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void ShapeRenamedByAnotherToolKeepsItsDataUnderTheNewName()
        {
            // VRCFury face tracking renames shapes on the original (Blink → VF_FT_Blink),
            // keeping order and data. The parts still use the old name.
            var snapshot = SourceSnapshot.Take(_body);
            var renamed = Track(Object.Instantiate(_source));
            renamed.ClearBlendShapes();
            renamed.AddBlendShapeFrame("VF_FT_Blink", 100f, Deltas(0, 0, 1), null, null);
            renamed.AddBlendShapeFrame("Smile", 100f, Deltas(0, 1, 0), null, null);
            _body.sharedMesh = renamed;

            SplitReassembly.Run(_splitter, _avatar, snapshot);
            var result = _body.sharedMesh;
            Assert.That(result.blendShapeCount, Is.EqualTo(2));
            Assert.That(result.GetBlendShapeName(0), Is.EqualTo("VF_FT_Blink"));
            Assert.That(result.GetBlendShapeIndex("Blink"), Is.EqualTo(-1));
            var deltas = new Vector3[result.vertexCount];
            result.GetBlendShapeFrameVertices(0, 0, deltas, new Vector3[result.vertexCount], new Vector3[result.vertexCount]);
            Assert.That(deltas[0], Is.EqualTo(new Vector3(0, 0, 1)));
            Assert.That(result.vertices[0], Is.EqualTo(Positions[0]), "a renamed shape must not be baked");
        }

        [Test]
        public void BlendshapeOrderFollowsTheOriginalSoIndicesStayValid()
        {
            // A part processor dropped "Blink" from every part; the merged mesh keeps an empty
            // placeholder so "Smile" stays at index 1 (eyelid settings store indices).
            foreach (var part in _splitter.generatedRenderers)
            {
                part.sharedMesh.ClearBlendShapes();
                part.sharedMesh.AddBlendShapeFrame("Smile", 100f, Deltas(0, 1, 0), null, null);
            }
            SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body));
            var result = _body.sharedMesh;
            Assert.That(result.GetBlendShapeName(0), Is.EqualTo("Blink"));
            Assert.That(result.GetBlendShapeName(1), Is.EqualTo("Smile"));
        }

        [Test]
        public void ShapeAddedByAPartActionIsAppended()
        {
            _splitter.generatedRenderers[0].sharedMesh.AddBlendShapeFrame("NBT_Hide", 100f, Deltas(1, 0, 0), null, null);
            SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body));
            Assert.That(_body.sharedMesh.GetBlendShapeName(2), Is.EqualTo("NBT_Hide"));
        }

        [Test]
        public void OriginalMeshEditedByAnotherToolStopsTheBuild()
        {
            var snapshot = SourceSnapshot.Take(_body);
            var edited = Track(Object.Instantiate(_source));
            edited.AddBlendShapeFrame("AddedElsewhere", 100f, Deltas(0, 0, 0), null, null);
            _body.sharedMesh = edited;
            var ex = Assert.Throws<System.InvalidOperationException>(() => SplitReassembly.Run(_splitter, _avatar, snapshot));
            StringAssert.Contains("AddedElsewhere", ex.Message);
        }

        [Test]
        public void ComponentLeftOnAPartStopsTheBuild()
        {
            _splitter.generatedRenderers[0].gameObject.AddComponent<BoxCollider>();
            var ex = Assert.Throws<System.InvalidOperationException>(() => SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body)));
            StringAssert.Contains("BoxCollider", ex.Message);
        }

        [Test]
        public void AnimationTargetingAPartStopsTheBuild()
        {
            var clip = Track(new AnimationClip { name = "TogglePart" });
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Body/Nordic_SplitMeshes/Submesh_1", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 1, 0));
            var controller = Track(new AnimatorController());
            controller.AddLayer("Base");
            controller.layers[0].stateMachine.AddState("Off").motion = clip;
            _avatar.AddComponent<Animator>().runtimeAnimatorController = controller;

            var ex = Assert.Throws<System.InvalidOperationException>(() => SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body)));
            StringAssert.Contains("Submesh_1", ex.Message);
        }

        [Test]
        public void IsolationMovesPartsOutOfTheAvatarAndRestoresThemInPlace()
        {
            var container = _splitter.generatedRoot;
            int sibling = container.GetSiblingIndex();
            SplitBuildIsolation.Hide(_avatar);
            Assert.That(container.IsChildOf(_avatar.transform), Is.False);
            Assert.That(_avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.EqualTo(new[] { _body }));
            SplitBuildIsolation.Restore(_avatar);
            Assert.That(container.parent, Is.SameAs(_body.transform));
            Assert.That(container.GetSiblingIndex(), Is.EqualTo(sibling));
            Assert.That(SplitBuildIsolation.TakeWeightsAtHide(_body), Is.Not.Null);
        }

        [Test]
        public void PartsFollowBonesRebindOnTheOriginalWhileIsolated()
        {
            // Simulates VRCFury Armature Link: while the parts are away, the original's bone is
            // replaced by an avatar bone (with an adjusted bindpose) and the old bone is deleted.
            var oldBone = _body.bones[0];
            SplitBuildIsolation.Hide(_avatar);
            var avatarBone = new GameObject("AvatarHips").transform;
            avatarBone.SetParent(_avatar.transform, false);
            avatarBone.localPosition = new Vector3(0, 1, 0);
            var rebound = Track(Object.Instantiate(_source));
            rebound.bindposes = new[] { Matrix4x4.Translate(new Vector3(0, -1, 0)) };
            _body.sharedMesh = rebound;
            _body.bones = new[] { avatarBone };
            Object.DestroyImmediate(oldBone.gameObject);
            SplitBuildIsolation.Restore(_avatar);
            SplitBuildIsolation.TakeWeightsAtHide(_body);

            foreach (var part in _splitter.generatedRenderers)
            {
                Assert.That(part.bones[0], Is.SameAs(avatarBone));
                Assert.That(part.sharedMesh.bindposes[0], Is.EqualTo(rebound.bindposes[0]));
            }
            SplitReassembly.Run(_splitter, _avatar, SourceSnapshot.Take(_body));
            Assert.That(_body.bones, Is.EqualTo(new[] { avatarBone }), "no missing bones after reassembly");
        }

        [Test]
        public void FingerprintIgnoresIdentityButNotContent()
        {
            var copy = Track(Object.Instantiate(_source));
            Assert.That(SplitSourceInfo.ComputeFingerprint(copy), Is.EqualTo(SplitSourceInfo.ComputeFingerprint(_source)));
            var moved = _source.vertices;
            moved[0] += Vector3.up;
            copy.vertices = moved;
            Assert.That(SplitSourceInfo.ComputeFingerprint(copy), Is.Not.EqualTo(SplitSourceInfo.ComputeFingerprint(_source)));
        }

        [Test]
        public void SharedVerticesAreCounted()
        {
            Assert.That(SplitSourceInfo.CountSharedVertices(_source), Is.EqualTo(0));
            var shared = Track(Object.Instantiate(_source));
            shared.SetTriangles(new[] { 0, 4, 5 }, 1);
            Assert.That(SplitSourceInfo.CountSharedVertices(shared), Is.EqualTo(1));
        }

        [Test]
        public void StaleSourceIsReported()
        {
            _splitter.assetData = Track(ScriptableObject.CreateInstance<NordicsSubmeshAssetData>());
            _splitter.assetData.meshes.AddRange(_splitter.generatedRenderers.Select(r => r.sharedMesh));
            _splitter.assetData.sourceFingerprint = SplitSourceInfo.ComputeFingerprint(_source);
            Assert.That(SplitMeshGenerator.ValidateGenerated(_splitter), Is.Null);

            var updated = Track(Object.Instantiate(_source));
            var moved = updated.vertices;
            moved[0] += Vector3.up;
            updated.vertices = moved;
            _body.sharedMesh = updated;
            StringAssert.Contains("changed", SplitMeshGenerator.ValidateGenerated(_splitter));
        }

        [Test]
        public void MultiFrameShapeIsInterpolatedBetweenFrames()
        {
            var mesh = Track(Object.Instantiate(_source));
            mesh.ClearBlendShapes();
            mesh.AddBlendShapeFrame("Two", 50f, Deltas(0, 0, 1), null, null);
            mesh.AddBlendShapeFrame("Two", 100f, Deltas(0, 0, 3), null, null);
            SplitMeshReassembler.EvaluateShape(mesh, 0, 75f, out var dv, out _, out _);
            Assert.That(dv[0].z, Is.EqualTo(2f).Within(1e-5f));
            SplitMeshReassembler.EvaluateShape(mesh, 0, 25f, out dv, out _, out _);
            Assert.That(dv[0].z, Is.EqualTo(0.5f).Within(1e-5f));
        }

        private static Vector3[] Deltas(float x, float y, float z) => Enumerable.Repeat(new Vector3(x, y, z), 6).ToArray();

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
