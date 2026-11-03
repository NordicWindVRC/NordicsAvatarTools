using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.BlendshapeToggle.Editor
{
    // A toggle captured at Generating. The collapse center is in mesh space, which later
    // processors (mesh simplification, cutters, armature rebinding) do not change.
    internal sealed class TogglePlan
    {
        internal string Name;
        internal bool Reverse;
        internal float Scale;
        internal Vector3 Center;
        internal int Slot;
        internal SkinnedMeshRenderer Target;
    }

    internal sealed class ToggleBuildState
    {
        internal readonly List<TogglePlan> Plans = new List<TogglePlan>();
    }

    // Writes the toggle blendshapes onto a renderer's current (already processed) mesh.
    // Computing them late means simplifiers and cutters never see collapsed base geometry
    // of reversed toggles, and the shape always matches the final vertices.
    internal static class ToggleBake
    {
        internal static Mesh Apply(SkinnedMeshRenderer renderer, IReadOnlyList<TogglePlan> plans)
        {
            var source = renderer.sharedMesh;
            var vertices = source.vertices;
            var baseVertices = (Vector3[])vertices.Clone();
            var shapes = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            foreach (var plan in plans)
            {
                var deltas = new Vector3[vertices.Length];
                if (plan.Slot < source.subMeshCount)
                    foreach (int index in ToggleGeometry.SlotIndices(source, plan.Slot))
                    {
                        deltas[index] = (plan.Center - vertices[index]) * (1f - plan.Scale);
                        if (plan.Reverse)
                        {
                            baseVertices[index] += deltas[index];
                            deltas[index] = -deltas[index];
                        }
                    }
                shapes[plan.Name] = deltas;
            }
            var clone = Object.Instantiate(source);
            clone.name = source.name + " (Nordic's Blendshape Toggle)";
            WriteShapes(source, clone, shapes);
            clone.vertices = baseVertices;
            // Weight 100 of a reversed toggle (and weight 0 otherwise) is the original shape.
            clone.bounds = source.bounds;
            return clone;
        }

        // Replaces placeholders in place, so shape order (and any index-based reference)
        // is preserved; shapes without a placeholder are appended.
        private static void WriteShapes(Mesh source, Mesh clone, Dictionary<string, Vector3[]> shapes)
        {
            bool anyExisting = shapes.Keys.Any(n => source.GetBlendShapeIndex(n) >= 0);
            if (anyExisting)
            {
                int n = source.vertexCount;
                clone.ClearBlendShapes();
                for (int shape = 0; shape < source.blendShapeCount; shape++)
                {
                    string name = source.GetBlendShapeName(shape);
                    if (shapes.TryGetValue(name, out var deltas))
                    {
                        clone.AddBlendShapeFrame(name, 100f, deltas, null, null);
                        continue;
                    }
                    for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                    {
                        var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
                        source.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                        clone.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(shape, frame), dv, dn, dt);
                    }
                }
            }
            foreach (var pair in shapes)
                if (source.GetBlendShapeIndex(pair.Key) < 0)
                    clone.AddBlendShapeFrame(pair.Key, 100f, pair.Value, null, null);
        }
    }
}
