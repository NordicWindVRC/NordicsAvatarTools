using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    internal enum SplitContextKind
    {
        PrefabAsset,     // Prefab Mode of the prefab (or variant) that owns the splitter
        Scene,           // A saved scene object that is not part of a prefab instance
        UnsavedScene,    // A scene object in a scene that has never been saved
        PrefabInstance,  // Part of a prefab instance: must be edited in that prefab
        NotEditable,     // e.g. a prefab asset selected in the Project window
    }

    // Decides where a split may be generated and which asset owns the result.
    internal readonly struct SplitContext
    {
        internal readonly SplitContextKind Kind;
        internal readonly string OwnerGuid;      // set for PrefabAsset and Scene
        internal readonly string PrefabToOpen;   // set when the split must be edited in a prefab
        internal readonly GameObject SearchRoot; // stage root, or null for the whole scene

        private SplitContext(SplitContextKind kind, string ownerGuid = null, string prefabToOpen = null, GameObject searchRoot = null)
        { Kind = kind; OwnerGuid = ownerGuid; PrefabToOpen = prefabToOpen; SearchRoot = searchRoot; }

        internal bool CanGenerate => Kind == SplitContextKind.PrefabAsset || Kind == SplitContextKind.Scene;

        internal string Message
        {
            get
            {
                switch (Kind)
                {
                    case SplitContextKind.UnsavedScene:
                        return "Save the scene before generating. The split meshes are owned by the scene asset.";
                    case SplitContextKind.PrefabInstance:
                        return "This renderer belongs to a prefab. Update the split in that prefab so every instance stays consistent.";
                    case SplitContextKind.NotEditable:
                        return "Open the prefab in Prefab Mode, or use a scene object, to generate split meshes.";
                    default:
                        return null;
                }
            }
        }

        internal static SplitContext Resolve(NordicsSubmeshSplitter splitter)
        {
            var go = splitter.gameObject;
            if (EditorUtility.IsPersistent(go))
                return new SplitContext(SplitContextKind.NotEditable, prefabToOpen: AssetDatabase.GetAssetPath(go));

            // Everything in Prefab Mode belongs to the open prefab: changes to nested
            // instances (the FBX model, nested prefabs, a variant's base) are stored as
            // overrides in it.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && go.scene == stage.scene)
                return new SplitContext(SplitContextKind.PrefabAsset,
                    AssetDatabase.AssetPathToGUID(stage.assetPath), searchRoot: stage.prefabContentsRoot);

            // In a scene, only an editable prefab (regular or variant) takes ownership; open
            // the outermost one, normally the avatar prefab. A model (FBX) instance cannot be
            // edited as a prefab, so a split on it belongs to the scene.
            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (outermost != null)
            {
                var type = PrefabUtility.GetPrefabAssetType(outermost);
                if (type == PrefabAssetType.Regular || type == PrefabAssetType.Variant)
                    return new SplitContext(SplitContextKind.PrefabInstance,
                        prefabToOpen: PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outermost));
            }
            if (!go.scene.IsValid() || string.IsNullOrEmpty(go.scene.path))
                return new SplitContext(SplitContextKind.UnsavedScene);
            return new SplitContext(SplitContextKind.Scene, AssetDatabase.AssetPathToGUID(go.scene.path));
        }
    }
}
