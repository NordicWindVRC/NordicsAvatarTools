using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Facts about a source mesh that the inspector and the build both need.
    // The fingerprint covers mesh content only (geometry, skinning, blendshapes,
    // submesh layout), never scene/prefab identity, so it is identical for the
    // prefab asset, its variants, scene instances and the NDMF build clone.
    // Results are cached per mesh instance and dirty count, and each value is only
    // computed when asked for, so drawing the inspector stays cheap.
    internal static class SplitSourceInfo
    {
        private static readonly Dictionary<int, (int dirty, string value)> Fingerprints = new Dictionary<int, (int, string)>();
        private static readonly Dictionary<int, (int dirty, int value)> SharedCounts = new Dictionary<int, (int, int)>();

        internal static string Fingerprint(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable) return null;
            int id = mesh.GetInstanceID(), dirty = EditorUtility.GetDirtyCount(mesh);
            if (Fingerprints.TryGetValue(id, out var entry) && entry.dirty == dirty) return entry.value;
            string value = ComputeFingerprint(mesh);
            Fingerprints[id] = (dirty, value);
            return value;
        }

        internal static int SharedVertexCount(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable) return 0;
            int id = mesh.GetInstanceID(), dirty = EditorUtility.GetDirtyCount(mesh);
            if (SharedCounts.TryGetValue(id, out var entry) && entry.dirty == dirty) return entry.value;
            int value = CountSharedVertices(mesh);
            SharedCounts[id] = (dirty, value);
            return value;
        }

        // A change-detection hash, not a security measure: whole arrays are hashed as raw
        // memory with a 64-bit multiply-xor mix, which is fast even for meshes with
        // hundreds of blendshapes.
        internal static string ComputeFingerprint(Mesh mesh)
        {
            var h = new Hasher();
            h.Add(mesh.vertexCount); h.Add(mesh.subMeshCount);
            foreach (var attr in mesh.GetVertexAttributes()) { h.Add((int)attr.attribute); h.Add(attr.dimension); }
            h.Add(mesh.vertices); h.Add(mesh.normals); h.Add(mesh.tangents); h.Add(mesh.colors);
            var uvs = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                mesh.GetUVs(channel, uvs);
                h.Add(uvs.Count);
                h.Add(uvs.ToArray());
            }
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                h.Add((int)mesh.GetTopology(i));
                var indices = mesh.GetIndices(i);
                h.Add(indices.Length);
                h.Add(indices);
            }
            h.Add(mesh.bindposes);
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                h.Add(counts.ToArray());
                h.Add(weights.ToArray());
            }
            h.Add(mesh.blendShapeCount);
            var dv = new Vector3[mesh.vertexCount]; var dn = new Vector3[mesh.vertexCount]; var dt = new Vector3[mesh.vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                h.Add(mesh.GetBlendShapeName(shape));
                int frames = mesh.GetBlendShapeFrameCount(shape);
                h.Add(frames);
                for (int frame = 0; frame < frames; frame++)
                {
                    h.Add(mesh.GetBlendShapeFrameWeight(shape, frame));
                    mesh.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                    h.Add(dv); h.Add(dn); h.Add(dt);
                }
            }
            return h.Value.ToString("x16");
        }

        // Vertices referenced by more than one submesh. Each part keeps its own copy,
        // so per-part processing can move the copies apart and open seams.
        internal static int CountSharedVertices(Mesh mesh)
        {
            var owner = new Dictionary<int, int>();
            var shared = new HashSet<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                foreach (int index in mesh.GetIndices(sub))
                {
                    if (!owner.TryGetValue(index, out int first)) owner[index] = sub;
                    else if (first != sub) shared.Add(index);
                }
            return shared.Count;
        }

        private struct Hasher
        {
            private const ulong Prime = 0x100000001B3UL;
            private ulong _h;
            private bool _started;

            internal ulong Value => _started ? _h : 0xCBF29CE484222325UL;

            private void Mix(ulong word)
            {
                if (!_started) { _h = 0xCBF29CE484222325UL; _started = true; }
                _h = (_h ^ word) * Prime;
                _h ^= _h >> 29;
            }

            internal void Add(int value) => Mix((ulong)(uint)value);
            internal void Add(float value) => Mix(BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));

            internal void Add(string value)
            {
                Add(value.Length);
                foreach (char c in value) Mix(c);
            }

            internal void Add<T>(T[] values) where T : unmanaged
            {
                Add(values.Length);
                var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(values));
                var words = MemoryMarshal.Cast<byte, ulong>(bytes);
                foreach (var word in words) Mix(word);
                for (int i = words.Length * 8; i < bytes.Length; i++) Mix(bytes[i]);
            }
        }
    }
}
