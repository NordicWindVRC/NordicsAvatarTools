using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.BlendshapeToggle.Editor
{
    internal sealed class ToggleGeometry
    {
        public HashSet<int> Indices;
        public Vector3 Center;
        public int TriangleCount;

        public static ToggleGeometry Validate(NordicsBlendshapeToggle toggle)
            => Validate(toggle, toggle.GetComponent<SkinnedMeshRenderer>(), toggle.materialSlot);

        // The renderer and slot may differ from the toggle's own when it targets a split part.
        public static ToggleGeometry Validate(NordicsBlendshapeToggle toggle, SkinnedMeshRenderer renderer, int slot)
        {
            if (!renderer) throw new InvalidOperationException("Requires a Skinned Mesh Renderer on this GameObject.");
            var mesh = renderer.sharedMesh;
            if (!mesh) throw new InvalidOperationException("The renderer has no mesh.");
            if (!mesh.isReadable) throw new InvalidOperationException("Mesh is not readable. Enable Read/Write in the model import settings.");
            if (slot < 0 || slot >= mesh.subMeshCount || slot >= renderer.sharedMaterials.Length)
                throw new InvalidOperationException("Selected material slot/submesh does not exist.");
            if (mesh.GetTopology(slot) != MeshTopology.Triangles)
                throw new InvalidOperationException("Selected submesh must use triangle topology.");
            if (string.IsNullOrWhiteSpace(toggle.blendshapeName))
                throw new InvalidOperationException("Blendshape name cannot be empty.");
            if (mesh.GetBlendShapeIndex(toggle.blendshapeName) >= 0)
                throw new InvalidOperationException("Blendshape name already exists on this mesh.");
            if (float.IsNaN(toggle.collapsedScale) || float.IsInfinity(toggle.collapsedScale) || toggle.collapsedScale < 0)
                throw new InvalidOperationException("Collapsed scale must be a finite number greater than or equal to zero.");
            if (!Enum.IsDefined(typeof(ToggleCenterMode), toggle.centerMode))
                throw new InvalidOperationException("Invalid center mode.");
            if (toggle.centerMode == ToggleCenterMode.Transform && !toggle.targetTransform)
                throw new InvalidOperationException("Assign a target Transform.");
            var indices = SlotIndices(mesh, slot);
            if (indices.Count == 0) throw new InvalidOperationException("Selected submesh contains no triangles.");
            var vertices = mesh.vertices;
            var bounds = new Bounds(vertices[indices.First()], Vector3.zero);
            foreach (int index in indices) bounds.Encapsulate(vertices[index]);
            Vector3 center = bounds.center;
            if (toggle.centerMode == ToggleCenterMode.MeshCenter) center = mesh.bounds.center;
            if (toggle.centerMode == ToggleCenterMode.Transform)
                center = renderer.transform.InverseTransformPoint(toggle.targetTransform.position);
            if (!Finite(center)) throw new InvalidOperationException("Calculated center is not finite.");
            return new ToggleGeometry { Indices = indices, Center = center, TriangleCount = mesh.GetTriangles(slot).Length / 3 };
        }

        // Vertices of one submesh, which must not be shared with any other submesh.
        internal static HashSet<int> SlotIndices(Mesh mesh, int slot)
        {
            if (mesh.GetTopology(slot) != MeshTopology.Triangles)
                throw new InvalidOperationException("Selected submesh must use triangle topology.");
            var indices = new HashSet<int>(mesh.GetTriangles(slot)); // Includes submesh baseVertex.
            for (int other = 0; other < mesh.subMeshCount; other++)
            {
                if (other == slot) continue;
                foreach (int index in mesh.GetIndices(other))
                    if (indices.Contains(index))
                        throw new InvalidOperationException($"Selected submesh shares vertex {index} with submesh {other}. Isolated vertices are required.");
            }
            return indices;
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        public Vector3[] CreateDeltas(Mesh mesh, float scale)
        {
            var vertices = mesh.vertices;
            var deltas = new Vector3[vertices.Length];
            foreach (int index in Indices)
            {
                deltas[index] = (Center - vertices[index]) * (1f - scale);
                if (!Finite(deltas[index])) throw new InvalidOperationException("Generated delta is not finite.");
            }
            return deltas;
        }
    }
}
