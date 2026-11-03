using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    // What the original renderer looked like at NDMF Resolving, before any tool ran.
    // Reassembly compares against it to tell allowed changes from unsupported ones.
    internal sealed class SourceSnapshot
    {
        internal Mesh Mesh;
        internal int VertexCount;
        internal int SubMeshCount;
        internal readonly List<string> BlendShapeNames = new List<string>();
        internal readonly Dictionary<string, float> Weights = new Dictionary<string, float>();

        internal static SourceSnapshot Take(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var snapshot = new SourceSnapshot { Mesh = mesh, VertexCount = mesh.vertexCount, SubMeshCount = mesh.subMeshCount };
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                snapshot.BlendShapeNames.Add(name);
                snapshot.Weights[name] = renderer.GetBlendShapeWeight(i);
            }
            return snapshot;
        }
    }

    // NDMF build-scoped state: lives on the BuildContext from Resolving to Optimizing.
    internal sealed class SplitBuildState
    {
        internal readonly Dictionary<NordicsSubmeshSplitter, SourceSnapshot> Sources =
            new Dictionary<NordicsSubmeshSplitter, SourceSnapshot>();
        // NDMF keeps running passes after one fails; reassembly skips once validation reported.
        internal bool ValidationFailed;
    }
}
