using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    [CustomEditor(typeof(NordicsSubmeshSplitter))]
    internal sealed class NordicsSubmeshSplitterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var splitter = (NordicsSubmeshSplitter)target;
            var context = SplitContext.Resolve(splitter);
            var sourceError = SplitMeshGenerator.ValidateSource(splitter);

            if (sourceError != null)
                EditorGUILayout.HelpBox(sourceError, MessageType.Error);
            if (!context.CanGenerate)
            {
                EditorGUILayout.HelpBox(context.Message, MessageType.Warning);
                if (!string.IsNullOrEmpty(context.PrefabToOpen) && GUILayout.Button("Open Prefab"))
                    PrefabStageUtility.OpenPrefab(context.PrefabToOpen);
            }

            if (sourceError == null)
            {
                var source = splitter.GetComponent<SkinnedMeshRenderer>();
                if (splitter.hasGenerated)
                {
                    var stale = SplitMeshGenerator.ValidateGenerated(splitter);
                    if (stale != null) EditorGUILayout.HelpBox(stale + " Builds stop until this is fixed.", MessageType.Error);
                }
                var shared = SplitMeshGenerator.SharedVertexWarning(source.sharedMesh);
                if (shared != null) EditorGUILayout.HelpBox(shared, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(sourceError != null || !context.CanGenerate))
            {
                if (GUILayout.Button("Update split meshes", GUILayout.Height(30)))
                {
                    try { SplitMeshGenerator.Generate(splitter); }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex, splitter);
                        EditorUtility.DisplayDialog("Nordic's Submesh Splitter", ex.Message, "OK");
                    }
                }
            }
        }
    }
}
