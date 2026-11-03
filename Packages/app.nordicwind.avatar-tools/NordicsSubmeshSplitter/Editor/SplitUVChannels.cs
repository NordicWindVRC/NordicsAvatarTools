using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Uses Unity's typed mesh API rather than interpreting raw interleaved vertex buffers.
    // The Vector4 representation is only an intermediate container: Write restores
    // the requested channel dimension instead of silently promoting it to 4D.
    internal static class SplitUVChannels
    {
        internal static VertexAttribute Attribute(int channel) =>
            (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);

        internal static int Dimension(Mesh mesh, int channel) =>
            mesh.HasVertexAttribute(Attribute(channel)) ?
                mesh.GetVertexAttributeDimension(Attribute(channel)) : 0;

        internal static List<Vector4> Read(Mesh mesh, int channel)
        {
            int dimension = Dimension(mesh, channel);
            if (dimension == 0) return null;
            var result = new List<Vector4>(mesh.vertexCount);
            // GetUVs<Vector4> converts source attributes (including half floats) to
            // floats. It does not modify the source mesh's vertex buffer layout.
            mesh.GetUVs(channel, result);
            if (result.Count != mesh.vertexCount)
                throw new InvalidOperationException($"UV{channel} on '{mesh.name}' has {result.Count} values for {mesh.vertexCount} vertices.");
            if (dimension < 1 || dimension > 4)
                throw new NotSupportedException($"UV{channel} on '{mesh.name}' has unsupported dimension {dimension}.");
            return result;
        }

        internal static void Write(Mesh mesh, int channel, IReadOnlyList<Vector4> values, int dimension)
        {
            if (dimension == 0 || values == null) return;
            if (dimension == 1)
                throw new NotSupportedException($"UV{channel} requires a 1D vertex attribute. The standard Mesh.SetUVs API cannot preserve it without changing the vertex layout; refusing lossy conversion.");
            if (dimension < 2 || dimension > 4)
                throw new NotSupportedException($"UV{channel}: unsupported dimension {dimension}.");
            if (values.Count != mesh.vertexCount)
                throw new InvalidOperationException($"UV{channel}: expected {mesh.vertexCount} values, got {values.Count}.");
            switch (dimension)
            {
                case 2:
                    var uv2 = new List<Vector2>(values.Count);
                    foreach (var v in values) uv2.Add(new Vector2(v.x, v.y));
                    mesh.SetUVs(channel, uv2);
                    break;
                case 3:
                    var uv3 = new List<Vector3>(values.Count);
                    foreach (var v in values) uv3.Add(new Vector3(v.x, v.y, v.z));
                    mesh.SetUVs(channel, uv3);
                    break;
                case 4:
                    var uv4 = new List<Vector4>(values.Count);
                    foreach (var v in values) uv4.Add(v);
                    mesh.SetUVs(channel, uv4);
                    break;
            }
        }
    }
}
