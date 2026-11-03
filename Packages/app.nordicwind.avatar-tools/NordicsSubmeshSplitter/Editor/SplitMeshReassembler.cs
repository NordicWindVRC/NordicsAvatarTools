using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Which blendshapes the merged mesh gets, in which order, and which are baked.
    internal sealed class BlendshapePlan
    {
        // The original renderer's current blendshape names. They come first, in this
        // order, so index-based references (descriptor eyelids) stay valid. A name no part
        // provides gets an empty placeholder shape.
        internal readonly List<string> Authority = new List<string>();
        // Shapes a general tool removed from the original during the build: baked into the
        // geometry at the given weight and left out of the merged mesh.
        internal readonly Dictionary<string, float> Bake = new Dictionary<string, float>(StringComparer.Ordinal);
        // Shapes another tool renamed on the original: merged name → name in the parts.
        internal readonly Dictionary<string, string> PartNames = new Dictionary<string, string>(StringComparer.Ordinal);

        internal string PartName(string name) => PartNames.TryGetValue(name, out var partName) ? partName : name;

        internal static BlendshapePlan FromMesh(Mesh mesh)
        {
            var plan = new BlendshapePlan();
            for (int i = 0; i < mesh.blendShapeCount; i++) plan.Authority.Add(mesh.GetBlendShapeName(i));
            return plan;
        }
    }

    // Pure build-clone mesh operation. Does not depend on AAO, Meshia, MA or VRCFury APIs.
    internal static class SplitMeshReassembler
    {
        private sealed class Part
        {
            internal Mesh mesh;
            internal Matrix4x4 toTarget;
            internal int offset;
            internal int[] boneRemap;
            // New vertex index -> original vertex index, in deterministic ascending order.
            internal int[] keptVertices;
        }

        internal static Mesh Merge(SkinnedMeshRenderer target, SkinnedMeshRenderer[] sources,
            Mesh original, out Transform[] mergedBones)
            => Merge(target, sources, original, BlendshapePlan.FromMesh(original), out mergedBones);

        internal static Mesh Merge(SkinnedMeshRenderer target, SkinnedMeshRenderer[] sources,
            Mesh original, BlendshapePlan plan, out Transform[] mergedBones)
        {
            var parts = new List<Part>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uvs = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
            var uvDimensions = new int[8];
            var triangles = new List<int[]>();
            var bones = new List<Transform>();
            var bindposes = new List<Matrix4x4>();
            var counts = new List<byte>();
            var weights = new List<BoneWeight1>();
            // Keep the original renderer's bone layout wherever it is still compatible.
            // Additional entries are introduced only for genuinely new bone/bindpose pairs.
            var originalBones = target.bones;
            var originalBindposes = original.bindposes;
            if (originalBones.Length != originalBindposes.Length)
                throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: original bone/bindpose mismatch.");
            bones.AddRange(originalBones);
            bindposes.AddRange(originalBindposes);
            var targetWorldToLocal = target.transform.worldToLocalMatrix;
            foreach (var renderer in sources)
            {
                var mesh = renderer.sharedMesh;
                if (!mesh.isReadable)
                    throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: unreadable processed mesh " + mesh.name);
                if (mesh.vertexCount == 0 || mesh.subMeshCount == 0)
                    throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: empty processed mesh " + mesh.name);
                // Build a single mapping across every submesh in this processed mesh.
                // Do not weld positions: seams, hard normals, and per-vertex skinning
                // must remain independent.
                var used = new bool[mesh.vertexCount];
                var sourceIndices = new int[mesh.subMeshCount][];
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                        throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: non-triangle submesh in " + mesh.name);
                    var indices = mesh.GetIndices(sub, true);
                    if (indices.Length % 3 != 0)
                        throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: incomplete triangles in " + mesh.name);
                    foreach (var index in indices)
                    {
                        if (index < 0 || index >= mesh.vertexCount)
                            throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: out-of-range index in " + mesh.name);
                        used[index] = true;
                    }
                    sourceIndices[sub] = indices;
                }
                var kept = new List<int>();
                var oldToNew = new int[mesh.vertexCount];
                for (int i = 0; i < oldToNew.Length; i++) oldToNew[i] = -1;
                for (int i = 0; i < used.Length; i++)
                    if (used[i])
                    {
                        oldToNew[i] = kept.Count;
                        kept.Add(i);
                    }
                var part = new Part { mesh = mesh, offset = vertices.Count,
                    keptVertices = kept.ToArray(),
                    toTarget = targetWorldToLocal * renderer.transform.localToWorldMatrix };
                parts.Add(part);
                var m = part.toTarget;
                var normalMatrix = m.inverse.transpose;
                var srcVertices = mesh.vertices;
                var srcNormals = mesh.normals;
                var srcTangents = mesh.tangents;
                var srcColors = mesh.colors;
                foreach (int i in part.keptVertices)
                {
                    vertices.Add(m.MultiplyPoint3x4(srcVertices[i]));
                    normals.Add(srcNormals.Length == mesh.vertexCount ?
                        normalMatrix.MultiplyVector(srcNormals[i]).normalized : Vector3.zero);
                    if (srcTangents.Length == mesh.vertexCount)
                    {
                        var t = srcTangents[i];
                        var v = m.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                        tangents.Add(new Vector4(v.x, v.y, v.z, t.w * (m.determinant < 0 ? -1 : 1)));
                    }
                    else tangents.Add(Vector4.zero);
                    colors.Add(srcColors.Length == mesh.vertexCount ? srcColors[i] : Color.white);
                }
                for (int channel = 0; channel < 8; channel++)
                {
                    var srcUv = SplitUVChannels.Read(mesh, channel);
                    int dimension = SplitUVChannels.Dimension(mesh, channel);
                    uvDimensions[channel] = Math.Max(uvDimensions[channel], dimension);
                    foreach (int i in part.keptVertices)
                        uvs[channel].Add(srcUv != null ? srcUv[i] : Vector4.zero);
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var indices = sourceIndices[sub];
                    for (int i = 0; i < indices.Length; i++)
                        indices[i] = part.offset + oldToNew[indices[i]];
                    // Reverse winding if the relative transform reflects handedness.
                    if (m.determinant < 0)
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        { int tmp = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = tmp; }
                    triangles.Add(indices);
                }
                var rendererBones = renderer.bones;
                var sourceBindposes = mesh.bindposes;
                if (rendererBones.Length != sourceBindposes.Length)
                    throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: bone/bindpose mismatch in " + mesh.name);
                // Map each source index to an output index. Match by Transform identity
                // AND effective bindpose; names cannot distinguish identical references.
                // Prefer the original slot when possible to preserve its layout.
                var remap = new int[rendererBones.Length];
                for (int i = 0; i < rendererBones.Length; i++)
                {
                    var bone = rendererBones[i];
                    var effectiveBindpose = sourceBindposes[i] * m.inverse;
                    int match = -1;
                    if (i < originalBones.Length &&
                        originalBones[i] == bone && BindposesEquivalent(originalBindposes[i], effectiveBindpose))
                        match = i;
                    if (match < 0)
                        for (int j = 0; j < bones.Count; j++)
                            if (bones[j] == bone && BindposesEquivalent(bindposes[j], effectiveBindpose))
                            { match = j; break; }
                    if (match < 0)
                    {
                        match = bones.Count;
                        bones.Add(bone);
                        bindposes.Add(effectiveBindpose);
                    }
                    remap[i] = match;
                    if (bones[match] != bone || !BindposesEquivalent(bindposes[match], effectiveBindpose))
                        throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: bone remap validation failed in " + mesh.name);
                }
                part.boneRemap = remap;
                using (var sourceCounts = mesh.GetBonesPerVertex())
                using (var sourceWeights = mesh.GetAllBoneWeights())
                {
                    if (sourceCounts.Length != mesh.vertexCount)
                        throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: invalid skin weights in " + mesh.name);
                    int[] weightStarts = new int[mesh.vertexCount];
                    int cursor = 0;
                    for (int i = 0; i < sourceCounts.Length; i++)
                    {
                        weightStarts[i] = cursor;
                        cursor += sourceCounts[i];
                    }
                    if (cursor != sourceWeights.Length)
                        throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: invalid flattened bone weights in " + mesh.name);
                    foreach (int oldIndex in part.keptVertices)
                    {
                        int count = sourceCounts[oldIndex];
                        counts.Add((byte)count);
                        for (int j = 0; j < count; j++)
                        {
                            var w = sourceWeights[weightStarts[oldIndex] + j];
                            if (w.boneIndex < 0 || w.boneIndex >= rendererBones.Length)
                                throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: invalid bone index in " + mesh.name);
                            w.boneIndex = part.boneRemap[w.boneIndex];
                            weights.Add(w);
                        }
                    }
                }
            }
            if (vertices.Count == 0) throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: no vertices to merge.");
            ApplyBakes(parts, plan, vertices, normals, tangents);
            var result = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            result.SetVertices(vertices);
            result.SetNormals(normals);
            result.SetTangents(tangents);
            result.SetColors(colors);
            for (int channel = 0; channel < 8; channel++)
                if (uvDimensions[channel] != 0)
                    SplitUVChannels.Write(result, channel, uvs[channel], uvDimensions[channel]);
            result.subMeshCount = triangles.Count;
            for (int sub = 0; sub < triangles.Count; sub++) result.SetTriangles(triangles[sub], sub, false);
            result.bindposes = bindposes.ToArray();
            using (var c = new NativeArray<byte>(counts.ToArray(), Allocator.Temp))
            using (var w = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                result.SetBoneWeights(c, w);
            RestoreBlendshapes(parts, plan, original, result);
            result.RecalculateBounds();
            mergedBones = bones.ToArray();
            return result;
        }

        // Matrices are in target-local coordinates at this point. Relative tolerance
        // accounts for small floating-point differences without merging distinct poses.
        private static bool BindposesEquivalent(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                float av = a[i], bv = b[i];
                if (float.IsNaN(av) || float.IsNaN(bv) ||
                    float.IsInfinity(av) || float.IsInfinity(bv)) return false;
                if (Mathf.Abs(av - bv) > 0.00001f * Mathf.Max(1f, Mathf.Abs(av), Mathf.Abs(bv)))
                    return false;
            }
            return true;
        }

        // Bakes shapes that a general tool removed from the original renderer into the
        // merged geometry, at the weight the original renderer had for them.
        private static void ApplyBakes(List<Part> parts, BlendshapePlan plan,
            List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents)
        {
            bool any = false;
            foreach (var bake in plan.Bake)
            {
                if (Mathf.Approximately(bake.Value, 0f)) continue;
                foreach (var part in parts)
                {
                    int index = part.mesh.GetBlendShapeIndex(bake.Key);
                    if (index < 0) continue;
                    EvaluateShape(part.mesh, index, bake.Value, out var dv, out var dn, out var dt);
                    var normalMatrix = part.toTarget.inverse.transpose;
                    for (int i = 0; i < part.keptVertices.Length; i++)
                    {
                        int oldIndex = part.keptVertices[i], target = part.offset + i;
                        vertices[target] += part.toTarget.MultiplyVector(dv[oldIndex]);
                        normals[target] += normalMatrix.MultiplyVector(dn[oldIndex]);
                        var tangentDelta = part.toTarget.MultiplyVector(dt[oldIndex]);
                        var t = tangents[target];
                        tangents[target] = new Vector4(t.x + tangentDelta.x, t.y + tangentDelta.y, t.z + tangentDelta.z, t.w);
                    }
                    any = true;
                }
            }
            if (!any) return;
            for (int i = 0; i < normals.Count; i++)
                if (normals[i] != Vector3.zero) normals[i] = normals[i].normalized;
        }

        // Deltas of a blendshape at a weight, interpolating between frames the way a
        // SkinnedMeshRenderer does: below the first frame the first frame is scaled from
        // zero; between frames they are interpolated; above the last frame the last two
        // frames are extrapolated (a single frame is scaled linearly).
        internal static void EvaluateShape(Mesh mesh, int shape, float weight,
            out Vector3[] dv, out Vector3[] dn, out Vector3[] dt)
        {
            int n = mesh.vertexCount, frames = mesh.GetBlendShapeFrameCount(shape);
            var (av, an, at) = Frame(mesh, shape, 0);
            float w0 = mesh.GetBlendShapeFrameWeight(shape, 0);
            if (frames == 1 || weight <= w0)
            {
                float k = Mathf.Approximately(w0, 0f) ? 0f : weight / w0;
                dv = Scale(av, k); dn = Scale(an, k); dt = Scale(at, k);
                return;
            }
            int upper = 1;
            while (upper < frames - 1 && mesh.GetBlendShapeFrameWeight(shape, upper) < weight) upper++;
            float wa = mesh.GetBlendShapeFrameWeight(shape, upper - 1), wb = mesh.GetBlendShapeFrameWeight(shape, upper);
            if (upper > 1) (av, an, at) = Frame(mesh, shape, upper - 1);
            var (bv, bn, bt) = Frame(mesh, shape, upper);
            float f = Mathf.Approximately(wa, wb) ? 1f : (weight - wa) / (wb - wa);
            dv = new Vector3[n]; dn = new Vector3[n]; dt = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                dv[i] = av[i] + (bv[i] - av[i]) * f;
                dn[i] = an[i] + (bn[i] - an[i]) * f;
                dt[i] = at[i] + (bt[i] - at[i]) * f;
            }
        }

        private static (Vector3[], Vector3[], Vector3[]) Frame(Mesh mesh, int shape, int frame)
        {
            int n = mesh.vertexCount;
            var v = new Vector3[n]; var nn = new Vector3[n]; var t = new Vector3[n];
            mesh.GetBlendShapeFrameVertices(shape, frame, v, nn, t);
            return (v, nn, t);
        }

        private static Vector3[] Scale(Vector3[] values, float k)
        {
            var result = new Vector3[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i] * k;
            return result;
        }

        private static void RestoreBlendshapes(List<Part> parts, BlendshapePlan plan, Mesh original, Mesh result)
        {
            // The original renderer's names first, in its order; then shapes that explicit
            // part actions added. Baked shapes are left out.
            var orderedNames = new List<string>(plan.Authority);
            var seen = new HashSet<string>(orderedNames, StringComparer.Ordinal);
            seen.UnionWith(plan.PartNames.Values); // old names of renamed shapes are not added again
            foreach (var part in parts)
                for (int i = 0; i < part.mesh.blendShapeCount; i++)
                {
                    string name = part.mesh.GetBlendShapeName(i);
                    if (!plan.Bake.ContainsKey(name) && seen.Add(name)) orderedNames.Add(name);
                }
            foreach (var name in orderedNames)
            {
                var frameWeights = new List<float>();
                string partName = plan.PartName(name);
                var reference = original != null && original.GetBlendShapeIndex(name) >= 0 ? original
                    : parts.Select(p => p.mesh).FirstOrDefault(m => m.GetBlendShapeIndex(partName) >= 0);
                if (reference != null)
                {
                    int index = reference.GetBlendShapeIndex(reference == original ? name : partName);
                    for (int f = 0; f < reference.GetBlendShapeFrameCount(index); f++)
                        frameWeights.Add(reference.GetBlendShapeFrameWeight(index, f));
                }
                // No part provides this shape (a processor dropped it from every part):
                // keep an empty placeholder so later indices do not shift.
                if (frameWeights.Count == 0) frameWeights.Add(100f);
                foreach (var part in parts)
                {
                    int index = part.mesh.GetBlendShapeIndex(partName);
                    if (index < 0) continue;
                    if (part.mesh.GetBlendShapeFrameCount(index) != frameWeights.Count)
                        throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: frame count changed for " + name + " in " + part.mesh.name);
                    for (int f = 0; f < frameWeights.Count; f++)
                        if (Mathf.Abs(part.mesh.GetBlendShapeFrameWeight(index, f) - frameWeights[f]) > 0.001f)
                            throw new InvalidOperationException("[Nordic's Submesh Splitter] Reassembly: frame weights changed for " + name + " in " + part.mesh.name);
                }
                for (int f = 0; f < frameWeights.Count; f++)
                {
                    var dv = new Vector3[result.vertexCount];
                    var dn = new Vector3[result.vertexCount];
                    var dt = new Vector3[result.vertexCount];
                    foreach (var part in parts)
                    {
                        int index = part.mesh.GetBlendShapeIndex(partName);
                        if (index < 0) continue;
                        var (sv, sn, st) = Frame(part.mesh, index, f);
                        var normalMatrix = part.toTarget.inverse.transpose;
                        for (int i = 0; i < part.keptVertices.Length; i++)
                        {
                            int oldIndex = part.keptVertices[i];
                            dv[part.offset + i] = part.toTarget.MultiplyVector(sv[oldIndex]);
                            dn[part.offset + i] = normalMatrix.MultiplyVector(sn[oldIndex]);
                            dt[part.offset + i] = part.toTarget.MultiplyVector(st[oldIndex]);
                        }
                    }
                    result.AddBlendShapeFrame(name, frameWeights[f], dv, dn, dt);
                }
            }
        }
    }
}
