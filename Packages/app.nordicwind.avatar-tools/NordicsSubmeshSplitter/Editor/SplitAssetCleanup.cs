using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Generation creates a new container whenever a splitter does not own its current one
    // (new prefab variant, reset identity), so superseded containers can accumulate.
    // This finds containers no scene or prefab depends on and moves them to the OS trash.
    internal static class SplitAssetCleanup
    {
        [MenuItem("Tools/NordicWind/Submesh Splitter/Clean Up Unused Split Mesh Assets", false, 120)]
        private static void Run()
        {
            // References only count once saved to disk; unsaved scenes would make used assets look unused.
            var openScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            if (!EditorSceneManager.SaveModifiedScenesIfUserWantsTo(openScenes)) return;
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.isDirty)
            {
                EditorUtility.DisplayDialog("Nordic's Submesh Splitter",
                    "Save or discard the changes to the open prefab before cleaning up.", "OK");
                return;
            }
            var unused = FindUnused();
            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog("Nordic's Submesh Splitter",
                    "No unused split mesh assets were found.", "OK");
                return;
            }
            string list = string.Join("\n", unused);
            if (!EditorUtility.DisplayDialog("Nordic's Submesh Splitter",
                    "These split mesh assets are not referenced by any scene or prefab:\n\n" + list +
                    "\n\nMove them to the system trash?", "Move to Trash", "Cancel"))
                return;
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(unused.ToArray(), failed);
            if (failed.Count != 0)
                UnityEngine.Debug.LogWarning("[Nordic's Submesh Splitter] Could not move to trash: " + string.Join(", ", failed));
        }

        internal static List<string> FindUnused()
        {
            // Search the whole project: containers stay where they are when the output folder changes.
            var containers = AssetDatabase.FindAssets("t:" + nameof(NordicsSubmeshAssetData), new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToList();
            if (containers.Count == 0) return containers;
            var owners = AssetDatabase.FindAssets("t:Scene t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            var used = new HashSet<string>(AssetDatabase.GetDependencies(owners, true));
            return containers.Where(p => !used.Contains(p)).ToList();
        }
    }
}
