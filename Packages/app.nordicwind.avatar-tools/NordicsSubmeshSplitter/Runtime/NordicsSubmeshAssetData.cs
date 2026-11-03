using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NordicWind.SubmeshSplitter
{
    // Serialized as the main object of a generated .asset container.
    // Must stay in a file named after the class: Unity resolves ScriptableObject
    // assets through the script file, and a mismatch saves them with a missing script.
    public sealed class NordicsSubmeshAssetData : ScriptableObject
    {
        // GUID of the prefab or scene asset that owns this container.
        [SerializeField, FormerlySerializedAs("ownerPrefabGuid")] internal string ownerAssetGuid;
        [SerializeField] internal string ownerObjectId;
        [SerializeField] internal List<Mesh> meshes = new List<Mesh>();
        // Context-independent hash of the source mesh the parts were generated from.
        [SerializeField] internal string sourceFingerprint;
    }
}
