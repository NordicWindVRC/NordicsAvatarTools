using UnityEngine;
using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
namespace NordicWind.AdvancedAtlaser
{
    public enum AtlasBlendShapeSelectionMode { AnyVertex, AllVertices }

    [Serializable]
    public sealed class AtlasExcludedBlendShape
    {
        public SkinnedMeshRenderer renderer;
        public string blendShapeName;
        [Min(0f)] public float threshold = 0.001f;
        public AtlasBlendShapeSelectionMode selectionMode = AtlasBlendShapeSelectionMode.AnyVertex;
    }

    [AddComponentMenu("NordicWind/Nordic's Advanced Atlaser")]
    [DisallowMultipleComponent]
    public sealed class NordicsAdvancedAtlaser : MonoBehaviour, INDMFEditorOnly
    {
        // Bakes a normal map that compensates for decimated meshes. Its resolution follows each
        // material's area in its atlas.
        public bool enableGeometryBaking = true;
        // Surfaces hidden close behind another surface with the same UVs (for example a body
        // part inside another mesh part) are left out of the bake, as if seen from outside.
        public bool excludeHiddenGeometry = true;
        // Mesh parts moved by these blend shapes are left out of the original geometry used for
        // the bake. Empty means nothing is excluded.
        public List<AtlasExcludedBlendShape> excludedBlendShapes = new List<AtlasExcludedBlendShape>();
    }
}
