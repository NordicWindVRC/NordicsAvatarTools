using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Where a source material's triangles go: its region of the atlas, and the output material.
    public sealed class AtlasRemapTarget
    {
        public Rect Region;
        public Material Output;
    }

    public sealed class AtlasRemapResult
    {
        public Mesh Mesh;
        public Material[] Materials;
        // Triangles that straddled a UV tile edge and were clamped, per source material.
        public readonly Dictionary<Material, int> Clamped = new Dictionary<Material, int>();
        // Vertices added by splitting.
        public int SplitVertices;
    }

    // The mesh remapper (ATLAS_OUTPUT.md, "Mesh remapper"): a new mesh with each source's UV0
    // moved into its region, vertices split where their triangles need different UVs, the
    // source materials swapped for the output materials, and the submeshes that then share a
    // material merged. The original mesh is never changed.
    public static class AtlasMeshRemapper
    {
        // Who a vertex is for: a group (an output region, or -1 for triangles left as they are)
        // and how its triangle brings its UV into 0–1.
        private struct VertexKey : IEquatable<VertexKey>
        {
            public int Vertex, Group, TileX, TileY;
            public bool Clamp;

            public bool Equals(VertexKey o) =>
                Vertex == o.Vertex && Group == o.Group && TileX == o.TileX && TileY == o.TileY && Clamp == o.Clamp;

            public override int GetHashCode() =>
                ((((Vertex * 397) ^ Group) * 397 ^ TileX) * 397 ^ TileY) * 2 + (Clamp ? 1 : 0);
        }

        private struct VertexUse
        {
            public AtlasRemapTarget Target;
            public Vector2 Shift;
            public bool Clamp;
        }

        // Null when no submesh of the mesh uses a source material.
        public static AtlasRemapResult Remap(Mesh mesh, Material[] materials, IReadOnlyDictionary<Material, AtlasRemapTarget> targets)
        {
            if (mesh == null || materials == null || targets == null) return null;
            int subMeshCount = mesh.subMeshCount;
            var submeshTargets = new AtlasRemapTarget[subMeshCount];
            var groups = new Dictionary<AtlasRemapTarget, int>();
            for (int s = 0; s < Math.Min(subMeshCount, materials.Length); s++)
            {
                if (materials[s] == null || mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                if (!targets.TryGetValue(materials[s], out var target) || target == null) continue;
                submeshTargets[s] = target;
                if (!groups.ContainsKey(target)) groups[target] = groups.Count;
            }
            if (groups.Count == 0) return null;

            int vertexCount = mesh.vertexCount;
            var result = new AtlasRemapResult();
            var uv0 = new List<Vector4>();
            mesh.GetUVs(0, uv0);
            if (uv0.Count != vertexCount)
            {
                uv0.Clear();
                for (int i = 0; i < vertexCount; i++) uv0.Add(Vector4.zero);
            }

            // Each vertex keeps its index for its first user; every other user gets a copy.
            var origin = new List<int>(vertexCount);
            var uses = new List<VertexUse>(vertexCount);
            var firstKey = new VertexKey?[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                origin.Add(i);
                uses.Add(default);
            }
            var copies = new Dictionary<VertexKey, int>();
            int Resolve(VertexKey key, VertexUse use)
            {
                int v = key.Vertex;
                if (firstKey[v] == null)
                {
                    firstKey[v] = key;
                    uses[v] = use;
                    return v;
                }
                if (firstKey[v].Value.Equals(key)) return v;
                if (copies.TryGetValue(key, out int copy)) return copy;
                copy = origin.Count;
                origin.Add(v);
                uses.Add(use);
                copies[key] = copy;
                return copy;
            }

            var indices = new int[subMeshCount][];
            for (int s = 0; s < subMeshCount; s++)
            {
                var source = mesh.GetIndices(s);
                var mapped = new int[source.Length];
                var target = submeshTargets[s];
                if (target == null)
                {
                    for (int i = 0; i < source.Length; i++)
                        mapped[i] = Resolve(new VertexKey { Vertex = source[i], Group = -1 }, default);
                }
                else
                {
                    int group = groups[target];
                    for (int i = 0; i + 2 < source.Length; i += 3)
                    {
                        Vector2 a = uv0[source[i]], b = uv0[source[i + 1]], c = uv0[source[i + 2]];
                        // A triangle within one UV tile moves into 0–1 by whole tiles; one that
                        // straddles a tile edge is clamped.
                        int tileX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
                        int tileY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
                        bool clamp = !Within(a, b, c, tileX, tileY);
                        if (clamp)
                        {
                            tileX = tileY = 0;
                            result.Clamped.TryGetValue(materials[s], out int count);
                            result.Clamped[materials[s]] = count + 1;
                        }
                        var use = new VertexUse { Target = target, Shift = new Vector2(-tileX, -tileY), Clamp = clamp };
                        for (int k = 0; k < 3; k++)
                            mapped[i + k] = Resolve(new VertexKey { Vertex = source[i + k], Group = group, TileX = tileX, TileY = tileY, Clamp = clamp }, use);
                    }
                }
                indices[s] = mapped;
            }
            result.SplitVertices = origin.Count - vertexCount;
            result.Mesh = Build(mesh, origin, uses, indices, materials, submeshTargets, out result.Materials);
            return result;
        }

        // Every vertex of the triangle within tile (x, y), its far edges included.
        private static bool Within(Vector2 a, Vector2 b, Vector2 c, int x, int y)
        {
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            return maxX <= x + 1 && maxY <= y + 1;
        }

        private static Mesh Build(Mesh mesh, List<int> origin, List<VertexUse> uses, int[][] indices,
            Material[] materials, AtlasRemapTarget[] submeshTargets, out Material[] newMaterials)
        {
            var result = new Mesh
            {
                name = mesh.name + " (Atlas)",
                indexFormat = origin.Count > 65535 ? IndexFormat.UInt32 : mesh.indexFormat
            };
            result.SetVertices(Expand(mesh.vertices, origin));
            if (mesh.HasVertexAttribute(VertexAttribute.Normal)) result.SetNormals(Expand(mesh.normals, origin));
            if (mesh.HasVertexAttribute(VertexAttribute.Tangent)) result.SetTangents(Expand(mesh.tangents, origin));
            if (mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                    result.SetColors(Expand(mesh.colors32, origin));
                else
                    result.SetColors(Expand(mesh.colors, origin));
            }
            for (int channel = 0; channel < 8; channel++)
                CopyUVs(mesh, result, channel, origin, channel == 0 ? uses : null);
            CopyBoneWeights(mesh, result, origin);
            result.bindposes = mesh.bindposes;
            CopyBlendShapes(mesh, result, origin);

            // Submeshes: those that now share an output material become one, where the first of
            // them was; the others keep their order.
            var parts = new List<(Material Material, List<int> Indices, MeshTopology Topology)>();
            var merged = new Dictionary<Material, int>();
            for (int s = 0; s < indices.Length; s++)
            {
                var material = s < materials.Length ? materials[s] : null;
                var target = submeshTargets[s];
                if (target != null)
                {
                    if (merged.TryGetValue(target.Output, out int at))
                    {
                        parts[at].Indices.AddRange(indices[s]);
                        continue;
                    }
                    merged[target.Output] = parts.Count;
                    material = target.Output;
                }
                parts.Add((material, new List<int>(indices[s]), mesh.GetTopology(s)));
            }
            result.subMeshCount = parts.Count;
            for (int i = 0; i < parts.Count; i++)
                result.SetIndices(parts[i].Indices, parts[i].Topology, i, false);
            result.bounds = mesh.bounds;

            var list = new List<Material>();
            foreach (var part in parts) list.Add(part.Material);
            // Material slots past the last submesh draw it again; they are kept.
            for (int i = indices.Length; i < materials.Length; i++) list.Add(materials[i]);
            newMaterials = list.ToArray();
            return result;
        }

        private static T[] Expand<T>(T[] values, List<int> origin)
        {
            if (values == null || values.Length == 0) return values;
            var result = new T[origin.Count];
            for (int i = 0; i < origin.Count; i++) result[i] = values[origin[i]];
            return result;
        }

        private static void CopyUVs(Mesh mesh, Mesh result, int channel, List<int> origin, List<VertexUse> uses)
        {
            var attribute = VertexAttribute.TexCoord0 + channel;
            if (!mesh.HasVertexAttribute(attribute)) return;
            int dimension = mesh.GetVertexAttributeDimension(attribute);
            var source = new List<Vector4>();
            mesh.GetUVs(channel, source);
            var values = new List<Vector4>(origin.Count);
            for (int i = 0; i < origin.Count; i++)
            {
                var uv = source[origin[i]];
                if (uses != null && uses[i].Target != null)
                {
                    var use = uses[i];
                    var moved = new Vector2(uv.x, uv.y) + use.Shift;
                    if (use.Clamp) moved = new Vector2(Mathf.Clamp01(moved.x), Mathf.Clamp01(moved.y));
                    var region = use.Target.Region;
                    uv.x = region.xMin + moved.x * region.width;
                    uv.y = region.yMin + moved.y * region.height;
                }
                values.Add(uv);
            }
            switch (dimension)
            {
                case 2: result.SetUVs(channel, values.ConvertAll(v => new Vector2(v.x, v.y))); break;
                case 3: result.SetUVs(channel, values.ConvertAll(v => new Vector3(v.x, v.y, v.z))); break;
                default: result.SetUVs(channel, values); break;
            }
        }

        private static void CopyBoneWeights(Mesh mesh, Mesh result, List<int> origin)
        {
            var perVertex = mesh.GetBonesPerVertex();
            if (perVertex.Length == 0) return;
            var weights = mesh.GetAllBoneWeights();
            var starts = new int[perVertex.Length];
            for (int i = 1; i < perVertex.Length; i++) starts[i] = starts[i - 1] + perVertex[i - 1];
            int total = 0;
            foreach (int v in origin) total += perVertex[v];
            using (var newPerVertex = new NativeArray<byte>(origin.Count, Allocator.Temp))
            using (var newWeights = new NativeArray<BoneWeight1>(total, Allocator.Temp))
            {
                var counts = newPerVertex;
                var all = newWeights;
                int at = 0;
                for (int i = 0; i < origin.Count; i++)
                {
                    int v = origin[i];
                    counts[i] = perVertex[v];
                    for (int k = 0; k < perVertex[v]; k++) all[at++] = weights[starts[v] + k];
                }
                result.SetBoneWeights(newPerVertex, newWeights);
            }
        }

        private static void CopyBlendShapes(Mesh mesh, Mesh result, List<int> origin)
        {
            int count = mesh.vertexCount;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var tangents = new Vector3[count];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                string name = mesh.GetBlendShapeName(shape);
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(shape, frame, positions, normals, tangents);
                    result.AddBlendShapeFrame(name, mesh.GetBlendShapeFrameWeight(shape, frame),
                        Expand(positions, origin), Expand(normals, origin), Expand(tangents, origin));
                }
            }
        }
    }
}
