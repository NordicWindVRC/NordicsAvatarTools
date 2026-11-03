using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace NordicWind.SubmeshSplitter.Editor
{
    // VRChat SDK builds (and VRCFury Play Mode, which runs the same callback chain) call
    // non-NDMF processors between NDMF Transforming (-11000) and NDMF Optimizing (-1025),
    // most notably VRCFury at -10000. Split parts are an editing aid that only explicit
    // per-part actions should see, so they are moved out of the avatar for that window
    // and put back unchanged before reassembly. Nothing else is touched: no other tool's
    // code or data is modified.
    internal static class SplitBuildIsolation
    {
        internal const int HideOrder = -10500;
        internal const int RestoreOrder = -1030;

        private sealed class Record
        {
            internal Transform Container;
            internal Transform Parent;
            internal int SiblingIndex;
            internal SkinnedMeshRenderer Source;
            // Per part: bone slots that matched the original renderer when the parts left.
            internal readonly List<(SkinnedMeshRenderer part, bool[] shared)> Parts = new List<(SkinnedMeshRenderer, bool[])>();
        }

        private static readonly Dictionary<int, (GameObject holder, List<Record> records)> Isolated =
            new Dictionary<int, (GameObject, List<Record>)>();

        // Blendshape weights of each original renderer just before other tools run.
        // A general tool (e.g. VRCFury Blendshape Optimizer) may remove shapes from the
        // original; reassembly bakes those shapes at the weight they had at this point.
        private static readonly Dictionary<SkinnedMeshRenderer, Dictionary<string, float>> WeightsAtHide =
            new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();

        internal static void Hide(GameObject avatar)
        {
            if (avatar == null) return;
            Restore(avatar); // never stack two isolations of the same avatar
            var records = new List<Record>();
            foreach (var splitter in avatar.GetComponentsInChildren<NordicsSubmeshSplitter>(true))
            {
                var container = splitter.generatedRoot;
                if (container == null || !container.IsChildOf(avatar.transform)) continue;
                var source = splitter.GetComponent<SkinnedMeshRenderer>();
                var record = new Record { Container = container, Parent = container.parent, SiblingIndex = container.GetSiblingIndex(), Source = source };
                if (source != null && source.sharedMesh != null)
                    foreach (var part in splitter.generatedRenderers)
                        if (part != null && part.sharedMesh != null) record.Parts.Add((part, SharedSlots(source, part)));
                records.Add(record);
                if (source != null && source.sharedMesh != null)
                {
                    var weights = new Dictionary<string, float>();
                    for (int i = 0; i < source.sharedMesh.blendShapeCount; i++)
                        weights[source.sharedMesh.GetBlendShapeName(i)] = source.GetBlendShapeWeight(i);
                    WeightsAtHide[source] = weights;
                }
            }
            if (records.Count == 0) return;
            var holder = new GameObject("Nordic's Submesh Splitter (parts isolated during build)")
                { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            holder.SetActive(false);
            foreach (var record in records) record.Container.SetParent(holder.transform, false);
            Isolated[avatar.GetInstanceID()] = (holder, records);
        }

        internal static void Restore(GameObject avatar)
        {
            if (avatar == null || !Isolated.TryGetValue(avatar.GetInstanceID(), out var entry)) return;
            Isolated.Remove(avatar.GetInstanceID());
            foreach (var record in entry.records)
            {
                if (record.Container == null || record.Parent == null) continue;
                record.Container.SetParent(record.Parent, false);
                record.Container.SetSiblingIndex(record.SiblingIndex);
                SyncSharedBones(record);
            }
            if (entry.holder != null) Object.DestroyImmediate(entry.holder);
        }

        // A part starts with the original renderer's bones and bindposes. Tools that run
        // while the parts are away (e.g. VRCFury Armature Link) rebind the original's bones
        // to the avatar armature, adjust its bindposes, and may delete the old bones. Bone
        // slots the part still shared with the original when it left follow those changes.
        private static void SyncSharedBones(Record record)
        {
            var source = record.Source;
            if (source == null || source.sharedMesh == null) return;
            var sourceBones = source.bones;
            var sourceBindposes = source.sharedMesh.bindposes;
            foreach (var (part, shared) in record.Parts)
            {
                if (part == null || part.sharedMesh == null) continue;
                var bones = part.bones;
                var bindposes = part.sharedMesh.bindposes;
                bool changed = false;
                int count = Mathf.Min(shared.Length, Mathf.Min(bones.Length, bindposes.Length));
                for (int i = 0; i < count && i < sourceBones.Length && i < sourceBindposes.Length; i++)
                {
                    if (!shared[i] || (bones[i] == sourceBones[i] && bindposes[i] == sourceBindposes[i])) continue;
                    bones[i] = sourceBones[i];
                    bindposes[i] = sourceBindposes[i];
                    changed = true;
                }
                if (!changed) continue;
                // Generated meshes are assets; edit a build-time copy.
                var mesh = Object.Instantiate(part.sharedMesh);
                mesh.name = part.sharedMesh.name;
                mesh.bindposes = bindposes;
                part.sharedMesh = mesh;
                part.bones = bones;
            }
        }

        internal static bool[] SharedSlots(SkinnedMeshRenderer source, SkinnedMeshRenderer part)
        {
            var sourceBones = source.bones; var partBones = part.bones;
            var sourceBindposes = source.sharedMesh.bindposes; var partBindposes = part.sharedMesh.bindposes;
            var shared = new bool[partBones.Length];
            for (int i = 0; i < shared.Length; i++)
                shared[i] = i < sourceBones.Length && i < sourceBindposes.Length && i < partBindposes.Length
                            && partBones[i] == sourceBones[i] && partBindposes[i] == sourceBindposes[i];
            return shared;
        }

        internal static Dictionary<string, float> TakeWeightsAtHide(SkinnedMeshRenderer source)
        {
            if (source == null || !WeightsAtHide.TryGetValue(source, out var weights)) return null;
            WeightsAtHide.Remove(source);
            return weights;
        }

        internal sealed class HideCallback : IVRCSDKPreprocessAvatarCallback
        {
            public int callbackOrder => HideOrder;
            public bool OnPreprocessAvatar(GameObject avatarGameObject) { Hide(avatarGameObject); return true; }
        }

        internal sealed class RestoreCallback : IVRCSDKPreprocessAvatarCallback
        {
            public int callbackOrder => RestoreOrder;
            public bool OnPreprocessAvatar(GameObject avatarGameObject) { Restore(avatarGameObject); return true; }
        }
    }
}
