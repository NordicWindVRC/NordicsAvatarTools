using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>At early capture, exclude complete original triangles touching any vertex
    /// changed by a selected blend-shape frame. No blend-shape posing is performed.</summary>
    internal static class AtlasBlendShapeExclusion
    {
        internal static List<AtlasExcludedBlendShape> EntriesForRenderer(
            NordicsAdvancedAtlaser atlaser, SkinnedMeshRenderer renderer)
        {
            var matches = new List<AtlasExcludedBlendShape>();
            if (atlaser == null || renderer == null || !atlaser.IgnoreGeometryAffectedByBlendShape ||
                atlaser.ExcludedBlendShapes == null) return matches;
            foreach (var entry in atlaser.ExcludedBlendShapes)
                if (entry != null && entry.Renderer == renderer && !string.IsNullOrEmpty(entry.BlendShapeName))
                    matches.Add(entry);
            return matches;
        }

        // Union triangles excluded by each entry independently. A different threshold or
        // Any/All rule on one shape must never alter the evaluation of another shape.
        internal static bool[] BuildExcludedTriangles(Mesh mesh, SkinnedMeshRenderer renderer,
            NordicsAdvancedAtlaser atlaser, int[] triangles, List<string> diagnostics)
        {
            if (mesh == null || triangles == null) return null;
            var entries = EntriesForRenderer(atlaser, renderer);
            if (entries.Count == 0) return null;
            var excluded = new bool[triangles.Length / 3];
            var dv = new Vector3[mesh.vertexCount];
            var dn = new Vector3[mesh.vertexCount];
            var dt = new Vector3[mesh.vertexCount];
            foreach (var entry in entries)
            {
                int shapeIndex = mesh.GetBlendShapeIndex(entry.BlendShapeName);
                if (shapeIndex < 0)
                {
                    diagnostics.Add(atlaser.name + ": blend shape '" + entry.BlendShapeName +
                        "' missing from source renderer '" + renderer.name + "'.");
                    continue;
                }
                float threshold = Mathf.Max(0f, entry.Threshold);
                float thresholdSquared = threshold * threshold;
                var affected = new bool[mesh.vertexCount];
                int frames = mesh.GetBlendShapeFrameCount(shapeIndex);
                for (int frame = 0; frame < frames; ++frame)
                {
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame, dv, dn, dt);
                    for (int v = 0; v < affected.Length; ++v)
                        if (dv[v].sqrMagnitude > thresholdSquared) affected[v] = true;
                }
                int newlyExcluded = 0;
                for (int tri = 0; tri < excluded.Length; ++tri)
                {
                    if (excluded[tri]) continue;
                    int i = tri * 3;
                    int a = triangles[i], b = triangles[i+1], c = triangles[i+2];
                    if (a < 0 || a >= affected.Length || b < 0 || b >= affected.Length ||
                        c < 0 || c >= affected.Length) continue;
                    bool match = entry.SelectionMode == AtlasBlendShapeSelectionMode.AllVertices
                        ? affected[a] && affected[b] && affected[c]
                        : affected[a] || affected[b] || affected[c];
                    if (match) { excluded[tri] = true; newlyExcluded++; }
                }
                diagnostics.Add(atlaser.name + ": blend shape '" + entry.BlendShapeName +
                    "' on '" + renderer.name + "': threshold=" + threshold +
                    ", mode=" + entry.SelectionMode + ", newly excluded triangles=" + newlyExcluded + ".");
            }
            return excluded;
        }

        internal static void SplitTriangles(int[] triangles, bool[] excludedFlags,
            out int[] kept, out int[] excluded)
        {
            if (triangles == null) { kept = new int[0]; excluded = new int[0]; return; }
            if (excludedFlags == null) { kept = triangles; excluded = new int[0]; return; }
            var good = new List<int>(triangles.Length);
            var removed = new List<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                var destination = excludedFlags[i / 3] ? removed : good;
                destination.Add(triangles[i]);
                destination.Add(triangles[i + 1]);
                destination.Add(triangles[i + 2]);
            }
            kept = good.ToArray(); excluded = removed.ToArray();
        }
        internal static int Count(bool[] affected)
        {
            int n = 0;
            if (affected != null) foreach (bool b in affected) if (b) ++n;
            return n;
        }

    }
}
