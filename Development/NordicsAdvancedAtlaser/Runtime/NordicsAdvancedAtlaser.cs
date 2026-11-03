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
        public SkinnedMeshRenderer Renderer;
        public string BlendShapeName;
        [Min(0f)] public float Threshold = 0.001f;
        public AtlasBlendShapeSelectionMode SelectionMode = AtlasBlendShapeSelectionMode.AnyVertex;
    }

    [AddComponentMenu("NordicWind/Nordic's Advanced Atlaser")]
    [DisallowMultipleComponent]
    public sealed class NordicsAdvancedAtlaser : MonoBehaviour, INDMFEditorOnly
    {
        public bool EnableGeometryBaking = true;
        // Source UV-space bake resolution. Always square and independent of output atlases.
        public int GeometryBakingResolution = 4096;
        public bool IgnoreGeometryAffectedByBlendShape = false;
        public List<AtlasExcludedBlendShape> ExcludedBlendShapes = new List<AtlasExcludedBlendShape>();
    }
}
