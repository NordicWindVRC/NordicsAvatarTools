using System.Runtime.CompilerServices;
using System.Collections.Generic;
using UnityEngine;

[assembly: InternalsVisibleTo("NordicWind.SubmeshSplitter.Editor")]
[assembly: InternalsVisibleTo("NordicWind.SubmeshSplitter.Tests")]

namespace NordicWind.SubmeshSplitter
{
    [DisallowMultipleComponent]
    [AddComponentMenu("NordicWind/Nordic's Submesh Splitter")]
    public sealed class NordicsSubmeshSplitter : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [SerializeField, HideInInspector] internal NordicsSubmeshAssetData assetData;
        [SerializeField, HideInInspector] internal Transform generatedRoot;
        [SerializeField, HideInInspector] internal List<SkinnedMeshRenderer> generatedRenderers = new List<SkinnedMeshRenderer>();
        [SerializeField, HideInInspector] internal string splitterId;
        [SerializeField, HideInInspector] internal bool originalRendererWasEnabled;
        [SerializeField, HideInInspector] internal bool hasGenerated;

        public IReadOnlyList<SkinnedMeshRenderer> GeneratedRenderers => generatedRenderers;

        /// <summary>
        /// Returns the split renderer that holds the given source submesh (material slot).
        /// Other NordicWind tools use this to apply explicit per-part actions.
        /// </summary>
        public bool TryGetGeneratedRenderer(int submesh, out SkinnedMeshRenderer renderer)
        {
            renderer = hasGenerated && generatedRenderers != null && submesh >= 0 && submesh < generatedRenderers.Count
                ? generatedRenderers[submesh] : null;
            return renderer != null;
        }
    }
}
